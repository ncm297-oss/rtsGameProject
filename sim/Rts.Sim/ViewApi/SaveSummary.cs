using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.Save;

namespace Rts.Sim.ViewApi;

/// <summary>
/// M6-V14: what the Load Game list shows of a save file, read from its header alone (the format of
/// <see cref="Simulation.TrySave"/>, docs/03 "Save format (M6-S2)"): the format version, the seed, the seats and the tick it
/// was made at. Read-only and never throws on bad bytes; it checks the shape of the header, not the version, the checksum or
/// the data hash: <see cref="Simulation.TryLoad"/> does that when the save is loaded, and its <see cref="SaveError"/> is what
/// the player is told. So a save damaged past its header is listed, then refused on Load.
/// </summary>
/// <param name="FormatVersion">The header's format version (any; only <see cref="SaveFormat.CurrentVersion"/> loads).</param>
/// <param name="Seed">The match's seed.</param>
/// <param name="Seats">One per player: the faction id (<c>-</c> for none), human or AI, the difficulty.</param>
/// <param name="MapWidth">The map's side in cells (its width).</param>
/// <param name="Tick">The tick number the save was made at (50 ms each).</param>
public sealed record SaveSummary(int FormatVersion, ulong Seed, ImmutableArray<ReplaySeat> Seats, int MapWidth, int Tick)
{
    /// <summary>Bytes read at most from a file's start: the longest header (16 seats with 256-byte ids) is under 5 KB.</summary>
    public const int MaxHeaderBytes = 8192;

    // MapGenParams fields the header writes (SaveHeader.WriteMap), each four bytes; the width is the first.
    private const int MapFields = 26;

    // The six capacities (units, commands, resources, buildings, projectiles, zones) before the map.
    private const int Capacities = 6;

    /// <summary>Reads the header at the start of <paramref name="stream"/> (at most <see cref="MaxHeaderBytes"/>); false for anything that isn't a save header, and for a stream that fails to read.</summary>
    public static bool TryRead(Stream stream, out SaveSummary? summary)
    {
        ArgumentNullException.ThrowIfNull(stream);
        summary = null;
        var buffer = new byte[MaxHeaderBytes];
        int read = 0;
        try
        {
            while (read < buffer.Length)
            {
                int got = stream.Read(buffer, read, buffer.Length - read);
                if (got <= 0) break;
                read += got;
            }
        }
        catch (Exception e) when (e is IOException or NotSupportedException or ObjectDisposedException or UnauthorizedAccessException)
        {
            return false;
        }
        return TryParse(buffer.AsSpan(0, read), out summary);
    }

    /// <summary>As <see cref="TryRead"/>, from a file's first bytes (all of them or at least the header).</summary>
    public static bool TryParse(ReadOnlySpan<byte> bytes, out SaveSummary? summary)
    {
        summary = null;
        if (bytes.Length < SaveFormat.FixedPrefix || !bytes[..SaveFormat.MagicLength].SequenceEqual(SaveFormat.Magic)) return false;
        int at = SaveFormat.MagicLength;
        int version = BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]);
        at = SaveFormat.FixedPrefix; // past the total length, which the full load checks
        if (version < 1 || !SkipString(bytes, ref at) || !Take(bytes, ref at, 8, out _)) return false; // sim version, data hash
        if (!Take(bytes, ref at, 8, out int seedAt) || !Take(bytes, ref at, 4, out int playersAt)) return false;
        ulong seed = BinaryPrimitives.ReadUInt64LittleEndian(bytes[seedAt..]);
        int players = BinaryPrimitives.ReadInt32LittleEndian(bytes[playersAt..]);
        if (players < 1 || players > SaveFormat.MaxPlayers) return false;
        var seats = ImmutableArray.CreateBuilder<ReplaySeat>(players);
        for (int p = 0; p < players; p++)
        {
            if (!ReadString(bytes, ref at, out string id) || id.Length == 0 || !Take(bytes, ref at, 2, out int kindAt)) return false;
            byte controller = bytes[kindAt], difficulty = bytes[kindAt + 1];
            if (controller > (byte)SeatController.Ai || difficulty > (byte)AiDifficulty.Hard) return false;
            seats.Add(new ReplaySeat(id, (SeatController)controller, (AiDifficulty)difficulty));
        }
        if (!Take(bytes, ref at, 4 * Capacities, out _) || !Take(bytes, ref at, 4 * MapFields, out int mapAt)) return false;
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes[mapAt..]);
        // combat (1 byte), the two cast walk-in floats, the two stats sample ints, then the tick.
        if (!Take(bytes, ref at, 1 + 4 + 4 + 4 + 4, out _) || !Take(bytes, ref at, 4, out int tickAt)) return false;
        int tick = BinaryPrimitives.ReadInt32LittleEndian(bytes[tickAt..]);
        if (tick < 0 || width < 1) return false;
        summary = new SaveSummary(version, seed, seats.MoveToImmutable(), width, tick);
        return true;
    }

    // n bytes at `at` (their offset out), or false when the bytes end first.
    private static bool Take(ReadOnlySpan<byte> bytes, ref int at, int n, out int start)
    {
        start = at;
        if (n < 0 || bytes.Length - at < n) return false;
        at += n;
        return true;
    }

    // A string field (int32 UTF-8 byte count, then the bytes), at most SaveFormat.MaxString bytes.
    private static bool ReadString(ReadOnlySpan<byte> bytes, ref int at, out string value)
    {
        value = "";
        if (!Take(bytes, ref at, 4, out int lengthAt)) return false;
        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes[lengthAt..]);
        if (length < 0 || length > SaveFormat.MaxString || !Take(bytes, ref at, length, out int textAt)) return false;
        try
        {
            value = new UTF8Encoding(false, true).GetString(bytes.Slice(textAt, length));
            return true;
        }
        catch (ArgumentException)
        {
            return false; // not UTF-8
        }
    }

    private static bool SkipString(ReadOnlySpan<byte> bytes, ref int at) => ReadString(bytes, ref at, out _);
}
