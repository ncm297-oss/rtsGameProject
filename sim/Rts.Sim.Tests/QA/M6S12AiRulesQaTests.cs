using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// QA, M6-S12 (BUG-1531 rebalance, BUG-1460 re-scout, BUG-1461 rally; QA focus "full"): the edges the developer's rows do not
/// stage. The rebalance with no finished drop-off (a site only), with a finished depot by the far forest and that depot razed
/// while the worker walks, with the home mines mined out (falls back to a far mine, never to none). The re-scout with no idle
/// worker (a seat that knows gold but no tree: BUG-1554 pins what is built), with an ally that knows a forest, and its fog
/// rule (a tree the seat never saw changes nothing it sends). The rally search's map read (<see cref="PlayerView.IsKnownClosed"/>)
/// against the fog: a building never seen reads open, a seen one closed, and one razed out of sight (BUG-1555).
/// </summary>
[Collection(SerialCollection.Name)] // measures allocation (TheReScoutFiring_AllocatesNothing)
public class M6S12AiRulesQaTests(ITestOutputHelper output)
{
    private const byte Wood = 2; // EconomyManager.Wood on Blackboard.GatherKind

    /// <summary>The quiet order (no build step for an hour) and every node in gather reach, as <c>RebalanceFromDropOffTests</c>.</summary>
    private static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
            AiScenes.SetBuildOrder(AiScenes.Opening(root), AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1))));
        dir.EditJson("common/rules.json", r => r["ai"]!["gatherReach"] = 512);
    }));

    private static readonly Lazy<GameData> s_quietAi = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.SetBuildOrder(AiScenes.Opening(root), AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1)))));

    // ---------------- the rebalance (BUG-1531) ----------------

    private enum Home { Keep, SiteOnly, MinedOut }

    /// <summary>
    /// One Laborer of player 0 at work on a far tree (cells 40, 52) with a far mine at (43, 50); the Keep on (8, 30); its home
    /// mines on row 24. <paramref name="home"/>: the Keep as is; razed with a depot <i>site</i> left at home (no finished drop-off);
    /// or the home mines mined out. With <paramref name="farDepot"/> a finished depot stands at (35, 47) by the far forest.
    /// </summary>
    private static (Simulation Sim, EntityHandle Lab, Vector2 FarMine, int Depot) Scene(Home home, bool farDepot)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quiet.Value, workers: 0);
        World w = sim.World;
        int size = AiScenes.FlatSize, mine = w.Data.FindResource("gold_mine"), tree = w.Data.FindResource("tree");
        Assert.True(w.Resources.Spawn(mine, 50 * size + 43, w.Data.Rules.StartMineGold, out _));
        Assert.True(w.Resources.Spawn(tree, 52 * size + 40, w.Data.Rules.TreeWood, out EntityHandle far));
        int depotType = w.Data.FindBuilding("malazan_depot");
        Assert.True(w.Data.Buildings[depotType].DropOff, "malazan_depot is no drop-off: the scene's premise");
        int depot = -1;
        if (farDepot)
        {
            Assert.True(w.Buildings.Spawn(0, depotType, 47 * size + 35, out EntityHandle d));
            depot = d.Index;
        }
        if (home == Home.SiteOnly)
        {
            int keep = -1;
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0) keep = k;
            CombatSystem.HitBuilding(w, new PendingHit(default, 1, w.Buildings.HandleOf(keep), true, w.Buildings.Hp[keep] + 1));
            Assert.False(w.Buildings.Alive[keep]);
            Assert.True(w.Buildings.Spawn(0, depotType, 26 * size + 12, out _, site: true)); // a site: not a drop-off
        }
        if (home == Home.MinedOut)
            for (int n = 0; n < w.Resources.Capacity; n++)
                if (w.Resources.Alive[n] && w.Data.Resources[w.Resources.TypeId[n]].Resource == ResourceKind.Gold && w.Resources.Cell[n] / size < 30
                    && w.Resources.Cell[n] % size < 20)
                    w.Resources.Take(w.Resources.HandleOf(n), int.MaxValue);
        EntityHandle lab = CombatScenes.Place(sim, 0, w.Data.FindUnit("malazan_laborer"), w.NavGrid.CellCenter(41, 52));
        EconomySystem.StartGather(w, lab.Index, far.Index);
        Assert.Equal(UnitState.Gathering, w.Units.State[lab.Index]);
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.GatherKind[lab.Index] = Wood;
        ai.Board.GatherNode[lab.Index] = default;
        ai.Board.UnitGeneration[lab.Index] = lab.Generation;
        ai.Board.UnitOrderedUntil[lab.Index] = -1;
        w.Fog.Update();
        return (sim, lab, new Vector2(44f * 2f, 51f * 2f), depot);
    }

    private static Command FirstGather(Simulation sim, List<Command> log, EntityHandle lab, int ticks)
    {
        for (int t = 0; t < ticks && !log.Exists(c => c.Kind == CommandKind.Gather && c.Unit == lab); t++) sim.Tick();
        return log.Find(c => c.Kind == CommandKind.Gather && c.Unit == lab);
    }

    private static ResourceKind KindAt(World w, Vector2 p)
    {
        int x = (int)(p.X / 2f), y = (int)(p.Y / 2f);
        for (int n = 0; n < w.Resources.Capacity; n++)
        {
            if (!w.Resources.Alive[n]) continue;
            int c = w.Resources.Cell[n], cx = c % w.NavGrid.Width, cy = c / w.NavGrid.Width;
            ResourceDef d = w.Data.Resources[w.Resources.TypeId[n]];
            if (x >= cx && x < cx + d.FootprintWidth && y >= cy && y < cy + d.FootprintHeight) return d.Resource;
        }
        throw new InvalidOperationException($"no live node at {p}");
    }

    /// <summary>
    /// QA focus "no finished drop-off": the Keep razed, only a depot site at home. docs/02 / the code: measured from the worker
    /// then, so the far mine 8 m from it; never no Gather at all (a site must not count as the drop-off it measures from).
    /// </summary>
    [Fact]
    public void NoFinishedDropOff_OnlyASite_TheRebalanceStillSends_ToTheMineNearestTheWorker()
    {
        (Simulation sim, EntityHandle lab, Vector2 farMine, _) = Scene(Home.SiteOnly, farDepot: false);
        List<Command> log = AiScenes.Log(sim, 0);
        Command g = FirstGather(sim, log, lab, 6 * sim.AiOf(0)!.ThinkTicks);
        output.WriteLine($"Gather: {(g.Kind == CommandKind.Gather ? $"tick {g.Tick} to {g.Position}, {Vector2.Distance(g.Position, farMine):F1} m from the far mine" : "none")}; all sent: {string.Join(", ", log.Select(c => c.Kind))}");
        Assert.True(g.Kind == CommandKind.Gather, "no rebalance Gather with only a site standing");
        Assert.Equal(ResourceKind.Gold, KindAt(sim.World, g.Position));
        Assert.True(Vector2.Distance(g.Position, farMine) < 3f, $"the Gather went to {g.Position}, not the mine nearest the worker {farMine}");
    }

    /// <summary>
    /// QA focus "drop-off razed mid-walk": a finished depot by the far forest is the worker's nearest drop-off, so the
    /// rebalance picks the far mine beside it (not the home mines 14 m from the Keep). The depot is razed while the worker walks:
    /// the worker still mines and brings gold home to the Keep inside 4,000 ticks, and the AI never sends it more than a
    /// handful of orders (no re-send loop).
    /// </summary>
    [Fact]
    public void ADepotByTheFarForest_IsTheMeasure_AndRazedMidWalk_TheWorkerStillDeliversHome()
    {
        (Simulation sim, EntityHandle lab, Vector2 farMine, int depot) = Scene(Home.Keep, farDepot: true);
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        Command g = FirstGather(sim, log, lab, 6 * sim.AiOf(0)!.ThinkTicks);
        Assert.True(g.Kind == CommandKind.Gather, "no rebalance Gather");
        output.WriteLine($"Gather at tick {g.Tick} to {g.Position}: {Vector2.Distance(g.Position, farMine):F1} m from the far mine");
        Assert.Equal(ResourceKind.Gold, KindAt(w, g.Position));
        Assert.True(Vector2.Distance(g.Position, farMine) < 3f, $"measured from the far depot the far mine is nearest; went to {g.Position}");
        // Walking now: raze the depot.
        while (w.Units.State[lab.Index] != UnitState.Moving && sim.TickNumber < g.Tick + 40) sim.Tick();
        Assert.Equal(UnitState.Moving, w.Units.State[lab.Index]);
        CombatSystem.HitBuilding(w, new PendingHit(default, 1, w.Buildings.HandleOf(depot), true, w.Buildings.Hp[depot] + 1));
        int razedAt = sim.TickNumber, gold0 = w.GatheredGold[0];
        while (sim.TickNumber < razedAt + 4000 && w.GatheredGold[0] == gold0) sim.Tick();
        int orders = log.Count(c => c.Unit == lab);
        output.WriteLine($"razed at {razedAt}; gold delivered {w.GatheredGold[0] - gold0} by tick {sim.TickNumber}; orders to the worker {orders}: {string.Join(", ", log.Where(c => c.Unit == lab).Select(c => $"{c.Kind}@{c.Tick}"))}");
        Assert.True(w.Units.IsAlive(lab));
        Assert.True(w.GatheredGold[0] > gold0, "the worker never delivered after its depot was razed mid-walk");
        Assert.True(orders <= 4, $"{orders} orders to one worker: a re-send loop");
    }

    /// <summary>
    /// QA focus "every home node mined out": the home mines gone, the rebalance falls back to a live gold mine elsewhere, never to
    /// none (with the reach lifted by the quiet data's 512 m). The mines are emptied after the scene's exploring, so the seat
    /// may still remember them: a first Gather to a remembered, gone mine is dropped (BUG-0570) and the next goes to a live one.
    /// Within 10 thinks the worker must be sent to a live gold mine.
    /// </summary>
    [Fact]
    public void HomeMinesMinedOut_TheRebalanceFallsBackToAFarMine_NeverToNone()
    {
        (Simulation sim, EntityHandle lab, Vector2 farMine, _) = Scene(Home.MinedOut, farDepot: false);
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        int think = sim.AiOf(0)!.ThinkTicks;
        Vector2 live = default;
        for (int t = 0; t < 10 * think && live == default; t++)
        {
            sim.Tick();
            foreach (Command c in log)
                if (c.Kind == CommandKind.Gather && c.Unit == lab && LiveGoldAt(w, c.Position)) live = c.Position;
        }
        output.WriteLine($"Gathers to the worker: {string.Join("; ", log.Where(c => c.Unit == lab).Select(c => $"{c.Kind}@{c.Tick} to {c.Position} (live gold {LiveGoldAt(w, c.Position)})"))}; "
            + $"first live gold {live}, {Vector2.Distance(live, farMine):F1} m from the far mine");
        Assert.True(live != default, "no Gather to a live gold mine within 10 thinks with the home mines mined out");
    }

    private static bool LiveGoldAt(World w, Vector2 p)
    {
        try { return KindAt(w, p) == ResourceKind.Gold; }
        catch (InvalidOperationException) { return false; }
    }

    // ---------------- the re-scout (BUG-1460) ----------------

    /// <summary>
    /// The blind scene (as <c>BlindRescoutTests</c>): unexplored, player 0's home forest felled, gold 1,000, wood 0, three units
    /// lost (past the cap), the sweep ended. <paramref name="keepMines"/> leaves its two home mines (it knows gold).
    /// </summary>
    internal static Simulation BlindScene(bool keepMines, PlayerSeat[]? seats = null, bool noTreeAnywhere = false)
    {
        seats ??= AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quietAi.Value, explored: false);
        World w = sim.World;
        int tree = w.Data.FindResource("tree");
        for (int n = 0; n < w.Resources.Capacity; n++)
        {
            if (!w.Resources.Alive[n]) continue;
            int c = w.Resources.Cell[n], x = c % AiScenes.FlatSize, y = c / AiScenes.FlatSize;
            bool corner = x < 24 && y >= 16 && y <= 48; // player 0's corner
            if (!corner && !(noTreeAnywhere && w.Resources.TypeId[n] == tree)) continue;
            if (keepMines && w.Resources.TypeId[n] != tree) continue;
            w.Resources.Take(w.Resources.HandleOf(n), int.MaxValue);
        }
        w.Ledger.Gold[0] = 1000;
        w.Ledger.Wood[0] = 0;
        int infantry = w.Data.FindUnit("malazan_heavy_infantry");
        (int hx, int hy) = AiScenes.HallAt(0);
        for (int k = 0; k < 3; k++)
        {
            EntityHandle h = CombatScenes.Place(sim, 0, infantry, w.NavGrid.CellCenter(hx + 6, hy + k));
            CombatSystem.HitUnit(w, new PendingHit(default, 1, h, false, w.Units.Hp[h.Index]));
        }
        sim.AiOf(0)!.Board.ScoutPhase = 2;
        return sim;
    }

    /// <summary>Counts phase 2 -> 1 changes of player 0 (re-scouts) over <paramref name="ticks"/>, and the windows it was blind to wood.</summary>
    private static (int Rescouts, int BlindWindows) Watch(Simulation sim, int ticks, int from = 0)
    {
        AiPlayer ai = sim.AiOf(0)!;
        int rescouts = 0, blind = 0;
        byte phase = ai.Board.ScoutPhase;
        for (int t = 0; t < ticks; t++)
        {
            bool count = sim.TickNumber >= from;
            if (count && sim.TickNumber % Math.Max(ai.Rules.ScoutAfterTicks, ai.ThinkTicks) == 0 && ai.Board.ScoutPhase == 2 && ScoutManager.BlindTo(ai, ResourceKind.Wood)) blind++;
            sim.Tick();
            if (count && phase == 2 && ai.Board.ScoutPhase == 1) rescouts++;
            phase = ai.Board.ScoutPhase;
        }
        return (rescouts, blind);
    }

    /// <summary>
    /// BUG-1554 (M6-S14, flipped; docs/01 2026-11-17 (3)): a seat short of wood that has never seen a tree but knows gold puts
    /// every worker on gold; before M6-S14 the re-scout took only a worker left idle, so such a seat re-scouted only on the
    /// odd think a worker happened to stand idle, though docs/01 2026-11-16 (3) says such a side "sends a worker scouting
    /// again". No tree stands anywhere; its home mines are seen before the first think. The seat holds 1,000 wood (not short,
    /// so not blind) until tick 1,200, by when every worker is at work on gold; then its wood is taken. (With no wood from tick
    /// 0 its workers are still idle at the first think, an idle re-scout sweeps the whole small map and nothing is left to look
    /// at: the row as first written read 0 re-scouts for that reason as much as for BUG-1554.) From tick 1,200 over 5 minutes: blind
    /// on the windows, and a re-scout that takes a worker at work on gold (not Idle the tick before it went: the base never
    /// does), while its blind re-scouts lost are below <c>ai.scoutReplacements</c> (none dies here).
    /// </summary>
    [Fact]
    public void KnowsGold_NeverSawATree_ShortOfWood_NoIdleWorker_Rescouts_Bug1554()
    {
        Simulation sim = BlindScene(keepMines: true, noTreeAnywhere: true);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        UnitStore u = w.Units;
        w.Ledger.Wood[0] = 1000;
        // Its home mines seen before the first think (three eyes over them for one fog update), so the economy puts every
        // worker on gold from the start instead of leaving them idle until a scout finds the mines.
        (int hx, int hy) = AiScenes.HallAt(0);
        var eyes = new List<EntityHandle>();
        for (int k = 0; k < 3; k++) eyes.Add(CombatScenes.Place(sim, 0, w.Data.FindUnit("malazan_laborer"), w.NavGrid.CellCenter(hx + 2 + 3 * k, hy - 3)));
        w.Fog.Update();
        foreach (EntityHandle e in eyes) u.Free(e);
        while (sim.TickNumber < 1200) sim.Tick();
        Assert.Equal(2, ai.Board.ScoutPhase); // no re-scout while it was not short of wood
        w.Ledger.Wood[0] = 0;
        int window = Math.Max(ai.Rules.ScoutAfterTicks, ai.ThinkTicks), rescouts = 0, fromWork = 0, blind = 0;
        var before = new UnitState[u.Capacity];
        byte phase = ai.Board.ScoutPhase;
        while (sim.TickNumber < 7200)
        {
            if (sim.TickNumber % window == 0 && ai.Board.ScoutPhase == 2 && ScoutManager.BlindTo(ai, ResourceKind.Wood)) blind++;
            for (int i = 0; i < u.Capacity; i++) before[i] = u.State[i];
            sim.Tick();
            if (phase == 2 && ai.Board.ScoutPhase == 1)
            {
                rescouts++;
                if (before[ai.Board.ScoutSlot] != UnitState.Idle) fromWork++;
            }
            phase = ai.Board.ScoutPhase;
        }
        int onGold = 0, workers = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != 0 || !w.Data.Units[u.TypeId[i]].IsWorker) continue;
            workers++;
            if (u.GatherNode[i].Generation != 0) onGold++;
        }
        output.WriteLine($"from tick 1,200: blind to wood on {blind} windows of {6000 / window}; re-scouts {rescouts}, of them a worker at work {fromWork}; workers {workers}, on a gather loop {onGold}; wood {w.Wood[0]}");
        Assert.True(blind >= 10, $"precondition: the seat is blind to wood on the windows ({blind})");
        Assert.True(fromWork > 0, "no re-scout took a worker at work on gold (BUG-1554: before M6-S14, idle workers only)");
        Assert.Equal(0, ai.Board.BlindScoutsLost);
        Assert.Equal(0, w.Wood[0]);
    }

    /// <summary>
    /// QA focus "team games": the blind seat's ally (player 2, north) has its forest in sight; allies share vision (docs/02), so
    /// the seat knows a live tree and is not blind: no re-scout, its workers go to the ally's forest or gold.
    /// </summary>
    [Fact]
    public void ABlindSeatWhoseAllySeesAForest_IsNotBlind_AndSendsNoScout()
    {
        PlayerSeat[] seats =
        {
            new(0, SeatController.Ai, AiDifficulty.Normal, 1), new(1, SeatController.Human, AiDifficulty.Normal, 2),
            new(0, SeatController.Human, AiDifficulty.Normal, 1), new(1, SeatController.Human, AiDifficulty.Normal, 2),
        };
        Simulation sim = BlindScene(keepMines: false, seats);
        sim.World.Fog.Update();
        AiPlayer ai = sim.AiOf(0)!;
        sim.Tick();
        bool blind = ScoutManager.BlindTo(ai, ResourceKind.Wood);
        (int rescouts, _) = Watch(sim, 3000);
        output.WriteLine($"blind to wood after the first think: {blind}; re-scouts in 3,000 ticks: {rescouts}");
        Assert.False(blind, "the ally's forest in shared sight should count");
        Assert.Equal(0, rescouts);
    }

    /// <summary>
    /// QA focus "never reads through fog": two blind scenes alike but for a stand of trees in the far south-east corner that
    /// player 0 has never explored. Until the first cell of that stand is explored, player 0's AI sends exactly the same
    /// commands in both (the re-scout included), and the hidden stand never makes it "not blind".
    /// </summary>
    [Fact]
    public void ATreeStandTheSeatNeverSaw_ChangesNothingItSends_UntilItsGroundIsExplored()
    {
        Simulation a = BlindScene(keepMines: false), b = BlindScene(keepMines: false);
        World wb = b.World;
        int tree = wb.Data.FindResource("tree"), size = AiScenes.FlatSize;
        var stand = new List<int>();
        for (int k = 0; k < 6; k++)
        {
            int cell = (60 - k / 3) * size + 58 + k % 3;
            Assert.True(wb.Resources.Spawn(tree, cell, wb.Data.Rules.TreeWood, out _));
            stand.Add(cell);
        }
        List<Command> la = AiScenes.Log(a, 0), lb = AiScenes.Log(b, 0);
        int until = 0;
        bool sentScout = false;
        while (a.TickNumber < 12_000)
        {
            bool seen = false;
            foreach (int c in stand) seen |= b.World.Fog.IsExplored(0, c);
            if (seen) break;
            a.Tick();
            b.Tick();
            sentScout |= a.AiOf(0)!.Board.ScoutPhase == 1;
            Assert.True(ScoutManager.BlindTo(a.AiOf(0)!, ResourceKind.Wood) == ScoutManager.BlindTo(b.AiOf(0)!, ResourceKind.Wood), $"tick {a.TickNumber}: the hidden stand changed the blind answer");
            until = a.TickNumber;
        }
        int n = Math.Min(la.Count(c => c.Tick <= until), lb.Count(c => c.Tick <= until));
        output.WriteLine($"compared to tick {until}: {n} commands, re-scout sent {sentScout}");
        Assert.True(sentScout, "the scene sent no re-scout: the row proves nothing about its fog rule");
        Assert.Equal(la.Where(c => c.Tick <= until).Select(Key), lb.Where(c => c.Tick <= until).Select(Key));
    }

    private static string Key(Command c) => $"{c.Tick} {c.Kind} {c.Unit} {c.TypeId} {c.Position} {c.Target}";

    /// <summary>
    /// Criterion 10 for the whole firing path (the developer's row measures <c>BlindTo</c> alone): the ticks round a window's
    /// first think on which the blind seat lays the sweep and sends its re-scout (<c>Waypoints</c>, <c>Sweep</c>, the Move)
    /// allocate nothing. Between measurements the scout is killed and the clock run to 3 ticks before the next window.
    /// </summary>
    [Fact]
    public void TheReScoutFiring_AllocatesNothing()
    {
        Simulation sim = BlindScene(keepMines: false);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        int window = Math.Max(ai.Rules.ScoutAfterTicks, ai.ThinkTicks);
        var sent = new List<(int Slot, int Gen)>();
        void KillAndAdvance()
        {
            Blackboard b = ai.Board;
            if (b.ScoutPhase == 1 && b.ScoutSlot >= 0 && w.Units.IsAlive(new EntityHandle(b.ScoutSlot, b.ScoutGeneration)))
            {
                sent.Add((b.ScoutSlot, b.ScoutGeneration));
                CombatSystem.HitUnit(w, new PendingHit(default, 1, new EntityHandle(b.ScoutSlot, b.ScoutGeneration), false, w.Units.Hp[b.ScoutSlot]));
            }
            int next = (sim.TickNumber / window + 1) * window - 3;
            while (sim.TickNumber < next) sim.Tick();
        }
        Action block = () => { for (int t = 0; t < 30; t++) sim.Tick(); };
        KillAndAdvance();
        block(); // warm: one firing unmeasured
        int runs = AllocationProbe.AssertZero(block, output, KillAndAdvance);
        Blackboard bb = ai.Board;
        output.WriteLine($"runs {runs}; scouts sent before: {sent.Count}; phase now {bb.ScoutPhase}, scout {bb.ScoutSlot}");
        Assert.Equal(1, bb.ScoutPhase); // the measured block fired a re-scout
        Assert.True(sent.Count >= 1 && sent.TrueForAll(s => s.Slot != bb.ScoutSlot || s.Gen != bb.ScoutGeneration), "the measured block sent no new scout");
    }

    // ---------------- the rally search's map read (BUG-1461) ----------------

    /// <summary>
    /// <see cref="PlayerView.IsKnownClosed"/> against the fog: player 1's building on ground player 0 never explored reads open;
    /// once a unit of player 0 has seen it, closed; after the unit walks away (a ghost), still closed. BUG-1555 (M6-S14, flipped:
    /// before, the cell read open here): razed while out of player 0's sight, its ghost stays (player 0 still remembers it, as
    /// <see cref="PlayerView.CanPlace"/> does) and the cell still reads closed, so the rally search learns of no death it did not
    /// see; once a unit of player 0 sees the ground again (the fog drops the ghost), it reads open.
    /// </summary>
    [Fact]
    public void IsKnownClosed_FollowsTheFog_AndARazeOutOfSight_StaysClosedUntilSeen_Bug1555()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quietAi.Value, explored: false);
        World w = sim.World;
        int size = AiScenes.FlatSize, depot = StartBase.BuildingOfSlot(w.Data, seats[1].Faction, BuildingSlot.TownHall);
        Assert.True(w.Buildings.Spawn(1, depot, 56 * size + 30, out EntityHandle hidden));
        int cx = 31, cy = 57;
        Assert.True((w.NavGrid.FlagsAt(cx, cy) & NavFlags.Building) != 0, "precondition: the footprint covers the probe cell");
        w.Fog.Update();
        PlayerView v = sim.AiOf(0)!.View;
        bool never = v.IsKnownClosed(cx, cy);
        EntityHandle eye = CombatScenes.Place(sim, 0, w.Data.FindUnit("malazan_laborer"), w.NavGrid.CellCenter(30, 50));
        w.Fog.Update();
        bool seen = v.IsKnownClosed(cx, cy);
        CombatSystem.HitUnit(w, new PendingHit(default, 1, eye, false, w.Units.Hp[eye.Index]));
        sim.Tick();
        w.Fog.Update();
        bool ghost = v.IsKnownClosed(cx, cy);
        bool ghostLeft = w.Fog.Ghosts(0)[hidden.Index].Generation == hidden.Generation;
        CombatSystem.HitBuilding(w, new PendingHit(default, 0, hidden, true, w.Buildings.Hp[hidden.Index] + 1));
        sim.Tick();
        w.Fog.Update();
        bool ghostAfter = w.Fog.Ghosts(0)[hidden.Index].Generation == hidden.Generation;
        bool razedUnseen = v.IsKnownClosed(cx, cy);
        CombatScenes.Place(sim, 0, w.Data.FindUnit("malazan_laborer"), w.NavGrid.CellCenter(30, 50));
        w.Fog.Update();
        bool ghostGone = w.Fog.Ghosts(0)[hidden.Index].Generation == 0;
        bool seenAgain = v.IsKnownClosed(cx, cy);
        output.WriteLine($"never seen {never}; seen {seen}; ghost {ghost} (ghost kept {ghostLeft}); razed out of sight: closed {razedUnseen}, ghost still remembered {ghostAfter}; seen again: closed {seenAgain}, ghost dropped {ghostGone}");
        Assert.False(never);
        Assert.True(seen);
        Assert.True(ghost);
        Assert.True(ghostAfter, "precondition: player 0 still remembers the building it did not see die");
        Assert.True(razedUnseen); // BUG-1555: the remembered footprint, as CanPlace (before M6-S14: open, the leak)
        Assert.True(ghostGone, "precondition: the ground seen again drops the ghost");
        Assert.False(seenAgain);
    }
}
