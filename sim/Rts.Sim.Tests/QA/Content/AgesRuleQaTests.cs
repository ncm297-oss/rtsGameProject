using System.Text.RegularExpressions;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D5): docs/02 "Ages" spells the Age II any-of rule out in a trailing clause, "two production halls, or one hall
/// and the Forge". TechContentTests.G pins the parenthesis before it but not the clause, so "three production halls,
/// or the Forge alone" passed every test. This pins the clause to <c>age_ii.requiresAnyOf</c>: n halls, or n - 1
/// halls and the Forge, where n is the any-of count and the listed slots are the production halls plus the Forge.
/// </summary>
public class AgesRuleQaTests
{
    private static GameData Data => TestSim.Data;

    private static readonly string[] CountWords = { "zero", "one", "two", "three", "four" };

    private static readonly Regex Clause = new(
        @"Forge\): (?<n>\w+) production halls, or (?<m>\w+) halls? and the Forge\.");

    [Fact]
    public void AgesTrailingClause_MatchesTheAnyOfRule()
    {
        string page = FactionPage.DocText(Path.Combine("docs", "02-game-design.md"), "Ages", 3);
        TechDef t = Data.Techs.Single(x => x.Key == "age_ii");

        Match m = Clause.Match(page);
        Assert.True(m.Success, $"02 Ages: page '{page}' has no '...Forge): <n> production halls, or <n-1> hall(s) and the Forge.'");

        var halls = new[] { BuildingSlot.InfantryHall, BuildingSlot.RangedHall, BuildingSlot.ShockHall };
        var listed = t.RequiresAnyOfSlots.Select(s => (BuildingSlot)s).ToArray();
        Assert.True(listed.Contains(BuildingSlot.Forge) && listed.Length == halls.Length + 1 && halls.All(listed.Contains),
            $"age_ii requiresAnyOf slots: clause assumes the three halls + Forge, data {string.Join(", ", listed)}");

        int n = t.RequiresAnyOfCount;
        Assert.True(m.Groups["n"].Value == CountWords[n], $"age_ii Ages clause halls: page {m.Groups["n"].Value} vs data {CountWords[n]}");
        Assert.True(m.Groups["m"].Value == CountWords[n - 1], $"age_ii Ages clause halls with the Forge: page {m.Groups["m"].Value} vs data {CountWords[n - 1]}");
    }
}
