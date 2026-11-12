using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-9b (BUG-1068 (a); docs/03 "Implementation (M5-9b)"): a seat whose gold is short for production's pick, with no own
/// worker at gold and no gold node it may gather known, <b>arms</b> the far expansion: it walks a worker to the nearest
/// start location no player starts on, and once it has <b>seen</b> a free gold mine within <c>ai.farExpandReach</c> (no own or
/// known enemy drop-off by it) it builds its gold-free depot there (the Town Hall only when it can pay for one). The arm
/// clears when a gold worker is at work again. Knob 0 turns the rule off.
/// </summary>
public class FarExpandTests
{
    private readonly ITestOutputHelper _out;

    public FarExpandTests(ITestOutputHelper output) => _out = output;

    /// <summary>The flat scene's start locations: players 0 and 1 on their halls, two unused ones south (2) and north (3) of the middle.</summary>
    private static readonly (int X, int Y)[] Starts = { (10, 32), (54, 32), (32, 56), (32, 8) };

    /// <summary>Where the far mine goes: by start 2, 75 m from player 0's hall and 70 m from player 1's (both past <c>ai.gatherReach</c> 64 m).</summary>
    private const int FarMineX = 33, FarMineY = 60;

    /// <summary>
    /// Malazan and Whirlwind openings whose build order sends nothing for an hour (so the bank is the scene's), with the
    /// starved wave off (this is not its test) and <c>ai.farExpandReach</c> at <paramref name="reach"/> when given.
    /// </summary>
    private static GameData FarData(double? reach = null) => AiScenes.DataWith(dir =>
    {
        foreach (string f in TestSim.ShippedFactions) // M7-H1 (BUG-1173 item 3): every faction, its own ids by slot
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                foreach (JsonNode? o in root["openings"]!.AsArray())
                    o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "build", AiScenes.BuildingKey(f, BuildingSlot.House), 0));
            });
        dir.EditJson("common/rules.json", r =>
        {
            r["ai"]!["starvedWaveAfter"] = 0;
            if (reach != null) r["ai"]!["farExpandReach"] = reach.Value;
        });
    });

    private static readonly Lazy<GameData> Shipped = new(() => FarData());
    private static readonly Lazy<GameData> Off = new(() => FarData(reach: 0));

    /// <summary>
    /// The far scene: the flat map with four start locations (<see cref="Starts"/>), a Malazan AI seat (player 0, Normal) and
    /// a Whirlwind seat (player 1, human unless <paramref name="bothAi"/>), <b>every gold mine emptied</b> before tick 0, banks
    /// 0 gold and <paramref name="wood"/> wood, the scout's sweep marked over (so only the leg walks out); with
    /// <paramref name="farMine"/> one full gold mine at (<see cref="FarMineX"/>, <see cref="FarMineY"/>), by start 2. Unexplored,
    /// so the far mine is unknown until a unit sees it.
    /// </summary>
    private static Simulation Scene(GameData data, bool farMine = true, int wood = 200, bool bothAi = false, (int X, int Y)? mineAt = null)
    {
        PlayerSeat[] seats =
        {
            new(0, SeatController.Ai, AiDifficulty.Normal),
            new(1, bothAi ? SeatController.Ai : SeatController.Human, AiDifficulty.Normal),
        };
        var starts = new StartLocation[Starts.Length];
        for (int k = 0; k < starts.Length; k++) starts[k] = new StartLocation(Starts[k].Y * AiScenes.FlatSize + Starts[k].X, 0);
        Heightmap map = LocalMovementTests.Flat(AiScenes.FlatSize).WithStartLocations(starts);
        Simulation sim = AiScenes.Flat(seats, data, map: map, explored: false);
        World w = sim.World;
        for (int i = 0; i < w.Resources.Capacity; i++)
            if (w.Resources.Alive[i] && data.Resources[w.Resources.TypeId[i]].Resource == ResourceKind.Gold)
                w.Resources.Take(w.Resources.HandleOf(i), int.MaxValue);
        for (int p = 0; p < 2; p++)
        {
            w.Ledger.Gold[p] = 0;
            w.Ledger.Wood[p] = wood;
        }
        if (farMine)
        {
            (int x, int y) = mineAt ?? (FarMineX, FarMineY);
            Assert.True(w.Resources.Spawn(data.FindResource("gold_mine"), y * AiScenes.FlatSize + x, data.Rules.ExpansionMineGold, out _));
        }
        for (int p = 0; p < 2; p++)
            if (sim.AiOf(p) is AiPlayer ai) ai.Board.ScoutPhase = 2; // the sweep is over: home has been looked at
        return sim;
    }

    /// <summary>What a run saw of player <paramref name="p"/>'s far expansion.</summary>
    private sealed class Watch
    {
        public int ArmedAt = -1, LegAt = -1, LegStart = -1, BuildAt = -1, BuildType = -1, StoodAt = -1, ClearedAt = -1, GoldAt = -1, GoldAfterStood = -1;
        public Vector2 BuildPoint;
        public bool MineSeenAtBuild;
        public int Builds, HallBuilds, Moves;
        public Action<string>? Trace;
        private byte _phase;
        private int _exp;

        public void Attach(AiPlayer ai, Simulation sim, EntityHandle farMine)
        {
            ai.SentForTests = c =>
            {
                if (c.Kind == CommandKind.Build && c.TypeId == ai.DepotType)
                {
                    Builds++;
                    if (BuildAt < 0)
                    {
                        BuildAt = ai.Now;
                        BuildType = c.TypeId;
                        BuildPoint = c.Position;
                        MineSeenAtBuild = farMine.Generation != 0 && ai.View.TryGetResource(farMine.Index, out SeenResource n) && n.Handle == farMine;
                    }
                }
                if (c.Kind == CommandKind.Build && c.TypeId == ai.TownHallType) HallBuilds++;
                if (c.Kind == CommandKind.Move && c.Unit.Index == ai.Board.FarScoutSlot) Moves++;
            };
            ai.ThoughtForTests = a =>
            {
                Blackboard b = a.Board;
                if (ArmedAt < 0 && b.FarPhase != 0) ArmedAt = a.Now;
                if (LegAt < 0 && b.FarStart >= 0)
                {
                    LegAt = a.Now;
                    LegStart = b.FarStart;
                }
                if (StoodAt < 0 && b.FarExpansions > 0 && b.FarPhase == 0) StoodAt = a.Now;
                if (Trace != null && (b.FarPhase != _phase || b.FarExpansions != _exp)) { Trace($"t{a.Now} phase {_phase}->{b.FarPhase} exp {b.FarExpansions} start {b.FarStart} gold {a.Gold} wood {a.Wood} gathering {a.Gathering(EconomyManager.Gold)}"); _phase = b.FarPhase; _exp = b.FarExpansions; }
                if (ArmedAt >= 0 && ClearedAt < 0 && b.FarPhase == 0 && b.FarExpansions == 0) ClearedAt = a.Now;
                if (GoldAt < 0 && a.Gathering(EconomyManager.Gold)) GoldAt = a.Now;
                if (StoodAt >= 0 && GoldAfterStood < 0 && a.Gathering(EconomyManager.Gold)) GoldAfterStood = a.Now;
            };
        }
    }

    private static EntityHandle MineAt(Simulation sim, int x, int y)
    {
        ResourceStore r = sim.World.Resources;
        for (int i = 0; i < r.Capacity; i++)
            if (r.Alive[i] && r.Cell[i] == y * AiScenes.FlatSize + x) return r.HandleOf(i);
        return default;
    }

    /// <summary>
    /// Criterion 3: the broke seat arms on its first think, walks a worker to the nearest unused start (2), sees the far mine
    /// there, and builds its gold-free depot (never the Town Hall: 0 gold) in gather reach of the mine, the leg's worker the
    /// builder; the depot stands, the far expansion ends, and a gold worker is then at work. The mine was in the seat's view
    /// (seen) when the Build was sent.
    /// </summary>
    [Fact]
    public void ABrokeSeat_ScoutsTheUnusedStart_AndBuildsItsDepot_ByTheFarMine()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data);
        AiPlayer ai = sim.AiOf(0)!;
        EntityHandle mine = MineAt(sim, FarMineX, FarMineY);
        Assert.False(ai.View.TryGetResource(mine.Index, out _)); // unseen at the start
        var watch = new Watch { Trace = _out.WriteLine };
        watch.Attach(ai, sim, mine);
        while (sim.TickNumber < 6000) sim.Tick();
        _out.WriteLine($"armed {watch.ArmedAt}, leg {watch.LegAt} to start {watch.LegStart}, build {watch.BuildAt} at {watch.BuildPoint}, stood {watch.StoodAt}, gold worker {watch.GoldAt} (after it stood {watch.GoldAfterStood}), moves {watch.Moves}");
        Assert.Equal("malazan_depot", data.Buildings[ai.DepotType].Key);
        Assert.Equal(0, watch.ArmedAt);
        Assert.Equal(0, watch.LegAt);
        Assert.Equal(2, watch.LegStart);
        Assert.True(watch.BuildAt > 0, "no depot ordered");
        Assert.True(watch.MineSeenAtBuild, "the depot was ordered by a mine the seat had not seen");
        Assert.Equal(ai.DepotType, watch.BuildType);
        Assert.Equal(1, watch.Builds);
        Assert.Equal(0, watch.HallBuilds);
        Vector2 mineCentre = ai.NodeCentre(data.FindResource("gold_mine"), FarMineY * AiScenes.FlatSize + FarMineX);
        Assert.True(Vector2.Distance(watch.BuildPoint, mineCentre) <= ai.Rules.GatherReach, "the depot is not in gather reach of the mine");
        Assert.True(watch.StoodAt > watch.BuildAt, "the depot never stood");
        Assert.Equal(1, ai.Board.FarExpansions);
        Assert.True(watch.GoldAfterStood >= watch.StoodAt, "no gold worker at work once the depot stood");
        Assert.Equal(0, ai.Board.FarPhase);
        Assert.Equal(-1, ai.Board.FarScoutSlot);
    }

    /// <summary>
    /// Criterion 3: a far mine the seat never sees is never built by. The mine stands in the east corner, out of the leg's
    /// sight: the leg walks start 2 then start 3 (the nearest first), marks both looked at, ends, and the seat waits armed
    /// with no Build sent.
    /// </summary>
    [Fact]
    public void AFarMine_TheSeatNeverSaw_IsNeverBuiltBy()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data, mineAt: (60, 60));
        AiPlayer ai = sim.AiOf(0)!;
        EntityHandle mine = MineAt(sim, 60, 60);
        var watch = new Watch();
        watch.Attach(ai, sim, mine);
        while (sim.TickNumber < 4000) sim.Tick();
        _out.WriteLine($"armed {watch.ArmedAt}, looked {ai.Board.FarLooked}, start {ai.Board.FarStart}, builds {watch.Builds}, seen {ai.View.TryGetResource(mine.Index, out _)}");
        Assert.False(ai.View.TryGetResource(mine.Index, out _));
        Assert.Equal(0, watch.Builds);
        Assert.Equal(FarExpansion.Armed, ai.Board.FarPhase);
        Assert.Equal((1 << 2) | (1 << 3), ai.Board.FarLooked);
        Assert.Equal(-1, ai.Board.FarStart);
        Assert.Equal(-1, ai.Board.FarScoutSlot);
    }

    /// <summary>
    /// Criterion 3: a far mine with a known enemy drop-off in gather reach of it is not built by. A Whirlwind supply cache stands
    /// beside the mine at start 2: the leg sees both and no depot (nor hall) is ordered by it. (The economy may then walk its
    /// gatherers to that mine, as before M5-9b: a gold worker at work clears the arm.)
    /// </summary>
    [Fact]
    public void AFarMine_ByAKnownEnemyDropOff_IsNotBuiltBy()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data);
        Assert.True(sim.World.Buildings.Spawn(1, data.FindBuilding("whirlwind_supply_cache"), 57 * AiScenes.FlatSize + 36, out _));
        AiPlayer ai = sim.AiOf(0)!;
        EntityHandle mine = MineAt(sim, FarMineX, FarMineY);
        var watch = new Watch();
        watch.Attach(ai, sim, mine);
        bool sawCache = false;
        while (sim.TickNumber < 4000)
        {
            sim.Tick();
            if (ai.View.TryGetResource(mine.Index, out _) && ai.Board.KnownEnemyBuildings > 0) sawCache = true;
        }
        _out.WriteLine($"looked {ai.Board.FarLooked}, start {ai.Board.FarStart}, builds {watch.Builds}, saw the cache {sawCache}");
        Assert.True(sawCache, "the leg never saw the mine and the cache");
        Assert.Equal(0, watch.Builds);
        Assert.Equal(0, watch.HallBuilds);
        Assert.Equal(0, ai.Board.FarExpansions);
        Assert.True(ai.Board.FarPhase == FarExpansion.Armed || ai.Gathering(EconomyManager.Gold), "disarmed with no gold worker");
    }

    /// <summary>
    /// Criterion 3: with under the depot's 75 wood the armed seat that knows the far mine waits: no Build is sent (no spam)
    /// until the wood is there, then exactly one.
    /// </summary>
    [Fact]
    public void ASeatShortOfTheDepotsWood_Waits_ThenBuildsOnce()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data, wood: 40);
        AiPlayer ai = sim.AiOf(0)!;
        EntityHandle mine = MineAt(sim, FarMineX, FarMineY);
        var watch = new Watch();
        watch.Attach(ai, sim, mine);
        int seenAt = -1;
        while (sim.TickNumber < 3000)
        {
            sim.Tick();
            if (seenAt < 0 && ai.View.TryGetResource(mine.Index, out _)) seenAt = sim.TickNumber;
            sim.World.Ledger.Wood[0] = Math.Min(sim.World.Ledger.Wood[0], 40); // wood held under 75
        }
        Assert.True(seenAt > 0, "the mine was never seen");
        Assert.Equal(0, watch.Builds);
        Assert.Equal(FarExpansion.Armed, ai.Board.FarPhase);
        sim.World.Ledger.Wood[0] = 200;
        while (sim.TickNumber < 4000) sim.Tick();
        _out.WriteLine($"seen {seenAt}, build at {watch.BuildAt}, builds {watch.Builds}");
        Assert.Equal(1, watch.Builds);
        Assert.True(watch.BuildAt >= 3000);
    }

    /// <summary>
    /// The drop-off the far expansion builds: the gold-free depot from data (each faction's cheapest drop-off with no gold
    /// cost), the Town Hall only when the seat can pay for one now.
    /// </summary>
    [Fact]
    public void TheDepot_IsTheGoldFreeDropOff_AndTheHallOnlyWhenPayable()
    {
        GameData data = TestSim.Data;
        Assert.Equal("malazan_depot", data.Buildings[FarExpansion.GoldFreeDropOff(data, data.Buildings[data.FindBuilding("malazan_depot")].Faction)].Key);
        Assert.Equal("whirlwind_supply_cache", data.Buildings[FarExpansion.GoldFreeDropOff(data, data.Buildings[data.FindBuilding("whirlwind_supply_cache")].Faction)].Key);
        Simulation sim = Scene(Shipped.Value);
        AiPlayer ai = sim.AiOf(0)!;
        int broke = -2, rich = -2;
        ai.ThoughtForTests = a => broke = FarExpansion.ExpansionType(a);
        sim.Tick();
        BuildingDef hall = ai.Data.Buildings[ai.TownHallType];
        sim.World.Ledger.Gold[0] = hall.CostGold + 100; // enough for a hall (and the think's other spending)
        sim.World.Ledger.Wood[0] = hall.CostWood + 100;
        ai.ThoughtForTests = a => rich = FarExpansion.ExpansionType(a);
        while (rich == -2) sim.Tick();
        Assert.Equal(ai.DepotType, broke);
        Assert.Equal(ai.TownHallType, rich);
    }

    /// <summary>
    /// Criterion 3: the arm clears when a gold worker is at work again. The armed seat walking its leg is given a gold mine by
    /// its hall: the economy sends gatherers there, the arm clears, the leg's worker is released, and no depot is built.
    /// </summary>
    [Fact]
    public void TheArm_Clears_WhenAGoldWorkerIsAtWorkAgain()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data, farMine: false);
        AiPlayer ai = sim.AiOf(0)!;
        var watch = new Watch();
        watch.Attach(ai, sim, default);
        while (sim.TickNumber < 200) sim.Tick();
        Assert.Equal(FarExpansion.Armed, ai.Board.FarPhase);
        Assert.True(ai.Board.FarScoutSlot >= 0, "no leg worker");
        (int hx, int hy) = AiScenes.HallAt(0);
        Assert.True(sim.World.Resources.Spawn(data.FindResource("gold_mine"), (hy + 1) * AiScenes.FlatSize + hx - 3, data.Rules.StartMineGold, out _)); // in the hall's sight
        while (sim.TickNumber < 1200 && watch.ClearedAt < 0) sim.Tick();
        _out.WriteLine($"armed {watch.ArmedAt}, gold worker {watch.GoldAt}, cleared {watch.ClearedAt}");
        Assert.True(watch.ClearedAt > 200, "the arm never cleared");
        Assert.True(watch.GoldAt >= 0 && watch.GoldAt <= watch.ClearedAt);
        Assert.Equal(0, ai.Board.FarPhase);
        Assert.Equal(-1, ai.Board.FarScoutSlot);
        Assert.Equal(-1, ai.Board.FarStart);
        Assert.Equal(0, watch.Builds);
        // It doesn't arm again while gold comes in.
        while (sim.TickNumber < 2400) sim.Tick();
        Assert.Equal(0, ai.Board.FarPhase);
    }

    /// <summary>
    /// Two broke seats on a symmetric map, each as far from both free starts: they walk to different ones (the tie broken by
    /// the seat's index), not both to the lowest.
    /// </summary>
    [Fact]
    public void TwoSeats_AsFarFromBothFreeStarts_WalkToDifferentOnes()
    {
        Simulation sim = Scene(Shipped.Value, farMine: false, bothAi: true);
        sim.Tick();
        AiPlayer a = sim.AiOf(0)!, b = sim.AiOf(1)!;
        _out.WriteLine($"seat 0 start {a.Board.FarStart}, seat 1 start {b.Board.FarStart}");
        Assert.Equal(2, a.Board.FarStart);
        Assert.Equal(3, b.Board.FarStart);
    }

    /// <summary>A map with no unused start (two players, two starts): the seat arms but has no leg to walk.</summary>
    [Fact]
    public void NoUnusedStart_NoLeg()
    {
        GameData data = Shipped.Value;
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Human, AiDifficulty.Normal) };
        StartLocation[] starts = { new(Starts[0].Y * AiScenes.FlatSize + Starts[0].X, 0), new(Starts[1].Y * AiScenes.FlatSize + Starts[1].X, 0) };
        Simulation sim = AiScenes.Flat(seats, data, map: LocalMovementTests.Flat(AiScenes.FlatSize).WithStartLocations(starts), explored: false);
        World w = sim.World;
        for (int i = 0; i < w.Resources.Capacity; i++)
            if (w.Resources.Alive[i] && data.Resources[w.Resources.TypeId[i]].Resource == ResourceKind.Gold)
                w.Resources.Take(w.Resources.HandleOf(i), int.MaxValue);
        w.Ledger.Gold[0] = 0;
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.ScoutPhase = 2;
        var moves = AiScenes.Log(sim, 0);
        while (sim.TickNumber < 400) sim.Tick();
        Assert.Equal(FarExpansion.Armed, ai.Board.FarPhase);
        Assert.Equal(-1, ai.Board.FarStart);
        Assert.Equal(-1, ai.Board.FarScoutSlot);
        Assert.DoesNotContain(moves, c => c.Kind == CommandKind.Move);
    }

    /// <summary>
    /// Criterion 3: with <c>ai.farExpandReach</c> 0 the same scene never arms (no leg, no depot) and the seat sends exactly the
    /// commands it sends with the rule's state forced off at every think (nothing of the rule runs).
    /// </summary>
    [Fact]
    public void KnobZero_NeverArms()
    {
        Simulation sim = Scene(Off.Value);
        AiPlayer ai = sim.AiOf(0)!;
        Assert.Equal(0f, ai.Rules.FarExpandReach);
        var log = AiScenes.Log(sim, 0);
        for (int t = 0; t < 4000; t++)
        {
            sim.Tick();
            Assert.Equal(0, ai.Board.FarPhase);
            Assert.Equal(-1, ai.Board.FarScoutSlot);
        }
        Assert.Equal(0, ai.Board.FarExpansions);
        Assert.DoesNotContain(log, c => c.Kind == CommandKind.Build && c.TypeId == ai.DepotType);
    }

    /// <summary>
    /// Criterion 3: a seat saved mid-arm (the leg walking to start 2) loads with the same far-expansion state, re-saves byte for
    /// byte, and follows the run hash for hash through the depot to the first gold worker, the seats equal after.
    /// </summary>
    [Fact]
    public void ASeatSavedMidArm_LoadsAndFollows()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data);
        AiPlayer ai = sim.AiOf(0)!;
        while (sim.TickNumber < 151) sim.Tick(); // between thinks, the leg on its way
        Assert.Equal(FarExpansion.Armed, ai.Board.FarPhase);
        Assert.Equal(2, ai.Board.FarStart);
        Assert.True(ai.Board.FarScoutSlot >= 0);
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation load = SaveTestRun.Load(data, bytes);
        AiPlayer loaded = load.AiOf(0)!;
        Assert.Equal(bytes, SaveTestRun.Save(load));
        SaveTestRun.AssertSameState(sim, load, "mid-arm");
        while (sim.TickNumber < 4000)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
        _out.WriteLine($"expansions {ai.Board.FarExpansions} / {loaded.Board.FarExpansions}, phase {ai.Board.FarPhase}");
        Assert.Equal(1, ai.Board.FarExpansions);
        SaveTestRun.AssertSameState(sim, load, "after the depot");
    }

    /// <summary>
    /// M5-H6 (BUG-1158; failed first: the leg sent a fresh worker after every death, and all five died): each leg worker is
    /// killed as soon as it holds the leg. It is replaced while the seat's own unit losses are at most
    /// <c>ai.scoutReplacements</c> (the scout's cap, BUG-1052); the death past that ends the leg, every start counted looked.
    /// So <c>scoutReplacements</c> + 1 workers are lost to it and the rest stay at home; the seat waits armed.
    /// </summary>
    [Fact]
    public void ALegWorker_Killed_IsReplacedOnlyWithinTheScoutCap_ThenTheLegEnds()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data, farMine: false);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        int workers = Workers(sim, 0), killed = 0;
        while (sim.TickNumber < 6000)
        {
            sim.Tick();
            var leg = new EntityHandle(ai.Board.FarScoutSlot, ai.Board.FarScoutGeneration);
            if (ai.Board.FarScoutSlot < 0 || !w.Units.IsAlive(leg) || killed > 10) continue;
            CombatSystem.HitUnit(w, new PendingHit(default, 1, leg, false, w.Units.Hp[leg.Index]));
            killed++;
        }
        _out.WriteLine($"leg workers killed {killed}, workers {Workers(sim, 0)} of {workers}, losses {ai.View.OwnUnitLosses}, looked {ai.Board.FarLooked}, phase {ai.Board.FarPhase}");
        Assert.Equal(ai.Rules.ScoutReplacements + 1, killed);
        Assert.Equal(workers - killed, Workers(sim, 0));
        Assert.Equal((1 << ai.View.StartCount) - 1, ai.Board.FarLooked);
        Assert.Equal(-1, ai.Board.FarStart);
        Assert.Equal(-1, ai.Board.FarScoutSlot);
        Assert.Equal(FarExpansion.Armed, ai.Board.FarPhase);
    }

    /// <summary>
    /// M5-H6 (BUG-1159 item 1; failed first: <see cref="FarExpansion.NextStart"/> read no reach): a free start whose centre is
    /// farther from the hall than <c>ai.farExpandReach</c> is not walked to (no mine there could be a candidate). Player 0's
    /// hall lies 65.1 m from start 3's centre and 66.5 m from start 2's: at 64 m neither is next, at 66 m only start 3, at
    /// 67 m both, and the whole-cell tie goes to the seat's turn (start 2), as before.
    /// </summary>
    [Theory]
    [InlineData(64.0, -1)]
    [InlineData(66.0, 3)]
    [InlineData(67.0, 2)]
    public void TheLeg_WalksOnlyToAStartWithinTheReach(double reach, int next)
    {
        Simulation sim = Scene(FarData(reach), farMine: false);
        AiPlayer ai = sim.AiOf(0)!;
        sim.Tick();
        float d2 = Vector2.Distance(ai.CellCentre(ai.View.StartCell(2)), ai.HallPoint), d3 = Vector2.Distance(ai.CellCentre(ai.View.StartCell(3)), ai.HallPoint);
        _out.WriteLine($"start 2 at {d2:F1} m, start 3 at {d3:F1} m; reach {reach}: next {FarExpansion.NextStart(ai)}, walking {ai.Board.FarStart}");
        Assert.InRange(d3, 64.5f, 65.5f);
        Assert.InRange(d2, 66.1f, 66.9f);
        Assert.Equal(next, FarExpansion.NextStart(ai));
        Assert.Equal(next, ai.Board.FarStart);
    }

    /// <summary>
    /// M5-H6 (BUG-1159 item 2; failed first: the start was dropped unmarked, so the next arm walked start 2 from its centre
    /// again): the leg that sees the far mine part way round start 2 keeps that start and its look point while the depot goes
    /// up and after; once the mine is spent and the seat arms again, the first leg Move goes to that look point, not the
    /// start's centre (unless the mine was seen on the way to the centre, where the two are one).
    /// </summary>
    [Fact]
    public void ALegEnded_OnACandidate_KeepsItsStart_AndTheNextArmWalksOnFromItsPoint()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data);
        AiPlayer ai = sim.AiOf(0)!;
        while (sim.TickNumber < 6000) sim.Tick();
        Assert.Equal(1, ai.Board.FarExpansions);
        Assert.Equal(0, ai.Board.FarPhase);
        Assert.Equal(2, ai.Board.FarStart);
        Assert.Equal(0, ai.Board.FarLooked);
        Assert.Equal(-1, ai.Board.FarScoutSlot);
        int look = ai.Board.FarLook;
        Vector2 expected = FarExpansion.LookPoint(ai, 2, look), first = new(-1, -1);
        int armedAt = -1;
        ai.SentForTests = c =>
        {
            if (c.Kind == CommandKind.Move && c.Unit.Index == ai.Board.FarScoutSlot && first.X < 0) first = c.Position;
        };
        ai.ThoughtForTests = a => { if (armedAt < 0 && a.Board.FarPhase != 0) armedAt = a.Now; };
        sim.World.Resources.Take(MineAt(sim, FarMineX, FarMineY), int.MaxValue); // the far mine spent
        sim.World.Ledger.Gold[0] = 0; // and its gold with it: broke again
        while (sim.TickNumber < 12_000 && first.X < 0) sim.Tick();
        _out.WriteLine($"kept look {look}, armed again {armedAt}, first leg Move {first} (look point {expected}), looked {ai.Board.FarLooked}");
        Assert.True(armedAt > 0, "the seat did not arm again");
        Assert.Equal(expected, first);
    }

    private static int Workers(Simulation sim, int p)
    {
        int n = 0;
        for (int i = 0; i < sim.World.Units.Capacity; i++)
            if (sim.World.Units.Alive[i] && sim.World.Units.Owner[i] == p && sim.World.Data.Units[sim.World.Units.TypeId[i]].IsWorker) n++;
        return n;
    }

    /// <summary>
    /// The knob-0 twin on one whole Normal v Normal match (Vathar Crossing seed 1, no seat arms there): the same commands with
    /// <c>ai.farExpandReach</c> 0 as shipped, to the same end tick.
    /// </summary>
    [Fact]
    public void AWholeMatch_WithNoSeatArmed_SendsTheSameCommands_AtKnobZero()
    {
        var shipped = Whole(TestSim.Data);
        var off = Whole(AiScenes.DataWith(dir => dir.EditJson("common/rules.json", r => r["ai"]!["farExpandReach"] = 0)));
        _out.WriteLine($"end {shipped.End} / {off.End}, armed {shipped.Armed} / {off.Armed}, commands {shipped.Logs[0].Count + shipped.Logs[1].Count}");
        Assert.False(shipped.Armed);
        Assert.False(off.Armed);
        Assert.Equal(shipped.End, off.End);
        for (int p = 0; p < 2; p++) Assert.True(shipped.Logs[p].SequenceEqual(off.Logs[p]), $"seat {p}'s commands differ");
    }

    private static (List<Command>[] Logs, int End, bool Armed) Whole(GameData data)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        int size = data.Maps[data.FindMap("vathar_crossing")].Size;
        Simulation sim = AiScenes.Generated(seats, 1, false, out _, data: data,
            configure: c => c with { MapId = "vathar_crossing", Map = c.Map with { Width = size, Height = size } });
        var logs = new[] { AiScenes.Log(sim, 0), AiScenes.Log(sim, 1) };
        bool armed = false;
        while (sim.TickNumber < 36_000 && !sim.MatchOver)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++) armed |= sim.AiOf(p)!.Board.FarPhase != 0;
        }
        Assert.True(sim.MatchOver, "the match did not end");
        return (logs, sim.TickNumber, armed);
    }
}
