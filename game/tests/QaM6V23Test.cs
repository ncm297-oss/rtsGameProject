using System;
using System.Collections.Generic;
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
using Rts.Sim.Orders;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-V23 on the real Match scene and shipped data. Rows:
/// (P) BUG-1501 bookkeeping fuzz: seeded random selections and controller orders (queued / unqueued Moves, Stops, Holds,
///     Builds as a first or later Shift placement) with 0 ticks (paused: bursts in one window), 1 tick, or 2-3 ticks (Fast)
///     between them: after every step <see cref="SelectionController.PendingQueuedOrders"/> equals the queued commands for
///     that slot still in the sim's command queue (the oracle), the sim never drops a Build for a full unit queue
///     (UnitQueueFull), and every view refusal has every selected worker genuinely full by the oracle;
/// (R) BUG-1500: three units with a queued Stop, a Hold and a no-target cast at one point nest at three levels; deselecting
///     one leaves two nested from level 0; one left is level 0;
/// (G) 30 workers (Laborers and Teblor Gatherers, gold and wood, mixed loop phases) given one first Shift placement: 30
///     queued Builds; across the wait the bank plus their cargo never falls except by the house's price; every queue starts;
/// (E) during the wait: the node running dry, the drop-off razed (its own match), the price unpayable when the turn comes,
///     the ghost cancelled, and five Shift placements on a loaded gatherer (built in turn, the load delivered first);
/// (C) the cast clip: its loop mode and length against the abilities' cast times, the phase it starts at, and which clip
///     each model type's overlay line now comes from (printed).
/// Pins no gatherer position and no AI outcome (the sim track tunes gather movement).
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V23Test.tscn</c>; prints "QA M6-V23 TEST PASS".</remarks>
public partial class QaM6V23Test : Node
{
    private const string Tag = "QA M6-V23 TEST";
    private static readonly FieldInfo CommandsField = typeof(Simulation).GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo DamageMethod = typeof(BuildingStore).GetMethod("Damage", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo LedgerProperty = typeof(World).GetProperty("Ledger", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly PropertyInfo RefusedProperty = typeof(Simulation).GetProperty("RefusedForTests", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo RemainingField = typeof(ResourceStore).GetField("_remaining", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private CommandCard _card = null!;
    private OrderPathView _view = null!;
    private int _hi, _laborer, _gatherer, _shaman, _house, _frenzy;
    private NVector2 _hall, _away, _perp;
    private readonly List<int> _usedAnchors = new();
    private int _buildQueueFullDrops;

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
            await StartMatch();
            await RingRow();
            await PendingFuzz(1);
            await EndMatch();
            for (int seed = 2; seed <= 4; seed++)
            {
                await StartMatch();
                await PendingFuzz(seed);
                await EndMatch();
            }
            await StartMatch();
            await GatherPhases();
            await EndMatch();
            await StartMatch();
            await WaitEdges();
            await EndMatch();
            await StartMatch();
            await RazedDuringWait();
            await EndMatch();
            await CastClipRow();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(60)) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task StartMatch()
    {
        _usedAnchors.Clear();
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        var runner = _match.GetNode<SimRunner>("SimRunner");
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--mute" }));
        runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _view = _match.GetNode<OrderPathView>("World3D/OrderPaths");
        _match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        Tick(2);
        _buildQueueFullDrops = 0;
        RefusedProperty.SetValue(_sim, new Action<Command, CommandRefusal, int>((c, why, _) =>
        {
            if (why == CommandRefusal.UnitQueueFull && c.Kind == CommandKind.Build) _buildQueueFullDrops++;
        }));
        _hi = _data.FindUnit("malazan_heavy_infantry");
        _laborer = StartBase.UnitOfSlot(_data, W.FactionOf(0), UnitSlot.Worker);
        _gatherer = _data.FindUnit("teblor_gatherer");
        _shaman = _data.FindUnit("teblor_shaman");
        _house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        _frenzy = _data.FindAbility("blood_oil_frenzy");
        _hall = HallCenter();
        var middle = new NVector2(G.Width, G.Height) * MapConstants.CellSize / 2f;
        _away = NVector2.Normalize(middle - _hall);
        _perp = new NVector2(-_away.Y, _away.X);
        SetMoney(50000, 50000);
        Tick(2);
        await Frame();
    }

    private async Task EndMatch()
    {
        _sel.Selection.Clear();
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    // ---- (R) rings from three units at one point ----

    private async Task RingRow()
    {
        NVector2 t = Passable(_hall + _away * 30f);
        EntityHandle a = Spawn(0, _hi, t), b = Spawn(0, _hi, Passable(t + _perp * 2f)), c = Spawn(0, _shaman, Passable(t - _perp * 2f));
        NVector2 goal = Passable(t + _away * 20f), q = OpenGround(t + _away * 10f);
        int cast = _data.Units[_shaman].Abilities.IndexOf(_frenzy);
        foreach (EntityHandle h in new[] { a, b, c })
        {
            _sim.Enqueue(Command.Move(0, h, goal));
            _sim.Enqueue(Command.Move(0, h, q, queued: true));
        }
        _sim.Enqueue(Command.Stop(0, a, queued: true));
        _sim.Enqueue(Command.HoldPosition(0, b, queued: true));
        _sim.Enqueue(Command.UseAbility(0, c, cast, q, queued: true));
        Apply();
        string RingsAt(out List<int> levels)
        {
            _view.Sync(W, 1f);
            var parts = new List<string>();
            levels = new List<int>();
            for (int k = 0; k < _view.MarkerCount; k++)
            {
                if (!_view.MarkerNoPlace(k) || NVector2.Distance(new NVector2(_view.MarkerPoint(k).X, _view.MarkerPoint(k).Z), q) > 0.1f) continue;
                parts.Add($"{_view.MarkerKind(k)}@{_view.MarkerLevel(k)} {_view.MarkerDrawnRadius(k):0.00} m");
                levels.Add(_view.MarkerLevel(k));
            }
            return $"[{string.Join("; ", parts)}] ({_view.RingsNode.Multimesh.VisibleInstanceCount} ring instances)";
        }
        _sel.Selection.Clear();
        foreach (EntityHandle h in new[] { a, b, c }) _sel.Selection.Add(h);
        await Frames();
        string three = RingsAt(out List<int> l3);
        GD.Print($"(R) Stop (a), Hold (b), Frenzy (c) at one point: {three}");
        Check(l3.Count == 3 && l3.OrderBy(x => x).SequenceEqual(new[] { 0, 1, 2 }), $"(R) three units, three kinds at one point: {three}, want levels 0, 1, 2");
        _sel.Selection.Clear();
        _sel.Selection.Add(a);
        _sel.Selection.Add(c);
        await Frames();
        string two = RingsAt(out List<int> l2);
        GD.Print($"(R) b deselected: {two}");
        Check(l2.Count == 2 && l2.OrderBy(x => x).SequenceEqual(new[] { 0, 1 }), $"(R) b deselected: {two}, want levels 0 and 1");
        _sel.Selection.Clear();
        _sel.Selection.Add(c);
        await Frames();
        string one = RingsAt(out List<int> l1);
        GD.Print($"(R) only c: {one}");
        Check(l1.Count == 1 && l1[0] == 0, $"(R) only c: {one}, want one ring at level 0");
        // A fourth ring at the point (a second Stop on a): capped at MaxRingLevel.
        _sim.Enqueue(Command.Stop(0, a, queued: true));
        Apply();
        _sel.Selection.Clear();
        foreach (EntityHandle h in new[] { a, b, c }) _sel.Selection.Add(h);
        await Frames();
        string four = RingsAt(out List<int> l4);
        GD.Print($"(R) four rings at one point: {four}");
        Check(l4.Count == 4 && l4.All(x => x <= OrderPathView.MaxRingLevel) && l4.Count(x => x == OrderPathView.MaxRingLevel) == 2, $"(R) four rings: {four}, want 0, 1, 2, 2");
        _sel.Selection.Clear();
        foreach (EntityHandle h in new[] { a, b, c }) _sim.Enqueue(Command.Stop(0, h));
        Apply();
    }

    // ---- (P) the pending-queued-order bookkeeping against the sim's command queue ----

    private async Task PendingFuzz(int seed)
    {
        var rng = new Random(seed * 7919);
        NVector2 mine = NodeCenter("gold_mine");
        var units = new List<EntityHandle>();
        for (int k = 0; k < 6; k++) units.Add(Spawn(0, k % 2 == 0 ? _laborer : _gatherer, Passable(_hall + _away * (14f + k * 2f) + _perp * (seed * 2f - 6f))));
        for (int k = 0; k < 3; k++) units.Add(Spawn(0, _hi, Passable(_hall + _away * (14f + k * 2f) + _perp * (seed * 2f - 2f))));
        var anchors = new List<int>();
        for (int k = 0; k < 6; k++)
        {
            int an = GreenAnchor(_house, _hall + _away * (24f + 6f * k) - _perp * (10f + seed * 2f));
            if (an >= 0) anchors.Add(an);
        }
        if (!Check(anchors.Count >= 3, $"(P{seed}) only {anchors.Count} house anchors")) return;
        int drops0 = _buildQueueFullDrops, refusals = 0, sentBuilds = 0, mismatches = 0, steps = 0, ticksRun = 0, maxPending = 0;
        for (int step = 0; step < 400; step++)
        {
            steps++;
            // Selection: a random non-empty subset of the live units.
            _sel.Selection.Clear();
            foreach (EntityHandle h in units) if (U.Alive[h.Index] && rng.Next(3) > 0) _sel.Selection.Add(h);
            if (_sel.Selection.Count == 0) _sel.Selection.Add(units[0]);
            bool queued = rng.Next(4) > 0;
            int act = rng.Next(10);
            NVector2 p = Passable(_hall + _away * (12f + rng.Next(30)) + _perp * (rng.Next(41) - 20));
            if (act < 4) _sel.Order(CommandKind.Move, new Vector2(p.X, p.Y), queued);
            else if (act == 4) _sel.Order(CommandKind.Stop, null, queued);
            else if (act == 5) _sel.Order(CommandKind.HoldPosition, null, queued);
            else if (act == 6) _sel.ContextOrder(new Vector2(mine.X, mine.Y), queued); // workers onto a gather loop
            else
            {
                int anchor = anchors[rng.Next(anchors.Count)];
                bool first = rng.Next(2) == 0;
                // The oracle's view of fullness before the call (same rule: queue + queued commands not yet applied).
                (bool allFull, int workers) = OracleFull(first);
                int before = _sel.RefusedBuilds;
                int sent = _sel.OrderBuild(_house, anchor, queued: !first, queueBehindEndingOrders: first);
                sentBuilds += sent;
                if (_sel.RefusedBuilds > before)
                {
                    refusals++;
                    Check(workers > 0 && allFull, $"(P{seed}) step {step}: the view refused a placement but the oracle has a worker with room");
                }
                else if (workers > 0) Check(!allFull && sent > 0, $"(P{seed}) step {step}: sent {sent} while the oracle says every worker is full");
            }
            int ticks = rng.Next(10) switch { < 3 => 0, < 7 => 1, _ => 2 + rng.Next(2) };
            if (step % 50 >= 40) ticks = 0; // a paused stretch: ten orders in one window
            for (int t = 0; t <= ticks; t++)
            {
                for (int i = 0; i < U.Capacity; i++)
                {
                    if (!U.Alive[i] || U.Owner[i] != 0) continue;
                    int counted = _sel.PendingQueuedOrders(_sim, i), oracle = OracleQueued(i);
                    maxPending = Math.Max(maxPending, counted);
                    if (counted != oracle && mismatches++ < 5) Check(false, $"(P{seed}) step {step} +{t} ticks, slot {i}: counted {counted}, the sim holds {oracle} queued commands not applied");
                }
                if (t < ticks) { _sim.Tick(); ticksRun++; }
            }
            if (step % 25 == 24)
            {
                // Keep sites from filling the ground: cancel own sites and stop everyone now and then.
                foreach (EntityHandle h in units) if (U.Alive[h.Index]) _sim.Enqueue(Command.Stop(0, h));
                Apply();
            }
        }
        Apply();
        await Frame();
        Check(_buildQueueFullDrops == drops0, $"(P{seed}) the sim dropped {_buildQueueFullDrops - drops0} Builds on a full unit queue");
        GD.Print($"(P{seed}) {steps} steps, {ticksRun} ticks: {sentBuilds} Builds sent, {refusals} placements refused, {_buildQueueFullDrops - drops0} Builds dropped by the sim, {mismatches} count mismatches, most pending on one slot {maxPending}");
        foreach (EntityHandle h in units) if (U.Alive[h.Index]) _sim.Enqueue(Command.Stop(0, h));
        Apply();
    }

    private (bool AllFull, int Workers) OracleFull(bool first)
    {
        OrderPath reader = OrderPathView.NewReader(W);
        bool allFull = true;
        int workers = 0;
        foreach (EntityHandle h in _sel.Selection.Items)
        {
            if (!_data.Units[U.TypeId[h.Index]].IsWorker) continue;
            workers++;
            bool q = !first || reader.QueuesBehind(h.Index);
            if (!q || U.QueueCount[h.Index] + OracleQueued(h.Index) < OrderConstants.QueueCapacity) allFull = false;
        }
        return (allFull, workers);
    }

    private int OracleQueued(int slot)
    {
        var q = (CommandQueue)CommandsField.GetValue(_sim)!;
        int n = 0;
        for (int k = 0; k < q.Count; k++)
            if (q[k].Player == 0 && q[k].IsQueued && q[k].Unit.Index == slot && q[k].Kind != CommandKind.SpawnUnit) n++;
        return n;
    }

    // ---- (G) 30 workers in mixed loop phases ----

    private async Task GatherPhases()
    {
        NVector2 mine = NodeCenter("gold_mine"), tree = NodeCenter("tree");
        var workers = new List<EntityHandle>();
        for (int k = 0; k < 30; k++)
        {
            int type = k % 2 == 0 ? _laborer : _gatherer;
            NVector2 node = k % 3 == 0 ? tree : mine;
            EntityHandle h = Spawn(0, type, Passable(node + _away * (2f + k % 5 * 2f) + _perp * (k % 7 - 3)));
            _sim.Enqueue(Command.Gather(0, h, node));
            workers.Add(h);
            Tick(1 + k * 7 % 23); // staggered so the loop phases differ
        }
        Tick(60);
        var phases = new Dictionary<string, int>();
        foreach (EntityHandle h in workers)
        {
            string ph = !EconomySystem.OnLoop(U, h.Index) ? "off-loop" : $"{U.State[h.Index]}{(U.Cargo[h.Index] > 0 ? "+cargo" : "")}";
            phases[ph] = phases.TryGetValue(ph, out int n) ? n + 1 : 1;
        }
        GD.Print($"(G) 30 workers' phases: {string.Join(", ", phases.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}"))}");
        Check(workers.All(h => EconomySystem.OnLoop(U, h.Index)), "(G) not every worker is on a loop");
        int anchor = GreenAnchor(_house, _hall - _away * 10f);
        if (anchor < 0) anchor = GreenAnchor(_house, _hall + _perp * 14f);
        if (!Check(anchor >= 0, "(G) no house spot")) return;
        _sel.Selection.Clear();
        foreach (EntityHandle h in workers) _sel.Selection.Add(h);
        int start = _sim.PendingCommandCount;
        int sent = _sel.OrderBuild(_house, anchor, queued: false, queueBehindEndingOrders: true);
        List<Command> list = Pending(start);
        Check(sent == 30 && list.Count == 30 && list.All(c => c.Kind == CommandKind.Build && c.IsQueued),
            $"(G) first Shift placement on 30 gatherers: sent {sent}, {list.Count(c => c.IsQueued)} of {list.Count} queued, want 30 queued");
        int cost = _data.Buildings[_house].CostGold + _data.Buildings[_house].CostWood;
        long Wealth() => W.Gold[0] + W.Wood[0] + workers.Sum(h => U.Alive[h.Index] ? U.Cargo[h.Index] : 0);
        long before = Wealth();
        int fell = 0, worstFall = 0, ticks = 0, firstSite = -1;
        for (; ticks < 4000; ticks++)
        {
            bool site = SiteAt(anchor) >= 0;
            _sim.Tick();
            long now = Wealth();
            long delta = now - before + (!site && SiteAt(anchor) >= 0 ? cost : 0);
            if (!site && SiteAt(anchor) >= 0) firstSite = ticks;
            if (delta < 0) { fell++; worstFall = (int)Math.Min(worstFall, delta); }
            before = now;
            if (workers.All(h => U.QueueCount[h.Index] == 0 && !EconomySystem.OnLoop(U, h.Index)) && ticks > 10) break;
        }
        int stillQueued = workers.Count(h => U.QueueCount[h.Index] > 0), onLoop = workers.Count(h => EconomySystem.OnLoop(U, h.Index));
        int building = workers.Count(h => U.State[h.Index] == UnitState.Building);
        GD.Print($"(G) after {ticks} ticks: site placed at tick {firstSite}, {building} building, {stillQueued} still queued, {onLoop} on a loop; the bank + cargo fell {fell} times (worst {worstFall})");
        Check(fell == 0, $"(G) the bank + the workers' cargo fell {fell} times (worst {worstFall}): something carried was lost");
        Check(stillQueued == 0 && onLoop == 0 && firstSite >= 0, $"(G) after {ticks} ticks {stillQueued} still queued, {onLoop} on a loop, site at {firstSite}");
        _sel.Selection.Clear();
        foreach (EntityHandle h in workers) _sim.Enqueue(Command.Stop(0, h));
        Apply();
        await Frame();
    }

    // ---- (E) during the wait ----

    private async Task WaitEdges()
    {
        NVector2 mine = NodeCenter("gold_mine");
        int cost = _data.Buildings[_house].CostGold + _data.Buildings[_house].CostWood;

        // (E1) the node runs dry while the loaded worker waits: still builds, the load not lost.
        {
            EntityHandle w = LoadedGatherer(mine, _laborer);
            int node = NodeSlotAt(mine);
            int anchor = GreenAnchor(_house, _hall + _perp * 16f);
            if (Check(w.Generation != 0 && node >= 0 && anchor >= 0, "(E1) staging"))
            {
                Place(w, anchor, first: true);
                Apply();
                ((int[])RemainingField.GetValue(W.Resources)!)[node] = 1;
                (bool started, int fell, int t) = Watch(w, anchor, cost, 3000);
                GD.Print($"(E1) node run dry during the wait: building {started} after {t} ticks, wealth fell {fell} times, node alive {W.Resources.Alive[node]}");
                Check(started && fell == 0, $"(E1) node dry: started {started}, wealth fell {fell} times");
                Stop(w);
            }
        }

        // (E2) the price is not payable when the turn comes: the worker is not stuck, the load is delivered, the queue empties.
        {
            EntityHandle w = LoadedGatherer(NodeCenter("gold_mine"), _laborer);
            int anchor = GreenAnchor(_house, _hall + _perp * 26f);
            if (Check(w.Generation != 0 && anchor >= 0, "(E2) staging"))
            {
                Place(w, anchor, first: true);
                Apply();
                SetMoney(0, 0);
                int carried = U.Cargo[w.Index];
                int t = 0, delivered = 0;
                var trace = new List<string>();
                string last = "";
                for (; t < 3000; t++)
                {
                    int g = W.Gold[0] + W.Wood[0];
                    _sim.Tick();
                    string s = $"{U.State[w.Index]} c{U.Cargo[w.Index]} q{U.QueueCount[w.Index]} loop{(EconomySystem.OnLoop(U, w.Index) ? 1 : 0)} b{(U.BuildTarget[w.Index].Generation != 0 ? 1 : 0)} g{W.Gold[0]} w{W.Wood[0]} kind {U.CargoKind[w.Index]}";
                    if (s != last && trace.Count < 25) trace.Add($"t{t}: {s}");
                    last = s;
                    if (W.Gold[0] + W.Wood[0] > g) delivered += W.Gold[0] + W.Wood[0] - g;
                    if (U.QueueCount[w.Index] == 0 && delivered > 0 && t > 20) break;
                }
                GD.Print($"(E2) trace: {string.Join(" | ", trace)}");
                GD.Print($"(E2) unpayable when its turn came: after {t} ticks queue {U.QueueCount[w.Index]}, state {U.State[w.Index]}, on loop {EconomySystem.OnLoop(U, w.Index)}, site {SiteAt(anchor)}, delivered {delivered} (carried {carried} at placement)");
                Check(U.QueueCount[w.Index] == 0 && delivered >= carried && SiteAt(anchor) < 0, $"(E2) unpayable: queue {U.QueueCount[w.Index]}, delivered {delivered} of {carried}, site {SiteAt(anchor)}");
                SetMoney(50000, 50000);
                Stop(w);
            }
        }

        // (E3) the ghost cancelled after the first Shift placement: the queued Build stays. (E4) Shift held for five
        // placements on a loaded gatherer: five queued Builds, the load delivered first, all five built in turn.
        {
            EntityHandle w = LoadedGatherer(NodeCenter("gold_mine"), _gatherer);
            var anchors = new List<int>();
            for (int k = 0; k < 5; k++)
            {
                int an = GreenAnchor(_house, _hall - _away * 12f + _perp * (k * 9f - 18f));
                if (an >= 0) anchors.Add(an);
            }
            if (Check(w.Generation != 0 && anchors.Count == 5, $"(E4) staging: {anchors.Count} anchors"))
            {
                _sel.SelectOnly(w);
                await Frame();
                for (int k = 0; k < 5; k++) Place(w, anchors[k], first: k == 0);
                _card.CloseMenu();
                Apply();
                int carried = U.Cargo[w.Index];
                Check(U.QueueCount[w.Index] == 5 && EconomySystem.OnLoop(U, w.Index), $"(E3/E4) five Shift placements, the ghost closed: queue {U.QueueCount[w.Index]}, on loop {EconomySystem.OnLoop(U, w.Index)}");
                var placedAt = new int[5];
                Array.Fill(placedAt, -1);
                int deliveredAt = -1, t = 0;
                for (; t < 12000 && placedAt.Any(x => x < 0); t++)
                {
                    int g = W.Gold[0] + W.Wood[0];
                    _sim.Tick();
                    if (deliveredAt < 0 && U.Cargo[w.Index] == 0) deliveredAt = t;
                    for (int k = 0; k < 5; k++) if (placedAt[k] < 0 && SiteAt(anchors[k]) >= 0) placedAt[k] = t;
                }
                GD.Print($"(E4) five Shift placements on a loaded Teblor Gatherer ({carried} carried): delivered at {deliveredAt}, sites placed at [{string.Join(", ", placedAt)}]");
                bool inTurn = true;
                for (int k = 1; k < 5; k++) if (placedAt[k] < placedAt[k - 1]) inTurn = false;
                Check(placedAt.All(x => x >= 0) && inTurn && deliveredAt >= 0 && deliveredAt <= placedAt[0], $"(E4) delivered {deliveredAt}, placed [{string.Join(", ", placedAt)}]");
                Stop(w);
            }
        }
        _sel.Selection.Clear();
        await Frame();
    }

    // (E5) the drop-off razed while a loaded worker waits (a match of its own): it starts the Build with its load kept.
    private async Task RazedDuringWait()
    {
        NVector2 mine = NodeCenter("gold_mine");
        EntityHandle w = LoadedGatherer(mine, _laborer);
        int anchor = GreenAnchor(_house, _hall + _away * 18f);
        if (!Check(w.Generation != 0 && anchor >= 0, "(E5) staging")) return;
        Place(w, anchor, first: true);
        Apply();
        Check(U.QueueCount[w.Index] == 1 && EconomySystem.OnLoop(U, w.Index), $"(E5) queued behind the trip: queue {U.QueueCount[w.Index]}");
        for (int k = 0; k < B.Capacity; k++)
            if (B.Alive[k] && B.Owner[k] == 0 && _data.Buildings[B.TypeId[k]].DropOff) DamageMethod.Invoke(B, new object[] { B.HandleOf(k), B.Hp[k] });
        int cargo = U.Cargo[w.Index];
        int t = RunUntil(() => U.BuildTarget[w.Index].Generation != 0 || U.QueueCount[w.Index] == 0, 400);
        GD.Print($"(E5) the drop-off razed during the wait: after {t} ticks build {U.BuildTarget[w.Index]}, queue {U.QueueCount[w.Index]}, cargo {U.Cargo[w.Index]} (had {cargo}), on loop {EconomySystem.OnLoop(U, w.Index)}");
        Check(U.BuildTarget[w.Index].Generation != 0 && U.Cargo[w.Index] == cargo && !EconomySystem.OnLoop(U, w.Index),
            $"(E5) razed drop-off: build {U.BuildTarget[w.Index]}, cargo {U.Cargo[w.Index]} of {cargo}, on loop {EconomySystem.OnLoop(U, w.Index)}");
        await Frame();
    }

    // ---- (C) the cast clip ----

    private async Task CastClipRow()
    {
        var probe = new UnitViews();
        AddChild(probe);
        probe.Bind(_data, 4, new uint[] { 0xC03030, 0x3060D0 });
        AnimationLibrary? lib = UnitModels.Library;
        if (Check(lib != null && lib.HasAnimation("attack_cast"), "(C) the shipped library has no attack_cast"))
        {
            Animation cast = lib!.GetAnimation("attack_cast");
            var casts = new List<string>();
            foreach (AbilityDef a in _data.Abilities) if (a.CastTicks > 0) casts.Add($"{a.Key} {a.CastTicks} ticks ({a.CastTicks / 20.0:0.00} s)");
            GD.Print($"(C) attack_cast: {cast.Length:0.00} s, loop mode {cast.LoopMode}; idle loop {lib.GetAnimation("idle").LoopMode}, attack loop {lib.GetAnimation("attack").LoopMode}; casts: {string.Join(", ", casts)}");
            var tops = new List<string>();
            UnitModels models = probe.Models!;
            for (int t = 0; t < _data.Units.Length; t++)
            {
                if (!models.Has(t)) continue;
                (string bone, string clip) = models.TopBoneOf(t);
                tops.Add($"{_data.Units[t].Key} {models.TopBone(t):0.00} m ({bone} in {clip})");
            }
            GD.Print($"(C) overlay line per model type: {string.Join("; ", tops)}");
        }
        probe.QueueFree();
        await Frame();
    }

    // ---- helpers ----

    private EntityHandle LoadedGatherer(NVector2 node, int type)
    {
        EntityHandle w = Spawn(0, type, Passable(node + _away * 4f + _perp * (_usedAnchors.Count % 5 - 2)));
        _sim.Enqueue(Command.Gather(0, w, node));
        Apply();
        RunUntil(() => U.State[w.Index] == UnitState.Gathering && U.Cargo[w.Index] > 0, 1500);
        if (!Check(EconomySystem.OnLoop(U, w.Index) && U.State[w.Index] == UnitState.Gathering && U.Cargo[w.Index] > 0,
                $"the worker is not working its node with a load ({U.State[w.Index]}, cargo {U.Cargo[w.Index]})")) return default;
        return w;
    }

    private void Place(EntityHandle w, int anchor, bool first)
    {
        _sel.SelectOnly(w);
        int sent = _sel.OrderBuild(_house, anchor, queued: !first, queueBehindEndingOrders: first);
        Check(sent == 1, $"placement on {w.Index} sent {sent}");
    }

    private (bool Started, int Fell, int Ticks) Watch(EntityHandle w, int anchor, int cost, int max)
    {
        long Wealth() => W.Gold[0] + W.Wood[0] + U.Cargo[w.Index];
        long before = Wealth();
        int fell = 0, t = 0;
        for (; t < max && U.BuildTarget[w.Index].Generation == 0; t++)
        {
            bool site = SiteAt(anchor) >= 0;
            _sim.Tick();
            long now = Wealth();
            if (now - before + (!site && SiteAt(anchor) >= 0 ? cost : 0) < 0) fell++;
            before = now;
        }
        return (U.BuildTarget[w.Index].Generation != 0, fell, t);
    }

    private void Stop(EntityHandle w)
    {
        _sel.Selection.Clear();
        _sim.Enqueue(Command.Stop(0, w));
        Apply();
    }

    private NVector2 NodeCenter(string id)
    {
        ResourceStore r = W.Resources;
        int type = _data.FindResource(id);
        ResourceDef def = _data.Resources[type];
        NVector2 best = default;
        float bestD = float.MaxValue;
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k] || r.TypeId[k] != type) continue;
            int a = r.Cell[k];
            var c = new NVector2(a % G.Width + def.FootprintWidth / 2f, a / G.Width + def.FootprintHeight / 2f) * MapConstants.CellSize;
            float d = NVector2.Distance(c, _hall);
            if (d < bestD) (best, bestD) = (c, d);
        }
        if (bestD == float.MaxValue) throw new InvalidOperationException($"no {id}");
        return best;
    }

    private int NodeSlotAt(NVector2 center)
    {
        ResourceStore r = W.Resources;
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k]) continue;
            ResourceDef def = _data.Resources[r.TypeId[k]];
            int a = r.Cell[k];
            var c = new NVector2(a % G.Width + def.FootprintWidth / 2f, a / G.Width + def.FootprintHeight / 2f) * MapConstants.CellSize;
            if (NVector2.Distance(c, center) < 0.01f) return k;
        }
        return -1;
    }

    private EntityHandle Spawn(int owner, int type, NVector2 at)
    {
        var alive = new bool[U.Capacity];
        for (int i = 0; i < U.Capacity; i++) alive[i] = U.Alive[i];
        _sim.Enqueue(Command.SpawnUnit(owner, type, at));
        Apply();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && !alive[i]) return new EntityHandle(i, U.Generation[i]);
        throw new InvalidOperationException($"spawn of {_data.Units[type].Key} at {at} refused");
    }

    private int SiteAt(int anchor) => anchor < 0 ? -1 : B.SlotAt(anchor % G.Width, anchor / G.Width);

    private NVector2 HallCenter()
    {
        for (int k = 0; k < B.Capacity; k++)
            if (B.Alive[k] && B.Owner[k] == 0 && _data.Buildings[B.TypeId[k]].Slot == BuildingSlot.TownHall)
                return StartBase.FootprintCenter(G, _data.Buildings[B.TypeId[k]], B.Cell[k]);
        throw new InvalidOperationException("player 0 has no hall");
    }

    private NVector2 OpenGround(NVector2 p)
    {
        G.WorldToCell(Passable(p), out int px, out int py);
        for (int r = 0; r < 20; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    NVector2 c = G.CellCenter(px + dx, py + dy);
                    if (StartLayout.IsOpen(G, px + dx, py + dy) && SelectionController.NodeAt(W, c) < 0 && BuildingPicker.SlotAt(B, G, c) < 0) return c;
                }
        throw new InvalidOperationException($"no open ground near {p}");
    }

    private NVector2 Passable(NVector2 p)
    {
        int x = Math.Clamp((int)(p.X / MapConstants.CellSize), 1, G.Width - 2), y = Math.Clamp((int)(p.Y / MapConstants.CellSize), 1, G.Height - 2);
        int cell = FlowField.NearestPassable(G, y * G.Width + x);
        if (cell < 0) throw new InvalidOperationException($"nothing passable near {p}");
        return G.CellCenter(cell % G.Width, cell / G.Width);
    }

    private int GreenAnchor(int type, NVector2 near)
    {
        BuildingDef def = _data.Buildings[type];
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            NVector2 c = StartBase.FootprintCenter(G, def, cell);
            float d = NVector2.Distance(c, near);
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
