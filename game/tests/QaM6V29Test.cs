using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-V29 (the draw on screen), the edges <c>DrawViewTest</c> leaves out. Dev matches through the shell on a data copy
/// whose <c>draw.afterSeconds</c> is 20 and whose <c>standard</c> preset starts with no gold (two halls, no worker: spent).
/// <list type="bullet">
/// <item>Two seats at 2 ticks a frame: the notice shows iff <c>DrawTicksLeft &gt;= 0</c> with the right second on every tick;
/// the pause menu open mid-clock holds the tick and the notice; a 200-character <c>hud.draw_countdown</c> at 1152 x 648 stays
/// on screen (note only); the draw reads Draw; then (Continue watching) the local player is knocked out after the draw: the
/// live result stays Draw with one banner; a save after that fall, loaded, and the recording beside it, played back to its
/// end, read Draw for both sides as live does (BUG-1790's two-sided half, fixed in M6-VH10).</item>
/// <item>Three seats, free-for-all, drawn, then the local player knocked out after the draw: live stays Draw (latched); the
/// result now and a save after the fall, loaded, print <c>KNOWN BUG-1790</c> while they read Defeat (Request 60: the sim
/// does not record who stood at the draw); <c>-- --strict</c> fails the scene on them.</item>
/// <item>Four seats, teams 1 1 2 2: the local player out, its ally standing at the draw: <c>ResultFor</c> is Draw for every
/// seat, and the banner Draw.</item>
/// <item>Four seats, free-for-all: the local player out first: Defeat for seat 0, Draw for seats 1-3; one banner.</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V29Test.tscn</c>; prints "QA M6-V29 TEST PASS" or FAIL
/// lines; "QA M6-V29 KNOWN" lines report an open bug's behaviour without failing the scene unless run with <c>-- --strict</c>.
/// </summary>
public partial class QaM6V29Test : Node
{
    private const int DrawSeconds = 20;
    private const int Wait = DrawSeconds * SimConstants.TicksPerSecond;

    private static readonly string[] Args = { "--units", "0", "--no-bases", "--mute", "--seed", "3", "--speed", "8", "--zoom", "40", Main.NoConfineFlag };

    private readonly List<string> _failures = new();
    private readonly List<string> _notes = new();
    private string _root = "", _copy = "";
    private GameData _data = null!;
    private UiText _ui = null!;
    private Main _main = null!;
    private int _ended, _misses, _shownTicks;
    private double _delta = 0.1;
    private bool _strict;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _strict = Array.IndexOf(OS.GetCmdlineUserArgs(), "--strict") >= 0;
        _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qam6v29-{OS.GetProcessId()}"));
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frames(1);
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            _copy = Forge("data");
            _data = DataLoader.LoadAll(_copy).Data!;
            _main = NewMain(_copy);
            AddChild(_main);
            await Frames(2);
            await FallAfterDraw();
            await ThreeSidesFallAfterDraw();
            _delta = 0.25;
            await FourSeatTeams();
            await FourSeatFreeForAll();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        if (_misses > 0) _failures.Add($"{_misses} ticks with the notice wrong");
        foreach (string n in _notes) GD.Print($"  note: {n}");
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6-V29 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V29 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- two seats: edges, then a fall after the draw ----

    private async Task FallAfterDraw()
    {
        _main.StartSkirmish(Args);
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        DrawNotice notice = match.DrawNotice!;
        await Halls(sim, (0, g.Width / 4, g.Height / 2), (1, g.Width * 3 / 4, g.Height / 2));
        await Until(sim, () => w.DrawTicksLeft >= 0 && w.DrawTicksLeft < Wait / 2, "two seats: mid-clock");

        // The pause menu open mid-clock: no tick runs, the notice keeps its time and stays up.
        _main.PauseMatch();
        int tick = sim.TickNumber, writes = notice.TextWrites;
        string text = notice.TextLabel!.Text;
        await Frames(20);
        Check(sim.TickNumber == tick && notice.Shown && notice.TextLabel.Text == text && notice.TextWrites == writes,
            $"pause menu: tick {tick} -> {sim.TickNumber}, shown {notice.Shown}, '{notice.TextLabel.Text}' (was '{text}'), writes {writes} -> {notice.TextWrites}");
        Check(_main.Pause!.Visible, "pause menu: not visible");

        // A long text (a translation or a rename) at 1152 x 648: a second notice on the same HUD with a 200-character line.
        string shipped = File.ReadAllText(UiText.DefaultPath);
        JsonNode root = JsonNode.Parse(shipped)!;
        root["hud"]!["draw_countdown"] = new string('W', 180) + " {0} unless.";
        var errors = new List<string>();
        UiText? longUi = UiText.Parse(root.ToJsonString(), errors);
        if (Check(longUi != null, $"long text: ui.json refused: [{string.Join("; ", errors)}]"))
        {
            var extra = new DrawNotice { Name = "QaLongNotice" };
            match.AddChild(extra);
            extra.Init(match.GetNode<SimRunner>("SimRunner"), match.GetNode<CanvasLayer>("Hud"), longUi);
            await Frames(3);
            Rect2 r = extra.TextLabel!.GetGlobalRect();
            Rect2 screen = GetViewport().GetVisibleRect();
            _notes.Add($"long text at 1152 x 648: label {r} (min width {extra.TextLabel.GetMinimumSize().X:0}), on screen {screen.Encloses(r)}");
            extra.TextLabel.QueueFree();
            match.RemoveChild(extra);
            extra.QueueFree();
            await Frames(1);
        }
        _main.Resume();

        await UntilBanner(sim, "two seats draw");
        Check(w.MatchDrawn && match.Outcome == MatchOutcome.Draw && _main.Banner!.Title.Text == _ui.MatchOver(MatchOverText.Draw) && !notice.Shown,
            $"two seats draw: drawn {w.MatchDrawn}, outcome {match.Outcome}, banner '{_main.Banner!.Title.Text}', shown {notice.Shown}");
        int drawTick = sim.TickNumber;

        // Continue watching; player 1 (a human seat here) razes player 0's hall after the draw.
        _main.ContinueWatching();
        await Frames(2);
        await KnockOut(sim, killer: 1, victim: 0);
        await Until(sim, () => w.IsDefeated(0), "fall after draw: player 0 out");
        await Frames(3);
        MatchOutcome now = Match.ResultFor(w, 0);
        _notes.Add($"fall after draw: drawn at tick {drawTick}, player 0 out at tick {sim.TickNumber}; latched {match.Outcome}, ResultFor now {now}, banners {_ended}");
        Check(_ended == 1 && !_main.BannerUp && match.Outcome == MatchOutcome.Draw,
            $"fall after draw, live: ended {_ended}, banner up {_main.BannerUp}, outcome {match.Outcome} (want one Draw banner)");
        // M6-VH10 (BUG-1790, two sides): the side out after the draw and the side standing both read Draw now, not only latched.
        Check(Match.SidesThatBuilt(w) == 2 && now == MatchOutcome.Draw && Match.ResultFor(w, 1) == MatchOutcome.Draw,
            $"fall after draw, live: sides {Match.SidesThatBuilt(w)}, ResultFor 0 {now}, 1 {Match.ResultFor(w, 1)} (want Draw for both)");
        Check(!notice.Shown, "fall after draw: the notice is back");

        _main.PauseMatch();
        await Frames(2);
        _main.ShowStats();
        await Frames(2);
        MatchStats stats = _main.Stats!;
        int col = Array.FindIndex(MatchStats.Columns, c => c.Label == MatchOverText.DefeatedAt);
        _notes.Add($"fall after draw, stats: row 0 '{stats.Cell(0, col)}', row 1 '{stats.Cell(1, col)}'");
        string victory = _ui.MatchOver(MatchOverText.Victory);
        var named = new List<string>();
        Walk(stats, n => { if (n is Label l && l.Visible && l.Text.Contains(victory, StringComparison.Ordinal)) named.Add(l.Text); });
        Check(named.Count == 0, $"fall after draw: the stats screen names a winner: [{string.Join(", ", named)}]");
        _main.CloseStats();
        Check(_main.SaveGame() && _main.LastSavePath != null, "fall after draw: save not written");
        string save = _main.LastSavePath ?? "";
        string beside = SaveStore.ReplayPathOf(save);
        _main.QuitToMenu();
        await Frames(2);

        // Loaded: the ruling says the same answer as live (Draw: the side stood at the draw).
        Check(_main.LoadSavedGame(save), "fall after draw: the save did not load");
        Match loaded = Watch(_main.CurrentMatch);
        await UntilBanner(loaded.Simulation!, "fall after draw, loaded");
        string loadedTitle = _main.Banner!.Title.Text;
        // M6-VH10: BUG-1790's two-sided half is fixed, so this row passes (it printed "KNOWN BUG-1790 ... loads to Defeat" before).
        World lw = loaded.Simulation!.World;
        Check(loaded.Outcome == MatchOutcome.Draw && loadedTitle == _ui.MatchOver(MatchOverText.Draw) && lw.IsDefeated(0)
            && Match.ResultFor(lw, 0) == MatchOutcome.Draw && Match.ResultFor(lw, 1) == MatchOutcome.Draw,
            $"fall after draw, loaded: outcome {loaded.Outcome} ('{loadedTitle}'), player 0 out {lw.IsDefeated(0)}, ResultFor 0 {Match.ResultFor(lw, 0)}, 1 {Match.ResultFor(lw, 1)} (want Draw, as live: BUG-1790)");
        _main.QuitToMenu();
        await Frames(2);

        // The recording beside the save, played back from the start: the draw latches first; played to its end (past the fall),
        // both sides still read Draw.
        if (Check(File.Exists(beside), $"fall after draw: no recording beside the save ({beside})"))
        {
            Check(_main.WatchReplay(beside), "fall after draw: the recording did not play");
            Match played = Watch(_main.CurrentMatch);
            await UntilBanner(played.Simulation!, "fall after draw, playback");
            Check(played.Outcome == MatchOutcome.Draw && _main.Banner!.Title.Text == _ui.MatchOver(MatchOverText.Draw),
                $"fall after draw, playback: outcome {played.Outcome}, banner '{_main.Banner!.Title.Text}' (want Draw, as live)");
            SimRunner runner = played.GetNode<SimRunner>("SimRunner");
            _main.ContinueWatching();
            World pw = played.Simulation!.World;
            await Until(played.Simulation!, () => runner.PlaybackState != PlaybackState.Playing, "fall after draw, playback: the recording's end");
            Check(pw.IsDefeated(0) && Match.ResultFor(pw, 0) == MatchOutcome.Draw && Match.ResultFor(pw, 1) == MatchOutcome.Draw && _ended == 1,
                $"fall after draw, playback at its end (tick {pw.TickNumber}): player 0 out {pw.IsDefeated(0)}, ResultFor 0 {Match.ResultFor(pw, 0)}, 1 {Match.ResultFor(pw, 1)}, banners {_ended} (want Draw for both)");
            _main.QuitToMenu();
            await Frames(2);
        }
    }

    // ---- three seats, free-for-all: drawn, then a side falls after the draw (Request 60's case, still open) ----

    private async Task ThreeSidesFallAfterDraw()
    {
        _main.BeforeStart = m => { Setup(m); m.GetNode<SimRunner>("SimRunner").PlayerCount = 3; };
        _main.StartSkirmish(Args.Concat(new[] { "--players", "3" }).ToArray());
        _main.BeforeStart = Setup;
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        await Halls(sim, (0, g.Width / 4, g.Height / 3), (1, g.Width * 3 / 4, g.Height / 3), (2, g.Width / 2, g.Height * 3 / 4));
        await UntilBanner(sim, "three seats draw");
        Check(w.MatchDrawn && match.Outcome == MatchOutcome.Draw && Match.SidesThatBuilt(w) == 3,
            $"three seats draw: drawn {w.MatchDrawn}, outcome {match.Outcome}, sides {Match.SidesThatBuilt(w)}");
        _main.ContinueWatching();
        await Frames(2);
        await KnockOut(sim, killer: 1, victim: 0);
        await Until(sim, () => w.IsDefeated(0), "three seats: player 0 out after the draw");
        await Frames(3);
        Check(_ended == 1 && match.Outcome == MatchOutcome.Draw, $"three seats, live: ended {_ended}, latched {match.Outcome} (want one Draw banner)");
        if (Match.ResultFor(w, 0) != MatchOutcome.Draw)
            Known("BUG-1790", $"three sides: the side out after the draw reads {Match.ResultFor(w, 0)} now (latched Draw live; Request 60)");
        _main.PauseMatch();
        await Frames(2);
        Check(_main.SaveGame() && _main.LastSavePath != null, "three seats: save not written");
        string save = _main.LastSavePath ?? "";
        _main.QuitToMenu();
        await Frames(2);
        Check(_main.LoadSavedGame(save), "three seats: the save did not load");
        Match loaded = Watch(_main.CurrentMatch);
        await UntilBanner(loaded.Simulation!, "three seats, loaded");
        Check(loaded.Outcome != MatchOutcome.Victory, "three seats, loaded: Victory");
        if (loaded.Outcome != MatchOutcome.Draw)
            Known("BUG-1790", $"three sides: a save taken after the local player fell after the draw loads to {loaded.Outcome} ('{_main.Banner!.Title.Text}'); live read Draw (Request 60)");
        else _notes.Add("BUG-1790 three sides: the loaded save reads Draw (now passes: make it a plain check)");
        _main.QuitToMenu();
        await Frames(2);
    }

    // A row for an open bug: printed as KNOWN, failing only with -- --strict (the QaV2Test convention).
    private void Known(string bug, string message)
    {
        GD.Print($"QA M6-V29 KNOWN {bug}: {message}");
        if (_strict) _failures.Add($"{bug}: {message}");
    }

    // ---- four seats, teams 1 1 2 2: the local player out, its ally standing at the draw ----

    private async Task FourSeatTeams()
    {
        _main.BeforeStart = m => { Setup(m); m.GetNode<SimRunner>("SimRunner").PlayerCount = 4; };
        _main.StartSkirmish(Args.Concat(new[] { "--players", "4", "--team", "1", "1", "--team", "2", "1", "--team", "3", "2", "--team", "4", "2" }).ToArray());
        _main.BeforeStart = Setup;
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        Check(w.AreAllied(0, 1) && w.AreAllied(2, 3) && !w.AreAllied(0, 2), "teams 2v2: alliances wrong");
        await Halls(sim, (0, g.Width / 5, g.Height / 3), (1, g.Width / 5, g.Height * 2 / 3), (2, g.Width * 4 / 5, g.Height / 3), (3, g.Width * 4 / 5, g.Height * 2 / 3));
        Spawn(sim, 0, Unit(w, 0, UnitSlot.Worker), g.Width / 5, g.Height / 3 + 6);
        await Ticks(sim, 2);
        await KnockOut(sim, killer: 2, victim: 0);
        await Until(sim, () => w.IsDefeated(0), "teams 2v2: player 0 out");
        await Frames(2);
        Check(!_main.BannerUp && match.Outcome == MatchOutcome.None, $"teams 2v2: a banner with the ally standing ({match.Outcome})");
        await UntilBanner(sim, "teams 2v2 draw");
        string results = string.Join(", ", Enumerable.Range(0, 4).Select(p => $"{p}:{Match.ResultFor(w, p)}"));
        Check(w.MatchDrawn && match.Outcome == MatchOutcome.Draw && _main.Banner!.Title.Text == _ui.MatchOver(MatchOverText.Draw)
            && Enumerable.Range(0, 4).All(p => Match.ResultFor(w, p) == MatchOutcome.Draw),
            $"teams 2v2 draw: drawn {w.MatchDrawn}, outcome {match.Outcome}, per seat [{results}] (want Draw for all)");
        _notes.Add($"teams 2v2 draw: per seat [{results}]");
        _main.QuitToMenu();
        await Frames(2);
    }

    // ---- four seats, free-for-all: the local player out first ----

    private async Task FourSeatFreeForAll()
    {
        _main.BeforeStart = m => { Setup(m); m.GetNode<SimRunner>("SimRunner").PlayerCount = 4; };
        _main.StartSkirmish(Args.Concat(new[] { "--players", "4" }).ToArray());
        _main.BeforeStart = Setup;
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        Check(!w.AreAllied(0, 1) && !w.AreAllied(2, 3), "ffa 4: alliances");
        await Halls(sim, (0, g.Width / 5, g.Height / 3), (1, g.Width / 5, g.Height * 2 / 3), (2, g.Width * 4 / 5, g.Height / 3), (3, g.Width * 4 / 5, g.Height * 2 / 3));
        Spawn(sim, 0, Unit(w, 0, UnitSlot.Worker), g.Width / 5, g.Height / 3 + 6);
        await Ticks(sim, 2);
        await KnockOut(sim, killer: 1, victim: 0);
        await UntilBanner(sim, "ffa 4: local out");
        Check(w.IsDefeated(0) && !w.MatchOver && match.Outcome == MatchOutcome.Defeat, $"ffa 4 local out: outcome {match.Outcome}");
        _main.ContinueWatching();
        await Until(sim, () => w.MatchDrawn, "ffa 4: the others' draw");
        await Frames(3);
        string results = string.Join(", ", Enumerable.Range(0, 4).Select(p => $"{p}:{Match.ResultFor(w, p)}"));
        Check(_ended == 1 && !_main.BannerUp && match.Outcome == MatchOutcome.Defeat && Match.ResultFor(w, 0) == MatchOutcome.Defeat
            && Enumerable.Range(1, 3).All(p => Match.ResultFor(w, p) == MatchOutcome.Draw),
            $"ffa 4 drawn: ended {_ended}, banner {_main.BannerUp}, outcome {match.Outcome}, per seat [{results}] (want 0 Defeat, 1-3 Draw)");
        _notes.Add($"ffa 4 draw: per seat [{results}]");
        _main.QuitToMenu();
        await Frames(2);
    }

    // ---- helpers (after DrawViewTest's) ----

    private string Forge(string sub)
    {
        string from = ProjectSettings.GlobalizePath("res://data"), to = Path.Combine(_root, sub);
        if (Directory.Exists(to)) Directory.Delete(to, true);
        foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        string rules = Path.Combine(to, "common", "rules.json");
        JsonNode root = JsonNode.Parse(File.ReadAllText(rules))!;
        foreach (JsonNode? preset in root["startingResources"]!.AsArray())
            if ((string?)preset!["id"] == StartingResourcesDef.StandardId) preset["gold"] = 0;
        root["draw"] = new JsonObject { ["afterSeconds"] = DrawSeconds };
        File.WriteAllText(rules, root.ToJsonString());
        DataLoadResult loaded = DataLoader.LoadAll(to);
        if (!loaded.Ok) throw new InvalidOperationException($"the copy failed to load: {string.Join("; ", loaded.Errors.Take(5))}");
        return to;
    }

    private Main NewMain(string data)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { Main.NoConfineFlag };
        main.DataPath = data;
        main.SettingsPath = Path.Combine(_root, "settings.cfg");
        main.ReplayDir = Path.Combine(_root, "replays");
        main.SaveDir = Path.Combine(_root, "saves");
        main.BeforeStart = Setup;
        return main;
    }

    private void Setup(Match m) => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = _delta;

    private Match Watch(Match? match)
    {
        if (match?.Simulation == null) throw new InvalidOperationException("the shell started no match");
        _ended = 0;
        match.Ended += _ => _ended++;
        match.GetNode<SimRunner>("SimRunner").Ticked += s => OnTicked(match, s);
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        return match;
    }

    // Every tick: the notice shows iff the clock runs, with ceil(left / 20) seconds.
    private void OnTicked(Match match, Simulation sim)
    {
        DrawNotice? n = match.DrawNotice;
        if (n?.TextLabel == null || !IsInstanceValid(n.TextLabel)) return;
        int left = sim.World.DrawTicksLeft;
        bool want = left >= 0;
        string text = want ? _ui.DrawCountdown((left + 19) / 20) : "";
        if (want) _shownTicks++;
        if (n.Shown != want || want && n.TextLabel.Text != text)
            if (_misses++ < 5) _failures.Add($"tick {sim.TickNumber}: left {left}, shown {n.Shown}, '{n.TextLabel.Text}'");
    }

    private async Task Halls(Simulation sim, params (int Player, int X, int Y)[] at)
    {
        World w = sim.World;
        foreach ((int p, int x, int y) in at)
        {
            int hall = DefeatStaging.Hall(_data, w, p);
            DefeatStaging.SpawnAt(sim, p, hall, DefeatStaging.Anchor(w, p, hall, x, y));
        }
        await Ticks(sim, 3);
        Check(w.Buildings.Count == at.Length, $"{w.Buildings.Count} halls placed (want {at.Length})");
    }

    private int Unit(World w, int player, UnitSlot slot) => StartBase.UnitOfSlot(_data, w.FactionOf(player), slot);

    private static void Spawn(Simulation sim, int player, int type, int x, int y)
    {
        NavGrid g = sim.World.NavGrid;
        int c = FlowField.NearestPassable(g, Math.Clamp(y, 0, g.Height - 1) * g.Width + Math.Clamp(x, 0, g.Width - 1));
        sim.Enqueue(Command.SpawnUnit(player, type, g.CellCenter(c % g.Width, c / g.Width)));
    }

    private async Task KnockOut(Simulation sim, int killer, int victim)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        EntityHandle hall = BuildingOf(w, victim);
        int cell = w.Buildings.Cell[hall.Index];
        int siege = Unit(w, killer, UnitSlot.Siege);
        for (int i = 0; i < 8; i++) Spawn(sim, killer, siege, cell % g.Width - 4 + i % 4 * 2, cell / g.Width - 5 - i / 4 * 2);
        DefeatStaging.SpawnKillers(sim, _data, killer, victim);
        await Ticks(sim, 2);
        foreach (EntityHandle u in DefeatStaging.UnitsOf(w, killer))
            if (w.Units.TypeId[u.Index] == siege) sim.Enqueue(Command.Attack(killer, u, hall, isBuilding: true));
        List<EntityHandle> theirs = DefeatStaging.UnitsOf(w, victim);
        foreach (EntityHandle u in DefeatStaging.UnitsOf(w, killer))
            if (w.Units.TypeId[u.Index] != siege && theirs.Count > 0) sim.Enqueue(Command.Attack(killer, u, theirs[0], isBuilding: false));
    }

    private static EntityHandle BuildingOf(World w, int player)
    {
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++) if (b.Alive[i] && b.Owner[i] == player) return new EntityHandle(i, b.Generation[i]);
        throw new InvalidOperationException($"player {player} has no building");
    }

    private static void Walk(Node n, Action<Node> visit)
    {
        visit(n);
        foreach (Node c in n.GetChildren()) Walk(c, visit);
    }

    private async Task UntilBanner(Simulation sim, string how)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!_main.BannerUp && clock.Elapsed.TotalSeconds < 120) await Frames(1);
        Check(_main.BannerUp && _ended == 1, $"{how}: banner {_main.BannerUp}, ended {_ended} by tick {sim.TickNumber}");
        if (!_main.BannerUp) throw new InvalidOperationException($"{how}: no banner by tick {sim.TickNumber}");
    }

    private async Task Until(Simulation sim, Func<bool> done, string how)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.Elapsed.TotalSeconds < 120) await Frames(1);
        if (!done()) throw new InvalidOperationException($"{how}: not reached by tick {sim.TickNumber}");
    }

    private async Task Ticks(Simulation sim, int n)
    {
        int until = sim.TickNumber + n;
        await Until(sim, () => sim.TickNumber >= until, $"tick {until}");
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
