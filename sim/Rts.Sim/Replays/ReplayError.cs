namespace Rts.Sim.Replays;

/// <summary>Why a replay could not be read or played. Codes only: player-facing text comes from data (CLAUDE.md rule 8).</summary>
public enum ReplayError
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>The file ends before its final <c>checksum</c> line (cut short, or empty).</summary>
    Truncated = 1,

    /// <summary>The bytes don't match the file's checksum: corrupted or hand-edited.</summary>
    ChecksumMismatch = 2,

    /// <summary>Not the replay text format: a non-ASCII or control byte, an unexpected line, a bad number, a wrong field count.</summary>
    Malformed = 3,

    /// <summary>The replay's format version is not <see cref="Replay.CurrentFormatVersion"/>.</summary>
    FormatVersionMismatch = 4,

    /// <summary>A header value is out of range: map params, player count, capacities, checkpoint interval, tick count, sim version text.</summary>
    InvalidHeader = 5,

    /// <summary>A command breaks the log rules: player, kind, tick order or range, sequence, or more commands in one tick than the queue holds.</summary>
    InvalidCommand = 6,

    /// <summary>Checkpoints are not exactly one per <see cref="Replay.CheckpointInterval"/> ticks up to <see cref="Replay.TickCount"/>.</summary>
    InvalidCheckpoint = 7,

    /// <summary>The replay was recorded with different game data (<see cref="Data.GameData.ContentHash"/> differs).</summary>
    DataMismatch = 8,

    /// <summary>Playback: the sim refused a command from the log (its queue was full).</summary>
    CommandRejected = 9,

    /// <summary>Playback: the sim stamped a command with a different tick or sequence number than the log has.</summary>
    SequenceMismatch = 10,

    /// <summary>Playback: a checkpoint's state hash differs, so the sim is not deterministic against this replay.</summary>
    CheckpointMismatch = 11,

    /// <summary>The replay file could not be opened or read.</summary>
    Unreadable = 12,
}
