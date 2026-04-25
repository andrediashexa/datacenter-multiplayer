namespace DCMultiplayer.Networking;

// One-stop check for "am I a non-host peer in an active lobby?"
// Used by ClientSuppression patches to disable code paths that would otherwise
// run a parallel simulation on the client.
internal static class Authority
{
    /// <summary>Debug toggle — pretend to be a client even when alone, so the
    /// suppression patches can be exercised in a single instance.</summary>
    public static bool ForceClient;

    public static bool IsHost => !ForceClient && SteamLobby.IsHost;

    public static bool IsInLobby => SteamLobby.IsInLobby;

    /// <summary>True only when we are connected as a non-host peer.</summary>
    public static bool IsClient => ForceClient || (SteamLobby.IsInLobby && !SteamLobby.IsHost);

    /// <summary>True when we own the simulation (single-player or hosting).</summary>
    public static bool IsAuthoritative => !ForceClient && (!SteamLobby.IsInLobby || SteamLobby.IsHost);
}
