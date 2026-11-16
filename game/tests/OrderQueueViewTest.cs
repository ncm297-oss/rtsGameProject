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
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V22 on the real Match scene and the shipped data: the selected units' queued orders drawn (<see cref="OrderPathView"/>).
/// Rows: (3) per order kind (Move, AttackMove, Attack, Gather, Build, Repair, UseAbility at a point and with no target,
/// Stop, HoldPosition) a unit walking a Move is given the order with Shift through the mouse's entry points (right click,
/// A + click, the ghost, the ability click, S / H, the context order), the sim's queue holds it, a marker of that kind is
/// drawn at the right point (0.1 m), and when its turn comes the unit starts it and the marker goes; (4) an enemy selected
/// draws nothing, a queued Attack's marker follows the fog frame by frame as its target walks out of sight and back, and
/// goes when the target dies; (5) the first Shift-placed building: a gatherer given a plain placement and an idle worker
/// build at once, a walker builds after it arrives (its Move kept), a builder builds houses 1, 2 and 3 in turn, and
/// BUG-0122's guard holds; M6-V23: behind a gather loop (BehindAGatherLoop: a loaded worker delivers its full load, then
/// builds; (a2) walking out empty-handed it builds from the tick the command applies; (a3) a second Shift placement queues
/// behind the first; (a4, a match of its own) with no drop-off standing it builds at once and keeps its cargo); (6) a
/// ninth queued order is dropped by the sim and 8 markers are drawn; (7) 0 bytes a frame once warm, and the drawing's cost
/// with 200 selected units, 8 shared and 8 distinct points each (reported). M6-VH8: (9) a Shift-placed building for a worker
/// whose queue is full is refused (nothing sent, the Refused sound, the ghost kept and the click not counted), and with one
/// full and one idle worker only the idle one builds; M6-V23 (BUG-1501): a Shift-queued Move and a Build inside one tick
/// onto a queue of 7 refuse the Build, and the count resets after the tick; (10) BUG-1470: a no-place entry is a torus ring in its own multimesh
/// drawn before the markers, its hole wider than the marker it surrounds, the next one out round it; (11) markers, rings
/// and lines sized by the zoom (on-screen pixels at 60, 40 and 20 m, checked against a minimum and a maximum); the mixed
/// queue with all 7 markers in frame at 40 and 60 m (BUG-1471 item 1); (12) a recorded match replayed to its end: the
/// own soldier's queue drawn, the enemy's not, and the figures still.
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/OrderQueueViewTest.tscn</c>; prints "ORDER QUEUE VIEW TEST
/// PASS" and exits 0, or each failure and exits 1. The test ticks the sim itself (runner disabled). Windowed with
/// <c>-- --shots &lt;dir&gt;</c> it saves the pictures (12 soldiers' queue, a worker's three queued buildings, a mixed queue at the
/// default zoom, zoomed out and zoomed in); with <c>-- --cost</c> it also reports frame time and draw calls (vsync off).
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
    private Sfx _sfx = null!;
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
        await FullQueueBuilding();
        await RingRows();
        await ZoomRows();
        await FogRows();
        await CostRows();
        await EndMatch();
        // M6-V23: the no-drop-off row razes player 0's hall, so it gets a match of its own.
        _usedAnchors.Clear();
        await StartMatch();
        Stage(24f);
        await NoDropOffRow();
        await EndMatch();
        // The mixed queue in a fresh match, on open ground away from the base and the rows' units: headless it checks the
        // staging and that every marker is in frame (BUG-1471 item 1); windowed with --shots it also saves the pictures.
        _usedAnchors.Clear();
        await StartMatch();
        Stage(50f);
        await Pictures();
        await EndMatch();
        await ReplayRows();
    }

    private async Task StartMatch(bool record = false)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        if (record) _runner.RecordCheckpointInterval = 20;
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--mute" }));
        _runner.ProcessMode = ProcessModeEnum.Disabled; // the test ticks the sim itself
        _sim = _runner.Simulation!;
        Open();
        Check(_view.Fog == _fog, "the match did not give the order paths the fog");
        Check(_view.Camera == _camera, "the match did not give the order paths the camera");
        Tick(2);
        await Frames();
    }

    // The match's nodes this scene reads (a live match's or a replay's).
    private void Open()
    {
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _ghost = _match.GetNode<BuildGhost>("World3D/BuildGhost");
        _view = _match.GetNode<OrderPathView>("World3D/OrderPaths");
        _fog = _match.GetNode<FogOfWar>("World3D/FogOfWar");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sfx = _match.GetNode<Sfx>("Sfx");
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
    private void Stage(float ground, bool money = true)
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
        if (money) SetMoney(50000, 50000); // a direct state edit: off for a recorded match (its replay would not match)
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

    // M6-V23 (was M6-V22's "known gap" row: a queued order behind a loop never started until M6-S11). A loaded worker on a
    // gather loop at the mine nearest the finished hall (in reach of its node, a drop-off standing), one Shift placement of
    // a house: the Build is sent queued and drawn, the worker keeps working, delivers its full load (the bank rises by it,
    // less the house's price when the site goes down in the same tick), then walks to the site and builds; nothing carried is
    // lost. No exact position or tick is pinned (the sim track tunes gather movement).
    private async Task BehindAGatherLoop()
    {
        EntityHandle worker = Spawn(0, _laborer, Passable(MineCenter() + _away * 4f));
        int i = worker.Index;
        _sim.Enqueue(Command.Gather(0, worker, MineCenter()));
        Apply();
        RunUntil(() => U.State[i] == UnitState.Gathering && U.Cargo[i] > 0, 1500);
        if (!Check(EconomySystem.OnLoop(U, i) && U.State[i] == UnitState.Gathering && U.Cargo[i] > 0,
                $"behind a gather loop: the worker is not working its node with a load (state {U.State[i]}, cargo {U.Cargo[i]})")) return;
        (List<Command> sent, int anchor) = await ShiftPlace(worker, Passable(_hall + _perp * 12f));
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.Build && sent[0].IsQueued, $"behind a gather loop: sent {Describe(sent)}, want one queued Build");
        Apply();
        _sel.SelectOnly(worker);
        _view.Sync(W, 1f);
        System.Numerics.Vector2 centre = anchor >= 0 ? StartBase.FootprintCenter(G, _data.Buildings[_house], anchor) : default;
        Check(EconomySystem.OnLoop(U, i) && U.QueueCount[i] == 1 && U.QueueKind[i * OrderConstants.QueueCapacity] == CommandKind.Build && SiteAt(anchor) < 0
              && FindMarker(CommandKind.Build, centre, false) >= 0,
            $"behind a gather loop: on loop {EconomySystem.OnLoop(U, i)}, queue {U.QueueCount[i]}, site {SiteAt(anchor)}, Build marker at {centre}: {Markers()}");
        int carry = _data.CarryOf(_laborer), cost = _data.Buildings[_house].CostGold;
        int delivered = -1, bankRise = int.MinValue, lost = 0, ticks = 0, builtAt = -1;
        bool siteBeforeDelivery = false;
        for (; ticks < 3000 && builtAt < 0; ticks++)
        {
            int cargo = U.Cargo[i], gold = W.Gold[0];
            bool siteBefore = SiteAt(anchor) >= 0;
            _sim.Tick();
            if (delivered < 0)
            {
                if (U.Cargo[i] == 0 && cargo > 0)
                {
                    delivered = cargo;
                    bool placedNow = !siteBefore && SiteAt(anchor) >= 0;
                    bankRise = W.Gold[0] - gold + (placedNow ? cost : 0);
                }
                else if (U.Cargo[i] < cargo) lost += cargo - U.Cargo[i];
                else if (siteBefore || U.BuildTarget[i].Generation != 0) siteBeforeDelivery = true;
            }
            if (delivered >= 0 && U.State[i] == UnitState.Building) builtAt = ticks;
        }
        Check(delivered == carry && bankRise == carry && lost == 0 && !siteBeforeDelivery,
            $"behind a gather loop: delivered {delivered} (carry {carry}), bank rose {bankRise}, lost {lost}, started the Build before delivering {siteBeforeDelivery}");
        Check(builtAt >= 0 && SiteAt(anchor) >= 0 && B.UnderConstruction[SiteAt(anchor)] && U.QueueCount[i] == 0 && !EconomySystem.OnLoop(U, i),
            $"behind a gather loop: building {builtAt >= 0} after {ticks} ticks, site {SiteAt(anchor)}, queue {U.QueueCount[i]}, on loop {EconomySystem.OnLoop(U, i)}");
        GD.Print($"behind a gather loop: a loaded worker, Shift + place a house: queued and drawn; delivered its full load ({delivered}, the bank +{bankRise}), then built (building {builtAt} ticks after the placement)");
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
        // (a) A worker on a gather loop, a plain placement (no Shift): replaced at once (M6-V23: with Shift it now queues,
        // the BehindAGatherLoop row and (a2) / (a3) below; this row was the Shift placement replacing the loop).
        EntityHandle gatherer = Spawn(0, _laborer, Passable(MineCenter() + _away * 4f));
        Apply();
        System.Numerics.Vector2 mine = MineCenter();
        _sim.Enqueue(Command.Gather(0, gatherer, mine));
        Apply();
        Tick(10);
        if (Check(EconomySystem.OnLoop(U, gatherer.Index), "(a) the worker is not on a gather loop"))
        {
            (List<Command> sent, int anchor) = await ShiftPlace(gatherer, Passable(U.Position[gatherer.Index] - _away * 8f), shift: false);
            Check(sent.Count == 1 && sent[0].Kind == CommandKind.Build && !sent[0].IsQueued, $"(a) gatherer, plain placement: sent {Describe(sent)}, want one Build replacing the loop");
            Apply();
            Check(U.BuildTarget[gatherer.Index].Generation != 0 && !EconomySystem.OnLoop(U, gatherer.Index) && SiteAt(anchor) >= 0,
                $"(a) gatherer: build {U.BuildTarget[gatherer.Index]}, on loop {EconomySystem.OnLoop(U, gatherer.Index)}, site {SiteAt(anchor)}");
            GD.Print($"(a) a gatherer, plain place: the Build replaced the loop at once (site {SiteAt(anchor)})");
        }

        // (a2) A worker walking out to its node empty-handed (out of reach): the Shift placement is queued, and the worker
        // starts it in the tick the command applies (the sim ends the loop for a queue when no trip is in hand: M6-S11).
        EntityHandle walkingOut = Spawn(0, _laborer, Passable(mine + _away * 12f));
        _sim.Enqueue(Command.Gather(0, walkingOut, mine));
        Apply();
        if (Check(EconomySystem.OnLoop(U, walkingOut.Index) && U.State[walkingOut.Index] == UnitState.Moving && U.Cargo[walkingOut.Index] == 0
                  && System.Numerics.Vector2.Distance(U.Position[walkingOut.Index], mine) > 6f,
                $"(a2) the worker is not walking out empty-handed (state {U.State[walkingOut.Index]}, cargo {U.Cargo[walkingOut.Index]}, {System.Numerics.Vector2.Distance(U.Position[walkingOut.Index], mine):0.0} m from the mine)"))
        {
            (List<Command> sent, int anchor) = await ShiftPlace(walkingOut, Passable(_hall - _perp * 12f));
            Check(sent.Count == 1 && sent[0].Kind == CommandKind.Build && sent[0].IsQueued, $"(a2) walking out: sent {Describe(sent)}, want one queued Build");
            // A command sent between ticks applies in the second tick after it (Simulation.Enqueue stamps the next tick
            // number): after the first the worker still walks its loop with the Build pending; the second applies it.
            _sim.Tick();
            bool waiting = _sim.PendingCommandCount == 1 && EconomySystem.OnLoop(U, walkingOut.Index) && U.BuildTarget[walkingOut.Index].Generation == 0;
            _sim.Tick();
            Check(waiting && _sim.PendingCommandCount == 0 && U.BuildTarget[walkingOut.Index].Generation != 0 && SiteAt(anchor) >= 0 && !EconomySystem.OnLoop(U, walkingOut.Index)
                  && U.QueueCount[walkingOut.Index] == 0,
                $"(a2) walking out: pending before the applying tick {waiting}; after it build {U.BuildTarget[walkingOut.Index]}, site {SiteAt(anchor)}, on loop {EconomySystem.OnLoop(U, walkingOut.Index)}, queue {U.QueueCount[walkingOut.Index]}");
            GD.Print($"(a2) a worker walking out empty-handed, Shift + place: queued, building from the tick it applied (site {SiteAt(anchor)})");
        }

        // (a3) A loaded worker at its node: two Shift placements, the second queued behind the first (both queued, the
        // worker still on its loop finishing its trip).
        EntityHandle twice = Spawn(0, _laborer, Passable(mine - _away * 4f));
        _sim.Enqueue(Command.Gather(0, twice, mine));
        Apply();
        int ti = twice.Index;
        RunUntil(() => U.State[ti] == UnitState.Gathering && U.Cargo[ti] > 0, 1500);
        if (Check(EconomySystem.OnLoop(U, ti) && U.State[ti] == UnitState.Gathering && U.Cargo[ti] > 0, $"(a3) the worker is not working its node with a load ({U.State[ti]}, cargo {U.Cargo[ti]})"))
        {
            int h1 = GreenAnchor(_house, Passable(_hall + _perp * 20f));
            if (Check(h1 >= 0 && await OpenGhost(_house, twice), "(a3) no house spot or ghost"))
            {
                int from = _sim.PendingCommandCount;
                Input.ActionPress("order_queue");
                LeftClick(await Aim(_house, h1));
                int h2 = GreenAnchor(_house, Passable(_hall + _perp * 20f + _away * 6f));
                if (h2 >= 0) LeftClick(await Aim(_house, h2));
                Input.ActionRelease("order_queue");
                List<Command> sent = Pending(from);
                _card.CloseMenu();
                Apply();
                Check(h2 >= 0 && sent.Count == 2 && sent.All(c => c.Kind == CommandKind.Build && c.IsQueued),
                    $"(a3) two Shift placements on a gatherer: sent {Describe(sent)}, want two queued Builds");
                int head = ti * OrderConstants.QueueCapacity;
                Check(EconomySystem.OnLoop(U, ti) && U.QueueCount[ti] == 2 && U.QueueKind[head] == CommandKind.Build && U.QueueKind[head + 1] == CommandKind.Build
                      && SiteAt(h1) < 0 && SiteAt(h2) < 0,
                    $"(a3) two Shift placements on a gatherer: on loop {EconomySystem.OnLoop(U, ti)}, queue {U.QueueCount[ti]}, sites {SiteAt(h1)} / {SiteAt(h2)}");
                GD.Print($"(a3) a loaded gatherer, two Shift placements: both queued behind its trip ({U.QueueCount[ti]} in its queue)");
            }
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
        foreach (EntityHandle h in new[] { gatherer, walkingOut, twice, idle, walker, builder }) _sim.Enqueue(Command.Stop(0, h));
        Apply();
    }

    // (a4) M6-V23: with no drop-off of its own standing, a loaded worker on its loop starts the Shift-placed building at
    // once (in the tick the command applies) and keeps what it carries (M6-S11's rule). The hall, the one drop-off, is
    // razed between ticks (a direct state edit; the player keeps its units, so it is not defeated).
    private async Task NoDropOffRow()
    {
        System.Numerics.Vector2 mine = MineCenter();
        EntityHandle worker = Spawn(0, _laborer, Passable(mine + _away * 4f));
        int i = worker.Index;
        _sim.Enqueue(Command.Gather(0, worker, mine));
        Apply();
        RunUntil(() => U.State[i] == UnitState.Gathering && U.Cargo[i] > 0, 1500);
        if (!Check(EconomySystem.OnLoop(U, i) && U.State[i] == UnitState.Gathering && U.Cargo[i] > 0,
                $"(a4) the worker is not working its node with a load (state {U.State[i]}, cargo {U.Cargo[i]})")) return;
        int razed = 0;
        for (int k = 0; k < B.Capacity; k++)
        {
            if (!B.Alive[k] || B.Owner[k] != 0 || !_data.Buildings[B.TypeId[k]].DropOff) continue;
            DamageMethod.Invoke(B, new object[] { B.HandleOf(k), B.Hp[k] });
            razed++;
        }
        int standing = 0;
        for (int k = 0; k < B.Capacity; k++) if (B.Alive[k] && B.Owner[k] == 0 && _data.Buildings[B.TypeId[k]].DropOff) standing++;
        if (!Check(razed > 0 && standing == 0, $"(a4) razed {razed} drop-offs, {standing} still standing")) return;
        int cargo = U.Cargo[i];
        (List<Command> sent, int anchor) = await ShiftPlace(worker, Passable(_hall + _perp * 12f));
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.Build && sent[0].IsQueued, $"(a4) no drop-off: sent {Describe(sent)}, want one queued Build");
        Apply(); // ticks until the command applies: its last tick is the one it applies in
        Check(_sim.PendingCommandCount == 0 && U.BuildTarget[i].Generation != 0 && SiteAt(anchor) >= 0 && !EconomySystem.OnLoop(U, i) && U.Cargo[i] == cargo,
            $"(a4) no drop-off: after the applying tick build {U.BuildTarget[i]}, site {SiteAt(anchor)}, on loop {EconomySystem.OnLoop(U, i)}, cargo {U.Cargo[i]} (had {cargo})");
        GD.Print($"(a4) no drop-off standing, a loaded gatherer, Shift + place: queued, building from the tick it applied, its {U.Cargo[i]} cargo kept");
    }

    // Selects the worker alone, opens a house ghost and Shift-clicks (a plain click without `shift`) a green spot near
    // `near`; returns the commands and the anchor.
    private async Task<(List<Command> Sent, int Anchor)> ShiftPlace(EntityHandle worker, System.Numerics.Vector2 near, bool shift = true)
    {
        int anchor = GreenAnchor(_house, near);
        if (!Check(anchor >= 0, $"no green house spot near {near}")) return (new List<Command>(), -1);
        if (!Check(await OpenGhost(_house, worker), "the house ghost did not open")) return (new List<Command>(), anchor);
        Vector2 px = await Aim(_house, anchor);
        int start = _sim.PendingCommandCount;
        if (shift) Input.ActionPress("order_queue");
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

    // ---- (9) BUG-1471 item 2: a Shift-placed building for a worker whose queue is full ----

    private async Task FullQueueBuilding()
    {
        // A worker walking a Move with 8 queued Moves: the Build would be dropped by the sim, so the view refuses it.
        EntityHandle full = Spawn(0, _laborer, Passable(_t - _perp * 12f));
        Apply();
        System.Numerics.Vector2 goal = Passable(U.Position[full.Index] + _away * 30f);
        _sim.Enqueue(Command.Move(0, full, goal));
        for (int k = 0; k < OrderConstants.QueueCapacity; k++) _sim.Enqueue(Command.Move(0, full, Passable(goal + _perp * (2f + 2f * k)), queued: true));
        Apply();
        if (!Check(U.QueueCount[full.Index] == OrderConstants.QueueCapacity, $"full queue: the worker holds {U.QueueCount[full.Index]}, want {OrderConstants.QueueCapacity}")) return;
        int anchor = GreenAnchor(_house, U.Position[full.Index] + _perp * 6f);
        if (!Check(anchor >= 0 && await OpenGhost(_house, full), "full queue: no house spot or ghost")) return;
        Vector2 px = await Aim(_house, anchor);
        await Gap();
        int start = _sim.PendingCommandCount, refused = _sfx.PlayCount(SfxEvent.Refused), command = _sfx.PlayCount(SfxEvent.Command), drops = _sel.RefusedBuilds;
        Input.ActionPress("order_queue");
        LeftClick(px);
        Input.ActionRelease("order_queue");
        List<Command> sent = Pending(start);
        Check(sent.Count == 0, $"full queue: sent {Describe(sent)}, want nothing");
        Check(_sfx.PlayCount(SfxEvent.Refused) == refused + 1 && _sfx.PlayCount(SfxEvent.Command) == command && _sel.RefusedBuilds == drops + 1,
            $"full queue: {_sfx.PlayCount(SfxEvent.Refused) - refused} Refused and {_sfx.PlayCount(SfxEvent.Command) - command} Command sounds, {_sel.RefusedBuilds - drops} refusals (want 1, 0, 1)");
        Check(_card.GhostActive && _ghost.TypeId == _house, $"full queue: the ghost went down (active {_card.GhostActive})");
        // Not counted as placed: with the queue emptied, a Shift click on the same anchor is again the ghost's first
        // placement (an idle worker: an unqueued Build), not a later one (queued) nor BUG-0122's same-anchor refusal.
        _sim.Enqueue(Command.Stop(0, full));
        Apply();
        await Gap();
        start = _sim.PendingCommandCount;
        Input.ActionPress("order_queue");
        LeftClick(px);
        Input.ActionRelease("order_queue");
        sent = Pending(start);
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.Build && !sent[0].IsQueued && sent[0].Unit == full,
            $"full queue, then emptied: sent {Describe(sent)}, want one unqueued Build (the refused click was not counted as placed)");
        _card.CloseMenu();
        Apply();
        GD.Print($"full queue: a Shift placement for a worker with {OrderConstants.QueueCapacity} queued sent nothing, played Refused once, kept the ghost; emptied, the same click built at once");

        // Two workers, one full and one idle: the idle one gets the Build, the full one nothing, and the placement counts.
        EntityHandle full2 = Spawn(0, _laborer, Passable(_t - _perp * 16f));
        EntityHandle free = Spawn(0, _laborer, Passable(_t - _perp * 18f));
        Apply();
        goal = Passable(U.Position[full2.Index] + _away * 30f);
        _sim.Enqueue(Command.Move(0, full2, goal));
        for (int k = 0; k < OrderConstants.QueueCapacity; k++) _sim.Enqueue(Command.Move(0, full2, Passable(goal + _perp * (2f + 2f * k)), queued: true));
        Apply();
        _sel.Selection.Clear();
        _sel.Selection.Add(full2);
        _sel.Selection.Add(free);
        await Frames();
        int a2 = GreenAnchor(_house, U.Position[free.Index] + _perp * 6f);
        if (!Check(a2 >= 0 && await OpenGhost(_house), "two workers: no house spot or ghost")) return;
        px = await Aim(_house, a2);
        await Gap();
        start = _sim.PendingCommandCount;
        command = _sfx.PlayCount(SfxEvent.Command);
        refused = _sfx.PlayCount(SfxEvent.Refused);
        Input.ActionPress("order_queue");
        LeftClick(px);
        Input.ActionRelease("order_queue");
        sent = Pending(start);
        Check(sent.Count == 1 && sent[0].Unit == free && !sent[0].IsQueued, $"one full and one idle worker: sent {Describe(sent)}, want one unqueued Build to the idle one");
        Check(_sfx.PlayCount(SfxEvent.Command) == command + 1 && _sfx.PlayCount(SfxEvent.Refused) == refused,
            $"one full and one idle worker: {_sfx.PlayCount(SfxEvent.Command) - command} Command and {_sfx.PlayCount(SfxEvent.Refused) - refused} Refused sounds (want 1, 0)");
        _card.CloseMenu();
        Apply();
        GD.Print($"one full and one idle worker: the idle one got the Build ({Describe(sent)}), the full one nothing");

        // M6-V23 (BUG-1501): a worker at 7 queued, a Shift-queued Move and then a Shift-placed Build inside one tick: the
        // Move counts against the queue, so the Build is refused; after a tick the count resets (once the worker's walk ends
        // and its queue is back at 7, the same click is sent).
        EntityHandle seven = Spawn(0, _laborer, Passable(_t - _perp * 20f));
        int si = seven.Index;
        System.Numerics.Vector2 near = Passable(U.Position[si] + _away * 4f);
        goal = Passable(U.Position[si] + _away * 30f);
        _sim.Enqueue(Command.Move(0, seven, near));
        for (int k = 0; k < OrderConstants.QueueCapacity - 1; k++) _sim.Enqueue(Command.Move(0, seven, Passable(goal + _perp * (2f + 2f * k)), queued: true));
        Apply();
        if (!Check(U.QueueCount[si] == OrderConstants.QueueCapacity - 1, $"move then build: the worker holds {U.QueueCount[si]}, want {OrderConstants.QueueCapacity - 1}")) return;
        int a3 = GreenAnchor(_house, U.Position[si] - _perp * 6f);
        if (!Check(a3 >= 0 && await OpenGhost(_house, seven), "move then build: no house spot or ghost")) return;
        px = await Aim(_house, a3);
        await Gap();
        start = _sim.PendingCommandCount;
        refused = _sfx.PlayCount(SfxEvent.Refused);
        drops = _sel.RefusedBuilds;
        _sel.Order(CommandKind.Move, new Vector2(goal.X, goal.Y), queued: true);
        int counted = _sel.PendingQueuedOrders(_sim, si);
        Input.ActionPress("order_queue");
        LeftClick(px);
        Input.ActionRelease("order_queue");
        sent = Pending(start);
        Check(counted == 1 && sent.Count == 1 && sent[0].Kind == CommandKind.Move && _sfx.PlayCount(SfxEvent.Refused) == refused + 1 && _sel.RefusedBuilds == drops + 1,
            $"move then build in one tick onto 7: counted {counted}, sent {Describe(sent)}, {_sfx.PlayCount(SfxEvent.Refused) - refused} Refused, {_sel.RefusedBuilds - drops} refusals (want 1, the Move only, 1, 1)");
        // A command sent between ticks applies in the second tick after it (Simulation.Enqueue stamps the next tick number):
        // after the first it is still pending and still counted; after the tick it applies in, the count is 0 and the
        // queue holds it.
        _sim.Tick();
        int stillCounted = _sel.PendingQueuedOrders(_sim, si), stillPending = _sim.PendingCommandCount - start;
        Apply();
        Check(stillCounted == 1 && stillPending == 1 && _sel.PendingQueuedOrders(_sim, si) == 0 && U.QueueCount[si] == OrderConstants.QueueCapacity,
            $"move then build: one tick on counted {stillCounted} ({stillPending} pending); applied, counted {_sel.PendingQueuedOrders(_sim, si)}, queue {U.QueueCount[si]} (want 1, 1, 0, {OrderConstants.QueueCapacity})");
        int popped = RunUntil(() => U.QueueCount[si] < OrderConstants.QueueCapacity, 400);
        await Gap();
        start = _sim.PendingCommandCount;
        Input.ActionPress("order_queue");
        LeftClick(px);
        Input.ActionRelease("order_queue");
        sent = Pending(start);
        Check(U.QueueCount[si] == OrderConstants.QueueCapacity - 1 && sent.Count == 1 && sent[0].Kind == CommandKind.Build && sent[0].IsQueued,
            $"move then build: {popped} ticks later the queue holds {U.QueueCount[si]} and the same click sent {Describe(sent)} (want 7 and one queued Build)");
        _card.CloseMenu();
        Apply();
        GD.Print($"move then build in one tick onto 7: the Build refused (the Move counted); after the tick the count was 0, and with the queue back at 7 the click was sent");
        _sel.Selection.Clear();
        foreach (EntityHandle h in new[] { full, full2, free, seven }) _sim.Enqueue(Command.Stop(0, h));
        Apply();
    }

    // Sound plays of one event closer than Sfx.MinGapMs are dropped: wait past it before counting.
    private async Task Gap() => await ToSignal(GetTree().CreateTimer(Sfx.MinGapMs / 1000.0 * 2.0, processAlways: true), SceneTreeTimer.SignalName.Timeout);

    // ---- (10) BUG-1470: a no-place entry is a ring round the marker before it, drawn under it ----

    private async Task RingRows()
    {
        // The drawing: a torus whose hole is wider than a marker, in its own multimesh, drawn before the markers. A filled
        // disc over the marker (the bug) fails here: a cylinder, a hole under the marker, or the rings drawn after the discs.
        var torus = _view.RingsNode.Multimesh.Mesh as TorusMesh;
        var ringMat = (_view.RingsNode.Multimesh.Mesh as PrimitiveMesh)?.Material as BaseMaterial3D;
        var discMat = (_view.MarkersNode.Multimesh.Mesh as PrimitiveMesh)?.Material as BaseMaterial3D;
        float hole = torus == null ? 0f : torus.InnerRadius / torus.OuterRadius * OrderPathView.RingRadius(0);
        Check(torus != null && hole > OrderPathView.MarkerRadius,
            $"ring mesh: {_view.RingsNode.Multimesh.Mesh?.GetType().Name}, hole {hole:0.###} m at the first ring, want a torus whose hole is wider than the {OrderPathView.MarkerRadius} m marker");
        Check(_view.MarkersNode.Multimesh.Mesh is CylinderMesh && !ReferenceEquals(_view.RingsNode.Multimesh, _view.MarkersNode.Multimesh),
            "the rings must be their own multimesh, the markers discs");
        Check(ringMat != null && discMat != null && ringMat.NoDepthTest && discMat.NoDepthTest && ringMat.RenderPriority < discMat.RenderPriority,
            $"draw order: ring priority {ringMat?.RenderPriority}, marker priority {discMat?.RenderPriority}, want the rings drawn first");

        // A soldier walking a Move, then a queued Move to p, a Stop and a Hold: the Move's disc at p, the Stop's ring round
        // it and the Hold's ring round that.
        EntityHandle unit = Spawn(0, _hi, _t);
        Apply();
        _sel.SelectOnly(unit);
        await Frames();
        System.Numerics.Vector2 goal = Passable(_t + _away * 12f), p = OpenGround(_t + _away * 12f + _perp * 8f);
        _sim.Enqueue(Command.Move(0, unit, goal));
        _sim.Enqueue(Command.Move(0, unit, p, queued: true));
        _sim.Enqueue(Command.Stop(0, unit, queued: true));
        _sim.Enqueue(Command.HoldPosition(0, unit, queued: true));
        Apply();
        _view.Sync(W, 1f);
        int disc = FindMarker(CommandKind.Move, p, false), stop = FindMarker(CommandKind.Stop, p, true), hold = FindMarker(CommandKind.HoldPosition, p, true);
        if (!Check(disc >= 0 && stop >= 0 && hold >= 0, $"rings: Move {disc}, Stop {stop}, Hold {hold} at {p}; markers {Markers()}")) return;
        float discR = _view.MarkerDrawnRadius(disc), stopR = _view.MarkerDrawnRadius(stop), holdR = _view.MarkerDrawnRadius(hold);
        Check(_view.MarkerLevel(stop) == 0 && _view.MarkerLevel(hold) == 1, $"rings: Stop level {_view.MarkerLevel(stop)}, Hold level {_view.MarkerLevel(hold)}, want 0 and 1");
        Check(stopR * OrderPathView.RingInner > discR, $"rings: the Stop ring's hole {stopR * OrderPathView.RingInner:0.###} m covers the {discR:0.###} m marker");
        Check(holdR * OrderPathView.RingInner >= stopR - 1e-4f, $"rings: the Hold ring's hole {holdR * OrderPathView.RingInner:0.###} m overlaps the Stop ring ({stopR:0.###} m)");
        Check(_view.RingsNode.Multimesh.VisibleInstanceCount == 2 && _view.MarkersNode.Multimesh.VisibleInstanceCount == _view.MarkerCount - 2,
            $"rings: {_view.RingsNode.Multimesh.VisibleInstanceCount} ring and {_view.MarkersNode.Multimesh.VisibleInstanceCount} disc instances for {_view.MarkerCount} markers");
        GD.Print($"rings: a Move disc ({discR:0.00} m), the Stop's ring ({stopR * OrderPathView.RingInner:0.00}-{stopR:0.00} m) round it, the Hold's ({holdR * OrderPathView.RingInner:0.00}-{holdR:0.00} m) round that; torus mesh, rings drawn before discs");
        _sel.Selection.Clear();
        _sim.Enqueue(Command.Stop(0, unit));
        Apply();
        _sim.Enqueue(Command.Move(0, unit, Passable(_t - _away * 8f - _perp * 6f)));
        Apply();
    }

    // ---- (11) markers, rings and lines sized by the zoom ----

    /// <summary>At the farthest zoom a marker is at least this wide on a 648 px view, and a line at least this many pixels.</summary>
    private const float MinMarkerPx = 16f, MinLinePx = 3f;

    /// <summary>At the nearest zoom a marker is at most this wide, a first ring at most this wide, and a line at most this many pixels.</summary>
    private const float MaxMarkerPx = 32f, MaxRingPx = 56f, MaxLinePx = 4f;

    private async Task ZoomRows()
    {
        EntityHandle unit = Spawn(0, _hi, _t);
        Apply();
        System.Numerics.Vector2 goal = Passable(_t + _away * 10f), p = OpenGround(_t + _away * 10f - _perp * 10f);
        _sim.Enqueue(Command.Move(0, unit, goal));
        _sim.Enqueue(Command.Move(0, unit, p, queued: true));
        _sim.Enqueue(Command.Stop(0, unit, queued: true));
        Apply();
        _sel.SelectOnly(unit);
        foreach (float zoom in new[] { CameraLimits.MaxZoom, CameraLimits.DefaultZoom, CameraLimits.MinZoom })
        {
            _camera.SetZoom(zoom);
            System.Numerics.Vector2 mid = (goal + p) / 2f;
            _camera.SetFocus(mid.X, mid.Y);
            await Frames();
            _view.Sync(W, 1f);
            int disc = FindMarker(CommandKind.Move, p, false), ring = FindMarker(CommandKind.Stop, p, true), seg = -1;
            for (int k = 0; k < _view.SegmentCount; k++) if (System.Numerics.Vector2.Distance(Flat(_view.Segment(k).To), p) < Tolerance) seg = k;
            if (!Check(disc >= 0 && ring >= 0 && seg >= 0, $"zoom {zoom}: disc {disc}, ring {ring}, leg {seg}; markers {Markers()}")) continue;
            Vector3 c = _view.MarkerPoint(disc);
            float discPx = Across(c, Vector3.Right, _view.MarkerDrawnRadius(disc) * 2f);
            float ringPx = Across(c, Vector3.Right, _view.MarkerDrawnRadius(ring) * 2f);
            (Vector3 from, Vector3 to) = _view.Segment(seg);
            Vector3 along = (to - from).Normalized();
            float linePx = Across((from + to) / 2f + Vector3.Up * OrderPathView.LineLift, new Vector3(-along.Z, 0f, along.X), _view.LineDrawnWidth);
            GD.Print($"zoom {zoom}: marker {discPx:0.0} px wide ({_view.MarkerDrawnRadius(disc) * 2f:0.00} m), first ring {ringPx:0.0} px, line {linePx:0.0} px ({_view.LineDrawnWidth:0.000} m) on a {GetViewport().GetVisibleRect().Size.Y} px tall view");
            Check(_view.MarkerZoomScale == OrderPathView.ZoomScale(zoom, OrderPathView.MarkerFullZoom) && _view.LineZoomScale == OrderPathView.ZoomScale(zoom, OrderPathView.LineFullZoom),
                $"zoom {zoom}: drawn at factors {_view.MarkerZoomScale} / {_view.LineZoomScale}");
            if (zoom == CameraLimits.MaxZoom)
                Check(discPx >= MinMarkerPx && linePx >= MinLinePx, $"zoom {zoom}: marker {discPx:0.0} px, line {linePx:0.0} px, want at least {MinMarkerPx} / {MinLinePx} px");
            if (zoom == CameraLimits.MinZoom)
                Check(discPx <= MaxMarkerPx && ringPx <= MaxRingPx && linePx <= MaxLinePx,
                    $"zoom {zoom}: marker {discPx:0.0} px, ring {ringPx:0.0} px, line {linePx:0.0} px, want at most {MaxMarkerPx} / {MaxRingPx} / {MaxLinePx} px");
        }
        _sel.Selection.Clear();
        _sim.Enqueue(Command.Stop(0, unit));
        Apply();
        _camera.SetZoom(CameraLimits.DefaultZoom);
    }

    // The on-screen length (px) of a `length` m span centred on `at` along `dir` (on the ground plane).
    private float Across(Vector3 at, Vector3 dir, float length)
    {
        Vector3 d = dir.Normalized() * (length / 2f);
        return _camera.UnprojectPosition(at + d).DistanceTo(_camera.UnprojectPosition(at - d));
    }

    // ---- (12) a replay: queued orders drawn as in a match (own seat only); figures still at its end ----

    private async Task ReplayRows()
    {
        // A recorded live match: an own soldier walking far with two queued Moves and a queued Stop, an enemy Raider with a
        // queued Move. The recording ends while both still hold their queues.
        await StartMatch(record: true);
        Stage(24f, money: false);
        EntityHandle own = Spawn(0, _hi, Passable(_t + _perp * 6f));
        EntityHandle foe = Spawn(1, _raider, Passable(_t - _perp * 30f));
        System.Numerics.Vector2 far = Passable(_t + _perp * 6f + _away * 60f);
        System.Numerics.Vector2 q1 = OpenGround(far + _perp * 10f), q2 = OpenGround(far + _perp * 10f - _away * 10f);
        _sim.Enqueue(Command.Move(0, own, far));
        _sim.Enqueue(Command.Move(0, own, q1, queued: true));
        _sim.Enqueue(Command.Move(0, own, q2, queued: true));
        _sim.Enqueue(Command.Stop(0, own, queued: true));
        _sim.Enqueue(Command.Move(1, foe, Passable(_t - _perp * 30f - _away * 40f)));
        _sim.Enqueue(Command.Move(1, foe, Passable(_t - _perp * 40f - _away * 40f), queued: true));
        Apply();
        Tick(40);
        Replay replay = _runner.Recorder!.ToReplay();
        await EndMatch();

        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        ReplayError refused = _match.StartReplay(_data, replay, LaunchOptions.Parse(new[] { "--speed", "8", "--mute" }), "order-queue-replay");
        if (!Check(refused == ReplayError.None, $"replay refused: {refused}")) { await EndMatch(); return; }
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _sim = _runner.Simulation!;
        Open();
        var units = _match.GetNode<UnitViews>("World3D/UnitViews");
        double playingRate = units.AnimationRate;
        ulong start = Time.GetTicksUsec();
        while (_runner.PlaybackState == PlaybackState.Playing && Time.GetTicksUsec() - start < 20_000_000UL) await Frame();
        if (!Check(_runner.PlaybackState == PlaybackState.Ended, $"replay: {_runner.PlaybackState} at tick {_sim.TickNumber} of {replay.TickCount}")) { await EndMatch(); return; }
        Check(U.IsAlive(own) && U.IsAlive(foe) && U.QueueCount[own.Index] == 3 && U.QueueCount[foe.Index] == 1,
            $"replay: at its end the soldier holds {U.QueueCount[own.Index]} and the Raider {U.QueueCount[foe.Index]} queued orders, want 3 and 1");
        _sel.SelectOnly(own);
        await Frames(); // the view draws from its own frame step, as in a match
        Check(FindMarker(CommandKind.Move, q1, false) >= 0 && FindMarker(CommandKind.Move, q2, false) >= 0 && FindMarker(CommandKind.Stop, q2, true) >= 0 && _view.MarkerCount == 3,
            $"replay, own soldier selected: markers {Markers()}, want Moves at {q1} and {q2} and the Stop's ring at {q2}");
        GD.Print($"replay: at its end (tick {_sim.TickNumber}) the own soldier's queue is drawn: {_view.MarkerCount} markers, {_view.SegmentCount} segments");
        _sel.SelectOnly(foe);
        await Frames();
        Check(_view.MarkerCount == 0 && _view.SegmentCount == 0, $"replay, enemy selected: {_view.MarkerCount} markers, {_view.SegmentCount} segments drawn (want none: own seat only)");
        GD.Print("replay: the enemy Raider selected draws nothing (own seat only, as in a match)");

        // M6-VH8: the figures stand still once the recording has ended (no more ticks come).
        AnimationPlayer? anim = units.AnimatorOf(own.Index);
        double before = anim?.CurrentAnimationPosition ?? -1;
        for (int k = 0; k < 10; k++) await Frame();
        double after = anim?.CurrentAnimationPosition ?? -1;
        Check(playingRate == 8.0 && units.AnimationRate == 0.0, $"replay: animation rate {playingRate} while playing, {units.AnimationRate} at its end (want 8 and 0)");
        Check(anim != null && anim.IsPlaying() && before == after, $"replay: the soldier's clip moved from {before:0.000} to {after:0.000} s over 10 frames after the end");
        GD.Print($"replay: at its end the figures stand still (rate {units.AnimationRate}, the soldier's {anim?.CurrentAnimation} clip at {after:0.000} s over 10 frames)");
        await EndMatch();
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

        // A mixed queue (BUG-1471 item 1: staged round a tree, so the Gather's marker is in frame too): a soldier (AttackMove,
        // Attack on a seen enemy, Stop), the Mage (Telas Fire at a point, Hold) and a worker (Gather on the tree, Build).
        System.Numerics.Vector2 tree = NearestTree(_t + _perp * 18f);
        System.Numerics.Vector2 back = System.Numerics.Vector2.Normalize(_hall - tree);
        System.Numerics.Vector2 mix = OpenGround(tree + back * 12f);
        System.Numerics.Vector2 f = System.Numerics.Vector2.Normalize(tree - mix), s = new(-f.Y, f.X);
        EntityHandle soldier = Spawn(0, _hi, mix);
        EntityHandle mage = Spawn(0, _mage, OpenGround(mix + s * 2f));
        EntityHandle laborer = Spawn(0, _laborer, OpenGround(mix + s * 4f));
        EntityHandle foe = Spawn(1, _raider, OpenGround(mix + f * 7f - s * 3f));
        _sim.Enqueue(Command.HoldPosition(1, foe));
        int site = GreenAnchor(_house, mix + f * 4f + s * 9f);
        _sim.Enqueue(Command.Move(0, soldier, OpenGround(mix + f * 3f - s * 4f)));
        _sim.Enqueue(Command.AttackMove(0, soldier, OpenGround(mix + f * 4f - s * 8f), queued: true));
        _sim.Enqueue(Command.Attack(0, soldier, foe, false, queued: true));
        _sim.Enqueue(Command.Stop(0, soldier, queued: true));
        _sim.Enqueue(Command.Move(0, mage, OpenGround(mix + f * 3f + s * 1f)));
        System.Numerics.Vector2 cast = OpenGround(mix + f * 8f + s * 1f);
        _sim.Enqueue(Command.UseAbility(0, mage, 0, cast, queued: true));
        _sim.Enqueue(Command.HoldPosition(0, mage, queued: true));
        _sim.Enqueue(Command.Move(0, laborer, OpenGround(mix + f * 3f + s * 5f)));
        _sim.Enqueue(Command.Gather(0, laborer, tree, queued: true));
        if (site >= 0) _sim.Enqueue(Command.Build(0, laborer, _house, PlacementGhost.AnchorPoint(G, site), queued: true));
        Apply();
        _sel.Selection.Clear();
        foreach (EntityHandle h in new[] { soldier, mage, laborer }) _sel.Selection.Add(h);
        Tick(4);
        _view.Sync(W, 1f);
        Check(_view.MarkerCount == 7 && site >= 0 && FindMarker(CommandKind.Gather, tree, false) >= 0 && FindMarker(CommandKind.Attack, U.Position[foe.Index], false) >= 0,
            $"mixed queue: site {site}, {_view.MarkerCount} markers, want 7 with the Gather on the tree and the Attack on the Raider: {Markers()}");
        var points = new List<System.Numerics.Vector2>();
        for (int k = 0; k < _view.MarkerCount; k++) points.Add(Flat(_view.MarkerPoint(k)));
        foreach (EntityHandle h in new[] { soldier, mage, laborer }) points.Add(U.Position[h.Index]);
        System.Numerics.Vector2 view = Centre(points.ToArray());
        await Shot("queue-mixed-default-zoom", view, CameraLimits.DefaultZoom);
        InFrame("mixed queue at the default zoom", 7);
        await Shot("queue-mixed-zoomed-out", view, CameraLimits.MaxZoom);
        InFrame("mixed queue zoomed out", 7);
        // The nearest zoom, on the soldier's Stop ring round its Attack marker and the Mage's Hold ring round its cast marker.
        await Shot("queue-mixed-zoomed-in", Centre(U.Position[foe.Index], cast), CameraLimits.MinZoom);
    }

    // Counts this frame's markers whose point is on screen and not under the HUD; checks all `want` are.
    private void InFrame(string what, int want)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        var hud = new List<Control>();
        foreach (string n in new[] { "Minimap", "ResourceBar", "CommandCard", "QueueStrip", "SelectionPanel" })
            if (_match.GetNodeOrNull<Control>($"Hud/{n}") is Control c && c.IsVisibleInTree()) hud.Add(c);
        int inside = 0;
        var outside = new List<string>();
        for (int k = 0; k < _view.MarkerCount; k++)
        {
            Vector3 at = _view.MarkerPoint(k);
            Vector2 px = _camera.UnprojectPosition(at);
            bool ok = !_camera.IsPositionBehind(at) && screen.HasPoint(px) && !hud.Exists(c => c.GetGlobalRect().HasPoint(px));
            if (ok) inside++;
            else outside.Add($"{_view.MarkerKind(k)} at ({px.X:0},{px.Y:0})");
        }
        GD.Print($"{what}: {inside} of {_view.MarkerCount} markers in frame{(outside.Count > 0 ? $" (out: {string.Join(", ", outside)})" : "")}");
        Check(inside == want && _view.MarkerCount == want, $"{what}: {inside} of {_view.MarkerCount} markers in frame, want {want} of {want}");
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

    // Puts the shot's content above the HUD (the camera looks north, -z, so the focus moves a little south of it), lets the
    // view draw a frame, and saves the picture when windowed with --shots.
    private async Task Shot(string name, System.Numerics.Vector2 focus, float zoom)
    {
        _camera.SetZoom(zoom);
        _camera.SetFocus(focus.X, focus.Y + zoom * 0.1f);
        await Frames();
        if (_shots == null || DisplayServer.GetName() == "headless") return;
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
