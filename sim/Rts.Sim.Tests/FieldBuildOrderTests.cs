using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Tests;

/// <summary>M1-4c: the flow-field build pass serves misses oldest order first, two per tick (BUG-0022), and the cache is sized from the config.</summary>
public class FieldBuildOrderTests
{
    [Fact]
    public void MaxFieldBuildsPerTick_IsTwo()
    {
        Assert.Equal(2, MovementConstants.MaxFieldBuildsPerTick);
    }

    [Theory]
    [InlineData(256, 16384, 32)]
    [InlineData(1024, 16384, 128)]
    [InlineData(4096, 16384, 128)]
    [InlineData(1024, 1_048_576, 32)]
    [InlineData(1, 16384, 32)]
    [InlineData(512, 16384, 64)]
    [InlineData(4096, 262_144, 51)] // 512 x 512: 64 MiB / 1.25 MiB per field
    public void CapacityFor_ScalesWithUnitsAndCapsMemory(int unitCapacity, int cellCount, int expected)
    {
        Assert.Equal(expected, FlowFieldCache.CapacityFor(unitCapacity, cellCount));
    }

    [Fact]
    public void World_OnDefaultConfig_SizesItsCacheWithCapacityFor()
    {
        foreach (int units in new[] { 64, 512, 1024 })
        {
            var world = new World(TestSim.Config(1, 2, UnitCapacity: units, CommandCapacity: 8));
            NavGrid g = world.NavGrid;
            Assert.Equal(128 * 128, g.Width * g.Height);
            Assert.Equal(FlowFieldCache.CapacityFor(units, g.Width * g.Height), world.FlowFields.Capacity);
        }
        Assert.Equal(128, new World(TestSim.Config(1, 2, 1024, 8)).FlowFields.Capacity);
    }

    [Fact]
    public void Move_StampsOrderTick_WithTheTickItApplies()
    {
        Simulation sim = MoveScenario.Spawn(seed: 3, units: 2, maxCost: 20f, out int goalCell);
        Vector2 goal = MoveScenario.Center(sim.World.NavGrid, goalCell);
        for (int t = 0; t < 5; t++) sim.Tick();
        sim.Enqueue(Command.Move(sim.World.Units.Owner[1], MoveScenario.Handle(sim, 1), goal));
        int applyTick = sim.TickNumber + 1; // commands apply one tick after they are queued
        sim.Tick();
        sim.Tick();
        Assert.Equal(applyTick, sim.World.Units.OrderTick[1]);
        Assert.Equal(0, sim.World.Units.OrderTick[0]);
    }

    /// <summary>
    /// 40 groups of two units whose orders applied on ticks 1..40 and none has a field yet (as after a
    /// burst the cap couldn't serve). Later orders have lower goal cells, so serving by cell index
    /// would pick the newest first.
    /// </summary>
    [Fact]
    public void BuildPass_ServesOldestOrdersFirst_TwoPerTick_EvenWhenNewerGoalsHaveLowerCells()
    {
        const int groups = 40;
        // 320 unit slots: cache capacity 40, so no field is evicted during the test.
        // M4-2b: combat off (config only, BUG-0135): the radius-0.4 / type-0 unit is the Cadre Mage, which fights now
        var sim = new Simulation(TestSim.ConfigNoCombat(4022, 2, UnitCapacity: 320, CommandCapacity: 256));
        World w = sim.World;
        NavGrid g = w.NavGrid;
        Assert.True(w.FlowFields.Capacity >= groups);
        List<int> open = FlowFieldOracle.PassableCells(g);

        // Goals: 40 passable cells, highest index first; spawns: other passable cells.
        var goals = new int[groups];
        for (int k = 0; k < groups; k++) goals[k] = open[open.Count - 1 - k * 97];
        for (int i = 0; i < 2 * groups; i++)
            sim.Enqueue(Command.SpawnUnit(i % 2, 0, MoveScenario.Center(g, open[i * 53 + 11])));
        for (int t = 0; t < 45; t++) sim.Tick();

        UnitStore u = w.Units;
        for (int i = 0; i < 2 * groups; i++)
        {
            int k = i / 2;
            Assert.NotEqual(goals[k], open[i * 53 + 11]);
            u.State[i] = UnitState.Moving;
            u.Goal[i] = MoveScenario.Center(g, goals[k]);
            u.GoalCell[i] = goals[k];
            u.OrderTick[i] = 1 + k; // group k was ordered on tick 1 + k
        }
        for (int k = 1; k < groups; k++) Assert.True(goals[k] < goals[k - 1]);

        for (int j = 0; j < groups / 2; j++)
        {
            int builds = w.FlowFields.BuildCount;
            var before = new Vector2[2 * groups];
            Array.Copy(u.Position, before, before.Length);
            sim.Tick();
            Assert.Equal(builds + 2, w.FlowFields.BuildCount);
            for (int k = 0; k < groups; k++)
            {
                bool served = k < 2 * (j + 1);
                Assert.True(served == w.FlowFields.Contains(goals[k]),
                    $"tick {j}: group {k} (ordered on tick {1 + k}) {(served ? "should" : "should not")} have its field");
                if (!served)
                {
                    // Waiting units hold still with zero velocity, still Moving.
                    for (int i = 2 * k; i < 2 * k + 2; i++)
                    {
                        Assert.Equal(before[i], u.Position[i]);
                        Assert.Equal(Vector2.Zero, u.Velocity[i]);
                        Assert.Equal(UnitState.Moving, u.State[i]);
                    }
                }
            }
        }
        int done = w.FlowFields.BuildCount;
        sim.Tick();
        Assert.Equal(done, w.FlowFields.BuildCount); // every group served: no more builds
    }

    [Fact]
    public void BuildPass_SameOrderTick_TiesGoToTheLowerGoalCell()
    {
        // Within one burst tick the only key left is the goal cell (docs/03 "Build cap").
        Simulation sim = MoveScenario.Spawn(seed: 9, units: 4, maxCost: 40f, out int center);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        List<int> open = FlowFieldOracle.PassableCells(g);
        int[] goals = { open[400], open[100], open[300], open[200] };
        for (int i = 0; i < 4; i++)
            sim.Enqueue(Command.Move(w.Units.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, goals[i])));
        sim.Tick(); // queued
        sim.Tick(); // applied on one tick: two builds, lowest cells
        Assert.True(w.FlowFields.Contains(open[100]));
        Assert.True(w.FlowFields.Contains(open[200]));
        Assert.False(w.FlowFields.Contains(open[300]));
        Assert.False(w.FlowFields.Contains(open[400]));
        sim.Tick();
        Assert.True(w.FlowFields.Contains(open[300]));
        Assert.True(w.FlowFields.Contains(open[400]));
        Assert.NotEqual(-1, center);
    }
}
