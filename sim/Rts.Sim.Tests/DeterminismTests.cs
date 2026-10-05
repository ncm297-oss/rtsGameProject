using System.Numerics;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>M1-6 criterion 3 (docs/03 "Determinism"): same seed and commands give the same state hash; another seed doesn't.</summary>
public class DeterminismTests
{
    private const int Ticks = 2000;
    private const int Every = 100;

    /// <summary>200 units on the default map, re-ordered every 250 ticks to a point picked from the seed, so they keep moving; returns the hash every 100 ticks.</summary>
    private static ulong[] Run(ulong seed, out int movingTicks)
    {
        Simulation sim = MoveScenario.Spawn(seed, units: 200, maxCost: 30f, out int goalCell);
        var g = sim.World.NavGrid;
        UnitStore u = sim.World.Units;
        var rng = new Determinism.SimRng(seed, 71);
        var hashes = new ulong[Ticks / Every];
        movingTicks = 0;
        for (int t = 1; t <= Ticks; t++)
        {
            if (t % 250 == 1)
            {
                Vector2 target = MoveScenario.Center(g, goalCell) + new Vector2(rng.NextInt(-12, 13) * 2f, rng.NextInt(-12, 13) * 2f);
                MoveScenario.MoveAll(sim, target);
            }
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (u.Alive[i] && u.State[i] == UnitState.Moving)
                {
                    movingTicks++;
                    break;
                }
            }
            if (t % Every == 0) hashes[t / Every - 1] = sim.StateHash();
        }
        return hashes;
    }

    [Fact]
    public void SameSeedAndCommands_EqualHashEvery100Ticks_For2000TicksOfMovement()
    {
        ulong[] a = Run(17, out int moving), b = Run(17, out _);
        Assert.True(moving > Ticks / 2, $"units moved on only {moving} of {Ticks} ticks");
        for (int i = 0; i < a.Length; i++)
            Assert.True(a[i] == b[i], $"hashes differ at tick {(i + 1) * Every}");
    }

    [Fact]
    public void DifferentSeeds_DifferByTick100()
    {
        ulong[] a = Run(17, out _), b = Run(18, out _);
        Assert.NotEqual(a[0], b[0]);
    }
}
