using System.Collections.Immutable;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D10b (BUG-0350): attacks on <c>AbilityContentTests.Compare</c>'s Cusser pin beyond the developer's mutants: reordered
/// columns, rows and Effect phrases must not invent problems; data mutants in the other direction still fail by name; two
/// friendly-fire wordings that disagree fail; every page damage claim is consumed by a data effect (BUG-0380); the landed
/// Sandstorm and Blinded are pinned, not pending (D10c).
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
    /// design) fails: since BUG-0380 every page claim must be consumed by a data effect, not only the reverse.
    /// </summary>
    [Fact]
    public void AnExtraPageDamageClaim_WithNoDataEffect_Fails()
    {
        string[] lines = Malazan();
        int row = Row(lines, "Cusser");
        lines[row] = lines[row].Replace("take 120 siege damage", "take 120 siege damage plus 30 magic damage", StringComparison.Ordinal);

        AbilityContentTests.Result r = Compare(Data.Abilities, lines);

        Assert.True(r.Problems.Any(p => p.Contains("'Cusser' Effect", StringComparison.Ordinal) && p.Contains("30 magic damage", StringComparison.Ordinal)),
            "an extra '30 magic damage' claim on the page passed:\n" + string.Join("\n", r.Problems));
    }

    /// <summary>BUG-0380: with a second damage effect in memory, the same two claims both match and pass.</summary>
    [Fact]
    public void TwoPageDamageClaims_MatchingTwoDataEffects_Pass()
    {
        string[] lines = Malazan();
        int row = Row(lines, "Cusser");
        lines[row] = lines[row].Replace("take 120 siege damage", "take 120 siege damage plus 30 magic damage", StringComparison.Ordinal);
        int magic = Data.DamageTable.DamageTypeKeys.IndexOf("magic");
        AbilityDef a = Data.Abilities.Single(x => x.DisplayName == "Cusser");
        ImmutableArray<AbilityDef> abilities = Data.Abilities.SetItem(Data.Abilities.IndexOf(a),
            Copy(a, a.Effects.Add(new AbilityEffect { Kind = AbilityEffectKind.Damage, DamageType = magic, Amount = 30, Status = -1 }), a.Affects));

        AbilityContentTests.Result r = Compare(abilities, lines);

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
    }

    /// <summary>BUG-0380: one claim can't stand for two effects; a page with one "120 siege damage" for two such effects fails.</summary>
    [Fact]
    public void OneClaimForTwoEqualDataEffects_Fails()
    {
        AbilityDef a = Data.Abilities.Single(x => x.DisplayName == "Cusser");
        ImmutableArray<AbilityDef> abilities = Data.Abilities.SetItem(Data.Abilities.IndexOf(a), Copy(a, a.Effects.AddRange(a.Effects), a.Affects));

        AbilityContentTests.Result r = Compare(abilities, Malazan());

        Assert.Contains(r.Problems, p => p.Contains("'Cusser' Effect amount", StringComparison.Ordinal));
    }

    /// <summary>A repeated claim with a single effect is a second, unconsumed claim and fails too.</summary>
    [Fact]
    public void ARepeatedPageClaim_ForOneDataEffect_Fails()
    {
        string[] lines = Malazan();
        int row = Row(lines, "Cusser");
        lines[row] = lines[row].Replace("friendly fire at 50%", "friendly fire at 50%; then 120 siege damage again", StringComparison.Ordinal);

        AbilityContentTests.Result r = Compare(Data.Abilities, lines);

        Assert.Contains(r.Problems, p => p.Contains("'Cusser' Effect amount", StringComparison.Ordinal) && p.Contains("no data damage effect", StringComparison.Ordinal));
    }

    /// <summary>
    /// D10b criterion 3 became D10c's pin: the loaded Sandstorm and Blinded are out of the allowances and both pins pass on
    /// the real pages, with no report line left naming either as pending or landed.
    /// </summary>
    [Fact]
    public void TheLandedSandstormAndBlinded_ArePinned_AndPass()
    {
        Assert.Empty(AbilityContentTests.PendingAbilities);
        Assert.DoesNotContain("Blinded", AbilityContentTests.PendingStatuses);

        AbilityContentTests.Result a = AbilityContentTests.Compare(Data, Data.Abilities, "whirlwind", PageTables.FactionLines("whirlwind"));
        AbilityContentTests.Result s = AbilityContentTests.CompareStatuses(Data, Data.Statuses, Data.Abilities, FactionPage.DocTable("docs/02-game-design.md", "Status effects", level: 3));

        Assert.True(a.Problems.Count + s.Problems.Count == 0, string.Join("\n", a.Problems.Concat(s.Problems)));
        Assert.DoesNotContain(a.Reports, l => l.Contains("Sandstorm", StringComparison.Ordinal));
        Assert.DoesNotContain(s.Reports, l => l.Contains("Blinded", StringComparison.Ordinal));
    }
}
