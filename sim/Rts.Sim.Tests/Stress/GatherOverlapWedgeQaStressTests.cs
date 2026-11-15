using System.Numerics;
using System.Reflection;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M6-S9 re-check (session 2026-11-14-2156, fix round 1): attacks on the developer's <c>KnownWedges</c> exclusions in
/// <see cref="GatherOverlapFuzzTests"/> and on the claim that the excluded wedges are today's rules, not the scaled distance.
/// (1) Every excluded row's 60 s breaches are the named wedge at a Keep and nothing else. (2) Switching the knob off in the
/// middle of the wedge (the World's two derived flags, set by reflection: test-only) shows whether the scaled distance is
/// what holds the worker. (3) A wider seed sweep shows whether the knob makes 60 s breaches more likely.
/// </summary>
[Collection(SerialCollection.Name)]
[Trait("Category", "Stress")]
public class GatherOverlapWedgeQaStressTests(ITestOutputHelper output)
{
    private const int Window = 60 * SimConstants.TicksPerSecond;

    /// <summary>One worker that passed 60 s without a trip, seen the tick it passed.</summary>
    internal sealed record Crosser(int Unit, int Tick, float Radius, int Cargo, UnitState State, float KeepGap, Vector2 Pos, int ParkedNear)
    {
        public override string ToString() =>
            $"unit {Unit} (r {Radius:F2}) at tick {Tick}: {State}, cargo {Cargo}, at {Pos}, {KeepGap:F2} m from its Keep's footprint, {ParkedNear} own loop-less workers within 2.5 m";
    }

    /// <summary>Distance from <paramref name="p"/> to <paramref name="player"/>'s first Keep footprint (0 inside); +inf if none.</summary>
    private static float KeepGap(World w, int player, Vector2 p)
    {
        BuildingStore b = w.Buildings;
        NavGrid g = w.NavGrid;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != player) continue;
            BuildingDef def = w.Data.Buildings[b.TypeId[k]];
            float x0 = b.Cell[k] % g.Width * MapConstants.CellSize, y0 = b.Cell[k] / g.Width * MapConstants.CellSize;
            float x1 = x0 + def.FootprintWidth * MapConstants.CellSize, y1 = y0 + def.FootprintHeight * MapConstants.CellSize;
            float dx = MathF.Max(MathF.Max(x0 - p.X, 0f), p.X - x1), dy = MathF.Max(MathF.Max(y0 - p.Y, 0f), p.Y - y1);
            return MathF.Sqrt(dx * dx + dy * dy);
        }
        return float.PositiveInfinity;
    }

    private static Crosser Describe(Simulation sim, int i)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        int parked = 0;
        for (int j = 0; j < u.Capacity; j++)
            if (j != i && u.Alive[j] && u.Owner[j] == u.Owner[i] && u.GatherNode[j].Generation == 0 && Vector2.Distance(u.Position[i], u.Position[j]) < 2.5f) parked++;
        return new Crosser(i, sim.TickNumber, u.Radius[i], u.Cargo[i], u.State[i], KeepGap(w, u.Owner[i], u.Position[i]), u.Position[i], parked);
    }

    /// <summary>Sets the World's derived knob flags (test-only, by reflection): what movement reads every tick.</summary>
    private static void SetKnob(World w, float overlap)
    {
        const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(World).GetField("<GatherOverlapOn>k__BackingField", f)!.SetValue(w, overlap > 0f);
        typeof(World).GetField("<GatherPairScale>k__BackingField", f)!.SetValue(w, 1f - overlap);
    }

    /// <summary>
    /// The fuzz on <paramref name="seed"/> with off data, the knob switched to <paramref name="on"/> after the first tick and back
    /// off from tick <paramref name="offFrom"/> (never when int.MaxValue). (The harness's floor check runs only for overlap &gt; 0,
    /// so the run is driven with off data and the flags switched.) Returns every crosser and the longest gap.
    /// </summary>
    private static (List<Crosser> Crossers, int Longest) Switched(ulong seed, float on, int offFrom)
    {
        var crossers = new List<Crosser>();
        int longest = 0;
        int state = 0; // 0 not yet on, 1 on, 2 off again
        GatherOverlapFuzzTests.Run(0, seed, tripWindow: false, watch: (sim, i, since) =>
        {
            if (state == 0) { SetKnob(sim.World, on); state = 1; }
            if (state == 1 && sim.TickNumber >= offFrom) { SetKnob(sim.World, 0f); state = 2; }
            longest = Math.Max(longest, since);
            if (since == Window + 1) crossers.Add(Describe(sim, i));
        });
        return (crossers, longest);
    }

    private static (List<Crosser> Crossers, int Longest) Plain(double overlap, ulong seed)
    {
        var crossers = new List<Crosser>();
        int longest = 0;
        GatherOverlapFuzzTests.Run(overlap, seed, tripWindow: false, watch: (sim, i, since) =>
        {
            longest = Math.Max(longest, since);
            if (since == Window + 1) crossers.Add(Describe(sim, i));
        });
        return (crossers, longest);
    }

    /// <summary>
    /// The four (value, seed) rows the developer's fuzz measures instead of asserting: every worker that passes 60 s without a
    /// trip there stands at its own Keep (within 3 m of the footprint), i.e. the exclusion hides only the wedge it names.
    /// </summary>
    [Theory]
    [InlineData(0.0, 4UL)]
    [InlineData(0.25, 3UL)]
    [InlineData(0.25, 4UL)]
    [InlineData(0.75, 7UL)]
    public void KnownWedges_EveryBreachInAnExcludedRow_IsTheNamedKeepWedge(double overlap, ulong seed)
    {
        var (crossers, longest) = Plain(overlap, seed);
        output.WriteLine($"seed {seed} at {overlap}: longest {longest / 20.0:F1} s; {crossers.Count} breach(es)");
        foreach (Crosser c in crossers) output.WriteLine("  " + c);
        // M6-S10 (BUG-1431 cured; the developer's KnownWedges is empty): these four rows assert the window again, and none
        // has a breach (longest 55.6 / 26.0 / 45.5 / 32.9 s). The row asserted Assert.NotEmpty(crossers) (the excluded wedge
        // present), then every breach within 3 m of its Keep (and 0.75 m wide at the cap); the per-breach checks stay for any
        // breach that comes back.
        Assert.Empty(crossers);
        foreach (Crosser c in crossers) Assert.True(c.KeepGap < 3f, $"a breach away from the Keep, not the named wedge: {c}");
        if (overlap == 0.75) Assert.All(crossers, c => Assert.Equal(0.75f, c.Radius));
    }

    /// <summary>
    /// The developer's claim, attacked: if today's rules hold the wedged worker, switching the knob off mid-wedge leaves it
    /// wedged past 60 s. Pass 1: the knob on from tick 2 (reproduces the row). Then the knob off from the stretch's start,
    /// and from 10, 20 and 40 s into it. Report: the longest gap for each. No bound (a probe for the Producer).
    /// </summary>
    [Theory]
    [InlineData(0.75f, 7UL)]
    [InlineData(0.25f, 3UL)]
    [InlineData(0.25f, 4UL)]
    public void KnobSwitchedOffMidWedge_Report(float on, ulong seed)
    {
        var (baseCrossers, baseLongest) = Switched(seed, on, int.MaxValue);
        output.WriteLine($"seed {seed}, knob {on} from tick 2: longest {baseLongest / 20.0:F1} s; breaches: {string.Join("; ", baseCrossers)}");
        var (offCrossers, offLongest) = Plain(0, seed);
        output.WriteLine($"seed {seed}, knob off throughout: longest {offLongest / 20.0:F1} s; breaches: {string.Join("; ", offCrossers)}");
        if (baseCrossers.Count == 0) return;
        int start = baseCrossers[0].Tick - (Window + 1);
        foreach (int s in new[] { 0, 10, 20, 40 })
        {
            var (c, l) = Switched(seed, on, start + s * SimConstants.TicksPerSecond);
            output.WriteLine($"  knob off from {s} s into the stretch (tick {start + s * 20}): longest {l / 20.0:F1} s; breaches: {(c.Count == 0 ? "none" : string.Join("; ", c))}");
        }
    }

    /// <summary>
    /// Seed 7 at the cap, the real run (data at 0.75): the knob switched off, or (the control) nudged to 0.74, from the wedged
    /// walker's stretch start and 10 / 20 / 40 s into it. If off frees it and 0.74 doesn't, the scaled distance holds it; if
    /// both free it, the wedge is chaotic (any perturbation frees it); if neither, today's rules hold it. (Off can't break the
    /// harness's floor check at 0.75: off pairs keep above 0.125 of their sum.) A probe, no bound.
    /// </summary>
    [Fact]
    public void Seed7AtTheCap_RealRun_KnobOffOrNudgedMidWedge_Report()
    {
        var (baseCrossers, baseLongest) = Plain(0.75, 7);
        output.WriteLine($"seed 7 at 0.75: longest {baseLongest / 20.0:F1} s; breaches: {string.Join("; ", baseCrossers)}");
        // M6-S10: the wedge is gone (32.9 s), so there is nothing to switch mid-wedge. The probe asserted
        // Assert.NotEmpty(baseCrossers) (81.7 s before the cure); re-stated: no breach at the cap on this seed.
        Assert.Empty(baseCrossers);
        if (baseCrossers.Count == 0) return;
        int start = baseCrossers[0].Tick - (Window + 1);
        foreach (float to in new[] { 0f, 0.74f })
            foreach (int s in new[] { 0, 10, 20, 40 })
            {
                var crossers = new List<Crosser>();
                int longest = 0;
                bool done = false;
                int from = start + s * SimConstants.TicksPerSecond;
                GatherOverlapFuzzTests.Run(0.75, 7, tripWindow: false, watch: (sim, i, since) =>
                {
                    if (!done && sim.TickNumber >= from) { SetKnob(sim.World, to); done = true; }
                    longest = Math.Max(longest, since);
                    if (since == Window + 1) crossers.Add(Describe(sim, i));
                });
                output.WriteLine($"  knob {to} from {s} s into the stretch (tick {from}): longest {longest / 20.0:F1} s; breaches: {(crossers.Count == 0 ? "none" : string.Join("; ", crossers))}");
            }
    }

    /// <summary>The knob-only breaches of the wider sweep (seed 23 at 0.25 / 0.5, seed 31 at 0.25), every worker past 50 s at 0 and on, described.</summary>
    [Theory]
    [InlineData(23UL, 0.5)]
    [InlineData(23UL, 0.25)]
    [InlineData(31UL, 0.25)]
    public void WiderSweep_KnobOnlyBreaches_Described(ulong seed, double overlap)
    {
        foreach (double v in new[] { 0.0, overlap })
        {
            var seen = new List<Crosser>();
            int longest = 0;
            GatherOverlapFuzzTests.Run(v, seed, tripWindow: false, watch: (sim, i, since) =>
            {
                longest = Math.Max(longest, since);
                if (since == 50 * SimConstants.TicksPerSecond) seen.Add(Describe(sim, i));
            });
            output.WriteLine($"seed {seed} at {v}: longest {longest / 20.0:F1} s; past 50 s: {(seen.Count == 0 ? "none" : string.Join("; ", seen))}");
        }
    }

    /// <summary>
    /// Does the knob make a 60 s breach more likely? The fuzz on seeds 17-56 at 0 / 0.25 / 0.5 / 0.75: per value the seeds past
    /// 60 s, and the breaches that form only with the knob on. Asserted: none forms only at 0.5 (the Producer's default to ship).
    /// </summary>
    [Fact]
    public void WiderSeedSweep_17To56_BreachesByValue()
    {
        double[] values = { 0.0, 0.25, 0.5, 0.75 };
        var over = new Dictionary<double, List<ulong>>();
        foreach (double v in values) over[v] = new List<ulong>();
        var knobOnly = new List<string>();
        int built = 0;
        for (ulong seed = 17; seed <= 56; seed++)
        {
            var longest = new Dictionary<double, int>();
            var where = new Dictionary<double, string>();
            try
            {
                foreach (double v in values)
                {
                    var (c, l) = Plain(v, seed);
                    longest[v] = l;
                    where[v] = c.Count == 0 ? "" : " [" + string.Join("; ", c) + "]";
                }
            }
            catch (Exception e) when (e.Message.Contains("no node within") || e.Message.Contains("no Keep"))
            {
                output.WriteLine($"seed {seed}: can't be built");
                continue;
            }
            built++;
            output.WriteLine($"seed {seed}: " + string.Join(", ", values.Select(v => $"{v}: {longest[v] / 20.0:F1} s{where[v]}")));
            foreach (double v in values)
            {
                if (longest[v] <= Window) continue;
                over[v].Add(seed);
                if (v > 0 && longest[0] <= Window) knobOnly.Add($"seed {seed} at {v}: {longest[v] / 20.0:F1} s ({longest[0] / 20.0:F1} s off)");
            }
        }
        output.WriteLine($"{built} seeds built. Past 60 s: " + string.Join(", ", values.Select(v => $"{v}: {over[v].Count} ({string.Join(" ", over[v])})")));
        output.WriteLine("Only with the knob on: " + (knobOnly.Count == 0 ? "none" : string.Join("; ", knobOnly)));
        Assert.DoesNotContain(knobOnly, s => s.Contains(" at 0.5:"));
    }
}
