using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// D28: the Teblor's one opening (<c>standard</c>, "War band") played by a Normal AI seat on the menu's generated map
/// (<see cref="AiSpendingReportTests.MenuMap"/>), seed 1, 20 minutes. (1) Against an idle seat, as the other factions'
/// opening rows run (<see cref="AiSpendingReportTests"/>' "Spending on the menu's map"): it sends its first wave, and the
/// tick is named and pinned. (2) Against each other faction's Normal AI: what happens is measured and pinned as a report
/// for the balance pass after M7-4 (BUG-1240: on this seed the Teblor fall before their first wave to both).
/// </summary>
public class TeblorOpeningQaTests
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ticks = 20 * 60 * Tps;

    // Measured 2026-11-11 (D28) on the shipped data: the Teblor's first wave against the idle seat (416 s, as the page's
    // "Spending on the menu's map" says). D31 (2026-11-14): re-measured on the landed opening (V24, its own reserve share
    // 0.25, the Gatherer's carry 15): 8321 -> 6841 (342 s). M6-S10 (sim track, the Keep-corner cure and gatherOverlap 0.5:
    // handed this row for the session): 6841 -> 6321 (316 s).
    private const int FirstWaveTick = 6321;

    private readonly ITestOutputHelper _out;

    public TeblorOpeningQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>The run's first wave, Age II and defeat ticks for player 0 (-1: never), and its waves started.</summary>
    private static (int First, int AgeTwo, int Fell, int Waves) Run(Simulation sim)
    {
        int first = -1, ageTwo = -1, fell = -1;
        for (int t = 0; t < Ticks; t++)
        {
            sim.Tick();
            if (first < 0 && sim.AiOf(0)!.Board.WavesStarted > 0) first = sim.TickNumber;
            if (ageTwo < 0 && sim.World.Age(0) >= 2) ageTwo = sim.TickNumber;
            if (fell < 0 && sim.World.IsDefeated(0)) fell = sim.TickNumber;
        }
        return (first, ageTwo, fell, sim.AiOf(0)!.Board.WavesStarted);
    }

    [Fact]
    public void TheStandardOpening_SendsItsFirstWave_OnTheGeneratedMap_Seed1Normal()
    {
        (PlayerSeat[] seats, int player) = AiOpeningSeeds.SeatsFor(TestSim.Data, "teblor", AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Assert.Equal(0, player);
        Simulation sim = AiSpendingReportTests.MenuMap(seats, 1);
        Assert.Equal(TestSim.Data.FindFaction("teblor"), sim.World.FactionOf(0));
        Assert.Equal("standard", sim.AiOf(0)!.Opening.Id);
        (int first, int ageTwo, int fell, int waves) = Run(sim);
        _out.WriteLine($"teblor standard, Normal v an idle seat, menu map seed 1: first wave at tick {first} ({first / Tps} s), "
            + $"waves started {waves}, Age II at tick {ageTwo}, the Teblor defeated at tick {fell}");
        Assert.True(first >= 0, "the Teblor standard opening sent no wave in 20 minutes on the menu map, seed 1, Normal");
        Assert.Equal(FirstWaveTick, first);
    }

    /// <summary>
    /// The same opening against a Normal AI of each other faction (the Teblor on player 0): a measured report, pinned so a
    /// change shows. BUG-1240 (D28, S3, for the balance pass): measured on seed 1 the Teblor were defeated before they sent
    /// a wave or reached Age II (D28: v Whirlwind (-1, -1, 12991), 649 s; v Malazan (-1, -1, 16376), 818 s). D31
    /// (2026-11-14), re-measured on the landed opening: both matches see a Teblor wave and Age II; the Malazan one stands.
    /// </summary>
    [Theory]
    // M6-S10 (sim track, the Keep-corner cure and gatherOverlap 0.5): v Whirlwind (7241, 9281, 18218) -> (6321, 8581, 20444),
    // the Teblor still beaten but 111 s later, 4 waves sent, the Whirlwind 3; v Malazan (6801, 9321, -1) -> (6461, 8961, -1),
    // the Teblor stand, 2 waves sent, the Malazan 3.
    [InlineData("whirlwind", 6321, 8581, 20444)] // M6-S10: wave 316 s, Age II 429 s, beaten 1022 s (D31: 362 / 464 / 910 s; the Whirlwind sent 1 wave)
    [InlineData("malazan", 6461, 8961, -1)]      // M6-S10: wave 323 s, Age II 448 s, stands at 20:00 (D31: 340 / 466 s; the Malazan sent 1 wave)
    public void TheStandardOpening_AgainstANormalAi_Report(string opponent, int firstWave, int ageTwo, int fell)
    {
        GameData d = TestSim.Data;
        PlayerSeat[] seats =
        {
            new(d.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Normal),
            new(d.FindFaction(opponent), SeatController.Ai, AiDifficulty.Normal),
        };
        Simulation sim = AiSpendingReportTests.MenuMap(seats, 1);
        (int first, int age, int defeated, int waves) = Run(sim);
        _out.WriteLine($"teblor standard v {opponent} {sim.AiOf(1)!.Opening.Id}, both Normal, menu map seed 1: first wave {first} "
            + $"({(first < 0 ? "never" : $"{first / Tps} s")}), waves {waves}, Age II {age}, the Teblor defeated at tick {defeated} "
            + $"({(defeated < 0 ? "stands" : $"{defeated / Tps} s")}); {opponent} waves {sim.AiOf(1)!.Board.WavesStarted}, defeated {sim.World.IsDefeated(1)}");
        Assert.Equal((firstWave, ageTwo, fell), (first, age, defeated));
    }
}
