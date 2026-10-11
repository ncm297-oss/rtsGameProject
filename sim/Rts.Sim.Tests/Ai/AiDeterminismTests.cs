using Rts.Sim.Ai;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-2 criterion 4: a match with AI seats is deterministic. Two runs of a generated-map match with two AI seats (Hard and
/// Easy, both factions, combat on), 3 seeds x 2,000 ticks, have equal state hashes after every tick, and their AIs sent the
/// same number of commands. The AI's own state is not hashed: what it sends is, through the command queue and its effects.
/// </summary>
public class AiDeterminismTests
{
    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(12UL)]
    public void TwoAiSeats_TwoRuns_SameHashEveryTick(ulong seed)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Hard), new(1, SeatController.Ai, AiDifficulty.Easy) };
        Simulation a = AiScenes.Generated(seats, seed, record: false, out _);
        Simulation b = AiScenes.Generated(seats, seed, record: false, out _);
        for (int t = 0; t < 2000; t++)
        {
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: the runs differ after tick {a.TickNumber}");
        }
        for (int p = 0; p < 2; p++)
        {
            Assert.True(a.AiOf(p)!.CommandsSent > 0, $"seed {seed}: AI {p} sent nothing");
            Assert.Equal(a.AiOf(p)!.CommandsSent, b.AiOf(p)!.CommandsSent);
            Assert.Equal(a.AiOf(p)!.Thinks, b.AiOf(p)!.Thinks);
        }
    }

    /// <summary>A think writes nothing but its commands: called between ticks, it adds to the queue exactly the commands it reports sent, and with none the state hash doesn't move.</summary>
    [Fact]
    public void AThink_ChangesTheStateOnlyThroughItsCommands()
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Hard), new(1, SeatController.Human, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Generated(seats, 3, record: false, out _);
        for (int t = 0; t < 400; t++) sim.Tick();
        while (sim.TickNumber % sim.AiOf(0)!.ThinkTicks != 0) sim.Tick();
        int pending = sim.PendingCommandCount, sent = sim.AiOf(0)!.CommandsSent;
        ulong before = sim.StateHash();
        sim.AiOf(0)!.Think();
        // The hash covers the pending commands: equal unless the think sent something.
        Assert.Equal(sim.AiOf(0)!.CommandsSent - sent, sim.PendingCommandCount - pending);
        if (sim.PendingCommandCount == pending) Assert.Equal(before, sim.StateHash());
    }
}
