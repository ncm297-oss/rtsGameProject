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

    [Fact]
    public void WorldStreams_AreAllDistinct()
    {
        var world = new World(new SimConfig(Seed: 99, PlayerCount: 4, UnitCapacity: 8, CommandCapacity: 8));
        Assert.Equal(RngStream.Count(4), world.RngCount);
        var firsts = new HashSet<uint>();
        for (int s = 0; s < world.RngCount; s++)
            firsts.Add(world.Rng(s).NextUInt());
        Assert.Equal(world.RngCount, firsts.Count);
    }

    [Fact]
    public void DrawingFromOneStream_LeavesOthersUnchanged()
    {
        var config = new SimConfig(Seed: 7, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 8);
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
