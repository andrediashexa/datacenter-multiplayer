using System;
using System.Collections.Generic;
using DCMultiplayer.Networking;
using Il2CppSteamworks;
using UnityEngine;

namespace DCMultiplayer.Replication;

// In-memory ring buffer of cross-peer text events. Anyone (host or client) can
// emit; everyone sees the same feed because the sender always also adds
// locally before broadcasting. Used for "GARRINCHA powered server X" style
// notifications — observability of remote actions while we don't yet replicate
// the underlying entities.
internal static class EventLog
{
    public readonly struct Entry
    {
        public readonly float Time;
        public readonly string Source;
        public readonly string Text;
        public Entry(float t, string src, string txt) { Time = t; Source = src; Text = txt; }
    }

    const int Capacity = 8;
    static readonly Entry[] _buf = new Entry[Capacity];
    static int _head;          // next write slot
    static int _count;

    public static int Count => _count;

    public static IEnumerable<Entry> Recent()
    {
        // newest first
        for (int i = 0; i < _count; i++)
        {
            int idx = (_head - 1 - i + Capacity) % Capacity;
            yield return _buf[idx];
        }
    }

    /// <summary>Local-only push (no broadcast). Used by the receiver path.</summary>
    public static void Push(string source, string text)
    {
        _buf[_head] = new Entry(Time.realtimeSinceStartup, source, text);
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    /// <summary>Push locally AND broadcast to all peers in the lobby.</summary>
    public static void Emit(string text)
    {
        string me = SteamFriends.GetPersonaName();
        Push(me, text);
        if (SteamLobby.IsInLobby)
        {
            var bytes = NetMsg.WriteEventText(text);
            Transport.Broadcast(Transport.ChEvent, bytes);
        }
    }

    public static void OnIncoming(CSteamID from, byte channel, byte[] data)
    {
        if (channel != Transport.ChEvent) return;
        if (data.Length < 1 || data[0] != NetMsg.MsgEventText) return;
        if (!NetMsg.TryReadEventText(data, out string text)) return;
        Push(SteamFriends.GetFriendPersonaName(from), text);
    }
}
