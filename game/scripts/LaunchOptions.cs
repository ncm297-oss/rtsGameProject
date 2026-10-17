using System;
using System.Globalization;
using Godot;
using Rts.Sim.Data;
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
/// <see cref="DefaultForests"/> and <see cref="DefaultMines"/>), <c>--workers &lt;n&gt;</c> (starting workers per player
/// beside each Town Hall, 0 to <see cref="MaxWorkers"/>; default <c>rules.json</c> <c>startingWorkers</c>, M3-V1),
/// <c>--no-bases</c> (dev / tests: no Town Halls and no starting workers, the M2 armies-only match), <c>--no-combat</c>
/// (dev / tests: <c>SimConfig.Combat = false</c>, units never fight; M4-V1, never a game option), <c>--no-fog</c> (dev / tests:
/// the view draws no fog and shows every unit and building; the sim's fog and combat's sight rules are unchanged; M4-V4,
/// never a game option), <c>--map-size 128|192|256</c> (square map side in cells, <see cref="MapSizes"/>; default the 128 map; M6-V1),
/// <c>--skirmish</c> (takes no value: start the menu's default match at once, skipping the main menu; M6-V1; since M6-V3 a Town
/// Hall and the starting workers a side and no army, and slot 2 an AI seat of difficulty <see cref="DefaultAi"/>, like the menu's
/// Start, unless <c>--units</c> / <c>--ai</c> are also given), <c>--players 2|3|4</c> (the match's seats, one per slot; default 2;
/// M6-V5), <c>--faction &lt;slot&gt; &lt;id&gt;</c>
/// (slot 1 to <see cref="SlotCount"/> = player 0 to 3 plays the faction with data id <c>id</c>, e.g. <c>--faction 2 whirlwind</c>; an id the data
/// lacks is warned about by <see cref="Match"/> and ignored; M6-V2) and <c>--ai easy|normal|hard</c> (slot 2 is an AI seat of that
/// difficulty; without it slot 2 is human, as before, except under <c>--skirmish</c>; M6-V2) or <c>--ai &lt;slot&gt; easy|normal|hard</c>
/// (slot 2 to <see cref="SlotCount"/>; slot 1 is always the local human; M6-V5) and <c>--colour &lt;slot&gt; &lt;n&gt;</c> (slot 1 to
/// <see cref="SlotCount"/> is drawn in player colour n, 1 to <see cref="UiText.ColourCount"/>, of <c>ui.json</c>'s table; without it
/// slot s takes colour s; M6-V3; a slot on a colour an earlier slot holds keeps its default, or the first free colour, M6-VH1, M6-V5). A slot past
/// the match's players is warned about and unused. Any of these flags
/// on the command line skips the main menu (<see cref="HasMatchFlag"/>), so every dev and test run starts its match as
/// before M6-V1. Bad values are warned about and ignored; a token starting with <c>--</c> is never taken as a value (BUG-0041).
/// </remarks>
public sealed class LaunchOptions
{
    /// <summary>Units each player spawns at match start without <c>--units</c>.</summary>
    public const int DefaultUnitsPerPlayer = 100;

    /// <summary>Most units per player <c>--units</c> accepts (two players fill the 2,000-slot store).</summary>
    public const int MaxUnitsPerPlayer = 1000;

    /// <summary>Longest <c>--bench</c> run accepted, in seconds (an hour; BUG-0103: a huge finite value never ended).</summary>
    public const double MaxBenchSeconds = 3600;

    /// <summary>Most starting workers per player <c>--workers</c> accepts (the CLI's <c>run --workers</c> bound).</summary>
    public const int MaxWorkers = 200;

    /// <summary>Forests on the match map without <c>--forests</c> (Producer default for the 128 map, M2-3b).</summary>
    public const int DefaultForests = 12;

    /// <summary>Gold mines on the match map without <c>--mines</c> (Producer default for the 128 map, M2-3b).</summary>
    public const int DefaultMines = 8;

    /// <summary>The skirmish setup's default difficulty for the AI slot, and slot 2's seat under <c>--skirmish</c> without a valid <c>--ai</c> (M6-V3, BUG-0630).</summary>
    public const AiDifficulty DefaultAi = AiDifficulty.Normal;

    /// <summary>The map sides <c>--map-size</c> and the skirmish setup offer, in cells (M6-V1).</summary>
    public static readonly int[] MapSizes = { 128, 192, 256 };

    /// <summary>The flags that start a match (each also skips the main menu, <see cref="HasMatchFlag"/>): every flag <see cref="Parse"/> reads.</summary>
    public static readonly string[] MatchFlags =
    {
        "--seed", "--speed", "--screenshot", "--screenshot-after", "--units", "--workers", "--forests", "--mines", "--zoom", "--bench",
        "--vsync", "--no-hud", "--debug-overlay", "--mute", "--no-bases", "--no-combat", "--no-fog", "--map-size", "--skirmish",
        "--faction", "--ai", "--colour", "--players",
    };

    /// <summary>Whether <paramref name="args"/> holds any of <see cref="MatchFlags"/>: the boot then starts the match at once instead of showing the main menu (M6-V1).</summary>
    public static bool HasMatchFlag(string[] args)
    {
        foreach (string a in args)
            if (Array.IndexOf(MatchFlags, a) >= 0) return true;
        return false;
    }

    /// <summary>Slots the flags accept (M6-V5: the generator's most start locations): 1 is player 0, the local player; slot s is player s - 1.</summary>
    public const int SlotCount = MapGenParams.MaxStartLocations;

    /// <summary>Fewest players <c>--players</c> accepts (and the match's default).</summary>
    public const int MinPlayers = 2;

    private readonly string?[] _factionIds = new string?[SlotCount];
    private readonly int?[] _colours = new int?[SlotCount];
    private readonly AiDifficulty?[] _ai = new AiDifficulty?[SlotCount];

    /// <summary>The faction data id <c>--faction &lt;slot&gt; &lt;id&gt;</c> picked for slot <paramref name="slot"/> (0-based: player 0 to 3), or null for the default (player p plays faction <c>p mod factions</c>).</summary>
    public string? FactionId(int slot) => (uint)slot < (uint)SlotCount ? _factionIds[slot] : null;

    /// <summary>The player colour <c>--colour &lt;slot&gt; &lt;n&gt;</c> picked for slot <paramref name="slot"/> (0-based), as a 0-based index into <c>ui.json</c>'s colour table (<see cref="UiText.ColourRgb"/>), or null for the default (slot s: colour s).</summary>
    public int? ColourIndex(int slot) => (uint)slot < (uint)SlotCount ? _colours[slot] : null;

    /// <summary>Player 1's AI difficulty (<c>--ai</c>; <see cref="DefaultAi"/> under <c>--skirmish</c>), or null when slot 2 is a human seat (every dev run and test before M6-V2).</summary>
    public AiDifficulty? Ai => _ai[1];

    /// <summary>Slot <paramref name="slot"/>'s (0-based) AI difficulty (<c>--ai</c>; <see cref="DefaultAi"/> for each of the match's slots past the first under <c>--skirmish</c>), or null for a human seat; always null for slot 0, the local player (M6-V5).</summary>
    public AiDifficulty? AiOf(int slot) => (uint)slot < (uint)SlotCount ? _ai[slot] : null;

    /// <summary>The match's players (<c>--players</c>, <see cref="MinPlayers"/> to <see cref="SlotCount"/>), or null for the runner's own count (2; M6-V5).</summary>
    public int? Players { get; private set; }

    /// <summary>Square map side in cells (<c>--map-size</c>, one of <see cref="MapSizes"/>), or null for the default 128 map.</summary>
    public int? MapSize { get; private set; }

    /// <summary>True for <c>--skirmish</c> (takes no value): the default match without the main menu.</summary>
    public bool Skirmish { get; private set; }

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

    /// <summary>Starting workers per player (<c>--workers</c>), or null for <c>rules.json</c> <c>startingWorkers</c>.</summary>
    public int? Workers { get; private set; }

    /// <summary>True to start without Town Halls and workers (<c>--no-bases</c>; takes no value): the armies-only match the M2 test scenes were written for.</summary>
    public bool NoBases { get; private set; }

    /// <summary>
    /// True to run the match with <c>SimConfig.Combat</c> off (<c>--no-combat</c>; takes no value; M4-V1, BUG-0147): units never
    /// scan, swing or die, so the M2 scenes written for a world without fights keep their assertions. A dev / test flag, never a
    /// game option.
    /// </summary>
    public bool NoCombat { get; private set; }

    /// <summary>
    /// True to draw the match without the fog of war (<c>--no-fog</c>; takes no value; M4-V4): the terrain and props clear
    /// everywhere, every unit, building, shot and minimap dot shown, as before M4-V4. View only: the sim's fog and the
    /// sight rules combat and orders follow are unchanged. A dev / test flag for the scenes that look at the whole map, never
    /// a game option.
    /// </summary>
    public bool NoFog { get; private set; }

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
        bool unitsGiven = false;
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
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int units) && units >= 0 && units <= MaxUnitsPerPlayer)
                    {
                        o.UnitsPerPlayer = units;
                        unitsGiven = true;
                    }
                    else Warn(flag, value);
                    break;
                case "--workers":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int workers) && workers >= 0 && workers <= MaxWorkers) o.Workers = workers;
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
                case "--map-size":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) && Array.IndexOf(MapSizes, size) >= 0) o.MapSize = size;
                    else Warn(flag, value);
                    break;
                case "--players":
                    // One digit, MinPlayers to SlotCount ("02" or "+3" are junk, as --colour's).
                    if (value is { Length: 1 } && value[0] >= '0' + MinPlayers && value[0] <= '0' + SlotCount) o.Players = value[0] - '0';
                    else Warn(flag, value);
                    break;
                case "--ai":
                    if (value is { Length: 1 } && char.IsAsciiDigit(value[0]))
                    {
                        // M6-V5: two values, a slot (2 to SlotCount: slot 1 is the local human) then the difficulty; neither a flag (BUG-0041).
                        string? id = SecondValue(args, i, hasValue);
                        int slot = Slot(value), d = id == null ? -1 : DataLimits.AiDifficultyIds.IndexOf(id);
                        if (slot >= 1 && d >= 0) o._ai[slot] = (AiDifficulty)d;
                        else Warn(flag, id == null ? value : $"{value} {id}");
                        if (id != null) i++;
                    }
                    else
                    {
                        // One value, the difficulty: slot 2 (M6-V2's form, which every scene and the smoke's --skirmish use).
                        int difficulty = value == null ? -1 : DataLimits.AiDifficultyIds.IndexOf(value);
                        if (difficulty >= 0) o._ai[1] = (AiDifficulty)difficulty;
                        else Warn(flag, value);
                    }
                    break;
                case "--faction":
                {
                    // Two values: the slot, then the faction id. Neither may be a flag (BUG-0041); whatever values are there are consumed.
                    string? id = SecondValue(args, i, hasValue);
                    int slot = Slot(value);
                    if (slot >= 0 && id != null && IsId(id)) o._factionIds[slot] = id;
                    else Warn(flag, id == null ? value : $"{value} {id}");
                    if (id != null) i++;
                    break;
                }
                case "--colour":
                {
                    // Two values, the slot then the colour (1-based, one digit: "02" or "+1" are junk); neither may be a flag (BUG-0041).
                    string? n = SecondValue(args, i, hasValue);
                    int slot = Slot(value);
                    if (slot >= 0 && n is { Length: 1 } && n[0] >= '1' && n[0] < '1' + UiText.ColourCount) o._colours[slot] = n[0] - '1';
                    else Warn(flag, n == null ? value : $"{value} {n}");
                    if (n != null) i++;
                    break;
                }
                case "--skirmish":
                    o.Skirmish = true;
                    continue; // takes no value
                case "--no-hud":
                    o.NoHud = true;
                    continue; // takes no value
                case "--debug-overlay":
                    o.DebugOverlay = true;
                    continue; // takes no value
                case "--mute":
                    o.Mute = true;
                    continue; // takes no value
                case "--no-bases":
                    o.NoBases = true;
                    continue; // takes no value
                case "--no-combat":
                    o.NoCombat = true;
                    continue; // takes no value
                case "--no-fog":
                    o.NoFog = true;
                    continue; // takes no value
                default:
                    continue;
            }
            if (hasValue) i++;
        }
        int players = o.Players ?? MinPlayers;
        // M6-V3 (BUG-0602 item 2, BUG-0630): --skirmish is the menu's Start with nothing changed: a hall and workers a side with
        // no dev army, and every slot past the first an AI seat of the setup's default difficulty (M6-V5: each of the match's
        // slots). An explicit, valid --units / --ai still wins.
        if (o.Skirmish && !unitsGiven) o.UnitsPerPlayer = 0;
        for (int s = 1; s < players && o.Skirmish; s++) o._ai[s] ??= DefaultAi;
        for (int s = players; s < SlotCount; s++)
            if (o._factionIds[s] != null || o._colours[s] != null || o._ai[s] != null)
                GD.PushWarning($"Ignoring slot {s + 1}'s --faction / --colour / --ai: the match has {players} players (--players).");
        // Colours are unique per match (docs/02 "Game flow" 2, BUG-0631 item 3), slot by slot: one on a colour an earlier slot
        // holds is warned about and keeps its default (slot s: colour s), or takes the first free colour when an earlier slot
        // holds that too. Slots past the match are moved the same way, unwarned (they are unused).
        Span<bool> held = stackalloc bool[UiText.ColourCount];
        for (int s = 0; s < SlotCount; s++)
        {
            int c = o._colours[s] ?? s % UiText.ColourCount;
            if (held[c])
            {
                int own = s % UiText.ColourCount;
                int moved = held[own] ? FirstFreeColour(held) : own;
                o._colours[s] = moved == own ? null : moved;
                if (s < players)
                    GD.PushWarning($"Ignoring --colour: slot {s + 1} on colour {c + 1}, which an earlier slot holds; slot {s + 1} takes colour {moved + 1} (colours are unique per match).");
                c = moved;
            }
            held[c] = true;
        }
        return o;
    }

    /// <summary>The first colour (0-based) of <c>ui.json</c>'s table other than <paramref name="taken"/>: where a pick that collides with the other slot's moves.</summary>
    public static int FirstFreeColour(int taken) => taken == 0 ? 1 : 0;

    /// <summary>The first colour (0-based) of <c>ui.json</c>'s table not marked in <paramref name="held"/> (M6-V5: where a pick that collides with another slot's moves); 0 if every one is held.</summary>
    public static int FirstFreeColour(ReadOnlySpan<bool> held)
    {
        for (int c = 0; c < held.Length; c++)
            if (!held[c]) return c;
        return 0;
    }

    // The 0-based slot a "1" .. "4" names, else -1 ("01", "+1", "5" are junk).
    private static int Slot(string? value) => value is { Length: 1 } && value[0] >= '1' && value[0] < '1' + SlotCount ? value[0] - '1' : -1;

    // A two-value flag's second value: the token after the first, unless the first is missing or either is a flag (BUG-0041).
    private static string? SecondValue(string[] args, int i, bool hasValue) =>
        hasValue && i + 2 < args.Length && !args[i + 2].StartsWith("--", StringComparison.Ordinal) ? args[i + 2] : null;

    // A data id's shape (snake_case: lower-case letters, digits, '_'); whether the data has it is Match's check.
    private static bool IsId(string id)
    {
        foreach (char c in id)
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')) return false;
        return id.Length > 0;
    }

    private static bool TryGroups(string? value, out int n) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 0 && n <= MapGenParams.MaxResourceGroups;

    private static void Warn(string flag, string? value) =>
        GD.PushWarning($"Ignoring {flag} with bad or missing value '{value}'.");
}
