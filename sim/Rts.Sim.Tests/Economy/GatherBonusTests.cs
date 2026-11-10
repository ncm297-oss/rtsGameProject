using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4c slice 2 criterion 2 (docs/02 "AI opponent": Hard's 40% gather bonus, 25% before D27): a seat's workers fill at
/// <c>gatherRate</c> x (1 + its difficulty's <c>gatherBonus</c>). Hard's 0.4 fills a load in 1 / 1.4 of the ticks; a
/// human seat and a Normal one fill at the rate itself, bit for bit (their per-tick hashes: <see cref="HardKnobsPinTests"/>).
/// </summary>
public class GatherBonusTests
{
    private readonly ITestOutputHelper _out;

    public GatherBonusTests(ITestOutputHelper output) => _out = output;

    /// <summary>Ticks of work a load of <paramref name="carry"/> takes at <paramref name="rate"/> a tick, the economy's float steps written out again.</summary>
    private static int ModelTicks(float rate, int carry)
    {
        float progress = 0f;
        int cargo = 0, ticks = 0;
        while (cargo < carry)
        {
            progress += rate;
            ticks++;
            if (progress < 1f) continue;
            int whole = (int)progress;
            cargo += Math.Min(whole, carry - cargo);
            progress -= whole;
        }
        return ticks;
    }

    /// <summary>
    /// The ticks of work player 0's first worker to fill a gold load spent on it (a tick of work moves its progress or its
    /// cargo), on the flat scene with player 0 an AI seat at <paramref name="difficulty"/>.
    /// </summary>
    private static int FirstGoldLoad(AiDifficulty difficulty)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Human));
        UnitStore u = sim.World.Units;
        int carry = sim.World.Data.Rules.WorkerCarry;
        var work = new int[u.Capacity];
        var cargo = new int[u.Capacity];
        var progress = new float[u.Capacity];
        for (int t = 0; t < 3000; t++)
        {
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.Owner[i] != 0 || u.CargoKind[i] != ResourceKind.Gold) continue;
                // The tick that fills the load resets the progress and turns the worker home: counted by its cargo.
                if (cargo[i] < carry && (u.Cargo[i] != cargo[i] || u.GatherProgress[i] != progress[i])) work[i]++;
                if (u.Cargo[i] == carry && cargo[i] < carry) return work[i];
                cargo[i] = u.Cargo[i];
                progress[i] = u.GatherProgress[i];
            }
        }
        throw new InvalidOperationException("no gold load filled in 3,000 ticks");
    }

    [Fact]
    public void AHardWorker_FillsItsCarry_In1Over1Point4OfANormalWorkersTicks()
    {
        RulesDef rules = TestSim.Data.Rules;
        int normal = FirstGoldLoad(AiDifficulty.Normal), hard = FirstGoldLoad(AiDifficulty.Hard);
        _out.WriteLine($"a gold load: Normal {normal} ticks, Hard {hard} ticks (x{(double)hard / normal:F4}); model {ModelTicks(rules.GoldPerTick, rules.WorkerCarry)} / {ModelTicks(rules.GoldPerTick * 1.4f, rules.WorkerCarry)}");
        Assert.Equal(236, normal); // 10 / (0.85 / 20) = 235.3: the 236th tick of work fills it
        Assert.Equal(169, hard); // 10 / (0.85 x 1.4 / 20) = 168.1 (D27, BUG-1163: was 189 at gatherBonus 0.25, 10 / (0.85 x 1.25 / 20) = 188.2)
        Assert.Equal(ModelTicks(rules.GoldPerTick, rules.WorkerCarry), normal);
        Assert.Equal(ModelTicks(rules.GoldPerTick * 1.4f, rules.WorkerCarry), hard);
        Assert.Equal((int)Math.Ceiling(normal / 1.4), hard); // 1 / 1.4 of Normal's ticks, to the whole tick
        // Wood alike, from the model: 10 / (0.75 / 20) = 266.7 and 190.5 (D27: was 213.3, 214, at x1.25).
        Assert.Equal((267, 191), (ModelTicks(rules.WoodPerTick, rules.WorkerCarry), ModelTicks(rules.WoodPerTick * 1.4f, rules.WorkerCarry)));
    }

    /// <summary>The per-player rates: Hard x1.4 (its <c>gatherBonus</c> 0.4; x1.25 / 0.25 before D27), Normal, Easy and a default (human, Normal) seat the rate itself, bit for bit.</summary>
    [Fact]
    public void TheRates_PerSeat()
    {
        RulesDef rules = TestSim.Data.Rules;
        PlayerSeat[] seats =
        {
            new(0, SeatController.Ai, AiDifficulty.Hard), new(1, SeatController.Ai, AiDifficulty.Normal),
            new(0, SeatController.Ai, AiDifficulty.Easy), new(1, SeatController.Human, AiDifficulty.Normal),
        };
        Simulation sim = AiScenes.Flat(seats);
        World w = sim.World;
        Assert.Equal(0.4f, TestSim.Data.Ai[0].Difficulty(AiDifficulty.Hard).GatherBonus); // D27 (BUG-1163): was 0.25f
        Assert.Equal(rules.GoldPerTick * 1.4f, w.GoldPerTickOf[0]);
        Assert.Equal(rules.WoodPerTick * 1.4f, w.WoodPerTickOf[0]);
        for (int p = 1; p < 4; p++)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(rules.GoldPerTick), BitConverter.SingleToInt32Bits(w.GoldPerTickOf[p]));
            Assert.Equal(BitConverter.SingleToInt32Bits(rules.WoodPerTick), BitConverter.SingleToInt32Bits(w.WoodPerTickOf[p]));
        }
        // A match built with the default seats (every seat human, Normal) gathers at the rate.
        var plain = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 64));
        Assert.All(plain.World.GoldPerTickOf, r => Assert.Equal(rules.GoldPerTick, r));
    }

    /// <summary>
    /// Why the bonus follows the difficulty, not the controller: playback builds every seat human with the recorded
    /// difficulty, so a Hard AI's recording gathers at Hard's rate when played back (<c>AiReplayTests</c>' Hard row plays
    /// back hash for hash on this). A human seat given Hard is that case.
    /// </summary>
    [Fact]
    public void APlayedBackHardSeat_GathersAtHardsRate()
    {
        PlayerSeat[] seats = { new(0, SeatController.Human, AiDifficulty.Hard), new(1, SeatController.Human, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Flat(seats);
        Assert.Equal(TestSim.Data.Rules.GoldPerTick * 1.4f, sim.World.GoldPerTickOf[0]); // D27: was x1.25f
        Assert.Equal(TestSim.Data.Rules.GoldPerTick, sim.World.GoldPerTickOf[1]);
    }
}
