namespace Rts.Sim.Replays;

/// <summary>Outcome of <see cref="ReplayPlayer.Run"/>.</summary>
/// <param name="Error"><see cref="ReplayError.None"/> when every checkpoint matched.</param>
/// <param name="Tick">The checkpoint tick that mismatched, or the sim tick at which playback stopped for another error; -1 when it never started.</param>
/// <param name="TicksRun">Ticks the playback sim ran (0 when the replay was refused up front).</param>
/// <param name="ExpectedHash">The replay's hash at <paramref name="Tick"/> for a checkpoint mismatch, else 0.</param>
/// <param name="ActualHash">The playback sim's hash at <paramref name="Tick"/> for a checkpoint mismatch, else 0.</param>
public readonly record struct ReplayResult(ReplayError Error, int Tick, int TicksRun, ulong ExpectedHash, ulong ActualHash)
{
    /// <summary>True when the replay played through and every checkpoint matched.</summary>
    public bool Ok => Error == ReplayError.None;
}
