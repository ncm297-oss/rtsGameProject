using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Godot;

namespace Rts.Game;

/// <summary>
/// The player's settings (M6-V6, docs/02 "Game flow" 7): resolution, window mode, VSync, quality, three volumes and the
/// edge pan speed, stored in <c>user://settings.cfg</c> (a Godot <see cref="ConfigFile"/>, text). View state only: nothing
/// here reaches the sim.
/// </summary>
/// <remarks>
/// <see cref="Load"/> never fails: a missing file is the defaults (one info line), a file it can't read or parse is the
/// defaults (one warning), and each missing, mistyped or out-of-range key takes its default (all named in one warning);
/// extra keys are ignored. The text is checked against the shape <see cref="Save"/> writes before <see cref="ConfigFile.Parse"/>
/// sees it, because Godot logs an engine ERROR for a value it can't parse. <see cref="Save"/> writes through
/// <see cref="System.IO"/> and reports a failure (a read-only folder) as a warning, never an error.
/// </remarks>
public sealed class GameSettings
{
    /// <summary>Where the game keeps the file.</summary>
    public const string DefaultPath = "user://settings.cfg";

    /// <summary>Smallest and largest window size the file may hold, in pixels.</summary>
    public const int MinWidth = 640, MinHeight = 360, MaxWidth = 7680, MaxHeight = 4320;

    /// <summary>Default window size: the project's (Godot's default <c>display/window/size</c>), so a first run looks as before M6-V6.</summary>
    public const int DefaultWidth = 1152, DefaultHeight = 648;

    /// <summary>Volume range (percent of full).</summary>
    public const int MaxVolume = 100;

    /// <summary>Edge pan speed range (a multiplier on the camera's edge pan).</summary>
    public const float MinEdgePan = 0.5f, MaxEdgePan = 2f;

    /// <summary>The bus volume a volume of 0 sets: silent, never <c>-inf</c> (Godot's own floor for a fader).</summary>
    public const float SilentDb = -80f;

    /// <summary>The audio buses the volumes drive (<c>game/default_bus_layout.tres</c>; Music and Sfx send to Master).</summary>
    public const string MasterBus = "Master", MusicBus = "Music", SfxBus = "Sfx";

    // One "key=value" line of the shape Save writes: a bool, a number or a plain quoted string. Comments and sections pass.
    private static readonly Regex LineShape = new(
        @"^\s*(|[;#].*|\[[A-Za-z0-9_]+\]|[A-Za-z0-9_]+\s*=\s*(true|false|-?\d+(\.\d+)?([eE][-+]?\d+)?|""[^""\\]*""))\s*$", RegexOptions.CultureInvariant);

    /// <summary>Window width in pixels (used in <see cref="SettingsWindowMode.Windowed"/>).</summary>
    public int Width { get; set; } = DefaultWidth;

    /// <summary>Window height in pixels.</summary>
    public int Height { get; set; } = DefaultHeight;

    /// <summary>Windowed, borderless or fullscreen.</summary>
    public SettingsWindowMode WindowMode { get; set; } = SettingsWindowMode.Windowed;

    /// <summary>VSync on (the project's default) or off.</summary>
    public bool Vsync { get; set; } = true;

    /// <summary>The graphics preset.</summary>
    public SettingsQuality Quality { get; set; } = SettingsQuality.Medium;

    /// <summary>Master volume, 0-100.</summary>
    public int MasterVolume { get; set; } = MaxVolume;

    /// <summary>Music volume, 0-100.</summary>
    public int MusicVolume { get; set; } = MaxVolume;

    /// <summary>Sound effects volume, 0-100.</summary>
    public int SfxVolume { get; set; } = MaxVolume;

    /// <summary>Edge pan speed multiplier, 0.5-2.0.</summary>
    public float EdgePan { get; set; } = 1f;

    /// <summary>A copy.</summary>
    public GameSettings Clone() => (GameSettings)MemberwiseClone();

    /// <summary>True when every field equals <paramref name="other"/>'s.</summary>
    public bool SameAs(GameSettings other) =>
        Width == other.Width && Height == other.Height && WindowMode == other.WindowMode && Vsync == other.Vsync && Quality == other.Quality
        && MasterVolume == other.MasterVolume && MusicVolume == other.MusicVolume && SfxVolume == other.SfxVolume && EdgePan == other.EdgePan;

    /// <summary>The bus gain for a 0-100 volume: <c>20 log10(v / 100)</c> dB (50 is -6 dB, 10 is -20 dB), 0 and below <see cref="SilentDb"/>, never NaN.</summary>
    public static float VolumeDb(int volume)
    {
        if (volume <= 0) return SilentDb;
        return Math.Max(SilentDb, Mathf.LinearToDb(Math.Min(volume, MaxVolume) / (float)MaxVolume));
    }

    /// <summary>The file's text (<see cref="ConfigFile.EncodeToText"/>): sections <c>display</c>, <c>audio</c>, <c>camera</c>.</summary>
    public string Encode()
    {
        var file = new ConfigFile();
        file.SetValue("display", "width", Width);
        file.SetValue("display", "height", Height);
        file.SetValue("display", "window_mode", UiText.Key(WindowMode));
        file.SetValue("display", "vsync", Vsync);
        file.SetValue("display", "quality", UiText.Key(Quality));
        file.SetValue("audio", "master", MasterVolume);
        file.SetValue("audio", "music", MusicVolume);
        file.SetValue("audio", "sfx", SfxVolume);
        file.SetValue("camera", "edge_pan", EdgePan);
        return file.EncodeToText();
    }

    /// <summary>Writes the file at <paramref name="path"/> (a <c>user://</c> path); false with one warning logged if it can't.</summary>
    public bool Save(string path)
    {
        string full = ProjectSettings.GlobalizePath(path);
        try
        {
            string? dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(full, Encode());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            GD.PushWarning($"Settings: could not write {path} ({ex.Message}); the picks apply for this run only.");
            return false;
        }
    }

    /// <summary>Reads <paramref name="path"/>; the defaults for anything missing or bad, each problem in <paramref name="problems"/> (empty for a clean file).</summary>
    /// <param name="path">A <c>user://</c> (or absolute) path.</param>
    /// <param name="problems">Filled with one entry per problem; a missing file is not a problem.</param>
    /// <param name="found">False when there is no file.</param>
    public static GameSettings Read(string path, List<string> problems, out bool found)
    {
        var settings = new GameSettings();
        string full = ProjectSettings.GlobalizePath(path);
        found = File.Exists(full);
        if (!found) return settings;
        string text;
        try
        {
            text = File.ReadAllText(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            problems.Add($"cannot read it ({ex.Message})");
            return settings;
        }
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (LineShape.IsMatch(lines[i])) continue;
            problems.Add($"line {i + 1} is not 'key=value' with a number, true / false or a quoted string");
            return settings;
        }
        var file = new ConfigFile();
        if (file.Parse(text) != Error.Ok)
        {
            problems.Add("it does not parse");
            return settings;
        }
        settings.Width = Int(file, "display", "width", MinWidth, MaxWidth, settings.Width, problems);
        settings.Height = Int(file, "display", "height", MinHeight, MaxHeight, settings.Height, problems);
        settings.WindowMode = Named(file, "display", "window_mode", settings.WindowMode, problems);
        settings.Vsync = Bool(file, "display", "vsync", settings.Vsync, problems);
        settings.Quality = Named(file, "display", "quality", settings.Quality, problems);
        settings.MasterVolume = Int(file, "audio", "master", 0, MaxVolume, settings.MasterVolume, problems);
        settings.MusicVolume = Int(file, "audio", "music", 0, MaxVolume, settings.MusicVolume, problems);
        settings.SfxVolume = Int(file, "audio", "sfx", 0, MaxVolume, settings.SfxVolume, problems);
        settings.EdgePan = Float(file, "camera", "edge_pan", MinEdgePan, MaxEdgePan, settings.EdgePan, problems);
        return settings;
    }

    /// <summary>
    /// <see cref="Read"/>, logged: no file prints one info line, any problem one warning naming them all ("using the defaults
    /// for those"). Never an error: a bad settings file must not stop the game.
    /// </summary>
    public static GameSettings Load(string path)
    {
        var problems = new List<string>();
        GameSettings settings = Read(path, problems, out bool found);
        if (!found) GD.Print($"Settings: no {path} yet; using the defaults.");
        else if (problems.Count > 0) GD.PushWarning($"Settings: {path}: {string.Join("; ", problems)}; using the defaults for those.");
        else GD.Print($"Settings: read {path}.");
        return settings;
    }

    private static bool TryValue(ConfigFile file, string section, string key, List<string> problems, out Variant value)
    {
        value = default;
        if (file.HasSectionKey(section, key))
        {
            value = file.GetValue(section, key);
            return true;
        }
        problems.Add($"{section}.{key} is missing");
        return false;
    }

    private static int Int(ConfigFile file, string section, string key, int min, int max, int fallback, List<string> problems)
    {
        if (!TryValue(file, section, key, problems, out Variant v)) return fallback;
        if (v.VariantType == Variant.Type.Int && v.AsInt64() >= min && v.AsInt64() <= max) return (int)v.AsInt64();
        problems.Add($"{section}.{key} must be a whole number from {min} to {max}, not {v}");
        return fallback;
    }

    private static float Float(ConfigFile file, string section, string key, float min, float max, float fallback, List<string> problems)
    {
        if (!TryValue(file, section, key, problems, out Variant v)) return fallback;
        double d = v.VariantType switch { Variant.Type.Float => v.AsDouble(), Variant.Type.Int => v.AsInt64(), _ => double.NaN };
        if (d >= min && d <= max) return (float)d;
        problems.Add($"{section}.{key} must be a number from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}, not {v}");
        return fallback;
    }

    private static bool Bool(ConfigFile file, string section, string key, bool fallback, List<string> problems)
    {
        if (!TryValue(file, section, key, problems, out Variant v)) return fallback;
        if (v.VariantType == Variant.Type.Bool) return v.AsBool();
        problems.Add($"{section}.{key} must be true or false, not {v}");
        return fallback;
    }

    private static T Named<T>(ConfigFile file, string section, string key, T fallback, List<string> problems) where T : struct, Enum
    {
        if (!TryValue(file, section, key, problems, out Variant v)) return fallback;
        if (v.VariantType == Variant.Type.String)
            foreach (T member in Enum.GetValues<T>())
                if (UiText.Key(member) == v.AsString()) return member;
        var names = new List<string>();
        foreach (T member in Enum.GetValues<T>()) names.Add($"\"{UiText.Key(member)}\"");
        problems.Add($"{section}.{key} must be one of {string.Join(", ", names)}, not {v}");
        return fallback;
    }
}
