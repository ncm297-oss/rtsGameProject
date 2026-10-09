using System.Text.RegularExpressions;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D5): docs/02 "Ages" spells the Age II any-of rule out in a trailing clause, "two production halls, or one hall
/// and the Forge". TechContentTests.G pins the parenthesis before it but not the clause, so "three production halls,
/// or the Forge alone" passed every test. This pins the clause to <c>age_ii.requiresAnyOf</c>: n halls, or n - 1
/// halls and the Forge, where n is the any-of count and the listed slots are the production halls plus the Forge.
/// D6 (BUG-0200): <c>TechContentTests.G</c> now carries the same check; this row stays as QA's independent oracle.
/// D7 QA (BUG-0230 item 1, BUG-0260): the clause must end its bullet, read from the file's own lines (a bullet starts at
/// a line beginning "- "), so a contradicting sentence appended to it fails even when written " - Or the Forge alone."
/// on the same line (which a space-joined section text cannot tell from the next bullet).
/// </summary>
public class AgesRuleQaTests
{
    private static GameData Data => TestSim.Data;

    private static readonly string[] CountWords = { "zero", "one", "two", "three", "four" };

    private static readonly Regex Clause = new(
        @"Forge\): (?<n>\w+) production halls, or (?<m>\w+) halls? and the Forge\.$");

    [Fact]
    public void AgesTrailingClause_MatchesTheAnyOfRule()
    {
        string page = ClauseBullet();
        TechDef t = Data.Techs.Single(x => x.Key == "age_ii");

        Match m = Clause.Match(page);
        Assert.True(m.Success, $"02 Ages: bullet '{page}' does not end with '...Forge): <n> production halls, or <n-1> hall(s) and the Forge.'");

        var halls = new[] { BuildingSlot.InfantryHall, BuildingSlot.RangedHall, BuildingSlot.ShockHall };
        var listed = t.RequiresAnyOfSlots.Select(s => (BuildingSlot)s).ToArray();
        Assert.True(listed.Contains(BuildingSlot.Forge) && listed.Length == halls.Length + 1 && halls.All(listed.Contains),
            $"age_ii requiresAnyOf slots: clause assumes the three halls + Forge, data {string.Join(", ", listed)}");

        int n = t.RequiresAnyOfCount;
        Assert.True(m.Groups["n"].Value == CountWords[n], $"age_ii Ages clause halls: page {m.Groups["n"].Value} vs data {CountWords[n]}");
        Assert.True(m.Groups["m"].Value == CountWords[n - 1], $"age_ii Ages clause halls with the Forge: page {m.Groups["m"].Value} vs data {CountWords[n - 1]}");
    }

    /// <summary>The "### Ages" bullet that states the any-of rule, its continuation lines joined by spaces.</summary>
    private static string ClauseBullet()
    {
        string[] lines = File.ReadAllLines(Path.Combine(TestDataDir.RepoRoot(), "docs", "02-game-design.md"));
        int i = Array.IndexOf(lines, "### Ages");
        Assert.True(i >= 0, "docs/02 has no '### Ages' heading");
        var bullets = new List<string>();
        for (i++; i < lines.Length && !lines[i].StartsWith('#'); i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("- ", StringComparison.Ordinal) || bullets.Count == 0) bullets.Add(line);
            else bullets[^1] += " " + line;
        }
        string[] withRule = bullets.Where(b => b.Contains("production halls", StringComparison.Ordinal)).ToArray();
        Assert.True(withRule.Length == 1, $"docs/02 Ages: {withRule.Length} bullets mention 'production halls', expected 1");
        return withRule[0].Replace("**", "");
    }
}
