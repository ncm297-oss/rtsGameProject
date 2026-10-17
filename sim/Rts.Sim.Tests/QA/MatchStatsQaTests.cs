using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Stress;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M6-S1 (session 2026-10-17-0056): the exclusions of the match stats counters through the real paths (not store seams):
/// a summon cast and its expiry; a razed building's queue refund and a defeated player's collapse (its research refunded);
/// a site killed; a worker killed with a full load; a research cancelled at the last tick; cancels at 0 % and at the last
/// tick of a training item. Plus the sample pass's cost: no allocation at interval 1, and a paired timing A/B.
/// </summary>
[Collection(SerialCollection.Name)]
public class MatchStatsQaTests
{
    private readonly ITestOutputHelper _out;

    public MatchStatsQaTests(ITestOutputHelper output) => _out = output;

    private static int Cost(World w, int type) => w.Data.Units[type].CostGold + w.Data.Units[type].CostWood;

    private static int BType(World w, int p, BuildingSlot slot) => StartBase.BuildingOfSlot(w.Data, w.FactionOf(p), slot);

    private static int UType(World w, int p, UnitSlot slot) => StartBase.UnitOfSlot(w.Data, w.FactionOf(p), slot);

    private static Vector2 Inside(World w, int k)
    {
        CombatSystem.BuildingRect(w, k, out Vector2 min, out Vector2 max);
        return (min + max) * 0.5f;
    }

    private static void Run(Simulation sim, int ticks)
    {
        for (int t = 0; t < ticks; t++) sim.Tick();
    }

    private static void AssertNothingCounted(World w, int p, string why)
    {
        Assert.True(w.GatheredGold[p] == 0 && w.GatheredWood[p] == 0, $"{why}: gathered {w.GatheredGold[p]} / {w.GatheredWood[p]}");
        Assert.True(w.UnitsTrained[p] == 0, $"{why}: trained {w.UnitsTrained[p]}");
        Assert.True(w.BuildingsBuilt[p] == 0, $"{why}: built {w.BuildingsBuilt[p]}");
    }

    // ---------- summons through the cast ----------

    [Fact]
    public void ASummonCast_IsNotTrained_CountsInArmyValueWhileItLives_AndItsExpiryCountsNothing()
    {
        GameData data = SummonTests.Data;
        var sim = TestSim.Explored(new Simulation(new SimConfig(1, 2, 64, 512) { Data = data, Combat = false, StatsSampleTicks = 1 }, LocalMovementTests.Flat(48)));
        World w = sim.World;
        int mage = data.FindUnit("malazan_cadre_mage"), infantry = data.FindUnit("malazan_heavy_infantry");
        EntityHandle caster = CombatScenes.Place(sim, 0, mage, CombatScenes.At(sim, 20, 20));
        sim.Tick();
        sim.Enqueue(Command.UseAbility(0, caster, 2, CombatScenes.At(sim, 20, 20))); // zz_test_summon_brief: 3 Heavy Infantry for 2 s
        int resolve = AbilityScenes.TickOf(sim, caster, resolved: true);
        Assert.Equal(3, SummonTests.Summoned(w).Count);
        Assert.Equal(0, w.UnitsTrained[0]);
        int value = w.Data.Units[mage].CostGold + w.Data.Units[mage].CostWood;
        Assert.Equal(value + 3 * Cost(w, infantry), w.ArmyValueSamples(0)[^1]);
        while (SummonTests.Summoned(w).Count > 0) sim.Tick();
        Assert.True(sim.TickNumber <= resolve + 60, "the summons outlived their 2 s");
        Assert.Equal(value, w.ArmyValueSamples(0)[^1]);
        Assert.Equal((0, 0, 0, 0), (w.UnitKills[0], w.UnitLosses[0], w.Kills[0], w.Losses[0]));
        Assert.Equal((0, 0), (w.UnitKills[1], w.Kills[1]));
        AssertNothingCounted(w, 0, "a summon");
    }

    // ---------- a razed building's queue, and the collapse ----------

    /// <summary>
    /// Player 1: a Town Hall (its only production building, at 1 hp), a 1-hp Barracks with two paid Trains queued, and a Forge
    /// researching. Player 0's line unit razes the Barracks (queue refunded: the totals rise, nothing gathered), then the hall:
    /// player 1 is out and the Forge collapses (its research refunded: still nothing gathered; the collapse is neither a
    /// kill nor a loss in either half).
    /// </summary>
    [Fact]
    public void ARazedQueue_AndACollapsedResearch_RefundButAreNotGathered_AndTheCollapseIsNoKillOrLoss()
    {
        Simulation sim = TestSim.Explored(CombatScenes.Flat(40, 64, 2, 3));
        World w = sim.World;
        BuildingStore b = w.Buildings;
        BuildMaps.SetTotals(sim, 1, 5000, 5000);
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        Assert.True(b.Spawn(1, BType(w, 1, BuildingSlot.TownHall), 14 * 40 + 25, out EntityHandle hall));
        Assert.True(b.Spawn(1, BType(w, 1, BuildingSlot.InfantryHall), 10 * 40 + 20, out EntityHandle barracks));
        Assert.True(b.Spawn(1, BType(w, 1, BuildingSlot.Forge), 30 * 40 + 22, out EntityHandle forge));
        Assert.True(b.Spawn(1, BType(w, 1, BuildingSlot.House), 34 * 40 + 22, out _)); // pop room
        EntityHandle unit = CombatScenes.Place(sim, 0, UType(w, 0, UnitSlot.Line), CombatScenes.At(sim, 17, 11));
        sim.Tick();
        int line1 = UType(w, 1, UnitSlot.Line);
        sim.Enqueue(Command.Train(1, Inside(w, barracks.Index), line1));
        sim.Enqueue(Command.Train(1, Inside(w, barracks.Index), line1));
        int tech = -1;
        foreach (int t in w.Data.TechsResearchableAt(b.TypeId[forge.Index]))
            if (w.CanResearch(1, forge.Index, t, out _)) { tech = t; break; }
        Assert.True(tech >= 0, "no research open at the Forge");
        sim.Enqueue(Command.Research(1, Inside(w, forge.Index), tech));
        Run(sim, 2);
        Assert.Equal(2, b.QueueCount[barracks.Index]);
        Assert.Equal(1, b.QueueCount[forge.Index]);
        b.SetHp(barracks.Index, 1);
        b.SetHp(hall.Index, 1);
        int before = w.Gold[1] + w.Wood[1];
        sim.Enqueue(Command.Attack(0, unit, barracks, isBuilding: true));
        Assert.True(CombatScenes.RunUntil(sim, () => !b.IsAlive(barracks), 600) < 600, "the Barracks stands");
        Assert.True(w.Gold[1] + w.Wood[1] > before, "no queue refund on the raze");
        Assert.Equal((1, 0, 1), (w.Kills[0], w.UnitKills[0], w.BuildingKills[0]));
        Assert.Equal((1, 0, 1), (w.Losses[1], w.UnitLosses[1], w.BuildingLosses[1]));
        AssertNothingCounted(w, 1, "the raze refund");
        before = w.Gold[1] + w.Wood[1];
        sim.Enqueue(Command.Attack(0, unit, hall, isBuilding: true));
        Assert.True(CombatScenes.RunUntil(sim, () => !b.IsAlive(hall), 1200) < 1200, "the hall stands");
        Run(sim, 2);
        Assert.False(b.IsAlive(forge), "the Forge did not collapse");
        Assert.True(w.Gold[1] + w.Wood[1] > before, "no research refund on the collapse");
        Assert.Equal((2, 0, 2), (w.Kills[0], w.UnitKills[0], w.BuildingKills[0]));
        Assert.Equal((2, 0, 2, 2), (w.Losses[1], w.UnitLosses[1], w.BuildingLosses[1], w.BuildingsLost[1]));
        Assert.Equal(-1, w.TechResearchedTick(1, tech));
        AssertNothingCounted(w, 1, "the collapse refund");
        AssertNothingCounted(w, 0, "the winner");
    }

    // ---------- a site killed, a loaded worker killed ----------

    [Fact]
    public void AKilledSite_IsABuildingKillAndLoss_NotBuilt_AndALoadedWorkerKilled_GathersNothing()
    {
        Simulation sim = TestSim.Explored(CombatScenes.Flat(40, 64, 2, 4));
        World w = sim.World;
        BuildingStore b = w.Buildings;
        UnitStore u = w.Units;
        Assert.True(b.Spawn(1, BType(w, 1, BuildingSlot.TownHall), 30 * 40 + 30, out _)); // keeps player 1 in
        Assert.True(b.Spawn(1, BType(w, 1, BuildingSlot.House), 10 * 40 + 20, out EntityHandle site, site: true));
        EntityHandle worker = CombatScenes.Place(sim, 1, UType(w, 1, UnitSlot.Worker), CombatScenes.At(sim, 15, 15));
        u.Cargo[worker.Index] = w.Data.Rules.WorkerCarry;
        u.CargoKind[worker.Index] = ResourceKind.Gold;
        u.Hp[worker.Index] = 1;
        EntityHandle unit = CombatScenes.Place(sim, 0, UType(w, 0, UnitSlot.Line), CombatScenes.At(sim, 15, 14, 0.4f));
        sim.Tick();
        sim.Enqueue(Command.Attack(0, unit, worker, isBuilding: false));
        Assert.True(CombatScenes.RunUntil(sim, () => !u.IsAlive(worker), 400) < 400, "the worker lives");
        Assert.Equal((1, 1, 0), (w.Kills[0], w.UnitKills[0], w.BuildingKills[0]));
        b.SetHp(site.Index, 1);
        sim.Enqueue(Command.Attack(0, unit, site, isBuilding: true));
        Assert.True(CombatScenes.RunUntil(sim, () => !b.IsAlive(site), 600) < 600, "the site stands");
        Assert.Equal((2, 1, 1), (w.Kills[0], w.UnitKills[0], w.BuildingKills[0]));
        Assert.Equal((2, 1, 1, 1), (w.Losses[1], w.UnitLosses[1], w.BuildingLosses[1], w.BuildingsLost[1]));
        AssertNothingCounted(w, 1, "a site and a loaded worker killed");
    }

    // ---------- cancels at 0 % and the last tick ----------

    /// <summary>
    /// A Train cancelled the tick it is queued (0 %), one cancelled with one tick left, and a research cancelled with one tick
    /// left: totals refunded, nothing trained, the tech never researched (-1). Then one trained to the end counts exactly once.
    /// </summary>
    [Fact]
    public void CancelsAtZeroAndAtTheLastTick_CountNothing_ACompletedItemCountsOnce()
    {
        Simulation sim = BuildMaps.NewSim(ResourceMaps.Flat(40, 30), units: 64);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        int keep = GatherMaps.Building(sim, 25, 10).Index;
        int bar = GatherMaps.Building(sim, 10, 10, type: ProductionMaps.Barracks).Index;
        int armory = GatherMaps.Building(sim, 10, 18, type: ResearchMaps.Armory).Index;
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        int inf = GatherMaps.Infantry;
        int ticks = w.Data.Units[inf].TrainTicks;
        // 0 %: queued and cancelled in the same tick's commands.
        sim.Enqueue(Command.Train(0, ProductionMaps.In(sim, bar), inf));
        sim.Enqueue(Command.CancelTrain(0, ProductionMaps.In(sim, bar), 0));
        Run(sim, 3);
        Assert.Equal((5000, 5000), (w.Gold[0], w.Wood[0]));
        // The last tick: a command queued now applies in the tick after next (GatherMaps.Building), so it is queued at
        // progress ticks - 2 and applies with the item one tick short of done.
        sim.Enqueue(Command.Train(0, ProductionMaps.In(sim, bar), inf));
        for (int t = 0; t < 4 * ticks && !(b.QueueCount[bar] > 0 && b.Progress[bar] == ticks - 2); t++) sim.Tick();
        Assert.Equal(ticks - 2, b.Progress[bar]);
        sim.Enqueue(Command.CancelTrain(0, ProductionMaps.In(sim, bar), 0));
        Run(sim, 2);
        Assert.Equal(0, b.QueueCount[bar]);
        Assert.Equal((5000, 5000), (w.Gold[0], w.Wood[0]));
        Assert.Equal(0, w.UnitsTrained[0]);
        // A research cancelled with one tick left.
        int tech = -1;
        foreach (int t in w.Data.TechsResearchableAt(b.TypeId[armory]))
            if (w.CanResearch(0, armory, t, out _)) { tech = t; break; }
        Assert.True(tech >= 0);
        int techTicks = w.Data.Techs[tech].ResearchTicks;
        sim.Enqueue(Command.Research(0, ProductionMaps.In(sim, armory), tech));
        for (int t = 0; t < 4 * techTicks && !(b.QueueCount[armory] > 0 && b.Progress[armory] == techTicks - 2); t++) sim.Tick();
        Assert.Equal(techTicks - 2, b.Progress[armory]);
        sim.Enqueue(Command.CancelTrain(0, ProductionMaps.In(sim, armory), 0));
        Run(sim, 2);
        Assert.False(w.HasTech(0, tech));
        Assert.Equal(-1, w.TechResearchedTick(0, tech));
        Assert.Equal((5000, 5000), (w.Gold[0], w.Wood[0]));
        Assert.Equal((0, 0, 0), (w.GatheredGold[0], w.GatheredWood[0], w.UnitsTrained[0]));
        // One trained to the end.
        sim.Enqueue(Command.Train(0, ProductionMaps.In(sim, bar), inf));
        Run(sim, ticks + 3);
        Assert.Equal(1, w.UnitsTrained[0]);
        Run(sim, 50);
        Assert.Equal(1, w.UnitsTrained[0]);
        _ = keep;
    }

    /// <summary>
    /// A site costing gold and wood (the Armory, 100 / 100; the developer's House row refunds wood only), cancelled once at 0
    /// work and once part-built: both totals come back, nothing is gathered or built. A build counting either refund line
    /// as a delivery fails here.
    /// </summary>
    [Fact]
    public void ASiteCancelledAtZeroAndPartBuilt_RefundsGoldAndWood_NothingGatheredOrBuilt()
    {
        Simulation sim = BuildMaps.NewSim(ResourceMaps.Flat(40, 30), units: 64);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        GatherMaps.Building(sim, 30, 4);
        GatherMaps.Building(sim, 30, 20, type: ProductionMaps.Barracks);
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        int armory = ResearchMaps.Armory;
        Assert.True(w.Data.Buildings[armory].CostGold > 0 && w.Data.Buildings[armory].CostWood > 0);
        EntityHandle builder = BuildMaps.WorkersRound(sim, 10, 10, 3, 3, 1)[0];
        for (int round = 0; round < 2; round++)
        {
            sim.Enqueue(Command.Build(0, builder, armory, GatherMaps.At(sim, 10, 10)));
            Run(sim, 2);
            int k = BuildMaps.SiteAt(sim, 10, 10);
            Assert.True(k >= 0 && b.UnderConstruction[k], $"round {round}: no site");
            if (round == 1)
            {
                Assert.True(CombatScenes.RunUntil(sim, () => b.Work[k] > b.WorkNeeded(b.TypeId[k]) / 3, 4000) < 4000, "no work done");
                Assert.True(b.UnderConstruction[k]);
            }
            int gold = w.Gold[0], wood = w.Wood[0];
            sim.Enqueue(Command.Cancel(0, GatherMaps.At(sim, 10, 10)));
            Run(sim, 2);
            Assert.True(BuildMaps.SiteAt(sim, 10, 10) < 0, $"round {round}: the site stands");
            Assert.True(w.Gold[0] > gold && w.Wood[0] > wood, $"round {round}: refund {w.Gold[0] - gold} gold, {w.Wood[0] - wood} wood");
            Assert.Equal((0, 0, 0), (w.GatheredGold[0], w.GatheredWood[0], w.BuildingsBuilt[0]));
        }
        Assert.Equal(0, w.Losses[0]); // a cancel is no loss
    }

    // ---------- the sample pass's cost ----------

    private static Simulation Crowd(int units, int interval, int players = 4)
    {
        var sim = new Simulation(TestSim.ConfigNoCombat(Seed: 9, PlayerCount: players, UnitCapacity: units + 8, CommandCapacity: 64) with
        {
            StatsSampleTicks = interval,
        }, LocalMovementTests.Flat(128));
        World w = sim.World;
        int n = 0;
        for (int y = 2; y < 126 && n < units; y += 2)
            for (int x = 2; x < 126 && n < units; x += 2, n++)
            {
                int p = n % players;
                int type = UType(w, p, (UnitSlot)(n % 4)); // workers, line, ranged and shock
                if (type < 0) type = UType(w, p, UnitSlot.Line);
                CombatScenes.Place(sim, p, type, CombatScenes.At(sim, x, y));
            }
        return sim;
    }

    /// <summary>Sampling every tick allocates nothing more than never sampling (2,500 units, 4 players, 400 ticks after a warm-up).</summary>
    [Fact]
    public void SamplingEveryTick_AllocatesNothingMore()
    {
        long Allocated(int interval)
        {
            Simulation sim = Crowd(2500, interval);
            Run(sim, 50);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Run(sim, 400);
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(interval == 1 ? Math.Min(450, 1024) : 0, sim.World.ArmyValueSamples(0).Length);
            return used;
        }
        long every = Allocated(1), never = Allocated(1_728_000);
        _out.WriteLine($"400 ticks, 2,500 units: allocated {every} B sampling every tick, {never} B never");
        Assert.True(every <= never, $"sampling allocates: {every} B v {never} B");
    }

    /// <summary>
    /// Paired A/B of the sample pass: the same 2,500-unit scene ticked at interval 1 (a sample every tick, the worst case) and
    /// at the maximum interval (never), interleaved in blocks. At the default 200 the cost is that difference / 200.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void TheSamplePass_CostsLittle_EvenEveryTick()
    {
        Simulation every = Crowd(2500, 1), never = Crowd(2500, 1_728_000);
        Run(every, 100);
        Run(never, 100);
        var sw = new Stopwatch();
        long tEvery = 0, tNever = 0;
        for (int block = 0; block < 20; block++)
        {
            sw.Restart();
            Run(block % 2 == 0 ? every : never, 50);
            long first = sw.ElapsedTicks;
            sw.Restart();
            Run(block % 2 == 0 ? never : every, 50);
            long second = sw.ElapsedTicks;
            if (block % 2 == 0) { tEvery += first; tNever += second; } else { tNever += first; tEvery += second; }
        }
        double msEvery = tEvery * 1000.0 / Stopwatch.Frequency / 1000, msNever = tNever * 1000.0 / Stopwatch.Frequency / 1000;
        double perSample = msEvery - msNever;
        _out.WriteLine($"2,500 units: {msEvery:F4} ms / tick sampling every tick, {msNever:F4} ms never; one sample ~{perSample * 1000:F1} us, at the default 200 ~{perSample * 1000 / 200:F3} us / tick");
        // A sample is one walk of 2,500 slots: well under 0.25 ms even on a loaded machine.
        Assert.True(perSample < 0.25, $"one sample pass costs {perSample:F4} ms");
    }

    // ---------- saturation ----------

    /// <summary>A delivery into a total at the cap still counts its full load (the total saturates, the gathered count does not lose it).</summary>
    [Fact]
    public void ADeliveryAtTheTotalsCap_CountsTheFullLoad()
    {
        Simulation sim = GatherMaps.NewSim(ResourceMaps.Flat(32, 20), units: 32);
        World w = sim.World;
        ResourceMaps.Spawn(w, ResourceMaps.Mine, 16, 8, 2500);
        GatherMaps.Building(sim, 8, 7);
        EntityHandle worker = GatherMaps.Unit(sim, GatherMaps.At(sim, 13, 6));
        BuildMaps.SetTotals(sim, 0, int.MaxValue - 1, 0);
        sim.Enqueue(Command.Gather(0, worker, GatherMaps.At(sim, 16, 8)));
        Assert.True(CombatScenes.RunUntil(sim, () => w.GatheredGold[0] > 0, 3000) < 3000, "no delivery");
        Assert.Equal(int.MaxValue, w.Gold[0]);
        Assert.Equal(w.Data.Rules.WorkerCarry, w.GatheredGold[0]);
    }
}
