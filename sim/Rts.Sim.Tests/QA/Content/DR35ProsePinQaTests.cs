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
    /// Age II table's twin cells (the same shipped runs) are read against the threshold table's 18-pop row.
    /// </summary>
    [Fact]
    public void Malazan_Bug0982Prose_MatchesTheThresholdTable()
    {
        string page = Joined("malazan");
        Match Row(string pop, int seed) =>
            Regex.Match(page, @"\| " + Regex.Escape(pop) + @" \| " + seed + @" \| ([^|]+?) \| ([^|]+?) \| ([^|]+?) \| ([^|]+?) \|");
        string Defeat(string pop, int seed) => Row(pop, seed).Groups[3].Value;
        string d14 = Defeat("18 / 1,332", 14);
        Assert.Matches(@"^\d+:\d\d$", d14);
        foreach (string pop in new[] { "16 / 1,184", "14 / 1,036" })
            Assert.Equal(d14, Defeat(pop, 14));
        string w8 = Regex.Match(Row("18 / 1,332", 8).Groups[1].Value, @"^(\d+:\d\d) \(\d+\)$").Groups[1].Value;
        Assert.NotEqual("", w8);
        foreach (string pop in new[] { "16 / 1,184", "14 / 1,036" })
            Assert.Matches(@"^\d+:\d\d \(\d+\)$", Row(pop, 8).Groups[1].Value);
        Assert.Equal("never", Defeat("18 / 1,332", 8));
        Assert.Equal("never", Defeat("16 / 1,184", 8));
        string d8At14 = Defeat("14 / 1,036", 8);
        Assert.Matches(@"^\d+:\d\d$", d8At14);
        Assert.Contains($"are destroyed (seed 14 at {d14}, 18 at", page, StringComparison.Ordinal);
        Assert.Contains($"seeds 7 and 8 wave and stand at 20:00, seed 8 first at {w8}", page, StringComparison.Ordinal);
        Assert.Contains($"at 14 seed 8 is destroyed at {d8At14} where at 18 it stands", page, StringComparison.Ordinal);
        Assert.Contains($"at every threshold seed 14 now falls at {d14} (was", page, StringComparison.Ordinal);
        Assert.Contains($"(at 18 pop first at {w8},", page, StringComparison.Ordinal);
        string a8 = Row("18 / 1,332", 8).Groups[2].Value, a14 = Row("18 / 1,332", 14).Groups[2].Value;
        Assert.Matches(@"^\d+:\d\d$", a8);
        Assert.Matches(@"^\d+:\d\d$", a14);
        Assert.Contains($"`raider_rush` seed 8 reaches Age II at {a8} (was 8:41) and stands at 20:00 (was destroyed at 13:47), and seed 14 reaches Age II at {a14} (was never) and falls at {d14} (was 14:16)", page, StringComparison.Ordinal);
        Assert.Contains($"seed 8: {a8} | seed 14: {a14} (defeated {d14}) |", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Age II tables' prose: seeds 32 and 45 (Malazan) and seeds 9, 13 and 19 (Whirlwind). D-R39 (2026-11-15, after the
    /// sim's M6-S10): seeds 32, 45 and 9 now reach Age II and stand at 20:00, so each is read as a cell with no defeat and
    /// its sentence says so.
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
        Assert.Contains("| seed 13: never |", w, StringComparison.Ordinal);
        Assert.Contains("seed 13's still stands at 20:00 without Age II", w, StringComparison.Ordinal);
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
