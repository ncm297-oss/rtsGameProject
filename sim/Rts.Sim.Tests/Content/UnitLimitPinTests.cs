using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D39: M8-4's unit limit (docs/03 "Implementation (M8-4)") in the content pins, before the Shadow roster carries it.
/// shadow.md's Schema gaps row G4 is docs/03's "For the data track (M8-4)" row; the description reading
/// (<see cref="UnitLimitText"/>) and the page pin (<see cref="UnitLimitPagePin"/>) are proven on temp copies of the shipped
/// data where the Teblor War-dog stands in for the Hound (D35's stand-in): every shipped <c>maxAlive</c> stripped first
/// (rule 58, <see cref="MaxAliveLoaderTests.Strip"/>), then the War-dog's <c>spawnCount</c> and <c>maxAlive</c> written, so
/// the copy is right whether or not the shipped data already carries the field.
/// </summary>
public class UnitLimitPinTests
{
    private const string Hound = "teblor_war_dog";
    private const string Page = "shadow.md";

    private static string[] ShadowLines() => PageTables.FactionLines("shadow");

    private static UnitLimitPagePin.LimitRow ShadowRow()
    {
        var problems = new List<string>();
        List<UnitLimitPagePin.LimitRow> rows = UnitLimitPagePin.Rows(ShadowLines(), Page, problems);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        return Assert.Single(rows);
    }

    /// <summary>shadow.md's proposed description for the Hound (its Owner review row).</summary>
    private static string ShadowHoundDescription() => UnitLimitPagePin.OwnerReviewUnitDescriptions(ShadowLines())["shadow_hound"];

    /// <summary>A stripped copy with the War-dog's <c>spawnCount</c> written as <paramref name="spawnCount"/> and its <c>maxAlive</c> as <paramref name="maxAlive"/> (null: left out).</summary>
    private static GameData Forge(int? maxAlive, int spawnCount = 1) => AiScenes.DataWith(dir =>
    {
        MaxAliveLoaderTests.Strip(dir);
        dir.SetUnitField("teblor", Hound, "spawnCount", spawnCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (maxAlive is int m) dir.SetUnitField("teblor", Hound, "maxAlive", m.ToString(System.Globalization.CultureInfo.InvariantCulture));
    });

    /// <summary>The War-dog with the page's pinned limit (shadow.md G4's number, read from the page).</summary>
    private static readonly Lazy<GameData> AtPageLimit = new(() => Forge(ShadowRow().MaxAlive));
    private static readonly Lazy<GameData> AtPageLimitPack = new(() => Forge(ShadowRow().MaxAlive, spawnCount: 3));
    private static readonly Lazy<GameData> AtSix = new(() => Forge(6));
    private static readonly Lazy<GameData> WithoutLimit = new(() => Forge(null));

    private static GameData Of(string forge) => forge switch
    {
        "page" => AtPageLimit.Value,
        "page-pack" => AtPageLimitPack.Value,
        "six" => AtSix.Value,
        "none" => WithoutLimit.Value,
        _ => throw new ArgumentException(forge),
    };

    private static UnitDef DogOf(GameData d) => d.Units[d.FindUnit(Hound)];

    // ---------- shadow.md against docs/03 (criterion 1) ----------

    [Fact]
    public void ShadowPagesG4Row_IsDocs03sRow()
    {
        string[] lines = PageTables.Lines("docs/03-technical-design.md");
        int from = Array.FindIndex(lines, l => l.StartsWith("**For the data track (M8-4).**", StringComparison.Ordinal));
        Assert.True(from >= 0, "docs/03 has no '**For the data track (M8-4).**' paragraph");
        int open = Array.FindIndex(lines, from, l => l.Trim() == "```json");
        int close = Array.FindIndex(lines, open + 1, l => l.Trim() == "```");
        JsonObject doc = JsonNode.Parse(string.Join("\n", lines[(open + 1)..close]))!.AsObject();

        UnitLimitPagePin.LimitRow row = ShadowRow();
        Assert.Equal("G4", row.Gap);
        Assert.Equal((string)doc["id"]!, row.Unit);
        Assert.Equal((int)doc["maxAlive"]!, row.MaxAlive);
        Assert.Equal(7, row.MaxAlive); // docs/03 and the design ("at most 7 alive", D-R38)
    }

    /// <summary>Criterion 1: the mechanics row names the built field, the gap row says built with docs/03's four facts, and no sentence says the limit has no field.</summary>
    [Fact]
    public void ShadowPage_SaysG4IsBuilt_WithTheFourFacts_AndNowhereThatTheLimitHasNoField()
    {
        string[] lines = ShadowLines();
        List<List<string>> tables = PageTables.TablesInSection(lines, PageTables.HeadingStartingWith(lines, "## Schema gaps"));
        string[] mechanics = Assert.Single(tables[0].Skip(2).Select(l => PageTables.Cells(l)), c => c[2].Contains("gap G4", StringComparison.Ordinal));
        Assert.Contains("`maxAlive` (M8-4; gap G4, built)", mechanics[2], StringComparison.Ordinal);
        string[] gap = Assert.Single(tables[1].Skip(2).Select(l => PageTables.Cells(l)), c => c[0] == "G4");
        Assert.StartsWith("**Built (M8-4", gap[2], StringComparison.Ordinal);
        string facts = gap[3];
        foreach (string fact in new[]
        {
            "`shadow_hound` `\"maxAlive\": 7`", // the field on the Hound
            "live Hounds", "plus those its queued items will spawn", // alive plus queued
            "Training only: a summon is never refused", // training only
            "The computer player trains the other units of its mix at the limit", // the AI
        })
            Assert.Contains(fact, facts, StringComparison.Ordinal);

        var noField = new Regex(@"\b(?:has none|no field|holds none|not built)\b|\bnone \(G4\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var aboutLimit = new Regex(@"\bG4\b|\b(?:cap|limit)\b[^.]*\b7\b|\b7\b[^.]*\b(?:cap|limit)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        string page = string.Join(" ", lines.Select(l => l.Trim()));
        foreach (string sentence in Regex.Split(page, @"(?<=[.|])\s+"))
            if (aboutLimit.IsMatch(sentence))
                Assert.False(noField.IsMatch(sentence), $"shadow.md: '{sentence}' says the limit has no field");
    }

    // ---------- the description reading (criterion 2) ----------

    [Fact]
    public void EveryShippedUnitDescription_StatesItsLimit_AndNoOther()
    {
        List<string> problems = UnitLimitText.Problems(TestSim.Data.Units);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void TheForgedCopies_CarryTheLimitOnTheStandIn()
    {
        Assert.Equal((7, 1), (DogOf(AtPageLimit.Value).MaxAlive, DogOf(AtPageLimit.Value).SpawnCount));
        Assert.Equal((7, 3), (DogOf(AtPageLimitPack.Value).MaxAlive, DogOf(AtPageLimitPack.Value).SpawnCount));
        Assert.Equal(6, DogOf(AtSix.Value).MaxAlive);
        Assert.Equal(0, DogOf(WithoutLimit.Value).MaxAlive);
        Assert.All(WithoutLimit.Value.Units, u => Assert.Equal(0, u.MaxAlive)); // rule 58: stripped everywhere
    }

    /// <summary>shadow.md's own Owner review sentence for the Hound passes on the War-dog at the page's limit, alone and as a pack of 3.</summary>
    [Theory]
    [InlineData("page")]
    [InlineData("page-pack")]
    public void ShadowPagesHoundSentence_Passes_AtTheFieldsLimit(string forge)
    {
        string text = ShadowHoundDescription();
        Assert.Contains("No more than 7 can live at once.", text, StringComparison.Ordinal);
        List<string> problems = UnitLimitText.Problems(DogOf(Of(forge)), text);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Other right wordings pass; percentages, reaches, times and counts in words are never read as a limit.</summary>
    [Theory]
    [InlineData("page", "At most 7 can be alive at once.")]
    [InlineData("page", "Hunts in packs. No more than 7 Hounds live at once; each other one within 8 m adds 10%, up to 30%.")]
    [InlineData("page", "Never more than 7 alive.")]
    [InlineData("none", "Each other War-dog within 8 m adds 10% damage, up to 30%.")]
    [InlineData("none", "Big shaggy dogs trained three at a time.")]
    [InlineData("none", "Lives up to 20 s, at most 5 m from its master.")]
    [InlineData("none", "Summons up to 3 Wraiths beside the caster.")]
    public void ARightOrUnrelatedSentence_Passes(string forge, string text)
    {
        List<string> problems = UnitLimitText.Problems(DogOf(Of(forge)), text);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ---------- D-H11 (BUG-1880 edge 1): a count of the unit, not of its targets ----------

    /// <summary>A real limit in three wordings (the number alone, the unit's own name, "of them") reads its number on the War-dog, and passes at the field's 7.</summary>
    [Theory]
    [InlineData("No more than 7 can live at once.")]
    [InlineData("At most 7 War-dogs at once.")]
    [InlineData("Up to 7 of them may be alive.")]
    [InlineData("A big dog. Never more than 7 living, each worth a horse.")]
    public void ALimitSentence_InAnyWording_ReadsItsNumber(string text)
    {
        Assert.Equal(new[] { 7 }, UnitLimitText.Stated(text, "War-dog").Select(s => s.Limit));
        List<string> problems = UnitLimitText.Problems(DogOf(Of("page")), text);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>A count of targets or projectiles "at once" reads no limit, even with the unit's name, an "alive" word or a bare "at once" in the sentence, and passes on a unit with no field.</summary>
    [Theory]
    [InlineData("Strikes up to 3 foes at once.")]
    [InlineData("Throws at most 2 spears at once.")]
    [InlineData("Bites no more than 4 enemies at once.")]
    [InlineData("A War-dog mauls up to 3 riders at once.")]
    [InlineData("Keeps at most 2 alive targets at once.")]
    [InlineData("Hits up to 3 at once while it is alive.")]
    public void ATargetCountSentence_InAnyWording_ReadsNone(string text)
    {
        Assert.Empty(UnitLimitText.Stated(text, "War-dog"));
        List<string> problems = UnitLimitText.Problems(DogOf(Of("none")), text);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>The unit's name is its words up to the head noun: "Hounds (of Shadow)" and "Edur Warriors" name the unit; another word of the name ("shadow bolts") does not.</summary>
    [Theory]
    [InlineData("Hound of Shadow", "At most 7 Hounds of Shadow at once.", 7)]
    [InlineData("Hound of Shadow", "At most 7 Hounds at once.", 7)]
    [InlineData("Hound of Shadow", "Hurls up to 3 shadow bolts at once.", 0)]
    [InlineData("Edur Warrior", "No more than 5 Edur Warriors at once.", 5)]
    [InlineData("Edur Warrior", "Spears up to 2 Warriors at once.", 0)]
    [InlineData("War-dog", "Drags up to 2 dogs at once.", 0)]
    public void TheUnitsName_IsItsWordsUpToTheHeadNoun(string unitName, string text, int limit)
    {
        int[] read = UnitLimitText.Stated(text, unitName).Select(s => s.Limit).ToArray();
        Assert.Equal(limit == 0 ? Array.Empty<int>() : new[] { limit }, read);
    }

    /// <summary>A description with both a target count and a limit (two sentences, or one sentence with a comma) reads the limit only.</summary>
    [Theory]
    [InlineData("Bites up to 3 foes at once. No more than 7 can live at once.")]
    [InlineData("Bites up to 3 foes at once, and no more than 7 can live at once.")]
    [InlineData("No more than 7 War-dogs live at once; each throws at most 2 spears at once.")]
    public void ADescriptionWithBoth_ReadsTheLimit(string text)
    {
        Assert.Equal(new[] { 7 }, UnitLimitText.Stated(text, "War-dog").Select(s => s.Limit));
        Assert.Empty(UnitLimitText.Problems(DogOf(Of("page")), text));
        Assert.Contains("teblor_war_dog description maxAlive: 'no more than 7' reads as the unit limit, data 6",
            UnitLimitText.Problems(DogOf(Of("six")), text.Replace("No more than", "no more than", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Criterion 2's failing rows: the page's sentence on a field of 6 or on no field, and "at most 8" (or "No more than 8")
    /// on the field of 7, each naming the unit, the field and both numbers; a field the text does not state fails too.
    /// </summary>
    [Theory]
    [InlineData("six", null, "teblor_war_dog description maxAlive: 'No more than 7' reads as the unit limit, data 6")]
    [InlineData("none", null, "teblor_war_dog description maxAlive: 'No more than 7' reads as the unit limit, data none")]
    [InlineData("page", "A horse-sized hound. At most 8 can live at once.", "teblor_war_dog description maxAlive: 'At most 8' reads as the unit limit, data 7")]
    [InlineData("page", "A horse-sized hound. At most 8 can live at once.", "teblor_war_dog description maxAlive: data 7, which the text does not state")]
    [InlineData("page-pack", "No more than 8 can live at once.", "teblor_war_dog description maxAlive: 'No more than 8' reads as the unit limit, data 7")]
    [InlineData("page", "A horse-sized hound that hunts in packs.", "teblor_war_dog description maxAlive: data 7, which the text does not state")]
    public void AWrongOrMissingLimit_FailsNamingTheUnitAndTheField(string forge, string? text, string want)
    {
        List<string> problems = UnitLimitText.Problems(DogOf(Of(forge)), text ?? ShadowHoundDescription());
        Assert.True(problems.Any(p => p.StartsWith(want, StringComparison.Ordinal)), $"no problem starting '{want}':\n" + string.Join("\n", problems));
    }

    // ---------- the page's row against its own note and sentence ----------

    [Fact]
    public void ShadowPagesLimitRow_AgreesWithItsNoteAndItsSentence()
    {
        Assert.Empty(UnitLimitPagePin.RowProblems(Page, ShadowLines(), ahead: true));
        Assert.Empty(PagePinReadinessTests.Problems("shadow", ShadowLines()));
    }

    /// <summary>One number of the row, the note or the sentence changed alone (or the row or note dropped) is a problem naming the gap, the unit and the field.</summary>
    [Theory]
    [InlineData("**At most 7 Hounds alive**", "**At most 8 Hounds alive**", "shadow.md Schema gaps G4 `shadow_hound` maxAlive: row 7, the 'Hound of Shadow' note 8")]
    [InlineData("**At most 7 Hounds alive** at once, as in the legend.", "", "shadow.md Schema gaps G4 `shadow_hound` maxAlive: row 7, the 'Hound of Shadow' note states no")]
    [InlineData("`\"maxAlive\": 7`", "`\"maxAlive\": 8`", "shadow.md Schema gaps G4 `shadow_hound` maxAlive: row 8, the 'Hound of Shadow' note 7")]
    [InlineData("`\"maxAlive\": 7`", "`\"maxAlive\": 8`", "shadow.md Schema gaps G4 `shadow_hound` maxAlive: row 8, the Owner review description 'No more than 7'")]
    [InlineData("No more than 7 can live at once. |", "No more than 8 can live at once. |", "shadow.md Schema gaps G4 `shadow_hound` maxAlive: row 7, the Owner review description 'No more than 8'")]
    [InlineData("No more than 7 can live at once. |", "|", "shadow.md Schema gaps G4 `shadow_hound` maxAlive: row 7, the Owner review description states no limit")]
    [InlineData("`\"maxAlive\": 7`", "`\"maxAlive\": 7.5`", "shadow.md Schema gaps G4: '\"maxAlive\": 7.5' is not one whole-number maxAlive")]
    [InlineData("`\"maxAlive\": 7`", "maxAlive 7", "shadow.md Unit notes 'Hound of Shadow': states a limit, but no Schema gaps row")]
    [InlineData("`\"maxAlive\": 7`", "maxAlive 7", "shadow.md Owner review `shadow_hound`: the description states a limit, but no Schema gaps row")]
    [InlineData("`shadow_hound` `\"maxAlive\"", "`shadow_houndx` `\"maxAlive\"", "shadow.md Schema gaps G4 `shadow_houndx`: not an id of the Units table")]
    public void AnEditedShadowRowNoteOrSentence_IsAProblem_NamingGapUnitAndField(string find, string replace, string expected)
    {
        string[] lines = ShadowLines();
        int[] hits = Enumerable.Range(0, lines.Length).Where(i => lines[i].Contains(find, StringComparison.Ordinal)).ToArray();
        Assert.True(hits.Length == 1, $"shadow.md has '{find}' on {hits.Length} lines, expected 1");
        lines[hits[0]] = lines[hits[0]].Replace(find, replace, StringComparison.Ordinal);

        List<string> problems = UnitLimitPagePin.RowProblems(Page, lines, ahead: true);

        Assert.True(problems.Any(p => p.StartsWith(expected, StringComparison.Ordinal)), $"expected '{expected}', got:\n{string.Join("\n", problems)}");
    }

    // ---------- the page pin, both ways, on the forged copy (criterion 3) ----------

    /// <summary>teblor.md with shadow.md's Hound limit sentence on the War-dog's note (names substituted), or with <paramref name="edit"/> applied.</summary>
    private static string[] TeblorWithShadowLimitSentence(Func<string, string>? edit = null)
    {
        string note = HitBonusPagePin.Notes(ShadowLines()).Single(n => n.Unit == "Hound of Shadow").Text;
        int at = note.IndexOf("At most ", StringComparison.Ordinal);
        Assert.True(at >= 0, "shadow.md's Hound note has no 'At most ' sentence");
        string sentence = note[at..(note.IndexOf('.', at) + 1)].Replace("Hounds", "War-dogs", StringComparison.Ordinal);

        string[] lines = PageTables.FactionLines("teblor");
        int dog = Array.FindIndex(lines, l => l.StartsWith("- **War-dog (unique, Age II):**", StringComparison.Ordinal));
        Assert.True(dog >= 0);
        Assert.Null(UnitLimitPagePin.LimitIn(HitBonusPagePin.Notes(lines).Single(n => n.Unit == "War-dog").Text)); // teblor.md states none today
        lines[dog] += " " + sentence;
        return edit == null ? lines : lines.Select(edit).ToArray();
    }

    [Fact]
    public void ThePagePin_PassesBothWays_WithThePagesLimitOnTheField()
    {
        string[] lines = TeblorWithShadowLimitSentence();
        Assert.Equal(ShadowRow().MaxAlive, UnitLimitPagePin.LimitIn(HitBonusPagePin.Notes(lines).Single(n => n.Unit == "War-dog").Text));
        foreach (GameData d in new[] { AtPageLimit.Value, AtPageLimitPack.Value })
        {
            List<string> problems = UnitLimitPagePin.LimitProblems(d, "teblor", lines);
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }

    /// <summary>Data to page: the field landed without the page's sentence fails, naming the unit and the field.</summary>
    [Fact]
    public void TheFieldWithoutTheSentence_Fails()
    {
        List<string> problems = UnitLimitPagePin.LimitProblems(AtPageLimit.Value, "teblor", PageTables.FactionLines("teblor"));
        Assert.Contains("teblor.md \"Unit notes\" 'War-dog' maxAlive: data 7, its note states no 'At most <n> ... alive'", problems);
    }

    /// <summary>Page to data: the sentence on a copy without the field, or with another number, fails naming both numbers.</summary>
    [Theory]
    [InlineData("none", "teblor.md \"Unit notes\" 'War-dog' maxAlive: page 7, data none")]
    [InlineData("six", "teblor.md \"Unit notes\" 'War-dog' maxAlive: page 7, data 6")]
    public void TheSentenceWithoutTheField_OrWithAnotherNumber_Fails(string forge, string want)
    {
        List<string> problems = UnitLimitPagePin.LimitProblems(Of(forge), "teblor", TeblorWithShadowLimitSentence());
        Assert.Contains(want, problems);
    }

    /// <summary>One number changed alone, on the page or (in memory) in the data, fails naming both numbers.</summary>
    [Theory]
    [InlineData("page", 0, "teblor.md \"Unit notes\" 'War-dog' maxAlive: page 8, data 7")]
    [InlineData("data", 8, "teblor.md \"Unit notes\" 'War-dog' maxAlive: page 7, data 8")]
    [InlineData("data", 1, "teblor.md \"Unit notes\" 'War-dog' maxAlive: page 7, data 1")]
    public void AMutatedNumber_FailsNamingBoth(string side, int data, string want)
    {
        GameData d = AtPageLimit.Value;
        int dog = d.FindUnit(Hound);
        string[] lines = side == "page"
            ? TeblorWithShadowLimitSentence(l => l.Replace("At most 7 War-dogs alive", "At most 8 War-dogs alive", StringComparison.Ordinal))
            : TeblorWithShadowLimitSentence();
        List<string> problems = UnitLimitPagePin.LimitProblems(d, "teblor", lines, side == "data" ? u => u.Id == dog ? data : u.MaxAlive : null);
        Assert.Contains(want, problems);
    }

    /// <summary>The limit pin over every data faction's page (no shipped unit has a limit and no page states one today).</summary>
    [Theory]
    [MemberData(nameof(ContentFactions.Theory), MemberType = typeof(ContentFactions))]
    public void EveryLimit_MatchesItsUnitNote_BothWays(string faction)
    {
        List<string> problems = UnitLimitPagePin.LimitProblems(TestSim.Data, faction, PageTables.FactionLines(faction));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Every page's limit rows (the data pages' and the pages ahead) agree with their notes and sentences.</summary>
    [Theory]
    [MemberData(nameof(ContentFactions.PageTheory), MemberType = typeof(ContentFactions))]
    public void EveryPagesLimitRows_AgreeWithItsNotesAndSentences(string faction)
    {
        List<string> problems = UnitLimitPagePin.RowProblems(faction + ".md", PageTables.FactionLines(faction), ahead: !ContentFactions.Keys.Contains(faction));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
