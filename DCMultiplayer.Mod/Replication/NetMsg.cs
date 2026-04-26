using System;
using System.Buffers.Binary;

namespace DCMultiplayer.Replication;

internal static class NetMsg
{
    // Channel-0 control messages reserved for plain text (ping/pong) for now.
    // Channel-1 (state) messages start with this byte.
    public const byte MsgPlayerPose = 0x10;
    public const byte MsgEconomyTick = 0x20;
    public const byte MsgEventText = 0x30;
    public const byte MsgCustomerPool = 0x40;
    public const byte MsgServerSnapshot = 0x50;
    public const byte MsgBaseAssignments = 0x60;
    public const byte MsgSwitchSnapshot = 0x70;
    public const byte MsgCableSnapshot = 0x80;

    // Layout for MsgPlayerPose (21 bytes total):
    //   [0]      byte    type = 0x10
    //   [1..12]  float32 posX, posY, posZ          (world space)
    //   [13..16] float32 yaw  (body rotation Y, deg)
    //   [17..20] float32 pitch (camera pitch, deg)
    public const int PlayerPoseSize = 21;

    public static int WritePlayerPose(Span<byte> buf, float x, float y, float z, float yaw, float pitch)
    {
        buf[0] = MsgPlayerPose;
        BinaryPrimitives.WriteSingleLittleEndian(buf.Slice(1, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(buf.Slice(5, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(buf.Slice(9, 4), z);
        BinaryPrimitives.WriteSingleLittleEndian(buf.Slice(13, 4), yaw);
        BinaryPrimitives.WriteSingleLittleEndian(buf.Slice(17, 4), pitch);
        return PlayerPoseSize;
    }

    public static bool TryReadPlayerPose(ReadOnlySpan<byte> buf, out float x, out float y, out float z, out float yaw, out float pitch)
    {
        x = y = z = yaw = pitch = 0f;
        if (buf.Length < PlayerPoseSize || buf[0] != MsgPlayerPose) return false;
        x = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(1, 4));
        y = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(5, 4));
        z = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(9, 4));
        yaw = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(13, 4));
        pitch = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(17, 4));
        return true;
    }

    // Layout for MsgEconomyTick (13 bytes total):
    //   [0]      byte    type = 0x20
    //   [1..4]   float32 money
    //   [5..8]   float32 xp
    //   [9..12]  float32 reputation
    public const int EconomyTickSize = 13;

    public static int WriteEconomyTick(Span<byte> buf, float money, float xp, float reputation)
    {
        buf[0] = MsgEconomyTick;
        BinaryPrimitives.WriteSingleLittleEndian(buf.Slice(1, 4), money);
        BinaryPrimitives.WriteSingleLittleEndian(buf.Slice(5, 4), xp);
        BinaryPrimitives.WriteSingleLittleEndian(buf.Slice(9, 4), reputation);
        return EconomyTickSize;
    }

    public static bool TryReadEconomyTick(ReadOnlySpan<byte> buf, out float money, out float xp, out float reputation)
    {
        money = xp = reputation = 0f;
        if (buf.Length < EconomyTickSize || buf[0] != MsgEconomyTick) return false;
        money = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(1, 4));
        xp = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(5, 4));
        reputation = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(9, 4));
        return true;
    }

    // Layout for MsgEventText (1 + 2 + N bytes):
    //   [0]      byte    type = 0x30
    //   [1..2]   uint16  text length (LE)
    //   [3..]    UTF-8 bytes
    public static byte[] WriteEventText(string text)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(text);
        if (utf8.Length > ushort.MaxValue) throw new System.ArgumentException("text too long", nameof(text));
        var buf = new byte[3 + utf8.Length];
        buf[0] = MsgEventText;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(1, 2), (ushort)utf8.Length);
        utf8.CopyTo(buf, 3);
        return buf;
    }

    public static bool TryReadEventText(ReadOnlySpan<byte> buf, out string text)
    {
        text = null;
        if (buf.Length < 3 || buf[0] != MsgEventText) return false;
        ushort len = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(1, 2));
        if (buf.Length < 3 + len) return false;
        text = System.Text.Encoding.UTF8.GetString(buf.Slice(3, len));
        return true;
    }

    // Layout for MsgCustomerPool:
    //   [0]      byte    type = 0x40
    //   [1..2]   uint16  count of indices
    //   [3..]    int32 * count (LE) — values from MainGameManager.availableCustomerIndices
    public static byte[] WriteCustomerPool(System.Collections.Generic.IList<int> indices)
    {
        if (indices.Count > ushort.MaxValue) throw new System.ArgumentException("too many indices", nameof(indices));
        var buf = new byte[3 + indices.Count * 4];
        buf[0] = MsgCustomerPool;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(1, 2), (ushort)indices.Count);
        for (int i = 0; i < indices.Count; i++)
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(3 + i * 4, 4), indices[i]);
        return buf;
    }

    public static bool TryReadCustomerPool(ReadOnlySpan<byte> buf, out int[] indices)
    {
        indices = null;
        if (buf.Length < 3 || buf[0] != MsgCustomerPool) return false;
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(1, 2));
        if (buf.Length < 3 + count * 4) return false;
        indices = new int[count];
        for (int i = 0; i < count; i++)
            indices[i] = BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(3 + i * 4, 4));
        return true;
    }

    // Server snapshot record — read-only state of one host server, rendered
    // by clients as a placeholder ghost. We don't ship velocity/animation
    // because servers don't move; this is a position+identity packet.
    public readonly struct ServerRec
    {
        public readonly string ServerId;
        public readonly float X, Y, Z;
        public readonly float Yaw;
        public readonly int ServerType;
        public readonly bool IsOn;
        public readonly bool IsBroken;
        public readonly int CustomerId;
        public readonly int AppId;
        public readonly string Ip;
        public ServerRec(string id, float x, float y, float z, float yaw, int type, bool on, bool broken, int cust, int app, string ip)
        { ServerId = id; X = x; Y = y; Z = z; Yaw = yaw; ServerType = type; IsOn = on; IsBroken = broken; CustomerId = cust; AppId = app; Ip = ip; }
    }

    // Layout for MsgServerSnapshot:
    //   [0]      byte    type = 0x50
    //   [1..2]   uint16  record count
    //   [3..]    per record:
    //              byte    idLen
    //              bytes   id (UTF-8)
    //              float32 x, y, z, yaw                 (16 B)
    //              int32   serverType                   (4 B)
    //              byte    flags (bit0=isOn, bit1=isBroken)
    //              int32   customerId                   (4 B)
    //              int32   appId                        (4 B)
    //              byte    ipLen
    //              bytes   ip (UTF-8)
    public static byte[] WriteServerSnapshot(System.Collections.Generic.IList<ServerRec> recs)
    {
        // Two passes: measure, then write — avoids growth juggling.
        int total = 3;
        var idBytes = new byte[recs.Count][];
        var ipBytes = new byte[recs.Count][];
        for (int i = 0; i < recs.Count; i++)
        {
            idBytes[i] = System.Text.Encoding.UTF8.GetBytes(recs[i].ServerId ?? "");
            ipBytes[i] = System.Text.Encoding.UTF8.GetBytes(recs[i].Ip ?? "");
            if (idBytes[i].Length > 255) throw new System.ArgumentException("serverId too long");
            if (ipBytes[i].Length > 255) throw new System.ArgumentException("ip too long");
            total += 1 + idBytes[i].Length + 16 + 4 + 1 + 4 + 4 + 1 + ipBytes[i].Length;
        }
        var buf = new byte[total];
        buf[0] = MsgServerSnapshot;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(1, 2), (ushort)recs.Count);
        int pos = 3;
        for (int i = 0; i < recs.Count; i++)
        {
            var r = recs[i];
            buf[pos++] = (byte)idBytes[i].Length;
            idBytes[i].CopyTo(buf, pos); pos += idBytes[i].Length;
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(pos, 4), r.X);  pos += 4;
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(pos, 4), r.Y);  pos += 4;
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(pos, 4), r.Z);  pos += 4;
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(pos, 4), r.Yaw); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos, 4), r.ServerType); pos += 4;
            byte flags = 0; if (r.IsOn) flags |= 1; if (r.IsBroken) flags |= 2;
            buf[pos++] = flags;
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos, 4), r.CustomerId); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos, 4), r.AppId); pos += 4;
            buf[pos++] = (byte)ipBytes[i].Length;
            ipBytes[i].CopyTo(buf, pos); pos += ipBytes[i].Length;
        }
        return buf;
    }

    // Layout for MsgBaseAssignments:
    //   [0]      byte    type = 0x60
    //   [1..2]   uint16  pair count
    //   [3..]    for each pair: int32 baseId, int32 customerId  (8 B each;
    //            customerId = -1 means base currently has no customer)
    public static byte[] WriteBaseAssignments(System.Collections.Generic.IList<(int baseId, int customerId)> pairs)
    {
        if (pairs.Count > ushort.MaxValue) throw new System.ArgumentException("too many pairs", nameof(pairs));
        var buf = new byte[3 + pairs.Count * 8];
        buf[0] = MsgBaseAssignments;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(1, 2), (ushort)pairs.Count);
        int pos = 3;
        for (int i = 0; i < pairs.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos, 4), pairs[i].baseId);     pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos, 4), pairs[i].customerId); pos += 4;
        }
        return buf;
    }

    public static bool TryReadBaseAssignments(ReadOnlySpan<byte> buf, out (int baseId, int customerId)[] pairs)
    {
        pairs = null;
        if (buf.Length < 3 || buf[0] != MsgBaseAssignments) return false;
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(1, 2));
        if (buf.Length < 3 + count * 8) return false;
        var arr = new (int, int)[count];
        for (int i = 0; i < count; i++)
        {
            int b = BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(3 + i * 8, 4));
            int c = BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(7 + i * 8, 4));
            arr[i] = (b, c);
        }
        pairs = arr;
        return true;
    }

    // Switch snapshot — same shape as ServerRec but trimmed: no IP / customer
    // / app fields (switches don't have them). Kept as a separate record/
    // message rather than overloading ServerRec so adding switch-specific
    // fields later (port count, vlan filter summary, ...) doesn't bleed
    // into the server format.
    public readonly struct SwitchRec
    {
        public readonly string SwitchId;
        public readonly float X, Y, Z;
        public readonly float Yaw;
        public readonly int SwitchType;
        public readonly bool IsOn;
        public readonly bool IsBroken;
        public SwitchRec(string id, float x, float y, float z, float yaw, int type, bool on, bool broken)
        { SwitchId = id; X = x; Y = y; Z = z; Yaw = yaw; SwitchType = type; IsOn = on; IsBroken = broken; }
    }

    // Layout for MsgSwitchSnapshot:
    //   [0]      byte    type = 0x70
    //   [1..2]   uint16  record count
    //   [3..]    per record:
    //              byte    idLen
    //              bytes   id (UTF-8)
    //              float32 x, y, z, yaw     (16 B)
    //              int32   switchType       (4 B)
    //              byte    flags (bit0=isOn, bit1=isBroken)
    public static byte[] WriteSwitchSnapshot(System.Collections.Generic.IList<SwitchRec> recs)
    {
        int total = 3;
        var idBytes = new byte[recs.Count][];
        for (int i = 0; i < recs.Count; i++)
        {
            idBytes[i] = System.Text.Encoding.UTF8.GetBytes(recs[i].SwitchId ?? "");
            if (idBytes[i].Length > 255) throw new System.ArgumentException("switchId too long");
            total += 1 + idBytes[i].Length + 16 + 4 + 1;
        }
        var buf = new byte[total];
        buf[0] = MsgSwitchSnapshot;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(1, 2), (ushort)recs.Count);
        int pos = 3;
        for (int i = 0; i < recs.Count; i++)
        {
            var r = recs[i];
            buf[pos++] = (byte)idBytes[i].Length;
            idBytes[i].CopyTo(buf, pos); pos += idBytes[i].Length;
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(pos, 4), r.X); pos += 4;
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(pos, 4), r.Y); pos += 4;
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(pos, 4), r.Z); pos += 4;
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(pos, 4), r.Yaw); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos, 4), r.SwitchType); pos += 4;
            byte flags = 0; if (r.IsOn) flags |= 1; if (r.IsBroken) flags |= 2;
            buf[pos++] = flags;
        }
        return buf;
    }

    public static bool TryReadSwitchSnapshot(ReadOnlySpan<byte> buf, out SwitchRec[] recs)
    {
        recs = null;
        if (buf.Length < 3 || buf[0] != MsgSwitchSnapshot) return false;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(1, 2));
        var arr = new SwitchRec[count];
        int pos = 3;
        for (int i = 0; i < count; i++)
        {
            if (pos >= buf.Length) return false;
            int idLen = buf[pos++];
            if (pos + idLen + 16 + 4 + 1 > buf.Length) return false;
            string id = System.Text.Encoding.UTF8.GetString(buf.Slice(pos, idLen)); pos += idLen;
            float x = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(pos, 4)); pos += 4;
            float y = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(pos, 4)); pos += 4;
            float z = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(pos, 4)); pos += 4;
            float yaw = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(pos, 4)); pos += 4;
            int type = BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(pos, 4)); pos += 4;
            byte flags = buf[pos++];
            arr[i] = new SwitchRec(id, x, y, z, yaw, type, (flags & 1) != 0, (flags & 2) != 0);
        }
        recs = arr;
        return true;
    }

    // Cable snapshot record — identity-only; positions come from looking
    // up the endpoints in the already-replicated server/switch/patch
    // panel ghost tables on the client.
    public readonly struct CableRec
    {
        public readonly int CableId;
        public readonly string EndpointA;
        public readonly string EndpointB;
        public CableRec(int id, string a, string b) { CableId = id; EndpointA = a; EndpointB = b; }
    }

    // Layout for MsgCableSnapshot:
    //   [0]      byte    type = 0x80
    //   [1..2]   uint16  record count
    //   [3..]    per record:
    //              int32  cableId
    //              byte   aIdLen
    //              bytes  aId (UTF-8)
    //              byte   bIdLen
    //              bytes  bId (UTF-8)
    public static byte[] WriteCableSnapshot(System.Collections.Generic.IList<CableRec> recs)
    {
        int total = 3;
        var aBytes = new byte[recs.Count][];
        var bBytes = new byte[recs.Count][];
        for (int i = 0; i < recs.Count; i++)
        {
            aBytes[i] = System.Text.Encoding.UTF8.GetBytes(recs[i].EndpointA ?? "");
            bBytes[i] = System.Text.Encoding.UTF8.GetBytes(recs[i].EndpointB ?? "");
            if (aBytes[i].Length > 255) throw new System.ArgumentException("endpointA too long");
            if (bBytes[i].Length > 255) throw new System.ArgumentException("endpointB too long");
            total += 4 + 1 + aBytes[i].Length + 1 + bBytes[i].Length;
        }
        var buf = new byte[total];
        buf[0] = MsgCableSnapshot;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(1, 2), (ushort)recs.Count);
        int pos = 3;
        for (int i = 0; i < recs.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos, 4), recs[i].CableId); pos += 4;
            buf[pos++] = (byte)aBytes[i].Length;
            aBytes[i].CopyTo(buf, pos); pos += aBytes[i].Length;
            buf[pos++] = (byte)bBytes[i].Length;
            bBytes[i].CopyTo(buf, pos); pos += bBytes[i].Length;
        }
        return buf;
    }

    public static bool TryReadCableSnapshot(ReadOnlySpan<byte> buf, out CableRec[] recs)
    {
        recs = null;
        if (buf.Length < 3 || buf[0] != MsgCableSnapshot) return false;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(1, 2));
        var arr = new CableRec[count];
        int pos = 3;
        for (int i = 0; i < count; i++)
        {
            if (pos + 4 + 1 > buf.Length) return false;
            int id = BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(pos, 4)); pos += 4;
            int aLen = buf[pos++];
            if (pos + aLen + 1 > buf.Length) return false;
            string a = System.Text.Encoding.UTF8.GetString(buf.Slice(pos, aLen)); pos += aLen;
            int bLen = buf[pos++];
            if (pos + bLen > buf.Length) return false;
            string b = System.Text.Encoding.UTF8.GetString(buf.Slice(pos, bLen)); pos += bLen;
            arr[i] = new CableRec(id, a, b);
        }
        recs = arr;
        return true;
    }

    public static bool TryReadServerSnapshot(ReadOnlySpan<byte> buf, out ServerRec[] recs)
    {
        recs = null;
        if (buf.Length < 3 || buf[0] != MsgServerSnapshot) return false;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(1, 2));
        var arr = new ServerRec[count];
        int pos = 3;
        for (int i = 0; i < count; i++)
        {
            if (pos >= buf.Length) return false;
            int idLen = buf[pos++];
            if (pos + idLen + 16 + 4 + 1 + 4 + 4 + 1 > buf.Length) return false;
            string id = System.Text.Encoding.UTF8.GetString(buf.Slice(pos, idLen)); pos += idLen;
            float x = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(pos, 4));   pos += 4;
            float y = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(pos, 4));   pos += 4;
            float z = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(pos, 4));   pos += 4;
            float yaw = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(pos, 4)); pos += 4;
            int type = BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(pos, 4));   pos += 4;
            byte flags = buf[pos++];
            int cust = BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(pos, 4));   pos += 4;
            int app = BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(pos, 4));    pos += 4;
            int ipLen = buf[pos++];
            if (pos + ipLen > buf.Length) return false;
            string ip = System.Text.Encoding.UTF8.GetString(buf.Slice(pos, ipLen)); pos += ipLen;
            arr[i] = new ServerRec(id, x, y, z, yaw, type, (flags & 1) != 0, (flags & 2) != 0, cust, app, ip);
        }
        recs = arr;
        return true;
    }
}
