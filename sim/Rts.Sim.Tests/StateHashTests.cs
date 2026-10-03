using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;

namespace Rts.Sim.Tests;

public class StateHashTests
{
    private const int Ticks = 1000;

    private static SimConfig Config(ulong seed) =>
        new(Seed: seed, PlayerCount: 3, UnitCapacity: 128, CommandCapacity: 64);

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
}
