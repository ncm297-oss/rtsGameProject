using System.Text;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Replays;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-2 criterion 2 (Producer decision 1 of the M5-2 PLAN): an AI's commands go through <c>Simulation.Enqueue</c> and are
/// recorded like a human's; the replay header (format 5) carries the seats; playback builds the sim with the header's
/// factions and no AI seat and re-enqueues the AI's logged commands between the same ticks, so every checkpoint matches.
/// </summary>
public class AiReplayTests
{
    private const int Ticks = 1500;

    /// <summary>A human player 0 (a few moves of its workers) against an AI player 1, recorded from tick 0.</summary>
    private static (Simulation Sim, ReplayRecorder Recorder) Recorded(ulong seed, AiDifficulty difficulty, bool twoAi = false)
    {
        PlayerSeat[] seats = AiScenes.Seats(difficulty, twoAi ? SeatController.Ai : SeatController.Human, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, seed, record: true, out ReplayRecorder? rec);
        for (int t = 0; t < Ticks; t++)
        {
            if (!twoAi && t % 200 == 50)
            {
                // The human walks its own units to the middle now and then.
                Rts.Sim.Entities.UnitStore u = sim.World.Units;
                var mid = sim.World.NavGrid.CellCenter(sim.World.NavGrid.Width / 2, sim.World.NavGrid.Height / 2);
                for (int i = 0; i < u.Capacity; i++)
                    if (u.Alive[i] && u.Owner[i] == 0) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), mid));
            }
            sim.Tick();
        }
        return (sim, rec!);
    }

    [Theory]
    [InlineData(1UL, AiDifficulty.Hard)]
    [InlineData(2UL, AiDifficulty.Easy)]
    [InlineData(3UL, AiDifficulty.Normal)]
    public void AMatchWithAnAiSeat_PlaysBackWithoutTheAi_ToEqualCheckpoints(ulong seed, AiDifficulty difficulty)
    {
        (Simulation sim, ReplayRecorder rec) = Recorded(seed, difficulty);
        Assert.True(sim.AiOf(1)!.CommandsSent > 0, "the AI sent nothing");
        Replay r = rec.ToReplay();
        Assert.Equal(5, r.FormatVersion);
        Assert.Equal(new[] { new ReplaySeat("malazan", SeatController.Human, difficulty), new ReplaySeat("whirlwind", SeatController.Ai, difficulty) }, r.Seats.ToArray());
        Assert.Contains(r.Commands, c => c.Player == 1 && c.Kind == CommandKind.Train);
        Assert.Contains(r.Commands, c => c.Player == 1 && c.Kind == CommandKind.Gather);
        Assert.Equal(sim.AiOf(1)!.CommandsSent + 6, r.Commands.Count(c => c.Player == 1)); // + its hall and five workers

        byte[] bytes = ReplayFormat.Write(r);
        string text = Encoding.ASCII.GetString(bytes);
        Assert.Contains($"\nplayers 2\nseat 0 malazan human {DataLimits.AiDifficultyIds[(int)difficulty]}\nseat 1 whirlwind ai {DataLimits.AiDifficultyIds[(int)difficulty]}\nunit-capacity ", text);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? back));
        ReplayTestRun.AssertEqual(r, back!);
        Assert.Equal(bytes, ReplayFormat.Write(back!));

        // Playback runs no AI: had it run one, its own commands would join the log's and the stamps would not match.
        ReplayResult played = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(Ticks, played.TicksRun);
        Assert.Equal(Ticks / 100, r.Checkpoints.Length);
    }

    [Fact]
    public void TwoAiSeats_PlayBackWithoutEither()
    {
        (Simulation sim, ReplayRecorder rec) = Recorded(4, AiDifficulty.Hard, twoAi: true);
        Assert.True(sim.AiOf(0)!.CommandsSent > 0 && sim.AiOf(1)!.CommandsSent > 0);
        Replay r = rec.ToReplay();
        Assert.All(r.Seats, s => Assert.Equal(SeatController.Ai, s.Controller));
        Assert.True(ReplayPlayer.Run(r, TestSim.Data).Ok);
    }

    /// <summary>The same log played on a sim that runs the AI too diverges (the AI's commands would be sent twice): what playback's no-AI rule avoids.</summary>
    [Fact]
    public void TheLogReplayedWithTheAiOn_WouldSendItsCommandsTwice()
    {
        (Simulation sim, ReplayRecorder rec) = Recorded(1, AiDifficulty.Hard);
        Replay r = rec.ToReplay();
        var again = new Simulation(sim.World.Config); // the recorded seats: player 1 runs its AI again
        int next = 0, differ = 0;
        while (again.TickNumber < 500)
        {
            while (next < r.Commands.Length && r.Commands[next].Tick == again.TickNumber + 1) again.Enqueue(r.Commands[next++]);
            again.Tick();
            if (again.TickNumber % 100 == 0 && again.StateHash() != r.Checkpoints[again.TickNumber / 100 - 1].Hash) differ++;
        }
        Assert.True(again.AiOf(1)!.CommandsSent > 0);
        Assert.True(differ > 0, "the AI's second set of commands changed nothing");
    }

    /// <summary>Recording changes nothing: a twin without a recorder has the same state hash after every tick (2 AI seats).</summary>
    [Fact]
    public void ARecorder_LeavesTheHash_TwinWithoutOne()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Ai);
        Simulation a = AiScenes.Generated(seats, 6, record: true, out _);
        Simulation b = AiScenes.Generated(seats, 6, record: false, out _);
        for (int t = 0; t < 1000; t++)
        {
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"twins differ after tick {a.TickNumber}");
        }
        Assert.Equal(a.AiOf(0)!.CommandsSent, b.AiOf(0)!.CommandsSent);
    }
}
