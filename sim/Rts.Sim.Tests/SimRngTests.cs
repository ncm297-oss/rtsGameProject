using Rts.Sim.Determinism;

namespace Rts.Sim.Tests;

public class SimRngTests
{
    [Fact]
    public void SameSeedAndStream_GiveIdenticalSequence()
    {
        var a = new SimRng(12345, 1);
        var b = new SimRng(12345, 1);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void DifferentStreams_SameSeed_GiveDifferentSequences()
    {
        var a = new SimRng(12345, (ulong)RngStream.Combat);
        var b = new SimRng(12345, (ulong)RngStream.MapGen);
        int same = 0;
        for (int i = 0; i < 1000; i++)
        {
            if (a.NextUInt() == b.NextUInt()) same++;
        }
        Assert.True(same < 5, $"{same} of 1000 draws matched");
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentSequences()
    {
        var a = new SimRng(1, 0);
        var b = new SimRng(2, 0);
        int same = 0;
        for (int i = 0; i < 1000; i++)
        {
            if (a.NextUInt() == b.NextUInt()) same++;
        }
        Assert.True(same < 5, $"{same} of 1000 draws matched");
    }

    /// <summary>BUG-0014: unmixed PCG seeding made seed ulong.MaxValue's stream 0 equal seed 0's shifted by one draw.</summary>
    [Fact]
    public void SeedMaxValue_AndSeedZero_GiveDifferentDrawsOnStream0()
    {
        var zero = new SimRng(0, 0);
        var max = new SimRng(ulong.MaxValue, 0);
        uint[] a = new uint[9], b = new uint[9];
        for (int i = 0; i < 9; i++)
        {
            a[i] = zero.NextUInt();
            b[i] = max.NextUInt();
        }
        Assert.False(a.AsSpan(0, 8).SequenceEqual(b.AsSpan(0, 8)), "first 8 draws are equal");
        // The old failure shape: one stream is the other shifted by a draw.
        Assert.False(a.AsSpan(0, 8).SequenceEqual(b.AsSpan(1, 8)), "seed 0 is seed max shifted by one draw");
        Assert.False(b.AsSpan(0, 8).SequenceEqual(a.AsSpan(1, 8)), "seed max is seed 0 shifted by one draw");
    }

    [Fact]
    public void NeighbouringSeeds_AreNotShiftsOfEachOther()
    {
        // Unmixed, seed s + 1's state was seed s's plus one, which for stream 0 (increment 1) made
        // states line up after a step. Check a spread of neighbours, wrap-around included.
        ulong[] seeds = { 0, 1, 2, 41, 42, ulong.MaxValue - 1, ulong.MaxValue };
        foreach (ulong s in seeds)
        {
            var a = new SimRng(s, 0);
            var b = new SimRng(unchecked(s + 1), 0);
            ulong bState = b.State;
            for (int i = 0; i < 64; i++)
            {
                a.NextUInt();
                Assert.NotEqual(bState, a.State);
            }
        }
    }

    [Fact]
    public void MixSeed_MatchesSplitMix64ReferenceOutputs()
    {
        // SplitMix64 started at state 0: its first three outputs (Vigna's reference implementation).
        Assert.Equal(0xE220A8397B1DCDAFUL, SimRng.MixSeed(0));
        Assert.Equal(0x6E789E6AA1B965F4UL, SimRng.MixSeed(0x9E3779B97F4A7C15UL));
        Assert.Equal(0x06C45D188009454FUL, SimRng.MixSeed(unchecked(2 * 0x9E3779B97F4A7C15UL)));
    }

    [Fact]
    public void Seeding_IsPinned()
    {
        // Guards the seeding itself: any change here moves every map and golden replay.
        var rng = new SimRng(1, 0);
        Assert.Equal(PinnedFirstDraw, rng.NextUInt());
    }

    [Fact]
    public void TestSeedsPreMix_InvertsMixSeed_SoOldStreamsAreReproducible()
    {
        foreach (ulong s in new ulong[] { 0, 1, 21, 904, 0xDEADBEEF, ulong.MaxValue })
            Assert.Equal(s, SimRng.MixSeed(TestSeeds.PreMix(s)));
        // The PCG state after seeding equals what the unmixed seeding gave seed 21: state 0, one step
        // (state = increment), add the raw seed, one step.
        const ulong mult = 6364136223846793005UL;
        ulong increment = (5UL << 1) | 1UL;
        ulong old = unchecked((increment + 21UL) * mult + increment);
        Assert.Equal(old, new SimRng(TestSeeds.PreMix(21), 5).State);
    }

    private const uint PinnedFirstDraw = 2157191000u;

    [Fact]
    public void WorldStreams_AreAllDistinct()
    {
        var world = new World(TestSim.Config(Seed: 99, PlayerCount: 4, UnitCapacity: 8, CommandCapacity: 8));
        Assert.Equal(RngStream.Count(4), world.RngCount);
        var firsts = new HashSet<uint>();
        for (int s = 0; s < world.RngCount; s++)
            firsts.Add(world.Rng(s).NextUInt());
        Assert.Equal(world.RngCount, firsts.Count);
    }

    [Fact]
    public void DrawingFromOneStream_LeavesOthersUnchanged()
    {
        var config = TestSim.Config(Seed: 7, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 8);
        var untouched = new World(config);
        var drawn = new World(config);

        for (int i = 0; i < 500; i++)
            drawn.Rng(RngStream.Combat).NextUInt();

        Assert.NotEqual(untouched.Rng(RngStream.Combat).State, drawn.Rng(RngStream.Combat).State);
        foreach (int s in new[] { RngStream.MapGen, RngStream.Ai(0), RngStream.Ai(1) })
        {
            for (int i = 0; i < 100; i++)
                Assert.Equal(untouched.Rng(s).NextUInt(), drawn.Rng(s).NextUInt());
        }
    }

    [Fact]
    public void NextInt_StaysInBounds()
    {
        var rng = new SimRng(42, 3);
        bool sawMin = false, sawMax = false;
        for (int i = 0; i < 100_000; i++)
        {
            int v = rng.NextInt(-3, 7);
            Assert.InRange(v, -3, 6);
            sawMin |= v == -3;
            sawMax |= v == 6;
        }
        Assert.True(sawMin && sawMax, "both ends of the range should occur");
    }

    [Fact]
    public void NextInt_HandlesFullIntRange()
    {
        var rng = new SimRng(42, 3);
        for (int i = 0; i < 1000; i++)
        {
            int v = rng.NextInt(int.MinValue, int.MaxValue);
            Assert.True(v < int.MaxValue);
        }
    }

    [Fact]
    public void NextInt_EmptyRangeThrows()
    {
        var rng = new SimRng(1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
    }

    [Fact]
    public void NextFloat_IsInUnitInterval()
    {
        var rng = new SimRng(9, 2);
        double sum = 0;
        for (int i = 0; i < 100_000; i++)
        {
            float f = rng.NextFloat();
            Assert.True(f >= 0f && f < 1f, $"{f} out of [0,1)");
            sum += f;
        }
        Assert.InRange(sum / 100_000, 0.49, 0.51);
    }
}
