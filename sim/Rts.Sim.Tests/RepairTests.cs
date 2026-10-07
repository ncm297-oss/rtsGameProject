using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-3 criterion 7: repair at <c>repair.rateFactor</c> of the one-builder rate for <c>repair.costFactor</c> of the cost, scaled by damage.</summary>
[Collection(SerialCollection.Name)]
public class RepairTests
{
    /// <summary>A Keep at 1,200 of 2,400 hp and <paramref name="n"/> workers in reach ordered to repair it; returns the ticks until it is whole, and the gold and wood spent.</summary>
    private static (int Ticks, int Gold, int Wood, Simulation Sim, EntityHandle[] Workers) RepairHalf(int n, int gold = 1000, int wood = 1000)
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle keep = Building(sim, 10, 8);
        EntityHandle[] ws = WorkersRound(sim, 10, 8, 4, 4, n);
        sim.World.Buildings.Damage(keep, 1200);
        SetTotals(sim, 0, gold, wood);
        foreach (EntityHandle w in ws) sim.Enqueue(Command.Repair(0, w, At(sim, 11, 9)));
        sim.Tick();
        sim.Tick(); // the Repairs apply, and the first repair tick runs
        int ticks = 1;
        while (sim.World.Buildings.Hp[keep.Index] < 2400 && ticks < 4000 && ws.Any(w => sim.World.Units.BuildTarget[w.Index] != default))
        {
            sim.Tick();
            ticks++;
        }
        return (ticks, gold - sim.World.Gold[0], wood - sim.World.Wood[0], sim, ws);
    }

    [Fact]
    public void OneWorker_RestoresHalfAKeep_In1800Ticks_For34GoldAnd34Wood()
    {
        RulesDef r = TestSim.Data.Rules;
        Assert.Equal((0.5f, 0.25f), (r.RepairRateFactor, r.RepairCostFactor));
        (int ticks, int gold, int wood, Simulation sim, EntityHandle[] ws) = RepairHalf(1);
        Assert.InRange(ticks, 1799, 1801); // 1,200 / (2,400 / 1,800 x 0.5)
        Assert.Equal((34, 34), (gold, wood)); // floor(0.25 x 275 x 0.5)
        Assert.Equal(2400, sim.World.Buildings.Hp[0]);
        Assert.Equal(UnitState.Idle, sim.World.Units.State[ws[0].Index]);
    }

    [Fact]
    public void TwoRepairers_HalveTheTime_AtTheSameCost()
    {
        (int ticks, int gold, int wood, _, _) = RepairHalf(2);
        Assert.InRange(ticks, 899, 901);
        Assert.Equal((34, 34), (gold, wood));
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1000, 0)]
    public void WithNothingToPayWith_RepairStopsAtOnce_AndTheWorkerIsIdle(int gold, int wood)
    {
        (int ticks, int spentGold, int spentWood, Simulation sim, EntityHandle[] ws) = RepairHalf(1, gold, wood);
        Assert.True(ticks <= 1, $"{ticks} ticks");
        Assert.Equal((0, 0), (spentGold, spentWood));
        Assert.Equal(1200, sim.World.Buildings.Hp[0]);
        Assert.Equal(UnitState.Idle, sim.World.Units.State[ws[0].Index]);
    }

    [Fact]
    public void RepairOnAWholeBuilding_ASite_OrAnEnemyBuilding_IsDropped()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 20), players: 2);
        Building(sim, 3, 3);                          // whole
        EntityHandle enemy = Building(sim, 30, 3, player: 1);
        sim.World.Buildings.Damage(enemy, 100);
        EntityHandle w = Unit(sim, At(sim, 15, 15));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 15, 10)));
        Run(sim, 2);
        EntityHandle site = sim.World.Buildings.HandleOf(SiteAt(sim, 15, 10));
        EntityHandle r = Unit(sim, At(sim, 20, 15));
        foreach (int x in new[] { 4, 15, 31 })
        {
            sim.Enqueue(Command.Repair(0, r, At(sim, x, x == 15 ? 10 : 4)));
            Run(sim, 2);
            Assert.Equal(default, sim.World.Units.BuildTarget[r.Index]);
        }
        Assert.Equal(site, sim.World.Units.BuildTarget[w.Index]);
    }
}
