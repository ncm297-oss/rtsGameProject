using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
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
    /// Page rows that may have no <c>abilities.json</c> entry yet: Sandstorm (M4-4b-2, zones; pinned in D10c). Once one lands
    /// it is compared like any other, but its differences are reported, not failed, until it is removed here. The Cusser
    /// left the list in D10b (BUG-0350).
    /// </summary>
    public static readonly string[] PendingAbilities = { "Sandstorm" };

    /// <summary>
    /// docs/02 "Status effects" rows with no <c>statuses.json</c> entry yet (stealth, frenzy, passives, zones). A landed one is
    /// compared like any other, but its differences are reported, not failed, until it is removed here (Blinded: D10c).
    /// </summary>
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
        // A copy of the pending Sandstorm row renamed: the copy fails, the pending original is only reported.
        var lines = PageTables.FactionLines("whirlwind").ToList();
        int row = lines.FindIndex(l => l.StartsWith("| Sandstorm |", StringComparison.Ordinal));
        Assert.True(row >= 0, "whirlwind.md has no Sandstorm row");
        lines.Insert(row + 1, lines[row].Replace("| Sandstorm |", "| Grenado |", StringComparison.Ordinal));

        Result r = Compare(Data, Data.Abilities, "whirlwind", lines.ToArray());

        Assert.Contains(r.Problems, p => p.Contains("Grenado", StringComparison.Ordinal));
        Assert.Single(r.Problems);
        Assert.Contains(r.Reports, l => l.Contains("Sandstorm", StringComparison.Ordinal) && l.Contains("pending", StringComparison.Ordinal));
    }

    /// <summary>
    /// D10a criterion 2 / D10b criterion 3: a Sandstorm entry landing (here a stand-in built in memory, cells deliberately off
    /// from the page and an Effect the pin can't read yet) passes with report lines naming it.
    /// </summary>
    [Fact]
    public void ALandedPendingAbility_PassesWithAReportLine()
    {
        var sandstorm = new AbilityDef
        {
            Id = Data.Abilities.Length,
            Key = "sandstorm",
            Faction = Data.FindFaction("whirlwind"),
            DisplayName = "Sandstorm",
            Description = "",
            Kind = AbilityKind.TargetGround,
            Range = 18,
            Radius = 5,
            CastTicks = 24,
            CooldownTicks = 45 * SimConstants.TicksPerSecond,
            DurationTicks = 12 * SimConstants.TicksPerSecond,
            Affects = AbilityAffects.EnemyUnits,
            Effects = ImmutableArray.Create(new AbilityEffect
            {
                Kind = AbilityEffectKind.ApplyStatus,
                DamageType = -1,
                Status = Data.Statuses.Single(s => s.Key == "slowed").Id,
                Magnitude = 0.3f,
                DurationTicks = SimConstants.TicksPerSecond,
            }),
        };
        var abilities = Data.Abilities.Add(sandstorm);

        Result r = Compare(Data, abilities, "whirlwind", PageTables.FactionLines("whirlwind"));
        foreach (string line in r.Reports) _out.WriteLine(line);

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
        Assert.Contains(r.Reports, l => l.Contains("Sandstorm", StringComparison.Ordinal) && l.Contains("landed", StringComparison.Ordinal));
        Assert.Contains(r.Reports, l => l.Contains("Sandstorm", StringComparison.Ordinal) && l.Contains("Radius", StringComparison.Ordinal));
        Assert.Contains(r.Reports, l => l.Contains("Sandstorm", StringComparison.Ordinal) && l.Contains("Effect", StringComparison.Ordinal));
    }

    [Fact]
    public void Cusser_IsLoaded_AndStrictlyPinned()
    {
        AbilityDef cusser = Assert.Single(Data.Abilities, a => a.DisplayName == "Cusser");
        Assert.DoesNotContain("Cusser", PendingAbilities);
        Assert.Contains(cusser.Effects, e => e.Kind == AbilityEffectKind.Damage && e.Buildings && e.FriendlyFire > 0);
    }

    /// <summary>A copy of <paramref name="a"/> with other effects and duration (AbilityDef has no <c>with</c>).</summary>
    private static AbilityDef Copy(AbilityDef a, ImmutableArray<AbilityEffect> effects, int durationTicks) => new()
    {
        Id = a.Id, Key = a.Key, Faction = a.Faction, DisplayName = a.DisplayName, Description = a.Description, Kind = a.Kind,
        Range = a.Range, Radius = a.Radius, CastTicks = a.CastTicks, CooldownTicks = a.CooldownTicks, DurationTicks = durationTicks,
        Affects = a.Affects, Effects = effects, HitsBuildings = effects.Any(e => e.Kind == AbilityEffectKind.Damage && e.Buildings),
    };

    /// <summary>The loaded abilities with the one named <paramref name="name"/> replaced by <paramref name="edit"/>'s copy.</summary>
    private static ImmutableArray<AbilityDef> With(string name, Func<AbilityDef, AbilityDef> edit)
    {
        AbilityDef a = Data.Abilities.Single(x => x.DisplayName == name);
        return Data.Abilities.SetItem(Data.Abilities.IndexOf(a), edit(a));
    }

    /// <summary>
    /// D10b criterion 1 (BUG-0350): one field of the Cusser's damage effect changed in memory fails naming the Cusser and the
    /// field, against the unchanged page.
    /// </summary>
    [Theory]
    [InlineData("amount")]
    [InlineData("type")]
    [InlineData("buildings")]
    [InlineData("friendlyFire")]
    [InlineData("friendlyFire0")]
    public void AMutatedCusserField_FailsNamingCusserAndTheField(string mutant)
    {
        int magic = Data.DamageTable.DamageTypeKeys.IndexOf("magic");
        var abilities = With("Cusser", a => Copy(a, a.Effects.Select(e => mutant switch
        {
            "amount" => e with { Amount = e.Amount + 1 },
            "type" => e with { DamageType = magic },
            "buildings" => e with { Buildings = false },
            "friendlyFire" => e with { FriendlyFire = 0.25f },
            _ => e with { FriendlyFire = 0 },
        }).ToImmutableArray(), a.DurationTicks));

        Result r = Compare(Data, abilities, "malazan", PageTables.FactionLines("malazan"));

        string field = mutant == "friendlyFire0" ? "friendlyFire" : mutant;
        Assert.True(r.Problems.Any(p => p.Contains("'Cusser'", StringComparison.Ordinal) && p.Contains("Effect " + field, StringComparison.Ordinal)),
            $"no problem naming 'Cusser' and '{field}':\n" + string.Join("\n", r.Problems));
    }

    /// <summary>D10b criterion 2: a Telas Fire that reached buildings would fail by name against "No effect on buildings".</summary>
    [Fact]
    public void ATelasFireThatHitBuildings_Fails()
    {
        // Telas Fire's effect is a status, so the buildings flag is set on a damage effect added beside it.
        var abilities = With("Telas Fire", a => Copy(a,
            a.Effects.Add(new AbilityEffect { Kind = AbilityEffectKind.Damage, DamageType = 0, Amount = 1, Status = -1, Buildings = true }), a.DurationTicks));

        Result r = Compare(Data, abilities, "malazan", PageTables.FactionLines("malazan"));

        Assert.Contains(r.Problems, p => p.Contains("'Telas Fire' Effect buildings", StringComparison.Ordinal));
    }

    /// <summary>D10b criterion 1: one Cusser page cell changed in memory fails naming the Cusser and the field.</summary>
    [Theory]
    [InlineData("Effect amount", "take 120 siege damage", "take 100 siege damage")]
    [InlineData("Effect type", "take 120 siege damage", "take 120 pierce damage")]
    [InlineData("Effect amount", "take 120 siege damage", "take heavy damage")]
    [InlineData("Effect buildings", "full damage to buildings", "No effect on buildings")]
    [InlineData("Effect buildings", "full damage to buildings", "half damage to buildings")]
    [InlineData("Effect friendlyFire", "friendly fire at 50%", "friendly fire at 25%")]
    [InlineData("Effect friendlyFire", ", friendly fire at 50%", "")]
    [InlineData("Effect friendlyFire", "friendly fire at 50%", "own units take 100%")]
    [InlineData("Effect affects", "Enemy units in the area", "Units in the area")]
    [InlineData("Range", "| 6 |", "| 7 |")]
    [InlineData("Radius", "| 3.5 m |", "| 3 m |")]
    [InlineData("Cast", "| 1.0 s |", "| 1.2 s |")]
    [InlineData("Cooldown", "| 45 s |", "| 30 s |")]
    [InlineData("Duration", "| 45 s | — |", "| 45 s | 2 s |")]
    [InlineData("Unit", "| Sapper |", "| Catapult |")]
    public void AMutatedCusserCell_FailsNamingCusserAndTheField(string column, string cell, string mutant)
    {
        string[] lines = PageTables.FactionLines("malazan");
        int row = Array.FindIndex(lines, l => l.StartsWith("| Cusser |", StringComparison.Ordinal));
        Assert.True(row >= 0 && lines[row].Contains(cell, StringComparison.Ordinal), $"malazan.md's Cusser row has no '{cell}'");
        lines[row] = lines[row].Replace(cell, mutant, StringComparison.Ordinal);

        Result r = Compare(Data, Data.Abilities, "malazan", lines);

        Assert.True(r.Problems.Any(p => p.Contains("'Cusser' " + column, StringComparison.Ordinal)),
            $"no problem naming 'Cusser' {column}:\n" + string.Join("\n", r.Problems));
    }

    /// <summary>"own units take half" says the same as "friendly fire at 50%" (the QA focus on wording robustness).</summary>
    [Theory]
    [InlineData("friendly fire at 50%")]
    [InlineData("own units take half")]
    [InlineData("own units in the blast take half")]
    [InlineData("Own units take 50%")]
    [InlineData("friendly fire at 50 %")]
    public void TheCusserFriendlyFire_ReadsEitherWording(string wording)
    {
        string[] lines = PageTables.FactionLines("malazan");
        int row = Array.FindIndex(lines, l => l.StartsWith("| Cusser |", StringComparison.Ordinal));
        lines[row] = lines[row].Replace("friendly fire at 50%", wording, StringComparison.Ordinal);

        Result r = Compare(Data, Data.Abilities, "malazan", lines);

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
    }

    /// <summary>BUG-0351: a data duration against the page's '—' reads as a value mismatch.</summary>
    [Fact]
    public void ADurationAgainstADash_ReadsAsAValueMismatch()
    {
        var abilities = With("Telas Fire", a => Copy(a, a.Effects, 6 * SimConstants.TicksPerSecond));

        Result r = Compare(Data, abilities, "malazan", PageTables.FactionLines("malazan"));

        Assert.Contains("malazan.md \"Abilities\" 'Telas Fire' Duration: page '—', data 6 s", r.Problems);
    }

    [Fact]
    public void EveryLoadedStatus_HasItsDocs02Row()
    {
        Result r = CompareStatuses(Data, Data.Statuses, Data.Abilities, FactionPage.DocTable(StatusDoc, "Status effects", level: 3));
        foreach (string line in r.Reports) _out.WriteLine(line);
        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
    }

    /// <summary>
    /// Compares the loaded <paramref name="statuses"/> to docs/02 "Status effects" <paramref name="rows"/>: each has a row
    /// whose Effect matches its kind, and each ability applying one is a Typical source. A pending status (or ability) that
    /// has landed goes to <c>Reports</c>, so the sim can ship it before the pin is written.
    /// </summary>
    public static Result CompareStatuses(GameData data, IReadOnlyList<StatusDef> statuses, IEnumerable<AbilityDef> abilities, string[][] rows)
    {
        var r = new Result(new List<string>(), new List<string>());
        foreach (StatusDef s in statuses)
        {
            bool pending = PendingStatuses.Contains(s.DisplayName);
            List<string> sink = pending ? r.Reports : r.Problems;
            if (pending) r.Reports.Add($"status '{s.DisplayName}' has landed in statuses.json; drop it from PendingStatuses to pin it");
            string[]? row = rows.FirstOrDefault(c => c[0] == s.DisplayName);
            if (row == null) { sink.Add($"status '{s.Key}' ({s.DisplayName}): no docs/02 \"Status effects\" row"); continue; }
            string effect = s.Kind switch
            {
                StatusKind.DamageOverTime => $"Damage over time ({data.DamageTable.DamageTypeKeys[s.DamageType]})",
                StatusKind.Slow => "Movement speed × (1 - magnitude)",
                _ => "",
            };
            if (row[1] != effect) sink.Add($"status '{s.Key}' Effect: docs/02 '{row[1]}', data {s.Kind} -> '{effect}'");
        }
        // Each loaded ability that applies a status is listed as one of its typical sources.
        foreach (AbilityDef a in abilities)
            foreach (AbilityEffect e in a.Effects)
            {
                if (e.Kind != AbilityEffectKind.ApplyStatus) continue;
                string name = statuses[e.Status].DisplayName;
                string[]? row = rows.FirstOrDefault(c => c[0] == name);
                if (row == null || row[2].Split(',').Select(x => x.Trim()).Contains(a.DisplayName)) continue;
                List<string> sink = PendingStatuses.Contains(name) || PendingAbilities.Contains(a.DisplayName) ? r.Reports : r.Problems;
                sink.Add($"status '{name}' Typical source '{row[2]}' does not list '{a.DisplayName}', which applies it");
            }
        return r;
    }

    /// <summary>D10b criterion 3: a Blinded status landing (stand-in, a kind this pin doesn't know) reports and passes.</summary>
    [Fact]
    public void ALandedPendingStatus_PassesWithAReportLine()
    {
        var blinded = new StatusDef { Id = Data.Statuses.Length, Key = "blinded", DisplayName = "Blinded", Description = "", Kind = (StatusKind)99 };
        var statuses = Data.Statuses.Add(blinded);

        Result r = CompareStatuses(Data, statuses, Data.Abilities, FactionPage.DocTable(StatusDoc, "Status effects", level: 3));
        foreach (string line in r.Reports) _out.WriteLine(line);

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
        Assert.Contains(r.Reports, l => l.Contains("Blinded", StringComparison.Ordinal) && l.Contains("landed", StringComparison.Ordinal));
        Assert.Contains(r.Reports, l => l.Contains("blinded", StringComparison.Ordinal) && l.Contains("Effect", StringComparison.Ordinal));
    }

    [Fact]
    public void ALoadedStatusWithAWrongKind_Fails()
    {
        StatusDef burning = Data.Statuses.Single(s => s.Key == "burning");
        var statuses = Data.Statuses.SetItem(burning.Id, new StatusDef { Id = burning.Id, Key = "burning", DisplayName = "Burning", Description = "", Kind = StatusKind.Slow });

        Result r = CompareStatuses(Data, statuses, Data.Abilities, FactionPage.DocTable(StatusDoc, "Status effects", level: 3));

        Assert.Contains(r.Problems, p => p.Contains("burning", StringComparison.Ordinal) && p.Contains("Effect", StringComparison.Ordinal));
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

        foreach ((string column, string problem) in EffectProblems(data, a, cell("Effect")))
            yield return (column, problem);
    }

    /// <summary>"120 siege damage": a damage claim (not "10 magic damage/s", which is a damage-over-time status's text).</summary>
    private static readonly Regex DamageClaim = new(@"(\d+(?:\.\d+)?) ([a-z_]+) damage(?!/s)", RegexOptions.CultureInvariant);

    /// <summary>"friendly fire at 50%" or "own units take half" / "own units in the blast take 25%": the friendly-fire fraction.</summary>
    private static readonly Regex FriendlyFireClaim = new(
        @"friendly fire at (?<p>\d+(?:\.\d+)?) ?%|own units (?:[a-z ]*? )?take (?:(?<half>half)|(?<p>\d+(?:\.\d+)?) ?%)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// The Effect cell against the effects (BUG-0350): who it lands on (<c>affects</c>), each damage effect's
    /// "&lt;amount&gt; &lt;type&gt; damage", buildings ("full damage to buildings" / "No effect on buildings"), the
    /// friendly-fire fraction, and each status's text. Each problem's column names the data field it disagrees with
    /// (<c>Effect amount</c>, <c>Effect type</c>, <c>Effect buildings</c>, <c>Effect friendlyFire</c>, ...).
    /// </summary>
    private static IEnumerable<(string Column, string Problem)> EffectProblems(GameData data, AbilityDef a, string effect)
    {
        bool Says(string text) => effect.Contains(text, StringComparison.OrdinalIgnoreCase);

        // Who: the phrase may sit anywhere in the cell ("Enemy units in the area take ..."); an enemy-only ability must not
        // read as one on your own units, and the reverse.
        string? whoProblem = a.Affects switch
        {
            AbilityAffects.EnemyUnits when !Says("enemy units") => "does not say 'enemy units'",
            AbilityAffects.OwnUnits when !Says("own units") => "does not say 'own units'",
            AbilityAffects.OwnUnits when Says("enemy units") => "says 'enemy units'",
            AbilityAffects.AllUnits when !Says("friendly fire") => "does not say 'friendly fire'",
            _ => null,
        };
        if (whoProblem != null) yield return ("Effect affects", $"'{effect}' {whoProblem} (data affects {a.Affects})");

        // Damage effects: every "<amount> <type> damage" claim matched to a damage effect; a near miss names the field.
        var claims = new List<(double Amount, string Type, string Text)>();
        foreach (Match m in DamageClaim.Matches(effect))
            claims.Add((double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value, m.Value));
        bool anyDamage = false;
        foreach (AbilityEffect e in a.Effects)
        {
            if (e.Kind != AbilityEffectKind.Damage) continue;
            anyDamage = true;
            string type = data.DamageTable.DamageTypeKeys[e.DamageType];
            string want = $"{e.Amount} {type} damage";
            if (claims.Any(c => c.Amount == e.Amount && c.Type == type)) continue;
            if (claims.Count == 0)
            {
                yield return ("Effect amount", $"'{effect}' does not say '{want}' (data amount {e.Amount}, type {type})");
                continue;
            }
            (double Amount, string Type, string Text) near = claims.FirstOrDefault(c => c.Type == type);
            if (near.Text == null) near = claims.FirstOrDefault(c => c.Amount == e.Amount);
            if (near.Text == null) near = claims[0];
            if (near.Amount != e.Amount) yield return ("Effect amount", $"page '{near.Text}', data amount {e.Amount} ('{want}')");
            if (near.Type != type) yield return ("Effect type", $"page '{near.Text}', data type '{type}' ('{want}')");
        }
        if (!anyDamage && claims.Count > 0)
            yield return ("Effect amount", $"page says '{claims[0].Text}', data has no damage effect");

        // Buildings: only a damage effect with "buildings": true reaches them; the page says which.
        bool hits = a.Effects.Any(e => e.Kind == AbilityEffectKind.Damage && e.Buildings);
        bool saysFull = Says("full damage to buildings"), saysNone = Says("No effect on buildings");
        if (hits && !saysFull) yield return ("Effect buildings", $"'{effect}' does not say 'full damage to buildings' (data buildings true)");
        if (hits && saysNone) yield return ("Effect buildings", $"'{effect}' says 'No effect on buildings', data buildings true");
        if (!hits && !saysNone) yield return ("Effect buildings", $"'{effect}' does not say 'No effect on buildings' (data buildings false)");
        if (!hits && saysFull) yield return ("Effect buildings", $"'{effect}' says 'full damage to buildings', data buildings false");

        // Friendly fire: the fraction of the hit the caster's own units take, as a percentage ("half" is 50%).
        double ff = a.Effects.Where(e => e.Kind == AbilityEffectKind.Damage).Select(e => (double)e.FriendlyFire).DefaultIfEmpty(0).Max();
        double wantPct = Math.Round(ff * 100.0, 3);
        var ffClaims = new List<(double Pct, string Text)>();
        foreach (Match m in FriendlyFireClaim.Matches(effect))
            ffClaims.Add((m.Groups["half"].Success ? 50 : double.Parse(m.Groups["p"].Value, CultureInfo.InvariantCulture), m.Value));
        if (ff > 0 && ffClaims.Count == 0)
            yield return ("Effect friendlyFire", $"'{effect}' does not say 'friendly fire at {FactionPage.Text(wantPct)}%' (data friendlyFire {FactionPage.Text(ff)})");
        foreach ((double pct, string text) in ffClaims)
            if (Math.Abs(pct - wantPct) > 1e-3)
                yield return ("Effect friendlyFire", $"page '{text}', data friendlyFire {FactionPage.Text(Math.Round(ff, 4))} ({FactionPage.Text(wantPct)}%)");

        foreach (AbilityEffect e in a.Effects)
        {
            if (e.Kind == AbilityEffectKind.Damage) continue;
            string want = e.Kind == AbilityEffectKind.ApplyStatus ? StatusText(data, e) : e.Kind.ToString();
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
            // BUG-0351: '—' (no value) against a data value is a value mismatch, not a missing unit.
            if (text == "—") return $"page '—', data {FactionPage.Text(Math.Round(value, 4))}{suffix}";
            if (!text.EndsWith(suffix, StringComparison.Ordinal)) return $"page '{text}' lacks the unit '{suffix.Trim()}', data {FactionPage.Text(Math.Round(value, 4))}{suffix}";
            number = text[..^suffix.Length];
        }
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double page)) return $"page '{text}' is not a number";
        return Math.Abs(page - value) < 1e-4 ? null : $"page '{text}', data {FactionPage.Text(Math.Round(value, 4))}{suffix}";
    }

    private static double Seconds(int ticks) => ticks / (double)SimConstants.TicksPerSecond;
}
