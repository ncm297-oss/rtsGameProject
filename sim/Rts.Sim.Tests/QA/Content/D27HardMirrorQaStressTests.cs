using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA, D27 (Hard's <c>gatherBonus</c> 0.25 -> 0.4, BUG-1163): with both seats on the faster Hard economy, a Hard v Hard
/// mirror on Vathar Crossing must still end inside the tally's 72,000-tick bound (no new stall from two richer armies), and
/// a Hard v Normal Vathar match must replay hash for hash (the per-seat rate is derived, not saved: two fresh runs agree).
/// </summary>
[Trait("Category", "Stress")]
public class D27HardMirrorQaStressTests
{
    private readonly ITestOutputHelper _out;

    public D27HardMirrorQaStressTests(ITestOutputHelper output) => _out = output;

    private static Simulation Vathar(AiDifficulty a, AiDifficulty b, ulong seed)
    {
        GameData data = TestSim.Data;
        int size = data.Maps[data.FindMap("vathar_crossing")].Size;
        PlayerSeat[] seats = { new(0, SeatController.Ai, a), new(1, SeatController.Ai, b) };
        return AiScenes.Generated(seats, seed, false, out _, data: data,
            configure: c => c with { MapId = "vathar_crossing", Map = c.Map with { Width = size, Height = size } });
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(5UL)]
    public void HardVersusHard_OnVatharCrossing_Ends(ulong seed)
    {
        Simulation sim = Vathar(AiDifficulty.Hard, AiDifficulty.Hard, seed);
        while (sim.TickNumber < 72_000 && !sim.MatchOver) sim.Tick();
        string winner = !sim.MatchOver ? "none" : sim.World.IsDefeated(1) ? "seat 0" : "seat 1";
        _out.WriteLine($"D27 mirror vathar_crossing seed {seed}: {winner} at {sim.TickNumber}");
        Assert.True(sim.MatchOver, $"Hard v Hard on Vathar Crossing seed {seed} did not end by tick 72,000");
    }

    [Fact]
    public void HardVersusNormal_OnVatharCrossing_TwoRunsHashEqual()
    {
        Simulation a = Vathar(AiDifficulty.Normal, AiDifficulty.Hard, 2), b = Vathar(AiDifficulty.Normal, AiDifficulty.Hard, 2);
        for (int t = 0; t < 12_000; t++)
        {
            a.Tick();
            b.Tick();
            if (t % 200 == 0) Assert.True(a.StateHash() == b.StateHash(), $"diverged at tick {a.TickNumber}");
        }
        Assert.Equal(a.StateHash(), b.StateHash());
    }
}
