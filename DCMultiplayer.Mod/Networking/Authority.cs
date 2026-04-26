namespace DCMultiplayer.Networking;

// One-stop check for "am I a non-host peer in an active lobby?"
// Used by ClientSuppression patches to disable code paths that would otherwise
// run a parallel simulation on the client.
internal static class Authority
{
    /// <summary>Debug toggle — pretend to be a client even when alone, so the
    /// suppression patches can be exercised in a single instance. Not bound
    /// to any input; flip at the source (or via reflection from another
    /// mod) when needed.</summary>
#pragma warning disable CS0649
    public static bool ForceClient;
#pragma warning restore CS0649

    /// <summary>When ON, joining as client triggers SaveSystem.LoadGame
    /// post-processing that empties the world-state portion of the SaveData
    /// (network, racks, items, technicians, mods) so the client's data
    /// center loads scenes-only and waits to be populated by host snapshots.
    /// Off by default — opt-in until validated. Flip at the source.</summary>
#pragma warning disable CS0649
    public static bool SuppressClientSave;
#pragma warning restore CS0649

    public static bool IsHost => !ForceClient && SteamLobby.IsHost;

    public static bool IsInLobby => SteamLobby.IsInLobby;

    /// <summary>True only when we are connected as a non-host peer.</summary>
    public static bool IsClient => ForceClient || (SteamLobby.IsInLobby && !SteamLobby.IsHost);

    /// <summary>True when we own the simulation (single-player or hosting).</summary>
    public static bool IsAuthoritative => !ForceClient && (!SteamLobby.IsInLobby || SteamLobby.IsHost);
}
