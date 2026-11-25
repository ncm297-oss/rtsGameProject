using System.Diagnostics;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-H3: <see cref="ChildProcessLimit"/>, the processor-time limit the two-process rows put on their child (docs/01
/// 2026-11-25 (2)), shown on tiny limits: a grandchild that spins is counted and ended on its processor time, a child that
/// sleeps is ended at the wall ceiling, a child that exits is left alone, and through <see cref="MapFileTests.RunChild(Type, string, string, string, TimeSpan, TimeSpan, string)"/>
/// a child test row that spins fails the parent row.
/// </summary>
public class ChildProcessLimitTests
{
    private const string SpinVariable = "RTS_M7H3_SPIN_CHILD", QuickVariable = "RTS_M7H3_QUICK_CHILD", QuickLine = "M7H3-QUICK ";

    private readonly ITestOutputHelper _out;

    public ChildProcessLimitTests(ITestOutputHelper output) => _out = output;

    private static Process Start(string file, string arguments)
    {
        var info = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true };
        return Process.Start(info)!;
    }

    /// <summary>
    /// <c>cmd</c> waits about two seconds (so the job holds it before it starts anything), then starts a PowerShell that
    /// writes its process id and spins. <c>cmd</c> itself uses almost no processor time, so only a count of the whole tree
    /// can end it: it is ended on 3 s of processor time, long before its 120 s wall ceiling, and the spinning grandchild is
    /// gone afterwards.
    /// </summary>
    [Fact]
    public void ASpinningGrandchild_IsCountedAndEnded_OnItsProcessorTime()
    {
        if (!OperatingSystem.IsWindows()) return; // the descendant count is a Windows job object
        string pidFile = Path.Combine(Path.GetTempPath(), $"m7h3-spin-{Environment.ProcessId}-{Environment.TickCount64}.txt");
        using Process p = Start("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & powershell -NoProfile -NonInteractive -Command \"Set-Content -Path '{pidFile}' -Value $PID; while ($true) {{ }}\"");
        var clock = Stopwatch.StartNew();
        ChildProcessLimit.Outcome how = ChildProcessLimit.Wait(p, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(120), out TimeSpan used);
        _out.WriteLine($"{how} after {clock.Elapsed.TotalSeconds:0.0} s wall, {used.TotalSeconds:0.0} s processor");
        Assert.Equal(ChildProcessLimit.Outcome.OverProcessorTime, how);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), $"ended only after {clock.Elapsed.TotalSeconds:0.0} s");
        Assert.True(p.HasExited);
        Assert.True(File.Exists(pidFile), "the grandchild never started");
        int pid = int.Parse(File.ReadAllText(pidFile).Trim());
        File.Delete(pidFile);
        bool gone;
        try
        {
            using Process g = Process.GetProcessById(pid);
            gone = g.WaitForExit(5_000);
        }
        catch (ArgumentException)
        {
            gone = true; // no such process any more
        }
        Assert.True(gone, $"the spinning grandchild {pid} is still running");
    }

    /// <summary>A child that sleeps (no processor time) is ended at the wall ceiling, not left to hang the run.</summary>
    [Fact]
    public void ASleepingChild_IsEnded_AtTheWallCeiling()
    {
        if (!OperatingSystem.IsWindows()) return;
        using Process p = Start("powershell.exe", "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 600\"");
        var clock = Stopwatch.StartNew();
        ChildProcessLimit.Outcome how = ChildProcessLimit.Wait(p, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(3), out TimeSpan used);
        _out.WriteLine($"{how} after {clock.Elapsed.TotalSeconds:0.0} s wall, {used.TotalSeconds:0.0} s processor");
        Assert.Equal(ChildProcessLimit.Outcome.OverWallCeiling, how);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30));
        Assert.True(p.HasExited);
    }

    /// <summary>A child that exits by itself is <see cref="ChildProcessLimit.Outcome.Exited"/>, its processor time counted.</summary>
    [Fact]
    public void AChildThatExits_IsLeftToExit()
    {
        if (!OperatingSystem.IsWindows()) return;
        using Process p = Start("cmd.exe", "/c exit 0");
        Assert.Equal(ChildProcessLimit.Outcome.Exited, ChildProcessLimit.Wait(p, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), out _));
        Assert.Equal(0, p.ExitCode);
    }

    /// <summary>
    /// The shared runner on a quick child row: it passes, and the child's processor time (its <c>dotnet test</c> host and
    /// test host together) is printed: the start-up cost the spinning row's limit below must stay clear of.
    /// </summary>
    [Fact]
    public void RunChild_OnAQuickRow_ReadsItsLine()
    {
        if (Environment.GetEnvironmentVariable(QuickVariable) == "1")
        {
            _out.WriteLine(QuickLine + "done");
            return;
        }
        var clock = Stopwatch.StartNew();
        string line = MapFileTests.RunChild(typeof(ChildProcessLimitTests), nameof(RunChild_OnAQuickRow_ReadsItsLine), QuickVariable, QuickLine,
            TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(360));
        _out.WriteLine($"the child read '{line}' in {clock.Elapsed.TotalSeconds:0.0} s wall");
        Assert.Equal("done", line);
    }

    /// <summary>
    /// A child test row that spins (the variable set) fails the parent row through the shared runner, on 10 s of processor
    /// time (the quick row's whole child takes about 2 s of wall clock), long before its 600 s wall ceiling; run without the variable
    /// the row returns at once.
    /// </summary>
    [Fact]
    public void RunChild_OnASpinningRow_FailsTheRow_OnProcessorTime()
    {
        if (Environment.GetEnvironmentVariable(SpinVariable) == "1")
        {
            while (true) { } // the child: spin until ended
        }
        if (!OperatingSystem.IsWindows()) return;
        var clock = Stopwatch.StartNew();
        Exception? e = Record.Exception(() => MapFileTests.RunChild(typeof(ChildProcessLimitTests), nameof(RunChild_OnASpinningRow_FailsTheRow_OnProcessorTime), SpinVariable, "never ",
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(600)));
        _out.WriteLine($"after {clock.Elapsed.TotalSeconds:0.0} s wall: {e?.Message}");
        Assert.NotNull(e);
        Assert.Contains("of processor time", e!.Message);
        Assert.Contains("and was ended", e.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(300), $"ended only after {clock.Elapsed.TotalSeconds:0.0} s");
    }
}
