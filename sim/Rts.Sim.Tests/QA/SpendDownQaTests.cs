using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.Tests.Content;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M6-S6 re-check round 1 (2026-11-05-1206): attacks on BUG-1050's spend-down rule (a preset's <c>aiSpendAbove</c>).
/// Unseen seeds against the brief's measure (is 200 tuned to the measured seeds?), save/load and replay of a <c>high</c> AI
/// match while it spends down, the rule's per-tick allocation, and extreme thresholds against extreme starts.
/// </summary>
[Collection(SerialCollection.Name)]
public class SpendDownQaTests
{
    private const int Tps = SimConstants.TicksPerSecond;
    private readonly ITestOutputHelper _out;

    public SpendDownQaTests(ITestOutputHelper output) => _out = output;

    // ---------- the brief's measure on seeds nobody tuned on ----------

    /// <summary>First seed scanned: above the 1-50 the dev's pairings and QA's round-0 rows use.</summary>
    internal const ulong UnseenFrom = 101;

    /// <summary>The first <paramref name="count"/> seeds from <see cref="UnseenFrom"/> on which <paramref name="player"/>'s Normal seat picks <paramref name="opening"/>.</summary>
    private static List<ulong> UnseenSeeds(int player, int opening, int count)
    {
        var seeds = new List<ulong>();
        for (ulong seed = UnseenFrom; seed < UnseenFrom + 400 && seeds.Count < count; seed++)
            if (AiOpeningSeeds.Pick(seed, player, AiDifficulty.Normal) == opening) seeds.Add(seed);
        Assert.Equal(count, seeds.Count);
        return seeds;
    }

    /// <summary>
    /// Each of the six openings on six seeds from 101 up (36 sides none of the tuning saw), Normal v Normal, menu map, 10
    /// minutes at <c>standard</c> and <c>high</c>. Asserted per side: the mean bank 2:00-10:00 at <c>high</c> at most 1.5
    /// times <c>standard</c>'s (the start is spent: 0.32-1.44 on the re-check). The brief's measure (the bank at 10:00 at
    /// most twice) is printed ("OVER2X"), not asserted: on the re-check 4 of these 36 sides were over (BUG-1050, the 10:00
    /// sample's noise; the Producer restates the measure). Lines "QAUNSEEN" are read by the QA report.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(StartingResourcesAiQaTests.Openings), MemberType = typeof(StartingResourcesAiQaTests))]
    public void SixUnseenSeedsPerOpening_AtHigh_SpendTheStart(int player, int opening)
    {
        var problems = new List<string>();
        foreach (ulong seed in UnseenSeeds(player, opening, 6))
        {
            var s = StartingResourcesAiQaTests.Play(seed, "standard");
            var h = StartingResourcesAiQaTests.Play(seed, "high");
            Assert.Equal(opening, h.Opening[player]);
            int p = player;
            string side = p == 0 ? "malazan" : "whirlwind";
            bool over = h.Bank10[p] > 2 * s.Bank10[p];
            double ratio = s.Mean[p] == 0 ? double.PositiveInfinity : (double)h.Mean[p] / s.Mean[p];
            _out.WriteLine($"QAUNSEEN {side} {h.Names[p]} seed {seed}: bank 5:00 {s.Bank5[p]} / {h.Bank5[p]}; 10:00 {s.Bank10[p]} / {h.Bank10[p]}{(over ? " OVER2X" : "")}; "
                + $"mean 2-10 {s.Mean[p]} / {h.Mean[p]} (x{ratio:0.00}); steps by 5:00 {s.Steps5[p]} / {h.Steps5[p]}; Age II {StartingResourcesAiQaTests.Sec(s.AgeTwo[p])} / {StartingResourcesAiQaTests.Sec(h.AgeTwo[p])}; "
                + $"waves {s.Waves[p]} / {h.Waves[p]}; defeated {s.Defeated[p]} / {h.Defeated[p]}");
            if (ratio > 1.5) problems.Add($"{side} {h.Names[p]} seed {seed}: mean bank high {h.Mean[p]}, over 1.5 x standard's {s.Mean[p]}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ---------- the threshold's boundary ----------

    /// <summary>The dev's SpendDownTests scene: the Malazan build order trains a Laborer (50 gold) every think, then two far time steps.</summary>
    private static readonly Lazy<GameData> s_boundary = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(
                    ("time", 0, "train", "malazan_laborer", 30),
                    ("time", 600, "build", "malazan_billet", 0),
                    ("time", 700, "build", "malazan_billet", 0));
                o["compositions"] = JsonNode.Parse("""[{ "age": 1, "units": { "malazan_heavy_infantry": 1 } }, { "age": 2, "units": { "malazan_heavy_infantry": 1 } }]""");
            }
        });
        dir.EditJson("common/rules.json", r => r["ai"]!["scoutAfter"] = 3600);
    }));

    /// <summary>
    /// The rule is on strictly above <c>aiSpendAbove</c>, counted after the commands already sent this think: a bank of
    /// <paramref name="gold"/> gold and no wood, less the 50-gold Laborer the build order sends first, leaves
    /// <paramref name="gold"/> - 50; at exactly 200 the far time step stays not due, at 201 it comes due this think. (Kills
    /// the <c>&gt;=</c> mutant the dev's at-the-threshold row lets live: its 150 / 50 drops to 150 after the Laborer.)
    /// </summary>
    [Theory]
    [InlineData(249, false)]
    [InlineData(250, false)]
    [InlineData(251, true)]
    public void TheThreshold_IsStrict_AfterWhatTheThinkAlreadySpent(int gold, bool spending)
    {
        GameData d = s_boundary.Value;
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human), d, startingResources: "high");
        Assert.True(sim.World.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), 44 * AiScenes.FlatSize + 8, out _));
        AiPlayer ai = sim.AiOf(0)!;
        Assert.Equal(50, d.Units[d.FindUnit("malazan_laborer")].CostGold);
        (bool Spending, int Bank) seen = default;
        ai.ThoughtForTests = a => seen = (a.SpendingDown, a.Gold + a.Wood);
        while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
        BuildMaps.SetTotals(sim, 0, gold, 0);
        sim.Tick();
        Assert.Equal(gold - 50, seen.Bank);
        Assert.Equal(spending, seen.Spending);
        Assert.Equal(spending, ai.Board.StepDueTick[1] >= 0);
    }

    // ---------- save / load and replay while spending down ----------

    /// <summary>
    /// A <c>high</c> Normal v Normal menu-map match (seed 13: Malazan <c>cadre_rush</c>, the opening that held the start),
    /// saved at 0:10, 1:00 and 3:00 (both seats above the 200 threshold at the first two, so the rule is live across the
    /// save), each load playing to 8:00 hash-equal to the unsaved run every tick. The threshold is rebuilt from the header.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void AHighAiMatch_SavedWhileSpendingDown_PlaysOnAsTheUnsavedRun()
    {
        var seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = StartingResourcesAiQaTests.MenuMap(seats, 13, "high");
        int[] saveAt = { 10 * Tps, 60 * Tps, 180 * Tps };
        const int end = 8 * 60 * Tps;
        var loads = new List<Simulation>();
        int spendingAtSave = 0;
        while (sim.TickNumber < end)
        {
            sim.Tick();
            if (Array.IndexOf(saveAt, sim.TickNumber) >= 0)
            {
                for (int p = 0; p < 2; p++) if (sim.World.Gold[p] + sim.World.Wood[p] > 200) spendingAtSave++;
                Simulation loaded = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(sim));
                Assert.Equal("high", loaded.World.Config.StartingResources);
                for (int p = 0; p < 2; p++) Assert.Equal(200, loaded.AiOf(p)!.SpendAbove);
                // The early-due steps survive: the loaded board's due ticks equal the live one's.
                for (int p = 0; p < 2; p++) Assert.Equal(sim.AiOf(p)!.Board.StepDueTick, loaded.AiOf(p)!.Board.StepDueTick);
                Assert.Equal(sim.StateHash(), loaded.StateHash());
                loads.Add(loaded);
            }
            foreach (Simulation l in loads)
            {
                if (l.TickNumber >= sim.TickNumber) continue;
                l.Tick();
                Assert.True(sim.StateHash() == l.StateHash(), $"tick {sim.TickNumber}: a load diverged from the unsaved run");
            }
        }
        Assert.Equal(3, loads.Count);
        Assert.True(spendingAtSave >= 3, $"only {spendingAtSave} seat-saves above the threshold: the save didn't cut the rule live");
    }

    /// <summary>
    /// A recorded <c>high</c> 1v1 Hard AI match on a generated map, 8,000 ticks, read back from text, plays to every
    /// checkpoint; played as <c>standard</c> it diverges at the first (the rule and the bank both move it).
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void AHighAiRecording_PlaysToEveryCheckpoint()
    {
        var seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, 11, record: true, out ReplayRecorder? rec, configure: c => c with { StartingResources = "high" });
        for (int t = 0; t < 8000; t++) sim.Tick();
        Replay replay = rec!.ToReplay();
        Assert.Equal(8, replay.FormatVersion);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult played = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.True(back!.Checkpoints.Length >= 30);
        ReplayResult asStandard = ReplayPlayer.Run(ReplayTestRun.With(back, formatVersion: 8, startingResources: "standard"), TestSim.Data);
        Assert.Equal(ReplayError.CheckpointMismatch, asStandard.Error);
    }

    // ---------- allocation ----------

    /// <summary>
    /// The spend-down path allocates nothing: a high match's first minute (both seats above 200) and a four-seat match whose
    /// threshold is 1 at a 1,000,000 start (the rule live every think, production looping over every producer).
    /// </summary>
    [Fact]
    public void TheSpendDownPath_Allocates0Bytes()
    {
        var two = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Ai);
        Simulation high = AiScenes.Generated(two, 2, record: false, out _, configure: c => c with { StartingResources = "high" });
        for (int t = 0; t < 200; t++) high.Tick();
        for (int p = 0; p < 2; p++) Assert.True(high.World.Gold[p] + high.World.Wood[p] > 200);
        AllocationProbe.AssertZero(() => { for (int t = 0; t < 100; t++) high.Tick(); }, _out);
        for (int p = 0; p < 2; p++) Assert.True(high.AiOf(p)!.SpendingDown || high.World.Gold[p] + high.World.Wood[p] <= 200);

        var four = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Ai, SeatController.Ai, SeatController.Ai);
        Simulation max = AiScenes.Generated(four, 5, record: false, out _, configure: c => c with { Data = s_edges.Value, StartingResources = "max_low" });
        for (int t = 0; t < 2400; t++) max.Tick();
        for (int p = 0; p < 4; p++) Assert.True(max.AiOf(p)!.SpendingDown);
        bool spending = false;
        AllocationProbe.AssertZero(() => { for (int t = 0; t < 100; t++) max.Tick(); spending = max.AiOf(0)!.SpendingDown; }, _out);
        Assert.True(spending);
    }

    // ---------- extreme thresholds and starts ----------

    private static readonly Lazy<GameData> s_edges = new(() => AiScenes.DataWith(dir => dir.EditJson("common/rules.json", r =>
    {
        int max = DataLimits.MaxInteger;
        r["startingResources"] = JsonNode.Parse(
            "[{\"id\":\"standard\",\"displayName\":\"S\",\"gold\":200,\"wood\":200},"
            + "{\"id\":\"zero_low\",\"displayName\":\"A\",\"gold\":0,\"wood\":0,\"aiSpendAbove\":1},"
            + $"{{\"id\":\"zero_high\",\"displayName\":\"B\",\"gold\":0,\"wood\":0,\"aiSpendAbove\":{max}}},"
            + $"{{\"id\":\"max_low\",\"displayName\":\"C\",\"gold\":{max},\"wood\":{max},\"aiSpendAbove\":1}},"
            + $"{{\"id\":\"max_high\",\"displayName\":\"D\",\"gold\":{max},\"wood\":{max},\"aiSpendAbove\":{max}}}]");
    })));

    /// <summary>
    /// Four Hard AIs, 6,000 ticks, at <c>aiSpendAbove</c> 1 and 1,000,000 with starts 0 / 0 and 1,000,000 / 1,000,000: no
    /// throw, twin runs hash-equal every 100 ticks, banks never negative, no queue past the engine's capacity, and no
    /// producer's queue past <c>ai.spendDownQueue</c> plus what the build order may add (one a think,
    /// judged), i.e. the queues don't run away.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("zero_low")]
    [InlineData("zero_high")]
    [InlineData("max_low")]
    [InlineData("max_high")]
    public void ExtremeThresholds_AtExtremeStarts_AreDeterministic_AndBounded(string preset)
    {
        GameData data = s_edges.Value;
        var seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Ai, SeatController.Ai, SeatController.Ai);
        Simulation a = AiScenes.Generated(seats, 5, record: false, out _, configure: c => c with { Data = data, StartingResources = preset });
        Simulation b = AiScenes.Generated(seats, 5, record: false, out _, configure: c => c with { Data = data, StartingResources = preset });
        int threshold = a.World.Config.StartingPreset.AiSpendAbove;
        for (int p = 0; p < 4; p++) Assert.Equal(threshold, a.AiOf(p)!.SpendAbove);
        int maxQueue = 0, thinksSpending = 0;
        for (int t = 0; t < 6000; t++)
        {
            a.Tick();
            b.Tick();
            var bs = a.World.Buildings;
            for (int s = 0; s < bs.Capacity; s++)
                if (bs.Alive[s]) maxQueue = Math.Max(maxQueue, bs.QueueCount[s]);
            for (int p = 0; p < 4; p++)
            {
                Assert.True(a.World.Gold[p] >= 0 && a.World.Wood[p] >= 0, $"tick {a.TickNumber} p{p}: negative bank");
                if (a.AiOf(p)!.SpendingDown) thinksSpending++;
            }
            if (t % 100 == 0) Assert.Equal(a.StateHash(), b.StateHash());
        }
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.True(maxQueue <= Rts.Sim.Economy.EconomyConstants.ProductionQueueCapacity);
        _out.WriteLine($"{preset}: max queue {maxQueue}, seat-ticks spending down {thinksSpending}");
        for (int p = 0; p < 4; p++)
            _out.WriteLine($"  p{p}: gold {a.World.Gold[p]} wood {a.World.Wood[p]} pop {a.World.HalfPop[p] / 2.0}/{a.World.HalfPopCap[p] / 2.0} waves {a.AiOf(p)!.Board.WavesStarted}");
        // max_high: 2,000,000 > 1,000,000, so the rule is live; zero_high never (a bank can't reach 1,000,000 in 5 minutes).
        if (preset == "zero_high") Assert.Equal(0, thinksSpending);
        if (preset == "max_low" || preset == "max_high") Assert.True(thinksSpending > 0);
    }
}
