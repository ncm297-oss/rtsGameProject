using Rts.Sim.Data;
using Xunit.Abstractions;
using static Rts.Sim.Tests.Scenario.CounterTriangleScene;

namespace Rts.Sim.Tests.Scenario;

/// <summary>
/// M4 criterion "Scenario tests for the counter triangle" (M4-2b; docs/02 "Faction template", the counter table): two
/// equal-cost groups on a flat map, attack-moved into each other, for both factions' pairs, each seat order; and siege
/// against a building next to the same cost of line infantry. Every row prints the survivors and the time. A row whose
/// doc winner loses fails naming the pair and the margin: that is a data issue for the data track's balance pass, not a
/// sim tune. The scene is <see cref="CounterTriangleScene"/> (public, shared with the data track's balance report).
/// </summary>
public class CounterTriangleTests
{
    private readonly ITestOutputHelper _out;

    public CounterTriangleTests(ITestOutputHelper output) => _out = output;

    private static string Describe(Side s) => $"{s.Key} {s.Left}/{s.Fielded} left ({s.Hp} hp, cost {s.CostLeft} of {s.FieldedCost})";

    [Theory]
    [InlineData("Line beats Shock", "malazan_heavy_infantry", "whirlwind_horse_raider")]
    [InlineData("Line beats Shock", "whirlwind_raider", "malazan_wickan_lancer")]
    [InlineData("Shock beats Ranged", "malazan_wickan_lancer", "whirlwind_desert_archer")]
    [InlineData("Shock beats Ranged", "whirlwind_horse_raider", "malazan_crossbowman")]
    [InlineData("Ranged beats Light (casters)", "malazan_crossbowman", "whirlwind_priest")]
    [InlineData("Ranged beats Light (casters)", "whirlwind_desert_archer", "malazan_cadre_mage")]
    public void EqualCostGroups_TheCounterWins(string rule, string winner, string loser)
    {
        var failures = new List<string>();
        for (int seat = 0; seat < 2; seat++)
        {
            (Side w, Side l, int ticks) = Fight(winner, loser, seat);
            string line = $"{rule}, {winner} as player {seat}: {Describe(w)} v {Describe(l)} after {ticks} ticks ({ticks / 20f:F1} s)";
            _out.WriteLine(line);
            if (!(w.Left > 0 && l.Left == 0))
                failures.Add($"{line}; margin {w.CostLeft - l.CostLeft} cost, {w.Hp - l.Hp} hp in {winner}'s favour");
        }
        Assert.True(failures.Count == 0, $"{winner} v {loser} ({rule}) does not give the doc's winner:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData("malazan_catapult", "malazan_heavy_infantry", "whirlwind_tent")]
    [InlineData("whirlwind_battering_ram", "whirlwind_raider", "malazan_billet")]
    public void SiegeBeatsBuildings_OneSiegeUnitKillsABuildingFasterThanTheSameCostOfLineInfantry(string siege, string line, string building)
    {
        GameData data = TestSim.Data;
        int n = SameCostCount(data, siege, line);
        const int max = SiegeMaxTicks;
        int siegeTicks = TimeToKill(siege, 1, building, max);
        int lineTicks = TimeToKill(line, n, building, max);
        string Show(int t) => t == int.MaxValue ? $"not in {max} ticks" : $"{t} ticks ({t / 20f:F1} s)";
        _out.WriteLine($"{building}: 1 {siege} {Show(siegeTicks)}; {n} {line} {Show(lineTicks)}");
        Assert.True(siegeTicks < lineTicks, $"Siege beats buildings: 1 {siege} {Show(siegeTicks)} is not faster than {n} {line} {Show(lineTicks)} on a {building}");
    }
}
