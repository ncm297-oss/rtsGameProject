using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResearchMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S1 criteria 1-3: the post-match stats counters (docs/03 "Implementation (M6-S1)"). Each counter counts its event and
/// not the excluded path: a delivery v a refund, a start total and a dev grant; a trained unit v a summon v a dev spawn; the
/// kill split for a unit and a building v an expiry; a completed construction v the start hall; the researched tick; and
/// the army-value samples (cadence, the sum, workers excluded, the capacity, a custom interval).
/// </summary>
[Collection(SerialCollection.Name)]
public class MatchStatsTests
{
    // ---------- (a) gathered resources ----------

    // 32 x 20 flat map: a Garrison Keep on cells 8-11 x 7-10 and a 2 x 2 mine on cells 16-17 x 8-9, as GatherLoopTests.
    private static (Simulation Sim, EntityHandle Worker) KeepAndNode(bool tree)
    {
        Simulation sim = GatherMaps.NewSim(Flat(32, 20), units: 32);
        if (tree) Spawn(sim.World, Tree, 16, 8, TreeWood);
        else Spawn(sim.World, Mine, 16, 8, 2500);
        Building(sim, 8, 7);
        EntityHandle w = Unit(sim, At(sim, 13, 6));
        sim.Enqueue(Command.Gather(0, w, At(sim, 16, 8)));
        return (sim, w);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADelivery_CountsAsGathered_ExactlyTheTotalsRise(bool tree)
    {
        (Simulation sim, _) = KeepAndNode(tree);
        World w = sim.World;
        int startGold = w.Gold[0], startWood = w.Wood[0];
        Assert.Equal((0, 0), (w.GatheredGold[0], w.GatheredWood[0])); // the starting totals count nothing
        int ran = CombatScenes.RunUntil(sim, () => w.Gold[0] + w.Wood[0] > startGold + startWood, 3000);
        Assert.True(ran < 3000, "no delivery");
        Assert.Equal(w.Gold[0] - startGold, w.GatheredGold[0]);
        Assert.Equal(w.Wood[0] - startWood, w.GatheredWood[0]);
        Assert.Equal(TestSim.Data.Rules.WorkerCarry, tree ? w.GatheredWood[0] : w.GatheredGold[0]);
        Assert.Equal(0, tree ? w.GatheredGold[0] : w.GatheredWood[0]);
        // Several loops later the count still tracks the totals (nothing is spent in this scene).
        Run(sim, 2000);
        Assert.True(w.GatheredGold[0] + w.GatheredWood[0] >= 3 * TestSim.Data.Rules.WorkerCarry);
        Assert.Equal((w.Gold[0] - startGold, w.Wood[0] - startWood), (w.GatheredGold[0], w.GatheredWood[0]));
    }

    [Fact]
    public void Refunds_AndADevGrant_AreNotGathered()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        World w = sim.World;
        int keep = Building(sim, 20, 4).Index;
        SetTotals(sim, 0, 2000, 2000); // the dev path: the totals move, the gathered count doesn't
        Assert.Equal((0, 0), (w.GatheredGold[0], w.GatheredWood[0]));
        // A training refund (BuildingStore.Refund): queue a worker, cancel it.
        sim.Enqueue(Command.Train(0, In(sim, keep), Laborer));
        Run(sim, 2);
        Assert.True(w.Gold[0] < 2000 || w.Wood[0] < 2000);
        sim.Enqueue(Command.CancelTrain(0, In(sim, keep), 0));
        Run(sim, 2);
        Assert.Equal((2000, 2000), (w.Gold[0], w.Wood[0]));
        // A construction refund (ConstructionSystem.ApplyCancel): place a site, cancel it at no work.
        EntityHandle builder = WorkersRound(sim, 10, 10, 2, 2, 1)[0];
        sim.Enqueue(Command.Build(0, builder, House, At(sim, 10, 10)));
        Run(sim, 2);
        Assert.True(SiteAt(sim, 10, 10) >= 0);
        int goldBefore = w.Gold[0], woodBefore = w.Wood[0];
        sim.Enqueue(Command.Cancel(0, At(sim, 10, 10)));
        Run(sim, 2);
        Assert.True(w.Gold[0] + w.Wood[0] > goldBefore + woodBefore, "no refund");
        Assert.Equal((0, 0), (w.GatheredGold[0], w.GatheredWood[0]));
        // M5-H3 (BUG-0850): the House costs wood only, so the site refund's gold line never ran here. An Armory site costs
        // both (100 / 100): cancelled, both totals rise and neither is gathered.
        int armory = ResearchMaps.Armory;
        Assert.True(w.Data.Buildings[armory].CostGold > 0 && w.Data.Buildings[armory].CostWood > 0);
        sim.Enqueue(Command.Build(0, builder, armory, At(sim, 10, 10)));
        Run(sim, 2);
        Assert.True(SiteAt(sim, 10, 10) >= 0, "no Armory site");
        goldBefore = w.Gold[0];
        woodBefore = w.Wood[0];
        sim.Enqueue(Command.Cancel(0, At(sim, 10, 10)));
        Run(sim, 2);
        Assert.True(w.Gold[0] > goldBefore && w.Wood[0] > woodBefore, $"refund {w.Gold[0] - goldBefore} gold, {w.Wood[0] - woodBefore} wood");
        Assert.Equal((0, 0), (w.GatheredGold[0], w.GatheredWood[0]));
    }

    // ---------- (b) units trained ----------

    [Fact]
    public void AFinishedQueueItem_CountsAsTrained_ASummonAndADevSpawnDoNot()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 30), units: 64);
        World w = sim.World;
        Building(sim, 25, 10);
        int bar = Building(sim, 10, 10, type: ProductionMaps.Barracks).Index;
        SetTotals(sim, 0, 5000, 5000);
        Unit(sim, At(sim, 30, 25), type: Infantry); // a dev spawn
        Assert.True(w.Units.TrySpawn(0, Infantry, w.Data.Units[Infantry], At(sim, 32, 25), sim.TickNumber + 100_000, out _)); // a summon (outlives the scene)
        Run(sim, 2);
        Assert.Equal(0, w.UnitsTrained[0]);
        sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        int units = w.LiveUnits(0);
        int ticks = w.Data.Units[Infantry].TrainTicks;
        Run(sim, 1 + ticks);
        Assert.Equal((units + 1, 1), (w.LiveUnits(0), w.UnitsTrained[0]));
        Run(sim, ticks);
        Assert.Equal((units + 2, 2), (w.LiveUnits(0), w.UnitsTrained[0]));
    }

    /// <summary>
    /// M5-H3 (BUG-0850): a summon made by a real cast (<c>SummonSystem</c> through an ability's resolve, not the store seam
    /// above): the test data's <c>zz_test_summon_brief</c> on a Cadre Mage, 3 Heavy Infantry for 2 s. They live and then
    /// expire; none counts as trained, and the expiry is no loss.
    /// </summary>
    [Fact]
    public void ASummonFromARealCast_IsNotTrained_AndItsExpiryIsNoLoss()
    {
        GameData data = SummonTests.Data;
        var sim = TestSim.Explored(new Simulation(new SimConfig(1, 2, 64, 512) { Data = data, Combat = false }, LocalMovementTests.Flat(48)));
        World w = sim.World;
        EntityHandle caster = CombatScenes.Place(sim, 0, data.FindUnit("malazan_cadre_mage"), CombatScenes.At(sim, 20, 20));
        sim.Tick();
        sim.Enqueue(Command.UseAbility(0, caster, 2, CombatScenes.At(sim, 20, 20))); // the mage's third ability: the brief summon
        AbilityScenes.TickOf(sim, caster, resolved: true);
        Assert.Equal(3, SummonTests.Summoned(w).Count);
        Assert.Equal(0, w.UnitsTrained[0]);
        for (int t = 0; t < 100 && SummonTests.Summoned(w).Count > 0; t++) sim.Tick();
        Assert.Empty(SummonTests.Summoned(w));
        Assert.Equal((0, 0, 0), (w.UnitsTrained[0], w.Losses[0], w.UnitLosses[0]));
    }

    // ---------- (c) kills and losses, split ----------

    [Fact]
    public void AUnitKill_CountsInTheUnitHalves_ABuildingKillInTheBuildingHalves()
    {
        Simulation sim = CombatScenes.Flat(32, players: 2);
        World w = sim.World;
        UnitStore u = w.Units;
        // A unit: two Heavy Infantry face to face, the victim at 1 hp.
        EntityHandle[] h = CombatScenes.Spawn(sim, (0, Infantry, CombatScenes.At(sim, 10, 10)), (1, Infantry, CombatScenes.At(sim, 10, 10, 1.2f)));
        u.Hp[h[1].Index] = 1;
        Assert.True(CombatScenes.RunUntil(sim, () => !u.IsAlive(h[1]), 400) < 400, "the victim lives");
        Assert.Equal((1, 1, 0), (w.Kills[0], w.UnitKills[0], w.BuildingKills[0]));
        Assert.Equal((1, 1, 0), (w.Losses[1], w.UnitLosses[1], w.BuildingLosses[1]));
        Assert.Equal((0, 0), (w.Kills[1], w.Losses[0]));
        // A building: player 1's Keep at 1 hp, attacked by the winner.
        EntityHandle hall = Building(sim, 13, 8, player: 1); // in the winner's sight
        w.Buildings.SetHp(hall.Index, 1);
        sim.Enqueue(Command.Attack(0, h[0], hall, isBuilding: true));
        Assert.True(CombatScenes.RunUntil(sim, () => !w.Buildings.IsAlive(hall), 1000) < 1000, "the hall stands");
        Assert.Equal((2, 1, 1), (w.Kills[0], w.UnitKills[0], w.BuildingKills[0]));
        Assert.Equal((2, 1, 1), (w.Losses[1], w.UnitLosses[1], w.BuildingLosses[1]));
        Assert.Equal(w.BuildingLosses[1], w.BuildingsLost[1]);
    }

    [Fact]
    public void AnExpiry_CountsNoKillAndNoLoss_InEitherHalf()
    {
        Simulation sim = CombatScenes.Flat(32, players: 2);
        World w = sim.World;
        Assert.True(w.Units.TrySpawn(0, Infantry, w.Data.Units[Infantry], CombatScenes.At(sim, 10, 10), 5, out EntityHandle s));
        Run(sim, 10);
        Assert.False(w.Units.IsAlive(s));
        Assert.Equal((0, 0, 0, 0, 0, 0), (w.Kills[0], w.Losses[0], w.UnitKills[0], w.UnitLosses[0], w.BuildingKills[0], w.BuildingLosses[0]));
    }

    [Fact]
    public void RecordDeath_SplitsByIsBuilding_AndSkipsExpiriesAndCollapses()
    {
        Simulation sim = CombatScenes.Flat(32, players: 2);
        World w = sim.World;
        w.RecordDeath(new DeathEvent(default, false, 0, 1, 0, Vector2.Zero));
        w.RecordDeath(new DeathEvent(default, true, 0, 1, 0, Vector2.Zero));
        w.RecordDeath(new DeathEvent(default, true, 0, 1, 0, Vector2.Zero));
        w.RecordDeath(new DeathEvent(default, false, 0, 0, -1, Vector2.Zero, Expired: true));
        w.RecordDeath(new DeathEvent(default, true, 0, 0, -1, Vector2.Zero, Collapsed: true));
        Assert.Equal((3, 1, 2), (w.Kills[0], w.UnitKills[0], w.BuildingKills[0]));
        Assert.Equal((3, 1, 2), (w.Losses[1], w.UnitLosses[1], w.BuildingLosses[1]));
        Assert.Equal((0, 0, 0), (w.Losses[0], w.UnitLosses[0], w.BuildingLosses[0]));
    }

    // ---------- (d) buildings built ----------

    [Fact]
    public void ACompletedConstruction_CountsAsBuilt_TheStartHallDoesNot()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        World w = sim.World;
        Building(sim, 20, 4); // spawned finished, as the start hall is
        Assert.Equal(0, w.BuildingsBuilt[0]);
        SetTotals(sim, 0, 1000, 1000);
        EntityHandle builder = WorkersRound(sim, 10, 10, 2, 2, 1)[0];
        sim.Enqueue(Command.Build(0, builder, House, At(sim, 10, 10)));
        Run(sim, 2);
        int k = SiteAt(sim, 10, 10);
        Assert.True(k >= 0 && w.Buildings.UnderConstruction[k]);
        Assert.Equal(0, w.BuildingsBuilt[0]); // a site isn't built yet
        Assert.True(CombatScenes.RunUntil(sim, () => !w.Buildings.UnderConstruction[k], 5000) < 5000, "never completed");
        Assert.Equal(1, w.BuildingsBuilt[0]);
        Run(sim, 20);
        Assert.Equal(1, w.BuildingsBuilt[0]); // counted once
    }

    // ---------- (e) the researched tick ----------

    [Fact]
    public void TechResearchedTick_IsTheTickHasTechFirstReadsTrue_MinusOneBefore()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 30), units: 64, players: 2);
        World w = sim.World;
        int keep = Building(sim, 25, 10).Index;
        Building(sim, 10, 10, type: ResearchMaps.Armory); // Age II's requirements, as ResearchTests' scene
        Building(sim, 10, 18, type: ProductionMaps.Barracks);
        SetTotals(sim, 0, 5000, 5000);
        Assert.True(w.CanResearch(0, keep, AgeII, out _));
        sim.Enqueue(Command.Research(0, In(sim, keep), AgeII));
        int first = -1;
        for (int t = 0; t < 2000 && first < 0; t++)
        {
            Assert.Equal(-1, w.TechResearchedTick(0, AgeII));
            sim.Tick();
            if (w.HasTech(0, AgeII)) first = sim.TickNumber;
        }
        Assert.True(first > 0, "never researched");
        Assert.Equal(first, w.TechResearchedTick(0, AgeII));
        Run(sim, 50);
        Assert.Equal(first, w.TechResearchedTick(0, AgeII)); // it keeps the first tick
        Assert.Equal(-1, w.TechResearchedTick(1, AgeII));
        Assert.Equal(-1, w.TechResearchedTick(0, Melee1));
        Assert.Equal(-1, w.TechResearchedTick(0, -1));
        Assert.Equal(-1, w.TechResearchedTick(0, w.Data.Techs.Length));
        Assert.Equal(-1, w.TechResearchedTick(-1, AgeII));
        Assert.Equal(-1, w.TechResearchedTick(2, AgeII));
    }

    // ---------- (f) army-value samples ----------

    private static Simulation SampleScene(int interval = 200, int capacity = 1024) =>
        new(TestSim.ConfigNoCombat(Seed: 3, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512) with
        {
            StatsSampleTicks = interval,
            StatsSampleCapacity = capacity,
        }, LocalMovementTests.Flat(32));

    private static int Cost(int type) => TestSim.Data.Units[type].CostGold + TestSim.Data.Units[type].CostWood;

    [Fact]
    public void Samples_AtTicks200And400_HoldTheLiveNonWorkerCost_WorkersExcluded()
    {
        Simulation sim = SampleScene();
        World w = sim.World;
        int xbow = CombatScenes.Crossbowman;
        Assert.Equal(200, w.StatsSampleTicks);
        Assert.True(TestSim.Data.Units[Laborer].IsWorker && TestSim.Data.Units[CampFollower].IsWorker && !TestSim.Data.Units[xbow].IsWorker);
        sim.Enqueue(Command.SpawnUnit(0, Infantry, CombatScenes.At(sim, 5, 5)));
        sim.Enqueue(Command.SpawnUnit(0, Infantry, CombatScenes.At(sim, 6, 5)));
        sim.Enqueue(Command.SpawnUnit(0, Laborer, CombatScenes.At(sim, 7, 5)));
        sim.Enqueue(Command.SpawnUnit(1, xbow, CombatScenes.At(sim, 25, 25)));
        sim.Enqueue(Command.SpawnUnit(1, CampFollower, CombatScenes.At(sim, 26, 25)));
        Run(sim, 199);
        Assert.Equal(0, w.ArmyValueSamples(0).Length);
        sim.Tick(); // TickNumber 200
        Assert.Equal(new[] { 2 * Cost(Infantry) }, w.ArmyValueSamples(0).ToArray());
        Assert.Equal(new[] { Cost(xbow) }, w.ArmyValueSamples(1).ToArray());
        // An Infantry goes, a Crossbowman joins player 0, and player 1 gets a summon (it counts while it lives).
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && u.TypeId[i] == Infantry) { u.Free(new EntityHandle(i, u.Generation[i])); break; }
        sim.Enqueue(Command.SpawnUnit(0, xbow, CombatScenes.At(sim, 8, 5)));
        Assert.True(u.TrySpawn(1, Infantry, w.Data.Units[Infantry], CombatScenes.At(sim, 25, 20), sim.TickNumber + 1000, out _));
        Run(sim, 199);
        Assert.Equal(1, w.ArmyValueSamples(0).Length);
        sim.Tick(); // 400
        Assert.Equal(new[] { 2 * Cost(Infantry), Cost(Infantry) + Cost(xbow) }, w.ArmyValueSamples(0).ToArray());
        Assert.Equal(new[] { Cost(xbow), Cost(xbow) + Cost(Infantry) }, w.ArmyValueSamples(1).ToArray());
        Assert.Equal(0, w.ArmyValueSamples(-1).Length);
        Assert.Equal(0, w.ArmyValueSamples(2).Length);
    }

    [Fact]
    public void Samples_StopAtCapacity_WithoutAFault()
    {
        Simulation sim = SampleScene(interval: 1, capacity: 3);
        World w = sim.World;
        sim.Enqueue(Command.SpawnUnit(0, Infantry, CombatScenes.At(sim, 5, 5)));
        Run(sim, 10);
        Assert.Equal(new[] { 0, Cost(Infantry), Cost(Infantry) }, w.ArmyValueSamples(0).ToArray()); // tick 1: the spawn applies in tick 1, sampled after it
        Assert.Equal(3, w.StatsSampleCapacity);
        Simulation none = SampleScene(interval: 1, capacity: 0);
        Run(none, 5);
        Assert.Equal(0, none.World.ArmyValueSamples(0).Length);
    }

    [Fact]
    public void AFiftyTickConfig_SamplesAt50()
    {
        Simulation sim = SampleScene(interval: 50);
        World w = sim.World;
        Assert.Equal(50, w.StatsSampleTicks);
        sim.Enqueue(Command.SpawnUnit(1, Infantry, CombatScenes.At(sim, 5, 5)));
        Run(sim, 49);
        Assert.Equal(0, w.ArmyValueSamples(1).Length);
        sim.Tick();
        Assert.Equal(new[] { Cost(Infantry) }, w.ArmyValueSamples(1).ToArray());
        Run(sim, 100);
        Assert.Equal(3, w.ArmyValueSamples(1).Length);
    }

    [Fact]
    public void TheSampleConfig_IsValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SampleScene(interval: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SampleScene(capacity: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SampleScene(capacity: 1_000_001));
    }

    // ---------- not hashed ----------

    [Fact]
    public void TheCounters_AreNotHashed()
    {
        Simulation a = SampleScene(interval: 1), b = SampleScene(interval: 1000);
        a.Enqueue(Command.SpawnUnit(0, Infantry, CombatScenes.At(a, 5, 5)));
        b.Enqueue(Command.SpawnUnit(0, Infantry, CombatScenes.At(b, 5, 5)));
        Run(a, 20);
        Run(b, 20);
        a.World.Ledger.Stats.CountTrained(0);
        a.World.Ledger.Stats.CountBuilt(0);
        a.World.Ledger.Stats.AddGathered(0, ResourceKind.Gold, 10);
        a.World.Ledger.Stats.MarkResearched(0, AgeII, 5);
        Assert.Equal(20, a.World.ArmyValueSamples(0).Length);
        Assert.Equal(b.StateHash(), a.StateHash());
    }
}
