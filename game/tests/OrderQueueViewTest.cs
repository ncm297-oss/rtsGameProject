using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V22 on the real Match scene and the shipped data: the selected units' queued orders drawn (<see cref="OrderPathView"/>).
/// Rows: (3) per order kind (Move, AttackMove, Attack, Gather, Build, Repair, UseAbility at a point and with no target,
/// Stop, HoldPosition) a unit walking a Move is given the order with Shift through the mouse's entry points (right click,
/// A + click, the ghost, the ability click, S / H, the context order), the sim's queue holds it, a marker of that kind is
/// drawn at the right point (0.1 m), and when its turn comes the unit starts it and the marker goes; (4) an enemy selected
/// draws nothing, a queued Attack's marker follows the fog frame by frame as its target walks out of sight and back, and
/// goes when the target dies; (5) the first Shift-placed building: a gatherer and an idle worker build at once, a walker
/// builds after it arrives (its Move kept), a builder builds houses 1, 2 and 3 in turn, and BUG-0122's guard holds; (6) a
/// ninth queued order is dropped by the sim and 8 markers are drawn; (7) 0 bytes a frame once warm, and the drawing's cost
/// with 200 selected units, 8 shared and 8 distinct points each (reported).
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/OrderQueueViewTest.tscn</c>; prints "ORDER QUEUE VIEW TEST
/// PASS" and exits 0, or each failure and exits 1. The test ticks the sim itself (runner disabled). Windowed with
/// <c>-- --shots &lt;dir&gt;</c> it saves the pictures (12 soldiers' queue, a worker's three queued buildings, a mixed queue at the
/// default zoom and zoomed out); with <c>-- --cost</c> it also reports frame time and draw calls (vsync off).
/// </remarks>
public partial class OrderQueueViewTest : Node
{
    private const string Tag = "ORDER QUEUE VIEW TEST";
    private const float Tolerance = 0.1f;
    private static readonly FieldInfo CommandsField = typeof(Simulation).GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo DamageMethod = typeof(BuildingStore).GetMethod("Damage", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo LedgerProperty = typeof(World).GetProperty("Ledger", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private readonly List<string> _gaps = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private CommandCard _card = null!;
    private BuildGhost _ghost = null!;
    private OrderPathView _view = null!;
    private FogOfWar _fog = null!;
    private RtsCamera _camera = null!;
    private string? _shots;
    private bool _cost;

    private int _hi, _laborer, _mage, _shaman, _raider, _house, _fire, _frenzy;
    private System.Numerics.Vector2 _hall, _away, _perp, _t;
    private EntityHandle _enemy, _spotter;
    private readonly List<int> _usedAnchors = new();

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private BuildingStore B => _sim.World.Buildings;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            int at = Array.IndexOf(args, "--shots");
            if (at >= 0 && at + 1 < args.Length) _shots = args[at + 1];
            _cost = Array.IndexOf(args, "--cost") >= 0;
            GetTree().Root.Size = new Vector2I(1152, 648); // headless windows are 64 x 64
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            await Run();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        Input.ActionRelease("order_queue");
        foreach (string g in _gaps) GD.Print($"known gap: {g}");
        foreach (string f in _failures.Take(60)) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        await StartMatch();
        Stage(24f);
        await KindRows();
        await NinthOrder();
        await BuildingRule();
        await FogRows();
        await CostRows();
        await EndMatch();
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        // The pictures in a fresh match, on open ground away from the base and the rows' units.
        _usedAnchors.Clear();
        await StartMatch();
        Stage(50f);
        await Pictures();
        await EndMatch();
    }

    private async Task StartMatch()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--mute" }));
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.ProcessMode = ProcessModeEnum.Disabled; // the test ticks the sim itself
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _ghost = _match.GetNode<BuildGhost>("World3D/BuildGhost");
        _view = _match.GetNode<OrderPathView>("World3D/OrderPaths");
        _fog = _match.GetNode<FogOfWar>("World3D/FogOfWar");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        Check(_view.Fog == _fog, "the match did not give the order paths the fog");
        Tick(2);
        await Frames();
    }

    private async Task EndMatch()
    {
        Input.ActionRelease("order_queue");
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    // The staging: ids from data, the frame (the hall, a direction toward the map's middle and across it), money, an
    // enemy Raider holding 16 m across from the test ground with a holding own Laborer 5 m from it as its spotter.
    private void Stage(float ground)
    {
        _hi = _data.FindUnit("malazan_heavy_infantry");
        _laborer = StartBase.UnitOfSlot(_data, W.FactionOf(0), UnitSlot.Worker);
        _mage = _data.FindUnit("malazan_cadre_mage");
        _shaman = _data.FindUnit("teblor_shaman");
        _raider = _data.FindUnit("whirlwind_raider");
        _house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        _fire = _data.FindAbility("telas_fire");
        _frenzy = _data.FindAbility("blood_oil_frenzy");
        _hall = HallCenter();
        var middle = new System.Numerics.Vector2(G.Width, G.Height) * MapConstants.CellSize / 2f;
        _away = System.Numerics.Vector2.Normalize(middle - _hall);
        _perp = new System.Numerics.Vector2(-_away.Y, _away.X);
        _t = Passable(_hall + _away * ground);
        SetMoney(50000, 50000);
        // On the test ground's level (a unit below a plateau does not see onto it: docs/02 "High ground").
        System.Numerics.Vector2 e = default;
        float level = Height(_t);
        for (int k = 0; k < 60; k++)
        {
            e = Passable(_t - _perp * (14f + k % 5 * 2f) + _away * (k / 5 * 3f - 6f));
            if (MathF.Abs(Height(e) - level) < 0.3f && MathF.Abs(Height(Passable(e + _perp * 5f)) - level) < 0.3f) break;
        }
        _enemy = Spawn(1, _raider, e);
        _spotter = Spawn(0, _laborer, Passable(e + _perp * 5f));
        _sim.Enqueue(Command.HoldPosition(1, _enemy));
        _sim.Enqueue(Command.HoldPosition(0, _spotter));
        Apply();
        for (int k = 0; k < 20 && !W.Fog.CanSeeUnit(0, _enemy.Index); k++) Tick(1);
        Check(W.Fog.CanSeeUnit(0, _enemy.Index), "staging: player 0 does not see the enemy Raider");
        GD.Print($"stage: hall at {_hall}, test ground {_t}, enemy {_enemy.Index}, spotter {_spotter.Index}");
    }

    // ---- (3) one row per order kind ----

    private async Task KindRows()
    {
        // Move: a Shift + right click on open ground.
        await KindRow("Move", _hi, CommandKind.Move, async unit =>
        {
            System.Numerics.Vector2 p = Passable(U.Position[unit.Index] + _away * 10f + _perp * 8f);
            Vector2 px = await OnScreen(p);
            if (!_sel.ContextTarget(px, out System.Numerics.Vector2 hit, out int b) || b >= 0) return (p, false, "the pixel did not pick open ground");
            RightClick(px);
            return (hit, false, null);
        }, unit => U.State[unit.Index] == UnitState.Moving && U.Mode[unit.Index] == CombatMode.None);

        // AttackMove: A + a Shift click on open ground (kept 20 m from the enemy so the leg does not turn into a fight).
        await KindRow("AttackMove", _hi, CommandKind.AttackMove, async unit =>
        {
            System.Numerics.Vector2 p = Passable(U.Position[unit.Index] + _away * 10f + _perp * 10f);
            Vector2 px = await OnScreen(p);
            if (!_sel.ContextTarget(px, out System.Numerics.Vector2 hit, out int b) || b >= 0) return (p, false, "the pixel did not pick open ground");
            Key(Godot.Key.A);
            LeftClick(px);
            return (hit, false, null);
        }, unit => U.Mode[unit.Index] == CombatMode.AttackMove);

        // Attack: a Shift + right click's Attack on the seen enemy (SelectionController.AttackOrder, what the click calls).
        await KindRow("Attack", _hi, CommandKind.Attack, unit =>
        {
            _sel.AttackOrder(_enemy, false, Input.IsActionPressed("order_queue"));
            return Task.FromResult((U.Position[_enemy.Index], false, (string?)null));
        }, unit => U.Target[unit.Index] == _enemy && U.Mode[unit.Index] == CombatMode.Ordered, followTarget: true);

        // Gather: a Shift + right click's context order on the nearest gold mine (its footprint centre, as a click on the prop gives).
        await KindRow("Gather", _laborer, CommandKind.Gather, unit =>
        {
            System.Numerics.Vector2 mine = MineCenter();
            _sel.ContextOrder(new Vector2(mine.X, mine.Y), Input.IsActionPressed("order_queue"));
            return Task.FromResult((mine, false, (string?)null));
        }, unit => EconomySystem.OnLoop(U, unit.Index));

        // Build: the build menu's house ghost, a Shift click on a green spot (the worker walks a Move: queued behind it).
        int buildAnchor = -1;
        await KindRow("Build", _laborer, CommandKind.Build, async unit =>
        {
            buildAnchor = GreenAnchor(_house, U.Position[unit.Index] + _away * 6f);
            if (buildAnchor < 0) return (default, false, "no green house spot");
            if (!await OpenGhost(_house)) return (default, false, "the house ghost did not open");
            Vector2 px = await Aim(_house, buildAnchor);
            LeftClick(px);
            _card.CloseMenu();
            return (StartBase.FootprintCenter(G, _data.Buildings[_house], buildAnchor), false, null);
        }, unit => U.BuildTarget[unit.Index].Generation != 0 && B.SlotAt(buildAnchor % G.Width, buildAnchor / G.Width) >= 0);

        // Repair: a Shift + right click's context order on the own damaged hall.
        int hall = HallSlot();
        DamageMethod.Invoke(B, new object[] { B.HandleOf(hall), _data.Buildings[B.TypeId[hall]].Hp / 4 });
        await KindRow("Repair", _laborer, CommandKind.Repair, unit =>
        {
            _sel.ContextOrder(new Vector2(_hall.X, _hall.Y), Input.IsActionPressed("order_queue"));
            return Task.FromResult((_hall, false, (string?)null));
        }, unit => U.BuildTarget[unit.Index] == B.HandleOf(hall));

        // UseAbility at a point: the Cadre Mage's Telas Fire armed, then a Shift click (it stays armed: disarmed after).
        await KindRow("UseAbility", _mage, CommandKind.UseAbility, async unit =>
        {
            System.Numerics.Vector2 p = Passable(U.Position[unit.Index] + _away * 10f + _perp * 6f);
            Vector2 px = await OnScreen(p);
            if (!_sel.BeginAbility(_fire)) return (p, false, "Telas Fire did not arm");
            if (!_sel.AbilityPoint(px, out System.Numerics.Vector2 hit)) return (p, false, "no ability point");
            LeftClick(px);
            _sel.CancelTargeting();
            return (hit, false, null);
        }, unit => U.CastAbility[unit.Index] >= 0);

        // UseAbility with no target: the Teblor Shaman's Frenzy key with Shift: no place, drawn at the previous point (the walk's goal).
        await KindRow("UseAbility (no target)", _shaman, CommandKind.UseAbility, unit =>
        {
            bool sent = _sel.BeginAbility(_frenzy, Input.IsActionPressed("order_queue"));
            return Task.FromResult((U.Goal[unit.Index], true, sent ? null : (string?)"Frenzy was not sent"));
        }, unit => U.CastAbility[unit.Index] >= 0);

        // Stop and HoldPosition: S and H with Shift: no place, drawn at the walk's goal.
        await KindRow("Stop", _hi, CommandKind.Stop, unit =>
        {
            Key(Godot.Key.S);
            return Task.FromResult((U.Goal[unit.Index], true, (string?)null));
        }, unit => U.State[unit.Index] == UnitState.Idle && U.GoalCell[unit.Index] == -1 && !U.Hold[unit.Index]);
        await KindRow("HoldPosition", _hi, CommandKind.HoldPosition, unit =>
        {
            Key(Godot.Key.H);
            return Task.FromResult((U.Goal[unit.Index], true, (string?)null));
        }, unit => U.Hold[unit.Index]);
        await BehindAGatherLoop();
    }

    // Known gap, a Request for the sim track (not this task's to fix): an order queued behind a gather loop never starts,
    // because the sim starts a queued order only when the unit is Idle (OrderSystem.Run) and a loop never is. The row
    // proves the drawing (node, then the queued point) and records the gap; it fails only if the drawing is wrong.
    private async Task BehindAGatherLoop()
    {
        EntityHandle worker = Spawn(0, _laborer, Passable(MineCenter() + _away * 4f));
        _sim.Enqueue(Command.Gather(0, worker, MineCenter()));
        Apply();
        _sel.SelectOnly(worker);
        await Frames();
        System.Numerics.Vector2 p = OpenGround(MineCenter() + _away * 12f);
        Input.ActionPress("order_queue");
        Vector2 px = await OnScreen(p);
        _sel.ContextTarget(px, out p, out _);
        RightClick(px);
        Input.ActionRelease("order_queue");
        Apply();
        _view.Sync(W, 1f);
        Check(EconomySystem.OnLoop(U, worker.Index) && U.QueueCount[worker.Index] == 1 && FindMarker(CommandKind.Move, p, false) >= 0,
            $"behind a gather loop: on loop {EconomySystem.OnLoop(U, worker.Index)}, queue {U.QueueCount[worker.Index]}, Move marker at {p}: {Markers()}");
        Tick(1200);
        bool waits = U.QueueCount[worker.Index] == 1 && EconomySystem.OnLoop(U, worker.Index);
        if (waits)
            _gaps.Add("Request for the sim track: a Move queued behind a worker's gather loop has not started after 1,200 ticks (60 s) and never will while the loop runs (OrderSystem.Run starts a queued order only for an Idle unit)");
        GD.Print($"behind a gather loop: queued Move drawn at {p}; after 1,200 ticks still queued: {waits}");
        _sel.Selection.Clear();
        _sim.Enqueue(Command.Stop(0, worker));
        Apply();
    }

    /// <summary>
    /// One kind's row: a fresh unit of <paramref name="type"/> on the test ground, selected alone, walks a Move 10 m; with
    /// Shift held <paramref name="issue"/> gives the order (returns the point the marker must be at, whether it is a
    /// no-place marker, and an error); the queue holds exactly that kind; the marker is drawn there; the unit's turn comes
    /// (its queue pops), <paramref name="started"/> holds, and the marker is gone. The unit is stopped and sent away after.
    /// </summary>
    private async Task KindRow(string name, int type, CommandKind kind, Func<EntityHandle, Task<(System.Numerics.Vector2 At, bool NoPlace, string? Error)>> issue,
        Func<EntityHandle, bool> started, bool followTarget = false)
    {
        EntityHandle unit = Spawn(0, type, _t);
        Apply();
        _sel.SelectOnly(unit);
        await Frames();
        System.Numerics.Vector2 goal = Passable(U.Position[unit.Index] + _away * 10f);
        _sel.Order(CommandKind.Move, new Vector2(goal.X, goal.Y), queued: false);
        Apply();
        if (!Check(U.State[unit.Index] == UnitState.Moving, $"{name}: the unit is not walking its Move ({U.State[unit.Index]})")) return;
        int start = _sim.PendingCommandCount;
        Input.ActionPress("order_queue");
        (System.Numerics.Vector2 want, bool noPlace, string? error) = await issue(unit);
        Input.ActionRelease("order_queue");
        if (!Check(error == null, $"{name}: {error}")) return;
        List<Command> sent = Pending(start);
        Check(sent.Count == 1 && sent[0].Kind == kind && sent[0].IsQueued && sent[0].Unit == unit, $"{name}: sent {Describe(sent)}, want one queued {kind}");
        Apply();
        int q = unit.Index * OrderConstants.QueueCapacity;
        if (!Check(U.QueueCount[unit.Index] == 1 && U.QueueKind[q] == kind, $"{name}: the queue holds {U.QueueCount[unit.Index]} [{U.QueueKind[q]}], want one {kind}")) return;
        if (followTarget) want = U.Position[_enemy.Index];
        if (noPlace) want = U.Goal[unit.Index];
        _view.Sync(W, 1f);
        int marker = FindMarker(kind, want, noPlace);
        Check(marker >= 0, $"{name}: no {kind}{(noPlace ? " (no place)" : "")} marker within {Tolerance} m of {want}; markers {Markers()}");
        Check(_view.SegmentCount >= (noPlace ? 1 : 2), $"{name}: {_view.SegmentCount} line segments, want the unit's walk{(noPlace ? "" : " and its leg to the queued point")}");
        int ticks = 0;
        bool lost = false;
        while (U.IsAlive(unit) && U.QueueCount[unit.Index] > 0 && ticks < 1500)
        {
            Tick(1);
            ticks++;
            _view.Sync(W, 1f);
            System.Numerics.Vector2 now = !noPlace ? (followTarget ? U.Position[_enemy.Index] : want)
                : U.State[unit.Index] == UnitState.Moving ? U.Goal[unit.Index] : U.Position[unit.Index]; // arrived: the unit is the previous point
            if (U.QueueCount[unit.Index] > 0 && FindMarker(kind, now, noPlace) < 0 && !lost)
            {
                lost = true;
                Check(false, $"{name}: the marker went at tick {ticks} while the order was still queued; markers {Markers()}");
            }
        }
        bool popped = U.IsAlive(unit) && U.QueueCount[unit.Index] == 0;
        if (!Check(popped, $"{name}: the queued {kind} never came to its turn in {ticks} ticks (state {U.State[unit.Index]})")) return;
        bool ok = started(unit);
        if (!ok) _gaps.Add($"{name}: popped but did not start (state {U.State[unit.Index]}, mode {U.Mode[unit.Index]}): a Request for the sim track");
        Check(ok, $"{name}: popped after {ticks} ticks but did not start it (state {U.State[unit.Index]}, mode {U.Mode[unit.Index]}, target {U.Target[unit.Index]}, build {U.BuildTarget[unit.Index]}, cast {U.CastAbility[unit.Index]})");
        int left = 0;
        for (int k = 0; k < _view.MarkerCount; k++) if (_view.MarkerKind(k) == kind) left++;
        Check(left == 0, $"{name}: {left} {kind} markers left after it started; markers {Markers()}");
        GD.Print($"{name}: queued behind a Move, marker at {want} ({(noPlace ? "no place" : "its point")}), started after {ticks} ticks, marker gone");
        // Out of the way of the next rows: stop it and send it off the test ground.
        _sel.Selection.Clear();
        _sim.Enqueue(Command.Stop(0, unit));
        Apply();
        _sim.Enqueue(Command.Move(0, unit, Passable(_t - _away * 8f + _perp * (6f + 2f * (_parked++ % 8)))));
        Apply();
    }

    private int _parked;

    // ---- (6) the ninth order ----

    private async Task NinthOrder()
    {
        EntityHandle unit = Spawn(0, _hi, _t);
        Apply();
        _sel.SelectOnly(unit);
        await Frames();
        System.Numerics.Vector2 goal = Passable(_t + _away * 14f);
        _sel.Order(CommandKind.Move, new Vector2(goal.X, goal.Y), queued: false);
        Apply();
        var points = new System.Numerics.Vector2[9];
        Input.ActionPress("order_queue");
        for (int k = 0; k < 9; k++)
        {
            points[k] = OpenGround(_t + _away * (4f + 2f * k) + _perp * 6f);
            Vector2 px = await OnScreen(points[k]);
            _sel.ContextTarget(px, out points[k], out _);
            for (int j = 0; j < k; j++) Check(System.Numerics.Vector2.Distance(points[j], points[k]) > 0.5f, $"ninth order: click points {j} and {k} coincide at {points[k]}");
            RightClick(px);
        }
        Input.ActionRelease("order_queue");
        Apply();
        int count = U.QueueCount[unit.Index];
        _view.Sync(W, 1f);
        int moves = 0;
        for (int k = 0; k < _view.MarkerCount; k++) if (_view.MarkerKind(k) == CommandKind.Move) moves++;
        bool ninthDrawn = FindMarker(CommandKind.Move, points[8], false) >= 0;
        Check(count == OrderConstants.QueueCapacity, $"ninth order: the queue holds {count}, want {OrderConstants.QueueCapacity}");
        Check(moves == OrderConstants.QueueCapacity && !ninthDrawn, $"ninth order: {moves} Move markers (ninth drawn {ninthDrawn}), want {OrderConstants.QueueCapacity}");
        for (int k = 0; k < 8; k++) Check(FindMarker(CommandKind.Move, points[k], false) >= 0, $"ninth order: no marker at queued point {k} {points[k]}");
        GD.Print($"ninth order: queue {count}, {moves} Move markers, the ninth dropped by the sim (UnitQueueFull) and not drawn");
        _sel.Selection.Clear();
        _sim.Enqueue(Command.Stop(0, unit));
        Apply();
    }

    // ---- (5) the first Shift-placed building ----

    private async Task BuildingRule()
    {
        System.Numerics.Vector2 yard = Passable(_t + _perp * 14f);
        // (a) A worker on a gather loop: replaced at once.
        EntityHandle gatherer = Spawn(0, _laborer, Passable(MineCenter() + _away * 4f));
        Apply();
        System.Numerics.Vector2 mine = MineCenter();
        _sim.Enqueue(Command.Gather(0, gatherer, mine));
        Apply();
        Tick(10);
        if (Check(EconomySystem.OnLoop(U, gatherer.Index), "(a) the worker is not on a gather loop"))
        {
            (List<Command> sent, int anchor) = await ShiftPlace(gatherer, Passable(U.Position[gatherer.Index] - _away * 8f));
            Check(sent.Count == 1 && sent[0].Kind == CommandKind.Build && !sent[0].IsQueued, $"(a) gatherer: sent {Describe(sent)}, want one Build replacing the loop");
            Apply();
            Check(U.BuildTarget[gatherer.Index].Generation != 0 && !EconomySystem.OnLoop(U, gatherer.Index) && SiteAt(anchor) >= 0,
                $"(a) gatherer: build {U.BuildTarget[gatherer.Index]}, on loop {EconomySystem.OnLoop(U, gatherer.Index)}, site {SiteAt(anchor)}");
            GD.Print($"(a) a gatherer, Shift + place: the Build replaced the loop at once (site {SiteAt(anchor)})");
        }

        // (b) An idle worker: at once.
        EntityHandle idle = Spawn(0, _laborer, yard);
        Apply();
        {
            (List<Command> sent, int anchor) = await ShiftPlace(idle, yard + _away * 6f);
            Check(sent.Count == 1 && !sent[0].IsQueued, $"(b) idle: sent {Describe(sent)}, want one unqueued Build");
            Apply();
            Check(U.BuildTarget[idle.Index].Generation != 0 && SiteAt(anchor) >= 0, $"(b) idle: build {U.BuildTarget[idle.Index]}, site {SiteAt(anchor)}");
            GD.Print($"(b) an idle worker, Shift + place: built at once (site {SiteAt(anchor)})");
        }

        // (c) A worker walking a Move: queued behind it, the Move kept, built after it arrives.
        EntityHandle walker = Spawn(0, _laborer, yard - _away * 4f);
        Apply();
        System.Numerics.Vector2 dest = Passable(yard + _perp * 14f);
        _sim.Enqueue(Command.Move(0, walker, dest));
        Apply();
        {
            System.Numerics.Vector2 goalBefore = U.Goal[walker.Index];
            int orderTick = U.OrderTick[walker.Index];
            (List<Command> sent, int anchor) = await ShiftPlace(walker, yard + _away * 12f);
            Check(sent.Count == 1 && sent[0].IsQueued, $"(c) walker: sent {Describe(sent)}, want one queued Build");
            Apply();
            Check(U.State[walker.Index] == UnitState.Moving && U.Goal[walker.Index] == goalBefore && U.OrderTick[walker.Index] == orderTick && U.QueueCount[walker.Index] == 1,
                $"(c) walker: state {U.State[walker.Index]}, goal {U.Goal[walker.Index]} (was {goalBefore}), queue {U.QueueCount[walker.Index]}: the Move was not kept");
            Check(SiteAt(anchor) < 0, "(c) walker: the site was placed before the walk ended");
            int t = RunUntil(() => U.BuildTarget[walker.Index].Generation != 0, 1500);
            Check(U.BuildTarget[walker.Index].Generation != 0 && SiteAt(anchor) >= 0 && System.Numerics.Vector2.Distance(U.Position[walker.Index], dest) < 6f,
                $"(c) walker: after {t} ticks build {U.BuildTarget[walker.Index]}, site {SiteAt(anchor)}, {System.Numerics.Vector2.Distance(U.Position[walker.Index], dest):0.0} m from its Move's end");
            GD.Print($"(c) a walker, Shift + place: queued, the Move kept, building {t} ticks later at its end");
        }

        // (d) + (e) A worker building house 1, then a new ghost: Shift + place houses 2 and 3 queue; the anchor just placed refuses a second click.
        EntityHandle builder = Spawn(0, _laborer, yard - _perp * 6f);
        Apply();
        int a1 = GreenAnchor(_house, U.Position[builder.Index] + _away * 6f);
        if (!Check(a1 >= 0 && await OpenGhost(_house, builder), "(d) no house spot or ghost")) return;
        LeftClick(await Aim(_house, a1));
        Apply();
        _card.CloseMenu();
        Check(U.BuildTarget[builder.Index].Generation != 0, "(d) house 1 not started");
        int a2 = GreenAnchor(_house, U.Position[builder.Index] + _away * 6f);
        if (!Check(a2 >= 0 && await OpenGhost(_house, builder), "(d) no spot for house 2")) return;
        int start = _sim.PendingCommandCount;
        Input.ActionPress("order_queue");
        LeftClick(await Aim(_house, a2));
        int a3 = GreenAnchor(_house, U.Position[builder.Index] + _away * 6f);
        Vector2 px3 = await Aim(_house, a3);
        LeftClick(px3);
        List<Command> sent23 = Pending(start);
        int before = _sim.PendingCommandCount;
        bool again = _card.GhostClick(true, px3);
        Input.ActionRelease("order_queue");
        Check(sent23.Count == 2 && sent23.All(c => c.Kind == CommandKind.Build && c.IsQueued), $"(d) houses 2 and 3: sent {Describe(sent23)}, want two queued Builds");
        Check(!again && _sim.PendingCommandCount == before, $"(e) BUG-0122: a second Shift click on house 3's anchor placed again ({again}, {_sim.PendingCommandCount - before} commands)");
        _card.CloseMenu();
        Apply();
        int s1 = SiteAt(a1);
        int done1 = -1, start2 = -1, done2 = -1, start3 = -1, done3 = -1;
        for (int t = 0; t < 4000 && done3 < 0; t++)
        {
            Tick(1);
            if (done1 < 0 && s1 >= 0 && !B.UnderConstruction[s1]) done1 = t;
            int s2 = SiteAt(a2), s3 = SiteAt(a3);
            if (start2 < 0 && s2 >= 0) start2 = t;
            if (done2 < 0 && s2 >= 0 && !B.UnderConstruction[s2]) done2 = t;
            if (start3 < 0 && s3 >= 0) start3 = t;
            if (done3 < 0 && s3 >= 0 && !B.UnderConstruction[s3]) done3 = t;
        }
        Check(done1 >= 0 && start2 >= done1 && done2 > start2 && start3 >= done2 && done3 > start3,
            $"(d) houses in turn: 1 done {done1}, 2 placed {start2} done {done2}, 3 placed {start3} done {done3}");
        GD.Print($"(d) houses 1-3 in turn: 1 done at tick {done1}, 2 placed {start2} done {done2}, 3 placed {start3} done {done3}; (e) the second click on 3's anchor refused");
        foreach (EntityHandle h in new[] { gatherer, idle, walker, builder }) _sim.Enqueue(Command.Stop(0, h));
        Apply();
    }

    // Selects the worker alone, opens a house ghost and Shift-clicks a green spot near `near`; returns the commands and the anchor.
    private async Task<(List<Command> Sent, int Anchor)> ShiftPlace(EntityHandle worker, System.Numerics.Vector2 near)
    {
        int anchor = GreenAnchor(_house, near);
        if (!Check(anchor >= 0, $"no green house spot near {near}")) return (new List<Command>(), -1);
        if (!Check(await OpenGhost(_house, worker), "the house ghost did not open")) return (new List<Command>(), anchor);
        Vector2 px = await Aim(_house, anchor);
        int start = _sim.PendingCommandCount;
        Input.ActionPress("order_queue");
        LeftClick(px);
        Input.ActionRelease("order_queue");
        List<Command> sent = Pending(start);
        _card.CloseMenu();
        return (sent, anchor);
    }

    // Opens the basic build menu for the selection (or `worker` alone) and arms the ghost of `type`; true when it is up.
    private async Task<bool> OpenGhost(int type, EntityHandle? worker = null)
    {
        if (worker is EntityHandle w) _sel.SelectOnly(w);
        await Frames();
        _card.CloseMenu();
        if (!_card.OpenMenu(false)) return false;
        int cell = -1;
        for (int i = 0; i < CommandCard.Cells && cell < 0; i++) if (_card.ActionAt(i) == CardCommand.Place && _card.TypeAt(i) == type) cell = i;
        if (cell < 0) return false;
        _card.Press(cell);
        return _card.GhostActive && _ghost.TypeId == type;
    }

    // ---- (4) fog and ownership ----

    private async Task FogRows()
    {
        await Frames();
        // An enemy selected, with queued orders of its own: nothing is drawn.
        _sim.Enqueue(Command.Move(1, _enemy, Passable(_t - _perp * 16f + _away * 6f)));
        _sim.Enqueue(Command.Move(1, _enemy, Passable(_t - _perp * 16f + _away * 10f), queued: true));
        _sim.Enqueue(Command.HoldPosition(1, _enemy, queued: true));
        Apply();
        _sel.SelectOnly(_enemy);
        _view.Sync(W, 1f);
        Check(U.QueueCount[_enemy.Index] == 2 && _view.MarkerCount == 0 && _view.SegmentCount == 0,
            $"enemy selected: queue {U.QueueCount[_enemy.Index]}, {_view.MarkerCount} markers, {_view.SegmentCount} segments drawn");
        GD.Print($"enemy selected with {U.QueueCount[_enemy.Index]} queued orders: nothing drawn");
        _sel.Selection.Clear();

        // A target that walks out of the spotter's sight and back: the marker follows the fog, frame by frame.
        System.Numerics.Vector2 f = Passable(_t + _away * 34f);
        EntityHandle target = Spawn(1, _raider, Passable(f + _away * 5f));
        EntityHandle spotter = Spawn(0, _laborer, f);
        EntityHandle attacker = Spawn(0, _hi, _t);
        Apply();
        _sim.Enqueue(Command.HoldPosition(0, spotter));
        _sim.Enqueue(Command.HoldPosition(1, target));
        Apply();
        for (int k = 0; k < 20 && !W.Fog.CanSeeUnit(0, target.Index); k++) Tick(1);
        _sel.SelectOnly(attacker);
        // A long walk before the Attack's turn (out sideways, back, out again) so the row ends before it comes.
        System.Numerics.Vector2 side = Passable(_t + _perp * 40f);
        if (System.Numerics.Vector2.Distance(side, _t) < 30f) side = Passable(_t - _perp * 40f);
        _sel.Order(CommandKind.Move, new Vector2(side.X, side.Y), queued: false);
        _sel.Order(CommandKind.Move, new Vector2(_t.X, _t.Y), queued: true);
        _sel.Order(CommandKind.Move, new Vector2(side.X, side.Y), queued: true);
        Apply();
        bool queued = _sel.AttackOrder(target, false, queued: true);
        Apply();
        int q = attacker.Index * OrderConstants.QueueCapacity;
        if (!Check(queued && U.QueueCount[attacker.Index] == 3 && U.QueuedTarget(q + 2) == target, $"fog: the queued Attack did not take (queue {U.QueueCount[attacker.Index]})")) return;
        _sim.Enqueue(Command.Move(1, target, Passable(f + _away * 34f)));
        Apply();
        int frames = 0, hidden = 0, shown = 0, flips = 0, wrong = 0;
        bool last = true, sawHidden = false;
        for (int t = 0; t < 900; t++)
        {
            Tick(1);
            if (!sawHidden && !W.Fog.CanSeeUnit(0, target.Index) && t > 0 && hidden > 10)
            {
                // Out of sight long enough: walk it back.
                _sim.Enqueue(Command.Move(1, target, Passable(f + _away * 5f)));
                sawHidden = true;
            }
            _view.Sync(W, 1f);
            frames++;
            bool visible = _fog.Refreshed(W)!.ShowsUnit(target.Index);
            bool drawn = HasAttackMarkerOn(target);
            if (drawn != visible && wrong++ < 3) Check(false, $"fog tick {W.TickNumber}: target visible {visible}, marker drawn {drawn}");
            if (visible) shown++; else hidden++;
            if (visible != last) flips++;
            last = visible;
            if (sawHidden && flips >= 2 && visible && shown > 5) break;
        }
        Check(flips >= 2 && hidden > 0 && shown > 0 && U.QueueCount[attacker.Index] >= 1 && U.QueueKind[q + U.QueueCount[attacker.Index] - 1] == CommandKind.Attack,
            $"fog: {frames} frames, {flips} visibility changes, {hidden} hidden / {shown} shown, queue {U.QueueCount[attacker.Index]} (want out and back in)");
        GD.Print($"fog: the queued Attack's marker matched the fog on {frames - wrong} of {frames} frames ({hidden} hidden, {shown} shown, {flips} changes)");

        // The target dies: its marker goes.
        _sim.Enqueue(Command.HoldPosition(1, target));
        Apply();
        U.Hp[target.Index] = 1; // a test seam: one blow kills it
        EntityHandle killer = Spawn(0, _hi, Passable(U.Position[target.Index] - _away * 2f));
        Apply();
        _sim.Enqueue(Command.Attack(0, killer, target, false));
        Apply();
        _view.Sync(W, 1f);
        bool sawDrawn = HasAttackMarkerOn(target);
        Check(sawDrawn, "dead target row: the marker was not drawn on the live, seen target before the kill");
        int ticks = RunUntil(() => !U.IsAlive(target), 600);
        _view.Sync(W, 1f);
        int attackMarkers = 0;
        for (int k = 0; k < _view.MarkerCount; k++) if (_view.MarkerKind(k) == CommandKind.Attack) attackMarkers++;
        Check(!U.IsAlive(target) && attackMarkers == 0, $"dead target: alive {U.IsAlive(target)} after {ticks} ticks, {attackMarkers} Attack markers drawn (before the kill drawn {sawDrawn})");
        GD.Print($"dead target: killed after {ticks} ticks, the Attack marker gone ({_view.MarkerCount} markers left, queue {U.QueueCount[attacker.Index]})");
        _sel.Selection.Clear();
        foreach (EntityHandle h in new[] { attacker, killer, spotter }) if (U.IsAlive(h)) _sim.Enqueue(Command.Stop(0, h));
        Apply();
    }

    private bool HasAttackMarkerOn(EntityHandle target)
    {
        if (!U.IsAlive(target)) return false;
        System.Numerics.Vector2 p = U.Position[target.Index];
        for (int k = 0; k < _view.MarkerCount; k++)
            if (_view.MarkerKind(k) == CommandKind.Attack && System.Numerics.Vector2.Distance(Flat(_view.MarkerPoint(k)), p) < Tolerance) return true;
        return false;
    }

    // ---- (7) cost and allocation ----

    private async Task CostRows()
    {
        // 200 own Heavy Infantry in a block, selected; first nothing queued, then 8 shared points, then 8 distinct points each.
        System.Numerics.Vector2 block = Passable(_t + _away * 16f + _perp * 10f);
        var units = new List<EntityHandle>();
        for (int k = 0; k < 200; k++) _sim.Enqueue(Command.SpawnUnit(0, _hi, Passable(block + new System.Numerics.Vector2(k % 20 - 10, k / 20 - 5) * 1.2f)));
        var aliveBefore = new HashSet<int>(Enumerable.Range(0, U.Capacity).Where(i => U.Alive[i]));
        Apply();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && !aliveBefore.Contains(i)) units.Add(new EntityHandle(i, U.Generation[i]));
        if (!Check(units.Count == 200, $"cost: {units.Count} units spawned")) return;
        _camera.SetZoom(CameraLimits.DefaultZoom);
        _camera.SetFocus(block.X, block.Y);
        _sel.Selection.Clear();
        (double noneUs, double noneFrame, double noneCalls) = await Measure("nothing selected", units, select: false);

        foreach (EntityHandle h in units) _sel.Selection.Add(h);
        // The same 200 selected with no order: the rings and the panel's portraits cost what they cost; the drawing adds nothing.
        (double idleUs, double idleFrame, double idleCalls) = await Measure("200 selected, no orders", units, select: true);
        Check(_view.MarkerCount == 0 && _view.SegmentCount == 0, $"cost: 200 idle units drew {_view.MarkerCount} markers, {_view.SegmentCount} segments");
        System.Numerics.Vector2 far = Passable(block + _away * 60f);
        foreach (EntityHandle h in units) _sim.Enqueue(Command.Move(0, h, far));
        for (int k = 0; k < 8; k++)
        {
            System.Numerics.Vector2 p = Passable(block + _perp * (6f + 3f * k) + _away * 4f);
            foreach (EntityHandle h in units) _sim.Enqueue(Command.Move(0, h, p, queued: true));
        }
        Apply();
        Check(units.All(h => U.QueueCount[h.Index] == 8), "cost: not every unit holds 8 queued orders (shared)");
        (double sharedUs, double sharedFrame, double sharedCalls) = await Measure("200 x 8 shared", units, select: true);
        Check(_view.MarkerCount == 8, $"cost: shared points drew {_view.MarkerCount} markers, want 8");

        foreach (EntityHandle h in units) _sim.Enqueue(Command.Move(0, h, far));
        int n = 0;
        foreach (EntityHandle h in units)
        {
            // Steps that no 2 m cell-centre difference matches, so no two units share a point.
            var off = new System.Numerics.Vector2(n % 20 * 0.37f, n / 20 * 0.41f);
            for (int k = 0; k < 8; k++) _sim.Enqueue(Command.Move(0, h, Passable(block + _perp * (6f + 3f * k) + _away * 4f) + off, queued: true));
            n++;
        }
        Apply();
        Check(units.All(h => U.QueueCount[h.Index] == 8), "cost: not every unit holds 8 queued orders (distinct)");
        (double distinctUs, double distinctFrame, double distinctCalls) = await Measure("200 x 8 distinct", units, select: true);
        Check(_view.MarkerCount == 1600, $"cost: distinct points drew {_view.MarkerCount} markers, want 1,600");
        GD.Print($"cost: Sync {noneUs:0.0} us nothing selected, {idleUs:0.0} us 200 idle selected, {sharedUs:0.0} us shared (+{sharedUs - noneUs:0.0}), {distinctUs:0.0} us distinct (+{distinctUs - noneUs:0.0})");
        if (_cost && DisplayServer.GetName() != "headless")
            GD.Print($"cost (windowed): frame {noneFrame:0.000} ms / {noneCalls:0} draw calls nothing selected; {idleFrame:0.000} ms / {idleCalls:0} the 200 selected with no orders;"
                + $" shared {sharedFrame:0.000} ms (+{sharedFrame - idleFrame:0.000} on the idle selection, +{sharedFrame - noneFrame:0.000} on none) / {sharedCalls:0} (+{sharedCalls - idleCalls:0});"
                + $" distinct {distinctFrame:0.000} ms (+{distinctFrame - idleFrame:0.000}, +{distinctFrame - noneFrame:0.000}) / {distinctCalls:0} (+{distinctCalls - idleCalls:0})");
        _sel.Selection.Clear();
        foreach (EntityHandle h in units) _sim.Enqueue(Command.Stop(0, h));
        Apply();
        _view.Sync(W, 1f);
    }

    // Warms the drawing, collects, then: 300 Syncs' bytes (must be 0) and mean time; windowed with --cost, 300 frames' mean
    // wall time and draw calls.
    private async Task<(double SyncUs, double FrameMs, double DrawCalls)> Measure(string what, List<EntityHandle> units, bool select)
    {
        for (int k = 0; k < 30; k++) _view.Sync(W, k % 5 / 5f);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long bytes = 0;
        var clock = new Stopwatch();
        for (int k = 0; k < 300; k++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            clock.Start();
            _view.Sync(W, k % 5 / 5f);
            clock.Stop();
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Check(bytes == 0, $"cost {what}: 300 warm frames allocated {bytes} bytes");
        double us = clock.Elapsed.TotalMilliseconds * 1000.0 / 300;
        double frameMs = 0, calls = 0;
        if (_cost && DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            for (int k = 0; k < 60; k++) await Frame();
            var wall = Stopwatch.StartNew();
            for (int k = 0; k < 300; k++)
            {
                await Frame();
                calls += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
            }
            frameMs = wall.Elapsed.TotalMilliseconds / 300;
            calls /= 300;
        }
        GD.Print($"cost {what}: {_view.MarkerCount} markers, {_view.SegmentCount} segments, Sync {us:0.0} us, {bytes} bytes over 300 warm frames"
            + (_cost ? $", frame {frameMs:0.000} ms, {calls:0} draw calls" : ""));
        return (us, frameMs, calls);
    }

    // ---- (8) pictures (windowed) ----

    private async Task Pictures()
    {
        // 12 soldiers: a Move, two queued Moves, a queued Attack on the seen enemy (west of the ground, across `perp`).
        System.Numerics.Vector2 ground = OpenGround(_t + _perp * 4f);
        var soldiers = new List<EntityHandle>();
        var aliveBefore = new HashSet<int>(Enumerable.Range(0, U.Capacity).Where(i => U.Alive[i]));
        // Each on an open cell of its own (one spawned on a cliff cell cannot walk and runs through its queue at once).
        var cells = new HashSet<System.Numerics.Vector2>();
        for (int k = 0; cells.Count < 12 && k < 200; k++) cells.Add(OpenGround(ground + new System.Numerics.Vector2(k % 6 - 2.5f, k / 6 - 1f) * 2f));
        foreach (System.Numerics.Vector2 c in cells) _sim.Enqueue(Command.SpawnUnit(0, _hi, c));
        Apply();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && !aliveBefore.Contains(i)) soldiers.Add(new EntityHandle(i, U.Generation[i]));
        _sel.Selection.Clear();
        foreach (EntityHandle h in soldiers) _sel.Selection.Add(h);
        System.Numerics.Vector2 m1 = OpenGround(ground + _away * 8f), m2 = OpenGround(ground + _away * 12f - _perp * 8f), m3 = OpenGround(ground + _away * 4f - _perp * 12f);
        _sel.Order(CommandKind.Move, new Vector2(m1.X, m1.Y), queued: false);
        _sel.Order(CommandKind.Move, new Vector2(m2.X, m2.Y), queued: true);
        _sel.Order(CommandKind.Move, new Vector2(m3.X, m3.Y), queued: true);
        Apply();
        Check(_sel.AttackOrder(_enemy, false, queued: true), "pictures: the queued Attack was refused");
        Apply();
        Tick(6);
        await Shot("queue-12-soldiers", Centre(ground, m1, m2, m3, U.Position[_enemy.Index]), CameraLimits.DefaultZoom);
        _sel.Selection.Clear();

        // A worker walking a Move with three queued buildings (the first Shift placement queues behind the walk).
        System.Numerics.Vector2 yard = OpenGround(_t + _perp * 34f);
        EntityHandle worker = Spawn(0, _laborer, yard);
        System.Numerics.Vector2 wGoal = OpenGround(yard + _away * 6f);
        _sim.Enqueue(Command.Move(0, worker, wGoal));
        Apply();
        var anchors = new List<int>();
        if (await OpenGhost(_house, worker))
        {
            Input.ActionPress("order_queue");
            for (int k = 0; k < 3; k++)
            {
                int a = GreenAnchor(_house, yard + _away * (6f + 5f * k) + _perp * 5f);
                if (a < 0) continue;
                anchors.Add(a);
                LeftClick(await Aim(_house, a));
            }
            Input.ActionRelease("order_queue");
            _card.CloseMenu();
        }
        Apply();
        Check(U.QueueCount[worker.Index] == 3, $"pictures: the worker holds {U.QueueCount[worker.Index]} queued Builds, want 3");
        _sel.SelectOnly(worker);
        await Shot("queue-worker-three-buildings", yard + _away * 9f + _perp * 3f, CameraLimits.DefaultZoom);

        // A mixed queue: a soldier (AttackMove, Attack, Stop), the Mage (Telas Fire, Hold) and a worker (Gather on a tree, Build).
        System.Numerics.Vector2 mix = OpenGround(_t - _away * 2f + _perp * 18f);
        EntityHandle soldier = Spawn(0, _hi, mix);
        EntityHandle mage = Spawn(0, _mage, OpenGround(mix + _perp * 2f));
        EntityHandle laborer = Spawn(0, _laborer, OpenGround(mix + _perp * 4f));
        System.Numerics.Vector2 tree = NearestTree(mix + _away * 6f + _perp * 6f);
        int site = GreenAnchor(_house, mix + _away * 6f + _perp * 8f);
        _sim.Enqueue(Command.Move(0, soldier, OpenGround(mix + _away * 5f)));
        _sim.Enqueue(Command.AttackMove(0, soldier, OpenGround(mix + _away * 9f - _perp * 5f), queued: true));
        _sim.Enqueue(Command.Attack(0, soldier, _enemy, false, queued: true));
        _sim.Enqueue(Command.Stop(0, soldier, queued: true));
        _sim.Enqueue(Command.Move(0, mage, OpenGround(mix + _away * 4f + _perp * 3f)));
        _sim.Enqueue(Command.UseAbility(0, mage, 0, OpenGround(mix + _away * 12f + _perp * 1f), queued: true));
        _sim.Enqueue(Command.HoldPosition(0, mage, queued: true));
        _sim.Enqueue(Command.Move(0, laborer, OpenGround(mix + _away * 3f + _perp * 6f)));
        _sim.Enqueue(Command.Gather(0, laborer, tree, queued: true));
        if (site >= 0) _sim.Enqueue(Command.Build(0, laborer, _house, PlacementGhost.AnchorPoint(G, site), queued: true));
        Apply();
        _sel.Selection.Clear();
        foreach (EntityHandle h in new[] { soldier, mage, laborer }) _sel.Selection.Add(h);
        Tick(4);
        System.Numerics.Vector2 view = Centre(mix, mix + _away * 12f, mix + _perp * 8f);
        await Shot("queue-mixed-default-zoom", view, CameraLimits.DefaultZoom);
        await Shot("queue-mixed-zoomed-out", view, CameraLimits.MaxZoom);
    }

    private static System.Numerics.Vector2 Centre(params System.Numerics.Vector2[] points)
    {
        System.Numerics.Vector2 min = points[0], max = points[0];
        foreach (System.Numerics.Vector2 p in points)
        {
            min = System.Numerics.Vector2.Min(min, p);
            max = System.Numerics.Vector2.Max(max, p);
        }
        return (min + max) / 2f;
    }

    // The centre of the live tree nearest a point (what a right click on its prop resolves to).
    private System.Numerics.Vector2 NearestTree(System.Numerics.Vector2 near)
    {
        ResourceStore r = W.Resources;
        int tree = _data.FindResource("tree");
        ResourceDef def = _data.Resources[tree];
        System.Numerics.Vector2 best = near;
        float bestD = float.MaxValue;
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k] || r.TypeId[k] != tree) continue;
            int a = r.Cell[k];
            var c = new System.Numerics.Vector2(a % G.Width + def.FootprintWidth / 2f, a / G.Width + def.FootprintHeight / 2f) * MapConstants.CellSize;
            float d = System.Numerics.Vector2.Distance(c, near);
            if (d < bestD) (best, bestD) = (c, d);
        }
        return best;
    }

    // Puts the shot's content above the HUD: the camera looks north (-z), so the focus moves a little south of it.
    private async Task Shot(string name, System.Numerics.Vector2 focus, float zoom)
    {
        _camera.SetZoom(zoom);
        _camera.SetFocus(focus.X, focus.Y + zoom * 0.1f);
        for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path}: {_view.MarkerCount} markers, {_view.SegmentCount} segments");
    }

    // ---- helpers ----

    private int FindMarker(CommandKind kind, System.Numerics.Vector2 at, bool noPlace)
    {
        for (int k = 0; k < _view.MarkerCount; k++)
            if (_view.MarkerKind(k) == kind && _view.MarkerNoPlace(k) == noPlace && System.Numerics.Vector2.Distance(Flat(_view.MarkerPoint(k)), at) < Tolerance) return k;
        return -1;
    }

    private string Markers()
    {
        var parts = new List<string>();
        for (int k = 0; k < _view.MarkerCount && k < 12; k++)
            parts.Add($"{_view.MarkerKind(k)}{(_view.MarkerNoPlace(k) ? "*" : "")}@({_view.MarkerPoint(k).X:0.0},{_view.MarkerPoint(k).Z:0.0})");
        return $"[{string.Join(" ", parts)}]";
    }

    private static System.Numerics.Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    private float Height(System.Numerics.Vector2 p) => TerrainHeight.At(W.Heightmap, p.X, p.Y);

    private EntityHandle Spawn(int owner, int type, System.Numerics.Vector2 at)
    {
        var alive = new bool[U.Capacity];
        for (int i = 0; i < U.Capacity; i++) alive[i] = U.Alive[i];
        _sim.Enqueue(Command.SpawnUnit(owner, type, at));
        Apply();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && !alive[i]) return new EntityHandle(i, U.Generation[i]);
        throw new InvalidOperationException($"spawn of {_data.Units[type].Key} at {at} refused");
    }

    private int SiteAt(int anchor) => anchor < 0 ? -1 : B.SlotAt(anchor % G.Width, anchor / G.Width);

    private int HallSlot()
    {
        for (int k = 0; k < B.Capacity; k++)
            if (B.Alive[k] && B.Owner[k] == 0 && _data.Buildings[B.TypeId[k]].Slot == BuildingSlot.TownHall) return k;
        throw new InvalidOperationException("player 0 has no hall");
    }

    private System.Numerics.Vector2 HallCenter()
    {
        int k = HallSlot();
        return StartBase.FootprintCenter(G, _data.Buildings[B.TypeId[k]], B.Cell[k]);
    }

    // The centre of the live gold mine nearest the hall (what a right click on its prop resolves to).
    private System.Numerics.Vector2 MineCenter()
    {
        ResourceStore r = W.Resources;
        int mine = _data.FindResource("gold_mine"), best = -1;
        float bestD = float.MaxValue;
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k] || r.TypeId[k] != mine) continue;
            ResourceDef def = _data.Resources[mine];
            int a = r.Cell[k];
            var c = new System.Numerics.Vector2(a % G.Width + def.FootprintWidth / 2f, a / G.Width + def.FootprintHeight / 2f) * MapConstants.CellSize;
            float d = System.Numerics.Vector2.Distance(c, _hall);
            if (d < bestD) (best, bestD, _mineCenter) = (k, d, c);
        }
        if (best < 0) throw new InvalidOperationException("no gold mine");
        return _mineCenter;
    }

    private System.Numerics.Vector2 _mineCenter;

    // The centre of the nearest open cell (passable, no node, no building) to a point.
    private System.Numerics.Vector2 OpenGround(System.Numerics.Vector2 p)
    {
        G.WorldToCell(Passable(p), out int px, out int py);
        for (int r = 0; r < 20; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    System.Numerics.Vector2 c = G.CellCenter(px + dx, py + dy);
                    if (StartLayout.IsOpen(G, px + dx, py + dy) && SelectionController.NodeAt(W, c) < 0 && BuildingPicker.SlotAt(B, G, c) < 0) return c;
                }
        throw new InvalidOperationException($"no open ground near {p}");
    }

    // The centre of the passable cell nearest a point (clamped onto the map).
    private System.Numerics.Vector2 Passable(System.Numerics.Vector2 p)
    {
        int x = Math.Clamp((int)(p.X / MapConstants.CellSize), 1, G.Width - 2), y = Math.Clamp((int)(p.Y / MapConstants.CellSize), 1, G.Height - 2);
        int cell = FlowField.NearestPassable(G, y * G.Width + x);
        if (cell < 0) throw new InvalidOperationException($"nothing passable near {p}");
        return G.CellCenter(cell % G.Width, cell / G.Width);
    }

    // The nearest anchor to `near` (within 4-30 m) where CanPlace says yes, no unit stands within 2 m of the footprint and
    // no anchor this scene used before is within 4 cells.
    private int GreenAnchor(int type, System.Numerics.Vector2 near)
    {
        BuildingDef def = _data.Buildings[type];
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, cell);
            float d = System.Numerics.Vector2.Distance(c, near);
            if (d >= bestD || d < 4f || d > 30f) continue;
            int x = cell % G.Width, y = cell / G.Width;
            bool used = false;
            foreach (int a in _usedAnchors) if (Math.Abs(a % G.Width - x) < def.FootprintWidth + 3 && Math.Abs(a / G.Width - y) < def.FootprintHeight + 3) used = true;
            if (used || !W.CanPlace(0, type, cell, out _)) continue;
            bool empty = true;
            for (int i = 0; i < U.Capacity && empty; i++)
                if (U.Alive[i] && MathF.Abs(U.Position[i].X - c.X) < def.FootprintWidth + 2f && MathF.Abs(U.Position[i].Y - c.Y) < def.FootprintHeight + 2f) empty = false;
            if (empty) (best, bestD) = (cell, d);
        }
        if (best >= 0) _usedAnchors.Add(best);
        return best;
    }

    // Puts the ghost's cursor on the middle of the footprint anchored at `anchor`; returns that screen point.
    private async Task<Vector2> Aim(int type, int anchor)
    {
        BuildingDef def = _data.Buildings[type];
        System.Numerics.Vector2 p = G.CellCenter(anchor % G.Width + def.FootprintWidth / 2, anchor / G.Width + def.FootprintHeight / 2);
        Vector2 px = await OnScreen(p);
        _ghost.ScreenOverride = px;
        await Frames();
        return px;
    }

    private async Task<Vector2> OnScreen(System.Numerics.Vector2 ground)
    {
        _camera.SetZoom(30f);
        _camera.SetFocus(ground.X, ground.Y);
        await Frame();
        return _camera.UnprojectPosition(new Vector3(ground.X, TerrainHeight.At(W.Heightmap, ground.X, ground.Y), ground.Y));
    }

    private void SetMoney(int gold, int wood)
    {
        object ledger = LedgerProperty.GetValue(W)!;
        ((int[])ledger.GetType().GetProperty("Gold")!.GetValue(ledger)!)[0] = gold;
        ((int[])ledger.GetType().GetProperty("Wood")!.GetValue(ledger)!)[0] = wood;
    }

    private List<Command> Pending(int from)
    {
        var q = (CommandQueue)CommandsField.GetValue(_sim)!;
        var list = new List<Command>();
        for (int i = from; i < q.Count; i++) list.Add(q[i]);
        return list;
    }

    private static string Describe(List<Command> list) =>
        $"[{string.Join(", ", list.Select(c => $"{c.Kind}{(c.IsQueued ? "+q" : "")} u{c.Unit.Index} t{c.TypeId} ({c.Position.X:0.#},{c.Position.Y:0.#})"))}]";

    // Ticks until every enqueued command has applied (two ticks for a command given between ticks).
    private void Apply()
    {
        int n = 0;
        while (_sim.PendingCommandCount > 0 && n < 4)
        {
            _sim.Tick();
            n++;
        }
        Check(_sim.PendingCommandCount == 0, $"commands still pending after {n} ticks");
    }

    private void Tick(int n)
    {
        for (int i = 0; i < n; i++) _sim.Tick();
    }

    private int RunUntil(Func<bool> done, int max)
    {
        int t = 0;
        while (t < max && !done())
        {
            _sim.Tick();
            t++;
        }
        return t;
    }

    private void Key(Godot.Key key)
    {
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void LeftClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private void RightClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Frames()
    {
        await Frame();
        await Frame();
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
