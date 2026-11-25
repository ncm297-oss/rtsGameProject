using System.Diagnostics;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M7-H3, session 2026-11-25-0924): attacks on the hang guards beyond the developer's own rows. The ruling (docs/01
/// 2026-11-25 (2)) is "a hang guard, not a speed limit": a child that is slow on the wall clock but uses little processor
/// time must not be ended, and the convergence Perf row's judge must still fail a slow tick that shows in every run even
/// when it lands on a different tick in each.
/// </summary>
public class M7H3QaTests
{
    private readonly ITestOutputHelper _out;

    public M7H3QaTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// The heart of the ruling: a child that takes longer on the wall clock than its processor-time limit (it sleeps 8 s
    /// against a 4 s limit, as a child starved by other suites would wait) exits by itself and is not ended. Under the old
    /// wall-clock limit it would have been killed at 4 s.
    /// </summary>
    [Fact]
    public void AChildSlowOnTheWallClock_ButLightOnProcessorTime_IsNotEnded()
    {
        if (!OperatingSystem.IsWindows()) return; // the descendant count is a Windows job object
        var info = new ProcessStartInfo("cmd.exe", "/c ping -n 9 127.0.0.1 >nul & exit 3") { UseShellExecute = false, CreateNoWindow = true };
        using Process p = Process.Start(info)!;
        var clock = Stopwatch.StartNew();
        ChildProcessLimit.Outcome how = ChildProcessLimit.Wait(p, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(120), out TimeSpan used);
        _out.WriteLine($"{how} after {clock.Elapsed.TotalSeconds:0.0} s wall, {used.TotalSeconds:0.00} s processor");
        Assert.Equal(ChildProcessLimit.Outcome.Exited, how);
        Assert.True(clock.Elapsed > TimeSpan.FromSeconds(4), $"the child took only {clock.Elapsed.TotalSeconds:0.0} s: the row proves nothing");
        Assert.Equal(3, p.ExitCode); // it ended by itself, not by the job's terminate (exit code 1)
    }

    /// <summary>
    /// The least-worst-tick judge (BUG-1539) on made-up times: a 20 ms tick in every run fails the 8 ms bound even when it
    /// falls on a different tick in each run (the judge takes the least of the runs' maxima, not a per-tick minimum, which
    /// would read 2 ms here and hide a slow tick that is real).
    /// </summary>
    [Fact]
    public void TheConvergenceJudge_FailsASlowTickInEveryRun_AtDifferentTicks()
    {
        var runs = new List<double[]>();
        int[] at = { 10, 200, 399 };
        for (int r = 0; r < LocalMovementTests.Serial.WorstOfRuns; r++)
        {
            double[] times = Enumerable.Repeat(2.0, 400).ToArray();
            times[at[r]] = 20.0;
            runs.Add(times);
        }
        (double _, double leastWorst) = LocalMovementTests.Serial.Judge(runs);
        Assert.Equal(20.0, leastWorst);
        Assert.False(leastWorst < 8.0);
    }

    /// <summary>The judge's run count is the ruling's three (docs/01 2026-11-25 (2)): a larger count would be a loosening.</summary>
    [Fact]
    public void TheConvergenceJudge_TakesThreeRuns_NotMore() => Assert.Equal(3, LocalMovementTests.Serial.WorstOfRuns);
}
