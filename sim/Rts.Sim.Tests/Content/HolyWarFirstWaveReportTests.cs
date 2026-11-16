using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D-R39 (2026-11-15, after the sim's M6-S10 gather change): does the Whirlwind <c>holy_war</c> opening fall before its
/// first wave is due? On (0, 2) seed 3 of <see cref="AiOpeningPairsReportTests"/> the Malazan standard opening's first
/// wave wipes its army and it is defeated at 12:29 with no wave sent. Measured here against all three Malazan openings on
/// each pairing's three smallest seeds in 1-500 (the seeds of the pages' Age II tables), two Normal AI seats on the menu's
/// map, 20 minutes. Per run: the Malazan first wave, when <c>holy_war</c>'s first wave was due
/// (<see cref="AiOpeningPairsReportTests.WaveDue"/>: its army at the opening's 20-pop threshold, or the starved charge),
/// its first wave and waves sent, its defeat and its peak army pop (read once a second). Pins whirlwind.md's table under
/// <see cref="Section"/> cell for cell (re-print it from the output when a number moves) and prints how many runs fell
/// before the wave was due: the finding for the balance pass (D33), not a failure.
/// </summary>
public class HolyWarFirstWaveReportTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ticks = 24_000;

    /// <summary>The whirlwind.md section whose table is this report's output.</summary>
    internal const string Section = "The holy war's first wave (D-R39, 2026-11-15)";

    internal const string Header = "| Malazan opening | seed | Malazan first wave | `holy_war` wave due | `holy_war` first wave (waves) | `holy_war` defeated at | `holy_war` peak army pop |";

    /// <summary>The three smallest seeds in 1-500 on which the Normal seats pick Malazan <paramref name="malazan"/> and Whirlwind <paramref name="whirlwind"/>.</summary>
    internal static ulong[] PairSeeds(int malazan, int whirlwind)
    {
        var seeds = new List<ulong>();
        for (ulong s = 1; s <= 500 && seeds.Count < 3; s++)
            if (AiOpeningSeeds.Pick(s, 0, AiDifficulty.Normal) == malazan && AiOpeningSeeds.Pick(s, 1, AiDifficulty.Normal) == whirlwind) seeds.Add(s);
        Assert.Equal(3, seeds.Count);
        return seeds.ToArray();
    }

    [Fact]
    public void HolyWar_AgainstEachMalazanOpening_FirstWaveDue_Report()
    {
        int holyWar = Array.FindIndex(TestSim.Data.Ai[TestSim.Data.FindFaction("whirlwind")].Openings.ToArray(), o => o.Id == "holy_war");
        Assert.True(holyWar >= 0, "whirlwind ships no holy_war opening");
        var malazanOpenings = TestSim.Data.Ai[TestSim.Data.FindFaction("malazan")].Openings;
        var printed = new List<string>();
        int runs = 0, fellBeforeDue = 0;
        output.WriteLine($"holy_war against each Malazan opening, Normal v Normal, menu map, {Ticks} ticks");
        output.WriteLine(Header);
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
        for (int m = 0; m < malazanOpenings.Length; m++)
        {
            foreach (ulong seed in PairSeeds(m, holyWar))
            {
                Simulation sim = AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seed);
                Assert.Equal((m, holyWar), (sim.AiOpening(0), sim.AiOpening(1)));
                AiPlayer mal = sim.AiOf(0)!, hw = sim.AiOf(1)!;
                int malWave = -1, due = -1, wave = -1, fell = -1, peak = 0;
                for (int t = 0; t < Ticks; t++)
                {
                    sim.Tick();
                    if (malWave < 0 && mal.Board.WavesStarted > 0) malWave = sim.TickNumber;
                    if (wave < 0 && hw.Board.WavesStarted > 0) wave = sim.TickNumber;
                    if (fell < 0 && sim.World.IsDefeated(1)) fell = sim.TickNumber;
                    if (fell < 0 && due < 0 && sim.TickNumber % hw.ThinkTicks == 0 && AiOpeningPairsReportTests.WaveDue(sim, hw)) due = sim.TickNumber;
                    if (sim.TickNumber % Tps == 0) peak = Math.Max(peak, ArmyHalfPop(sim, 1));
                }
                runs++;
                if (fell >= 0 && (due < 0 || due >= fell)) fellBeforeDue++;
                printed.Add($"| `{malazanOpenings[m].Id}` | {seed} | {Sec(malWave)} | {Sec(due)} | {Sec(wave)} ({hw.Board.WavesStarted}) | {Sec(fell)} | {peak / 2.0} |");
                output.WriteLine(printed[^1]);
            }
        }
        output.WriteLine($"  holy_war defeated before its first wave was due in {fellBeforeDue} of {runs} runs");
        (string[] header, List<string[]> rows) = PageTables.NamedTable(PageTables.FactionLines("whirlwind"), Section, "whirlwind.md");
        Assert.Equal(PageTables.Cells(Header), header);
        Assert.Equal(printed.Select(r => string.Join(" | ", PageTables.Cells(r, stripBackticks: true))), rows.Select(r => string.Join(" | ", r)));
    }

    private static int ArmyHalfPop(Simulation sim, int player)
    {
        var u = sim.World.Units;
        int army = 0;
        for (int s = 0; s < u.Capacity; s++)
            if (u.Alive[s] && u.Owner[s] == player && sim.World.Data.Units[u.TypeId[s]].Slot != UnitSlot.Worker)
                army += sim.World.Data.Units[u.TypeId[s]].HalfPop;
        return army;
    }

    private static string Sec(int tick) => tick < 0 ? "never" : $"{tick / Tps / 60}:{tick / Tps % 60:00}";
}
