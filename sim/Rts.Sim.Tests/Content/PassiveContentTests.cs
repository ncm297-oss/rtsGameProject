using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// Content (D11): every unit passive in <c>units.json</c> (<see cref="UnitDef.Passives"/>) matches the "Passive **Name**:"
/// sentence of its unit's bullet in the faction page's "Unit notes", field by field, both ways: a page passive with no data
/// passive fails unless it is a named allowance (<see cref="PendingPassives"/>), and a data passive with no page sentence
/// fails. The data has no passive names, so a page sentence is matched to a data passive by its trigger wording:
/// "below &lt;n&gt;% HP" is <c>whenBelowHp</c>, "when a &lt;Unit&gt; dies" is <c>onDeath</c>.
/// </summary>
public class PassiveContentTests
{
    private static readonly string[] Factions = { "malazan", "whirlwind" };

    /// <summary>
    /// Page passives with no data passive yet (Malazan's Heavy Infantry and Wickan Lancer, whose triggers the sim doesn't
    /// have). Once one lands it is compared like any other, but its differences are reported, not failed, until it is
    /// removed here.
    /// </summary>
    public static readonly string[] PendingPassives = { "Shield Wall", "Charge" };

    private readonly ITestOutputHelper _out;

    public PassiveContentTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => TestSim.Data;

    /// <summary>One "Passive **Name**: text" sentence of a unit's "Unit notes" bullet.</summary>
    public sealed record PagePassive(string Unit, string Name, string Text);

    private const RegexOptions Opts = RegexOptions.CultureInvariant;

    private const string Num = @"(?<n>\d+(?:\.\d+)?)";

    /// <summary>"**Zealot (unique, Age II):**": the unit's display name, any parenthesis dropped.</summary>
    private static readonly Regex BulletUnit = new(@"^\*\*(?<unit>[^*:(]+?)\s*(?:\([^)]*\))?:\*\*", Opts);

    /// <summary>"Passive **Name**: text", the text running to the next "Passive **" or the bullet's end.</summary>
    private static readonly Regex PassiveSentence = new(@"Passive \*\*(?<name>[^*]+)\*\*:\s*(?<text>.*?)(?=\s+Passive \*\*|$)", Opts);

    private static readonly Regex BelowHp = new(@"^below " + Num + @" ?% HP\b", Opts);
    private static readonly Regex OnDeath = new(@"^when an? (?<unit>.+?) dies\b", Opts);
    private static readonly Regex Gains = new(@"\bgains? (?<status>[A-Z][A-Za-z]*)", Opts);
    private static readonly Regex Within = new(@"\bwithin " + Num + @" ?m\b", Opts);
    private static readonly Regex For = new(@"\bfor " + Num + @" ?s\b", Opts);
    private static readonly Regex Takers = new(@"\bdies, (?<list>.+?) within\b", Opts);
    private static readonly Regex AttackSpeed = new(@"\+" + Num + @" ?% attack speed\b", Opts);

    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void EveryDataPassive_MatchesItsPageSentence_AndEveryPageSentenceIsLoadedOrPending(string faction)
    {
        AbilityContentTests.Result r = Compare(Data, faction, PageTables.FactionLines(faction));
        foreach (string line in r.Reports) _out.WriteLine(line);
        Assert.True(r.Problems.Count == 0, $"docs/factions/{faction}.md \"Unit notes\" passives differ from units.json:\n" + string.Join("\n", r.Problems));
    }

    [Fact]
    public void TheZealotPassives_AreLoaded_AndStrictlyPinned()
    {
        // Guards the pin itself: with no loaded passive, or with these pending, every check above would pass vacuously.
        UnitDef zealot = Data.Units[Data.FindUnit("whirlwind_zealot")];
        Assert.Equal(2, zealot.Passives.Length);
        List<PagePassive> page = PagePassives(PageTables.FactionLines("whirlwind"), "whirlwind.md");
        Assert.Contains(page, p => p.Unit == "Zealot" && p.Name == "Frenzy of the Apocalypse");
        Assert.Contains(page, p => p.Unit == "Zealot" && p.Name == "Martyrdom");
        Assert.DoesNotContain("Frenzy of the Apocalypse", PendingPassives);
        Assert.DoesNotContain("Martyrdom", PendingPassives);
    }

    [Fact]
    public void PendingPassives_AreSentencesOnSomePage()
    {
        var names = Factions.SelectMany(f => PagePassives(PageTables.FactionLines(f), f + ".md")).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (string name in PendingPassives) Assert.True(names.Contains(name), $"pending passive '{name}' is on no faction page; drop it from the allowance");
    }

    /// <summary>D11 criterion 1, the page side: one whirlwind.md Zealot passive number or word changed fails naming the passive and the field.</summary>
    [Theory]
    [InlineData("Frenzy of the Apocalypse", "hpFraction", "below 50% HP", "below 40% HP")]
    [InlineData("Frenzy of the Apocalypse", "trigger", "below 50% HP", "below half HP")]
    [InlineData("Frenzy of the Apocalypse", "magnitude", "gains Frenzied (+50% attack speed)", "gains Frenzied (+30% attack speed)")]
    [InlineData("Frenzy of the Apocalypse", "magnitude", "gains Frenzied (+50% attack speed)", "gains Frenzied")]
    [InlineData("Frenzy of the Apocalypse", "status", "gains Frenzied", "gains Burning")]
    [InlineData("Frenzy of the Apocalypse", "duration", "(+50% attack speed). Passive", "(+50% attack speed) for 5 s. Passive")]
    [InlineData("Martyrdom", "unit", "when a Zealot dies", "when a Raider dies")]
    [InlineData("Martyrdom", "units", "Zealots within", "Raiders within")]
    [InlineData("Martyrdom", "units", "Zealots within", "Zealots and Raiders within")]
    [InlineData("Martyrdom", "radius", "within 6 m", "within 8 m")]
    [InlineData("Martyrdom", "radius", "within 6 m", "nearby")]
    [InlineData("Martyrdom", "status", "gain Frenzied", "gain Slowed")]
    [InlineData("Martyrdom", "magnitude", "gain Frenzied (+50% attack speed)", "gain Frenzied (+25% attack speed)")]
    [InlineData("Martyrdom", "magnitude", "gain Frenzied (+50% attack speed)", "gain Frenzied")]
    [InlineData("Martyrdom", "duration", "for 5 s", "for 4 s")]
    [InlineData("Martyrdom", "duration", "for 5 s", "briefly")]
    public void AMutatedPageNumber_FailsNamingThePassiveAndField(string passive, string field, string text, string mutant)
    {
        string[] lines = Mutated("whirlwind", text, mutant);

        AbilityContentTests.Result r = Compare(Data, "whirlwind", lines);

        string want = $"'{passive}' {field}:";
        Assert.True(r.Problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem naming {want}\n" + string.Join("\n", r.Problems));
    }

    /// <summary>D11 criterion 1, the data side: one Zealot passive field changed in memory fails naming the passive and the field.</summary>
    [Theory]
    [InlineData("Frenzy of the Apocalypse", "hpFraction", "below-0.4")]
    [InlineData("Frenzy of the Apocalypse", "magnitude", "below-magnitude-0.3")]
    [InlineData("Frenzy of the Apocalypse", "status", "below-slowed")]
    [InlineData("Martyrdom", "magnitude", "death-magnitude-0.25")]
    [InlineData("Martyrdom", "duration", "death-duration-4")]
    [InlineData("Martyrdom", "radius", "death-radius-8")]
    [InlineData("Martyrdom", "units", "death-units-plus-raider")]
    [InlineData("Martyrdom", "units", "death-units-raider")]
    [InlineData("Martyrdom", "status", "death-slowed")]
    [InlineData("Frenzy of the Apocalypse", "trigger", "drop-below")]
    [InlineData("Martyrdom", "trigger", "drop-death")]
    public void AMutatedDataNumber_FailsNamingThePassiveAndField(string passive, string field, string mutant)
    {
        int slowed = Data.Statuses.Single(s => s.Key == "slowed").Id;
        int raider = Data.FindUnit("whirlwind_raider");
        int ticks = SimConstants.TicksPerSecond;
        UnitDef zealot = Data.Units[Data.FindUnit("whirlwind_zealot")];
        ImmutableArray<UnitPassive> passives = zealot.Passives
            .Where(p => !(mutant == "drop-below" && p.Trigger == PassiveTrigger.WhenBelowHp) && !(mutant == "drop-death" && p.Trigger == PassiveTrigger.OnDeath))
            .Select(p => (p.Trigger, mutant) switch
            {
                (PassiveTrigger.WhenBelowHp, "below-0.4") => p with { HpFraction = 0.4f },
                (PassiveTrigger.WhenBelowHp, "below-magnitude-0.3") => p with { Magnitude = 0.3f },
                (PassiveTrigger.WhenBelowHp, "below-slowed") => p with { Status = slowed },
                (PassiveTrigger.OnDeath, "death-magnitude-0.25") => p with { Magnitude = 0.25f },
                (PassiveTrigger.OnDeath, "death-duration-4") => p with { DurationTicks = 4 * ticks },
                (PassiveTrigger.OnDeath, "death-radius-8") => p with { Radius = 8 },
                (PassiveTrigger.OnDeath, "death-units-plus-raider") => p with { Units = p.Units.Add(raider) },
                (PassiveTrigger.OnDeath, "death-units-raider") => p with { Units = ImmutableArray.Create(raider) },
                (PassiveTrigger.OnDeath, "death-slowed") => p with { Status = slowed },
                _ => p,
            }).ToImmutableArray();

        AbilityContentTests.Result r = Compare(Data, "whirlwind", PageTables.FactionLines("whirlwind"),
            passivesOf: u => u.Id == zealot.Id ? passives : u.Passives);

        string want = $"'{passive}' {field}:";
        Assert.True(r.Problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem naming {want}\n" + string.Join("\n", r.Problems));
    }

    /// <summary>A data passive with no page sentence fails naming the unit and the trigger.</summary>
    [Fact]
    public void ADataPassiveMissingFromThePage_FailsNamingUnitAndTrigger()
    {
        string[] lines = Mutated("whirlwind", " Passive **Martyrdom**: when a Zealot dies,", "");

        AbilityContentTests.Result r = Compare(Data, "whirlwind", lines);

        Assert.Contains(r.Problems, p => p.Contains("'Zealot' passive onDeath:", StringComparison.Ordinal));
    }

    /// <summary>The allowance works both ways: an unknown page passive fails, a pending one is only reported.</summary>
    [Fact]
    public void AnUnknownPagePassive_Fails_ButAPendingOneDoesNot()
    {
        string[] lines = Mutated("whirlwind", "for 5 s.", "for 5 s. Passive **Holy Rage**: +2 armor while near a Priest.");
        AbilityContentTests.Result failing = Compare(Data, "whirlwind", lines);
        AbilityContentTests.Result pending = Compare(Data, "whirlwind", lines, pendingPassives: new[] { "Holy Rage" });

        Assert.Contains(failing.Problems, p => p.Contains("'Holy Rage'", StringComparison.Ordinal));
        Assert.True(pending.Problems.Count == 0, string.Join("\n", pending.Problems));
        Assert.Contains(pending.Reports, l => l.Contains("'Holy Rage'", StringComparison.Ordinal) && l.Contains("pending", StringComparison.Ordinal));
    }

    /// <summary>Equivalent wordings still pass: the sentence's numbers are read by their words, not their position.</summary>
    [Theory]
    [InlineData("below 50% HP gains Frenzied (+50% attack speed)", "below 50 % HP gains Frenzied, +50 % attack speed")]
    [InlineData("Zealots within 6 m gain Frenzied (+50% attack speed) for 5 s", "Zealots within 6 m gain Frenzied for 5 s (+50% attack speed)")]
    public void AnEquivalentWording_Passes(string text, string mutant)
    {
        AbilityContentTests.Result r = Compare(Data, "whirlwind", Mutated("whirlwind", text, mutant));

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
    }

    /// <summary>The faction page's lines with <paramref name="text"/> (which must be on exactly one line) replaced.</summary>
    private static string[] Mutated(string faction, string text, string mutant)
    {
        string[] lines = PageTables.FactionLines(faction);
        int[] hits = Enumerable.Range(0, lines.Length).Where(i => lines[i].Contains(text, StringComparison.Ordinal)).ToArray();
        Assert.True(hits.Length == 1, $"{faction}.md has '{text}' on {hits.Length} lines, expected 1");
        lines[hits[0]] = lines[hits[0]].Replace(text, mutant, StringComparison.Ordinal);
        return lines;
    }

    /// <summary>
    /// The "Passive **Name**: text" sentences of each bullet under "### Unit notes" in <paramref name="lines"/>, with the
    /// bullet's unit (continuation lines joined; the text keeps its words, the bold markers inside it dropped).
    /// </summary>
    public static List<PagePassive> PagePassives(string[] lines, string where)
    {
        int i = PageTables.Heading(lines, "### Unit notes", where);
        var bullets = new List<string>();
        for (i++; i < lines.Length && !lines[i].StartsWith('#'); i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal)) bullets.Add(line[2..]);
            else if (line.Length > 0 && bullets.Count > 0 && lines[i].StartsWith(' ')) bullets[^1] += " " + line;
        }
        var result = new List<PagePassive>();
        foreach (string bullet in bullets)
        {
            Match unit = BulletUnit.Match(bullet);
            if (!unit.Success) continue;
            foreach (Match m in PassiveSentence.Matches(bullet))
                result.Add(new PagePassive(unit.Groups["unit"].Value.Trim(), m.Groups["name"].Value.Trim(), m.Groups["text"].Value.Replace("**", "").Trim()));
        }
        return result;
    }

    /// <summary>The trigger a page sentence's wording names, or null when it is neither shape.</summary>
    private static PassiveTrigger? TriggerOf(string text) =>
        BelowHp.IsMatch(text) ? PassiveTrigger.WhenBelowHp : OnDeath.IsMatch(text) ? PassiveTrigger.OnDeath : null;

    private static string TriggerId(PassiveTrigger t) => DataLimits.PassiveTriggerIds[(int)t];

    /// <summary>
    /// Compares <paramref name="faction"/>'s "Unit notes" passive sentences in <paramref name="lines"/> to its units'
    /// passives, both ways. <paramref name="passivesOf"/> stands in for <see cref="UnitDef.Passives"/> (in-memory mutants),
    /// <paramref name="pendingPassives"/> for <see cref="PendingPassives"/>. Problems read
    /// <c>&lt;faction&gt;.md "Unit notes" '&lt;Unit&gt;' passive '&lt;Name&gt;' &lt;field&gt;: ...</c>, the field a
    /// <c>passives[]</c> JSON field (or <c>trigger</c> / <c>unit</c>, the dying unit of an <c>onDeath</c>).
    /// </summary>
    public static AbilityContentTests.Result Compare(GameData data, string faction, string[] lines,
        Func<UnitDef, ImmutableArray<UnitPassive>>? passivesOf = null, IReadOnlyCollection<string>? pendingPassives = null)
    {
        Func<UnitDef, ImmutableArray<UnitPassive>> of = passivesOf ?? (u => u.Passives);
        IReadOnlyCollection<string> pending = pendingPassives ?? PendingPassives;
        var r = new AbilityContentTests.Result(new List<string>(), new List<string>());
        string where = $"{faction}.md \"Unit notes\"";
        List<PagePassive> page = PagePassives(lines, faction + ".md");
        int factionId = data.FindFaction(faction);
        UnitDef[] units = data.Units.Where(u => u.Faction == factionId).ToArray();

        // Data -> page: every loaded passive has its sentence, and the sentence states each of its fields.
        foreach (UnitDef u in units)
            foreach (UnitPassive p in of(u))
            {
                PagePassive? pp = page.FirstOrDefault(x => x.Unit == u.DisplayName && TriggerOf(x.Text) == p.Trigger);
                if (pp == null)
                {
                    r.Problems.Add($"{where} '{u.DisplayName}' passive {TriggerId(p.Trigger)}: no \"Passive **Name**:\" sentence states the data passive");
                    continue;
                }
                bool isPending = pending.Contains(pp.Name);
                List<string> sink = isPending ? r.Reports : r.Problems;
                if (isPending) r.Reports.Add($"{where} '{pp.Name}' has landed in units.json; drop it from PendingPassives to pin it");
                foreach ((string field, string problem) in FieldProblems(data, u, p, pp.Text))
                    sink.Add($"{where} '{u.DisplayName}' passive '{pp.Name}' {field}: {problem}");
            }

        // Page -> data: every sentence is a loaded passive of its unit, or pending.
        foreach (PagePassive pp in page)
        {
            UnitDef? u = units.FirstOrDefault(x => x.DisplayName == pp.Unit);
            PassiveTrigger? t = TriggerOf(pp.Text);
            bool loaded = u != null && t is PassiveTrigger trigger && of(u).Any(p => p.Trigger == trigger);
            if (loaded) continue;
            string what = u == null ? $"no {faction} unit is named '{pp.Unit}'"
                : t == null ? "its wording names no trigger the sim has (\"below <n>% HP\", \"when a <Unit> dies\")"
                : $"'{pp.Unit}' has no {TriggerId(t.Value)} passive in units.json";
            if (pending.Contains(pp.Name)) r.Reports.Add($"{where} '{pp.Unit}' passive '{pp.Name}' trigger: {what} (pending: reported, not failed)");
            else r.Problems.Add($"{where} '{pp.Unit}' passive '{pp.Name}' trigger: {what}");
        }
        return r;
    }

    /// <summary>Each field of <paramref name="p"/> against the words of its page sentence <paramref name="text"/>.</summary>
    private static IEnumerable<(string Field, string Problem)> FieldProblems(GameData data, UnitDef unit, UnitPassive p, string text)
    {
        StatusDef status = data.Statuses[p.Status];

        Match gains = Gains.Match(text);
        if (!gains.Success) yield return ("status", $"page '{text}' names no status it gains, data '{status.DisplayName}'");
        else if (gains.Groups["status"].Value != status.DisplayName) yield return ("status", $"page '{gains.Groups["status"].Value}', data '{status.DisplayName}'");

        foreach ((string field, string problem) in MagnitudeProblems(status, p.Magnitude, text)) yield return (field, problem);

        Match below = BelowHp.Match(text);
        if (p.Trigger == PassiveTrigger.WhenBelowHp && (!below.Success || !Same(Number(below) / 100.0, p.HpFraction)))
            yield return ("hpFraction", below.Success ? $"page '{below.Value}', data {Pct(p.HpFraction)}%" : $"page '{text}' states no 'below <n>% HP', data {Pct(p.HpFraction)}%");

        // Duration and radius: an onDeath passive states both; a whenBelowHp one holds while below the line and states neither.
        bool death = p.Trigger == PassiveTrigger.OnDeath;
        Match dur = For.Match(text);
        double seconds = p.DurationTicks / (double)SimConstants.TicksPerSecond;
        if (death ? !dur.Success || !Same(Number(dur), seconds) : dur.Success)
            yield return ("duration", dur.Success ? $"page '{dur.Value}', data {(death ? Text(seconds) + " s" : "none (held while below the line)")}" : $"page '{text}' states no 'for <n> s', data {Text(seconds)} s");
        Match within = Within.Match(text);
        if (death ? !within.Success || !Same(Number(within), p.Radius) : within.Success)
            yield return ("radius", within.Success ? $"page '{within.Value}', data {(death ? Text(p.Radius) + " m" : "none")}" : $"page '{text}' states no 'within <n> m', data {Text(p.Radius)} m");

        if (!death) yield break;

        Match dies = OnDeath.Match(text);
        if (dies.Groups["unit"].Value != unit.DisplayName) yield return ("unit", $"page 'when a {dies.Groups["unit"].Value} dies', data the passive is {unit.DisplayName}'s");

        string[] want = p.Units.Select(id => data.Units[id].DisplayName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Match takers = Takers.Match(text);
        string[] got = takers.Success
            ? Regex.Split(takers.Groups["list"].Value, @",\s*|\s+and\s+", Opts).Select(Singular).OrderBy(x => x, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        if (!got.SequenceEqual(want))
            yield return ("units", $"page '{(takers.Success ? takers.Groups["list"].Value : text)}', data {string.Join(", ", want)}");
    }

    /// <summary>The magnitude claim a status kind's sentence makes; only Frenzied ships on a passive, other kinds fail until pinned.</summary>
    private static IEnumerable<(string Field, string Problem)> MagnitudeProblems(StatusDef status, float magnitude, string text)
    {
        if (status.Kind != StatusKind.Frenzied)
        {
            yield return ("magnitude", $"no page wording is pinned for a {DataLimits.StatusKindIds[(int)status.Kind]} passive's magnitude (data {Text(magnitude)}); extend PassiveContentTests");
            yield break;
        }
        Match m = AttackSpeed.Match(text);
        if (!m.Success) yield return ("magnitude", $"page '{text}' states no '+<n>% attack speed', data +{Pct(magnitude)}%");
        else if (!Same(Number(m) / 100.0, magnitude)) yield return ("magnitude", $"page '{m.Value}', data +{Pct(magnitude)}% attack speed");
    }

    /// <summary>"Zealots" -> "Zealot": the page lists the taking types in the plural.</summary>
    private static string Singular(string name)
    {
        string n = name.Trim();
        return n.EndsWith('s') ? n[..^1] : n;
    }

    private static double Number(Match m) => double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);

    private static bool Same(double a, double b) => Math.Abs(a - b) < 1e-4;

    private static string Pct(float fraction) => Text(Math.Round(fraction * 100.0, 4));

    private static string Text(double v) => FactionPage.Text(Math.Round(v, 4));
}
