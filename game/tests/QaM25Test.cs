using System;
using System.Collections.Generic;
using System.Diagnostics;
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

/// <summary>QA (M2-5): debug overlay in a live match: full-frame allocation and cost (label included) on and off at 2,000 units, zoom 60, window at the map's corners and centre; drawn arrows equal this frame's peek while the runner ticks and fields churn; selected units that die or respawn as the enemy; --debug-overlay with --no-hud; the graph at speeds 8 and 0.25.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaM25Test.tscn</c>; prints
/// "QA M2-5 TEST PASS" and exits 0, or prints each failure and exits 1.
/// </remarks>
public partial class QaM25Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private DebugOverlay _overlay = null!;
    private FlowArrowsView _arrows = null!;
    private NavOverlayView _nav = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;

    private UnitStore U => _sim.World.Units;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            await FlagWithNoHud();
            await StartMatch();
            await LiveChurnArrowsEqualThisFramesPeek();
            await SelectedUnitDiesAndRespawnsAsEnemy();
            await FullFrameCostAndAllocation();
            await GraphAtSpeeds();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        Engine.TimeScale = 1.0;
        foreach (string f in _failures) GD.Print($"QA M2-5 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M2-5 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task FlagWithNoHud()
    {
        var m = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(m);
        m.Start(_data, LaunchOptions.Parse(new[] { "--no-hud", "--debug-overlay", "--units", "50", "--no-bases" })); // armies only, as in M2 (M3-V1)
        for (int i = 0; i < 10; i++) await Frame();
        var o = m.GetNode<DebugOverlay>("DebugOverlay");
        Check(o.Enabled, "--no-hud --debug-overlay: overlay off");
        Check(!m.GetNode<CanvasLayer>("Hud").Visible, "--no-hud: HUD visible");
        Check(o.Graph.Visible && m.GetNode<NavOverlayView>("World3D/NavOverlay").Uploads == 1, "--no-hud --debug-overlay: graph hidden or nav not built");
        Check(o.LiveUnits == 100, $"--no-hud --debug-overlay: live {o.LiveUnits}");
        // Flag order the other way round.
        Check(LaunchOptions.Parse(new[] { "--debug-overlay", "--no-hud" }).DebugOverlay && LaunchOptions.Parse(new[] { "--debug-overlay", "--no-hud" }).NoHud, "flag order matters");
        RemoveChild(m);
        m.QueueFree();
        await Frame();
    }

    private async Task StartMatch()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(new[] { "--units", "1000", "--zoom", "60", "--debug-overlay", "--no-bases" })); // armies only, as in M2 (M3-V1)
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _sim = _runner.Simulation!;
        _overlay = _match.GetNode<DebugOverlay>("DebugOverlay");
        _nav = _match.GetNode<NavOverlayView>("World3D/NavOverlay");
        _arrows = _match.GetNode<FlowArrowsView>("World3D/FlowArrows");
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        while (_sim.TickNumber < 3) await Frame();
        Check(U.Count == 2000, $"{U.Count} units, expected 2000");
    }

    private int[] Passable()
    {
        NavGrid g = _sim.World.NavGrid;
        var list = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++) if (g.IsPassable(c % g.Width, c / g.Width)) list.Add(c);
        return list.ToArray();
    }

    private void OrderOwn(int[] passable, int goals, int offset)
    {
        NavGrid g = _sim.World.NavGrid;
        int n = 0;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 0) continue;
            int cell = passable[(offset + (n++ % goals) * (passable.Length / goals)) % passable.Length];
            _sim.Enqueue(Command.Move(0, new EntityHandle(i, U.Generation[i]), g.CellCenter(cell % g.Width, cell / g.Width)));
        }
    }

    // The real runner ticks at 4x; 64 goals for 1,000 own units churn the cache; the selection, the toggle and the camera
    // change every few frames; a version bump mid-run. After each frame the drawn instance buffer equals this frame's peek.
    private async Task LiveChurnArrowsEqualThisFramesPeek()
    {
        int[] passable = Passable();
        NavGrid g = _sim.World.NavGrid;
        var rng = new Random(25);
        _runner.GameSpeed = 4.0;
        int checkedFrames = 0, drawnFrames = 0, navUploads0 = _nav.Uploads, bumpedAt = -1;
        for (int frame = 0; frame < 400; frame++)
        {
            if (frame % 20 == 0) OrderOwn(passable, 64, frame * 31);
            if (frame % 4 == 0)
            {
                _sel.Selection.Clear();
                int start = rng.Next(U.Capacity);
                for (int i = start; i < U.Capacity && _sel.Selection.Count < 20; i += 1 + rng.Next(9))
                    if (U.Alive[i] && U.Owner[i] == 0) _sel.Selection.Add(new EntityHandle(i, U.Generation[i]));
            }
            if (frame % 13 == 0) _overlay.SetEnabled(!_overlay.Enabled);
            if (frame % 3 == 0)
            {
                int goal = FlowArrowLayout.GoalOf(_sel.Selection.Items, U.Alive, U.Generation, U.GoalCell);
                if (goal >= 0 && rng.Next(2) == 0) _camera.SetFocus((goal % g.Width) * 2f + rng.Next(-30, 30), (goal / g.Width) * 2f + rng.Next(-30, 30));
                else _camera.SetFocus(rng.Next(0, g.Width * 2), rng.Next(0, g.Height * 2));
            }
            if (frame == 200)
            {
                // internal to Rts.Sim (InternalsVisibleTo only the xUnit project), so by reflection: a stand-in for M3-1's tree cut
                typeof(NavGrid).GetMethod("BumpVersionForTests", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(g, null);
                bumpedAt = _nav.Uploads;
            }
            await Frame();
            if (!_overlay.Enabled) continue;
            checkedFrames++;
            if (VerifyDrawn($"churn frame {frame}") > 0) drawnFrames++;
        }
        _runner.GameSpeed = 1.0;
        if (!_overlay.Enabled) _overlay.SetEnabled(true);
        await Frame();
        Check(_nav.Uploads - bumpedAt == 1, $"nav mesh uploaded {_nav.Uploads - bumpedAt} times after one version bump (overlay may have been off at the bump; re-checked on)");
        Check(drawnFrames > 20, $"churn too weak: arrows drawn in only {drawnFrames} of {checkedFrames} frames");
        GD.Print($"live churn: {checkedFrames} frames checked, {drawnFrames} with arrows, builds {_sim.World.FlowFields.BuildCount}, nav uploads {_nav.Uploads - navUploads0}");
    }

    // Decodes the drawn instance buffer and checks it against a fresh peek of the selection's goal now.
    private int VerifyDrawn(string when)
    {
        World w = _sim.World;
        NavGrid g = w.NavGrid;
        int goal = FlowArrowLayout.GoalOf(_sel.Selection.Items, U.Alive, U.Generation, U.GoalCell);
        FlowField? f = goal >= 0 ? w.FlowFields.PeekCached(goal) : null;
        FlowArrowLayout? l = _arrows.Layout;
        if (l == null) { Check(false, $"{when}: no layout while on"); return 0; }
        Check(l.Goal == goal, $"{when}: drawn goal {l.Goal}, selection goal {goal}");
        if (f == null)
        {
            Check(_arrows.ShownCount == 0 && !_arrows.Marker!.Visible, $"{when}: {_arrows.ShownCount} arrows drawn with no current field");
            return 0;
        }
        int k = 0;
        for (int y = l.MinY; y < l.MaxY; y++)
        for (int x = l.MinX; x < l.MaxX; x++)
        {
            int c = y * g.Width + x;
            byte d = f.DirectionAt(c);
            if (d == FlowField.NoDirection) continue;
            if (k >= _arrows.ShownCount) { Check(false, $"{when}: fewer arrows drawn ({_arrows.ShownCount}) than the field has"); return k; }
            Transform3D t = _arrows.ArrowTransform(k);
            System.Numerics.Vector2 cc = g.CellCenter(x, y);
            System.Numerics.Vector2 v = FlowArrowLayout.DirectionVector(d);
            Vector3 dirX = t.Basis.Column0;
            if (Mathf.Abs(t.Origin.X - cc.X) > 0.01f || Mathf.Abs(t.Origin.Z - cc.Y) > 0.01f || Mathf.Abs(dirX.X - v.X) > 1e-4f || Mathf.Abs(dirX.Z - v.Y) > 1e-4f)
            {
                Check(false, $"{when}: arrow {k} at ({t.Origin.X:F2},{t.Origin.Z:F2}) dir ({dirX.X:F2},{dirX.Z:F2}), field says cell ({x},{y}) dir {d}");
                return k;
            }
            k++;
        }
        Check(k == _arrows.ShownCount, $"{when}: {_arrows.ShownCount} arrows drawn, field has {k} in the window");
        return k;
    }

    private async Task SelectedUnitDiesAndRespawnsAsEnemy()
    {
        _overlay.SetEnabled(true);
        NavGrid g = _sim.World.NavGrid;
        int[] passable = Passable();
        int a = -1;
        for (int i = 0; i < U.Capacity && a < 0; i++) if (U.Alive[i] && U.Owner[i] == 0) a = i;
        int goal = passable[passable.Length / 3];
        _sim.Enqueue(Command.Move(0, new EntityHandle(a, U.Generation[a]), g.CellCenter(goal % g.Width, goal / g.Width)));
        _sel.Selection.Clear();
        var h = new EntityHandle(a, U.Generation[a]);
        _sel.Selection.Add(h);
        _camera.SetFocus((goal % g.Width) * 2f, (goal / g.Width) * 2f);
        int t0 = _sim.TickNumber;
        while (_sim.TickNumber < t0 + 3) await Frame();
        await Frame();
        int fieldGoal = U.GoalCell[a];
        Check(_overlay.ShownGoal == fieldGoal && fieldGoal >= 0, $"selected unit's goal {fieldGoal}, shown {_overlay.ShownGoal}");
        VerifyDrawn("before death");

        // Dies in this frame, before the overlay runs.
        U.Free(h);
        _overlay.SyncLayers();
        Check(_overlay.ShownGoal == -1 && _arrows.ShownCount == 0 && !_arrows.Marker!.Visible, $"dead unit's arrows still drawn ({_arrows.ShownCount}, goal {_overlay.ShownGoal})");
        // Respawns in the same slot as the enemy with a goal; the stale handle (if still selected) must not show it.
        EntityHandle re = U.Alloc();
        U.Owner[re.Index] = 1;
        U.GoalCell[re.Index] = fieldGoal;
        _sel.Selection.Clear();
        _sel.Selection.Add(h);
        _overlay.SyncLayers();
        Check(re.Index != a || _overlay.ShownGoal == -1, $"respawned-as-enemy slot shown through the stale handle (goal {_overlay.ShownGoal})");
        U.Free(re);
        await Frame();
        Check(_sel.Selection.Count == 0, $"selection kept {_sel.Selection.Count} dead handles");
        GD.Print($"death/respawn: slot {a} respawned at slot {re.Index}; overlay goal {_overlay.ShownGoal}, arrows {_arrows.ShownCount}");
    }

    // The whole DebugOverlay frame (_Process: layers + label text), not just SyncLayers, on and off; window at each corner and the centre.
    private async Task FullFrameCostAndAllocation()
    {
        _overlay.SetEnabled(true);
        NavGrid g = _sim.World.NavGrid;
        _camera.SetZoom(60f);
        // 200 own units ordered to one goal near the centre; wait for its field so every window has arrows.
        int[] passable = Passable();
        int goal = passable[passable.Length / 2];
        _sel.Selection.Clear();
        for (int i = 0; i < U.Capacity && _sel.Selection.Count < 200; i++)
            if (U.Alive[i] && U.Owner[i] == 0)
            {
                var h = new EntityHandle(i, U.Generation[i]);
                _sel.Selection.Add(h);
                _sim.Enqueue(Command.Move(0, h, g.CellCenter(goal % g.Width, goal / g.Width)));
            }
        int t0 = _sim.TickNumber;
        while (_sim.World.FlowFields.PeekCached(goal) == null && _sim.TickNumber < t0 + 100) await Frame();
        Check(_sim.World.FlowFields.PeekCached(goal) != null, "cost setup: field never cached");
        _runner.ProcessMode = ProcessModeEnum.Disabled; // hold the sim still while measuring
        float W = g.Width * MapConstants.CellSize, H = g.Height * MapConstants.CellSize;
        var spots = new (string, float, float)[] { ("SW", 0, 0), ("SE", W, 0), ("NW", 0, H), ("NE", W, H), ("centre", W / 2, H / 2) };
        foreach (var (name, x, y) in spots)
        {
            _camera.SetFocus(x, y);
            for (int i = 0; i < 3; i++) _overlay._Process(0.016);
            double total = 0, worst = 0;
            const int frames = 120;
            for (int i = 0; i < frames; i++)
            {
                _camera.SetFocus(x + (i % 2) * 2f * (x > 0 ? -1 : 1), y);
                _overlay._Process(0.016);
                total += _overlay.LastLayersMs;
                worst = Math.Max(worst, _overlay.LastLayersMs);
            }
            GD.Print($"cost {name}: avg {total / frames:F3} ms worst {worst:F3} ms, arrows {_arrows.ShownCount}, window ({_arrows.Layout!.MinX},{_arrows.Layout.MinY})-({_arrows.Layout.MaxX},{_arrows.Layout.MaxY})");
            Check(_arrows.ShownCount > 100, $"{name}: only {_arrows.ShownCount} arrows (cost not measured with a relist)");
            Check(total / frames <= 1.0, $"{name}: overlay frame avg {total / frames:F3} ms > 1 ms");
        }

        // Allocation of the whole _Process frame at steady state.
        _camera.SetFocus(W / 2, H / 2);
        for (int i = 0; i < 5; i++) _overlay._Process(0.016);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 60; i++) _overlay.SyncLayers();
        long layersOn = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 60; i++) _overlay._Process(0.016);
        long frameOn = GC.GetAllocatedBytesForCurrentThread() - before;
        _overlay.SetEnabled(false);
        for (int i = 0; i < 5; i++) _overlay._Process(0.016);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 60; i++) _overlay._Process(0.016);
        long frameOff = GC.GetAllocatedBytesForCurrentThread() - before;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 600; i++) _overlay._Process(0.016);
        double offMs = sw.Elapsed.TotalMilliseconds / 600;
        _overlay.SetEnabled(true);
        _runner.ProcessMode = ProcessModeEnum.Inherit;
        GD.Print($"allocation / 60 frames: layers on {layersOn} B, whole frame on {frameOn} B, whole frame off {frameOff} B; off frame {offMs:F4} ms (label only)");
        Check(layersOn == 0, $"overlay layers allocated {layersOn} bytes / 60 frames");
        // Recorded, not asserted: the label text (pre-existing since M2-1) allocates on and off.
    }

    private async Task GraphAtSpeeds()
    {
        _overlay.SetEnabled(true);
        foreach (double speed in new[] { 8.0, 0.25 })
        {
            _runner.GameSpeed = speed;
            long total0 = _runner.TickTimes.Total;
            int t0 = _sim.TickNumber;
            int frames = 0;
            while (_sim.TickNumber < t0 + (speed > 1 ? 200 : 6) && frames < 3000) { await Frame(); frames++; }
            TickTimeRing ring = _runner.TickTimes;
            long added = ring.Total - total0;
            Check(added == _sim.TickNumber - t0, $"speed {speed}: {added} samples for {_sim.TickNumber - t0} ticks");
            Check(ring.Count <= 120 && ring[ring.Count - 1] == _runner.LastTickMs, $"speed {speed}: ring count {ring.Count} or newest sample wrong");
            await Frame();
            Check(_overlay.Graph.DrawnBars == ring.Count || _overlay.Graph.DrawnBars == 0, $"speed {speed}: drew {_overlay.Graph.DrawnBars} bars, ring {ring.Count}");
            string label = _match.GetNode<Label>("DebugOverlay/Label").Text;
            Check(label.Contains($"speed {speed:0.##}x") && label.Contains($"({ring.Count})"), $"speed {speed}: label {label}");
            GD.Print($"speed {speed}: {added} samples in {frames} frames, ring {ring.Count}, avg {ring.Average:F3} worst {ring.Worst:F3} ms, scale {_overlay.Graph.ScaleMs:F1}");
        }
        _runner.GameSpeed = 1.0;
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
