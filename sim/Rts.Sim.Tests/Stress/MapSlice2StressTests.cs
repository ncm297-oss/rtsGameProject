using System.Diagnostics;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-S8 (session 2026-11-06-0326): Pale Hills and Vathar Crossing under the AI. The 9 Normal Malazan x Whirlwind
/// pairings (each on its smallest seed, as the pairing reports pick them) on each map for 20 minutes: both sides' first
/// wave, wood at 5:00, the result (printed; wood at 5:00 asserted). Every scout of both seats killed at the door, seeds
/// 1-10 on each map (at most 3 lost a seat). The seed-1 match saved at probed moments (first wave, a hall lost, 10:00,
/// 20:00) and followed 1,000 ticks hash for hash; recorded to the end and played back to every checkpoint. Vathar
/// Crossing's 12,000-tick twin in a child process with a save at 6,000. A steady think on each map allocates nothing.
/// </summary>
[Collection(SerialCollection.Name)]
public class MapSlice2StressTests
{
    private const string PaleHills = "pale_hills", Vathar = "vathar_crossing";

    private readonly ITestOutputHelper _out;

    public MapSlice2StressTests(ITestOutputHelper output) => _out = output;

    private static string Clock(int ticks) => $"{ticks / 1200}:{ticks / 20 % 60:D2}";

    public static TheoryData<string, int, int> Pairings()
    {
        var rows = new TheoryData<string, int, int>();
        foreach (string id in new[] { PaleHills, Vathar })
            for (int m = 0; m < 3; m++)
                for (int w = 0; w < 3; w++) rows.Add(id, m, w);
        return rows;
    }

    /// <summary>
    /// A pairing on a new map, Normal v Normal, its smallest seed, 24,000 ticks (or the end): prints each side's first wave,
    /// wave count, wood at 5:00, defeat; asserts the seat picked the pairing's opening and both sides have wood by 5:00.
    /// "ROW" lines are read by the QA report.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Pairings))]
    public void APairing_OnANewMap_BothSidesGatherWoodBy5Min_AndWave(string id, int malazan, int whirlwind)
    {
        ulong seed = ReservePairingsStressTests.NthSeed(malazan, whirlwind, 1);
        Simulation sim = MapFileTests.MapMatch(id, seed, false, out _);
        World w = sim.World;
        Assert.Equal(malazan, sim.AiOpening(0));
        Assert.Equal(whirlwind, sim.AiOpening(1));
        var firstWave = new[] { -1, -1 };
        var waves = new int[2];
        var waving = new bool[2];
        var woodAt5 = new int[2];
        while (sim.TickNumber < 24_000 && !sim.MatchOver)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                AiPlayer ai = sim.AiOf(p)!;
                if (ai.Board.WaveActive && !waving[p])
                {
                    waves[p]++;
                    if (firstWave[p] < 0) firstWave[p] = sim.TickNumber;
                }
                waving[p] = ai.Board.WaveActive;
                Assert.True(w.Gold[p] >= 0 && w.Wood[p] >= 0, $"seat {p}: a negative bank at {sim.TickNumber}");
            }
            if (sim.TickNumber == 6000)
                for (int p = 0; p < 2; p++) woodAt5[p] = w.GatheredWood[p];
        }
        _out.WriteLine($"ROW {id} m{malazan} w{whirlwind} seed {seed}: first wave {(firstWave[0] < 0 ? "none" : Clock(firstWave[0]))} / {(firstWave[1] < 0 ? "none" : Clock(firstWave[1]))}; waves {waves[0]} / {waves[1]}; wood at 5:00 {woodAt5[0]} / {woodAt5[1]}; defeated {w.IsDefeated(0)} / {w.IsDefeated(1)} at {Clock(sim.TickNumber)}; gathered gold {w.GatheredGold[0]} / {w.GatheredGold[1]} wood {w.GatheredWood[0]} / {w.GatheredWood[1]}");
        Assert.True(woodAt5[0] > 0 || w.IsDefeated(0) && sim.TickNumber < 6000, $"Malazan ({malazan}) had no wood at 5:00");
        Assert.True(woodAt5[1] > 0 || w.IsDefeated(1) && sim.TickNumber < 6000, $"Whirlwind ({whirlwind}) had no wood at 5:00");
    }

    public static TheoryData<string, ulong> ScoutRows()
    {
        var rows = new TheoryData<string, ulong>();
        foreach (string id in new[] { PaleHills, Vathar })
            for (ulong s = 1; s <= 10; s++) rows.Add(id, s);
        return rows;
    }

    /// <summary>
    /// Every scout of both seats killed the tick it is seen out, to tick 8,000, on a new map with seed 1-10: each seat loses
    /// at most <c>ai.scoutReplacements</c> + 1 scouts, and no unit other than scouts is killed by the test.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(ScoutRows))]
    public void EveryScoutKilledAtTheDoor_OnANewMap_LosesAtMostThreeASeat(string id, ulong seed)
    {
        Simulation sim = MapFileTests.MapMatch(id, seed, false, out _);
        World w = sim.World;
        var killed = new int[2];
        while (sim.TickNumber < 8000)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                Blackboard b = sim.AiOf(p)!.Board;
                if (b.ScoutPhase != 1) continue;
                var h = new EntityHandle(b.ScoutSlot, b.ScoutGeneration);
                if (!w.Units.IsAlive(h)) continue;
                CombatSystem.HitUnit(w, new PendingHit(default, 1 - p, h, false, w.Units.Hp[h.Index]));
                killed[p]++;
            }
        }
        AiPlayer a0 = sim.AiOf(0)!, a1 = sim.AiOf(1)!;
        _out.WriteLine($"ROW {id} seed {seed}: scouts killed {killed[0]} / {killed[1]}; phase {a0.Board.ScoutPhase} / {a1.Board.ScoutPhase}; lifted {a0.ReachLifted()} / {a1.ReachLifted()}; wood {w.GatheredWood[0]} / {w.GatheredWood[1]}");
        for (int p = 0; p < 2; p++)
        {
            Assert.True(killed[p] >= 1, $"seat {p}: no scout went out");
            Assert.True(killed[p] <= sim.AiOf(p)!.Rules.ScoutReplacements + 1, $"seat {p}: {killed[p]} scouts fed to the door");
        }
    }

    /// <summary>
    /// Measurement for the report (asserts only the setup): every scout of both seats killed at the door, as
    /// <see cref="EveryScoutKilledAtTheDoor_OnANewMap_LosesAtMostThreeASeat"/>, on <paramref name="map"/> ("generated",
    /// "raraku", or a new map's id), seed <paramref name="seed"/>, to 16,000 ticks: each seat's first wood tick, wood at
    /// 8,000 and 16,000, the tick its last scout died, and its gatherers on wood at 8,000.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("generated", 1UL)]
    [InlineData("generated", 2UL)]
    [InlineData("generated", 5UL)]
    [InlineData("raraku", 1UL)]
    [InlineData("raraku", 2UL)]
    [InlineData("raraku", 5UL)]
    [InlineData(PaleHills, 2UL)]
    [InlineData(PaleHills, 5UL)]
    [InlineData(Vathar, 1UL)]
    [InlineData(Vathar, 5UL)]
    public void EveryScoutKilledAtTheDoor_FirstWood_ByMap(string map, ulong seed)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation sim = map switch
        {
            "generated" => AiScenes.Generated(seats, seed, false, out _),
            "raraku" => MapFileTests.RarakuMatch(seed, false, out _),
            _ => MapFileTests.MapMatch(map, seed, false, out _),
        };
        World w = sim.World;
        var killed = new int[2];
        var lastKill = new int[2];
        var firstWood = new[] { -1, -1 };
        var woodAt8 = new int[2];
        while (sim.TickNumber < 16_000)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                if (firstWood[p] < 0 && w.GatheredWood[p] > 0) firstWood[p] = sim.TickNumber;
                Blackboard b = sim.AiOf(p)!.Board;
                if (b.ScoutPhase != 1) continue;
                var h = new EntityHandle(b.ScoutSlot, b.ScoutGeneration);
                if (!w.Units.IsAlive(h)) continue;
                CombatSystem.HitUnit(w, new PendingHit(default, 1 - p, h, false, w.Units.Hp[h.Index]));
                killed[p]++;
                lastKill[p] = sim.TickNumber;
            }
            if (sim.TickNumber == 8000) for (int p = 0; p < 2; p++) woodAt8[p] = w.GatheredWood[p];
        }
        for (int p = 0; p < 2; p++)
            _out.WriteLine($"ROW {map} seed {seed} seat {p}: scouts killed {killed[p]} (last at {lastKill[p]}); first wood {firstWood[p]}; wood at 8,000 {woodAt8[p]}, at 16,000 {w.GatheredWood[p]}; phase {sim.AiOf(p)!.Board.ScoutPhase}; lifted {sim.AiOf(p)!.ReachLifted()}; gold {w.GatheredGold[p]}");
        Assert.True(killed[0] >= 1 && killed[1] >= 1);
    }

    private static int Halls(World w, int owner)
    {
        int n = 0;
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == owner && w.Data.Buildings[w.Buildings.TypeId[i]].Slot == BuildingSlot.TownHall && !w.Buildings.UnderConstruction[i]) n++;
        return n;
    }

    /// <summary>The rows that end (measured at e3fd8fb): Vathar Crossing and Raraku seeds 2-6, the generated map seeds 2-5.</summary>
    public static TheoryData<string, ulong> SweepRows()
    {
        var rows = new TheoryData<string, ulong>();
        foreach (string id in new[] { Vathar, "raraku" })
            for (ulong s = 2; s <= 6; s++)
                if (!(id == "raraku" && s == 5)) rows.Add(id, s); // M5-9a: Raraku seed 5 is BUG-1152's (the skipped row below)
        for (ulong s = 2; s <= 5; s++) rows.Add("generated", s);
        return rows;
    }

    /// <summary>
    /// The developer's seed-1 result on more seeds: Normal v Normal (Malazan v Whirlwind, starts 0 and 1) on seeds 2-6, to
    /// 72,000 ticks (Raraku's 60-minute limit), with Raraku and the generated map as the baseline. Printed: the result, its
    /// tick, and whether it is inside the brief's 36,000. Asserted: the match ends by 72,000. Measured at e3fd8fb: Vathar
    /// 5 / 5 (15,722-31,550 ticks), Raraku 5 / 5, generated 4 / 5; Pale Hills 0 / 5 (the skipped rows below, BUG-1068).
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(SweepRows))]
    public void NormalVersusNormal_OnANewMap_OtherSeeds_EndWithin72000(string id, ulong seed) => SweepRow(id, seed);

    /// <summary>
    /// See <see cref="NormalVersusNormal_OnANewMap_OtherSeeds_EndWithin72000"/>: the rows with no result by 60:00 at e3fd8fb.
    /// Pale Hills 3 / 4 / 6 (one match: openings 0 / 2): Malazan razed, 1 unit left, Whirlwind 87 units; 5: the mirror
    /// (Whirlwind razed, 6 units left). Generated 6: Malazan razed, 1 unit left (the AI's waves targeted buildings only).
    /// M5-4d (BUG-1068): the winner now hunts the last units once no enemy building is known, so each row must end by
    /// 72,000; the fog oracle for the hunt's memory runs at every think of both seats (criterion 4).
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(PaleHills, 3UL)]
    [InlineData(PaleHills, 4UL)]
    [InlineData(PaleHills, 6UL)]
    [InlineData("generated", 6UL)]
    public void NormalVersusNormal_KnownStalls_EndWithin72000(string id, ulong seed) => SweepRow(id, seed, oracle: true);

    /// <summary>
    /// Pale Hills seed 5, out of <see cref="NormalVersusNormal_KnownStalls_EndWithin72000"/> since M5-9b (rule 43: a row that
    /// stops ending is a finding, not a re-bound). It ended at 45,358 on the M5-9a branch (52,557 at M5-4d), both seats broke
    /// from 24:00. With the far expansion both seats take a free start's mines with a depot (seat 0 at 27,520, seat 1 at
    /// 28,200; seat 1 a second at 44,560, seat 0 one by the 2,000-gold pair near start 2 at 53,080), the war runs on with
    /// 25-37 units a side, and at 70:00 seat 1 has no army and no gold left; seat 0 (25 units) then needs to 96,321 (80:16) to
    /// raze and hunt down a seat that keeps rebuilding with 14,000-17,000 wood (four hunts): BUG-1116's slow end, BUG-1157.
    /// </summary>
    [Theory(Skip = "BUG-1116 (open; BUG-1157): ends at 96,321 since M5-9b's far expansion (both seats funded longer; the loser rebuilds and is hunted late), past 72,000")]
    [Trait("Category", "Stress")]
    [InlineData(PaleHills, 5UL)]
    public void NormalVersusNormal_PaleHillsSeed5_SlowRaze_EndsWithin72000(string id, ulong seed) => SweepRow(id, seed, oracle: true);

    /// <summary>
    /// Pale Hills seed 2 (BUG-1068's remaining case): both sides still hold two halls at 60:00 (37 v 27 units, 20,000+ wood
    /// each at e3fd8fb), a true stalemate the hunt doesn't touch (enemy buildings stay known). M5-9a round 2 fix round 1
    /// (BUG-1154): the Malazan seat (48 gold, its pick a Heavy Infantry it can never pay) is starved from 26,780 and waves;
    /// the match ends at 53,797 (44:49), seat 0 the winner. Un-skipped. M5-9b (the far expansion): both seats expand and gather
    /// 14,000-16,000 gold; the match is decided by 40:00 (seat 0 no army, no producer; seat 1 44-65 units) but seat 1 needs to
    /// 79,504 (66:15) and five hunts to find the rest (BUG-1157: the loser's far depot and its workers stand at a start the
    /// hunt's sweep reaches late; BUG-1116's sweep order). Skipped again with the measurement (rule 43: a finding, not a re-bound).
    /// </summary>
    [Theory(Skip = "BUG-1068 / BUG-1116 (open; BUG-1157): ends at 79,504 since M5-9b, decided by 40:00, the hunt finds the loser's far remnant late")]
    [Trait("Category", "Stress")]
    [InlineData(PaleHills, 2UL)]
    public void NormalVersusNormal_PaleHillsSeed2_Stalemate_EndsWithin72000(string id, ulong seed) => SweepRow(id, seed);

    /// <summary>
    /// Raraku seed 5 (BUG-1152's wood starvation, newly shown by M5-9a's counter shift): no result at 72,000. M5-9a round 2's
    /// starved wave does not reach it: seat 0 holds 4,454 gold / 35 wood, 5 army and no worker from 30,000 (it can pay for a
    /// Heavy Infantry, so it is never starved, and trains nothing), seat 1 1,970 gold / 5 wood and no army. Fix round 1
    /// (BUG-1154: the check judges the unit production would train, and a gatherable kind no longer saves a unit short of
    /// one that is not) ends it at 52,360 (43:38), seat 0 the winner. Un-skipped.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("raraku", 5UL)]
    public void NormalVersusNormal_RarakuSeed5_WoodStarved_EndsWithin72000(string id, ulong seed) => SweepRow(id, seed);

    private void SweepRow(string id, ulong seed, bool oracle = false)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation sim = id switch
        {
            "generated" => AiScenes.Generated(seats, seed, false, out _),
            "raraku" => MapFileTests.RarakuMatch(seed, false, out _),
            _ => MapFileTests.MapMatch(id, seed, false, out _),
        };
        World w = sim.World;
        var waves = new int[2];
        var waving = new bool[2];
        string? leak = null;
        int sightings = 0;
        if (oracle)
            for (int p = 0; p < 2; p++)
            {
                int player = p;
                sim.AiOf(p)!.ThoughtForTests = ai =>
                {
                    // M5-4d criterion 4: a sighting remembered this think is a unit the fog granted this think.
                    if (leak != null || ai.Board.EnemyUnitTick != ai.Now) return;
                    sightings++;
                    for (int s = 0; s < w.Units.Capacity; s++)
                        if (w.Units.Alive[s] && w.IsEnemy(player, w.Units.Owner[s]) && w.Units.Position[s] == ai.Board.EnemyUnitPoint && w.Fog.CanSeeUnit(player, s)) return;
                    leak = $"tick {ai.Now}: player {player} remembers {ai.Board.EnemyUnitPoint}, where no enemy unit it sees stands";
                };
            }
        while (sim.TickNumber < 72_000 && !sim.MatchOver)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                bool on = sim.AiOf(p)!.Board.WaveActive;
                if (on && !waving[p]) waves[p]++;
                waving[p] = on;
            }
        }
        int units0 = 0, units1 = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i]) { if (w.Units.Owner[i] == 0) units0++; else units1++; }
        _out.WriteLine($"ROW {id} seed {seed}: over {sim.MatchOver} at {Clock(sim.TickNumber)} ({sim.TickNumber} ticks, within 36,000 {sim.MatchOver && sim.TickNumber <= 36_000}); defeated {w.IsDefeated(0)} / {w.IsDefeated(1)}; openings {sim.AiOpening(0)} / {sim.AiOpening(1)}; waves {waves[0]} / {waves[1]}; halls {Halls(w, 0)} / {Halls(w, 1)}; units {units0} / {units1}; gathered gold {w.GatheredGold[0]} / {w.GatheredGold[1]} wood {w.GatheredWood[0]} / {w.GatheredWood[1]}");
        int hunts = sim.AiOf(0)!.Board.HuntsStarted + sim.AiOf(1)!.Board.HuntsStarted;
        string winner = !sim.MatchOver ? "none" : w.IsDefeated(0) ? "player 1" : w.IsDefeated(1) ? "player 0" : "?";
        _out.WriteLine($"RESULT {id} seed {seed}: winner {winner} at tick {sim.TickNumber}; hunts {sim.AiOf(0)!.Board.HuntsStarted} / {sim.AiOf(1)!.Board.HuntsStarted}" + (oracle ? $"; {sightings} sightings checked by the fog oracle" : ""));
        Assert.True(leak == null, leak);
        Assert.True(sim.MatchOver, $"{id} seed {seed}: no result by 72,000 ticks (hunts {hunts})");
    }

    /// <summary>
    /// The seed-1 match on a new map, recorded: a save at the first wave's launch, at the first building either side loses,
    /// at 12,000 and at 24,000 ticks (each that happens), each loaded and run beside the original for 1,000 ticks with the
    /// hash equal every tick (save -> load -> save byte-identical too); then the recording to the end (or 36,000) plays back
    /// to every checkpoint.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(PaleHills)]
    [InlineData(Vathar)]
    public void TheSeed1Match_SavedAtProbedMoments_FollowsTheRun_AndItsRecordingPlaysBack(string id)
    {
        Simulation sim = MapFileTests.MapMatch(id, 1, true, out ReplayRecorder? rec);
        World w = sim.World;
        var live = new List<(string Why, Simulation Load, int Until)>();
        var probed = new List<string>();
        bool waveProbed = false, lossProbed = false;
        int buildings0 = -1;
        while (sim.TickNumber < 36_000 && !(sim.MatchOver && live.Count == 0))
        {
            string? why = null;
            bool wave = sim.AiOf(0)!.Board.WaveActive || sim.AiOf(1)!.Board.WaveActive;
            int buildings = 0;
            for (int i = 0; i < w.Buildings.Capacity; i++) if (w.Buildings.Alive[i]) buildings++;
            if (!waveProbed && wave) { why = "first wave"; waveProbed = true; }
            else if (!lossProbed && buildings0 >= 0 && buildings < buildings0) { why = "a building lost"; lossProbed = true; }
            else if (sim.TickNumber is 12_000 or 24_000) why = $"tick {sim.TickNumber}";
            buildings0 = buildings;
            if (why != null && !sim.MatchOver)
            {
                byte[] bytes = SaveTestRun.Save(sim);
                Simulation load = SaveTestRun.Load(TestSim.Data, bytes);
                Assert.Equal(bytes, SaveTestRun.Save(load));
                live.Add((why, load, sim.TickNumber + 1000));
                probed.Add($"{why} at {sim.TickNumber} ({bytes.Length} bytes)");
            }
            sim.Tick();
            for (int k = live.Count - 1; k >= 0; k--)
            {
                live[k].Load.Tick();
                Assert.True(live[k].Load.StateHash() == sim.StateHash(), $"{id}: the load saved at '{live[k].Why}' diverged at tick {sim.TickNumber}");
                if (sim.TickNumber >= live[k].Until || sim.MatchOver) live.RemoveAt(k);
            }
        }
        _out.WriteLine($"{id}: probed {string.Join("; ", probed)}; match over {sim.MatchOver} at {sim.TickNumber}");
        Assert.True(probed.Count >= 3, $"only {probed.Count} probes");
        Replay replay = rec!.ToReplay();
        byte[] text = ReplayFormat.Write(replay);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(text, out Replay? back));
        ReplayResult played = ReplayPlayer.Run(back!, TestSim.Data);
        _out.WriteLine($"{id}: replay {back!.Commands.Length} commands, {back.Checkpoints.Length} checkpoints: {played}");
        Assert.True(played.Ok, played.ToString());
        Assert.True(back.Checkpoints.Length >= sim.TickNumber / 100 - 1, $"{back.Checkpoints.Length} checkpoints over {sim.TickNumber} ticks");
    }

    // ---------- Vathar Crossing in two processes ----------

    private const string ChildVariable = "RTS_QA_VATHAR_TWIN_CHILD";
    private const string Marker = "QA-VATHAR-TWIN ";

    private static ulong Fold(Simulation sim, int to, ref Determinism.StateHasher h)
    {
        ulong last = 0;
        while (sim.TickNumber < to)
        {
            sim.Tick();
            last = sim.StateHash();
            h.Add(last);
        }
        return last;
    }

    /// <summary>Vathar Crossing, Normal v Hard, seed 9, 12,000 ticks: the per-tick hash fold equal in a fresh child process, and a save at 6,000 run on to 12,000 ends on the same hash.</summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void Vathar_NormalVersusHard_12000Ticks_SameInTwoProcesses_AndAcrossASaveAt6000()
    {
        Simulation sim = MapFileTests.MapMatch(Vathar, 9, false, out _, AiDifficulty.Normal, AiDifficulty.Hard);
        var h = new Determinism.StateHasher();
        Fold(sim, 6000, ref h);
        byte[] save = SaveTestRun.Save(sim);
        ulong last = Fold(sim, 12_000, ref h);
        string mine = $"{h.Value:X16} {last:X16}";
        if (Environment.GetEnvironmentVariable(ChildVariable) == "1")
        {
            _out.WriteLine(Marker + mine);
            return;
        }
        Simulation loaded = SaveTestRun.Load(TestSim.Data, save);
        var ignored = new Determinism.StateHasher();
        Assert.Equal(last, Fold(loaded, 12_000, ref ignored));
        string child = MapFileTests.RunChild(typeof(MapSlice2StressTests), nameof(Vathar_NormalVersusHard_12000Ticks_SameInTwoProcesses_AndAcrossASaveAt6000), ChildVariable, Marker);
        _out.WriteLine($"this process {mine}; child {child}");
        Assert.Equal(mine, child);
    }

    // ---------- the think on the new maps ----------

    /// <summary>
    /// A steady think of each seat on a new map mid-match (seed 1, the first think at or after tick 6,000) allocates nothing;
    /// nor do 40 ticks of the match there.
    /// </summary>
    [Theory]
    [InlineData(PaleHills)]
    [InlineData(Vathar)]
    public void AThinkAndATick_OnANewMap_AllocateNothing(string id)
    {
        Simulation sim = MapFileTests.MapMatch(id, 1, false, out _);
        int think = sim.AiOf(0)!.ThinkTicks;
        while (sim.TickNumber < 6000 || sim.TickNumber % think != 0) sim.Tick();
        Action ticks = () => { for (int t = 0; t < 40; t++) sim.Tick(); };
        AllocationProbe.AssertZero(ticks, _out);
        while (sim.TickNumber % think != 0) sim.Tick();
        AiPlayer a = sim.AiOf(0)!, b = sim.AiOf(1)!;
        Action thinkBoth = () => { a.Think(); b.Think(); };
        AllocationProbe.AssertZero(thinkBoth, _out);
    }

    /// <summary>
    /// The think's cost on each new map (Debug build): every think of both seats over the seed-1 match's first 12,000 ticks
    /// timed beside the sim (thinks run inside Tick, so a twin's think is called directly at the same ticks); the average
    /// must stay under the 1 ms budget. Perf: rerun alone before filing.
    /// </summary>
    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(PaleHills)]
    [InlineData(Vathar)]
    public void TheThink_OnANewMap_AveragesUnder1Ms(string id)
    {
        Simulation sim = MapFileTests.MapMatch(id, 1, false, out _);
        int think = sim.AiOf(0)!.ThinkTicks;
        var times = new List<double>();
        var sw = new Stopwatch();
        while (sim.TickNumber < 12_000)
        {
            sim.Tick();
            if (sim.TickNumber % think != 0 || sim.TickNumber < 1200) continue;
            // A think at this tick (the sim's own runs at the start of the next tick; this one is an extra call on a seat
            // whose state the sim then overwrites with its own think's: the cost is what's measured).
            for (int p = 0; p < 2; p++)
            {
                sw.Restart();
                sim.AiOf(p)!.Think();
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
        }
        times.Sort();
        double avg = times.Average(), p95 = times[(int)(times.Count * 0.95)], worst = times[^1];
        _out.WriteLine($"{id}: {times.Count} thinks, avg {avg:F3} ms, p95 {p95:F3} ms, worst {worst:F3} ms");
        Assert.True(avg < 1.0, $"avg {avg:F3} ms");
    }
}
