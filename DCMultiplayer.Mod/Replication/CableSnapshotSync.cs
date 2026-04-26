using System.Collections.Generic;
using DCMultiplayer.Networking;
using Il2CppSteamworks;
using MelonLoader;
using UnityEngine;

namespace DCMultiplayer.Replication;

// Replicates the host's cable graph (NetworkMap.cableConnections is a
// Dictionary<int, ValueTuple<string,string>> mapping cableId to its two
// device-id endpoints). The wire format only carries identity — endpoint
// world positions are looked up at apply time from the existing server /
// switch ghost tables, so cables only render where both sides are visible.
internal static class CableSnapshotSync
{
    public static MelonLogger.Instance Log = new("DC_MP_Cab");

    class Ghost
    {
        public GameObject Go;
        public LineRenderer Line;
        public NetMsg.CableRec Rec;
    }

    static readonly Dictionary<int, Ghost> _ghosts = new();
    public static int Count => _ghosts.Count;

    static List<NetMsg.CableRec> CollectFromHost()
    {
        var list = new List<NetMsg.CableRec>();
        var nm = Il2Cpp.NetworkMap.instance;
        if (nm == null) return list;
        var dict = nm.cableConnections;
        if (dict == null) return list;
        foreach (var kv in dict)
        {
            int id = kv.Key;
            var tup = kv.Value;
            list.Add(new NetMsg.CableRec(id, tup.Item1 ?? "", tup.Item2 ?? ""));
        }
        return list;
    }

    public static void BroadcastSnapshot()
    {
        if (!Authority.IsAuthoritative) return;
        if (!SteamLobby.IsInLobby) return;
        var recs = CollectFromHost();
        var bytes = NetMsg.WriteCableSnapshot(recs);
        Transport.Broadcast(Transport.ChEvent, bytes);
        Log.Msg($"-> snapshot ({recs.Count} cables, {bytes.Length} bytes)");
    }

    public static void SendTo(CSteamID target)
    {
        if (!Authority.IsAuthoritative) return;
        var recs = CollectFromHost();
        var bytes = NetMsg.WriteCableSnapshot(recs);
        Transport.SendTo(target, Transport.ChEvent, bytes);
        Log.Msg($"-> snapshot ({recs.Count} cables) to {SteamFriends.GetFriendPersonaName(target)}");
    }

    public static void OnIncoming(CSteamID from, byte channel, byte[] data)
    {
        if (channel != Transport.ChEvent) return;
        if (data.Length < 1 || data[0] != NetMsg.MsgCableSnapshot) return;
        if (!Authority.IsClient) return;
        if (!NetMsg.TryReadCableSnapshot(data, out var recs)) return;

        var present = new HashSet<int>(recs.Length);
        foreach (var r in recs)
        {
            present.Add(r.CableId);
            if (!_ghosts.TryGetValue(r.CableId, out var g))
            {
                g = SpawnGhost(r);
                _ghosts[r.CableId] = g;
            }
            else
            {
                g.Rec = r;
            }
        }

        if (_ghosts.Count > recs.Length)
        {
            List<int> toKill = null;
            foreach (var key in _ghosts.Keys)
                if (!present.Contains(key)) (toKill ??= new()).Add(key);
            if (toKill != null)
                foreach (var key in toKill) Despawn(key);
        }

        Log.Msg($"<- snapshot applied ({recs.Length} ghosts)");
    }

    static Ghost SpawnGhost(NetMsg.CableRec r)
    {
        var go = new GameObject($"DCMP_CableGhost_{r.CableId}");
        var line = go.AddComponent<LineRenderer>();
        // Two-vertex straight line for now — no Bezier sag yet. Width
        // tuned to be visible without dominating the rack.
        line.positionCount = 2;
        line.startWidth = 0.04f;
        line.endWidth = 0.04f;
        line.useWorldSpace = true;
        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // Cable colour cycles by id so each cable is distinguishable.
        var c = Color.HSVToRGB(((uint)r.CableId & 0xFFu) / 255f, 0.7f, 0.95f);
        line.startColor = c;
        line.endColor = c;
        Object.DontDestroyOnLoad(go);
        return new Ghost { Go = go, Line = line, Rec = r };
    }

    static void Despawn(int id)
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
        {
            if (_ghosts.Count > 0) ClearAll();
            return;
        }

        // Keep each cable's endpoints synced with the latest known ghost
        // positions every frame. Cheap (linear in cables, ~10s usually)
        // and means cables snap to their devices without us needing
        // delta messages on every server/switch move.
        foreach (var kv in _ghosts)
        {
            var g = kv.Value;
            if (g.Go == null || g.Line == null) continue;
            if (TryResolve(g.Rec.EndpointA, out var a) && TryResolve(g.Rec.EndpointB, out var b))
            {
                g.Line.SetPosition(0, a);
                g.Line.SetPosition(1, b);
                g.Go.SetActive(true);
            }
            else
            {
                // Hide cables until both endpoints are visible — avoids
                // dangling lines pointing at (0,0,0).
                g.Go.SetActive(false);
            }
        }
    }

    static bool TryResolve(string id, out Vector3 pos)
    {
        if (string.IsNullOrEmpty(id)) { pos = default; return false; }
        if (ServerSnapshotSync.TryGetPosition(id, out pos)) return true;
        if (SwitchSnapshotSync.TryGetPosition(id, out pos)) return true;
        // Patch panels and customer bases come later — for now, unresolved.
        pos = default;
        return false;
    }
}
