using System.Collections.Generic;
using DCMultiplayer.Networking;
using DCMultiplayer.Replication;
using Il2CppSteamworks;
using UnityEngine;

namespace DCMultiplayer.UI;

internal static class Hud
{
    // OnGUI is invoked twice per frame (Layout + Repaint) and both passes must
    // emit the exact same number of controls. Anything dynamic (event log,
    // player money, lobby member count) must be snapshotted once at the very
    // start so both passes see identical data.
    static readonly List<EventLog.Entry> _eventSnap = new();
    static int _lobbyMemberCount;
    static bool _hasPlayer;
    static float _money, _xp, _rep;
    static string _versionMismatch, _workshopMismatch;

    public static void Draw()
    {
        if (Event.current.type == EventType.Layout) CaptureSnapshot();
        DrawStatus();
        DrawEvents();
    }

    static void CaptureSnapshot()
    {
        _eventSnap.Clear();
        foreach (var e in EventLog.Recent()) _eventSnap.Add(e);

        _lobbyMemberCount = SteamLobby.IsInLobby
            ? SteamMatchmaking.GetNumLobbyMembers(SteamLobby.Current)
            : 0;

        var pm = Il2Cpp.PlayerManager.instance;
        var p = pm?.playerClass;
        _hasPlayer = p != null;
        if (_hasPlayer)
        {
            _money = p.money;
            _xp = p.xp;
            _rep = p.reputation;
        }

        _versionMismatch = SteamLobby.VersionMismatch;
        _workshopMismatch = SteamLobby.WorkshopMismatch;
    }

    static void DrawEvents()
    {
        if (_eventSnap.Count == 0) return;
        const float w = 380f;
        const float lineH = 18f;
        float h = 22f + lineH * _eventSnap.Count;
        const float margin = 8f;
        float x = Screen.width - w - margin;
        float y = margin;
        GUI.Box(new Rect(x, y, w, h), "");
        GUILayout.BeginArea(new Rect(x + 8f, y + 4f, w - 16f, h - 8f));
        GUILayout.Label("=== events ===");
        float now = Time.realtimeSinceStartup;
        foreach (var e in _eventSnap)
        {
            float age = now - e.Time;
            GUILayout.Label($"[{age:F0}s] {e.Source}: {e.Text}");
        }
        GUILayout.EndArea();
    }

    static void DrawStatus()
    {
        const float w = 360f;
        const float h = 230f;
        const float margin = 8f;
        float y = Screen.height - h - margin;
        GUI.Box(new Rect(margin, y, w, h), "");
        GUILayout.BeginArea(new Rect(margin + 8f, y + 6f, w - 16f, h - 12f));

        GUILayout.Label($"=== DC Multiplayer {DCMultiplayer.ModInfo.Version} ===");

        if (!Il2Cpp.SteamManager.Initialized)
        {
            GUILayout.Label("Steam not ready");
        }
        else if (!SteamLobby.IsInLobby)
        {
            string forced = DCMultiplayer.Networking.Authority.ForceClient ? "  [ForceClient ON]" : "";
            GUILayout.Label("Idle" + forced);
            GUILayout.Label("F6 force-client   F8 host   F11 invite");
        }
        else
        {
            string role = SteamLobby.IsHost ? "  (host)" : "  (client — sim suppressed)";
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Lobby {SteamLobby.Current.m_SteamID}{role}");
            if (GUILayout.Button("copy", GUILayout.Width(50f)))
                GUIUtility.systemCopyBuffer = SteamLobby.Current.m_SteamID.ToString();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_versionMismatch))
                GUILayout.Label($"!! version mismatch ({_versionMismatch}) !!");
            if (!string.IsNullOrEmpty(_workshopMismatch))
                GUILayout.Label($"!! workshop mismatch ({_workshopMismatch}) !!");
            GUILayout.Label($"Members: {_lobbyMemberCount}");
            for (int i = 0; i < _lobbyMemberCount; i++)
            {
                var id = SteamMatchmaking.GetLobbyMemberByIndex(SteamLobby.Current, i);
                string name = SteamFriends.GetFriendPersonaName(id);
                bool isMe = id == SteamUser.GetSteamID();
                bool isOwner = id == SteamMatchmaking.GetLobbyOwner(SteamLobby.Current);
                GUILayout.Label($"  - {name}{(isMe ? " (you)" : "")}{(isOwner ? " *host" : "")}");
            }
            GUILayout.Label("F7 warp  F9 leave  F10 dump  F11 invite  F12 ping");
            GUILayout.Label($"net  rx {Transport.LastRxPackets}p / {Transport.LastRxBytes}B   tx {Transport.LastTxPackets}p / {Transport.LastTxBytes}B");
            GUILayout.Label($"avatars: {DCMultiplayer.Replication.RemotePlayers.Count}   last <- {Transport.LastRxFrom}: {Transport.LastRxText}");
            // Always render the money line (constant control count between Layout/Repaint).
            // When no Player is loaded yet we just show dashes.
            GUILayout.Label(_hasPlayer ? $"$ {_money:N0}    XP {_xp:N0}    rep {_rep:N0}" : "$ —    XP —    rep —");
        }

        GUILayout.EndArea();
    }
}
