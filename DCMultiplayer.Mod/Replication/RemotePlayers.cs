using System.Collections.Generic;
using DCMultiplayer.Networking;
using Il2CppSteamworks;
using MelonLoader;
using UnityEngine;

namespace DCMultiplayer.Replication;

internal static class RemotePlayers
{
    public static MelonLogger.Instance Log = new("DC_MP_Avatar");

    class Avatar
    {
        public GameObject Go;
        public Vector3 TargetPos;
        public Vector3 CurrentPos;
        public float TargetYaw;
        public float CurrentYaw;
        public float LastSeen;
        public string Name;
        public int PacketCount;
    }

    static readonly Dictionary<ulong, Avatar> _avatars = new();
    const float SmoothLerp = 12f;       // larger = snappier
    const float StaleAfterSec = 5f;     // despawn if no packet in N seconds

    public static int Count => _avatars.Count;
    public static Vector3? FirstAvatarPosition()
    {
        foreach (var av in _avatars.Values)
            if (av.Go != null) return av.CurrentPos;
        return null;
    }

    public static void OnIncoming(CSteamID from, byte channel, byte[] data)
    {
        if (channel != Transport.ChState) return;
        if (!NetMsg.TryReadPlayerPose(data, out float x, out float y, out float z, out float yaw, out float pitch))
            return;

        ulong id = from.m_SteamID;
        if (!_avatars.TryGetValue(id, out var av))
        {
            av = SpawnAvatar(from);
            av.CurrentPos = new Vector3(x, y, z); // start at first received position, no lerp from origin
            _avatars[id] = av;
        }
        av.TargetPos = new Vector3(x, y, z);
        av.TargetYaw = yaw;
        av.LastSeen = Time.realtimeSinceStartup;

        if (++av.PacketCount <= 3)
            Log.Msg($"pose <- {av.Name}  pos=({x:F1},{y:F1},{z:F1}) yaw={yaw:F0}  (#{av.PacketCount})");
    }

    public static void Tick(float dt)
    {
        // smooth toward target + prune stale
        float now = Time.realtimeSinceStartup;
        List<ulong> toRemove = null;
        foreach (var kv in _avatars)
        {
            var av = kv.Value;
            if (av.Go == null) { (toRemove ??= new()).Add(kv.Key); continue; }
            if (now - av.LastSeen > StaleAfterSec) { (toRemove ??= new()).Add(kv.Key); continue; }

            float k = 1f - Mathf.Exp(-SmoothLerp * dt);
            av.CurrentPos = Vector3.Lerp(av.CurrentPos, av.TargetPos, k);
            av.CurrentYaw = Mathf.LerpAngle(av.CurrentYaw, av.TargetYaw, k);
            av.Go.transform.position = av.CurrentPos;
            av.Go.transform.rotation = Quaternion.Euler(0f, av.CurrentYaw, 0f);
        }
        if (toRemove != null)
            foreach (var id in toRemove) Despawn(id);

        // also despawn anyone who left the lobby
        if (SteamLobby.IsInLobby) PruneLeftLobby();
        else ClearAll();
    }

    static void PruneLeftLobby()
    {
        var lobby = SteamLobby.Current;
        int n = SteamMatchmaking.GetNumLobbyMembers(lobby);
        var present = new HashSet<ulong>(n);
        for (int i = 0; i < n; i++)
            present.Add(SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID);

        List<ulong> toKill = null;
        foreach (var id in _avatars.Keys)
            if (!present.Contains(id)) (toKill ??= new()).Add(id);
        if (toKill != null)
            foreach (var id in toKill) Despawn(id);
    }

    public static void ClearAll()
    {
        foreach (var av in _avatars.Values)
            if (av.Go != null) Object.Destroy(av.Go);
        _avatars.Clear();
    }

    static Avatar SpawnAvatar(CSteamID from)
    {
        string name = SteamFriends.GetFriendPersonaName(from);
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = $"DCMP_RemoteAvatar_{from.m_SteamID}";
        // make it impossible to miss while debugging
        go.transform.localScale = new Vector3(1.2f, 1.5f, 1.2f);

        // disable the auto-added collider so we don't shove the local player
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        // Color from steamid hash + emissive so it glows even in the dark
        var rend = go.GetComponent<Renderer>();
        if (rend != null && rend.material != null)
        {
            float hue = (from.m_SteamID & 0xFFu) / 255f;
            var col2 = Color.HSVToRGB(hue, 0.85f, 1f);
            rend.material.color = col2;
            rend.material.SetColor("_EmissionColor", col2 * 1.5f);
            rend.material.EnableKeyword("_EMISSION");
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Object.DontDestroyOnLoad(go);
        Log.Msg($"Spawned avatar for {name} ({from.m_SteamID})");
        return new Avatar { Go = go, Name = name, LastSeen = Time.realtimeSinceStartup };
    }

    static void Despawn(ulong id)
    {
        if (!_avatars.TryGetValue(id, out var av)) return;
        if (av.Go != null) Object.Destroy(av.Go);
        _avatars.Remove(id);
        Log.Msg($"Despawned avatar for {av.Name} ({id})");
    }
}
