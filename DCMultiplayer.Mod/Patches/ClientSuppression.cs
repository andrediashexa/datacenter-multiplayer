using DCMultiplayer.Networking;
using HarmonyLib;
using MelonLoader;

namespace DCMultiplayer.Patches;

// Patches that turn the client into a "thin" simulation: it stops generating
// independent state (auto-save, customer shuffles, money/xp ticks) and only
// applies what the host broadcasts. Each prefix returns false to skip the
// original method when Authority.IsClient is true.

internal static class CSLog
{
    static MelonLogger.Instance _l;
    public static MelonLogger.Instance L => _l ??= new MelonLogger.Instance("DC_MP_Auth");
}

// ── Auto-save ──────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(Il2Cpp.MainGameManager), nameof(Il2Cpp.MainGameManager.RestartAutoSave))]
internal static class CS_MGM_RestartAutoSave
{
    static bool Prefix()
    {
        if (Authority.IsClient) { CSLog.L.Msg("blocked: RestartAutoSave"); return false; }
        return true;
    }
}

[HarmonyPatch(typeof(Il2Cpp.MainGameManager), nameof(Il2Cpp.MainGameManager.SetAutoSaveEnabled))]
internal static class CS_MGM_SetAutoSaveEnabled
{
    static bool Prefix(bool enabled)
    {
        if (Authority.IsClient && enabled) { CSLog.L.Msg("blocked: SetAutoSaveEnabled(true)"); return false; }
        return true;
    }
}

// ── Customer pool generation ───────────────────────────────────────────────

[HarmonyPatch(typeof(Il2Cpp.MainGameManager), nameof(Il2Cpp.MainGameManager.ShuffleAvailableCustomers))]
internal static class CS_MGM_ShuffleAvailableCustomers
{
    static bool Prefix()
    {
        if (Authority.IsClient) { CSLog.L.Msg("blocked: ShuffleAvailableCustomers"); return false; }
        return true;
    }
}

// ── Local economic ticks ───────────────────────────────────────────────────

[HarmonyPatch(typeof(Il2Cpp.Player), nameof(Il2Cpp.Player.UpdateCoin))]
internal static class CS_Player_UpdateCoin
{
    // Returning false here aborts the spend/earn — but we must also write a sane
    // result so callers like BuyItem don't think the transaction succeeded.
    // For now the safe story is: client never produces money events anyway, so
    // anything calling UpdateCoin on the client is a parallel-sim leak that we
    // want to silence. Return false (transaction failed) to keep callers honest.
    static bool Prefix(ref bool __result)
    {
        if (Authority.IsClient) { __result = false; return false; }
        return true;
    }
}

[HarmonyPatch(typeof(Il2Cpp.Player), nameof(Il2Cpp.Player.UpdateXP))]
internal static class CS_Player_UpdateXP
{
    static bool Prefix(ref bool __result)
    {
        if (Authority.IsClient) { __result = false; return false; }
        return true;
    }
}

[HarmonyPatch(typeof(Il2Cpp.Player), nameof(Il2Cpp.Player.UpdateReputation))]
internal static class CS_Player_UpdateReputation
{
    static bool Prefix()
    {
        if (Authority.IsClient) return false;
        return true;
    }
}
