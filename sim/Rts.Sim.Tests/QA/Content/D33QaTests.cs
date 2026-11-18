using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Tests.Content;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D33 (session 2026-11-17-0816). The data hash move and the numbers D33's descriptions quote, checked from QA's side:
/// <list type="bullet">
/// <item><c>main</c>'s data is rebuilt on a copy (the D32 Teblor fixture and D33's two waves put back) and must hash to the
/// diff base's golden header (B74DC7AE61B4862D), so the "before" rows QA measures are really <c>main</c>'s.</item>
/// <item>A save and a recording made on that data <b>with AI seats</b> (the AI's step board is stored, and the Teblor
/// opening grew from 26 to 29 steps) are refused <c>DataMismatch</c> on the shipped data, never thrown, and load on the
/// copy (the refusal is the hash, not a broken file).</item>
/// <item>The two changed waves quote their own numbers and keep D21's cost rule; every opening's description fits D21's 200.</item>
/// </list>
/// </summary>
public class D33QaTests(ITestOutputHelper output)
{
    /// <summary>The golden header at D33's diff base (10acb53).</summary>
    internal const ulong MainHash = 0xB74DC7AE61B4862DUL;

    /// <summary>The hash D33 states.</summary>
    internal const ulong D33Hash = 0x318E8C4256456C05UL;

    private static readonly Lazy<GameData> s_main = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/teblor/ai.json", root => root["openings"]![0] = D32TeblorOpeningReportTests.D32());
        D33BalanceReportTests.PutBackTheWaves(dir);
    }));

    /// <summary><c>main</c>'s data (the diff base), rebuilt from the shipped files.</summary>
    internal static GameData Main => s_main.Value;

    [Fact]
    public void TheRebuiltMainData_HasTheDiffBasesHash_AndTheShippedDataD33s()
    {
        output.WriteLine($"main copy {Main.ContentHash():X16}, shipped {TestSim.Data.ContentHash():X16}");
        Assert.Equal(MainHash, Main.ContentHash());
        Assert.Equal(D33Hash, TestSim.Data.ContentHash());
        int t = Main.FindFaction("teblor");
        Assert.Equal(26, Main.Ai[t].Openings[0].BuildOrder.Length);
        Assert.Equal(29, TestSim.Data.Ai[TestSim.Data.FindFaction("teblor")].Openings[0].BuildOrder.Length);
    }

    /// <summary>Two AI seats (the Teblor <c>standard</c> against the Malazan) on <c>main</c>'s data, the generated map, seed 1, to <paramref name="ticks"/>.</summary>
    private static Simulation MainAiMatch(int ticks, bool record, out ReplayRecorder? rec)
    {
        GameData d = Main;
        PlayerSeat[] seats =
        {
            new(d.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Normal),
            new(d.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal),
        };
        Simulation sim = AiScenes.Generated(seats, 1, record, out rec, data: d);
        Assert.Equal("standard", sim.AiOf(0)!.Opening.Id);
        while (sim.TickNumber < ticks) sim.Tick();
        return sim;
    }

    [Fact]
    public void AnAiSeatSaveFromMain_IsRefusedDataMismatch_NotThrown_AndLoadsOnMainsData()
    {
        // 11:00: past the Teblor's 9:00 / 10:00 steps, so the stored step board is the 26-step one at work.
        Simulation sim = MainAiMatch(13_200, false, out _);
        byte[] bytes = SaveTestRun.Save(sim);
        ulong saved = BitConverter.ToUInt64(bytes, SaveBytes.HeaderOffsets(bytes).DataHash);
        Assert.Equal(MainHash, saved);
        SaveError onShipped = SaveError.None;
        Exception? thrown = Record.Exception(() => onShipped = SaveTestRun.TryLoad(TestSim.Data, bytes));
        output.WriteLine($"save at {sim.TickNumber}, {bytes.Length} bytes: on the shipped data {onShipped}, thrown {thrown?.GetType().Name ?? "none"}");
        Assert.Null(thrown);
        Assert.Equal(SaveError.DataMismatch, onShipped);
        Simulation back = SaveTestRun.Load(Main, bytes);
        Assert.Equal(sim.StateHash(), back.StateHash());
    }

    [Fact]
    public void AnAiSeatRecordingFromMain_IsRefusedBeforeTick1_AndPlaysOnMainsData()
    {
        MainAiMatch(1_200, true, out ReplayRecorder? rec);
        Replay replay = rec!.ToReplay();
        Assert.Equal(MainHash, replay.DataHash);
        ReplayResult refused = ReplayPlayer.Run(replay, TestSim.Data);
        output.WriteLine($"recording of {replay.TickCount} ticks on the shipped data: {refused.Error}, ran {refused.TicksRun}");
        Assert.Equal(ReplayError.DataMismatch, refused.Error);
        Assert.Equal(0, refused.TicksRun);
        ReplayResult ok = ReplayPlayer.Run(replay, Main);
        Assert.True(ok.Ok, $"{ok.Error} at {ok.Tick}");
    }

    /// <summary>D21's rule: <c>minArmyCost</c> is the floor of the opening's Age I mix cost at <c>minArmyPop</c>.</summary>
    [Theory]
    [InlineData("malazan", "cadre_rush", 16, 1184, "The first wave waits for 16 army pop.")]
    [InlineData("whirlwind", "holy_war", 14, 918, "the first wave goes at 14 army pop.")]
    public void TheChangedWave_QuotesItsPop_AndKeepsD21sCostRule(string faction, string id, int pop, int cost, string words)
    {
        GameData d = TestSim.Data;
        AiOpening o = d.Ai[d.FindFaction(faction)].Openings.Single(x => x.Id == id);
        Assert.Equal(pop * 2, o.AttackMinHalfPop);
        Assert.Equal(cost, o.AttackMinCost);
        Assert.Equal(cost, D33BalanceReportTests.AgeOneCost(d, o, pop));
        Assert.EndsWith(words, o.Description, StringComparison.Ordinal);
    }

    /// <summary>Every opening's description fits D21's 200 characters (D33's Teblor sentence is 195).</summary>
    [Fact]
    public void EveryOpeningDescription_FitsD21s200()
    {
        GameData d = TestSim.Data;
        for (int f = 0; f < d.Ai.Length; f++)
            foreach (AiOpening o in d.Ai[f].Openings)
            {
                output.WriteLine($"{d.Factions[f].Key} {o.Id}: {o.Description.Length}");
                Assert.InRange(o.Description.Length, 1, 200);
            }
    }

    /// <summary>
    /// The Teblor page's step table (29 rows) is the file both ways: each row's at / do / id / count is the file's step of
    /// the same number, and no file step lacks a row.
    /// </summary>
    [Fact]
    public void TeblorStepTable_IsTheFile_BothWays()
    {
        string page = File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), "docs", "factions", "teblor.md"));
        int start = page.IndexOf("### Opening 1: `standard` (War band)", StringComparison.Ordinal);
        Assert.True(start >= 0);
        string section = page[start..page.IndexOf("**Army composition", start, StringComparison.Ordinal)];
        var rows = System.Text.RegularExpressions.Regex.Matches(section, @"^\| (\d+) \| (pop|time) (\d+) \| (\w+) \| (?:`(\w+)`|—) \| (\d+) \|", System.Text.RegularExpressions.RegexOptions.Multiline);
        JsonArray order = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", "teblor", "ai.json")))!["openings"]![0]!["buildOrder"]!.AsArray();
        Assert.Equal(order.Count, rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            var g = rows[i].Groups;
            JsonNode s = order[i]!;
            string trigger = s["at"]!.AsObject().Single().Key;
            Assert.Equal((i + 1).ToString(), g[1].Value);
            Assert.Equal(trigger, g[2].Value);
            Assert.Equal((int)s["at"]![trigger]!, int.Parse(g[3].Value));
            Assert.Equal((string)s["do"]!, g[4].Value);
            Assert.Equal((string?)s["id"] ?? "", g[5].Value);
            Assert.Equal(s["count"] is null ? 1 : (int)s["count"]!, int.Parse(g[6].Value));
        }
    }
}
