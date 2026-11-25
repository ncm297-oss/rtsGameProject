using System.Collections.Concurrent;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M7-8 (Request 55), a measurement with no bound (rule 48: only its preconditions are asserted). The Teblor mirror on Pale
/// Hills, seeds 1-8 (16 Teblor seats a row), both seats at the row's difficulty, to 15:00, on the three orders of
/// <see cref="TeblorSwitchOrders"/>: the shipped <c>standard</c>; A, <c>throughReserve</c> on the Destrier Rider, War-dog and
/// Shaman train steps; B, A plus <c>noReserve</c> on the expand step. Per row, the seats that researched Blood-oil
/// Anointing, fielded a Shaman / a Destrier Rider / a War-dog pack / a Breaker (alive at some whole second), finished a
/// second Longhouse, and were defeated. docs/03 "For the data track (M7-8)" carries the table.
/// </summary>
[Trait("Category", "Stress")]
public class ReserveSwitchReportTests(ITestOutputHelper output)
{
    private const int Fifteen = 18_000;
    private static readonly ulong[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8 };
    private static readonly string[] Units = { "teblor_shaman", "teblor_destrier_rider", "teblor_war_dog", "teblor_breaker" };

    private sealed record Seat(bool Anointed, bool[] Fielded, bool SecondHall, bool Defeated);

    private static Seat[] Play(GameData data, ulong seed, AiDifficulty diff)
    {
        Simulation sim = TeblorSwitchOrders.Match(data, "pale_hills", TeblorSwitchOrders.Mirror(data, diff), seed);
        World w = sim.World;
        for (int p = 0; p < 2; p++) Assert.Equal("standard", sim.AiOf(p)!.Opening.Id);
        int[] types = Units.Select(data.FindUnit).ToArray();
        Assert.DoesNotContain(-1, types);
        int hall = data.FindBuilding("teblor_longhouse");
        var fielded = new bool[2][];
        var second = new bool[2];
        for (int p = 0; p < 2; p++) fielded[p] = new bool[types.Length];
        while (sim.TickNumber < Fifteen)
        {
            sim.Tick();
            if (sim.TickNumber % SimConstants.TicksPerSecond != 0) continue;
            var u = w.Units;
            for (int s = 0; s < u.Capacity; s++)
            {
                if (!u.Alive[s] || (uint)u.Owner[s] >= 2u) continue;
                int k = Array.IndexOf(types, u.TypeId[s]);
                if (k >= 0) fielded[u.Owner[s]][k] = true;
            }
            var halls = new int[2];
            var b = w.Buildings;
            for (int s = 0; s < b.Capacity; s++)
                if (b.Alive[s] && (uint)b.Owner[s] < 2u && !b.UnderConstruction[s] && b.TypeId[s] == hall) halls[b.Owner[s]]++;
            for (int p = 0; p < 2; p++) second[p] |= halls[p] >= 2;
        }
        Assert.Equal(Fifteen, sim.TickNumber);
        int anointing = data.FindTech("teblor_blood_oil_anointing");
        var seats = new Seat[2];
        for (int p = 0; p < 2; p++) seats[p] = new Seat(w.TechResearchedTick(p, anointing) >= 0, fielded[p], second[p], w.IsDefeated(p));
        return seats;
    }

    [Theory]
    [InlineData("Easy")]
    [InlineData("Normal")]
    public void TeblorMirror_PaleHills_ThreeOrders_Report(string difficulty)
    {
        AiDifficulty diff = Enum.Parse<AiDifficulty>(difficulty);
        var data = TeblorSwitchOrders.Names.Select(TeblorSwitchOrders.Data).ToArray();
        var jobs = new List<(int Order, ulong Seed)>();
        for (int o = 0; o < data.Length; o++)
            foreach (ulong s in Seeds) jobs.Add((o, s));
        var results = new Seat[jobs.Count][];
        var errors = new ConcurrentQueue<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            try { results[i] = Play(data[jobs[i].Order], jobs[i].Seed, diff); }
            catch (Exception e) { errors.Enqueue($"{TeblorSwitchOrders.Names[jobs[i].Order]} seed {jobs[i].Seed}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        output.WriteLine($"M7-8: the Teblor mirror on Pale Hills, {difficulty} v {difficulty}, seeds 1-8 (16 seats an order), to 15:00, {clock.Elapsed.TotalSeconds:0} s");
        output.WriteLine("| order | data hash | Anointing | Shaman | Rider | War-dog pack | Breaker | 2nd Longhouse | defeated |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        for (int o = 0; o < data.Length; o++)
        {
            var seats = Enumerable.Range(0, jobs.Count).Where(i => jobs[i].Order == o).SelectMany(i => results[i]).ToList();
            Assert.Equal(16, seats.Count);
            output.WriteLine($"| {TeblorSwitchOrders.Names[o]} | {data[o].ContentHash():X16} | {Count(seats, s => s.Anointed)} | {Count(seats, s => s.Fielded[0])} | {Count(seats, s => s.Fielded[1])} | "
                + $"{Count(seats, s => s.Fielded[2])} | {Count(seats, s => s.Fielded[3])} | {Count(seats, s => s.SecondHall)} | {Count(seats, s => s.Defeated)} |");
        }
        output.WriteLine("Each cell: seats of 16 (seat 0's of 8 + seat 1's of 8).");
    }

    /// <summary>"n (a + b)": the seats of <paramref name="seats"/> (seat 0, seat 1 alternating by match) that match, and how many of each start.</summary>
    private static string Count(List<Seat> seats, Func<Seat, bool> match)
    {
        int a = 0, b = 0;
        for (int i = 0; i < seats.Count; i++)
            if (match(seats[i])) { if (i % 2 == 0) a++; else b++; }
        return $"{a + b} ({a} + {b})";
    }
}
