using System.Collections.Generic;
using DCMultiplayer.Networking;
using Il2CppSteamworks;
using MelonLoader;
using UnityEngine;

namespace DCMultiplayer.Replication;

// Patch panels aren't tracked in NetworkMap (no Dictionary<string,
// PatchPanel>), so we discover them by scanning the live scene with
// Object.FindObjectsOfType. Same render strategy as switches: a thin
// coloured cube ghost in a distinctive hue band.
internal static class PatchPanelSnapshotSync
{
    public static MelonLogger.Instance Log = new("DC_MP_PP");

    class Ghost
    {
        public GameObject Go;
        public NetMsg.PatchPanelRec Rec;
    }

    static readonly Dictionary<string, Ghost> _ghosts = new();
    public static int Count => _ghosts.Count;

    public static bool TryGetPosition(string panelId, out Vector3 pos)
    {
        if (_ghosts.TryGetValue(panelId, out var g) && g.Go != null)
        { pos = g.Go.transform.position; return true; }
        pos = default; return false;
    }

    static List<NetMsg.PatchPanelRec> CollectFromHost()
    {
        var list = new List<NetMsg.PatchPanelRec>();
        var found = Object.FindObjectsOfType<Il2Cpp.PatchPanel>();
        if (found == null) return list;
        for (int i = 0; i < found.Length; i++)
        {
            var p = found[i];
            if (p == null || string.IsNullOrEmpty(p.patchPanelId)) continue;
            var t = p.transform;
            list.Add(new NetMsg.PatchPanelRec(
                p.patchPanelId,
                t.position.x, t.position.y, t.position.z,
                t.eulerAngles.y,
                p.patchPanelType
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
        var bytes = NetMsg.WritePatchPanelSnapshot(recs);
        Transport.Broadcast(Transport.ChEvent, bytes);
        Log.Msg($"-> snapshot ({recs.Count} patch panels, {bytes.Length} bytes)");
    }

    public static void SendTo(CSteamID target)
    {
        if (!Authority.IsAuthoritative) return;
        var recs = CollectFromHost();
        var bytes = NetMsg.WritePatchPanelSnapshot(recs);
        Transport.SendTo(target, Transport.ChEvent, bytes);
        Log.Msg($"-> snapshot ({recs.Count} patch panels) to {SteamFriends.GetFriendPersonaName(target)}");
    }

    public static void OnIncoming(CSteamID from, byte channel, byte[] data)
    {
        if (channel != Transport.ChEvent) return;
        if (data.Length < 1 || data[0] != NetMsg.MsgPatchPanelSnapshot) return;
        if (!Authority.IsClient) return;
        if (!NetMsg.TryReadPatchPanelSnapshot(data, out var recs)) return;

        var present = new HashSet<string>(recs.Length);
        foreach (var r in recs)
        {
            present.Add(r.PanelId);
            if (!_ghosts.TryGetValue(r.PanelId, out var g))
            {
                g = SpawnGhost(r);
                _ghosts[r.PanelId] = g;
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

    static Ghost SpawnGhost(NetMsg.PatchPanelRec r)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"DCMP_PatchPanelGhost_{r.PanelId}";
        // Slimmer than switches; patch panels are passive 1U strips.
        go.transform.localScale = new Vector3(0.5f, 0.025f, 0.55f);

        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        var rend = go.GetComponent<Renderer>();
        if (rend != null && rend.material != null)
        {
            // Yellow-orange hue band (0.10–0.20) — distinct from servers
            // (full hue) and switches (0.30–0.50 green).
            float hue = 0.10f + (((uint)r.PanelId.GetHashCode() & 0xFFu) / 255f) * 0.10f;
            var c = Color.HSVToRGB(hue, 0.6f, 0.9f);
            rend.material.color = c;
            rend.material.SetColor("_EmissionColor", c * 0.8f);
            rend.material.EnableKeyword("_EMISSION");
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Object.DontDestroyOnLoad(go);
        Log.Msg($"Spawned ghost {r.PanelId}  type={r.PanelType}");
        return new Ghost { Go = go, Rec = r };
    }

    static void ApplyTo(Ghost g, NetMsg.PatchPanelRec r)
    {
        g.Go.transform.position = new Vector3(r.X, r.Y, r.Z);
        g.Go.transform.rotation = Quaternion.Euler(0f, r.Yaw, 0f);
        g.Rec = r;
    }

    static void Despawn(string id)
    {
        if (!_ghosts.TryGetValue(id, out var g)) return;
        if (g.Go != null) Object.Destroy(g.Go);
        _ghosts.Remove(id);
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
