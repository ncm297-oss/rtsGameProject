using System.Collections.Concurrent;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D32 (session 2026-11-14-1246), a measurement (rule 48: only preconditions asserted). The shipped Teblor opening (D32's
/// X7) re-run in QA's own match loop (a copy of <see cref="D31ShippedShareQaStressTests"/>' loop, not the developer's
/// <c>TeblorOpeningHorizonReportTests.PlayAll</c>), with the brief's four figures counted here: Age II and a wave by 10:00
/// per seed, defeats by 15:00, matches with a Shaman alive at any second before 15:00, matches with a second finished
/// Longhouse by 15:00. Rows: Normal (the brief's row), Easy and Hard on both seats, each beside D31's opening on the same
/// seeds, and the Teblor mirror (both seats measured) at the three difficulties. Four maps, seeds 1-8.
/// </summary>
[Trait("Category", "Stress")]
public class D32OpeningQaStressTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ten = 12_000;
    private const int Fifteen = 18_000;
    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly ulong[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8 };

    private sealed record Row(ulong Seed, string Map, string Opponent, int Seat, int AgeTwoAt, int FirstWaveAt, int Waves10, int DefeatedAt,
        int ShamanAt, int CircleAt, int ExpandSentAt, int HallAt, bool EveryKind, int AnointedAt, bool[] Fielded);

    /// <summary>The Age II mix's unit kinds, in the order the Fielded column prints them.</summary>
    private static readonly string[] AgeTwoUnits = { "teblor_javelin_thrower", "teblor_shaman", "teblor_breaker", "teblor_destrier_rider", "teblor_war_dog" };

    private static Row[] Play(GameData data, string map, string opponent, ulong seed, AiDifficulty diff)
    {
        int teblor = data.FindFaction("teblor");
        PlayerSeat t = new(teblor, SeatController.Ai, diff);
        PlayerSeat o = new(data.FindFaction(opponent), SeatController.Ai, diff);
        // As the brief's row: the Teblor on seat 0 against the Malazan, on seat 1 against the Whirlwind; both seats in the mirror.
        bool first = opponent != "whirlwind";
        PlayerSeat[] seats = first ? new[] { t, o } : new[] { o, t };
        int[] measured = opponent == "teblor" ? new[] { 0, 1 } : new[] { first ? 0 : 1 };
        Simulation sim = map == "generated"
            ? AiScenes.Generated(seats, seed, false, out _, data: data)
            : AiScenes.Generated(seats, seed, false, out _, data: data,
                configure: c => c with { MapId = map, Map = c.Map with { Width = data.Maps[data.FindMap(map)].Size, Height = data.Maps[data.FindMap(map)].Size } });
        World w = sim.World;
        int n = measured.Length;
        var ais = new AiPlayer[n];
        var expandIx = new int[n];
        var kinds = new SortedSet<int>[n];
        var built = new HashSet<int>[n];
        for (int k = 0; k < n; k++)
        {
            Assert.Equal(teblor, w.FactionOf(measured[k]));
            ais[k] = sim.AiOf(measured[k])!;
            Assert.Equal("standard", ais[k].Opening.Id);
            kinds[k] = new SortedSet<int>();
            built[k] = new HashSet<int>();
            expandIx[k] = -1;
            var steps = ais[k].Opening.BuildOrder;
            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i].Action == AiStepAction.Build) kinds[k].Add(steps[i].Target);
                if (steps[i].Action == AiStepAction.Expand) expandIx[k] = i;
            }
            Assert.True(expandIx[k] >= 0);
        }
        int hall = data.FindBuilding("teblor_longhouse"), circle = data.FindBuilding("teblor_shamans_circle");
        int shaman = data.FindUnit("teblor_shaman");
        int[] ageTwo = AgeTwoUnits.Select(data.FindUnit).ToArray();
        Assert.DoesNotContain(-1, ageTwo);
        int anoint = data.FindTech("teblor_blood_oil_anointing");
        var ageAt = new int[n]; var waveAt = new int[n]; var waves10 = new int[n]; var fell = new int[n];
        var shamanAt = new int[n]; var circleAt = new int[n]; var sentAt = new int[n]; var hallAt = new int[n];
        var fielded = new bool[n][];
        for (int k = 0; k < n; k++) fielded[k] = new bool[ageTwo.Length];
        for (int k = 0; k < n; k++) ageAt[k] = waveAt[k] = fell[k] = shamanAt[k] = circleAt[k] = sentAt[k] = hallAt[k] = -1;
        while (sim.TickNumber < Fifteen)
        {
            sim.Tick();
            int now = sim.TickNumber;
            for (int k = 0; k < n; k++)
            {
                int p = measured[k];
                if (ageAt[k] < 0 && w.Age(p) >= 2) ageAt[k] = now;
                if (waveAt[k] < 0 && ais[k].Board.WavesStarted > 0) waveAt[k] = now;
                if (now == Ten) waves10[k] = ais[k].Board.WavesStarted;
                if (fell[k] < 0 && w.IsDefeated(p)) fell[k] = now;
                if (sentAt[k] < 0 && ais[k].Board.StepDone[expandIx[k]]) sentAt[k] = now;
            }
            if (now % Tps != 0) continue;
            var u = w.Units;
            for (int s = 0; s < u.Capacity; s++)
            {
                if (!u.Alive[s]) continue;
                int k = Array.IndexOf(measured, u.Owner[s]);
                if (k < 0) continue;
                if (u.TypeId[s] == shaman && shamanAt[k] < 0) shamanAt[k] = now;
                int kind = Array.IndexOf(ageTwo, u.TypeId[s]);
                if (kind >= 0) fielded[k][kind] = true;
            }
            var bs = w.Buildings;
            var halls = new int[n];
            for (int s = 0; s < bs.Capacity; s++)
            {
                if (!bs.Alive[s] || bs.UnderConstruction[s]) continue;
                int k = Array.IndexOf(measured, bs.Owner[s]);
                if (k < 0) continue;
                built[k].Add(bs.TypeId[s]);
                if (bs.TypeId[s] == hall) halls[k]++;
                if (bs.TypeId[s] == circle && circleAt[k] < 0) circleAt[k] = now;
            }
            for (int k = 0; k < n; k++) if (hallAt[k] < 0 && halls[k] >= 2) hallAt[k] = now;
        }
        var rows = new Row[n];
        for (int k = 0; k < n; k++)
            rows[k] = new Row(seed, map, opponent, measured[k], ageAt[k], waveAt[k], waves10[k], fell[k], shamanAt[k], circleAt[k], sentAt[k], hallAt[k],
                kinds[k].All(built[k].Contains), w.TechResearchedTick(measured[k], anoint), fielded[k]);
        return rows;
    }

    /// <summary>
    /// Why a Circle that stands trains no Shaman: one match, the Teblor's thinks from its Circle's completion to 15:00, which
    /// step holds the reserve (and its cost), the bank, and the Shaman step's queued count. Measurement only.
    /// </summary>
    [Theory]
    [InlineData("Easy", "raraku", "malazan", 1UL)]
    [InlineData("Normal", "pale_hills", "teblor", 1UL)]
    public void ShamanStep_AfterTheCircle_ThinkByThink_Report(string difficulty, string map, string opponent, ulong seed)
    {
        GameData data = TestSim.Data;
        AiDifficulty diff = Enum.Parse<AiDifficulty>(difficulty);
        PlayerSeat t = new(data.FindFaction("teblor"), SeatController.Ai, diff);
        PlayerSeat o = new(data.FindFaction(opponent), SeatController.Ai, diff);
        int size = data.Maps[data.FindMap(map)].Size;
        Simulation sim = AiScenes.Generated(new[] { t, o }, seed, false, out _, data: data,
            configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
        AiPlayer ai = sim.AiOf(0)!;
        var steps = ai.Opening.BuildOrder;
        int shamanStep = -1;
        for (int i = 0; i < steps.Length && shamanStep < 0; i++)
            if (steps[i].Action == AiStepAction.Train && steps[i].Target == data.FindUnit("teblor_shaman")) shamanStep = i;
        int circle = data.FindBuilding("teblor_shamans_circle");
        Assert.True(shamanStep >= 0);
        var holders = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int thinks = 0, circleUp = -1, firstQueued = -1;
        ai.ThoughtForTests = a =>
        {
            if (circleUp < 0)
            {
                var bs = sim.World.Buildings;
                for (int s = 0; s < bs.Capacity; s++)
                    if (bs.Alive[s] && bs.Owner[s] == 0 && bs.TypeId[s] == circle && !bs.UnderConstruction[s]) circleUp = a.Now;
                if (circleUp < 0) return;
            }
            if (firstQueued < 0 && a.Board.StepQueued[shamanStep] > 0) firstQueued = a.Now;
            thinks++;
            string key = a.ReserveStep < 0 ? "none" : $"step {a.ReserveStep + 1} {steps[a.ReserveStep].Action} ({a.ReserveGold} g / {a.ReserveWood} w)";
            holders[key] = holders.TryGetValue(key, out int k) ? k + 1 : 1;
            if (thinks % 15 == 1)
                output.WriteLine($"{T(a.Now)} bank {a.View.Gold} g / {a.View.Wood} w | reserve {key} | defending {a.Board.Defending} | Shaman step queued {a.Board.StepQueued[shamanStep]} done {a.Board.StepDone[shamanStep]}");
        };
        while (sim.TickNumber < Fifteen) sim.Tick();
        UnitDef sh = data.Units[data.FindUnit("teblor_shaman")];
        output.WriteLine($"{difficulty} {map} v {opponent} seed {seed}: Circle up {T(circleUp)}, Shaman step (#{shamanStep + 1}) first queued {T(firstQueued)}; Shaman costs {sh.CostGold} g / {sh.CostWood} w; {thinks} thinks after the Circle");
        foreach (var kv in holders) output.WriteLine($"  reserve {kv.Key}: {kv.Value} thinks");
    }

    private static string T(int tick) => tick < 0 ? "never" : $"{tick / Tps / 60}:{tick / Tps % 60:00}";

    private static GameData D31Data() =>
        AiScenes.DataWithAi("teblor", root => root["openings"]![0] = D32TeblorOpeningReportTests.D31());

    private static string Median(IEnumerable<int> ticks)
    {
        var l = ticks.Where(t => t >= 0).OrderBy(t => t).ToList();
        return l.Count == 0 ? "-" : T(l[l.Count / 2]);
    }

    /// <summary>
    /// One row: <paramref name="opening"/> "shipped" or "d31"; <paramref name="opponents"/> "mw" (the brief's pairing: the
    /// Malazan with the Teblor on seat 0, the Whirlwind with it on seat 1) or "teblor" (the mirror, both seats measured).
    /// </summary>
    [Theory]
    [InlineData("shipped", "Normal", "mw")]
    [InlineData("d31", "Easy", "mw")]
    [InlineData("shipped", "Easy", "mw")]
    [InlineData("d31", "Hard", "mw")]
    [InlineData("shipped", "Hard", "mw")]
    [InlineData("shipped", "Easy", "teblor")]
    [InlineData("shipped", "Normal", "teblor")]
    [InlineData("shipped", "Hard", "teblor")]
    public void Opening_Row_Report(string opening, string difficulty, string opponents)
    {
        GameData data = opening == "shipped" ? TestSim.Data : D31Data();
        AiDifficulty diff = Enum.Parse<AiDifficulty>(difficulty);
        int teblor = data.FindFaction("teblor");
        int shamanSteps = data.Ai[teblor].Openings[0].BuildOrder.Count(s => s.Action == AiStepAction.Train && s.Target == data.FindUnit("teblor_shaman"));
        Assert.Equal(opening == "shipped" ? 1 : 0, shamanSteps);
        string[] opps = opponents == "mw" ? new[] { "malazan", "whirlwind" } : new[] { "teblor" };
        var jobs = new List<(ulong S, string M, string O)>();
        foreach (ulong seed in Seeds)
            foreach (string m in Maps)
                foreach (string o in opps) jobs.Add((seed, m, o));
        var results = new Row[jobs.Count][];
        var errors = new ConcurrentQueue<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            try { results[i] = Play(data, jobs[i].M, jobs[i].O, jobs[i].S, diff); }
            catch (Exception e) { errors.Enqueue($"{jobs[i]}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        Row[] rows = results.SelectMany(r => r).ToArray();
        output.WriteLine($"QA D32: opening {opening}, {difficulty} v {difficulty}, opponents {opponents}, {jobs.Count} matches ({rows.Length} Teblor seats) to 15:00 in {clock.Elapsed.TotalSeconds:0} s");
        foreach (Row r in rows)
            output.WriteLine($"| {r.Seed} | {r.Map} | {r.Opponent} | seat {r.Seat} | Age II {T(r.AgeTwoAt)} | 1st wave {T(r.FirstWaveAt)} | waves10 {r.Waves10} | defeated {T(r.DefeatedAt)} | "
                + $"Shaman {T(r.ShamanAt)} | Circle {T(r.CircleAt)} | expand sent {T(r.ExpandSentAt)} | 2nd Longhouse {T(r.HallAt)} | every kind {r.EveryKind} | Anointing {T(r.AnointedAt)} | fielded {string.Join(",", r.Fielded.Select(f => f ? 1 : 0))} |");
        bool Age10(Row r) => r.AgeTwoAt >= 0 && r.AgeTwoAt <= Ten;
        int met = rows.GroupBy(r => r.Seed).Count(g => g.Count(Age10) >= 6 && g.Count(r => r.Waves10 > 0) >= 6);
        output.WriteLine("| seeds met (6 of 8) | defeated by 15:00 | a Shaman | 2nd Longhouse by 15:00 | expand step sent | Circle | Age II by 10:00 | wave by 10:00 | 1st wave (median) | Age II (median) | every kind | Anointing | fielded: Javelin Throwers / Shamans / Breakers / Destrier Riders / War-dogs |");
        output.WriteLine($"| {met}/8 | {rows.Count(r => r.DefeatedAt >= 0)} | {rows.Count(r => r.ShamanAt >= 0)} | {rows.Count(r => r.HallAt >= 0)} | {rows.Count(r => r.ExpandSentAt >= 0)} | "
            + $"{rows.Count(r => r.CircleAt >= 0)} | {rows.Count(Age10)} | {rows.Count(r => r.Waves10 > 0)} | {Median(rows.Select(r => r.FirstWaveAt))} | {Median(rows.Select(r => r.AgeTwoAt))} | "
            + $"{rows.Count(r => r.EveryKind)} | {rows.Count(r => r.AnointedAt >= 0)} | {string.Join(" / ", Enumerable.Range(0, AgeTwoUnits.Length).Select(i => rows.Count(r => r.Fielded[i])))} | (of {rows.Length})");
        output.WriteLine("Per map: 2nd Longhouse / expand sent / Shaman / defeated");
        foreach (string m in Maps)
        {
            var g = rows.Where(r => r.Map == m).ToList();
            output.WriteLine($"| {m} | {g.Count(r => r.HallAt >= 0)} / {g.Count(r => r.ExpandSentAt >= 0)} / {g.Count(r => r.ShamanAt >= 0)} / {g.Count(r => r.DefeatedAt >= 0)} | (of {g.Count})");
        }
        output.WriteLine("Per seed: Age II 10:00 / wave 10:00 / defeated");
        output.WriteLine(string.Join(" | ", Seeds.Select(s =>
        {
            var g = rows.Where(r => r.Seed == s).ToList();
            return $"s{s} {g.Count(Age10)}/{g.Count(r => r.Waves10 > 0)}/{g.Count(r => r.DefeatedAt >= 0)}";
        })));
        Assert.Equal(64, rows.Length);
    }
}
