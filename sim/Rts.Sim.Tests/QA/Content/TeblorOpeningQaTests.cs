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
    // "Spending on the menu's map" says).
    private const int FirstWaveTick = 8321;

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
    /// change shows. BUG-1240 (D28, S3, for the balance pass): measured on seed 1 the Teblor are defeated before they send
    /// a wave or reach Age II.
    /// </summary>
    [Theory]
    [InlineData("whirlwind", -1, -1, 12991)] // 649 s; the Whirlwind sent 1 wave
    [InlineData("malazan", -1, -1, 16376)]   // 818 s; the Malazan sent 2 waves
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
