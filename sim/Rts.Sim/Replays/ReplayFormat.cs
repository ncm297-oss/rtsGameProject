using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Replays;

/// <summary>Reads and writes replays as line-based ASCII text (docs/03 "Save/load and replays").</summary>
/// <remarks>
/// One <c>key value</c> line per header field in a fixed order, then <c>commands N</c> and N
/// <c>c</c> lines, <c>checkpoints N</c> and N <c>k</c> lines, <c>end</c>, and last a
/// <c>checksum</c> line: the FNV-1a 64 hash of every byte before it, so any changed byte is caught.
/// Integers are invariant-culture decimal in canonical form; hashes are 16 uppercase hex digits;
/// floats are their exact IEEE-754 bit pattern as 8 uppercase hex digits. Lines end in LF only.
/// Reading never throws on bad input: it returns a <see cref="ReplayError"/> and no replay.
/// Runs outside <c>Tick</c>, so it may allocate.
/// </remarks>
public static class ReplayFormat
{
    /// <summary>File extension for replay files.</summary>
    public const string FileExtension = ".replay";

    private const string Magic = "rts-replay";
    private const string ChecksumKey = "checksum ";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The file bytes for a valid replay; throws <see cref="ArgumentException"/> for one that breaks <see cref="Replay.Validate"/>.</summary>
    public static byte[] Write(Replay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ReplayError invalid = replay.Validate();
        if (invalid != ReplayError.None)
            throw new ArgumentException($"Replay is not valid ({invalid}); it would not read back.", nameof(replay));

        var sb = new StringBuilder();
        Line(sb, Magic, Int(replay.FormatVersion));
        Line(sb, "sim-version", replay.SimVersion);
        Line(sb, "data-hash", Hex(replay.DataHash));
        Line(sb, "seed", replay.Seed.ToString(Inv));
        Line(sb, "players", Int(replay.PlayerCount));
        Line(sb, "unit-capacity", Int(replay.UnitCapacity));
        Line(sb, "command-capacity", Int(replay.CommandCapacity));
        Line(sb, "resource-capacity", Int(replay.ResourceCapacity));
        Line(sb, "checkpoint-interval", Int(replay.CheckpointInterval));
        Line(sb, "ticks", Int(replay.TickCount));
        MapGenParams m = replay.Map;
        Line(sb, "map.width", Int(m.Width));
        Line(sb, "map.height", Int(m.Height));
        Line(sb, "map.edge-margin", Int(m.EdgeMargin));
        Line(sb, "map.level1-plateaus", Int(m.Level1Plateaus));
        Line(sb, "map.level1-min-size", Int(m.Level1MinSize));
        Line(sb, "map.level1-max-size", Int(m.Level1MaxSize));
        Line(sb, "map.level2-plateaus", Int(m.Level2Plateaus));
        Line(sb, "map.level2-min-size", Int(m.Level2MinSize));
        Line(sb, "map.level2-max-size", Int(m.Level2MaxSize));
        Line(sb, "map.level2-inset", Int(m.Level2Inset));
        Line(sb, "map.ramp-width", Int(m.RampWidth));
        Line(sb, "map.ramp-length", Int(m.RampLength));
        Line(sb, "map.ramps-per-plateau", Int(m.RampsPerPlateau));
        Line(sb, "map.ramp-tries", Int(m.RampTries));
        Line(sb, "map.min-passable-fraction", Bits(m.MinPassableFraction));
        Line(sb, "map.max-attempts", Int(m.MaxAttempts));
        Line(sb, "map.forests", Int(m.Forests));
        Line(sb, "map.forest-min-trees", Int(m.ForestMinTrees));
        Line(sb, "map.forest-max-trees", Int(m.ForestMaxTrees));
        Line(sb, "map.gold-mines", Int(m.GoldMines));
        Line(sb, "map.mine-spacing", Bits(m.MineSpacing));

        Line(sb, "commands", Int(replay.Commands.Length));
        foreach (Command c in replay.Commands)
        {
            sb.Append("c ").Append(Int(c.Tick)).Append(' ').Append(Int(c.Player)).Append(' ').Append(Int(c.Sequence))
                .Append(' ').Append(Int((int)c.Kind)).Append(' ').Append(Int(c.TypeId))
                .Append(' ').Append(Bits(c.Position.X)).Append(' ').Append(Bits(c.Position.Y))
                .Append(' ').Append(Int(c.Unit.Index)).Append(' ').Append(Int(c.Unit.Generation))
                .Append(' ').Append(Int(c.Flags)).Append('\n');
        }
        Line(sb, "checkpoints", Int(replay.Checkpoints.Length));
        foreach (ReplayCheckpoint k in replay.Checkpoints)
            sb.Append("k ").Append(Int(k.Tick)).Append(' ').Append(Hex(k.Hash)).Append('\n');
        sb.Append("end\n");
        return Seal(sb.ToString());
    }

    /// <summary>Writes <see cref="Write"/>'s bytes to <paramref name="path"/>.</summary>
    public static void WriteFile(Replay replay, string path) => File.WriteAllBytes(path, Write(replay));

    /// <summary>Reads a replay file; an unreadable file gives <see cref="ReplayError.Unreadable"/>.</summary>
    public static ReplayError TryReadFile(string path, out Replay? replay)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            replay = null;
            return ReplayError.Unreadable;
        }
        return TryRead(bytes, out replay);
    }

    /// <summary>Parses replay file bytes; on any error returns its code and a null replay.</summary>
    public static ReplayError TryRead(ReadOnlySpan<byte> bytes, out Replay? replay) => TryRead(bytes, out replay, out _);

    /// <summary>As <see cref="TryRead(ReadOnlySpan{byte}, out Replay?)"/>, plus the 1-based line of a syntax error (0 when not tied to a line).</summary>
    public static ReplayError TryRead(ReadOnlySpan<byte> bytes, out Replay? replay, out int errorLine)
    {
        replay = null;
        errorLine = 0;

        // The last line must be a complete checksum line; anything else means the file was cut short.
        if (bytes.Length == 0 || bytes[^1] != (byte)'\n') return ReplayError.Truncated;
        int start = bytes[..^1].LastIndexOf((byte)'\n') + 1;
        ReadOnlySpan<byte> last = bytes[start..^1];
        if (!last.StartsWith("checksum "u8)) return ReplayError.Truncated;
        if (!TryHex(Ascii(last[ChecksumKey.Length..]), out ulong stored)) return ReplayError.Malformed;
        ReadOnlySpan<byte> body = bytes[..start];
        if (Checksum(body) != stored) return ReplayError.ChecksumMismatch;
        if (body.Length == 0) return ReplayError.Truncated;
        foreach (byte b in body)
            if (b != (byte)'\n' && (b < 0x20 || b > 0x7E)) return ReplayError.Malformed;

        var r = new LineReader(Encoding.ASCII.GetString(body[..^1]).Split('\n'));
        ReplayError e = Parse(r, out replay);
        if (e != ReplayError.None)
        {
            errorLine = r.ErrorLine;
            replay = null;
        }
        return e;
    }

    /// <summary>Appends the checksum line to a body that ends in LF. Tests use it to build files with valid checksums but bad content.</summary>
    internal static byte[] Seal(string body)
    {
        byte[] head = Encoding.ASCII.GetBytes(body);
        byte[] tail = Encoding.ASCII.GetBytes(ChecksumKey + Hex(Checksum(head)) + "\n");
        byte[] all = new byte[head.Length + tail.Length];
        head.CopyTo(all, 0);
        tail.CopyTo(all, head.Length);
        return all;
    }

    /// <summary>FNV-1a 64 over raw bytes.</summary>
    internal static ulong Checksum(ReadOnlySpan<byte> bytes)
    {
        const ulong offsetBasis = 14695981039346656037UL, prime = 1099511628211UL;
        ulong h = offsetBasis;
        foreach (byte b in bytes) h = unchecked((h ^ b) * prime);
        return h;
    }

    private static ReplayError Parse(LineReader r, out Replay? replay)
    {
        replay = null;
        if (!r.Fields(Magic, 1, out string[] f) || !TryInt(f[0], out int version)) return ReplayError.Malformed;
        // A newer format may change everything after this line, so stop here.
        if (version != Replay.CurrentFormatVersion) return ReplayError.FormatVersionMismatch;

        if (!r.Fields("sim-version", 1, out f)) return ReplayError.Malformed;
        string simVersion = f[0];
        if (!r.Hex("data-hash", out ulong dataHash)) return ReplayError.Malformed;
        if (!r.Fields("seed", 1, out f) || !TryULong(f[0], out ulong seed)) return ReplayError.Malformed;
        if (!r.Int("players", out int players)) return ReplayError.Malformed;
        if (!r.Int("unit-capacity", out int unitCapacity)) return ReplayError.Malformed;
        if (!r.Int("command-capacity", out int commandCapacity)) return ReplayError.Malformed;
        if (!r.Int("resource-capacity", out int resourceCapacity)) return ReplayError.Malformed;
        if (!r.Int("checkpoint-interval", out int interval)) return ReplayError.Malformed;
        if (!r.Int("ticks", out int ticks)) return ReplayError.Malformed;

        if (!r.Int("map.width", out int width) || !r.Int("map.height", out int height)
            || !r.Int("map.edge-margin", out int edgeMargin)
            || !r.Int("map.level1-plateaus", out int l1) || !r.Int("map.level1-min-size", out int l1Min) || !r.Int("map.level1-max-size", out int l1Max)
            || !r.Int("map.level2-plateaus", out int l2) || !r.Int("map.level2-min-size", out int l2Min) || !r.Int("map.level2-max-size", out int l2Max)
            || !r.Int("map.level2-inset", out int l2Inset)
            || !r.Int("map.ramp-width", out int rampWidth) || !r.Int("map.ramp-length", out int rampLength)
            || !r.Int("map.ramps-per-plateau", out int rampsPer) || !r.Int("map.ramp-tries", out int rampTries)
            || !r.Fields("map.min-passable-fraction", 1, out f) || !TryBits(f[0], out float minPassable)
            || !r.Int("map.max-attempts", out int maxAttempts)
            || !r.Int("map.forests", out int forests)
            || !r.Int("map.forest-min-trees", out int forestMin) || !r.Int("map.forest-max-trees", out int forestMax)
            || !r.Int("map.gold-mines", out int goldMines)
            || !r.Fields("map.mine-spacing", 1, out f) || !TryBits(f[0], out float mineSpacing))
            return ReplayError.Malformed;
        var map = new MapGenParams
        {
            Width = width,
            Height = height,
            EdgeMargin = edgeMargin,
            Level1Plateaus = l1,
            Level1MinSize = l1Min,
            Level1MaxSize = l1Max,
            Level2Plateaus = l2,
            Level2MinSize = l2Min,
            Level2MaxSize = l2Max,
            Level2Inset = l2Inset,
            RampWidth = rampWidth,
            RampLength = rampLength,
            RampsPerPlateau = rampsPer,
            RampTries = rampTries,
            MinPassableFraction = minPassable,
            MaxAttempts = maxAttempts,
            Forests = forests,
            ForestMinTrees = forestMin,
            ForestMaxTrees = forestMax,
            GoldMines = goldMines,
            MineSpacing = mineSpacing,
        };

        // Counts are checked against the lines actually present, never used to size a buffer up front.
        if (!r.Int("commands", out int commandCount) || commandCount < 0 || commandCount > r.Remaining) return ReplayError.Malformed;
        var commands = ImmutableArray.CreateBuilder<Command>(commandCount);
        for (int i = 0; i < commandCount; i++)
        {
            if (!r.Fields("c", 10, out f)
                || !TryInt(f[0], out int tick) || !TryInt(f[1], out int player) || !TryInt(f[2], out int sequence)
                || !TryInt(f[3], out int kind) || !TryInt(f[4], out int typeId)
                || !TryBits(f[5], out float x) || !TryBits(f[6], out float y)
                || !TryInt(f[7], out int unitIndex) || !TryInt(f[8], out int unitGeneration)
                || !TryInt(f[9], out int flags))
                return ReplayError.Malformed;
            commands.Add(new Command
            {
                Kind = (CommandKind)kind,
                Player = player,
                Tick = tick,
                Sequence = sequence,
                TypeId = typeId,
                Position = new Vector2(x, y),
                Unit = new EntityHandle(unitIndex, unitGeneration),
                Flags = flags,
            });
        }

        if (!r.Int("checkpoints", out int checkpointCount) || checkpointCount < 0 || checkpointCount > r.Remaining) return ReplayError.Malformed;
        var checkpoints = ImmutableArray.CreateBuilder<ReplayCheckpoint>(checkpointCount);
        for (int i = 0; i < checkpointCount; i++)
        {
            if (!r.Fields("k", 2, out f) || !TryInt(f[0], out int tick) || !TryHex(f[1], out ulong hash)) return ReplayError.Malformed;
            checkpoints.Add(new ReplayCheckpoint(tick, hash));
        }
        if (!r.Fields("end", 0, out _) || r.Remaining != 0) return ReplayError.Malformed;

        var result = new Replay
        {
            FormatVersion = version,
            SimVersion = simVersion,
            DataHash = dataHash,
            Map = map,
            Seed = seed,
            PlayerCount = players,
            UnitCapacity = unitCapacity,
            CommandCapacity = commandCapacity,
            ResourceCapacity = resourceCapacity,
            CheckpointInterval = interval,
            TickCount = ticks,
            Commands = commands.MoveToImmutable(),
            Checkpoints = checkpoints.MoveToImmutable(),
        };
        r.ErrorLine = 0; // semantic errors aren't tied to one line
        ReplayError invalid = result.Validate();
        if (invalid != ReplayError.None) return invalid;
        replay = result;
        return ReplayError.None;
    }

    private static void Line(StringBuilder sb, string key, string value) => sb.Append(key).Append(' ').Append(value).Append('\n');

    private static string Int(int value) => value.ToString(Inv);

    private static string Hex(ulong value) => value.ToString("X16", Inv);

    private static string Bits(float value) => ((uint)BitConverter.SingleToInt32Bits(value)).ToString("X8", Inv);

    private static string Ascii(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes);

    // Parsers accept only the canonical text the writer produces, so every value has one spelling.

    private static bool TryInt(string s, out int value) =>
        int.TryParse(s, NumberStyles.AllowLeadingSign, Inv, out value) && value.ToString(Inv) == s;

    private static bool TryULong(string s, out ulong value) =>
        ulong.TryParse(s, NumberStyles.None, Inv, out value) && value.ToString(Inv) == s;

    private static bool TryHex(string s, out ulong value) =>
        ulong.TryParse(s, NumberStyles.AllowHexSpecifier, Inv, out value) && Hex(value) == s;

    private static bool TryBits(string s, out float value)
    {
        value = 0f;
        if (!uint.TryParse(s, NumberStyles.AllowHexSpecifier, Inv, out uint bits) || bits.ToString("X8", Inv) != s) return false;
        value = BitConverter.Int32BitsToSingle((int)bits);
        return true;
    }

    /// <summary>Walks the body lines; each read expects a given key and field count.</summary>
    private sealed class LineReader
    {
        private readonly string[] _lines;
        private int _next;

        public LineReader(string[] lines) => _lines = lines;

        public int Remaining => _lines.Length - _next;

        public int ErrorLine { get; set; }

        /// <summary>Reads the next line as <c>key f1 ... fn</c> with exactly <paramref name="count"/> single-space-separated fields.</summary>
        public bool Fields(string key, int count, out string[] fields)
        {
            fields = Array.Empty<string>();
            ErrorLine = _next + 1;
            if (_next >= _lines.Length) return false;
            string[] parts = _lines[_next++].Split(' ');
            if (parts.Length != count + 1 || parts[0] != key) return false;
            fields = parts[1..];
            foreach (string p in fields) if (p.Length == 0) return false;
            return true;
        }

        public bool Int(string key, out int value)
        {
            value = 0;
            return Fields(key, 1, out string[] f) && TryInt(f[0], out value);
        }

        public bool Hex(string key, out ulong value)
        {
            value = 0;
            return Fields(key, 1, out string[] f) && TryHex(f[0], out value);
        }
    }
}
