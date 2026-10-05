using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>QA attacks on the M1-4b Move command, spawn-from-data and MovementSystem edges.</summary>
public class MoveQaTests
{
    private readonly ITestOutputHelper _out;

    public MoveQaTests(ITestOutputHelper output) => _out = output;

    private const float MapMeters = 128 * MapConstants.CellSize;

    /// <summary>Two identical sims with one unit per player standing on passable ground near the center.</summary>
    private static (Simulation A, Simulation B, EntityHandle U0, EntityHandle U1) Pair(ulong seed = 5)
    {
        Simulation Make()
        {
            var sim = new Simulation(TestSim.Config(seed, PlayerCount: 2, UnitCapacity: 4, CommandCapacity: 16));
            NavGrid g = sim.World.NavGrid;
            Vector2 at = MoveScenario.Center(g, MoveScenario.CentralCell(g));
            sim.Enqueue(Command.SpawnUnit(0, 0, at));
            sim.Enqueue(Command.SpawnUnit(1, 1, at));
            sim.Tick();
            sim.Tick();
            return sim;
        }
        Simulation a = Make(), b = Make();
        return (a, b, MoveScenario.Handle(a, 0), MoveScenario.Handle(a, 1));
    }

    /// <summary>Sim A gets <paramref name="bad"/>, sim B a Noop from the same player; after a tick both hashes must match.</summary>
    private static void AssertDropped(Command bad)
    {
        (Simulation a, Simulation b, _, _) = Pair();
        Assert.Equal(a.StateHash(), b.StateHash());
        a.Enqueue(bad);
        b.Enqueue(Command.Noop(bad.Player));
        // A command applies on the tick after the one it was queued in.
        for (int t = 0; t < 3; t++)
        {
            a.Tick();
            b.Tick();
        }
        Assert.Equal(0, a.PendingCommandCount);
        Assert.Equal(b.StateHash(), a.StateHash());
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(UnitState.Idle, a.World.Units.State[i]);
            Assert.Equal(-1, a.World.Units.GoalCell[i]);
        }
    }

    public static IEnumerable<object[]> BadTargets()
    {
        float[] bad = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.1f, -1e-30f, MapMeters, MapMeters + 0.001f, 1e30f, -1e30f, float.MaxValue };
        foreach (float v in bad)
        {
            yield return new object[] { v, 100f };
            yield return new object[] { 100f, v };
        }
    }

    [Theory]
    [MemberData(nameof(BadTargets))]
    public void Move_TargetNotFiniteOrOffMap_IsDropped(float x, float y)
    {
        AssertDropped(Command.Move(0, new EntityHandle(0, 0), new Vector2(x, y)));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(int.MinValue, 0)]
    [InlineData(4, 0)]
    [InlineData(int.MaxValue, 0)]
    [InlineData(0, 0)]   // right slot, wrong generation (generations start at 1)
    [InlineData(0, 2)]
    [InlineData(0, -1)]
    [InlineData(2, 0)]   // never allocated slot
    [InlineData(1, 0)]   // live, but player 1's unit
    public void Move_BadOrForeignHandle_IsDropped(int index, int generation)
    {
        AssertDropped(Command.Move(0, new EntityHandle(index, generation), new Vector2(100f, 100f)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void Move_UnknownPlayer_ThrowsAtEnqueue_AndQueuesNothing(int player)
    {
        (Simulation a, _, EntityHandle u0, _) = Pair();
        ulong before = a.StateHash();
        Assert.Throws<ArgumentOutOfRangeException>(() => a.Enqueue(Command.Move(player, u0, new Vector2(100f, 100f))));
        Assert.Equal(0, a.PendingCommandCount);
        Assert.Equal(before, a.StateHash());
    }

    [Fact]
    public void Move_StaleHandleAfterFreeAndReallocOfSameSlot_DoesNotMoveTheNewUnit()
    {
        (Simulation a, _, EntityHandle u0, _) = Pair();
        UnitStore u = a.World.Units;
        NavGrid g = a.World.NavGrid;
        Vector2 spot = u.Position[0];
        // Order the unit away, then kill it mid-walk and respawn into the same slot.
        a.Enqueue(Command.Move(0, u0, spot + new Vector2(20f, 0f)));
        a.Tick();
        a.Tick();
        Assert.Equal(UnitState.Moving, u.State[0]);
        u.Free(u0);
        a.Enqueue(Command.SpawnUnit(0, 2, spot));
        a.Enqueue(Command.Move(0, u0, spot + new Vector2(-20f, 0f))); // stale: same index, old generation
        a.Tick();
        a.Tick();
        Assert.True(u.Alive[0]);
        Assert.NotEqual(u0.Generation, u.Generation[0]);
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.Equal(-1, u.GoalCell[0]);
        Assert.Equal(spot, u.Position[0]);
        Assert.Equal(Vector2.Zero, u.Velocity[0]);
        Assert.Equal(TestSim.Data.Units[2].SpeedPerTick, u.Speed[0]);
        Assert.True(g.WorldToCell(spot, out _, out _));
    }

    [Fact]
    public void Move_ToLastFloatInsideMap_IsAccepted_AndResolvesTheRingCell()
    {
        (Simulation a, _, EntityHandle u0, _) = Pair();
        NavGrid g = a.World.NavGrid;
        float edge = MathF.BitDecrement(MapMeters);
        a.Enqueue(Command.Move(0, u0, new Vector2(edge, edge)));
        a.Tick();
        a.Tick();
        UnitStore u = a.World.Units;
        Assert.Equal(UnitState.Moving, u.State[0]);
        int expected = FlowFieldQaTests.QaNearest(g, 127 * 128 + 127);
        Assert.Equal(expected, u.GoalCell[0]);
        Assert.Equal(g.CellCenter(expected % 128, expected / 128), u.Goal[0]);
    }

    [Fact]
    public void Move_ToNegativeZero_IsAccepted_AsCellZero()
    {
        (Simulation a, _, EntityHandle u0, _) = Pair();
        a.Enqueue(Command.Move(0, u0, new Vector2(-0f, -0f)));
        a.Tick();
        a.Tick();
        Assert.Equal(UnitState.Moving, a.World.Units.State[0]);
        Assert.Equal(FlowFieldQaTests.QaNearest(a.World.NavGrid, 0), a.World.Units.GoalCell[0]);
    }

    [Fact]
    public void Move_ToEveryCliffCellOfAMap_GoalIsPassable_AndMatchesFieldTarget()
    {
        var sim = new Simulation(TestSim.Config(8, 1, 4, 64));
        NavGrid g = sim.World.NavGrid;
        Vector2 at = MoveScenario.Center(g, MoveScenario.CentralCell(g));
        sim.Enqueue(Command.SpawnUnit(0, 0, at));
        sim.Tick();
        sim.Tick();
        EntityHandle h = MoveScenario.Handle(sim, 0);
        UnitStore u = sim.World.Units;
        int checkedCells = 0;
        for (int c = 0; c < g.Width * g.Height; c += 3)
        {
            if (g.IsPassable(c % g.Width, c / g.Width)) continue;
            Vector2 target = MoveScenario.Center(g, c) + new Vector2(0.3f, -0.7f);
            sim.Enqueue(Command.Move(0, h, target));
            sim.Tick();
            sim.Tick();
            int gc = u.GoalCell[0];
            Assert.True(g.IsPassable(gc % g.Width, gc / g.Width), $"cell {c}: goal cell {gc} blocked");
            Assert.Equal(FlowFieldQaTests.QaNearest(g, c), gc);
            Assert.True(g.WorldToCell(u.Goal[0], out int gx, out int gy) && gy * g.Width + gx == gc);
            checkedCells++;
        }
        _out.WriteLine($"{checkedCells} blocked targets");
        Assert.True(checkedCells > 200);
    }

    [Fact]
    public void Move_SpammedEveryTick_ToSameTarget_BuildsOneField_AndStillArrives()
    {
        Simulation sim = MoveScenario.Spawn(seed: 3, units: 20, maxCost: 40f, out int goalCell);
        Vector2 goal = MoveScenario.Center(sim.World.NavGrid, goalCell);
        int builds = sim.World.FlowFields.BuildCount;
        for (int t = 0; t < 1200; t++)
        {
            MoveScenario.MoveAll(sim, goal);
            sim.Tick();
        }
        Assert.Equal(builds + 1, sim.World.FlowFields.BuildCount);
        // M1-4d-1: 20 units can't all fit within 1 m of the point, and a spammed Move re-arms the whole
        // blob every tick, so they settle only once the spam stops: as a blob linked to the point.
        UnitStore u = sim.World.Units;
        sim.Tick(); // the last Moves apply
        for (int t = 0; t < 60; t++) sim.Tick();
        bool[] arrived = MoveScenario.Arrived(sim.World);
        for (int i = 0; i < 20; i++)
            Assert.True(arrived[i], $"unit {i} at {u.Position[i]} ({u.State[i]}, {Vector2.Distance(u.Position[i], goal):F2} m from the goal)");
        Assert.Null(MoveScenario.FirstPackViolation(sim.World));
    }

    [Fact]
    public void UnitSpawnedOnCliffOrOffMap_GivenMove_NeverMoves_AndGoesIdle()
    {
        var sim = new Simulation(TestSim.Config(8, 1, 8, 64));
        NavGrid g = sim.World.NavGrid;
        int cliff = -1;
        for (int c = g.Width + 1; c < g.Width * g.Height && cliff < 0; c++)
        {
            int x = c % g.Width, y = c / g.Width;
            if (x > 0 && x < g.Width - 1 && !g.IsPassable(x, y)) cliff = c;
        }
        Vector2 onCliff = MoveScenario.Center(g, cliff);
        Vector2 offMap = new(-5f, 40f);
        sim.Enqueue(Command.SpawnUnit(0, 0, onCliff));
        sim.Enqueue(Command.SpawnUnit(0, 0, offMap));
        sim.Tick();
        sim.Tick();
        Vector2 goal = MoveScenario.Center(g, MoveScenario.CentralCell(g));
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), goal));
        for (int t = 0; t < 5; t++) sim.Tick();
        UnitStore u = sim.World.Units;
        Assert.Equal(onCliff, u.Position[0]);
        Assert.Equal(offMap, u.Position[1]);
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.Equal(UnitState.Idle, u.State[1]);
    }

    [Fact]
    public void Move_OnTheSpawnTick_WithPredictedHandle_Applies()
    {
        // Commands apply in (player, sequence) order, so a Move queued right after the spawn
        // can name the slot the spawn will take.
        var sim = new Simulation(TestSim.Config(8, 1, 4, 64));
        NavGrid g = sim.World.NavGrid;
        int c = MoveScenario.CentralCell(g);
        sim.Enqueue(Command.SpawnUnit(0, 0, MoveScenario.Center(g, c)));
        sim.Enqueue(Command.Move(0, new EntityHandle(0, sim.World.Units.Generation[0]), MoveScenario.Center(g, c) + new Vector2(10f, 0f)));
        sim.Tick();
        sim.Tick();
        Assert.Equal(UnitState.Moving, sim.World.Units.State[0]);
    }

    [Fact]
    public void EveryShippedUnitType_SpawnsWithDataSpeedAndRadius_AndMovesExactlyThatFar()
    {
        int types = TestSim.UnitTypeCount;
        // M1-4d-1: units on one spot push apart, so each type walks in its own sim.
        for (int type = 0; type < types; type++)
            WalkOneType(type);
    }

    private static void WalkOneType(int type)
    {
        var sim = new Simulation(TestSim.Config(8, 1, 1, 8));
        NavGrid g = sim.World.NavGrid;
        // An open straight run: a passable cell with 12 passable cells to its east.
        int start = -1;
        for (int y = 2; y < g.Height - 2 && start < 0; y++)
            for (int x = 2; x < g.Width - 16 && start < 0; x++)
            {
                bool ok = true;
                for (int k = 0; k <= 12 && ok; k++) ok = g.IsPassable(x + k, y);
                if (ok) start = y * g.Width + x;
            }
        Vector2 from = MoveScenario.Center(g, start);
        sim.Enqueue(Command.SpawnUnit(0, type, from));
        sim.Tick();
        sim.Tick();
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), from + new Vector2(20f, 0f)));
        sim.Tick();
        sim.Tick(); // applies Moves and takes the first step
        UnitStore u = sim.World.Units;
        var def = TestSim.Data.Units[type];
        Assert.Equal(def.SpeedPerTick, u.Speed[0]);
        Assert.Equal(def.Radius, u.Radius[0]);
        Assert.True(def.SpeedPerTick > 0f, $"{def.Key} has no speed");
        Assert.Equal(def.SpeedPerTick, u.Position[0].X - from.X, 4);
        Assert.Equal(from.Y, u.Position[0].Y);
        Assert.Equal(0f, u.Facing[0], 4);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(1000)]
    public void SpawnUnit_UnknownTypeId_IsDropped_AndDoesNotConsumeASlot(int typeId)
    {
        var sim = new Simulation(TestSim.Config(8, 1, 2, 8));
        sim.Enqueue(Command.SpawnUnit(0, typeId, new Vector2(100f, 100f)));
        sim.Enqueue(Command.SpawnUnit(0, TestSim.UnitTypeCount, new Vector2(100f, 100f)));
        sim.Enqueue(Command.SpawnUnit(0, TestSim.UnitTypeCount - 1, new Vector2(100f, 100f)));
        sim.Tick();
        sim.Tick();
        Assert.Equal(1, sim.World.Units.Count);
        Assert.Equal(TestSim.UnitTypeCount - 1, sim.World.Units.TypeId[0]);
    }

    [Fact]
    public void DiagonalNeighborAcrossBlockedCorner_UnitDoesNotArriveThroughTheCorner()
    {
        // Two passable cells touching only at a corner whose both side cells are blocked: a unit
        // standing at that corner is < 1 m from a goal just across it, but the field has no step
        // there. Arrival must not count a goal that is only reachable the long way round.
        for (ulong seed = 0; seed < 40; seed++)
        {
            var sim = new Simulation(TestSim.Config(seed, 1, 2, 16));
            NavGrid g = sim.World.NavGrid;
            for (int y = 1; y < g.Height - 2; y++)
                for (int x = 1; x < g.Width - 2; x++)
                {
                    // (x, y) and (x+1, y+1) passable, (x+1, y) and (x, y+1) blocked.
                    if (!g.IsPassable(x, y) || !g.IsPassable(x + 1, y + 1) || g.IsPassable(x + 1, y) || g.IsPassable(x, y + 1)) continue;
                    FlowField f = FlowField.Build(g, (y + 1) * g.Width + x + 1);
                    float around = f.CostAt(y * g.Width + x);
                    Vector2 corner = new((x + 1) * MapConstants.CellSize, (y + 1) * MapConstants.CellSize);
                    Vector2 from = corner - new Vector2(0.3f, 0.3f);
                    Vector2 goal = corner + new Vector2(0.3f, 0.3f);
                    sim.Enqueue(Command.SpawnUnit(0, 0, from));
                    sim.Tick();
                    sim.Tick();
                    sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
                    sim.Tick();
                    sim.Tick();
                    UnitStore u = sim.World.Units;
                    _out.WriteLine($"seed {seed} corner ({x},{y}): path around costs {around} cells; unit state {u.State[0]} at {u.Position[0]}, goal {goal}");
                    Assert.True(float.IsPositiveInfinity(around) || u.State[0] == UnitState.Moving || Vector2.Distance(u.Position[0], from) > 0.01f,
                        $"unit 'arrived' {Vector2.Distance(from, goal):F2} m from a goal {around} cells away by path");
                    return;
                }
        }
        _out.WriteLine("no corner-touching passable pair in 40 maps");
    }
}
