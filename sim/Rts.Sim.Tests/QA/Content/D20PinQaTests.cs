using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// D20 (2026-10-17, M5-4f): pins the faction pages' re-worded "Defense" lines to <c>common/rules.json</c> and their
/// "Houses at the cap" notes to what <see cref="AiHouseRuleReportTests.MenuMapNormal_HouseSources_Report"/> measures, so a
/// knob, a cost or the executor's D15 C behaviour that moves fails here by name until the page is re-worded. D21 re-pinned
/// the house notes per opening (the reason is on <see cref="HouseNote_SaysWhatTheMenuMapRunsShow"/>).
/// </summary>
public class D20PinQaTests
{
    /// <summary>The knobs the Defense line must quote (M5-4f's three); any other <c>`ai.x` N</c> it quotes is pinned too.</summary>
    private static readonly string[] SizedDefenseKnobs = { "defendRatio", "defendMinValue", "recallHallHp" };

    private static readonly Regex QuotedKnob = new(@"`ai\.(\w+)` \(?(\d+(?:\.\d+)?)", RegexOptions.CultureInvariant);

    private static readonly Regex OneRaiderCase = new(
        @"One ([A-Z][\w ]+?) \((\d+) gold \+ wood\) is answered with (\d+) worth, e\.g\. two ([A-Z][\w ]+?)s? from home \((\d+)\), and a wave out keeps going\.",
        RegexOptions.CultureInvariant);

    private static string DefenseLine(string faction)
    {
        string[] bullets = FactionPage.Bullets(faction, AiContentTests.Section, 2);
        string? line = bullets.FirstOrDefault(b => b.StartsWith("Defense ", StringComparison.Ordinal));
        Assert.True(line != null, $"{faction}.md '{AiContentTests.Section}' has no 'Defense' bullet");
        return line!;
    }

    private static JsonObject RulesAi() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "common", "rules.json")))!["ai"]!.AsObject();

    /// <summary>Every <c>`ai.x` N</c> the Defense line quotes equals <c>rules.json</c>'s <c>ai.x</c>, and the three sized-defense knobs are quoted.</summary>
    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void DefenseLine_QuotesTheRulesJsonKnobs(string faction)
    {
        string line = DefenseLine(faction);
        JsonObject ai = RulesAi();
        var quoted = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in QuotedKnob.Matches(line))
        {
            string knob = m.Groups[1].Value;
            Assert.True(ai.ContainsKey(knob), $"{faction}.md Defense quotes `ai.{knob}`, which rules.json's ai block lacks");
            double page = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            double file = (double)ai[knob]!;
            Assert.True(page == file, $"{faction}.md Defense quotes `ai.{knob}` {m.Groups[2].Value}; rules.json has {file.ToString(CultureInfo.InvariantCulture)}");
            quoted.Add(knob);
        }
        foreach (string knob in SizedDefenseKnobs)
            Assert.True(quoted.Contains(knob), $"{faction}.md Defense does not quote `ai.{knob}` with its value");
        Assert.Contains("2026-10-17", line);
        Assert.Contains("M5-4f", line);
    }

    /// <summary>
    /// The one-unit case: the raider's cost is its data cost, the answer is <c>max(defendRatio x cost, defendMinValue)</c>, and
    /// the pick rule (picked while the answer is short by more than half the next candidate's cost, the first always) takes
    /// exactly two of the named defender, worth what the page says.
    /// </summary>
    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void DefenseLine_OneUnitCase_FollowsTheSizingRule(string faction)
    {
        string line = DefenseLine(faction);
        Match m = OneRaiderCase.Match(line);
        Assert.True(m.Success, $"{faction}.md Defense has no 'One X (N gold + wood) is answered with N worth, e.g. two Y from home (N), and a wave out keeps going.' sentence");
        GameData d = TestSim.Data;
        int own = d.FindFaction(faction);
        UnitDef raider = d.Units.Single(u => u.Faction != own && u.DisplayName == m.Groups[1].Value);
        UnitDef defender = d.Units.Single(u => u.Faction == own && u.DisplayName == m.Groups[4].Value);
        int cost = raider.CostGold + raider.CostWood, each = defender.CostGold + defender.CostWood;
        Assert.Equal(cost.ToString(CultureInfo.InvariantCulture), m.Groups[2].Value);
        AiRulesDef rules = d.AiRules;
        double answer = Math.Max(rules.DefendRatio * cost, rules.DefendMinValue);
        Assert.Equal(answer.ToString(CultureInfo.InvariantCulture), m.Groups[3].Value);
        int picks = 0, picked = 0;
        while (picks == 0 || answer - picked > each / 2.0) { picks++; picked += each; }
        Assert.True(picks == 2, $"{faction}: the pick rule takes {picks} {defender.DisplayName} for an answer of {answer}, the page says two");
        Assert.Equal(picked.ToString(CultureInfo.InvariantCulture), m.Groups[5].Value);
    }

    /// <summary>
    /// Per opening since D21 (re-pinned 2026-10-18: the openings moved every number, and the old two-seed note became one
    /// note per opening): each opening's "Houses at the cap" note says what its menu-map run shows
    /// (<see cref="AiHouseRuleReportTests.NoteSentence"/>: when the cap reaches 100, the cap the last house step was done at,
    /// the steps the D15 C skip fired on, the houses standing and past the cap) and its date; the standard opening's note
    /// also gives the 12th-house comparison (<see cref="AiHouseRuleReportTests.TwelfthSentence"/>). On every run no house
    /// Build was sent once the cap was the hard cap (D15 C, asserted on the run itself).
    /// </summary>
    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void HouseNote_SaysWhatTheMenuMapRunsShow(string faction)
    {
        AiDef ai = TestSim.Data.Ai[TestSim.Data.FindFaction(faction)];
        string[] lines = PageTables.FactionLines(faction);
        for (int k = 0; k < ai.Openings.Length; k++)
        {
            AiHouseRuleReportTests.HouseRun r = AiHouseRuleReportTests.MenuMapRun(faction, k);
            Assert.True(r.Steps.All(s => s.SendsAtCap == 0), $"{faction} {ai.Openings[k].Id} seed {r.Seed}: a house Build was sent at the hard cap (D15 C says none)");
            string note = Note(AiContentTests.OpeningLines(lines, faction, k, ai.Openings[k]), $"{faction}.md opening {k + 1}");
            var wants = new List<string> { "(D21, 2026-10-18", AiHouseRuleReportTests.NoteSentence(r) };
            if (k == 0)
            {
                AiHouseRuleReportTests.HouseRun twelfth = AiHouseRuleReportTests.TwelfthHouseRun(faction);
                Assert.True(twelfth.Steps.All(s => s.SendsAtCap == 0), $"{faction} with the 12th house: a house Build was sent at the hard cap");
                wants.Add(AiHouseRuleReportTests.TwelfthSentence(twelfth));
            }
            foreach (string want in wants)
                Assert.True(note.Contains(want, StringComparison.Ordinal), $"{faction}.md opening {k + 1} 'Houses at the cap' lacks \"{want}\"; it reads: {note}");
        }
    }

    private static string Note(string[] lines, string where)
    {
        int i = Array.FindIndex(lines, l => l.StartsWith("**Houses at the cap", StringComparison.Ordinal));
        Assert.True(i >= 0, $"{where} has no '**Houses at the cap' paragraph");
        var text = new List<string>();
        for (; i < lines.Length && lines[i].Trim().Length > 0; i++) text.Add(lines[i].Trim());
        return string.Join(" ", text).Replace("**", "");
    }
}
