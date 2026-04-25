using DCMultiplayer.Networking;
using Il2CppSteamworks;
using MelonLoader;

namespace DCMultiplayer.Replication;

// Host: every second, broadcast money/xp/reputation snapshot.
// Client: when received, write directly into Player (its own UpdateCoin/etc.
// are blocked by ClientSuppression patches, so this is the only path that can
// move those values on the client).
internal static class EconomySync
{
    const float SendIntervalSec = 1.0f;
    static float _accum;
    public static MelonLogger.Instance Log = new("DC_MP_Econ");
    public static float LastMoney, LastXp, LastRep;

    public static void Tick(float dt)
    {
        if (!Authority.IsAuthoritative) return;        // only host broadcasts
        if (!SteamLobby.IsInLobby) return;             // pointless if alone

        _accum += dt;
        if (_accum < SendIntervalSec) return;
        _accum = 0f;

        var pm = Il2Cpp.PlayerManager.instance;
        var p = pm?.playerClass;
        if (p == null) return;

        System.Span<byte> buf = stackalloc byte[NetMsg.EconomyTickSize];
        NetMsg.WriteEconomyTick(buf, p.money, p.xp, p.reputation);
        Transport.Broadcast(Transport.ChState, buf.ToArray(), Transport.SendUnreliable);
    }

    public static void OnIncoming(CSteamID from, byte channel, byte[] data)
    {
        if (channel != Transport.ChState) return;
        if (data.Length < 1 || data[0] != NetMsg.MsgEconomyTick) return;
        if (!Authority.IsClient) return;               // host ignores its own
        if (!NetMsg.TryReadEconomyTick(data, out float money, out float xp, out float rep)) return;

        var pm = Il2Cpp.PlayerManager.instance;
        var p = pm?.playerClass;
        if (p == null) return;

        p.money = money;
        p.xp = xp;
        p.reputation = rep;
        LastMoney = money; LastXp = xp; LastRep = rep;
    }
}
