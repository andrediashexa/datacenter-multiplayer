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
        SteamMatchmaking.SetLobbyData(Current, "dcmp_version", DCMultiplayer.ModInfo.Version);
        SteamMatchmaking.SetLobbyData(Current, "dcmp_host_name", SteamFriends.GetPersonaName());
        Log.Msg($"Lobby created  id={Current.m_SteamID}  (you are host)");
        DumpMembers();
    }

    static void OnLobbyEntered(LobbyEnter_t r)
    {
        Current = new CSteamID(r.m_ulSteamIDLobby);
        IsHost = SteamMatchmaking.GetLobbyOwner(Current) == SteamUser.GetSteamID();
        Log.Msg($"Lobby entered  id={Current.m_SteamID}  asHost={IsHost}");
        Log.Msg($"  version={SteamMatchmaking.GetLobbyData(Current, "dcmp_version")}  hostName={SteamMatchmaking.GetLobbyData(Current, "dcmp_host_name")}");
        DumpMembers();
    }

    static void OnLobbyChatUpdate(LobbyChatUpdate_t r)
    {
        var changed = new CSteamID(r.m_ulSteamIDUserChanged);
        var changeFlags = (EChatMemberStateChange)r.m_rgfChatMemberStateChange;
        string name = SteamFriends.GetFriendPersonaName(changed);
        Log.Msg($"ChatUpdate  user={name}({changed.m_SteamID})  change={changeFlags}");
        DumpMembers();
    }

    static void OnJoinRequested(GameLobbyJoinRequested_t r)
    {
        Log.Msg($"JoinRequested  lobby={r.m_steamIDLobby.m_SteamID}  friend={r.m_steamIDFriend.m_SteamID}");
        SteamMatchmaking.JoinLobby(r.m_steamIDLobby);
    }
}
