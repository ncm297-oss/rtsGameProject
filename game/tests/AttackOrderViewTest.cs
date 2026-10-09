using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M4-V2 on the real Match scene: the Attack order from the HUD. 10 Heavy Infantry v 10 Raiders near the map centre, a
/// Tent behind the Raiders and the player's Billet beside it as a spotter, all staged in the player's sight (<see cref="AttackStage"/>:
/// since M4-3a an Attack on an unseen enemy is dropped, BUG-0218); clicks go through the viewport (<c>PushInput</c>), as the player's do. Rows per seed:
/// a right-click on an enemy unit (every selected unit holds that target, <c>CombatMode.Ordered</c>, the next tick), A + click on
/// an enemy (the same), A + click on the ground (an AttackMove, no Attack), Shift + right-click on two enemies while walking
/// away (both queued as Attack entries, in order, <c>QueuedTarget</c>), a right-click on the enemy Tent (a building target), a
/// right-click on an own unit (a Move), a minimap right-click on an enemy dot (a Move: the minimap's Attack half waits for
/// M4-3); the Command sound; the panel's ordered-attack label while chasing and "Attacking" while swinging; the red ring
/// (one pooled node, on the target, gone after 0.5 s, gone when the target dies, 0 bytes over 300 steady brawl frames); the
/// F12 line's target slot; and the hash twin (the scene's command stream replayed bare equals the views' sim every tick).
/// </summary>
/// <remarks>Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/AttackOrderViewTest.tscn</c> (seeds 1 and 6; <c>-- --seed N</c> for one); prints "ATTACK ORDER VIEW TEST PASS" and exits 0, or each failure and exits 1. Windowed with <c>-- --shots &lt;dir&gt;</c> it also saves the red ring on the first right-clicked enemy (<c>attack-ring-seedN.png</c>).</remarks>
public partial class AttackOrderViewTest : Node
{
    private const int PerSide = 10;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private SelectionPanel _panel = null!;
    private DebugOverlay _overlay = null!;
    private TargetRing _ring = null!;
    private UnitViews _units = null!;
    private CombatViews _combat = null!;
    private Minimap _mini = null!;
    private Sfx _sfx = null!;
    private RtsCamera _camera = null!;
    private Vector2 _screen;
    private string? _shots;
    private int _tent = -1, _billet = -1;

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private BuildingStore B => _sim.World.Buildings;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            ulong[] seeds = { 1, 6 };
            int at = Array.IndexOf(args, "--seed");
            if (at >= 0 && at + 1 < args.Length && ulong.TryParse(args[at + 1], out ulong one)) seeds = new[] { one };
            at = Array.IndexOf(args, "--shots");
            if (at >= 0 && at + 1 < args.Length) _shots = args[at + 1];
            GetTree().Root.Size = new Vector2I(1152, 648); // headless windows are 64 x 64
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            UiRows();
            foreach (ulong seed in seeds) await Run(seed);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(60)) GD.Print($"ATTACK ORDER VIEW TEST FAIL: {f}");
        if (_failures.Count > 60) GD.Print($"ATTACK ORDER VIEW TEST FAIL: ... {_failures.Count - 60} more");
        if (_failures.Count == 0) GD.Print("ATTACK ORDER VIEW TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    // states.ordered_attack is data: present in the shipped file, required by the loader, never a C# literal.
    private void UiRows()
    {
        Check(_ui.OrderedAttackText.Length > 0 && _ui.OrderedAttackText != _ui.StateText(UnitState.Attacking),
            $"ui.json states.ordered_attack '{_ui.OrderedAttackText}' (must be its own text)");
        string json = System.IO.File.ReadAllText(UiText.DefaultPath).Replace("\r\n", "\n");
        string line = $"    \"{UiText.OrderedAttackKey}\": \"{_ui.OrderedAttackText}\"";
        if (!Check(json.Contains(line), $"ui.json: line '{line}' not found")) return;
        var errors = new List<string>();
        string without = json.Replace(",\n" + line, "");
        Check(UiText.Parse(without, errors) == null && errors.Count == 1 && errors[0].Contains("states.ordered_attack"),
            $"ui.json without states.ordered_attack: {string.Join("; ", errors)}");
        GD.Print($"ui: states.ordered_attack = '{_ui.OrderedAttackText}', states.attacking = '{_ui.StateText(UnitState.Attacking)}'");
    }

    private void StartMatch(ulong seed)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--units", "0", "--no-bases", "--mute", "--debug-overlay", "--zoom", "30" }));
        _runner.ProcessMode = ProcessModeEnum.Disabled; // the test ticks the sim itself
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _panel = _match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        _overlay = _match.GetNode<DebugOverlay>("DebugOverlay");
        _ring = _match.GetNode<TargetRing>("World3D/TargetRing");
        _units = _match.GetNode<UnitViews>("World3D/UnitViews");
        _combat = _match.GetNode<CombatViews>("World3D/CombatViews");
        _mini = _match.GetNode<Minimap>("Hud/Minimap");
        _sfx = _match.GetNode<Sfx>("Sfx");
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

    // Every target a row clicks is staged in player 0's sight (AttackStage; an Attack on an unseen enemy is dropped, BUG-0218).
    private System.Numerics.Vector2 Stage() => AttackStage.Stage(_sim, _data, PerSide);

    private async Task Run(ulong seed)
    {
        StartMatch(seed);
        System.Numerics.Vector2 mid = Stage();
        _sim.Tick();
        _sim.Tick();
        for (int i = 0; i < B.Capacity; i++)
            if (B.Alive[i]) { if (B.Owner[i] == 0) _billet = i; else _tent = i; }
        Check(U.Count == 2 * PerSide && _tent >= 0 && _billet >= 0, $"seed {seed}: {U.Count} units, tent {_tent}, billet {_billet}");
        _camera.SetFocus(mid.X, mid.Y);
        await Frame();
        await Frame();

        await RightClickEnemy(seed);
        await AClickEnemy(seed);
        await AClickGround(seed);
        await ShiftQueue(seed);
        await RightClickBuilding(seed);
        await RightClickOwn(seed);
        await MinimapRightClick(seed);
        await PanelRows(seed);
        await RingRows(seed);
        await SteadyBytes(seed);

        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok && replay.Checkpoints.Length == replay.TickCount, $"seed {seed}: twin {twin.Error} at tick {twin.Tick} ({twin.ExpectedHash:x} vs {twin.ActualHash:x})");
        int attacks = replay.Commands.Count(c => c.Kind == CommandKind.Attack);
        GD.Print($"seed {seed}: hash twin {replay.Commands.Length} commands ({attacks} Attack), {replay.Checkpoints.Length} checkpoints equal");
        await EndMatch();
    }

    // ---- rows ----

    private async Task RightClickEnemy(ulong seed)
    {
        int n = SelectAllOwn();
        if (!Check(n > 0, $"seed {seed}: no own unit on screen to select")) return;
        if (!TryEnemyPixel(out EntityHandle enemy, out Vector2 px, exclude: default)) { Check(false, $"seed {seed}: no clean enemy pixel"); return; }
        int attacks = _sel.IssuedCount(CommandKind.Attack), sounds = _sfx.PlayCount(SfxEvent.Command);
        RightClick(px);
        Check(_sel.IssuedCount(CommandKind.Attack) == attacks + n, $"seed {seed} right-click enemy: {_sel.IssuedCount(CommandKind.Attack) - attacks} Attacks for {n} selected");
        Check(_sfx.PlayCount(SfxEvent.Command) == sounds + 1, $"seed {seed} right-click enemy: {_sfx.PlayCount(SfxEvent.Command) - sounds} Command sounds");
        Check(_ring.Mark.Active && _ring.Mark.Target == enemy && !_ring.Mark.IsBuilding, $"seed {seed} right-click enemy: ring mark {_ring.Mark.Target} active {_ring.Mark.Active}");
        await Shot($"attack-ring-seed{seed}");
        ApplyOrders();
        ExpectTarget(seed, "right-click enemy", enemy, building: false);
        await Frame();
        await Frame(); // process_frame fires before the nodes' _Process: the second one has seen the overlay run
        Check(_overlay.TargetSlot == enemy.Index && !_overlay.TargetIsBuilding && Label().Contains($"target u{enemy.Index}"),
            $"seed {seed} right-click enemy: F12 target {_overlay.TargetSlot}, label lacks 'target u{enemy.Index}'");
        GD.Print($"seed {seed}: right-click on enemy {enemy.Index}: {n} Attacks, all hold it ({OrderedOn(enemy)} ordered)");
        await Gap();
    }

    private async Task AClickEnemy(ulong seed)
    {
        int n = SelectAllOwn();
        if (!TryEnemyPixel(out EntityHandle enemy, out Vector2 px, exclude: _ring.Mark.Target)) { Check(false, $"seed {seed}: no second enemy pixel"); return; }
        int attacks = _sel.IssuedCount(CommandKind.Attack), moves = _sel.IssuedCount(CommandKind.AttackMove), sounds = _sfx.PlayCount(SfxEvent.Command);
        Key(Godot.Key.A);
        Check(_sel.Targeting && _sel.TargetKind == CommandKind.AttackMove, $"seed {seed}: A did not arm attack-move targeting");
        LeftClick(px);
        Check(!_sel.Targeting, $"seed {seed} A + click enemy: targeting still armed");
        Check(_sel.IssuedCount(CommandKind.Attack) == attacks + n && _sel.IssuedCount(CommandKind.AttackMove) == moves,
            $"seed {seed} A + click enemy: {_sel.IssuedCount(CommandKind.Attack) - attacks} Attacks, {_sel.IssuedCount(CommandKind.AttackMove) - moves} AttackMoves for {n}");
        Check(_sfx.PlayCount(SfxEvent.Command) == sounds + 1, $"seed {seed} A + click enemy: no Command sound");
        ApplyOrders();
        ExpectTarget(seed, "A + click enemy", enemy, building: false);
        GD.Print($"seed {seed}: A + click on enemy {enemy.Index}: {n} Attacks, all hold it");
        await Gap();
    }

    private async Task AClickGround(ulong seed)
    {
        int n = SelectAllOwn();
        if (!TryGroundPixel(out Vector2 px)) { Check(false, $"seed {seed}: no empty ground pixel"); return; }
        int attacks = _sel.IssuedCount(CommandKind.Attack), moves = _sel.IssuedCount(CommandKind.AttackMove);
        Key(Godot.Key.A);
        LeftClick(px);
        Check(_sel.IssuedCount(CommandKind.AttackMove) == moves + n && _sel.IssuedCount(CommandKind.Attack) == attacks,
            $"seed {seed} A + click ground: {_sel.IssuedCount(CommandKind.AttackMove) - moves} AttackMoves, {_sel.IssuedCount(CommandKind.Attack) - attacks} Attacks for {n}");
        ApplyOrders();
        int wrong = NotInMode(CombatMode.AttackMove);
        Check(wrong == 0, $"seed {seed} A + click ground: {wrong} selected units not in AttackMove mode");
        GD.Print($"seed {seed}: A + click on ground: {n} AttackMoves");
        await Gap();
    }

    // Walk everyone away (a Move far west), then Shift + right-click two enemies: both appended as Attack entries.
    private async Task ShiftQueue(ulong seed)
    {
        int n = SelectAllOwn();
        System.Numerics.Vector2 west = U.Position[_sel.Selection.Items[0].Index] - new System.Numerics.Vector2(30f, 0f);
        _sel.Order(CommandKind.Move, new Vector2(west.X, west.Y), queued: false);
        if (!TryEnemyPixel(out EntityHandle first, out Vector2 px1, exclude: default)) { Check(false, $"seed {seed}: no first queued enemy"); return; }
        if (!TryEnemyPixel(out EntityHandle second, out Vector2 px2, exclude: first)) { Check(false, $"seed {seed}: no second queued enemy"); return; }
        int attacks = _sel.IssuedCount(CommandKind.Attack);
        Input.ActionPress("order_queue");
        RightClick(px1);
        RightClick(px2);
        Input.ActionRelease("order_queue");
        Check(_sel.IssuedCount(CommandKind.Attack) == attacks + 2 * n, $"seed {seed} shift: {_sel.IssuedCount(CommandKind.Attack) - attacks} Attacks for 2 x {n}");
        ApplyOrders();
        int good = QueuedBoth(seed, first, second);
        GD.Print($"seed {seed}: Shift + right-click on {first.Index} then {second.Index}: {good} units queue both as Attack entries");
        await Gap();
    }

    private async Task RightClickBuilding(ulong seed)
    {
        int n = SelectAllOwn();
        if (!TryBuildingPixel(_tent, out Vector2 px)) { Check(false, $"seed {seed}: the Tent has no clean pixel"); return; }
        var tent = new EntityHandle(_tent, B.Generation[_tent]);
        int attacks = _sel.IssuedCount(CommandKind.Attack);
        RightClick(px);
        Check(_sel.IssuedCount(CommandKind.Attack) == attacks + n, $"seed {seed} right-click Tent: {_sel.IssuedCount(CommandKind.Attack) - attacks} Attacks for {n}");
        Check(_ring.Mark.Target == tent && _ring.Mark.IsBuilding, $"seed {seed} right-click Tent: ring on {_ring.Mark.Target} building {_ring.Mark.IsBuilding}");
        ApplyOrders();
        ExpectTarget(seed, "right-click Tent", tent, building: true);
        _ring.Sync(W, 1f, 0f);
        float r = TargetRing.BuildingScale * Math.Max(_data.Buildings[B.TypeId[_tent]].FootprintWidth, _data.Buildings[B.TypeId[_tent]].FootprintHeight) * MapConstants.CellSize / 2f;
        Check(_ring.Shown && Math.Abs(_ring.Ring.Transform.Basis.Scale.X - r) < 1e-3f, $"seed {seed} Tent ring: shown {_ring.Shown}, radius {_ring.Ring.Transform.Basis.Scale.X} want {r}");
        await Frame();
        await Frame();
        Check(Label().Contains($"target b{_tent}"), $"seed {seed} right-click Tent: F12 label lacks 'target b{_tent}'");
        // Own building: not a target (a right-click on it is the old context order, never an Attack).
        if (TryBuildingPixel(_billet, out Vector2 own))
        {
            int before = _sel.IssuedCount(CommandKind.Attack);
            RightClick(own);
            Check(_sel.IssuedCount(CommandKind.Attack) == before, $"seed {seed}: a right-click on the own Billet sent Attacks");
            GD.Print($"seed {seed}: right-click on the own Billet: no Attack");
        }
        GD.Print($"seed {seed}: right-click on the Tent: {n} building Attacks");
        await Gap();
    }

    private async Task RightClickOwn(ulong seed)
    {
        int n = SelectAllOwn();
        int target = -1;
        Vector2 px = default;
        for (int i = 0; i < U.Capacity && target < 0; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 0 || !_sel.TryScreenPosition(i, out px) || !InPlayArea(px)) continue;
            if (UnitUnder(px) == i) target = i;
        }
        if (!Check(target >= 0, $"seed {seed}: no clean own pixel")) return;
        int attacks = _sel.IssuedCount(CommandKind.Attack), moves = _sel.IssuedCount(CommandKind.Move);
        RightClick(px);
        Check(_sel.IssuedCount(CommandKind.Attack) == attacks && _sel.IssuedCount(CommandKind.Move) == moves + n,
            $"seed {seed} right-click own unit: {_sel.IssuedCount(CommandKind.Attack) - attacks} Attacks, {_sel.IssuedCount(CommandKind.Move) - moves} Moves for {n}");
        GD.Print($"seed {seed}: right-click on own unit {target}: {n} Moves");
        await Gap();
    }

    private async Task MinimapRightClick(ulong seed)
    {
        int n = SelectAllOwn();
        int enemy = -1;
        for (int i = 0; i < U.Capacity && enemy < 0; i++) if (U.Alive[i] && U.Owner[i] == 1) enemy = i;
        if (!Check(enemy >= 0, $"seed {seed}: no enemy for the minimap row")) return;
        System.Numerics.Vector2 dot = _mini.Fit.ToPixel(U.Position[enemy]);
        int attacks = _sel.IssuedCount(CommandKind.Attack), moves = _sel.IssuedCount(CommandKind.Move);
        _mini._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(dot.X, dot.Y) });
        Check(_sel.IssuedCount(CommandKind.Attack) == attacks && _sel.IssuedCount(CommandKind.Move) == moves + n,
            $"seed {seed} minimap right-click on an enemy dot: {_sel.IssuedCount(CommandKind.Attack) - attacks} Attacks, {_sel.IssuedCount(CommandKind.Move) - moves} Moves");
        ApplyOrders();
        await Gap();
    }

    // One unit selected, ordered onto the farthest enemy in its sight: the panel reads states.ordered_attack while it chases, "Attacking" once it swings.
    private async Task PanelRows(ulong seed)
    {
        EntityHandle me = default;
        for (int i = 0; i < U.Capacity && me == default; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && _sel.TryScreenPosition(i, out Vector2 p) && InPlayArea(p)) me = new EntityHandle(i, U.Generation[i]);
        if (!Check(me != default, $"seed {seed}: no unit for the panel row")) return;
        _sel.SelectOnly(me);
        // The farthest enemy still inside its sight (less 2 m): one outside is dropped as unseen under fog (M4-3a,
        // BUG-0218), and a chase that loses sight of its target without gaining gives it up (BUG-0137).
        EntityHandle far = default;
        float best = -1f, sight = _data.Units[U.TypeId[me.Index]].Sight - 2f;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 1) continue;
            float d = System.Numerics.Vector2.Distance(U.Position[i], U.Position[me.Index]);
            if (d > best && d <= sight) { best = d; far = new EntityHandle(i, U.Generation[i]); }
        }
        if (!Check(far != default && best > 3f, $"seed {seed}: no far enemy ({best:0.0} m)")) return;
        Check(_sel.AttackOrder(far, false, queued: false), $"seed {seed}: AttackOrder refused");
        ApplyOrders();
        _panel.Sync();
        bool chasing = U.Mode[me.Index] == CombatMode.Ordered && U.State[me.Index] == UnitState.Moving;
        Check(chasing && _panel.StateLabel.Text == _ui.OrderedAttackText,
            $"seed {seed} panel: mode {U.Mode[me.Index]} state {U.State[me.Index]}, text '{_panel.StateLabel.Text}', want '{_ui.OrderedAttackText}' while chasing");
        int ticks = 0;
        while (U.IsAlive(me) && U.IsAlive(far) && !(U.State[me.Index] == UnitState.Attacking && U.Mode[me.Index] == CombatMode.Ordered) && ticks < 600)
        {
            _sim.Tick();
            ticks++;
            _panel.Sync();
            if (U.Mode[me.Index] == CombatMode.Ordered && U.State[me.Index] != UnitState.Attacking && _panel.StateLabel.Text != _ui.OrderedAttackText)
                Check(false, $"seed {seed} panel tick {W.TickNumber}: chasing, text '{_panel.StateLabel.Text}'");
        }
        _panel.Sync();
        bool swinging = U.IsAlive(me) && U.State[me.Index] == UnitState.Attacking && U.Mode[me.Index] == CombatMode.Ordered;
        Check(swinging && _panel.StateLabel.Text == _ui.StateText(UnitState.Attacking),
            $"seed {seed} panel after {ticks} ticks: swinging {swinging}, text '{_panel.StateLabel.Text}', want '{_ui.StateText(UnitState.Attacking)}'");
        GD.Print($"seed {seed}: panel '{_ui.OrderedAttackText}' while chasing {best:0.0} m, '{_panel.StateLabel.Text}' after {ticks} ticks");
        await Gap();
    }

    // The ring, driven with a fixed step: one pooled node on the target, following it, gone after 0.5 s and when it dies.
    private async Task RingRows(ulong seed)
    {
        _ring.ProcessMode = ProcessModeEnum.Disabled;
        Check(_ring.NodesMade == 1 && _ring.GetChildCount() == 1, $"seed {seed}: ring nodes {_ring.NodesMade}, children {_ring.GetChildCount()}");
        EntityHandle enemy = default;
        for (int i = 0; i < U.Capacity && enemy == default; i++) if (U.Alive[i] && U.Owner[i] == 1) enemy = new EntityHandle(i, U.Generation[i]);
        if (!Check(enemy != default, $"seed {seed}: no enemy for the ring row")) return;
        _ring.Show(enemy, false);
        float t = 0f;
        for (int f = 0; f < 9; f++)
        {
            _ring.Sync(W, 0.5f, f == 0 ? 0f : 0.05f);
            t += f == 0 ? 0f : 0.05f;
            Vector3 g = UnitViews.GroundPoint(W, enemy.Index, 0.5f);
            Vector3 o = _ring.Ring.Transform.Origin;
            Check(_ring.Shown && Math.Abs(o.X - g.X) < 1e-4f && Math.Abs(o.Z - g.Z) < 1e-4f && Math.Abs(o.Y - g.Y - TargetRing.Lift) < 1e-4f,
                $"seed {seed} ring at {t:0.00} s: shown {_ring.Shown} at {o}, unit at {g}");
            _sim.Tick();
        }
        _ring.Sync(W, 0.5f, 0.06f); // 0.46 s
        Check(_ring.Shown, $"seed {seed}: ring gone before 0.5 s");
        _ring.Sync(W, 0.5f, 0.05f); // 0.51 s
        Check(!_ring.Shown, $"seed {seed}: ring still shown after 0.5 s");
        // A dead or recycled target hides it at once.
        _ring.Show(new EntityHandle(enemy.Index, enemy.Generation + 1), false);
        _ring.Sync(W, 0.5f, 0.01f);
        Check(!_ring.Shown && !_ring.Mark.Active, $"seed {seed}: ring shown on a recycled handle");
        _ring.Show(new EntityHandle(_tent, B.Generation[_tent] + 1), true);
        _ring.Sync(W, 0.5f, 0.01f);
        Check(!_ring.Shown, $"seed {seed}: ring shown on a recycled building handle");
        Check(_ring.NodesMade == 1 && _ring.GetChildCount() == 1, $"seed {seed}: ring made {_ring.NodesMade} nodes");
        GD.Print($"seed {seed}: ring on unit {enemy.Index} followed it for 0.45 s, gone at 0.51 s; 1 node");
        await Frame();
    }

    // 300 frames of the running brawl with a ring up: ring, panel, unit and combat views allocate nothing.
    private async Task SteadyBytes(ulong seed)
    {
        // Everyone attack-moves into everyone (through commands, so the twin sees it).
        System.Numerics.Vector2 mid = default;
        int alive = 0;
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i]) { mid += U.Position[i]; alive++; }
        mid /= Math.Max(1, alive);
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i]) _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), mid));
        EntityHandle me = default;
        for (int i = 0; i < U.Capacity && me == default; i++) if (U.Alive[i] && U.Owner[i] == 0) me = new EntityHandle(i, U.Generation[i]);
        if (me != default) _sel.SelectOnly(me);
        for (int k = 0; k < 30; k++) _sim.Tick();
        long bytes = 0;
        int frames = 0, shown = 0;
        for (int f = 0; f < 300; f++)
        {
            if (f % 5 == 0) _sim.Tick();
            if (!_ring.Mark.Active)
                for (int i = 0; i < U.Capacity; i++)
                    if (U.Alive[i] && U.Owner[i] == 1) { _ring.Show(new EntityHandle(i, U.Generation[i]), false); break; }
            long before = GC.GetAllocatedBytesForCurrentThread();
            _ring.Sync(W, f % 5 / 5f, 0.016f);
            _panel.Sync();
            _units.Sync(W, f % 5 / 5f, 0.016f);
            _combat.Sync(W, f % 5 / 5f);
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            frames++;
            if (_ring.Shown) shown++;
        }
        Check(bytes == 0 && shown > 200, $"seed {seed}: {frames} steady frames ({shown} with the ring) allocated {bytes} bytes");
        GD.Print($"seed {seed}: {frames} steady brawl frames, ring shown {shown}: {bytes} bytes");
        await Frame();
    }

    // ---- helpers ----

    // Ticks until every enqueued command has applied: an order given between ticks T-1 and T applies at the start of tick
    // T + 1 (Command.Tick = TickNumber + 1), so two ticks; returns them.
    private int ApplyOrders()
    {
        int n = 0;
        while (_sim.PendingCommandCount > 0 && n < 4)
        {
            _sim.Tick();
            n++;
        }
        Check(_sim.PendingCommandCount == 0, $"commands still pending after {n} ticks");
        return n;
    }

    private int NotInMode(CombatMode mode)
    {
        int wrong = 0;
        foreach (EntityHandle h in _sel.Selection.Items)
            if (U.IsAlive(h) && U.Mode[h.Index] != mode) wrong++;
        return wrong;
    }

    // Every live selected unit's queue is exactly [Attack first, Attack second] (unit targets); returns how many.
    private int QueuedBoth(ulong seed, EntityHandle first, EntityHandle second)
    {
        int good = 0, n = 0;
        foreach (EntityHandle h in _sel.Selection.Items)
        {
            if (!U.IsAlive(h)) continue;
            n++;
            int i = h.Index, q = i * OrderConstants.QueueCapacity;
            bool ok = U.QueueCount[i] == 2 && U.QueueKind[q] == CommandKind.Attack && U.QueuedTarget(q) == first && U.QueueTypeId[q] == 0
                && U.QueueKind[q + 1] == CommandKind.Attack && U.QueuedTarget(q + 1) == second && U.QueueTypeId[q + 1] == 0;
            if (ok) good++;
            else Check(false, $"seed {seed} shift: unit {i} queue {U.QueueCount[i]} [{U.QueueKind[q]} {U.QueuedTarget(q)}, {U.QueueKind[q + 1]} {U.QueuedTarget(q + 1)}], want Attack {first}, Attack {second}");
        }
        Check(n > 0 && good == n, $"seed {seed} shift: {good} of {n} queues hold both Attacks");
        return good;
    }

    private void ExpectTarget(ulong seed, string row, EntityHandle target, bool building)
    {
        int bad = 0, n = 0;
        foreach (EntityHandle h in _sel.Selection.Items)
        {
            if (!U.IsAlive(h)) continue;
            n++;
            int i = h.Index;
            if (U.Target[i] != target || U.TargetIsBuilding[i] != building || U.Mode[i] != CombatMode.Ordered)
            {
                bad++;
                if (bad <= 3) Check(false, $"seed {seed} {row}: unit {i} target {U.Target[i]} building {U.TargetIsBuilding[i]} mode {U.Mode[i]}, want {target} building {building} Ordered");
            }
        }
        Check(n > 0 && bad == 0, $"seed {seed} {row}: {bad} of {n} selected units don't hold the target");
    }

    private int OrderedOn(EntityHandle target)
    {
        int n = 0;
        foreach (EntityHandle h in _sel.Selection.Items) if (U.IsAlive(h) && U.Target[h.Index] == target && U.Mode[h.Index] == CombatMode.Ordered) n++;
        return n;
    }

    // Selects every own unit (any selection order); returns the count.
    private int SelectAllOwn()
    {
        int n = _sel.BoxSelect(Vector2.Zero, _screen, add: false);
        if (n == 0)
            for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.Owner[i] == 0) { _sel.SelectOnly(new EntityHandle(i, U.Generation[i])); return 1; }
        return n;
    }

    // Away from the HUD panels (minimap bottom-left, panel and card along the bottom, resource bar top right).
    private bool InPlayArea(Vector2 p) => p.X > 30 && p.X < _screen.X - 30 && p.Y > 60 && p.Y < _screen.Y - 270;

    // The live unit whose body the ray at the pixel meets first (the ViewApi pick, independent of the controller).
    private int UnitUnder(Vector2 px)
    {
        Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
        return UnitPicker.PickRay(U.Alive, U.PrevPosition, U.Position, U.Radius, W.Heightmap, (float)_runner.Alpha, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), UnitViews.ExtraBodyHeight, out _);
    }

    // An enemy unit (not `exclude`) whose body centre pixel is in the play area and shows that unit first.
    private bool TryEnemyPixel(out EntityHandle enemy, out Vector2 px, EntityHandle exclude)
    {
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 1 || (exclude.Index == i && exclude.Generation == U.Generation[i])) continue;
            if (!_sel.TryScreenPosition(i, out px) || !InPlayArea(px) || UnitUnder(px) != i) continue;
            if (!_sel.EnemyAt(px, out enemy, out bool b) || b || enemy.Index != i) continue;
            return true;
        }
        enemy = default;
        px = default;
        return false;
    }

    // A pixel on the building's box (mid height over its footprint centre) where no unit stands in front.
    private bool TryBuildingPixel(int slot, out Vector2 px)
    {
        BuildingDef def = _data.Buildings[B.TypeId[slot]];
        System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, B.Cell[slot]);
        float y = TerrainHeight.At(W.Heightmap, c.X, c.Y);
        float hw = def.FootprintWidth * MapConstants.CellSize * 0.35f;
        foreach (float dx in new[] { 0f, -hw, hw })
            foreach (float h in new[] { 1.5f, 2.5f, 0.8f })
            {
                px = _camera.UnprojectPosition(new Vector3(c.X + dx, y + h, c.Y));
                if (!InPlayArea(px) || UnitUnder(px) >= 0) continue;
                if (_sel.ContextTarget(px, out _, out int hit) && hit == slot) return true;
            }
        px = default;
        return false;
    }

    // An empty ground pixel near the screen centre (no unit or building under it).
    private bool TryGroundPixel(out Vector2 px)
    {
        for (int k = 0; k < 200; k++)
        {
            px = new Vector2(_screen.X / 2 + (k % 20 - 10) * 18f, _screen.Y / 3 + (k / 20 - 5) * 14f);
            if (!InPlayArea(px) || UnitUnder(px) >= 0) continue;
            if (_sel.ContextTarget(px, out _, out int b) && b < 0) return true;
        }
        px = default;
        return false;
    }

    // Windowed only: the frame with the F12 layers off, saved as <name>.png in --shots.
    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        _overlay.SetEnabled(false);
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        _overlay.SetEnabled(true);
        GD.Print($"screenshot {path}");
    }

    private string Label() => _match.GetNode<Label>("DebugOverlay/Label").Text;

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private void Key(Key key)
    {
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void LeftClick(Vector2 at)
    {
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private void RightClick(Vector2 at)
    {
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    // The Command sound has a 50 ms gap (Sfx.MinGapMs): wait past it so the next row's sound counts.
    private async Task Gap()
    {
        await ToSignal(GetTree().CreateTimer(0.07), SceneTreeTimer.SignalName.Timeout);
        await Frame();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
