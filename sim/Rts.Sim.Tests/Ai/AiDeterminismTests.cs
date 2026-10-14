using Rts.Sim.Ai;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-2 criterion 4: a match with AI seats is deterministic. Two runs of a generated-map match with two AI seats (Hard and
/// Easy, both factions, combat on), 3 seeds x 2,000 ticks, have equal state hashes after every tick, and their AIs sent the
/// same number of commands. M5-3: with the test's early-wave data (<see cref="AiScenes.EarlyWaves"/>) and six raiders each in
/// the other's base (<see cref="AiScenes.Generated"/>), the two seats waved, fought and retreated within the run; since M5-4b
/// they defend their bases against those raiders instead (no wave starts while a threat holds), and a run without raiders
/// covers the waves. The AI's own state is not hashed: what it sends is, through the command queue and its effects.
/// </summary>
public class AiDeterminismTests
{
    /// <summary>
    /// The two runs, <paramref name="ticks"/> long: equal hashes after every tick, the same commands and thinks; each seat
    /// waved (<paramref name="waves"/>) or defended its base (<paramref name="defends"/>), the same in both runs.
    /// </summary>
    private static void TwoRuns(ulong seed, int raiders, int ticks, bool waves, bool defends)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Hard), new(1, SeatController.Ai, AiDifficulty.Easy) };
        Simulation a = AiScenes.Generated(seats, seed, record: false, out _, data: s_early.Value, raiders: raiders);
        Simulation b = AiScenes.Generated(seats, seed, record: false, out _, data: s_early.Value, raiders: raiders);
        for (int t = 0; t < ticks; t++)
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
            if (waves) Assert.True(a.AiOf(p)!.Board.WavesStarted > 0, $"seed {seed}: AI {p} never attacked");
            Assert.Equal(a.AiOf(p)!.Board.WavesStarted, b.AiOf(p)!.Board.WavesStarted);
            if (defends) Assert.True(a.AiOf(p)!.Board.Defenses > 0, $"seed {seed}: AI {p} never defended");
            Assert.Equal(a.AiOf(p)!.Board.Defenses, b.AiOf(p)!.Board.Defenses);
            Assert.Equal(a.AiOf(p)!.Board.DefenseVersion, b.AiOf(p)!.Board.DefenseVersion);
        }
    }

    /// <summary>
    /// M5-4b: a defended base. Six raiders of each seat start in the other's base, so both seats defend from their first
    /// think: the army is pulled home, the workers flee, no wave starts while the raiders stand there (since M5-4b no wave is
    /// expected here: before it, these runs waved).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(12UL)]
    public void TwoAiSeats_DefendedBases_TwoRuns_SameHashEveryTick(ulong seed) => TwoRuns(seed, raiders: 6, ticks: 2000, waves: false, defends: true);

    /// <summary>M5-3's waves without raiders: both seats build an army and attack within 6,000 ticks; the waves reach the other base, which defends.</summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    public void TwoAiSeats_Waves_TwoRuns_SameHashEveryTick(ulong seed) => TwoRuns(seed, raiders: 0, ticks: 6000, waves: true, defends: false);

    private static readonly Lazy<GameData> s_early = new(() => AiScenes.EarlyWaves());

    /// <summary>
    /// M5-4c criterion 3: a casting match. Two Normal AI seats (both factions, combat on), each with two of every caster type of
    /// its faction (Cadre Mage and Sapper, Priest) among the other's workers from the start: both seats cast within the run,
    /// and two runs hash equal after every tick, with the same casts sent.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(12UL)]
    public void TwoAiSeats_Casting_TwoRuns_SameHashEveryTick(ulong seed)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation a = AiScenes.Generated(seats, seed, record: false, out _, casters: 2);
        Simulation b = AiScenes.Generated(seats, seed, record: false, out _, casters: 2);
        int landed = 0;
        for (int t = 0; t < 4000; t++)
        {
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: the runs differ after tick {a.TickNumber}");
            foreach (Abilities.AbilityEvent e in a.World.AbilityEvents)
                if (e.Resolved) landed++;
        }
        for (int p = 0; p < 2; p++)
        {
            Assert.True(a.AiOf(p)!.Board.CastsSent > 0, $"seed {seed}: AI {p} never cast");
            Assert.Equal(a.AiOf(p)!.Board.CastsSent, b.AiOf(p)!.Board.CastsSent);
            Assert.Equal(a.AiOf(p)!.CommandsSent, b.AiOf(p)!.CommandsSent);
        }
        Assert.True(landed > 0, $"seed {seed}: no cast landed");
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
