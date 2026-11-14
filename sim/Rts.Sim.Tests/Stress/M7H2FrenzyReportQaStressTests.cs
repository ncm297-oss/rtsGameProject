using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA for M7-H2 (BUG-1340): re-derives figures of the fixed <see cref="FrenzyCastReportTests"/> from the world, by a
/// second playthrough of each match that reads only the unit store and the sim's <see cref="World.AbilityEvents"/>:
/// (1) "landed": the report counts a rise of the Shaman's first ability ready tick; here, resolved Frenzy events of the
/// Teblor seat. (2) "first Shaman": the first tick a Teblor Shaman is alive. (3) "first army unit": the report reads the
/// AI's army list at thinks; here, the first tick an own non-worker, non-Shaman unit is alive (the report's is at most one
/// think later). (4) The diagnostic's "fighting >= 2 = 0": at every tick, no Teblor Shaman has two own non-Shaman army
/// units attacking within the Frenzy's radius (world truth, every tick, not only ready thinks: an upper bound).
/// </summary>
[Trait("Category", "Stress")]
public class M7H2FrenzyReportQaStressTests(ITestOutputHelper output)
{
    private const int Fifteen = 18_000;

    private sealed class World2
    {
        public int Landed, FirstShaman = -1, FirstArmy = -1, TicksFight2;
    }

    private static World2 Replay(GameData data, string opponent, ulong seed, bool second, int startShamans)
    {
        int teblor = data.FindFaction("teblor");
        PlayerSeat t = new(teblor, SeatController.Ai, AiDifficulty.Normal);
        PlayerSeat o = new(data.FindFaction(opponent), SeatController.Ai, AiDifficulty.Normal);
        int p = second ? 1 : 0;
        Simulation sim = AiScenes.Generated(second ? new[] { o, t } : new[] { t, o }, seed, false, out _, data: data);
        World w = sim.World;
        int shaman = data.FindUnit("teblor_shaman");
        int frenzy = data.FindAbility(BloodOilScenes.FrenzyAbility);
        float r2 = data.Abilities[frenzy].Radius * data.Abilities[frenzy].Radius;
        var r = new World2();
        bool spawned = startShamans == 0;
        while (sim.TickNumber < Fifteen)
        {
            sim.Tick();
            int now = sim.TickNumber;
            if (!spawned)
            {
                UnitStore us = w.Units;
                for (int s = 0; s < us.Capacity && !spawned; s++)
                {
                    if (!us.Alive[s] || us.Owner[s] != p) continue;
                    for (int k = 0; k < startShamans; k++) sim.Enqueue(Command.SpawnUnit(p, shaman, us.Position[s] + new Vector2(-0.5f, 0.8f + 0.9f * k)));
                    spawned = true;
                }
            }
            foreach (AbilityEvent e in w.AbilityEvents)
                if (e.Resolved && e.Owner == p && e.Ability == frenzy) r.Landed++;
            UnitStore u = w.Units;
            bool fight2 = false;
            for (int s = 0; s < u.Capacity; s++)
            {
                if (!u.Alive[s] || u.Owner[s] != p) continue;
                if (u.TypeId[s] != shaman)
                {
                    if (r.FirstArmy < 0 && data.Units[u.TypeId[s]].Slot != UnitSlot.Worker) r.FirstArmy = now;
                    continue;
                }
                if (r.FirstShaman < 0) r.FirstShaman = now;
                int fighting = 0;
                for (int k = 0; k < u.Capacity; k++)
                {
                    if (!u.Alive[k] || u.Owner[k] != p || u.TypeId[k] == shaman || data.Units[u.TypeId[k]].Slot == UnitSlot.Worker) continue;
                    if (u.State[k] == UnitState.Attacking && Vector2.DistanceSquared(u.Position[k], u.Position[s]) <= r2) fighting++;
                }
                if (fighting >= 2) fight2 = true;
            }
            if (fight2) r.TicksFight2++;
        }
        return r;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TheFixedReportsFigures_ReDerivedFromTheWorld(int startShamans)
    {
        GameData data = BloodOilScenes.Data;
        var jobs = new List<(string Opp, ulong Seed, bool Second)>();
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            foreach (string opp in new[] { "malazan", "whirlwind" })
                foreach (bool second in new[] { false, true }) jobs.Add((opp, seed, second));
        var report = new FrenzyCastReportTests.Match[jobs.Count];
        var world = new World2[jobs.Count];
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 4) }, i =>
        {
            report[i] = FrenzyCastReportTests.Play("qa", data, jobs[i].Opp, jobs[i].Seed, jobs[i].Second, startShamans);
            world[i] = Replay(data, jobs[i].Opp, jobs[i].Seed, jobs[i].Second, startShamans);
        });
        output.WriteLine($"start Shamans {startShamans}: report landed {report.Sum(m => m.Landed)} / world resolved Frenzy events {world.Sum(x => x.Landed)}");
        output.WriteLine($"matches with a Shaman: report {report.Count(m => m.FirstShaman >= 0)} / world {world.Count(x => x.FirstShaman >= 0)}");
        output.WriteLine($"first army unit (median): report {Med(report.Select(m => m.FirstArmy))} / world {Med(world.Select(x => x.FirstArmy))}; "
            + $"report minus world per match: {string.Join(",", Enumerable.Range(0, jobs.Count).Where(i => report[i].FirstArmy >= 0).Select(i => report[i].FirstArmy - world[i].FirstArmy))}");
        output.WriteLine($"ticks (any time, any Shaman) with 2+ own army units attacking within r: {world.Sum(x => x.TicksFight2)}; report's fighting >=2 thinks: {report.Sum(m => m.Phases.Sum(ph => ph.FightingNear[1]))}");
        for (int i = 0; i < jobs.Count; i++)
        {
            string at = $"seed {jobs[i].Seed} v {jobs[i].Opp} second {jobs[i].Second}";
            Assert.True(world[i].Landed == report[i].Landed, $"{at}: landed report {report[i].Landed} world {world[i].Landed}");
            Assert.True(world[i].FirstShaman == report[i].FirstShaman, $"{at}: first Shaman report {report[i].FirstShaman} world {world[i].FirstShaman}");
            if (report[i].FirstArmy >= 0)
                Assert.True(report[i].FirstArmy >= world[i].FirstArmy && world[i].FirstArmy >= 0, $"{at}: first army report {report[i].FirstArmy} world {world[i].FirstArmy}");
        }
        if (world.Sum(x => x.TicksFight2) == 0) Assert.Equal(0, report.Sum(m => m.Phases.Sum(ph => ph.FightingNear[1])));
    }

    private static string Med(IEnumerable<int> v)
    {
        int[] a = v.Where(x => x >= 0).OrderBy(x => x).ToArray();
        return a.Length == 0 ? "never" : $"{a[a.Length / 2] / 1200}:{a[a.Length / 2] / 20 % 60:00} ({a.Length})";
    }
}
