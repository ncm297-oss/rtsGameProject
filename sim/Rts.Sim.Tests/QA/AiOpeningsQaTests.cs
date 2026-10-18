using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M5-8a, session 2026-10-17-2344, full tier): named AI openings and BUG-0910, beyond the developer's rows. Hostile
/// <c>openings</c> files (half-converted, null entries, a non-list, a third duplicate, ids that are not snake_case, errors in
/// two openings at once, an unknown field); the pick's RNG budget (exactly one draw on Normal / Hard, none on Easy, and no AI
/// manager draws after it, so no older recording's AI moves); whole-match twins and a replay with three openings of 1, 26
/// and 256 steps picked across four seats; the Blackboard sized by each of them; the file order in the hash and in Easy's
/// pick; and BUG-0910's two sides on every difficulty (a requirement-blocked step goes out within 2 thinks, a real no-site
/// search still waits <see cref="BuildOrderExecutor.NoSiteRetryThinks"/> thinks).
/// </summary>
public class AiOpeningsQaTests
{
    private const string MalazanAi = "factions/malazan/ai.json";
    private readonly ITestOutputHelper _out;

    public AiOpeningsQaTests(ITestOutputHelper output) => _out = output;

    private static DataLoadResult LoadWith(Action<JsonObject> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, edit);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        if (r.Errors.Count > 0) Assert.Null(r.Data);
        return r;
    }

    private static DataLoadResult LoadText(Func<string, string> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string path = dir.FullPath(MalazanAi);
        File.WriteAllText(path, edit(File.ReadAllText(path)));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        if (r.Errors.Count > 0) Assert.Null(r.Data);
        return r;
    }

    /// <summary>
    /// The shipped openings list cut to its first opening (BUG-0980), so a fixture that appends openings has exactly the
    /// indices and count it pins, whatever count the shipped file holds.
    /// </summary>
    private static JsonArray FirstOnly(JsonObject root)
    {
        JsonArray openings = root["openings"]!.AsArray();
        while (openings.Count > 1) openings.RemoveAt(openings.Count - 1);
        return openings;
    }

    private static JsonNode CopyOf(JsonObject root, string id)
    {
        JsonNode copy = root["openings"]![0]!.DeepClone();
        copy["id"] = id;
        return copy;
    }

    private string Dump(DataLoadResult r)
    {
        string s = string.Join("\n", r.Errors.Select(e => $"{e.File} {e.Path}: {e.Message}"));
        _out.WriteLine(s);
        return s;
    }

    // ---------- the loader on hostile openings ----------

    /// <summary>
    /// A half-converted pre-M5-8a file: the three old top-level fields kept and no <c>openings</c>. Four errors, one per old field
    /// (naming openings[]) and the missing list; nothing loads.
    /// </summary>
    [Fact]
    public void AnUnconvertedFile_FourErrors_OnePerOldField_AndTheMissingList()
    {
        DataLoadResult r = LoadWith(root =>
        {
            JsonNode o = root["openings"]![0]!;
            root["buildOrder"] = o["buildOrder"]!.DeepClone();
            root["compositions"] = o["compositions"]!.DeepClone();
            root["attack"] = o["attack"]!.DeepClone();
            root.Remove("openings");
        });
        string all = Dump(r);
        Assert.Equal(4, r.Errors.Count);
        Assert.All(r.Errors, e => Assert.Equal(MalazanAi, e.File));
        foreach (string f in new[] { "buildOrder", "compositions", "attack" })
            Assert.Contains(r.Errors, e => e.Path == f && e.Message.Contains("moved into openings[]"));
        Assert.Contains(r.Errors, e => e.Path == "openings" && e.Message.Contains("missing required field"));
        Assert.DoesNotContain("unknown", all, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A stale field written as JSON null at the top level is still the old field: refused, not silently loaded (an unknown
    /// field set to null is an error everywhere else). Skipped with its bug id: the loader reads it as absent.
    /// </summary>
    [Theory(Skip = "BUG-0940: a top-level \"buildOrder\": null (or compositions / attack) loads with no error")]
    [InlineData("buildOrder")]
    [InlineData("compositions")]
    [InlineData("attack")]
    public void AnOldTopLevelFieldSetToNull_IsRefused(string field)
    {
        DataLoadResult r = LoadWith(root => root[field] = null);
        Dump(r);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(field, e.Path);
    }

    /// <summary>For comparison: an unknown top-level field set to null is an error (the rule the stale-null case should follow).</summary>
    [Fact]
    public void AnUnknownTopLevelFieldSetToNull_IsAnError()
    {
        DataLoadResult r = LoadWith(root => root["buildOrders"] = null);
        Dump(r);
        Assert.NotEmpty(r.Errors);
    }

    /// <summary>Openings that are not a list of objects: null entries, a list of nulls, an object, a string, a number.</summary>
    [Theory]
    [InlineData("[null]", "openings[0]")]
    [InlineData("[{OPENING}, null]", "openings[1]")]
    [InlineData("{}", "")]
    [InlineData("\"standard\"", "")]
    [InlineData("3", "")]
    [InlineData("null", "openings")]
    public void OpeningsOfTheWrongShape_AreErrors_NeverACrash(string json, string path)
    {
        DataLoadResult r = LoadText(text =>
        {
            JsonObject root = JsonNode.Parse(text)!.AsObject();
            string first = root["openings"]![0]!.ToJsonString();
            root["openings"] = JsonNode.Parse(json.Replace("{OPENING}", first));
            return root.ToJsonString();
        });
        Dump(r);
        Assert.NotEmpty(r.Errors);
        Assert.Null(r.Data);
        if (path.Length > 0) Assert.Contains(r.Errors, e => e.Path == path);
        Assert.All(r.Errors, e => Assert.Equal(MalazanAi, e.File));
    }

    /// <summary>Three openings, the third a second <c>standard</c>: one error at openings[2].id naming openings[0]; also id case variants.</summary>
    [Theory]
    [InlineData("standard", "duplicate id 'standard' (also openings[0])")]
    [InlineData("second", "duplicate id 'second' (also openings[1])")]
    [InlineData("Standard", "is not snake_case")]
    [InlineData("1st_rush", "is not snake_case")]
    [InlineData("_rush", "is not snake_case")]
    [InlineData("fast-rush", "is not snake_case")]
    [InlineData("fast rush", "is not snake_case")]
    [InlineData("", "missing required text")]
    public void AThirdOpeningsBadId_IsOneErrorAtItsId(string id, string message)
    {
        DataLoadResult r = LoadWith(root =>
        {
            JsonArray openings = FirstOnly(root);
            openings.Add(CopyOf(root, "second"));
            openings.Add(CopyOf(root, id));
        });
        Dump(r);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal("openings[2].id", e.Path);
        Assert.Contains(message, e.Message);
    }

    /// <summary>Faults in two openings are both reported (the loader doesn't stop at the first opening), each at its own path.</summary>
    [Fact]
    public void FaultsInTwoOpenings_AreBothReported_AtTheirPaths()
    {
        DataLoadResult r = LoadWith(root =>
        {
            JsonArray openings = FirstOnly(root);
            JsonNode second = CopyOf(root, "second"), third = CopyOf(root, "third");
            second["compositions"]![0]!["units"] = new JsonObject { ["malazan_ghost"] = 1 };
            third.AsObject().Remove("buildOrder");
            openings.Add(second);
            openings.Add(third);
        });
        Dump(r);
        Assert.Equal(2, r.Errors.Count);
        Assert.Contains(r.Errors, e => e.Path.StartsWith("openings[1].compositions[0]", StringComparison.Ordinal) && e.Message.Contains("malazan_ghost"));
        Assert.Contains(r.Errors, e => e.Path == "openings[2].buildOrder" && e.Message.Contains("missing required field"));
    }

    /// <summary>An unknown field inside an opening (e.g. a weight the schema doesn't have) is an error, as everywhere.</summary>
    [Fact]
    public void AnUnknownFieldInAnOpening_IsAnError()
    {
        DataLoadResult r = LoadWith(root => root["openings"]![0]!["weight"] = 2);
        Dump(r);
        Assert.NotEmpty(r.Errors);
        Assert.Contains(r.Errors, e => e.File == MalazanAi);
    }

    /// <summary>Exactly 8 openings load (the maximum is inclusive); each keeps its file index.</summary>
    [Fact]
    public void EightOpenings_Load_InFileOrder()
    {
        DataLoadResult r = LoadWith(root =>
        {
            JsonArray openings = FirstOnly(root);
            for (int k = 1; k < DataLimits.MaxAiOpenings; k++) openings.Add(CopyOf(root, $"opening_{k}"));
        });
        Assert.True(r.Ok, Dump(r));
        AiDef ai = r.Data!.Ai[r.Data.FindFaction("malazan")];
        Assert.Equal(8, ai.Openings.Length);
        for (int k = 1; k < 8; k++) Assert.Equal(k, ai.FindOpening($"opening_{k}"));
        Assert.Equal(-1, ai.FindOpening("nope"));
    }

    /// <summary>
    /// The file order is content: swapping two openings in the file moves <see cref="GameData.ContentHash"/>, and Easy plays
    /// whichever is now first.
    /// </summary>
    [Fact]
    public void SwappingTwoOpeningsInTheFile_MovesTheHash_AndEasyPlaysTheNewFirst()
    {
        GameData ab = AiScenes.DataWithAi("malazan", root =>
        {
            JsonNode b = CopyOf(root.AsObject(), "rush");
            b["buildOrder"] = AiScenes.BuildOrder(("time", 0, "build", "malazan_billet", 0));
            FirstOnly(root.AsObject()).Add(b);
        });
        GameData ba = AiScenes.DataWithAi("malazan", root =>
        {
            JsonArray openings = root["openings"]!.AsArray();
            JsonNode a = openings[0]!.DeepClone();
            JsonNode b = CopyOf(root.AsObject(), "rush");
            b["buildOrder"] = AiScenes.BuildOrder(("time", 0, "build", "malazan_billet", 0));
            openings.Clear();
            openings.Add(b);
            openings.Add(a);
        });
        Assert.NotEqual(ab.ContentHash(), ba.ContentHash());
        Simulation easyAb = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Easy, SeatController.Ai, SeatController.Human), ab);
        Simulation easyBa = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Easy, SeatController.Ai, SeatController.Human), ba);
        Assert.Equal("standard", easyAb.AiOf(0)!.Opening.Id);
        Assert.Equal("rush", easyBa.AiOf(0)!.Opening.Id);
    }

    // ---------- the pick's RNG budget ----------

    /// <summary>
    /// The pick is the only draw the AI's stream sees: after construction Easy's stream is fresh and Normal / Hard's is one
    /// <c>NextInt(0, count)</c> on from fresh, for 1 to 8 openings (a one-opening faction draws too); and after 2,000 ticks of
    /// a two-AI generated match no manager has drawn since (so the pick shifts no later draw: an older recording's AI would
    /// act the same). A tripwire: a manager that starts drawing from <see cref="AiPlayer.Rng"/> must revisit docs/03 "The pick".
    /// </summary>
    [Fact]
    public void ThePick_IsTheOnlyDrawOnTheAisStream()
    {
        foreach (int count in new[] { 1, 2, 3, 8 })
            foreach (AiDifficulty difficulty in new[] { AiDifficulty.Easy, AiDifficulty.Normal, AiDifficulty.Hard })
                for (ulong seed = 1; seed <= 20; seed++)
                {
                    var rng = new SimRng(seed, (ulong)RngStream.Ai(1));
                    var expected = new SimRng(seed, (ulong)RngStream.Ai(1));
                    int pick = AiPlayer.PickOpening(count, difficulty, ref rng);
                    int want = difficulty == AiDifficulty.Easy ? 0 : expected.NextInt(0, count);
                    Assert.Equal(want, pick);
                    Assert.Equal(expected.State, rng.State);
                }

        foreach (AiDifficulty difficulty in new[] { AiDifficulty.Easy, AiDifficulty.Normal, AiDifficulty.Hard })
        {
            Simulation sim = AiScenes.Generated(AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Ai), 5, record: false, out _, raiders: 4);
            var after = new ulong[2];
            for (int p = 0; p < 2; p++)
            {
                var fresh = new SimRng(5, (ulong)RngStream.Ai(p));
                // The pick draws over the seat's faction's shipped openings (BUG-0980: no longer 1; the bound can move the draw).
                if (difficulty != AiDifficulty.Easy) fresh.NextInt(0, TestSim.Data.Ai[p % 2].Openings.Length);
                Assert.Equal(fresh.State, sim.AiOf(p)!.Rng.State);
                after[p] = sim.AiOf(p)!.Rng.State;
            }
            for (int t = 0; t < 2000; t++) sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                Assert.True(sim.AiOf(p)!.CommandsSent > 0);
                Assert.True(after[p] == sim.AiOf(p)!.Rng.State, $"{difficulty} seat {p}: a manager drew from the AI stream after the pick");
            }
        }
    }

    // ---------- three openings of 1, 26 and 256 steps ----------

    /// <summary>
    /// Both factions with three openings: 0 the shipped <c>standard</c> (26 steps), 1 a one-step rush with a 1-pop wave, 2 the
    /// standard order padded to <see cref="DataLimits.MaxAiBuildOrderSteps"/> steps with worker trains (the longest legal order).
    /// </summary>
    private static readonly Lazy<GameData> s_mixed = new(() => AiScenes.DataWith(dir =>
    {
        foreach (string f in new[] { "malazan", "whirlwind" })
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                JsonArray openings = FirstOnly(root);
                JsonNode standard = openings[0]!;
                string worker = f == "malazan" ? "malazan_laborer" : "whirlwind_camp_follower";
                string barracks = f == "malazan" ? "malazan_barracks" : "whirlwind_raider_camp";
                JsonNode rush = CopyOf(root, "rush");
                rush["buildOrder"] = AiScenes.BuildOrder(("time", 0, "build", barracks, 0));
                rush["attack"] = new JsonObject { ["minArmyPop"] = 1, ["minArmyCost"] = 0 };
                JsonNode longest = CopyOf(root, "longest");
                JsonArray steps = longest["buildOrder"]!.AsArray();
                for (int k = 0; steps.Count < DataLimits.MaxAiBuildOrderSteps; k++)
                    steps.Add(new JsonObject { ["at"] = new JsonObject { ["time"] = 620 + k }, ["do"] = "train", ["id"] = worker });
                openings.Add(rush);
                openings.Add(longest);
            });
    }));

    private static int StreamPick(ulong seed, int player, int count)
    {
        var rng = new SimRng(seed, (ulong)RngStream.Ai(player));
        return rng.NextInt(0, count);
    }

    /// <summary>The first seed from 1 whose four Normal / Hard seats pick all three openings between them.</summary>
    private static ulong SeedCoveringAllThree()
    {
        for (ulong seed = 1; seed < 500; seed++)
        {
            var seen = new bool[3];
            for (int p = 0; p < 4; p++) seen[StreamPick(seed, p, 3)] = true;
            if (seen.All(x => x)) return seed;
        }
        throw new InvalidOperationException("no seed covers all three openings");
    }

    private static PlayerSeat[] FourMixedSeats() => new[]
    {
        new PlayerSeat(0, SeatController.Ai, AiDifficulty.Normal),
        new PlayerSeat(1, SeatController.Ai, AiDifficulty.Hard),
        new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard),
        new PlayerSeat(1, SeatController.Ai, AiDifficulty.Normal),
    };

    /// <summary>
    /// Four AI seats (Normal and Hard, both factions) on one generated map whose picks cover openings of 1, 26 and 256 steps:
    /// two sims from the same config agree on every pick and on the state hash at every 100th tick for 3,000 ticks; the
    /// recorded one replays clean; and each seat's Blackboard is its own opening's length.
    /// </summary>
    [Fact]
    public void FourSeatsOnThreeOpenings_TwinsEqualAtEveryCheckpoint_AndTheReplayPlaysClean()
    {
        GameData data = s_mixed.Value;
        ulong seed = SeedCoveringAllThree();
        Simulation a = AiScenes.Generated(FourMixedSeats(), seed, record: true, out ReplayRecorder? rec, units: 800, data: data, raiders: 3);
        Simulation b = AiScenes.Generated(FourMixedSeats(), seed, record: false, out _, units: 800, data: data, raiders: 3);
        var picks = new int[4];
        for (int p = 0; p < 4; p++)
        {
            picks[p] = a.AiOpening(p);
            Assert.Equal(StreamPick(seed, p, 3), picks[p]);
            Assert.Equal(picks[p], b.AiOpening(p));
            AiPlayer ai = a.AiOf(p)!;
            Assert.Equal(ai.Opening.BuildOrder.Length, ai.Board.StepDone.Length);
            Assert.Equal(ai.Opening.BuildOrder.Length, ai.Board.StepNoSiteUntil.Length);
        }
        _out.WriteLine($"seed {seed}: picks {string.Join(", ", picks)} (steps {string.Join(", ", Enumerable.Range(0, 4).Select(p => a.AiOf(p)!.Opening.BuildOrder.Length))})");
        Assert.Equal(3, picks.Distinct().Count());
        for (int t = 1; t <= 3000; t++)
        {
            a.Tick();
            b.Tick();
            if (t % 100 == 0) Assert.True(a.StateHash() == b.StateHash(), $"twins differ at tick {t}");
        }
        for (int p = 0; p < 4; p++) Assert.True(a.AiOf(p)!.CommandsSent > 0, $"seat {p} sent nothing");
        ReplayResult played = ReplayPlayer.Run(rec!.ToReplay(), data);
        Assert.True(played.Ok, played.ToString());
    }

    /// <summary>
    /// Each of the three openings picked by a Normal and a Hard seat on the flat scene runs 1,500 ticks without an index error;
    /// the step arrays are the picked order's length, and with the 256-step order the executor reaches past step 26.
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void EachOpeningLength_RunsWithItsOwnBlackboard(AiDifficulty difficulty)
    {
        GameData data = s_mixed.Value;
        for (int index = 0; index < 3; index++)
        {
            ulong seed = 1;
            while (StreamPick(seed, 0, 3) != index) seed++;
            Simulation sim = AiScenes.Flat(AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Ai), data, seed: seed);
            AiPlayer ai = sim.AiOf(0)!;
            Assert.Equal(index, sim.AiOpening(0));
            BuildMaps.SetTotals(sim, 0, 20000, 20000);
            for (int t = 0; t < 1500; t++) sim.Tick();
            int steps = ai.Opening.BuildOrder.Length;
            Assert.Equal(steps, ai.Board.StepDone.Length);
            Assert.Equal(steps, ai.Board.StepDueTick.Length);
            int due = ai.Board.StepDueTick.Count(d => d >= 0);
            _out.WriteLine($"{difficulty} opening {index} ({ai.Opening.Id}, {steps} steps): {due} steps due, {ai.Board.StepDone.Count(d => d)} done, {ai.CommandsSent} commands");
            Assert.True(due > 0);
        }
    }

    // ---------- BUG-0910 on every difficulty ----------

    /// <summary>
    /// BUG-0910's regression row on every difficulty (the developer's row is Hard only): a Corral step due (past the
    /// difficulty's slack) with no Barracks for 2-6 thinks never gets a no-site wait (no search ran), and sends its Build within
    /// 2 thinks of a Barracks standing on Normal and Hard. Easy: within 3, because at Easy's 2 s think a worker the economy
    /// just sent walking is no builder (<c>AiPlayer.BuilderRank</c>: only idle or gathering workers), not because of a wait.
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void ARequirementBlockedBuild_GetsNoWait_AndGoesOutWithin2Thinks_OnEveryDifficulty(AiDifficulty difficulty)
    {
        GameData data = AiScenes.DataWithAi("malazan", root =>
            AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 0, "build", "malazan_wickan_corral", 0)));
        int corral = data.FindBuilding("malazan_wickan_corral");
        var lags = new List<int>();
        for (int wait = 2; wait <= 6; wait++)
        {
            Simulation s = AiScenes.Flat(AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Human), data);
            BuildMaps.SetTotals(s, 0, 5000, 5000);
            List<Command> log = AiScenes.Log(s, 0);
            AiPlayer a = s.AiOf(0)!;
            // Past the difficulty's buildOrderSlack first (Easy 15 thinks, Normal 5), so the step is due and blocked only by its requires.
            int slackThinks = (a.Knobs.BuildOrderSlackTicks + a.ThinkTicks - 1) / a.ThinkTicks;
            for (int t = 0; t < (slackThinks + wait) * a.ThinkTicks; t++)
            {
                s.Tick();
                Assert.True(a.Board.StepNoSiteUntil[0] == 0, $"{difficulty} tick {t}: a no-site wait set on a step whose search never ran");
            }
            Assert.DoesNotContain(log, c => c.Kind == CommandKind.Build && c.TypeId == corral);
            Assert.Equal(0, a.SiteSearches); // the step stopped at the cell-free test: nothing searched
            Assert.True(s.World.Buildings.Spawn(0, data.FindBuilding("malazan_barracks"), 44 * AiScenes.FlatSize + 8, out _));
            int at = a.Thinks, lag = -1;
            for (int t = 0; t < 10 * a.ThinkTicks && lag < 0; t++)
            {
                s.Tick();
                Assert.Equal(0, a.Board.StepNoSiteUntil[0]);
                if (log.Any(c => c.Kind == CommandKind.Build && c.TypeId == corral)) lag = a.Thinks - at;
            }
            lags.Add(lag);
        }
        _out.WriteLine($"{difficulty}: thinks from the Barracks to the Corral's Build: {string.Join(", ", lags)}");
        int bound = difficulty == AiDifficulty.Easy ? 3 : 2;
        Assert.True(lags.All(l => l >= 0 && l <= bound), $"{difficulty}: {string.Join(", ", lags)}");
    }

    /// <summary>
    /// BUG-0910's other side on every difficulty: a step whose ring search ran and found nothing (a 2-cell search radius round a
    /// hall that fills it) still searches only once every <see cref="BuildOrderExecutor.NoSiteRetryThinks"/> thinks (BUG-0791).
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void ARealNoSiteSearch_StillWaitsNoSiteRetryThinks_OnEveryDifficulty(AiDifficulty difficulty)
    {
        GameData data = AiScenes.DataWith(dir =>
        {
            dir.EditJson(MalazanAi, root =>
                AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 0, "build", "malazan_barracks", 0)));
            dir.EditJson("common/rules.json", r => r["ai"]!["placementSearchRadius"] = 2);
        });
        Simulation sim = AiScenes.Flat(AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Human), data);
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        List<Command> log = AiScenes.Log(sim, 0);
        AiPlayer ai = sim.AiOf(0)!;
        const int thinks = 20;
        // Easy's slack delays the first due think; count from the first search.
        for (int t = 0; t < (thinks + 10) * ai.ThinkTicks && ai.SiteSearches == 0; t++) sim.Tick();
        Assert.Equal(1, ai.SiteSearches);
        int start = ai.Thinks;
        while (ai.Thinks - start < thinks) sim.Tick();
        int expected = thinks / BuildOrderExecutor.NoSiteRetryThinks; // searches after the first one
        _out.WriteLine($"{difficulty}: {ai.SiteSearches - 1} more searches in {thinks} thinks (expected about {expected})");
        Assert.DoesNotContain(log, c => c.Kind == CommandKind.Build && c.TypeId == data.FindBuilding("malazan_barracks"));
        Assert.InRange(ai.SiteSearches - 1, expected - 1, expected + 1);
    }
}
