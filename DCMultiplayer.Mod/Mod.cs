using DCMultiplayer.Networking;
using DCMultiplayer.Replication;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(DCMultiplayer.Mod), DCMultiplayer.ModInfo.Name, DCMultiplayer.ModInfo.Version, DCMultiplayer.ModInfo.Author)]
[assembly: MelonGame("Waseku", "Data Center")]

namespace DCMultiplayer;

public class Mod : MelonMod
{
    bool _steamReady;

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("DCMultiplayer alive — IL2CPP, Unity 6000.4.2f1");
        LoggerInstance.Msg("Hotkeys:  F4 suppress-save  F5 loopback  F6 force-client  F7 warp-to-remote  F8 host  F9 leave  F10 dump  F11 invite  F12 ping");
        Transport.OnMessage += OnNetworkMessage;
        Transport.OnMessage += RemotePlayers.OnIncoming;
        Transport.OnMessage += EconomySync.OnIncoming;
        Transport.OnMessage += EventLog.OnIncoming;
        Transport.OnMessage += CustomerPoolSync.OnIncoming;
        Transport.OnMessage += ServerSnapshotSync.OnIncoming;
        Transport.OnMessage += SwitchSnapshotSync.OnIncoming;
        Transport.OnMessage += BaseAssignmentsSync.OnIncoming;
        Transport.OnMessage += CableSnapshotSync.OnIncoming;
        Transport.OnMessage += PatchPanelSnapshotSync.OnIncoming;
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        LoggerInstance.Msg($"Scene initialized: [{buildIndex}] {sceneName}");
        if (!_steamReady && Il2Cpp.SteamManager.Initialized)
        {
            try { SteamLobby.Init(); }
            catch (System.Exception ex) { LoggerInstance.Error($"SteamLobby.Init failed: {ex.Message}"); }
            try { Transport.Init(); }
            catch (System.Exception ex) { LoggerInstance.Error($"Transport.Init failed: {ex.Message}"); }
            _steamReady = true;
        }
    }

    public override void OnUpdate()
    {
        if (!_steamReady) return;
        Transport.Pump();
        float dt = Time.deltaTime;
        PlayerSync.Tick(dt);
        EconomySync.Tick(dt);
        RemotePlayers.Tick(dt);
        ServerSnapshotSync.Tick(dt);
        SwitchSnapshotSync.Tick(dt);
        PatchPanelSnapshotSync.Tick(dt);
        CableSnapshotSync.Tick(dt);
        ComputerShopBadge.RefreshText();
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb[Key.F4].wasPressedThisFrame)  { Authority.SuppressClientSave = !Authority.SuppressClientSave; LoggerInstance.Msg($"SuppressClientSave = {Authority.SuppressClientSave}"); }
        if (kb[Key.F5].wasPressedThisFrame)  { Transport.DebugLoopback = !Transport.DebugLoopback; LoggerInstance.Msg($"DebugLoopback = {Transport.DebugLoopback}"); }
        if (kb[Key.F6].wasPressedThisFrame)  { Authority.ForceClient = !Authority.ForceClient; LoggerInstance.Msg($"ForceClient = {Authority.ForceClient}"); }
        if (kb[Key.F7].wasPressedThisFrame)  TeleportToFirstAvatar();
        if (kb[Key.F8].wasPressedThisFrame)  SteamLobby.HostLobby();
        if (kb[Key.F9].wasPressedThisFrame)  SteamLobby.Leave();
        if (kb[Key.F10].wasPressedThisFrame) SteamLobby.DumpMembers();
        if (kb[Key.F11].wasPressedThisFrame) SteamLobby.InviteFriendsOverlay();
        if (kb[Key.F12].wasPressedThisFrame) BroadcastPing();
    }

    void TeleportToFirstAvatar()
    {
        var pos = RemotePlayers.FirstAvatarPosition();
        if (!pos.HasValue) { LoggerInstance.Msg("F7: no remote avatar to teleport to"); return; }
        var pm = Il2Cpp.PlayerManager.instance;
        var p = pm?.playerClass;
        if (p == null) { LoggerInstance.Msg("F7: no local Player"); return; }
        // step a bit back from the avatar so we don't intersect it
        var target = pos.Value + new Vector3(0f, 0.5f, -2f);
        p.WarpPlayer(target, Quaternion.identity);
        LoggerInstance.Msg($"F7: warped to {target}");
    }

    // The IMGUI overlay was the original lobby surface; v0.0.16 moved that
    // UX onto the in-game ComputerShop terminal. The HUD code is kept for
    // reference (in case we want to bring back a corner status indicator
    // for non-computer scenes) but no longer rendered.
    // public override void OnGUI() => Hud.Draw();

    void BroadcastPing()
    {
        if (!SteamLobby.IsInLobby) { LoggerInstance.Msg("Ping: not in lobby"); return; }
        string text = $"PING from {Il2CppSteamworks.SteamFriends.GetPersonaName()} @ {System.DateTime.Now:HH:mm:ss.fff}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        Transport.Broadcast(Transport.ChControl, bytes);
        LoggerInstance.Msg($"-> {text}");
    }

    void OnNetworkMessage(Il2CppSteamworks.CSteamID from, byte channel, byte[] data)
    {
        if (channel == Transport.ChControl)
        {
            string text = System.Text.Encoding.UTF8.GetString(data);
            LoggerInstance.Msg($"<- ch{channel} from {Il2CppSteamworks.SteamFriends.GetFriendPersonaName(from)}: {text}");

            if (text.StartsWith("PING"))
            {
                string reply = $"PONG echo of '{text}'";
                Transport.SendTo(from, Transport.ChControl, System.Text.Encoding.UTF8.GetBytes(reply));
            }
        }
    }
}
