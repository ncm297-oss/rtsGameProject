using System;
using System.Collections.Generic;
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

/// <summary>M2-5: the real Match scene's debug overlay: off by default and nothing built, F12 and the action toggle it, <c>--debug-overlay</c> starts it on, arrows match the cached field's directions (never stale through a field churn), counts, the 120-sample graph, a hash twin, 0 bytes and the 1 ms budget at 2,000 units.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/DebugOverlayTest.tscn</c>; prints
/// "DEBUG OVERLAY TEST PASS" and exits 0, or prints each failure and exits 1. The match starts with
/// <c>--units 0</c> and the test spawns 1,000 units per player itself, feeding every command to a bare
/// twin <see cref="Simulation"/> too; the runner is disabled while the twin is compared, so ticks are exact.
/// </remarks>
public partial class DebugOverlayTest : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!, _twin = null!;
    private DebugOverlay _overlay = null!;
    private NavOverlayView _nav = null!;
    private FlowArrowsView _arrows = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private int _hashChecks;
    private EntityHandle[] _group = Array.Empty<EntityHandle>();

    private UnitStore U => _sim.World.Units;

    public override async void _Ready()
    {
        try
        {
            await Run();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        Engine.TimeScale = 1.0;
        foreach (string f in _failures) GD.Print($"DEBUG OVERLAY TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("DEBUG OVERLAY TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        GetTree().Root.Size = new Vector2I(1152, 648); // headless windows are 64 x 64
        await Frame();
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        _data = loaded.Data!;

        await FlagStartsOn();
        await StartMainMatch();
        await OffByDefault();
        await Toggle();
        await Arrows();
        await EvictedFieldIsNotDrawn();
        await ChurnNeverStale();
        await Counts();
        await Budget();
        await Graph();
        GD.Print($"hash twin: {_hashChecks} ticks compared, all equal");
    }

    // --debug-overlay: on from the start, layers built on the first frame.
    private async Task FlagStartsOn()
    {
        Check(LaunchOptions.Parse(new[] { "--debug-overlay" }).DebugOverlay, "--debug-overlay not parsed");
        Check(!LaunchOptions.Parse(Array.Empty<string>()).DebugOverlay, "overlay on without the flag");
        Check(LaunchOptions.Parse(new[] { "--debug-overlay", "--seed", "3" }).Seed == 3, "--debug-overlay swallowed the next flag");
        var m = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(m);
        m.Start(_data, LaunchOptions.Parse(new[] { "--units", "10", "--debug-overlay" }));
        var o = m.GetNode<DebugOverlay>("DebugOverlay");
        await Settle();
        var nav = m.GetNode<NavOverlayView>("World3D/NavOverlay");
        Check(o.Enabled, "--debug-overlay: overlay off");
        Check(nav.Visible && m.GetNode<FlowArrowsView>("World3D/FlowArrows").Visible && o.Graph.Visible, "--debug-overlay: a layer is hidden");
        Check(nav.Uploads == 1 && nav.Builder != null, $"--debug-overlay: nav mesh uploaded {nav.Uploads} times");
        Check(m.GetNode<Label>("DebugOverlay/Label").Text.Contains("\nunits 20 "), "--debug-overlay: label has no counts line");
        RemoveChild(m);
        m.QueueFree();
        await Frame();
    }

    private async Task StartMainMatch()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(new[] { "--units", "0", "--zoom", "60" }));
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.ProcessMode = ProcessModeEnum.Disabled; // the test ticks the sim (and its twin) itself
        _sim = _runner.Simulation!;
        SimConfig c = _sim.World.Config;
        _twin = new Simulation(c); // the same config, map parameters included (M2-3b: the match map has resources)
        _overlay = _match.GetNode<DebugOverlay>("DebugOverlay");
        _nav = _match.GetNode<NavOverlayView>("World3D/NavOverlay");
        _arrows = _match.GetNode<FlowArrowsView>("World3D/FlowArrows");
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;

        // Match.SpawnArmies' layout, 1,000 per player, to both sims.
        NavGrid grid = _sim.World.NavGrid;
        for (int p = 0; p < 2; p++)
        {
            FactionDef f = _data.Factions[p];
            float maxR = 0f;
            foreach (int t in f.Units) maxR = Math.Max(maxR, _data.Units[t].Radius);
            System.Numerics.Vector2[] spots = StartLayout.Block(grid, 1000, p == 0, maxR);
            for (int k = 0; k < spots.Length; k++) Send(Command.SpawnUnit(p, f.Units[k % f.Units.Length], spots[k]));
        }
        await Ticks(2);
        Check(U.Count == 2000, $"{U.Count} units spawned, expected 2000");
    }

    private async Task OffByDefault()
    {
        await Settle();
        Check(!_overlay.Enabled, "overlay on by default");
        Check(!_nav.Visible && !_arrows.Visible && !_overlay.Graph.Visible, "a layer is visible while off");
        // Off: nothing built, nothing synced, even with a selection that has a goal and many frames.
        SelectOwn(40, 0);
        SendMoves(_sel.Selection.Items, Centre(FlowField.NearestPassable(_sim.World.NavGrid, 20 * 128 + 64)));
        await Ticks(5);
        for (int i = 0; i < 20; i++) await Frame();
        Check(_nav.Builder == null && _nav.Uploads == 0, "nav overlay built while off");
        Check(_arrows.Layout == null && _arrows.Uploads == 0, "arrows built while off");
        Check(_overlay.LastLayersMs == 0 && _overlay.LiveUnits == 0, "overlay layers synced while off");
        Check(!_match.GetNode<Label>("DebugOverlay/Label").Text.Contains("\n"), "label has the overlay line while off");
    }

    private async Task Toggle()
    {
        Check(InputMap.HasAction("debug_overlay"), "no debug_overlay action in project.godot");
        bool f12 = false;
        foreach (InputEvent e in InputMap.ActionGetEvents("debug_overlay"))
            if (e is InputEventKey k && k.PhysicalKeycode == Key.F12) f12 = true;
        Check(f12, "debug_overlay is not bound to F12");

        // A real F12 key press through the viewport turns it on; the release and an echo do nothing.
        PushKey(Key.F12, true);
        await Settle();
        Check(_overlay.Enabled, "F12 did not turn the overlay on");
        PushKey(Key.F12, false);
        PushKey(Key.F12, true, echo: true);
        await Settle();
        Check(_overlay.Enabled && _overlay.Toggles == 1, $"F12 release / echo toggled (toggles {_overlay.Toggles})");
        Check(_nav.Visible && _arrows.Visible && _overlay.Graph.Visible, "a layer is hidden while on");
        Check(_nav.Uploads == 1, $"nav mesh uploaded {_nav.Uploads} times on turning on");
        // Again: off; the action event (any binding) works too.
        PushKey(Key.F12, true);
        PushKey(Key.F12, false);
        await Settle();
        Check(!_overlay.Enabled && !_nav.Visible && !_arrows.Visible && !_overlay.Graph.Visible, "second F12 did not turn it off");
        Input.ParseInputEvent(new InputEventAction { Action = "debug_overlay", Pressed = true });
        Input.FlushBufferedEvents();
        await Settle();
        Check(_overlay.Enabled, "the debug_overlay action did not turn it on");
        // Many frames on with an unchanged grid: the nav mesh is never rebuilt.
        for (int i = 0; i < 30; i++) await Frame();
        Check(_nav.Uploads == 1 && _nav.Builder!.Builds == 1, $"nav mesh rebuilt without a version change ({_nav.Uploads} uploads, {_nav.Builder.Builds} fills)");
        Check(_nav.Builder.BuiltVersion == _sim.World.NavGrid.Version, "nav overlay built from another version");
    }

    // Arrows after a Move once the field is cached: every drawn arrow equals DirectionAt of its cell; none without a goal.
    private async Task Arrows()
    {
        World w = _sim.World;
        NavGrid g = w.NavGrid;
        int target = FlowField.NearestPassable(g, 40 * g.Width + 70);
        SelectOwn(30, 1);
        SendMoves(_sel.Selection.Items, Centre(target));
        int goal = -1;
        for (int i = 0; i < 20 && (goal < 0 || w.FlowFields.PeekCached(goal) == null); i++)
        {
            await Ticks(1);
            goal = FlowArrowLayout.GoalOf(_sel.Selection.Items, U.Alive, U.Generation, U.GoalCell);
        }
        Check(goal == target, $"selection goal {goal}, ordered {target}");
        System.Numerics.Vector2 c = Centre(goal);
        _camera.SetFocus(c.X + 10f, c.Y + 6f);
        await Settle();
        Check(_overlay.ShownGoal == goal, $"overlay shows goal {_overlay.ShownGoal}, want {goal}");
        int drawn = CheckArrowsMatch("after move");
        Check(drawn > 500, $"only {drawn} arrows around the goal");
        Check(_arrows.Marker!.Visible, "goal marker hidden with the goal in the window");
        System.Numerics.Vector2 mk = Centre(w.FlowFields.PeekCached(goal)!.TargetCell);
        Check(Mathf.Abs(_arrows.Marker.Position.X - mk.X) < 0.01f && Mathf.Abs(_arrows.Marker.Position.Z - mk.Y) < 0.01f, "goal marker not on the target cell");

        // Map corner: the window is clipped, the arrows still match.
        _camera.SetFocus(0f, 0f);
        await Settle();
        CheckArrowsMatch("map corner");
        Check(_arrows.Layout!.MinX == 0 && _arrows.Layout.MinY == 0 && _arrows.Layout.MaxX == 20 && _arrows.Layout.MaxY == 20, "corner window not clipped");
        _camera.SetFocus(c.X + 10f, c.Y + 6f);

        // No selection, or a selection with no goal: no arrows.
        var keep = _sel.Selection.Items.ToArray();
        _group = keep;
        _sel.Selection.Clear();
        await Settle();
        Check(_arrows.ShownCount == 0 && _overlay.ShownGoal == -1, $"{_arrows.ShownCount} arrows with nothing selected");
        Check(!_arrows.Marker.Visible, "goal marker shown with nothing selected");
        for (int i = 0; i < U.Capacity; i++)
        {
            if (U.Alive[i] && U.Owner[i] == 0 && U.GoalCell[i] < 0)
            {
                _sel.Selection.Add(new EntityHandle(i, U.Generation[i]));
                break;
            }
        }
        await Settle();
        Check(_sel.Selection.Count == 1 && _arrows.ShownCount == 0, $"{_arrows.ShownCount} arrows for a unit with no goal");
        _sel.Selection.Clear();
        foreach (EntityHandle h in keep) _sel.Selection.Add(h);
        await Settle();
        Check(_arrows.ShownCount == drawn, $"reselected: {_arrows.ShownCount} arrows, had {drawn}");
    }

    // The churn started by EvictedFieldIsNotDrawn (more live goals than the 128-field cache) goes on with the Arrows group selected again: every tick the drawn arrows match the current peek or are empty.
    private async Task ChurnNeverStale()
    {
        World w = _sim.World;
        _sel.Selection.Clear();
        foreach (EntityHandle h in _group) _sel.Selection.Add(h);
        int goal = FlowArrowLayout.GoalOf(_sel.Selection.Items, U.Alive, U.Generation, U.GoalCell);
        int empty = 0, matched = 0, toggles = 0;
        for (int t = 0; t < 80; t++)
        {
            await Ticks(1);
            if (t % 25 == 24)
            {
                _overlay.SetEnabled(false); // off and on again mid-churn
                await Settle();
                _overlay.SetEnabled(true);
                toggles++;
            }
            if (t % 10 == 5) _sel.Selection.Remove(_sel.Selection.Items[0]); // selection changing
            if (t % 20 == 19) Rechurn(t);
            await Settle();
            if (CheckArrowsMatch($"churn tick {t}") > 0) matched++;
            else empty++;
        }
        GD.Print($"churn: {w.FlowFields.BuildCount} field builds, cache {w.FlowFields.Count}/{w.FlowFields.Capacity}, frames with arrows {matched}, empty {empty}, toggles {toggles}, goal {goal}");
    }

    // One unit walks 4 cells and arrives (its field drawn); then 1,000 units go to 200 goals (more than the 128-field cache)
    // and the unit's unused field is evicted: from then on no arrows, never stale ones.
    private async Task EvictedFieldIsNotDrawn()
    {
        World w = _sim.World;
        NavGrid g = w.NavGrid;
        int x = -1;
        for (int i = 0; i < U.Capacity && x < 0; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 0 || U.State[i] == UnitState.Moving || Array.IndexOf(_group, new EntityHandle(i, U.Generation[i])) >= 0) continue;
            g.WorldToCell(U.Position[i], out int cx, out int cy);
            if (g.IsPassable(cx + 4, cy)) x = i;
        }
        Check(x >= 0, "no idle own unit with open ground 4 cells east");
        if (x < 0) return;
        var h = new EntityHandle(x, U.Generation[x]);
        g.WorldToCell(U.Position[x], out int ux, out int uy);
        int goal = uy * g.Width + ux + 4;
        _sel.Selection.Clear();
        _sel.Selection.Add(h);
        Send(Command.Move(0, h, Centre(goal)));
        _camera.SetFocus(Centre(goal).X, Centre(goal).Y);
        bool seen = false;
        int t = 0;
        for (; t < 200 && !(seen && U.State[x] != UnitState.Moving); t++)
        {
            await Ticks(1);
            await Settle();
            CheckArrowsMatch($"walk tick {t}");
            if (w.FlowFields.PeekCached(goal) != null && _arrows.Layout!.Goal == goal && _arrows.ShownCount > 0) seen = true;
        }
        Check(seen, $"goal {goal}: field never drawn in {t} ticks");
        Check(U.State[x] != UnitState.Moving && U.GoalCell[x] == goal, $"unit {x} still {U.State[x]} after {t} ticks (goal cell {U.GoalCell[x]})");

        // The churn: every other own unit to one of 200 goals.
        NavGrid grid = w.NavGrid;
        var passable = new List<int>();
        for (int i = 0; i < grid.Width * grid.Height; i++) if (grid.IsPassable(i % grid.Width, i / grid.Width)) passable.Add(i);
        int n = 0;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 0 || i == x || Array.IndexOf(_group, new EntityHandle(i, U.Generation[i])) >= 0) continue;
            int cell = passable[(n++ % 200) * (passable.Count / 200)];
            Send(Command.Move(0, new EntityHandle(i, U.Generation[i]), Centre(cell)));
        }
        bool evicted = false;
        for (t = 0; t < 300 && !evicted; t++)
        {
            await Ticks(1);
            await Settle();
            Check(_arrows.Layout!.Goal == goal, $"evict tick {t}: overlay goal {_arrows.Layout.Goal}, want {goal}");
            CheckArrowsMatch($"evict tick {t}");
            evicted = w.FlowFields.PeekCached(goal) == null;
        }
        Check(evicted, $"goal {goal}: field still cached after {t} churn ticks");
        Check(_arrows.ShownCount == 0 && !_arrows.Marker!.Visible, $"{_arrows.ShownCount} arrows after the field was evicted");
        GD.Print($"evicted: unit {x}'s goal {goal} drawn, then evicted after {t} churn ticks; arrows {_arrows.ShownCount}");
    }

    private void Rechurn(int salt)
    {
        NavGrid g = _sim.World.NavGrid;
        int n = 0;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 0 || U.State[i] == UnitState.Moving || _sel.Selection.Contains(new EntityHandle(i, U.Generation[i]))) continue;
            int cell = FlowField.NearestPassable(g, ((n++ % 150) * 97 + salt * 31) % (g.Width * g.Height));
            Send(Command.Move(0, new EntityHandle(i, U.Generation[i]), Centre(cell)));
        }
    }

    private async Task Counts()
    {
        await Settle();
        int moving = 0;
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.State[i] == UnitState.Moving) moving++;
        Check(_overlay.LiveUnits == U.Count, $"live {_overlay.LiveUnits} vs {U.Count}");
        Check(_overlay.MovingUnits == moving, $"moving {_overlay.MovingUnits} vs {moving}");
        Check(_overlay.CachedFields == _sim.World.FlowFields.Count, $"fields {_overlay.CachedFields} vs {_sim.World.FlowFields.Count}");
        await Frame();
        string label = _match.GetNode<Label>("DebugOverlay/Label").Text;
        Check(label.Contains($"\nunits {U.Count}   moving {moving}   fields {_sim.World.FlowFields.Count}/"), $"label counts wrong: {label}");
        GD.Print($"counts: {U.Count} live, {moving} moving, {_sim.World.FlowFields.Count} fields");
    }

    // At 2,000 units, zoom 60: a frame's overlay work with the camera panning a cell every frame (a relist each frame) and steady.
    private async Task Budget()
    {
        System.Numerics.Vector2 start = _camera.Focus;
        for (int i = 0; i < 5; i++) _overlay.SyncLayers(); // warm up
        double total = 0, worst = 0;
        int relists = _arrows.Uploads;
        const int frames = 120;
        for (int i = 0; i < frames; i++)
        {
            _camera.SetFocus(start.X + (i % 30) * MapConstants.CellSize, start.Y);
            _overlay.SyncLayers();
            total += _overlay.LastLayersMs;
            worst = Math.Max(worst, _overlay.LastLayersMs);
        }
        relists = _arrows.Uploads - relists;
        GD.Print($"overlay frame at {U.Count} units, panning ({relists} relists of {_arrows.ShownCount} arrows): avg {total / frames:F3} ms, worst {worst:F3} ms");
        Check(total / frames <= 1.0, $"overlay frame avg {total / frames:F3} ms > 1 ms");
        Check(relists >= frames - 5, $"only {relists} relists while panning");

        // Allocation: panning frames and steady frames, measured on this thread (the frame code runs here).
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 60; i++)
        {
            _camera.SetFocus(start.X + (i % 30) * MapConstants.CellSize, start.Y);
            _overlay.SyncLayers();
        }
        long panning = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 60; i++) _overlay.SyncLayers();
        long steady = GC.GetAllocatedBytesForCurrentThread() - before;
        GD.Print($"overlay allocation: panning {panning} bytes / 60 frames, steady {steady} bytes / 60 frames");
        Check(panning == 0 && steady == 0, $"overlay frames allocated (panning {panning}, steady {steady} bytes)");
        _camera.SetFocus(start.X, start.Y);

        // Off: the frame does no overlay work at all.
        _overlay.SetEnabled(false);
        int uploads = _arrows.Uploads, navUploads = _nav.Uploads;
        double last = _overlay.LastLayersMs;
        for (int i = 0; i < 10; i++)
        {
            await Ticks(1);
            await Frame();
        }
        Check(_arrows.Uploads == uploads && _nav.Uploads == navUploads && _overlay.LastLayersMs == last, "layers synced while off");
        _overlay.SetEnabled(true);
    }

    private async Task Graph()
    {
        Check(_runner.TickTimes.Count == 0, $"{_runner.TickTimes.Count} samples before the runner ticked");
        _runner.ProcessMode = ProcessModeEnum.Inherit; // the runner ticks from here on; the twin stops
        Engine.TimeScale = 4.0;
        int start = _sim.TickNumber;
        while (_sim.TickNumber < start + 60) await Frame();
        Check(_runner.TickTimes.Count == _sim.TickNumber - start, $"{_runner.TickTimes.Count} samples after {_sim.TickNumber - start} ticks");
        while (_sim.TickNumber < start + 150) await Frame();
        Engine.TimeScale = 1.0;
        TickTimeRing ring = _runner.TickTimes;
        Check(ring.Count == 120, $"graph holds {ring.Count} samples after {_sim.TickNumber - start} ticks, want 120");
        Check(ring[ring.Count - 1] == _runner.LastTickMs, "newest sample is not the last tick's cost");
        await Settle();
        GD.Print($"graph: {ring.Count} samples, avg {ring.Average:F3} ms, worst {ring.Worst:F3} ms, drawn bars {_overlay.Graph.DrawnBars}, scale {_overlay.Graph.ScaleMs:F1} ms");
        Check(_overlay.Graph.DrawnBars == 0 || _overlay.Graph.DrawnBars == 120, $"graph drew {_overlay.Graph.DrawnBars} bars");
        Check(_overlay.Graph.ScaleMs == 0 || _overlay.Graph.ScaleMs >= 2 * TickTimeRing.BudgetMs, "graph scale below twice the budget");
        string label = _match.GetNode<Label>("DebugOverlay/Label").Text;
        Check(label.Contains("worst") && label.Contains("(120)"), $"label has no tick stats: {label}");
    }

    /// <summary>Every drawn arrow sits on its cell's centre and points along DirectionAt of the current cached field; the count equals the window's directed cells. Returns the count.</summary>
    private int CheckArrowsMatch(string when)
    {
        World w = _sim.World;
        NavGrid g = w.NavGrid;
        FlowArrowLayout layout = _arrows.Layout!;
        int selGoal = FlowArrowLayout.GoalOf(_sel.Selection.Items, U.Alive, U.Generation, U.GoalCell);
        Check(layout.Goal == selGoal, $"{when}: arrows for goal {layout.Goal}, the selection's goal is {selGoal}");
        FlowField? f = layout.Goal >= 0 ? w.FlowFields.PeekCached(layout.Goal) : null;
        if (f == null)
        {
            Check(_arrows.ShownCount == 0 && !_arrows.Marker!.Visible, $"{when}: {_arrows.ShownCount} arrows (or the marker) with no field");
            return 0;
        }
        int want = 0;
        for (int y = layout.MinY; y < layout.MaxY; y++)
            for (int x = layout.MinX; x < layout.MaxX; x++)
                if (f.DirectionAt(y * g.Width + x) != FlowField.NoDirection) want++;
        Check(_arrows.ShownCount == want, $"{when}: {_arrows.ShownCount} arrows, field has {want} directed cells in the window");
        int bad = 0, engineBad = 0;
        bool headless = DisplayServer.GetName() == "headless";
        for (int k = 0; k < _arrows.ShownCount; k++)
        {
            Transform3D t = _arrows.ArrowTransform(k);
            if (!headless && !t.IsEqualApprox(_arrows.EngineTransform(k))) engineBad++;
            var dir = new System.Numerics.Vector2(t.Basis.Column0.X, t.Basis.Column0.Z);
            Vector3 pos = t.Origin;
            // A proper rotation about +Y: unit X axis on the ground, Y up, right-handed.
            if (Mathf.Abs(t.Basis.Determinant() - 1f) > 1e-4f || !t.Basis.Column1.IsEqualApprox(Vector3.Up)) bad++;
            if (!g.WorldToCell(new System.Numerics.Vector2(pos.X, pos.Z), out int cx, out int cy)) { bad++; continue; }
            byte d = f.DirectionAt(cy * g.Width + cx);
            if (d == FlowField.NoDirection) { bad++; continue; }
            var expect = System.Numerics.Vector2.Normalize(new System.Numerics.Vector2(FlowField.OffsetX(d), FlowField.OffsetY(d)));
            if (System.Numerics.Vector2.Distance(expect, dir) > 1e-4f) bad++;
        }
        Check(bad == 0, $"{when}: {bad} of {_arrows.ShownCount} arrows disagree with DirectionAt");
        Check(engineBad == 0, $"{when}: {engineBad} instance transforms in the engine differ from the uploaded buffer");
        return _arrows.ShownCount;
    }

    private void SelectOwn(int count, int skip)
    {
        _sel.Selection.Clear();
        int seen = 0;
        for (int i = 0; i < U.Capacity && _sel.Selection.Count < count; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 0) continue;
            if (seen++ % 7 != skip % 7) continue;
            _sel.Selection.Add(new EntityHandle(i, U.Generation[i]));
        }
    }

    private void SendMoves(ReadOnlySpan<EntityHandle> units, System.Numerics.Vector2 point)
    {
        foreach (EntityHandle h in units) Send(Command.Move(0, h, point));
    }

    private void Send(Command c)
    {
        _sim.Enqueue(c);
        _twin.Enqueue(c);
    }

    // Ticks both sims; the overlay runs its frames in between, so the hashes prove it never wrote the sim.
    private async Task Ticks(int n)
    {
        for (int i = 0; i < n; i++)
        {
            _sim.Tick();
            _twin.Tick();
            _hashChecks++;
            if (_sim.StateHash() != _twin.StateHash())
            {
                Check(false, $"tick {_sim.TickNumber}: overlay-read sim diverged from its twin");
                throw new InvalidOperationException("hash twin diverged");
            }
            await Frame();
        }
    }

    private System.Numerics.Vector2 Centre(int cell)
    {
        NavGrid g = _sim.World.NavGrid;
        return g.CellCenter(cell % g.Width, cell / g.Width);
    }

    private void PushKey(Key key, bool pressed, bool echo = false) =>
        GetViewport().PushInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed, Echo = echo });

    // process_frame fires before the nodes' _Process, so a frame's sync is visible only after a second one.
    private async Task Settle()
    {
        await Frame();
        await Frame();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
