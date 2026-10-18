namespace Rts.Sim.Determinism;

// M6-S2: a stream restored from a save, word for word, so a loaded sim draws exactly what the unsaved one would.
public partial struct SimRng
{
    /// <summary>A stream at <paramref name="state"/> with <paramref name="increment"/> (odd: <see cref="IsValidIncrement"/>), as <see cref="State"/> and <see cref="Increment"/> read them.</summary>
    internal static SimRng FromWords(ulong state, ulong increment) => new(state, increment, restore: true);

    /// <summary>Whether <paramref name="increment"/> is one a stream can have (every constructed stream's is odd).</summary>
    internal static bool IsValidIncrement(ulong increment) => (increment & 1UL) == 1UL;

    private SimRng(ulong state, ulong increment, bool restore)
    {
        _ = restore; // only tells this constructor apart from the seeding one
        _state = state;
        _increment = increment;
    }
}
