using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V16 (docs/02 "Game flow" 2 and 4): teams on the setup screen, <c>--team</c>, the banner that speaks for the local
/// player's side, and the stats grouped by team, through the shell (<c>Main.tscn</c>).
/// <list type="bullet">
/// <item>Setup: each of the four slots shows a Team list (None, Team 1 .. Team 4; default None, which passes no <c>--team</c>);
/// slots 1 + 2 on Team 1 and 3 + 4 on Team 2 give <c>--team</c> pairs that <see cref="LaunchOptions.Parse"/> reads back, and
/// Start launches a match whose <c>World.TeamOf</c> is 1, 1, 2, 2 (the played seats carry the teams).</item>
/// <item><c>--team 1 1 --team 2 2</c> starts the same way (two sides); junk values are warned about and ignored, a
/// slot past the players is unused.</item>
/// <item>BUG-1062 item 2: Start is disabled, with <c>setup.one_team</c> beside it, while every shown slot is on one team (a
/// hidden slot's team doesn't count); None or a second team on a shown slot enables it.</item>
/// <item>Banner, the local player out first (teams 1, 2, 1, 2): player 3 (an executioner seat that never builds, so it is
/// outside the match-over count, as in <see cref="DefeatStaging"/>) razes player 0's only hall: no banner and no result while
/// the ally (player 2) stands; player 2 then razes player 1's hall: "Victory" for the local player, out but on the winning
/// side. The stats group the rows: "Team 1" (players 0, 2), "Team 2" (players 1, 3).</item>
/// <item>Banner, the local side wiped (teams 1, 1, 2, 2): player 3 razes player 0's hall (no banner: player 1 stands), then
/// player 1's: "Defeat" with the match over (player 2 stands). Stats "Team 1" (0, 1), "Team 2" (2, 3).</item>
/// <item>The row order of a 1v1v2 (teams 0, 0, 1, 1): the team first, the no-team seats after, ungrouped.</item>
/// <item>BUG-1061, the stats screen of mixed matches (opened from the pause menu): a 1v1v2 (teams 1, 1, 0, 0) shows "Team 1"
/// over players 0, 1 and "No team" (<c>match_over.no_team</c>) over 2, 3; teams 0, 1, 0 show "Team 1" over player 1 and
/// "No team" over 0, 2; a free-for-all shows neither. The grid walk fails on a player row under a label of another
/// team (or under none when the match has a team).</item>
/// </list>
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/TeamsTest.tscn</c>; prints "TEAMS TEST PASS" or
/// "TEAMS TEST FAIL: ..." lines. The settings file is a per-process <c>user://</c> one that is never written.
/// </remarks>
public partial class TeamsTest : Node
{
    private static readonly string[] Base = { "--units", "0", "--no-bases", "--mute", "--seed", "2", "--speed", "8" };

    private readonly List<string> _failures = new();
    private readonly List<DefeatEvent> _events = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Main _main = null!;
    private int _ended;
    private string? _shots;

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
            _main.UserArgs = new[] { Main.NoConfineFlag };
            _main.SettingsPath = $"user://teams-test-{OS.GetProcessId()}/settings.cfg";
            AddChild(_main);
            await Frames(2);
            UiRows();
            FlagRows();
            await SetupColumn();
            await TeamFlagStart();
            await OneTeamStart();
            await LocalOutAllyWins();
            await LocalSideWiped();
            RowOrderOfA1v1v2();
            await MixedStats();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"TEAMS TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("TEAMS TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- text and flags ----

    private void UiRows()
    {
        foreach (SetupText k in new[] { SetupText.Team, SetupText.TeamNone, SetupText.TeamItem })
            Check(_ui.Setup(k).Length > 0, $"ui.json setup.{UiText.Key(k)} is empty");
        Check(_ui.MatchOver(MatchOverText.Team).Length > 0 && _ui.LoadGame(LoadText.SeatTeam).Length > 0, "ui.json match_over.team / load.seat_team empty");
        Check(_ui.TeamItem(1) != _ui.TeamItem(2) && _ui.TeamLabel(1) != _ui.TeamLabel(2) && _ui.SeatOnTeam("a", 1) != _ui.SeatOnTeam("a", 2),
            "a team format names no team number");
        Check(_ui.TeamItem(1) == "Team 1" && _ui.TeamLabel(2) == "Team 2" && _ui.SeatOnTeam("Malazan Empire (You)", 1) == "Malazan Empire (You) (T1)",
            $"team texts '{_ui.TeamItem(1)}', '{_ui.TeamLabel(2)}', '{_ui.SeatOnTeam("Malazan Empire (You)", 1)}'");
    }

    private void FlagRows()
    {
        Check(LaunchOptions.HasMatchFlag(new[] { "--team", "1", "1" }) && LaunchOptions.MatchFlags.Contains("--team"), "--team is no match flag");
        LaunchOptions o = LaunchOptions.Parse(new[] { "--players", "4", "--team", "1", "1", "--team", "2", "1", "--team", "3", "2", "--team", "4", "2" });
        Check(Enumerable.Range(0, 4).Select(o.TeamOf).SequenceEqual(new[] { 1, 1, 2, 2 }), $"--team pairs read [{string.Join(", ", Enumerable.Range(0, 4).Select(o.TeamOf))}]");
        Check(o.TeamOf(-1) == 0 && o.TeamOf(4) == 0, "TeamOf out of range");
        // Junk: a team past 4, slot 0 or 5, a padded or signed number, a missing value; a flag is never taken as a value (BUG-0041).
        foreach (string[] junk in new[]
        {
            new[] { "--team", "1", "5" }, new[] { "--team", "0", "1" }, new[] { "--team", "5", "1" }, new[] { "--team", "1", "01" },
            new[] { "--team", "1", "+1" }, new[] { "--team", "1", "-1" }, new[] { "--team", "1" }, new[] { "--team" }, new[] { "--team", "x", "1" },
        })
        {
            LaunchOptions j = LaunchOptions.Parse(junk);
            Check(Enumerable.Range(0, 4).All(s => j.TeamOf(s) == 0), $"junk '{string.Join(' ', junk)}' set a team");
        }
        LaunchOptions next = LaunchOptions.Parse(new[] { "--team", "1", "--seed", "4" });
        Check(next.TeamOf(0) == 0 && next.Seed == 4, "--team without its second value swallowed --seed");
        LaunchOptions last = LaunchOptions.Parse(new[] { "--team", "2", "3", "--team", "2", "0" });
        Check(last.TeamOf(1) == 0, "the last --team for a slot does not win");
        // The seats: SeatsFor carries each player's team; players past the match are left out.
        var seats = SimRunner.SeatsFor(3, _data.Factions.Length, new[] { -1, -1, -1, -1 }, new AiDifficulty?[4], new[] { 1, 0, 1, 2 });
        Check(seats.Length == 3 && seats.Select(s => s.Team).SequenceEqual(new[] { 1, 0, 1 }), $"SeatsFor teams [{string.Join(", ", seats)}]");
        Check(SimRunner.SeatsFor(2, _data.Factions.Length, new[] { -1, -1 }, new AiDifficulty?[4]).All(s => s.Team == 0), "SeatsFor without teams");
    }

    // ---- the setup screen ----

    private async Task SetupColumn()
    {
        _main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = _main.Setup!;
        setup.AddSlot();
        setup.AddSlot();
        await Frames(2);
        string[] items = new[] { _ui.Setup(SetupText.TeamNone) }.Concat(Enumerable.Range(1, LaunchOptions.MaxTeam).Select(_ui.TeamItem)).ToArray();
        for (int s = 0; s < SkirmishSetup.MaxSlots; s++)
        {
            OptionButton list = setup.TeamOptions[s];
            string[] got = Enumerable.Range(0, list.ItemCount).Select(i => list.GetItemText(i)).ToArray();
            Check(got.SequenceEqual(items) && list.Selected == 0 && setup.TeamPick(s) == 0 && list.IsVisibleInTree(),
                $"slot {s + 1}'s team list [{string.Join(", ", got)}], selected {list.Selected}, visible {list.IsVisibleInTree()}");
        }
        string[]? none = setup.MatchArgs();
        Check(none != null && !none.Contains("--team"), $"the default (None) passes --team: {string.Join(' ', none ?? Array.Empty<string>())}");
        // The layout: the team column inside the window, beside the difficulty column.
        Rect2 view = GetViewport().GetVisibleRect();
        foreach (Control c in setup.TeamOptions.Concat<Control>(setup.FactionOptions).Concat(setup.ColourOptions).Append(setup.StartButton))
            Check(view.Grow(0.5f).Encloses(c.GetGlobalRect()), $"setup control {c.Name} {c.GetGlobalRect()} leaves the {view.Size} window");
        Check(setup.TeamOptions[3].GetGlobalRect().Position.X > setup.DifficultyOptions[3]!.GetGlobalRect().End.X, "the team column is not right of the difficulty column");
        await Shot("setup-teams");
        int[] picks = { 1, 1, 2, 2 };
        for (int s = 0; s < 4; s++) setup.TeamOptions[s].Select(picks[s]);
        await Shot("setup-2v2");
        string[] args = setup.MatchArgs()!;
        string joined = string.Join(' ', args);
        Check(joined.Contains("--team 1 1") && joined.Contains("--team 2 1") && joined.Contains("--team 3 2") && joined.Contains("--team 4 2"), $"MatchArgs: {joined}");
        LaunchOptions parsed = LaunchOptions.Parse(args);
        Check(Enumerable.Range(0, 4).Select(parsed.TeamOf).SequenceEqual(picks) && parsed.Players == 4, $"MatchArgs round trip: teams [{string.Join(", ", Enumerable.Range(0, 4).Select(parsed.TeamOf))}]");
        // A mixed pick round-trips too (slot 2 None).
        setup.TeamOptions[1].Select(0);
        LaunchOptions mixed = LaunchOptions.Parse(setup.MatchArgs()!);
        Check(Enumerable.Range(0, 4).Select(mixed.TeamOf).SequenceEqual(new[] { 1, 0, 2, 2 }), "the mixed pick does not round-trip");
        setup.TeamOptions[1].Select(1);
        setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Match? match = _main.CurrentMatch;
        if (!Check(match?.Simulation != null, "Start started no match")) return;
        World w = match!.Simulation!.World;
        Check(Enumerable.Range(0, 4).Select(w.TeamOf).SequenceEqual(picks) && w.AreAllied(0, 1) && w.AreAllied(2, 3) && !w.AreAllied(0, 2)
            && match.Seats.Select(s => s.Team).SequenceEqual(picks) && match.Seats.Skip(1).All(s => s.Controller == SeatController.Ai),
            $"the started match: TeamOf [{string.Join(", ", Enumerable.Range(0, 4).Select(w.TeamOf))}], seats [{string.Join(", ", match.Seats)}]");
        // The right-click's alliance table: player 1 is no enemy of the local player, 2 and 3 are.
        var sel = match.GetNode<SelectionController>("SelectionController");
        Check(sel.Enemies(w).ToArray().SequenceEqual(new[] { false, false, true, true }), "the selection's enemy table");
        await QuitMatch();
    }

    private async Task TeamFlagStart()
    {
        // Two teams (BUG-1062 item 2: a one-team match never ends, so the setup no longer offers one); slot 4 is past --players.
        _main.StartSkirmish(new[] { "--team", "1", "1", "--team", "2", "2", "--team", "4", "2", "--mute", "--seed", "3", "--units", "0" });
        await Frames(2);
        Match? match = _main.CurrentMatch;
        if (!Check(match?.Simulation != null, "--team started no match")) return;
        World w = match!.Simulation!.World;
        Check(w.Config.PlayerCount == 2 && w.TeamOf(0) == 1 && w.TeamOf(1) == 2 && !w.AreAllied(0, 1) && w.IsEnemy(0, 1)
            && match.Seats.Select(s => s.Team).SequenceEqual(new[] { 1, 2 }),
            $"--team 1 1 --team 2 2: {w.Config.PlayerCount} players, TeamOf {w.TeamOf(0)}, {w.TeamOf(1)}");
        await QuitMatch();
    }

    // BUG-1062 item 2: Start is off, with the ui.json line beside it, while every shown slot is on one team; a hidden slot's
    // team doesn't count, and None on any shown slot (a side of its own) or a second team turns Start back on.
    private async Task OneTeamStart()
    {
        _main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = _main.Setup!;
        while (setup.SlotCount < SkirmishSetup.MaxSlots) setup.AddSlot();
        void Pick(int slot, int team)
        {
            setup.TeamOptions[slot].Select(team);
            setup.TeamOptions[slot].EmitSignal(OptionButton.SignalName.ItemSelected, (long)team); // as a click on the item
        }
        for (int s = 0; s < SkirmishSetup.MaxSlots; s++) Pick(s, 0);
        await Frames(1);
        Check(!setup.StartButton.Disabled && !setup.OneTeamLabel.Visible && !setup.OneTeam, "a free-for-all: Start off or the one-team line shown");
        for (int s = 0; s < 3; s++) Pick(s, 1);
        Pick(3, 2);
        await Frames(1);
        Check(!setup.StartButton.Disabled && !setup.OneTeamLabel.Visible, "teams 1, 1, 1, 2: Start off or the one-team line shown");
        setup.RemoveSlot(); // slot 4 (team 2) hidden: the shown slots are all team 1
        await Frames(2);
        Rect2 view = GetViewport().GetVisibleRect();
        Check(setup.OneTeam && setup.StartButton.Disabled && setup.OneTeamLabel.IsVisibleInTree()
            && setup.OneTeamLabel.Text == _ui.Setup(SetupText.OneTeam) && _ui.Setup(SetupText.OneTeam).Length > 0
            && view.Grow(0.5f).Encloses(setup.OneTeamLabel.GetGlobalRect()) && view.Grow(0.5f).Encloses(setup.StartButton.GetGlobalRect()),
            $"teams 1, 1, 1 (slot 4 hidden on team 2): OneTeam {setup.OneTeam}, Start disabled {setup.StartButton.Disabled}, line visible "
            + $"{setup.OneTeamLabel.IsVisibleInTree()} '{setup.OneTeamLabel.Text}' at {setup.OneTeamLabel.GetGlobalRect()}");
        await Shot("setup-one-team");
        setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed); // Pressed reaching it anyway starts nothing
        await Frames(2);
        Check(_main.CurrentMatch == null, "Start with every shown slot on one team started a match");
        Pick(2, 0);
        await Frames(1);
        Check(!setup.StartButton.Disabled && !setup.OneTeamLabel.Visible, "teams 1, 1, None: Start off or the one-team line shown");
        Pick(2, 1);
        setup.AddSlot(); // slot 4 shown again, still team 2
        await Frames(1);
        Check(!setup.StartButton.Disabled && !setup.OneTeamLabel.Visible, "slot 4 (team 2) shown again: Start off or the one-team line shown");
        for (int s = 0; s < SkirmishSetup.MaxSlots; s++) Pick(s, 0);
        _main.ShowMenu();
        await Frames(1);
    }

    // ---- the banner per side ----

    private async Task LocalOutAllyWins()
    {
        Match match = Start(4, "--team", "1", "1", "--team", "2", "2", "--team", "3", "1", "--team", "4", "2");
        Simulation sim = match.Simulation!;
        World w = sim.World;
        Check(Enumerable.Range(0, 4).Select(w.TeamOf).SequenceEqual(new[] { 1, 2, 1, 2 }), "ally wins: teams");
        await Halls(sim, 3); // player 3 never builds: the executioner
        EntityHandle own = BuildingOf(w, 0);
        await Army(sim, 3, own, 6);
        Raze(sim, 3, own);
        await UntilDefeated(sim, 0);
        await Ticks(sim, 10);
        Check(!_main.BannerUp && match.Outcome == MatchOutcome.None && !sim.MatchOver && _ended == 0 && Match.ResultFor(w, 0) == MatchOutcome.None,
            $"ally wins, the local player out: banner {_main.BannerUp}, outcome {match.Outcome}, MatchOver {sim.MatchOver} (want none: the ally stands)");
        EntityHandle enemy = BuildingOf(w, 1);
        await Army(sim, 2, enemy, 6);
        Raze(sim, 2, enemy);
        await UntilBanner(sim, "ally wins");
        MatchOverBanner banner = _main.Banner!;
        Check(banner.Title.Text == _ui.MatchOver(MatchOverText.Victory) && match.Outcome == MatchOutcome.Victory && sim.MatchOver && w.IsDefeated(0) && !w.IsDefeated(2),
            $"ally wins: title '{banner.Title.Text}', outcome {match.Outcome}, MatchOver {sim.MatchOver}, defeated 0 {w.IsDefeated(0)} / 2 {w.IsDefeated(2)}");
        await Click(banner.StatsButton);
        CheckGrouped(match, new[] { 0, 2, 1, 3 }, new[] { 1, 2 }, noTeam: false, "ally wins");
        await Shot("stats-ally-wins");
        await Type(Key.Escape);
        await QuitMatch();
    }

    private async Task LocalSideWiped()
    {
        Match match = Start(4, "--team", "1", "1", "--team", "2", "1", "--team", "3", "2", "--team", "4", "2");
        Simulation sim = match.Simulation!;
        World w = sim.World;
        await Halls(sim, 3);
        EntityHandle own = BuildingOf(w, 0);
        await Army(sim, 3, own, 6);
        Raze(sim, 3, own);
        await UntilDefeated(sim, 0);
        await Ticks(sim, 10);
        Check(!_main.BannerUp && match.Outcome == MatchOutcome.None && !sim.MatchOver && _ended == 0,
            $"side wiped, player 0 down: banner {_main.BannerUp}, outcome {match.Outcome} (want none: player 1 stands)");
        EntityHandle ally = BuildingOf(w, 1);
        await Army(sim, 3, ally, 6);
        Raze(sim, 3, ally);
        await UntilBanner(sim, "side wiped");
        MatchOverBanner banner = _main.Banner!;
        Check(banner.Title.Text == _ui.MatchOver(MatchOverText.Defeat) && match.Outcome == MatchOutcome.Defeat && sim.MatchOver && w.IsDefeated(1) && !w.IsDefeated(2)
            && banner.ContinueButton.Visible,
            $"side wiped: title '{banner.Title.Text}', outcome {match.Outcome}, MatchOver {sim.MatchOver}");
        await Click(banner.StatsButton);
        CheckGrouped(match, new[] { 0, 1, 2, 3 }, new[] { 1, 2 }, noTeam: false, "side wiped");
        await Type(Key.Escape);
        await QuitMatch();
    }

    // The open stats screen: the rows in `order` under one label per team of `teams` (and, with `noTeam`, the no-team seats
    // under "No team"), every counter right, the screen fits.
    private void CheckGrouped(Match match, int[] order, int[] teams, bool noTeam, string how)
    {
        MatchStats stats = _main.Stats!;
        World w = match.Simulation!.World;
        Check(stats.Visible && stats.RowCount == order.Length && stats.DisplayOrder.SequenceEqual(order),
            $"{how}: stats rows [{string.Join(", ", stats.DisplayOrder)}] (want [{string.Join(", ", order)}])");
        string[] labels = teams.Select(_ui.TeamLabel).ToArray();
        Check(stats.TeamLabels.Select(l => l.Text).SequenceEqual(labels), $"{how}: team labels [{string.Join(", ", stats.TeamLabels.Select(l => l.Text))}]");
        Check(noTeam == (stats.NoTeamLabel != null) && (stats.NoTeamLabel == null || stats.NoTeamLabel.Text == _ui.MatchOver(MatchOverText.NoTeam)),
            $"{how}: no-team label '{stats.NoTeamLabel?.Text}' (want one: {noTeam})");
        // Each label sits above its group's first row and below the previous group's last; BUG-1061: every player row sits
        // under the label of its own group (a no-team seat under "No team"), or under none when nobody has a team.
        var grid = (GridContainer)stats.Table;
        var cells = grid.GetChildren().OfType<Control>().ToList();
        int columns = grid.Columns, row = 1; // row 0 holds the headings
        int k = 0, label = 0, group = -1; // group: the team of the label above (0 = "No team"), -1 = no label yet
        bool walkedNoTeam = false;
        while (k < order.Length && (row + 1) * columns <= cells.Count)
        {
            Control first = cells[row * columns];
            if (label < stats.TeamLabels.Count && first == stats.TeamLabels[label])
            {
                Check(w.TeamOf(order[k]) == teams[label], $"{how}: label '{stats.TeamLabels[label].Text}' heads player {order[k]} of team {w.TeamOf(order[k])}");
                group = teams[label];
                label++;
            }
            else if (stats.NoTeamLabel != null && first == stats.NoTeamLabel)
            {
                Check(!walkedNoTeam && label == labels.Length && w.TeamOf(order[k]) == 0, $"{how}: the no-team label heads player {order[k]} of team {w.TeamOf(order[k])}");
                walkedNoTeam = true;
                group = 0;
            }
            else
            {
                Check(first is HBoxContainer && ((Label)first.GetChild(1)).Text == stats.FactionName(order[k]),
                    $"{how}: grid row {row} is not player {order[k]}'s");
                int team = w.TeamOf(order[k]);
                Check(group == -1 ? teams.Length == 0 : team == group,
                    $"{how}: player {order[k]} (team {team}) shows under the {(group < 0 ? "no" : group == 0 ? "'No team'" : $"team {group}")} label");
                k++;
            }
            row++;
        }
        Check(k == order.Length && label == labels.Length && walkedNoTeam == noTeam, $"{how}: walked {k} rows, {label} labels, no-team label {walkedNoTeam} in the grid");
        if (stats.NoTeamLabel != null)
            Check(GetViewport().GetVisibleRect().Grow(0.5f).Encloses(stats.NoTeamLabel.GetGlobalRect()), $"{how}: the no-team label {stats.NoTeamLabel.GetGlobalRect()} off screen");
        foreach (string f in StatsScreenCheck.Columns(stats, w, _ui, how)) Check(false, f);
        foreach (string f in StatsScreenCheck.Graph(stats, match, drawn: false, how)) Check(false, f);
        foreach (string f in StatsScreenCheck.Fits(stats, GetViewport().GetVisibleRect(), how)) Check(false, f);
        foreach (Label l in stats.TeamLabels)
            Check(GetViewport().GetVisibleRect().Grow(0.5f).Encloses(l.GetGlobalRect()), $"{how}: label '{l.Text}' {l.GetGlobalRect()} off screen");
    }

    private void RowOrderOfA1v1v2()
    {
        var seats = System.Collections.Immutable.ImmutableArray.Create(
            new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 0), new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 0),
            new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 1), new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 1));
        var sim = new Simulation(new SimConfig(1, 4, 64, 64) { Data = _data, Map = MapGenParams.Default with { Forests = 0, GoldMines = 0, StartLocations = 4 }, Seats = seats });
        Check(MatchStats.RowOrder(sim.World).SequenceEqual(new[] { 2, 3, 0, 1 }), $"1v1v2 row order [{string.Join(", ", MatchStats.RowOrder(sim.World))}]");
        var ffa = new Simulation(new SimConfig(1, 3, 64, 64) { Data = _data, Map = MapGenParams.Default with { Forests = 0, GoldMines = 0, StartLocations = 3 } });
        Check(MatchStats.RowOrder(ffa.World).SequenceEqual(new[] { 0, 1, 2 }), "a free-for-all's row order is the player order");
        var three = System.Collections.Immutable.ImmutableArray.Create(
            new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 2), new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 0),
            new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 1), new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 2));
        var odd = new Simulation(new SimConfig(1, 4, 64, 64) { Data = _data, Map = MapGenParams.Default with { Forests = 0, GoldMines = 0, StartLocations = 4 }, Seats = three });
        Check(MatchStats.RowOrder(odd.World).SequenceEqual(new[] { 2, 0, 3, 1 }), $"teams 2, 0, 1, 2 row order [{string.Join(", ", MatchStats.RowOrder(odd.World))}]");
    }

    // BUG-1061: mixed matches' stats screens, opened from the pause menu (no halls, so nobody is out and the counters stay put).
    private async Task MixedStats()
    {
        await StatsOf(4, new[] { "--team", "1", "1", "--team", "2", "1" }, new[] { 0, 1, 2, 3 }, new[] { 1 }, noTeam: true, "1v1v2", "stats-1v1v2");
        await StatsOf(3, new[] { "--team", "2", "1" }, new[] { 1, 0, 2 }, new[] { 1 }, noTeam: true, "teams 0, 1, 0", "stats-3-slots");
        await StatsOf(3, Array.Empty<string>(), new[] { 0, 1, 2 }, Array.Empty<int>(), noTeam: false, "free-for-all", null);
        await StatsOf(4, new[] { "--team", "1", "1", "--team", "2", "1", "--team", "3", "2", "--team", "4", "2" }, new[] { 0, 1, 2, 3 }, new[] { 1, 2 }, noTeam: false, "2v2 (paused)", null);
    }

    private async Task StatsOf(int players, string[] teams, int[] order, int[] labels, bool noTeam, string how, string? shot)
    {
        Match match = Start(players, teams);
        await Frames(2);
        _main.PauseMatch();
        await Frames(1);
        _main.ShowStats();
        await Frames(2);
        CheckGrouped(match, order, labels, noTeam, how);
        if (shot != null) await Shot(shot);
        _main.CloseStats();
        await QuitMatch();
    }

    // ---- set-up (commands only), as FreeForAllTest ----

    private Match Start(int players, params string[] extra)
    {
        var args = Base.Concat(new[] { "--players", players.ToString() }).ToList();
        for (int s = 1; s <= players; s++) args.AddRange(new[] { "--faction", s.ToString(), "malazan" });
        _main.StartSkirmish(args.Concat(extra).ToArray());
        Match? match = _main.CurrentMatch;
        if (match?.Simulation == null) throw new InvalidOperationException("the shell started no match");
        _events.Clear();
        _ended = 0;
        match.Ended += _ => _ended++;
        match.GetNode<SimRunner>("SimRunner").Ticked += s => _events.AddRange(s.World.DefeatEvents);
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        return match;
    }

    // One finished training hall for each of players 0 .. halls - 1 (west, east, north); no unit.
    private async Task Halls(Simulation sim, int halls)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        (int X, int Y)[] at = { (g.Width / 4, g.Height / 2), (g.Width * 3 / 4, g.Height / 2), (g.Width / 2, g.Height / 5), (g.Width / 2, g.Height * 4 / 5) };
        for (int p = 0; p < halls; p++)
        {
            int hall = DefeatStaging.Hall(_data, w, p);
            DefeatStaging.SpawnAt(sim, p, hall, DefeatStaging.Anchor(w, p, hall, at[p].X, at[p].Y));
        }
        await Ticks(sim, 3);
        Check(w.Buildings.Count == halls, $"{w.Buildings.Count} halls placed (want {halls})");
    }

    private async Task Army(Simulation sim, int player, EntityHandle target, int n)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int siege = StartBase.UnitOfSlot(_data, w.FactionOf(player), UnitSlot.Siege);
        int cell = w.Buildings.Cell[target.Index];
        BuildingDef def = _data.Buildings[w.Buildings.TypeId[target.Index]];
        int cx = cell % g.Width, cy = cell / g.Width, level = g.LevelAt(cx, cy), placed = 0;
        for (int r = 4; r <= 5 && placed < n; r++)
            for (int y = cy - r; y <= cy + def.FootprintHeight - 1 + r && placed < n; y++)
                for (int x = cx - r; x <= cx + def.FootprintWidth - 1 + r && placed < n; x++)
                {
                    bool ring = x == cx - r || x == cx + def.FootprintWidth - 1 + r || y == cy - r || y == cy + def.FootprintHeight - 1 + r;
                    if (!ring || (x + y) % 2 != 0 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) continue;
                    sim.Enqueue(Command.SpawnUnit(player, siege, g.CellCenter(x, y)));
                    placed++;
                }
        Check(placed == n, $"only {placed} of {n} shooters fit round player {w.Buildings.Owner[target.Index]}'s hall");
        await Ticks(sim, 3);
    }

    // Every unit of `player` attacks `target` (the earlier battery too, so it never idles near another hall).
    private static void Raze(Simulation sim, int player, EntityHandle target)
    {
        foreach (EntityHandle u in DefeatStaging.UnitsOf(sim.World, player)) sim.Enqueue(Command.Attack(player, u, target, isBuilding: true));
    }

    private static EntityHandle BuildingOf(World w, int player)
    {
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++) if (b.Alive[i] && b.Owner[i] == player) return new EntityHandle(i, b.Generation[i]);
        throw new InvalidOperationException($"player {player} has no building");
    }

    private async Task UntilDefeated(Simulation sim, int player)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!sim.World.IsDefeated(player) && clock.Elapsed.TotalSeconds < 90) await Frames(1);
        Check(sim.World.IsDefeated(player), $"player {player} not defeated by tick {sim.TickNumber}");
    }

    private async Task UntilBanner(Simulation sim, string how)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!_main.BannerUp && clock.Elapsed.TotalSeconds < 90) await Frames(1);
        Check(_main.BannerUp && _ended == 1, $"{how}: banner {_main.BannerUp}, ended {_ended} by tick {sim.TickNumber}");
        if (!_main.BannerUp) throw new InvalidOperationException($"{how}: no banner by tick {sim.TickNumber}");
    }

    private async Task QuitMatch()
    {
        if (_main.BannerUp) await Click(_main.Banner!.MenuButton);
        else _main.QuitToMenu();
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

    // Windowed with `-- --shots <dir>`: the viewport as teams-<name>.png.
    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/teams-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
