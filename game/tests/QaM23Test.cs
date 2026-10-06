using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>QA (M2-3): order and selection keys through the viewport's real input routing (PushInput, events carrying modifier flags), modifier edge cases, empty-selection keys, A-mode with the minimap, Tab after a minimap click, key echo, 1,000-unit overflow.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaM23Test.tscn</c>; prints
/// "QA M2-3 TEST PASS" and exits 0, or prints each failure and exits 1. Modifier keys are held with
/// Input.ActionPress (the Input singleton's state, as a held key would set it) and the key / mouse
/// events themselves carry the matching shift / ctrl flags, as the OS sends them.
/// </remarks>
public partial class QaM23Test : Node
{
    private static readonly string[] Modifiers = { "order_queue", "select_add", "select_type", "group_assign", "group_add" };
    private readonly List<string> _failures = new();
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private Minimap _mini = null!;
    private Simulation _sim = null!;
    private Vector2 _screen;
    private bool _shift, _ctrl;

    private UnitStore U => _sim.World.Units;

    public override async void _Ready()
    {
        try
        {
            await Setup();
            await EmptySelectionKeys();
            await GroupModifiers();
            await KeyEcho();
            await CtrlDuringBoxDrag();
            await DoubleClickEdges();
            await TargetingWithMinimap();
            await TabAfterMinimapClick();
            await TargetingWithEmptiedSelection();
            Overflow();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        ReleaseAll();
        foreach (string f in _failures) GD.Print($"QA M2-3 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M2-3 TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private async Task Setup()
    {
        GetTree().Root.Size = new Vector2I(1152, 648);
        await Frame();
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(loaded.Data!, LaunchOptions.Parse(new[] { "--units", "60" }));
        var runner = match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = runner.Simulation!;
        _sel = match.GetNode<SelectionController>("SelectionController");
        _camera = match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _mini = match.GetNode<Minimap>("Hud/Minimap");
        Tick(3);
        await Frame();
        _screen = _camera.GetViewport().GetVisibleRect().Size;
        Check(U.Count == 120, $"{U.Count} units, expected 120");
    }

    // S, H, A, Tab, Esc, digits with every modifier on an empty selection: nothing is enqueued, no group changes, nothing throws.
    private async Task EmptySelectionKeys()
    {
        List<int> own = Own();
        SelectSlots(own.Take(4).ToList());
        Key(Godot.Key.Key3, ctrl: true);
        Check(_sel.Groups.Items(2).Length == 4, "Ctrl+3 did not assign 4");
        LeftClick(FindEmpty());
        Check(_sel.Selection.Count == 0, "click on empty ground did not clear");
        int pending = _sim.PendingCommandCount;
        foreach (Key k in new[] { Godot.Key.S, Godot.Key.H, Godot.Key.A, Godot.Key.Tab, Godot.Key.Escape })
        {
            Key(k);
            Key(k, shift: true);
        }
        Check(!_sel.Targeting, "A on an empty selection armed targeting");
        Key(Godot.Key.Key3, ctrl: true);
        Check(_sel.Groups.Items(2).Length == 4, "Ctrl+3 with an empty selection wiped the group");
        Key(Godot.Key.Key3, shift: true);
        Check(_sel.Groups.Items(2).Length == 4, "Shift+3 with an empty selection changed the group");
        Key(Godot.Key.Key5);
        Check(_sel.Selection.Count == 0, "recall of an empty group selected something");
        RightClick(FindEmpty());
        Check(_sim.PendingCommandCount == pending, $"keys on an empty selection enqueued {_sim.PendingCommandCount - pending} commands");
        Check(_sel.Subgroups.Count == 0, "subgroups with an empty selection");
        await Frame();
    }

    // Ctrl + Shift + digit: assign wins; Shift + digit with real shift flags adds; digit recalls; group 9 works.
    private async Task GroupModifiers()
    {
        List<int> own = Own();
        SelectSlots(own.Take(3).ToList());
        Key(Godot.Key.Key9, ctrl: true);
        SelectSlots(own.Skip(3).Take(2).ToList());
        Key(Godot.Key.Key9, ctrl: true, shift: true);
        Check(Same(_sel.Groups.Items(8), own.Skip(3).Take(2)), $"Ctrl+Shift+9: group has {_sel.Groups.Items(8).Length}, expected the 2 (assign wins)");
        SelectSlots(own.Skip(5).Take(2).ToList());
        Key(Godot.Key.Key9, shift: true);
        Check(Same(_sel.Groups.Items(8), own.Skip(3).Take(4)), $"Shift+9 did not add: {_sel.Groups.Items(8).Length}");
        LeftClick(FindEmpty());
        Key(Godot.Key.Key9);
        Check(Same(_sel.Selection.Items, own.Skip(3).Take(4)), $"9 recalled {_sel.Selection.Count}");
        // Shift + 9 with the group already holding the selection: no duplicates.
        Key(Godot.Key.Key9, shift: true);
        Check(_sel.Groups.Items(8).Length == 4, "Shift+9 duplicated members");
        await Frame();
    }

    // A held S / H auto-repeats (echo events): one order only.
    private async Task KeyEcho()
    {
        SelectSlots(Own().Take(5).ToList());
        int stops = _sel.IssuedCount(CommandKind.Stop);
        Key(Godot.Key.S, release: false);
        for (int i = 0; i < 5; i++) Push(new InputEventKey { Keycode = Godot.Key.S, PhysicalKeycode = Godot.Key.S, Pressed = true, Echo = true });
        Push(new InputEventKey { Keycode = Godot.Key.S, PhysicalKeycode = Godot.Key.S, Pressed = false });
        Check(_sel.IssuedCount(CommandKind.Stop) - stops == 5, $"held S issued {_sel.IssuedCount(CommandKind.Stop) - stops} stops for 5 units");
        Tick(2);
        await Frame();
    }

    // Ctrl held during a box drag box-selects (all types), never type-selects.
    private async Task CtrlDuringBoxDrag()
    {
        List<int> own = Own();
        Vector2 a = At(own[0]), b = At(own[1]);
        Vector2 lo = new(Math.Min(a.X, b.X) - 6, Math.Min(a.Y, b.Y) - 6), hi = new(Math.Max(a.X, b.X) + 6, Math.Max(a.Y, b.Y) + 6);
        if (hi.X - lo.X < 10) hi.X = lo.X + 10;
        List<int> inBox = own.Where(s => { Vector2 p = At(s); return p.X >= lo.X && p.X <= hi.X && p.Y >= lo.Y && p.Y <= hi.Y; }).ToList();
        HoldMods(shift: false, ctrl: true);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = lo, CtrlPressed = true });
        Push(new InputEventMouseMotion { Position = (lo + hi) / 2, CtrlPressed = true });
        Push(new InputEventMouseMotion { Position = hi, CtrlPressed = true });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = hi, CtrlPressed = true });
        ReleaseAll();
        Check(Same(_sel.Selection.Items, inBox), $"Ctrl + box drag selected {_sel.Selection.Count}, expected the {inBox.Count} in the box");
        await Frame();
    }

    // Double-click edges: second click on empty ground acts as a click; on an enemy selects nothing; Shift + double-click on empty keeps.
    private async Task DoubleClickEdges()
    {
        List<int> own = Own();
        int s = own[0];
        LeftClick(At(s));
        LeftClick(FindEmpty(), doubleClick: true);
        Check(_sel.Selection.Count == 0, $"double-click finishing on empty ground left {_sel.Selection.Count} selected");

        SelectSlots(own.Take(3).ToList());
        HoldMods(shift: true, ctrl: false);
        LeftClick(FindEmpty(), shift: true);
        LeftClick(FindEmpty(), doubleClick: true, shift: true);
        ReleaseAll();
        Check(_sel.Selection.Count == 3, $"Shift + double-click on empty ground changed the selection to {_sel.Selection.Count}");

        // An enemy on screen: move the camera over the enemy army.
        System.Numerics.Vector2 enemy = Mean(Enumerable.Range(0, U.Capacity).Where(i => U.Alive[i] && U.Owner[i] == 1).ToList());
        _camera.SetFocus(enemy.X, enemy.Y);
        await Frame();
        int e = Enumerable.Range(0, U.Capacity).FirstOrDefault(i => U.Alive[i] && U.Owner[i] == 1 && OnScreen(i), -1);
        Check(e >= 0, "no enemy on screen");
        if (e >= 0)
        {
            LeftClick(At(e));
            LeftClick(At(e), doubleClick: true);
            Check(!AnyEnemySelected(), "double-click on an enemy selected an enemy");
            HoldMods(shift: false, ctrl: true);
            LeftClick(At(e), ctrl: true);
            ReleaseAll();
            Check(!AnyEnemySelected(), "Ctrl + click on an enemy selected an enemy");
        }
        System.Numerics.Vector2 home = Mean(Enumerable.Range(0, U.Capacity).Where(i => U.Alive[i] && U.Owner[i] == 0).ToList());
        _camera.SetFocus(home.X, home.Y);
        await Frame();
    }

    // A twice stays armed; a minimap left click jumps the camera and keeps targeting; a 3D click then orders AttackMove.
    private async Task TargetingWithMinimap()
    {
        List<int> sel = Own().Take(6).ToList();
        SelectSlots(sel);
        Key(Godot.Key.A);
        Key(Godot.Key.A);
        Check(_sel.Targeting, "A twice disarmed");
        Rect2 r = _mini.GetGlobalRect();
        Vector2 inMini = r.Position + r.Size * 0.5f;
        int pending = _sim.PendingCommandCount;
        System.Numerics.Vector2 before = _camera.Focus;
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = inMini });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = inMini });
        Check(_sel.Targeting, "a minimap click cancelled A targeting (documented: it doesn't)");
        Check(_sim.PendingCommandCount == pending, "a minimap left click while targeting enqueued commands");
        GD.Print($"A then minimap click: camera moved {System.Numerics.Vector2.Distance(before, _camera.Focus):F1} m, still targeting {_sel.Targeting}");
        Check(Same(_sel.Selection.Items, sel), "the minimap click while targeting changed the selection");
        // BUG-0068: a minimap right-click while targeting cancels it and orders nothing, as on the 3D view.
        int moves = _sel.IssuedCount(CommandKind.Move);
        RightClick(inMini);
        Check(_sel.IssuedCount(CommandKind.Move) == moves && _sim.PendingCommandCount == pending,
            $"A then minimap right-click ordered {_sel.IssuedCount(CommandKind.Move) - moves} moves");
        Check(!_sel.Targeting, "a minimap right-click did not cancel A targeting");
        // Not targeting: the same minimap right-click is a Move again.
        RightClick(inMini);
        Check(_sel.IssuedCount(CommandKind.Move) - moves == sel.Count, $"a plain minimap right-click ordered {_sel.IssuedCount(CommandKind.Move) - moves} moves, expected {sel.Count}");
        moves = _sel.IssuedCount(CommandKind.Move);
        // A 3D right-click while targeting: cancels, orders nothing (unchanged).
        Key(Godot.Key.A);
        RightClick(FindEmpty());
        Check(!_sel.Targeting && _sel.IssuedCount(CommandKind.Move) == moves, "a 3D right-click while targeting ordered a move or kept targeting");
        // Esc through the real routing
        Key(Godot.Key.A);
        Check(_sel.Targeting, "A did not re-arm");
        Key(Godot.Key.Escape);
        Check(!_sel.Targeting, "Esc via the viewport did not cancel");
        // Back home, A + click on an own unit: AttackMove, the click never selects.
        System.Numerics.Vector2 home = Mean(sel);
        _camera.SetFocus(home.X, home.Y);
        await Frame();
        int attacks = _sel.IssuedCount(CommandKind.AttackMove);
        int other = Own().First(s => !sel.Contains(s));
        Key(Godot.Key.A);
        LeftClick(At(other));
        Check(Same(_sel.Selection.Items, sel), "a left click on an own unit while targeting changed the selection");
        Check(_sel.IssuedCount(CommandKind.AttackMove) - attacks == sel.Count && !_sel.Targeting, "A + click on an own unit did not attack-move");
        Tick(2);
        await Frame();
    }

    // Tab through the real routing still cycles after the minimap was clicked (no GUI focus steals it); Tab with one type stays put.
    private async Task TabAfterMinimapClick()
    {
        List<int> own = Own();
        List<int> three = own.GroupBy(s => U.TypeId[s]).Take(3).Select(g => g.First()).ToList();
        SelectSlots(three);
        Rect2 r = _mini.GetGlobalRect();
        Vector2 inMini = r.Position + new Vector2(3, 3);
        System.Numerics.Vector2 focus = _camera.Focus;
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = inMini });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = inMini });
        _camera.SetFocus(focus.X, focus.Y);
        await Frame();
        int before = _sel.Subgroups.Index;
        Key(Godot.Key.Tab);
        Check(_sel.Subgroups.Count == three.Count && _sel.Subgroups.Index == (before + 1) % three.Count, $"Tab after a minimap click: index {before} -> {_sel.Subgroups.Index} of {_sel.Subgroups.Count}");
        Key(Godot.Key.Tab, shift: true);
        Check(_sel.Subgroups.Index == (before + 2) % three.Count, "Shift+Tab did not advance (no reverse cycling is specified)");

        int t = U.TypeId[own[0]];
        SelectSlots(own.Where(s => U.TypeId[s] == t).Take(3).ToList());
        for (int k = 0; k < 3; k++) Key(Godot.Key.Tab);
        Check(_sel.Subgroups.Count == 1 && _sel.Subgroups.Index == 0 && _sel.Subgroups.ActiveType == t, "Tab with one type moved off it");
        await Frame();
    }

    // BUG-0067: A armed, then every selected unit dies: the next click selects normally and orders nothing.
    // Once with a frame in between (the _Process prune) and once with the click arriving first.
    private async Task TargetingWithEmptiedSelection()
    {
        for (int pass = 0; pass < 2; pass++)
        {
            List<int> own = Own();
            List<int> doomed = own.Take(2).ToList();
            int survivor = own[2];
            SelectSlots(doomed);
            Key(Godot.Key.A);
            Check(_sel.Targeting, "A did not arm");
            foreach (int s in doomed) U.Free(new EntityHandle(s, U.Generation[s]));
            if (pass == 0)
            {
                await Frame();
                Check(!_sel.Targeting, "targeting outlived an emptied selection");
            }
            int attacks = _sel.IssuedCount(CommandKind.AttackMove), pending = _sim.PendingCommandCount;
            LeftClick(At(survivor));
            Check(!_sel.Targeting, $"pass {pass}: still targeting after the click");
            Check(_sel.IssuedCount(CommandKind.AttackMove) == attacks && _sim.PendingCommandCount == pending, $"pass {pass}: the click ordered something");
            Check(Same(_sel.Selection.Items, new[] { survivor }), $"pass {pass}: the click selected {_sel.Selection.Count}, expected the clicked unit");
            await Frame();
        }
    }

    // 1,000 selected against a nearly full queue: S, H, Shift+H, Shift + right-click, A + click each dropped whole, once.
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
        Check(n == 1000, $"{n} own for overflow");
        while (_sim.PendingCommandCount < cap - n + 1) _sim.Enqueue(Command.Noop(1));
        int pending = _sim.PendingCommandCount, dropped = _sel.DroppedOrders;
        Key(Godot.Key.S);
        Key(Godot.Key.H);
        HoldMods(shift: true, ctrl: false);
        Key(Godot.Key.H, shift: true);
        RightClick(_screen / 2, shift: true);
        ReleaseAll();
        Key(Godot.Key.A);
        LeftClick(_screen / 2);
        Check(_sim.PendingCommandCount == pending, $"overflow: pending {pending} -> {_sim.PendingCommandCount} (partial order)");
        Check(_sel.DroppedOrders - dropped == 5, $"overflow: {_sel.DroppedOrders - dropped} drops for 5 orders");
        GD.Print($"overflow: 5 orders x {n} units dropped whole; targeting after the dropped A-click: {_sel.Targeting}");
        // Exactly fits: one slot more frees the whole order (pending commands drain on the second Tick()).
        _sim.Tick();
        _sim.Tick();
        while (_sim.PendingCommandCount < cap - n) _sim.Enqueue(Command.Noop(1));
        Key(Godot.Key.S);
        Check(_sim.PendingCommandCount == cap, $"an order that exactly fills the queue was not accepted ({_sim.PendingCommandCount}/{cap})");
        _sim.Tick();
    }

    // ---- input helpers: events go through the viewport, so GUI routing (minimap Stop filter, focus) applies ----

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private void HoldMods(bool shift, bool ctrl)
    {
        _shift = shift;
        _ctrl = ctrl;
        if (shift) { Input.ActionPress("order_queue"); Input.ActionPress("select_add"); Input.ActionPress("group_add"); }
        if (ctrl) { Input.ActionPress("select_type"); Input.ActionPress("group_assign"); }
    }

    private void ReleaseAll()
    {
        foreach (string m in Modifiers) Input.ActionRelease(m);
        _shift = _ctrl = false;
    }

    private void Key(Key key, bool shift = false, bool ctrl = false, bool release = true)
    {
        bool held = _shift || _ctrl;
        if (!held) HoldMods(shift, ctrl);
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true, ShiftPressed = shift, CtrlPressed = ctrl });
        if (release) Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false, ShiftPressed = shift, CtrlPressed = ctrl });
        if (!held) ReleaseAll();
    }

    private void LeftClick(Vector2 at, bool doubleClick = false, bool shift = false, bool ctrl = false)
    {
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, DoubleClick = doubleClick, ShiftPressed = shift, CtrlPressed = ctrl });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, ShiftPressed = shift, CtrlPressed = ctrl });
    }

    private void RightClick(Vector2 at, bool shift = false)
    {
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at, ShiftPressed = shift });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at, ShiftPressed = shift });
    }

    private void SelectSlots(List<int> slots)
    {
        LeftClick(At(slots[0]));
        HoldMods(shift: true, ctrl: false);
        foreach (int s in slots.Skip(1)) LeftClick(At(s), shift: true);
        ReleaseAll();
        Check(Same(_sel.Selection.Items, slots), $"select {slots.Count}: got {_sel.Selection.Count}");
    }

    // ---- world helpers ----

    private void Tick(int n) { for (int i = 0; i < n; i++) _sim.Tick(); }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool OnScreen(int i)
    {
        if (!_sel.TryScreenPosition(i, out Vector2 p)) return false;
        Rect2 mini = _mini.GetGlobalRect();
        return p.X >= 20 && p.Y >= 20 && p.X <= _screen.X - 20 && p.Y <= _screen.Y - 20 && !mini.Grow(10).HasPoint(p);
    }

    // Own on-screen units (away from the edges and the minimap), spaced so clicks are unambiguous.
    private List<int> Own()
    {
        var list = new List<int>();
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 0 || !OnScreen(i)) continue;
            Vector2 p = At(i);
            if (list.All(o => At(o).DistanceTo(p) > 14f)) list.Add(i);
        }
        return list;
    }

    private Vector2 At(int slot)
    {
        _sel.TryScreenPosition(slot, out Vector2 p);
        return p;
    }

    private System.Numerics.Vector2 Mean(List<int> slots) =>
        slots.Aggregate(System.Numerics.Vector2.Zero, (m, s) => m + U.Position[s]) / slots.Count;

    private Vector2 FindEmpty()
    {
        var units = new List<Vector2>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && _sel.TryScreenPosition(i, out Vector2 p)) units.Add(p);
        Rect2 mini = _mini.GetGlobalRect().Grow(10);
        for (float y = _screen.Y * 0.3f; y < _screen.Y * 0.8f; y += 10)
            for (float x = _screen.X * 0.1f; x < _screen.X * 0.9f; x += 10)
            {
                var p = new Vector2(x, y);
                if (!mini.HasPoint(p) && units.All(u => u.DistanceTo(p) > 30f)) return p;
            }
        throw new InvalidOperationException("no empty ground on screen");
    }

    private bool AnyEnemySelected()
    {
        foreach (EntityHandle h in _sel.Selection.Items) if (U.Owner[h.Index] != 0) return true;
        return false;
    }

    private bool Same(ReadOnlySpan<EntityHandle> items, IEnumerable<int> slots)
    {
        var want = slots.ToHashSet();
        if (items.Length != want.Count) return false;
        foreach (EntityHandle h in items) if (!want.Contains(h.Index) || U.Generation[h.Index] != h.Generation) return false;
        return true;
    }

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
