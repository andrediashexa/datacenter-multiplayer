using DCMultiplayer.Networking;
using DCMultiplayer.Replication;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(DCMultiplayer.Mod), DCMultiplayer.ModInfo.Name, DCMultiplayer.ModInfo.Version, DCMultiplayer.ModInfo.Author)]
[assembly: MelonGame("Waseku", "Data Center")]

namespace DCMultiplayer;

public class Mod : MelonMod
{
    bool _steamReady;

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg($"DCMultiplayer {ModInfo.Version} alive — IL2CPP, Unity 6000.4.2f1");
        LoggerInstance.Msg("All controls live on the in-game computer's Multiplayer panel.");
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
        Transport.OnMessage += IntentBus.OnIncoming;
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
