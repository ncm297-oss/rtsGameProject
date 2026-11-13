using System.Globalization;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D30: the numbers a faction page states for M7-4's row fields, in its prose as well as its tables, held to the data both
/// ways, and the page's "the rows land with D31" notes held to what has not landed:
/// <list type="bullet">
/// <item><b>Damage bonus</b>, "+20% damage" in "## Abilities": a <c>damageBonus</c> x 100 of a status a faction ability
/// applies.</item>
/// <item><b>Aftermath</b>, "Slowed 20% for 4 s" (any slow status's name) in "## Abilities": the <c>onEnd</c> of a status a
/// faction ability applies, when that aftermath is a slow.</item>
/// <item><b>Duration bonus</b>, "duration +4 s" in "## Faction upgrade: ..." and "## Techs": a faction tech's
/// <c>statusDuration</c> amount.</item>
/// <item><b>Building scale</b>, "1.25× scale" in the prose (not the table) of "## Buildings": a faction building's
/// <c>scale</c> other than 1.</item>
/// </list>
/// A claim in a sentence that says the row "lands with D&lt;n&gt;" (<see cref="Pending"/>) is a promise: the data must not
/// have it yet. Any other claim must be in the data, and every such data value must be claimed outside a pending sentence.
/// So a page says "the rows land with D31" exactly while the rows are not shipped, and drops it the session they are.
/// </summary>
internal static class RuleRowClaims
{
    private const RegexOptions Opts = RegexOptions.CultureInvariant;

    /// <summary>"the row lands with D31", "the rows land with D31": the note that a claim's row is not in the data yet.</summary>
    public static readonly Regex Pending = new(@"\brows? lands? with D\d+\b", Opts);

    private static readonly Regex DamageClaim = new(@"\+(\d+(?:\.\d+)?) ?% damage\b", Opts);
    private static readonly Regex DurationClaim = new(@"\bduration \+(\d+(?:\.\d+)?) s\b", Opts);
    private static readonly Regex ScaleClaim = new(@"(\d+(?:\.\d+)?) ?× scale\b", Opts);

    /// <summary>
    /// The sentences of a <c>## </c> section of <paramref name="lines"/> to the next one, bold removed: each table row is one,
    /// and each run of prose lines is joined by spaces and split after a full stop followed by a capital, a backtick, a
    /// bracket or a quote. With <paramref name="prose"/> the table rows are dropped.
    /// </summary>
    private static List<string> Section(string[] lines, string headingPrefix, string page, bool prose = false)
    {
        int i = PageTables.HeadingStartingWith(lines, headingPrefix);
        Assert.True(i >= 0, $"{page} has no '{headingPrefix}' section (RuleRowClaims)");
        var sentences = new List<string>();
        var run = new List<string>();
        void Flush()
        {
            if (run.Count > 0) sentences.AddRange(Regex.Split(string.Join(" ", run).Replace("**", ""), @"(?<=\.)\s+(?=[A-Z`(""])", Opts));
            run.Clear();
        }
        for (i++; i < lines.Length && !lines[i].StartsWith("## ", StringComparison.Ordinal); i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) { Flush(); continue; }
            if (line.StartsWith('|'))
            {
                Flush();
                if (!prose) sentences.Add(line.Replace("**", ""));
                continue;
            }
            run.Add(line);
        }
        Flush();
        return sentences;
    }

    private static string Num(double v) => FactionPage.Text(Math.Round(v, 3));

    /// <summary>One kind of claim, <paramref name="claim"/> read from <paramref name="sentences"/>, against <paramref name="data"/>, both ways.</summary>
    private static void Check(List<string> problems, string page, string what, IEnumerable<string> sentences, Regex claim, Func<Match, string> value,
        IReadOnlyCollection<string> data)
    {
        var said = new HashSet<string>(StringComparer.Ordinal);
        foreach (string sentence in sentences)
        {
            bool pending = Pending.IsMatch(sentence);
            foreach (Match m in claim.Matches(sentence))
            {
                string v = value(m);
                if (pending)
                {
                    if (data.Contains(v))
                        problems.Add($"{page} {what}: '{m.Value}' is noted as landing later ('{Pending.Match(sentence).Value}'), but the data has it; drop the note");
                    continue;
                }
                said.Add(v);
                if (!data.Contains(v))
                    problems.Add($"{page} {what}: page '{m.Value}' vs data [{string.Join(", ", data)}] (or note it: 'the row lands with D<n>')");
            }
        }
        foreach (string v in data)
            if (!said.Contains(v)) problems.Add($"{page} {what}: data {v}, which the page does not state outside a 'lands with' note");
    }

    /// <summary>Every way <paramref name="faction"/>'s page (<paramref name="lines"/>) and <paramref name="data"/> disagree on the claims above; empty when they agree.</summary>
    public static List<string> Problems(GameData data, string faction, string[] lines)
    {
        var problems = new List<string>();
        string page = faction + ".md";
        int f = data.FindFaction(faction);

        // The statuses the faction's abilities apply, directly or through a zone.
        var applied = new SortedSet<int>();
        foreach (AbilityDef a in data.Abilities.Where(x => x.Faction == f))
            foreach (AbilityEffect e in a.Effects)
            {
                if (e.Kind == AbilityEffectKind.ApplyStatus) applied.Add(e.Status);
                if (e.Kind == AbilityEffectKind.CreateZone && !e.ZoneStatuses.IsDefault)
                    foreach (ZoneStatus z in e.ZoneStatuses) applied.Add(z.Status);
            }
        StatusDef[] statuses = applied.Select(i => data.Statuses[i]).ToArray();

        List<string> abilities = Section(lines, "## Abilities", page);
        var bonuses = statuses.Where(s => s.DamageBonus > 0).Select(s => Num(s.DamageBonus * 100.0)).Distinct().ToArray();
        Check(problems, page, "Abilities damage bonus", abilities, DamageClaim, m => Num(Parse(m.Groups[1].Value)), bonuses);

        // The aftermath: "<slow name> 20% for 4 s", one claim kind per slow status the data has.
        foreach (StatusDef slow in data.Statuses.Where(s => s.Kind == StatusKind.Slow))
        {
            var ends = statuses.Where(s => s.OnEndStatus == slow.Id)
                .Select(s => $"{Num(s.OnEndMagnitude * 100.0)}% for {Num(s.OnEndTicks / (double)SimConstants.TicksPerSecond)} s").Distinct().ToArray();
            var claim = new Regex($@"\b{Regex.Escape(slow.DisplayName)} (\d+(?:\.\d+)?) ?% for (\d+(?:\.\d+)?) s\b", Opts);
            Check(problems, page, $"Abilities aftermath ({slow.DisplayName})", abilities, claim,
                m => $"{Num(Parse(m.Groups[1].Value))}% for {Num(Parse(m.Groups[2].Value))} s", ends);
        }

        List<string> techs = Section(lines, "## Faction upgrade: ", page).Concat(Section(lines, "## Techs", page)).ToList();
        var durations = data.Techs.Where(t => t.Faction == f).SelectMany(t => t.Effects).Where(e => e.Stat == TechStat.StatusDuration)
            .Select(e => Num(e.Amount / SimConstants.TicksPerSecond)).Distinct().ToArray();
        Check(problems, page, "faction upgrade duration", techs, DurationClaim, m => Num(Parse(m.Groups[1].Value)), durations);

        List<string> buildings = Section(lines, "## Buildings", page, prose: true);
        var scales = data.Buildings.Where(b => b.Faction == f && b.Scale != 1f).Select(b => Num(b.Scale)).Distinct().ToArray();
        Check(problems, page, "Buildings scale", buildings, ScaleClaim, m => Num(Parse(m.Groups[1].Value)), scales);
        return problems;
    }

    private static double Parse(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}
