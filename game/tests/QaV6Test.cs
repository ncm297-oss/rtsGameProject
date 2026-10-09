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
/// QA M4-V2 (2026-10-08-0913) on the real Match scene, the attacks the developer's <c>AttackOrderViewTest</c> doesn't make.
/// Match 1 (no staging writes, so the replay twin must hold): (1) 50 right-clicks on one enemy over one second of ticks with
/// ten selected: exactly 10 Attacks per click, the controller's click path (pick + order + ring) allocates 0 bytes per click,
/// the command queue never overflows; (2) Shift + right-click three enemies, then S: every queue empty, no target, no mode,
/// the panel back to the plain state; (3) A + click on an own unit is an attack-move, never an Attack; the hash twin.
/// Match 2 (staged): (4) the clicked enemy vanishes before the order applies, its slot re-used by a new enemy far away:
/// nobody targets the new unit, the ring hides, the F12 line drops to "target -", no exception; (5) a right-click on a
/// fresh corpse is a Move; (6) an own unit standing in front of an enemy hides it (Move, no Attack); (7) an enemy unit in
/// front of the enemy Tent is the target where it covers the box, the Tent elsewhere on the box.
/// Windowed with <c>-- --shots &lt;dir&gt;</c>: the F12 overlay with a target at 1280 x 720 and 1920 x 1080.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaV6Test.tscn</c>; prints "QA M4-V2 TEST PASS".</remarks>
public partial class QaV6Test : Node
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
    private RtsCamera _camera = null!;
    private Vector2 _screen;
    private string? _shots;
    private int _tent = -1, _hi, _raider;

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
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            _hi = _data.FindUnit("malazan_heavy_infantry");
            _raider = _data.FindUnit("whirlwind_raider");
            if (_shots != null && DisplayServer.GetName() != "headless") await Shots();
            else
            {
                await MatchOne(1);
                await MatchOne(6);
                await MatchTwo(1);
            }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(60)) GD.Print($"QA M4-V2 TEST FAIL: {f}");
        if (_failures.Count > 60) GD.Print($"QA M4-V2 TEST FAIL: ... {_failures.Count - 60} more");
        if (_failures.Count == 0) GD.Print("QA M4-V2 TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private void StartMatch(ulong seed)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--units", "0", "--no-bases", "--mute", "--debug-overlay", "--zoom", "30" }));
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _panel = _match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        _overlay = _match.GetNode<DebugOverlay>("DebugOverlay");
        _ring = _match.GetNode<TargetRing>("World3D/TargetRing");
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

    // AttackOrderViewTest's staging (AttackStage): 10 v 10 within player 0's sight, the Tent behind the Raiders with
    // player 0's Billet beside it as the spotter (an Attack on an unseen enemy is dropped since M4-3a, BUG-0218).
    private System.Numerics.Vector2 Stage()
    {
        System.Numerics.Vector2 mid = AttackStage.Stage(_sim, _data, PerSide);
        _sim.Tick();
        _sim.Tick();
        for (int i = 0; i < B.Capacity; i++) if (B.Alive[i] && B.Owner[i] == 1) _tent = i;
        return mid;
    }

    // ---- match 1: spam, queue + Stop, A + click own, twin ----

    private async Task MatchOne(ulong seed)
    {
        StartMatch(seed);
        System.Numerics.Vector2 mid = Stage();
        _camera.SetFocus(mid.X, mid.Y);
        await Frame();
        await Frame();
        await Spam(seed);
        await QueueThenStop(seed);
        await AClickOwn(seed);
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok && replay.Checkpoints.Length == replay.TickCount, $"seed {seed}: twin {twin.Error} at tick {twin.Tick}");
        GD.Print($"seed {seed}: hash twin {replay.Commands.Length} commands ({replay.Commands.Count(c => c.Kind == CommandKind.Attack)} Attack), {replay.Checkpoints.Length} checkpoints equal");
        await EndMatch();
    }

    // 50 clicks on one enemy over 20 ticks (2-3 clicks a tick), ten selected; the click path's bytes measured per click.
    private async Task Spam(ulong seed)
    {
        int n = SelectAllOwn();
        if (!TryEnemyPixel(out EntityHandle enemy, out Vector2 px, default)) { Check(false, $"seed {seed} spam: no enemy pixel"); return; }
        // Warm-up click (JIT, first-use caches), not measured.
        _sel.CommandAt(px, false);
        int attacks = _sel.IssuedCount(CommandKind.Attack), dropped = _sel.DroppedOrders;
        long bytes = 0, worst = 0;
        int clicks = 0, landed = 0, maxPending = 0;
        for (int tick = 0; tick < 20; tick++)
        {
            int perTick = tick % 2 == 0 ? 3 : 2;
            for (int c = 0; c < perTick && clicks < 50; c++, clicks++)
            {
                if (!_sel.TryScreenPosition(enemy.Index, out px) || !_sel.EnemyAt(px, out EntityHandle e, out _) || e != enemy) continue;
                long before = GC.GetAllocatedBytesForCurrentThread();
                bool ok = _sel.CommandAt(px, false);
                long b = GC.GetAllocatedBytesForCurrentThread() - before;
                bytes += b;
                worst = Math.Max(worst, b);
                if (ok) landed++;
                maxPending = Math.Max(maxPending, _sim.PendingCommandCount);
            }
            _sim.Tick();
            await Frame();
        }
        int sent = _sel.IssuedCount(CommandKind.Attack) - attacks;
        Check(landed >= 40, $"seed {seed} spam: only {landed} of 50 clicks found the enemy under the cursor");
        Check(sent == landed * n, $"seed {seed} spam: {sent} Attacks for {landed} clicks x {n} selected");
        Check(bytes == 0, $"seed {seed} spam: the click path allocated {bytes} bytes over {landed} clicks (worst {worst})");
        Check(_sel.DroppedOrders == dropped && maxPending <= W.Config.CommandCapacity, $"seed {seed} spam: {_sel.DroppedOrders - dropped} orders dropped, pending peak {maxPending}");
        Check(_ring.Mark.Target == enemy, $"seed {seed} spam: ring on {_ring.Mark.Target}, want {enemy}");
        GD.Print($"seed {seed}: spam {landed} clicks on enemy {enemy.Index} x {n} selected = {sent} Attacks, {bytes} bytes, pending peak {maxPending}");
        while (_sim.PendingCommandCount > 0) _sim.Tick();
        await Gap();
    }

    private async Task QueueThenStop(ulong seed)
    {
        int n = SelectAllOwn();
        System.Numerics.Vector2 west = U.Position[_sel.Selection.Items[0].Index] - new System.Numerics.Vector2(30f, 0f);
        _sel.Order(CommandKind.Move, new Vector2(west.X, west.Y), queued: false);
        var picked = new List<(EntityHandle h, Vector2 px)>();
        EntityHandle last = default;
        for (int k = 0; k < 3; k++)
        {
            if (!TryEnemyPixel(out EntityHandle e, out Vector2 p, last, picked.Select(x => x.h).ToArray())) break;
            picked.Add((e, p));
            last = e;
        }
        if (!Check(picked.Count == 3, $"seed {seed} queue: only {picked.Count} distinct enemy pixels")) return;
        int attacks = _sel.IssuedCount(CommandKind.Attack);
        Input.ActionPress("order_queue");
        foreach ((_, Vector2 p) in picked) RightClick(p);
        Input.ActionRelease("order_queue");
        Check(_sel.IssuedCount(CommandKind.Attack) == attacks + 3 * n, $"seed {seed} queue: {_sel.IssuedCount(CommandKind.Attack) - attacks} Attacks for 3 x {n}");
        ApplyOrders();
        int three = CountSelected(i => U.QueueCount[i] == 3);
        Check(three > 0, $"seed {seed} queue: no unit queued all three Attacks behind its Move");
        Key(Godot.Key.S);
        ApplyOrders();
        int bad = CountSelected(i => U.QueueCount[i] != 0 || U.Target[i] != default || U.Mode[i] != CombatMode.None);
        int queues = CountSelected(i => U.QueueCount[i] != 0);
        // An Idle unit scans: one with an enemy in sight may take a target by itself right after Stop (not the order's).
        int ordered = CountSelected(i => U.Mode[i] == CombatMode.Ordered);
        int onQueued = CountSelected(i => picked.Any(p => p.h == U.Target[i]) && U.Mode[i] == CombatMode.Ordered);
        if (bad > 0)
            foreach (int i in SelectedSlots())
                if (U.QueueCount[i] != 0 || U.Target[i] != default || U.Mode[i] != CombatMode.None)
                    GD.Print($"seed {seed} after Stop: unit {i} queue {U.QueueCount[i]} target {U.Target[i]} mode {U.Mode[i]} state {U.State[i]} (enemy {(U.Target[i] != default && U.IsAlive(U.Target[i]) ? System.Numerics.Vector2.Distance(U.Position[i], U.Position[U.Target[i].Index]).ToString("0.0") + " m" : "-")})");
        Check(queues == 0 && ordered == 0 && onQueued == 0, $"seed {seed} queue: after Stop {queues} queues left, {ordered} units still Ordered, {onQueued} on a queued target");
        _sel.SelectOnly(_sel.Selection.Items[0]);
        _panel.Sync();
        Check(_panel.StateLabel.Text != _ui.OrderedAttackText, $"seed {seed} queue: panel still reads '{_panel.StateLabel.Text}' after Stop");
        await Frame();
        await Frame();
        EntityHandle shown = U.Target[_sel.Selection.Items[0].Index];
        string want = shown == default ? "target -" : $"target {(U.TargetIsBuilding[_sel.Selection.Items[0].Index] ? "b" : "u")}{shown.Index}";
        Check(Label().Contains(want), $"seed {seed} queue: F12 lacks '{want}' after Stop");
        GD.Print($"seed {seed}: Shift x 3 then S: {three} units held three Attacks; after Stop all clear, panel '{_panel.StateLabel.Text}'");
        await Gap();
    }

    private async Task AClickOwn(ulong seed)
    {
        int n = SelectAllOwn();
        int own = -1;
        Vector2 px = default;
        for (int i = 0; i < U.Capacity && own < 0; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && _sel.TryScreenPosition(i, out px) && InPlayArea(px) && UnitUnder(px) == i) own = i;
        if (!Check(own >= 0, $"seed {seed}: no own pixel")) return;
        int attacks = _sel.IssuedCount(CommandKind.Attack), am = _sel.IssuedCount(CommandKind.AttackMove);
        Key(Godot.Key.A);
        LeftClick(px);
        Check(_sel.IssuedCount(CommandKind.Attack) == attacks && _sel.IssuedCount(CommandKind.AttackMove) == am + n,
            $"seed {seed} A + click own: {_sel.IssuedCount(CommandKind.Attack) - attacks} Attacks, {_sel.IssuedCount(CommandKind.AttackMove) - am} AttackMoves");
        ApplyOrders();
        await Gap();
    }

    // ---- match 2: staged rows ----

    private async Task MatchTwo(ulong seed)
    {
        StartMatch(seed);
        System.Numerics.Vector2 mid = Stage();
        _camera.SetFocus(mid.X, mid.Y);
        await Frame();
        await Frame();
        await VanishedTarget(seed);
        await CorpseClick(seed);
        await OwnInFront(seed, mid);
        await UnitBeforeTent(seed);
        await EndMatch();
    }

    private async Task VanishedTarget(ulong seed)
    {
        int n = SelectAllOwn();
        if (!TryEnemyPixel(out EntityHandle enemy, out Vector2 px, default)) { Check(false, "vanish: no enemy pixel"); return; }
        RightClick(px);
        Check(_ring.Mark.Target == enemy, "vanish: ring not on the clicked enemy");
        // Between the click and the tick that applies it: the enemy is gone, its slot re-used by a new enemy 40 m away.
        U.Free(enemy);
        int farCell = FlowField.NearestPassable(G, G.Height / 2 * G.Width + G.Width / 2 + 40);
        _sim.Enqueue(Command.SpawnUnit(1, _raider, G.CellCenter(farCell % G.Width, farCell / G.Width)));
        ApplyOrders();
        var reborn = new EntityHandle(enemy.Index, U.Generation[enemy.Index]);
        Check(U.IsAlive(reborn) && reborn != enemy, $"vanish: slot {enemy.Index} not re-used (gen {U.Generation[enemy.Index]})");
        int onOld = CountSelected(i => U.Target[i] == enemy), onNew = CountSelected(i => U.Target[i] == reborn);
        Check(onOld == 0 && onNew == 0, $"vanish: {onOld} units hold the dead handle, {onNew} the new unit in its slot");
        _ring.Sync(W, 1f, 0.01f);
        Check(!_ring.Shown, "vanish: the ring is drawn on the re-used slot");
        for (int k = 0; k < 6; k++) { _sim.Tick(); await Frame(); }
        Check(!Label().Contains($"target u{enemy.Index}") || onNew > 0, $"vanish: F12 still names slot {enemy.Index}: {Label()}");
        GD.Print($"vanish: enemy {enemy} clicked then freed, slot re-used as {reborn}: {n} selected, none hold either; ring hidden");
        await Gap();
    }

    private async Task CorpseClick(ulong seed)
    {
        int n = SelectAllOwn();
        if (!TryEnemyPixel(out EntityHandle enemy, out Vector2 px, default)) { Check(false, "corpse: no enemy pixel"); return; }
        U.Hp[enemy.Index] = 1; // staging: one hit kills it
        RightClick(px);
        int ticks = 0;
        while (U.IsAlive(enemy) && ticks < 600) { _sim.Tick(); ticks++; }
        if (!Check(!U.IsAlive(enemy), $"corpse: the 1-hp enemy survived {ticks} ticks")) return;
        System.Numerics.Vector2 death = default;
        foreach (DeathEvent d in W.Deaths.ToArray()) if (d.Victim == enemy) death = d.Position;
        // Every unit away from the corpse first, so the pixel shows only the disc and the ground.
        SelectAllOwn();
        _sel.Order(CommandKind.Move, new Vector2(death.X - 25f, death.Y), false);
        for (int k = 0; k < 2; k++) _sim.Tick();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 1) _sim.Enqueue(Command.Move(1, new EntityHandle(i, U.Generation[i]), death + new System.Numerics.Vector2(25f, 0f)));
        for (int k = 0; k < 160; k++) _sim.Tick();
        await Frame();
        await Frame();
        Vector2 at = _camera.UnprojectPosition(new Vector3(death.X, TerrainHeight.At(W.Heightmap, death.X, death.Y) + 0.05f, death.Y));
        if (!Check(InPlayArea(at) && UnitUnder(at) < 0, $"corpse: pixel {at} off screen or a unit stands on it ({UnitUnder(at)})")) return;
        n = SelectAllOwn();
        int attacks = _sel.IssuedCount(CommandKind.Attack), moves = _sel.IssuedCount(CommandKind.Move);
        RightClick(at);
        Check(_sel.IssuedCount(CommandKind.Attack) == attacks && _sel.IssuedCount(CommandKind.Move) == moves + n,
            $"corpse: right-click on the corpse sent {_sel.IssuedCount(CommandKind.Attack) - attacks} Attacks, {_sel.IssuedCount(CommandKind.Move) - moves} Moves for {n}");
        GD.Print($"corpse: right-click on enemy {enemy.Index}'s corpse (died after {ticks} ticks): {n} Moves, 0 Attacks");
        ApplyOrders();
        await Gap();
    }

    // An own unit and an enemy staged 1.2 m apart on the camera's line, far from the rest; both orders tried.
    private async Task OwnInFront(ulong seed, System.Numerics.Vector2 mid)
    {
        int exercised = 0;
        foreach (float dz in new[] { 1.2f, -1.2f })
        {
            int cell = FlowField.NearestPassable(G, (G.Height / 2 + 12) * G.Width + G.Width / 2 + (dz > 0 ? -8 : 8));
            System.Numerics.Vector2 e = G.CellCenter(cell % G.Width, cell / G.Width);
            var alive = (bool[])U.Alive.Clone();
            _sim.Enqueue(Command.SpawnUnit(1, _raider, e));
            _sim.Enqueue(Command.SpawnUnit(0, _hi, e + new System.Numerics.Vector2(0f, dz)));
            _sim.Tick();
            _sim.Tick();
            int enemy = -1, own = -1;
            for (int i = 0; i < U.Capacity; i++)
                if (U.Alive[i] && !alive[i]) { if (U.Owner[i] == 1) enemy = i; else own = i; }
            if (!Check(enemy >= 0 && own >= 0, "own-in-front: spawn failed")) continue;
            _camera.SetFocus(e.X, e.Y);
            await Frame();
            await Frame();
            // Scan the own unit's screen footprint for pixels where its body is first and the enemy's capsule lies behind on the same ray.
            if (!_sel.TryScreenPosition(own, out Vector2 c)) continue;
            int tried = 0, attacked = 0;
            for (int k = 0; k < 120; k++)
            {
                Vector2 p = c + new Vector2(k % 12 - 6, k / 12 * 3 - 15);
                if (!InPlayArea(p) || UnitUnder(p) != own || !BehindOnRay(p, enemy)) continue;
                tried++;
                SelectAllOwnExcept(own);
                int before = _sel.IssuedCount(CommandKind.Attack);
                _sel.CommandAt(p, false);
                if (_sel.IssuedCount(CommandKind.Attack) != before) attacked++;
            }
            if (tried > 0) exercised++;
            Check(attacked == 0, $"own-in-front dz {dz}: {attacked} of {tried} clicks through the own body attacked the enemy behind it");
            GD.Print($"own-in-front dz {dz}: {tried} pixels with the enemy behind the own body: {attacked} Attacks");
            while (_sim.PendingCommandCount > 0) _sim.Tick();
        }
        Check(exercised > 0, "own-in-front: no camera geometry put the enemy behind the own unit");
        _camera.SetFocus(mid.X, mid.Y);
        await Frame();
        await Gap();
    }

    private async Task UnitBeforeTent(ulong seed)
    {
        if (!Check(_tent >= 0 && B.Alive[_tent], "tent row: no Tent")) return;
        BuildingDef def = _data.Buildings[B.TypeId[_tent]];
        System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, B.Cell[_tent]);
        float halfH = def.FootprintHeight * MapConstants.CellSize / 2f;
        _camera.SetFocus(c.X, c.Y);
        await Frame();
        await Frame();
        // Which side faces the camera: the side whose ground point projects lower on screen.
        Vector2 north = _camera.UnprojectPosition(new Vector3(c.X, 0f, c.Y - halfH)), south = _camera.UnprojectPosition(new Vector3(c.X, 0f, c.Y + halfH));
        float side = south.Y > north.Y ? 1f : -1f;
        var alive = (bool[])U.Alive.Clone();
        // BUG-0226: close to the box (its radius plus 0.15 m off the footprint) so rays through its upper body go on into the
        // box; 0.7 m out, the ray through its pixel met the ground first and the box was never behind it.
        float gap = _data.Units[_raider].Radius + 0.15f;
        _sim.Enqueue(Command.SpawnUnit(1, _raider, c + new System.Numerics.Vector2(0f, side * (halfH + gap))));
        _sim.Tick();
        _sim.Tick();
        int front = -1;
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && !alive[i]) front = i;
        await Frame();
        await Frame();
        Vector2 px = default;
        if (!Check(front >= 0 && _sel.TryScreenPosition(front, out px), "tent row: no front unit")) return;
        // A pixel on the Raider whose ray also enters the Tent box behind it (BUG-0226): from the top of its body down.
        int b = -1;
        System.Numerics.Vector2 fp = U.Position[front];
        float ground = TerrainHeight.At(W.Heightmap, fp.X, fp.Y), r = U.Radius[front];
        for (float h = UnitViews.BodyHeight(r) - 0.05f; h > 0.2f && b != _tent; h -= 0.1f)
            foreach (float dx in new[] { 0f, -0.5f * r, 0.5f * r })
            {
                Vector2 q = _camera.UnprojectPosition(new Vector3(fp.X + dx, ground + h, fp.Y));
                if (UnitUnder(q) != front) continue;
                Vector3 qo = _camera.ProjectRayOrigin(q), qd = _camera.ProjectRayNormal(q);
                if (BuildingPicker.PickRay(B, _data.Buildings, G, W.Heightmap, -1, new(qo.X, qo.Y, qo.Z), new(qd.X, qd.Y, qd.Z), BuildingViews.BoxHeight, BuildingViews.SiteMinHeight, out _) != _tent) continue;
                b = _tent;
                px = q;
                break;
            }
        Check(b == _tent, "tent row: no pixel on the Raider has the Tent box behind it (the row would not test what it says)");
        bool onUnit = _sel.EnemyAt(px, out EntityHandle t, out bool isB);
        Check(onUnit && !isB && t.Index == front, $"tent row: the Raider before the Tent (box behind it: {b == _tent}) resolved to {t} building {isB}");
        // A pixel on the box's top centre (no unit there): the Tent.
        Vector2 top = _camera.UnprojectPosition(new Vector3(c.X - halfH * 0.6f, TerrainHeight.At(W.Heightmap, c.X, c.Y) + BuildingViews.BoxHeight - 0.2f, c.Y));
        bool onTent = UnitUnder(top) < 0 && _sel.EnemyAt(top, out t, out isB) && isB && t.Index == _tent;
        Check(onTent, $"tent row: the Tent's top at {top} resolved to {t} building {isB} (unit under {UnitUnder(top)})");
        GD.Print($"tent row: Raider {front} before the Tent (box behind: {b == _tent}) -> unit; box top -> Tent {onTent}");
        await Frame();
    }

    // ---- windowed: F12 at two window sizes ----

    private async Task Shots()
    {
        foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
        {
            DisplayServer.WindowSetSize(size);
            GetTree().Root.Size = size;
            StartMatch(1);
            System.Numerics.Vector2 mid = Stage();
            _camera.SetFocus(mid.X, mid.Y);
            await Frame();
            await Frame();
            int n = SelectAllOwn();
            if (TryEnemyPixel(out EntityHandle enemy, out Vector2 px, default)) RightClick(px);
            ApplyOrders();
            _overlay.SetEnabled(true);
            for (int i = 0; i < 6; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string path = $"{_shots}/qa-v6-f12-{size.X}x{size.Y}.png";
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"screenshot {path} ({n} selected, target {enemy}); label:\n{Label()}");
            await EndMatch();
        }
    }

    // ---- helpers ----

    private List<int> SelectedSlots()
    {
        var list = new List<int>();
        foreach (EntityHandle h in _sel.Selection.Items) if (U.IsAlive(h)) list.Add(h.Index);
        return list;
    }

    // Live selected units (slots) matching the rule.
    private int CountSelected(Func<int, bool> rule)
    {
        int n = 0;
        foreach (EntityHandle h in _sel.Selection.Items) if (U.IsAlive(h) && rule(h.Index)) n++;
        return n;
    }

    private void ApplyOrders()
    {
        int n = 0;
        while (_sim.PendingCommandCount > 0 && n < 4) { _sim.Tick(); n++; }
        Check(_sim.PendingCommandCount == 0, $"commands still pending after {n} ticks");
    }

    private int SelectAllOwn()
    {
        int n = _sel.BoxSelect(Vector2.Zero, _screen, add: false);
        if (n == 0)
            for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.Owner[i] == 0) { _sel.SelectOnly(new EntityHandle(i, U.Generation[i])); return 1; }
        return n;
    }

    private void SelectAllOwnExcept(int slot)
    {
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && i != slot) { _sel.SelectOnly(new EntityHandle(i, U.Generation[i])); return; }
    }

    private bool InPlayArea(Vector2 p) => p.X > 30 && p.X < _screen.X - 30 && p.Y > 60 && p.Y < _screen.Y - 270;

    private int UnitUnder(Vector2 px)
    {
        Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
        return UnitPicker.PickRay(U.Alive, U.PrevPosition, U.Position, U.Radius, W.Heightmap, (float)_runner.Alpha, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), UnitViews.ExtraBodyHeight, out _);
    }

    // True when the ray at the pixel also passes through unit `slot`'s drawn capsule (somewhere along it).
    private bool BehindOnRay(Vector2 px, int slot)
    {
        Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
        System.Numerics.Vector2 p = System.Numerics.Vector2.Lerp(U.PrevPosition[slot], U.Position[slot], Math.Clamp((float)_runner.Alpha, 0f, 1f));
        float r = U.Radius[slot], y = TerrainHeight.At(W.Heightmap, p.X, p.Y);
        return UnitPicker.Capsule(new(o.X, o.Y, o.Z), System.Numerics.Vector3.Normalize(new(d.X, d.Y, d.Z)), new(p.X, y + r, p.Y), new(p.X, y + r + UnitViews.ExtraBodyHeight, p.Y), r) >= 0f;
    }

    private bool TryEnemyPixel(out EntityHandle enemy, out Vector2 px, EntityHandle exclude, EntityHandle[]? excludeAll = null)
    {
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 1 || (exclude.Index == i && exclude.Generation == U.Generation[i])) continue;
            if (excludeAll != null && excludeAll.Any(h => h.Index == i)) continue;
            if (!_sel.TryScreenPosition(i, out px) || !InPlayArea(px) || UnitUnder(px) != i) continue;
            if (!_sel.EnemyAt(px, out enemy, out bool b) || b || enemy.Index != i) continue;
            return true;
        }
        enemy = default;
        px = default;
        return false;
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

    private async Task Gap()
    {
        await WallClock.Wait(this, 70); // the wall clock, as Sfx's gap reads it (BUG-0220)
        await Frame();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
