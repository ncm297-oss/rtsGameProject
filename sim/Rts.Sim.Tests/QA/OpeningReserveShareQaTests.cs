using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M7-6, session 2026-11-13-1118): an opening's <c>attack.reserveArmyShare</c> (Request 52) attacked from the sides the
/// developer's <c>Ai/OpeningReserveShareTests</c> does not take: the edge values 0 and 1 (and 1 over a rules value of 0, so the
/// opening wins both ways), each difficulty's <c>waveSizeMultiplier</c> at the exact boundary, the field on one opening of
/// three with the opening picked by seed (and Easy, which always plays the first), two seats of one faction on different
/// openings (the "reads another seat's opening" mutant), a seat defending at the moment its gate would open, a save taken
/// in the middle of a held reserve (and refused under the data without the field), and a replay round trip.
/// </summary>
public class OpeningReserveShareQaTests
{
    private readonly ITestOutputHelper _out;

    public OpeningReserveShareQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Shipped data with each faction's opening <c>i</c> given <paramref name="share"/>(faction, i) when not null; optionally
    /// every Malazan opening's build order Age II alone and its army Heavy Infantry, the scout home (ReserveTests' gate scene);
    /// optionally <c>rules.json</c> <c>ai.reserveArmyShare</c> set.
    /// </summary>
    internal static GameData Shares(Func<string, int, double?> share, bool ageTwoOnly = false, double? rules = null) => AiScenes.DataWith(dir =>
    {
        foreach (string f in TestSim.ShippedFactions)
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                JsonArray openings = root["openings"]!.AsArray();
                for (int i = 0; i < openings.Count; i++)
                {
                    JsonNode o = openings[i]!;
                    if (share(f, i) is double s) o["attack"]!["reserveArmyShare"] = s;
                    if (ageTwoOnly && f == "malazan")
                    {
                        o["buildOrder"] = AiScenes.BuildOrder(("time", 0, "research", "age_ii", 0));
                        o["compositions"] = JsonNode.Parse("""[{ "age": 1, "units": { "malazan_heavy_infantry": 1 } }, { "age": 2, "units": { "malazan_heavy_infantry": 1 } }]""");
                    }
                }
            });
        dir.EditJson("common/rules.json", r =>
        {
            if (ageTwoOnly) r["ai"]!["scoutAfter"] = 3600;
            if (rules != null) r["ai"]!["reserveArmyShare"] = rules.Value;
        });
    });

    /// <summary>Units of <paramref name="type"/> for <paramref name="share"/> of <paramref name="ai"/>'s own wave (both measures, x its difficulty's multiplier), rounded up.</summary>
    private static int Needed(AiPlayer ai, int type, double share)
    {
        UnitDef u = ai.Data.Units[type];
        double wave = share * ai.Knobs.WaveSizeMultiplier;
        int forPop = (int)Math.Ceiling(wave * ai.Opening.AttackMinHalfPop / u.HalfPop - 1e-6);
        int forCost = (int)Math.Ceiling(wave * ai.Opening.AttackMinCost / u.UnitWorth - 1e-6);
        return Math.Max(forPop, forCost);
    }

    /// <summary>Places <paramref name="count"/> units of <paramref name="type"/> for player <paramref name="p"/> beside its hall on the flat scene.</summary>
    private static void Army(Simulation sim, int p, int type, int count)
    {
        (int hx, int hy) = AiScenes.HallAt(p);
        int toward = p % 2 == 0 ? 1 : -1;
        for (int k = 0; k < count; k++)
            CombatScenes.Place(sim, p, type, sim.World.NavGrid.CellCenter(hx + toward * (6 + k % 6), hy + 1 + k / 6));
    }

    /// <summary>The gate scene: a Hard (or <paramref name="difficulty"/>) Malazan AI seat 0 v a human seat, a finished Barracks and Crossbow Range.</summary>
    private static Simulation GateScene(GameData d, AiDifficulty difficulty = AiDifficulty.Hard, ulong seed = 3)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Human), d, seed: seed);
        Assert.True(sim.World.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), 44 * AiScenes.FlatSize + 8, out _));
        Assert.True(sim.World.Buildings.Spawn(0, d.FindBuilding("malazan_crossbow_range"), 44 * AiScenes.FlatSize + 16, out _));
        return sim;
    }

    /// <summary>The next think of player <paramref name="p"/> (the purse 300 / 300 for player 0, below Age II's 400 / 200 gold): its reserve step, gate and defense.</summary>
    private static (int Step, bool May, bool Defending) Think(Simulation sim, int p = 0, int from = 0)
    {
        AiPlayer ai = sim.AiOf(p)!;
        (int, bool, bool) seen = (-2, false, false);
        while (sim.TickNumber < from) // past the difficulty's buildOrderSlack, so a time-0 step is due; the purse empty so nothing trains
        {
            BuildMaps.SetTotals(sim, p, 0, 0);
            sim.Tick();
        }
        ai.ThoughtForTests = a => seen = (a.ReserveStep, a.MayReserve(), a.Board.Defending);
        while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
        BuildMaps.SetTotals(sim, 0, 300, 300);
        sim.Tick();
        ai.ThoughtForTests = null;
        return seen;
    }

    // ---------- the edge values ----------

    private static readonly Lazy<GameData> s_zero = new(() => Shares((f, _) => f == "malazan" ? 0 : null, ageTwoOnly: true));
    private static readonly Lazy<GameData> s_absentAgeTwo = new(() => Shares((_, _) => null, ageTwoOnly: true));

    /// <summary>
    /// Share 0 means "reserve from the start": with no army at all, the seat whose opening carries 0 names Age II at its first
    /// think (the behaviour M5-4g's fix round 1 removed globally because it cost a quarter of the sides their first wave);
    /// the same seat without the field (the rules' 1) names nothing.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShareZero_NoArmy_NamesTheReserveAtTheFirstThink_AbsentDoesNot(bool carries)
    {
        GameData d = carries ? s_zero.Value : s_absentAgeTwo.Value;
        Simulation sim = GateScene(d);
        Assert.Equal(carries ? 0f : null, sim.AiOf(0)!.Opening.ReserveArmyShare);
        (int step, bool may, bool defending) = Think(sim);
        Assert.False(defending);
        Assert.Equal(carries, may);
        Assert.Equal(carries ? 0 : -1, step);
    }

    private static readonly Lazy<GameData> s_oneOverZero = new(() => Shares((f, _) => f == "malazan" ? 1 : null, ageTwoOnly: true, rules: 0));
    private static readonly Lazy<GameData> s_absentOverZero = new(() => Shares((_, _) => null, ageTwoOnly: true, rules: 0));

    /// <summary>
    /// The opening's value replaces the rules' in both directions (not a min or a max of the two): rules 0 with the opening at
    /// 1 needs the whole wave (shut with no army, shut one unit short, open at the whole wave); rules 0 with the field absent
    /// is open with no army.
    /// </summary>
    [Theory]
    [InlineData(true, -1, false)] // no army
    [InlineData(true, 0, true)] // the whole wave
    [InlineData(true, 1, false)] // the whole wave less one
    [InlineData(false, -1, true)]
    public void ShareOne_OverRulesZero_NeedsTheWholeWave(bool carries, int lessUnits, bool open)
    {
        GameData d = carries ? s_oneOverZero.Value : s_absentOverZero.Value;
        Assert.Equal(0f, d.AiRules.ReserveArmyShare);
        Simulation sim = GateScene(d);
        AiPlayer ai = sim.AiOf(0)!;
        int hi = d.FindUnit("malazan_heavy_infantry");
        if (lessUnits >= 0) Army(sim, 0, hi, Needed(ai, hi, 1) - lessUnits);
        (int step, bool may, _) = Think(sim);
        Assert.Equal(open, may);
        Assert.Equal(open ? 0 : -1, step);
    }

    // ---------- each difficulty's multiplier at the boundary ----------

    private static readonly Lazy<GameData> s_halfMalazan = new(() => Shares((f, _) => f == "malazan" ? 0.5 : null, ageTwoOnly: true));

    /// <summary>
    /// Easy (0.6), Normal (1) and Hard (1.25) seats with every Malazan opening at 0.5: the gate opens at exactly 0.5 x the
    /// difficulty's multiplier of the seat's own wave, and is shut one Heavy Infantry below it; without the field the same
    /// army keeps it shut (the rules' 1). Printed: the counts per difficulty.
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void EachDifficulty_TheGateOpensAtHalfItsOwnScaledWave_NotOneUnitBelow(AiDifficulty difficulty)
    {
        var result = new List<string>();
        foreach ((bool carries, int less, bool open) in new[] { (true, 0, true), (true, 1, false), (false, 0, false) })
        {
            GameData d = carries ? s_halfMalazan.Value : s_absentAgeTwo.Value;
            Simulation sim = GateScene(d, difficulty);
            AiPlayer ai = sim.AiOf(0)!;
            int hi = d.FindUnit("malazan_heavy_infantry");
            int half = Needed(ai, hi, 0.5), whole = Needed(ai, hi, 1);
            Assert.True(whole > half && half > 1, $"{difficulty}: half {half}, whole {whole}");
            Army(sim, 0, hi, half - less);
            (int step, bool may, bool defending) = Think(sim, from: 31 * SimConstants.TicksPerSecond); // Easy's slack is 30 s
            result.Add($"{difficulty} x{ai.Knobs.WaveSizeMultiplier} opening {ai.Opening.Id} field {carries} units {half - less} (half {half}, whole {whole}): may {may} step {step}");
            Assert.False(defending);
            Assert.True(open == may, result[^1]);
            Assert.Equal(open ? 0 : -1, step);
        }
        foreach (string line in result) _out.WriteLine(line);
    }

    // ---------- one opening of three, picked by seed; two seats of one faction ----------

    private static readonly Lazy<GameData> s_secondOnly = new(() => Shares((f, i) => f == "malazan" && i == 1 ? 0.5 : null, ageTwoOnly: true));

    /// <summary>
    /// The field on Malazan's second opening only. Over 24 seeds a Hard seat plays the opening its seed picks; with half of
    /// that opening's wave in Heavy Infantry its gate is open exactly when it plays the second. Both outcomes are seen. An
    /// Easy seat always plays the first opening (M5-8a), so the field never applies to it.
    /// </summary>
    [Fact]
    public void TheFieldOnOneOpeningOfThree_AppliesExactlyWhenTheSeedPicksIt()
    {
        GameData d = s_secondOnly.Value;
        var picked = new int[3];
        for (ulong seed = 1; seed <= 24; seed++)
            foreach (AiDifficulty difficulty in new[] { AiDifficulty.Hard, AiDifficulty.Easy })
            {
                if (difficulty == AiDifficulty.Easy && seed > 4) continue;
                Simulation sim = GateScene(d, difficulty, seed);
                AiPlayer ai = sim.AiOf(0)!;
                if (difficulty == AiDifficulty.Easy) Assert.Equal(0, ai.OpeningIndex);
                else picked[ai.OpeningIndex]++;
                int hi = d.FindUnit("malazan_heavy_infantry");
                Army(sim, 0, hi, Needed(ai, hi, 0.5));
                (_, bool may, _) = Think(sim);
                Assert.True(may == (ai.OpeningIndex == 1), $"seed {seed} {difficulty}: opening {ai.OpeningIndex} ({ai.Opening.Id}), share {ai.Opening.ReserveArmyShare}, may {may}");
            }
        _out.WriteLine($"Hard picks over 24 seeds: {string.Join(", ", picked)}");
        Assert.True(picked[1] > 0 && picked[0] + picked[2] > 0, "the seeds never picked both kinds of opening");
    }

    /// <summary>The first seed at or after 1 whose two Hard Malazan AI seats (both faction 0) play different openings.</summary>
    private static (ulong Seed, int First, int Second) DifferentOpenings()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            Simulation sim = AiScenes.Flat(new[] { new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard), new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard) }, seed: seed);
            int a = sim.AiOf(0)!.OpeningIndex, b = sim.AiOf(1)!.OpeningIndex;
            if (a != b) return (seed, a, b);
        }
        throw new InvalidOperationException("no seed gave two different openings");
    }

    /// <summary>
    /// Two Hard AI seats of the same faction (Malazan) playing different openings, the field (0.5) on one seat's opening only,
    /// each seat with half its own wave in Heavy Infantry: the seat whose opening carries it has its gate open, the other's
    /// stays shut, both ways round. Catches a gate that reads another seat's opening, the faction's first opening, or any
    /// opening of the faction that carries the field.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TwoSeatsOfOneFaction_OnDifferentOpenings_EachFollowsItsOwn(int carrier)
    {
        (ulong seed, int first, int second) = DifferentOpenings();
        int carried = carrier == 0 ? first : second;
        GameData d = Shares((f, i) => f == "malazan" && i == carried ? 0.5 : null);
        Simulation sim = AiScenes.Flat(new[] { new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard), new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard) }, d, seed: seed);
        Assert.Equal((first, second), (sim.AiOf(0)!.OpeningIndex, sim.AiOf(1)!.OpeningIndex));
        int hi = d.FindUnit("malazan_heavy_infantry");
        var may = new bool?[2];
        for (int p = 0; p < 2; p++)
        {
            AiPlayer ai = sim.AiOf(p)!;
            Assert.True(Needed(ai, hi, 1) > Needed(ai, hi, 0.5));
            Army(sim, p, hi, Needed(ai, hi, 0.5));
            int seat = p;
            ai.ThoughtForTests = a =>
            {
                Assert.False(a.Board.Defending);
                may[seat] = a.MayReserve();
            };
        }
        sim.Tick();
        _out.WriteLine($"seed {seed}: openings {first} / {second}, field on {carried}: may {may[0]} / {may[1]}");
        Assert.Equal(new bool?[] { carrier == 0, carrier == 1 }, may);
    }

    // ---------- a defense at the moment the gate would open ----------

    /// <summary>
    /// Every Malazan opening at 0.5 and exactly half the wave at home: the gate is open while the base is quiet (Age II named);
    /// four enemy Raiders seen by the hall: the next think defends and names no reserve, its gate shut by the defense although
    /// the army is past the opening's share.
    /// </summary>
    [Fact]
    public void ADefense_AtTheMomentTheGateWouldOpen_ShutsIt()
    {
        GameData d = s_halfMalazan.Value;
        Simulation sim = GateScene(d);
        AiPlayer ai = sim.AiOf(0)!;
        int hi = d.FindUnit("malazan_heavy_infantry");
        Army(sim, 0, hi, Needed(ai, hi, 0.5));
        (int quietStep, bool quietMay, bool quietDefending) = Think(sim);
        Assert.Equal((0, true, false), (quietStep, quietMay, quietDefending));
        (int hx, int hy) = AiScenes.HallAt(0);
        var near = new System.Numerics.Vector2(hx * MapConstants.CellSize, (hy + 20) * MapConstants.CellSize);
        int raider = d.FindUnit("whirlwind_raider");
        for (int k = 0; k < 4; k++) CombatScenes.Spot(sim, 0, CombatScenes.Place(sim, 1, raider, near + new System.Numerics.Vector2(k, 0f)));
        (int step, bool may, bool defending) = Think(sim);
        Assert.Equal((-1, false, true), (step, may, defending));
    }

    // ---------- a save in the middle of a reserve; the data mismatch ----------

    private static readonly Lazy<GameData> s_mixed = new(() => Shares((f, i) => f == "malazan" ? 0.5 : f == "whirlwind" && i != 0 ? 0.25 : null));

    /// <summary>
    /// A two-seat Normal AI match (generated map, seed 6) on data where every Malazan opening carries 0.5 and two of
    /// Whirlwind's 0.25: at the first think a seat names a reserve, the match is saved one tick later (between thinks, the
    /// reserve held), loaded, field-equal for both seats, and ticked beside the reference for 1,200 ticks: the same hash and
    /// the same reserve named at every think. The same bytes under the shipped data (no field) are refused as a data mismatch.
    /// </summary>
    [Fact]
    public void SavedInTheMiddleOfAReserve_LoadsAndContinuesIdentical_AndTheShippedDataRefusesIt()
    {
        GameData d = s_mixed.Value;
        Simulation reference = AiScenes.Generated(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), 6, false, out _, data: d);
        var refRes = new List<string>();
        Hook(reference, refRes);
        int namedAt = -1;
        while (namedAt < 0 && reference.TickNumber < 20_000)
        {
            refRes.Clear();
            int t = reference.TickNumber;
            reference.Tick();
            if (refRes.Any(r => !r.Contains("step -1", StringComparison.Ordinal))) namedAt = t;
        }
        Assert.True(namedAt >= 0, "no seat named a reserve in 20,000 ticks");
        reference.Tick(); // one tick into the held reserve
        byte[] bytes = SaveTestRun.Save(reference);
        Assert.Equal(SaveFormat.CurrentVersion, BitConverter.ToInt32(bytes, SaveFormat.MagicLength));
        Assert.Equal(10, SaveFormat.CurrentVersion);
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(TestSim.Data, bytes));
        Simulation loaded = SaveTestRun.Load(d, bytes);
        for (int p = 0; p < 2; p++) SaveTestRun.AssertSameAi(reference.AiOf(p), loaded.AiOf(p), $"saved at {reference.TickNumber}: seat {p}");
        var loadRes = new List<string>();
        Hook(loaded, loadRes);
        int named = 0;
        for (int k = 0; k < 1200; k++)
        {
            refRes.Clear();
            loadRes.Clear();
            reference.Tick();
            loaded.Tick();
            Assert.True(refRes.SequenceEqual(loadRes), $"tick {reference.TickNumber}: reserves differ\n  {string.Join(" | ", refRes)}\n  {string.Join(" | ", loadRes)}");
            Assert.True(reference.StateHash() == loaded.StateHash(), $"tick {reference.TickNumber}: hash differs");
            named += refRes.Count(r => !r.Contains("step -1", StringComparison.Ordinal));
        }
        SaveTestRun.AssertSameState(reference, loaded, "after 1,200 ticks");
        _out.WriteLine($"reserve first named at {namedAt}; saved at {namedAt + 1}; thinks naming a reserve in the next 1,200 ticks {named}; openings {reference.AiOf(0)!.Opening.Id} ({reference.AiOf(0)!.Opening.ReserveArmyShare}) / {reference.AiOf(1)!.Opening.Id} ({reference.AiOf(1)!.Opening.ReserveArmyShare})");
    }

    private static void Hook(Simulation sim, List<string> reserves)
    {
        for (int p = 0; p < sim.World.Config.PlayerCount; p++)
            if (sim.AiOf(p) is { } ai)
                ai.ThoughtForTests = x => reserves.Add($"p{x.Player} t{x.Now} step {x.ReserveStep} {x.ReserveGold}/{x.ReserveWood} may {x.MayReserve()}");
    }

    // ---------- BUG-1310: the helper test's own row-writer once the data ships the rows ----------

    /// <summary>
    /// BUG-1310: <c>BloodOilScenesTests.DataTrackRows</c> writes docs/03's M7-4 rows onto a copy of the shipped data; once
    /// the data track ships them, the copy it starts from already carries them. Simulated here by applying it twice: the copy
    /// must still load (one <c>teblor_frenzied</c> row), or <c>BloodOilScenesTests</c>' premise goes red on the data track's
    /// session.
    /// </summary>
    [Fact(Skip = "BUG-1310 (open, S2): DataTrackRows appends the rows blindly; a copy that already carries them gets a duplicate teblor_frenzied")]
    public void DataTrackRows_OnACopyThatAlreadyCarriesThem_StillLoads()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        BloodOilScenesTests.DataTrackRows(dir); // what the data track will ship
        BloodOilScenesTests.DataTrackRows(dir); // what BloodOilScenesTests then writes on top
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Single(r.Data!.Statuses, s => s.Key == BloodOilScenes.Status);
    }

    // ---------- the content hash: absent is not "0" ----------

    /// <summary>
    /// The field is hashed only when a file carries it. A hash that always added it (absent as 0) would leave every
    /// carried-value test green but move the shipped hash; it is told apart by the file that carries 0: that copy must hash
    /// differently from the shipped (absent) one, on any faction's opening. Also 0 and 1 differ, and the same file loaded
    /// twice hashes the same.
    /// </summary>
    [Theory]
    [InlineData("malazan", 0)]
    [InlineData("whirlwind", 2)]
    [InlineData("teblor", 0)]
    public void AFileCarryingZero_HashesUnlikeTheFieldAbsent(string faction, int opening)
    {
        ulong Hash(double? share) => Shares((f, i) => f == faction && i == opening ? share : null).ContentHash();
        ulong absent = Hash(null), zero = Hash(0), one = Hash(1);
        Assert.Equal(TestSim.Data.ContentHash(), absent);
        Assert.NotEqual(absent, zero);
        Assert.NotEqual(zero, one);
        Assert.Equal(zero, Hash(0));
    }

    // ---------- a replay round trip ----------

    /// <summary>
    /// A recorded two-seat AI match (Hard v Normal, seed 9) on the mixed-share data, 4,000 ticks: written, read back byte-equal,
    /// and played back on the same data to equal checkpoints; played on the shipped data it is refused (the share is in the
    /// data hash, so a recording made with it cannot be played without it).
    /// </summary>
    [Fact]
    public void AReplayWithPerOpeningShares_RoundTrips_AndTheShippedDataRefusesIt()
    {
        GameData d = s_mixed.Value;
        var seats = new[] { new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard), new PlayerSeat(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Generated(seats, 9, record: true, out ReplayRecorder? rec, data: d);
        int named = 0;
        for (int p = 0; p < 2; p++) sim.AiOf(p)!.ThoughtForTests = a => { if (a.ReserveStep >= 0) named++; };
        for (int t = 0; t < 4000; t++) sim.Tick();
        Replay r = rec!.ToReplay();
        byte[] bytes = ReplayFormat.Write(r);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? back));
        Assert.Equal(bytes, ReplayFormat.Write(back!));
        Assert.Equal(d.ContentHash(), back!.DataHash);
        ReplayResult played = ReplayPlayer.Run(back, d);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(4000, played.TicksRun);
        ReplayResult refused = ReplayPlayer.Run(back, TestSim.Data);
        Assert.False(refused.Ok);
        _out.WriteLine($"thinks naming a reserve {named}; checkpoints {r.Checkpoints.Length}; on the shipped data: {refused}");
    }
}
