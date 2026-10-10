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
    /// Page rows that may have no <c>abilities.json</c> entry yet. Once one lands it is compared like any other, but its
    /// differences are reported, not failed, until it is removed here. The Cusser left the list in D10b (BUG-0350),
    /// Sandstorm in D10c; the next M4-4b-3 abilities go here when their page rows are written ahead of the sim.
    /// </summary>
    public static readonly string[] PendingAbilities = Array.Empty<string>();

    /// <summary>
    /// docs/02 "Status effects" rows with no <c>statuses.json</c> entry yet (stealth, frenzy, passives). A landed one is
    /// compared like any other, but its differences are reported, not failed, until it is removed here (Blinded left in D10c).
    /// </summary>
    public static readonly string[] PendingStatuses = { "Stealthed", "Revealed", "Frenzied", "Regenerating" };

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
        // Two renamed copies of the Sandstorm row: the unknown one fails, the one named in the allowance is only reported.
        var lines = PageTables.FactionLines("whirlwind").ToList();
        int row = lines.FindIndex(l => l.StartsWith("| Sandstorm |", StringComparison.Ordinal));
        Assert.True(row >= 0, "whirlwind.md has no Sandstorm row");
        lines.Insert(row + 1, lines[row].Replace("| Sandstorm |", "| Grenado |", StringComparison.Ordinal));
        lines.Insert(row + 1, lines[row].Replace("| Sandstorm |", "| Dust Devil |", StringComparison.Ordinal));

        Result r = Compare(Data, Data.Abilities, "whirlwind", lines.ToArray(), pendingAbilities: new[] { "Dust Devil" });

        Assert.Contains(r.Problems, p => p.Contains("Grenado", StringComparison.Ordinal));
        Assert.Single(r.Problems);
        Assert.Contains(r.Reports, l => l.Contains("Dust Devil", StringComparison.Ordinal) && l.Contains("pending", StringComparison.Ordinal));
    }

    /// <summary>
    /// D10a criterion 2 / D10b criterion 3, kept after Sandstorm's pin (D10c): an ability landing while still in the allowance
    /// (here the real Sandstorm with its cells deliberately off from the page) passes with report lines naming it.
    /// </summary>
    [Fact]
    public void ALandedPendingAbility_PassesWithAReportLine()
    {
        var abilities = WithSandstorm(a => CopyAll(a, radius: 5, effects: ImmutableArray.Create(new AbilityEffect
        {
            Kind = AbilityEffectKind.ApplyStatus,
            DamageType = -1,
            Status = Data.Statuses.Single(s => s.Key == "burning").Id,
            Magnitude = 3,
            DurationTicks = SimConstants.TicksPerSecond,
        })));

        Result r = Compare(Data, abilities, "whirlwind", PageTables.FactionLines("whirlwind"), pendingAbilities: new[] { "Sandstorm" });
        foreach (string line in r.Reports) _out.WriteLine(line);

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
        Assert.Contains(r.Reports, l => l.Contains("Sandstorm", StringComparison.Ordinal) && l.Contains("landed", StringComparison.Ordinal));
        Assert.Contains(r.Reports, l => l.Contains("Sandstorm", StringComparison.Ordinal) && l.Contains("Radius", StringComparison.Ordinal));
        Assert.Contains(r.Reports, l => l.Contains("Sandstorm", StringComparison.Ordinal) && l.Contains("Effect", StringComparison.Ordinal));
    }

    [Fact]
    public void Sandstorm_IsLoaded_AndStrictlyPinned()
    {
        AbilityDef sandstorm = Assert.Single(Data.Abilities, a => a.DisplayName == "Sandstorm");
        Assert.DoesNotContain("Sandstorm", PendingAbilities);
        Assert.Contains(sandstorm.Effects, e => e.Kind == AbilityEffectKind.CreateZone && e.ZoneStatuses.Length > 0);
    }

    /// <summary>The loaded abilities with Sandstorm replaced by <paramref name="edit"/>'s copy.</summary>
    public static ImmutableArray<AbilityDef> WithSandstorm(Func<AbilityDef, AbilityDef> edit) => With("Sandstorm", edit);

    /// <summary>A copy of <paramref name="a"/> with any of its numbers, <c>affects</c> or effects replaced.</summary>
    public static AbilityDef CopyAll(AbilityDef a, float? range = null, float? radius = null, int? castTicks = null, int? cooldownTicks = null,
        int? durationTicks = null, AbilityAffects? affects = null, ImmutableArray<AbilityEffect>? effects = null)
    {
        ImmutableArray<AbilityEffect> fx = effects ?? a.Effects;
        return new AbilityDef
        {
            Id = a.Id, Key = a.Key, Faction = a.Faction, DisplayName = a.DisplayName, Description = a.Description, Kind = a.Kind,
            Range = range ?? a.Range, Radius = radius ?? a.Radius, CastTicks = castTicks ?? a.CastTicks,
            CooldownTicks = cooldownTicks ?? a.CooldownTicks, DurationTicks = durationTicks ?? a.DurationTicks,
            Affects = affects ?? a.Affects, Effects = fx,
            HitsBuildings = fx.Any(e => e.Kind == AbilityEffectKind.Damage && e.Buildings),
            ZoneEffect = ZoneIndex(fx),
        };
    }

    private static int ZoneIndex(ImmutableArray<AbilityEffect> effects)
    {
        for (int i = 0; i < effects.Length; i++) if (effects[i].Kind == AbilityEffectKind.CreateZone) return i;
        return -1;
    }

    /// <summary>Sandstorm's zone effect with its statuses (and blocks-vision flag) edited.</summary>
    private static AbilityDef EditZone(AbilityDef a, Func<AbilityEffect, AbilityEffect> edit) =>
        CopyAll(a, effects: a.Effects.Select(e => e.Kind == AbilityEffectKind.CreateZone ? edit(e) : e).ToImmutableArray());

    private static int StatusId(string key) => Data.Statuses.Single(s => s.Key == key).Id;

    /// <summary>
    /// D10c criterion 1, the data side: one Sandstorm field changed in memory fails naming 'Sandstorm' and the field against
    /// the unchanged whirlwind.md row.
    /// </summary>
    [Theory]
    [InlineData("range", "Range")]
    [InlineData("radius", "Radius")]
    [InlineData("cast", "Cast")]
    [InlineData("cooldown", "Cooldown")]
    [InlineData("duration", "Duration")]
    [InlineData("affects-own", "Effect affects")]
    [InlineData("affects-all", "Effect affects")]
    [InlineData("blocksVision", "Effect blocksVision")]
    [InlineData("drop-blinded", "Effect status")]
    [InlineData("drop-slowed", "Effect status")]
    [InlineData("slow-magnitude", "Effect magnitude")]
    [InlineData("add-burning", "Effect status")]
    [InlineData("blinded-to-burning", "Effect status")]
    [InlineData("damage", "Effect amount")]
    public void AMutatedSandstormField_FailsNamingSandstormAndTheField(string mutant, string column)
    {
        int ticks = SimConstants.TicksPerSecond;
        ImmutableArray<AbilityDef> abilities = WithSandstorm(a => mutant switch
        {
            "range" => CopyAll(a, range: 20),
            "radius" => CopyAll(a, radius: 5),
            "cast" => CopyAll(a, castTicks: a.CastTicks + 4),
            "cooldown" => CopyAll(a, cooldownTicks: 30 * ticks),
            "duration" => CopyAll(a, durationTicks: 10 * ticks),
            "affects-own" => CopyAll(a, affects: AbilityAffects.OwnUnits),
            "affects-all" => CopyAll(a, affects: AbilityAffects.AllUnits),
            "blocksVision" => EditZone(a, e => e with { BlocksVision = false }),
            "drop-blinded" => EditZone(a, e => e with { ZoneStatuses = e.ZoneStatuses.RemoveAll(z => z.Status == StatusId("blinded")) }),
            "drop-slowed" => EditZone(a, e => e with { ZoneStatuses = e.ZoneStatuses.RemoveAll(z => z.Status == StatusId("slowed")) }),
            "slow-magnitude" => EditZone(a, e => e with { ZoneStatuses = e.ZoneStatuses.Select(z => z.Status == StatusId("slowed") ? z with { Magnitude = 0.4f } : z).ToImmutableArray() }),
            "add-burning" => EditZone(a, e => e with { ZoneStatuses = e.ZoneStatuses.Add(new ZoneStatus { Status = StatusId("burning"), Magnitude = 5, DurationTicks = ticks }) }),
            "blinded-to-burning" => EditZone(a, e => e with { ZoneStatuses = e.ZoneStatuses.Select(z => z.Status == StatusId("blinded") ? new ZoneStatus { Status = StatusId("burning"), Magnitude = 5, DurationTicks = ticks } : z).ToImmutableArray() }),
            _ => CopyAll(a, effects: a.Effects.Add(new AbilityEffect { Kind = AbilityEffectKind.Damage, DamageType = 0, Amount = 10, Status = -1 })),
        });

        Result r = Compare(Data, abilities, "whirlwind", PageTables.FactionLines("whirlwind"));

        Assert.True(r.Problems.Any(p => p.Contains("'Sandstorm' " + column, StringComparison.Ordinal)),
            $"no problem naming 'Sandstorm' {column}:\n" + string.Join("\n", r.Problems));
    }

    /// <summary>D10c criterion 1, the page side: one whirlwind.md Sandstorm cell changed in memory fails naming 'Sandstorm' and the field.</summary>
    [Theory]
    [InlineData("Range", "| 18 |", "| 20 |")]
    [InlineData("Radius", "| 6 m |", "| 5 m |")]
    [InlineData("Cast", "| 1.2 s |", "| 1.5 s |")]
    [InlineData("Cooldown", "| 45 s |", "| 30 s |")]
    [InlineData("Duration", "| 12 s |", "| 10 s |")]
    [InlineData("Duration", "| 12 s |", "| — |")]
    [InlineData("Unit", "| Priest of the Whirlwind |", "| Zealot |")]
    [InlineData("Kind", "| Target ground (zone) |", "| Target unit |")]
    [InlineData("Effect affects", "Enemy units inside", "Non-Whirlwind units inside")]
    [InlineData("Effect affects", "Enemy units inside", "Units inside")]
    [InlineData("Effect blocksVision", " Enemies outside can't see into the storm.", "")]
    [InlineData("Effect status", "Blinded and Slowed 30%", "Slowed 30%")]
    [InlineData("Effect status", "Blinded and Slowed 30%", "Blinded")]
    [InlineData("Effect status", "Blinded and Slowed 30%", "Blinded, Burning and Slowed 30%")]
    [InlineData("Effect magnitude", "Slowed 30%", "Slowed 40%")]
    [InlineData("Effect magnitude", "Slowed 30%", "Slowed")]
    [InlineData("Effect buildings", "No effect on buildings", "full damage to buildings")]
    [InlineData("Effect amount", "Slowed 30%.", "Slowed 30% and take 20 magic damage.")]
    public void AMutatedSandstormCell_FailsNamingSandstormAndTheField(string column, string cell, string mutant)
    {
        string[] lines = PageTables.FactionLines("whirlwind");
        int row = Array.FindIndex(lines, l => l.StartsWith("| Sandstorm |", StringComparison.Ordinal));
        Assert.True(row >= 0 && lines[row].Contains(cell, StringComparison.Ordinal), $"whirlwind.md's Sandstorm row has no '{cell}'");
        lines[row] = lines[row].Replace(cell, mutant, StringComparison.Ordinal);

        Result r = Compare(Data, Data.Abilities, "whirlwind", lines);

        Assert.True(r.Problems.Any(p => p.Contains("'Sandstorm' " + column, StringComparison.Ordinal)),
            $"no problem naming 'Sandstorm' {column}:\n" + string.Join("\n", r.Problems));
    }

    /// <summary>The Sandstorm Effect cell read in other orders and wordings still passes (a zone's phrases are not positional).</summary>
    [Theory]
    [InlineData("No effect on buildings. Enemies outside cannot see in; enemy units inside are Slowed by 30% and Blinded")]
    [InlineData("Blinded and Slowed 30 %: enemy units inside. Enemies outside can't see into the storm. No effect on buildings")]
    public void AReorderedSandstormEffect_Passes(string effect)
    {
        string[] lines = PageTables.FactionLines("whirlwind");
        int row = Array.FindIndex(lines, l => l.StartsWith("| Sandstorm |", StringComparison.Ordinal));
        string[] cells = PageTables.Cells(lines[row]);
        cells[^1] = effect;
        lines[row] = "| " + string.Join(" | ", cells) + " |";

        Result r = Compare(Data, Data.Abilities, "whirlwind", lines);

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
    }

    [Fact]
    public void EveryAbilityAndStatusDescription_StatesOnlyItsOwnDataNumbers()
    {
        List<string> problems = DescriptionProblems(Data, Data.Abilities, Data.Statuses);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>D10c: the description pin can fail, by name, for a stale number in an ability or a status tooltip.</summary>
    [Theory]
    [InlineData("cusser", "120 siege", "100 siege")]
    [InlineData("telas_fire", "for 4 seconds", "for 5 seconds")]
    [InlineData("sandstorm", "Slowed by 30%", "Slowed by 40%")]
    [InlineData("sandstorm", "for 12 seconds", "for 10 seconds")]
    [InlineData("cusser", "take half", "take a quarter")]
    [InlineData("blinded", "drops to 2 m", "drops to 4 m")]
    [InlineData("blinded", "more than 3 m", "more than 5 m")]
    public void AStaleNumberInADescription_FailsNamingIt(string key, string text, string mutant)
    {
        ImmutableArray<AbilityDef> abilities = Data.Abilities;
        ImmutableArray<StatusDef> statuses = Data.Statuses;
        if (key == "blinded")
        {
            StatusDef b = statuses.Single(s => s.Key == key);
            Assert.Contains(text, b.Description, StringComparison.Ordinal);
            statuses = statuses.SetItem(b.Id, new StatusDef
            {
                Id = b.Id, Key = b.Key, DisplayName = b.DisplayName, Description = b.Description.Replace(text, mutant, StringComparison.Ordinal),
                Kind = b.Kind, DamageType = b.DamageType, Sight = b.Sight, Reach = b.Reach,
            });
        }
        else
        {
            AbilityDef a = abilities.Single(x => x.Key == key);
            Assert.Contains(text, a.Description, StringComparison.Ordinal);
            AbilityDef c = CopyAll(a);
            abilities = abilities.SetItem(abilities.IndexOf(a), new AbilityDef
            {
                Id = c.Id, Key = c.Key, Faction = c.Faction, DisplayName = c.DisplayName, Description = a.Description.Replace(text, mutant, StringComparison.Ordinal),
                Kind = c.Kind, Range = c.Range, Radius = c.Radius, CastTicks = c.CastTicks, CooldownTicks = c.CooldownTicks,
                DurationTicks = c.DurationTicks, Affects = c.Affects, Effects = c.Effects, HitsBuildings = c.HitsBuildings, ZoneEffect = c.ZoneEffect,
            });
        }

        List<string> problems = DescriptionProblems(Data, abilities, statuses);

        Assert.True(problems.Any(p => p.StartsWith(key + " description", StringComparison.Ordinal)),
            $"no description problem for '{key}':\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// Each number in an ability's or a status's <c>description</c> is one of its own data numbers (ability: range, radius,
    /// cast, cooldown, duration in seconds, damage amounts, friendly fire as a percentage, each applied status's magnitude,
    /// percentage and duration; status: sight, reach), and "half" / "a quarter" of the hit is the data's friendly fire. The
    /// words around the numbers (who, buildings, which status) are not parsed here; the page pins hold those.
    /// </summary>
    public static List<string> DescriptionProblems(GameData data, IEnumerable<AbilityDef> abilities, IEnumerable<StatusDef> statuses)
    {
        var problems = new List<string>();
        foreach (AbilityDef a in abilities)
        {
            var allowed = new List<double> { a.Range, a.Radius, Seconds(a.CastTicks), Seconds(a.CooldownTicks), Seconds(a.DurationTicks) };
            double ff = 0;
            foreach (AbilityEffect e in a.Effects)
            {
                if (e.Kind == AbilityEffectKind.Damage) { allowed.Add(e.Amount); allowed.Add(e.FriendlyFire * 100.0); ff = Math.Max(ff, e.FriendlyFire); }
                if (e.Kind == AbilityEffectKind.ApplyStatus) { allowed.Add(e.Magnitude); allowed.Add(e.Magnitude * 100.0); allowed.Add(Seconds(e.DurationTicks)); }
                if (e.Kind == AbilityEffectKind.CreateZone && !e.ZoneStatuses.IsDefault)
                    foreach (ZoneStatus z in e.ZoneStatuses) { allowed.Add(z.Magnitude); allowed.Add(z.Magnitude * 100.0); allowed.Add(Seconds(z.DurationTicks)); }
            }
            NumbersNotIn(a.Key, a.Description, allowed, problems);
            foreach ((string word, double fraction) in new[] { ("half", 0.5), ("a quarter", 0.25) })
                if (Regex.IsMatch(a.Description, @"\btake " + word + @"\b", RegexOptions.CultureInvariant) && Math.Abs(ff - fraction) > 1e-4)
                    problems.Add($"{a.Key} description says 'take {word}', data friendlyFire {FactionPage.Text(Math.Round(ff, 4))}");
            if (ff > 0 && !Regex.IsMatch(a.Description, @"\btake (?:half|a quarter|\d+(?:\.\d+)? ?%)", RegexOptions.CultureInvariant))
                problems.Add($"{a.Key} description: data friendlyFire {FactionPage.Text(Math.Round(ff, 4))}, the text doesn't say what own units take");
        }
        foreach (StatusDef s in statuses)
            NumbersNotIn(s.Key, s.Description, new List<double> { s.Sight, s.Reach }, problems);
        return problems;
    }

    private static void NumbersNotIn(string key, string description, List<double> allowed, List<string> problems)
    {
        foreach (Match m in Regex.Matches(description, @"\d+(?:\.\d+)?", RegexOptions.CultureInvariant))
        {
            double n = double.Parse(m.Value, CultureInfo.InvariantCulture);
            if (!allowed.Any(v => Math.Abs(v - n) < 1e-3))
                problems.Add($"{key} description: '{m.Value}' is none of its data numbers ('{description}')");
        }
    }

    /// <summary>A Sandstorm whose zone hid nothing passes against a page that doesn't claim it (the claim is read both ways).</summary>
    [Fact]
    public void ANonBlockingZone_PassesAgainstAPageThatSaysSo()
    {
        var abilities = WithSandstorm(a => EditZone(a, e => e with { BlocksVision = false }));
        string[] lines = PageTables.FactionLines("whirlwind");
        int row = Array.FindIndex(lines, l => l.StartsWith("| Sandstorm |", StringComparison.Ordinal));
        lines[row] = lines[row].Replace(" Enemies outside can't see into the storm.", "", StringComparison.Ordinal);

        Result r = Compare(Data, abilities, "whirlwind", lines);

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
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
    public static Result CompareStatuses(GameData data, IReadOnlyList<StatusDef> statuses, IEnumerable<AbilityDef> abilities, string[][] rows,
        IReadOnlyCollection<string>? pendingStatuses = null)
    {
        IReadOnlyCollection<string> pendingNames = pendingStatuses ?? PendingStatuses;
        var r = new Result(new List<string>(), new List<string>());
        foreach (StatusDef s in statuses)
        {
            bool pending = pendingNames.Contains(s.DisplayName);
            List<string> sink = pending ? r.Reports : r.Problems;
            if (pending) r.Reports.Add($"status '{s.DisplayName}' has landed in statuses.json; drop it from PendingStatuses to pin it");
            string[]? row = rows.FirstOrDefault(c => c[0] == s.DisplayName);
            if (row == null) { sink.Add($"status '{s.Key}' ({s.DisplayName}): no docs/02 \"Status effects\" row"); continue; }
            if (s.Kind == StatusKind.Blind)
            {
                foreach ((string field, string problem) in BlindProblems(row[1], s))
                    sink.Add($"status '{s.Key}' ({s.DisplayName}) {field}: {problem}");
                continue;
            }
            string effect = s.Kind switch
            {
                StatusKind.DamageOverTime => $"Damage over time ({data.DamageTable.DamageTypeKeys[s.DamageType]})",
                StatusKind.Slow => "Movement speed × (1 - magnitude)",
                _ => "",
            };
            if (row[1] != effect) sink.Add($"status '{s.Key}' Effect: docs/02 '{row[1]}', data {s.Kind} -> '{effect}'");
        }
        // Each loaded ability that applies a status (directly or through a zone) is listed as one of its typical sources.
        foreach (AbilityDef a in abilities)
            foreach (int status in AppliedStatuses(a))
            {
                string name = statuses[status].DisplayName;
                string[]? row = rows.FirstOrDefault(c => c[0] == name);
                if (row == null || row[2].Split(',').Select(x => x.Trim()).Contains(a.DisplayName)) continue;
                List<string> sink = pendingNames.Contains(name) || PendingAbilities.Contains(a.DisplayName) ? r.Reports : r.Problems;
                sink.Add($"status '{name}' Typical source '{row[2]}' does not list '{a.DisplayName}', which applies it");
            }
        return r;
    }

    /// <summary>The status ids <paramref name="a"/> puts on units: its applyStatus effects' and its zones' statuses.</summary>
    private static IEnumerable<int> AppliedStatuses(AbilityDef a)
    {
        foreach (AbilityEffect e in a.Effects)
        {
            if (e.Kind == AbilityEffectKind.ApplyStatus) yield return e.Status;
            if (e.Kind == AbilityEffectKind.CreateZone && !e.ZoneStatuses.IsDefault)
                foreach (ZoneStatus z in e.ZoneStatuses) yield return z.Status;
        }
    }

    /// <summary>"Sight radius 2 m": docs/02's sight claim for a blind status.</summary>
    private static readonly Regex SightClaim = new(@"Sight radius (\d+(?:\.\d+)?) m", RegexOptions.CultureInvariant);

    /// <summary>"more than 3 m away": docs/02's reach claim for a blind status.</summary>
    private static readonly Regex ReachClaim = new(@"more than (\d+(?:\.\d+)?) m away", RegexOptions.CultureInvariant);

    /// <summary>
    /// D10c: a blind status's docs/02 Effect, "Sight radius &lt;sight&gt; m; can't acquire or attack targets more than
    /// &lt;reach&gt; m away", against <see cref="StatusDef.Sight"/> and <see cref="StatusDef.Reach"/>; each problem names the
    /// field (<c>sight</c>, <c>reach</c>), or <c>Effect</c> when the sentence around the numbers changed.
    /// </summary>
    private static IEnumerable<(string Field, string Problem)> BlindProblems(string effect, StatusDef s)
    {
        foreach ((string field, Regex claim, double value) in new[] { ("sight", SightClaim, (double)s.Sight), ("reach", ReachClaim, (double)s.Reach) })
        {
            Match m = claim.Match(effect);
            if (!m.Success) { yield return (field, $"docs/02 '{effect}' states no {field} (data {field} {FactionPage.Text(Math.Round(value, 4))} m)"); continue; }
            double page = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (Math.Abs(page - value) > 1e-4) yield return (field, $"docs/02 '{m.Value}', data {field} {FactionPage.Text(Math.Round(value, 4))} m");
        }
        // The words around the numbers are the rule itself (sight and acquire/attack): a reworded rule fails too.
        string shape = SightClaim.Replace(ReachClaim.Replace(effect, "more than # m away"), "Sight radius # m");
        const string Want = "Sight radius # m; can't acquire or attack targets more than # m away";
        if (shape != Want) yield return ("Effect", $"docs/02 '{effect}', data Blind -> '{Want.Replace("#", "<n>")}'");
    }

    /// <summary>D10b criterion 3, kept after Blinded's pin (D10c): a status landing while pending (stand-in: a Frenzied of a kind this pin doesn't know) reports and passes.</summary>
    [Fact]
    public void ALandedPendingStatus_PassesWithAReportLine()
    {
        var frenzied = new StatusDef { Id = Data.Statuses.Length, Key = "frenzied", DisplayName = "Frenzied", Description = "", Kind = (StatusKind)99 };
        var statuses = Data.Statuses.Add(frenzied);

        Result r = CompareStatuses(Data, statuses, Data.Abilities, FactionPage.DocTable(StatusDoc, "Status effects", level: 3));
        foreach (string line in r.Reports) _out.WriteLine(line);

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
        Assert.Contains(r.Reports, l => l.Contains("Frenzied", StringComparison.Ordinal) && l.Contains("landed", StringComparison.Ordinal));
        Assert.Contains(r.Reports, l => l.Contains("frenzied", StringComparison.Ordinal) && l.Contains("Effect", StringComparison.Ordinal));
    }

    [Fact]
    public void Blinded_IsLoaded_AndStrictlyPinned()
    {
        StatusDef blinded = Assert.Single(Data.Statuses, s => s.DisplayName == "Blinded");
        Assert.Equal(StatusKind.Blind, blinded.Kind);
        Assert.DoesNotContain("Blinded", PendingStatuses);
    }

    /// <summary>D10c criterion 2: docs/02's Blinded row changed in memory fails naming Blinded and the field.</summary>
    [Theory]
    [InlineData("sight", "Sight radius 2 m", "Sight radius 3 m")]
    [InlineData("sight", "Sight radius 2 m", "Sight radius 1.5 m")]
    [InlineData("sight", "Sight radius 2 m", "Sight reduced")]
    [InlineData("reach", "more than 3 m away", "more than 4 m away")]
    [InlineData("reach", "more than 3 m away", "more than 2 m away")]
    [InlineData("Effect", "can't acquire or attack", "can't attack")]
    public void AMutatedDocs02BlindedRow_FailsNamingBlindedAndTheField(string field, string text, string mutant)
    {
        string[][] rows = FactionPage.DocTable(StatusDoc, "Status effects", level: 3);
        string[] row = rows.Single(c => c[0] == "Blinded");
        Assert.True(row[1].Contains(text, StringComparison.Ordinal), $"docs/02 Blinded row has no '{text}'");
        row[1] = row[1].Replace(text, mutant, StringComparison.Ordinal);

        Result r = CompareStatuses(Data, Data.Statuses, Data.Abilities, rows);

        Assert.True(r.Problems.Any(p => p.Contains("(Blinded) " + field + ":", StringComparison.Ordinal)),
            $"no problem naming Blinded {field}:\n" + string.Join("\n", r.Problems));
    }

    /// <summary>D10c criterion 2, the data side: Blinded's <c>sight</c> / <c>reach</c> changed in memory fails by name.</summary>
    [Theory]
    [InlineData("sight", 3f, 3f)]
    [InlineData("sight", 1f, 3f)]
    [InlineData("reach", 2f, 4f)]
    [InlineData("reach", 2f, 2.5f)]
    public void AMutatedBlindedField_FailsNamingBlindedAndTheField(string field, float sight, float reach)
    {
        StatusDef b = Data.Statuses.Single(s => s.Key == "blinded");
        var statuses = Data.Statuses.SetItem(b.Id, new StatusDef
        {
            Id = b.Id, Key = b.Key, DisplayName = b.DisplayName, Description = b.Description, Kind = b.Kind, DamageType = b.DamageType,
            Sight = sight, Reach = reach,
        });

        Result r = CompareStatuses(Data, statuses, Data.Abilities, FactionPage.DocTable(StatusDoc, "Status effects", level: 3));

        Assert.True(r.Problems.Any(p => p.Contains("(Blinded) " + field + ":", StringComparison.Ordinal)),
            $"no problem naming Blinded {field}:\n" + string.Join("\n", r.Problems));
    }

    [Fact]
    public void ASandstormMissingFromATypicalSource_Fails()
    {
        // Sandstorm applies Blinded and Slowed only through its zone: the typical-source check must see zone statuses.
        string[][] rows = FactionPage.DocTable(StatusDoc, "Status effects", level: 3);
        string[] row = rows.Single(c => c[0] == "Blinded");
        row[2] = row[2].Replace("Sandstorm, ", "", StringComparison.Ordinal);

        Result r = CompareStatuses(Data, Data.Statuses, Data.Abilities, rows);

        Assert.Contains(r.Problems, p => p.Contains("'Blinded' Typical source", StringComparison.Ordinal) && p.Contains("'Sandstorm'", StringComparison.Ordinal));
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
    /// <c>Problems</c>. <paramref name="pendingAbilities"/> stands in for <see cref="PendingAbilities"/> (tests of the allowance itself).
    /// </summary>
    public static Result Compare(GameData data, IEnumerable<AbilityDef> abilities, string faction, string[] lines,
        IReadOnlyCollection<string>? pendingAbilities = null)
    {
        IReadOnlyCollection<string> pendingNames = pendingAbilities ?? PendingAbilities;
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
            bool pending = pendingNames.Contains(a.DisplayName);
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
            if (pendingNames.Contains(name)) r.Reports.Add($"{where} '{name}' is pending: no abilities.json entry yet");
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

        // Damage effects, both ways (BUG-0380): each damage effect consumes one "<amount> <type> damage" claim, an exact match
        // first, then a near miss (same type, else same amount, else any) whose differing field is named; every claim left
        // unconsumed is a promise the data doesn't keep.
        var claims = new List<(double Amount, string Type, string Text)>();
        foreach (Match m in DamageClaim.Matches(effect))
            claims.Add((double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value, m.Value));
        var used = new bool[claims.Count];
        int Unused(Func<(double Amount, string Type, string Text), bool> match)
        {
            for (int i = 0; i < claims.Count; i++) if (!used[i] && match(claims[i])) return i;
            return -1;
        }
        var unmatched = new List<(int Amount, string Type)>();
        foreach (AbilityEffect e in a.Effects)
        {
            if (e.Kind != AbilityEffectKind.Damage) continue;
            string type = data.DamageTable.DamageTypeKeys[e.DamageType];
            int exact = Unused(c => c.Amount == e.Amount && c.Type == type);
            if (exact >= 0) used[exact] = true;
            else unmatched.Add((e.Amount, type));
        }
        foreach ((int amount, string type) in unmatched)
        {
            string want = $"{amount} {type} damage";
            int near = Unused(c => c.Type == type);
            if (near < 0) near = Unused(c => c.Amount == amount);
            if (near < 0) near = Unused(_ => true);
            if (near < 0)
            {
                yield return ("Effect amount", $"'{effect}' does not say '{want}' (data amount {amount}, type {type})");
                continue;
            }
            used[near] = true;
            if (claims[near].Amount != amount) yield return ("Effect amount", $"page '{claims[near].Text}', data amount {amount} ('{want}')");
            if (claims[near].Type != type) yield return ("Effect type", $"page '{claims[near].Text}', data type '{type}' ('{want}')");
        }
        for (int i = 0; i < claims.Count; i++)
            if (!used[i]) yield return ("Effect amount", $"page says '{claims[i].Text}', which no data damage effect matches");

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
            if (e.Kind == AbilityEffectKind.Damage || e.Kind == AbilityEffectKind.CreateZone) continue;
            string want = e.Kind == AbilityEffectKind.ApplyStatus ? StatusText(data, e) : e.Kind.ToString();
            if (!effect.Contains(want, StringComparison.Ordinal)) yield return ("Effect", $"'{effect}' does not say '{want}' ({e.Kind})");
        }

        // Zones (D10c): the statuses a createZone effect applies, by name and magnitude, and whether it hides its inside.
        // The Duration column (the zone's lifetime, the ability's duration) is checked with the other numbers above.
        bool blocks = false;
        foreach (AbilityEffect e in a.Effects)
        {
            if (e.Kind != AbilityEffectKind.CreateZone) continue;
            blocks |= e.BlocksVision;
            if (e.ZoneStatuses.IsDefault) continue;
            foreach (ZoneStatus z in e.ZoneStatuses)
                foreach ((string column, string problem) in ZoneStatusProblems(data, z, effect))
                    yield return (column, problem);
        }
        Match blockClaim = BlocksVisionClaim.Match(effect);
        if (blocks && !blockClaim.Success)
            yield return ("Effect blocksVision", $"'{effect}' does not say enemies outside can't see in (data blocksVision true)");
        if (!blocks && blockClaim.Success)
            yield return ("Effect blocksVision", $"page '{blockClaim.Value}', data blocksVision false");

        // The reverse: a status the cell names that no effect applies is a promise the data doesn't keep.
        var applied = AppliedStatuses(a).ToHashSet();
        foreach (StatusDef s in data.Statuses)
            if (!applied.Contains(s.Id) && SaysStatus(effect, s.DisplayName))
                yield return ("Effect status", $"'{effect}' names '{s.DisplayName}', which no data effect applies");
    }

    /// <summary>"can't see into the storm" / "cannot see in": the page's claim that a zone hides what is inside it.</summary>
    private static readonly Regex BlocksVisionClaim = new(@"can(?:'|’|no)t see in(?:to)?\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Whether <paramref name="effect"/> names the status <paramref name="name"/> as a whole word (status names are capitalized).</summary>
    private static bool SaysStatus(string effect, string name) =>
        Regex.IsMatch(effect, @"\b" + Regex.Escape(name) + @"\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// One zone status against the Effect cell: its name (<c>Effect status</c>), and its magnitude (<c>Effect magnitude</c>):
    /// a slow as "Slowed 30%" (or "Slowed by 30%"), a damage over time as "Burning: 10 magic damage/s". A blind has no magnitude.
    /// The status's own duration (how long it lingers after a unit leaves) is data only: the page doesn't state it.
    /// </summary>
    private static IEnumerable<(string Column, string Problem)> ZoneStatusProblems(GameData data, ZoneStatus z, string effect)
    {
        StatusDef s = data.Statuses[z.Status];
        if (!SaysStatus(effect, s.DisplayName))
        {
            yield return ("Effect status", $"'{effect}' does not name '{s.DisplayName}' (data zone status '{s.Key}')");
            yield break;
        }
        if (s.Kind == StatusKind.Slow)
        {
            double wantPct = Math.Round(z.Magnitude * 100.0, 3);
            Match m = Regex.Match(effect, Regex.Escape(s.DisplayName) + @" (?:by )?(\d+(?:\.\d+)?) ?%", RegexOptions.CultureInvariant);
            if (!m.Success)
                yield return ("Effect magnitude", $"'{effect}' does not say '{s.DisplayName} {FactionPage.Text(wantPct)}%' (data magnitude {FactionPage.Text(Math.Round(z.Magnitude, 4))})");
            else if (Math.Abs(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - wantPct) > 1e-3)
                yield return ("Effect magnitude", $"page '{m.Value}', data magnitude {FactionPage.Text(Math.Round(z.Magnitude, 4))} ({FactionPage.Text(wantPct)}%)");
        }
        else if (s.Kind == StatusKind.DamageOverTime)
        {
            string want = $"{s.DisplayName}: {FactionPage.Text(z.Magnitude)} {data.DamageTable.DamageTypeKeys[s.DamageType]} damage/s";
            if (!effect.Contains(want, StringComparison.Ordinal))
                yield return ("Effect magnitude", $"'{effect}' does not say '{want}' (data magnitude {FactionPage.Text(z.Magnitude)})");
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
