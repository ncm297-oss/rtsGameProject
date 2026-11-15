using System.Reflection;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-S10 fix round 1: controls on the re-staged criterion-2 run of <see cref="CounterCompositionTests"/> (the test-built
/// gold trickle, <c>Purse</c>). The row passes by one unit (6 v 5 Line); these rows check the unit comes from the counter
/// weights and not from some other effect of the sighting (the reveal, the raiders standing for one think). The private
/// <c>Run</c> is called by reflection so the control runs exactly the staged scene the product row runs, not a copy of it.
/// </summary>
public class CounterRestageQaTests
{
    private readonly ITestOutputHelper _out;

    public CounterRestageQaTests(ITestOutputHelper output) => _out = output;

    private (int Line, int Ranged, int[] Count, int[] Tick) Run(AiDifficulty difficulty, bool seen, GameData? data)
    {
        var host = new CounterCompositionTests(_out);
        MethodInfo run = typeof(CounterCompositionTests).GetMethod("Run", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((int, int, int[], int[]))run.Invoke(host, new object?[] { difficulty, seen, data })!;
    }

    /// <summary>With Normal's counterStrength at 0, the seen run remembers the 8 Shock but trains exactly what the unseen run trains.</summary>
    [Fact]
    public void WithStrengthZero_TheSightingChangesNothing_OnTheRestagedRun()
    {
        GameData zero = AiScenes.DataWithAi("malazan", root => root["difficulties"]!["normal"]!["counterStrength"] = 0);
        var seen = Run(AiDifficulty.Normal, true, zero);
        var unseen = Run(AiDifficulty.Normal, false, zero);
        _out.WriteLine($"strength 0: seen {seen.Line} / {seen.Ranged}, unseen {unseen.Line} / {unseen.Ranged}");
        Assert.Equal(8, seen.Count[(int)UnitSlot.Shock]);
        Assert.Equal(unseen.Line, seen.Line);
        Assert.Equal(unseen.Ranged, seen.Ranged);
    }

    /// <summary>The shipped strength (0.5) on the same run: the seen seat trains more Line (the product row's claim, re-run here beside its control).</summary>
    [Fact]
    public void WithTheShippedStrength_TheSightingAddsLine_OnTheRestagedRun()
    {
        var seen = Run(AiDifficulty.Normal, true, null);
        var unseen = Run(AiDifficulty.Normal, false, null);
        _out.WriteLine($"strength 0.5: seen {seen.Line} / {seen.Ranged}, unseen {unseen.Line} / {unseen.Ranged}");
        Assert.True(seen.Line > unseen.Line, $"line {seen.Line} seen vs {unseen.Line} unseen");
    }
}
