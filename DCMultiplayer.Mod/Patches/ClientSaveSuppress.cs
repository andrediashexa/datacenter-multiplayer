using DCMultiplayer.Networking;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;

namespace DCMultiplayer.Patches;

// Postfix on SaveSystem.LoadGame(string) -> SaveData. When we are joining as
// client (with the experimental suppress flag on), strip the world-state
// portion of the SaveData *before* it propagates to LoadGameScenesVoid and
// the SaveSystem.onLoadingData callback subscribers.
//
// What we keep:
//   - loadedScenes        — scene set must still load so the world geometry
//                           (walls, floors, fixed props) is there to populate
//   - playerData          — player still needs to spawn somewhere; money/xp
//                           get overwritten by EconomySync within a second
//   - balanceSheetData    — same reasoning
//   - shopItemUnlockStates — UI gating for the shop, doesn't spawn entities
//   - isWallOpened        — walls are part of the building, not infra
//   - nameOfSave / version — metadata
//
// What we wipe (replaced with empty containers):
//   - networkData         — servers / switches / cables (host snapshot will
//                           replace these)
//   - rackMountObjectData — racks
//   - interactObjectData  — placed items in world
//   - modItemData         — workshop instances
//   - technicianData      — NPCs (not yet replicated; they just won't show)
//   - hiredTechnicians
//   - repairJobQueue
//
// Empty containers are required (not null): the consumer code expects
// iterable lists/arrays.

[HarmonyPatch(typeof(Il2Cpp.SaveSystem), nameof(Il2Cpp.SaveSystem.LoadGame))]
internal static class CSS_SaveSystem_LoadGame
{
    static MelonLogger.Instance _l;
    static MelonLogger.Instance L => _l ??= new MelonLogger.Instance("DC_MP_Save");

    static void Postfix(Il2Cpp.SaveData __result)
    {
        if (__result == null) return;
        if (!Authority.IsClient) return;
        if (!Authority.SuppressClientSave) return;

        try
        {
            __result.networkData = new Il2Cpp.NetworkSaveData();
            __result.rackMountObjectData = new Il2CppSystem.Collections.Generic.List<Il2Cpp.InteractObjectData>();
            __result.interactObjectData = new Il2CppSystem.Collections.Generic.List<Il2Cpp.InteractObjectData>();
            __result.modItemData = new Il2CppSystem.Collections.Generic.List<Il2Cpp.ModItemSaveData>();
            __result.technicianData = new Il2CppSystem.Collections.Generic.List<Il2Cpp.TechnicianSaveData>();
            __result.hiredTechnicians = new Il2CppStructArray<int>(0);
            __result.repairJobQueue = new Il2CppSystem.Collections.Generic.List<Il2Cpp.RepairJobSaveData>();

            L.Msg("client save suppression: world-state cleared, keeping scenes/player/balance");
        }
        catch (System.Exception ex)
        {
            L.Error($"client save suppression failed (continuing with full save): {ex.Message}");
        }
    }
}
