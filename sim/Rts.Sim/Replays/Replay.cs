using System;
using System.Collections.Immutable;
using Rts.Sim.Commands;
using Rts.Sim.Map;

namespace Rts.Sim.Replays;

/// <summary>A recorded match: the setup to rebuild the sim, every accepted command as stamped, and state hashes to check against.</summary>
/// <remarks>
/// Made by <see cref="ReplayRecorder.ToReplay"/>, stored by <see cref="ReplayFormat"/>, checked by
/// <see cref="ReplayPlayer"/>. Immutable; <see cref="Validate"/> holds the rules every reader and
/// the player enforce (docs/03 "Save/load and replays").
/// </remarks>
public sealed class Replay
{
    /// <summary>The format version this build writes (4 since M4-2a: the <see cref="Combat"/> switch in the header and an attack target on every command line; 3 since M3-1: the resource capacity and the resource placer's map fields; 2 since M1-7: command lines carry <see cref="Command.Flags"/>).</summary>
    public const int CurrentFormatVersion = 4;

    /// <summary>The oldest format version this build still reads and plays: 3 (M3-1), which has no combat line (combat on) and no attack targets.</summary>
    public const int OldestFormatVersion = 3;

    /// <summary>Largest player count a replay may declare (a format limit that bounds what a file can make the reader allocate).</summary>
    public const int MaxPlayers = 16;

    /// <summary>Largest unit or command capacity a replay may declare (a format limit, as <see cref="MaxPlayers"/>).</summary>
    public const int MaxCapacity = 1_000_000;

    /// <summary>Largest tick count a replay may declare: 24 hours at 20 Hz, so a small file can't declare a playback of years (BUG-0040).</summary>
    public const int MaxTickCount = 1_728_000;

    /// <summary>Format version, <see cref="OldestFormatVersion"/> to <see cref="CurrentFormatVersion"/>; the writer writes the replay in this format.</summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary><see cref="SimConfig.Combat"/> when recorded (format 4; a format 3 replay was recorded with combat on).</summary>
    public bool Combat { get; init; } = true;

    /// <summary><see cref="SimInfo.Version"/> of the build that recorded it (informational; playback does not check it).</summary>
    public required string SimVersion { get; init; }

    /// <summary><see cref="Data.GameData.ContentHash"/> of the data it was recorded with.</summary>
    public required ulong DataHash { get; init; }

    /// <summary>Map generator settings (<see cref="SimConfig.Map"/>).</summary>
    public required MapGenParams Map { get; init; }

    /// <summary>Match seed (<see cref="SimConfig.Seed"/>).</summary>
    public required ulong Seed { get; init; }

    /// <summary><see cref="SimConfig.PlayerCount"/>.</summary>
    public required int PlayerCount { get; init; }

    /// <summary><see cref="SimConfig.UnitCapacity"/>: part of the state hash, and a full store drops spawns.</summary>
    public required int UnitCapacity { get; init; }

    /// <summary><see cref="SimConfig.CommandCapacity"/>.</summary>
    public required int CommandCapacity { get; init; }

    /// <summary><see cref="SimConfig.ResourceCapacity"/>: part of the state hash, and a full store refuses nodes (M3-1).</summary>
    public int ResourceCapacity { get; init; } = Entities.ResourceStore.DefaultCapacity;

    /// <summary>Ticks between checkpoints.</summary>
    public required int CheckpointInterval { get; init; }

    /// <summary>Ticks recorded: playback runs the sim until <c>TickNumber</c> equals this.</summary>
    public required int TickCount { get; init; }

    /// <summary>Every command the sim accepted, as stamped (tick, sequence), in the order it was enqueued.</summary>
    public required ImmutableArray<Command> Commands { get; init; }

    /// <summary>One checkpoint per <see cref="CheckpointInterval"/> ticks: ticks N, 2N, ... up to <see cref="TickCount"/>.</summary>
    public required ImmutableArray<ReplayCheckpoint> Checkpoints { get; init; }

    /// <summary>Checks every rule a playable replay meets; returns the first broken one's code, or <see cref="ReplayError.None"/>.</summary>
    /// <remarks>The data hash is checked by <see cref="ReplayPlayer.Run"/>, which has the data.</remarks>
    public ReplayError Validate()
    {
        if (FormatVersion < OldestFormatVersion || FormatVersion > CurrentFormatVersion) return ReplayError.FormatVersionMismatch;
        // Format 3 has no combat line: it can only say "on".
        if (FormatVersion < 4 && !Combat) return ReplayError.InvalidHeader;
        if (!IsToken(SimVersion)) return ReplayError.InvalidHeader;
        if (Map == null || !MapIsValid(Map)) return ReplayError.InvalidHeader;
        if (PlayerCount < 1 || PlayerCount > MaxPlayers) return ReplayError.InvalidHeader;
        if (UnitCapacity < 1 || UnitCapacity > MaxCapacity) return ReplayError.InvalidHeader;
        if (CommandCapacity < 1 || CommandCapacity > MaxCapacity) return ReplayError.InvalidHeader;
        if (ResourceCapacity < 1 || ResourceCapacity > MaxCapacity) return ReplayError.InvalidHeader;
        if (TickCount < 0 || TickCount > MaxTickCount) return ReplayError.InvalidHeader;
        // Not "at most TickCount": the recorder writes replays shorter than one interval (0 checkpoints).
        if (CheckpointInterval < 1 || CheckpointInterval > MaxTickCount) return ReplayError.InvalidHeader;

        if (Commands.IsDefault) return ReplayError.InvalidCommand;
        var nextSequence = new int[PlayerCount];
        int lastTick = 1, sameTick = 0;
        for (int i = 0; i < Commands.Length; i++)
        {
            Command c = Commands[i];
            if (c.Tick < lastTick || c.Tick > TickCount) return ReplayError.InvalidCommand;
            sameTick = c.Tick == lastTick ? sameTick + 1 : 1;
            lastTick = c.Tick;
            // Commands stamped for one tick all wait in the queue together.
            if (sameTick > CommandCapacity) return ReplayError.InvalidCommand;
            if ((uint)c.Player >= (uint)PlayerCount) return ReplayError.InvalidCommand;
            // The rule Simulation.Enqueue applies, so everything it accepts reads back (BUG-0054).
            if (!c.IsWellFormed()) return ReplayError.InvalidCommand;
            // Format 3 lines have no attack target, so an Attack can't be written there.
            if (FormatVersion < 4 && c.Kind == CommandKind.Attack) return ReplayError.InvalidCommand;
            if (c.Sequence != nextSequence[c.Player]) return ReplayError.InvalidCommand;
            nextSequence[c.Player]++;
        }

        if (Checkpoints.IsDefault || Checkpoints.Length != TickCount / CheckpointInterval) return ReplayError.InvalidCheckpoint;
        for (int i = 0; i < Checkpoints.Length; i++)
        {
            if (Checkpoints[i].Tick != (i + 1) * CheckpointInterval) return ReplayError.InvalidCheckpoint;
        }
        return ReplayError.None;
    }

    /// <summary>True for a non-empty run of printable ASCII without spaces (so it fits one field of a line).</summary>
    internal static bool IsToken(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 64) return false;
        foreach (char ch in s)
            if (ch <= ' ' || ch > '~') return false;
        return true;
    }

    private static bool MapIsValid(MapGenParams map)
    {
        try
        {
            map.Validate();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
