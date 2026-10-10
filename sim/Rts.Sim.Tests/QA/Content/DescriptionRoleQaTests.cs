using System.Collections.Immutable;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D-H2 (BUG-0410): attacks on the role-aware description number check beyond the developer's mutants. Every shipped
/// description number is rewritten into every other role with the same value; a number whose value is legitimately in two
/// roles still passes; the zone status linger vs the zone's own life; numberless descriptions; a pending status of an
/// unknown kind stating numbers.
/// </summary>
public class DescriptionRoleQaTests
{
    private static GameData Data => TestSim.Data;

    /// <summary>How each role is written around a number, and the role text the problem must name.</summary>
    private static readonly (string Role, string Template, string Says)[] Forms =
    {
        ("duration", "{0} seconds", "reads as a duration,"),
        ("lingering", "lingers {0} s", "reads as a lingering duration"),
        ("cooldown", "{0} s cooldown", "reads as a cooldown"),
        ("cast", "{0} s to cast", "reads as a cast time"),
        ("distance", "{0} m", "reads as a distance"),
        ("percent", "{0}%", "reads as a percentage"),
        ("damage", "{0} magic damage", "reads as a damage amount"),
        ("rate", "{0} magic damage a second", "reads as a damage per second"),
    };

    /// <summary>Every number in a shipped ability or status description: entry, exact text, value, the role it has now.</summary>
    public static TheoryData<string, string, string, string, string> ShippedNumberInOtherRoles()
    {
        var shipped = new (string Key, string Text, string N, string Role)[]
        {
            ("telas_fire", "10 magic damage a second", "10", "rate"),
            ("telas_fire", "4 seconds", "4", "duration"),
            ("cusser", "120 siege damage", "120", "damage"),
            ("sandstorm", "12 seconds", "12", "duration"),
            ("sandstorm", "30%", "30", "percent"),
            ("blinded", "2 m", "2", "distance"),
            ("blinded", "3 m", "3", "distance"),
        };
        var rows = new TheoryData<string, string, string, string, string>();
        foreach (var s in shipped)
            foreach (var f in Forms)
                if (f.Role != s.Role) rows.Add(s.Key, s.Text, string.Format(f.Template, s.N), f.Role, f.Says);
        return rows;
    }

    /// <summary>
    /// The same number in any other role fails naming the entry, the number and that role (no shipped number's value is
    /// also a data number of another role of its entry, checked by the guard).
    /// </summary>
    [Theory]
    [MemberData(nameof(ShippedNumberInOtherRoles))]
    public void AShippedNumber_RewrittenIntoAnotherRole_Fails(string key, string text, string mutant, string role, string says)
    {
        (ImmutableArray<AbilityDef> abilities, ImmutableArray<StatusDef> statuses) = AbilityContentTests.WithDescription(key, text, mutant);

        List<string> problems = AbilityContentTests.DescriptionProblems(Data, abilities, statuses);

        Assert.True(problems.Any(p => p.StartsWith(key + " description", StringComparison.Ordinal) && p.Contains(says, StringComparison.Ordinal)),
            $"'{key}' '{text}' -> '{mutant}' ({role}): no problem saying '{says}':\n" + string.Join("\n", problems));
    }

    [Fact]
    public void ShippedDescriptions_HaveNoProblems_AndBurningAndSlowedStateNoNumbers()
    {
        Assert.Empty(AbilityContentTests.DescriptionProblems(Data, Data.Abilities, Data.Statuses));
        Assert.DoesNotMatch(@"\d", Data.Statuses.Single(s => s.Key == "burning").Description);
        Assert.DoesNotMatch(@"\d", Data.Statuses.Single(s => s.Key == "slowed").Description);
    }

    /// <summary>Burning and Slowed have no numbers of their own: any number appended to them, in any role, fails.</summary>
    [Theory]
    [InlineData("burning")]
    [InlineData("slowed")]
    public void ANumberAddedToANumberlessStatus_FailsInEveryRole(string key)
    {
        StatusDef s = Data.Statuses.Single(x => x.Key == key);
        foreach (var f in Forms)
        {
            string extra = " " + string.Format(f.Template, "10") + ".";
            (ImmutableArray<AbilityDef> abilities, ImmutableArray<StatusDef> statuses) =
                AbilityContentTests.WithDescription(key, s.Description, s.Description + extra);
            List<string> problems = AbilityContentTests.DescriptionProblems(Data, abilities, statuses);
            Assert.True(problems.Any(p => p.StartsWith(key + " description", StringComparison.Ordinal) && p.Contains(f.Says, StringComparison.Ordinal)),
                $"{key} + '{extra}': " + string.Join("\n", problems));
        }
    }

    /// <summary>A description with every number taken out states nothing wrong: the check pins stated numbers only.</summary>
    [Theory]
    [InlineData("sandstorm", "Calls up a sandstorm for 12 seconds: enemy units inside are Blinded and Slowed by 30%,", "Calls up a sandstorm: enemy units inside are Blinded and Slowed,")]
    [InlineData("cusser", "120 siege damage", "siege damage")]
    [InlineData("blinded", "sight drops to 2 m, and it can't target anything more than 3 m away", "sight drops, and it can't target far")]
    public void ADescriptionWithNoNumbers_Passes(string key, string text, string mutant)
    {
        (ImmutableArray<AbilityDef> abilities, ImmutableArray<StatusDef> statuses) = AbilityContentTests.WithDescription(key, text, mutant);
        List<string> problems = AbilityContentTests.DescriptionProblems(Data, abilities, statuses);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static ImmutableArray<AbilityDef> SandstormWith(int? cooldownTicks = null, int? castTicks = null, float? range = null)
    {
        AbilityDef a = Data.Abilities.Single(x => x.Key == "sandstorm");
        return Data.Abilities.SetItem(Data.Abilities.IndexOf(a), AbilityContentTests.CopyAll(a, cooldownTicks: cooldownTicks, castTicks: castTicks, range: range));
    }

    /// <summary>
    /// A value that is legitimately two roles' number (in memory: Sandstorm's cooldown = its 12 s duration) passes in
    /// either role's words, and a value of neither still fails.
    /// </summary>
    [Theory]
    [InlineData(" (12 s cooldown)", true)]
    [InlineData(" Usable every 12 seconds.", true)]
    [InlineData(" Range 12 m.", true)]
    [InlineData(" (45 s cooldown)", false)]
    [InlineData(" Range 18 m.", false)]
    public void AValueInTwoRoles_PassesInEither(string sentence, bool passes)
    {
        int t = 12 * SimConstants.TicksPerSecond;
        ImmutableArray<AbilityDef> abilities = SandstormWith(cooldownTicks: t, range: 12);
        AbilityDef s = abilities.Single(x => x.Key == "sandstorm");
        abilities = abilities.SetItem(abilities.IndexOf(s), Described(s, s.Description + sentence));

        List<string> problems = AbilityContentTests.DescriptionProblems(Data, abilities, Data.Statuses);

        if (passes) Assert.True(problems.Count == 0, string.Join("\n", problems));
        else Assert.Contains(problems, p => p.StartsWith("sandstorm description", StringComparison.Ordinal));
    }

    private static AbilityDef Described(AbilityDef a, string description) => new()
    {
        Id = a.Id, Key = a.Key, Faction = a.Faction, DisplayName = a.DisplayName, Description = description, Kind = a.Kind,
        Range = a.Range, Radius = a.Radius, CastTicks = a.CastTicks, CooldownTicks = a.CooldownTicks, DurationTicks = a.DurationTicks,
        Affects = a.Affects, Effects = a.Effects, HitsBuildings = a.HitsBuildings, ZoneEffect = a.ZoneEffect,
    };

    /// <summary>
    /// Sandstorm's zone statuses linger 1 s, the zone lives 12 s: each number is right only in its own words, wrong in the
    /// other's.
    /// </summary>
    [Theory]
    [InlineData("for 12 seconds", "for 12 seconds", true)]
    [InlineData("for 12 seconds", "for 1 second", false)]
    [InlineData("Your own units are unaffected.", "Your own units are unaffected. The effects linger 1 s.", true)]
    [InlineData("Your own units are unaffected.", "Your own units are unaffected. The effects linger 12 s.", false)]
    [InlineData("Your own units are unaffected.", "Your own units are unaffected. Blinded for 1 second after leaving.", true)]
    [InlineData("Your own units are unaffected.", "Your own units are unaffected. Blinded for 12 seconds after leaving.", false)]
    public void TheLinger_AndTheZoneLife_AreNotInterchangeable(string text, string mutant, bool passes)
    {
        (ImmutableArray<AbilityDef> abilities, ImmutableArray<StatusDef> statuses) = AbilityContentTests.WithDescription("sandstorm", text, mutant);
        List<string> problems = AbilityContentTests.DescriptionProblems(Data, abilities, statuses);
        if (passes) Assert.True(problems.Count == 0, string.Join("\n", problems));
        else Assert.Contains(problems, p => p.StartsWith("sandstorm description", StringComparison.Ordinal)
            && (p.Contains("reads as a duration,", StringComparison.Ordinal) || p.Contains("reads as a lingering", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The scratch-tree case in memory: a landed pending Frenzied of a kind no check knows, with numbers in every role, and
    /// a non-pending ability applying it, neither throws; the status only reports.
    /// </summary>
    [Fact]
    public void ALandedFrenziedOfAnUnknownKind_StatingNumbers_ReportsOnly()
    {
        var frenzied = new StatusDef
        {
            Id = Data.Statuses.Length, Key = "frenzied", DisplayName = "Frenzied",
            Description = "Attacks 25% faster and deals 5 magic damage a second for 8 s, up to 4 m away; 30 s cooldown.",
            Kind = (StatusKind)3,
        };
        Assert.Contains("Frenzied", AbilityContentTests.PendingStatuses);
        var reports = new List<string>();

        List<string> problems = AbilityContentTests.DescriptionProblems(Data, Data.Abilities, Data.Statuses.Add(frenzied), reports);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(reports.Count(r => r.StartsWith("frenzied description", StringComparison.Ordinal)) >= 5, string.Join("\n", reports));
    }
}
