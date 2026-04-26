using System;
using Il2CppSteamworks;
using MelonLoader;

namespace DCMultiplayer.Networking;

// Phase A: lobby only. No transport, no replication. Just verify CreateLobby /
// JoinLobby / OnLobbyChatUpdate fire and we can read the member list.
//
// SteamAPI.RunCallbacks() is already pumped by the game's SteamManager.Update,
// so Callback<T>.Create suffices on our side.

internal static class SteamLobby
{
    public const int MaxMembers = 4;
    public static MelonLogger.Instance Log = new("DC_MP_Lobby");

    public static CSteamID Current { get; private set; } = CSteamID.Nil;
    public static bool IsInLobby => Current != CSteamID.Nil;
    public static bool IsHost { get; private set; }

    /// <summary>Set on join when the lobby's advertised version doesn't match
    /// our build. Banner in HUD reads this.</summary>
    public static string VersionMismatch { get; private set; }

    /// <summary>Set on join when our local Workshop folder list disagrees
    /// with the host's. Banner in HUD reads this.</summary>
    public static string WorkshopMismatch { get; private set; }

    static Callback<LobbyCreated_t> _cbCreated;
    static Callback<LobbyEnter_t> _cbEntered;
    static Callback<LobbyChatUpdate_t> _cbChat;
    static Callback<GameLobbyJoinRequested_t> _cbJoinReq;

    public static void Init()
    {
        if (!Il2Cpp.SteamManager.Initialized)
        {
            Log.Error("SteamManager not initialized — Steam not running?");
            return;
        }
        var me = SteamUser.GetSteamID();
        Log.Msg($"Init  steamId={me.m_SteamID}  name={SteamFriends.GetPersonaName()}");

        _cbCreated = Callback<LobbyCreated_t>.Create((Action<LobbyCreated_t>)OnLobbyCreated);
        _cbEntered = Callback<LobbyEnter_t>.Create((Action<LobbyEnter_t>)OnLobbyEntered);
        _cbChat = Callback<LobbyChatUpdate_t>.Create((Action<LobbyChatUpdate_t>)OnLobbyChatUpdate);
        _cbJoinReq = Callback<GameLobbyJoinRequested_t>.Create((Action<GameLobbyJoinRequested_t>)OnJoinRequested);
    }

    public static void HostLobby()
    {
        if (IsInLobby) { Log.Warning("Already in a lobby"); return; }
        Log.Msg($"CreateLobby (FriendsOnly, max={MaxMembers})");
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MaxMembers);
    }

    public static void Leave()
    {
        if (!IsInLobby) { Log.Warning("Not in a lobby"); return; }
        Log.Msg($"LeaveLobby  {Current.m_SteamID}");
        SteamMatchmaking.LeaveLobby(Current);
        Current = CSteamID.Nil;
        IsHost = false;
    }

    public static void DumpMembers()
    {
        if (!IsInLobby) { Log.Msg("Not in a lobby"); return; }
        int n = SteamMatchmaking.GetNumLobbyMembers(Current);
        Log.Msg($"Members ({n}):");
        for (int i = 0; i < n; i++)
        {
            var id = SteamMatchmaking.GetLobbyMemberByIndex(Current, i);
            string name = SteamFriends.GetFriendPersonaName(id);
            bool isMe = id == SteamUser.GetSteamID();
            bool isOwner = id == SteamMatchmaking.GetLobbyOwner(Current);
            Log.Msg($"  [{i}] {id.m_SteamID}  {name}{(isMe ? " (me)" : "")}{(isOwner ? " (host)" : "")}");
        }
    }

    public static void InviteFriendsOverlay()
    {
        if (!IsInLobby) { Log.Warning("Not in a lobby"); return; }
        SteamFriends.ActivateGameOverlayInviteDialog(Current);
    }

    static void OnLobbyCreated(LobbyCreated_t r)
    {
        if (r.m_eResult != EResult.k_EResultOK)
        {
            Log.Error($"Lobby creation failed: {r.m_eResult}");
            return;
        }
        Current = new CSteamID(r.m_ulSteamIDLobby);
        IsHost = true;
        VersionMismatch = null;
        WorkshopMismatch = null;
        SteamMatchmaking.SetLobbyData(Current, "dcmp_version", DCMultiplayer.ModInfo.Version);
        SteamMatchmaking.SetLobbyData(Current, "dcmp_host_name", SteamFriends.GetPersonaName());
        SteamMatchmaking.SetLobbyData(Current, "dcmp_workshop", WorkshopManifest.Encode(WorkshopManifest.LocalIds()));
        Log.Msg($"Lobby created  id={Current.m_SteamID}  (you are host)");
        DumpMembers();
    }

    static void OnLobbyEntered(LobbyEnter_t r)
    {
        Current = new CSteamID(r.m_ulSteamIDLobby);
        IsHost = SteamMatchmaking.GetLobbyOwner(Current) == SteamUser.GetSteamID();
        VersionMismatch = null;
        WorkshopMismatch = null;

        string hostVersion = SteamMatchmaking.GetLobbyData(Current, "dcmp_version");
        string hostName = SteamMatchmaking.GetLobbyData(Current, "dcmp_host_name");
        Log.Msg($"Lobby entered  id={Current.m_SteamID}  asHost={IsHost}");
        Log.Msg($"  version={hostVersion}  hostName={hostName}");

        if (!IsHost)
        {
            // Compare versions
            if (!string.IsNullOrEmpty(hostVersion) && hostVersion != DCMultiplayer.ModInfo.Version)
            {
                VersionMismatch = $"host={hostVersion} mine={DCMultiplayer.ModInfo.Version}";
                Log.Warning($"VERSION MISMATCH: {VersionMismatch}");
            }

            // Compare workshop manifests
            string hostManifest = SteamMatchmaking.GetLobbyData(Current, "dcmp_workshop");
            var hostIds = WorkshopManifest.Decode(hostManifest);
            var mineIds = WorkshopManifest.LocalIds();
            var (missing, extra) = WorkshopManifest.Diff(hostIds, mineIds);
            if (missing.Count > 0 || extra.Count > 0)
            {
                WorkshopMismatch = $"missing {missing.Count}, extra {extra.Count}";
                Log.Warning($"WORKSHOP MISMATCH: {WorkshopMismatch}");
                if (missing.Count > 0) Log.Warning($"  missing here: {string.Join(", ", missing)}");
                if (extra.Count > 0) Log.Warning($"  extra here:   {string.Join(", ", extra)}");
            }
        }

        DumpMembers();
    }

    static void OnLobbyChatUpdate(LobbyChatUpdate_t r)
    {
        var changed = new CSteamID(r.m_ulSteamIDUserChanged);
        var changeFlags = (EChatMemberStateChange)r.m_rgfChatMemberStateChange;
        string name = SteamFriends.GetFriendPersonaName(changed);
        Log.Msg($"ChatUpdate  user={name}({changed.m_SteamID})  change={changeFlags}");
        DumpMembers();

        // Host-only: when a peer joins, push the initial snapshot directly to them.
        // For now snapshot consists of the customer pool; will grow as more
        // subsystems are wrapped.
        if (IsHost
            && changed != SteamUser.GetSteamID()
            && (changeFlags & EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
        {
            try { DCMultiplayer.Replication.CustomerPoolSync.SendTo(changed); }
            catch (System.Exception ex) { Log.Error($"customer pool snapshot send failed: {ex.Message}"); }
            try { DCMultiplayer.Replication.ServerSnapshotSync.SendTo(changed); }
            catch (System.Exception ex) { Log.Error($"server snapshot send failed: {ex.Message}"); }
            try { DCMultiplayer.Replication.SwitchSnapshotSync.SendTo(changed); }
            catch (System.Exception ex) { Log.Error($"switch snapshot send failed: {ex.Message}"); }
            try { DCMultiplayer.Replication.BaseAssignmentsSync.SendTo(changed); }
            catch (System.Exception ex) { Log.Error($"base assignments snapshot send failed: {ex.Message}"); }
        }
    }

    static void OnJoinRequested(GameLobbyJoinRequested_t r)
    {
        Log.Msg($"JoinRequested  lobby={r.m_steamIDLobby.m_SteamID}  friend={r.m_steamIDFriend.m_SteamID}");
        SteamMatchmaking.JoinLobby(r.m_steamIDLobby);
    }
}
