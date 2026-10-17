using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V4: the match-over banner and the stats screen, through the shell (<c>Main.tscn</c>, a dev match: <c>--units 0
/// --no-bases</c>, every seat human) and injected clicks and keys. The scenes are set up with commands only (dev
/// <c>SpawnBuilding</c> / <c>SpawnUnit</c>, then <c>Attack</c>). M6-V7 (Requests 40): staged so each defeat lands on the
/// same tick under the M5-4b rule and the M5-4e one (<see cref="DefeatStaging"/>): every loser's only building is a finished
/// training hall and it has no live unit when the hall falls (checked on the defeat's tick).
/// <list type="bullet">
/// <item>Victory: player 0's army razes player 1's only building. The HUD's K / L under the banner equal the sim's (BUG-0710). The banner shows once, on the tick of the defeat, the tree
/// paused (no tick over real time), its title <c>match_over.victory</c>; Esc opens no pause menu over it. Stats: two rows,
/// Kills / Losses / gold / wood equal to the sim's spans, the length and player 1's defeat time to the second, "You" and
/// "Human"; Back returns to the banner. Continue watching unpauses (ticks move), no second banner; Esc is the pause menu,
/// which now has Stats (the numbers of the moment); Esc closes it, Esc resumes; Quit to menu.</item>
/// <item>Defeat (a second skirmish from the same shell, no stale banner): player 1 razes player 0's only building; the
/// banner's Main menu reaches the menu with the match freed (no match node left, the old match invalid, the tree unpaused).</item>
/// <item>Draw: both players' halls shelled by identical batteries of a third seat that never builds (outside the match-over
/// count): both fall on one tick, one banner, <c>match_over.draw</c>.</item>
/// <item>Three seats: player 0 falls first: Defeat while the sim's <c>MatchOver</c> is still false; Continue; player 2 falls
/// later (the match is over), and no second banner shows; the stats have three rows.</item>
/// </list>
/// M6-V11: every stats check reads every counter column against its span, the Age II cell, the graph (a point per sample, the
/// last point at the last sample, the line colours the swatches, the lines drawn) and the layout inside the window
/// (<see cref="StatsScreenCheck"/>). Victory: Age II not_reached at the banner; after Continue player 0 researches it
/// (dev Town Hall and ranged hall, a dev grant, <c>Command.Research</c>) and the pause menu's Stats shows its clock. Defeat
/// opens the screen before the first sample: no graph point, no line drawn, no error.
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/MatchOverViewTest.tscn</c>; prints "MATCH OVER VIEW TEST
/// PASS" or "MATCH OVER VIEW TEST FAIL: ..." lines. Windowed with <c>-- --shots &lt;dir&gt; [--size 1280x720]</c> it saves
/// <c>matchover-banner.png</c>, <c>matchover-stats.png</c> and <c>matchover-stats-late.png</c> (after Age II). The settings file is a per-process <c>user://</c> one.
/// </remarks>
public partial class MatchOverViewTest : Node
{
    private static readonly string[] Args = { "--units", "0", "--no-bases", "--mute", "--seed", "2", "--speed", "8" };

    private readonly List<string> _failures = new();
    private readonly List<DefeatEvent> _events = new();
    private readonly List<int> _unitsAtDefeat = new(); // the loser's live units on its defeat's tick (the staging wants 0)
    private GameData _data = null!;
    private UiText _ui = null!;
    private Main _main = null!;
    private string? _shots;
    private int _ended;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // the banner pauses the tree
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
            _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            _main.UserArgs = Args;
            _main.SettingsPath = $"user://matchover-test-{OS.GetProcessId()}/settings.cfg";
            AddChild(_main);
            await Frames(2);
            // M6-V11: --size 1280x720 for the screenshots, after the shell applied its settings' window size.
            if (StatsScreenCheck.TryWindow(OS.GetCmdlineUserArgs(), out Vector2I window))
            {
                DisplayServer.WindowSetSize(window);
                GetTree().Root.Size = window;
                await Frames(2);
            }
            await Victory();
            await Defeat();
            await Draw();
            await ThreeSeats();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"MATCH OVER VIEW TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("MATCH OVER VIEW TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- the cases ----

    private async Task Victory()
    {
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        await Bases(sim, 2);
        EntityHandle target = BuildingOf(w, 1);
        await Army(sim, 0, target, 6);
        Raze(sim, 0, target);
        await UntilBanner(sim, "victory");
        MatchOverBanner banner = _main.Banner!;
        int tick = sim.TickNumber;
        Check(_events.Count == 1 && _events[0].Player == 1 && tick == _events[0].Tick + 1,
            $"victory: events [{string.Join(", ", _events)}], banner at tick {tick} (want the defeat's tick + 1)");
        Check(GetTree().Paused && banner.Visible && banner.Shown == MatchOutcome.Victory && match.Outcome == MatchOutcome.Victory && sim.MatchOver,
            $"victory: paused {GetTree().Paused}, banner {banner.Visible} {banner.Shown}, outcome {match.Outcome}, MatchOver {sim.MatchOver}");
        // BUG-0710 item 1: the HUD under the banner shows the killing tick's K / L, the numbers the stats screen shows.
        ResourceBar bar = match.GetNode<ResourceBar>("Hud/ResourceBar");
        Check(bar.ShownKills == w.Kills[0] && bar.ShownLosses == w.Losses[0] && w.Kills[0] > 0,
            $"victory: HUD K / L {bar.ShownKills} / {bar.ShownLosses} under the banner, sim {w.Kills[0]} / {w.Losses[0]}");
        Check(banner.Title.Text == _ui.MatchOver(MatchOverText.Victory) && banner.StatsButton.Text == _ui.MatchOver(MatchOverText.Stats)
            && banner.MenuButton.Text == _ui.MatchOver(MatchOverText.Menu) && banner.ContinueButton.Text == _ui.MatchOver(MatchOverText.Continue),
            $"victory text: '{banner.Title.Text}', '{banner.StatsButton.Text}', '{banner.MenuButton.Text}', '{banner.ContinueButton.Text}'");
        await Seconds(0.4);
        Check(sim.TickNumber == tick, $"banner up: ticks {tick} -> {sim.TickNumber}");
        await Type(Key.Escape);
        Check(banner.Visible && !_main.Paused && GetTree().Paused, $"Esc on the banner: banner {banner.Visible}, pause menu {_main.Paused}");
        await Shot("banner");

        // Stats over the banner, Back to it.
        await Click(banner.StatsButton);
        MatchStats stats = _main.Stats!;
        Check(stats.Visible, "Stats did not open the stats screen");
        CheckStats(match, 2, "victory");
        Check(stats.Cell(0, DefeatedColumn) == _ui.MatchOver(MatchOverText.Standing) && stats.Cell(1, DefeatedColumn) == _ui.Clock(_events[0].Tick + 1),
            $"victory: defeated cells '{stats.Cell(0, DefeatedColumn)}', '{stats.Cell(1, DefeatedColumn)}'");
        Check(stats.Cell(0, AgeColumn) == _ui.MatchOver(MatchOverText.NotReached) && stats.Cell(1, AgeColumn) == _ui.MatchOver(MatchOverText.NotReached)
            && w.TechResearchedTick(0, _data.AgeTechs[0]) < 0, $"victory: Age II cells before research '{stats.Cell(0, AgeColumn)}', '{stats.Cell(1, AgeColumn)}'");
        Check(stats.SeatName(0) == _ui.Setup(SetupText.SlotHuman) && stats.SeatName(1) == _ui.MatchOver(MatchOverText.Human),
            $"victory: seats '{stats.SeatName(0)}', '{stats.SeatName(1)}'");
        await Shot("stats");
        await Click(stats.BackButton);
        Check(!stats.Visible && banner.Visible && GetTree().Paused, $"stats Back: stats {stats.Visible}, banner {banner.Visible}");

        // Continue watching: the match runs on, no second banner; Esc is the pause menu again, with Stats.
        await Click(banner.ContinueButton);
        Check(!banner.Visible && !GetTree().Paused, $"Continue: banner {banner.Visible}, paused {GetTree().Paused}");
        await Seconds(0.5);
        Check(sim.TickNumber > tick && !banner.Visible && _ended == 1, $"after Continue: tick {sim.TickNumber} (banner at {tick}), banner {banner.Visible}, ended {_ended}");
        // M6-V11: player 0 researches Age II after the banner (dev halls, a dev grant): the cell turns from not_reached to its clock.
        await AgeTwo(sim, 0);
        await Type(Key.Escape);
        PauseMenu pause = _main.Pause!;
        Check(_main.Paused && pause.StatsButton.Visible && pause.StatsButton.Text == _ui.MatchOver(MatchOverText.Stats), $"pause after the banner: paused {_main.Paused}, Stats {pause.StatsButton.Visible}");
        await Click(pause.StatsButton);
        Check(stats.Visible, "the pause menu's Stats did not open the stats screen");
        CheckStats(match, 2, "from the pause menu");
        await Shot("stats-late");
        int ageTick = w.TechResearchedTick(0, _data.AgeTechs[0]);
        Check(ageTick > tick && stats.Cell(0, AgeColumn) == _ui.Clock(ageTick) && stats.Cell(0, AgeColumn) != _ui.MatchOver(MatchOverText.NotReached)
            && stats.Cell(1, AgeColumn) == _ui.MatchOver(MatchOverText.NotReached),
            $"Age II after research: tick {ageTick} (banner {tick}), cells '{stats.Cell(0, AgeColumn)}', '{stats.Cell(1, AgeColumn)}' (want '{_ui.Clock(ageTick)}', not_reached)");
        Check(w.ArmyValueSamples(0).Length >= 2 && stats.Graph.GraphPoints(0).Length == w.ArmyValueSamples(0).Length && w.ArmyValueSamples(0)[^1] > 0,
            $"graph after research: {w.ArmyValueSamples(0).Length} samples of player 0, {stats.Graph.GraphPoints(0).Length} points (want 2+ and a live army)");
        await Type(Key.Escape);
        Check(!stats.Visible && _main.Paused, $"Esc on the stats: stats {stats.Visible}, pause menu {_main.Paused}");
        await Type(Key.Escape);
        Check(!_main.Paused && !GetTree().Paused, "Esc did not resume");
        await Type(Key.Escape);
        await Click(pause.QuitToMenuButton);
        Check(_main.CurrentMatch == null && !stats.Visible && !banner.Visible, "Quit to menu after the banner");
    }

    private async Task Defeat()
    {
        _main.StartSkirmish(Args);
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        Check(!_main.Banner!.Visible && !GetTree().Paused, "a second skirmish starts with a banner up");
        await EmptyGraph(match);
        await Bases(sim, 2);
        EntityHandle own = BuildingOf(w, 0);
        await Army(sim, 1, own, 6);
        Raze(sim, 1, own);
        await UntilBanner(sim, "defeat");
        MatchOverBanner banner = _main.Banner!;
        Check(_events.Count == 1 && _events[0].Player == 0 && banner.Title.Text == _ui.MatchOver(MatchOverText.Defeat) && match.Outcome == MatchOutcome.Defeat && GetTree().Paused,
            $"defeat: events [{string.Join(", ", _events)}], title '{banner.Title.Text}', outcome {match.Outcome}, paused {GetTree().Paused}");
        await CheckStatsVia(banner, match, 2, "defeat");
        // Main menu: the match is freed.
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        await Click(banner.MenuButton);
        await Frames(2);
        Check(_main.CurrentMatch == null && _main.Menu is { Visible: true } && !banner.Visible && !GetTree().Paused,
            $"Main menu: match {_main.CurrentMatch != null}, menu {_main.Menu?.Visible}, banner {banner.Visible}, paused {GetTree().Paused}");
        List<string> left = MatchNodes();
        Check(left.Count == 0, $"match nodes left after Main menu: {string.Join(", ", left.Take(10))}");
        Check(!IsInstanceValid(match) && !IsInstanceValid(runner), "the old match or its runner is still a live object");
    }

    private async Task Draw()
    {
        // M6-V7: a third seat that never builds (so neither rule counts it for the match's end) holds two identical batteries;
        // players 0 and 1 each own one hall of the same type, translated copies, and no unit. One order: both fall on one tick.
        _main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").PlayerCount = 3;
        _main.StartSkirmish(Args);
        _main.BeforeStart = null;
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int[] halls = DefeatStaging.Halls(_data, w, new[] { 0, 1 }, out bool alike);
        Check(alike, $"draw: the two halls differ ({_data.Buildings[halls[0]].Key}, {_data.Buildings[halls[1]].Key}): no simultaneous fall");
        int[] anchors = DefeatStaging.Row(w, new[] { 0, 1 }, halls);
        for (int p = 0; p < 2; p++) DefeatStaging.SpawnAt(sim, p, halls[p], anchors[p]);
        await Ticks(sim, 3);
        Check(w.Buildings.Count == 2, $"draw: {w.Buildings.Count} halls placed (want 2)");
        int shooter = DefeatStaging.Shooter(_data);
        var batteries = new System.Numerics.Vector2[2][];
        for (int p = 0; p < 2; p++)
        {
            batteries[p] = DefeatStaging.Battery(g, _data.Buildings[halls[p]], anchors[p]);
            foreach (System.Numerics.Vector2 at in batteries[p]) sim.Enqueue(Command.SpawnUnit(2, shooter, at));
        }
        await Ticks(sim, 3);
        var guns = new EntityHandle[2][];
        for (int p = 0; p < 2; p++)
        {
            EntityHandle target = DefeatStaging.BuildingAt(w, p, anchors[p]);
            guns[p] = new EntityHandle[batteries[p].Length];
            for (int k = 0; k < batteries[p].Length; k++)
            {
                EntityHandle gun = guns[p][k] = DefeatStaging.UnitAt(w, 2, batteries[p][k]);
                Check(gun != default && target != default, $"draw: battery {p} gun {k} at {batteries[p][k]}: {gun}, hall {target}");
                if (gun != default && target != default) sim.Enqueue(Command.Attack(2, gun, target, isBuilding: true));
            }
        }
        Check(DefeatStaging.SamePhase(guns), $"draw: the batteries' guns scan on different ticks: [{string.Join(" | ", guns.Select(b => string.Join(",", b.Select(h => h.Index))))}]");
        Check(!w.EverBuilt(2) && DefeatStaging.UnitsOf(w, 0).Count == 0 && DefeatStaging.UnitsOf(w, 1).Count == 0,
            $"draw staging: seat 2 built {w.EverBuilt(2)}, units of 0 / 1: {DefeatStaging.UnitsOf(w, 0).Count} / {DefeatStaging.UnitsOf(w, 1).Count}");
        await UntilBanner(sim, "draw");
        MatchOverBanner banner = _main.Banner!;
        Check(_events.Count == 2 && _events[0].Tick == _events[1].Tick && banner.Title.Text == _ui.MatchOver(MatchOverText.Draw) && match.Outcome == MatchOutcome.Draw,
            $"draw: events [{string.Join(", ", _events)}], title '{banner.Title.Text}', outcome {match.Outcome}");
        await Click(banner.ContinueButton);
        await Seconds(0.3);
        Check(_ended == 1 && !banner.Visible, $"draw: ended {_ended}, banner {banner.Visible} after Continue");
        await QuitMatch();
    }

    private async Task ThreeSeats()
    {
        _main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").PlayerCount = 3;
        _main.StartSkirmish(Args);
        _main.BeforeStart = null;
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        Check(w.Config.PlayerCount == 3, $"three seats: {w.Config.PlayerCount} players");
        await Bases(sim, 3);
        EntityHandle own = BuildingOf(w, 0);
        await Army(sim, 1, own, 6);
        Raze(sim, 1, own);
        await UntilBanner(sim, "three seats");
        MatchOverBanner banner = _main.Banner!;
        Check(banner.Title.Text == _ui.MatchOver(MatchOverText.Defeat) && match.Outcome == MatchOutcome.Defeat && !sim.MatchOver && w.IsDefeated(0),
            $"three seats: title '{banner.Title.Text}', outcome {match.Outcome}, MatchOver {sim.MatchOver} (want false: players 1 and 2 stand)");
        await CheckStatsVia(banner, match, 3, "three seats");
        await Click(banner.ContinueButton);
        // Player 1 razes player 2 as well: the match is over now, and still one banner.
        EntityHandle third = BuildingOf(w, 2);
        await Army(sim, 1, third, 6);
        Raze(sim, 1, third);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!sim.MatchOver && clock.Elapsed.TotalSeconds < 60) await Frames(1);
        await Seconds(0.3);
        Check(sim.MatchOver && _events.Count == 2 && _ended == 1 && !banner.Visible && !GetTree().Paused,
            $"three seats, player 2 down: MatchOver {sim.MatchOver}, events {_events.Count}, ended {_ended}, banner {banner.Visible}, paused {GetTree().Paused}");
        await QuitMatch();
    }

    // ---- checks ----

    private static int DefeatedColumn => Array.FindIndex(MatchStats.Columns, c => c.Label == MatchOverText.DefeatedAt);

    private static int AgeColumn => Array.FindIndex(MatchStats.Columns, c => c.Label == MatchOverText.AgeIiAt);

    private static readonly System.Reflection.PropertyInfo LedgerProperty =
        typeof(World).GetProperty("Ledger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    // M6-V11: Age II for `player` through Command.Research: a dev Town Hall and a second qualifying hall (the any-of rule wants
    // two of infantry / ranged / shock / forge; the player has its infantry hall), a dev grant of gold and wood (the QaV3b seam on
    // the ledger), then the research, run until the sim says HasTech.
    private async Task AgeTwo(Simulation sim, int player)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int age = _data.AgeTechs[0];
        int faction = w.FactionOf(player);
        int town = StartBase.BuildingOfSlot(_data, faction, BuildingSlot.TownHall), ranged = StartBase.BuildingOfSlot(_data, faction, BuildingSlot.RangedHall);
        int x0 = g.Width / 4, y0 = g.Height / 2;
        int townAt = DefeatStaging.Anchor(w, player, town, x0, y0 - 10);
        DefeatStaging.SpawnAt(sim, player, town, townAt);
        await Ticks(sim, 2);
        DefeatStaging.SpawnAt(sim, player, ranged, DefeatStaging.Anchor(w, player, ranged, x0, y0 + 10));
        await Ticks(sim, 2);
        object ledger = LedgerProperty.GetValue(w)!;
        ((int[])ledger.GetType().GetProperty("Gold")!.GetValue(ledger)!)[player] = 5000;
        ((int[])ledger.GetType().GetProperty("Wood")!.GetValue(ledger)!)[player] = 5000;
        EntityHandle hall = DefeatStaging.BuildingAt(w, player, townAt);
        ResearchError why = ResearchError.None;
        Check(hall != default && w.CanResearch(player, hall.Index, age, out why), $"Age II: hall {hall}, research refused ({why})");
        sim.Enqueue(Command.Research(player, SelectionController.SiteCenter(w, hall.Index), age));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!w.HasTech(player, age) && clock.Elapsed.TotalSeconds < 60) await Frames(1);
        Check(w.HasTech(player, age) && w.TechResearchedTick(player, age) > 0, $"Age II not researched by tick {sim.TickNumber} (researched tick {w.TechResearchedTick(player, age)})");
        await Frames(1);
    }

    // The open stats screen against the sim now: one row per seat, every number its span's, the length and defeat times.
    private void CheckStats(Match match, int players, string how)
    {
        MatchStats stats = _main.Stats!;
        World w = match.Simulation!.World;
        Check(stats.RowCount == players, $"{how}: {stats.RowCount} stats rows (want {players})");
        Check(stats.Title.Text == _ui.MatchOver(MatchOverText.StatsTitle) && stats.Length.Text == _ui.MatchLength(match.Simulation.TickNumber)
            && stats.BackButton.Text == _ui.MatchOver(MatchOverText.Back),
            $"{how}: title '{stats.Title.Text}', length '{stats.Length.Text}' at tick {match.Simulation.TickNumber}");
        // M6-V11: every counter column equals its span, the Age II cell the researched tick's clock or not_reached; the graph.
        foreach (string f in StatsScreenCheck.Columns(stats, w, _ui, how)) Check(false, f);
        foreach (string f in StatsScreenCheck.Graph(stats, match, drawn: true, how)) Check(false, f);
        foreach (string f in StatsScreenCheck.Fits(stats, GetViewport().GetVisibleRect(), how)) Check(false, f);
        Check(stats.Graph.Title == _ui.MatchOver(MatchOverText.ArmyValue) && stats.Graph.IsVisibleInTree(), $"{how}: graph title '{stats.Graph.Title}', visible {stats.Graph.IsVisibleInTree()}");
        for (int p = 0; p < Math.Min(players, stats.RowCount); p++)
        {
            DefeatEvent? e = _events.Where(x => x.Player == p).Select(x => (DefeatEvent?)x).FirstOrDefault();
            string want = e is DefeatEvent d ? _ui.Clock(d.Tick + 1) : _ui.MatchOver(MatchOverText.Standing);
            Check(stats.Cell(p, DefeatedColumn) == want, $"{how}: row {p} defeated '{stats.Cell(p, DefeatedColumn)}' (want '{want}')");
            Check(stats.FactionName(p) == _data.Factions[w.FactionOf(p)].DisplayName, $"{how}: row {p} faction '{stats.FactionName(p)}'");
            uint rgb = match.PlayerRgb[p];
            Check(stats.Swatch(p).ToRgba32() >> 8 == rgb, $"{how}: row {p} swatch {stats.Swatch(p).ToHtml(false)} (want {rgb:X6})");
        }
        Check(w.Kills[0] + w.Kills[1] > 0 && w.BuildingKills[0] + w.BuildingKills[1] > 0, $"{how}: nobody razed anything (the scene's razing counts a killing blow)");
    }

    // M6-V11: the screen opened (through Main.ShowStats, the seam ShellTest uses) before the first sample: the graph has no
    // point, draws the axes and the title and no line, with no error; every counter cell 0, Age II not_reached.
    private async Task EmptyGraph(Match match)
    {
        Simulation sim = match.Simulation!;
        _main.PauseMatch();
        await Frames(1);
        _main.ShowStats();
        await Frames(2);
        MatchStats stats = _main.Stats!;
        World w = sim.World;
        ArmyValueGraph g = stats.Graph;
        Check(stats.Visible && sim.TickNumber < w.StatsSampleTicks && w.ArmyValueSamples(0).Length == 0 && w.ArmyValueSamples(1).Length == 0,
            $"empty graph: stats {stats.Visible} at tick {sim.TickNumber}, samples {w.ArmyValueSamples(0).Length} / {w.ArmyValueSamples(1).Length}");
        Check(g.LineCount == 2 && g.GraphPoints(0).Length == 0 && g.GraphPoints(1).Length == 0 && g.MaxValue == 0 && g.DrawCount > 0 && g.DrawnLines == 0,
            $"empty graph: {g.LineCount} lines, points {g.GraphPoints(0).Length} / {g.GraphPoints(1).Length}, scale {g.MaxValue}, drawn {g.DrawCount} times, {g.DrawnLines} lines");
        foreach (string f in StatsScreenCheck.Columns(stats, w, _ui, "empty graph")) Check(false, f);
        foreach (string f in StatsScreenCheck.Graph(stats, match, drawn: true, "empty graph")) Check(false, f);
        for (int c = 0; c < MatchStats.Columns.Length; c++)
            if (c != AgeColumn && c != DefeatedColumn) Check(stats.Cell(0, c) == "0" && stats.Cell(1, c) == "0", $"empty graph: column {MatchStats.Columns[c].Label} '{stats.Cell(0, c)}', '{stats.Cell(1, c)}' at tick {sim.TickNumber}");
        _main.CloseStats();
        _main.Resume();
        await Frames(1);
        Check(!stats.Visible && !GetTree().Paused, $"empty graph: stats {stats.Visible}, paused {GetTree().Paused} after closing");
    }

    private async Task CheckStatsVia(MatchOverBanner banner, Match match, int players, string how)
    {
        await Click(banner.StatsButton);
        CheckStats(match, players, how);
        await Type(Key.Escape);
        Check(!_main.Stats!.Visible && banner.Visible, $"{how}: Esc on the stats: stats {_main.Stats.Visible}, banner {banner.Visible}");
    }

    // ---- scene set-up (commands only) ----

    private Match Watch(Match? match)
    {
        if (match?.Simulation == null) throw new InvalidOperationException("the shell started no match");
        _events.Clear();
        _ended = 0;
        match.Ended += _ => _ended++;
        _unitsAtDefeat.Clear();
        match.GetNode<SimRunner>("SimRunner").Ticked += s =>
        {
            foreach (DefeatEvent e in s.World.DefeatEvents)
            {
                _events.Add(e);
                _unitsAtDefeat.Add(DefeatStaging.UnitsOf(s.World, e.Player).Count);
            }
        };
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        return match;
    }

    // One finished training hall per player (a dev SpawnBuilding: no explored ground needed, so no scout), spread over the map:
    // player 0 west, 1 east, 2 north. No player gets a unit here: a loser with no unit falls with its hall under both rules.
    private async Task Bases(Simulation sim, int players)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        (int X, int Y)[] at = Spots(g);
        for (int p = 0; p < players; p++)
        {
            int hall = DefeatStaging.Hall(_data, w, p);
            DefeatStaging.SpawnAt(sim, p, hall, DefeatStaging.Anchor(w, p, hall, at[p].X, at[p].Y));
        }
        await Ticks(sim, 3);
        Check(w.Buildings.Count == players, $"{w.Buildings.Count} buildings placed (want {players})");
    }

    private static (int X, int Y)[] Spots(NavGrid g) => new[] { (g.Width / 4, g.Height / 2), (g.Width * 3 / 4, g.Height / 2), (g.Width / 2, g.Height / 5) };

    // `n` of `player`'s siege units on open cells round `target`'s footprint.
    private async Task Army(Simulation sim, int player, EntityHandle target, int n)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int line = StartBase.UnitOfSlot(_data, w.FactionOf(player), UnitSlot.Siege); // siege: 3x on structures, melee 0.4x
        int cell = w.Buildings.Cell[target.Index];
        int cx = cell % g.Width, cy = cell / g.Width;
        for (int i = 0; i < n; i++)
        {
            int x = Math.Clamp(cx - 3 + i % 5 * 2, 0, g.Width - 1), y = Math.Clamp(cy - 4 - i / 5 * 2, 0, g.Height - 1);
            int c = FlowField.NearestPassable(g, y * g.Width + x);
            sim.Enqueue(Command.SpawnUnit(player, line, g.CellCenter(c % g.Width, c / g.Width)));
        }
        await Ticks(sim, 3);
    }

    // Every live unit of `player` attacks `target`.
    private static void Raze(Simulation sim, int player, EntityHandle target)
    {
        foreach (EntityHandle u in UnitsOf(sim.World, player)) sim.Enqueue(Command.Attack(player, u, target, isBuilding: true));
    }

    private static List<EntityHandle> UnitsOf(World w, int player)
    {
        var list = new List<EntityHandle>();
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == player) list.Add(new EntityHandle(i, u.Generation[i]));
        return list;
    }

    private static EntityHandle BuildingOf(World w, int player)
    {
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++) if (b.Alive[i] && b.Owner[i] == player) return new EntityHandle(i, b.Generation[i]);
        throw new InvalidOperationException($"player {player} has no building");
    }

    private async Task UntilBanner(Simulation sim, string how)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!_main.BannerUp && clock.Elapsed.TotalSeconds < 90) await Frames(1);
        Check(_main.BannerUp && _ended == 1, $"{how}: banner {_main.BannerUp}, ended {_ended} by tick {sim.TickNumber}");
        if (!_main.BannerUp) throw new InvalidOperationException($"{how}: no banner");
        // The staging (M6-V7): every loser had no live unit on its defeat's tick, so the old and the new rule agree on that tick.
        Check(_unitsAtDefeat.Count == _events.Count && _unitsAtDefeat.TrueForAll(n => n == 0), $"{how}: losers' live units on their defeat ticks [{string.Join(", ", _unitsAtDefeat)}] (want all 0)");
    }

    private async Task QuitMatch()
    {
        if (_main.BannerUp) await Click(_main.Banner!.MenuButton);
        else
        {
            _main.PauseMatch();
            await Frames(2); // the Stats button shows after a banner: the menu's rects are laid out on the next frame (FreeForAllTest's rule)
            await Click(_main.Pause!.QuitToMenuButton);
        }
        await Frames(2);
        Check(_main.CurrentMatch == null && !GetTree().Paused, "quit to menu");
    }

    private async Task Ticks(Simulation sim, int n)
    {
        int until = sim.TickNumber + n;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < until && clock.Elapsed.TotalSeconds < 30) await Frames(1);
        Check(sim.TickNumber >= until, $"tick {sim.TickNumber} after 30 s (want {until})");
    }

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

    // ---- input ----

    private async Task Click(Control c)
    {
        Check(c.IsVisibleInTree(), $"{c.Name} is not visible");
        Vector2 at = c.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        await Frames(2);
    }

    private async Task Type(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(2);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Seconds(double seconds)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/matchover-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
