using Rts.Sim.Determinism;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-1): fuzzes the view's frame-time accumulator against an independent model of the cap rule.</summary>
public class FixedStepClockQaTests
{
    private const double T = FixedStepClock.TickSeconds;

    // Reference model kept deliberately naive: owed time in double seconds, whole ticks fire when
    // owed time reaches a tick (1 us slack for rounding), at most 5 a frame, and after a capped
    // frame less than one tick may stay owed.
    private sealed class Model
    {
        public double Owed;

        public int Advance(double delta, double speed)
        {
            double scaled = delta * speed;
            if (double.IsNaN(scaled) || double.IsInfinity(scaled) || scaled <= 0) return 0;
            Owed += scaled;
            int n = 0;
            while (Owed >= T - FixedStepClock.Epsilon && n < FixedStepClock.MaxTicksPerFrame)
            {
                Owed -= T;
                n++;
            }
            if (Owed < 0) Owed = 0;
            if (Owed >= T - FixedStepClock.Epsilon) Owed = T - 2 * FixedStepClock.Epsilon;
            return n;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(42)]
    [InlineData(1234)]
    public void Fuzz_RandomDeltasAndSpeeds_MatchModel_AlphaInRange(ulong seed)
    {
        var rng = new SimRng(seed, 77);
        var clock = new FixedStepClock();
        var model = new Model();
        long ticks = 0;
        for (int f = 0; f < 10_000; f++)
        {
            // Mostly normal frames, sometimes stalls up to 2 s.
            double delta = rng.NextInt(0, 10) == 0 ? rng.NextFloat() * 2.0 : rng.NextFloat() * 0.05;
            double speed = 0.25 + rng.NextFloat() * 7.75;
            int n = clock.Advance(delta, speed);
            int expected = model.Advance(delta, speed);
            Assert.True(n == expected, $"frame {f}: delta {delta} speed {speed} gave {n} ticks, model {expected}");
            Assert.InRange(n, 0, FixedStepClock.MaxTicksPerFrame);
            double a = clock.Alpha;
            Assert.True(a >= 0.0 && a < 1.0, $"frame {f}: alpha {a}");
            Assert.True(Math.Abs(a * T - model.Owed) < 1e-6, $"frame {f}: alpha {a} vs model owed {model.Owed / T}");
            ticks += n;
        }
        Assert.True(ticks > 0);
    }

    [Theory]
    [InlineData(60.0, 1.0)]
    [InlineData(144.0, 1.0)]
    [InlineData(30.0, 1.0)]
    [InlineData(240.0, 1.0)]
    [InlineData(60.0, 0.25)]
    [InlineData(60.0, 2.0)]
    [InlineData(60.0, 8.0)]
    public void SteadyFrameRate_NoDriftOverAnHour(double fps, double speed)
    {
        var clock = new FixedStepClock();
        int frames = (int)(fps * 3600);
        long ticks = 0;
        for (int f = 0; f < frames; f++)
        {
            int n = clock.Advance(1.0 / fps, speed);
            Assert.True(n <= FixedStepClock.MaxTicksPerFrame);
            ticks += n;
        }
        long expected = (long)Math.Floor(3600.0 * speed / T + 1e-6);
        // Ticks owed but not yet run at the end: under one.
        Assert.InRange(ticks, expected - 1, expected);
    }

    [Fact]
    public void Exact60Fps_TickPatternIsStrictlyEveryThirdFrame()
    {
        var clock = new FixedStepClock();
        for (int f = 1; f <= 600_000; f++)
        {
            int n = clock.Advance(1.0 / 60.0, 1.0);
            int expected = f % 3 == 0 ? 1 : 0;
            if (n != expected) Assert.Fail($"frame {f}: {n} ticks, expected {expected} (drift)");
        }
    }

    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(1.0 / 60, double.NaN)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 1.0)]
    [InlineData(1.0 / 60, double.PositiveInfinity)]
    [InlineData(double.PositiveInfinity, 0.0)]
    [InlineData(-1.0, -1.0, Skip = "BUG-0041: negative delta x negative speed is a positive step; un-skip when fixed")]
    [InlineData(double.Epsilon, 1.0)]
    public void NonFiniteOrNegativeInputs_AddNoTicks_AndKeepAlpha(double delta, double speed)
    {
        var clock = new FixedStepClock();
        clock.Advance(0.03, 1.0);
        double before = clock.Alpha;
        int n = clock.Advance(delta, speed);
        Assert.Equal(0, n);
        Assert.True(Math.Abs(clock.Alpha - before) < 1e-12, $"alpha moved {before} -> {clock.Alpha}");
        // Still works afterwards.
        Assert.Equal(1, clock.Advance(0.02, 1.0));
    }

    [Theory]
    [InlineData(1e6)]
    [InlineData(1e300)]
    public void HugeFinite_Stall_CapsAtFive_AndRecovers(double delta)
    {
        var clock = new FixedStepClock();
        Assert.Equal(FixedStepClock.MaxTicksPerFrame, clock.Advance(delta, 8.0));
        Assert.InRange(clock.Alpha, 0.0, 0.9999999999);
        Assert.InRange(clock.Advance(1.0 / 60, 1.0), 0, 1);
    }

    [Fact]
    public void BackToBackStalls_NeverExceedFivePerFrame_NorSixInTwoNormalFrames()
    {
        var clock = new FixedStepClock();
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(5, clock.Advance(2.0, 8.0));
            Assert.True(clock.Alpha < 1.0);
        }
        int next = clock.Advance(1.0 / 60, 1.0) + clock.Advance(1.0 / 60, 1.0);
        Assert.InRange(next, 0, 1);
    }
}
