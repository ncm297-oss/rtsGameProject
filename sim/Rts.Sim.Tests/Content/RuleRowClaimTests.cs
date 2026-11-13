using System.Text.RegularExpressions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D30: <see cref="RuleRowClaims"/> on the shipped data and pages: every M7-4 number a page states is in the data, or noted
/// as landing later and not in the data yet, and every such number in the data is stated (teblor.md's Frenzy +20% damage,
/// its aftermath, the Anointing's +4 s and the buildings' 1.25× carry "the rows land with D31" until D31 ships them, and
/// lose it then). The tests hold on either side of the landing. The landing copy and the wrong copies are in
/// <see cref="TeblorLandingPinTests"/>.
/// </summary>
public class RuleRowClaimTests
{
    [Theory]
    [MemberData(nameof(ContentFactions.Theory), MemberType = typeof(ContentFactions))]
    public void ThePagesRuleRowClaims_MatchTheData_BothWays(string faction)
    {
        List<string> problems = RuleRowClaims.Problems(TestSim.Data, faction, PageTables.FactionLines(faction));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Whether the shipped data carries the Teblor's M7-4 rows (they land together, D31).</summary>
    private static bool Landed => TestSim.Data.FindStatus(TeblorLanding.Status) >= 0;

    /// <summary>The page's text from <paramref name="claim"/>'s first mention to the end of its sentence, bold removed.</summary>
    private static string SentenceFrom(string text, string claim)
    {
        int at = text.IndexOf(claim, StringComparison.Ordinal);
        Assert.True(at >= 0, $"teblor.md does not say '{claim}'");
        int end = text.IndexOf(". ", at, StringComparison.Ordinal);
        return text[at..(end < 0 ? text.Length : end)];
    }

    /// <summary>Not vacuous: teblor.md states each of the four, noted as landing later exactly while the data lacks the rows.</summary>
    [Theory]
    [InlineData("+20% damage")]
    [InlineData("Slowed 20% for 4 s")]
    [InlineData("duration +4 s")]
    [InlineData("1.25× scale")]
    public void TheTeblorPage_StatesEachClaim_NotedExactlyWhileTheRowsAreOut(string claim)
    {
        string sentence = SentenceFrom(string.Join(" ", PageTables.FactionLines("teblor")).Replace("**", ""), claim);
        Assert.True(RuleRowClaims.Pending.IsMatch(sentence) != Landed,
            $"teblor.md '{sentence}': the rows are {(Landed ? "in" : "not in")} the data, so the note should be {(Landed ? "gone" : "there")}");
    }

    /// <summary>
    /// The note read the other way: toggled on today's page (data unchanged), each claim fails by name. While the rows are out,
    /// the notes are broken ("landed in D31") and the bare claim fails as not in the data; once in, a note is added after the
    /// claim and fails as noted while the data has it.
    /// </summary>
    [Theory]
    [InlineData("duration +4 s", "faction upgrade duration")]
    [InlineData("+20% damage", "Abilities damage bonus")]
    [InlineData("Slowed 20% for 4 s", "Abilities aftermath (Slowed)")]
    [InlineData("1.25× scale", "Buildings scale")]
    public void TheNoteToggled_FailsNamingTheClaim(string claim, string what)
    {
        string text = string.Join("\n", PageTables.FactionLines("teblor"));
        string edited;
        string want;
        if (!Landed)
        {
            edited = Regex.Replace(text, @"\b(rows?) lands? with (D\d+)\b", "$1 landed in $2");
            want = $"{what}: page '{claim}' vs data";
        }
        else
        {
            int at = text.IndexOf(claim, StringComparison.Ordinal);
            Assert.True(at >= 0, $"teblor.md does not say '{claim}'");
            edited = text.Insert(at + claim.Length, " (the row lands with D99)");
            want = $"{what}: '{claim}' is noted as landing later";
        }

        List<string> problems = RuleRowClaims.Problems(TestSim.Data, "teblor", edited.Split('\n'));

        Assert.True(problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem '{want}':\n" + string.Join("\n", problems));
    }
}
