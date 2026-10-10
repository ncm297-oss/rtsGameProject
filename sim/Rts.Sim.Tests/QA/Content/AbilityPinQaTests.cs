using System.Collections.Immutable;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D10b (BUG-0350): attacks on <c>AbilityContentTests.Compare</c>'s Cusser pin beyond the developer's mutants: reordered
/// columns, rows and Effect phrases must not invent problems; data mutants in the other direction still fail by name; two
/// friendly-fire wordings that disagree fail; a landed Sandstorm shaped like the sim branch's (an unknown zone effect kind
/// carrying the Blinded status, plus a Slowed status) and a landed Blinded status of an unknown kind report and pass.
/// </summary>
public class AbilityPinQaTests
{
    private static GameData Data => TestSim.Data;

    private static string[] Malazan() => PageTables.FactionLines("malazan");

    private static int Row(string[] lines, string name)
    {
        int row = Array.FindIndex(lines, l => l.StartsWith("| " + name + " |", StringComparison.Ordinal));
        Assert.True(row >= 0, $"malazan.md has no '{name}' row");
        return row;
    }

    private static AbilityContentTests.Result Compare(ImmutableArray<AbilityDef> abilities, string[] lines) =>
        AbilityContentTests.Compare(Data, abilities, "malazan", lines);

    private static AbilityDef Copy(AbilityDef a, ImmutableArray<AbilityEffect> effects, AbilityAffects affects) => new()
    {
        Id = a.Id, Key = a.Key, Faction = a.Faction, DisplayName = a.DisplayName, Description = a.Description, Kind = a.Kind,
        Range = a.Range, Radius = a.Radius, CastTicks = a.CastTicks, CooldownTicks = a.CooldownTicks, DurationTicks = a.DurationTicks,
        Affects = affects, Effects = effects, HitsBuildings = effects.Any(e => e.Kind == AbilityEffectKind.Damage && e.Buildings),
    };

    private static ImmutableArray<AbilityDef> WithCusser(Func<AbilityEffect, AbilityEffect> edit, AbilityAffects? affects = null)
    {
        AbilityDef a = Data.Abilities.Single(x => x.DisplayName == "Cusser");
        return Data.Abilities.SetItem(Data.Abilities.IndexOf(a), Copy(a, a.Effects.Select(edit).ToImmutableArray(), affects ?? a.Affects));
    }

    [Fact]
    public void TheRealPage_HasNoProblems_AsABaseline()
    {
        AbilityContentTests.Result r = Compare(Data.Abilities, Malazan());
        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
    }

    [Fact]
    public void ReorderedColumns_DoNotInventProblems()
    {
        // Reverse every column of the Abilities table (header, separator and rows).
        string[] lines = Malazan();
        int h = Array.FindIndex(lines, l => l == AbilityContentTests.Header);
        Assert.True(h >= 0);
        for (int i = h; i < lines.Length && lines[i].StartsWith('|'); i++)
            lines[i] = "| " + string.Join(" | ", PageTables.Cells(lines[i]).Reverse()) + " |";

        AbilityContentTests.Result r = Compare(Data.Abilities, lines);

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
    }

    [Fact]
    public void ReorderedColumns_StillCatchAStaleCusserAmount()
    {
        string[] lines = Malazan();
        int h = Array.FindIndex(lines, l => l == AbilityContentTests.Header);
        for (int i = h; i < lines.Length && lines[i].StartsWith('|'); i++)
            lines[i] = "| " + string.Join(" | ", PageTables.Cells(lines[i]).Reverse()) + " |";
        int row = Row(lines, "Enemy units in the area take 120 siege damage, full damage to buildings (≈355 to a Town Hall), friendly fire at 50%");
        lines[row] = lines[row].Replace("take 120 siege", "take 90 siege", StringComparison.Ordinal);

        AbilityContentTests.Result r = Compare(Data.Abilities, lines);

        Assert.Contains(r.Problems, p => p.Contains("'Cusser' Effect amount", StringComparison.Ordinal));
    }

    [Fact]
    public void SwappedRows_DoNotInventProblems()
    {
        string[] lines = Malazan();
        int telas = Row(lines, "Telas Fire"), cusser = Row(lines, "Cusser");
        (lines[telas], lines[cusser]) = (lines[cusser], lines[telas]);

        AbilityContentTests.Result r = Compare(Data.Abilities, lines);

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
    }

    [Theory]
    [InlineData("Friendly fire at 50%, full damage to buildings. Enemy units in the area take 120 siege damage")]
    [InlineData("Full damage to buildings; own units take half; enemy units in the area take 120 siege damage")]
    [InlineData("120 siege damage to enemy units in the area, full damage to buildings, own units in the blast take 50%")]
    public void ReorderedCusserEffectPhrases_DoNotInventProblems(string effect)
    {
        string[] lines = Malazan();
        int row = Row(lines, "Cusser");
        string[] cells = PageTables.Cells(lines[row]);
        cells[^1] = effect;
        lines[row] = "| " + string.Join(" | ", cells) + " |";

        AbilityContentTests.Result r = Compare(Data.Abilities, lines);

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
    }

    /// <summary>Data mutants in the direction the developer's set doesn't cover.</summary>
    [Theory]
    [InlineData("amount-down", "Effect amount")]
    [InlineData("type-pierce", "Effect type")]
    [InlineData("ff-full", "Effect friendlyFire")]
    [InlineData("ff-tiny", "Effect friendlyFire")]
    [InlineData("affects-own", "Effect affects")]
    public void AMutatedCusserField_OtherDirection_FailsByName(string mutant, string field)
    {
        int pierce = Data.DamageTable.DamageTypeKeys.IndexOf("pierce");
        ImmutableArray<AbilityDef> abilities = mutant switch
        {
            "amount-down" => WithCusser(e => e with { Amount = e.Amount - 20 }),
            "type-pierce" => WithCusser(e => e with { DamageType = pierce }),
            "ff-full" => WithCusser(e => e with { FriendlyFire = 1f }),
            "ff-tiny" => WithCusser(e => e with { FriendlyFire = 0.49f }),
            _ => WithCusser(e => e, AbilityAffects.OwnUnits),
        };

        AbilityContentTests.Result r = Compare(abilities, Malazan());

        Assert.True(r.Problems.Any(p => p.Contains("'Cusser' " + field, StringComparison.Ordinal)),
            $"no problem naming 'Cusser' {field}:\n" + string.Join("\n", r.Problems));
    }

    [Fact]
    public void TwoFriendlyFireWordingsThatDisagree_Fail()
    {
        string[] lines = Malazan();
        int row = Row(lines, "Cusser");
        lines[row] = lines[row].Replace("friendly fire at 50%", "friendly fire at 50%, own units take 25%", StringComparison.Ordinal);

        AbilityContentTests.Result r = Compare(Data.Abilities, lines);

        Assert.Contains(r.Problems, p => p.Contains("'Cusser' Effect friendlyFire", StringComparison.Ordinal) && p.Contains("25%", StringComparison.Ordinal));
    }

    [Fact]
    public void TelasFirePage_ClaimingFriendlyFire_Fails()
    {
        // Telas Fire has no damage effect (friendlyFire 0); a page that promises friendly fire is a value mismatch.
        string[] lines = Malazan();
        int row = Row(lines, "Telas Fire");
        lines[row] = lines[row].Replace("No effect on buildings", "No effect on buildings, friendly fire at 50%", StringComparison.Ordinal);

        AbilityContentTests.Result r = Compare(Data.Abilities, lines);

        Assert.Contains(r.Problems, p => p.Contains("'Telas Fire' Effect friendlyFire", StringComparison.Ordinal));
    }

    /// <summary>
    /// A second damage claim on the page with no matching damage effect (e.g. "plus 30 fire damage" left over from an old
    /// design) should fail; the pin only checks that every data effect has a claim, not the reverse.
    /// </summary>
    [Fact(Skip = "BUG-0380: a page damage claim with no matching damage effect passes when the ability has another damage effect")]
    public void AnExtraPageDamageClaim_WithNoDataEffect_Fails()
    {
        string[] lines = Malazan();
        int row = Row(lines, "Cusser");
        lines[row] = lines[row].Replace("take 120 siege damage", "take 120 siege damage plus 30 magic damage", StringComparison.Ordinal);

        AbilityContentTests.Result r = Compare(Data.Abilities, lines);

        Assert.True(r.Problems.Any(p => p.Contains("'Cusser' Effect", StringComparison.Ordinal) && p.Contains("30 magic damage", StringComparison.Ordinal)),
            "an extra '30 magic damage' claim on the page passed:\n" + string.Join("\n", r.Problems));
    }

    /// <summary>
    /// D10b criterion 3, shaped like the sim branch's landing: Sandstorm with a zone effect of a kind this build doesn't
    /// know (stands in for <c>createZone</c>) plus the statuses it applies, and Blinded with an unknown status kind (stands
    /// in for <c>blind</c>). Both pins must report, not fail, and must not throw.
    /// </summary>
    [Fact]
    public void ALandedSandstormAndBlinded_ShapedLikeTheSimBranch_ReportAndPass()
    {
        var blinded = new StatusDef { Id = Data.Statuses.Length, Key = "blinded", DisplayName = "Blinded", Description = "Can barely see.", Kind = (StatusKind)2 };
        ImmutableArray<StatusDef> statuses = Data.Statuses.Add(blinded);
        int slowed = Data.Statuses.Single(s => s.Key == "slowed").Id;
        var sandstorm = new AbilityDef
        {
            Id = Data.Abilities.Length,
            Key = "sandstorm",
            Faction = Data.FindFaction("whirlwind"),
            DisplayName = "Sandstorm",
            Description = "A storm of sand.",
            Kind = AbilityKind.TargetGround,
            Range = 18,
            Radius = 6,
            CastTicks = 24,
            CooldownTicks = 45 * SimConstants.TicksPerSecond,
            DurationTicks = 12 * SimConstants.TicksPerSecond,
            Affects = AbilityAffects.EnemyUnits,
            Effects = ImmutableArray.Create(
                new AbilityEffect { Kind = (AbilityEffectKind)2, DamageType = -1, Status = blinded.Id, DurationTicks = 12 * SimConstants.TicksPerSecond },
                new AbilityEffect { Kind = AbilityEffectKind.ApplyStatus, DamageType = -1, Status = blinded.Id, DurationTicks = SimConstants.TicksPerSecond },
                new AbilityEffect { Kind = AbilityEffectKind.ApplyStatus, DamageType = -1, Status = slowed, Magnitude = 0.3f, DurationTicks = SimConstants.TicksPerSecond }),
        };
        ImmutableArray<AbilityDef> abilities = Data.Abilities.Add(sandstorm);
        // Compare reads statuses from GameData (the real load), which has no Blinded here; once the sim lands it the loaded
        // GameData carries it. So the ability-side check drops the in-memory applyStatus(blinded) effect.
        var sandstormForPage = new AbilityDef
        {
            Id = sandstorm.Id, Key = sandstorm.Key, Faction = sandstorm.Faction, DisplayName = sandstorm.DisplayName,
            Description = sandstorm.Description, Kind = sandstorm.Kind, Range = sandstorm.Range, Radius = sandstorm.Radius,
            CastTicks = sandstorm.CastTicks, CooldownTicks = sandstorm.CooldownTicks, DurationTicks = sandstorm.DurationTicks,
            Affects = sandstorm.Affects, Effects = ImmutableArray.Create(sandstorm.Effects[0], sandstorm.Effects[2]),
        };
        ImmutableArray<AbilityDef> pageAbilities = Data.Abilities.Add(sandstormForPage);

        AbilityContentTests.Result a = AbilityContentTests.Compare(Data, pageAbilities, "whirlwind", PageTables.FactionLines("whirlwind"));
        AbilityContentTests.Result m = AbilityContentTests.Compare(Data, pageAbilities, "malazan", Malazan());
        AbilityContentTests.Result s = AbilityContentTests.CompareStatuses(Data, statuses, abilities, FactionPage.DocTable("docs/02-game-design.md", "Status effects", level: 3));

        Assert.True(a.Problems.Count + m.Problems.Count + s.Problems.Count == 0, string.Join("\n", a.Problems.Concat(m.Problems).Concat(s.Problems)));
        Assert.Contains(a.Reports, l => l.Contains("Sandstorm", StringComparison.Ordinal) && l.Contains("landed", StringComparison.Ordinal));
        Assert.Contains(s.Reports, l => l.Contains("Blinded", StringComparison.Ordinal) && l.Contains("landed", StringComparison.Ordinal));
    }
}
