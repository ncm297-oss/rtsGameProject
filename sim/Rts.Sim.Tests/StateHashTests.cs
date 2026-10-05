using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

public class StateHashTests
{
    private const int Ticks = 1000;

    private static SimConfig Config(ulong seed) =>
        TestSim.Config(Seed: seed, PlayerCount: 3, UnitCapacity: 128, CommandCapacity: 64);

    // A fixed script: spawns from all players on a few ticks, plus RNG draws driven by the sim's
    // own streams, so the hash covers units, RNG states, and pending commands.
    private static Simulation Run(ulong seed, bool extraSpawn = false)
    {
        var sim = new Simulation(Config(seed));
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 50 == 0)
            {
                for (int p = 0; p < 3; p++)
                {
                    ref SimRng rng = ref sim.World.Rng(RngStream.Ai(p));
                    var pos = new Vector2(rng.NextFloat() * 100f, rng.NextFloat() * 100f);
                    sim.Enqueue(Command.SpawnUnit(p, typeId: rng.NextInt(0, 4), pos));
                }
            }
            if (extraSpawn && t == 500)
                sim.Enqueue(Command.SpawnUnit(0, typeId: 0, Vector2.Zero));
            sim.Tick();
        }
        return sim;
    }

    [Fact]
    public void SameSeedAndCommands_GiveEqualHash()
    {
        Simulation a = Run(1234);
        Simulation b = Run(1234);
        Assert.Equal(Ticks, a.TickNumber);
        Assert.True(a.World.Units.Count > 0);
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void DifferentSeed_GivesDifferentHash()
    {
        Assert.NotEqual(Run(1234).StateHash(), Run(1235).StateHash());
    }

    [Fact]
    public void OneExtraSpawn_GivesDifferentHash()
    {
        Assert.NotEqual(Run(1234).StateHash(), Run(1234, extraSpawn: true).StateHash());
    }

    [Fact]
    public void Hash_CoversTickPendingCommandsAndRng()
    {
        var a = new Simulation(Config(5));
        var b = new Simulation(Config(5));
        Assert.Equal(a.StateHash(), b.StateHash());

        b.Enqueue(Command.Noop(0));
        Assert.NotEqual(a.StateHash(), b.StateHash());

        var c = new Simulation(Config(5));
        c.World.Rng(RngStream.Combat).NextUInt();
        Assert.NotEqual(a.StateHash(), c.StateHash());

        var d = new Simulation(Config(5));
        d.Tick();
        Assert.NotEqual(a.StateHash(), d.StateHash());
    }

    [Fact]
    public void Hash_IsStableValueForFreshWorld()
    {
        // Guards against accidental use of per-process randomized hashing.
        ulong h1 = new Simulation(Config(77)).StateHash();
        ulong h2 = new Simulation(Config(77)).StateHash();
        Assert.Equal(h1, h2);
        Assert.NotEqual(0UL, h1);
    }

    // ---------- M1-4b: movement ----------

    /// <summary>100 units walking: one group Move at tick 0, half of them re-targeted at tick 200. Hash after every tick.</summary>
    private static ulong[] RunMoves(ulong seed)
    {
        Simulation sim = MoveScenario.Spawn(seed, units: 100, maxCost: 60f, out int goalCell);
        var g = sim.World.NavGrid;
        Vector2 goal = MoveScenario.Center(g, goalCell);
        var hashes = new ulong[600];
        for (int t = 0; t < hashes.Length; t++)
        {
            if (t == 0) MoveScenario.MoveAll(sim, goal + new Vector2(0.5f, 0.5f));
            if (t == 200)
            {
                for (int i = 0; i < 100; i += 2)
                    sim.Enqueue(Command.Move(sim.World.Units.Owner[i], MoveScenario.Handle(sim, i), goal + new Vector2(-12f, 8f)));
            }
            sim.Tick();
            hashes[t] = sim.StateHash();
        }
        return hashes;
    }

    [Fact]
    public void Moves_SameSeed_IdenticalHashEveryTick_DifferentSeedDiffers()
    {
        ulong[] a = RunMoves(21), b = RunMoves(21);
        Assert.Equal(a, b);
        Assert.Equal(600, a.Distinct().Count()); // the tick number alone changes the hash, but this guards a frozen sim too
        ulong[] c = RunMoves(22);
        Assert.NotEqual(a[^1], c[^1]);
    }

    [Fact]
    public void Hash_CoversStateGoalGoalCellAndCommandUnit()
    {
        Simulation sim = MoveScenario.Spawn(5, units: 2, maxCost: 10f, out int goalCell);
        UnitStore u = sim.World.Units;
        ulong h0 = sim.StateHash();

        u.State[1] = UnitState.Moving;
        Assert.NotEqual(h0, sim.StateHash());
        u.State[1] = UnitState.Idle;
        Assert.Equal(h0, sim.StateHash());

        u.Goal[1] = new Vector2(0f, float.Epsilon);
        Assert.NotEqual(h0, sim.StateHash());
        u.Goal[1] = default;
        Assert.Equal(h0, sim.StateHash());

        u.GoalCell[1] = goalCell;
        Assert.NotEqual(h0, sim.StateHash());
        u.GoalCell[1] = -1;
        Assert.Equal(h0, sim.StateHash());

        // Speed and Radius derive from TypeId, so they are deliberately not hashed.
        u.Speed[1] += 1f;
        u.Radius[1] += 1f;
        Assert.Equal(h0, sim.StateHash());

        // Two pending Moves that differ only in the unit's handle (index or generation) hash differently.
        static Simulation Pending(EntityHandle h)
        {
            Simulation s = MoveScenario.Spawn(5, units: 2, maxCost: 10f, out _);
            s.Enqueue(Command.Move(0, h, Vector2.One));
            return s;
        }
        ulong p0 = Pending(new EntityHandle(0, 1)).StateHash();
        Assert.NotEqual(p0, Pending(new EntityHandle(1, 1)).StateHash());
        Assert.NotEqual(p0, Pending(new EntityHandle(0, 2)).StateHash());
        Assert.Equal(p0, Pending(new EntityHandle(0, 1)).StateHash());
    }

    // ---------- M1-4c: the flow-field cache and order ticks are sim state (BUG-0021) ----------

    [Fact]
    public void Hash_CoversOrderTick()
    {
        Simulation sim = MoveScenario.Spawn(5, units: 2, maxCost: 10f, out _);
        ulong h0 = sim.StateHash();
        sim.World.Units.OrderTick[1] = 7;
        Assert.NotEqual(h0, sim.StateHash());
        sim.World.Units.OrderTick[1] = 0;
        Assert.Equal(h0, sim.StateHash());
    }

    [Fact]
    public void Hash_CoversFlowFieldCache_AGetChangesIt_TheSameGetsMakeItEqualAgain()
    {
        var a = new Simulation(Config(9));
        var b = new Simulation(Config(9));
        var g = a.World.NavGrid;
        List<int> open = FlowFieldOracle.PassableCells(g);
        int c1 = open[open.Count / 3], c2 = open[2 * open.Count / 3];
        Assert.Equal(a.StateHash(), b.StateHash());

        a.World.FlowFields.Get(c1);
        Assert.NotEqual(a.StateHash(), b.StateHash()); // before any tick
        b.World.FlowFields.Get(c1);
        Assert.Equal(a.StateHash(), b.StateHash());

        a.World.FlowFields.Get(c2);
        b.World.FlowFields.Get(c2);
        Assert.Equal(a.StateHash(), b.StateHash());

        // A hit builds nothing; it only moves the LRU stamp, and that alone changes the hash.
        int builds = a.World.FlowFields.BuildCount;
        a.World.FlowFields.Get(c1);
        Assert.Equal(builds, a.World.FlowFields.BuildCount);
        Assert.NotEqual(a.StateHash(), b.StateHash());
        b.World.FlowFields.Get(c1);
        Assert.Equal(a.StateHash(), b.StateHash());
        a.Tick();
        b.Tick();
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Hash_SameCachedFieldsAndClock_DifferentLruOrder_Differs()
    {
        var a = new Simulation(Config(9));
        var b = new Simulation(Config(9));
        List<int> open = FlowFieldOracle.PassableCells(a.World.NavGrid);
        int c1 = open[100], c2 = open[200];
        // Same slots, same clock (4 uses), same count; only which field was used last differs.
        a.World.FlowFields.Get(c1); a.World.FlowFields.Get(c2); a.World.FlowFields.Get(c1); a.World.FlowFields.Get(c2);
        b.World.FlowFields.Get(c1); b.World.FlowFields.Get(c2); b.World.FlowFields.Get(c2); b.World.FlowFields.Get(c1);
        Assert.Equal(a.World.FlowFields.BuildCount, b.World.FlowFields.BuildCount);
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }
}
