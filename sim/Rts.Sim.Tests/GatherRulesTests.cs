using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Stress;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-2 criteria 5-7: the exposure rule (BUG-0075), the depleted-node rule and crowding at one mine.</summary>
public class GatherRulesTests
{
    private readonly ITestOutputHelper _out;

    public GatherRulesTests(ITestOutputHelper output) => _out = output;

    // ---------------------------------------------------------------- exposure (criterion 5)

    /// <summary>24 x 16 flat map: a 3 x 3 grove at cells 10-12 x 6-8 (slots 0-8 row by row, the middle is slot 4) holding <paramref name="wood"/> each, and a Keep at cells 2-5 x 6-9.</summary>
    private static Simulation Grove(int wood, out EntityHandle[] trees)
    {
        Simulation sim = GatherMaps.NewSim(Flat(24, 16));
        trees = new EntityHandle[9];
        for (int y = 6; y <= 8; y++)
            for (int x = 10; x <= 12; x++)
                trees[(y - 6) * 3 + x - 10] = Spawn(sim.World, Tree, x, y, wood);
        Building(sim, 2, 6);
        return sim;
    }

    [Fact]
    public void AnInteriorTree_IsNeverGatheredWhileItsFourNeighborsStand_TheWorkerIsRedirected()
    {
        Simulation sim = Grove(TreeWood, out EntityHandle[] trees);
        EntityHandle w = Unit(sim, At(sim, 7, 7));
        sim.Enqueue(Command.Gather(0, w, At(sim, 11, 7))); // the middle tree
        Run(sim, 2);
        UnitStore u = sim.World.Units;
        Assert.Equal(trees[1], u.GatherNode[w.Index]); // nearest exposed tree of the grove to the middle, ties by slot
        Assert.False(EconomySystem.IsExposed(sim.World, trees[4].Index));
        Run(sim, 3000);
        Assert.Equal(TreeWood, sim.World.Resources.Remaining[trees[4].Index]);
        Assert.True(sim.World.Wood[0] > 200, $"wood {sim.World.Wood[0]}");
    }

    [Fact]
    public void AWorkerSetLooseOnAGrove_FellsAllNine_AndEveryPassableCellStaysReachableAfterEachFall()
    {
        Simulation sim = Grove(10, out EntityHandle[] trees);
        EntityHandle w = Unit(sim, At(sim, 7, 7));
        sim.Enqueue(Command.Gather(0, w, At(sim, 11, 7)));
        ResourceStore r = sim.World.Resources;
        int version = sim.World.NavGrid.Version, felled = 0;
        for (int t = 0; t < 20000 && felled < 9; t++)
        {
            sim.Tick();
            if (sim.World.NavGrid.Version == version) continue;
            version = sim.World.NavGrid.Version;
            felled = trees.Count(h => !r.IsAlive(h));
            Assert.Null(ResourceOracle.Reach(sim.World.NavGrid));
            if (r.IsAlive(trees[4])) continue;
            Assert.True(felled >= 2, "the middle tree fell before any neighbor");
        }
        Assert.Equal(9, felled);
        Assert.Equal(200 + 90, sim.World.Wood[0] + sim.World.Units.Cargo[w.Index]);
    }

    // ---------------------------------------------------------------- depletion (criterion 6)

    // 40 x 20 flat map: a Keep at cells 2-5 x 8-11, mines at cells 14-15 x 9-10 and 20-21 x 9-10 (centers 12 m apart).
    private static Simulation TwoMines(int firstGold, bool second, out EntityHandle first, out EntityHandle other)
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 20));
        first = Spawn(sim.World, Mine, 14, 9, firstGold);
        other = second ? Spawn(sim.World, Mine, 20, 9, 2500) : default;
        Building(sim, 2, 8);
        return sim;
    }

    [Fact]
    public void WhenAMineRunsOut_ItsWorkersMoveToTheNearestMineWithin20m()
    {
        Simulation sim = TwoMines(25, second: true, out EntityHandle first, out EntityHandle other);
        var w = new[] { Unit(sim, At(sim, 12, 9)), Unit(sim, At(sim, 12, 10)) };
        foreach (EntityHandle h in w) sim.Enqueue(Command.Gather(0, h, At(sim, 14, 9)));
        UnitStore u = sim.World.Units;
        for (int t = 0; t < 3000 && sim.World.Resources.IsAlive(first); t++) sim.Tick();
        Assert.False(sim.World.Resources.IsAlive(first));
        // A worker walking in reaches the dead mine's replacement at once; one carrying a load after its deposit.
        for (int t = 0; t < 600 && w.Any(h => u.GatherNode[h.Index] != other); t++) sim.Tick();
        foreach (EntityHandle h in w) Assert.Equal(other, u.GatherNode[h.Index]);
        Run(sim, 1200);
        Assert.True(sim.World.Resources.Remaining[other.Index] < 2500);
        Assert.True(sim.World.Gold[0] >= 200 + 25, $"gold {sim.World.Gold[0]}");
    }

    [Fact]
    public void WhenAMineRunsOutWithNoneWithin20m_TheWorkerGoesIdleKeepingItsCargo()
    {
        Simulation sim = TwoMines(15, second: false, out EntityHandle first, out _);
        EntityHandle w = Unit(sim, At(sim, 13, 9));
        sim.Enqueue(Command.Gather(0, w, At(sim, 14, 9)));
        UnitStore u = sim.World.Units;
        for (int t = 0; t < 3000 && sim.World.Resources.IsAlive(first); t++) sim.Tick();
        Run(sim, 2);
        Assert.False(EconomySystem.OnLoop(u, w.Index));
        Assert.Equal(UnitState.Idle, u.State[w.Index]);
        Assert.Equal(5, u.Cargo[w.Index]); // 10 deposited, the last 5 kept
        Assert.Equal(210, sim.World.Gold[0]);
    }

    [Fact]
    public void AMineFarOutside20m_IsNotWhereADepletedLoopGoes()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 20));
        EntityHandle near = Spawn(sim.World, Mine, 10, 9, 5);
        Spawn(sim.World, Mine, 24, 9, 2500); // 28 m away
        Building(sim, 2, 8);
        EntityHandle w = Unit(sim, At(sim, 9, 9));
        sim.Enqueue(Command.Gather(0, w, At(sim, 10, 9)));
        for (int t = 0; t < 2000 && sim.World.Resources.IsAlive(near); t++) sim.Tick();
        Run(sim, 2);
        Assert.False(EconomySystem.OnLoop(sim.World.Units, w.Index));
    }

    // ---------------------------------------------------------------- crowding (criterion 7)

    [Fact]
    public void TwentyWorkersOnOne2x2Mine_AllDepositWithin60s_AndNoneIsStuckAtTick1200()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 30), units: 32);
        EntityHandle mine = Spawn(sim.World, Mine, 20, 14, 2500);
        Building(sim, 8, 13); // 8 m from the mine
        var w = new EntityHandle[20];
        for (int k = 0; k < w.Length; k++) w[k] = Unit(sim, At(sim, 13 + k % 4, 10 + k / 4) + new Vector2(0.3f, 0.3f));
        foreach (EntityHandle h in w) sim.Enqueue(Command.Gather(0, h, At(sim, 20, 14)));
        UnitStore u = sim.World.Units;
        var deposited = new bool[w.Length];
        var lastCargo = new int[w.Length];
        var lastChange = new int[w.Length];
        int gold = sim.World.Gold[0];
        for (int t = 0; t < 1200; t++)
        {
            sim.Tick();
            for (int k = 0; k < w.Length; k++)
            {
                int c = u.Cargo[w[k].Index];
                if (c < lastCargo[k]) deposited[k] = true; // only a deposit empties a load here
                if (c != lastCargo[k]) lastChange[k] = t;
                lastCargo[k] = c;
            }
            Assert.True(sim.World.Gold[0] >= gold, "income went down");
            gold = sim.World.Gold[0];
        }
        _out.WriteLine($"20 workers, 60 s: +{sim.World.Gold[0] - 200} gold; deposited at least once: {deposited.Count(d => d)}/20");
        Assert.All(deposited, Assert.True);
        for (int k = 0; k < w.Length; k++)
        {
            int i = w[k].Index;
            // On the loop: standing at it (Gathering / Returning), walking a leg, or Idle for the one tick
            // between arriving (movement, phase 9) and the economy taking it up again (phase 4).
            Assert.True(EconomySystem.OnLoop(u, i), $"slot {i} left the loop");
            Assert.True(u.State[i] is UnitState.Gathering or UnitState.Returning or UnitState.Moving or UnitState.Idle, $"slot {i} is {u.State[i]}");
            Assert.True(1200 - lastChange[k] < 600, $"slot {i}: cargo unchanged since tick {lastChange[k]}");
        }
        Assert.True(sim.World.Resources.IsAlive(mine));
    }
}
