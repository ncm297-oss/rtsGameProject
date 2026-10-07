using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M2-7: the --bench step order, loop and end of run.</summary>
[Collection(SerialCollection.Name)]
public class BenchScriptTests
{
    private readonly ITestOutputHelper _out;

    public BenchScriptTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void FirstStepFiresOnFirstFrame_ThenStepsFollowTheTableInOrder()
    {
        var script = new BenchScript(100);
        var fired = new List<(BenchStep, int, double)>();
        for (int frame = 0; frame < 6000 && !script.Finished; frame++)
        {
            if (script.Advance(1.0 / 60, out BenchScript.Entry e)) fired.Add((e.Step, e.Arg, script.Elapsed));
            if (fired.Count == BenchScript.Sequence.Length + 1) break;
        }
        Assert.Equal(BenchScript.Sequence.Length + 1, fired.Count);
        for (int i = 0; i < BenchScript.Sequence.Length; i++)
        {
            Assert.Equal(BenchScript.Sequence[i].Step, fired[i].Item1);
            Assert.Equal(BenchScript.Sequence[i].Arg, fired[i].Item2);
        }
        // The sequence loops back to its first step.
        Assert.Equal(BenchScript.Sequence[0].Step, fired[^1].Item1);
        Assert.Equal(1, script.Loops);
        Assert.Equal(1.0 / 60, fired[0].Item3, 9);
        // Each step fires within one frame of its scheduled time.
        double at = 0;
        for (int i = 0; i < BenchScript.Sequence.Length; i++)
        {
            Assert.InRange(fired[i].Item3, at, at + 1.0 / 60 + 1e-9);
            at += BenchScript.Sequence[i].Seconds;
        }
        _out.WriteLine($"loop {BenchScript.LoopSeconds} s, {BenchScript.Sequence.Length} steps");
    }

    [Fact]
    public void EveryBriefedStepIsInTheLoop_AndAllFourJumpsLandInsideThreeSeconds()
    {
        var steps = BenchScript.Sequence.Select(e => e.Step).ToArray();
        foreach (BenchStep s in Enum.GetValues<BenchStep>()) Assert.Contains(s, steps);
        Assert.Equal(new[] { 0, 1, 2, 3 }, BenchScript.Sequence.Where(e => e.Step == BenchStep.MinimapJump).Select(e => e.Arg));
        Assert.Equal(new[] { 0, 1, 2 }, BenchScript.Sequence.Where(e => e.Step == BenchStep.QueuePoint).Select(e => e.Arg));
        // Box-select comes after the camera is on the army and before the first order.
        Assert.Equal(BenchStep.FocusArmy, steps[0]);
        Assert.Equal(BenchStep.BoxSelectArmy, steps[1]);

        var script = new BenchScript(3);
        int jumps = 0;
        while (!script.Finished)
            if (script.Advance(1.0 / 60, out BenchScript.Entry e) && e.Step == BenchStep.MinimapJump) jumps++;
        Assert.Equal(4, jumps);
    }

    [Fact]
    public void DurationEndsTheRun_NoStepAfterwards()
    {
        var script = new BenchScript(2.5);
        int steps = 0;
        int frames = 0;
        while (!script.Finished)
        {
            if (script.Advance(0.1, out _)) steps++;
            frames++;
            Assert.True(frames < 100);
        }
        Assert.True(script.Elapsed >= 2.5 && script.Elapsed < 2.6 + 1e-9);
        Assert.Equal(steps, script.StepsRun);
        Assert.False(script.Advance(0.1, out _));
        Assert.False(script.Advance(100, out _));
        Assert.Equal(steps, script.StepsRun);
    }

    [Fact]
    public void LongFrame_FiresOneStepPerCall_AndBadDeltasAddNothing()
    {
        var script = new BenchScript(60);
        Assert.True(script.Advance(0, out _)); // first step due at time 0
        Assert.True(script.Advance(5.0, out _));
        Assert.True(script.Advance(0, out _)); // backlog: one step per call, not all at once
        Assert.Equal(3, script.StepsRun);
        double t = script.Elapsed;
        script.Advance(double.NaN, out _);
        script.Advance(-1, out _);
        script.Advance(double.PositiveInfinity, out _);
        Assert.Equal(t, script.Elapsed);
        Assert.False(script.Finished);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void BadDuration_Throws(double seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BenchScript(seconds));

    [Fact]
    public void Advance_AllocatesNothing()
    {
        var script = new BenchScript(1e9);
        int n = 0;
        Action block = () =>
        {
            for (int i = 0; i < 1000; i++)
                if (script.Advance(0.05, out BenchScript.Entry e)) n += e.Arg + 1;
        };
        block();
        AllocationProbe.AssertZero(block, _out);
    }
}
