using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-H3 (docs/01 2026-11-25 (2): a hang guard, not a speed limit): waits for a child process a test started and ends it
/// when the child has used more than its limit of <b>processor time</b>, or when the wall clock passes a ceiling above that
/// limit. A child that is slow only because the machine is busy (other test hosts, the other tracks' suites) uses no more
/// processor time than alone, so it is not ended; a child that spins forever is ended once it has burnt its limit; a child
/// that hangs without spinning (a deadlock, a locked file) is ended at the wall ceiling.
/// </summary>
/// <remarks>
/// On Windows the child is put in a job object right after it starts, and the limit counts the processor time of every
/// process in the job: a <c>dotnet test</c> child does its work in a <c>testhost</c> grandchild, which joins the job when
/// it is started. Off Windows, or if the job cannot be made, only the child's own processor time is counted (the wall
/// ceiling still holds). A process the child starts in the few milliseconds before it is assigned would escape the count;
/// <c>dotnet test</c> starts its test host long after that.
/// </remarks>
internal static class ChildProcessLimit
{
    /// <summary>How a wait ended.</summary>
    internal enum Outcome
    {
        /// <summary>The child exited by itself.</summary>
        Exited,

        /// <summary>The child (with its descendants) used more than the processor-time limit and was ended.</summary>
        OverProcessorTime,

        /// <summary>The wall clock passed the ceiling before the processor-time limit was reached, and the child was ended.</summary>
        OverWallCeiling,
    }

    /// <summary>
    /// The wall ceiling of the rows that give only a processor-time limit: three times the limit. The four-seat end-game
    /// child takes about 191 s alone (3 m 11 s) against its 1,200 s limit; the ceiling (3,600 s) bounds a child that hangs
    /// without spinning to an hour, which a contended Stress run (its child slowed by two to three times) does not reach.
    /// </summary>
    internal const int WallCeilingFactor = 3;

    private const int PollMs = 250;

    /// <summary>
    /// Waits for <paramref name="process"/> (just started, not yet waited on): returns when it exits, or ends it (the whole
    /// tree) once its processor time passes <paramref name="processorLimit"/> or the wall clock passes
    /// <paramref name="wallCeiling"/>. <paramref name="processorTime"/> is what was counted.
    /// </summary>
    internal static Outcome Wait(Process process, TimeSpan processorLimit, TimeSpan wallCeiling, out TimeSpan processorTime)
    {
        using Job? job = Job.TryAssign(process);
        var wall = Stopwatch.StartNew();
        while (true)
        {
            bool exited = process.WaitForExit(PollMs);
            processorTime = ProcessorTime(job, process);
            if (exited) return Outcome.Exited;
            if (processorTime > processorLimit)
            {
                End(job, process);
                return Outcome.OverProcessorTime;
            }
            if (wall.Elapsed > wallCeiling)
            {
                End(job, process);
                return Outcome.OverWallCeiling;
            }
        }
    }

    private static TimeSpan ProcessorTime(Job? job, Process process)
    {
        if (job != null) return job.ProcessorTime;
        try { return process.TotalProcessorTime; }
        catch (InvalidOperationException) { return TimeSpan.Zero; } // exited and released meanwhile
    }

    private static void End(Job? job, Process process)
    {
        job?.Terminate();
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { } // it exited meanwhile
        process.WaitForExit(5_000);
    }

    /// <summary>A Windows job object holding the child and every process it starts afterwards.</summary>
    private sealed class Job : IDisposable
    {
        private IntPtr _handle;

        private Job(IntPtr handle) => _handle = handle;

        /// <summary>A job with <paramref name="process"/> in it, or null where jobs are not available.</summary>
        internal static Job? TryAssign(Process process)
        {
            if (!OperatingSystem.IsWindows()) return null;
            IntPtr h = Native.CreateJobObject(IntPtr.Zero, null);
            if (h == IntPtr.Zero) return null;
            if (!Native.AssignProcessToJobObject(h, process.Handle))
            {
                Native.CloseHandle(h);
                return null;
            }
            return new Job(h);
        }

        /// <summary>User plus kernel time of every process that has been in the job, the exited ones included.</summary>
        internal TimeSpan ProcessorTime
        {
            get
            {
                if (!Native.QueryInformationJobObject(_handle, Native.JobObjectBasicAccountingInformation, out Native.BasicAccounting info,
                        Marshal.SizeOf<Native.BasicAccounting>(), IntPtr.Zero))
                    return TimeSpan.Zero;
                return new TimeSpan(info.TotalUserTime + info.TotalKernelTime); // 100 ns units, as TimeSpan ticks
            }
        }

        internal void Terminate() => Native.TerminateJobObject(_handle, 1);

        public void Dispose()
        {
            if (_handle == IntPtr.Zero) return;
            Native.CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private static class Native
    {
        internal const int JobObjectBasicAccountingInformation = 1;

        [StructLayout(LayoutKind.Sequential)]
        internal struct BasicAccounting
        {
            public long TotalUserTime;
            public long TotalKernelTime;
            public long ThisPeriodTotalUserTime;
            public long ThisPeriodTotalKernelTime;
            public uint TotalPageFaultCount;
            public uint TotalProcesses;
            public uint ActiveProcesses;
            public uint TotalTerminatedProcesses;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool QueryInformationJobObject(IntPtr job, int infoClass, out BasicAccounting info, int length, IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool TerminateJobObject(IntPtr job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
