using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V1: the game shell, through the boot scene the game runs (<c>Main.tscn</c> with no user args) and injected input on
/// the viewport. Main menu -> Skirmish -> setup (Random, then seed 7 typed, a letter refused, map 192) -> Start: a match on
/// seed 7 and a 192 x 192 map that ticks. Esc with A armed cancels the targeting and does not pause; Esc then pauses: the
/// tree is paused, no tick advances over half a second, and a click and the A key reach nothing; Esc resumes and ticks move.
/// Pause -> Quit to menu: no match, sim or view node left anywhere in the tree, the tree unpaused, the menu up; Skirmish
/// again keeps seed 7 and 192 and starts a fresh match that ticks. Also: the boot rule (no user args show the menu, any
/// match flag starts the match: <see cref="LaunchOptions.HasMatchFlag"/>), the <c>--map-size</c> and <c>--skirmish</c>
/// parse, and the new <c>ui.json</c> sections (every key present, each missing key one error naming it).
/// M6-V2: the <c>--faction</c> / <c>--ai</c> parse and <see cref="SimRunner.BuildSeats"/> rows; the setup's live faction and
/// difficulty lists (data and <c>ui.json</c> text only, BUG-0581 item 1); the picks (sides swapped, Easy) kept over Back and
/// Quit to menu, and each match's seats equal to them.
/// M6-V3: the menu's match (and <c>--skirmish</c>, slot 2 an AI too, BUG-0630) is a Town Hall and the starting workers a side, no army (<c>--units 0</c>,
/// BUG-0604), the camera on the local hall; each slot's colour list (<c>ui.json</c> names, defaults colour 1 / 2), the picks
/// kept over Back and Quit, and the match drawn in them (<c>Match.PlayerRgb</c>, unit and building materials, minimap dots);
/// the <c>--colour</c> parse and its junk; each missing <c>setup.colour_*</c> key one error naming it; BUG-0602 item 1.
/// M6-VH1: the corpse legibility rules over the whole colour table (8 fills, 28 pairs; BUG-0632); colours unique per match
/// (each list greys the other's colour, a colliding pick moves to the first free one, <c>--colour</c> on one colour twice keeps
/// slot 2's default; BUG-0631 item 3).
/// M6-V6: the <c>menu.settings</c>, <c>pause.settings</c> and <c>settings.*</c> keys (each missing one an error naming it, a
/// bad <c>settings.resolution_item</c> refused); <c>--vsync off</c> with a settings file saying on: off for the run, the file
/// unchanged. The settings screen itself is <c>SettingsTest</c>'s.
/// M6-V4: the 18 <c>match_over.*</c> keys, each required by name (a missing one, or the section, one error naming it), and
/// bad <c>clock</c> / <c>match_length</c> formats refused. The banner and stats screen are <c>MatchOverViewTest</c>'s.
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/ShellTest.tscn</c>; prints "SHELL TEST PASS" and exits 0,
/// else "SHELL TEST FAIL: ..." lines and exits 1. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves <c>shell-menu.png</c>,
/// <c>shell-setup.png</c>, <c>shell-match.png</c> and <c>shell-pause.png</c>.
/// </remarks>
public partial class ShellTest : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Main _main = null!;
    private string? _shots;

    public override async void _Ready()
    {
        // The shell pauses the tree; the test must keep running through it.
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shots = args[i + 1];
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            LaunchRows();
            UiRows();
            await Flow();
            await SkirmishBoot();
            await VsyncOverride();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"SHELL TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("SHELL TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- rows ----

    private void LaunchRows()
    {
        Check(!LaunchOptions.HasMatchFlag(Array.Empty<string>()), "no args counted as a match flag");
        Check(!LaunchOptions.HasMatchFlag(new[] { "--shots", "x" }), "an unknown flag counted as a match flag");
        foreach (string flag in new[] { "--seed", "--units", "--screenshot", "--bench", "--no-fog", "--mute", "--skirmish", "--map-size" })
            Check(LaunchOptions.HasMatchFlag(new[] { flag, "1" }), $"{flag} not a match flag");
        // Every flag Parse reads skips the menu: a flag Parse knows but MatchFlags lacks would boot a dev run into the menu.
        string source = File.ReadAllText(ProjectSettings.GlobalizePath("res://scripts/LaunchOptions.cs"));
        foreach (string flag in source.Split("case \"").Skip(1).Select(s => s[..s.IndexOf('"')]))
            Check(LaunchOptions.MatchFlags.Contains(flag), $"Parse reads {flag}, which is not in MatchFlags");
        foreach (int size in LaunchOptions.MapSizes)
            Check(LaunchOptions.Parse(new[] { "--map-size", size.ToString() }).MapSize == size, $"--map-size {size} not read");
        Check(LaunchOptions.Parse(new[] { "--map-size", "100" }).MapSize == null, "--map-size 100 accepted");
        Check(LaunchOptions.Parse(new[] { "--map-size", "--skirmish" }) is { MapSize: null, Skirmish: true }, "--map-size swallowed --skirmish (BUG-0041)");
        Check(LaunchOptions.Parse(Array.Empty<string>()) is { MapSize: null, Skirmish: false, Seed: null, UnitsPerPlayer: LaunchOptions.DefaultUnitsPerPlayer, Ai: null },
            "defaults changed");

        // M6-V2: --faction <slot> <id> and --ai <difficulty>.
        Check(LaunchOptions.HasMatchFlag(new[] { "--faction", "2", "whirlwind" }) && LaunchOptions.HasMatchFlag(new[] { "--ai", "hard" }), "--faction / --ai not match flags");
        LaunchOptions none = LaunchOptions.Parse(Array.Empty<string>());
        Check(none.FactionId(0) == null && none.FactionId(1) == null && none.FactionId(2) == null && none.FactionId(-1) == null, "a faction picked with no flag");
        LaunchOptions both = LaunchOptions.Parse(new[] { "--faction", "1", "whirlwind", "--faction", "2", "malazan", "--ai", "easy", "--seed", "4" });
        Check(both.FactionId(0) == "whirlwind" && both.FactionId(1) == "malazan" && both.Ai == AiDifficulty.Easy && both.Seed == 4,
            $"--faction 1 whirlwind --faction 2 malazan --ai easy --seed 4: '{both.FactionId(0)}', '{both.FactionId(1)}', {both.Ai}, {both.Seed}");
        foreach ((string id, AiDifficulty want) in new[] { ("easy", AiDifficulty.Easy), ("normal", AiDifficulty.Normal), ("hard", AiDifficulty.Hard) })
            Check(LaunchOptions.Parse(new[] { "--ai", id }).Ai == want, $"--ai {id} not read");
        foreach (string[] junk in new[]
        {
            new[] { "--ai", "Hard" }, new[] { "--ai", "insane" }, new[] { "--ai", "1" }, new[] { "--ai" }, new[] { "--ai", "--seed", "3" },
        })
        {
            LaunchOptions o = LaunchOptions.Parse(junk);
            Check(o.Ai == null, $"'{string.Join(' ', junk)}' accepted as {o.Ai}");
        }
        Check(LaunchOptions.Parse(new[] { "--ai", "--seed", "3" }).Seed == 3, "--ai swallowed --seed (BUG-0041)");
        foreach (string[] junk in new[]
        {
            new[] { "--faction", "3", "malazan" }, new[] { "--faction", "0", "malazan" }, new[] { "--faction", "whirlwind" }, new[] { "--faction", "2" },
            new[] { "--faction", "2", "Whirl wind!" }, new[] { "--faction", "2", "--ai", "hard" }, new[] { "--faction", "--seed", "3" }, new[] { "--faction" },
        })
        {
            LaunchOptions o = LaunchOptions.Parse(junk);
            Check(o.FactionId(0) == null && o.FactionId(1) == null, $"'{string.Join(' ', junk)}' picked '{o.FactionId(0)}', '{o.FactionId(1)}'");
        }
        Check(LaunchOptions.Parse(new[] { "--faction", "2", "--ai", "hard" }).Ai == AiDifficulty.Hard, "--faction 2 swallowed --ai (BUG-0041)");
        Check(LaunchOptions.Parse(new[] { "--faction", "--seed", "3" }).Seed == 3, "--faction swallowed --seed (BUG-0041)");
        Check(LaunchOptions.Parse(new[] { "--faction", "2", "zzz", "--seed", "5" }) is { Seed: 5 } z && z.FactionId(1) == "zzz",
            "an id-shaped unknown faction is Match's to refuse (it needs the data), and must not eat the next flag");

        // M6-V3: --skirmish is the menu's match (no army unless --units says so); --colour <slot> <n>.
        Check(LaunchOptions.Parse(new[] { "--skirmish" }).UnitsPerPlayer == 0, "--skirmish spawns a dev army");
        Check(LaunchOptions.Parse(new[] { "--skirmish", "--units", "100" }).UnitsPerPlayer == 100
            && LaunchOptions.Parse(new[] { "--units", "7", "--skirmish" }).UnitsPerPlayer == 7, "--skirmish overrode an explicit --units");
        Check(LaunchOptions.Parse(new[] { "--skirmish", "--units", "junk" }).UnitsPerPlayer == 0, "--skirmish --units junk: not the menu's 0");
        // BUG-0630: --skirmish's slot 2 is the menu's AI seat (default difficulty) unless a valid --ai says otherwise; without it, human.
        Check(LaunchOptions.Parse(new[] { "--skirmish" }).Ai == LaunchOptions.DefaultAi, $"--skirmish slot 2: {LaunchOptions.Parse(new[] { "--skirmish" }).Ai?.ToString() ?? "human"}");
        Check(LaunchOptions.Parse(new[] { "--skirmish", "--ai", "hard" }).Ai == AiDifficulty.Hard
            && LaunchOptions.Parse(new[] { "--ai", "easy", "--skirmish" }).Ai == AiDifficulty.Easy, "--skirmish overrode an explicit --ai");
        Check(LaunchOptions.Parse(new[] { "--skirmish", "--ai", "junk" }).Ai == LaunchOptions.DefaultAi, "--skirmish --ai junk: not the menu's default");
        Check(LaunchOptions.Parse(new[] { "--seed", "3" }).Ai == null, "a dev run without --skirmish / --ai got an AI seat");
        Check(LaunchOptions.HasMatchFlag(new[] { "--colour", "1", "3" }), "--colour not a match flag");
        Check(none.ColourIndex(0) == null && none.ColourIndex(1) == null && none.ColourIndex(2) == null && none.ColourIndex(-1) == null, "a colour picked with no flag");
        LaunchOptions colours = LaunchOptions.Parse(new[] { "--colour", "1", "8", "--colour", "2", "1", "--seed", "4" });
        Check(colours.ColourIndex(0) == 7 && colours.ColourIndex(1) == 0 && colours.Seed == 4, $"--colour 1 8 --colour 2 1: {colours.ColourIndex(0)}, {colours.ColourIndex(1)}");
        Check(LaunchOptions.Parse(new[] { "--colour", "2", "3", "--colour", "2", "5" }).ColourIndex(1) == 4, "repeated --colour: the last good value does not win");
        Check(LaunchOptions.Parse(new[] { "--colour", "2", "3", "--colour", "2", "9" }).ColourIndex(1) == 2, "repeated --colour: junk replaced a good value");
        foreach (string[] junk in new[]
        {
            new[] { "--colour", "3", "1" }, new[] { "--colour", "0", "1" }, new[] { "--colour", "1", "0" }, new[] { "--colour", "1", "9" },
            new[] { "--colour", "1", "02" }, new[] { "--colour", "1", "+1" }, new[] { "--colour", "1", "red" }, new[] { "--colour", "1" },
            new[] { "--colour" }, new[] { "--colour", "1", "--seed", "3" }, new[] { "--colour", "01", "2" },
        })
        {
            LaunchOptions o = LaunchOptions.Parse(junk);
            Check(o.ColourIndex(0) == null && o.ColourIndex(1) == null, $"'{string.Join(' ', junk)}' picked {o.ColourIndex(0)}, {o.ColourIndex(1)}");
        }
        // A repeated flag as the value: the first is refused, the second read (slot 1 on colour 2, slot 2's default: slot 2 moves to colour 1).
        LaunchOptions twice = LaunchOptions.Parse(new[] { "--colour", "--colour", "1", "2" });
        Check(twice.ColourIndex(0) == 1 && twice.ColourIndex(1) == 0, $"'--colour --colour 1 2' picked {twice.ColourIndex(0)}, {twice.ColourIndex(1)}");
        // M6-VH1 (BUG-0631 item 3): colours are unique per match. Both slots on one colour: warned, slot 2 keeps its default
        // (colour 2), or takes the first free colour (colour 1) when slot 1 holds colour 2. (Effective colour: ColourIndex ?? slot.)
        foreach ((string[] args, int want0, int want1) in new[]
        {
            (new[] { "--colour", "1", "3", "--colour", "2", "3" }, 2, 1), (new[] { "--colour", "2", "1" }, 0, 1),
            (new[] { "--colour", "1", "2" }, 1, 0), (new[] { "--colour", "1", "2", "--colour", "2", "2" }, 1, 0),
            (new[] { "--colour", "2", "8", "--colour", "1", "8" }, 7, 1), (new[] { "--colour", "1", "4", "--colour", "2", "6" }, 3, 5),
            (new[] { "--colour", "1", "2", "--colour", "2", "1" }, 1, 0),
        })
        {
            LaunchOptions o = LaunchOptions.Parse(args);
            int got0 = o.ColourIndex(0) ?? 0, got1 = o.ColourIndex(1) ?? 1;
            Check(got0 == want0 && got1 == want1 && got0 != got1, $"'{string.Join(' ', args)}': colours {got0 + 1}, {got1 + 1}, want {want0 + 1}, {want1 + 1}");
        }
        Check(LaunchOptions.FirstFreeColour(0) == 1 && LaunchOptions.FirstFreeColour(1) == 0 && LaunchOptions.FirstFreeColour(7) == 0, "FirstFreeColour");
        Check(LaunchOptions.Parse(new[] { "--colour", "1", "--seed", "3" }).Seed == 3 && LaunchOptions.Parse(new[] { "--colour", "--seed", "3" }).Seed == 3,
            "--colour swallowed --seed (BUG-0041)");

        // SimRunner's seats: explicit, one per player, the old defaults without picks.
        int factions = _data.Factions.Length;
        var plain = SimRunner.BuildSeats(3, factions, new[] { -1, -1 }, null);
        Check(plain.Length == 3 && Enumerable.Range(0, 3).All(p => plain[p] == new PlayerSeat(p % factions, SeatController.Human, AiDifficulty.Normal)),
            $"default seats {string.Join(", ", plain)}");
        var picked = SimRunner.BuildSeats(3, factions, new[] { 1, 0 }, AiDifficulty.Hard);
        Check(picked[0] == new PlayerSeat(1, SeatController.Human, AiDifficulty.Normal) && picked[1] == new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard)
            && picked[2] == new PlayerSeat(2 % factions, SeatController.Human, AiDifficulty.Normal), $"picked seats {string.Join(", ", picked)}");
    }

    private void UiRows()
    {
        var keys = new List<(string Section, string Key, string Text)>();
        foreach (MenuText k in Enum.GetValues<MenuText>()) keys.Add(("menu", UiText.Key(k), _ui.Menu(k)));
        foreach (SetupText k in Enum.GetValues<SetupText>()) keys.Add(("setup", UiText.Key(k), _ui.Setup(k)));
        foreach (PauseText k in Enum.GetValues<PauseText>()) keys.Add(("pause", UiText.Key(k), _ui.Pause(k)));
        foreach (SettingsText k in Enum.GetValues<SettingsText>()) keys.Add(("settings", UiText.Key(k), _ui.Settings(k)));
        foreach (MatchOverText k in Enum.GetValues<MatchOverText>()) keys.Add(("match_over", UiText.Key(k), _ui.MatchOver(k)));
        Check(keys.Count == 4 + 15 + 4 + 22 + 18, $"{keys.Count} shell keys"); // settings: 22 since M6-V7 (confine_cursor)
        string json = File.ReadAllText(UiText.DefaultPath).Replace("\r\n", "\n");
        foreach ((string section, string key, string text) in keys)
        {
            Check(text.Length > 0, $"ui.json {section}.{key} is empty");
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root[section]!.AsObject().Remove(key);
            var errors = new List<string>();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 1 && errors[0].Contains($"{section}.{key}"),
                $"ui.json without {section}.{key}: [{string.Join("; ", errors)}]");
        }
        foreach (string section in new[] { "menu", "setup", "pause", "settings", "match_over" })
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root.Remove(section);
            var errors = new List<string>();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 1 && errors[0].Contains(section), $"ui.json without {section}: [{string.Join("; ", errors)}]");
        }
        // The keys the brief names, by their ui.json spelling.
        string[] named = { "menu.title", "menu.skirmish", "menu.quit", "setup.title", "setup.seed", "setup.random", "setup.map_size", "setup.slot_human",
            "setup.slot_ai", "setup.faction", "setup.difficulty", "setup.start", "setup.back", "pause.title", "pause.resume", "pause.quit_to_menu",
            "setup.map_size_item", "setup.difficulty_easy", "setup.difficulty_normal", "setup.difficulty_hard", "setup.colour",
            // M6-V6: the settings screen's keys.
            "menu.settings", "pause.settings", "settings.title", "settings.resolution", "settings.resolution_item", "settings.window_mode",
            "settings.window_windowed", "settings.window_borderless", "settings.window_fullscreen", "settings.vsync", "settings.vsync_on",
            "settings.vsync_off", "settings.quality", "settings.quality_low", "settings.quality_medium", "settings.quality_high",
            "settings.master_volume", "settings.music_volume", "settings.sfx_volume", "settings.edge_pan_speed", "settings.apply",
            "settings.defaults", "settings.back",
            // M6-V4: the match-over banner's and the stats screen's keys.
            "match_over.victory", "match_over.defeat", "match_over.draw", "match_over.stats", "match_over.menu", "match_over.continue",
            "match_over.stats_title", "match_over.match_length", "match_over.clock", "match_over.player", "match_over.kills",
            "match_over.losses", "match_over.gold_in_hand", "match_over.wood_in_hand", "match_over.defeated_at", "match_over.standing",
            "match_over.human", "match_over.back" };
        foreach (string n in named) Check(keys.Any(k => $"{k.Section}.{k.Key}" == n), $"no key {n}");
        Check(!keys.Any(k => k.Key == "coming_soon"), "setup.coming_soon is still read (nothing shows it since M6-V2)");

        // M6-V4: the two match_over formats; one that can't format (or names nothing) is one error naming its key.
        Check(_ui.Clock(0) != _ui.Clock(20 * 61) && _ui.Clock(20 * 61).Contains('1') && _ui.MatchLength(20 * 75).Contains(_ui.Clock(20 * 75)),
            $"clock '{_ui.Clock(20 * 61)}', length '{_ui.MatchLength(20 * 75)}'");
        foreach ((string key, string bad) in new[] { ("clock", "{0}"), ("clock", "{1:00}"), ("clock", "{0}:{2}"), ("clock", "{{0}}:{{1}}"),
            ("match_length", "Length"), ("match_length", "{1}"), ("match_length", "{{0}}") })
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root["match_over"]![key] = bad;
            var errors = new List<string>();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 1 && errors[0].Contains($"match_over.{key}"), $"match_over.{key} '{bad}': [{string.Join("; ", errors)}]");
        }

        // BUG-0581 item 1: the map size item is a ui.json format; one that can't format the size is one error naming the key.
        Check(_ui.MapSizeItem(192).Contains("192"), $"map size item '{_ui.MapSizeItem(192)}'");
        // BUG-0602 item 1: an escaped "{{0}}" holds the text "{0}" but names no size.
        foreach (string bad in new[] { "{1} x {1}", "size", "{0", "{0} {x}", "{{0}}", "{{0}} x {{0}}" })
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root["setup"]!["map_size_item"] = bad;
            var errors = new List<string>();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 1 && errors[0].Contains("setup.map_size_item"),
                $"map_size_item '{bad}': [{string.Join("; ", errors)}]");
        }
        Check(UiText.Parse(json.Replace("\"{0} × {0}\"", "\"{{0}} × {0}\""), new List<string>()) is UiText partial && partial.MapSizeItem(128) == "{0} × 128",
            "a format naming the size once, beside a literal '{0}', was refused");

        // M6-V6: the resolution item names the width and the height; anything else is one error naming the key.
        Check(_ui.ResolutionItem(1920, 1080).Contains("1920") && _ui.ResolutionItem(1920, 1080).Contains("1080"), $"resolution item '{_ui.ResolutionItem(1920, 1080)}'");
        foreach (string bad in new[] { "{0} x {0}", "{1}", "{0} x {2}", "{0", "{{0}} x {{1}}", "size" })
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root["settings"]!["resolution_item"] = bad;
            var errors = new List<string>();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 1 && errors[0].Contains("settings.resolution_item"),
                $"resolution_item '{bad}': [{string.Join("; ", errors)}]");
        }

        // M6-V3: the colour table, setup.colour_1 .. colour_8 ({ displayName, rgb }); each missing key or field one error naming it.
        for (int c = 0; c < UiText.ColourCount; c++)
        {
            Check(_ui.ColourName(c).Length > 0, $"colour {c} has no name");
            Check(UiText.ColourKey(c) == $"colour_{c + 1}", $"colour key {UiText.ColourKey(c)}");
            foreach (string field in new[] { "", "displayName", "rgb" })
            {
                var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
                if (field.Length == 0) root["setup"]!.AsObject().Remove(UiText.ColourKey(c));
                else root["setup"]![UiText.ColourKey(c)]!.AsObject().Remove(field);
                string at = field.Length == 0 ? $"setup.{UiText.ColourKey(c)}" : $"setup.{UiText.ColourKey(c)}.{field}";
                var errors = new List<string>();
                Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 1 && errors[0].Contains(at), $"ui.json without {at}: [{string.Join("; ", errors)}]");
            }
        }
        var rgbs = Enumerable.Range(0, UiText.ColourCount).Select(_ui.ColourRgb).ToArray();
        Check(rgbs.Distinct().Count() == UiText.ColourCount && Enumerable.Range(0, UiText.ColourCount).Select(_ui.ColourName).Distinct().Count() == UiText.ColourCount,
            $"two colours alike in the table: {string.Join(", ", rgbs.Select(c => c.ToString("X6")))}");
        // BUG-0431's rule carried to the table: an enemy zone's rim is its owner's colour, so every colour stays far from the own rim.
        foreach (uint rgb in rgbs)
        {
            Color c = UnitViews.ColorFromRgb(rgb), own = AbilityViews.OwnZoneRimColor;
            float gap = Math.Max(Math.Abs(c.R - own.R), Math.Max(Math.Abs(c.G - own.G), Math.Abs(c.B - own.B)));
            Check(gap >= 0.4f, $"colour {rgb:X6} is only {gap:0.000} from the own zone rim");
        }
        CorpseRulesOverTable(rgbs);
        foreach (string bad in new[] { "red", "#12345", "#1234567", "#GG0000", "4B4F55A", "#-12345", "# 12345" })
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root["setup"]![UiText.ColourKey(2)]!["rgb"] = bad;
            var errors = new List<string>();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 1 && errors[0].Contains("setup.colour_3.rgb"), $"rgb '{bad}': [{string.Join("; ", errors)}]");
        }
        // BUG-0631 item 1: two entries with one rgb (either case) are one error naming both keys; three alike, two errors.
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root["setup"]![UiText.ColourKey(6)]!["rgb"] = $"#{_ui.ColourRgb(1):x6}";
            var errors = new List<string>();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 1 && errors[0].Contains("setup.colour_2.rgb") && errors[0].Contains("setup.colour_7.rgb")
                && errors[0].Contains($"#{_ui.ColourRgb(1):X6}"), // the value as the data writes it, with its '#' (BUG-0631 item 1's nit)
                $"colours 2 and 7 alike: [{string.Join("; ", errors)}]");
            root["setup"]![UiText.ColourKey(0)]!["rgb"] = $"#{_ui.ColourRgb(1):X6}";
            errors.Clear();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 2, $"colours 1, 2 and 7 alike: [{string.Join("; ", errors)}]");
        }
        // The table is read from the file (a renamed, recoloured entry shows the new name and value), never from C#.
        var renamedRoot = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        renamedRoot["setup"]![UiText.ColourKey(4)]!["displayName"] = "Zz Test";
        renamedRoot["setup"]![UiText.ColourKey(4)]!["rgb"] = "#01a2F3";
        UiText? renamed = UiText.Parse(renamedRoot.ToJsonString(), new List<string>());
        Check(renamed != null && renamed.ColourName(4) == "Zz Test" && renamed.ColourRgb(4) == 0x01A2F3u, "the colour table is not read from ui.json");
        foreach (string script in new[] { "SkirmishSetup.cs", "Match.cs", "UiText.cs", "LaunchOptions.cs" })
        {
            string source = File.ReadAllText(ProjectSettings.GlobalizePath($"res://scripts/{script}"));
            for (int c = 0; c < UiText.ColourCount; c++)
                Check(!source.Contains($"\"{_ui.ColourName(c)}\"", StringComparison.Ordinal) && !source.Contains($"{_ui.ColourRgb(c):X6}", StringComparison.OrdinalIgnoreCase),
                    $"{script} has colour {c + 1}'s name or value as a literal");
        }
    }

    // ---- the flow ----

    // M6-VH1 (BUG-0631 item 3): colours are unique per match. Each list greys only the colour the other slot holds; a pick of
    // the other's colour (through the API or the list's own ItemSelected, as a click sends) moves to the first free colour.
    // Leaves the defaults (colours 1 and 2) picked.
    private void UniqueColourPicks(SkirmishSetup setup)
    {
        Check(GreyedOnly(setup, 0, 1) && GreyedOnly(setup, 1, 0), "defaults: each colour list should grey only the other slot's colour");
        setup.PickColour(0, 5);
        Check(setup.ColourPick(0) == 5 && GreyedOnly(setup, 1, 5) && GreyedOnly(setup, 0, 1), $"slot 1 on colour 6: picks {setup.ColourPick(0)}, {setup.ColourPick(1)}");
        setup.PickColour(1, 5);
        Check(setup.ColourPick(1) == 0 && setup.ColourPick(0) == 5 && GreyedOnly(setup, 0, 0) && GreyedOnly(setup, 1, 5),
            $"slot 2 picking slot 1's colour 6: picks {setup.ColourPick(0)}, {setup.ColourPick(1)} (want 6, first free 1)");
        setup.PickColour(0, 0);
        Check(setup.ColourPick(0) == 1 && setup.ColourPick(1) == 0, $"slot 1 picking slot 2's colour 1: picks {setup.ColourPick(0)}, {setup.ColourPick(1)} (want first free 2, 1)");
        // The list's own signal (a popup click) on a taken colour.
        setup.ColourOptions[1].Select(1);
        setup.ColourOptions[1].EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        Check(setup.ColourPick(1) == 0 && setup.ColourPick(0) == 1, $"ItemSelected on the taken colour 2: picks {setup.ColourPick(0)}, {setup.ColourPick(1)}");
        setup.ColourOptions[1].Select(6);
        setup.ColourOptions[1].EmitSignal(OptionButton.SignalName.ItemSelected, 6L);
        Check(setup.ColourPick(1) == 6 && GreyedOnly(setup, 0, 6), $"ItemSelected on colour 7: picks {setup.ColourPick(0)}, {setup.ColourPick(1)}");
        string[] args = setup.MatchArgs()!;
        Check(string.Join(' ', args).Contains("--colour 1 2") && string.Join(' ', args).Contains("--colour 2 7"), $"MatchArgs [{string.Join(' ', args)}]");
        setup.PickColour(0, 0);
        setup.PickColour(1, 1);
        Check(setup.ColourPick(0) == 0 && setup.ColourPick(1) == 1 && GreyedOnly(setup, 0, 1) && GreyedOnly(setup, 1, 0), "back to the default colours");
    }

    // Whether `slot`'s colour list greys exactly the colour `taken`.
    private static bool GreyedOnly(SkirmishSetup setup, int slot, int taken)
    {
        OptionButton list = setup.ColourOptions[slot];
        for (int c = 0; c < list.ItemCount; c++)
            if (list.IsItemDisabled(c) != (c == taken)) return false;
        return list.ItemCount == UiText.ColourCount;
    }

    // BUG-0632: CombatViewTest's corpse rules (BUG-0160 item 3) over every colour the menu offers, through a real CombatViews
    // bound to the whole table: each fill (owner colour x CorpseShade) has luminance >= 0.2 and a darker rim, and each of the
    // 28 pairs of fills is >= 0.25 apart, so any two picks keep both sides' corpses legible.
    private void CorpseRulesOverTable(uint[] rgbs)
    {
        var combat = new CombatViews();
        AddChild(combat);
        combat.Bind(_data, 1, rgbs);
        int pairs = 0;
        for (int a = 0; a < rgbs.Length; a++)
        {
            Color fill = combat.CorpseColor(a), rim = combat.CorpseRimColor(a);
            float lum = 0.2126f * fill.R + 0.7152f * fill.G + 0.0722f * fill.B;
            Check(lum >= 0.2f, $"corpse rules: {UiText.ColourKey(a)} {rgbs[a]:X6}'s corpse fill reads black (luminance {lum:0.000})");
            Check(rim.R < fill.R && rim.G < fill.G && rim.B < fill.B, $"corpse rules: {UiText.ColourKey(a)}'s rim {rim} is not darker than its fill {fill}");
            for (int b = a + 1; b < rgbs.Length; b++)
            {
                Color other = combat.CorpseColor(b);
                float d = MathF.Sqrt((fill.R - other.R) * (fill.R - other.R) + (fill.G - other.G) * (fill.G - other.G) + (fill.B - other.B) * (fill.B - other.B));
                Check(d >= 0.25f, $"corpse rules: the fills of {UiText.ColourKey(a)} and {UiText.ColourKey(b)} are only {d:0.000} apart");
                pairs++;
            }
        }
        Check(pairs == 28, $"corpse rules: {pairs} pairs checked, want 28");
        RemoveChild(combat);
        combat.Free();
    }

    private async Task Flow()
    {
        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        _main.UserArgs = Array.Empty<string>();
        AddChild(_main);
        await Frames(2);

        // The menu, with no user args.
        Check(_main.CurrentMatch == null && _main.Menu is { Visible: true } && _main.Setup is { Visible: false } && !_main.Paused,
            $"boot: match {_main.CurrentMatch != null}, menu {_main.Menu?.Visible}, setup {_main.Setup?.Visible}");
        Check(MatchNodes().Count == 0, $"match nodes on the menu: {string.Join(", ", MatchNodes())}");
        MainMenu menu = _main.Menu!;
        Check(menu.SkirmishButton.Text == _ui.Menu(MenuText.Skirmish) && menu.QuitButton.Text == _ui.Menu(MenuText.Quit)
            && Labels(menu).Contains(_ui.Menu(MenuText.Title)), $"menu text: '{menu.SkirmishButton.Text}', '{menu.QuitButton.Text}', labels {string.Join("|", Labels(menu))}");
        await Shot("menu");

        // Skirmish -> setup.
        await Click(menu.SkirmishButton);
        SkirmishSetup setup = _main.Setup!;
        Check(setup.Visible && !menu.Visible, $"Skirmish: setup {setup.Visible}, menu {menu.Visible}");
        Check(setup.SlotLabels[0].Text == _ui.Setup(SetupText.SlotHuman) && setup.SlotLabels[1].Text == _ui.Setup(SetupText.SlotAi),
            $"slots '{setup.SlotLabels[0].Text}', '{setup.SlotLabels[1].Text}'");
        // M6-V2: live lists. Each faction list holds every loaded faction's displayName in id order; defaults faction 0 / 1.
        for (int s = 0; s < 2; s++)
        {
            OptionButton list = setup.FactionOptions[s];
            var items = Enumerable.Range(0, list.ItemCount).Select(i => list.GetItemText(i)).ToArray();
            Check(!list.Disabled && items.SequenceEqual(_data.Factions.Select(f => f.DisplayName)) && list.Selected == s && setup.FactionPick(s) == s,
                $"slot {s} faction list: disabled {list.Disabled}, items [{string.Join("|", items)}], selected {list.Selected}");
        }
        // M6-V3: each colour list holds the table's names in order, each with a swatch; defaults colour 1 for "You", colour 2 for "AI".
        for (int s = 0; s < 2; s++)
        {
            OptionButton list = setup.ColourOptions[s];
            var items = Enumerable.Range(0, list.ItemCount).Select(i => list.GetItemText(i)).ToArray();
            Check(!list.Disabled && items.SequenceEqual(Enumerable.Range(0, UiText.ColourCount).Select(_ui.ColourName)) && setup.ColourPick(s) == s
                && list.GetItemIcon(0) != null, $"slot {s} colour list: items [{string.Join("|", items)}], pick {setup.ColourPick(s)}");
        }
        Check(Labels(setup).Contains(_ui.Setup(SetupText.Colour)), "setup has no colour heading");
        UniqueColourPicks(setup);
        OptionButton diff = setup.DifficultyOption;
        var diffItems = Enumerable.Range(0, diff.ItemCount).Select(i => diff.GetItemText(i)).ToArray();
        Check(!diff.Disabled && diffItems.SequenceEqual(new[] { SetupText.DifficultyEasy, SetupText.DifficultyNormal, SetupText.DifficultyHard }.Select(_ui.Setup))
            && setup.DifficultyPick == AiDifficulty.Normal, $"difficulty list: disabled {diff.Disabled}, items [{string.Join("|", diffItems)}], pick {setup.DifficultyPick}");
        var sizeItems = Enumerable.Range(0, setup.MapSizeOption.ItemCount).Select(i => setup.MapSizeOption.GetItemText(i)).ToArray();
        Check(sizeItems.SequenceEqual(LaunchOptions.MapSizes.Select(_ui.MapSizeItem)), $"map size items [{string.Join("|", sizeItems)}] not from setup.map_size_item");
        // No C# literal on the screen (BUG-0581 item 1): every label, button and list item is ui.json or faction data.
        var allowed = new HashSet<string>(Enum.GetValues<SetupText>().Select(_ui.Setup).Concat(LaunchOptions.MapSizes.Select(_ui.MapSizeItem))
            .Concat(_data.Factions.Select(f => f.DisplayName))) { "" };
        foreach (Node n in setup.FindChildren("*", "", true, false))
        {
            string? text = n switch { Label l => l.Text, OptionButton o => null, Button b => b.Text, _ => null };
            if (text != null) Check(allowed.Contains(text), $"setup shows '{text}' ({n.Name}), not from ui.json or data");
        }
        foreach (SetupText k in new[] { SetupText.Title, SetupText.Seed, SetupText.MapSize, SetupText.Faction, SetupText.Difficulty })
            Check(Labels(setup).Contains(_ui.Setup(k)), $"setup has no label '{_ui.Setup(k)}'");
        Check(setup.StartButton.Text == _ui.Setup(SetupText.Start) && setup.BackButton.Text == _ui.Setup(SetupText.Back) && setup.RandomButton.Text == _ui.Setup(SetupText.Random),
            "setup button text");
        Check(setup.MapSizeOption.ItemCount == LaunchOptions.MapSizes.Length && setup.MapSize == 128, $"map sizes {setup.MapSizeOption.ItemCount}, picked {setup.MapSize}");

        // The picks: the sides swapped (You play faction 1, the AI faction 0) and Easy.
        int f0 = 1 % _data.Factions.Length, f1 = 0;
        setup.FactionOptions[0].Select(f0);
        setup.FactionOptions[1].Select(f1);
        setup.DifficultyOption.Select((int)AiDifficulty.Easy);
        // M6-V3: colours 4 and 6, away from both defaults.
        const int c0 = 3, c1 = 5;
        setup.PickColour(0, c0);
        setup.PickColour(1, c1);

        // Back and in again: the picks survive.
        await Click(setup.BackButton);
        Check(menu.Visible && !setup.Visible, "Back did not show the menu");
        await Click(menu.SkirmishButton);
        Check(setup.Visible, "Skirmish (second time) did not show the setup");
        Check(setup.FactionPick(0) == f0 && setup.FactionPick(1) == f1 && setup.DifficultyPick == AiDifficulty.Easy && setup.ColourPick(0) == c0 && setup.ColourPick(1) == c1,
            $"Back lost the picks: {setup.FactionPick(0)}, {setup.FactionPick(1)}, {setup.DifficultyPick}, colours {setup.ColourPick(0)}, {setup.ColourPick(1)}");
        Check(GreyedOnly(setup, 0, c1) && GreyedOnly(setup, 1, c0), "after Back each colour list should grey only the other slot's colour");

        // Random writes a number; then the seed is typed: 7, a letter (Start greys), Backspace.
        string before = setup.SeedEdit.Text;
        await Click(setup.RandomButton);
        Check(setup.Seed != null && !setup.StartButton.Disabled, $"Random wrote '{setup.SeedEdit.Text}' (was '{before}')");
        await Click(setup.SeedEdit);
        setup.SeedEdit.SelectAll();
        await Type(Key.Key7, '7');
        Check(setup.SeedEdit.Text == "7" && setup.Seed == 7 && !setup.StartButton.Disabled, $"typed seed '{setup.SeedEdit.Text}'");
        await Type(Key.X, 'x');
        Check(setup.Seed == null && setup.StartButton.Disabled, $"seed '{setup.SeedEdit.Text}': Start disabled {setup.StartButton.Disabled}");
        await Type(Key.Backspace, '\0');
        Check(setup.Seed == 7 && !setup.StartButton.Disabled, $"after Backspace '{setup.SeedEdit.Text}'");
        setup.MapSizeOption.Select(Array.IndexOf(LaunchOptions.MapSizes, 192));
        Check(setup.MapSize == 192, $"map size {setup.MapSize}");
        await Shot("setup");

        // Start -> the match.
        await Click(setup.StartButton);
        await StartedMatch(7, 192);
        Seats(f0, f1, AiDifficulty.Easy);
        HallAndWorkers();
        await Colours(c0, c1);
        await Shot("match");
        Match match = _main.CurrentMatch!;
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        var sel = match.GetNode<SelectionController>("SelectionController");

        // Esc while A is armed is the targeting's cancel, not the pause.
        EntityHandle own = OwnUnit(runner.Simulation!);
        Check(own.Generation != 0 && sel.SelectOnly(own), "no own unit to select");
        await Type(Key.A, 'a');
        Check(sel.Targeting, "A did not arm attack-move");
        await Type(Key.Escape, '\0');
        Check(!sel.Targeting && !_main.Paused && !GetTree().Paused, $"Esc with A armed: targeting {sel.Targeting}, paused {_main.Paused}");

        // Esc pauses: no tick, no input reaches the match.
        await Type(Key.Escape, '\0');
        PauseMenu pause = _main.Pause!;
        Check(_main.Paused && GetTree().Paused && pause.Visible, $"Esc: paused {_main.Paused}, tree {GetTree().Paused}, menu {pause.Visible}");
        Check(pause.ResumeButton.Text == _ui.Pause(PauseText.Resume) && pause.QuitToMenuButton.Text == _ui.Pause(PauseText.QuitToMenu)
            && Labels(pause).Contains(_ui.Pause(PauseText.Title)), "pause text");
        await Shot("pause");
        int tick = runner.Simulation!.TickNumber;
        int selected = sel.Selection.Count;
        sel.CancelTargeting();
        await Type(Key.A, 'a');
        if (sel.TryScreenPosition(own.Index, out Vector2 at)) await ClickAt(at + new Vector2(200f, 0f)); // a click on open ground would deselect
        // Real time, not frames: headless frames are far shorter than a 50 ms tick. Half a second is 10 ticks at 1x.
        int pausedFrames = await Seconds(0.5);
        Check(runner.Simulation.TickNumber == tick, $"paused: ticks {tick} -> {runner.Simulation.TickNumber} over {pausedFrames} frames");
        Check(!sel.Targeting && sel.Selection.Count == selected, $"paused: A armed {sel.Targeting}, selection {selected} -> {sel.Selection.Count}");

        // Esc resumes.
        await Type(Key.Escape, '\0');
        Check(!_main.Paused && !GetTree().Paused && !pause.Visible, "Esc on the pause menu did not resume");
        await Seconds(0.3);
        Check(runner.Simulation.TickNumber > tick, $"resumed: no tick past {tick}");

        // Pause -> Quit to menu: the match is gone.
        await Type(Key.Escape, '\0');
        Check(_main.Paused, "second pause");
        await Click(pause.QuitToMenuButton);
        await Frames(2);
        Check(_main.CurrentMatch == null && !GetTree().Paused && !_main.Paused && menu.Visible && !setup.Visible,
            $"Quit to menu: match {_main.CurrentMatch != null}, tree paused {GetTree().Paused}, menu {menu.Visible}");
        List<string> left = MatchNodes();
        Check(left.Count == 0, $"match nodes left after Quit to menu: {string.Join(", ", left.Take(10))}");
        Check(!IsInstanceValid(match) && !IsInstanceValid(runner), "the old match or its runner is still a live object");

        // Skirmish again: the picks are kept, a fresh match.
        await Click(menu.SkirmishButton);
        Check(setup.Visible && setup.Seed == 7 && setup.MapSize == 192, $"setup again: seed '{setup.SeedEdit.Text}', map {setup.MapSize}");
        Check(setup.FactionPick(0) == f0 && setup.FactionPick(1) == f1 && setup.DifficultyPick == AiDifficulty.Easy && setup.ColourPick(0) == c0 && setup.ColourPick(1) == c1,
            $"Quit to menu lost the picks: {setup.FactionPick(0)}, {setup.FactionPick(1)}, {setup.DifficultyPick}, colours {setup.ColourPick(0)}, {setup.ColourPick(1)}");
        await Click(setup.StartButton);
        await StartedMatch(7, 192);
        Seats(f0, f1, AiDifficulty.Easy);
        await Colours(c0, c1);
        Check(_main.CurrentMatch != match, "the second match is the first one");
        Check(MatchNodes().Count(n => n.StartsWith(nameof(Match) + " ", StringComparison.Ordinal)) == 1, "not exactly one Match in the tree");
    }

    // M6-V6: --vsync off over a settings file saying on: off for this run (EffectiveVsync; headless has no window to ask), the
    // file's own value and bytes unchanged. --vsync is a match flag, so the boot starts the match at once.
    private async Task VsyncOverride()
    {
        const string path = "user://shell-test-settings/settings.cfg";
        var on = new GameSettings { Vsync = true, EdgePan = 1.25f };
        Check(on.Save(path), "could not write the test settings file");
        string full = ProjectSettings.GlobalizePath(path);
        string before = File.ReadAllText(full);
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { "--vsync", "off", "--units", "0" };
        main.SettingsPath = path;
        AddChild(main);
        await Frames(3);
        Check(main.CurrentMatch != null && main.VsyncOverride == false && !main.EffectiveVsync && main.Settings.Vsync && main.Settings.EdgePan == 1.25f,
            $"--vsync off over a file saying on: match {main.CurrentMatch != null}, override {main.VsyncOverride}, effective {main.EffectiveVsync}, file's {main.Settings.Vsync}");
        Check(main.CurrentMatch?.GetNode<RtsCamera>("RtsCamera").EdgePanSpeed == 1.25f, "a dev-flag match did not take the file's edge pan");
        Check(File.ReadAllText(full) == before, "the boot rewrote the settings file");
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);
        File.Delete(full);
    }

    // M6-V2: the running match's seats are the picks: the runner's, the sim's config's, and World.FactionOf agree.
    private void Seats(int f0, int f1, AiDifficulty difficulty)
    {
        SimRunner runner = _main.CurrentMatch!.GetNode<SimRunner>("SimRunner");
        World w = runner.Simulation!.World;
        var want = new[] { new PlayerSeat(f0, SeatController.Human, AiDifficulty.Normal), new PlayerSeat(f1, SeatController.Ai, difficulty) };
        Check(runner.Seats.SequenceEqual(want) && w.Config.Seats.SequenceEqual(want), $"seats: runner [{string.Join(", ", runner.Seats)}], config [{string.Join(", ", w.Config.Seats)}]");
        Check(w.FactionOf(0) == f0 && w.FactionOf(1) == f1 && w.Config.HasAiSeat, $"FactionOf {w.FactionOf(0)}, {w.FactionOf(1)}");
    }

    // M6-V3 (BUG-0602 item 2, BUG-0630): a --skirmish boot is the menu's Start with the defaults: a hall and workers a side,
    // slot 2 an AI (Normal), colours 1 / 2.
    private async Task SkirmishBoot()
    {
        RemoveChild(_main);
        _main.QueueFree();
        await Frames(2);
        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        _main.UserArgs = new[] { "--skirmish", "--mute" };
        AddChild(_main);
        await Frames(2);
        Check(_main.CurrentMatch != null && !_main.Menu!.Visible, "--skirmish did not start a match");
        if (_main.CurrentMatch == null) return;
        await Seconds(0.3);
        HallAndWorkers();
        Seats(0, 1 % _data.Factions.Length, LaunchOptions.DefaultAi); // BUG-0630: slot 2 the menu's AI seat
        await Colours(0, 1);
    }

    // M6-V3 (BUG-0604): the menu's match is docs/02's opening: per player one Town Hall and the starting workers, no army;
    // the camera on the local hall.
    private void HallAndWorkers()
    {
        Match match = _main.CurrentMatch!;
        World w = match.GetNode<SimRunner>("SimRunner").Simulation!.World;
        Check(match.UnitsPerPlayer == 0 && match.WorkersPerPlayer == _data.Rules.StartingWorkers,
            $"menu match: {match.UnitsPerPlayer} units, {match.WorkersPerPlayer} workers per player");
        UnitStore u = w.Units;
        int[] workers = new int[2], others = new int[2], halls = new int[2], buildings = new int[2];
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || (uint)u.Owner[i] > 1) continue;
            if (_data.Units[u.TypeId[i]].Slot == UnitSlot.Worker) workers[u.Owner[i]]++;
            else others[u.Owner[i]]++;
        }
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || (uint)b.Owner[k] > 1) continue;
            buildings[b.Owner[k]]++;
            if (_data.Buildings[b.TypeId[k]].Slot == BuildingSlot.TownHall) halls[b.Owner[k]]++;
        }
        // Checked within a second of the start: no worker trained yet (a worker takes longer), never a soldier.
        for (int p = 0; p < 2; p++)
            Check(halls[p] == 1 && workers[p] == _data.Rules.StartingWorkers && others[p] == 0,
                $"player {p}: {halls[p]} halls ({buildings[p]} buildings), {workers[p]} workers, {others[p]} other units");
        Check(match.Bases != null && match.Bases.HallAnchor[0] >= 0, "no planned hall for player 0");
        if (match.Bases == null || match.Bases.HallAnchor[0] < 0) return;
        System.Numerics.Vector2 hall = Rts.Sim.ViewApi.StartBase.FootprintCenter(w.NavGrid, _data.Buildings[match.Bases.HallType[0]], match.Bases.HallAnchor[0]);
        System.Numerics.Vector2 focus = match.StartFocus; // the edge pan may have moved the camera since
        Check(System.Numerics.Vector2.Distance(focus, hall) < 1f, $"camera focus {focus}, the local hall at {hall}");
    }

    // M6-V3: the match draws player p in its pick: Match.PlayerRgb, unit bodies, building materials, the local minimap dots.
    private async Task Colours(int c0, int c1)
    {
        Match match = _main.CurrentMatch!;
        uint[] want = { _ui.ColourRgb(c0), _ui.ColourRgb(c1) };
        Check(match.PlayerRgb.Length == 2 && match.PlayerRgb[0] == want[0] && match.PlayerRgb[1] == want[1],
            $"PlayerRgb [{string.Join(", ", match.PlayerRgb.Select(c => c.ToString("X6")))}], want {want[0]:X6}, {want[1]:X6}");
        var units = match.GetNode<UnitViews>("World3D/UnitViews");
        var buildings = match.GetNode<BuildingViews>("World3D/BuildingViews");
        var mini = match.GetNode<Minimap>("Hud/Minimap");
        Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        int refreshes = mini.Refreshes;
        await Seconds(0.3); // a minimap refresh and a view sync past the spawn tick
        // The dots are drawn from positions as of the last refresh: compare at those, so refresh once now.
        mini.Refresh(sim);
        UnitStore u = sim.World.Units;
        int wrongBody = 0, wrongDot = 0, seen = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || (uint)u.Owner[i] > 1 || !units.IsShown(i)) continue;
            seen++;
            Color c = UnitViews.ColorFromRgb(want[u.Owner[i]]);
            if (units.ViewOf(i)?.MaterialOverride is not StandardMaterial3D m || !m.AlbedoColor.IsEqualApprox(c)) wrongBody++;
            if (u.Owner[i] == SelectionController.LocalPlayer && mini.Raster!.TryPixelOf(u.Position[i], out int x, out int y))
            {
                Color got = mini.DotsImage.GetPixel(x, y);
                if (Mathf.Abs(got.R - c.R) > 0.01f || Mathf.Abs(got.G - c.G) > 0.01f || Mathf.Abs(got.B - c.B) > 0.01f) wrongDot++;
            }
        }
        Check(seen > 0 && wrongBody == 0 && wrongDot == 0 && mini.Refreshes > refreshes,
            $"colours: {seen} units shown, {wrongBody} bodies and {wrongDot} own dots not their player's pick");
        for (int p = 0; p < 2; p++)
            Check(buildings.PlayerMaterial(p).AlbedoColor.IsEqualApprox(UnitViews.ColorFromRgb(want[p])), $"player {p} building colour {buildings.PlayerMaterial(p).AlbedoColor}");
    }

    // The match is up on `seed` and a `size` map, and ticks.
    private async Task StartedMatch(ulong seed, int size)
    {
        await Frames(2);
        Match? match = _main.CurrentMatch;
        Check(match != null && !_main.Menu!.Visible && !_main.Setup!.Visible, $"Start: match {match != null}, menu {_main.Menu!.Visible}, setup {_main.Setup!.Visible}");
        if (match == null) throw new InvalidOperationException("no match after Start");
        Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        Check(sim.World.Config.Seed == seed && sim.World.Heightmap.Width == size && sim.World.Heightmap.Height == size && sim.World.NavGrid.Width == size,
            $"match seed {sim.World.Config.Seed}, map {sim.World.Heightmap.Width} x {sim.World.Heightmap.Height}");
        int tick = sim.TickNumber;
        await Seconds(0.3);
        Check(sim.TickNumber > tick, $"the match does not tick ({tick})");
    }

    private static EntityHandle OwnUnit(Simulation sim)
    {
        var u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer) return new EntityHandle(i, u.Generation[i]);
        return default;
    }

    // Every node a match brings (its root, runner, views, controllers, HUD), anywhere under the root.
    private List<string> MatchNodes()
    {
        var found = new List<string>();
        Walk(GetTree().Root, found);
        return found;
    }

    private static void Walk(Node n, List<string> found)
    {
        if (n is Match or SimRunner or UnitViews or BuildingViews or TerrainView or SelectionController or RtsCamera or FogOfWar or Minimap or CommandCard
            or AbilityViews or CombatViews or ProjectileViews or PropsView or ResourceBar or SelectionPanel or Sfx or DebugOverlay or Screenshotter)
            found.Add($"{n.GetType().Name} {n.GetPath()}");
        foreach (Node c in n.GetChildren()) Walk(c, found);
    }

    private static List<string> Labels(Node root)
    {
        var texts = new List<string>();
        foreach (Node n in root.FindChildren("*", "Label", true, false)) texts.Add(((Label)n).Text);
        return texts;
    }

    // ---- input ----

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private async Task Click(Control c)
    {
        Check(c.IsVisibleInTree(), $"{c.Name} is not visible");
        await ClickAt(c.GetGlobalRect().GetCenter());
    }

    private async Task ClickAt(Vector2 at)
    {
        Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        await Frames(2);
    }

    private async Task Type(Key key, char unicode)
    {
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = true });
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = false });
        await Frames(2);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // Waits at least `seconds` of real time (the sim ticks from frame time); returns the frames waited.
    private async Task<int> Seconds(double seconds)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int frames = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            frames++;
        }
        return frames;
    }

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/shell-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
