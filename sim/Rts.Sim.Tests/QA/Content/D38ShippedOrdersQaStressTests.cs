using System.Collections.Concurrent;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D38 inspection, 2026-11-26-0717, focus 2): the 64-seat row of each difficulty re-measured on the shipped file by
/// QA's own loop, not the developer's (<c>D34TeblorStepOrderReportTests.Play</c> / <c>D38ShippedOrdersReportTests</c>).
/// It uses the same row (four maps, the Teblor against the Malazan on seat 0 and the Whirlwind on seat 1, the same difficulty
/// on both seats, seeds 1-8, to 15:00) and the same definitions: "fielded" means alive and owned at a whole-second sample,
/// a second Longhouse means two finished halls at one sample, and defeat or the Anointing count by 15:00. Every seat also
/// checks that it plays its difficulty's list (<c>AiPlayer.Steps</c>). The expected figures are the brief's (D37's rows) in
/// D37's column order. A sim change that moves Teblor play moves this row (rule 41). <c>D38QA_DIFFS</c> narrows a run.
/// </summary>
[Trait("Category", "Stress")]
public class D38ShippedOrdersQaStressTests(ITestOutputHelper output)
{
    private static readonly Dictionary<AiDifficulty, string> Brief = new()
    {
        [AiDifficulty.Easy] = "1 / 0 / 38 / 28 / 34 / 42 / 22",
        [AiDifficulty.Normal] = "9 / 23 / 23 / 18 / 30 / 40 / 18",
        [AiDifficulty.Hard] = "11 / 23 / 23 / 40 / 42 / 47 / 25",
    };

    private sealed record Seen(AiDifficulty Diff, string Map, bool Defeated, bool TwoHalls, bool Shaman, bool Breaker, bool Rider, bool Dog, bool Anointed);

    private static Seen PlayOne(GameData data, AiDifficulty x, string map, string opponent, ulong seed)
    {
        int teblor = data.FindFaction("teblor");
        bool teblorFirst = opponent == "malazan";
        var mine = new PlayerSeat(teblor, SeatController.Ai, x);
        var theirs = new PlayerSeat(data.FindFaction(opponent), SeatController.Ai, x);
        PlayerSeat[] seats = teblorFirst ? new[] { mine, theirs } : new[] { theirs, mine };
        int me = teblorFirst ? 0 : 1;
        Simulation sim;
        if (map == "generated") sim = AiScenes.Generated(seats, seed, false, out _, data: data);
        else
        {
            int size = data.Maps[data.FindMap(map)].Size;
            sim = AiScenes.Generated(seats, seed, false, out _, data: data, configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
        }
        AiPlayer ai = sim.AiOf(me)!;
        if (!ai.Steps.SequenceEqual(data.Ai[teblor].Openings[0].BuildOrderFor(x))) throw new InvalidOperationException($"{x} seat does not play its list");
        int shaman = data.FindUnit("teblor_shaman"), breaker = data.FindUnit("teblor_breaker"), rider = data.FindUnit("teblor_destrier_rider"), dog = data.FindUnit("teblor_war_dog");
        int longhouse = data.FindBuilding("teblor_longhouse");
        bool fell = false, two = false, sh = false, br = false, ri = false, dg = false;
        World w = sim.World;
        int end = 15 * 60 * SimConstants.TicksPerSecond;
        while (sim.TickNumber < end)
        {
            sim.Tick();
            fell |= w.IsDefeated(me);
            if (sim.TickNumber % SimConstants.TicksPerSecond != 0) continue;
            for (int s = 0; s < w.Units.Capacity; s++)
            {
                if (!w.Units.Alive[s] || w.Units.Owner[s] != me) continue;
                int t = w.Units.TypeId[s];
                sh |= t == shaman; br |= t == breaker; ri |= t == rider; dg |= t == dog;
            }
            int halls = 0;
            for (int s = 0; s < w.Buildings.Capacity; s++)
                if (w.Buildings.Alive[s] && w.Buildings.Owner[s] == me && !w.Buildings.UnderConstruction[s] && w.Buildings.TypeId[s] == longhouse) halls++;
            two |= halls >= 2;
        }
        bool anointed = w.TechResearchedTick(me, data.FindTech("teblor_blood_oil_anointing")) >= 0;
        return new Seen(x, map, fell, two, sh, br, ri, dg, anointed);
    }

    [Fact]
    public void QA_ShippedFile_SixtyFourSeats_EqualTheBriefsRows_Report()
    {
        string? only = Environment.GetEnvironmentVariable("D38QA_DIFFS");
        AiDifficulty[] diffs = only is { Length: > 0 } ? only.Split(',').Select(Enum.Parse<AiDifficulty>).ToArray() : Enum.GetValues<AiDifficulty>();
        GameData data = TestSim.Data;
        string[] maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
        var jobs = new List<(AiDifficulty X, string M, string O, ulong S)>();
        foreach (AiDifficulty x in diffs)
            foreach (string m in maps)
                foreach (string o in new[] { "malazan", "whirlwind" })
                    for (ulong s = 1; s <= 8; s++) jobs.Add((x, m, o, s));
        var seen = new Seen[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = 6 }, i =>
        {
            var j = jobs[i];
            try { seen[i] = PlayOne(data, j.X, j.M, j.O, j.S); }
            catch (Exception e) { errors.Enqueue($"{j.X} {j.M} {j.O} {j.S}: {e.Message}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        output.WriteLine($"QA D38: {jobs.Count} matches in {clock.Elapsed.TotalSeconds:0} s, data {data.ContentHash():X16}");
        var differ = new List<string>();
        foreach (AiDifficulty x in diffs)
        {
            var g = seen.Where(r => r.Diff == x).ToList();
            Assert.Equal(64, g.Count);
            string row = $"{g.Count(r => r.Defeated)} / {g.Count(r => r.TwoHalls)} / {g.Count(r => r.Shaman)} / {g.Count(r => r.Breaker)} / {g.Count(r => r.Rider)} / {g.Count(r => r.Dog)} / {g.Count(r => r.Anointed)}";
            output.WriteLine($"| {x} | {row} | brief {Brief[x]} | {(row == Brief[x] ? "equal" : "DIFFERS")} |");
            if (row != Brief[x]) differ.Add($"{x}: QA {row}, brief {Brief[x]}");
        }
        Assert.True(differ.Count == 0, string.Join("; ", differ));
    }
}
