using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-V16 (teams in the view), the cases TeamsTest / TeamFilesTest leave out, through the shell (<c>Main.tscn</c>).
/// <list type="bullet">
/// <item>Defeat before the match is over: teams 1, 1, 0, 0 (the local side vs two free-for-all seats); player 3 razes player 0's
/// hall (no banner: the ally stands), then player 1's: "Defeat" while players 2 and 3 still fight (MatchOver false). The stats:
/// "Team 1" over players 0, 1, then 2, 3 ungrouped.</item>
/// <item>Three slots, one teamed (teams 1, 0, 0): player 2 razes player 0's hall: "Defeat" at once (no ally), match not over;
/// the stats: one "Team 1" label over player 0, players 1 and 2 after it.</item>
/// <item>The pickers in a live 2v2 (teams 1, 1, 2, 2, no fog): the 3D right-click on an ally's unit and on an ally's hall is a
/// Move (the recorder's command), on an enemy's an Attack; the minimap's right-click on an ally's dot is a Move, on an
/// enemy's dot an Attack.</item>
/// <item>Old files: a format-6 rewrite of a fresh 1v1 recording lists without a team mark and plays to every checkpoint;
/// a format-2 rewrite of a save lists and is refused on Load with the version reason; a format-3 save with a team byte of 9
/// or 255 lists as unreadable (Delete only) and nothing crashes.</item>
/// <item>BUG-1061 re-check: the stats grid read row by row (team labels, "No team", player rows, label rows otherwise empty) for
/// the two banner screens above and, from the pause menu, teams 0, 1, 2, 3 (opened twice), 4, 3, 2, 1, 0, 0, 0, 2 and a
/// free-for-all (opened twice). The four-label screens fit (BUG-1063, fixed: the graph shrinks past two label rows).</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V16Test.tscn</c>; prints "QA M6-V16 TEST PASS" or
/// "QA M6-V16 TEST FAIL: ..." lines. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the two stats screens.
/// </summary>
public partial class QaM6V16Test : Node
{
    private static readonly string[] Base = { "--units", "0", "--no-bases", "--mute", "--seed", "2", "--speed", "8" };

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Main _main = null!;
    private int _ended;
    private string? _shots;
    private string _root = "", _replays = "", _saves = "";

    public override async void _Ready()
    {
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
            _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m6-v16-{OS.GetProcessId()}"));
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            _replays = Path.Combine(_root, "replays");
            _saves = Path.Combine(_root, "saves");
            Directory.CreateDirectory(_replays);
            Directory.CreateDirectory(_saves);
            _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            _main.UserArgs = new[] { Main.NoConfineFlag };
            _main.SettingsPath = $"user://qa-m6-v16-{OS.GetProcessId()}/settings.cfg";
            AddChild(_main);
            await Frames(2);
            await DefeatBeforeMatchOver();
            await ThreeSlotsOneTeamed();
            await AllyPicks();
            await OldFiles();
            await StatsLayouts();
            try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6-V16 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V16 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- the banner per side, before the match is over ----

    private async Task DefeatBeforeMatchOver()
    {
        Match match = Start(4, "--team", "1", "1", "--team", "2", "1");
        Simulation sim = match.Simulation!;
        World w = sim.World;
        Check(Enumerable.Range(0, 4).Select(w.TeamOf).SequenceEqual(new[] { 1, 1, 0, 0 }), "1v1v2: teams");
        await Halls(sim, 4);
        EntityHandle own = BuildingOf(w, 0);
        await Army(sim, 3, own, 6);
        Raze(sim, 3, own);
        await UntilDefeated(sim, 0);
        await Ticks(sim, 10);
        Check(!_main.BannerUp && match.Outcome == MatchOutcome.None && _ended == 0, $"1v1v2, player 0 out: banner {_main.BannerUp}, outcome {match.Outcome} (want none: the ally stands)");
        EntityHandle ally = BuildingOf(w, 1);
        await Army(sim, 3, ally, 6);
        Raze(sim, 3, ally);
        await UntilBanner(sim, "1v1v2 side out");
        MatchOverBanner banner = _main.Banner!;
        Check(banner.Title.Text == _ui.MatchOver(MatchOverText.Defeat) && match.Outcome == MatchOutcome.Defeat && !sim.MatchOver
            && w.IsDefeated(0) && w.IsDefeated(1) && !w.IsDefeated(2) && !w.IsDefeated(3),
            $"1v1v2 side out: title '{banner.Title.Text}', outcome {match.Outcome}, MatchOver {sim.MatchOver} (want Defeat, not over: 2 and 3 fight on)");
        Check(banner.ContinueButton.Visible, "1v1v2 side out: no Continue while two others fight on");
        await Click(banner.StatsButton);
        MatchStats stats = _main.Stats!;
        Check(stats.Visible && stats.DisplayOrder.SequenceEqual(new[] { 0, 1, 2, 3 }) && stats.TeamLabels.Select(l => l.Text).SequenceEqual(new[] { _ui.TeamLabel(1) }),
            $"1v1v2 stats: rows [{string.Join(", ", stats.DisplayOrder)}], labels [{string.Join(", ", stats.TeamLabels.Select(l => l.Text))}]");
        foreach (string f in StatsScreenCheck.Fits(stats, GetViewport().GetVisibleRect(), "1v1v2")) Check(false, f);
        CheckLayout(stats, "T1 P0 P1 N P2 P3", "1v1v2 (banner)"); // BUG-1061
        await Shot("stats-1v1v2");
        await Type(Key.Escape);
        await QuitMatch();
    }

    private async Task ThreeSlotsOneTeamed()
    {
        Match match = Start(3, "--team", "1", "1");
        Simulation sim = match.Simulation!;
        World w = sim.World;
        Check(Enumerable.Range(0, 3).Select(w.TeamOf).SequenceEqual(new[] { 1, 0, 0 }) && !w.HasAlly(0), "3 slots: teams");
        await Halls(sim, 3);
        EntityHandle own = BuildingOf(w, 0);
        await Army(sim, 2, own, 6);
        Raze(sim, 2, own);
        await UntilBanner(sim, "3 slots, lone team");
        MatchOverBanner banner = _main.Banner!;
        Check(banner.Title.Text == _ui.MatchOver(MatchOverText.Defeat) && match.Outcome == MatchOutcome.Defeat && !sim.MatchOver,
            $"3 slots: title '{banner.Title.Text}', outcome {match.Outcome}, MatchOver {sim.MatchOver}");
        await Click(banner.StatsButton);
        MatchStats stats = _main.Stats!;
        Check(stats.DisplayOrder.SequenceEqual(new[] { 0, 1, 2 }) && stats.TeamLabels.Count == 1 && stats.TeamLabels[0].Text == _ui.TeamLabel(1),
            $"3 slots stats: rows [{string.Join(", ", stats.DisplayOrder)}], labels [{string.Join(", ", stats.TeamLabels.Select(l => l.Text))}]");
        CheckLayout(stats, "T1 P0 N P1 P2", "3 slots (banner)"); // BUG-1061
        await Shot("stats-3-slots");
        await Type(Key.Escape);
        await QuitMatch();
    }

    // ---- the pickers: a right-click on an ally is a Move ----

    private async Task AllyPicks()
    {
        Match match = Start(4, "--no-fog", "--no-combat", "--speed", "1", "--team", "1", "1", "--team", "2", "1", "--team", "3", "2", "--team", "4", "2");
        Simulation sim = match.Simulation!;
        World w = sim.World;
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        var sel = match.GetNode<SelectionController>("SelectionController");
        var camera = match.GetNode<RtsCamera>("RtsCamera");
        var mini = match.GetNode<Minimap>("Hud/Minimap");
        Check(runner.Recorder != null, "the match records no commands");
        await Halls(sim, 4);
        NavGrid g = w.NavGrid;
        int line = StartBase.UnitOfSlot(_data, w.FactionOf(0), UnitSlot.Line);
        // Own, ally and enemy units in a row in the middle of the map, 10 m apart.
        float cx = g.Width / 2 * MapConstants.CellSize, cy = g.Height / 2 * MapConstants.CellSize;
        var at = new[] { new System.Numerics.Vector2(cx - 10f, cy), new System.Numerics.Vector2(cx, cy), new System.Numerics.Vector2(cx + 10f, cy) };
        int[] owners = { 0, 1, 2 };
        for (int k = 0; k < 3; k++) sim.Enqueue(Command.SpawnUnit(owners[k], line, at[k]));
        await Ticks(sim, 2);
        EntityHandle mine = DefeatStaging.UnitAt(w, 0, at[0]), allyUnit = DefeatStaging.UnitAt(w, 1, at[1]), enemyUnit = DefeatStaging.UnitAt(w, 2, at[2]);
        if (!Check(w.Units.IsAlive(mine) && w.Units.IsAlive(allyUnit) && w.Units.IsAlive(enemyUnit), "the three units did not spawn")) { await QuitMatch(); return; }
        camera.SetFocus(cx, cy);
        await Frames(3);
        Check(sel.Enemies(w).ToArray().SequenceEqual(new[] { false, false, true, true }), "enemy table");

        // 3D: ally's unit.
        sel.Selection.Clear();
        sel.Selection.Add(mine);
        if (Check(sel.TryScreenPosition(allyUnit.Index, out Vector2 allyPx), "the ally's unit is off screen"))
        {
            Check(UnitUnder(camera, runner, w, allyPx) == allyUnit.Index, "the ally's unit is not the first body under its pixel");
            Check(!sel.EnemyAt(allyPx, out _, out _), "EnemyAt says the ally's unit is an enemy");
            CommandKind kind = await Order(sim, runner, () => sel.CommandAt(allyPx, false));
            Check(kind == CommandKind.Move, $"right-click on the ally's unit gave {kind} (want Move)");
        }
        if (Check(sel.TryScreenPosition(enemyUnit.Index, out Vector2 enemyPx), "the enemy's unit is off screen"))
        {
            bool isEnemy = sel.EnemyAt(enemyPx, out EntityHandle t, out bool isB);
            Check(isEnemy && !isB && t == enemyUnit, $"EnemyAt on the enemy's unit: {isEnemy} {t}");
            CommandKind kind = await Order(sim, runner, () => sel.CommandAt(enemyPx, false));
            Check(kind == CommandKind.Attack, $"right-click on the enemy's unit gave {kind} (want Attack)");
        }

        // 3D: ally's hall (player 1) and enemy's hall (player 2).
        foreach ((int owner, CommandKind want) in new[] { (1, CommandKind.Move), (2, CommandKind.Attack) })
        {
            EntityHandle hall = BuildingOf(w, owner);
            BuildingDef def = _data.Buildings[w.Buildings.TypeId[hall.Index]];
            System.Numerics.Vector2 c = StartBase.FootprintCenter(g, def, w.Buildings.Cell[hall.Index]);
            camera.SetFocus(c.X, c.Y);
            await Frames(3);
            float y = TerrainHeight.At(w.Heightmap, c.X, c.Y);
            Vector2 px = default;
            bool found = false;
            foreach (float h in new[] { 1.5f, 2.5f, 0.8f })
            {
                px = camera.UnprojectPosition(new Vector3(c.X, y + h, c.Y));
                if (sel.ContextTarget(px, out _, out int hit) && hit == hall.Index) { found = true; break; }
            }
            if (!Check(found, $"player {owner}'s hall has no pixel")) continue;
            Check(sel.EnemyAt(px, out _, out bool b) == (want == CommandKind.Attack) && (want != CommandKind.Attack || b), $"EnemyAt on player {owner}'s hall");
            sel.Selection.Clear();
            sel.Selection.Add(mine);
            CommandKind kind = await Order(sim, runner, () => sel.CommandAt(px, false));
            Check(kind == want, $"right-click on player {owner}'s hall gave {kind} (want {want})");
        }

        // Minimap: ally's dot and enemy's dot, at their drawn positions.
        mini.Refresh(sim);
        foreach ((EntityHandle h, CommandKind want) in new[] { (allyUnit, CommandKind.Move), (enemyUnit, CommandKind.Attack) })
        {
            System.Numerics.Vector2 p = w.Units.Position[h.Index];
            sel.Selection.Clear();
            sel.Selection.Add(mine);
            CommandKind said = CommandKind.Noop;
            CommandKind kind = await Order(sim, runner, () => { said = mini.CommandAt(p, false); return true; });
            Check(said == want && kind == want, $"minimap right-click on player {w.Units.Owner[h.Index]}'s dot: said {said}, recorded {kind} (want {want})");
            mini.Refresh(sim);
        }
        await QuitMatch();
    }

    // The live unit whose body the ray at the pixel meets first (the ViewApi pick, independent of the controller).
    private static int UnitUnder(RtsCamera camera, SimRunner runner, World w, Vector2 px)
    {
        UnitStore u = w.Units;
        Vector3 o = camera.ProjectRayOrigin(px), d = camera.ProjectRayNormal(px);
        return UnitPicker.PickRay(u.Alive, u.PrevPosition, u.Position, u.Radius, w.Heightmap, (float)runner.Alpha, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), UnitViews.ExtraBodyHeight, out _);
    }

    // Runs `order`, ticks, and returns the kind of the last command player 0 sent on that tick (Noop for none).
    private async Task<CommandKind> Order(Simulation sim, SimRunner runner, Func<bool> order)
    {
        int before = runner.Recorder!.CommandCount;
        order();
        await Ticks(sim, 2);
        return LastKind(runner.Recorder, before);
    }

    private static CommandKind LastKind(ReplayRecorder rec, int from)
    {
        CommandKind kind = CommandKind.Noop;
        for (int i = from; i < rec.CommandCount; i++)
        {
            Command c = rec.CommandAt(i);
            if (c.Player == 0 && c.Kind is CommandKind.Move or CommandKind.Attack or CommandKind.AttackMove) kind = c.Kind;
        }
        return kind;
    }

    // ---- old files ----

    private async Task OldFiles()
    {
        _main.ReplayDir = _replays;
        _main.SaveDir = _saves;
        _main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = 0.25;
        _main.StartSkirmish(new[] { "--skirmish", "--players", "2", "--seed", "11", "--mute" });
        Match? match = _main.CurrentMatch;
        if (!Check(match?.Simulation != null, "the 1v1 did not start")) return;
        Simulation sim = match!.Simulation!;
        string? replayPath = _main.ReplayPath;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < 150 && clock.Elapsed.TotalSeconds < 60) await Frames(1);
        _main.PauseMatch();
        await Frames(1);
        Check(_main.SaveGame(), "the 1v1 did not save");
        string? savePath = _main.LastSavePath;
        _main.Resume();
        while (sim.TickNumber < 300 && clock.Elapsed.TotalSeconds < 120) await Frames(1);
        _main.QuitToMenu();
        await Frames(2);
        _main.BeforeStart = null;
        if (!Check(replayPath != null && File.Exists(replayPath) && savePath != null && File.Exists(savePath), $"files: {replayPath}, {savePath}")) return;

        // Format 6: the same recording, version 6, four-token seat lines, resealed.
        string seven = File.ReadAllText(replayPath!);
        string body = seven[..seven.LastIndexOf("checksum ", StringComparison.Ordinal)];
        var sb = new StringBuilder();
        foreach (string l in body.Split('\n'))
        {
            if (l.Length == 0) continue;
            if (l == "rts-replay 7") sb.Append("rts-replay 6\n");
            else if (l.StartsWith("seat ", StringComparison.Ordinal)) sb.Append(string.Join(' ', l.Split(' ').Take(5))).Append('\n');
            else sb.Append(l).Append('\n');
        }
        string sixBody = sb.ToString();
        string six = Path.Combine(_replays, "20261105-080000-seed11.replay");
        File.WriteAllText(six, sixBody + $"checksum {Fnv(Encoding.ASCII.GetBytes(sixBody)):X16}\n");
        Check(ReplayFormat.TryReadFile(six, out Replay? r6) == ReplayError.None && r6!.FormatVersion == 6, "the format-6 rewrite does not read in the sim");
        Check(ReplayStore.TryReadHeader(six, out ReplayEntry? e6) && e6!.Seats.All(s => s.Team == 0), "the format-6 rewrite does not list");

        // Saves: format 2 (no team bytes), and format 3 with a team byte of 9 and 255.
        byte[] save = File.ReadAllBytes(savePath!);
        int teamsAt = TeamByteOffset(save);
        if (Check(teamsAt > 0, "no team byte offset found in the save"))
        {
            byte[] two = save.Take(teamsAt).Concat(save.Skip(teamsAt + 2)).ToArray();
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(two.AsSpan(SaveFormat.MagicLength), 2);
            File.WriteAllBytes(Path.Combine(_saves, "20261105-080100-seed11.sav"), two);
            foreach ((byte v, string name) in new[] { ((byte)9, "20261105-080200-seed11.sav"), ((byte)255, "20261105-080300-seed11.sav") })
            {
                byte[] bad = (byte[])save.Clone();
                bad[teamsAt + 1] = v;
                File.WriteAllBytes(Path.Combine(_saves, name), bad);
            }
        }
        _main.ShowLoadGame();
        await Frames(2);
        LoadGameScreen load = _main.LoadScreen!;
        int row6 = load.RowOf(six);
        Check(row6 >= 0 && load.KindOf(row6) == LoadGameScreen.RowKind.Replay && !load.List.GetRoot().GetChild(row6).GetText(2).Contains("(T", StringComparison.Ordinal),
            $"format-6 row {row6} kind {load.KindOf(row6)}");
        string twoPath = Path.Combine(_saves, "20261105-080100-seed11.sav");
        int row2 = load.RowOf(twoPath);
        Check(row2 >= 0 && load.KindOf(row2) == LoadGameScreen.RowKind.Save, $"format-2 save row {row2} kind {load.KindOf(row2)}");
        foreach (string name in new[] { "20261105-080200-seed11.sav", "20261105-080300-seed11.sav" })
        {
            int r = load.RowOf(Path.Combine(_saves, name));
            Check(r >= 0 && load.KindOf(r) == LoadGameScreen.RowKind.UnreadableSave, $"{name}: row {r} kind {load.KindOf(r)} (want an unreadable save)");
            if (r >= 0)
            {
                load.Select(r);
                Check(load.LoadButton.Disabled && !load.DeleteButton.Disabled, $"{name}: Load {(load.LoadButton.Disabled ? "off" : "on")}, Delete {(load.DeleteButton.Disabled ? "off" : "on")}");
            }
        }
        // Load the format-2 save: refused with the version reason, the screen stays.
        if (row2 >= 0 && load.Select(row2) && !load.LoadButton.Disabled)
        {
            load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            Check(_main.CurrentMatch == null && load.Visible && load.Note.Text.Length > 0, $"format-2 save Load: match {_main.CurrentMatch != null}, note '{load.Note.Text}'");
            GD.Print($"  format-2 save refusal note: '{load.Note.Text}'");
        }
        // Watch the format-6 replay: every checkpoint equal.
        row6 = load.RowOf(six);
        if (Check(load.Select(row6) && !load.WatchButton.Disabled, "the format-6 row can't be watched"))
        {
            load.WatchButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            Match? watched = _main.CurrentMatch;
            if (Check(watched is { IsPlayback: true }, $"Watch on the format-6 replay started no playback (note '{load.Note.Text}')"))
            {
                SimRunner runner = watched!.GetNode<SimRunner>("SimRunner");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (runner.PlaybackState == PlaybackState.Playing && watch.Elapsed.TotalSeconds < 120) await Frames(1);
                Check(runner.PlaybackState == PlaybackState.Ended && runner.PlaybackError == ReplayError.None && runner.CheckpointsCompared == runner.Playback!.Checkpoints.Length
                    && runner.CheckpointsCompared > 0, $"format-6 playback {runner.PlaybackState} ({runner.PlaybackError}), {runner.CheckpointsCompared} checkpoints");
                if (_main.Banner != null && _main.BannerUp) _main.Banner.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
                else _main.QuitToMenu();
                await Frames(2);
            }
        }
    }

    // The offset of the save's first team byte: the one offset where writing 1 reads as player 0's team 1.
    private static int TeamByteOffset(byte[] save)
    {
        int end = Math.Min(save.Length, SaveSummary.MaxHeaderBytes);
        for (int i = 0; i < end; i++)
        {
            if (save[i] != 0) continue;
            byte[] b = (byte[])save.Clone();
            b[i] = 1;
            if (SaveSummary.TryParse(b, out SaveSummary? s) && s!.Teams.Length == 2 && s.Teams[0] == 1 && s.Teams[1] == 0) return i;
        }
        return -1;
    }

    private static ulong Fnv(byte[] bytes)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in bytes) h = unchecked((h ^ b) * 1099511628211UL);
        return h;
    }

    // ---- BUG-1061 re-check: the stats grid's label rows, read off the grid itself ----

    // The most label rows a 4-seat match can have (three teams and a no-team seat, four teams), the no-team seat first in
    // player order, a teamless 4-seat FFA, and the screen opened twice (a rebuild must not keep the old label rows).
    private async Task StatsLayouts()
    {
        await PausedStats(4, new[] { "--team", "2", "1", "--team", "3", "2", "--team", "4", "3" }, "T1 P1 T2 P2 T3 P3 N P0", "teams 0, 1, 2, 3", "stats-3-teams-and-one", twice: true);
        await PausedStats(4, new[] { "--team", "1", "4", "--team", "2", "3", "--team", "3", "2", "--team", "4", "1" }, "T1 P3 T2 P2 T3 P1 T4 P0", "teams 4, 3, 2, 1", "stats-4-teams", twice: false);
        await PausedStats(4, new[] { "--team", "4", "2" }, "T2 P3 N P0 P1 P2", "teams 0, 0, 0, 2", null, twice: false);
        await PausedStats(4, Array.Empty<string>(), "P0 P1 P2 P3", "free-for-all", null, twice: true);
    }

    private async Task PausedStats(int players, string[] teams, string want, string how, string? shot, bool twice)
    {
        Match match = Start(players, teams);
        await Frames(2);
        _main.PauseMatch();
        await Frames(1);
        for (int pass = 0; pass < (twice ? 2 : 1); pass++)
        {
            _main.ShowStats();
            await Frames(2);
            MatchStats stats = _main.Stats!;
            CheckLayout(stats, want, $"{how} (pass {pass + 1})");
            // BUG-1063 (fixed): four label rows (three teams and a no-team seat, or four teams) pushed Back off a 1152 x 648
            // screen; the graph now gives up a label row's height for each label row past two.
            foreach (string f in StatsScreenCheck.Fits(stats, GetViewport().GetVisibleRect(), how)) Check(false, f);
            int labelRows = stats.TeamLabels.Count + (stats.NoTeamLabel != null ? 1 : 0);
            float wantGraph = MatchStats.GraphHeight - MatchStats.LabelRowHeight * Math.Max(0, labelRows - MatchStats.LabelRowsRoom);
            Check(Mathf.IsEqualApprox(stats.Graph.CustomMinimumSize.Y, wantGraph),
                $"{how}: {labelRows} label rows, graph minimum height {stats.Graph.CustomMinimumSize.Y} (want {wantGraph})");
            if (stats.NoTeamLabel != null)
                Check(GetViewport().GetVisibleRect().Grow(0.5f).Encloses(stats.NoTeamLabel.GetGlobalRect()) && stats.NoTeamLabel.IsVisibleInTree(),
                    $"{how}: the no-team label {stats.NoTeamLabel.GetGlobalRect()} is off screen or hidden");
            if (pass == 0 && shot != null) await Shot(shot);
            _main.CloseStats();
            await Frames(2);
        }
        await QuitMatch();
    }

    // The grid's rows below the headings as tokens: "T<n>" a team label, "N" the no-team label, "P<p>" player p's row.
    // Every label row's other cells must be empty, and the no-team label must read match_over.no_team.
    private void CheckLayout(MatchStats stats, string want, string how)
    {
        var grid = (GridContainer)stats.Table;
        var cells = grid.GetChildren().OfType<Control>().Where(c => !c.IsQueuedForDeletion()).ToList();
        int columns = grid.Columns;
        var got = new List<string>();
        for (int row = 1; (row + 1) * columns <= cells.Count; row++)
        {
            Control first = cells[row * columns];
            if (first is Label l && stats.NoTeamLabel == l)
            {
                got.Add("N");
                Check(l.Text == _ui.MatchOver(MatchOverText.NoTeam) && l.Text.Length > 0, $"{how}: the no-team label reads '{l.Text}'");
            }
            else if (first is Label t && stats.TeamLabels.Contains(t)) got.Add("T" + t.Text.Split(' ').Last());
            else if (first is HBoxContainer)
            {
                int k = got.Count(g => g.StartsWith('P'));
                got.Add(k < stats.DisplayOrder.Count ? "P" + stats.DisplayOrder[k] : "P?");
                continue;
            }
            else got.Add("?");
            for (int c = 1; c < columns; c++)
                Check(cells[row * columns + c] is not Label { Text.Length: > 0 }, $"{how}: label row {row} has text in column {c}");
        }
        Check(cells.Count % columns == 0, $"{how}: {cells.Count} grid cells is not a whole number of {columns}-cell rows");
        Check(string.Join(" ", got) == want, $"{how}: grid rows [{string.Join(" ", got)}] (want [{want}])");
        Check(stats.TeamLabels.Count + (stats.NoTeamLabel != null ? 1 : 0) == got.Count(g => !g.StartsWith('P')), $"{how}: label rows in the grid differ from TeamLabels / NoTeamLabel");
    }

    // ---- staging, as TeamsTest ----

    private Match Start(int players, params string[] extra)
    {
        var args = Base.Concat(new[] { "--players", players.ToString() }).ToList();
        for (int s = 1; s <= players; s++) args.AddRange(new[] { "--faction", s.ToString(), "malazan" });
        _main.StartSkirmish(args.Concat(extra).ToArray());
        Match? match = _main.CurrentMatch;
        if (match?.Simulation == null) throw new InvalidOperationException("the shell started no match");
        _ended = 0;
        match.Ended += _ => _ended++;
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        return match;
    }

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
        Check(placed == n, $"only {placed} of {n} shooters fit");
        await Ticks(sim, 3);
    }

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

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/qa-m6-v16-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    // A row for an open bug: reported, failing only with -- --strict (the QaV2Test convention); once it prints "now passes",
    // make it a plain check.
    private void Known(string bug, bool ok, string message)
    {
        if (ok)
        {
            GD.Print($"QA M6-V16 KNOWN {bug} row now passes: {message}");
            return;
        }
        GD.Print($"QA M6-V16 KNOWN {bug} (open): {message}");
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--strict") >= 0) _failures.Add($"{bug}: {message}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
