using System.Globalization;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// Content (D10a): every loaded ability's row in its faction page's "Abilities" table matches <c>abilities.json</c> cell by
/// cell, every page row is a loaded ability or a named allowance (abilities the sim hasn't shipped yet), and the loaded
/// statuses' names match docs/02 "Status effects" both ways. Columns are found by name, so a reordered table still pins.
/// </summary>
public class AbilityContentTests
{
    private static readonly string[] Factions = { "malazan", "whirlwind" };

    /// <summary>The "Abilities" header both pages share (docs/02 "Ability system" fields, Duration "—" when absent).</summary>
    public const string Header = "| Ability | Unit | Kind | Range | Radius | Cast | Cooldown | Duration | Effect |";

    /// <summary>
    /// Page rows that may have no <c>abilities.json</c> entry yet: Cusser (sim slice 2) and Sandstorm (M4-4b-2, zones).
    /// Once one lands it is compared like any other, but its differences are reported, not failed, until it is removed here.
    /// </summary>
    public static readonly string[] PendingAbilities = { "Cusser", "Sandstorm" };

    /// <summary>docs/02 "Status effects" rows with no <c>statuses.json</c> entry yet (stealth, frenzy, passives, zones).</summary>
    public static readonly string[] PendingStatuses = { "Stealthed", "Revealed", "Frenzied", "Regenerating", "Blinded" };

    private const string StatusDoc = "docs/02-game-design.md";

    private readonly ITestOutputHelper _out;

    public AbilityContentTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => TestSim.Data;

    /// <summary>The outcome of comparing a page to the data: <c>Problems</c> fail the pin, <c>Reports</c> are printed only.</summary>
    public sealed record Result(List<string> Problems, List<string> Reports);

    [Fact]
    public void BothPages_ShareTheAbilitiesHeader()
    {
        foreach (string faction in Factions)
        {
            (string[] header, _) = PageTables.NamedTable(PageTables.FactionLines(faction), "Abilities", faction + ".md");
            Assert.True(Header == "| " + string.Join(" | ", header) + " |",
                $"docs/factions/{faction}.md \"Abilities\" header is '| {string.Join(" | ", header)} |', expected '{Header}'");
        }
    }

    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void EveryLoadedAbility_MatchesItsPageRow_AndEveryRowIsLoadedOrPending(string faction)
    {
        Result r = Compare(Data, Data.Abilities, faction, PageTables.FactionLines(faction));
        foreach (string line in r.Reports) _out.WriteLine(line);
        Assert.True(r.Problems.Count == 0, $"docs/factions/{faction}.md \"Abilities\" differs from abilities.json:\n" + string.Join("\n", r.Problems));
    }

    [Fact]
    public void TelasFire_IsLoaded_AndStrictlyPinned()
    {
        // Guards the pin itself: if Telas Fire stopped loading or became pending, every check above would pass vacuously.
        Assert.Contains(Data.Abilities, a => a.DisplayName == "Telas Fire");
        Assert.DoesNotContain("Telas Fire", PendingAbilities);
    }

    [Fact]
    public void PendingAbilities_AreRowsOnSomePage()
    {
        var rows = Factions.SelectMany(f => PageTables.NamedTable(PageTables.FactionLines(f), "Abilities", f + ".md").Rows)
            .Select(c => c[0]).ToHashSet(StringComparer.Ordinal);
        foreach (string name in PendingAbilities) Assert.True(rows.Contains(name), $"pending ability '{name}' has no faction-page row; drop it from the allowance");
    }

    /// <summary>
    /// The pin can fail (D10a criterion 1): one Telas Fire cell changed in memory gives a problem naming the ability and
    /// the column.
    /// </summary>
    [Theory]
    [InlineData("Ability", "| Telas Fire |", "| Telas Flame |")]
    [InlineData("Unit", "| Cadre Mage |", "| Cadre Adept |")]
    [InlineData("Kind", "| Target ground | 16 |", "| Target unit | 16 |")]
    [InlineData("Range", "| 16 |", "| 18 |")]
    [InlineData("Radius", "| 3 m |", "| 4 m |")]
    [InlineData("Radius", "| 3 m |", "| 3 |")]
    [InlineData("Cast", "| 0.8 s |", "| 1.0 s |")]
    [InlineData("Cooldown", "| 25 s |", "| 30 s |")]
    [InlineData("Duration", "| 25 s | — |", "| 25 s | 4 s |")]
    [InlineData("Effect", "Burning: 10 magic", "Burning: 12 magic")]
    [InlineData("Effect", "damage/s for 4 s", "damage/s for 5 s")]
    [InlineData("Effect", "Burning: 10 magic", "Burning: 10 fire")]
    [InlineData("Effect", "are Burning:", "are Slowed:")]
    [InlineData("Effect", "| Enemy units in the area", "| Own units in the area")]
    [InlineData("Effect", ". No effect on buildings", ". Full damage to buildings")]
    public void AMutatedTelasFireCell_FailsNamingAbilityAndColumn(string column, string cell, string mutant)
    {
        string[] lines = PageTables.FactionLines("malazan");
        int row = Array.FindIndex(lines, l => l.StartsWith("| Telas Fire |", StringComparison.Ordinal));
        Assert.True(row >= 0 && lines[row].Contains(cell, StringComparison.Ordinal), $"malazan.md's Telas Fire row has no '{cell}'");
        lines[row] = lines[row].Replace(cell, mutant, StringComparison.Ordinal);

        Result r = Compare(Data, Data.Abilities, "malazan", lines);

        Assert.Contains(r.Problems, p => p.Contains("Telas Fire", StringComparison.Ordinal) && p.Contains(column, StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownPageRow_Fails_ButAPendingOneDoesNot()
    {
        string[] lines = PageTables.FactionLines("malazan");
        int row = Array.FindIndex(lines, l => l.StartsWith("| Cusser |", StringComparison.Ordinal));
        Assert.True(row >= 0, "malazan.md has no Cusser row");
        lines[row] = lines[row].Replace("| Cusser |", "| Grenado |", StringComparison.Ordinal);

        Result r = Compare(Data, Data.Abilities, "malazan", lines);

        Assert.Contains(r.Problems, p => p.Contains("Grenado", StringComparison.Ordinal));
        Assert.Single(r.Problems);
    }

    /// <summary>
    /// D10a criterion 2: a Cusser entry landing (here a stand-in built in memory, cells deliberately off from the page)
    /// passes with report lines naming it, and the strict Telas Fire pin still holds alongside it.
    /// </summary>
    [Fact]
    public void ALandedPendingAbility_PassesWithAReportLine()
    {
        int malazan = Data.FindFaction("malazan");
        var cusser = new AbilityDef
        {
            Id = Data.Abilities.Length,
            Key = "cusser",
            Faction = malazan,
            DisplayName = "Cusser",
            Description = "",
            Kind = AbilityKind.TargetGround,
            Range = 6,
            Radius = 3.5f,
            CastTicks = SimConstants.TicksPerSecond,
            CooldownTicks = 45 * SimConstants.TicksPerSecond,
            Affects = AbilityAffects.AllUnits,
            Effects = System.Collections.Immutable.ImmutableArray.Create(new AbilityEffect
            {
                Kind = AbilityEffectKind.Damage,
                DamageType = Data.DamageTable.DamageTypeKeys.IndexOf("siege"),
                Amount = 99,
                Status = -1,
            }),
        };
        var abilities = Data.Abilities.Add(cusser);

        Result r = Compare(Data, abilities, "malazan", PageTables.FactionLines("malazan"));
        foreach (string line in r.Reports) _out.WriteLine(line);

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
        Assert.Contains(r.Reports, l => l.Contains("Cusser", StringComparison.Ordinal) && l.Contains("landed", StringComparison.Ordinal));
        Assert.Contains(r.Reports, l => l.Contains("Cusser", StringComparison.Ordinal) && l.Contains("Effect", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryLoadedStatus_HasItsDocs02Row()
    {
        string[][] rows = FactionPage.DocTable(StatusDoc, "Status effects", level: 3);
        var problems = new List<string>();
        foreach (StatusDef s in Data.Statuses)
        {
            string[]? row = rows.FirstOrDefault(c => c[0] == s.DisplayName);
            if (row == null) { problems.Add($"status '{s.Key}' ({s.DisplayName}): no docs/02 \"Status effects\" row"); continue; }
            string effect = s.Kind switch
            {
                StatusKind.DamageOverTime => $"Damage over time ({Data.DamageTable.DamageTypeKeys[s.DamageType]})",
                StatusKind.Slow => "Movement speed × (1 - magnitude)",
                _ => "",
            };
            if (row[1] != effect) problems.Add($"status '{s.Key}' Effect: docs/02 '{row[1]}', data {s.Kind} -> '{effect}'");
            Assert.DoesNotContain(s.DisplayName, PendingStatuses);
        }
        // Each loaded ability that applies a status is listed as one of its typical sources.
        foreach (AbilityDef a in Data.Abilities)
            foreach (AbilityEffect e in a.Effects)
            {
                if (e.Kind != AbilityEffectKind.ApplyStatus) continue;
                string name = Data.Statuses[e.Status].DisplayName;
                string[]? row = rows.FirstOrDefault(c => c[0] == name);
                if (row != null && !row[2].Split(',').Select(x => x.Trim()).Contains(a.DisplayName))
                    problems.Add($"status '{name}' Typical source '{row[2]}' does not list '{a.DisplayName}', which applies it");
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void EveryDocs02StatusRow_IsLoadedOrPending()
    {
        string[][] rows = FactionPage.DocTable(StatusDoc, "Status effects", level: 3);
        var loaded = Data.Statuses.Select(s => s.DisplayName).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Burning", loaded);
        Assert.Contains("Slowed", loaded);
        var problems = new List<string>();
        foreach (string[] row in rows)
        {
            if (loaded.Contains(row[0]))
            {
                if (PendingStatuses.Contains(row[0])) _out.WriteLine($"status '{row[0]}' has landed in statuses.json; drop it from PendingStatuses");
                continue;
            }
            if (!PendingStatuses.Contains(row[0])) problems.Add($"docs/02 \"Status effects\" row '{row[0]}' is neither in statuses.json nor pending");
        }
        foreach (string name in PendingStatuses)
            if (!rows.Any(c => c[0] == name)) problems.Add($"pending status '{name}' has no docs/02 row; drop it from the allowance");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// Compares <paramref name="faction"/>'s "Abilities" table in <paramref name="lines"/> to its loaded
    /// <paramref name="abilities"/>, both ways. A pending ability's differences go to <c>Reports</c>, every other one to
    /// <c>Problems</c>.
    /// </summary>
    public static Result Compare(GameData data, IEnumerable<AbilityDef> abilities, string faction, string[] lines)
    {
        var r = new Result(new List<string>(), new List<string>());
        string where = $"{faction}.md \"Abilities\"";
        (string[] header, List<string[]> rows) = PageTables.NamedTable(lines, "Abilities", faction + ".md");
        int Col(string name) => Array.IndexOf(header, name);
        foreach (string name in PageTables.Cells(Header))
            if (Col(name) < 0) r.Problems.Add($"{where}: no '{name}' column, rows not checked");
        if (r.Problems.Count > 0) return r;

        int factionId = data.FindFaction(faction);
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        foreach (AbilityDef a in abilities)
        {
            if (a.Faction != factionId) continue;
            loaded.Add(a.DisplayName);
            bool pending = PendingAbilities.Contains(a.DisplayName);
            List<string> sink = pending ? r.Reports : r.Problems;
            if (pending) r.Reports.Add($"{where} '{a.DisplayName}' has landed in abilities.json; drop it from PendingAbilities to pin it");
            string[]? row = rows.FirstOrDefault(c => c[Col("Ability")] == a.DisplayName);
            if (row == null)
            {
                sink.Add($"{where} '{a.DisplayName}' Ability: no row for loaded ability '{a.Key}'");
                continue;
            }
            foreach ((string column, string problem) in CellProblems(data, a, name => row[Col(name)]))
                sink.Add($"{where} '{a.DisplayName}' {column}: {problem}");
        }
        foreach (string[] row in rows)
        {
            string name = row[Col("Ability")];
            if (loaded.Contains(name)) continue;
            if (PendingAbilities.Contains(name)) r.Reports.Add($"{where} '{name}' is pending: no abilities.json entry yet");
            else r.Problems.Add($"{where} '{name}' Ability: row has no abilities.json entry and is not pending");
        }
        return r;
    }

    private static IEnumerable<(string Column, string Problem)> CellProblems(GameData data, AbilityDef a, Func<string, string> cell)
    {
        string[] units = data.Units.Where(u => u.Abilities.Contains(a.Id)).Select(u => u.DisplayName).ToArray();
        string unitText = string.Join(", ", units);
        if (cell("Unit") != unitText) yield return ("Unit", $"page '{cell("Unit")}', data '{unitText}' (units listing '{a.Key}')");

        // "Target ground (zone)": the parenthetical names the effect, the kind is the words before it.
        string kind = cell("Kind");
        int paren = kind.IndexOf(" (", StringComparison.Ordinal);
        if (paren >= 0) kind = kind[..paren];
        string kindText = a.Kind switch { AbilityKind.TargetGround => "Target ground", _ => a.Kind.ToString() };
        if (kind != kindText) yield return ("Kind", $"page '{cell("Kind")}', data '{kindText}'");

        foreach ((string column, string suffix, double value) in new[]
        {
            ("Range", "", a.Range),
            ("Radius", " m", a.Radius),
            ("Cast", " s", Seconds(a.CastTicks)),
            ("Cooldown", " s", Seconds(a.CooldownTicks)),
        })
        {
            string? problem = NumberProblem(cell(column), suffix, value);
            if (problem != null) yield return (column, problem);
        }

        string duration = cell("Duration");
        if (a.DurationTicks == 0)
        {
            if (duration != "—") yield return ("Duration", $"page '{duration}', data has no duration (write '—')");
        }
        else
        {
            string? problem = NumberProblem(duration, " s", Seconds(a.DurationTicks));
            if (problem != null) yield return ("Duration", problem);
        }

        string effect = cell("Effect");
        string who = a.Affects switch
        {
            AbilityAffects.EnemyUnits => "Enemy units",
            AbilityAffects.OwnUnits => "Own units",
            _ => "friendly fire",
        };
        bool whoOk = a.Affects == AbilityAffects.AllUnits ? effect.Contains(who, StringComparison.Ordinal) : effect.StartsWith(who, StringComparison.Ordinal);
        if (!whoOk) yield return ("Effect", $"'{effect}' does not say '{who}' (affects {a.Affects})");
        // Abilities never touch buildings yet (AbilityAffects); the page has to say so.
        if (!effect.Contains("No effect on buildings", StringComparison.Ordinal))
            yield return ("Effect", $"'{effect}' does not say 'No effect on buildings'");
        foreach (AbilityEffect e in a.Effects)
        {
            string want = e.Kind switch
            {
                AbilityEffectKind.Damage => $"{e.Amount} {data.DamageTable.DamageTypeKeys[e.DamageType]} damage",
                AbilityEffectKind.ApplyStatus => StatusText(data, e),
                _ => e.Kind.ToString(),
            };
            if (!effect.Contains(want, StringComparison.Ordinal)) yield return ("Effect", $"'{effect}' does not say '{want}' ({e.Kind})");
        }
    }

    private static string StatusText(GameData data, AbilityEffect e)
    {
        StatusDef s = data.Statuses[e.Status];
        return s.Kind switch
        {
            StatusKind.DamageOverTime =>
                $"{s.DisplayName}: {FactionPage.Text(e.Magnitude)} {data.DamageTable.DamageTypeKeys[s.DamageType]} damage/s for {FactionPage.Text(Seconds(e.DurationTicks))} s",
            StatusKind.Slow => $"{s.DisplayName} {FactionPage.Text(Math.Round(e.Magnitude * 100.0, 3))}%",
            _ => s.DisplayName,
        };
    }

    private static string? NumberProblem(string text, string suffix, double value)
    {
        string number = text;
        if (suffix.Length > 0)
        {
            if (!text.EndsWith(suffix, StringComparison.Ordinal)) return $"page '{text}' lacks the unit '{suffix.Trim()}', data {FactionPage.Text(value)}";
            number = text[..^suffix.Length];
        }
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double page)) return $"page '{text}' is not a number";
        return Math.Abs(page - value) < 1e-4 ? null : $"page '{text}', data {FactionPage.Text(Math.Round(value, 4))}{suffix}";
    }

    private static double Seconds(int ticks) => ticks / (double)SimConstants.TicksPerSecond;
}
