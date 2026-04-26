using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSteamworks;
using MelonLoader;

namespace DCMultiplayer.Networking;

// Phase A — minimal P2P transport on top of SteamNetworkingMessages.
//
// Channels:
//   0  control       (handshake, ping/pong, lobby admin)
//   1  state         (periodic snapshots)
//   2  events        (one-shot game events: place server, connect cable, etc.)
//
// Send flags (from steamnetworkingtypes.h):
//   k_nSteamNetworkingSend_Unreliable        = 0
//   k_nSteamNetworkingSend_NoNagle           = 1
//   k_nSteamNetworkingSend_Reliable          = 8
//   k_nSteamNetworkingSend_ReliableNoNagle   = 9

internal static class Transport
{
    public const int SendUnreliable = 0;
    public const int SendReliable = 8;
    public const int SendReliableNoNagle = 9;

    public const byte ChControl = 0;
    public const byte ChState = 1;
    public const byte ChEvent = 2;

    /// <summary>Debug toggle — Broadcast() also dispatches to local
    /// OnMessage handlers as if the message arrived from ourselves. Lets
    /// us exercise the receive path without a real second peer. Receivers
    /// that gate on Authority.IsClient still won't apply, so use together
    /// with Authority.ForceClient when testing client-side behavior. Not
    /// bound to any input; flip at the source.</summary>
#pragma warning disable CS0649
    public static bool DebugLoopback;
#pragma warning restore CS0649

    public static MelonLogger.Instance Log = new("DC_MP_Net");

    public static long LastRxBytes { get; private set; }
    public static long LastTxBytes { get; private set; }
    public static int LastRxPackets { get; private set; }
    public static int LastTxPackets { get; private set; }
    public static string LastRxFrom { get; private set; } = "—";
    public static string LastRxText { get; private set; } = "—";

    public static event Action<CSteamID, byte, byte[]> OnMessage;

    static Callback<SteamNetworkingMessagesSessionRequest_t> _cbSessReq;
    static readonly Il2CppStructArray<IntPtr> _rxBuf = new Il2CppStructArray<IntPtr>(32);

    public static void Init()
    {
        // SteamNetworkingMessagesSessionFailed_t is non-blittable under Il2CppInterop
        // (carries a fixed-char string) so we can't bind that callback directly.
        // Failures still surface via SendMessageToUser's EResult and via empty receives.
        _cbSessReq = Callback<SteamNetworkingMessagesSessionRequest_t>.Create((Action<SteamNetworkingMessagesSessionRequest_t>)OnSessionRequest);
        Log.Msg("Transport ready");
    }

    public static void SendTo(CSteamID target, byte channel, byte[] payload, int sendFlags = SendReliable)
    {
        if (target == SteamUser.GetSteamID()) return; // never send to self
        var ident = new SteamNetworkingIdentity();
        ident.SetSteamID(target);

        IntPtr buf = Marshal.AllocHGlobal(payload.Length);
        try
        {
            Marshal.Copy(payload, 0, buf, payload.Length);
            var r = SteamNetworkingMessages.SendMessageToUser(ref ident, buf, (uint)payload.Length, sendFlags, channel);
            if (r != EResult.k_EResultOK)
                Log.Warning($"send to {target.m_SteamID} ch={channel} failed: {r}");
            else
            {
                LastTxBytes += payload.Length;
                LastTxPackets++;
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static void Broadcast(byte channel, byte[] payload, int sendFlags = SendReliable)
    {
        if (DebugLoopback)
        {
            try { OnMessage?.Invoke(SteamUser.GetSteamID(), channel, payload); }
            catch (Exception ex) { Log.Error($"loopback dispatch failed: {ex.Message}"); }
        }
        if (!SteamLobby.IsInLobby) return;
        var lobby = SteamLobby.Current;
        var me = SteamUser.GetSteamID();
        int n = SteamMatchmaking.GetNumLobbyMembers(lobby);
        for (int i = 0; i < n; i++)
        {
            var id = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
            if (id != me) SendTo(id, channel, payload, sendFlags);
        }
    }

    public static void Pump()
    {
        for (int ch = 0; ch <= 2; ch++) PumpChannel(ch);
    }

    static void PumpChannel(int channel)
    {
        int got = SteamNetworkingMessages.ReceiveMessagesOnChannel(channel, _rxBuf, _rxBuf.Length);
        if (got <= 0) return;

        for (int i = 0; i < got; i++)
        {
            IntPtr msgPtr = _rxBuf[i];
            if (msgPtr == IntPtr.Zero) continue;
            try
            {
                var msg = SteamNetworkingMessage_t.FromIntPtr(msgPtr);
                int size = msg.m_cbSize;
                byte[] data = new byte[size];
                Marshal.Copy(msg.m_pData, data, 0, size);
                CSteamID from = msg.m_identityPeer.GetSteamID();

                LastRxBytes += size;
                LastRxPackets++;
                LastRxFrom = SteamFriends.GetFriendPersonaName(from);
                if (channel == ChControl)
                    LastRxText = System.Text.Encoding.UTF8.GetString(data, 0, Math.Min(size, 60));

                OnMessage?.Invoke(from, (byte)channel, data);
            }
            catch (Exception ex)
            {
                Log.Error($"rx ch={channel} parse error: {ex.Message}");
            }
            finally
            {
                SteamNetworkingMessage_t.Release(msgPtr);
            }
        }
    }

    static void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t r)
    {
        // (kept simple — accept if peer is in our lobby)
        var from = r.m_identityRemote.GetSteamID();
        bool inLobby = false;
        if (SteamLobby.IsInLobby)
        {
            int n = SteamMatchmaking.GetNumLobbyMembers(SteamLobby.Current);
            for (int i = 0; i < n; i++)
                if (SteamMatchmaking.GetLobbyMemberByIndex(SteamLobby.Current, i) == from)
                { inLobby = true; break; }
        }

        if (inLobby)
        {
            var ident = r.m_identityRemote;
            bool ok = SteamNetworkingMessages.AcceptSessionWithUser(ref ident);
            Log.Msg($"SessionRequest accepted from {SteamFriends.GetFriendPersonaName(from)} ({from.m_SteamID}) -> {ok}");
        }
        else
        {
            Log.Warning($"SessionRequest REJECTED — {from.m_SteamID} not in our lobby");
        }
    }

}
