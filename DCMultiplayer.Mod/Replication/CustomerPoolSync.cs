using System.Collections.Generic;
using DCMultiplayer.Networking;
using Il2CppSteamworks;
using MelonLoader;

namespace DCMultiplayer.Replication;

// Replicates MainGameManager.availableCustomerIndices from host to client.
// The client's own ShuffleAvailableCustomers is already blocked by
// ClientSuppression, so without us the list would never populate. Host pushes
// the current list whenever it changes (via Harmony postfix on Shuffle and
// ButtonCustomerChosen) and once on every peer join.
//
// Both peers share the same `customerItems` array (deterministic — defined in
// game data), so an index list alone is enough to reproduce the visible pool.
internal static class CustomerPoolSync
{
    public static MelonLogger.Instance Log = new("DC_MP_Cust");

    public static int LastApplied { get; private set; }

    public static void BroadcastCurrent()
    {
        if (!Authority.IsAuthoritative) return;
        if (!SteamLobby.IsInLobby) return;

        var mgm = Il2Cpp.MainGameManager.instance;
        if (mgm == null) return;
        var list = mgm.availableCustomerIndices;
        if (list == null) return;

        var copy = new List<int>(list.Count);
        for (int i = 0; i < list.Count; i++) copy.Add(list[i]);

        var bytes = NetMsg.WriteCustomerPool(copy);
        Transport.Broadcast(Transport.ChEvent, bytes);
        Log.Msg($"-> pool ({copy.Count} entries)");
    }

    public static void SendTo(CSteamID target)
    {
        if (!Authority.IsAuthoritative) return;
        var mgm = Il2Cpp.MainGameManager.instance;
        if (mgm == null) return;
        var list = mgm.availableCustomerIndices;
        if (list == null) return;

        var copy = new List<int>(list.Count);
        for (int i = 0; i < list.Count; i++) copy.Add(list[i]);

        var bytes = NetMsg.WriteCustomerPool(copy);
        Transport.SendTo(target, Transport.ChEvent, bytes);
        Log.Msg($"-> pool ({copy.Count} entries) to {SteamFriends.GetFriendPersonaName(target)}");
    }

    public static void OnIncoming(CSteamID from, byte channel, byte[] data)
    {
        if (channel != Transport.ChEvent) return;
        if (data.Length < 1 || data[0] != NetMsg.MsgCustomerPool) return;
        if (!Authority.IsClient) return;
        if (!NetMsg.TryReadCustomerPool(data, out int[] indices)) return;

        var mgm = Il2Cpp.MainGameManager.instance;
        if (mgm == null)
        {
            Log.Warning($"<- pool ({indices.Length} entries) but MainGameManager not ready, dropping");
            return;
        }

        // Build a fresh Il2Cpp List<int> and assign.
        var newList = new Il2CppSystem.Collections.Generic.List<int>();
        foreach (int idx in indices) newList.Add(idx);
        mgm.availableCustomerIndices = newList;

        LastApplied = indices.Length;
        Log.Msg($"<- pool ({indices.Length} entries) applied");
    }
}
