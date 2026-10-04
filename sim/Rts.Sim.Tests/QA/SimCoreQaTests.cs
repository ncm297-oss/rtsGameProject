using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests.QA;

/// <summary>QA edge-case attacks on the M1-1 sim core (handles, RNG, SimMath, command queue, state hash).</summary>
public class SimCoreQaTests
{
    // ---------- RNG ----------

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData((long)int.MinValue)]
    [InlineData((long)int.MaxValue)]
    [InlineData(long.MinValue)]
    public void Rng_ExtremeSeeds_AreNonDegenerate(long seed)
    {
        var rng = new SimRng(unchecked((ulong)seed), 0);
        var seen = new HashSet<uint>();
        int zeros = 0;
        for (int i = 0; i < 10_000; i++)
        {
            uint v = rng.NextUInt();
            if (v == 0) zeros++;
            seen.Add(v);
        }
        Assert.True(zeros <= 1, $"seed {seed}: {zeros} zero draws");
        Assert.True(seen.Count > 9_990, $"seed {seed}: only {seen.Count} distinct values in 10k draws");
        Assert.NotEqual(0UL, rng.State);
    }

    [Fact]
    public void Rng_InterleavedDraws_MatchIsolatedDraws()
    {
        // Two streams drawn alternately must equal the same streams drawn alone.
        var a = new SimRng(42, 1);
        var b = new SimRng(42, 2);
        var aAlone = new SimRng(42, 1);
        var bAlone = new SimRng(42, 2);
        var aSeq = new uint[5000];
        var bSeq = new uint[5000];
        for (int i = 0; i < 5000; i++)
        {
            aSeq[i] = a.NextUInt();
            if (i % 3 == 0) b.NextFloat(); // irregular interleave
            bSeq[i] = b.NextUInt();
        }
        for (int i = 0; i < 5000; i++)
            Assert.Equal(aSeq[i], aAlone.NextUInt());
        for (int i = 0; i < 5000; i++)
        {
            if (i % 3 == 0) bAlone.NextFloat();
            Assert.Equal(bSeq[i], bAlone.NextUInt());
        }
    }

    [Fact]
    public void Rng_NextFloat_StrictlyBelowOne_AndMaxValueIsBelowOne()
    {
        // The largest possible output: top 24 bits all set.
        float max = (0xFFFFFFFFu >> 8) * (1f / 16777216f);
        Assert.True(max < 1f);
        var rng = new SimRng(7, 3);
        for (int i = 0; i < 1_000_000; i++)
        {
            float f = rng.NextFloat();
            Assert.True(f >= 0f && f < 1f, $"draw {i}: {f}");
        }
    }

    [Theory]
    [InlineData(int.MinValue, int.MinValue + 1)]
    [InlineData(int.MaxValue - 1, int.MaxValue)]
    [InlineData(-5, 5)]
    [InlineData(0, 3)]
    public void Rng_NextInt_EdgeRanges_StayInBounds_AndHitEnds(int min, int max)
    {
        var rng = new SimRng(99, 0);
        bool hitMin = false, hitMaxMinus1 = false;
        for (int i = 0; i < 100_000; i++)
        {
            int v = rng.NextInt(min, max);
            Assert.InRange(v, min, max - 1);
            hitMin |= v == min;
            hitMaxMinus1 |= v == max - 1;
        }
        Assert.True(hitMin && hitMaxMinus1);
    }

    [Fact]
    public void Rng_NextInt_IsRoughlyUniform()
    {
        var rng = new SimRng(5, 0);
        var buckets = new int[7];
        const int n = 700_000;
        for (int i = 0; i < n; i++)
            buckets[rng.NextInt(0, 7)]++;
        foreach (int b in buckets)
            Assert.InRange(b, 98_500, 101_500);
    }

    // ---------- SimMath ----------

    [Theory]
    [InlineData(1e6f)]
    [InlineData(-1e6f)]
    [InlineData(1e7f)]
    public void SinCos_LargeFiniteAngles_StayWithinUnitRange(float x) => AssertUnitRange(x);

    [Theory] // regression test for BUG-0003
    [InlineData(1e9f)]
    [InlineData(-1e9f)]
    [InlineData(1e20f)]
    [InlineData(float.MaxValue)]
    [InlineData(-float.MaxValue)]
    public void SinCos_HugeFiniteAngles_StayWithinUnitRange(float x) => AssertUnitRange(x);

    private static void AssertUnitRange(float x)
    {
        // Accuracy is documented to degrade past a few hundred radians, but a sine outside
        // [-1, 1] (or NaN for a finite input) would poison any direction vector built from it.
        float s = SimMath.Sin(x);
        float c = SimMath.Cos(x);
        Assert.False(float.IsNaN(s), $"Sin({x}) = NaN");
        Assert.False(float.IsNaN(c), $"Cos({x}) = NaN");
        Assert.InRange(s, -1.0001f, 1.0001f);
        Assert.InRange(c, -1.0001f, 1.0001f);
    }

    [Fact]
    public void SinCos_ModeratelyLargeAngles_AccuracyAt1e3()
    {
        // Documented: accurate for |x| up to "a few hundred radians". Probe 1000 rad for the record.
        double max = 0;
        for (int i = 0; i <= 10_000; i++)
        {
            float x = -1000f + i * 0.2f;
            max = Math.Max(max, Math.Abs(SimMath.Sin(x) - Math.Sin(x)));
        }
        Assert.True(max <= 1e-3, $"max Sin error on [-1000, 1000]: {max}");
    }

    [Fact]
    public void SimMath_NaNAndInfinity_DoNotThrow()
    {
        float[] bad = { float.NaN, float.PositiveInfinity, float.NegativeInfinity };
        foreach (float v in bad)
        {
            _ = SimMath.Sin(v);
            _ = SimMath.Cos(v);
            _ = SimMath.Atan2(v, 1f);
            _ = SimMath.Atan2(1f, v);
            _ = SimMath.Atan2(v, v);
        }
        Assert.True(float.IsNaN(SimMath.Sin(float.NaN)));
    }

    [Fact]
    public void Atan2_SignedZeroAndAxes_AreInRange_AndCloseToMath()
    {
        (float y, float x)[] cases =
        {
            (0f, 0f), (-0f, 0f), (0f, -0f), (-0f, -0f),
            (0f, -1f), (-0f, -1f), (-0f, 1f), (1f, -0f), (-1f, -0f),
            (1e-30f, 1f), (1f, 1e-30f), (1e30f, 1e30f), (float.Epsilon, -float.Epsilon),
        };
        foreach ((float y, float x) in cases)
        {
            float a = SimMath.Atan2(y, x);
            Assert.InRange(a, -SimMath.Pi, SimMath.Pi);
            if (x == 0f && y == 0f) continue;
            double expected = Math.Atan2(y, x);
            // (-0, -1): Math gives -pi; pi is the same direction, accept either.
            double err = Math.Min(Math.Abs(a - expected), Math.Abs(Math.Abs(a) - Math.PI) + Math.Abs(Math.Abs(expected) - Math.PI));
            Assert.True(err <= 1e-3, $"Atan2({y}, {x}) = {a}, Math = {expected}");
        }
    }

    [Fact]
    public void Atan2_DenseGrid_MatchesMathWithin1e3_AndRoundTripsWithSinCos()
    {
        double max = 0;
        for (int iy = -200; iy <= 200; iy++)
        {
            for (int ix = -200; ix <= 200; ix++)
            {
                float y = iy * 0.013f, x = ix * 0.017f;
                if (x == 0f && y == 0f) continue;
                max = Math.Max(max, Math.Abs(SimMath.Atan2(y, x) - Math.Atan2(y, x)));
            }
        }
        Assert.True(max <= 1e-3, $"max Atan2 error {max}");

        for (int i = -1000; i <= 1000; i++)
        {
            float ang = i * 0.00314f;
            float back = SimMath.Atan2(SimMath.Sin(ang), SimMath.Cos(ang));
            Assert.True(Math.Abs(back - ang) <= 1e-3, $"roundtrip {ang} -> {back}");
        }
    }

    // ---------- Handles ----------

    [Fact]
    public void UnitStore_ZeroOrNegativeCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnitStore(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnitStore(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Simulation(TestSim.Config(1, PlayerCount: 0, UnitCapacity: 4, CommandCapacity: 4)));
    }

    [Fact]
    public void UnitStore_CapacityOne_AllocFreeAllocCycle()
    {
        var s = new UnitStore(1);
        EntityHandle a = s.Alloc();
        Assert.False(s.TryAlloc(out _));
        Assert.Throws<InvalidOperationException>(() => s.Alloc());
        s.Free(a);
        Assert.Throws<ArgumentException>(() => s.Free(a)); // double free
        EntityHandle b = s.Alloc();
        Assert.Equal(a.Index, b.Index);
        Assert.NotEqual(a.Generation, b.Generation);
        Assert.False(s.IsAlive(a));
        Assert.True(s.IsAlive(b));
    }

    [Fact]
    public void UnitStore_ForgedHandles_NeverResolve()
    {
        var s = new UnitStore(8);
        EntityHandle h = s.Alloc();
        Assert.False(s.IsAlive(new EntityHandle(-1, h.Generation)));
        Assert.False(s.IsAlive(new EntityHandle(8, 1)));
        Assert.False(s.IsAlive(new EntityHandle(int.MaxValue, 1)));
        Assert.False(s.IsAlive(new EntityHandle(int.MinValue, 1)));
        Assert.False(s.IsAlive(new EntityHandle(h.Index, h.Generation + 1)));
        Assert.False(s.IsAlive(new EntityHandle(1, 1))); // never allocated
        Assert.Throws<ArgumentException>(() => s.Free(new EntityHandle(1, 1)));
        Assert.Throws<ArgumentException>(() => s.Free(new EntityHandle(-1, 1)));
        Assert.Equal(1, s.Count);
    }

    // ---------- Commands ----------

    [Fact] // regression test for BUG-0004
    public void Enqueue_WhenQueueFull_LeavesStateUnchanged()
    {
        // "Fail explicitly, not corrupt": a rejected command should not advance the player's
        // sequence counter (which is part of the state hash).
        var sim = new Simulation(TestSim.Config(1, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 2));
        sim.Enqueue(Command.Noop(0));
        sim.Enqueue(Command.Noop(0));
        ulong before = sim.StateHash();
        Assert.Throws<InvalidOperationException>(() => sim.Enqueue(Command.Noop(1)));
        Assert.Equal(2, sim.PendingCommandCount);
        Assert.Equal(before, sim.StateHash());
    }

    [Fact]
    public void Enqueue_InvalidPlayer_LeavesStateUnchanged()
    {
        var sim = new Simulation(TestSim.Config(1, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 8));
        ulong before = sim.StateHash();
        Assert.ThrowsAny<ArgumentException>(() => sim.Enqueue(Command.Noop(-1)));
        Assert.ThrowsAny<ArgumentException>(() => sim.Enqueue(Command.Noop(2)));
        Assert.ThrowsAny<ArgumentException>(() => sim.Enqueue(Command.Noop(int.MinValue)));
        Assert.Equal(before, sim.StateHash());
    }

    [Fact]
    public void Enqueue_CallerSuppliedTickAndSequence_AreOverwritten()
    {
        var sim = new Simulation(TestSim.Config(1, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 8));
        var forged = new Command { Kind = CommandKind.SpawnUnit, Player = 0, Tick = -5, Sequence = -100 };
        sim.Enqueue(forged);
        Assert.Equal(0, sim.World.Units.Count);
        sim.Tick(); // tick 0: forged tick -5 must not have made it due already
        Assert.Equal(0, sim.World.Units.Count);
        sim.Tick();
        Assert.Equal(1, sim.World.Units.Count);
    }

    [Fact] // regression test for BUG-0006
    public void SpawnUnit_NonFinitePosition_NeverEntersState()
    {
        var sim = new Simulation(TestSim.Config(1, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 8));
        sim.Enqueue(Command.SpawnUnit(0, 0, new Vector2(float.NaN, float.PositiveInfinity)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y),
                $"slot {i} position {u.Position[i]}");
        }
    }

    [Fact]
    public void UnknownCommandKind_IsIgnoredWithoutCrash()
    {
        var sim = new Simulation(TestSim.Config(1, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 8));
        sim.Enqueue(new Command { Kind = (CommandKind)999, Player = 0 });
        sim.Tick();
        sim.Tick();
        Assert.Equal(0, sim.PendingCommandCount);
        Assert.Equal(0, sim.World.Units.Count);
    }

    [Fact]
    public void Flood_10000Commands_AcrossPlayers_ApplyIn_PlayerSequenceOrder()
    {
        const int players = 8;
        const int n = 10_000;
        var sim = new Simulation(TestSim.Config(11, players, UnitCapacity: n, CommandCapacity: n));
        // Interleave players in a scrambled order; Position.Y records the per-player issue index
        // (M1-4b: TypeId must now name a real unit type, so it can no longer carry the index).
        var rng = new SimRng(1, 0);
        var issued = new int[players];
        for (int i = 0; i < n; i++)
        {
            int p = rng.NextInt(0, players);
            issued[p]++;
            sim.Enqueue(Command.SpawnUnit(p, typeId: i % TestSim.UnitTypeCount, new Vector2(p, issued[p])));
        }
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        Assert.Equal(n, u.Count);
        // Slots are allocated in apply order, so walking slots 0..n-1 must give (player, seq) ascending.
        int lastPlayer = -1, lastIssue = -1;
        for (int i = 0; i < n; i++)
        {
            int p = u.Owner[i];
            Assert.True(p >= lastPlayer, $"slot {i}: player {p} after {lastPlayer}");
            if (p != lastPlayer) { lastPlayer = p; lastIssue = -1; }
            int issue = (int)u.Position[i].Y;
            Assert.True(issue > lastIssue, $"slot {i}: issue {issue} after {lastIssue}");
            lastIssue = issue;
        }
    }

    [Fact]
    public void Flood_CommandsSpreadOverTicks_EachAppliesOnItsOwnTick()
    {
        var sim = new Simulation(TestSim.Config(1, PlayerCount: 3, UnitCapacity: 4096, CommandCapacity: 64));
        for (int t = 0; t < 500; t++)
        {
            int before = sim.World.Units.Count;
            for (int k = 0; k < 5; k++)
                sim.Enqueue(Command.SpawnUnit(k % 3, typeId: t % TestSim.UnitTypeCount, new Vector2(t, k)));
            sim.Tick();
            // Commands enqueued before this tick apply on the following tick, not this one.
            Assert.Equal(t == 0 ? 0 : before + 5, sim.World.Units.Count);
            Assert.Equal(5, sim.PendingCommandCount);
        }
    }

    // ---------- State hash ----------

    [Fact]
    public void Hash_FlippingOneBitOfOneFloat_ChangesHash()
    {
        var sim = new Simulation(TestSim.Config(9, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 64));
        for (int i = 0; i < 20; i++)
            sim.Enqueue(Command.SpawnUnit(i % 2, i % TestSim.UnitTypeCount, new Vector2(i * 1.5f, -i)));
        sim.Tick();
        sim.Tick();
        ulong h0 = sim.StateHash();
        UnitStore u = sim.World.Units;

        void Check(Action mutate, Action undo, string what)
        {
            mutate();
            Assert.NotEqual(h0, sim.StateHash());
            undo();
            Assert.Equal(h0, sim.StateHash());
        }

        Vector2 p = u.Position[13];
        Check(() => u.Position[13] = new Vector2(BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(p.X) ^ 1), p.Y),
              () => u.Position[13] = p, "position lsb");
        float f = u.Facing[7];
        Check(() => u.Facing[7] = -0f, () => u.Facing[7] = f, "facing +0 -> -0");
        Vector2 v = u.Velocity[0];
        Check(() => u.Velocity[0] = new Vector2(v.X, float.Epsilon), () => u.Velocity[0] = v, "velocity epsilon");
        int owner = u.Owner[19];
        Check(() => u.Owner[19] = 1 - owner, () => u.Owner[19] = owner, "owner");
        ulong rngBefore = sim.World.Rng(RngStream.Combat).State;
        sim.World.Rng(RngStream.Combat).NextUInt();
        Assert.NotEqual(h0, sim.StateHash());
        Assert.NotEqual(rngBefore, sim.World.Rng(RngStream.Combat).State);
    }

    [Fact]
    public void Hash_SwappingTwoUnitsPositions_ChangesHash()
    {
        // Order sensitivity: a commutative hash would miss two units trading places.
        var sim = new Simulation(TestSim.Config(9, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 8));
        sim.Enqueue(Command.SpawnUnit(0, 0, new Vector2(1, 2)));
        sim.Enqueue(Command.SpawnUnit(0, 0, new Vector2(3, 4)));
        sim.Tick();
        sim.Tick();
        ulong h0 = sim.StateHash();
        UnitStore u = sim.World.Units;
        (u.Position[0], u.Position[1]) = (u.Position[1], u.Position[0]);
        (u.PrevPosition[0], u.PrevPosition[1]) = (u.PrevPosition[1], u.PrevPosition[0]);
        Assert.NotEqual(h0, sim.StateHash());
    }

    [Fact]
    public void Hash_IsPure_CallingItDoesNotChangeState()
    {
        var sim = new Simulation(TestSim.Config(3, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 16));
        sim.Enqueue(Command.SpawnUnit(1, 2, Vector2.One));
        ulong a = sim.StateHash();
        ulong b = sim.StateHash();
        Assert.Equal(a, b);
        sim.Tick();
        sim.Tick();
        Assert.Equal(sim.StateHash(), sim.StateHash());
    }
}
