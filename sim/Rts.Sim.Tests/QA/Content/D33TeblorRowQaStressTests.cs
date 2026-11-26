using System.Collections.Concurrent;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D33 (session 2026-11-17-0816), a measurement (rule 48: only preconditions asserted). BUG-1420's 64-seat row in QA's
/// own loop (the counting of <see cref="D32OpeningQaStressTests"/>, not the developer's
/// <c>Content/D33BalanceReportTests.Play</c>): the Teblor <c>standard</c> on seat 0 against the Malazan and on seat 1
/// against the Whirlwind, the same difficulty on both seats, four maps, seeds 1-8, to 15:00. Rows: <c>main</c>'s data
/// (<see cref="D33QaTests.Main"/>, hash B74DC7AE61B4862D, the brief's "before") and the shipped data, at Easy, Normal and
/// Hard, so the brief's "today" figures and the Easy row of BUG-1420's table can be read beside D33's.
/// </summary>
[Trait("Category", "Stress")]
public class D33TeblorRowQaStressTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ten = 12_000;
    private const int Fifteen = 18_000;
    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly ulong[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8 };
    private static readonly string[] AgeTwoUnits = { "teblor_javelin_thrower", "teblor_shaman", "teblor_breaker", "teblor_destrier_rider", "teblor_war_dog" };

    private sealed record Row(ulong Seed, string Map, string Opponent, int AgeTwoAt, int Waves10, int DefeatedAt, int HallAt, int AnointedAt, bool EveryKind, bool[] Fielded);

    private static Row Play(GameData data, string map, string opponent, ulong seed, AiDifficulty diff)
    {
        int teblor = data.FindFaction("teblor");
        PlayerSeat t = new(teblor, SeatController.Ai, diff);
        PlayerSeat o = new(data.FindFaction(opponent), SeatController.Ai, diff);
        bool first = opponent != "whirlwind";
        PlayerSeat[] seats = first ? new[] { t, o } : new[] { o, t };
        int p = first ? 0 : 1;
        Simulation sim = map == "generated"
            ? AiScenes.Generated(seats, seed, false, out _, data: data)
            : AiScenes.Generated(seats, seed, false, out _, data: data,
                configure: c => c with { MapId = map, Map = c.Map with { Width = data.Maps[data.FindMap(map)].Size, Height = data.Maps[data.FindMap(map)].Size } });
        World w = sim.World;
        Assert.Equal(teblor, w.FactionOf(p));
        AiPlayer ai = sim.AiOf(p)!;
        Assert.Equal("standard", ai.Opening.Id);
        var kinds = new SortedSet<int>();
        foreach (AiStep s in ai.Steps) if (s.Action == AiStepAction.Build) kinds.Add(s.Target);
        var built = new bool[data.Buildings.Length];
        int hall = data.FindBuilding("teblor_longhouse");
        int[] ageTwo = AgeTwoUnits.Select(data.FindUnit).ToArray();
        Assert.DoesNotContain(-1, ageTwo);
        var fielded = new bool[ageTwo.Length];
        int ageAt = -1, waves10 = 0, fell = -1, hallAt = -1;
        while (sim.TickNumber < Fifteen)
        {
            sim.Tick();
            int now = sim.TickNumber;
            if (ageAt < 0 && w.Age(p) >= 2) ageAt = now;
            if (now == Ten) waves10 = ai.Board.WavesStarted;
            if (fell < 0 && w.IsDefeated(p)) fell = now;
            if (now % Tps != 0) continue;
            var u = w.Units;
            for (int s = 0; s < u.Capacity; s++)
            {
                if (!u.Alive[s] || u.Owner[s] != p) continue;
                int k = Array.IndexOf(ageTwo, u.TypeId[s]);
                if (k >= 0) fielded[k] = true;
            }
            var bs = w.Buildings;
            int halls = 0;
            for (int s = 0; s < bs.Capacity; s++)
            {
                if (!bs.Alive[s] || bs.Owner[s] != p || bs.UnderConstruction[s]) continue;
                built[bs.TypeId[s]] = true;
                if (bs.TypeId[s] == hall) halls++;
            }
            if (hallAt < 0 && halls >= 2) hallAt = now;
        }
        return new Row(seed, map, opponent, ageAt, waves10, fell, hallAt, w.TechResearchedTick(p, data.FindTech("teblor_blood_oil_anointing")),
            kinds.All(k => built[k]), fielded);
    }

    [Theory]
    [InlineData("main", "Normal")]
    [InlineData("main", "Hard")]
    [InlineData("main", "Easy")]
    [InlineData("shipped", "Normal")]
    [InlineData("shipped", "Hard")]
    [InlineData("shipped", "Easy")]
    public void TeblorRow_Report(string which, string difficulty)
    {
        // D34 fix round 1 (BUG-1641): "shipped" is D33's shipped data, its opening from the D33 fixture on a shipped copy (was
        // TestSim.Data, which is D34's since the Teblor opening moved), so this row stays D33's record.
        // M6-S13 (sim track, the hash move): on the shipped files with the draw block out (D33's data had none).
        GameData data = which == "main" ? D33QaTests.Main : Draw.DrawScenes.TeblorOpeningBeforeDraw(Rts.Sim.Tests.Content.TeblorOpeningFixtureTests.D33Fixture());
        Assert.Equal(which == "main" ? D33QaTests.MainHash : D33QaTests.D33Hash, data.ContentHash());
        AiDifficulty diff = Enum.Parse<AiDifficulty>(difficulty);
        var jobs = new List<(ulong S, string M, string O)>();
        foreach (ulong seed in Seeds)
            foreach (string m in Maps)
                foreach (string o in new[] { "malazan", "whirlwind" }) jobs.Add((seed, m, o));
        var rows = new Row[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            try { rows[i] = Play(data, jobs[i].M, jobs[i].O, jobs[i].S, diff); }
            catch (Exception e) { errors.Enqueue($"{jobs[i]}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        output.WriteLine($"QA D33: {which} data ({data.ContentHash():X16}), {difficulty} v {difficulty}, {rows.Length} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s");
        output.WriteLine("| data | difficulty | defeated | 2nd Longhouse | Age II by 10:00 | wave by 10:00 | every kind | Anointing | Javelin / Shaman / Breaker / Rider / War-dog | all three new kinds |");
        output.WriteLine($"| {which} | {difficulty} | {rows.Count(r => r.DefeatedAt >= 0)} | {rows.Count(r => r.HallAt >= 0)} | {rows.Count(r => r.AgeTwoAt >= 0 && r.AgeTwoAt <= Ten)} | "
            + $"{rows.Count(r => r.Waves10 > 0)} | {rows.Count(r => r.EveryKind)} | {rows.Count(r => r.AnointedAt >= 0)} | "
            + $"{string.Join(" / ", Enumerable.Range(0, AgeTwoUnits.Length).Select(k => rows.Count(r => r.Fielded[k])))} | {rows.Count(r => r.Fielded[2] && r.Fielded[3] && r.Fielded[4])} |");
        foreach (string m in Maps)
        {
            var g = rows.Where(r => r.Map == m).ToList();
            output.WriteLine($"  {m}: defeated {g.Count(r => r.DefeatedAt >= 0)}, 2nd Longhouse {g.Count(r => r.HallAt >= 0)}, Breaker/Rider/Dog {g.Count(r => r.Fielded[2])}/{g.Count(r => r.Fielded[3])}/{g.Count(r => r.Fielded[4])}, Anointing {g.Count(r => r.AnointedAt >= 0)}");
        }
        Assert.Equal(64, rows.Length);
    }
}
