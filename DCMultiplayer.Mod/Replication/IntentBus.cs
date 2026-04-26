using DCMultiplayer.Networking;
using Il2CppSteamworks;
using MelonLoader;

namespace DCMultiplayer.Replication;

// Client -> host action requests. Wire format is a 1-byte subtype after
// the MsgIntent type byte plus a subtype-specific payload (see NetMsg
// helpers). Only the host applies the action; the resulting state change
// propagates back to all peers via the existing snapshot broadcasts.
//
// Channel choice: ChEvent (reliable). Intents must not be lost — a
// dropped power-toggle would leave the client's mental model
// permanently out of sync with the host's.
internal static class IntentBus
{
    public static MelonLogger.Instance Log = new("DC_MP_Intent");

    // ── client side ──────────────────────────────────────────────────────

    public static void RequestRefresh()
    {
        if (!SteamLobby.IsInLobby) { Log.Msg("RequestRefresh: not in lobby"); return; }
        if (Authority.IsAuthoritative)
        {
            // Host can refresh themselves — useful for the Refresh button
            // even when single-host. Fan out the broadcasts directly.
            DoRefresh();
            return;
        }
        var bytes = NetMsg.WriteIntentNoPayload(NetMsg.IntentRefresh);
        Transport.SendTo(SteamMatchmaking.GetLobbyOwner(SteamLobby.Current), Transport.ChEvent, bytes);
        Log.Msg("-> IntentRefresh");
    }

    public static void RequestServerPowerToggle(string serverId)
    {
        if (!SteamLobby.IsInLobby) return;
        if (string.IsNullOrEmpty(serverId)) return;
        var bytes = NetMsg.WriteIntentWithStringId(NetMsg.IntentToggleServerPower, serverId);
        if (Authority.IsAuthoritative)
        {
            ApplyServerPowerToggle(serverId);
            return;
        }
        Transport.SendTo(SteamMatchmaking.GetLobbyOwner(SteamLobby.Current), Transport.ChEvent, bytes);
        Log.Msg($"-> IntentToggleServerPower {serverId}");
    }

    public static void RequestSwitchPowerToggle(string switchId)
    {
        if (!SteamLobby.IsInLobby) return;
        if (string.IsNullOrEmpty(switchId)) return;
        var bytes = NetMsg.WriteIntentWithStringId(NetMsg.IntentToggleSwitchPower, switchId);
        if (Authority.IsAuthoritative)
        {
            ApplySwitchPowerToggle(switchId);
            return;
        }
        Transport.SendTo(SteamMatchmaking.GetLobbyOwner(SteamLobby.Current), Transport.ChEvent, bytes);
        Log.Msg($"-> IntentToggleSwitchPower {switchId}");
    }

    // ── host side ────────────────────────────────────────────────────────

    public static void OnIncoming(CSteamID from, byte channel, byte[] data)
    {
        if (channel != Transport.ChEvent) return;
        if (data.Length < 1 || data[0] != NetMsg.MsgIntent) return;
        if (!Authority.IsAuthoritative)
        {
            // Clients receive but don't act — defensive against host's
            // own broadcast loopback or peers misbehaving.
            return;
        }
        if (!NetMsg.TryReadIntent(data, out byte subtype, out var payload))
        {
            Log.Warning("malformed intent dropped");
            return;
        }

        string fromName = SteamFriends.GetFriendPersonaName(from);

        switch (subtype)
        {
            case NetMsg.IntentRefresh:
                Log.Msg($"<- IntentRefresh from {fromName}");
                DoRefresh();
                break;

            case NetMsg.IntentToggleServerPower:
                if (NetMsg.TryReadStringIdPayload(payload, out string serverId))
                {
                    Log.Msg($"<- IntentToggleServerPower {serverId} from {fromName}");
                    ApplyServerPowerToggle(serverId);
                }
                break;

            case NetMsg.IntentToggleSwitchPower:
                if (NetMsg.TryReadStringIdPayload(payload, out string switchId))
                {
                    Log.Msg($"<- IntentToggleSwitchPower {switchId} from {fromName}");
                    ApplySwitchPowerToggle(switchId);
                }
                break;

            default:
                Log.Warning($"unknown intent subtype 0x{subtype:X2} from {fromName}");
                break;
        }
    }

    // Re-broadcast everything we have. Useful as a manual resync trigger
    // when a client has missed packets or is otherwise out of sync.
    static void DoRefresh()
    {
        try { CustomerPoolSync.BroadcastCurrent(); } catch (System.Exception ex) { Log.Warning($"refresh customer pool: {ex.Message}"); }
        try { ServerSnapshotSync.BroadcastSnapshot(); } catch (System.Exception ex) { Log.Warning($"refresh server: {ex.Message}"); }
        try { SwitchSnapshotSync.BroadcastSnapshot(); } catch (System.Exception ex) { Log.Warning($"refresh switch: {ex.Message}"); }
        try { PatchPanelSnapshotSync.BroadcastSnapshot(); } catch (System.Exception ex) { Log.Warning($"refresh patch panel: {ex.Message}"); }
        try { BaseAssignmentsSync.BroadcastCurrent(); } catch (System.Exception ex) { Log.Warning($"refresh base assignments: {ex.Message}"); }
        try { CableSnapshotSync.BroadcastSnapshot(); } catch (System.Exception ex) { Log.Warning($"refresh cable: {ex.Message}"); }
        EventLog.Emit("snapshot resync requested");
    }

    static void ApplyServerPowerToggle(string serverId)
    {
        var nm = Il2Cpp.NetworkMap.instance;
        var s = nm?.GetServer(serverId);
        if (s == null) { Log.Warning($"toggle: server '{serverId}' not found"); return; }
        // Server.PowerButton(forceState) reads the button image to figure
        // out the inverted state when called without explicit args; we
        // pass the inverse of isOn explicitly to avoid that round-trip.
        s.PowerButton(!s.isOn);
        EventLog.Emit($"toggled server {s.IP ?? serverId} {(s.isOn ? "ON" : "OFF")} (intent)");
        // ServerSnapshotSync will re-broadcast via its existing
        // PowerButton postfix — no extra send needed here.
    }

    static void ApplySwitchPowerToggle(string switchId)
    {
        var nm = Il2Cpp.NetworkMap.instance;
        var sw = nm?.GetSwitchById(switchId);
        if (sw == null) { Log.Warning($"toggle: switch '{switchId}' not found"); return; }
        sw.PowerButton(!sw.isOn);
        EventLog.Emit($"toggled switch {switchId} {(sw.isOn ? "ON" : "OFF")} (intent)");
    }
}
