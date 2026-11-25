namespace Rts.Sim.Tests;

/// <summary>
/// M7-H3 (BUG-1582, docs/01 2026-11-25 (2): a hang guard, not a speed limit): runs a piece of work on a thread of its own
/// (never the thread pool, which a busy Stress run can starve for minutes) and waits for it with a limit. The work's
/// exception, if any, comes back to the caller. A thread that is still running at the limit is left behind (a background
/// thread: it cannot keep the test process alive) and the caller fails its row.
/// </summary>
internal static class HangGuard
{
    /// <summary>
    /// Runs <paramref name="work"/> on a new background thread and waits up to <paramref name="limit"/>; true when it finished
    /// (with <paramref name="error"/> its exception, or null), false when it was still running at the limit.
    /// </summary>
    internal static bool Run(Action work, TimeSpan limit, out Exception? error)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { work(); }
            catch (Exception e) { caught = e; }
        })
        {
            IsBackground = true,
            Name = "HangGuard",
        };
        thread.Start();
        bool done = thread.Join(limit);
        error = done ? caught : null;
        return done;
    }
}
