using System.Collections.Immutable;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D11: attacks on the Zealot passive pin (<see cref="PassiveContentTests"/>), the Frenzied row / description pin and the
/// new Blinded wording, beyond the developer's mutants: small drifts (one tick, a tenth of a metre, a percent point), the
/// shipped files all clean, every description role on the numberless Frenzied, and a second same-trigger page sentence.
/// </summary>
public class ZealotPassivePinQaTests
{
    private static GameData Data => TestSim.Data;

    [Fact]
    public void ShippedPagesAndData_AreClean_ForPassivesStatusesAndDescriptions()
    {
        foreach (string f in new[] { "malazan", "whirlwind" })
        {
            AbilityContentTests.Result r = PassiveContentTests.Compare(Data, f, PageTables.FactionLines(f));
            Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
        }
        AbilityContentTests.Result s = AbilityContentTests.CompareStatuses(Data, Data.Statuses, Data.Abilities,
            FactionPage.DocTable("docs/02-game-design.md", "Status effects", level: 3));
        Assert.True(s.Problems.Count == 0, string.Join("\n", s.Problems));
        Assert.DoesNotContain(s.Reports, l => l.Contains("renzied", StringComparison.Ordinal));
        Assert.Empty(AbilityContentTests.DescriptionProblems(Data, Data.Abilities, Data.Statuses));
        Assert.Contains("can't attack anything more than 3 m away", Data.Statuses.Single(x => x.Key == "blinded").Description);
        Assert.DoesNotMatch(@"\d", Data.Statuses.Single(x => x.Key == "frenzied").Description);
    }

    /// <summary>Small page drifts, each on a single number, fail naming the passive and the field.</summary>
    [Theory]
    [InlineData("Frenzy of the Apocalypse", "hpFraction", "below 50% HP", "below 51% HP")]
    [InlineData("Frenzy of the Apocalypse", "hpFraction", "below 50% HP", "below 5% HP")]
    [InlineData("Frenzy of the Apocalypse", "magnitude", "HP gains Frenzied (+50% attack speed)", "HP gains Frenzied (+51% attack speed)")]
    [InlineData("Frenzy of the Apocalypse", "radius", "(+50% attack speed). Passive", "(+50% attack speed) within 6 m. Passive")]
    [InlineData("Martyrdom", "duration", "for 5 s", "for 5.5 s")]
    [InlineData("Martyrdom", "duration", "for 5 s", "for 50 s")]
    [InlineData("Martyrdom", "radius", "within 6 m", "within 6.5 m")]
    [InlineData("Martyrdom", "radius", "within 6 m", "within 60 m")]
    [InlineData("Martyrdom", "magnitude", "gain Frenzied (+50% attack speed)", "gain Frenzied (+5% attack speed)")]
    [InlineData("Martyrdom", "units", "Zealots within", "allied units within")]
    public void ASmallPageDrift_FailsNamingThePassiveAndField(string passive, string field, string text, string mutant)
    {
        string[] lines = PageTables.FactionLines("whirlwind");
        int hit = Array.FindIndex(lines, l => l.Contains(text, StringComparison.Ordinal));
        Assert.True(hit >= 0, text);
        lines[hit] = lines[hit].Replace(text, mutant, StringComparison.Ordinal);

        AbilityContentTests.Result r = PassiveContentTests.Compare(Data, "whirlwind", lines);

        string want = $"'{passive}' {field}:";
        Assert.True(r.Problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem naming {want}\n" + string.Join("\n", r.Problems));
    }

    /// <summary>Small data drifts in memory (a tick, a tenth of a metre, a percent point) fail naming the passive and field.</summary>
    [Theory]
    [InlineData("Frenzy of the Apocalypse", "hpFraction", "hp-0.51")]
    [InlineData("Frenzy of the Apocalypse", "magnitude", "below-mag-0.51")]
    [InlineData("Martyrdom", "duration", "death-ticks-plus-1")]
    [InlineData("Martyrdom", "radius", "death-radius-6.1")]
    [InlineData("Martyrdom", "magnitude", "death-mag-0.49")]
    [InlineData("Martyrdom", "units", "death-units-empty")]
    public void ASmallDataDrift_FailsNamingThePassiveAndField(string passive, string field, string mutant)
    {
        UnitDef zealot = Data.Units[Data.FindUnit("whirlwind_zealot")];
        ImmutableArray<UnitPassive> passives = zealot.Passives.Select(p => (p.Trigger, mutant) switch
        {
            (PassiveTrigger.WhenBelowHp, "hp-0.51") => p with { HpFraction = 0.51f },
            (PassiveTrigger.WhenBelowHp, "below-mag-0.51") => p with { Magnitude = 0.51f },
            (PassiveTrigger.OnDeath, "death-ticks-plus-1") => p with { DurationTicks = p.DurationTicks + 1 },
            (PassiveTrigger.OnDeath, "death-radius-6.1") => p with { Radius = 6.1f },
            (PassiveTrigger.OnDeath, "death-mag-0.49") => p with { Magnitude = 0.49f },
            (PassiveTrigger.OnDeath, "death-units-empty") => p with { Units = ImmutableArray<int>.Empty },
            _ => p,
        }).ToImmutableArray();

        AbilityContentTests.Result r = PassiveContentTests.Compare(Data, "whirlwind", PageTables.FactionLines("whirlwind"),
            passivesOf: u => u.Id == zealot.Id ? passives : u.Passives);

        string want = $"'{passive}' {field}:";
        Assert.True(r.Problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem naming {want}\n" + string.Join("\n", r.Problems));
    }

    /// <summary>
    /// The Frenzied description is numberless on purpose (each source has its own strength): a number appended in any role
    /// fails naming frenzied, as Burning and Slowed do (DescriptionRoleQaTests).
    /// </summary>
    [Theory]
    [InlineData("10 seconds")]
    [InlineData("lingers 10 s")]
    [InlineData("10 s cooldown")]
    [InlineData("10 s to cast")]
    [InlineData("10 m")]
    [InlineData("50%")]
    [InlineData("10 magic damage")]
    [InlineData("10 magic damage a second")]
    public void ANumberAddedToFrenzied_FailsInEveryRole(string extra)
    {
        StatusDef f = Data.Statuses.Single(x => x.Key == "frenzied");
        (ImmutableArray<AbilityDef> abilities, ImmutableArray<StatusDef> statuses) =
            AbilityContentTests.WithDescription("frenzied", f.Description, f.Description + " " + extra + ".");

        List<string> problems = AbilityContentTests.DescriptionProblems(Data, abilities, statuses);

        Assert.Contains(problems, p => p.StartsWith("frenzied description", StringComparison.Ordinal));
    }

    /// <summary>The new Blinded wording keeps both numbers pinned: either drifting, or put in a non-distance role, fails.</summary>
    [Theory]
    [InlineData("can't attack anything more than 3 m away", "can't attack anything more than 4 m away")]
    [InlineData("sight drops to 2 m", "sight drops to 1 m")]
    [InlineData("can't attack anything more than 3 m away", "can't attack anything for 3 seconds")]
    public void TheNewBlindedWording_StillPinsItsNumbers(string text, string mutant)
    {
        (ImmutableArray<AbilityDef> abilities, ImmutableArray<StatusDef> statuses) = AbilityContentTests.WithDescription("blinded", text, mutant);

        List<string> problems = AbilityContentTests.DescriptionProblems(Data, abilities, statuses);

        Assert.Contains(problems, p => p.StartsWith("blinded description", StringComparison.Ordinal));
    }

    /// <summary>
    /// BUG-0470: a second page sentence with the same trigger for the same unit (here a contradicting "Last Rites" with other
    /// numbers) passes, because page -> data only asks that the unit has a passive of that trigger and data -> page compares
    /// only the first matching sentence. The data allows one passive per trigger, so two sentences can't both be true.
    /// </summary>
    [Fact(Skip = "BUG-0470: a second same-trigger page passive sentence is never compared")]
    public void ASecondSameTriggerPageSentence_WithOtherNumbers_Fails()
    {
        string[] lines = PageTables.FactionLines("whirlwind");
        int hit = Array.FindIndex(lines, l => l.Contains("Zealots within 6 m gain Frenzied (+50% attack speed) for 5 s.", StringComparison.Ordinal));
        Assert.True(hit >= 0);
        lines[hit] += " Passive **Last Rites**: when a Zealot dies, Zealots within 12 m gain Frenzied (+90% attack speed) for 9 s.";

        AbilityContentTests.Result r = PassiveContentTests.Compare(Data, "whirlwind", lines);

        Assert.Contains(r.Problems, p => p.Contains("'Last Rites'", StringComparison.Ordinal));
    }
}
