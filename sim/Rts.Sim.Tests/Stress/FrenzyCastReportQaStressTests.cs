using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA for M7-7 Part A: an independent re-measure of the developer's <see cref="FrenzyCastReportTests"/>, read from the
/// world (not the AI's view) at every Teblor think. Two things it checks. (1) "Too few Shamans": the first Shaman and the
/// Shamans alive at 5 / 10 / 15 min, counted from the unit store each tick. (2) "The Shaman stands with the army": the
/// developer's diagnostic counts the other spawned Shaman as "army", so in the opening minutes, when the army is the two
/// Shamans, the distance to the centroid is the gap between them. Here the army is own non-worker units that are not
/// Shamans, and the figures are split before and after 5:00. A measurement (no bound): only preconditions are asserted.
/// </summary>
[Trait("Category", "Stress")]
public class FrenzyCastReportQaStressTests(ITestOutputHelper output)
{
    private const int Five = 6_000, Ten = 12_000, Fifteen = 18_000;

    private sealed class Row
    {
        public int FirstShaman = -1, S5, S10, S15;
        // [phase]: 0 before 5:00, 1 after
        public readonly int[] Ready = new int[2], NoArmy = new int[2], Near1 = new int[2], Near2 = new int[2], Near4 = new int[2], Fight2 = new int[2], Enemy2 = new int[2];
        public readonly List<float>[] Dist = { new(), new() };
    }

    private static Row Play(GameData data, string opponent, ulong seed, bool second, int startShamans)
    {
        int teblor = data.FindFaction("teblor");
        PlayerSeat t = new(teblor, SeatController.Ai, AiDifficulty.Normal);
        PlayerSeat o = new(data.FindFaction(opponent), SeatController.Ai, AiDifficulty.Normal);
        int p = second ? 1 : 0;
        Simulation sim = AiScenes.Generated(second ? new[] { o, t } : new[] { t, o }, seed, false, out _, data: data);
        World w = sim.World;
        int shaman = data.FindUnit("teblor_shaman");
        // M7-H2 QA: by id, and the ready tick read at the Frenzy's own slot (was the first ability and slot 0).
        AbilityDef frenzy = data.Abilities[data.FindAbility(BloodOilScenes.FrenzyAbility)];
        int frenzySlot = data.Units[shaman].Abilities.IndexOf(frenzy.Id);
        Assert.True(frenzySlot >= 0, "the Shaman has no Frenzy");
        float r2 = frenzy.Radius * frenzy.Radius;
        int minTargets = data.AiRules.CastMinTargets;
        var row = new Row();
        sim.AiOf(p)!.ThoughtForTests = _ =>
        {
            UnitStore u = w.Units;
            int phase = sim.TickNumber < Five ? 0 : 1;
            bool any = false, enemy = false;
            int bestNear = 0, bestFight = 0;
            float best = float.MaxValue;
            bool army = false;
            for (int s = 0; s < u.Capacity; s++)
            {
                if (!u.Alive[s] || u.Owner[s] != p || u.TypeId[s] != shaman) continue;
                if (u.AbilityReadyTick[s * DataLimits.MaxUnitAbilities + frenzySlot] > sim.TickNumber + 1 || u.State[s] == UnitState.Casting) continue;
                any = true;
                Vector2 at = u.Position[s], sum = Vector2.Zero;
                int n = 0, near = 0, fight = 0, enemies = 0;
                for (int k = 0; k < u.Capacity; k++)
                {
                    if (!u.Alive[k]) continue;
                    if (u.Owner[k] != p) // 1v1: every other owner is the enemy (world truth, not the fogged view)
                    {
                        if (Vector2.DistanceSquared(u.Position[k], at) <= r2) enemies++;
                        continue;
                    }
                    if (u.TypeId[k] == shaman || w.Data.Units[u.TypeId[k]].Slot == UnitSlot.Worker) continue;
                    sum += u.Position[k];
                    n++;
                    if (Vector2.DistanceSquared(u.Position[k], at) > r2) continue;
                    near++;
                    if (u.State[k] == UnitState.Attacking) fight++;
                }
                if (enemies >= minTargets) enemy = true;
                bestNear = Math.Max(bestNear, near);
                bestFight = Math.Max(bestFight, fight);
                if (n > 0)
                {
                    army = true;
                    best = Math.Min(best, Vector2.Distance(sum / n, at));
                }
            }
            if (!any) return;
            row.Ready[phase]++;
            if (!army) row.NoArmy[phase]++;
            else row.Dist[phase].Add(best);
            if (bestNear >= 1) row.Near1[phase]++;
            if (bestNear >= 2) row.Near2[phase]++;
            if (bestNear >= 4) row.Near4[phase]++;
            if (bestFight >= 2) row.Fight2[phase]++;
            if (enemy) row.Enemy2[phase]++;
        };
        bool spawned = startShamans == 0;
        while (sim.TickNumber < Fifteen)
        {
            sim.Tick();
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
            int now = sim.TickNumber, alive = 0;
            UnitStore u = w.Units;
            for (int s = 0; s < u.Capacity; s++)
                if (u.Alive[s] && u.Owner[s] == p && u.TypeId[s] == shaman) alive++;
            if (row.FirstShaman < 0 && alive > 0) row.FirstShaman = now;
            if (now == Five) row.S5 = alive;
            if (now == Ten) row.S10 = alive;
            if (now == Fifteen) row.S15 = alive;
        }
        return row;
    }

    private static string Median(IEnumerable<float> v)
    {
        float[] a = v.OrderBy(x => x).ToArray();
        return a.Length == 0 ? "-" : $"{a[a.Length / 2]:0.0}";
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FrenzyReadyThinks_ArmyWithoutShamans_ByPhase_Report(int startShamans)
    {
        GameData data = BloodOilScenes.Data;
        var jobs = new List<(string, ulong, bool)>();
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            foreach (string o in new[] { "malazan", "whirlwind" })
                foreach (bool second in new[] { false, true }) jobs.Add((o, seed, second));
        var rows = new Row[jobs.Count];
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 4) },
            i => rows[i] = Play(data, jobs[i].Item1, jobs[i].Item2, jobs[i].Item3, startShamans));
        output.WriteLine($"start Shamans {startShamans}; 32 matches; army = own non-worker, non-Shaman units (world truth)");
        output.WriteLine($"matches with a Shaman: {rows.Count(r => r.FirstShaman >= 0)}; first: {string.Join(", ", rows.Where(r => r.FirstShaman >= 0).Select(r => r.FirstShaman).OrderBy(x => x).Select(x => $"{x / 1200}:{x / 20 % 60:00}"))}");
        output.WriteLine($"Shamans alive 5 / 10 / 15 (sum): {rows.Sum(r => r.S5)} / {rows.Sum(r => r.S10)} / {rows.Sum(r => r.S15)}");
        output.WriteLine("| phase | ready thinks | no army at all | army unit within r: 1+ / 2+ / 4+ | 2+ attacking within r | 2+ enemies within r (today) | dist to army centroid median |");
        for (int ph = 0; ph < 2; ph++)
            output.WriteLine($"| {(ph == 0 ? "< 5:00" : ">= 5:00")} | {rows.Sum(r => r.Ready[ph])} | {rows.Sum(r => r.NoArmy[ph])} | {rows.Sum(r => r.Near1[ph])} / {rows.Sum(r => r.Near2[ph])} / {rows.Sum(r => r.Near4[ph])} | "
                + $"{rows.Sum(r => r.Fight2[ph])} | {rows.Sum(r => r.Enemy2[ph])} | {Median(rows.SelectMany(r => r.Dist[ph]))} |");
        Assert.Equal(32, rows.Length);
        if (startShamans > 0) Assert.All(rows, r => Assert.True(r.FirstShaman >= 0));
    }
}
