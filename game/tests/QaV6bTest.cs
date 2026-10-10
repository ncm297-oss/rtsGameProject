using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M4-V6b (session 2026-10-10-0215) on the real Match scene. (1) The marker pool at its cap: 500 units at
/// <c>--units 500 --no-fog</c> with all 8 status entries each (4,000 markers): every marker drawn, 0 bytes over 300
/// frames of <c>AbilityViews._Process</c> and of <c>Sync</c>. (2) 200 resolves landing in one tick: the flash pool caps at
/// its size, every shown flash on visible ground, all gone within 0.5 s, the pool empty after, 0 bytes. (3) Two mages,
/// Shift-queued casts, the first caster dying mid-queue: no exception, no bar or ring for the dead slot, still armed, the
/// next Shift click goes to the live mage. (4) The selection panel's "+N" line with one selected unit dying per frame
/// out of 150: 0 bytes once warm (BUG-0340). (5) Minimap left-clicks (the "select" action, cached too): 0 bytes.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaV6bTest.tscn</c>; prints "QA V6B TEST PASS" and exits 0, or each failure and exits 1.</remarks>
public partial class QaV6bTest : Node
{
    private static readonly FieldInfo CommandsField = typeof(Simulation).GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private SelectionPanel _panel = null!;
    private CommandCard _card = null!;
    private AbilityViews _views = null!;
    private Minimap _mini = null!;
    private RtsCamera _camera = null!;
    private Vector2 _screen;
    private int _telas, _mage, _raider;

    private World W => _sim.World;
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
            _telas = _data.FindAbility("telas_fire");
            _mage = _data.FindUnit("malazan_cadre_mage");
            _raider = _data.FindUnit("whirlwind_raider");
            await MarkerPool(1);
            await FlashStorm(6);
            await DyingQueuedCaster(1);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(40)) GD.Print($"QA V6B TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA V6B TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private void StartMatch(ulong seed, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        var args = new List<string> { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--mute", "--zoom", "30" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _panel = _match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _views = _match.GetNode<AbilityViews>("World3D/AbilityViews");
        _mini = _match.GetNode<Minimap>("Hud/Minimap");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _screen = GetViewport().GetVisibleRect().Size;
    }

    private async Task EndMatch()
    {
        Input.ActionRelease("order_queue");
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    // ---- (1) 500 units x 8 statuses, (4) panel "+N" with deaths, (5) minimap left-clicks ----

    private async Task MarkerPool(ulong seed)
    {
        StartMatch(seed, "--units", "250", "--no-fog", "--no-combat");
        _sim.Tick();
        _sim.Tick();
        Twin(seed, "pool");
        // Test staging after the twin: fill every live unit's 8 entries straight into the store's public arrays.
        StatusStore s = U.Statuses;
        int n = _data.Statuses.Length, units = 0;
        for (int i = 0; i < U.Capacity && units < 500; i++)
        {
            if (!U.Alive[i]) continue;
            s.Count[i] = StatusStore.PerUnit;
            for (int k = 0; k < StatusStore.PerUnit; k++)
            {
                int at = i * StatusStore.PerUnit + k;
                s.StatusId[at] = k % n;
                s.TicksRemaining[at] = 100000;
                s.Magnitude[at] = 0f;
            }
            units++;
        }
        int want = units * StatusStore.PerUnit;
        for (int f = 0; f < 30; f++) _views._Process(0.016);
        Check(_views.ShownMarkers == want && _views.MarkerMesh.VisibleInstanceCount == want,
            $"pool: {_views.ShownMarkers} markers shown (mesh {_views.MarkerMesh.VisibleInstanceCount}), want {want} for {units} units");
        int badRows = 0;
        for (int k = 0; k < _views.ShownMarkers; k++)
        {
            StatusMark m = _views.MarkerAt(k);
            Color got = _views.MarkerMesh.GetInstanceColor(k), wantColor = _views.StatusColor(m.Status);
            // The headless (dummy) renderer keeps no instance data: the colour is read back only in a windowed run.
            bool colorOk = DisplayServer.GetName() == "headless" || Math.Abs(got.R - wantColor.R) < 0.01f && Math.Abs(got.G - wantColor.G) < 0.01f && Math.Abs(got.B - wantColor.B) < 0.01f;
            if (m.Row != StatusStore.PerUnit || m.Place != k % StatusStore.PerUnit || !colorOk)
                if (badRows++ < 3) Check(false, $"pool: marker {k} {m}, colour {got} want {wantColor}");
        }
        var watch = new System.Diagnostics.Stopwatch(); // made outside the measured span
        long p0 = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (int f = 0; f < 300; f++) _views._Process(0.016);
        watch.Stop();
        long processBytes = GC.GetAllocatedBytesForCurrentThread() - p0;
        p0 = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 300; f++)
        {
            if (f % 3 == 0) _sim.Tick();
            _views.Sync(W, f % 3 / 3f, new Vector2(_screen.X * 0.5f, _screen.Y * 0.4f), false);
        }
        long syncBytes = GC.GetAllocatedBytesForCurrentThread() - p0;
        GD.Print($"pool: {units} units x 8 = {_views.ShownMarkers} markers; _Process {processBytes} B over 300 frames ({watch.Elapsed.TotalMilliseconds / 300:0.000} ms a frame), Sync with ticks {syncBytes} B");
        Check(processBytes == 0 && syncBytes == 0, $"pool: _Process {processBytes} B, Sync {syncBytes} B over 300 frames with {want} markers");
        Check(units == 500, $"pool: staged {units} units, want 500");

        // A unit dying mid-status: its 8 markers go the same frame.
        int victim = -1;
        for (int i = 0; i < U.Capacity && victim < 0; i++) if (U.Alive[i]) victim = i;
        U.Free(new EntityHandle(victim, U.Generation[victim]));
        _views._Process(0.016);
        int left = 0;
        for (int k = 0; k < _views.ShownMarkers; k++) if (_views.MarkerAt(k).Unit == victim) left++;
        Check(left == 0 && _views.ShownMarkers == want - StatusStore.PerUnit, $"pool: {left} markers left over freed slot {victim}, {_views.ShownMarkers} shown");

        // (4) The panel: 150 own units selected, one dies per frame; the "+N" line changes every frame.
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        for (int i = 0; i < U.Capacity && _sel.Selection.Count < 150; i++)
            if (U.Alive[i] && U.Owner[i] == 0) _sel.Selection.Add(new EntityHandle(i, U.Generation[i]));
        _sel.Subgroups.Update(_sel.Selection.Items, U.TypeId, reset: true);
        await Frames();
        var order = new List<EntityHandle>(_sel.Selection.Items.ToArray());
        int rebuilds0 = _panel.Rebuilds, killed = 0;
        long panelBytes = 0;
        for (int f = 0; f < 60; f++)
        {
            EntityHandle h = order[order.Count - 1 - f];
            U.Free(h);
            _sel.Selection.Prune(U.Alive, U.Generation);
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            _panel.Sync();
            long spent = GC.GetAllocatedBytesForCurrentThread() - b0;
            if (f >= 10) panelBytes += spent;
            killed++;
        }
        string label = _panel.OverflowLabel.Text;
        int expectOver = PortraitGrid.Overflow(150 - killed);
        GD.Print($"panel: {killed} selected units died, {_panel.Rebuilds - rebuilds0} overflow rewrites, label '{label}' (want +{expectOver}), {panelBytes} B over the last 50");
        Check(panelBytes == 0 && _panel.Rebuilds - rebuilds0 >= 50 && label == (expectOver > 0 ? $"+{expectOver}" : label),
            $"panel: {panelBytes} B over 50 deaths, {_panel.Rebuilds - rebuilds0} rewrites, label '{label}'");

        // (5) Minimap left-click jumps (select action): 0 B once warm.
        var events = new InputEvent[16];
        MinimapTransform fit = _mini.Fit;
        for (int k = 0; k < events.Length; k++)
        {
            System.Numerics.Vector2 mp = fit.ToPixel(new System.Numerics.Vector2(20f + 4f * k, 30f));
            events[k] = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = k % 2 == 0, Position = new Vector2(mp.X, mp.Y) };
        }
        for (int k = 0; k < 4; k++) _mini._GuiInput(events[k]);
        long m0 = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 4; k < events.Length; k++) _mini._GuiInput(events[k]);
        long miniBytes = GC.GetAllocatedBytesForCurrentThread() - m0;
        GD.Print($"minimap left-clicks: {events.Length - 4} events, {miniBytes} B");
        Check(miniBytes == 0, $"minimap left-click: {miniBytes} B over {events.Length - 4} events");
        await EndMatch();
    }

    // ---- (2) 200 resolves in one tick ----

    private async Task FlashStorm(ulong seed)
    {
        StartMatch(seed, "--units", "0", "--no-bases", "--no-combat");
        int center = FlowField.NearestPassable(W.NavGrid, W.NavGrid.Height / 2 * W.NavGrid.Width + W.NavGrid.Width / 2);
        System.Numerics.Vector2 c = W.NavGrid.CellCenter(center % W.NavGrid.Width, center / W.NavGrid.Width);
        _sim.Enqueue(Command.SpawnUnit(0, _data.FindUnit("malazan_laborer"), c));
        for (int t = 0; t < 8; t++) _sim.Tick();
        Twin(seed, "storm");
        ResolveFlashes f = _views.Flashes;
        // 200 resolves "in one tick": 100 on visible ground round the Laborer, 100 far away under the fog.
        long tick = W.TickNumber;
        int seen = 0;
        for (int k = 0; k < 200; k++)
        {
            System.Numerics.Vector2 p = k < 100
                ? c + new System.Numerics.Vector2(k % 10 - 5f, k / 10 - 5f)
                : new System.Numerics.Vector2(4f + (k - 100) % 10, 4f + (k - 100) / 10);
            if (W.Fog.IsVisible(0, FogView.CellOf(W.Fog.Width, W.Fog.Height, p))) seen++;
            f.Add(_telas, k % 2, p, tick);
        }
        for (int k = 0; k < 400; k++) f.Add(_telas, 1, c, tick); // and 400 more: the pool must cap, never grow
        _views.Sync(W, 0.5f, Vector2.Zero, false);
        Check(f.Count == f.Capacity && _views.ShownFlashes <= f.Capacity, $"storm: {f.Count} in the pool of {f.Capacity}, {_views.ShownFlashes} shown");
        int badShown = 0;
        for (int i = 0; i < f.Capacity; i++)
            if (_views.FlashTransform(i).Basis.X != Vector3.Zero && !W.Fog.IsVisible(0, FogView.CellOf(W.Fog.Width, W.Fog.Height, f.Point[i]))) badShown++;
        Check(badShown == 0, $"storm: {badShown} flashes drawn over fogged ground");
        long bytes = 0;
        int ticks = 0;
        while (ticks < 20 && (f.Count > 0 || _views.ShownFlashes > 0))
        {
            _sim.Tick();
            ticks++;
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            _views.Sync(W, 0.5f, Vector2.Zero, false);
            bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
        }
        int lingering = 0;
        for (int i = 0; i < f.Capacity; i++) if (_views.FlashTransform(i).Basis.X != Vector3.Zero) lingering++;
        GD.Print($"storm: 600 flashes added in one tick ({seen} of the first 200 on visible ground), pool {f.Capacity}, replaced {f.Replaced}; gone after {ticks} ticks; {bytes} B; {lingering} instances still drawn");
        Check(f.Count == 0 && _views.ShownFlashes == 0 && lingering == 0 && ticks <= ResolveFlashes.LifetimeTicks + 1,
            $"storm: after {ticks} ticks {f.Count} in the pool, {_views.ShownFlashes} shown, {lingering} instances drawn");
        Check(bytes == 0, $"storm: {bytes} B while the storm faded");
        await EndMatch();
    }

    // ---- (3) Shift-queued casts, the first caster dies mid-queue ----

    private async Task DyingQueuedCaster(ulong seed)
    {
        StartMatch(seed, "--units", "0", "--no-bases", "--no-combat");
        int center = FlowField.NearestPassable(W.NavGrid, W.NavGrid.Height / 2 * W.NavGrid.Width + W.NavGrid.Width / 2);
        System.Numerics.Vector2 c = W.NavGrid.CellCenter(center % W.NavGrid.Width, center / W.NavGrid.Width);
        _sim.Enqueue(Command.SpawnUnit(0, _mage, c + new System.Numerics.Vector2(-6f, 0f)));
        _sim.Enqueue(Command.SpawnUnit(0, _mage, c + new System.Numerics.Vector2(-6f, 3f)));
        _sim.Enqueue(Command.SpawnUnit(0, _mage, c + new System.Numerics.Vector2(-6f, -3f)));
        for (int t = 0; t < 4; t++) _sim.Tick();
        var mages = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.TypeId[i] == _mage) mages.Add(new EntityHandle(i, U.Generation[i]));
        if (!Check(mages.Count == 3, $"dying: {mages.Count} mages")) { await EndMatch(); return; }
        _camera.SetFocus(c.X, c.Y);
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        foreach (EntityHandle m in mages) _sel.Selection.Add(m);
        _sel.Subgroups.Update(_sel.Selection.Items, U.TypeId, reset: true);
        await Frames();
        Input.ActionPress("order_queue");
        Check(_sel.BeginAbility(_telas), "dying: could not arm Telas Fire");
        var px = new Vector2(_screen.X * 0.55f, _screen.Y * 0.35f);
        int from = Pending();
        _sel.AttackMoveClick(px);
        Command first = PendingList(from).FirstOrDefault();
        _sim.Tick();
        _sim.Tick();
        // The first caster dies with its queued cast in hand.
        if (U.IsAlive(first.Unit)) U.Free(first.Unit);
        int bad = 0;
        for (int t = 0; t < 6; t++)
        {
            _sim.Tick();
            _views.Sync(W, 0.5f, px, true);
            _card.Sync();
            _panel.Sync();
            for (int k = 0; k < _views.ShownBars; k++) if (_views.BarSlot(k) == first.Unit.Index && !U.Alive[first.Unit.Index]) bad++;
            for (int k = 0; k < _views.ShownCircles; k++) if (_views.CircleSlot(k) == first.Unit.Index && !U.Alive[first.Unit.Index]) bad++;
        }
        bool armed = _sel.Targeting && _sel.TargetAbility == _telas;
        from = Pending();
        _sel.AttackMoveClick(px + new Vector2(20f, 0f));
        List<Command> second = PendingList(from);
        Input.ActionRelease("order_queue");
        GD.Print($"dying: first cast by {first.Unit.Index} (freed), armed after {armed}, second click sent {second.Count} ({(second.Count > 0 ? second[0].Unit.Index : -1)}, queued {(second.Count > 0 && second[0].IsQueued)}), {bad} bars/rings over the dead slot");
        Check(first.Kind == CommandKind.UseAbility && first.IsQueued, $"dying: the first click sent {first.Kind}");
        Check(bad == 0, $"dying: {bad} bars or rings drawn over the dead caster's slot");
        Check(armed, "dying: the caster's death disarmed the Shift targeting");
        Check(second.Count == 1 && second[0].Kind == CommandKind.UseAbility && second[0].IsQueued && U.IsAlive(second[0].Unit) && second[0].Unit != first.Unit,
            $"dying: the second click sent {second.Count} commands");
        for (int t = 0; t < 80; t++) { _sim.Tick(); _views.Sync(W, 0.5f, px, false); }
        await EndMatch();
    }

    private void Twin(ulong seed, string row)
    {
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok, $"{row} seed {seed}: twin {twin.Error} at tick {twin.Tick}");
    }

    private int Pending() => ((CommandQueue)CommandsField.GetValue(_sim)!).Count;

    private List<Command> PendingList(int from)
    {
        var q = (CommandQueue)CommandsField.GetValue(_sim)!;
        var list = new List<Command>();
        for (int i = from; i < q.Count; i++) list.Add(q[i]);
        return list;
    }

    private async Task Frames()
    {
        await Frame();
        await Frame();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
