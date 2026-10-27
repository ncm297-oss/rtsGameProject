using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using Rts.Sim;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>
/// The save folder (M6-V14, docs/03 "Implementation (M6-V14)"): where the pause menu's Save writes
/// <c>&lt;yyyyMMdd-HHmmss&gt;-seed&lt;seed&gt;.sav</c> (with the recording so far beside it as the same stem's
/// <c>.replay</c>), and the saves the Load Game screen lists beside the replays.
/// </summary>
/// <remarks>
/// View code only, as <see cref="ReplayStore"/>: the wall clock names the file and nothing else (CLAUDE.md rule 2), and
/// every file operation catches its I/O errors (a failed save costs the file, never the match). The list reads each file's
/// header only (<see cref="SaveSummary"/>); a file whose header doesn't read is listed as unreadable, for Delete. The full
/// read (length, checksum, data hash, body) is <see cref="Simulation.TryLoad"/>'s, on Load. No cap: the player deletes saves.
/// </remarks>
public static class SaveStore
{
    /// <summary>The player's save folder.</summary>
    public const string DefaultDir = "user://saves";

    /// <summary>A save file's extension.</summary>
    public const string FileExtension = ".sav";

    /// <summary>The file name for a save made at <paramref name="localNow"/> of a match on <paramref name="seed"/>; <paramref name="copy"/> above 0 adds <c>-2</c>, <c>-3</c> ... for a second save in the same second.</summary>
    public static string FileName(DateTime localNow, ulong seed, int copy = 0) =>
        string.Create(CultureInfo.InvariantCulture, $"{localNow.ToString(ReplayStore.StampFormat, CultureInfo.InvariantCulture)}-seed{seed}{(copy > 0 ? $"-{copy + 1}" : "")}{FileExtension}");

    /// <summary>An absolute path in <paramref name="dir"/> (a <c>user://</c> or OS path) that neither a save nor its recording holds yet.</summary>
    public static string NewPath(string dir, DateTime localNow, ulong seed)
    {
        string full = Path.GetFullPath(ProjectSettings.GlobalizePath(dir));
        for (int copy = 0; ; copy++)
        {
            string path = Path.Combine(full, FileName(localNow, seed, copy));
            if (!File.Exists(path) && !File.Exists(ReplayPathOf(path))) return path;
        }
    }

    /// <summary>The recording written beside the save at <paramref name="savePath"/>: the same stem with <see cref="ReplayFormat.FileExtension"/>.</summary>
    public static string ReplayPathOf(string savePath) => Path.ChangeExtension(savePath, ReplayFormat.FileExtension);

    /// <summary>
    /// Saves <paramref name="sim"/> to <paramref name="path"/> (call it between ticks): <see cref="Simulation.TrySave"/> into
    /// memory, then the bytes into a <c>.tmp</c> beside the file, moved over it, so a crash mid-write never leaves a cut file
    /// under the name. False when the sim refuses (<paramref name="refused"/> its code, nothing written) or the write fails
    /// (<paramref name="refused"/> <see cref="SaveError.None"/>, the reason in <paramref name="ioError"/>).
    /// </summary>
    public static bool Write(Simulation sim, string path, out SaveError refused, out string ioError)
    {
        ioError = "";
        using var bytes = new MemoryStream();
        if (!sim.TrySave(bytes, out refused)) return false;
        string tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(tmp, bytes.ToArray());
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ioError = e.Message;
            try { File.Delete(tmp); } catch (Exception d) when (d is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }

    /// <summary>
    /// The saves in <paramref name="dir"/> whose header reads, newest first (<see cref="ReplayStore.DateOf"/>, then the name);
    /// every other <c>.sav</c> goes to <paramref name="unreadable"/> (newest first). Empty for a null or missing folder.
    /// </summary>
    public static List<SaveEntry> List(string? dir, List<string> unreadable)
    {
        var entries = new List<SaveEntry>();
        if (dir == null) return entries;
        var bad = new List<string>();
        foreach (string path in Files(dir))
        {
            if (TryReadHeader(path, out SaveEntry? entry)) entries.Add(entry!);
            else bad.Add(path);
        }
        entries.Sort((a, b) => a.When != b.When ? b.When.CompareTo(a.When) : string.CompareOrdinal(b.Path, a.Path));
        bad.Sort((a, b) =>
        {
            DateTime da = ReplayStore.DateOf(a), db = ReplayStore.DateOf(b);
            return da != db ? db.CompareTo(da) : string.CompareOrdinal(b, a);
        });
        unreadable.AddRange(bad);
        return entries;
    }

    /// <summary>The list entry of the save at <paramref name="path"/> from its header (<see cref="SaveSummary.TryRead"/>); false for anything else and for a file that can't be opened.</summary>
    public static bool TryReadHeader(string path, out SaveEntry? entry)
    {
        entry = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
            if (!SaveSummary.TryRead(stream, out SaveSummary? s)) return false;
            entry = new SaveEntry(path, ReplayStore.DateOf(path), s!.Seed, s.Seats, s.Tick);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the save at <paramref name="path"/> into a new simulation for <paramref name="data"/>
    /// (<see cref="Simulation.TryLoad"/>): null with the code in <paramref name="error"/> when it refuses, or with
    /// <paramref name="openFailed"/> true when the file can't be opened at all.
    /// </summary>
    public static Simulation? Load(Rts.Sim.Data.GameData data, string path, out SaveError error, out bool openFailed)
    {
        error = SaveError.None;
        openFailed = false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
            return Simulation.TryLoad(data, stream, out Simulation? sim, out error) ? sim : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            openFailed = true;
            return null;
        }
    }

    /// <summary>
    /// BUG-1002 item 1 (M6-VH4): the <c>.replay</c> files in save folder <paramref name="dir"/> with no <c>.sav</c> of the same
    /// stem beside them (the save deleted outside the game, or a Delete that removed the save but not its locked recording),
    /// unsorted. Load Game lists them with the replays, so Delete reaches them. None for a null or missing folder.
    /// </summary>
    public static List<string> LoneReplays(string? dir)
    {
        var lone = new List<string>();
        if (dir == null) return lone;
        try
        {
            string full = Path.GetFullPath(ProjectSettings.GlobalizePath(dir));
            if (!Directory.Exists(full)) return lone;
            foreach (string path in Directory.GetFiles(full))
                if (path.EndsWith(ReplayFormat.FileExtension, StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.ChangeExtension(path, FileExtension)))
                    lone.Add(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
        return lone;
    }

    /// <summary>Deletes a save file and the recording beside it; false when the save can't be deleted (gone already, locked).</summary>
    public static bool Delete(string path)
    {
        bool deleted = ReplayStore.Delete(path);
        if (path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) ReplayStore.Delete(ReplayPathOf(path));
        return deleted;
    }

    // Every *.sav file directly in the folder (absolute paths, one separator), unsorted; none for a missing folder.
    private static List<string> Files(string dir)
    {
        var files = new List<string>();
        try
        {
            string full = Path.GetFullPath(ProjectSettings.GlobalizePath(dir));
            if (!Directory.Exists(full)) return files;
            foreach (string path in Directory.GetFiles(full))
                if (path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) files.Add(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
        return files;
    }
}
