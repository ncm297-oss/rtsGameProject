using System.Diagnostics;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-4 criterion 14 (Request 56 G4), a report: the cost of the train check with a limit beside 6,000 live units. The count is
/// kept (one array read), so the check does not grow with the unit store; the rows print 1,000 checks of the capped Heavy
/// Infantry and 1,000 of the uncapped Laborer (at its Keep), the median of 21 rounds each, and bound only a broken check
/// (a store walk per call would be tens of microseconds a call: the bound is 1 ms for 1,000).
/// </summary>
[Collection(SerialCollection.Name)]
public class UnitLimitPerfTests
{
    private readonly ITestOutputHelper _out;

    public UnitLimitPerfTests(ITestOutputHelper output) => _out = output;

    [Fact]
    [Trait("Category", "Perf")]
    public void AThousandChecks_Beside6000LiveUnits_Report()
    {
        GameData d = UnitLimitTests.s_capped.Value;
        (Simulation sim, int[,] bar) = UnitLimitTests.Base(d, players: 2, units: 6_200);
        World w = sim.World;
        int heavy = UnitLimitTests.HeavyOf(d), laborer = d.FindUnit("malazan_laborer");
        int keep = -1;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == d.FindBuilding("malazan_garrison_keep")) keep = k;
        UnitLimitTests.PlaceLive(sim, 0, heavy, 7);
        for (int k = 0; k < 5_993; k++) CombatScenes.Place(sim, k % 2, laborer, CombatScenes.At(sim, k % 48, 20 + (k / 48) % 28));
        Assert.Equal(6_000, w.Units.Count);
        double capped = Median(() => Checks(w, bar[0, 0], heavy)), plain = Median(() => Checks(w, keep, laborer));
        Assert.False(w.CanTrain(0, bar[0, 0], heavy, out TrainError why));
        Assert.Equal(TrainError.LimitReached, why);
        Assert.True(w.CanTrain(0, keep, laborer, out _));
        _out.WriteLine($"6,000 live units: 1,000 checks of the capped type {capped:F1} us ({capped:F1} ns a check), of an uncapped type {plain:F1} us ({plain:F1} ns a check); medians of 21");
        Assert.True(capped < 1_000, $"1,000 capped checks took {capped:F1} us");
    }

    private static void Checks(World w, int slot, int type)
    {
        for (int k = 0; k < 1_000; k++) w.CanTrain(0, slot, type, out _);
    }

    /// <summary>The median of 21 timed runs of <paramref name="block"/>, in microseconds (one warm-up first).</summary>
    private static double Median(Action block)
    {
        block();
        var times = new double[21];
        for (int r = 0; r < times.Length; r++)
        {
            long start = Stopwatch.GetTimestamp();
            block();
            times[r] = (Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency;
        }
        Array.Sort(times);
        return times[times.Length / 2];
    }
}
