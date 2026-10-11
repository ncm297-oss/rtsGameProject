using System;
using System.Collections.Immutable;
using Rts.Sim.Commands;
using Rts.Sim.Data;

namespace Rts.Sim.Replays;

/// <summary>Re-runs a <see cref="Replay"/> on a fresh sim and compares every checkpoint hash.</summary>
public static class ReplayPlayer
{
    /// <summary>Plays <paramref name="replay"/> against <paramref name="data"/>; stops at the first error or checkpoint mismatch.</summary>
    /// <remarks>
    /// Refuses before any tick when the replay breaks <see cref="Replay.Validate"/> (format version
    /// first) or was recorded with other data. Each command is enqueued when <c>TickNumber</c> is
    /// its tick minus one, in log order, so the sim stamps it exactly as it did when recording; the
    /// stamp is checked against the log. Checkpoints are hashed by a recorder on the playback sim,
    /// so recording and playback hash at the same point of the tick.
    /// </remarks>
    /// <remarks>The sim plays with the replay's own <see cref="Replay.Combat"/> switch (format 4; on for format 3) and (format 5) the header's factions with every seat human (no AI runs: its commands are in the log); a seat naming a faction the data lacks is <see cref="ReplayError.InvalidHeader"/>.</remarks>
    public static ReplayResult Run(Replay replay, GameData data)
    {
        ArgumentNullException.ThrowIfNull(replay);
        return Run(replay, data, replay.Combat);
    }

    /// <summary>
    /// <see cref="Run(Replay, GameData)"/> with <see cref="SimConfig.Combat"/> given by the caller, overriding the header's:
    /// for format 3 replays recorded by a sim with combat off (the pre-M4 test scenes, BUG-0135), which can't say so.
    /// </summary>
    public static ReplayResult Run(Replay replay, GameData data, bool combat)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(data);
        ReplayError invalid = replay.Validate();
        if (invalid != ReplayError.None) return Refused(invalid);
        if (data.ContentHash() != replay.DataHash) return Refused(ReplayError.DataMismatch);

        // M5-2 (format 5): the header's factions, every seat human: the AI's recorded commands play its part, so playback
        // never runs an AI. A format 3 / 4 replay was recorded with the default seats.
        ImmutableArray<PlayerSeat> seats = ImmutableArray<PlayerSeat>.Empty;
        if (!replay.Seats.IsDefaultOrEmpty)
        {
            var builder = ImmutableArray.CreateBuilder<PlayerSeat>(replay.Seats.Length);
            foreach (ReplaySeat seat in replay.Seats)
            {
                int faction = seat.Faction == "-" ? -1 : data.FindFaction(seat.Faction);
                if (faction < 0 && (seat.Faction != "-" || data.Factions.Length > 0)) return Refused(ReplayError.InvalidHeader);
                builder.Add(new PlayerSeat(faction, SeatController.Human, seat.Difficulty));
            }
            seats = builder.MoveToImmutable();
        }
        var config = new SimConfig(replay.Seed, replay.PlayerCount, replay.UnitCapacity, replay.CommandCapacity)
        {
            Data = data,
            Map = replay.Map,
            ResourceCapacity = replay.ResourceCapacity,
            Combat = combat,
            Seats = seats,
        };
        var sim = new Simulation(config);
        var recorder = new ReplayRecorder(sim, replay.CheckpointInterval, replay.TickCount, replay.Commands.Length);

        int next = 0, compared = 0;
        while (true)
        {
            while (next < replay.Commands.Length && replay.Commands[next].Tick == sim.TickNumber + 1)
            {
                Command logged = replay.Commands[next];
                try
                {
                    sim.Enqueue(logged);
                }
                catch (InvalidOperationException)
                {
                    return Stopped(ReplayError.CommandRejected, sim);
                }
                ref readonly Command stamped = ref recorder.CommandAt(recorder.CommandCount - 1);
                if (stamped.Tick != logged.Tick || stamped.Sequence != logged.Sequence)
                    return Stopped(ReplayError.SequenceMismatch, sim);
                next++;
            }
            if (sim.TickNumber == replay.TickCount) break;

            sim.Tick();
            if (recorder.CheckpointCount > compared)
            {
                ReplayCheckpoint expected = replay.Checkpoints[compared];
                ReplayCheckpoint actual = recorder.CheckpointAt(compared);
                if (actual != expected)
                    return new ReplayResult(ReplayError.CheckpointMismatch, actual.Tick, sim.TickNumber, expected.Hash, actual.Hash);
                compared++;
            }
        }
        return new ReplayResult(ReplayError.None, sim.TickNumber, sim.TickNumber, 0, 0);
    }

    private static ReplayResult Refused(ReplayError error) => new(error, -1, 0, 0, 0);

    private static ReplayResult Stopped(ReplayError error, Simulation sim) => new(error, sim.TickNumber, sim.TickNumber, 0, 0);
}
