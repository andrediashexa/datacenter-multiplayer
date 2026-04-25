using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace DCMultiplayer.Patches;

// Phase C: pure observers. No state changes, no replication — just learn the
// game's lifecycle by logging. Once we've watched a normal play session and
// these patches don't crash, we know the targets resolve correctly under
// IL2CPP and can promote them to actual replication hooks.

internal static class Log
{
    static MelonLogger.Instance _l;
    public static MelonLogger.Instance L => _l ??= new MelonLogger.Instance("DC_MP_Obs");
}

// ── Player ───────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(Il2Cpp.Player), nameof(Il2Cpp.Player.LoadPlayer))]
internal static class P_Player_LoadPlayer
{
    static void Postfix(Il2Cpp.Player __instance, Il2Cpp.PlayerData data)
    {
        var pos = __instance.transform.position;
        Log.L.Msg($"Player.LoadPlayer  coins={data?.coins} xp={data?.xp} rep={data?.reputation}  pos={pos}");
    }
}

[HarmonyPatch(typeof(Il2Cpp.Player), nameof(Il2Cpp.Player.WarpPlayer))]
internal static class P_Player_WarpPlayer
{
    static void Postfix(Vector3 _position, Quaternion _rotation)
    {
        Log.L.Msg($"Player.WarpPlayer  to={_position}  rot={_rotation.eulerAngles}");
    }
}

[HarmonyPatch(typeof(Il2Cpp.Player), nameof(Il2Cpp.Player.UpdateCoin))]
internal static class P_Player_UpdateCoin
{
    static void Postfix(Il2Cpp.Player __instance, float _coinChhangeAmount, bool __result)
    {
        if (__result) Log.L.Msg($"Player.UpdateCoin  delta={_coinChhangeAmount:+#,0.##;-#,0.##;0}  total={__instance.money:N2}");
    }
}

[HarmonyPatch(typeof(Il2Cpp.Player), nameof(Il2Cpp.Player.UpdateXP))]
internal static class P_Player_UpdateXP
{
    static void Postfix(Il2Cpp.Player __instance, float amount, bool __result)
    {
        if (__result) Log.L.Msg($"Player.UpdateXP    delta={amount:+#,0.##;-#,0.##;0}  total={__instance.xp:N2}");
    }
}

[HarmonyPatch(typeof(Il2Cpp.Player), nameof(Il2Cpp.Player.UpdateReputation))]
internal static class P_Player_UpdateReputation
{
    static void Postfix(Il2Cpp.Player __instance, float amount)
    {
        Log.L.Msg($"Player.UpdateReputation  delta={amount:+#,0.##;-#,0.##;0}  total={__instance.reputation:N2}");
    }
}

// ── Server ───────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.ServerInsertedInRack))]
internal static class P_Server_Inserted
{
    static void Postfix(Il2Cpp.Server __instance, Il2Cpp.ServerSaveData serverSaveData)
    {
        Log.L.Msg($"Server.Inserted  id={__instance.ServerID}  type={__instance.serverType}  ip={__instance.IP}  appId={__instance.appID}");
    }
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.PowerButton))]
internal static class P_Server_Power
{
    static void Postfix(Il2Cpp.Server __instance, bool forceState)
    {
        Log.L.Msg($"Server.Power  id={__instance.ServerID}  isOn={__instance.isOn}");
    }
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.RegisterLink))]
internal static class P_Server_RegisterLink
{
    static void Postfix(Il2Cpp.Server __instance, Il2Cpp.CableLink link)
    {
        Log.L.Msg($"Server.RegisterLink  server={__instance.ServerID}  link={link?.GetHashCode():X}");
    }
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.UnregisterLink))]
internal static class P_Server_UnregisterLink
{
    static void Postfix(Il2Cpp.Server __instance, Il2Cpp.CableLink link)
    {
        Log.L.Msg($"Server.UnregisterLink  server={__instance.ServerID}  link={link?.GetHashCode():X}");
    }
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.ItIsBroken))]
internal static class P_Server_Broken
{
    static void Postfix(Il2Cpp.Server __instance) => Log.L.Msg($"Server.ItIsBroken  id={__instance.ServerID}");
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.RepairDevice))]
internal static class P_Server_Repair
{
    static void Postfix(Il2Cpp.Server __instance) => Log.L.Msg($"Server.RepairDevice  id={__instance.ServerID}");
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.SetIP))]
internal static class P_Server_SetIP
{
    static void Postfix(Il2Cpp.Server __instance, string _ip) => Log.L.Msg($"Server.SetIP  id={__instance.ServerID}  ip={_ip}");
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.UpdateAppID))]
internal static class P_Server_UpdateAppId
{
    static void Postfix(Il2Cpp.Server __instance, int _appID) => Log.L.Msg($"Server.UpdateAppID  id={__instance.ServerID}  app={_appID}");
}

[HarmonyPatch(typeof(Il2Cpp.Server), nameof(Il2Cpp.Server.UpdateCustomer))]
internal static class P_Server_UpdateCustomer
{
    static void Postfix(Il2Cpp.Server __instance, int newCustomerID) => Log.L.Msg($"Server.UpdateCustomer  id={__instance.ServerID}  cust={newCustomerID}");
}

// ── NetworkSwitch ────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(Il2Cpp.NetworkSwitch), nameof(Il2Cpp.NetworkSwitch.SwitchInsertedInRack))]
internal static class P_Switch_Inserted
{
    static void Postfix(Il2Cpp.NetworkSwitch __instance) => Log.L.Msg($"Switch.Inserted  id={__instance.switchId}  type={__instance.switchType}");
}

[HarmonyPatch(typeof(Il2Cpp.NetworkSwitch), nameof(Il2Cpp.NetworkSwitch.PowerButton))]
internal static class P_Switch_Power
{
    static void Postfix(Il2Cpp.NetworkSwitch __instance) => Log.L.Msg($"Switch.Power  id={__instance.switchId}  isOn={__instance.isOn}");
}

[HarmonyPatch(typeof(Il2Cpp.NetworkSwitch), nameof(Il2Cpp.NetworkSwitch.SetVlanAllowed))]
internal static class P_Switch_VlanAllow
{
    static void Postfix(Il2Cpp.NetworkSwitch __instance, int portIndex, int vlanId) => Log.L.Msg($"Switch.VlanAllow    id={__instance.switchId}  port={portIndex}  vlan={vlanId}");
}

[HarmonyPatch(typeof(Il2Cpp.NetworkSwitch), nameof(Il2Cpp.NetworkSwitch.SetVlanDisallowed))]
internal static class P_Switch_VlanDisallow
{
    static void Postfix(Il2Cpp.NetworkSwitch __instance, int portIndex, int vlanId) => Log.L.Msg($"Switch.VlanDisallow id={__instance.switchId}  port={portIndex}  vlan={vlanId}");
}

[HarmonyPatch(typeof(Il2Cpp.NetworkSwitch), nameof(Il2Cpp.NetworkSwitch.HandleNewCableWhileOff))]
internal static class P_Switch_NewCableOff
{
    static void Postfix(Il2Cpp.NetworkSwitch __instance, int cableId) => Log.L.Msg($"Switch.NewCableWhileOff  id={__instance.switchId}  cable={cableId}");
}

// ── PatchPanel ───────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(Il2Cpp.PatchPanel), nameof(Il2Cpp.PatchPanel.InsertedInRack))]
internal static class P_PatchPanel_Inserted
{
    static void Postfix(Il2Cpp.PatchPanel __instance) => Log.L.Msg($"PatchPanel.Inserted  id={__instance.patchPanelId}  type={__instance.patchPanelType}");
}

// ── MainGameManager / LoadingScreen / saves ──────────────────────────────────

[HarmonyPatch(typeof(Il2Cpp.MainGameManager), nameof(Il2Cpp.MainGameManager.RestartAutoSave))]
internal static class P_MGM_RestartAutoSave
{
    static void Postfix(Il2Cpp.MainGameManager __instance) => Log.L.Msg($"MGM.RestartAutoSave  enabled={__instance.autoSaveEnabled}  interval={__instance.autoSaveIntervalMinutes}min");
}

[HarmonyPatch(typeof(Il2Cpp.MainGameManager), nameof(Il2Cpp.MainGameManager.SetAutoSaveEnabled))]
internal static class P_MGM_SetAutoSave
{
    static void Postfix(bool enabled) => Log.L.Msg($"MGM.SetAutoSaveEnabled  {enabled}");
}

[HarmonyPatch(typeof(Il2Cpp.MainGameManager), nameof(Il2Cpp.MainGameManager.ButtonCustomerChosen))]
internal static class P_MGM_CustomerChosen
{
    static void Postfix(int _cardID) => Log.L.Msg($"MGM.ButtonCustomerChosen  card={_cardID}");
}

[HarmonyPatch(typeof(Il2Cpp.MainGameManager), nameof(Il2Cpp.MainGameManager.ShuffleAvailableCustomers))]
internal static class P_MGM_ShuffleCustomers
{
    static void Postfix() => Log.L.Msg("MGM.ShuffleAvailableCustomers");
}

[HarmonyPatch(typeof(Il2Cpp.LoadingScreen), nameof(Il2Cpp.LoadingScreen.LoadLevel))]
internal static class P_LS_LoadLevel
{
    static void Postfix(int sceneIndex) => Log.L.Msg($"LoadingScreen.LoadLevel  scene={sceneIndex}");
}

[HarmonyPatch(typeof(Il2Cpp.LoadingScreen), nameof(Il2Cpp.LoadingScreen.UnLoadLevel))]
internal static class P_LS_UnLoadLevel
{
    static void Postfix(int sceneIndex) => Log.L.Msg($"LoadingScreen.UnLoadLevel  scene={sceneIndex}");
}

[HarmonyPatch(typeof(Il2Cpp.LoadingScreen), nameof(Il2Cpp.LoadingScreen.LoadGameScenesVoid))]
internal static class P_LS_LoadGame
{
    static void Postfix() => Log.L.Msg("LoadingScreen.LoadGameScenesVoid  (snapshot loader fired)");
}

// ── ModLoader ────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(Il2Cpp.ModLoader), nameof(Il2Cpp.ModLoader.LoadAllMods))]
internal static class P_ML_LoadAll
{
    static void Postfix(Il2Cpp.ModLoader __instance) => Log.L.Msg($"ModLoader.LoadAllMods  templates={__instance.modTemplates?.Count}  nextId={__instance.nextModID}");
}

[HarmonyPatch(typeof(Il2Cpp.ModLoader), nameof(Il2Cpp.ModLoader.LoadModPack))]
internal static class P_ML_LoadModPack
{
    static void Postfix(string folderPath) => Log.L.Msg($"ModLoader.LoadModPack  {folderPath}");
}
