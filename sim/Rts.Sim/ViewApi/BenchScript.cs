using System;

namespace Rts.Sim.ViewApi;

/// <summary>The <c>--bench</c> sequence as a clock-driven state machine (M2-7): which step runs when, and when the run ends.</summary>
/// <remarks>
/// Pure: no Godot, no sim reference, so it is unit-tested. The view feeds it each frame's delta;
/// the first step is due on the first call, each step then waits its <see cref="Entry.Seconds"/>
/// before the next, and the sequence repeats until <see cref="Duration"/> has elapsed. At most one
/// step fires per call, so a long frame delays the steps behind it instead of bunching them.
/// One loop takes <see cref="LoopSeconds"/>; the four minimap jumps all land inside the first 3 s,
/// so <c>--bench 3</c> exercises every camera move. Allocates nothing after construction.
/// </remarks>
public sealed class BenchScript
{
    /// <summary>One scripted step: what to do, its argument (corner or queue-point index) and how long to wait after it.</summary>
    public readonly record struct Entry(BenchStep Step, int Arg, double Seconds);

    /// <summary>The steps in order; the script loops over them.</summary>
    public static readonly Entry[] Sequence =
    {
        new(BenchStep.FocusArmy, 0, 0.25),
        new(BenchStep.BoxSelectArmy, 0, 0.5),
        new(BenchStep.OrderAcross, 0, 1.0),
        new(BenchStep.MinimapJump, 0, 0.25),
        new(BenchStep.MinimapJump, 1, 0.25),
        new(BenchStep.MinimapJump, 2, 0.25),
        new(BenchStep.MinimapJump, 3, 0.25),
        new(BenchStep.FocusArmy, 0, 0.25),
        new(BenchStep.ZoomIn, 0, 1.0),
        new(BenchStep.ZoomOut, 0, 1.0),
        new(BenchStep.AttackMove, 0, 2.0),
        new(BenchStep.QueuePoint, 0, 0.5),
        new(BenchStep.QueuePoint, 1, 0.5),
        new(BenchStep.QueuePoint, 2, 0.5),
        new(BenchStep.Hold, 0, 1.0),
        new(BenchStep.Stop, 0, 0.5),
    };

    /// <summary>Seconds one pass over <see cref="Sequence"/> takes.</summary>
    public static double LoopSeconds
    {
        get
        {
            double s = 0;
            foreach (Entry e in Sequence) s += e.Seconds;
            return s;
        }
    }

    private int _index;
    private double _nextAt;

    /// <summary>Creates a script that runs for <paramref name="durationSeconds"/> (must be positive and finite).</summary>
    public BenchScript(double durationSeconds)
    {
        if (!(durationSeconds > 0) || !double.IsFinite(durationSeconds)) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        Duration = durationSeconds;
    }

    /// <summary>Run length in seconds.</summary>
    public double Duration { get; }

    /// <summary>Seconds fed in so far.</summary>
    public double Elapsed { get; private set; }

    /// <summary>Steps fired so far.</summary>
    public int StepsRun { get; private set; }

    /// <summary>Whole passes over <see cref="Sequence"/> completed.</summary>
    public int Loops { get; private set; }

    /// <summary>True once <see cref="Elapsed"/> reached <see cref="Duration"/>; no step fires after that.</summary>
    public bool Finished { get; private set; }

    /// <summary>Adds one frame's seconds; returns true with the step to run now, or false (nothing due, or finished).</summary>
    /// <remarks>Zero, negative or non-finite deltas add no time.</remarks>
    public bool Advance(double deltaSeconds, out Entry step)
    {
        step = default;
        if (Finished) return false;
        if (deltaSeconds > 0 && double.IsFinite(deltaSeconds)) Elapsed += deltaSeconds;
        if (Elapsed >= Duration)
        {
            Finished = true;
            return false;
        }
        if (Elapsed < _nextAt) return false;
        step = Sequence[_index];
        _nextAt += step.Seconds;
        StepsRun++;
        if (++_index == Sequence.Length)
        {
            _index = 0;
            Loops++;
        }
        return true;
    }
}
