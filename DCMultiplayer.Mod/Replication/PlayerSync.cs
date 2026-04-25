using DCMultiplayer.Networking;
using UnityEngine;

namespace DCMultiplayer.Replication;

internal static class PlayerSync
{
    const float SendIntervalSec = 0.05f; // 20 Hz
    static float _accum;

    public static void Tick(float dt)
    {
        if (!SteamLobby.IsInLobby) return;
        _accum += dt;
        if (_accum < SendIntervalSec) return;
        _accum = 0f;

        var pm = Il2Cpp.PlayerManager.instance;
        if (pm == null) return;
        var go = pm.playerGO;
        if (go == null) return;

        var t = go.transform;
        var pos = t.position;
        float yaw = t.eulerAngles.y;

        // Camera pitch — try the cinemachine vcam first, fall back to main camera
        float pitch = 0f;
        var vcam = pm.vcam;
        if (vcam != null) pitch = vcam.transform.eulerAngles.x;
        else if (Camera.main != null) pitch = Camera.main.transform.eulerAngles.x;

        System.Span<byte> buf = stackalloc byte[NetMsg.PlayerPoseSize];
        NetMsg.WritePlayerPose(buf, pos.x, pos.y, pos.z, yaw, pitch);
        Transport.Broadcast(Transport.ChState, buf.ToArray(), Transport.SendUnreliable);
    }
}
