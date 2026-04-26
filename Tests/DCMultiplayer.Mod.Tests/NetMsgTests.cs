using System;
using System.Collections.Generic;
using DCMultiplayer.Replication;
using Xunit;

namespace DCMultiplayer.Mod.Tests;

// Round-trip tests for every wire format defined in NetMsg. Each kind of
// message must:
//  1. survive Write -> TryRead with all fields preserved
//  2. reject buffers with the wrong type byte
//  3. reject truncated buffers
//
// Floats are compared with a generous epsilon — IEEE 754 round-trip via
// little-endian bytes preserves bit patterns, so equality should be exact,
// but tests stay forgiving of compiler reorderings.

public class PlayerPoseTests
{
    [Theory]
    [InlineData(0f, 0f, 0f, 0f, 0f)]
    [InlineData(1.5f, -2.25f, 100.125f, 270f, -45f)]
    [InlineData(float.MaxValue, float.MinValue, 0f, 359.9f, -89.9f)]
    public void RoundTrips(float x, float y, float z, float yaw, float pitch)
    {
        Span<byte> buf = stackalloc byte[NetMsg.PlayerPoseSize];
        int n = NetMsg.WritePlayerPose(buf, x, y, z, yaw, pitch);
        Assert.Equal(NetMsg.PlayerPoseSize, n);

        Assert.True(NetMsg.TryReadPlayerPose(buf, out var rx, out var ry, out var rz, out var ryaw, out var rpitch));
        Assert.Equal(x, rx);
        Assert.Equal(y, ry);
        Assert.Equal(z, rz);
        Assert.Equal(yaw, ryaw);
        Assert.Equal(pitch, rpitch);
    }

    [Fact]
    public void RejectsWrongType()
    {
        Span<byte> buf = stackalloc byte[NetMsg.PlayerPoseSize];
        NetMsg.WritePlayerPose(buf, 1, 2, 3, 4, 5);
        buf[0] = 0xFF;
        Assert.False(NetMsg.TryReadPlayerPose(buf, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void RejectsShortBuffer()
    {
        Span<byte> buf = stackalloc byte[NetMsg.PlayerPoseSize - 1];
        buf[0] = NetMsg.MsgPlayerPose;
        Assert.False(NetMsg.TryReadPlayerPose(buf, out _, out _, out _, out _, out _));
    }
}

public class EconomyTickTests
{
    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(123456.78f, 9876.5f, 12.0f)]
    [InlineData(-1f, -1f, -1f)]
    public void RoundTrips(float money, float xp, float rep)
    {
        Span<byte> buf = stackalloc byte[NetMsg.EconomyTickSize];
        int n = NetMsg.WriteEconomyTick(buf, money, xp, rep);
        Assert.Equal(NetMsg.EconomyTickSize, n);

        Assert.True(NetMsg.TryReadEconomyTick(buf, out var rm, out var rxp, out var rr));
        Assert.Equal(money, rm);
        Assert.Equal(xp, rxp);
        Assert.Equal(rep, rr);
    }

    [Fact]
    public void RejectsWrongType()
    {
        Span<byte> buf = stackalloc byte[NetMsg.EconomyTickSize];
        NetMsg.WriteEconomyTick(buf, 1, 2, 3);
        buf[0] = 0;
        Assert.False(NetMsg.TryReadEconomyTick(buf, out _, out _, out _));
    }
}

public class EventTextTests
{
    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("multi-line\nlog\nentry")]
    [InlineData("acentuação úý 中文 🎮")]
    public void RoundTrips(string text)
    {
        var buf = NetMsg.WriteEventText(text);
        Assert.True(NetMsg.TryReadEventText(buf, out var rt));
        Assert.Equal(text, rt);
    }

    [Fact]
    public void RejectsTruncated()
    {
        var buf = NetMsg.WriteEventText("payload here");
        // Drop the last 5 bytes — declared length now overruns the buffer
        var trunc = new byte[buf.Length - 5];
        Buffer.BlockCopy(buf, 0, trunc, 0, trunc.Length);
        Assert.False(NetMsg.TryReadEventText(trunc, out _));
    }

    [Fact]
    public void RejectsOversizeOnWrite()
    {
        // Build a string that overflows ushort — should throw on write.
        var huge = new string('x', ushort.MaxValue + 1);
        Assert.Throws<ArgumentException>(() => NetMsg.WriteEventText(huge));
    }
}

public class CustomerPoolTests
{
    [Fact]
    public void EmptyRoundTrips()
    {
        var bytes = NetMsg.WriteCustomerPool(new List<int>());
        Assert.True(NetMsg.TryReadCustomerPool(bytes, out var rx));
        Assert.Empty(rx);
    }

    [Theory]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 1, 2, 3, 4, 5 })]
    [InlineData(new[] { -1, int.MaxValue, int.MinValue, 0 })]
    public void RoundTrips(int[] indices)
    {
        var bytes = NetMsg.WriteCustomerPool(indices);
        Assert.True(NetMsg.TryReadCustomerPool(bytes, out var rx));
        Assert.Equal(indices, rx);
    }

    [Fact]
    public void LargePoolRoundTrips()
    {
        var indices = new int[1000];
        for (int i = 0; i < indices.Length; i++) indices[i] = i * 7;
        var bytes = NetMsg.WriteCustomerPool(indices);
        Assert.True(NetMsg.TryReadCustomerPool(bytes, out var rx));
        Assert.Equal(indices, rx);
    }

    [Fact]
    public void RejectsWrongType()
    {
        var bytes = NetMsg.WriteCustomerPool(new[] { 1, 2, 3 });
        bytes[0] = 0xAA;
        Assert.False(NetMsg.TryReadCustomerPool(bytes, out _));
    }
}

public class ServerSnapshotTests
{
    static NetMsg.ServerRec Rec(string id, float x = 1f, bool on = true, bool broken = false, int cust = 5, int app = 7, string ip = "10.0.0.1")
        => new NetMsg.ServerRec(id, x, 2.5f, 3.75f, 90f, 4, on, broken, cust, app, ip);

    [Fact]
    public void EmptyRoundTrips()
    {
        var bytes = NetMsg.WriteServerSnapshot(new List<NetMsg.ServerRec>());
        Assert.True(NetMsg.TryReadServerSnapshot(bytes, out var rx));
        Assert.Empty(rx);
    }

    [Fact]
    public void SingleRecordPreservesAllFields()
    {
        var src = Rec("server-abc", x: 12.34f, on: true, broken: false, cust: 42, app: 7, ip: "192.168.1.5");
        var bytes = NetMsg.WriteServerSnapshot(new[] { src });
        Assert.True(NetMsg.TryReadServerSnapshot(bytes, out var rx));
        var got = Assert.Single(rx);
        Assert.Equal(src.ServerId, got.ServerId);
        Assert.Equal(src.X, got.X);
        Assert.Equal(src.Y, got.Y);
        Assert.Equal(src.Z, got.Z);
        Assert.Equal(src.Yaw, got.Yaw);
        Assert.Equal(src.ServerType, got.ServerType);
        Assert.Equal(src.IsOn, got.IsOn);
        Assert.Equal(src.IsBroken, got.IsBroken);
        Assert.Equal(src.CustomerId, got.CustomerId);
        Assert.Equal(src.AppId, got.AppId);
        Assert.Equal(src.Ip, got.Ip);
    }

    [Fact]
    public void FlagBitsIndependent()
    {
        // Make sure isOn / isBroken don't bleed into each other.
        var combos = new[]
        {
            Rec("s1", on: false, broken: false),
            Rec("s2", on: true,  broken: false),
            Rec("s3", on: false, broken: true),
            Rec("s4", on: true,  broken: true),
        };
        var bytes = NetMsg.WriteServerSnapshot(combos);
        Assert.True(NetMsg.TryReadServerSnapshot(bytes, out var rx));
        Assert.Equal(4, rx.Length);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(combos[i].IsOn, rx[i].IsOn);
            Assert.Equal(combos[i].IsBroken, rx[i].IsBroken);
        }
    }

    [Fact]
    public void RejectsTruncated()
    {
        var bytes = NetMsg.WriteServerSnapshot(new[] { Rec("a"), Rec("b") });
        var trunc = new byte[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, trunc, 0, trunc.Length);
        Assert.False(NetMsg.TryReadServerSnapshot(trunc, out _));
    }

    [Fact]
    public void RejectsWrongType()
    {
        var bytes = NetMsg.WriteServerSnapshot(new[] { Rec("a") });
        bytes[0] = 0;
        Assert.False(NetMsg.TryReadServerSnapshot(bytes, out _));
    }

    [Fact]
    public void HandlesEmptyStrings()
    {
        var src = new NetMsg.ServerRec("", 0, 0, 0, 0, 0, false, false, 0, 0, "");
        var bytes = NetMsg.WriteServerSnapshot(new[] { src });
        Assert.True(NetMsg.TryReadServerSnapshot(bytes, out var rx));
        Assert.Equal("", rx[0].ServerId);
        Assert.Equal("", rx[0].Ip);
    }
}

public class BaseAssignmentsTests
{
    [Fact]
    public void EmptyRoundTrips()
    {
        var bytes = NetMsg.WriteBaseAssignments(new List<(int, int)>());
        Assert.True(NetMsg.TryReadBaseAssignments(bytes, out var rx));
        Assert.Empty(rx);
    }

    [Fact]
    public void RoundTripsAllPairs()
    {
        var pairs = new List<(int, int)>
        {
            (0, -1),       // unassigned
            (1, 42),
            (2, int.MaxValue),
            (3, 0),
            (1000, -1),
        };
        var bytes = NetMsg.WriteBaseAssignments(pairs);
        Assert.True(NetMsg.TryReadBaseAssignments(bytes, out var rx));
        Assert.Equal(pairs.Count, rx.Length);
        for (int i = 0; i < pairs.Count; i++)
        {
            Assert.Equal(pairs[i].Item1, rx[i].baseId);
            Assert.Equal(pairs[i].Item2, rx[i].customerId);
        }
    }

    [Fact]
    public void RejectsTruncated()
    {
        var bytes = NetMsg.WriteBaseAssignments(new List<(int, int)> { (1, 2), (3, 4) });
        var trunc = new byte[bytes.Length - 4];
        Buffer.BlockCopy(bytes, 0, trunc, 0, trunc.Length);
        Assert.False(NetMsg.TryReadBaseAssignments(trunc, out _));
    }

    [Fact]
    public void RejectsWrongType()
    {
        var bytes = NetMsg.WriteBaseAssignments(new List<(int, int)> { (1, 2) });
        bytes[0] = 0;
        Assert.False(NetMsg.TryReadBaseAssignments(bytes, out _));
    }
}

public class SwitchSnapshotTests
{
    static NetMsg.SwitchRec Rec(string id, bool on = true, bool broken = false)
        => new NetMsg.SwitchRec(id, 1f, 2f, 3f, 90f, 5, on, broken);

    [Fact]
    public void EmptyRoundTrips()
    {
        var bytes = NetMsg.WriteSwitchSnapshot(new List<NetMsg.SwitchRec>());
        Assert.True(NetMsg.TryReadSwitchSnapshot(bytes, out var rx));
        Assert.Empty(rx);
    }

    [Fact]
    public void SingleRecordPreservesAllFields()
    {
        var src = new NetMsg.SwitchRec("sw-abc", 12.34f, 5.6f, -7.8f, 180f, 3, true, false);
        var bytes = NetMsg.WriteSwitchSnapshot(new[] { src });
        Assert.True(NetMsg.TryReadSwitchSnapshot(bytes, out var rx));
        var got = Assert.Single(rx);
        Assert.Equal(src.SwitchId, got.SwitchId);
        Assert.Equal(src.X, got.X);
        Assert.Equal(src.Y, got.Y);
        Assert.Equal(src.Z, got.Z);
        Assert.Equal(src.Yaw, got.Yaw);
        Assert.Equal(src.SwitchType, got.SwitchType);
        Assert.Equal(src.IsOn, got.IsOn);
        Assert.Equal(src.IsBroken, got.IsBroken);
    }

    [Fact]
    public void FlagBitsIndependent()
    {
        var combos = new[]
        {
            Rec("sw1", on: false, broken: false),
            Rec("sw2", on: true,  broken: false),
            Rec("sw3", on: false, broken: true),
            Rec("sw4", on: true,  broken: true),
        };
        var bytes = NetMsg.WriteSwitchSnapshot(combos);
        Assert.True(NetMsg.TryReadSwitchSnapshot(bytes, out var rx));
        Assert.Equal(4, rx.Length);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(combos[i].IsOn, rx[i].IsOn);
            Assert.Equal(combos[i].IsBroken, rx[i].IsBroken);
        }
    }

    [Fact]
    public void RejectsTruncated()
    {
        var bytes = NetMsg.WriteSwitchSnapshot(new[] { Rec("a"), Rec("b") });
        var trunc = new byte[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, trunc, 0, trunc.Length);
        Assert.False(NetMsg.TryReadSwitchSnapshot(trunc, out _));
    }

    [Fact]
    public void RejectsWrongType()
    {
        var bytes = NetMsg.WriteSwitchSnapshot(new[] { Rec("a") });
        bytes[0] = 0;
        Assert.False(NetMsg.TryReadSwitchSnapshot(bytes, out _));
    }
}

public class TypeByteUniquenessTest
{
    // If two messages share a type byte, OnIncoming dispatch can't tell
    // them apart and one will silently be swallowed by the wrong handler.
    [Fact]
    public void TypeBytesAreUnique()
    {
        var seen = new HashSet<byte>();
        Assert.True(seen.Add(NetMsg.MsgPlayerPose));
        Assert.True(seen.Add(NetMsg.MsgEconomyTick));
        Assert.True(seen.Add(NetMsg.MsgEventText));
        Assert.True(seen.Add(NetMsg.MsgCustomerPool));
        Assert.True(seen.Add(NetMsg.MsgServerSnapshot));
        Assert.True(seen.Add(NetMsg.MsgBaseAssignments));
        Assert.True(seen.Add(NetMsg.MsgSwitchSnapshot));
    }
}
