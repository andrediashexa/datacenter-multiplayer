using DCMultiplayer.Networking;
using DCMultiplayer.Replication;
using HarmonyLib;

namespace DCMultiplayer.Patches;

// Postfix patches that, on the authoritative peer (single-player or host),
// emit human-readable event lines to all peers. Pure observability; nothing
// here modifies game state.

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.PowerButton))]
internal static class HE_Server_Power
{
    static void Postfix(Il2Cpp.Server __instance)
    {
        if (!Authority.IsAuthoritative || !SteamLobby.IsInLobby) return;
        EventLog.Emit($"powered server {__instance.IP ?? __instance.ServerID} {(__instance.isOn ? "ON" : "OFF")}");
        ServerSnapshotSync.BroadcastSnapshot();
    }
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.ServerInsertedInRack))]
internal static class HE_Server_Inserted
{
    static void Postfix(Il2Cpp.Server __instance)
    {
        if (!Authority.IsAuthoritative || !SteamLobby.IsInLobby) return;
        EventLog.Emit($"placed server {__instance.IP ?? __instance.ServerID} (type {__instance.serverType})");
        ServerSnapshotSync.BroadcastSnapshot();
    }
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.ItIsBroken))]
internal static class HE_Server_Broken
{
    static void Postfix(Il2Cpp.Server __instance)
    {
        if (!Authority.IsAuthoritative || !SteamLobby.IsInLobby) return;
        EventLog.Emit($"server {__instance.IP ?? __instance.ServerID} BROKE");
        ServerSnapshotSync.BroadcastSnapshot();
    }
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.RepairDevice))]
internal static class HE_Server_Repair
{
    static void Postfix(Il2Cpp.Server __instance)
    {
        if (!Authority.IsAuthoritative || !SteamLobby.IsInLobby) return;
        EventLog.Emit($"repaired server {__instance.IP ?? __instance.ServerID}");
        ServerSnapshotSync.BroadcastSnapshot();
    }
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.SetIP))]
internal static class HE_Server_SetIP
{
    static void Postfix(Il2Cpp.Server __instance)
    {
        if (!Authority.IsAuthoritative || !SteamLobby.IsInLobby) return;
        ServerSnapshotSync.BroadcastSnapshot();
    }
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.UpdateAppID))]
internal static class HE_Server_UpdateAppID
{
    static void Postfix(Il2Cpp.Server __instance)
    {
        if (!Authority.IsAuthoritative || !SteamLobby.IsInLobby) return;
        ServerSnapshotSync.BroadcastSnapshot();
    }
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.UpdateCustomer))]
internal static class HE_Server_UpdateCustomer
{
    static void Postfix(Il2Cpp.Server __instance)
    {
        if (!Authority.IsAuthoritative || !SteamLobby.IsInLobby) return;
        ServerSnapshotSync.BroadcastSnapshot();
    }
}

[HarmonyPatch(typeof(Il2Cpp.NetworkSwitch), nameof(Il2Cpp.NetworkSwitch.PowerButton))]
internal static class HE_Switch_Power
{
    static void Postfix(Il2Cpp.NetworkSwitch __instance)
    {
        if (!Authority.IsAuthoritative || !SteamLobby.IsInLobby) return;
        EventLog.Emit($"powered switch {__instance.switchId} {(__instance.isOn ? "ON" : "OFF")}");
    }
}

[HarmonyPatch(typeof(Il2Cpp.NetworkSwitch), nameof(Il2Cpp.NetworkSwitch.SwitchInsertedInRack))]
internal static class HE_Switch_Inserted
{
    static void Postfix(Il2Cpp.NetworkSwitch __instance)
    {
        if (!Authority.IsAuthoritative || !SteamLobby.IsInLobby) return;
        EventLog.Emit($"placed switch {__instance.switchId} (type {__instance.switchType})");
    }
}

[HarmonyPatch(typeof(Il2Cpp.MainGameManager), nameof(Il2Cpp.MainGameManager.ButtonCustomerChosen))]
internal static class HE_MGM_CustomerChosen
{
    static void Postfix(int _cardID)
    {
        if (!Authority.IsAuthoritative || !SteamLobby.IsInLobby) return;
        EventLog.Emit($"chose customer card #{_cardID}");
        CustomerPoolSync.BroadcastCurrent();
        BaseAssignmentsSync.BroadcastCurrent();
    }
}

[HarmonyPatch(typeof(Il2Cpp.MainGameManager), nameof(Il2Cpp.MainGameManager.ShuffleAvailableCustomers))]
internal static class HE_MGM_ShuffleCustomers
{
    static void Postfix()
    {
        // ShuffleAvailableCustomers itself is blocked on the client by
        // ClientSuppression, so this postfix only runs on the authoritative peer.
        if (!Authority.IsAuthoritative || !SteamLobby.IsInLobby) return;
        CustomerPoolSync.BroadcastCurrent();
    }
}
