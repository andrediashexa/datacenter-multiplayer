using System.Collections.Generic;
using DCMultiplayer.Networking;
using Il2CppSteamworks;
using MelonLoader;
using UnityEngine;

namespace DCMultiplayer.Replication;

// Switch counterpart of ServerSnapshotSync. Reads from
// Il2Cpp.NetworkMap.instance.switches, ships pose+identity per switch,
// renders coloured cube ghosts on the client.
internal static class SwitchSnapshotSync
{
    public static MelonLogger.Instance Log = new("DC_MP_Sw");

    class Ghost
    {
        public GameObject Go;
        public NetMsg.SwitchRec Rec;
    }

    static readonly Dictionary<string, Ghost> _ghosts = new();
    public static int Count => _ghosts.Count;

    public static bool TryGetPosition(string switchId, out Vector3 pos)
    {
        if (_ghosts.TryGetValue(switchId, out var g) && g.Go != null)
        { pos = g.Go.transform.position; return true; }
        pos = default; return false;
    }

    static List<NetMsg.SwitchRec> CollectFromHost()
    {
        var list = new List<NetMsg.SwitchRec>();
        var nm = Il2Cpp.NetworkMap.instance;
        if (nm == null) return list;
        var dict = nm.switches;
        if (dict == null) return list;

        foreach (var kv in dict)
        {
            var s = kv.Value;
            if (s == null) continue;
            var t = s.transform;
            list.Add(new NetMsg.SwitchRec(
                s.switchId ?? kv.Key,
                t.position.x, t.position.y, t.position.z,
                t.eulerAngles.y,
                s.switchType,
                s.isOn,
                s.isBroken
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
        var bytes = NetMsg.WriteSwitchSnapshot(recs);
        Transport.Broadcast(Transport.ChEvent, bytes);
        Log.Msg($"-> snapshot ({recs.Count} switches, {bytes.Length} bytes)");
    }

    public static void SendTo(CSteamID target)
    {
        if (!Authority.IsAuthoritative) return;
        var recs = CollectFromHost();
        var bytes = NetMsg.WriteSwitchSnapshot(recs);
        Transport.SendTo(target, Transport.ChEvent, bytes);
        Log.Msg($"-> snapshot ({recs.Count} switches, {bytes.Length} bytes) to {SteamFriends.GetFriendPersonaName(target)}");
    }

    public static void OnIncoming(CSteamID from, byte channel, byte[] data)
    {
        if (channel != Transport.ChEvent) return;
        if (data.Length < 1 || data[0] != NetMsg.MsgSwitchSnapshot) return;
        if (!Authority.IsClient) return;
        if (!NetMsg.TryReadSwitchSnapshot(data, out var recs)) return;

        var present = new HashSet<string>(recs.Length);
        foreach (var r in recs)
        {
            present.Add(r.SwitchId);
            if (!_ghosts.TryGetValue(r.SwitchId, out var g))
            {
                g = SpawnGhost(r);
                _ghosts[r.SwitchId] = g;
            }
            ApplyTo(g, r);
        }

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

    static Ghost SpawnGhost(NetMsg.SwitchRec r)
    {
        // Slightly thinner / shorter than server ghosts so a stack of
        // mixed devices looks layered when populated together.
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"DCMP_SwitchGhost_{r.SwitchId}";
        go.transform.localScale = new Vector3(0.5f, 0.04f, 0.6f);

        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        var rend = go.GetComponent<Renderer>();
        if (rend != null && rend.material != null)
        {
            // Switches use a green-leaning hue band so they're visually
            // distinct from servers (which span the full hue range).
            float hue = 0.30f + (((uint)r.SwitchId.GetHashCode() & 0xFFu) / 255f) * 0.20f;
            var c = Color.HSVToRGB(hue, 0.6f, r.IsOn ? 1f : 0.4f);
            rend.material.color = c;
            rend.material.SetColor("_EmissionColor", c * (r.IsOn ? 1.2f : 0.2f));
            rend.material.EnableKeyword("_EMISSION");
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Object.DontDestroyOnLoad(go);
        Log.Msg($"Spawned ghost {r.SwitchId}  type={r.SwitchType}");
        return new Ghost { Go = go, Rec = r };
    }

    static void ApplyTo(Ghost g, NetMsg.SwitchRec r)
    {
        g.Go.transform.position = new Vector3(r.X, r.Y, r.Z);
        g.Go.transform.rotation = Quaternion.Euler(0f, r.Yaw, 0f);
        if (g.Rec.IsOn != r.IsOn || g.Rec.IsBroken != r.IsBroken)
        {
            var rend = g.Go.GetComponent<Renderer>();
            if (rend != null && rend.material != null)
            {
                float hue = 0.30f + (((uint)r.SwitchId.GetHashCode() & 0xFFu) / 255f) * 0.20f;
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

    public static void Tick(float dt)
    {
        if (!SteamLobby.IsInLobby || !Authority.IsClient)
            if (_ghosts.Count > 0) ClearAll();
    }
}
