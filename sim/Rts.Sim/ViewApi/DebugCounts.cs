using System;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>Entity counts for the debug overlay's label (M2-5); reads the unit arrays only.</summary>
public static class DebugCounts
{
    /// <summary>Live units whose state is <see cref="UnitState.Moving"/>.</summary>
    public static int Moving(ReadOnlySpan<bool> alive, ReadOnlySpan<UnitState> state) => InState(alive, state, UnitState.Moving);

    /// <summary>Live units whose state is <paramref name="wanted"/> (M3-V1: the overlay's worker counts use Gathering, Returning and Building).</summary>
    public static int InState(ReadOnlySpan<bool> alive, ReadOnlySpan<UnitState> state, UnitState wanted)
    {
        int n = 0, len = Math.Min(alive.Length, state.Length);
        for (int i = 0; i < len; i++)
        {
            if (alive[i] && state[i] == wanted) n++;
        }
        return n;
    }
}
