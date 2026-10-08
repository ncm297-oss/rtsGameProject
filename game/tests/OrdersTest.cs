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

/// <summary>M2-3: drives the real Match scene's <see cref="SelectionController"/> with injected key and mouse events: type select, control groups, Tab subgroups, S / H / A / Shift-queue orders, whole-order overflow.</summary>
/// <remarks>
/// Run: <c>&amp; $env:GODOT --headless --path game res://tests/OrdersTest.tscn</c>; prints "ORDERS TEST
/// PASS" and exits 0, or prints each failure and exits 1. The test ticks the sim itself (SimRunner
/// disabled), so every "within N ticks" is exact. Windowed with <c>-- --shots &lt;dir&gt;</c> it also
/// saves screenshots of the hold, queue and targeting states.
/// </remarks>
public partial class OrdersTest : Node
{
    private const float VisitRadius = 5f; // meters: a crowd of 5 units of radius 0.4 m gathers well inside this
    private static readonly string[] Modifiers = { "order_queue", "select_add", "select_type", "group_assign", "group_add" };

    private readonly List<string> _failures = new();
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private Label _label = null!;
    private Simulation _sim = null!;
    private Vector2 _screen;
    private string? _shots;

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
        foreach (string m in Modifiers) Input.ActionRelease(m);
        foreach (string f in _failures) GD.Print($"ORDERS TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("ORDERS TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length) _shots = args[at + 1];
        GetTree().Root.Size = new Vector2I(1152, 648); // headless windows are 64 x 64
        await Frame();
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(loaded.Data!, LaunchOptions.Parse(new[] { "--units", "40", "--no-bases", "--no-combat" })); // armies only, no fights, as in M2 (M3-V1, M4-V1 BUG-0147)
        var runner = match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled; // the test ticks the sim itself
        _sim = runner.Simulation!;
        _sel = match.GetNode<SelectionController>("SelectionController");
        _camera = match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _label = match.GetNode<Label>("DebugOverlay/Label");
        Tick(3);
        await Frame();
        _screen = _camera.GetViewport().GetVisibleRect().Size;
        Check(U.Count == 80, $"{U.Count} units spawned, expected 80");

        await TypeSelect();
        await Groups();
        await Tabs();
        await Orders();
        Overflow();
    }

    // Criterion 3: double-click and Ctrl + click select exactly the own on-screen units of the type.
    private async Task TypeSelect()
    {
        System.Numerics.Vector2 home = OwnMean();
        _camera.SetZoom(CameraLimits.MinZoom);
        int slot = -1, type = -1;
        List<int> expected = new();
        foreach (float dx in new[] { 8f, -8f, 14f, -14f, 0f })
        {
            _camera.SetFocus(home.X + dx, home.Y);
            await Frame();
            foreach ((int s, Vector2 _) in Own())
            {
                int t = U.TypeId[s];
                List<int> on = Own().Where(o => U.TypeId[o.Slot] == t).Select(o => o.Slot).ToList();
                int all = Enumerable.Range(0, U.Capacity).Count(i => U.Alive[i] && U.Owner[i] == 0 && U.TypeId[i] == t);
                if (on.Count >= 2 && all > on.Count) { slot = s; type = t; expected = on; break; }
            }
            if (slot >= 0) break;
        }
        Check(slot >= 0, "no camera spot with a type both on and off screen");
        if (slot < 0) return;
        GD.Print($"type select: type {type}, {expected.Count} on screen of {Enumerable.Range(0, U.Capacity).Count(i => U.Alive[i] && U.Owner[i] == 0 && U.TypeId[i] == type)} own");

        Click(At(slot));
        Click(At(slot), doubleClick: true);
        ExpectSlots("double-click", expected);

        int other = Own().First(o => U.TypeId[o.Slot] != type).Slot;
        Click(At(other));
        Hold("select_add");
        Click(At(slot));
        Click(At(slot), doubleClick: true);
        Release("select_add");
        ExpectSlots("shift + double-click adds", expected.Append(other).ToList());

        Click(FindEmpty());
        Hold("select_type");
        Click(At(slot));
        Release("select_type");
        ExpectSlots("ctrl + click", expected);

        Vector2 empty = FindEmpty();
        Click(empty);
        Click(empty, doubleClick: true);
        ExpectSlots("double-click on empty ground clears", new List<int>());

        _camera.SetZoom(CameraLimits.DefaultZoom);
        _camera.SetFocus(home.X, home.Y);
        await Frame();
    }

    // Criterion 4: Ctrl + digit assigns, Shift + digit adds, digit recalls, double-tap centres the camera.
    private async Task Groups()
    {
        List<int> own = Own().Select(o => o.Slot).ToList();
        List<int> five = own.Take(5).ToList();
        SelectSlots(five);
        Hold("group_assign");
        Press(Key.Key1);
        Release("group_assign");
        Click(FindEmpty());
        ExpectSlots("cleared", new List<int>());
        Press(Key.Key1);
        ExpectSlots("recall group 1", five);

        SelectSlots(own.Skip(5).Take(2).ToList());
        Hold("group_assign");
        Press(Key.Key2);
        Release("group_assign");
        SelectSlots(own.Skip(7).Take(2).ToList());
        Hold("group_add");
        Press(Key.Key2);
        Release("group_add");
        Click(FindEmpty());
        Press(Key.Key2);
        ExpectSlots("shift + 2 added", own.Skip(5).Take(4).ToList());

        // A group member dies and its slot is reused by an enemy: never recalled.
        int victim = five[0];
        System.Numerics.Vector2 spot = U.Position[victim];
        U.Free(new EntityHandle(victim, U.Generation[victim]));
        _sim.Enqueue(Command.SpawnUnit(1, _sim.World.Data.Factions[1].Units[0], spot));
        Tick(2); // a command enqueued before tick N applies on tick N + 1: the second Tick() call
        Check(U.Alive[victim] && U.Owner[victim] == 1, $"slot {victim} was not reused by the enemy spawn");
        Press(Key.Key1);
        ulong tapped = Time.GetTicksMsec();
        ExpectSlots("recall after the respawn", five.Skip(1).ToList());

        // Double-tap: a lone press after the window leaves the camera, the second press centres it.
        _camera.SetFocus(U.Position[victim].X + 60f, U.Position[victim].Y);
        // BUG-0088: wait on the clock the tap window is measured with (Time.GetTicksMsec), not a process-time timer,
        // which can run ahead of the wall clock after slow frames and fire inside the window.
        while (Time.GetTicksMsec() - tapped < (ulong)ControlGroups.DoubleTapMs + 50) await Frame();
        System.Numerics.Vector2 away = _camera.Focus;
        Press(Key.Key1);
        Check(System.Numerics.Vector2.Distance(_camera.Focus, away) < 0.01f, "a single recall moved the camera");
        Press(Key.Key1);
        System.Numerics.Vector2 mean = five.Skip(1).Aggregate(System.Numerics.Vector2.Zero, (m, s) => m + U.Position[s]) / 4f;
        float off = System.Numerics.Vector2.Distance(_camera.Focus, mean);
        GD.Print($"double-tap: focus {off:F3} m from the group mean");
        Check(off <= 1f, $"double-tap focus is {off:F2} m from the group mean");
        _camera.SetFocus(OwnMean().X, OwnMean().Y);
        await Frame();
    }

    // Criterion 5: Tab cycles the selection's types in ascending id and resets on a new selection.
    private async Task Tabs()
    {
        List<int> three = Own().GroupBy(o => U.TypeId[o.Slot]).Take(3).Select(g => g.First().Slot).ToList();
        int[] types = three.Select(s => U.TypeId[s]).OrderBy(t => t).ToArray();
        Check(types.Length == 3, $"only {types.Length} types on screen");
        SelectSlots(three);
        Subgroups sub = _sel.Subgroups;
        Check(sub.Count == 3 && sub.Index == 0 && sub.ActiveType == types[0], $"new selection: {sub.Count} subgroups, index {sub.Index}");
        for (int k = 1; k <= 3; k++)
        {
            Press(Key.Tab);
            Check(sub.Index == k % 3 && sub.ActiveType == types[k % 3], $"tab {k}: index {sub.Index} type {sub.ActiveType}, expected {k % 3} / {types[k % 3]}");
        }
        Press(Key.Tab);
        await LabelUpdate();
        string want = $"sub {types[1]} 2/3";
        Check(_label.Text.Contains(want), $"label '{_label.Text}' lacks '{want}'");
        SelectSlots(three);
        Check(sub.Index == 0, $"a new selection kept subgroup {sub.Index}");
        Click(FindEmpty());
        Press(Key.Tab);
        Check(sub.Count == 0 && sub.ActiveType == -1, "tab on an empty selection did something");
    }

    // Criteria 1-2 headless: S, H (holders never pushed), Shift + right-click path, A targeting, Shift + S.
    private async Task Orders()
    {
        List<int> own = Own().Select(o => o.Slot).ToList();
        List<int> walkers = own.Take(10).ToList();
        List<int> pushers = own.Skip(10).Take(10).ToList();
        SelectSlots(walkers);

        // S on 10 walkers.
        int moves = _sel.IssuedCount(CommandKind.Move);
        RightClick(FindEmpty());
        Check(_sel.IssuedCount(CommandKind.Move) - moves == 10, "right-click did not issue 10 moves");
        Tick(10);
        Check(walkers.All(s => U.State[s] == UnitState.Moving), "walkers not all Moving before S");
        int stops = _sel.IssuedCount(CommandKind.Stop);
        Press(Key.S);
        Check(_sel.IssuedCount(CommandKind.Stop) - stops == 10, "S did not issue 10 stops");
        Tick(2);
        Check(walkers.All(s => U.State[s] == UnitState.Idle && U.GoalCell[s] == -1), "S: walkers not all Idle with GoalCell -1 after 2 ticks");

        // H, then another group walks through the holders: they never move.
        Press(Key.H);
        Tick(2);
        Check(walkers.All(s => U.Hold[s]), "H: not every selected unit is holding");
        var held = walkers.ToDictionary(s => s, s => U.Position[s]);
        System.Numerics.Vector2 holdMean = Mean(walkers), pushMean = Mean(pushers);
        System.Numerics.Vector2 beyond = holdMean + System.Numerics.Vector2.Normalize(holdMean - pushMean) * 10f;
        SelectSlots(pushers);
        _sel.Order(CommandKind.Move, new Vector2(beyond.X, beyond.Y), false);
        float closest = float.MaxValue;
        for (int t = 0; t < 300; t++)
        {
            Tick(1);
            foreach (int p in pushers) foreach (int h in walkers) closest = Math.Min(closest, System.Numerics.Vector2.Distance(U.Position[p], U.Position[h]));
            if (t == 120) await Shot("hold");
        }
        float pushed = walkers.Max(s => System.Numerics.Vector2.Distance(U.Position[s], held[s]));
        GD.Print($"hold: pushers came within {closest:F2} m of a holder; holders moved at most {pushed:F4} m");
        Check(pushed == 0f && walkers.All(s => U.Hold[s]), $"a holder was pushed {pushed:F3} m");

        // Shift + right-click three points: each unit's goal goes p1, p2, p3 and it reaches each in turn (Hold ends on the
        // first leg). From here on only up to 5 spread-out holders stay on the field: walkers meeting idle, holding or
        // packed friends give up (BUG-0028, sim side), and this scene checks the view's order path, not crowd routing.
        System.Numerics.Vector2 m = Mean(walkers);
        List<int> queuers = new();
        foreach (int s in walkers) if (queuers.Count < 5 && queuers.All(o => System.Numerics.Vector2.Distance(U.Position[o], U.Position[s]) >= 3f)) queuers.Add(s);
        Check(queuers.Count >= 3, $"only {queuers.Count} spread-out walkers");
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.Owner[i] == 0 && !queuers.Contains(i)) U.Free(new EntityHandle(i, U.Generation[i]));
        walkers = queuers;
        await Frame();
        SelectSlots(queuers);
        // Three points 10+ m apart, each 8+ m from every other unit, so the walkers' crowd can reach each one.
        var points = new List<System.Numerics.Vector2>();
        List<int> others = Enumerable.Range(0, U.Capacity).Where(i => U.Alive[i] && !queuers.Contains(i)).ToList();
        for (int k = 0; k < 64 && points.Count < 3; k++)
        {
            float r = 10f + 4f * (k / 16), a = k % 16 * MathF.PI / 8f;
            System.Numerics.Vector2 c = m + new System.Numerics.Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            if (TryGroundScreen(c, out _) && others.All(o => System.Numerics.Vector2.Distance(U.Position[o], c) > 8f)
                && points.All(q => System.Numerics.Vector2.Distance(q, c) >= 10f)) points.Add(c);
        }
        Check(points.Count == 3, $"only {points.Count} passable on-screen queue points");
        if (points.Count < 3) return;
        moves = _sel.IssuedCount(CommandKind.Move);
        Hold("order_queue");
        Hold("select_add");
        var targets = new System.Numerics.Vector2[3];
        for (int k = 0; k < 3; k++)
        {
            TryGroundScreen(points[k], out Vector2 px);
            targets[k] = Pick(px);
            RightClick(px);
        }
        Release("order_queue");
        Release("select_add");
        Check(_sel.IssuedCount(CommandKind.Move) - moves == 3 * queuers.Count, "Shift + right-click x3 did not issue 3 queued moves per unit");
        Tick(2);
        Check(queuers.All(s => U.QueueCount[s] == 2 && U.State[s] == UnitState.Moving && !U.Hold[s]), "first queued leg did not start (queue 2, Moving, Hold cleared)");
        var leg = queuers.ToDictionary(s => s, _ => 0);
        var reached = queuers.ToDictionary(s => s, _ => new bool[3]);
        int ticks = 0;
        for (; ticks < 1500 && reached.Values.Any(r => !r[2]); ticks++)
        {
            Tick(1);
            foreach (int s in queuers)
            {
                int now = Array.FindIndex(targets, p => System.Numerics.Vector2.Distance(U.Goal[s], p) < 0.01f);
                if (now > leg[s])
                {
                    Check(now == leg[s] + 1 && reached[s][leg[s]], $"slot {s} went to leg {now} at tick {ticks} before reaching leg {leg[s]} " +
                        $"({System.Numerics.Vector2.Distance(U.Position[s], targets[leg[s]]):F1} m from it)");
                    leg[s] = now;
                }
                if (now >= 0 && System.Numerics.Vector2.Distance(U.Position[s], targets[now]) <= VisitRadius) reached[s][now] = true;
            }
            if (ticks == 200) await Shot("queue");
        }
        GD.Print($"shift-queue: {queuers.Count} units, 3 legs each done in {ticks} ticks ({ticks / 160f:F1} s at 8x)");
        Check(reached.Values.All(r => r.All(x => x)), "not every unit visited all three queued points in order within 1500 ticks");

        // A + click on the first queue point (every unit is 10+ m from it, at the third): targeting, AttackMove per selected unit, the click never selects.
        SelectSlots(walkers);
        Vector2 home = Screen(points[0]);
        System.Numerics.Vector2 homeGround = Pick(home);
        int attacks = _sel.IssuedCount(CommandKind.AttackMove);
        Press(Key.A);
        Check(_sel.Targeting, "A did not start targeting");
        await LabelUpdate();
        Check(_label.Text.EndsWith(" A"), $"label '{_label.Text}' has no A suffix");
        await Shot("targeting");
        Click(home);
        Check(!_sel.Targeting && _sel.IssuedCount(CommandKind.AttackMove) - attacks == walkers.Count, $"A + click: {_sel.IssuedCount(CommandKind.AttackMove) - attacks} attack-moves for {walkers.Count}");
        ExpectSlots("A + click kept the selection", walkers);
        var start = walkers.ToDictionary(s => s, s => System.Numerics.Vector2.Distance(U.Position[s], homeGround));
        Tick(2);
        Check(walkers.All(s => U.State[s] == UnitState.Moving && System.Numerics.Vector2.Distance(U.Goal[s], homeGround) < 0.01f),
            $"A + click: not every unit is walking to the point: {string.Join(", ", walkers.Select(s => $"{s} {U.State[s]} {System.Numerics.Vector2.Distance(U.Goal[s], homeGround):F2}"))}");
        Tick(400);
        float far = walkers.Max(s => System.Numerics.Vector2.Distance(U.Position[s], homeGround));
        float mean = walkers.Average(s => System.Numerics.Vector2.Distance(U.Position[s], homeGround));
        GD.Print($"attack-move: {walkers.Count} units from up to {start.Values.Max():F1} m to mean {mean:F1} m, max {far:F1} m");
        Check(walkers.All(s => System.Numerics.Vector2.Distance(U.Position[s], homeGround) <= VisitRadius) && mean <= VisitRadius,
            "attack-movers did not close on the point");

        // Esc and right-click cancel without ordering; A twice stays armed; an off-map click keeps targeting.
        int pending = _sim.PendingCommandCount;
        Press(Key.A);
        Press(Key.Escape);
        Check(!_sel.Targeting, "Esc did not cancel targeting");
        Press(Key.A);
        Press(Key.A);
        Check(_sel.Targeting, "A twice disarmed targeting");
        moves = _sel.IssuedCount(CommandKind.Move);
        RightClick(home);
        Check(!_sel.Targeting && _sim.PendingCommandCount == pending && _sel.IssuedCount(CommandKind.Move) == moves, "right-click while targeting ordered something or kept targeting");
        _camera.SetFocus(0f, 0f);
        await Frame();
        Press(Key.A);
        Click(new Vector2(4, 4));
        Check(_sel.Targeting && _sim.PendingCommandCount == pending, "an off-map click while targeting ordered or disarmed");
        Press(Key.Escape);
        _camera.SetFocus(m.X, m.Y);
        await Frame();
        Click(FindEmpty());
        Press(Key.A);
        Check(!_sel.Targeting, "A with an empty selection started targeting");

        // Shift + S: a walker keeps walking with the Stop queued, then stops on arrival.
        SelectSlots(walkers);
        RightClick(Screen(points[2]));
        Tick(3);
        Hold("order_queue");
        Press(Key.S);
        Release("order_queue");
        Tick(2);
        Check(walkers.All(s => (U.State[s] == UnitState.Moving && U.QueueCount[s] == 1) || (U.State[s] == UnitState.Idle && U.GoalCell[s] == -1)),
            "Shift + S: a walker neither kept walking with 1 queued nor stopped");
        Tick(600);
        Check(walkers.All(s => U.State[s] == UnitState.Idle && U.GoalCell[s] == -1 && U.QueueCount[s] == 0), "Shift + S: walkers did not stop after arriving");
    }

    // Criterion 7: 1,000 selected against a nearly full queue: S, H, Shift + S, Shift + right-click each dropped whole.
    private void Overflow()
    {
        float maxR = _sim.World.Data.Factions[0].Units.Max(t => _sim.World.Data.Units[t].Radius);
        System.Numerics.Vector2[] spots = StartLayout.Block(_sim.World.NavGrid, 1000, true, maxR);
        int own = Enumerable.Range(0, U.Capacity).Count(i => U.Alive[i] && U.Owner[i] == 0);
        for (int k = own; k < spots.Length; k++) _sim.Enqueue(Command.SpawnUnit(0, _sim.World.Data.Factions[0].Units[0], spots[k]));
        Tick(2);
        _sel.Selection.Clear();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.Owner[i] == 0) _sel.Selection.Add(new EntityHandle(i, U.Generation[i]));
        int n = _sel.Selection.Count, cap = _sim.World.Config.CommandCapacity;
        Check(n == 1000, $"{n} own units for the overflow check");
        while (_sim.PendingCommandCount < cap - n + 1) _sim.Enqueue(Command.Noop(1));
        int pending = _sim.PendingCommandCount, dropped = _sel.DroppedOrders;
        Press(Key.S);
        Press(Key.H);
        Hold("order_queue");
        Press(Key.S);
        RightClick(_screen / 2); // the camera's focus: on the map
        Release("order_queue");
        Check(_sim.PendingCommandCount == pending && _sel.DroppedOrders == dropped + 4 && _sel.Selection.Count == n,
            $"overflow: pending {pending} -> {_sim.PendingCommandCount}, dropped {dropped} -> {_sel.DroppedOrders}");
        GD.Print($"overflow: {pending} pending + {n} selected > {cap}: 4 orders dropped whole");
    }

    private void Tick(int n)
    {
        for (int i = 0; i < n; i++) _sim.Tick();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    // process_frame fires before the nodes' _Process, so the label is current only after a second one.
    private async Task LabelUpdate()
    {
        await Frame();
        await Frame();
    }

    private async Task Shot(string name)
    {
        if (_shots == null) return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/orders-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path}");
    }

    // Own live units whose projected centre is inside the viewport.
    private List<(int Slot, Vector2 At)> Own()
    {
        var list = new List<(int, Vector2)>();
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != SelectionController.LocalPlayer || !_sel.TryScreenPosition(i, out Vector2 p)) continue;
            if (p.X >= 0 && p.Y >= 0 && p.X <= _screen.X && p.Y <= _screen.Y) list.Add((i, p));
        }
        return list;
    }

    private System.Numerics.Vector2 OwnMean() =>
        Mean(Enumerable.Range(0, U.Capacity).Where(i => U.Alive[i] && U.Owner[i] == 0).ToList());

    private System.Numerics.Vector2 Mean(List<int> slots) =>
        slots.Aggregate(System.Numerics.Vector2.Zero, (m, s) => m + U.Position[s]) / slots.Count;

    private Vector2 At(int slot)
    {
        _sel.TryScreenPosition(slot, out Vector2 p);
        return p;
    }

    private Vector2 Screen(System.Numerics.Vector2 ground) =>
        _camera.UnprojectPosition(new Vector3(ground.X, TerrainHeight.At(_sim.World.Heightmap, ground.X, ground.Y), ground.Y));

    // A ground point's screen position, if it is well inside the screen and its picked cell is passable.
    private bool TryGroundScreen(System.Numerics.Vector2 ground, out Vector2 px)
    {
        px = Screen(ground);
        if (px.X < 20 || px.Y < 20 || px.X > _screen.X - 20 || px.Y > _screen.Y - 20) return false;
        System.Numerics.Vector2 hit = Pick(px);
        NavGrid grid = _sim.World.NavGrid;
        return grid.WorldToCell(hit, out int x, out int y) && grid.IsPassable(x, y);
    }

    private System.Numerics.Vector2 Pick(Vector2 px)
    {
        Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
        Check(GroundPicker.TryPick(_sim.World.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out System.Numerics.Vector3 hit), $"pixel {px} missed the map");
        return new(hit.X, hit.Z);
    }

    private void SelectSlots(List<int> slots)
    {
        Click(At(slots[0]));
        Hold("select_add");
        foreach (int s in slots.Skip(1)) Click(At(s));
        Release("select_add");
        ExpectSlots("select", slots);
    }

    private static void Hold(string action) => Input.ActionPress(action);

    private static void Release(string action) => Input.ActionRelease(action);

    private void Press(Key key)
    {
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void Click(Vector2 at, bool doubleClick = false)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, DoubleClick = doubleClick });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private void RightClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    // A screen point at least 30 px from every live unit, in the middle of the screen (so it is on the map).
    private Vector2 FindEmpty()
    {
        var units = new List<Vector2>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && _sel.TryScreenPosition(i, out Vector2 p)) units.Add(p);
        for (float y = _screen.Y * 0.3f; y < _screen.Y * 0.8f; y += 10)
        {
            for (float x = _screen.X * 0.1f; x < _screen.X * 0.9f; x += 10)
            {
                var p = new Vector2(x, y);
                if (units.All(u => u.DistanceTo(p) > 30f)) return p;
            }
        }
        throw new InvalidOperationException("no empty ground on screen");
    }

    private void ExpectSlots(string what, List<int> slots)
    {
        bool ok = _sel.Selection.Count == slots.Count && slots.All(s => _sel.Selection.Contains(new EntityHandle(s, U.Generation[s])));
        Check(ok, $"{what}: {_sel.Selection.Count} selected, expected slots [{string.Join(", ", slots)}]");
    }

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
