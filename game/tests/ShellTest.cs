using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

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
            // BUG-0770: every Main here reads and writes a per-process file, so the player's settings.cfg is left as it was.
            byte[]? playerSettings = PlayerSettings();
            LaunchRows();
            UiRows();
            await Flow();
            await SkirmishBoot();
            await FreeForAll();
            await VsyncOverride();
            byte[]? after = PlayerSettings();
            Check(playerSettings == null ? after == null : after != null && after.AsSpan().SequenceEqual(playerSettings),
                $"the player's {GameSettings.DefaultPath} changed over the run (BUG-0770)");
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

        SlotRows();

        // SimRunner's seats: explicit, one per player, the old defaults without picks.
        int factions = _data.Factions.Length;
        var plain = SimRunner.BuildSeats(3, factions, new[] { -1, -1 }, null);
        Check(plain.Length == 3 && Enumerable.Range(0, 3).All(p => plain[p] == new PlayerSeat(p % factions, SeatController.Human, AiDifficulty.Normal)),
            $"default seats {string.Join(", ", plain)}");
        var picked = SimRunner.BuildSeats(3, factions, new[] { 1, 0 }, AiDifficulty.Hard);
        Check(picked[0] == new PlayerSeat(1, SeatController.Human, AiDifficulty.Normal) && picked[1] == new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard)
            && picked[2] == new PlayerSeat(2 % factions, SeatController.Human, AiDifficulty.Normal), $"picked seats {string.Join(", ", picked)}");
        // M6-V5: one AI difficulty per slot; slot 0 is the local human whatever the array holds.
        var four = SimRunner.SeatsFor(4, factions, new[] { 1 % factions, 0, -1, 0 }, new AiDifficulty?[] { AiDifficulty.Hard, AiDifficulty.Easy, AiDifficulty.Hard, null });
        Check(four.Length == 4 && four[0] == new PlayerSeat(1 % factions, SeatController.Human, AiDifficulty.Normal)
            && four[1] == new PlayerSeat(0, SeatController.Ai, AiDifficulty.Easy) && four[2] == new PlayerSeat(2 % factions, SeatController.Ai, AiDifficulty.Hard)
            && four[3] == new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal), $"four seats {string.Join(", ", four)}");
        Check(SimRunner.SeatsFor(3, factions, new[] { -1, -1, -1, -1 }, new AiDifficulty?[4]).SequenceEqual(plain), "SeatsFor without picks is not BuildSeats' defaults");
    }

    // M6-V5: --players, and --faction / --ai / --colour for slots 1-4 (junk refused, BUG-0041's rule: a flag is never a value).
    private void SlotRows()
    {
        Check(LaunchOptions.HasMatchFlag(new[] { "--players", "3" }), "--players not a match flag");
        Check(LaunchOptions.Parse(Array.Empty<string>()).Players == null, "--players set with no flag");
        for (int n = 2; n <= 4; n++) Check(LaunchOptions.Parse(new[] { "--players", n.ToString() }).Players == n, $"--players {n} not read");
        foreach (string[] junk in new[]
        {
            new[] { "--players", "1" }, new[] { "--players", "5" }, new[] { "--players", "0" }, new[] { "--players", "03" }, new[] { "--players", "+3" },
            new[] { "--players", "three" }, new[] { "--players", "" }, new[] { "--players" }, new[] { "--players", "--seed", "3" },
        })
            Check(LaunchOptions.Parse(junk).Players == null, $"'{string.Join(' ', junk)}' accepted as {LaunchOptions.Parse(junk).Players}");
        Check(LaunchOptions.Parse(new[] { "--players", "--seed", "3" }).Seed == 3, "--players swallowed --seed (BUG-0041)");
        Check(LaunchOptions.Parse(new[] { "--players", "4", "--players", "9" }).Players == 4, "a junk --players replaced a good one");

        // --faction / --colour for slots 3 and 4; slot 0 and 5 are junk.
        LaunchOptions many = LaunchOptions.Parse(new[] { "--players", "4", "--faction", "3", "whirlwind", "--faction", "4", "malazan", "--colour", "3", "6", "--colour", "4", "8" });
        Check(many.FactionId(2) == "whirlwind" && many.FactionId(3) == "malazan" && many.ColourIndex(2) == 5 && many.ColourIndex(3) == 7 && many.FactionId(4) == null,
            $"slots 3 / 4: factions '{many.FactionId(2)}', '{many.FactionId(3)}', colours {many.ColourIndex(2)}, {many.ColourIndex(3)}");
        foreach (string[] junk in new[] { new[] { "--faction", "5", "malazan" }, new[] { "--faction", "0", "malazan" }, new[] { "--faction", "03", "malazan" },
            new[] { "--colour", "5", "1" }, new[] { "--colour", "0", "1" }, new[] { "--colour", "+3", "1" } })
        {
            LaunchOptions o = LaunchOptions.Parse(junk);
            Check(Enumerable.Range(0, 5).All(s => o.FactionId(s) == null && o.ColourIndex(s) == null), $"'{string.Join(' ', junk)}' picked something");
        }

        // --ai <slot> <difficulty>: slots 2-4; the one-value form stays slot 2; slot 1 is always the local human.
        LaunchOptions ais = LaunchOptions.Parse(new[] { "--ai", "3", "hard", "--ai", "4", "easy", "--ai", "2", "normal", "--players", "4" });
        Check(ais.AiOf(0) == null && ais.AiOf(1) == AiDifficulty.Normal && ais.Ai == AiDifficulty.Normal && ais.AiOf(2) == AiDifficulty.Hard && ais.AiOf(3) == AiDifficulty.Easy
            && ais.AiOf(4) == null && ais.AiOf(-1) == null, $"--ai per slot: {ais.AiOf(1)}, {ais.AiOf(2)}, {ais.AiOf(3)}");
        Check(LaunchOptions.Parse(new[] { "--ai", "hard", "--ai", "2", "easy" }).Ai == AiDifficulty.Easy
            && LaunchOptions.Parse(new[] { "--ai", "2", "easy", "--ai", "hard" }).Ai == AiDifficulty.Hard, "--ai's two forms on slot 2: the last good value does not win");
        foreach (string[] junk in new[]
        {
            new[] { "--ai", "1", "hard" }, new[] { "--ai", "5", "hard" }, new[] { "--ai", "0", "easy" }, new[] { "--ai", "3" }, new[] { "--ai", "3", "Hard" },
            new[] { "--ai", "3", "insane" }, new[] { "--ai", "3", "--seed", "3" }, new[] { "--ai", "--ai", "3" },
        })
        {
            LaunchOptions o = LaunchOptions.Parse(junk);
            Check(Enumerable.Range(0, 4).All(s => o.AiOf(s) == null), $"'{string.Join(' ', junk)}' made an AI seat: {o.AiOf(1)}, {o.AiOf(2)}, {o.AiOf(3)}");
        }
        Check(LaunchOptions.Parse(new[] { "--ai", "3", "--seed", "3" }).Seed == 3 && LaunchOptions.Parse(new[] { "--ai", "3", "--players", "3" }).Players == 3,
            "--ai <slot> swallowed the next flag (BUG-0041)");
        Check(LaunchOptions.Parse(new[] { "--ai", "1", "hard", "--seed", "6" }).Seed == 6, "--ai 1 hard: the refused pair did not consume its two values");
        // --skirmish: every slot of the match past the first an AI of the default difficulty; an explicit --ai wins; slots past the match none.
        LaunchOptions sk4 = LaunchOptions.Parse(new[] { "--skirmish", "--players", "4", "--ai", "3", "hard" });
        Check(sk4.AiOf(1) == LaunchOptions.DefaultAi && sk4.AiOf(2) == AiDifficulty.Hard && sk4.AiOf(3) == LaunchOptions.DefaultAi && sk4.AiOf(0) == null,
            $"--skirmish --players 4 --ai 3 hard: {sk4.AiOf(1)}, {sk4.AiOf(2)}, {sk4.AiOf(3)}");
        LaunchOptions sk2 = LaunchOptions.Parse(new[] { "--skirmish" });
        Check(sk2.Players == null && sk2.AiOf(1) == LaunchOptions.DefaultAi && sk2.AiOf(2) == null && sk2.AiOf(3) == null, $"--skirmish alone: slots 3 / 4 {sk2.AiOf(2)}, {sk2.AiOf(3)}");

        // Colours unique over four slots, slot by slot: a slot on an earlier slot's colour keeps its default, else the first free one.
        foreach ((string[] args, int[] want) in new[]
        {
            (new[] { "--players", "4" }, new[] { 0, 1, 2, 3 }),
            (new[] { "--players", "4", "--colour", "1", "3" }, new[] { 2, 1, 0, 3 }),
            (new[] { "--players", "4", "--colour", "2", "1", "--colour", "3", "1", "--colour", "4", "1" }, new[] { 0, 1, 2, 3 }),
            (new[] { "--players", "4", "--colour", "1", "2", "--colour", "2", "3", "--colour", "3", "4", "--colour", "4", "1" }, new[] { 1, 2, 3, 0 }),
            (new[] { "--players", "3", "--colour", "1", "2", "--colour", "2", "1", "--colour", "3", "1" }, new[] { 1, 0, 2, 3 }),
            (new[] { "--players", "4", "--colour", "1", "3", "--colour", "2", "4", "--colour", "3", "4" }, new[] { 2, 3, 0, 1 }),
            (new[] { "--players", "4", "--colour", "1", "8", "--colour", "2", "8", "--colour", "3", "8", "--colour", "4", "8" }, new[] { 7, 1, 2, 3 }),
        })
        {
            LaunchOptions o = LaunchOptions.Parse(args);
            int[] got = Enumerable.Range(0, 4).Select(s => o.ColourIndex(s) ?? s).ToArray();
            Check(got.SequenceEqual(want) && got.Distinct().Count() == 4, $"'{string.Join(' ', args)}': colours {string.Join(", ", got.Select(c => c + 1))}, want {string.Join(", ", want.Select(c => c + 1))}");
        }
        // A fuzz of four-slot --colour streams: the four effective colours always differ, and a slot's pick no earlier slot holds is kept.
        var rng = new Random(5705);
        int wrong = 0;
        for (int run = 0; run < 400; run++)
        {
            var args = new List<string> { "--players", "4" };
            var last = new int?[4];
            for (int k = rng.Next(0, 7); k > 0; k--)
            {
                int s = rng.Next(0, 6), c = rng.Next(0, 10); // slots 0 and 5, colours 0 and 9 are junk
                args.AddRange(new[] { "--colour", s.ToString(), c.ToString() });
                if (s >= 1 && s <= 4 && c >= 1 && c <= 8) last[s - 1] = c - 1;
            }
            LaunchOptions o = LaunchOptions.Parse(args.ToArray());
            int[] got = Enumerable.Range(0, 4).Select(s => o.ColourIndex(s) ?? s).ToArray();
            bool ok = got.Distinct().Count() == 4 && got.All(c => (uint)c < UiText.ColourCount);
            var earlier = new HashSet<int>();
            for (int s = 0; s < 4; s++)
            {
                if (last[s] is int pick && !earlier.Contains(pick)) ok &= got[s] == pick;
                earlier.Add(got[s]);
            }
            if (!ok && wrong++ < 5) Check(false, $"--colour fuzz '{string.Join(' ', args)}': colours {string.Join(", ", got.Select(c => c + 1))}");
        }
        Check(wrong == 0, $"--colour fuzz over four slots: {wrong} of 400 wrong");
        Check(LaunchOptions.FirstFreeColour(stackalloc bool[] { true, false, true }) == 1 && LaunchOptions.FirstFreeColour(stackalloc bool[] { false, true }) == 0
            && LaunchOptions.FirstFreeColour(stackalloc bool[] { true, true }) == 0, "FirstFreeColour(held)");
    }

    private void UiRows()
    {
        var keys = new List<(string Section, string Key, string Text)>();
        foreach (MenuText k in Enum.GetValues<MenuText>()) keys.Add(("menu", UiText.Key(k), _ui.Menu(k)));
        foreach (SetupText k in Enum.GetValues<SetupText>()) keys.Add(("setup", UiText.Key(k), _ui.Setup(k)));
        foreach (PauseText k in Enum.GetValues<PauseText>()) keys.Add(("pause", UiText.Key(k), _ui.Pause(k)));
        foreach (SettingsText k in Enum.GetValues<SettingsText>()) keys.Add(("settings", UiText.Key(k), _ui.Settings(k)));
        foreach (MatchOverText k in Enum.GetValues<MatchOverText>()) keys.Add(("match_over", UiText.Key(k), _ui.MatchOver(k)));
        // M6-V8: the Keybinds tab's keys section and the resource click's two hud lines.
        foreach (KeysText k in Enum.GetValues<KeysText>()) keys.Add(("keys", UiText.Key(k), _ui.Keys(k)));
        keys.Add(("hud", UiText.Key(HudText.ResourceLeft), _ui.Hud(HudText.ResourceLeft)));
        keys.Add(("hud", UiText.Key(HudText.LastSeen), _ui.Hud(HudText.LastSeen)));
        Check(keys.Count == 4 + 17 + 4 + 22 + 18 + 15 + 2, $"{keys.Count} shell keys"); // settings: 22 since M6-V7 (confine_cursor); keys 15 + hud 2 since M6-V8; setup 17 since M6-V5 (add_slot, remove_slot)
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
        foreach (string section in new[] { "menu", "setup", "pause", "settings", "match_over", "keys" })
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root.Remove(section);
            var errors = new List<string>();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 1 && errors[0].Contains(section), $"ui.json without {section}: [{string.Join("; ", errors)}]");
        }
        // The keys the brief names, by their ui.json spelling.
        string[] named = { "menu.title", "menu.skirmish", "menu.quit", "setup.title", "setup.seed", "setup.random", "setup.map_size", "setup.slot_human",
            "setup.slot_ai", "setup.faction", "setup.difficulty", "setup.start", "setup.back", "pause.title", "pause.resume", "pause.quit_to_menu",
            "setup.map_size_item", "setup.difficulty_easy", "setup.difficulty_normal", "setup.difficulty_hard", "setup.colour", "setup.add_slot", "setup.remove_slot",
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
            "match_over.human", "match_over.back",
            // M6-V8: the resource click's lines and the Keybinds tab's keys.
            "hud.resource_left", "hud.last_seen", "keys.tab_general", "keys.tab_keybinds", "keys.order_attack_move", "keys.order_stop",
            "keys.order_hold", "keys.order_move", "keys.subgroup_next", "keys.build_basic", "keys.build_advanced", "keys.group", "keys.card",
            "keys.press_key", "keys.conflict", "keys.reserved", "keys.defaults" };
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

        // M6-V8: the formats of the keys section and hud.resource_left; one that can't format (or names too little) is one
        // error naming its key. Every order, Tab and build key's name is an input action the key map binds.
        Check(_ui.ResourceLeft(1500, "Gold").Contains("1500") && _ui.ResourceLeft(1500, "Gold").Contains("Gold")
            && _ui.KeysFormat(KeysText.Card, 2, 4).Contains('2') && _ui.KeysFormat(KeysText.Card, 2, 4).Contains('4')
            && _ui.KeysFormat(KeysText.Conflict, "K", "Stop").Contains("Stop") && _ui.KeysFormat(KeysText.Group, 7).Contains('7'),
            $"formats: '{_ui.ResourceLeft(1500, "Gold")}', '{_ui.KeysFormat(KeysText.Card, 2, 4)}', '{_ui.KeysFormat(KeysText.Conflict, "K", "Stop")}'");
        foreach ((string section, string key, string bad) in new[] { ("keys", "group", "{1}"), ("keys", "group", "Group"), ("keys", "group", "{{0}}"),
            ("keys", "reserved", "{0} {1}"), ("keys", "reserved", "Reserved"), ("keys", "card", "{0}"), ("keys", "card", "{0} {2}"),
            ("keys", "conflict", "{1}"), ("keys", "conflict", "{0"), ("hud", "resource_left", "{0} left"), ("hud", "resource_left", "{2} {1}") })
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root[section]![key] = bad;
            var errors = new List<string>();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 1 && errors[0].Contains($"{section}.{key}"), $"{section}.{key} '{bad}': [{string.Join("; ", errors)}]");
        }
        for (int i = 0; i < KeyMap.FirstGroup; i++)
            Check(Enum.TryParse(KeyMap.Actions[i].Replace("_", ""), ignoreCase: true, out KeysText _) && InputMap.HasAction(KeyMap.Actions[i]),
                $"action {KeyMap.Actions[i]} has no keys text or no input action");

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

    // BUG-0770: a Main on a per-process settings file (missing: the defaults, never written), so a rebind on the desk can't change the run.
    private static Main NewMain(string[] args, string scene)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = args;
        main.SettingsPath = $"user://shell-test-{scene}-{OS.GetProcessId()}/settings.cfg";
        return main;
    }

    private static byte[]? PlayerSettings()
    {
        string path = ProjectSettings.GlobalizePath(GameSettings.DefaultPath);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private async Task Flow()
    {
        _main = NewMain(Array.Empty<string>(), "flow");
        Check(_main.SettingsPath != GameSettings.DefaultPath, "Flow's Main reads the player's settings (BUG-0770)");
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
    // file's own value and bytes unchanged. --vsync is a match flag, so the boot starts the match at once. BUG-0684: the file
    // is under a per-process folder (item 1); the settings screen shows the run's VSync, greyed, and Back keeps the file's
    // (item 2); a closing commit over the running match never shows the main menu (item 3).
    private async Task VsyncOverride()
    {
        string path = $"user://shell-test-settings-{OS.GetProcessId()}/settings.cfg";
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
        SettingsScreen? screen = main.SettingsScreen;
        if (Check(screen != null, "no settings screen") && screen != null)
        {
            main.ShowSettings();
            await Frames(1);
            Check(screen.VsyncOption.Selected == 1 && screen.VsyncOption.Disabled && screen.Picks().Vsync,
                $"--vsync off: the screen shows VSync item {screen.VsyncOption.Selected} (want 1, off), disabled {screen.VsyncOption.Disabled}, picks' VSync {screen.Picks().Vsync} (want the file's, on)");
            main.CommitSettings(screen.Picks(), stay: false);
            await Frames(1);
            Check(!screen.Visible && main.Menu is { Visible: false } && main.CurrentMatch != null && !main.EffectiveVsync && main.Settings.Vsync,
                $"a closing commit over the running match: screen {screen.Visible}, menu {main.Menu?.Visible}, match {main.CurrentMatch != null}, effective {main.EffectiveVsync}, file's {main.Settings.Vsync}");
            var file = new ConfigFile();
            Check(file.Load(path) == Error.Ok && (bool)file.GetValue("display", "vsync", false), $"--vsync off, Back: the file's vsync {file.GetValue("display", "vsync", false)} (want true)");
        }
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);
        try { Directory.Delete(Path.GetDirectoryName(full)!, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
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
        _main = NewMain(new[] { "--skirmish", "--mute" }, "skirmish");
        AddChild(_main);
        await Frames(2);
        Check(_main.CurrentMatch != null && !_main.Menu!.Visible, "--skirmish did not start a match");
        if (_main.CurrentMatch == null) return;
        await Seconds(0.3);
        HallAndWorkers();
        Seats(0, 1 % _data.Factions.Length, LaunchOptions.DefaultAi); // BUG-0630: slot 2 the menu's AI seat
        await Colours(0, 1);
    }

    // M6-V5: the setup's 2-4 slots through the shell. Add player twice (Add greys at four, Remove at two), the rows' text
    // and defaults, unique colours over four slots (the API and the lists' own signal), Remove then Add keeps the slot's
    // picks (its colour moved if a shown slot took it meanwhile), the picks kept over Back. Start: a four-seat match (the
    // human and three AIs of three difficulties, two factions) on the generator's four start locations, each hall seeing a
    // mine, four colours; the local player's army then wipes AI slot 4: its defeat time in the stats, no banner, the match
    // runs on. Then Remove to three slots: a three-seat match the same way.
    private async Task FreeForAll()
    {
        RemoveChild(_main);
        _main.QueueFree();
        await Frames(2);
        _main = NewMain(Array.Empty<string>(), "ffa");
        AddChild(_main);
        await Frames(2);
        MainMenu menu = _main.Menu!;
        SkirmishSetup setup = _main.Setup!;
        await Click(menu.SkirmishButton);
        Check(setup.SlotCount == 2 && Shown(setup) == 2 && !setup.AddSlotButton.Disabled && setup.RemoveSlotButton.Disabled
            && setup.AddSlotButton.Text == _ui.Setup(SetupText.AddSlot) && setup.RemoveSlotButton.Text == _ui.Setup(SetupText.RemoveSlot),
            $"setup opens on {setup.SlotCount} slots ({Shown(setup)} shown), Add disabled {setup.AddSlotButton.Disabled}, Remove disabled {setup.RemoveSlotButton.Disabled}");
        int factions = _data.Factions.Length;
        await Click(setup.AddSlotButton);
        await Click(setup.AddSlotButton);
        Check(setup.SlotCount == 4 && Shown(setup) == 4 && setup.AddSlotButton.Disabled && !setup.RemoveSlotButton.Disabled,
            $"Add twice: {setup.SlotCount} slots, {Shown(setup)} shown, Add disabled {setup.AddSlotButton.Disabled}");
        await Click(setup.AddSlotButton); // disabled: nothing
        Check(setup.SlotCount == 4, $"a fifth slot: {setup.SlotCount}");
        for (int s = 2; s < 4; s++)
            Check(setup.SlotLabels[s].Text == _ui.Setup(SetupText.SlotAi) && setup.FactionPick(s) == s % factions && setup.ColourPick(s) == s
                && setup.DifficultyPickOf(s) == LaunchOptions.DefaultAi && setup.DifficultyOptions[s]!.ItemCount == 3,
                $"slot {s + 1}: '{setup.SlotLabels[s].Text}', faction {setup.FactionPick(s)}, colour {setup.ColourPick(s)}, {setup.DifficultyPickOf(s)}");
        Check(Enumerable.Range(0, 4).All(s => GreyedExactly(setup, s)), "four slots: a colour list greys other than the other slots' colours");

        // Unique colours over four slots: a held colour is refused and moves to the first colour no other shown slot holds.
        setup.PickColour(0, 4);
        setup.PickColour(3, 4);
        Check(setup.ColourPick(0) == 4 && setup.ColourPick(3) == 0, $"slot 4 picking slot 1's colour 5: picks {Picks(setup)} (want 5, 2, 3, 1)");
        setup.PickColour(1, 3);
        setup.ColourOptions[2].Select(4);
        setup.ColourOptions[2].EmitSignal(OptionButton.SignalName.ItemSelected, 4L);
        Check(setup.ColourPick(1) == 3 && setup.ColourPick(2) == 1, $"slot 3's list picking slot 1's colour 5: picks {Picks(setup)} (want 5, 4, 2, 1)");
        Check(Enumerable.Range(0, 4).All(s => GreyedExactly(setup, s)) && Enumerable.Range(0, 4).Select(setup.ColourPick).Distinct().Count() == 4,
            $"after the picks: {Picks(setup)}");
        // Factions (both used twice) and the three AI difficulties.
        int[] f = { 1 % factions, 0, 1 % factions, 0 };
        for (int s = 0; s < 4; s++) setup.FactionOptions[s].Select(f[s]);
        setup.DifficultyOptions[1]!.Select((int)AiDifficulty.Easy);
        setup.DifficultyOptions[2]!.Select((int)AiDifficulty.Hard);
        setup.DifficultyOptions[3]!.Select((int)AiDifficulty.Normal);

        // Remove hides slot 4 (its colour 1 free for the others); Add brings it back with its picks.
        await Click(setup.RemoveSlotButton);
        Check(setup.SlotCount == 3 && Shown(setup) == 3 && !setup.AddSlotButton.Disabled && Enumerable.Range(0, 3).All(s => GreyedExactly(setup, s))
            && !setup.ColourOptions[0].IsItemDisabled(0), $"Remove: {setup.SlotCount} slots, colour 1 greyed for slot 1 {setup.ColourOptions[0].IsItemDisabled(0)}");
        string three = string.Join(' ', setup.MatchArgs()!);
        Check(three.Contains("--players 3") && !three.Contains("--colour 4") && !three.Contains("--ai 4") && three.Contains("--ai 3 hard"), $"three slots' args [{three}]");
        await Click(setup.AddSlotButton);
        Check(setup.SlotCount == 4 && setup.ColourPick(3) == 0 && setup.FactionPick(3) == f[3] && setup.DifficultyPickOf(3) == AiDifficulty.Normal,
            $"Add again: slot 4 colour {setup.ColourPick(3)}, faction {setup.FactionPick(3)}, {setup.DifficultyPickOf(3)}");
        // Hidden, its colour can be taken; shown again, it moves to the first free colour.
        await Click(setup.RemoveSlotButton);
        setup.PickColour(0, 0);
        Check(setup.ColourPick(0) == 0, $"slot 1 on the hidden slot's colour 1: {setup.ColourPick(0)}");
        await Click(setup.AddSlotButton);
        Check(setup.ColourPick(3) == 2 && setup.ColourPick(0) == 0 && Enumerable.Range(0, 4).All(s => GreyedExactly(setup, s)),
            $"slot 4 shown again on a taken colour: picks {Picks(setup)} (want 1, 4, 2, 3)");
        setup.PickColour(0, 4);
        int[] c = { 4, 3, 1, 2 };
        Check(Enumerable.Range(0, 4).All(s => setup.ColourPick(s) == c[s]), $"final colours {Picks(setup)}");
        string four = string.Join(' ', setup.MatchArgs()!);
        Check(four.Contains("--players 4") && four.Contains("--ai 2 easy") && four.Contains("--ai 3 hard") && four.Contains("--ai 4 normal")
            && four.Contains("--colour 4 3") && four.Contains($"--faction 4 {_data.Factions[f[3]].Key}"), $"four slots' args [{four}]");

        // Back and in again: the slots and picks survive.
        await Click(setup.BackButton);
        await Click(menu.SkirmishButton);
        Check(setup.SlotCount == 4 && Shown(setup) == 4 && Enumerable.Range(0, 4).All(s => setup.ColourPick(s) == c[s] && setup.FactionPick(s) == f[s])
            && setup.DifficultyPickOf(2) == AiDifficulty.Hard, $"Back lost the slots: {setup.SlotCount}, colours {Picks(setup)}");
        await Shot("setup-four");

        // Start: four seats.
        var ai = new AiDifficulty?[] { null, AiDifficulty.Easy, AiDifficulty.Hard, AiDifficulty.Normal };
        await Click(setup.StartButton);
        await StartedMatch(setup.Seed!.Value, setup.MapSize);
        SeatsAre(f, ai);
        HallAndWorkers(4);
        await Colours(c);
        await Shot("match-four");
        await WipeOut(4, ai);
        await QuitFromPause();

        // Three slots: Remove, Start.
        await Click(menu.SkirmishButton);
        await Click(setup.RemoveSlotButton);
        await Click(setup.StartButton);
        await StartedMatch(setup.Seed!.Value, setup.MapSize);
        SeatsAre(f[..3], ai[..3]);
        HallAndWorkers(3);
        await Colours(c[..3]);
        await QuitFromPause();
    }

    // The local player's dev army (a dev spawn: Line units and siege beside the hall, every few ticks sent at the victim's
    // first live unit, else its first building) wipes out the last seat. The seat's defeat is stamped (Match.DefeatedAt), no
    // banner opens (the local player stands, two others too), the match runs on; the stats list every seat.
    private async Task WipeOut(int players, AiDifficulty?[] ai)
    {
        Match match = _main.CurrentMatch!;
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        Simulation sim = runner.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int victim = players - 1;
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        runner.FixedFrameDelta = 1.0 / 20; // a tick a frame times the speed, however loaded the machine (the scripted-run seam)
        runner.GameSpeed = SimRunner.MaxSpeed;
        int st = w.StartLocations[victim].Cell;
        int line = Rts.Sim.ViewApi.StartBase.UnitOfSlot(_data, w.FactionOf(0), UnitSlot.Line);
        int siege = Rts.Sim.ViewApi.StartBase.UnitOfSlot(_data, w.FactionOf(0), UnitSlot.Siege);
        for (int k = 0; k < 24; k++)
        {
            double a = k * Math.PI * 2 / 24;
            int x = Math.Clamp(st % g.Width + (int)Math.Round(Math.Cos(a) * 9), 0, g.Width - 1), y = Math.Clamp(st / g.Width + (int)Math.Round(Math.Sin(a) * 9), 0, g.Height - 1);
            int cell = FlowField.NearestPassable(g, y * g.Width + x);
            sim.Enqueue(Command.SpawnUnit(0, k % 4 == 0 ? siege : line, g.CellCenter(cell % g.Width, cell / g.Width)));
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int lastOrder = -100;
        while (!w.IsDefeated(victim) && clock.Elapsed.TotalSeconds < 120)
        {
            if (sim.TickNumber - lastOrder >= 20)
            {
                lastOrder = sim.TickNumber;
                EntityHandle unitTarget = DefeatStaging.UnitsOf(w, victim).FirstOrDefault();
                EntityHandle buildingTarget = default;
                for (int k = 0; k < w.Buildings.Capacity && buildingTarget.Generation == 0; k++)
                    if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == victim) buildingTarget = new EntityHandle(k, w.Buildings.Generation[k]);
                foreach (EntityHandle u in DefeatStaging.UnitsOf(w, 0))
                {
                    bool isSiege = w.Units.TypeId[u.Index] == siege;
                    bool atBuilding = buildingTarget.Generation != 0 && (isSiege || unitTarget.Generation == 0);
                    if (atBuilding) sim.Enqueue(Command.Attack(0, u, buildingTarget, isBuilding: true));
                    else if (unitTarget.Generation != 0) sim.Enqueue(Command.Attack(0, u, unitTarget, isBuilding: false));
                }
            }
            await Frames(1);
        }
        Check(w.IsDefeated(victim) && match.DefeatedAt(victim) > 0, $"{players} seats: player {victim} not wiped out by tick {sim.TickNumber} (defeated {w.IsDefeated(victim)})");
        await Frames(3);
        Check(!_main.BannerUp && match.Outcome == MatchOutcome.None && !sim.MatchOver && Enumerable.Range(0, victim).All(p => !w.IsDefeated(p)),
            $"{players} seats after player {victim} fell: banner {_main.BannerUp}, outcome {match.Outcome}, MatchOver {sim.MatchOver}");
        int tick = sim.TickNumber;
        await Frames(4);
        Check(sim.TickNumber > tick, "the match stopped after a seat fell");
        runner.FixedFrameDelta = null;
        runner.GameSpeed = 1.0;
        _main.ShowStats();
        MatchStats stats = _main.Stats!;
        int col = Array.FindIndex(MatchStats.Columns, x => x.Label == MatchOverText.DefeatedAt);
        Check(stats.Visible && stats.RowCount == players, $"stats: visible {stats.Visible}, {stats.RowCount} rows (want {players})");
        for (int p = 0; p < Math.Min(players, stats.RowCount); p++)
        {
            string want = p == victim ? _ui.Clock(match.DefeatedAt(victim)) : _ui.MatchOver(MatchOverText.Standing);
            string seat = p == 0 ? _ui.Setup(SetupText.SlotHuman) : _ui.Setup(MatchStatsDifficulty(ai[p]!.Value));
            Check(stats.Cell(p, col) == want && stats.SeatName(p) == seat && stats.FactionName(p) == _data.Factions[w.FactionOf(p)].DisplayName
                && stats.Swatch(p).ToRgba32() >> 8 == match.PlayerRgb[p],
                $"stats row {p}: defeated '{stats.Cell(p, col)}' (want '{want}'), seat '{stats.SeatName(p)}' (want '{seat}'), faction '{stats.FactionName(p)}'");
        }
        await Shot("stats-four");
        _main.CloseStats();
        Check(!stats.Visible, "the stats did not close");
    }

    private static SetupText MatchStatsDifficulty(AiDifficulty d) => SkirmishSetup.DifficultyKeys[(int)d];

    private async Task QuitFromPause()
    {
        await Type(Key.Escape, '\0');
        Check(_main.Paused, "Esc did not pause the match");
        await Click(_main.Pause!.QuitToMenuButton);
        await Frames(2);
        Check(_main.CurrentMatch == null && !GetTree().Paused && _main.Menu!.Visible, "Quit to menu");
    }

    // M6-V5: the match's seats for N players: slot 0 the local human, slot p an AI of ai[p] (null: human).
    private void SeatsAre(int[] factions, AiDifficulty?[] ai)
    {
        SimRunner runner = _main.CurrentMatch!.GetNode<SimRunner>("SimRunner");
        World w = runner.Simulation!.World;
        var want = factions.Select((f, p) => new PlayerSeat(f, ai[p] != null ? SeatController.Ai : SeatController.Human, ai[p] ?? AiDifficulty.Normal)).ToArray();
        Check(runner.Seats.SequenceEqual(want) && w.Config.Seats.SequenceEqual(want) && Enumerable.Range(0, want.Length).All(p => w.FactionOf(p) == factions[p]),
            $"{want.Length} seats: runner [{string.Join(", ", runner.Seats)}], want [{string.Join(", ", want)}]");
    }

    private static int Shown(SkirmishSetup setup) => setup.SlotLabels.Count(l => l.Visible);

    private static string Picks(SkirmishSetup setup) => string.Join(", ", Enumerable.Range(0, setup.SlotCount).Select(s => setup.ColourPick(s) + 1));

    // Whether `slot`'s colour list greys exactly the colours the other shown slots hold.
    private static bool GreyedExactly(SkirmishSetup setup, int slot)
    {
        OptionButton list = setup.ColourOptions[slot];
        for (int c = 0; c < list.ItemCount; c++)
        {
            bool held = Enumerable.Range(0, setup.SlotCount).Any(s => s != slot && setup.ColourPick(s) == c);
            if (list.IsItemDisabled(c) != held) return false;
        }
        return list.ItemCount == UiText.ColourCount;
    }

    // M6-V3 (BUG-0604): the menu's match is docs/02's opening: per player one Town Hall and the starting workers, no army;
    // the camera on the local hall.
    // M6-V5: with `players` seats, each hall is centred on its own start location of the generator's `players` and sees a
    // gold mine (M6-VH1's check, BUG-0604, for every seat); the halls are apart.
    private void HallAndWorkers(int players = 2)
    {
        Match match = _main.CurrentMatch!;
        World w = match.GetNode<SimRunner>("SimRunner").Simulation!.World;
        Check(match.UnitsPerPlayer == 0 && match.WorkersPerPlayer == _data.Rules.StartingWorkers,
            $"menu match: {match.UnitsPerPlayer} units, {match.WorkersPerPlayer} workers per player");
        Check(w.Config.PlayerCount == players && w.StartLocations.Length == players,
            $"{players} seats: {w.Config.PlayerCount} players, {w.StartLocations.Length} start locations");
        UnitStore u = w.Units;
        int[] workers = new int[players], others = new int[players], halls = new int[players], buildings = new int[players];
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || (uint)u.Owner[i] >= (uint)players) continue;
            if (_data.Units[u.TypeId[i]].Slot == UnitSlot.Worker) workers[u.Owner[i]]++;
            else others[u.Owner[i]]++;
        }
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || (uint)b.Owner[k] >= (uint)players) continue;
            buildings[b.Owner[k]]++;
            if (_data.Buildings[b.TypeId[k]].Slot == BuildingSlot.TownHall) halls[b.Owner[k]]++;
        }
        // Checked within a second of the start: no worker trained yet (a worker takes longer), never a soldier.
        for (int p = 0; p < players; p++)
            Check(halls[p] == 1 && workers[p] == _data.Rules.StartingWorkers && others[p] == 0,
                $"{players} seats, player {p}: {halls[p]} halls ({buildings[p]} buildings), {workers[p]} workers, {others[p]} other units");
        var centres = new List<System.Numerics.Vector2>();
        for (int p = 0; p < players && match.Bases != null; p++)
        {
            if (!Check(match.Bases.HallAnchor[p] >= 0, $"{players} seats: no planned hall for player {p}")) continue;
            System.Numerics.Vector2 at = Rts.Sim.ViewApi.StartBase.FootprintCenter(w.NavGrid, _data.Buildings[match.Bases.HallType[p]], match.Bases.HallAnchor[p]);
            int st = w.StartLocations[Math.Min(p, w.StartLocations.Length - 1)].Cell;
            System.Numerics.Vector2 start = w.NavGrid.CellCenter(st % w.NavGrid.Width, st / w.NavGrid.Width);
            Check(System.Numerics.Vector2.Distance(at, start) < 1.5f * MapConstants.CellSize,
                $"{players} seats, player {p}: hall at {at}, its start location at {start}");
            Check(centres.TrueForAll(c => System.Numerics.Vector2.Distance(c, at) > 20f), $"{players} seats, player {p}: hall at {at} beside another's");
            centres.Add(at);
            Check(SeesMine(w, p, out float nearest), $"{players} seats, player {p}: no gold mine in sight at the start (nearest {nearest:0} m; BUG-0604)");
        }
        Check(match.Bases != null && match.Bases.HallAnchor[0] >= 0, "no planned hall for player 0");
        if (match.Bases == null || match.Bases.HallAnchor[0] < 0) return;
        System.Numerics.Vector2 hall = Rts.Sim.ViewApi.StartBase.FootprintCenter(w.NavGrid, _data.Buildings[match.Bases.HallType[0]], match.Bases.HallAnchor[0]);
        System.Numerics.Vector2 focus = match.StartFocus; // the edge pan may have moved the camera since
        Check(System.Numerics.Vector2.Distance(focus, hall) < 1f, $"camera focus {focus}, the local hall at {hall}");
    }

    // Whether `player` sees any cell of a gold mine now, and the nearest mine's distance from its hall's start location.
    private bool SeesMine(World w, int player, out float nearest)
    {
        bool sees = false;
        nearest = float.MaxValue;
        int st = w.StartLocations[Math.Min(player, w.StartLocations.Length - 1)].Cell;
        System.Numerics.Vector2 home = w.NavGrid.CellCenter(st % w.NavGrid.Width, st / w.NavGrid.Width);
        ResourceStore r = w.Resources;
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k]) continue;
            ResourceDef def = _data.Resources[r.TypeId[k]];
            if (def.Resource != ResourceKind.Gold) continue;
            int ax = r.Cell[k] % w.NavGrid.Width, ay = r.Cell[k] / w.NavGrid.Width;
            for (int dy = 0; dy < def.FootprintHeight; dy++)
                for (int dx = 0; dx < def.FootprintWidth; dx++)
                    if (w.Fog.IsVisible(player, (ay + dy) * w.NavGrid.Width + ax + dx)) sees = true;
            nearest = Math.Min(nearest, System.Numerics.Vector2.Distance(w.NavGrid.CellCenter(ax, ay), home));
        }
        return sees;
    }

    // M6-V3: the match draws player p in its pick: Match.PlayerRgb, unit bodies, building materials, the local minimap dots.
    // M6-V5: for every seat (`picks` one per player), the colours all different.
    private async Task Colours(params int[] picks)
    {
        Match match = _main.CurrentMatch!;
        uint[] want = picks.Select(_ui.ColourRgb).ToArray();
        Check(match.PlayerRgb.SequenceEqual(want) && want.Distinct().Count() == want.Length,
            $"PlayerRgb [{string.Join(", ", match.PlayerRgb.Select(c => c.ToString("X6")))}], want [{string.Join(", ", want.Select(c => c.ToString("X6")))}]");
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
            if (!u.Alive[i] || (uint)u.Owner[i] >= (uint)want.Length || !units.IsShown(i)) continue;
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
        for (int p = 0; p < want.Length; p++)
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
