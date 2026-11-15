using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherOverlapScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S9's measurement report (docs/03 "Implementation (M6-S9)"), no bound: on copies of the shipped data with
/// <c>gatherOverlap</c> 0 / 0.25 / 0.5 / 0.75, (a) 5, 10, 16 and 24 workers on one gold mine beside a Keep and on a tree line
/// for 5 minutes, for the Malazan Laborer (0.4 m) and the Teblor Gatherer (0.75 m, carry 15); (b) Normal v Normal on the
/// menu's map and the three named maps, seed 1, at 0 and 0.5. These figures choose the value that ships.
/// </summary>
[Trait("Category", "Stress")]
public class GatherOverlapReport
{
    private readonly ITestOutputHelper _out;

    public GatherOverlapReport(ITestOutputHelper output) => _out = output;

    private static readonly double[] Values = { 0, 0.25, 0.5, 0.75 };

    [Fact]
    public void TableA_WorkersOnOneMineAndOnATreeLine_FiveMinutes()
    {
        _out.WriteLine("| Node | Worker | N | Overlap | Banked | Trips | Stuck ticks | Give-ups | Closest pair (share of radii sum) | Mean stand gap |");
        _out.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (Node node in new[] { Node.Mine, Node.TreeLine })
            foreach (Workers kind in new[] { Workers.Laborer, Workers.Gatherer })
                foreach (int n in new[] { 5, 10, 16, 24 })
                    foreach (double v in Values)
                    {
                        Figures f = Row(v, node, kind, n, 6000);
                        _out.WriteLine($"| {node} | {kind} | {n} | {v} | {f.Cells} |");
                    }
    }

    /// <summary>
    /// <see cref="GatherOverlapFuzzTests"/>' scene on its seeds (2-16 without 13) at every value, the trip window only measured:
    /// per run the longest a worker on its loop went without a trip (its node alive), and the income. At M6-S9 it showed the four
    /// wedges the fuzz's rows measured instead of asserting (BUG-1430, BUG-1431); M6-S10 cured them (none left).
    /// </summary>
    [Fact]
    public void TableC_EconomyFuzz_LongestGapWithoutATrip()
    {
        _out.WriteLine("| Seed | " + string.Join(" | ", Values.Select(v => $"{v}: longest gap (s), income p0 / p1")) + " |");
        _out.WriteLine("| --- |" + string.Concat(Values.Select(_ => " --- |")));
        foreach (ulong seed in GatherOverlapFuzzTests.Seeds)
        {
            var cells = new List<string>();
            foreach (double v in Values)
            {
                GatherOverlapFuzzTests.Result r = GatherOverlapFuzzTests.Run(v, seed, tripWindow: false);
                cells.Add($"{r.LongestGap / 20.0:F1}, {r.Income0} / {r.Income1}");
            }
            _out.WriteLine($"| {seed} | {string.Join(" | ", cells)} |");
        }
    }

    /// <summary>A Normal v Normal match (Malazan v Whirlwind, seats 0 and 1) on <paramref name="map"/> seed 1 on <paramref name="data"/>.</summary>
    private static Simulation Match(string map, GameData data)
    {
        var seats = new[] { new PlayerSeat(0, SeatController.Ai, AiDifficulty.Normal, 0), new PlayerSeat(1, SeatController.Ai, AiDifficulty.Normal, 0) };
        if (map == "menu") return AiSpendingReportTests.MenuMap(seats, 1, data);
        int size = data.Maps[data.FindMap(map)].Size;
        return AiScenes.Generated(seats, 1, false, out _, data: data,
            configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
    }

    [Fact]
    public void TableB_NormalVersusNormal_Seed1_AtZeroAndHalf()
    {
        const int bound = 72_000, tenMinutes = 12_000;
        _out.WriteLine("| Map | Overlap | Result tick | Winner | Gold + wood gathered by 10:00 (p0 / p1) | Ends |");
        _out.WriteLine("| --- | --- | --- | --- | --- | --- |");
        foreach (string map in new[] { "menu", "vathar_crossing", "raraku", "pale_hills" })
            foreach (double v in new[] { 0, 0.5 })
            {
                Simulation sim = Match(map, DataAt(v));
                World w = sim.World;
                string gathered = "";
                CriterionMatch.Result r = CriterionMatch.Play(sim, bound, checks: false, onTick: s =>
                {
                    if (s.TickNumber == tenMinutes) gathered = $"{w.GatheredGold[0] + w.GatheredWood[0]} / {w.GatheredGold[1] + w.GatheredWood[1]}";
                });
                _out.WriteLine($"| {map} | {v} | {r.EndTick} ({CriterionMatch.Clock(r.EndTick)}) | {r.Winner} | {gathered} | {(r.Over ? "yes" : $"no (bound {bound})")} |");
            }
    }
}
