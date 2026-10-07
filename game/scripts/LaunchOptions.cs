using System;
using System.Globalization;
using Godot;
using Rts.Sim.Map;

namespace Rts.Game;

/// <summary>Debug options from the user args after <c>--</c> on the Godot command line.</summary>
/// <remarks>
/// <c>--seed &lt;n&gt;</c>, <c>--speed &lt;x&gt;</c>, <c>--screenshot &lt;path&gt;</c>,
/// <c>--screenshot-after &lt;seconds&gt;</c> (default 2), <c>--units &lt;n&gt;</c> (per player, 0 to
/// <see cref="MaxUnitsPerPlayer"/>, default <see cref="DefaultUnitsPerPlayer"/>), <c>--zoom &lt;m&gt;</c>
/// (start zoom, clamped to the camera limits; for perf runs), <c>--no-hud</c> (hide the HUD), <c>--debug-overlay</c>
/// (start with the F12 debug overlay on, e.g. for a screenshot), <c>--mute</c> (mute the master audio bus), <c>--bench &lt;seconds&gt;</c>
/// (run the scripted benchmark, print one <c>bench:</c> line and quit; positive seconds up to <see cref="MaxBenchSeconds"/>), <c>--vsync on|off</c>
/// (window vsync; default is the project setting, on), <c>--forests &lt;n&gt;</c> and <c>--mines &lt;n&gt;</c>
/// (resource groups on the map, 0 to <see cref="MapGenParams.MaxResourceGroups"/>, defaults
/// <see cref="DefaultForests"/> and <see cref="DefaultMines"/>). Bad values are
/// warned about and ignored; a token starting with <c>--</c> is never taken as a value (BUG-0041).
/// </remarks>
public sealed class LaunchOptions
{
    /// <summary>Units each player spawns at match start without <c>--units</c>.</summary>
    public const int DefaultUnitsPerPlayer = 100;

    /// <summary>Most units per player <c>--units</c> accepts (two players fill the 2,000-slot store).</summary>
    public const int MaxUnitsPerPlayer = 1000;

    /// <summary>Longest <c>--bench</c> run accepted, in seconds (an hour; BUG-0103: a huge finite value never ended).</summary>
    public const double MaxBenchSeconds = 3600;

    /// <summary>Forests on the match map without <c>--forests</c> (Producer default for the 128 map, M2-3b).</summary>
    public const int DefaultForests = 12;

    /// <summary>Gold mines on the match map without <c>--mines</c> (Producer default for the 128 map, M2-3b).</summary>
    public const int DefaultMines = 8;

    /// <summary>Forests the map generator places.</summary>
    public int Forests { get; private set; } = DefaultForests;

    /// <summary>Gold mines the map generator places.</summary>
    public int Mines { get; private set; } = DefaultMines;

    /// <summary>Sim seed override, or null for the scene's value.</summary>
    public ulong? Seed { get; private set; }

    /// <summary>Game speed override, or null for 1x.</summary>
    public double? Speed { get; private set; }

    /// <summary>Where to save a screenshot before quitting, or null for a normal run.</summary>
    public string? ScreenshotPath { get; private set; }

    /// <summary>Seconds of real time to wait before taking the screenshot.</summary>
    public double ScreenshotAfter { get; private set; } = 2.0;

    /// <summary>Units each player spawns at match start.</summary>
    public int UnitsPerPlayer { get; private set; } = DefaultUnitsPerPlayer;

    /// <summary>Start zoom override in meters, or null for the default.</summary>
    public float? Zoom { get; private set; }

    /// <summary>True to hide the HUD (clean screenshots, perf comparisons).</summary>
    public bool NoHud { get; private set; }

    /// <summary>True to start with the debug overlay (nav grid, flow arrows, tick graph) on.</summary>
    public bool DebugOverlay { get; private set; }

    /// <summary>True to mute the master audio bus (<c>--mute</c>; takes no value).</summary>
    public bool Mute { get; private set; }

    /// <summary>Seconds of scripted benchmark to run after the match starts (<c>--bench</c>), or null for a normal run.</summary>
    public double? BenchSeconds { get; private set; }

    /// <summary>Vsync override (<c>--vsync on|off</c>), or null to keep the project setting.</summary>
    public bool? Vsync { get; private set; }

    /// <summary>Parses <see cref="OS.GetCmdlineUserArgs"/>.</summary>
    public static LaunchOptions FromCommandLine() => Parse(OS.GetCmdlineUserArgs());

    /// <summary>Parses a user-arg list.</summary>
    public static LaunchOptions Parse(string[] args)
    {
        var o = new LaunchOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string flag = args[i];
            string? value = i + 1 < args.Length ? args[i + 1] : null;
            // A missing value must not swallow the next flag (BUG-0041): leave it for the next pass.
            bool hasValue = value != null && !value.StartsWith("--", StringComparison.Ordinal);
            if (!hasValue) value = null;
            switch (flag)
            {
                case "--seed":
                    if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong seed)) o.Seed = seed;
                    else Warn(flag, value);
                    break;
                case "--speed":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double speed) && speed > 0) o.Speed = speed;
                    else Warn(flag, value);
                    break;
                case "--screenshot":
                    if (!string.IsNullOrWhiteSpace(value)) o.ScreenshotPath = value;
                    else Warn(flag, value);
                    break;
                case "--screenshot-after":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double after) && after >= 0) o.ScreenshotAfter = after;
                    else Warn(flag, value);
                    break;
                case "--units":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int units) && units >= 0 && units <= MaxUnitsPerPlayer) o.UnitsPerPlayer = units;
                    else Warn(flag, value);
                    break;
                case "--forests":
                    if (TryGroups(value, out int forests)) o.Forests = forests;
                    else Warn(flag, value);
                    break;
                case "--mines":
                    if (TryGroups(value, out int mines)) o.Mines = mines;
                    else Warn(flag, value);
                    break;
                case "--zoom":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float zoom) && float.IsFinite(zoom)) o.Zoom = zoom;
                    else Warn(flag, value);
                    break;
                case "--bench":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bench) && bench > 0 && bench <= MaxBenchSeconds) o.BenchSeconds = bench;
                    else Warn(flag, value);
                    break;
                case "--vsync":
                    if (value is "on" or "off") o.Vsync = value == "on";
                    else Warn(flag, value);
                    break;
                case "--no-hud":
                    o.NoHud = true;
                    continue; // takes no value
                case "--debug-overlay":
                    o.DebugOverlay = true;
                    continue; // takes no value
                case "--mute":
                    o.Mute = true;
                    continue; // takes no value
                default:
                    continue;
            }
            if (hasValue) i++;
        }
        return o;
    }

    private static bool TryGroups(string? value, out int n) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 0 && n <= MapGenParams.MaxResourceGroups;

    private static void Warn(string flag, string? value) =>
        GD.PushWarning($"Ignoring {flag} with bad or missing value '{value}'.");
}
