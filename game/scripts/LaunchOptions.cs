using System;
using System.Globalization;
using Godot;

namespace Rts.Game;

/// <summary>Debug options from the user args after <c>--</c> on the Godot command line.</summary>
/// <remarks>
/// <c>--seed &lt;n&gt;</c>, <c>--speed &lt;x&gt;</c>, <c>--screenshot &lt;path&gt;</c>,
/// <c>--screenshot-after &lt;seconds&gt;</c> (default 2), <c>--units &lt;n&gt;</c> (per player, 0 to
/// <see cref="MaxUnitsPerPlayer"/>, default <see cref="DefaultUnitsPerPlayer"/>), <c>--zoom &lt;m&gt;</c>
/// (start zoom, clamped to the camera limits; for perf runs), <c>--no-hud</c> (hide the HUD). Bad values are
/// warned about and ignored; a token starting with <c>--</c> is never taken as a value (BUG-0041).
/// </remarks>
public sealed class LaunchOptions
{
    /// <summary>Units each player spawns at match start without <c>--units</c>.</summary>
    public const int DefaultUnitsPerPlayer = 100;

    /// <summary>Most units per player <c>--units</c> accepts (two players fill the 2,000-slot store).</summary>
    public const int MaxUnitsPerPlayer = 1000;

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
                case "--zoom":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float zoom) && float.IsFinite(zoom)) o.Zoom = zoom;
                    else Warn(flag, value);
                    break;
                case "--no-hud":
                    o.NoHud = true;
                    continue; // takes no value
                default:
                    continue;
            }
            if (hasValue) i++;
        }
        return o;
    }

    private static void Warn(string flag, string? value) =>
        GD.PushWarning($"Ignoring {flag} with bad or missing value '{value}'.");
}
