using DCMultiplayer.Networking;
using DCMultiplayer.Replication;
using Il2CppSteamworks;
using UnityEngine;

namespace DCMultiplayer.UI;

internal static class Hud
{
    public static void Draw()
    {
        DrawStatus();
        DrawEvents();
    }

    static void DrawEvents()
    {
        if (EventLog.Count == 0) return;
        const float w = 380f;
        const float lineH = 18f;
        float h = 22f + lineH * EventLog.Count;
        const float margin = 8f;
        float x = Screen.width - w - margin;
        float y = margin;
        GUI.Box(new Rect(x, y, w, h), "");
        GUILayout.BeginArea(new Rect(x + 8f, y + 4f, w - 16f, h - 8f));
        GUILayout.Label("=== events ===");
        float now = Time.realtimeSinceStartup;
        foreach (var e in EventLog.Recent())
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
            GUILayout.Label($"Lobby {SteamLobby.Current.m_SteamID}{role}");
            int n = SteamMatchmaking.GetNumLobbyMembers(SteamLobby.Current);
            GUILayout.Label($"Members: {n}");
            for (int i = 0; i < n; i++)
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
            var pm = Il2Cpp.PlayerManager.instance;
            var p = pm?.playerClass;
            if (p != null)
                GUILayout.Label($"$ {p.money:N0}    XP {p.xp:N0}    rep {p.reputation:N0}");
        }

        GUILayout.EndArea();
    }
}
