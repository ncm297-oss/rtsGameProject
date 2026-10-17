namespace Rts.Game;

/// <summary>Where a <see cref="SimRunner"/>'s replay playback is (M6-V9).</summary>
public enum PlaybackState
{
    /// <summary>No playback: a live match (or nothing started).</summary>
    None,
    /// <summary>Playing: the runner ticks the replay's sim and feeds it the logged commands.</summary>
    Playing,
    /// <summary>The sim reached the recording's tick count with every checkpoint equal; no more ticks run.</summary>
    Ended,
    /// <summary>A checkpoint or a command stamp differed (<see cref="SimRunner.PlaybackError"/>); no more ticks run.</summary>
    Stopped,
}
