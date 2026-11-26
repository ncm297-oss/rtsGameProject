using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D38 (BUG-1612): the Teblor <c>standard</c> opening ships D37's three orders through M7-9's <c>buildOrders</c>
/// (<see cref="D38TeblorOrders"/>): the file is the three fixtures step for step, each difficulty's seat resolves and plays
/// its own list, the data hash moved once (from the base, D34's order without <c>buildOrders</c>), and teblor.md's three
/// step tables are their lists both ways. The 64-seat rows on the shipped file are <see cref="D38ShippedOrdersReportTests"/>.
/// </summary>
public class D38ShippedOrdersTests(ITestOutputHelper output)
{
    private static GameData Data => TestSim.Data;

    private static JsonObject Shipped() => TeblorOpeningVariantsReportTests.Shipped();

    // ---- criterion 1: the file is the three fixtures

    [Fact]
    public void TheShippedOpening_IsTheThreeD37Fixtures_StepForStep()
    {
        List<string> problems = D38TeblorOrders.Problems(Shipped());
        Assert.True(problems.Count == 0, string.Join("; ", problems));
        // The lists differ from each other (no entry is dead weight: docs/03 M7-9 Question 4).
        JsonObject o = Shipped();
        foreach (string a in D38TeblorOrders.Difficulties)
            foreach (string b in D38TeblorOrders.Difficulties)
                if (string.CompareOrdinal(a, b) < 0)
                    Assert.False(JsonNode.DeepEquals(D38TeblorOrders.ListOf(o, a), D38TeblorOrders.ListOf(o, b)), $"{a} and {b} play the same list");
    }

    /// <summary>The check names each way the shipped file can stop being the fixtures: a step, a switch, a key, the place of the field.</summary>
    [Fact]
    public void TheFixtureCheck_NamesEachWayTheFileCanDrift()
    {
        JsonObject Edited(Action<JsonObject> edit)
        {
            JsonObject o = Shipped();
            edit(o);
            return o;
        }
        Assert.Contains("buildOrders.easy differs from the D37 easy fixture",
            D38TeblorOrders.Problems(Edited(o => o["buildOrders"]!["easy"]![17]!["at"] = new JsonObject { ["time"] = 610 })));
        Assert.Contains("buildOrders.hard differs from the D37 hard fixture",
            D38TeblorOrders.Problems(Edited(o => o["buildOrders"]!["hard"]![21]!.AsObject().Remove("throughReserve"))));
        Assert.Contains("buildOrder differs from the D37 normal fixture",
            D38TeblorOrders.Problems(Edited(o => o["buildOrder"]![26]!["count"] = 2)));
        Assert.Contains("buildOrders holds 'easy, hard, normal', expected 'easy, hard'",
            D38TeblorOrders.Problems(Edited(o => o["buildOrders"]!["normal"] = o["buildOrder"]!.DeepClone())));
        Assert.Contains("buildOrders holds 'hard, easy', expected 'easy, hard'",
            D38TeblorOrders.Problems(Edited(o =>
            {
                JsonNode easy = o["buildOrders"]!["easy"]!.DeepClone();
                o["buildOrders"]!.AsObject().Remove("easy");
                o["buildOrders"]!["easy"] = easy;
            })));
        Assert.Contains("buildOrders is not placed right after buildOrder",
            D38TeblorOrders.Problems(Edited(o =>
            {
                JsonNode lists = o["buildOrders"]!.DeepClone();
                o.Remove("buildOrders");
                o["buildOrders"] = lists;
            })));
        Assert.Contains("no buildOrders field", D38TeblorOrders.Problems(Edited(o => o.Remove("buildOrders"))));
    }

    // ---- criterion 2: each difficulty resolves and plays its list

    /// <summary>A fixture's order as the loader reads it (on a shipped copy whose <c>standard</c> is the fixture).</summary>
    private static ImmutableArray<AiStep> LoadedFixture(string difficulty) =>
        TeblorOpeningVariantsReportTests.DataWith(D38TeblorOrders.Fixture(difficulty)).Ai[Data.FindFaction("teblor")].Openings[0].BuildOrder;

    [Fact]
    public void EachDifficulty_ResolvesTheListOfItsFixture()
    {
        AiOpening o = Data.Ai[Data.FindFaction("teblor")].Openings[0];
        Assert.Equal("standard", o.Id);
        foreach (string key in D38TeblorOrders.Difficulties)
        {
            AiDifficulty x = D38TeblorOrders.Difficulty(key);
            Assert.True(LoadedFixture(key).SequenceEqual(o.BuildOrderFor(x)), $"{x} does not resolve the D37 {key} fixture's list");
        }
        Assert.True(o.BuildOrder.SequenceEqual(o.BuildOrderFor(AiDifficulty.Normal)), "Normal does not play buildOrder");
    }

    /// <summary>
    /// In a match, a Teblor seat of each difficulty plays its own list (<c>AiPlayer.Steps</c>), the Easy seat on the first
    /// opening, which is the one carrying the Easy entry (Easy always plays opening 0: docs/03 "For the data track (M7-9)").
    /// </summary>
    [Fact]
    public void ASeatOfEachDifficulty_PlaysItsOwnList()
    {
        int teblor = Data.FindFaction("teblor");
        JsonNode file = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", "teblor", "ai.json")))!;
        JsonArray openings = file["openings"]!.AsArray();
        for (int k = 0; k < openings.Count; k++)
            if (openings[k]!["buildOrders"]?["easy"] != null) Assert.Equal(0, k); // an Easy entry anywhere else is never played
        Assert.NotNull(openings[0]!["buildOrders"]?["easy"]);
        var played = new List<ImmutableArray<AiStep>>();
        foreach (string key in D38TeblorOrders.Difficulties)
        {
            AiDifficulty x = D38TeblorOrders.Difficulty(key);
            PlayerSeat[] seats = { new(teblor, SeatController.Ai, x), new(Data.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal) };
            Simulation sim = AiScenes.Generated(seats, 1, false, out _, data: Data);
            AiPlayer ai = sim.AiOf(0)!;
            Assert.Equal(0, ai.OpeningIndex);
            Assert.True(LoadedFixture(key).SequenceEqual(ai.Steps), $"the {x} seat does not play the D37 {key} fixture's list");
            played.Add(ai.Steps);
        }
        Assert.False(played[0].SequenceEqual(played[1]));
        Assert.False(played[1].SequenceEqual(played[2]));
        Assert.False(played[0].SequenceEqual(played[2]));
    }

    // ---- criterion 5: the hash moved once (differential; no literal of the shipped hash, rules 49 / 73)

    /// <summary>
    /// The shipped hash is the golden's header (read, not typed). The base's data is D34's order as <c>buildOrder</c> and no
    /// <c>buildOrders</c>: that copy is D34's hash with the draw block out (D34's own pin), and with the block in it is not
    /// the shipped hash, so D38 moved it; the three lists each move it (Normal's alone, then each entry).
    /// </summary>
    [Fact]
    public void TheHashMovedOnce_FromD34sOrderWithoutBuildOrders()
    {
        ulong shipped = Data.ContentHash();
        string golden = File.ReadAllText(ReplayTestRun.GoldenPath);
        Assert.Contains("data-hash " + shipped.ToString("X16"), golden);

        JsonObject d34 = TeblorOpeningFixtureTests.D34Fixture();
        JsonObject asBase = Shipped();
        asBase.Remove("buildOrders");
        asBase["buildOrder"] = d34["buildOrder"]!.DeepClone();
        Assert.True(JsonNode.DeepEquals(asBase, d34), "the shipped opening without D38's lists is not the D34 fixture");
        ulong baseHash = TeblorOpeningVariantsReportTests.DataWith(asBase).ContentHash();
        Assert.NotEqual(shipped, baseHash);
        Assert.DoesNotContain("data-hash " + baseHash.ToString("X16"), golden);
        Assert.Equal(QA.Content.D34QaTests.D34Hash, Draw.DrawScenes.TeblorOpeningBeforeDraw(asBase).ContentHash());
        output.WriteLine($"shipped {shipped:X16} (the golden's header); the base's data (D34's order, no buildOrders) {baseHash:X16}");

        JsonObject normalOnly = Shipped();
        normalOnly.Remove("buildOrders");
        ulong normal = TeblorOpeningVariantsReportTests.DataWith(normalOnly).ContentHash();
        JsonObject easyOnly = Shipped();
        easyOnly["buildOrders"]!.AsObject().Remove("hard");
        ulong easy = TeblorOpeningVariantsReportTests.DataWith(easyOnly).ContentHash();
        Assert.Equal(shipped, TeblorOpeningVariantsReportTests.DataWith(Shipped()).ContentHash());
        var hashes = new[] { baseHash, normal, easy, shipped };
        Assert.Equal(hashes.Length, hashes.Distinct().Count());
    }

    // ---- criterion 6: the page's three step tables are their lists both ways

    /// <summary>The page with the row <paramref name="row"/> of the step table of <paramref name="key"/> (null: Normal's) edited.</summary>
    private static string[] PageWithRow(string? key, int row, string find, string replace)
    {
        string[] lines = PageTables.FactionLines("teblor");
        int from = key == null
            ? Array.FindIndex(lines, l => l.StartsWith("### Opening 1: `standard`", StringComparison.Ordinal))
            : Array.FindIndex(lines, l => l.StartsWith("**", StringComparison.Ordinal) && l.Contains(AiContentTests.EntryLabel(key), StringComparison.Ordinal));
        Assert.True(from >= 0, $"teblor.md has no table for {key ?? "buildOrder"}");
        int i = Array.FindIndex(lines, from, l => l.StartsWith($"| {row} |", StringComparison.Ordinal));
        Assert.True(i >= 0 && lines[i].Contains(find, StringComparison.Ordinal), $"teblor.md {key ?? "buildOrder"} row {row} lacks '{find}'");
        lines[i] = lines[i].Replace(find, replace, StringComparison.Ordinal);
        return lines;
    }

    [Theory]
    [InlineData(null, "teblor standard step 18: page 'time 610 | train | teblor_breaker | 1' vs data 'time 600 | train | teblor_breaker | 1'")]
    [InlineData("easy", "teblor standard (easy) step 18: page 'time 610 | train | teblor_breaker | 1' vs data 'time 600 | train | teblor_breaker | 1'")]
    [InlineData("hard", "teblor standard (hard) step 18: page 'time 610 | train | teblor_breaker | 1' vs data 'time 600 | train | teblor_breaker | 1'")]
    public void APageCellChanged_InEachTable_IsNamed(string? key, string want)
    {
        Assert.Empty(AiContentTests.Problems(Data, "teblor", PageTables.FactionLines("teblor")));
        Assert.Contains(want, AiContentTests.Problems(Data, "teblor", PageWithRow(key, 18, "| time 600 |", "| time 610 |")));
    }

    /// <summary>A switch taken off a row of each table, and an entry's table labelled with the other key, are named.</summary>
    [Fact]
    public void APageSwitchOrLabelChanged_IsNamed()
    {
        string through = " " + AiContentTests.ThroughReserveWords;
        Assert.Contains("teblor standard step 21: page 'time 600 | train | teblor_destrier_rider | 1' vs data 'time 600 | train | teblor_destrier_rider | 1 (through the reserve)'",
            AiContentTests.Problems(Data, "teblor", PageWithRow(null, 21, through, "")));
        Assert.Contains("teblor standard (easy) step 22: page 'time 540 | train | teblor_shaman | 1' vs data 'time 540 | train | teblor_shaman | 1 (through the reserve)'",
            AiContentTests.Problems(Data, "teblor", PageWithRow("easy", 22, through, "")));
        Assert.Contains("teblor standard (hard) step 22: page 'time 600 | train | teblor_war_dog | 1' vs data 'time 600 | train | teblor_war_dog | 1 (through the reserve)'",
            AiContentTests.Problems(Data, "teblor", PageWithRow("hard", 22, through, "")));
        string[] lines = PageTables.FactionLines("teblor");
        int label = Array.FindIndex(lines, l => l.StartsWith("**", StringComparison.Ordinal) && l.Contains(AiContentTests.EntryLabel("easy"), StringComparison.Ordinal));
        lines[label] = lines[label].Replace(AiContentTests.EntryLabel("easy"), "`buildOrders.normal`", StringComparison.Ordinal);
        Assert.Contains(AiContentTests.Problems(Data, "teblor", lines), p => p.StartsWith("teblor standard (easy): the table after the composition's 1 is not labelled `buildOrders.easy`", StringComparison.Ordinal));
    }

    /// <summary>The file side: a step of each list changed in a copy of <c>ai.json</c> fails against the unchanged page, naming the list.</summary>
    [Theory]
    [InlineData(null, 17, "teblor standard step 18: page 'time 600 | train | teblor_breaker | 1' vs data 'time 610 | train | teblor_breaker | 1'")]
    [InlineData("easy", 17, "teblor standard (easy) step 18: page 'time 600 | train | teblor_breaker | 1' vs data 'time 610 | train | teblor_breaker | 1'")]
    [InlineData("hard", 17, "teblor standard (hard) step 18: page 'time 600 | train | teblor_breaker | 1' vs data 'time 610 | train | teblor_breaker | 1'")]
    public void AFileStepChanged_InEachList_IsNamed(string? key, int step, string want)
    {
        GameData d = AiScenes.DataWithAi("teblor", root =>
        {
            JsonNode o = root["openings"]![0]!;
            JsonArray list = (key == null ? o["buildOrder"] : o["buildOrders"]![key])!.AsArray();
            list[step]!["at"] = new JsonObject { ["time"] = 610 };
        });
        List<string> problems = AiContentTests.Problems(d, "teblor", PageTables.FactionLines("teblor"));
        Assert.Contains(want, problems);
        Assert.Single(problems);
    }

    /// <summary>The file side of a switch: Hard's War-dog step without its switch is named on Hard's table only.</summary>
    [Fact]
    public void AFileSwitchRemoved_IsNamedOnItsTable()
    {
        GameData d = AiScenes.DataWithAi("teblor", root => root["openings"]![0]!["buildOrders"]!["hard"]![21]!.AsObject().Remove("throughReserve"));
        List<string> problems = AiContentTests.Problems(d, "teblor", PageTables.FactionLines("teblor"));
        Assert.Equal(new[] { "teblor standard (hard) step 22: page 'time 600 | train | teblor_war_dog | 1 (through the reserve)' vs data 'time 600 | train | teblor_war_dog | 1'" }, problems);
    }
}
