using System;

namespace Rts.Sim.ViewApi;

/// <summary>Turns variable frame times into whole 50 ms sim ticks plus an interpolation alpha (docs/03 "Presentation timing").</summary>
/// <remarks>
/// Pure bookkeeping for the view's <c>SimRunner</c>: it never touches the sim. Time is kept in
/// double seconds; a tick fires when the accumulator is within <see cref="Epsilon"/> of a full
/// tick, so 60 frames of 1/60 s give exactly 20 ticks despite rounding.
/// </remarks>
public sealed class FixedStepClock
{
    /// <summary>Most ticks one frame may run; after a stall the backlog is dropped instead (no death spiral).</summary>
    public const int MaxTicksPerFrame = 5;

    /// <summary>Length of one tick in seconds.</summary>
    public const double TickSeconds = SimConstants.TickMs / 1000.0;

    /// <summary>Rounding slack in seconds (1 microsecond).</summary>
    public const double Epsilon = 1e-6;

    private double _accumulator;

    /// <summary>Fraction of the next tick already elapsed, in [0, 1); views lerp previous to current by it.</summary>
    public double Alpha => Math.Clamp(_accumulator / TickSeconds, 0.0, 1.0 - 1e-9);

    /// <summary>Adds one frame of real time scaled by <paramref name="speed"/> and returns how many ticks to run now (0 to <see cref="MaxTicksPerFrame"/>).</summary>
    /// <remarks>Zero, negative or non-finite inputs add nothing, so a paused game keeps its alpha.</remarks>
    public int Advance(double deltaSeconds, double speed)
    {
        // Each input is checked on its own: two negatives must not multiply into a positive step (BUG-0041).
        if (!(deltaSeconds > 0.0) || !(speed > 0.0)) return 0;
        double scaled = deltaSeconds * speed;
        if (!(scaled > 0.0) || double.IsInfinity(scaled)) return 0;

        _accumulator += scaled;
        int ticks = 0;
        while (_accumulator + Epsilon >= TickSeconds && ticks < MaxTicksPerFrame)
        {
            _accumulator -= TickSeconds;
            ticks++;
        }
        if (_accumulator < 0.0) _accumulator = 0.0;
        // Hit the cap with time still owed: keep less than one tick so the next frame can't burst again.
        if (_accumulator + Epsilon >= TickSeconds) _accumulator = TickSeconds - 2 * Epsilon;
        return ticks;
    }
}
