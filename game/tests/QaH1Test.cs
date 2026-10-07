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

/// <summary>QA (M2-H1): A-targeting state machine fuzz through the viewport's real routing: A, Esc, S, H, minimap left / right, 3D left (ground or own unit) / right, group recall, deaths of the whole selection with and without a frame in between, in seeded random order.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaH1Test.tscn</c>; prints
/// "QA M2-H1 TEST PASS" and exits 0, or prints each failure and exits 1. Every step checks the
/// documented rule (docs/03 "Implementation (M2-3)", A attack-move targeting): which commands it
/// may enqueue, whether targeting is on afterwards, and whether the selection changed. After every
/// frame, targeting implies a live selection.
/// </remarks>
public partial class QaH1Test : Node
{
    private const int Steps = 1500;
    private readonly List<string> _failures = new();
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private Minimap _mini = null!;
    private Simulation _sim = null!;
    private Vector2 _screen;
    private readonly Dictionary<string, int> _actionCounts = new();
    private int _attackOrders, _moveOrders, _armed;

    private UnitStore U => _sim.World.Units;

    public override async void _Ready()
    {
        try
        {
            await Setup();
            await Fuzz(seed: 1);
            await Fuzz(seed: 2);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(40)) GD.Print($"QA M2-H1 TEST FAIL: {f}");
        if (_failures.Count > 40) GD.Print($"QA M2-H1 TEST FAIL: ... {_failures.Count - 40} more");
        GD.Print($"fuzz: {string.Join(", ", _actionCounts.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}; A armed {_armed} times, {_attackOrders} attack-move orders, {_moveOrders} move orders");
        if (_failures.Count == 0) GD.Print("QA M2-H1 TEST PASS");
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
    }

    private async Task Fuzz(int seed)
    {
        var rng = new Random(seed); // test-side randomness only (Godot test scene, not the sim)
        System.Numerics.Vector2 home = Mean(Enumerable.Range(0, U.Capacity).Where(i => U.Alive[i] && U.Owner[i] == 0).ToList());
        string prev = "start";
        for (int step = 0; step < Steps; step++)
        {
            // Keep enough own units on screen: respawn near home, then put the camera back.
            if (Own().Count < 8) await Refill(home);
            _camera.SetFocus(home.X, home.Y);

            int pick = rng.Next(14);
            string name = pick switch
            {
                0 or 1 => "A",
                2 => "Esc",
                3 => "S",
                4 => "H",
                5 => "miniLeft",
                6 => "miniRight",
                7 => "3dRightGround",
                8 => "3dLeftGround",
                9 => "3dLeftOwn",
                10 => "selectSome",
                11 => "recall",
                12 => "killSelection",
                _ => "frame",
            };
            _actionCounts[name] = _actionCounts.GetValueOrDefault(name) + 1;
            await Do(name, rng, $"seed {seed} step {step} ({prev} -> {name})");
            prev = name;
            if (_failures.Count > 60) return;
        }
        await Frame();
        Check(!_sel.Targeting || LiveSelected() > 0, $"seed {seed} end: targeting with an empty selection");
        Key(Godot.Key.Escape);
    }

    private async Task Do(string name, Random rng, string where)
    {
        bool wasTargeting = _sel.Targeting;
        int live = LiveSelected();
        int staleCount = _sel.Selection.Count;
        int[] before = Issued();
        int pending = _sim.PendingCommandCount;
        List<EntityHandle> selBefore = _sel.Selection.Items.ToArray().ToList();
        Rect2 mini = _mini.GetGlobalRect();
        Vector2 inMini = mini.Position + new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()) * mini.Size * 0.9f + mini.Size * 0.05f;

        switch (name)
        {
            case "A":
                Key(Godot.Key.A);
                // Targeting = Selection.Count > 0 (the not-yet-pruned count; the next press or frame prunes).
                Check(_sel.Targeting == (staleCount > 0), $"{where}: A with {staleCount} selected ({live} live) left targeting {_sel.Targeting}");
                ExpectNoCommands(before, pending, where);
                if (_sel.Targeting && !wasTargeting) _armed++;
                break;
            case "Esc":
                Key(Godot.Key.Escape);
                Check(!_sel.Targeting, $"{where}: Esc left targeting on");
                ExpectNoCommands(before, pending, where);
                break;
            case "S":
            case "H":
                Key(name == "S" ? Godot.Key.S : Godot.Key.H);
                Check(!_sel.Targeting, $"{where}: {name} left targeting on");
                CommandKind k = name == "S" ? CommandKind.Stop : CommandKind.HoldPosition;
                ExpectOnly(before, pending, k, live, where);
                break;
            case "miniLeft":
                LeftClick(inMini);
                Check(_sel.Targeting == wasTargeting, $"{where}: a minimap left click changed targeting {wasTargeting} -> {_sel.Targeting}");
                ExpectNoCommands(before, pending, where);
                Check(SameHandles(selBefore), $"{where}: a minimap left click changed the selection");
                break;
            case "miniRight":
                RightClick(inMini);
                Check(!_sel.Targeting, $"{where}: a minimap right-click left targeting on");
                if (wasTargeting) ExpectNoCommands(before, pending, where);
                else { ExpectOnly(before, pending, CommandKind.Move, live, where); _moveOrders += live > 0 ? 1 : 0; }
                Check(SameHandlesLive(selBefore), $"{where}: a minimap right-click changed the selection");
                break;
            case "3dRightGround":
            {
                // M3-V1: ground on a tree or mine with workers selected is a Gather for them and a Move for the rest.
                Vector2 at = FindEmpty(rng);
                int workers = LiveWorkersSelected();
                bool onNode = SelectionController.NodeAt(_sim.World, GroundAt(at)) >= 0;
                RightClick(at);
                Check(!_sel.Targeting, $"{where}: a 3D right-click left targeting on");
                if (wasTargeting) ExpectNoCommands(before, pending, where);
                else if (onNode && workers > 0) { ExpectGatherSplit(before, pending, workers, live - workers, where); _moveOrders++; }
                else { ExpectOnly(before, pending, CommandKind.Move, live, where); _moveOrders += live > 0 ? 1 : 0; }
                break;
            }
            case "3dLeftGround":
                LeftClick(FindEmpty(rng));
                Check(!_sel.Targeting, $"{where}: a 3D left click on ground left targeting on");
                if (wasTargeting && live > 0)
                {
                    ExpectOnly(before, pending, CommandKind.AttackMove, live, where);
                    Check(SameHandlesLive(selBefore), $"{where}: A + ground click changed the selection");
                    _attackOrders++;
                }
                else
                {
                    ExpectNoCommands(before, pending, where);
                    Check(_sel.Selection.Count == 0, $"{where}: a plain click on empty ground left {_sel.Selection.Count} selected");
                }
                break;
            case "3dLeftOwn":
            {
                List<int> own = Own();
                int s = own[rng.Next(own.Count)];
                LeftClick(At(s));
                Check(!_sel.Targeting, $"{where}: a 3D left click on a unit left targeting on");
                if (wasTargeting && live > 0)
                {
                    ExpectOnly(before, pending, CommandKind.AttackMove, live, where);
                    Check(SameHandlesLive(selBefore), $"{where}: A + click on an own unit changed the selection");
                    _attackOrders++;
                }
                else
                {
                    ExpectNoCommands(before, pending, where);
                    Check(_sel.Selection.Count == 1 && _sel.Selection.Items[0].Index == s, $"{where}: a plain click on own unit {s} selected {_sel.Selection.Count}");
                }
                break;
            }
            case "selectSome":
            {
                // Through the real path: Esc first if targeting (a click would be the order point), then click + Shift-clicks.
                if (_sel.Targeting) Key(Godot.Key.Escape);
                List<int> own = Own();
                int n = 1 + rng.Next(Math.Min(6, own.Count));
                List<int> chosen = own.OrderBy(_ => rng.Next()).Take(n).ToList();
                LeftClick(At(chosen[0]));
                Input.ActionPress("select_add");
                foreach (int s in chosen.Skip(1)) LeftClick(At(s), shift: true);
                Input.ActionRelease("select_add");
                Check(_sel.Selection.Count == n, $"{where}: selected {_sel.Selection.Count}, expected {n}");
                if (rng.Next(3) == 0)
                {
                    Input.ActionPress("group_assign");
                    Push(new InputEventKey { Keycode = Godot.Key.Key1, PhysicalKeycode = Godot.Key.Key1, Pressed = true, CtrlPressed = true });
                    Push(new InputEventKey { Keycode = Godot.Key.Key1, PhysicalKeycode = Godot.Key.Key1, Pressed = false, CtrlPressed = true });
                    Input.ActionRelease("group_assign");
                }
                ExpectNoCommands(before, pending, where);
                break;
            }
            case "recall":
            {
                int groupLive = _sel.Groups.Items(0).ToArray().Count(h => U.Alive[h.Index] && U.Generation[h.Index] == h.Generation);
                Key(Godot.Key.Key1);
                ExpectNoCommands(before, pending, where);
                if (groupLive > 0)
                {
                    Check(_sel.Selection.Count == groupLive, $"{where}: recall selected {_sel.Selection.Count}, group has {groupLive} live");
                    Check(_sel.Targeting == wasTargeting, $"{where}: a recall of a live group changed targeting {wasTargeting} -> {_sel.Targeting}");
                }
                else Check(_sel.Targeting == wasTargeting, $"{where}: an empty-group recall changed targeting {wasTargeting} -> {_sel.Targeting}");
                break;
            }
            case "killSelection":
            {
                foreach (EntityHandle h in selBefore)
                    if (U.Alive[h.Index] && U.Generation[h.Index] == h.Generation) U.Free(h);
                if (rng.Next(2) == 0)
                {
                    await Frame();
                    Check(!_sel.Targeting, $"{where}: targeting survived a frame after the whole selection died");
                }
                break;
            }
            default:
                await Frame();
                Check(!_sel.Targeting || LiveSelected() > 0, $"{where}: targeting after a frame with {LiveSelected()} live selected");
                break;
        }
        // Drain the queue so nothing overflows; ticking never touches targeting.
        bool t = _sel.Targeting;
        Tick(1);
        Check(_sel.Targeting == t, $"{where}: a sim tick changed targeting");
    }

    private async Task Refill(System.Numerics.Vector2 home)
    {
        GameData data = _sim.World.Data;
        float maxR = data.Factions[0].Units.Max(t => data.Units[t].Radius);
        System.Numerics.Vector2[] spots = StartLayout.Block(_sim.World.NavGrid, 30, true, maxR);
        foreach (System.Numerics.Vector2 p in spots)
        {
            if (U.Count >= U.Capacity - 1) break;
            _sim.Enqueue(Command.SpawnUnit(0, data.Factions[0].Units[0], p));
        }
        Tick(2);
        _camera.SetFocus(home.X, home.Y);
        await Frame();
    }

    // ---- expectations ----

    private int[] Issued()
    {
        var a = new int[8];
        for (int k = 0; k < a.Length; k++) a[k] = _sel.IssuedCount((CommandKind)k);
        return a;
    }

    private void ExpectNoCommands(int[] before, int pending, string where)
    {
        int[] now = Issued();
        for (int k = 0; k < now.Length; k++)
            Check(now[k] == before[k], $"{where}: unexpected {(CommandKind)k} x{now[k] - before[k]}");
        Check(_sim.PendingCommandCount == pending, $"{where}: {_sim.PendingCommandCount - pending} commands enqueued, expected none");
    }

    private void ExpectOnly(int[] before, int pending, CommandKind kind, int count, string where)
    {
        int[] now = Issued();
        for (int k = 0; k < now.Length; k++)
        {
            int want = k == (int)kind ? count : 0;
            Check(now[k] - before[k] == want, $"{where}: {(CommandKind)k} x{now[k] - before[k]}, expected {want}");
        }
        Check(_sim.PendingCommandCount - pending == count, $"{where}: {_sim.PendingCommandCount - pending} enqueued, expected {count}");
    }

    private void ExpectGatherSplit(int[] before, int pending, int gathers, int moves, string where)
    {
        int[] now = Issued();
        for (int k = 0; k < now.Length; k++)
        {
            int want = k == (int)CommandKind.Gather ? gathers : k == (int)CommandKind.Move ? moves : 0;
            Check(now[k] - before[k] == want, $"{where}: {(CommandKind)k} x{now[k] - before[k]}, expected {want}");
        }
        Check(_sim.PendingCommandCount - pending == gathers + moves, $"{where}: {_sim.PendingCommandCount - pending} enqueued, expected {gathers + moves}");
    }

    private int LiveWorkersSelected()
    {
        int n = 0;
        foreach (EntityHandle h in _sel.Selection.Items)
            if (U.Alive[h.Index] && U.Generation[h.Index] == h.Generation && _sim.World.Data.Units[U.TypeId[h.Index]].Slot == UnitSlot.Worker) n++;
        return n;
    }

    // The ground point (sim x, y) under a screen pixel, as the controller picks it; NaN off the map.
    private System.Numerics.Vector2 GroundAt(Vector2 px)
    {
        Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
        return GroundPicker.TryPick(_sim.World.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out System.Numerics.Vector3 hit)
            ? new(hit.X, hit.Z) : new(float.NaN, float.NaN);
    }

    private int LiveSelected()
    {
        int n = 0;
        foreach (EntityHandle h in _sel.Selection.Items) if (U.Alive[h.Index] && U.Generation[h.Index] == h.Generation) n++;
        return n;
    }

    private bool SameHandles(List<EntityHandle> before)
    {
        ReadOnlySpan<EntityHandle> now = _sel.Selection.Items;
        if (now.Length != before.Count) return false;
        foreach (EntityHandle h in now) if (!before.Contains(h)) return false;
        return true;
    }

    // Same set once dead members are dropped (an order prunes first).
    private bool SameHandlesLive(List<EntityHandle> before)
    {
        var live = before.Where(h => U.Alive[h.Index] && U.Generation[h.Index] == h.Generation).ToList();
        ReadOnlySpan<EntityHandle> now = _sel.Selection.Items;
        int liveNow = 0;
        foreach (EntityHandle h in now)
        {
            if (!(U.Alive[h.Index] && U.Generation[h.Index] == h.Generation)) continue;
            liveNow++;
            if (!live.Contains(h)) return false;
        }
        return liveNow == live.Count;
    }

    // ---- input helpers: events go through the viewport, so GUI routing (minimap Stop filter) applies ----

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private void Key(Key key)
    {
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void LeftClick(Vector2 at, bool shift = false)
    {
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, ShiftPressed = shift });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, ShiftPressed = shift });
    }

    private void RightClick(Vector2 at)
    {
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
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

    private Vector2 FindEmpty(Random rng)
    {
        var units = new List<Vector2>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && _sel.TryScreenPosition(i, out Vector2 p)) units.Add(p);
        Rect2 mini = _mini.GetGlobalRect().Grow(10);
        var spots = new List<Vector2>();
        for (float y = _screen.Y * 0.3f; y < _screen.Y * 0.8f; y += 10)
            for (float x = _screen.X * 0.1f; x < _screen.X * 0.9f; x += 10)
            {
                var p = new Vector2(x, y);
                if (!mini.HasPoint(p) && units.All(u => u.DistanceTo(p) > 30f)) spots.Add(p);
            }
        if (spots.Count == 0) throw new InvalidOperationException("no empty ground on screen");
        return spots[rng.Next(spots.Count)];
    }

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
