using System.Collections.Generic;
using DCMultiplayer.Networking;
using Il2CppSteamworks;
using MelonLoader;

namespace DCMultiplayer.Replication;

// Replicates which CustomerItem each customer base in the host's data center
// is currently hosting. Together with CustomerPoolSync (same set of cards in
// the choice canvas) and ServerSnapshotSync (same servers in the racks) this
// closes the loop for the read-only "I see the same data center" experience.
//
// Wire format (NetMsg.MsgBaseAssignments) is just a list of (baseId,
// customerId) pairs — both peers share the same `customerItems` source
// array so resolving customerId -> CustomerItem is deterministic and we
// don't need to ship CustomerItem data itself.
internal static class BaseAssignmentsSync
{
    public static MelonLogger.Instance Log = new("DC_MP_Base");

    static List<(int baseId, int customerId)> CollectFromHost()
    {
        var list = new List<(int, int)>();
        var nm = Il2Cpp.NetworkMap.instance;
        if (nm == null) return list;
        var dict = nm.customerBases;
        if (dict == null) return list;

        foreach (var kv in dict)
        {
            var b = kv.Value;
            if (b == null) continue;
            list.Add((b.customerBaseID, b.customerID));
        }
        return list;
    }

    public static void BroadcastCurrent()
    {
        if (!Authority.IsAuthoritative) return;
        if (!SteamLobby.IsInLobby) return;
        var pairs = CollectFromHost();
        if (pairs.Count == 0) return;
        var bytes = NetMsg.WriteBaseAssignments(pairs);
        Transport.Broadcast(Transport.ChEvent, bytes);
        Log.Msg($"-> assignments ({pairs.Count} bases)");
    }

    public static void SendTo(CSteamID target)
    {
        if (!Authority.IsAuthoritative) return;
        var pairs = CollectFromHost();
        var bytes = NetMsg.WriteBaseAssignments(pairs);
        Transport.SendTo(target, Transport.ChEvent, bytes);
        Log.Msg($"-> assignments ({pairs.Count} bases) to {SteamFriends.GetFriendPersonaName(target)}");
    }

    public static void OnIncoming(CSteamID from, byte channel, byte[] data)
    {
        if (channel != Transport.ChEvent) return;
        if (data.Length < 1 || data[0] != NetMsg.MsgBaseAssignments) return;
        if (!Authority.IsClient) return;
        if (!NetMsg.TryReadBaseAssignments(data, out var pairs)) return;

        var nm = Il2Cpp.NetworkMap.instance;
        var mgm = Il2Cpp.MainGameManager.instance;
        if (nm == null || mgm == null)
        {
            Log.Warning($"<- assignments ({pairs.Length}) but managers not ready, dropping");
            return;
        }

        int applied = 0;
        foreach (var (baseId, customerId) in pairs)
        {
            // Look up the base. NetworkMap.instance.customerBases is a
            // Dictionary<int, CustomerBase>. If a base wasn't registered yet
            // (e.g. snapshot arrived before all bases finished Awake), skip.
            Il2Cpp.CustomerBase b = null;
            try { b = nm.GetCustomerBase(baseId); } catch { /* stale id */ }
            if (b == null) continue;

            // Set the assignment fields directly. We deliberately don't call
            // SetUpBase / LoadData yet — those want a CustomerBaseSaveData
            // (per-app subnets, vlan ids, accumulated speeds, ...) and
            // shipping that is the next iteration's job. For now the client
            // just gets identity-level "this base is hosting customer N".
            b.customerID = customerId;
            if (customerId >= 0)
            {
                var item = mgm.GetCustomerItemByID(customerId);
                if (item != null) b.customerItem = item;
            }
            else
            {
                b.customerItem = null;
            }
            applied++;
        }
        Log.Msg($"<- assignments ({pairs.Length} pairs, {applied} applied)");
    }
}
