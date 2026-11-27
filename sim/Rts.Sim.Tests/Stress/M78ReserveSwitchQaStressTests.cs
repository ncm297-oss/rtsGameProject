using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M7-8 (Request 55): the reserve switches in whole matches. (1) A fuzz: every faction's every opening with a random
/// switch on each step, random maps, seats, difficulties and starting resources; twin runs equal every 100 ticks, a third
/// run saved and reloaded at ticks off the think boundary equal too, no exception, banks never negative. (2) A probe of
/// whether <c>noReserve</c> on the Teblor expand step can starve the expansion for good: order B against the shipped order,
/// three maps, Easy and Normal, to 30:00, the tick each seat's second Longhouse stands (a measurement: it prints).
/// </summary>
[Trait("Category", "Stress")]
public class M78ReserveSwitchQaStressTests(ITestOutputHelper output)
{
    private static readonly string[] Maps = { "pale_hills", "raraku", "vathar_crossing", "generated" };

    /// <summary>Shipped data with each step of every opening given its switch <c>true</c> with probability 1/2 (else left out), the reserve gate open (<c>reserveArmyShare</c> 0).</summary>
    private static GameData RandomSwitches(int seed, out int written)
    {
        int n = 0;
        var rng = new Random(seed);
        GameData d = AiScenes.DataWith(dir =>
        {
            foreach (string f in TestSim.ShippedFactions)
                dir.EditJson($"factions/{f}/ai.json", root =>
                {
                    foreach (JsonNode? o in root["openings"]!.AsArray())
                    {
                        // The reserve gate open from the start, so the switches have a reserve to act on in 4,000 ticks.
                        if (o!["attack"] is JsonObject attack) attack["reserveArmyShare"] = 0;
                        foreach (JsonNode? s in o["buildOrder"]!.AsArray())
                            if (rng.Next(2) == 0)
                            {
                                s![ReserveSwitchLoaderTests.SwitchFor((string)s["do"]!)] = true;
                                n++;
                            }
                    }
                });
            dir.EditJson("common/rules.json", r => r["ai"]!["reserveArmyShare"] = 0);
        });
        written = n;
        return d;
    }

    private static Simulation Match(GameData d, Random rng, out string what)
    {
        string map = Maps[rng.Next(Maps.Length)];
        int players = map is "generated" or "raraku" || rng.Next(2) == 0 ? 2 : 4; // raraku has two starts
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++)
            seats[p] = new PlayerSeat(rng.Next(d.Factions.Length), SeatController.Ai, (AiDifficulty)rng.Next(3));
        string start = rng.Next(3) == 0 ? "high" : StartingResourcesDef.StandardId;
        ulong seed = (ulong)rng.Next(1, 1000);
        what = $"{map} seed {seed} {start} seats [{string.Join(", ", seats.Select(s => $"{d.Factions[s.Faction].Key} {s.Difficulty}"))}]";
        int units = players > 2 ? 1024 : 400;
        if (map == "generated") return AiScenes.Generated(seats, seed, false, out _, data: d, units: units, configure: c => c with { StartingResources = start });
        int size = d.Maps[d.FindMap(map)].Size;
        return AiScenes.Generated(seats, seed, false, out _, data: d, units: units,
            configure: c => c with { MapId = map, StartingResources = start, Map = c.Map with { Width = size, Height = size } });
    }

    [Fact]
    public void RandomSwitches_RandomMatches_TwinsAndSavesEqual_NoException()
    {
        const int Seeds = 16, Bound = 4_000, Every = 100;
        var lines = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<string>();
        Parallel.For(1, Seeds + 1, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 3) }, seed =>
        {
            string what = $"fuzz seed {seed}";
            try
            {
                GameData d = RandomSwitches(seed, out int written);
                Simulation a = Match(d, new Random(seed * 7919), out what), b = Match(d, new Random(seed * 7919), out _), c = Match(d, new Random(seed * 7919), out _);
                int reserved = 0, saves = 0;
                for (int p = 0; p < a.World.Config.PlayerCount; p++) a.AiOf(p)!.ThoughtForTests = ai => { if (ai.ReserveStep >= 0) reserved++; };
                var rng = new Random(seed);
                int nextSave = 200 + rng.Next(1, 300);
                while (a.TickNumber < Bound && !a.MatchOver)
                {
                    a.Tick();
                    b.Tick();
                    c.Tick();
                    if (c.TickNumber == nextSave)
                    {
                        // A tick off the think boundary (thinks are every 10-20 ticks): mid-window.
                        c = SaveTestRun.Load(d, SaveTestRun.Save(c));
                        saves++;
                        nextSave += 500 + rng.Next(1, 37);
                    }
                    if (a.TickNumber % Every != 0) continue;
                    ulong h = a.StateHash();
                    if (h != b.StateHash()) throw new Exception($"twins differ at {a.TickNumber}");
                    if (h != c.StateHash()) throw new Exception($"the save chain differs at {a.TickNumber}");
                    for (int p = 0; p < a.World.Config.PlayerCount; p++)
                        if (a.World.Gold[p] < 0 || a.World.Wood[p] < 0) throw new Exception($"player {p} bank negative at {a.TickNumber}");
                }
                lines.Enqueue($"seed {seed}: {what}; {written} switches; {a.TickNumber} ticks, {saves} saves, {reserved} reserved thinks, end {a.StateHash():X16}");
            }
            catch (Exception e)
            {
                errors.Enqueue($"{what}: {e}");
            }
        });
        foreach (string l in lines.OrderBy(l => int.Parse(l.Split(':')[0].Split(' ')[1]))) output.WriteLine(l);
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        Assert.Equal(Seeds, lines.Count);
    }

    /// <summary>The tick each Teblor seat's second Longhouse stood finished (-1 never), and the tick its expand step was done.</summary>
    private static (int[] Second, int[] ExpandDone, ulong End) Play(GameData d, string map, AiDifficulty diff, ulong seed, int bound)
    {
        Simulation sim = TeblorSwitchOrders.Match(d, map, TeblorSwitchOrders.Mirror(d, diff), seed);
        World w = sim.World;
        int hall = d.FindBuilding("teblor_longhouse");
        var second = new[] { -1, -1 };
        var expandDone = new[] { -1, -1 };
        while (sim.TickNumber < bound && !sim.MatchOver)
        {
            sim.Tick();
            if (sim.TickNumber % 20 != 0) continue;
            var halls = new int[2];
            for (int s = 0; s < w.Buildings.Capacity; s++)
                if (w.Buildings.Alive[s] && (uint)w.Buildings.Owner[s] < 2u && !w.Buildings.UnderConstruction[s] && w.Buildings.TypeId[s] == hall) halls[w.Buildings.Owner[s]]++;
            for (int p = 0; p < 2; p++)
            {
                if (second[p] < 0 && halls[p] >= 2) second[p] = sim.TickNumber;
                AiPlayer ai = sim.AiOf(p)!;
                var steps = ai.Steps;
                for (int i = 0; i < steps.Length; i++)
                    if (steps[i].Action == AiStepAction.Expand && ai.Board.StepDone[i] && expandDone[p] < 0) expandDone[p] = sim.TickNumber;
            }
        }
        return (second, expandDone, sim.StateHash());
    }

    [Fact]
    public void NoReserveOnTheTeblorExpand_SecondLonghouseTimes_Report()
    {
        const int Bound = 36_000; // 30:00
        GameData shipped = TeblorSwitchOrders.Data("shipped"), b = TeblorSwitchOrders.Data("B");
        var jobs = new List<(string Order, string Map, AiDifficulty Diff, ulong Seed)>();
        foreach (string map in new[] { "pale_hills", "raraku", "vathar_crossing" })
            foreach (AiDifficulty diff in new[] { AiDifficulty.Easy, AiDifficulty.Normal })
                foreach (ulong seed in new[] { 1UL, 2UL })
                    foreach (string order in new[] { "shipped", "B" })
                        jobs.Add((order, map, diff, seed));
        var results = new (int[] Second, int[] ExpandDone, ulong End)[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 3) }, i =>
        {
            try { results[i] = Play(jobs[i].Order == "B" ? b : shipped, jobs[i].Map, jobs[i].Diff, jobs[i].Seed, Bound); }
            catch (Exception e) { errors.Enqueue($"{jobs[i]}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        static string T(int t) => t < 0 ? "never" : $"{t / 1200}:{t / 20 % 60:D2}";
        output.WriteLine("| map | difficulty | seed | order | seat 0 expand step done | seat 0 2nd Longhouse | seat 1 expand step done | seat 1 2nd Longhouse | end hash |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        for (int i = 0; i < jobs.Count; i++)
            output.WriteLine($"| {jobs[i].Map} | {jobs[i].Diff} | {jobs[i].Seed} | {jobs[i].Order} | {T(results[i].ExpandDone[0])} | {T(results[i].Second[0])} | {T(results[i].ExpandDone[1])} | {T(results[i].Second[1])} | {results[i].End:X16} |");
    }
}
