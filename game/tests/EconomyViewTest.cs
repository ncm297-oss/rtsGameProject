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
using Rts.Sim.Orders;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>M3-V1 criteria 1-5 and 7 on the real Match scene: start bases, resource bar, right-click Gather, worker feedback, site views, idle allocation.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/EconomyViewTest.tscn</c>; prints "ECONOMY
/// VIEW TEST PASS" and exits 0, or prints each failure and exits 1. The test ticks the sim itself (SimRunner disabled),
/// so every "within N ticks" is exact; after each tick it waits two frames, so the views it checks were updated by the
/// first frame after the change (process_frame fires before the nodes' _Process). Windowed with <c>-- --shots &lt;dir&gt;</c>
/// it also saves the start of each seed, the workers hauling, and a site.
/// </remarks>
public partial class EconomyViewTest : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private string? _shots;

    // The match under test.
    private Match _match = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private UnitViews _units = null!;
    private BuildingViews _buildings = null!;
    private ResourceBar _bar = null!;
    private Sfx _sfx = null!;

    private World W => _sim.World;
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
        Input.ActionRelease("order_queue");
        foreach (string f in _failures) GD.Print($"ECONOMY VIEW TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("ECONOMY VIEW TEST PASS");
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
        _data = loaded.Data!;

        WorkersOptionRows();
        NoSpotRow();
        foreach (ulong seed in new ulong[] { 1, 6, 31 }) await Boot(seed, 100, null);
        await Boot(1, 100, 0);
        await Boot(6, 0, 7);
        await Gather();
        await Site();
    }

    // --workers parsing: the default (null: rules.json), bounds, refused values.
    private void WorkersOptionRows()
    {
        (string Args, int? Workers)[] rows =
        {
            ("", null), ("--workers 0", 0), ("--workers 5", 5), ("--workers 200", 200), ("--workers 201", null),
            ("--workers -1", null), ("--workers x", null), ("--workers --units 5", null),
        };
        foreach ((string a, int? want) in rows)
        {
            LaunchOptions o = LaunchOptions.Parse(a.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            Check(o.Workers == want, $"'{a}': workers {o.Workers}, want {want}");
        }
        Check(LaunchOptions.Parse(new[] { "--workers", "--units", "5" }).UnitsPerPlayer == 5, "--workers swallowed the next flag");
        Check(_data.Rules.StartingWorkers == 5, $"rules.json startingWorkers is {_data.Rules.StartingWorkers}, the brief's numbers assume 5");
    }

    // Criterion 1, no spot: with every cell taken by the army no hall fits; one warning per player, nothing enqueued.
    private void NoSpotRow()
    {
        var sim = new Simulation(new SimConfig(1, 2, 64, 64) { Data = _data, Map = MapGenParams.Default with { Forests = 12, GoldMines = 8 } });
        NavGrid g = sim.World.NavGrid;
        var everywhere = new List<System.Numerics.Vector2>();
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
                if (g.IsPassable(x, y)) everywhere.Add(g.CellCenter(x, y));
        StartBasePlan plan = Match.SpawnBases(sim, new[] { everywhere.ToArray(), Array.Empty<System.Numerics.Vector2>() }, 5, out int warnings);
        // Player 0's "army" covers the map, so player 1 has no room either.
        Check(warnings == 2 && plan.HallAnchor[0] < 0 && plan.HallAnchor[1] < 0, $"no-spot: {warnings} warnings, anchors {plan.HallAnchor[0]} / {plan.HallAnchor[1]}");
        Check(sim.PendingCommandCount == 0 && plan.Workers[0].Length == 0, $"no-spot: {sim.PendingCommandCount} commands enqueued");
        sim.Tick();
        sim.Tick();
        Check(sim.World.Buildings.Count == 0 && sim.World.Units.Count == 0, "no-spot: something spawned");
    }

    private async Task StartMatch(ulong seed, int units, int? workers)
    {
        var args = new List<string> { "--seed", seed.ToString(), "--units", units.ToString(), "--mute", "--no-fog" }; // both players' halls and workers are checked (M4-V4)
        if (workers is int n) args.AddRange(new[] { "--workers", n.ToString() });
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled; // the test ticks the sim itself
        _sim = runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _units = _match.GetNode<UnitViews>("World3D/UnitViews");
        _buildings = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _bar = _match.GetNode<ResourceBar>("Hud/ResourceBar");
        _sfx = _match.GetNode<Sfx>("Sfx");
        Tick(2); // spawns apply on the first tick
        await Frames();
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    // Criterion 1: per player a finished Town Hall of its faction at the planned anchor, shown as a box in its colour,
    // and the planned workers beside it; criterion 2's start text.
    private async Task Boot(ulong seed, int units, int? workers)
    {
        await StartMatch(seed, units, workers);
        int want = workers ?? _data.Rules.StartingWorkers;
        string what = $"seed {seed} units {units} workers {workers?.ToString() ?? "default"}";
        StartBasePlan plan = _match.Bases!;
        BuildingStore b = W.Buildings;
        Check(_match.WorkersPerPlayer == want, $"{what}: match asked for {_match.WorkersPerPlayer} workers");
        Check(b.Count == 2, $"{what}: {b.Count} buildings, want 2 Town Halls");
        for (int p = 0; p < 2; p++)
        {
            int slot = -1;
            for (int k = 0; k < b.Capacity; k++) if (b.Alive[k] && b.Owner[k] == p) slot = k;
            if (!Check(slot >= 0, $"{what}: player {p} has no building")) continue;
            BuildingDef def = _data.Buildings[b.TypeId[slot]];
            Check(def.Slot == BuildingSlot.TownHall && def.Faction == W.FactionOf(p), $"{what}: player {p} has {def.Key}");
            Check(b.Cell[slot] == plan.HallAnchor[p] && !b.UnderConstruction[slot] && b.Hp[slot] == def.Hp, $"{what}: player {p} hall state");
            Check(_buildings.IsShown(slot) && _buildings.ViewOf(slot)!.Visible, $"{what}: player {p} hall not shown");
            Check(_buildings.BoxOf(slot).MaterialOverride == _buildings.PlayerMaterial(p) && !_buildings.ShownAsSite(slot), $"{what}: player {p} hall not in its colour");
            Check(_buildings.ShownBar(slot, out _) == BuildingBarKind.None && !_buildings.BarFillOf(slot).Visible, $"{what}: undamaged hall shows a bar");
            Aabb box = _buildings.BoxOf(slot).GetAabb();
            Check(Mathf.IsEqualApprox(box.Size.X, def.FootprintWidth * MapConstants.CellSize) && Mathf.IsEqualApprox(box.Size.Z, def.FootprintHeight * MapConstants.CellSize)
                && Mathf.IsEqualApprox(box.Size.Y, BuildingViews.BoxHeight), $"{what}: hall box {box.Size}");
            int placed = 0;
            foreach (System.Numerics.Vector2 spot in plan.Workers[p])
                for (int i = 0; i < U.Capacity; i++)
                    if (U.Alive[i] && U.Owner[i] == p && U.TypeId[i] == plan.WorkerType[p] && U.Position[i] == spot) placed++;
            Check(plan.Workers[p].Length == want && placed == want, $"{what}: player {p} has {placed} of {want} workers");
            Check(_data.Units[plan.WorkerType[p]].Slot == UnitSlot.Worker && _data.Units[plan.WorkerType[p]].Faction == W.FactionOf(p), $"{what}: player {p} worker type");
        }
        Check(U.Count == 2 * (units + want), $"{what}: {U.Count} units, want {2 * (units + want)}");
        FactionDef f = _data.Factions[W.FactionOf(0)];
        string text = $"{f.GoldName} {W.Gold[0]}  {f.WoodName} {W.Wood[0]}";
        Check(_bar.Text == text && _bar.Text == "Gold 200  Wood 200", $"{what}: bar reads '{_bar.Text}', want '{text}'");
        Check(_bar.Visible && _bar.ShownGold == W.Gold[0] && _bar.ShownWood == W.Wood[0], $"{what}: bar state");
        GD.Print($"{what}: halls at {plan.HallAnchor[0]} / {plan.HallAnchor[1]}, {U.Count} units, bar '{_bar.Text}'");
        if (units == 100 && workers == null)
        {
            FocusHall(0);
            await Shot($"start-seed{seed}");
        }
        await EndMatch();
    }

    // Criteria 2, 3, 4 and 7 on the default match, seed 1: its west hall has a mine 10 m and a tree 13 m away (centre to
    // centre), so a worker walks there in well under 60 ticks (4 m/s); a farther node takes longer by its walk.
    private async Task Gather()
    {
        await StartMatch(1, 100, null);
        StartBasePlan plan = _match.Bases!;
        List<EntityHandle> workers = BaseWorkers(plan);
        Check(workers.Count == 5, $"gather: {workers.Count} base workers");
        System.Numerics.Vector2 hall = HallCenter(plan, 0);
        List<EntityHandle> soldiers = Enumerable.Range(0, U.Capacity)
            .Where(i => U.Alive[i] && U.Owner[i] == 0 && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker)
            .OrderBy(i => System.Numerics.Vector2.Distance(U.Position[i], hall)).Take(5)
            .Select(i => new EntityHandle(i, U.Generation[i])).ToList();
        Check(soldiers.Count == 5, $"gather: {soldiers.Count} soldiers");
        int mine = NearestNode(hall, ResourceKind.Gold), tree = NearestNode(hall, ResourceKind.Wood);
        System.Numerics.Vector2 minePoint = NodeCenter(mine), treePoint = NodeCenter(tree);
        GD.Print($"gather: mine {mine} {System.Numerics.Vector2.Distance(minePoint, hall):0.0} m, tree {tree} {System.Numerics.Vector2.Distance(treePoint, hall):0.0} m from the hall");

        // Gold: 5 workers selected, a right-click on the mine through the real input path.
        Vector2 mineScreen = await OnScreen(minePoint);
        Check(SelectionController.NodeAt(W, Pick(mineScreen)) == mine, "the mine's screen point doesn't pick the mine");
        Select(workers);
        int gathers = _sel.IssuedCount(CommandKind.Gather), moves = _sel.IssuedCount(CommandKind.Move);
        await SoundGap();
        int sounds = _sfx.PlayCount(SfxEvent.Command);
        RightClick(mineScreen);
        Check(_sel.IssuedCount(CommandKind.Gather) == gathers + 5 && _sel.IssuedCount(CommandKind.Move) == moves, "right-click on a mine with 5 workers: not 5 Gathers");
        Check(_sfx.PlayCount(SfxEvent.Command) == sounds + 1, "right-click Gather played no Command sound");
        await RunGather(workers, ResourceKind.Gold, "gold", 60);
        await OverlayCounts();

        // Wood: the same workers, a right-click on the nearest tree (their gold cargo, if any, is dropped: AoE rule).
        Vector2 treeScreen = await OnScreen(treePoint);
        Check(SelectionController.NodeAt(W, Pick(treeScreen)) == tree, "the tree's screen point doesn't pick the tree");
        Select(workers);
        RightClick(treeScreen);
        // From the mine, not the hall: the walk to the tree is longer than the first order's.
        await RunGather(workers, ResourceKind.Wood, "wood", 200);
        await Shot("hauling");
        await CargoAt60();
        await CanopyClick(workers, hall);

        await Mixed(workers, soldiers, minePoint);
        await IdleAllocation();
        await EndMatch();
    }

    // Ticks until every worker is Gathering (within reachBy ticks) and the total rises (within 1,200), checking each frame
    // that the bar, the tints and the cargo markers match the sim.
    private async Task RunGather(List<EntityHandle> workers, ResourceKind kind, string what, int reachBy)
    {
        int start = kind == ResourceKind.Gold ? W.Gold[0] : W.Wood[0];
        var reached = new int[workers.Count];
        Array.Fill(reached, -1);
        int rose = -1, cargoFrames = 0, deposits = 0;
        var carrying = new bool[U.Capacity];
        for (int t = 1; t <= 1200 && (rose < 0 || reached.Any(r => r < 0)); t++)
        {
            Tick(1);
            await Frames();
            for (int k = 0; k < workers.Count; k++)
                if (reached[k] < 0 && U.State[workers[k].Index] == UnitState.Gathering) reached[k] = t;
            int total = kind == ResourceKind.Gold ? W.Gold[0] : W.Wood[0];
            if (rose < 0 && total > start) rose = t;
            CheckBar(what);
            for (int i = 0; i < U.Capacity; i++)
            {
                if (!U.Alive[i]) continue;
                bool has = U.Cargo[i] > 0;
                if (has) cargoFrames++;
                if (carrying[i] && !has && _units.MarkerOf(i) is { Visible: false }) deposits++;
                carrying[i] = has;
            }
            CheckWorkerViews(what);
        }
        int slowest = reached.Max();
        Check(reached.All(r => r >= 0) && slowest <= reachBy, $"{what}: workers reached Gathering at ticks [{string.Join(", ", reached)}], want all within {reachBy}");
        Check(rose >= 0 && rose <= 1200, $"{what}: total rose at tick {rose}, want within 1,200");
        Check(cargoFrames > 0 && deposits > 0, $"{what}: {cargoFrames} carrying frames, {deposits} deposits seen");
        GD.Print($"{what}: all Gathering by tick {slowest}, total rose at tick {rose} ({start} -> {(kind == ResourceKind.Gold ? W.Gold[0] : W.Wood[0])}), {deposits} deposits");
    }

    // The F12 overlay's second line counts the workers gathering, returning and building (equal to the sim's states).
    private async Task OverlayCounts()
    {
        var overlay = _match.GetNode<DebugOverlay>("DebugOverlay");
        overlay.SetEnabled(true);
        for (int t = 0; t < 400 && DebugCounts.InState(U.Alive, U.State, UnitState.Gathering) == 0; t++) Tick(1);
        await Frames();
        int g = 0, r = 0, b = 0;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i]) continue;
            if (U.State[i] == UnitState.Gathering) g++;
            else if (U.State[i] == UnitState.Returning) r++;
            else if (U.State[i] == UnitState.Building) b++;
        }
        string label = _match.GetNode<Label>("DebugOverlay/Label").Text;
        Check(overlay.GatheringUnits == g && overlay.ReturningUnits == r && overlay.BuildingUnits == b && g > 0,
            $"overlay counts {overlay.GatheringUnits} / {overlay.ReturningUnits} / {overlay.BuildingUnits}, sim {g} / {r} / {b}");
        // Its own line since M4-V2 (BUG-0160: the one long line ran under the resource bar).
        Check(label.Contains($"\nworkers gathering {g}, returning {r}, building {b}\n"), $"overlay label lacks the worker counts: {label}");
        overlay.SetEnabled(false);
        await Frames();
    }

    // Criterion 3, mixed selection: workers gather and soldiers move; Shift queues both; plain ground is a Move for all.
    private async Task Mixed(List<EntityHandle> workers, List<EntityHandle> soldiers, System.Numerics.Vector2 minePoint)
    {
        var all = workers.Concat(soldiers).ToList();
        System.Numerics.Vector2 ground = EmptyGroundNear(minePoint);

        // Plain ground: a Move for everyone; the workers' loop ends.
        Select(all);
        int gathers = _sel.IssuedCount(CommandKind.Gather), moves = _sel.IssuedCount(CommandKind.Move);
        RightClick(await OnScreen(ground));
        Check(_sel.IssuedCount(CommandKind.Gather) == gathers && _sel.IssuedCount(CommandKind.Move) == moves + 10, "plain ground: not 10 Moves");
        Tick(2);
        Check(workers.All(h => U.GatherNode[h.Index] == default && U.State[h.Index] == UnitState.Moving), "plain ground: a worker kept its gather loop");

        // Shift + right-click on the mine: queued behind the Move, a Gather per worker and a Move per soldier.
        Vector2 mineScreen = await OnScreen(minePoint);
        Select(all);
        gathers = _sel.IssuedCount(CommandKind.Gather);
        moves = _sel.IssuedCount(CommandKind.Move);
        Input.ActionPress("order_queue");
        RightClick(mineScreen);
        Input.ActionRelease("order_queue");
        Check(_sel.IssuedCount(CommandKind.Gather) == gathers + 5 && _sel.IssuedCount(CommandKind.Move) == moves + 5, "shift mixed: not 5 Gathers + 5 Moves");
        Tick(2);
        int cap = OrderConstants.QueueCapacity;
        foreach (EntityHandle h in workers)
            Check(U.QueueCount[h.Index] == 1 && U.QueueKind[h.Index * cap] == CommandKind.Gather, $"shift: worker {h.Index} queue {U.QueueCount[h.Index]} {U.QueueKind[h.Index * cap]}");
        foreach (EntityHandle h in soldiers)
            Check(U.QueueCount[h.Index] == 1 && U.QueueKind[h.Index * cap] == CommandKind.Move, $"shift: soldier {h.Index} queue {U.QueueCount[h.Index]} {U.QueueKind[h.Index * cap]}");

        // Unqueued on the mine: workers start gathering it, soldiers walk there.
        Select(all);
        RightClick(mineScreen);
        Tick(2);
        foreach (EntityHandle h in workers)
            Check(W.Resources.IsAlive(U.GatherNode[h.Index]) && U.QueueCount[h.Index] == 0, $"mixed: worker {h.Index} has no gather loop");
        foreach (EntityHandle h in soldiers)
            Check(U.GatherNode[h.Index] == default && U.QueueCount[h.Index] == 0
                && (U.State[h.Index] == UnitState.Moving || System.Numerics.Vector2.Distance(U.Position[h.Index], minePoint) < 8f), $"mixed: soldier {h.Index} not moving to the mine");

        // Only soldiers on a node: a Move.
        Select(soldiers);
        gathers = _sel.IssuedCount(CommandKind.Gather);
        moves = _sel.IssuedCount(CommandKind.Move);
        RightClick(mineScreen);
        Check(_sel.IssuedCount(CommandKind.Gather) == gathers && _sel.IssuedCount(CommandKind.Move) == moves + 5, "soldiers on a mine: not 5 Moves");
        // The minimap's right click stays a Move even over a node (it calls Order directly).
        Select(workers);
        moves = _sel.IssuedCount(CommandKind.Move);
        _sel.Order(CommandKind.Move, new Vector2(minePoint.X, minePoint.Y), false);
        Check(_sel.IssuedCount(CommandKind.Move) == moves + 5, "Order(Move) over a mine was not a Move");
        Tick(2);
        // Back to work for the allocation row (markers in use).
        Select(workers);
        RightClick(mineScreen);
        for (int t = 0; t < 400; t++) Tick(1);
        await Frames();
    }

    // Criterion 7: 300 idle frames of the bar, the building views and the unit views (markers and tints in use) allocate nothing.
    private async Task IdleAllocation()
    {
        await Frames();
        int markers = Enumerable.Range(0, U.Capacity).Count(i => U.Alive[i] && U.Cargo[i] > 0);
        Check(_units.MarkerCount > 0, "allocation row: no cargo marker was ever made");
        int builds = _bar.Builds, bars = _buildings.BarUpdates, nodes = _buildings.NodeCount, markerNodes = _units.MarkerCount;
        _bar.Sync(W);
        _buildings.Sync(W);
        _units.Sync(W, 0.5f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 300; f++)
        {
            _bar.Sync(W);
            _buildings.Sync(W);
            _units.Sync(W, f / 300f);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GD.Print($"idle: 300 frames of ResourceBar + BuildingViews + UnitViews ({markers} carrying): {bytes} bytes");
        Check(bytes == 0, $"300 idle frames allocated {bytes} bytes");
        // And through real frames: nothing rebuilt or re-placed.
        for (int f = 0; f < 300; f++) await Frame();
        Check(_bar.Builds == builds && _buildings.BarUpdates == bars && _buildings.NodeCount == nodes && _units.MarkerCount == markerNodes,
            $"idle frames rebuilt: bar {builds} -> {_bar.Builds}, bars {bars} -> {_buildings.BarUpdates}");
    }

    // Criterion 5: a worker's Build makes a site: slate box and a progress bar growing with Work; Cancel removes it within a frame.
    private async Task Site()
    {
        await StartMatch(1, 20, null);
        StartBasePlan plan = _match.Bases!;
        List<EntityHandle> workers = BaseWorkers(plan);
        System.Numerics.Vector2 hall = HallCenter(plan, 0);
        int house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        int anchor = SiteSpot(house, hall);
        if (!Check(anchor >= 0, "site: no spot for a house near the hall")) return;
        NavGrid g = W.NavGrid;
        System.Numerics.Vector2 sitePoint = g.CellCenter(anchor % g.Width, anchor / g.Width);
        _sim.Enqueue(Command.Build(0, workers[0], house, sitePoint));
        _sim.Enqueue(Command.Build(0, workers[1], house, sitePoint));
        Tick(2);
        await Frames();
        BuildingStore b = W.Buildings;
        int slot = -1;
        for (int k = 0; k < b.Capacity; k++) if (b.Alive[k] && b.Cell[k] == anchor) slot = k;
        if (!Check(slot >= 0 && b.UnderConstruction[slot], "site: the Build placed no site")) return;
        int needed = b.WorkNeeded(house);
        float last = -1f;
        int growth = 0, building = 0;
        for (int t = 0; t < 600 && b.Work[slot] < needed / 3; t++)
        {
            Tick(1);
            await Frames();
            BuildingBarKind bar = _buildings.ShownBar(slot, out float fill);
            float want = (float)b.Work[slot] / needed;
            Check(_buildings.IsShown(slot) && _buildings.ShownAsSite(slot) && _buildings.BoxOf(slot).MaterialOverride == _buildings.SiteMaterial,
                $"site tick {t}: not shown as a site");
            Check(bar == BuildingBarKind.Progress && Mathf.IsEqualApprox(fill, want), $"site tick {t}: bar {bar} {fill}, want progress {want}");
            Check(_buildings.BarFillOf(slot).Visible == (want > 0f), $"site tick {t}: fill visible {_buildings.BarFillOf(slot).Visible} at {want}");
            if (fill > last && last >= 0f) growth++;
            Check(fill >= last, $"site tick {t}: bar shrank {last} -> {fill}");
            last = fill;
            building += workers.Take(2).Count(h => U.State[h.Index] == UnitState.Building);
            CheckWorkerViews("site");
        }
        Check(growth > 10 && building > 0, $"site: bar grew {growth} times, {building} builder-ticks seen");
        GD.Print($"site: progress {last:0.00} after {b.Work[slot]} / {needed} work, bar grew {growth} times");
        FocusOn(StartBase.FootprintCenter(g, _data.Buildings[house], anchor));
        await Shot("site");
        int hallSlot = -1;
        for (int k = 0; k < b.Capacity && hallSlot < 0; k++)
            if (b.Alive[k] && b.Owner[k] == 0 && _data.Buildings[b.TypeId[k]].Slot == BuildingSlot.TownHall) hallSlot = k;
        await SiteHue(slot, hallSlot);
        // The halls (finished, full hp) show no bar meanwhile.
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && !b.UnderConstruction[k]) Check(_buildings.ShownBar(k, out _) == BuildingBarKind.None, $"finished building {k} shows a bar");

        _sim.Enqueue(Command.Cancel(0, sitePoint));
        int ticks = 0;
        while (b.Alive[slot] && ticks < 5)
        {
            Tick(1);
            ticks++;
        }
        Check(!b.Alive[slot], "site: Cancel didn't free the site");
        await Frames();
        Check(!_buildings.IsShown(slot) && !_buildings.ViewOf(slot)!.Visible, "site: the box outlived the site by a frame");
        Check(workers.Take(2).All(h => _units.ShownTint(h.Index) == _units.TintStateFor(U.State[h.Index])), "site: builders' tint after cancel");
        await ResourceRedraw(workers, house, hall);
        await EndMatch();
    }

    // BUG-0107 (2): a worker carrying wood at 60 m zoom: its cargo marker is at least 6 px wide on a 1152 x 648 view (the
    // shared cube grows with the zoom above 30 m; it was 3-4 px), and back at 30 m it is the 0.35 m cube again.
    private async Task CargoAt60()
    {
        int carrier = -1;
        for (int t = 0; t < 600 && carrier < 0; t++)
        {
            for (int i = 0; i < U.Capacity && carrier < 0; i++)
                if (U.Alive[i] && U.Owner[i] == 0 && U.Cargo[i] > 0 && U.CargoKind[i] == ResourceKind.Wood) carrier = i;
            if (carrier < 0) Tick(1);
        }
        if (!Check(carrier >= 0, "cargo row: no worker carrying wood")) return;
        await Frames();
        _camera.SetFocus(U.Position[carrier].X, U.Position[carrier].Y);
        var widths = new List<string>();
        foreach (float zoom in new[] { 30f, 60f })
        {
            _camera.SetZoom(zoom);
            await Frames();
            MeshInstance3D? marker = _units.MarkerOf(carrier);
            if (!Check(marker != null && marker.Visible, $"cargo row @{zoom}: no visible marker")) return;
            float size = _units.CargoMeshOf(ResourceKind.Wood).Size.X;
            Vector3 right = _camera.GlobalBasis.X.Normalized(), at = marker!.GlobalPosition;
            float px = _camera.UnprojectPosition(at + right * size / 2f).DistanceTo(_camera.UnprojectPosition(at - right * size / 2f));
            widths.Add($"{zoom} m: {size:0.##} m = {px:0.#} px");
            if (zoom == 30f) Check(Mathf.IsEqualApprox(size, UnitViews.CargoSize), $"cargo @30 m: {size} m");
            else Check(px >= 6f, $"BUG-0107 cargo @{zoom} m: {px:0.#} px wide ({size} m)");
            if (zoom == 60f) await Shot("cargo-60");
        }
        GD.Print($"BUG-0107 cargo marker: {string.Join(", ", widths)}");
        _camera.SetZoom(30f);
        await Frames();
    }

    // M3-V3b: a right click on a tree's canopy (2.8 m up its trunk), whose ground point lies behind the tree, is that
    // tree: ContextTarget gives its footprint centre and the 5 selected workers get one Gather each.
    private async Task CanopyClick(List<EntityHandle> workers, System.Numerics.Vector2 hall)
    {
        // A tree with open ground in front (the camera looks north, so south of it), so no other prop stands in the way.
        NavGrid g = W.NavGrid;
        ResourceStore rs = W.Resources;
        int tree = -1;
        float best = float.PositiveInfinity;
        for (int i = 0; i < rs.Capacity; i++)
        {
            if (!rs.Alive[i] || _data.Resources[rs.TypeId[i]].Resource != ResourceKind.Wood) continue;
            int x = rs.Cell[i] % g.Width, y = rs.Cell[i] / g.Width;
            bool open = y + 4 < g.Height;
            for (int dy = 1; dy <= 4 && open; dy++)
                for (int dx = -1; dx <= 1 && open; dx++)
                    open = x + dx >= 0 && x + dx < g.Width && (g.FlagsAt(x + dx, y + dy) & NavFlags.Resource) == 0;
            float d = System.Numerics.Vector2.Distance(NodeCenter(i), hall);
            if (open && d < best) (tree, best) = (i, d);
        }
        if (!Check(tree >= 0, "canopy row: no tree with open ground in front")) return;
        System.Numerics.Vector2 c = NodeCenter(tree);
        FocusOn(c);
        await Frames();
        Vector2 screen = _camera.UnprojectPosition(new Vector3(c.X, TerrainHeight.At(W.Heightmap, c.X, c.Y) + 2.8f, c.Y));
        int behind = SelectionController.NodeAt(W, Pick(screen));
        bool hit = _sel.ContextTarget(screen, out System.Numerics.Vector2 point, out int building);
        int picked = ResourcePicker.NodeAtPoint(W.NavGrid, _data.Resources, W.Resources.Alive, W.Resources.TypeId, W.Resources.Cell, point);
        Check(hit && building == -1 && picked == tree, $"canopy click: target {point} node {picked} building {building}, want tree {tree} (the ground point's node is {behind})");
        Select(workers);
        int gathers = _sel.IssuedCount(CommandKind.Gather);
        RightClick(screen);
        Check(_sel.IssuedCount(CommandKind.Gather) == gathers + workers.Count, $"canopy click: {_sel.IssuedCount(CommandKind.Gather) - gathers} Gathers");
        Tick(2);
        int onTree = workers.Count(h => U.GatherNode[h.Index].Index == tree);
        Check(onTree == workers.Count, $"canopy click: {onTree} of {workers.Count} workers on the tree");
        GD.Print($"canopy click: tree {tree}, the ground point behind it is node {behind}; {onTree} workers sent to the tree");
    }

    // BUG-0107 (1): a site's colour differs in hue from every player's finished-building colour (at least 60 degrees);
    // windowed with --shots, also on the rendered pixels of a full-height site next to the finished hall.
    private async Task SiteHue(int site, int hall)
    {
        Color siteColor = _buildings.SiteMaterial.AlbedoColor;
        for (int p = 0; p < W.Config.PlayerCount; p++)
        {
            float d = HueDistance(siteColor, _buildings.PlayerMaterial(p).AlbedoColor);
            Check(d >= 60f, $"BUG-0107: site hue {siteColor.H * 360f:0} vs player {p}'s {_buildings.PlayerMaterial(p).AlbedoColor.H * 360f:0}: {d:0} degrees apart");
        }
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        // Pixels: the top-centre of the site and of the hall, with the camera between them (the colour doesn't depend on
        // the site's height, so this is the full-height site's colour too).
        BuildingStore b = W.Buildings;
        FocusOn((SelectionController.SiteCenter(W, site) + SelectionController.SiteCenter(W, hall)) / 2f);
        await Frames();
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image img = GetViewport().GetTexture().GetImage();
        Color Sample(int slot)
        {
            System.Numerics.Vector2 c = SelectionController.SiteCenter(W, slot);
            float rise = BuildingPicker.BoxRise(b, _data.Buildings, slot, BuildingViews.SiteMinHeight);
            Vector2 px = _camera.UnprojectPosition(new Vector3(c.X, TerrainHeight.At(W.Heightmap, c.X, c.Y) + BuildingViews.BoxHeight * rise, c.Y));
            int x = (int)px.X, y = (int)px.Y + 2;
            Check(x >= 0 && y >= 0 && x < img.GetWidth() && y < img.GetHeight(), $"BUG-0107 pixels: building {slot} off screen at {px}");
            return img.GetPixel(Math.Clamp(x, 0, img.GetWidth() - 1), Math.Clamp(y, 0, img.GetHeight() - 1));
        }
        Color s = Sample(site), h = Sample(hall);
        float dh = HueDistance(s, h);
        GD.Print($"BUG-0107 pixels: site {s} (hue {s.H * 360f:0}), hall {h} (hue {h.H * 360f:0}), {dh:0} degrees apart");
        Check(dh >= 40f, $"BUG-0107 pixels: site {s} vs hall {h}: {dh:0} degrees apart");
    }

    private static float HueDistance(Color a, Color b)
    {
        float d = MathF.Abs(a.H - b.H) * 360f;
        return Math.Min(d, 360f - d);
    }

    // BUG-0107 (3): ten building changes (five sites placed and cancelled) leave the minimap's resource layer alone; a tree
    // felled redraws it once.
    private async Task ResourceRedraw(List<EntityHandle> workers, int house, System.Numerics.Vector2 hall)
    {
        Minimap mini = _match.GetNode<Minimap>("Hud/Minimap");
        mini.Refresh(_sim);
        int draws = mini.Raster!.ResourceDraws, version = W.NavGrid.Version;
        NavGrid g = W.NavGrid;
        for (int k = 0; k < 5; k++)
        {
            int anchor = SiteSpot(house, hall);
            if (!Check(anchor >= 0, $"redraw row {k}: no spot")) return;
            System.Numerics.Vector2 p = g.CellCenter(anchor % g.Width, anchor / g.Width);
            _sim.Enqueue(Command.Build(0, workers[0], house, p));
            Tick(2);
            mini.Refresh(_sim);
            _sim.Enqueue(Command.Cancel(0, p));
            Tick(2);
            mini.Refresh(_sim);
        }
        await Frames();
        Check(W.NavGrid.Version >= version + 10 && mini.Raster.ResourceDraws == draws,
            $"BUG-0107: 10 building changes (grid version {version} -> {W.NavGrid.Version}) redrew the resource layer {mini.Raster.ResourceDraws - draws} times");
        // A tree felled: one redraw.
        int tree = NearestNode(hall, ResourceKind.Wood);
        ResourceStore r = W.Resources;
        System.Reflection.MethodInfo take = typeof(ResourceStore).GetMethod("Take", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        take.Invoke(r, new object[] { r.HandleOf(tree), int.MaxValue });
        mini.Refresh(_sim);
        mini.Refresh(_sim);
        Check(!r.Alive[tree] && mini.Raster.ResourceDraws == draws + 1, $"BUG-0107: a felled tree redrew the resource layer {mini.Raster.ResourceDraws - draws} times");
        GD.Print($"BUG-0107 minimap: 10 building changes -> {0} resource redraws; a fell -> {mini.Raster.ResourceDraws - draws}");
    }

    // Every frame: tint and cargo marker of each live unit equal what the sim says.
    private void CheckWorkerViews(string what)
    {
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i]) continue;
            UnitState tint = _units.TintStateFor(U.State[i]);
            MeshInstance3D view = _units.ViewOf(i)!;
            // M4-V1: a unit just hit shows the white hit flash in place of its tint while it is lit.
            Material? overlay = _units.Flash.IsLit(i) ? _units.FlashMaterial : _units.TintOf(tint);
            if (_units.ShownTint(i) != tint || view.MaterialOverlay != overlay || _units.ShownLit(i) != _units.Flash.IsLit(i))
            {
                Check(false, $"{what} tick {_sim.TickNumber}: unit {i} state {U.State[i]} shows tint {_units.ShownTint(i)}");
                return;
            }
            int cargo = U.Cargo[i] > 0 ? (int)U.CargoKind[i] : -1;
            MeshInstance3D? marker = _units.MarkerOf(i);
            bool ok = _units.ShownCargo(i) == cargo
                && (cargo < 0 ? marker == null || !marker.Visible : marker != null && marker.Visible && marker.Mesh == _units.CargoMeshOf((ResourceKind)cargo));
            if (!ok)
            {
                Check(false, $"{what} tick {_sim.TickNumber}: unit {i} cargo {U.Cargo[i]} {U.CargoKind[i]} shows {_units.ShownCargo(i)}");
                return;
            }
        }
    }

    private void CheckBar(string what)
    {
        FactionDef f = _data.Factions[W.FactionOf(0)];
        string text = $"{f.GoldName} {W.Gold[0]}  {f.WoodName} {W.Wood[0]}";
        if (_bar.Text != text || _bar.ShownGold != W.Gold[0] || _bar.ShownWood != W.Wood[0])
            Check(false, $"{what} tick {_sim.TickNumber}: bar '{_bar.Text}', sim '{text}'");
    }

    private List<EntityHandle> BaseWorkers(StartBasePlan plan)
    {
        var list = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && U.TypeId[i] == plan.WorkerType[0] && plan.Workers[0].Contains(U.Position[i]))
                list.Add(new EntityHandle(i, U.Generation[i]));
        return list;
    }

    private System.Numerics.Vector2 HallCenter(StartBasePlan plan, int p) =>
        StartBase.FootprintCenter(W.NavGrid, _data.Buildings[plan.HallType[p]], plan.HallAnchor[p]);

    private int NearestNode(System.Numerics.Vector2 from, ResourceKind kind)
    {
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < W.Resources.Capacity; i++)
        {
            if (!W.Resources.Alive[i] || _data.Resources[W.Resources.TypeId[i]].Resource != kind) continue;
            float d = System.Numerics.Vector2.Distance(NodeCenter(i), from);
            if (d < bestD) (best, bestD) = (i, d);
        }
        return best;
    }

    private System.Numerics.Vector2 NodeCenter(int node)
    {
        ResourceDef d = _data.Resources[W.Resources.TypeId[node]];
        int c = W.Resources.Cell[node], w = W.NavGrid.Width;
        return new((c % w + d.FootprintWidth * 0.5f) * MapConstants.CellSize, (c / w + d.FootprintHeight * 0.5f) * MapConstants.CellSize);
    }

    // A house anchor near the hall whose footprint and ring are open and unoccupied (the test's own check, not CanPlace).
    private int SiteSpot(int type, System.Numerics.Vector2 near)
    {
        NavGrid g = W.NavGrid;
        BuildingDef def = _data.Buildings[type];
        g.WorldToCell(near, out int hx, out int hy);
        int level = g.LevelAt(hx, hy), best = -1;
        float bestD = float.PositiveInfinity;
        for (int cell = 0; cell < g.Width * g.Height; cell++)
        {
            System.Numerics.Vector2 c = StartBase.FootprintCenter(g, def, cell);
            float d = System.Numerics.Vector2.Distance(c, near);
            if (d >= bestD || d > 30f || !StartBase.IsHallSpot(g, W.Buildings, _data.Buildings, type, cell, level, null)) continue;
            bool empty = true;
            for (int i = 0; i < U.Capacity && empty; i++)
                if (U.Alive[i] && MathF.Abs(U.Position[i].X - c.X) < def.FootprintWidth + 1f && MathF.Abs(U.Position[i].Y - c.Y) < def.FootprintHeight + 1f) empty = false;
            if (empty) (best, bestD) = (cell, d);
        }
        return best;
    }

    private System.Numerics.Vector2 EmptyGroundNear(System.Numerics.Vector2 p)
    {
        NavGrid g = W.NavGrid;
        g.WorldToCell(p, out int px, out int py);
        for (int r = 4; r < 20; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (StartLayout.IsOpen(g, px + dx, py + dy) && SelectionController.NodeAt(W, g.CellCenter(px + dx, py + dy)) < 0)
                        return g.CellCenter(px + dx, py + dy);
        throw new InvalidOperationException("no open ground near the mine");
    }

    private void Select(List<EntityHandle> units)
    {
        _sel.Selection.Clear();
        foreach (EntityHandle h in units) _sel.Selection.Add(h);
    }

    private void RightClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    // Puts the camera over a ground point and returns its screen position.
    private async Task<Vector2> OnScreen(System.Numerics.Vector2 ground)
    {
        FocusOn(ground);
        await Frame();
        return _camera.UnprojectPosition(new Vector3(ground.X, TerrainHeight.At(W.Heightmap, ground.X, ground.Y), ground.Y));
    }

    private void FocusOn(System.Numerics.Vector2 ground)
    {
        _camera.SetZoom(30f);
        _camera.SetFocus(ground.X, ground.Y);
    }

    private void FocusHall(int p) => FocusOn(HallCenter(_match.Bases!, p));

    private System.Numerics.Vector2 Pick(Vector2 px)
    {
        Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
        Check(GroundPicker.TryPick(W.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out System.Numerics.Vector3 hit), $"pixel {px} missed the map");
        return new(hit.X, hit.Z);
    }

    // The Command sound is rate-limited to one per 50 ms; wait it out so a play is counted.
    private Task SoundGap() => WallClock.Wait(this, Sfx.MinGapMs + 20); // the wall clock, as Sfx's gap reads it (BUG-0220)

    private void Tick(int n)
    {
        for (int i = 0; i < n; i++) _sim.Tick();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    // process_frame fires before the nodes' _Process, so the views are current only after a second one.
    private async Task Frames()
    {
        await Frame();
        await Frame();
    }

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        await Frames();
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/economy-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
