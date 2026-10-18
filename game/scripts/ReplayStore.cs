using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Replays;

namespace Rts.Game;

/// <summary>
/// The replay folder (M6-V9, docs/03 "Implementation (M6-V9)"): where the shell writes each match's recording, the oldest
/// pruned past <see cref="MaxReplays"/>, and the readable files the Load Game screen lists.
/// </summary>
/// <remarks>
/// View code only. The wall clock is read here and in <see cref="Main"/> for one thing, the file name's start time; it never
/// reaches the sim (CLAUDE.md rule 2). Every file operation catches its I/O errors: a full disk or a locked file costs the
/// recording, never the match. BUG-0830: the list and the prune read only each file's header and last line
/// (<see cref="TryReadHeader"/>); a file whose header doesn't read is unreadable: it holds no cap slot, the prune never
/// deletes it, and Load Game lists it for Delete. The full read (checksum, commands) happens when a replay is watched.
/// </remarks>
public static class ReplayStore
{
    /// <summary>The player's replay folder.</summary>
    public const string DefaultDir = "user://replays";

    /// <summary>Most readable replay files the folder keeps: writing one more deletes the oldest (by the start time in the name).</summary>
    /// <remarks>A one-hour four-seat match is a few MB of text; the cap keeps the folder and the list small.</remarks>
    public const int MaxReplays = 20;

    /// <summary>The start time's form at the front of a file name (local time): <c>20261016-203600-seed7.replay</c>.</summary>
    public const string StampFormat = "yyyyMMdd-HHmmss";

    /// <summary>The file name for a match started at <paramref name="localStart"/> on <paramref name="seed"/>; <paramref name="copy"/> above 0 adds <c>-2</c>, <c>-3</c> ... for a second match in the same second.</summary>
    public static string FileName(DateTime localStart, ulong seed, int copy = 0) =>
        string.Create(CultureInfo.InvariantCulture, $"{localStart.ToString(StampFormat, CultureInfo.InvariantCulture)}-seed{seed}{(copy > 0 ? $"-{copy + 1}" : "")}{ReplayFormat.FileExtension}");

    /// <summary>An absolute path in <paramref name="dir"/> (a <c>user://</c> or OS path) no file holds yet for a match started at <paramref name="localStart"/> on <paramref name="seed"/>.</summary>
    public static string NewPath(string dir, DateTime localStart, ulong seed)
    {
        string full = Path.GetFullPath(ProjectSettings.GlobalizePath(dir));
        for (int copy = 0; ; copy++)
        {
            string path = Path.Combine(full, FileName(localStart, seed, copy));
            if (!File.Exists(path)) return path;
        }
    }

    /// <summary>
    /// Writes <paramref name="replay"/> to <paramref name="path"/> (<see cref="ReplayFormat.WriteFile"/> into a <c>.tmp</c>
    /// beside it, then moved over the file, so a crash mid-write never leaves a cut file under the name), creating the folder;
    /// false with the reason in <paramref name="error"/> when it can't.
    /// </summary>
    public static bool Write(Replay replay, string path, out string error)
    {
        error = "";
        string tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ReplayFormat.WriteFile(replay, tmp);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = e.Message;
            try { File.Delete(tmp); } catch (Exception d) when (d is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }

    /// <summary>
    /// Deletes the oldest readable <c>.replay</c> files in <paramref name="dir"/> past <paramref name="keep"/> (newest kept, by
    /// <see cref="DateOf"/>); returns how many it deleted. Unreadable files (<see cref="TryReadHeader"/>) are neither counted nor
    /// deleted. <paramref name="justWritten"/> is always kept and counts as the newest, whatever its name says: a folder of
    /// future-dated names (another machine's clock, a time-zone change, the DST fall-back hour) never costs the new recording.
    /// </summary>
    public static int Prune(string dir, int keep, string? justWritten = null)
    {
        var files = new List<string>();
        foreach (string path in Files(dir))
            if (TryReadHeader(path, out _)) files.Add(path);
        files.Sort(NewestFirst);
        if (justWritten != null)
        {
            int at = files.FindIndex(f => SamePath(f, justWritten));
            if (at > 0)
            {
                string mine = files[at];
                files.RemoveAt(at);
                files.Insert(0, mine);
            }
        }
        int deleted = 0;
        for (int i = Math.Max(0, keep); i < files.Count; i++)
            if ((justWritten == null || !SamePath(files[i], justWritten)) && Delete(files[i])) deleted++;
        return deleted;
    }

    /// <summary>
    /// The readable replays in <paramref name="dir"/>, newest first (<see cref="DateOf"/>, then the name), from their headers
    /// (<see cref="TryReadHeader"/>); every <c>.replay</c> file whose header doesn't read is left out and counted in
    /// <paramref name="skipped"/>. Empty for a null or missing folder.
    /// </summary>
    public static List<ReplayEntry> List(string? dir, out int skipped) => List(dir, null, out skipped);

    /// <summary>As <see cref="List(string?, out int)"/>, also adding each unreadable file's path to <paramref name="unreadable"/> (newest first by <see cref="DateOf"/>).</summary>
    public static List<ReplayEntry> List(string? dir, List<string>? unreadable, out int skipped)
    {
        skipped = 0;
        var entries = new List<ReplayEntry>();
        if (dir == null) return entries;
        var bad = new List<string>();
        foreach (string path in Files(dir))
        {
            if (TryReadHeader(path, out ReplayEntry? entry)) entries.Add(entry!);
            else bad.Add(path);
        }
        skipped = bad.Count;
        entries.Sort((a, b) => a.When != b.When ? b.When.CompareTo(a.When) : string.CompareOrdinal(b.Path, a.Path));
        if (unreadable != null)
        {
            bad.Sort(NewestFirst);
            unreadable.AddRange(bad);
        }
        return entries;
    }

    /// <summary>Header lines read at most before <c>ticks</c>: the fixed keys and up to <see cref="Replay.MaxPlayers"/> seat lines.</summary>
    private const int MaxHeaderLines = 16 + Replay.MaxPlayers;

    /// <summary>Bytes read from a file's end for its last line (a checksum line is 26).</summary>
    private const int TailBytes = 64;

    /// <summary>
    /// The list entry of the replay at <paramref name="path"/> from its header alone (BUG-0830: the list no longer reads whole
    /// files): the lines <c>rts-replay</c> (a format this build reads) through <c>ticks</c> in the writer's order, the seat
    /// lines of format 5 on, and a last line that is a <c>checksum</c> line (a cut file has none). The commands, checkpoints and
    /// the checksum itself are checked when the replay is watched (<see cref="ReplayFormat.TryReadFile"/>), so a file damaged past
    /// its header is listed and then refused. False for anything else, and for a file that can't be opened.
    /// </summary>
    public static bool TryReadHeader(string path, out ReplayEntry? entry)
    {
        entry = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
            if (!EndsWithChecksum(stream)) return false;
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false);
            string[] keys = { "sim-version", "data-hash", "seed", "players" };
            if (!Fields(reader, "rts-replay", out string v) || !int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int version)
                || version < Replay.OldestFormatVersion || version > Replay.CurrentFormatVersion)
                return false;
            var values = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                if (!Fields(reader, keys[i], out values[i])) return false;
            if (!ulong.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed)
                || !int.TryParse(values[3], NumberStyles.None, CultureInfo.InvariantCulture, out int players) || players < 1 || players > Replay.MaxPlayers)
                return false;
            var seats = ImmutableArray<ReplaySeat>.Empty;
            if (version >= 5)
            {
                var builder = ImmutableArray.CreateBuilder<ReplaySeat>(players);
                for (int p = 0; p < players; p++)
                {
                    if (!Fields(reader, "seat", out string seatLine)) return false;
                    string[] f = seatLine.Split(' ');
                    int difficulty = f.Length == 4 ? DataLimits.AiDifficultyIds.IndexOf(f[3]) : -1;
                    if (difficulty < 0 || f[0] != p.ToString(CultureInfo.InvariantCulture) || f[1].Length == 0 || f[2] is not ("human" or "ai")) return false;
                    builder.Add(new ReplaySeat(f[1], f[2] == "ai" ? SeatController.Ai : SeatController.Human, (AiDifficulty)difficulty));
                }
                seats = builder.MoveToImmutable();
            }
            // unit-capacity, command-capacity, resource-capacity, checkpoint-interval, then ticks.
            for (int n = 0; n < MaxHeaderLines; n++)
            {
                string? line = reader.ReadLine();
                if (line == null) return false;
                if (!line.StartsWith("ticks ", StringComparison.Ordinal)) continue;
                if (!int.TryParse(line.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out int ticks)) return false;
                entry = new ReplayEntry(path, DateOf(path), seed, seats, ticks);
                return true;
            }
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // The next line is `key value...`; the value is the rest of the line after one space.
    private static bool Fields(StreamReader reader, string key, out string value)
    {
        value = "";
        string? line = reader.ReadLine();
        if (line == null || line.Length <= key.Length + 1 || !line.StartsWith(key, StringComparison.Ordinal) || line[key.Length] != ' ') return false;
        value = line[(key.Length + 1)..];
        return true;
    }

    // The file ends in LF and its last line starts "checksum ": what a cut file lacks.
    private static bool EndsWithChecksum(FileStream stream)
    {
        long length = stream.Length;
        if (length < 2) return false;
        int n = (int)Math.Min(TailBytes, length);
        stream.Position = length - n;
        var tail = new byte[n];
        int read = 0;
        while (read < n)
        {
            int got = stream.Read(tail, read, n - read);
            if (got <= 0) return false;
            read += got;
        }
        if (tail[^1] != (byte)'\n') return false;
        int start = Array.LastIndexOf(tail, (byte)'\n', n - 2) + 1;
        if (start == 0 && n == TailBytes) return false; // no line break in the tail: the last line is too long to be a checksum
        return Encoding.ASCII.GetString(tail, start, n - 1 - start).StartsWith("checksum ", StringComparison.Ordinal);
    }

    /// <summary>When the match in <paramref name="path"/> started: the <see cref="StampFormat"/> time at the front of its name, else the file's last write time.</summary>
    public static DateTime DateOf(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.Length >= StampFormat.Length
            && DateTime.TryParseExact(name[..StampFormat.Length], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when))
            return when;
        try
        {
            return File.GetLastWriteTime(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>Deletes one replay file; false when it can't (gone already, locked).</summary>
    public static bool Delete(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // Every *.replay file directly in the folder (absolute paths in the OS's form, one separator: BUG-0830), unsorted; none for
    // a missing folder.
    private static List<string> Files(string dir)
    {
        var files = new List<string>();
        string full;
        try
        {
            full = Path.GetFullPath(ProjectSettings.GlobalizePath(dir));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return files;
        }
        try
        {
            if (!Directory.Exists(full)) return files;
            foreach (string path in Directory.GetFiles(full))
                if (path.EndsWith(ReplayFormat.FileExtension, StringComparison.OrdinalIgnoreCase)) files.Add(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
        return files;
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static int NewestFirst(string a, string b)
    {
        DateTime da = DateOf(a), db = DateOf(b);
        return da != db ? db.CompareTo(da) : string.CompareOrdinal(b, a);
    }
}
