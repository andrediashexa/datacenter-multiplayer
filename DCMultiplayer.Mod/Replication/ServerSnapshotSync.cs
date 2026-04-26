using System.Collections.Generic;
using DCMultiplayer.Networking;
using Il2CppSteamworks;
using MelonLoader;
using UnityEngine;

namespace DCMultiplayer.Replication;

// First slice of "what's actually in the host's data center". Read-only ghost
// rendering: each remote server appears as a coloured cube at the host's
// world-space position. No interaction, no game-side registration with
// NetworkMap on the client. Updated whenever the host registers a new server
// or any of the things we track changes (Power, IP, customer, broken state).
internal static class ServerSnapshotSync
{
    public static MelonLogger.Instance Log = new("DC_MP_Srv");

    class Ghost
    {
        public GameObject Go;
        public NetMsg.ServerRec Rec;
    }

    static readonly Dictionary<string, Ghost> _ghosts = new();
    public static int Count => _ghosts.Count;

    // ── send (host) ──────────────────────────────────────────────────────

    static List<NetMsg.ServerRec> CollectFromHost()
    {
        var list = new List<NetMsg.ServerRec>();
        var nm = Il2Cpp.NetworkMap.instance;
        if (nm == null) return list;
        var dict = nm.servers;
        if (dict == null) return list;

        foreach (var kv in dict)
        {
            var s = kv.Value;
            if (s == null) continue;
            var t = s.transform;
            list.Add(new NetMsg.ServerRec(
                s.ServerID ?? kv.Key,
                t.position.x, t.position.y, t.position.z,
                t.eulerAngles.y,
                s.serverType,
                s.isOn,
                s.isBroken,
                s.GetCustomerID(),
                s.appID,
                s.IP ?? ""
            ));
        }
        return list;
    }

    public static void BroadcastSnapshot()
    {
        if (!Authority.IsAuthoritative) return;
        if (!SteamLobby.IsInLobby) return;
        var recs = CollectFromHost();
        if (recs.Count == 0) return;
        var bytes = NetMsg.WriteServerSnapshot(recs);
        Transport.Broadcast(Transport.ChEvent, bytes);
        Log.Msg($"-> snapshot ({recs.Count} servers, {bytes.Length} bytes)");
    }

    public static void SendTo(CSteamID target)
    {
        if (!Authority.IsAuthoritative) return;
        var recs = CollectFromHost();
        var bytes = NetMsg.WriteServerSnapshot(recs);
        Transport.SendTo(target, Transport.ChEvent, bytes);
        Log.Msg($"-> snapshot ({recs.Count} servers, {bytes.Length} bytes) to {SteamFriends.GetFriendPersonaName(target)}");
    }

    // ── receive (client) ─────────────────────────────────────────────────

    public static void OnIncoming(CSteamID from, byte channel, byte[] data)
    {
        if (channel != Transport.ChEvent) return;
        if (data.Length < 1 || data[0] != NetMsg.MsgServerSnapshot) return;
        if (!Authority.IsClient) return;
        if (!NetMsg.TryReadServerSnapshot(data, out var recs)) return;

        var present = new HashSet<string>(recs.Length);
        foreach (var r in recs)
        {
            present.Add(r.ServerId);
            if (!_ghosts.TryGetValue(r.ServerId, out var g))
            {
                g = SpawnGhost(r);
                _ghosts[r.ServerId] = g;
            }
            ApplyTo(g, r);
        }

        // Despawn ghosts the host no longer reports.
        if (_ghosts.Count > recs.Length)
        {
            List<string> toKill = null;
            foreach (var key in _ghosts.Keys)
                if (!present.Contains(key)) (toKill ??= new()).Add(key);
            if (toKill != null)
                foreach (var key in toKill) Despawn(key);
        }

        Log.Msg($"<- snapshot applied ({recs.Length} ghosts)");
    }

    static Ghost SpawnGhost(NetMsg.ServerRec r)
    {
        // Use a flat cube as a placeholder rack-unit slab.
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"DCMP_ServerGhost_{r.ServerId}";
        go.transform.localScale = new Vector3(0.5f, 0.05f, 0.7f); // 1U-ish

        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        var rend = go.GetComponent<Renderer>();
        if (rend != null && rend.material != null)
        {
            float hue = ((uint)r.ServerId.GetHashCode() & 0xFFu) / 255f;
            var c = Color.HSVToRGB(hue, 0.6f, r.IsOn ? 1f : 0.4f);
            rend.material.color = c;
            rend.material.SetColor("_EmissionColor", c * (r.IsOn ? 1.2f : 0.2f));
            rend.material.EnableKeyword("_EMISSION");
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Object.DontDestroyOnLoad(go);
        Log.Msg($"Spawned ghost {r.ServerId}  type={r.ServerType} ip={r.Ip}");
        return new Ghost { Go = go, Rec = r };
    }

    static void ApplyTo(Ghost g, NetMsg.ServerRec r)
    {
        g.Go.transform.position = new Vector3(r.X, r.Y, r.Z);
        g.Go.transform.rotation = Quaternion.Euler(0f, r.Yaw, 0f);
        // Update colour if power state changed
        if (g.Rec.IsOn != r.IsOn || g.Rec.IsBroken != r.IsBroken)
        {
            var rend = g.Go.GetComponent<Renderer>();
            if (rend != null && rend.material != null)
            {
                float hue = ((uint)r.ServerId.GetHashCode() & 0xFFu) / 255f;
                var c = Color.HSVToRGB(hue, 0.6f, r.IsOn ? 1f : 0.4f);
                if (r.IsBroken) c = Color.red;
                rend.material.color = c;
                rend.material.SetColor("_EmissionColor", c * (r.IsOn ? 1.2f : 0.2f));
            }
        }
        g.Rec = r;
    }

    static void Despawn(string id)
    {
        if (!_ghosts.TryGetValue(id, out var g)) return;
        if (g.Go != null) Object.Destroy(g.Go);
        _ghosts.Remove(id);
        Log.Msg($"Despawned ghost {id}");
    }

    public static void ClearAll()
    {
        foreach (var g in _ghosts.Values)
            if (g.Go != null) Object.Destroy(g.Go);
        _ghosts.Clear();
    }

    // ── housekeeping (run from Mod.OnUpdate) ─────────────────────────────

    public static void Tick(float dt)
    {
        // If we left the lobby or stopped being a client, drop ghosts.
        if (!SteamLobby.IsInLobby || !Authority.IsClient)
            if (_ghosts.Count > 0) ClearAll();
    }
}
