using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Rts.Sim.Tests;

/// <summary>M7-H3 (BUG-1582): <see cref="HangGuard"/> runs work on a thread of its own and reports a hang at its limit.</summary>
public class HangGuardTests
{
    [Fact]
    public void Work_ThatFinishes_IsDone_OnItsOwnThread()
    {
        int pool = -1;
        bool done = HangGuard.Run(() => pool = Thread.CurrentThread.IsThreadPoolThread ? 1 : 0, TimeSpan.FromSeconds(30), out Exception? error);
        Assert.True(done);
        Assert.Null(error);
        Assert.Equal(0, pool); // not the thread pool, which a busy run can starve
    }

    [Fact]
    public void Work_ThatThrows_HandsBackItsException()
    {
        bool done = HangGuard.Run(() => throw new InvalidOperationException("boom"), TimeSpan.FromSeconds(30), out Exception? error);
        Assert.True(done);
        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public void Work_ThatSpins_IsReportedHung_AtTheLimit()
    {
        var stop = new StrongBox<bool>(false);
        var clock = Stopwatch.StartNew();
        bool done = HangGuard.Run(() => { while (!Volatile.Read(ref stop.Value)) { } }, TimeSpan.FromMilliseconds(300), out Exception? error);
        double took = clock.Elapsed.TotalSeconds;
        Volatile.Write(ref stop.Value, true);
        Assert.False(done);
        Assert.Null(error);
        Assert.True(took >= 0.25 && took < 10, $"{took:0.00} s");
    }
}
