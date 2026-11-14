using System.Collections.Concurrent;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D31 (session 2026-11-14-0616), a measurement (rule 48: only preconditions asserted). The shipped share's row
/// re-run independently of the developer's harness (<c>TeblorOpeningHorizonReportTests.PlayAll</c>): QA's own copy of
/// <see cref="DH8OwnShareQaStressTests"/>' match loop, on <see cref="TestSim.Data"/> exactly as it ships (V24, own share
/// 0.25, carry 15, the Frenzy rows) and on the shipped data with only the share at 0.5; seeds 1-8, QA's eight pairings
/// (four maps x Malazan / Whirlwind), Normal against Normal, to 15:00. Printed in D30's columns.
/// </summary>
[Trait("Category", "Stress")]
public class D31ShippedShareQaStressTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ten = 12_000;
    private const int Fifteen = 18_000;
    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly string[] Opponents = { "malazan", "whirlwind" };

    private sealed record Row(ulong Seed, string Map, string Opponent, int AgeTwoAt, int Waves10, int ExpandedAt, bool EveryKind, int AnointedAt,
        int DefeatedAt, int Army15Half, int FrenzyStarts);

    private static Row Play(GameData data, string map, string opponent, ulong seed)
    {
        int teblor = data.FindFaction("teblor");
        PlayerSeat t = new(teblor, SeatController.Ai, AiDifficulty.Normal);
        PlayerSeat o = new(data.FindFaction(opponent), SeatController.Ai, AiDifficulty.Normal);
        bool first = opponent == "malazan";
        PlayerSeat[] seats = first ? new[] { t, o } : new[] { o, t };
        int p = first ? 0 : 1;
        Simulation sim = map == "generated"
            ? AiScenes.Generated(seats, seed, false, out _, data: data)
            : AiScenes.Generated(seats, seed, false, out _, data: data,
                configure: c => c with { MapId = map, Map = c.Map with { Width = data.Maps[data.FindMap(map)].Size, Height = data.Maps[data.FindMap(map)].Size } });
        World w = sim.World;
        Assert.Equal(teblor, w.FactionOf(p));
        AiPlayer ai = sim.AiOf(p)!;
        var kinds = new SortedSet<int>();
        foreach (AiStep s in ai.Opening.BuildOrder) if (s.Action == AiStepAction.Build) kinds.Add(s.Target);
        var built = new HashSet<int>();
        int hall = data.FindBuilding("teblor_longhouse"), anoint = data.FindTech("teblor_blood_oil_anointing"), frenzy = data.FindStatus("teblor_frenzied");
        int ageAt = -1, waves10 = 0, expanded = -1, fell = -1, starts = 0;
        var had = new bool[w.Units.Capacity];
        var gen = new int[w.Units.Capacity];
        while (sim.TickNumber < Fifteen)
        {
            sim.Tick();
            int now = sim.TickNumber;
            if (ageAt < 0 && w.Age(p) >= 2) ageAt = now;
            if (now == Ten) waves10 = ai.Board.WavesStarted;
            if (fell < 0 && w.IsDefeated(p)) fell = now;
            var u = w.Units;
            for (int s = 0; s < u.Capacity; s++)
            {
                bool has = false;
                if (u.Alive[s] && u.Owner[s] == p)
                    for (int k = 0; k < u.Statuses.Count[s]; k++)
                        if (u.Statuses.StatusId[s * Rts.Sim.Abilities.StatusStore.PerUnit + k] == frenzy) has = true;
                if (u.Generation[s] != gen[s]) (gen[s], had[s]) = (u.Generation[s], false);
                if (has && !had[s]) starts++;
                had[s] = has;
            }
            if (now % Tps != 0) continue;
            var bs = w.Buildings;
            int halls = 0;
            for (int s = 0; s < bs.Capacity; s++)
            {
                if (!bs.Alive[s] || bs.Owner[s] != p || bs.UnderConstruction[s]) continue;
                built.Add(bs.TypeId[s]);
                if (bs.TypeId[s] == hall) halls++;
            }
            if (expanded < 0 && halls >= 2) expanded = now;
        }
        int army = 0;
        for (int s = 0; s < w.Units.Capacity; s++)
            if (w.Units.Alive[s] && w.Units.Owner[s] == p && data.Units[w.Units.TypeId[s]].Slot != UnitSlot.Worker) army += data.Units[w.Units.TypeId[s]].HalfPop;
        return new Row(seed, map, opponent, ageAt, waves10, expanded, kinds.All(built.Contains), w.TechResearchedTick(p, anoint), fell, army, starts);
    }

    private static string T(int tick) => tick < 0 ? "never" : $"{tick / Tps / 60}:{tick / Tps % 60:00}";

    [Theory]
    [InlineData(0.25)] // the shipped data, untouched
    [InlineData(0.5)]  // the shipped data, the share alone moved
    public void ShippedTree_Seeds1To8_Report(double share)
    {
        GameData data = share == 0.25 ? TestSim.Data
            : AiScenes.DataWithAi("teblor", root => root["openings"]![0]!["attack"]!["reserveArmyShare"] = share);
        int teblor = data.FindFaction("teblor");
        Assert.Equal((float)share, data.Ai[teblor].Openings[0].ReserveArmyShare);
        Assert.Equal(15, data.CarryOf(data.FindUnit("teblor_gatherer")));
        Assert.True(data.FindStatus("teblor_frenzied") >= 0);
        var jobs = new List<(ulong S, string M, string O)>();
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            foreach (string m in Maps)
                foreach (string o in Opponents) jobs.Add((seed, m, o));
        var rows = new Row[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            try { rows[i] = Play(data, jobs[i].M, jobs[i].O, jobs[i].S); }
            catch (Exception e) { errors.Enqueue($"{jobs[i]}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        output.WriteLine($"QA D31: the shipped tree, own share {share}, {rows.Length} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s");
        foreach (Row r in rows)
            output.WriteLine($"| {r.Seed} | {r.Map} | {r.Opponent} | Age II {T(r.AgeTwoAt)} | waves10 {r.Waves10} | expanded {T(r.ExpandedAt)} | every kind {r.EveryKind} | Anointing {T(r.AnointedAt)} | defeated {T(r.DefeatedAt)} | army15 {r.Army15Half / 2.0} | frenzy starts {r.FrenzyStarts} |");
        int met = rows.GroupBy(r => r.Seed).Count(g => g.Count(r => r.AgeTwoAt >= 0 && r.AgeTwoAt <= Ten) >= 6 && g.Count(r => r.Waves10 > 0) >= 6);
        output.WriteLine("| seeds met | Age II by 10:00 | wave by 10:00 | expanded | every building kind | Anointing | defeated by 15:00 | army pop 15:00 (sum) | Frenzy starts (unit-applications) |");
        output.WriteLine($"| {met}/8 | {rows.Count(r => r.AgeTwoAt >= 0 && r.AgeTwoAt <= Ten)} | {rows.Count(r => r.Waves10 > 0)} | {rows.Count(r => r.ExpandedAt >= 0)} | {rows.Count(r => r.EveryKind)} | "
            + $"{rows.Count(r => r.AnointedAt >= 0)} | {rows.Count(r => r.DefeatedAt >= 0)} | {rows.Sum(r => r.Army15Half) / 2.0} | {rows.Sum(r => r.FrenzyStarts)} |");
        Assert.Equal(64, rows.Length);
    }
}
