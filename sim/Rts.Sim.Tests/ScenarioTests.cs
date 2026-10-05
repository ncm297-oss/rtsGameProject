using System.Numerics;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// The M1 headline scenario (docs/05 M1, task M1-5): 200 units of every shipped type cross the
/// default generated map from near the west edge to the farthest plateau cell, over cliffs and up at
/// least one ramp. All arrive within the derived limit (see <see cref="CrossMapScenario.LimitTicks"/>),
/// none gives up, none ever stands on blocked ground, and the run is deterministic.
/// </summary>
public class ScenarioTests
{
    /// <summary>Army size (docs/05 M1: "200 units path across the map").</summary>
    private const int Army = 200;

    /// <summary>Start region radius in path cells: at most about 200 cells of ground at the map edge, so a compact army.</summary>
    private const float StartRadius = 12f;

    private readonly ITestOutputHelper _out;

    public ScenarioTests(ITestOutputHelper output) => _out = output;

    /// <summary>Orders the army and ticks until no unit is Moving or the limit is reached; checks blocked ground every tick. Returns the ticks taken.</summary>
    private static int Run(CrossMapScenario s, string label)
    {
        World w = s.Sim.World;
        UnitStore u = w.Units;
        Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(w));
        s.OrderAll();
        s.Sim.Tick(); // the Moves are stamped for the next tick
        s.Sim.Tick(); // they apply, and the army starts walking
        int ticks = 1;
        Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(w));
        while (s.AnyMoving() && ticks < s.LimitTicks)
        {
            s.Sim.Tick();
            ticks++;
            int bad = MoveScenario.FirstUnitOnBlockedGround(w);
            Assert.True(bad < 0, $"{label} tick {ticks}: unit {bad} at {(bad < 0 ? default : u.Position[bad])} is on blocked ground or off the map");
        }
        return ticks;
    }

    private static int Count(bool[] flags)
    {
        int n = 0;
        foreach (bool f in flags) if (f) n++;
        return n;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    [InlineData(7UL)]
    [InlineData(8UL)]
    public void TwoHundredUnits_AcrossTheMap_UpARamp_AllArriveWithinLimit_NoneGiveUp_NeverOnBlockedGround(ulong seed)
    {
        CrossMapScenario s = CrossMapScenario.Create(seed, Army, StartRadius);
        _out.WriteLine($"seed {seed}: {s.Describe()}");
        // The route climbs: level 0 at the start, a plateau at the goal, and levels change only on ramps.
        Assert.Equal(0, s.StartLevel);
        Assert.True(s.GoalLevel >= 1, $"goal level {s.GoalLevel}");
        Assert.True(s.LevelChanges >= 1 && s.RampCellsOnPath >= 1, $"{s.LevelChanges} level changes, {s.RampCellsOnPath} ramp cells");
        // A cross-map order, not a stroll: the path is at least half the map's width.
        Assert.True(s.PathMeters >= 0.5f * s.Sim.World.NavGrid.Width * Map.MapConstants.CellSize, $"path {s.PathMeters} m");
        UnitStore u = s.Sim.World.Units;
        var types = new bool[TestSim.UnitTypeCount];
        for (int i = 0; i < Army; i++)
        {
            types[u.TypeId[i]] = true;
            Assert.Equal(0, u.Owner[i]);
        }
        Assert.DoesNotContain(false, types); // every shipped type marches

        int ticks = Run(s, $"seed {seed}");
        bool[] arrived = MoveScenario.Arrived(s.Sim.World);
        _out.WriteLine($"seed {seed}: {ticks} ticks of {s.LimitTicks} allowed; arrived {Count(arrived)}, gave up {s.GaveUp()}");

        Assert.False(s.AnyMoving(), $"units still moving at the limit of {s.LimitTicks} ticks");
        Assert.True(ticks < s.LimitTicks, $"{ticks} ticks");
        Assert.Equal(0, s.GaveUp());
        for (int i = 0; i < Army; i++)
        {
            Assert.Equal(UnitState.Idle, u.State[i]);
            Assert.Equal(Vector2.Zero, u.Velocity[i]);
            Assert.NotEqual(-1, u.GoalCell[i]);
            Assert.True(arrived[i], $"unit {i} stopped {Vector2.Distance(u.Position[i], s.Goal):F1} m from the goal, not linked to the blob (goal cell {u.GoalCell[i]})");
        }
        string? pack = MoveScenario.FirstPackViolation(s.Sim.World);
        Assert.True(pack == null, pack);
    }

    /// <summary>Same seed and commands: equal <see cref="Simulation.StateHash"/> at every 100-tick checkpoint and at the end.</summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void CrossMap_TwoSimsSameSeedAndCommands_EqualHashAtEveryCheckpoint(ulong seed)
    {
        CrossMapScenario a = CrossMapScenario.Create(seed, Army, StartRadius);
        CrossMapScenario b = CrossMapScenario.Create(seed, Army, StartRadius);
        Assert.Equal(a.Sim.StateHash(), b.Sim.StateHash());
        a.OrderAll();
        b.OrderAll();
        int ticks = 0, checkpoints = 0;
        while ((ticks < 2 || a.AnyMoving() || b.AnyMoving()) && ticks < a.LimitTicks)
        {
            a.Sim.Tick();
            b.Sim.Tick();
            ticks++;
            if (ticks % 100 == 0)
            {
                Assert.True(a.Sim.StateHash() == b.Sim.StateHash(), $"seed {seed}: hashes differ at tick {ticks}");
                checkpoints++;
            }
        }
        Assert.False(a.AnyMoving());
        Assert.Equal(a.Sim.StateHash(), b.Sim.StateHash());
        _out.WriteLine($"seed {seed}: {checkpoints} checkpoints equal, end hash {a.Sim.StateHash():X16} after {ticks} ticks");
        Assert.True(checkpoints >= 10);
    }

    /// <summary>
    /// Report row: the same scenario with owners alternating 0/1, so half the army is the other
    /// player's (enemies are walls and are never shoved). Prints arrivals and give-ups; asserts only
    /// the hard rules (never on blocked ground, all stop within the limit).
    /// </summary>
    [Fact]
    public void CrossMap_TwoOwners_Report()
    {
        for (ulong seed = 1; seed <= 8; seed++)
        {
            CrossMapScenario s = CrossMapScenario.Create(seed, Army, StartRadius, players: 2);
            int ticks = Run(s, $"2 owners, seed {seed}");
            bool[] arrived = MoveScenario.Arrived(s.Sim.World);
            string? pack = MoveScenario.FirstPackViolation(s.Sim.World);
            _out.WriteLine($"2 owners, seed {seed}: {ticks} ticks of {s.LimitTicks}; arrived {Count(arrived)}, gave up {s.GaveUp()}, pack {(pack == null ? "ok" : "violated")}");
            Assert.False(s.AnyMoving(), $"seed {seed}: units still moving at the limit");
        }
    }

    /// <summary>The scenario's allocation check: runs alone in <see cref="SerialCollection"/>.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// A whole crossing (field build, the walk, the ramp, the blob forming) allocates nothing.
        /// A first full run warms the JIT; each measured run starts from a fresh, ordered sim set up
        /// outside the measurement, so a re-run repeats exactly the same ticks.
        /// </summary>
        [Fact]
        public void CrossMap_WholeRun_AllocatesNothing()
        {
            CrossMapScenario warm = CrossMapScenario.Create(1, Army, StartRadius);
            warm.OrderAll();
            int ticks = 0;
            while ((ticks < 2 || warm.AnyMoving()) && ticks < warm.LimitTicks)
            {
                warm.Sim.Tick();
                ticks++;
            }
            Assert.False(warm.AnyMoving());

            Simulation? sim = null;
            Action setup = () =>
            {
                CrossMapScenario s = CrossMapScenario.Create(1, Army, StartRadius);
                s.OrderAll();
                sim = s.Sim;
            };
            Action block = () =>
            {
                for (int t = 0; t < ticks; t++) sim!.Tick();
            };
            int runs = AllocationProbe.AssertZero(block, _out, setup);
            Assert.Equal(warm.Sim.StateHash(), sim!.StateHash());
            _out.WriteLine($"{ticks} ticks measured, {runs} run(s)");
        }
    }
}
