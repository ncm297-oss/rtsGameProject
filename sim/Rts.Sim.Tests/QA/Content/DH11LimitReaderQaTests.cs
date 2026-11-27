using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA for D-H11 (session 2026-11-27-0817, tier light): <see cref="UnitLimitText.Stated"/> on awkward sentences the
/// developer's rows do not carry (a limit of 1, a limit and a volley in one sentence, the unit's name beside a target
/// count, a number written as a word). Rows that read a target count as a limit are skipped with BUG-1920.
/// </summary>
public class DH11LimitReaderQaTests
{
    private const string Hound = "Hound of Shadow";

    private static int[] Read(string text, string? name = Hound) => UnitLimitText.Stated(text, name).Select(s => s.Limit).ToArray();

    /// <summary>A real limit in awkward wordings (a limit of 1, the full name, a volley in the same or the next clause) reads its number only.</summary>
    [Theory]
    [InlineData("At most 1 can live at once.", 1)]
    [InlineData("At most 1 alive.", 1)]
    [InlineData("No more than 1 Hound of Shadow alive.", 1)]
    [InlineData("No more than 7 Hounds of Shadow can live at once.", 7)]
    [InlineData("No more than 7 can live at once and each throws up to 2 spears.", 7)]
    [InlineData("Throws up to 2 spears while no more than 7 live.", 7)]
    [InlineData("No more than 7 can live at once; each bites up to 3 foes at once.", 7)]
    [InlineData("Never more than 7 alive.", 7)]
    public void AnAwkwardLimitSentence_ReadsOnlyTheLimit(string text, int limit)
    {
        Assert.Equal(new[] { limit }, Read(text));
    }

    /// <summary>Target counts with an alive word before the noun, the unit's name as the subject, or a percent read none.</summary>
    [Theory]
    [InlineData("Keeps at most 2 alive targets at once.")]
    [InlineData("A Hound of Shadow bites up to 3 foes at once.")]
    [InlineData("Up to 30% damage at once while alive.")]
    public void AnAwkwardTargetCount_ReadsNone(string text)
    {
        Assert.Empty(Read(text));
    }

    /// <summary>The documented limit: a number written as a word is not read (D-H11 report, docs/factions/shadow.md), so a Hound saying "seven" states no limit.</summary>
    [Fact]
    public void ANumberWrittenAsAWord_IsNotRead()
    {
        Assert.Empty(Read("No more than seven can live at once."));
    }

    /// <summary>BUG-1920: a target count whose noun is followed by an alive word, or "alive" later in a linked clause, reads as a unit limit.</summary>
    [Theory(Skip = "BUG-1920: UnitLimitText reads a target count followed by 'living' / 'remain alive' / a later 'alive' as a unit limit")]
    [InlineData("Bites up to 3 foes living nearby.")]
    [InlineData("Heals up to 3 allies living nearby.")]
    [InlineData("At most 2 enemies remain alive after its bite.")]
    [InlineData("Up to 3 are hit at once while it is alive.")]
    public void ATargetCountFollowedByAnAliveWord_ReadsNone(string text)
    {
        Assert.Empty(Read(text));
    }
}
