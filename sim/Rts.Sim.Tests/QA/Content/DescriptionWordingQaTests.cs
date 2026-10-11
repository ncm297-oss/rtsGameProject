using System.Collections.Immutable;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D-H3 (BUG-0440): the four new wordings (colon after cooldown, hyphen before the unit word, "dmg", the per-phrase
/// linger) written for every shipped description number in every role. In its own role the number passes, in any other
/// role it fails naming that role; shipped text stays clean.
/// </summary>
public class DescriptionWordingQaTests
{
    private static GameData Data => TestSim.Data;

    /// <summary>The BUG-0440 wording of each role, and the role text the problem must name.</summary>
    private static readonly (string Role, string Template, string Says)[] Forms =
    {
        ("duration", "a {0}-second", "reads as a duration,"),
        ("lingering", "{0} seconds after a unit leaves", "reads as a lingering duration"),
        ("cooldown", "(cooldown: {0} s)", "reads as a cooldown"),
        ("cast", "a {0}-second cast", "reads as a cast time"),
        ("distance", "a {0}-metre", "reads as a distance"),
        ("percent", "{0} per cent", "reads as a percentage"),
        ("damage", "{0} magic dmg", "reads as a damage amount"),
        ("rate", "{0} magic dmg a second", "reads as a damage per second"),
    };

    private static readonly (string Key, string Text, string N, string Role)[] Shipped =
    {
        ("telas_fire", "10 magic damage a second", "10", "rate"),
        ("telas_fire", "4 seconds", "4", "duration"),
        ("cusser", "120 siege damage", "120", "damage"),
        ("sandstorm", "12 seconds", "12", "duration"),
        ("sandstorm", "30%", "30", "percent"),
        ("blinded", "2 m", "2", "distance"),
        ("blinded", "3 m", "3", "distance"),
    };

    public static TheoryData<string, string, string, string, string> EveryNumberInEveryRole()
    {
        var rows = new TheoryData<string, string, string, string, string>();
        foreach (var s in Shipped)
            foreach (var f in Forms)
                rows.Add(s.Key, s.Text, string.Format(f.Template, s.N), f.Role == s.Role ? "" : f.Role, f.Says);
        return rows;
    }

    /// <summary>Own role (role "") passes; any other role fails naming it.</summary>
    [Theory]
    [MemberData(nameof(EveryNumberInEveryRole))]
    public void ABug0440Wording_BindsTheNumberToItsRole(string key, string text, string mutant, string role, string says)
    {
        (ImmutableArray<AbilityDef> abilities, ImmutableArray<StatusDef> statuses) = AbilityContentTests.WithDescription(key, text, mutant);

        List<string> problems = AbilityContentTests.DescriptionProblems(Data, abilities, statuses);

        if (role == "") Assert.True(problems.Count == 0, $"'{key}' '{text}' -> '{mutant}' (own role):\n" + string.Join("\n", problems));
        else
            Assert.True(problems.Any(p => p.StartsWith(key + " description", StringComparison.Ordinal) && p.Contains(says, StringComparison.Ordinal)),
                $"'{key}' '{text}' -> '{mutant}' ({role}): no problem saying '{says}':\n" + string.Join("\n", problems));
    }

    /// <summary>Linger phrasings: the right number (1 s) passes, the storm's 12 s in the same words fails.</summary>
    [Theory]
    [InlineData(" The effects linger for 1 s.", true)]
    [InlineData(" The effects linger for 12 s.", false)]
    [InlineData(" Blinded and Slowed last 1 second after they leave.", true)]
    [InlineData(" Blinded and Slowed last 12 seconds after they leave.", false)]
    [InlineData(" Effects linger 1 s after leaving.", true)]
    public void LingerPhrasings(string sentence, bool passes)
    {
        const string Anchor = "Your own units are unaffected.";
        (ImmutableArray<AbilityDef> abilities, ImmutableArray<StatusDef> statuses) =
            AbilityContentTests.WithDescription("sandstorm", Anchor, Anchor + sentence);

        List<string> problems = AbilityContentTests.DescriptionProblems(Data, abilities, statuses);

        if (passes) Assert.True(problems.Count == 0, sentence + "\n" + string.Join("\n", problems));
        else Assert.Contains(problems, p => p.StartsWith("sandstorm description", StringComparison.Ordinal));
    }

    /// <summary>
    /// BUG-0500: the linger word after the number ("a 1 s linger") or three words before it ("linger on a unit for 1 s")
    /// passed at the base (clause search) and now reads as the storm's duration: a false failure on the right number.
    /// </summary>
    [Theory(Skip = "BUG-0500: per-phrase linger misses a linger word after the number or three words before it")]
    [InlineData(" A 1 s linger after leaving.")]
    [InlineData(" A 1-second linger after leaving.")]
    [InlineData(" The effects linger on a unit for 1 s.")]
    public void LingerPhrasings_ThatPassedAtTheBase_StillPass(string sentence) => LingerPhrasings(sentence, true);

    /// <summary>D11 landing: the shipped data hash equals the golden replay's header (read from the file, no literal).</summary>
    [Fact]
    public void TheShippedContentHash_IsTheGoldenReplaysHeader()
    {
        string path = Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Replays", "cross_map_seed1.replay");
        string line = File.ReadLines(path).First(l => l.StartsWith("data-hash ", StringComparison.Ordinal));
        ulong golden = ulong.Parse(line["data-hash ".Length..].Trim(), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(golden, Data.ContentHash());
    }

    [Fact]
    public void ShippedDescriptions_StillHaveNoProblems()
    {
        Assert.Empty(AbilityContentTests.DescriptionProblems(Data, Data.Abilities, Data.Statuses));
    }
}
