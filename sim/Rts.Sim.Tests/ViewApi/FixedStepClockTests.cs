using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>The view's frame-time to tick accumulator (docs/03 "Presentation timing").</summary>
public class FixedStepClockTests
{
    private const double Frame60 = 1.0 / 60.0;

    private static void AssertAlphaInRange(FixedStepClock clock)
    {
        Assert.InRange(clock.Alpha, 0.0, 1.0);
        Assert.True(clock.Alpha < 1.0, $"alpha {clock.Alpha} must stay below 1");
    }

    [Fact]
    public void SixtyFramesAt60Fps_GiveTwentyTicks_AlphaAlwaysInRange()
    {
        var clock = new FixedStepClock();
        int ticks = 0;
        for (int f = 0; f < 60; f++)
        {
            int n = clock.Advance(Frame60, 1.0);
            Assert.InRange(n, 0, 1);
            ticks += n;
            AssertAlphaInRange(clock);
        }
        Assert.Equal(20, ticks);
    }

    [Fact]
    public void SpeedTwo_GivesFortyTicksPerSecond()
    {
        var clock = new FixedStepClock();
        int ticks = 0;
        for (int f = 0; f < 60; f++)
        {
            ticks += clock.Advance(Frame60, 2.0);
            AssertAlphaInRange(clock);
        }
        Assert.Equal(40, ticks);
    }

    [Fact]
    public void QuarterSpeed_GivesFiveTicksPerSecond()
    {
        var clock = new FixedStepClock();
        int ticks = 0;
        for (int f = 0; f < 60; f++)
            ticks += clock.Advance(Frame60, 0.25);
        Assert.Equal(5, ticks);
    }

    [Fact]
    public void OneSecondStall_RunsFiveTicks_AndDropsTheBacklog()
    {
        var clock = new FixedStepClock();
        Assert.Equal(FixedStepClock.MaxTicksPerFrame, clock.Advance(1.0, 1.0));
        AssertAlphaInRange(clock);

        // The 15 ticks still owed are gone: a normal frame runs at most one.
        int next = clock.Advance(Frame60, 1.0);
        Assert.InRange(next, 0, 1);
        AssertAlphaInRange(clock);

        // And the clock is back to normal pace afterwards.
        int later = 0;
        for (int f = 0; f < 60; f++)
            later += clock.Advance(Frame60, 1.0);
        Assert.InRange(later, 20, 21);
    }

    [Fact]
    public void HugeStall_AtMaxSpeed_StillCapsAtFive()
    {
        var clock = new FixedStepClock();
        Assert.Equal(FixedStepClock.MaxTicksPerFrame, clock.Advance(3600.0, 8.0));
        Assert.InRange(clock.Advance(Frame60, 1.0), 0, 1);
    }

    [Fact]
    public void SpeedZero_RunsNoTicks_AndFreezesAlpha()
    {
        var clock = new FixedStepClock();
        clock.Advance(0.03, 1.0);
        double alpha = clock.Alpha;
        Assert.True(alpha > 0.0);
        for (int f = 0; f < 120; f++)
        {
            Assert.Equal(0, clock.Advance(Frame60, 0.0));
            Assert.Equal(alpha, clock.Alpha);
        }
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(-1.0, 1.0)]
    [InlineData(-1e300, 8.0)]
    [InlineData(1.0 / 60.0, -2.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(1.0 / 60.0, double.NaN)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 1.0)]
    public void ZeroNegativeOrNonFiniteInput_YieldsNothing_NoThrow(double delta, double speed)
    {
        var clock = new FixedStepClock();
        clock.Advance(0.02, 1.0);
        double alpha = clock.Alpha;
        Assert.Equal(0, clock.Advance(delta, speed));
        Assert.Equal(alpha, clock.Alpha);
        // Still works normally afterwards.
        Assert.Equal(1, clock.Advance(0.03, 1.0));
        AssertAlphaInRange(clock);
    }

    [Fact]
    public void Alpha_IsFractionOfTheNextTick()
    {
        var clock = new FixedStepClock();
        Assert.Equal(0.0, clock.Alpha);
        clock.Advance(0.025, 1.0);
        Assert.Equal(0.5, clock.Alpha, 6);
        Assert.Equal(1, clock.Advance(0.0375, 1.0));
        Assert.Equal(0.25, clock.Alpha, 6);
    }
}
