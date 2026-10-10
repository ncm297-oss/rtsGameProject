using System.Collections.Immutable;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D10c: attacks on the Sandstorm / Blinded pins and the BUG-0380 claim consumption beyond the developer's mutants:
/// re-ordered zone phrases, "Non-Whirlwind" vs "Enemy units", the Slowed typical source, Blinded data / page mutants in
/// other shapes, damage claims before and after a matching claim with one and two damage effects in memory, and the
/// description pin's number check.
/// </summary>
public class SandstormBlindedPinQaTests
{
    private static GameData Data => TestSim.Data;
    private const string StatusDoc = "docs/02-game-design.md";

    private static string[] Whirlwind() => PageTables.FactionLines("whirlwind");

    private static int SandstormRow(string[] lines)
    {
        int row = Array.FindIndex(lines, l => l.StartsWith("| Sandstorm |", StringComparison.Ordinal));
        Assert.True(row >= 0, "whirlwind.md has no Sandstorm row");
        return row;
    }

    private static string[] WithSandstormEffect(string effect)
    {
        string[] lines = Whirlwind();
        int row = SandstormRow(lines);
        string[] cells = PageTables.Cells(lines[row]);
        cells[^1] = effect;
        lines[row] = "| " + string.Join(" | ", cells) + " |";
        return lines;
    }

    private static AbilityContentTests.Result Sandstorm(string[] lines, ImmutableArray<AbilityDef>? abilities = null) =>
        AbilityContentTests.Compare(Data, abilities ?? Data.Abilities, "whirlwind", lines);

    private static string Join(AbilityContentTests.Result r) => string.Join("\n", r.Problems);

    [Fact]
    public void TheRealWhirlwindPage_AndStatusRows_HaveNoProblems()
    {
        AbilityContentTests.Result a = Sandstorm(Whirlwind());
        AbilityContentTests.Result s = AbilityContentTests.CompareStatuses(Data, Data.Statuses, Data.Abilities,
            FactionPage.DocTable(StatusDoc, "Status effects", level: 3));
        Assert.True(a.Problems.Count + s.Problems.Count == 0, Join(a) + "\n" + Join(s));
        Assert.Empty(a.Reports);
    }

    /// <summary>Phrases re-ordered, "Your own units" first, "Slowed by 30 %" with a space: still a pass.</summary>
    [Theory]
    [InlineData("Your own units are unaffected. No effect on buildings. Enemies outside can't see into the storm. Enemy units inside are Slowed by 30 % and Blinded")]
    [InlineData("Enemy units inside are Blinded and Slowed 30%; enemies outside cannot see in; no effect on buildings")]
    [InlineData("Enemy units inside are Slowed 30% and Blinded. Enemies outside can’t see into the storm. No effect on buildings")]
    public void ReorderedSandstormPhrases_Pass(string effect)
    {
        AbilityContentTests.Result r = Sandstorm(WithSandstormEffect(effect));
        Assert.True(r.Problems.Count == 0, Join(r));
    }

    /// <summary>The old "Non-Whirlwind units" wording, wherever it sits, fails on Effect affects (Producer decision (a) of M4-4b-2).</summary>
    [Theory]
    [InlineData("Non-Whirlwind units inside are Blinded and Slowed 30%. Enemies outside can't see into the storm. Whirlwind units are unaffected. No effect on buildings")]
    [InlineData("Blinded and Slowed 30%: non-Whirlwind units inside. Enemies outside can't see into the storm. No effect on buildings")]
    public void TheNonWhirlwindWording_FailsOnAffects(string effect)
    {
        AbilityContentTests.Result r = Sandstorm(WithSandstormEffect(effect));
        Assert.Contains(r.Problems, p => p.Contains("'Sandstorm' Effect affects", StringComparison.Ordinal));
    }

    /// <summary>A data mutant in each direction against the re-ordered page: the reordering doesn't hide a field.</summary>
    [Theory]
    [InlineData("magnitude", "Effect magnitude")]
    [InlineData("noBlock", "Effect blocksVision")]
    [InlineData("affectsOwn", "Effect affects")]
    [InlineData("dropBlinded", "Effect status")]
    public void ADataMutant_AgainstAReorderedPage_StillFails(string mutant, string column)
    {
        string[] lines = WithSandstormEffect("No effect on buildings. Enemies outside cannot see in; enemy units inside are Slowed by 30% and Blinded");
        int blinded = Data.Statuses.Single(s => s.Key == "blinded").Id;
        ImmutableArray<AbilityDef> abilities = AbilityContentTests.WithSandstorm(a => mutant switch
        {
            "affectsOwn" => AbilityContentTests.CopyAll(a, affects: AbilityAffects.OwnUnits),
            _ => AbilityContentTests.CopyAll(a, effects: a.Effects.Select(e => e.Kind != AbilityEffectKind.CreateZone ? e : mutant switch
            {
                "magnitude" => e with { ZoneStatuses = e.ZoneStatuses.Select(z => z.Status == blinded ? z : z with { Magnitude = 0.25f }).ToImmutableArray() },
                "noBlock" => e with { BlocksVision = false },
                _ => e with { ZoneStatuses = e.ZoneStatuses.RemoveAll(z => z.Status == blinded) },
            }).ToImmutableArray()),
        });

        AbilityContentTests.Result r = Sandstorm(lines, abilities);

        Assert.True(r.Problems.Any(p => p.Contains("'Sandstorm' " + column, StringComparison.Ordinal)), Join(r));
    }

    /// <summary>The typical-source check reads zone statuses for Slowed too, not only Blinded.</summary>
    [Fact]
    public void ASandstormMissingFromSlowedsTypicalSources_Fails()
    {
        string[][] rows = FactionPage.DocTable(StatusDoc, "Status effects", level: 3);
        string[] row = rows.Single(c => c[0] == "Slowed");
        row[2] = row[2].Replace("Sandstorm, ", "", StringComparison.Ordinal);

        AbilityContentTests.Result r = AbilityContentTests.CompareStatuses(Data, Data.Statuses, Data.Abilities, rows);

        Assert.Contains(r.Problems, p => p.Contains("'Slowed' Typical source", StringComparison.Ordinal) && p.Contains("'Sandstorm'", StringComparison.Ordinal));
    }

    /// <summary>Blinded's sight and reach swapped in data fails on both fields; a Blinded whose kind became slow fails on Effect.</summary>
    [Fact]
    public void BlindedSwappedOrRekinded_Fails()
    {
        StatusDef b = Data.Statuses.Single(s => s.Key == "blinded");
        string[][] rows = FactionPage.DocTable(StatusDoc, "Status effects", level: 3);
        StatusDef With(StatusKind kind, float sight, float reach) => new()
        {
            Id = b.Id, Key = b.Key, DisplayName = b.DisplayName, Description = b.Description, Kind = kind, DamageType = b.DamageType, Sight = sight, Reach = reach,
        };

        AbilityContentTests.Result swapped = AbilityContentTests.CompareStatuses(Data, Data.Statuses.SetItem(b.Id, With(StatusKind.Blind, b.Reach, b.Sight)), Data.Abilities, rows);
        AbilityContentTests.Result rekinded = AbilityContentTests.CompareStatuses(Data, Data.Statuses.SetItem(b.Id, With(StatusKind.Slow, 0, 0)), Data.Abilities, rows);

        Assert.Contains(swapped.Problems, p => p.Contains("(Blinded) sight:", StringComparison.Ordinal));
        Assert.Contains(swapped.Problems, p => p.Contains("(Blinded) reach:", StringComparison.Ordinal));
        Assert.Contains(rekinded.Problems, p => p.Contains("'blinded' Effect", StringComparison.Ordinal));
    }

    /// <summary>A docs/02 Blinded row that lost its row, or a Blinded back in the pending list, can't silently pass.</summary>
    [Fact]
    public void ABlindedWithNoDocs02Row_Fails()
    {
        string[][] rows = FactionPage.DocTable(StatusDoc, "Status effects", level: 3).Where(c => c[0] != "Blinded").ToArray();
        AbilityContentTests.Result r = AbilityContentTests.CompareStatuses(Data, Data.Statuses, Data.Abilities, rows);
        Assert.Contains(r.Problems, p => p.Contains("'blinded' (Blinded): no docs/02", StringComparison.Ordinal));
    }

    // ---- BUG-0380: claims before / after a matching claim, one and two damage effects in memory ----

    private static string[] Malazan() => PageTables.FactionLines("malazan");

    private static int CusserRow(string[] lines) => Array.FindIndex(lines, l => l.StartsWith("| Cusser |", StringComparison.Ordinal));

    private static ImmutableArray<AbilityDef> CusserPlusMagic30()
    {
        AbilityDef a = Data.Abilities.Single(x => x.DisplayName == "Cusser");
        int magic = Data.DamageTable.DamageTypeKeys.IndexOf("magic");
        return Data.Abilities.SetItem(Data.Abilities.IndexOf(a), AbilityContentTests.CopyAll(a,
            effects: a.Effects.Add(new AbilityEffect { Kind = AbilityEffectKind.Damage, DamageType = magic, Amount = 30, Status = -1 })));
    }

    [Theory]
    [InlineData("take 120 siege damage", "take 30 magic damage, then 120 siege damage")]   // extra claim before the match
    [InlineData("take 120 siege damage", "take 120 siege damage, then 30 magic damage")]   // extra claim after the match
    public void AnExtraClaim_BeforeOrAfterTheMatchingOne_Fails(string cell, string mutant)
    {
        string[] lines = Malazan();
        int row = CusserRow(lines);
        lines[row] = lines[row].Replace(cell, mutant, StringComparison.Ordinal);

        AbilityContentTests.Result r = AbilityContentTests.Compare(Data, Data.Abilities, "malazan", lines);

        Assert.Contains(r.Problems, p => p.Contains("'Cusser' Effect amount", StringComparison.Ordinal) && p.Contains("'30 magic damage'", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Problems, p => p.Contains("'Cusser' Effect type", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("take 30 magic damage and 120 siege damage", true)]       // both claims, reversed order
    [InlineData("take 120 siege damage", false)]                          // one claim for two effects
    [InlineData("take 120 magic damage and 30 siege damage", false)]      // amounts crossed
    [InlineData("take 120 siege damage and 30 fire damage", false)]       // wrong type on the second
    public void TwoDamageEffectsInMemory_MatchOnlyTheirOwnClaims(string mutant, bool passes)
    {
        string[] lines = Malazan();
        int row = CusserRow(lines);
        lines[row] = lines[row].Replace("take 120 siege damage", mutant, StringComparison.Ordinal);

        AbilityContentTests.Result r = AbilityContentTests.Compare(Data, CusserPlusMagic30(), "malazan", lines);

        if (passes) Assert.True(r.Problems.Count == 0, Join(r));
        else Assert.Contains(r.Problems, p => p.Contains("'Cusser' Effect", StringComparison.Ordinal));
    }

    /// <summary>A damage claim on an ability with no damage effect at all (the zone) is reported (BUG-0380 for zero effects).</summary>
    [Fact]
    public void ADamageClaimOnSandstorm_Fails()
    {
        string[] lines = WithSandstormEffect("Enemy units inside are Blinded and Slowed 30% and take 5 magic damage. Enemies outside can't see into the storm. No effect on buildings");
        AbilityContentTests.Result r = Sandstorm(lines);
        Assert.Contains(r.Problems, p => p.Contains("'Sandstorm' Effect amount", StringComparison.Ordinal) && p.Contains("5 magic damage", StringComparison.Ordinal));
    }

    // ---- Description numbers (D10c's DescriptionProblems) ----

    private static List<string> SandstormDescription(string from, string to)
    {
        AbilityDef a = Data.Abilities.Single(x => x.Key == "sandstorm");
        Assert.Contains(from, a.Description, StringComparison.Ordinal);
        AbilityDef c = AbilityContentTests.CopyAll(a);
        var mutated = new AbilityDef
        {
            Id = c.Id, Key = c.Key, Faction = c.Faction, DisplayName = c.DisplayName, Description = a.Description.Replace(from, to, StringComparison.Ordinal),
            Kind = c.Kind, Range = c.Range, Radius = c.Radius, CastTicks = c.CastTicks, CooldownTicks = c.CooldownTicks,
            DurationTicks = c.DurationTicks, Affects = c.Affects, Effects = c.Effects, HitsBuildings = c.HitsBuildings, ZoneEffect = c.ZoneEffect,
        };
        return AbilityContentTests.DescriptionProblems(Data, Data.Abilities.SetItem(Data.Abilities.IndexOf(a), mutated), Data.Statuses);
    }

    [Fact]
    public void ANumberNotInData_InTheSandstormDescription_Fails()
    {
        List<string> problems = SandstormDescription("for 12 seconds", "for 13 seconds");
        Assert.Contains(problems, p => p.StartsWith("sandstorm description", StringComparison.Ordinal));
    }

    /// <summary>
    /// BUG-0410: the description pin checks a bag of numbers, so a stale duration that happens to equal another data number
    /// (Sandstorm's 18 m range, 45 s cooldown, the 30% slow) passes: "for 18 seconds" or "Slowed by 45%" are not caught.
    /// </summary>
    [Theory(Skip = "BUG-0410: description numbers are matched against any data number, not the one their words name")]
    [InlineData("for 12 seconds", "for 18 seconds")]
    [InlineData("for 12 seconds", "for 45 seconds")]
    [InlineData("Slowed by 30%", "Slowed by 45%")]
    [InlineData("Slowed by 30%", "Slowed by 12%")]
    public void ADescriptionNumber_InTheWrongRole_Fails(string from, string to)
    {
        List<string> problems = SandstormDescription(from, to);
        Assert.Contains(problems, p => p.StartsWith("sandstorm description", StringComparison.Ordinal));
    }
}
