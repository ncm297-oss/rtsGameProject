using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using Rts.Sim.Replays;

namespace Rts.Game;

/// <summary>
/// The replay folder (M6-V9, docs/03 "Implementation (M6-V9)"): where the shell writes each match's recording, the oldest
/// pruned past <see cref="MaxReplays"/>, and the readable files the Load Game screen lists.
/// </summary>
/// <remarks>
/// View code only. The wall clock is read here and in <see cref="Main"/> for one thing, the file name's start time; it never
/// reaches the sim (CLAUDE.md rule 2). Every file operation catches its I/O errors: a full disk or a locked file costs the
/// recording, never the match.
/// </remarks>
public static class ReplayStore
{
    /// <summary>The player's replay folder.</summary>
    public const string DefaultDir = "user://replays";

    /// <summary>Most replay files the folder keeps: writing one more deletes the oldest (by the start time in the name).</summary>
    /// <remarks>A one-hour four-seat match is a few MB of text and the list reads every file when it opens, so the cap keeps the folder and the screen small.</remarks>
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

    /// <summary>Deletes the oldest <c>.replay</c> files in <paramref name="dir"/> past <paramref name="keep"/> (newest kept, by <see cref="DateOf"/>); returns how many it deleted.</summary>
    public static int Prune(string dir, int keep)
    {
        List<string> files = Files(dir);
        files.Sort(NewestFirst);
        int deleted = 0;
        for (int i = Math.Max(0, keep); i < files.Count; i++)
            if (Delete(files[i])) deleted++;
        return deleted;
    }

    /// <summary>
    /// The readable replays in <paramref name="dir"/>, newest first (<see cref="DateOf"/>, then the name); every <c>.replay</c>
    /// file <see cref="ReplayFormat.TryReadFile"/> refuses is left out and counted in <paramref name="skipped"/>. Empty for a
    /// null or missing folder.
    /// </summary>
    public static List<ReplayEntry> List(string? dir, out int skipped)
    {
        skipped = 0;
        var entries = new List<ReplayEntry>();
        if (dir == null) return entries;
        foreach (string path in Files(dir))
        {
            if (ReplayFormat.TryReadFile(path, out Replay? replay) != ReplayError.None || replay == null)
            {
                skipped++;
                continue;
            }
            entries.Add(new ReplayEntry(path, DateOf(path), replay.Seed, replay.Seats, replay.TickCount));
        }
        entries.Sort((a, b) => a.When != b.When ? b.When.CompareTo(a.When) : string.CompareOrdinal(b.Path, a.Path));
        return entries;
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

    // Every *.replay file directly in the folder (absolute paths), unsorted; none for a missing folder.
    private static List<string> Files(string dir)
    {
        var files = new List<string>();
        string full = ProjectSettings.GlobalizePath(dir);
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

    private static int NewestFirst(string a, string b)
    {
        DateTime da = DateOf(a), db = DateOf(b);
        return da != db ? db.CompareTo(da) : string.CompareOrdinal(b, a);
    }
}
