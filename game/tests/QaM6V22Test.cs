using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Save;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA (M6-V22): attacks on the queued-orders drawing (<see cref="OrderPathView"/>) and the first-Shift-building rule on the
/// real Match scene and shipped data. Rows: selection churn (200 selected with 8 shared points, cleared, an enemy, a mix,
/// a selected unit that dies and whose slot a new own unit takes; 0 bytes in the mixed case); a queued Attack on an enemy
/// building never seen (no marker, no leg); a queued Attack whose target dies and whose slot a new, seen enemy takes (no
/// marker on the newcomer); the building rule for a worker carrying cargo home (queued behind its trip since M6-V23; it
/// replaced before) and a builder whose site was cancelled (replaces); a first Shift placement queued behind a walk that the sim refuses when its turn comes (no money: no
/// site, the worker idle, the marker gone); a save written with queued orders and loaded into a new match draws the same
/// markers once the unit is selected again.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V22Test.tscn</c>; prints "QA M6-V22 TEST PASS".</remarks>
public partial class QaM6V22Test : Node
{
    private const string Tag = "QA M6-V22 TEST";
    private const float Tolerance = 0.1f;
    private static readonly PropertyInfo LedgerProperty = typeof(World).GetProperty("Ledger", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo CommandsField = typeof(Simulation).GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private CommandCard _card = null!;
    private BuildGhost _ghost = null!;
    private OrderPathView _view = null!;
    private FogOfWar _fog = null!;
    private RtsCamera _camera = null!;
    private int _hi, _laborer, _raider, _house;
    private System.Numerics.Vector2 _hall, _away, _perp, _t;
    private readonly List<int> _usedAnchors = new();

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private BuildingStore B => _sim.World.Buildings;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            await StartMatch(null);
            Stage();
            await SelectionChurn();
            await UnseenBuildingTarget();
            await ReusedTargetSlot();
            await CargoAndCancelledSite();
            await RefusedWhenItsTurnComes();
            await SaveAndLoad();
            await EndMatch();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        Input.ActionRelease("order_queue");
        foreach (string f in _failures.Take(60)) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task StartMatch(Simulation? loaded)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        LaunchOptions options = LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--mute" });
        if (loaded == null) _match.Start(_data, options);
        else _match.StartLoaded(_data, loaded, options, "qa-m6v22.save");
        SimRunner runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _ghost = _match.GetNode<BuildGhost>("World3D/BuildGhost");
        _view = _match.GetNode<OrderPathView>("World3D/OrderPaths");
        _fog = _match.GetNode<FogOfWar>("World3D/FogOfWar");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        if (loaded == null) Tick(2);
        await Frames();
    }

    private async Task EndMatch()
    {
        Input.ActionRelease("order_queue");
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    private void Stage()
    {
        _hi = _data.FindUnit("malazan_heavy_infantry");
        _laborer = StartBase.UnitOfSlot(_data, W.FactionOf(0), UnitSlot.Worker);
        _raider = _data.FindUnit("whirlwind_raider");
        _house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        _hall = HallCenter(0);
        var middle = new System.Numerics.Vector2(G.Width, G.Height) * MapConstants.CellSize / 2f;
        _away = System.Numerics.Vector2.Normalize(middle - _hall);
        _perp = new System.Numerics.Vector2(-_away.Y, _away.X);
        _t = Passable(_hall + _away * 24f);
        SetMoney(50000, 50000);
    }

    // ---- selection churn ----

    private async Task SelectionChurn()
    {
        await Frames();
        System.Numerics.Vector2 block = Passable(_t + _away * 16f + _perp * 10f);
        var aliveBefore = new HashSet<int>(Enumerable.Range(0, U.Capacity).Where(i => U.Alive[i]));
        for (int k = 0; k < 200; k++) _sim.Enqueue(Command.SpawnUnit(0, _hi, Passable(block + new System.Numerics.Vector2(k % 20 - 10, k / 20 - 5) * 1.2f)));
        Apply();
        var units = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && !aliveBefore.Contains(i)) units.Add(new EntityHandle(i, U.Generation[i]));
        if (!Check(units.Count == 200, $"churn: {units.Count} spawned")) return;
        System.Numerics.Vector2 far = Passable(block + _away * 60f);
        foreach (EntityHandle h in units) _sim.Enqueue(Command.Move(0, h, far));
        for (int k = 0; k < 8; k++)
        {
            System.Numerics.Vector2 p = Passable(block + _perp * (6f + 3f * k) + _away * 4f);
            foreach (EntityHandle h in units) _sim.Enqueue(Command.Move(0, h, p, queued: true));
        }
        EntityHandle enemy = Spawn(1, _raider, Passable(block - _perp * 12f));
        _sim.Enqueue(Command.Move(1, enemy, Passable(block - _perp * 20f)));
        _sim.Enqueue(Command.Move(1, enemy, Passable(block - _perp * 24f), queued: true));
        Apply();
        foreach (EntityHandle h in units) _sel.Selection.Add(h);
        _view.Sync(W, 0.5f);
        Check(_view.MarkerCount == 8 && _view.SegmentCount > 0, $"churn: 200 selected drew {_view.MarkerCount} markers / {_view.SegmentCount} segments, want 8");
        Check(_view.MarkersNode.Multimesh.VisibleInstanceCount == 8, $"churn: {_view.MarkersNode.Multimesh.VisibleInstanceCount} marker instances visible, want 8");
        // Deselect between frames: the next frame draws nothing (no instance left over from the 200).
        _sel.Selection.Clear();
        _view.Sync(W, 0.5f);
        Check(_view.MarkerCount == 0 && _view.SegmentCount == 0 && _view.MarkersNode.Multimesh.VisibleInstanceCount == 0 && _view.LinesNode.Multimesh.VisibleInstanceCount == 0,
            $"churn: cleared selection still draws {_view.MarkerCount} markers / {_view.SegmentCount} segments (visible {_view.MarkersNode.Multimesh.VisibleInstanceCount} / {_view.LinesNode.Multimesh.VisibleInstanceCount})");
        // An enemy with a queue: nothing.
        _sel.SelectOnly(enemy);
        _view.Sync(W, 0.5f);
        Check(U.QueueCount[enemy.Index] == 1 && _view.MarkerCount == 0 && _view.SegmentCount == 0, $"churn: enemy (queue {U.QueueCount[enemy.Index]}) drew {_view.MarkerCount} / {_view.SegmentCount}");
        // A mix (enemy + one own unit): only the own unit's path.
        _sel.Selection.Clear();
        _sel.Selection.Add(enemy);
        _sel.Selection.Add(units[0]);
        _view.Sync(W, 0.5f);
        bool enemyLeg = false;
        for (int k = 0; k < _view.SegmentCount; k++)
            if (System.Numerics.Vector2.Distance(Flat(_view.Segment(k).To), Passable(block - _perp * 24f)) < 1f || System.Numerics.Vector2.Distance(Flat(_view.Segment(k).To), Passable(block - _perp * 20f)) < 1f) enemyLeg = true;
        Check(_view.MarkerCount == 8 && !enemyLeg, $"churn: mixed selection drew {_view.MarkerCount} markers (want 8), enemy leg {enemyLeg}");
        // Allocation with a mixed selection of 200 own + the enemy: 0 bytes once warm.
        foreach (EntityHandle h in units) _sel.Selection.Add(h);
        for (int k = 0; k < 30; k++) _view.Sync(W, k % 5 / 5f);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long bytes = 0;
        for (int k = 0; k < 300; k++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            _view.Sync(W, k % 5 / 5f);
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Check(bytes == 0, $"churn: mixed selection allocated {bytes} bytes over 300 warm syncs");
        // A selected unit dies, and a new own unit takes its slot with a queue of its own: the old handle draws nothing.
        _sel.Selection.Clear();
        EntityHandle victim = units[5];
        _sel.SelectOnly(victim);
        U.Free(victim); // a test seam: dead between frames, still in the selection
        _view.Sync(W, 0.5f);
        Check(_view.MarkerCount == 0 && _view.SegmentCount == 0, $"churn: a dead selected unit drew {_view.MarkerCount} / {_view.SegmentCount}");
        EntityHandle newcomer = Spawn(0, _hi, Passable(block + _perp * 30f));
        _sim.Enqueue(Command.Move(0, newcomer, Passable(block + _perp * 30f + _away * 10f)));
        _sim.Enqueue(Command.Move(0, newcomer, Passable(block + _perp * 34f + _away * 10f), queued: true));
        Apply();
        _sel.Selection.Clear();
        _sel.Selection.Add(victim); // the stale handle only (Prune not run), as a group recall of an old selection could hold
        _view.Sync(W, 0.5f);
        Check(newcomer.Index != victim.Index || (_view.MarkerCount == 0 && _view.SegmentCount == 0),
            $"churn: the stale handle of slot {victim.Index} drew the newcomer's path ({_view.MarkerCount} / {_view.SegmentCount})");
        GD.Print($"churn: 200 -> 8 markers, cleared -> 0, enemy -> 0, mixed -> {_view.MarkerCount}, {bytes} bytes; slot reuse {(newcomer.Index == victim.Index ? "tested" : "not reused")}");
        _sel.Selection.Clear();
        foreach (EntityHandle h in units) if (U.IsAlive(h)) _sim.Enqueue(Command.Stop(0, h));
        if (U.IsAlive(newcomer)) _sim.Enqueue(Command.Stop(0, newcomer));
        Apply();
    }

    // ---- fog: a building never seen ----

    private async Task UnseenBuildingTarget()
    {
        await Frames();
        int hall1 = HallSlot(1);
        EntityHandle hall = B.HandleOf(hall1);
        FogView fog = _fog.Refreshed(W)!;
        if (!Check(!fog.ShowsBuilding(hall1) && !fog.ShowsGhost(hall1), "unseen building: player 1's hall is already shown to player 0")) return;
        EntityHandle unit = Spawn(0, _hi, _t);
        _sim.Enqueue(Command.Move(0, unit, Passable(_t + _perp * 20f)));
        Apply();
        _sim.Enqueue(Command.Attack(0, unit, hall, isBuilding: true, queued: true));
        Apply();
        int queued = U.QueueCount[unit.Index];
        _sel.SelectOnly(unit);
        _view.Sync(W, 1f);
        System.Numerics.Vector2 c = HallCenter(1);
        int attackMarkers = 0;
        bool legToHall = false;
        for (int k = 0; k < _view.MarkerCount; k++) if (_view.MarkerKind(k) == CommandKind.Attack) attackMarkers++;
        for (int k = 0; k < _view.SegmentCount; k++) if (System.Numerics.Vector2.Distance(Flat(_view.Segment(k).To), c) < 3f) legToHall = true;
        Check(attackMarkers == 0 && !legToHall, $"unseen building: queue {queued}, {attackMarkers} Attack markers, leg to the hidden hall {legToHall}");
        GD.Print($"unseen building: queued Attack on the hidden enemy hall {(queued == 1 ? "accepted by the sim" : "refused by the sim")}; {attackMarkers} markers, leg {legToHall}");
        _sel.Selection.Clear();
        _sim.Enqueue(Command.Stop(0, unit));
        Apply();
    }

    // ---- a queued Attack's target dies and a new, seen enemy takes its slot ----

    private async Task ReusedTargetSlot()
    {
        await Frames();
        // On one level with its spotter (a unit below a plateau does not see onto it).
        System.Numerics.Vector2 spot = default;
        for (int k = 0; k < 80; k++)
        {
            spot = Passable(_t - _perp * (10f + k % 8 * 2f) + _away * (k / 8 * 3f - 9f));
            if (MathF.Abs(Height(spot) - Height(_t)) < 0.3f && MathF.Abs(Height(Passable(spot + _perp * 4f)) - Height(spot)) < 0.3f) break;
        }
        EntityHandle spotter = Spawn(0, _laborer, Passable(spot + _perp * 4f));
        EntityHandle target = Spawn(1, _raider, spot);
        _sim.Enqueue(Command.HoldPosition(0, spotter));
        _sim.Enqueue(Command.HoldPosition(1, target));
        Apply();
        for (int k = 0; k < 20 && !W.Fog.CanSeeUnit(0, target.Index); k++) Tick(1);
        EntityHandle unit = Spawn(0, _hi, Passable(_t + _perp * 6f));
        _sim.Enqueue(Command.Move(0, unit, Passable(_t + _perp * 30f)));
        Apply();
        _sim.Enqueue(Command.Attack(0, unit, target, isBuilding: false, queued: true));
        Apply();
        _sel.SelectOnly(unit);
        _view.Sync(W, 1f);
        bool before = AttackMarkerNear(U.Position[target.Index]);
        Check(U.QueueCount[unit.Index] == 1, $"reused slot: the queued Attack did not take (queue {U.QueueCount[unit.Index]}, seen {W.Fog.CanSeeUnit(0, target.Index)})");
        U.Free(target);
        _sim.Enqueue(Command.SpawnUnit(1, _raider, Passable(spot + _away * 2f)));
        Apply();
        bool reused = U.Alive[target.Index] && U.Generation[target.Index] != target.Generation;
        _sim.Enqueue(Command.HoldPosition(1, new EntityHandle(target.Index, U.Generation[target.Index])));
        Apply();
        for (int k = 0; k < 4; k++) Tick(1);
        _view.Sync(W, 1f);
        bool seen = U.Alive[target.Index] && _fog.Refreshed(W)!.ShowsUnit(target.Index);
        int attackMarkers = 0;
        for (int k = 0; k < _view.MarkerCount; k++) if (_view.MarkerKind(k) == CommandKind.Attack) attackMarkers++;
        Check(before, "reused slot: no Attack marker on the live, seen target first");
        Check(attackMarkers == 0, $"reused slot (reused {reused}, newcomer seen {seen}): {attackMarkers} Attack markers drawn on the newcomer");
        GD.Print($"reused slot: newcomer in slot {target.Index} reused {reused}, seen {seen}, {attackMarkers} Attack markers");
        _sel.Selection.Clear();
        _sim.Enqueue(Command.Stop(0, unit));
        Apply();
    }

    private bool AttackMarkerNear(System.Numerics.Vector2 p)
    {
        for (int k = 0; k < _view.MarkerCount; k++)
            if (_view.MarkerKind(k) == CommandKind.Attack && System.Numerics.Vector2.Distance(Flat(_view.MarkerPoint(k)), p) < Tolerance) return true;
        return false;
    }

    // ---- the building rule: cargo walking home, a cancelled site ----

    private async Task CargoAndCancelledSite()
    {
        System.Numerics.Vector2 mine = MineCenter();
        EntityHandle carrier = Spawn(0, _laborer, Passable(mine + _away * 4f));
        _sim.Enqueue(Command.Gather(0, carrier, mine));
        Apply();
        int t = RunUntil(() => U.Cargo[carrier.Index] > 0 && U.State[carrier.Index] == UnitState.Moving && EconomySystem.OnLoop(U, carrier.Index), 3000);
        if (Check(U.Cargo[carrier.Index] > 0 && U.State[carrier.Index] == UnitState.Moving, $"cargo: the worker never walked home with cargo in {t} ticks ({U.State[carrier.Index]}, cargo {U.Cargo[carrier.Index]})"))
        {
            (List<Command> sent, int anchor) = await ShiftPlace(carrier, Passable(_hall - _away * 10f));
            // M6-V23, re-stated: the Build was sent unqueued and replaced the loop at once (the site placed in the applying
            // tick); now it is queued behind the trip: the worker delivers the load first, then places the site.
            Check(sent.Count == 1 && sent[0].Kind == CommandKind.Build && sent[0].IsQueued, $"cargo: a worker walking its load home sent {Describe(sent)}, want one queued Build");
            Apply();
            int carried = U.Cargo[carrier.Index];
            Check(U.BuildTarget[carrier.Index].Generation == 0 && SiteAt(anchor) < 0 && EconomySystem.OnLoop(U, carrier.Index) && carried > 0,
                $"cargo: after the applying tick build {U.BuildTarget[carrier.Index]}, site {SiteAt(anchor)}, on loop {EconomySystem.OnLoop(U, carrier.Index)}, cargo {carried}: want still walking home");
            int waited = RunUntil(() => U.BuildTarget[carrier.Index].Generation != 0, 3000);
            Check(U.BuildTarget[carrier.Index].Generation != 0 && SiteAt(anchor) >= 0 && U.Cargo[carrier.Index] == 0,
                $"cargo: {waited} ticks on build {U.BuildTarget[carrier.Index]}, site {SiteAt(anchor)}, cargo {U.Cargo[carrier.Index]} (delivered first)");
            GD.Print($"cargo: walking home with {carried} cargo after {t} ticks, Shift + place queued; delivered, then building {waited} ticks later");
        }

        // A builder whose site is cancelled, then a first Shift placement: it stands idle, so the Build replaces.
        EntityHandle builder = Spawn(0, _laborer, Passable(_t + _perp * 22f));
        Apply();
        int a1 = GreenAnchor(_house, U.Position[builder.Index] + _away * 6f);
        if (!Check(a1 >= 0, "cancelled site: no spot")) return;
        System.Numerics.Vector2 p1 = PlacementGhost.AnchorPoint(G, a1);
        _sim.Enqueue(Command.Build(0, builder, _house, p1));
        Apply();
        int site = SiteAt(a1);
        Check(site >= 0 && U.BuildTarget[builder.Index].Generation != 0, $"cancelled site: site {site}, build {U.BuildTarget[builder.Index]}");
        _sim.Enqueue(Command.Cancel(0, StartBase.FootprintCenter(G, _data.Buildings[_house], a1)));
        Apply();
        Tick(2);
        Check(SiteAt(a1) < 0, "cancelled site: the site still stands");
        _sel.SelectOnly(builder);
        _view.Sync(W, 1f);
        Check(_view.MarkerCount == 0 && _view.SegmentCount == 0, $"cancelled site: the builder still draws {_view.MarkerCount} / {_view.SegmentCount}");
        (List<Command> sent2, int a2) = await ShiftPlace(builder, U.Position[builder.Index] - _away * 6f);
        Check(sent2.Count == 1 && !sent2[0].IsQueued, $"cancelled site: builder (state {U.State[builder.Index]}, build {U.BuildTarget[builder.Index]}) sent {Describe(sent2)}, want one unqueued Build");
        Apply();
        Check(SiteAt(a2) >= 0, "cancelled site: the new Shift placement did not start at once");
        GD.Print($"cancelled site: the builder stood {U.State[builder.Index]} and the Shift placement replaced");
        _sim.Enqueue(Command.Stop(0, carrier));
        _sim.Enqueue(Command.Stop(0, builder));
        Apply();
    }

    // ---- a first Shift placement queued behind a walk that the sim refuses when its turn comes ----

    private async Task RefusedWhenItsTurnComes()
    {
        System.Numerics.Vector2 yard = Passable(_t - _perp * 26f);
        EntityHandle walker = Spawn(0, _laborer, yard);
        System.Numerics.Vector2 dest = Passable(yard - _perp * 12f);
        _sim.Enqueue(Command.Move(0, walker, dest));
        Apply();
        (List<Command> sent, int anchor) = await ShiftPlace(walker, yard + _away * 10f);
        Check(sent.Count == 1 && sent[0].IsQueued, $"refused: a walker sent {Describe(sent)}, want one queued Build");
        Apply();
        Check(U.QueueCount[walker.Index] == 1, $"refused: queue {U.QueueCount[walker.Index]}");
        _sel.SelectOnly(walker);
        _view.Sync(W, 1f);
        int buildMarkers = 0;
        for (int k = 0; k < _view.MarkerCount; k++) if (_view.MarkerKind(k) == CommandKind.Build) buildMarkers++;
        Check(buildMarkers == 1, $"refused: {buildMarkers} Build markers before the turn");
        SetMoney(0, 0); // spent elsewhere while it walks
        int t = RunUntil(() => U.QueueCount[walker.Index] == 0, 1500);
        Tick(2);
        _view.Sync(W, 1f);
        buildMarkers = 0;
        for (int k = 0; k < _view.MarkerCount; k++) if (_view.MarkerKind(k) == CommandKind.Build) buildMarkers++;
        Check(U.QueueCount[walker.Index] == 0 && SiteAt(anchor) < 0 && U.BuildTarget[walker.Index].Generation == 0 && U.State[walker.Index] == UnitState.Idle && buildMarkers == 0,
            $"refused: after {t} ticks queue {U.QueueCount[walker.Index]}, site {SiteAt(anchor)}, build {U.BuildTarget[walker.Index]}, state {U.State[walker.Index]}, {buildMarkers} Build markers");
        GD.Print($"refused: the queued Build popped after {t} ticks with no money: no site, the worker {U.State[walker.Index]}, marker gone");
        SetMoney(50000, 50000);
        _sel.Selection.Clear();
    }

    // ---- save with queued orders, load into a new match, reselect ----

    private async Task SaveAndLoad()
    {
        EntityHandle unit = Spawn(0, _hi, Passable(_t + _away * 4f));
        System.Numerics.Vector2 m0 = Passable(_t + _away * 30f), m1 = Passable(_t + _away * 10f + _perp * 8f), m2 = Passable(_t + _away * 14f - _perp * 8f);
        _sim.Enqueue(Command.Move(0, unit, m0));
        Apply();
        _sim.Enqueue(Command.Move(0, unit, m1, queued: true));
        _sim.Enqueue(Command.AttackMove(0, unit, m2, queued: true));
        _sim.Enqueue(Command.HoldPosition(0, unit, queued: true));
        Apply();
        _sel.SelectOnly(unit);
        _view.Sync(W, 1f);
        var before = new List<(CommandKind, bool, System.Numerics.Vector2)>();
        for (int k = 0; k < _view.MarkerCount; k++) before.Add((_view.MarkerKind(k), _view.MarkerNoPlace(k), Flat(_view.MarkerPoint(k))));
        string path = Path.Combine(Path.GetTempPath(), $"qa-m6v22-{System.Environment.ProcessId}{SaveStore.FileExtension}");
        if (!Check(SaveStore.Write(_sim, path, out SaveError refused, out string io), $"save: refused {refused} {io}")) return;
        ulong hash = _sim.StateHash();
        Simulation? loaded = SaveStore.Load(_data, path, out SaveError error, out _);
        File.Delete(path);
        if (!Check(loaded != null, $"load: {error}")) return;
        await EndMatch();
        await StartMatch(loaded);
        Check(_sim.StateHash() == hash, "load: the loaded sim's hash differs");
        _view.Sync(W, 1f);
        Check(_view.MarkerCount == 0, $"load: {_view.MarkerCount} markers drawn before anything is selected");
        _sel.SelectOnly(unit);
        _view.Sync(W, 1f);
        var after = new List<(CommandKind, bool, System.Numerics.Vector2)>();
        for (int k = 0; k < _view.MarkerCount; k++) after.Add((_view.MarkerKind(k), _view.MarkerNoPlace(k), Flat(_view.MarkerPoint(k))));
        bool same = before.Count == after.Count && before.Count == 3 && before.Zip(after).All(p => p.First.Item1 == p.Second.Item1 && p.First.Item2 == p.Second.Item2 && System.Numerics.Vector2.Distance(p.First.Item3, p.Second.Item3) < Tolerance);
        Check(same, $"load: markers before [{string.Join(" ", before)}] after [{string.Join(" ", after)}]");
        GD.Print($"save/load: {before.Count} markers before the save, {after.Count} after the load and reselect, same {same}");
    }

    // ---- helpers (as OrderQueueViewTest) ----

    private async Task<(List<Command> Sent, int Anchor)> ShiftPlace(EntityHandle worker, System.Numerics.Vector2 near)
    {
        int anchor = GreenAnchor(_house, near);
        if (!Check(anchor >= 0, $"no green house spot near {near}")) return (new List<Command>(), -1);
        _sel.SelectOnly(worker);
        await Frames();
        _card.CloseMenu();
        int cell = -1;
        if (_card.OpenMenu(false))
            for (int i = 0; i < CommandCard.Cells && cell < 0; i++) if (_card.ActionAt(i) == CardCommand.Place && _card.TypeAt(i) == _house) cell = i;
        if (!Check(cell >= 0, "the house button is missing")) return (new List<Command>(), anchor);
        _card.Press(cell);
        if (!Check(_card.GhostActive, "the house ghost did not open")) return (new List<Command>(), anchor);
        BuildingDef def = _data.Buildings[_house];
        System.Numerics.Vector2 p = G.CellCenter(anchor % G.Width + def.FootprintWidth / 2, anchor / G.Width + def.FootprintHeight / 2);
        _camera.SetZoom(30f);
        _camera.SetFocus(p.X, p.Y);
        await Frame();
        Vector2 px = _camera.UnprojectPosition(new Vector3(p.X, TerrainHeight.At(W.Heightmap, p.X, p.Y), p.Y));
        _ghost.ScreenOverride = px;
        await Frames();
        int start = _sim.PendingCommandCount;
        Input.ActionPress("order_queue");
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = px });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = px });
        Input.ActionRelease("order_queue");
        var q = (CommandQueue)CommandsField.GetValue(_sim)!;
        var sent = new List<Command>();
        for (int i = start; i < q.Count; i++) sent.Add(q[i]);
        _card.CloseMenu();
        return (sent, anchor);
    }

    private static string Describe(List<Command> list) =>
        $"[{string.Join(", ", list.Select(c => $"{c.Kind}{(c.IsQueued ? "+q" : "")} u{c.Unit.Index}"))}]";

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

    private int SiteAt(int anchor) => anchor < 0 ? -1 : B.SlotAt(anchor % G.Width, anchor / G.Width);

    private int HallSlot(int player)
    {
        for (int k = 0; k < B.Capacity; k++)
            if (B.Alive[k] && B.Owner[k] == player && _data.Buildings[B.TypeId[k]].Slot == BuildingSlot.TownHall) return k;
        throw new InvalidOperationException($"player {player} has no hall");
    }

    private System.Numerics.Vector2 HallCenter(int player)
    {
        int k = HallSlot(player);
        return StartBase.FootprintCenter(G, _data.Buildings[B.TypeId[k]], B.Cell[k]);
    }

    private System.Numerics.Vector2 MineCenter()
    {
        ResourceStore r = W.Resources;
        int mine = _data.FindResource("gold_mine");
        ResourceDef def = _data.Resources[mine];
        System.Numerics.Vector2 best = default;
        float bestD = float.MaxValue;
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k] || r.TypeId[k] != mine) continue;
            int a = r.Cell[k];
            var c = new System.Numerics.Vector2(a % G.Width + def.FootprintWidth / 2f, a / G.Width + def.FootprintHeight / 2f) * MapConstants.CellSize;
            float d = System.Numerics.Vector2.Distance(c, _hall);
            if (d < bestD) (best, bestD) = (c, d);
        }
        return best;
    }

    private System.Numerics.Vector2 Passable(System.Numerics.Vector2 p)
    {
        int x = Math.Clamp((int)(p.X / MapConstants.CellSize), 1, G.Width - 2), y = Math.Clamp((int)(p.Y / MapConstants.CellSize), 1, G.Height - 2);
        int cell = FlowField.NearestPassable(G, y * G.Width + x);
        if (cell < 0) throw new InvalidOperationException($"nothing passable near {p}");
        return G.CellCenter(cell % G.Width, cell / G.Width);
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

    private void SetMoney(int gold, int wood)
    {
        object ledger = LedgerProperty.GetValue(W)!;
        ((int[])ledger.GetType().GetProperty("Gold")!.GetValue(ledger)!)[0] = gold;
        ((int[])ledger.GetType().GetProperty("Wood")!.GetValue(ledger)!)[0] = wood;
    }

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
