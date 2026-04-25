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
}
