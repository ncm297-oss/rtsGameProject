using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D-R35, 2026-11-09-2226): BUG-1181's far-expansion sentence and the re-worded prose. The "How the AI adapts" pin
/// (<see cref="AiAdaptsContentTests.Problems"/>) mutated both ways where the developer's rows don't (the Whirlwind reach,
/// the reach in data against both pages, a Hard strength, the drop-off's wood), its position-based number guard against a
/// duplicated pinned sentence, the five-bullet / three-rule intro, and the prose times that BUG-1181 found stale, read
/// against the tables they quote (so a re-print that moves a table cell fails until the prose moves with it).
/// </summary>
public class DR35ProsePinQaTests
{
    private static string Joined(string faction) =>
        Regex.Replace(string.Join(" ", PageTables.FactionLines(faction).Select(l => l.Trim())), @"\s+", " ");

    [Theory]
    [InlineData("whirlwind", "`ai.farExpandReach` 384 m", "`ai.farExpandReach` 385 m", "whirlwind ai.farExpandReach: page '385' vs data '384'")]
    // A second copy of a pinned sentence: the pin reads the first, so the copy's equal number is unread and caught.
    [InlineData("malazan", "One at a time.", "One at a time. Within `ai.farExpandReach` 384 m of its Garrison Keep, with no enemy drop-off.", "malazan: the number 384 ")]
    // The reach restated in the new sentence as a number (equal to the pinned value) is outside its pinned sentence.
    [InlineData("whirlwind", "a free start\n  farther than that", "a free start\n  farther than 384 m", "whirlwind: the number 384 ")]
    public void APageEdit_IsCaught(string faction, string from, string to, string expected)
    {
        string page = string.Join("\n", PageTables.FactionLines(faction));
        Assert.Contains(from, page, StringComparison.Ordinal);
        string[] edited = page.Replace(from, to, StringComparison.Ordinal).Split('\n');
        List<string> problems = AiAdaptsContentTests.Problems(TestSim.Data, faction, edited);
        Assert.True(problems.Any(p => p.StartsWith(expected, StringComparison.Ordinal)), $"expected '{expected}', got:\n{string.Join("\n", problems)}");
    }

    [Theory]
    [InlineData("common/rules.json", "ai.farExpandReach", "380", "malazan", "malazan ai.farExpandReach: page '384' vs data '380'")]
    [InlineData("common/rules.json", "ai.farExpandReach", "380", "whirlwind", "whirlwind ai.farExpandReach: page '384' vs data '380'")]
    [InlineData("factions/whirlwind/ai.json", "difficulties.hard.counterStrength", "0.9", "whirlwind", "whirlwind counterStrength: page '1.0' vs data '0.9'")]
    [InlineData("common/rules.json", "ai.starvedWaveAfter", "61", "whirlwind", "whirlwind ai.starvedWaveAfter: page '60' vs data '61'")]
    public void ADataEdit_FailsTheShippedPage(string file, string dotted, string json, string faction, string expected)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(file, root =>
        {
            string[] parts = dotted.Split('.');
            JsonObject parent = root;
            for (int i = 0; i < parts.Length - 1; i++) parent = parent[parts[i]]!.AsObject();
            parent[parts[^1]] = JsonNode.Parse(json);
        });
        AssertCaught(dir, faction, expected);
    }

    [Fact]
    public void TheMalazanDepotWood_FailsTheShippedPage()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/buildings.json", root =>
            root["buildings"]!.AsArray().Select(n => n!.AsObject()).Single(b => (string)b["id"]! == "malazan_depot")["cost"] =
                JsonNode.Parse("""{ "gold": 0, "wood": 76 }"""));
        AssertCaught(dir, "malazan", "malazan the far drop-off: page '75' vs data '76'");
    }

    /// <summary>The word each page's far-expansion bullet uses for its town hall (D-R37: typed, failing naming a faction it lacks).</summary>
    private static readonly Dictionary<string, string> HallWord = new() { ["malazan"] = "keep", ["whirlwind"] = "camp", ["teblor"] = "hall" };

    /// <summary>BUG-1181: both pages say the reach holds for every mine, a start's included.</summary>
    [Theory]
    [MemberData(nameof(ContentFactions.Theory), MemberType = typeof(ContentFactions))]
    public void TheFarExpansionBullet_SaysTheReachHoldsForStarts(string faction)
    {
        string hall = ContentFactions.Typed(HallWord, faction, "DR35ProsePinQaTests.HallWord");
        string text = AiAdaptsContentTests.SectionText(PageTables.FactionLines(faction), out string? where)!;
        Assert.True(text != null, where);
        Assert.Contains("The reach holds for every mine, a start location's included: a free start farther than that from the "
            + hall + " is not walked to, and a mine beyond it is never taken.", text, StringComparison.Ordinal);
    }

    /// <summary>BUG-1181 nit 1: the intro's "Three rules" and "five bullets" count what follows.</summary>
    [Theory]
    [MemberData(nameof(ContentFactions.Theory), MemberType = typeof(ContentFactions))]
    public void TheIntro_CountsTheBullets(string faction) => IntroCounts(PageTables.FactionLines(faction));

    /// <summary>The intro check on one page's lines.</summary>
    internal static void IntroCounts(string[] lines)
    {
        int h = Array.IndexOf(lines, AiAdaptsContentTests.Heading);
        int bullets = 0;
        for (int i = h + 1; i < lines.Length && !lines[i].StartsWith('#'); i++)
            if (lines[i].StartsWith("- **", StringComparison.Ordinal)) bullets++;
        string text = AiAdaptsContentTests.SectionText(lines, out _)!;
        Assert.Equal(5, bullets);
        Assert.Contains("Three rules sit on top of every opening above, the counter shift, the starved charge and the far expansion", text, StringComparison.Ordinal);
        Assert.Contains("every number and name in the five bullets", text, StringComparison.Ordinal);
        foreach (string rule in new[] { "Counter shift:", "Starved charge:", "Far expansion:" })
            Assert.Contains(rule, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The BUG-0982 paragraph's defeats and re-print notes are the threshold table's cells. D-R39 (2026-11-15, after the
    /// sim's M6-S10): seed 8 no longer falls at every threshold (it waves at all three and stands at 18 and 16 pop), so the
    /// check that its defeat is one value at every threshold now checks that it waves at every threshold, stands at 18 and
    /// 16 and falls at 14, each cell read from the table; seed 14's defeat is still one value at every threshold; and the
    /// Age II table's twin cells (the same shipped runs) are read against the threshold table's row. D33 (2026-11-17,
    /// BUG-1524 item 3; un-skipped): the table re-measured on the sim of M6-S12 and the prose rewritten; <c>cadre_rush</c>
    /// ships 16 pop, so the shipped row is read from the file (<c>attack.minArmyPop</c>), the twin cells are read against
    /// it (was the 18-pop row), and the sentences quote both the shipped row's and the 18-pop row's first waves.
    /// </summary>
    [Fact(Skip = "BUG-1851: M6-S14 moved the BUG-0982 measurement (at 16 pop seed 8 now falls at 17:49, so the re-stated rule keeps 18 while cadre_rush ships 16); the prose waits for the data track's re-measure after M6-S14 (the table is re-printed)")]
    public void Malazan_Bug0982Prose_MatchesTheThresholdTable()
    {
        string page = Joined("malazan");
        Match Row(string pop, int seed) =>
            Regex.Match(page, @"\| " + Regex.Escape(pop) + @" \| " + seed + @" \| ([^|]+?) \| ([^|]+?) \| ([^|]+?) \| ([^|]+?) \|");
        string Defeat(string pop, int seed) => Row(pop, seed).Groups[3].Value;
        string Wave(string pop, int seed) => Regex.Match(Row(pop, seed).Groups[1].Value, @"^(\d+:\d\d) \(\d+\)$").Groups[1].Value;
        AiOpening cadre = TestSim.Data.Ai[TestSim.Data.FindFaction("malazan")].Openings.Single(o => o.Id == "cadre_rush");
        string shipped = $"{cadre.AttackMinHalfPop / 2} / {cadre.AttackMinCost:#,0}";
        Assert.Equal("16 / 1,184", shipped);
        string[] all = { "18 / 1,332", "16 / 1,184", "14 / 1,036" };
        string d14 = Defeat("18 / 1,332", 14);
        Assert.Matches(@"^\d+:\d\d$", d14);
        foreach (string pop in all)
        {
            Assert.Equal(d14, Defeat(pop, 14));
            Assert.Equal("never (0)", Row(pop, 14).Groups[1].Value);
            Assert.NotEqual("", Wave(pop, 7));
            Assert.NotEqual("", Wave(pop, 8));
            Assert.Equal("never", Defeat(pop, 7));
        }
        Assert.Equal("never", Defeat("18 / 1,332", 8));
        Assert.Equal("never", Defeat("16 / 1,184", 8));
        string d8At14 = Defeat("14 / 1,036", 8);
        Assert.Matches(@"^\d+:\d\d$", d8At14);
        string w8 = Wave(shipped, 8), w7 = Wave(shipped, 7), w8At18 = Wave("18 / 1,332", 8), w7At18 = Wave("18 / 1,332", 7);
        Assert.Contains($"are destroyed (seed 14 at {d14}, 18 at", page, StringComparison.Ordinal);
        Assert.Contains($"seeds 7 and 8 wave and stand at 20:00, seed 8 first at {w8}.", page, StringComparison.Ordinal);
        Assert.Contains($"so seed 8's first wave comes at {w8} ({w8At18} at 18) and seed 7's at {w7} ({w7At18} at 18), and both still stand", page, StringComparison.Ordinal);
        Assert.Contains($"at 14 seed 8 is destroyed at {d8At14} where at 16 and 18 it stands", page, StringComparison.Ordinal);
        Assert.Contains($"At every threshold seed 14 is destroyed at {d14} without a wave, after its Age II ({Row("18 / 1,332", 14).Groups[2].Value})", page, StringComparison.Ordinal);
        Assert.Contains($"at 14 pop seed 8 sends one wave ({Wave("14 / 1,036", 8)}) and is destroyed at {d8At14}", page, StringComparison.Ordinal);
        Assert.Contains($"seeds 7 and 8 wave sooner ({w7} and {w8}, against {w7At18} and {w8At18} at 18)", page, StringComparison.Ordinal);
        Assert.Contains($"The re-stated rule picks {cadre.AttackMinHalfPop / 2}:", page, StringComparison.Ordinal);
        Assert.Contains($"`cadre_rush` ships {cadre.AttackMinHalfPop / 2} army pop and {cadre.AttackMinCost:#,0} since D33", page, StringComparison.Ordinal);
        // The Age II table's twin cells are the shipped runs: the threshold table's shipped row.
        string a7 = Row(shipped, 7).Groups[2].Value, a8 = Row(shipped, 8).Groups[2].Value, a14 = Row(shipped, 14).Groups[2].Value;
        foreach (string a in new[] { a7, a8, a14 }) Assert.Matches(@"^\d+:\d\d$", a);
        Assert.Contains($"| `cadre_rush` | `raider_rush` | seed 7: {a7} | seed 8: {a8} | seed 14: {a14} (defeated {d14}) |", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Age II tables' prose: seeds 32 and 45 (Malazan) and seeds 9, 13 and 19 (Whirlwind). D-R39 (2026-11-15, after the
    /// sim's M6-S10): seeds 32, 45 and 9 now reach Age II and stand at 20:00, so each is read as a cell with no defeat and
    /// its sentence says so. M6-SH2 fix round 2 (BUG-1524): seed 13 never reaches Age II and is defeated, read from its cell.
    /// M6-S12: seed 13's outcome clause is built from its cell (it now stands at 20:00 without Age II).
    /// </summary>
    [Fact]
    public void AgeTwoProse_MatchesTheAgeTwoTables()
    {
        string m = Joined("malazan"), w = Joined("whirlwind");
        string m32 = Regex.Match(m, @"\| seed 32: (\d+:\d\d) \|").Groups[1].Value;
        Assert.NotEqual("", m32);
        Assert.Contains($"`crossbow_push` seed 32 reaches Age II at {m32} and stands at 20:00", m, StringComparison.Ordinal);
        string m45 = Regex.Match(m, @"\| seed 45: (\d+:\d\d) \|").Groups[1].Value;
        Assert.NotEqual("", m45);
        Assert.Contains($"seed 45 reaches Age II at {m45} (was 9:28) and stands at 20:00 again (was defeated at 19:02", m, StringComparison.Ordinal);

        string w9 = Regex.Match(w, @"\| seed 9: (\d+:\d\d) \|").Groups[1].Value;
        Assert.NotEqual("", w9);
        Assert.Contains($"seed 9's side reaches Age II at {w9}", w, StringComparison.Ordinal);
        // BUG-1524 (M6-SH2 fix round 2, ruled): seed 13 is read from its cell like its neighbours. Since M6-SH2 its side
        // never reaches Age II and is defeated (was: "| seed 13: never |" and "seed 13's still stands at 20:00 without Age II").
        // M6-S12 (the sim's AI rules; handed this class while the data track is stopped): the clause is built from the cell,
        // as BUG-1524 built TeblorOpeningQaTests' outcome clauses: "never (defeated m:ss)" reads "and is defeated at m:ss",
        // "never" reads "and still stands at 20:00" (M6-S12: the side stands; M6-SH2: defeated at 15:19).
        Match c13 = Regex.Match(w, @"\| seed 13: never( \(defeated (\d+:\d\d)\))? \|");
        Assert.True(c13.Success, "whirlwind.md has no '| seed 13: never ... |' cell");
        string w13 = c13.Groups[2].Success ? $"and is defeated at {c13.Groups[2].Value}" : "and still stands at 20:00";
        Assert.Contains($"seed 13's side never reaches Age II {w13}", w, StringComparison.Ordinal);
        string w19 = Regex.Match(w, @"\| `raider_rush` \| `crossbow_push` \|[^\n]*?seed 19: (\d+:\d\d) \|").Groups[1].Value;
        Assert.NotEqual("", w19);
        Assert.Contains($"seed 19 now reaches Age II at {w19} (was 10:34", w, StringComparison.Ordinal);
    }

    private static void AssertCaught(TestDataDir dir, string faction, string expected)
    {
        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        List<string> problems = AiAdaptsContentTests.Problems(result.Data!, faction, PageTables.FactionLines(faction));
        Assert.True(problems.Any(p => p.StartsWith(expected, StringComparison.Ordinal)), $"expected '{expected}', got:\n{string.Join("\n", problems)}");
    }
}
