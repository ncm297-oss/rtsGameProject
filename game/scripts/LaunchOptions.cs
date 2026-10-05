using System.Globalization;
using Godot;

namespace Rts.Game;

/// <summary>Debug options from the user args after <c>--</c> on the Godot command line.</summary>
/// <remarks>
/// <c>--seed &lt;n&gt;</c>, <c>--speed &lt;x&gt;</c>, <c>--screenshot &lt;path&gt;</c>,
/// <c>--screenshot-after &lt;seconds&gt;</c> (default 2). Bad values are warned about and ignored.
/// </remarks>
public sealed class LaunchOptions
{
    /// <summary>Sim seed override, or null for the scene's value.</summary>
    public ulong? Seed { get; private set; }

    /// <summary>Game speed override, or null for 1x.</summary>
    public double? Speed { get; private set; }

    /// <summary>Where to save a screenshot before quitting, or null for a normal run.</summary>
    public string? ScreenshotPath { get; private set; }

    /// <summary>Seconds of real time to wait before taking the screenshot.</summary>
    public double ScreenshotAfter { get; private set; } = 2.0;

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
            switch (flag)
            {
                case "--seed":
                    if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong seed)) o.Seed = seed;
                    else Warn(flag, value);
                    i++;
                    break;
                case "--speed":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double speed) && speed > 0) o.Speed = speed;
                    else Warn(flag, value);
                    i++;
                    break;
                case "--screenshot":
                    if (!string.IsNullOrWhiteSpace(value)) o.ScreenshotPath = value;
                    else Warn(flag, value);
                    i++;
                    break;
                case "--screenshot-after":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double after) && after >= 0) o.ScreenshotAfter = after;
                    else Warn(flag, value);
                    i++;
                    break;
            }
        }
        return o;
    }

    private static void Warn(string flag, string? value) =>
        GD.PushWarning($"Ignoring {flag} with bad or missing value '{value}'.");
}
