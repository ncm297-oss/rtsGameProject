using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-4f (session 2026-10-16-2036), the BUG-0790 ruling: replays <c>GatherPocketStressTests</c>'s
/// <c>(MineOneSide, 40)</c> scene (the same map, spawns, seeds and wedge rule) and, at every tick a worker counts as
/// wedged, looks at the mine's face: is there a free standing spot (centre within <see cref="EconomyConstants.Reach"/> of
/// the footprint, the body clear of blocked cells and of every other unit) the worker could have taken?
/// </summary>
[Collection(SerialCollection.Name)]
public class Bug0790FaceProbeQaTests
{
    private readonly ITestOutputHelper _out;

    public Bug0790FaceProbeQaTests(ITestOutputHelper output) => _out = output;

    private const int MapW = 32, MapH = 26, Workers = 40, Bound = 2 * EconomyConstants.RetryTicks;

    internal static EntityHandle BuildMinePocket(Simulation sim)
    {
        World w = sim.World;
        void T(int x, int y) => Spawn(w, Tree, x, y, TreeWood);
        for (int x = 10; x <= 15; x++) T(x, 2);
        for (int y = 3; y <= 4; y++)
        {
            T(10, y); T(11, y); T(14, y); T(15, y);
        }
        return Spawn(w, Mine, 12, 3, 100_000);
    }

    /// <summary>One wedged tick seen by the probe.</summary>
    internal readonly record struct WedgeTick(int Tick, int Unit, int Run, Vector2 Pos, UnitState State, int InReach, float BestFree, Vector2 FreeAt, float RingFree, float CornerFree, float NearestFree, Vector2 NearestAt);

    /// <summary>Runs the row's scene for <paramref name="seed"/>; returns every tick some worker's run passed <paramref name="from"/>, with the face's free spot.</summary>
    internal static (int Worst, int WorstUnit, int WorstTick, List<WedgeTick> Ticks) Replay(GameData? data, ulong seed, int from, int ticks = 3000)
    {
        Simulation sim = data == null ? GatherMaps.NewSim(Flat(MapW, MapH), units: 48, seed: seed)
            : new Simulation(new SimConfig(seed, 1, 48, 4 * 48 + 16) { Data = data }, Flat(MapW, MapH));
        World w = sim.World;
        UnitStore u = w.Units;
        TestSim.KeepIn(sim, 0);
        EntityHandle node = BuildMinePocket(sim);
        Building(sim, 24, 18);
        var rng = new SimRng(seed, 907);
        var hs = new EntityHandle[Workers];
        var before = (bool[])u.Alive.Clone();
        for (int k = 0; k < Workers; k++)
            sim.Enqueue(Command.SpawnUnit(0, Laborer, new Vector2(4f + rng.NextFloat() * 36f, 30f + rng.NextFloat() * 16f)));
        Run(sim, 2);
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !before[i]) hs[n++] = new EntityHandle(i, u.Generation[i]);
        Assert.Equal(Workers, n);
        int cell = w.Resources.Cell[node.Index];
        foreach (EntityHandle h in hs) sim.Enqueue(Command.Gather(0, h, At(sim, cell % MapW, cell / MapW)));
        var run = new int[Workers];
        var anchor = new Vector2[Workers];
        var seen = new List<WedgeTick>();
        int worst = 0, worstUnit = -1, worstTick = 0;
        for (int t = 1; t <= ticks; t++)
        {
            sim.Tick();
            for (int k = 0; k < Workers; k++)
            {
                int i = hs[k].Index;
                if (!u.IsAlive(hs[k])) continue;
                bool nodeLeg = EconomySystem.OnLoop(u, i) && w.Resources.IsAlive(u.GatherNode[i]) && u.Cargo[i] < w.Data.Rules.WorkerCarry;
                if (!nodeLeg || InReach(w, i, u.Position[i]) || SomeoneBetween(w, i) || Vector2.Distance(u.Position[i], anchor[k]) > 0.5f)
                {
                    anchor[k] = u.Position[i];
                    run[k] = 0;
                    continue;
                }
                if (++run[k] > worst) (worst, worstUnit, worstTick) = (run[k], i, t);
                if (run[k] > from)
                {
                    float best = BestFreeSpot(w, i, out Vector2 at);
                    float ring = BestFreeSpot(w, i, out Vector2 ringAt, 1), corner = BestFreeSpot(w, i, out _, 2);
                    float nearest = BestFreeSpot(w, i, out Vector2 nearAt, 3);
                    seen.Add(new WedgeTick(t, i, run[k], u.Position[i], u.State[i], CountInReach(w, i), best, ringAt, ring, corner, nearest, nearAt));
                }
            }
        }
        return (worst, worstUnit, worstTick, seen);
    }

    private static bool InReach(World w, int i, Vector2 p)
    {
        int n = w.Units.GatherNode[i].Index;
        ResourceDef def = w.Data.Resources[w.Resources.TypeId[n]];
        return DistanceToFootprint(w.NavGrid, p, w.Resources.Cell[n], def.FootprintWidth, def.FootprintHeight) <= EconomyConstants.Reach;
    }

    private static int CountInReach(World w, int i)
    {
        int c = 0;
        for (int j = 0; j < w.Units.Capacity; j++)
            if (w.Units.Alive[j] && EconomySystem.IsWorker(w, j) && InReach(w, i, w.Units.Position[j])) c++;
        return c;
    }

    /// <summary>
    /// The largest clearance (m) over the face's spots in reach: min over other units of (distance − radii) and over blocked
    /// cells of (distance − own radius). ≥ 0: a free spot exists (the body fits without touching anyone); &lt; 0: every spot in
    /// reach overlaps something by at least that much.
    /// </summary>
    internal static float BestFreeSpot(World w, int i, out Vector2 bestAt) => BestFreeSpot(w, i, out bestAt, 0);

    /// <summary>
    /// <paramref name="region"/>: 0 any spot; 1 only spots in a cell 4-adjacent to the footprint (the cells
    /// <c>EconomySystem.WalkToFootprint</c> offers); 2 only spots in the other (diagonal corner) cells; 3 the free spot
    /// nearest the worker (returns its distance in m, or +inf with none; clearance ≥ 0 required).
    /// </summary>
    internal static float BestFreeSpot(World w, int i, out Vector2 bestAt, int region)
    {
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        int n = u.GatherNode[i].Index;
        ResourceDef def = w.Data.Resources[w.Resources.TypeId[n]];
        int anchor = w.Resources.Cell[n];
        const float cs = MapConstants.CellSize, step = 0.05f;
        float x0 = anchor % g.Width * cs, y0 = anchor / g.Width * cs, r = u.Radius[i];
        float x1 = x0 + def.FootprintWidth * cs, y1 = y0 + def.FootprintHeight * cs, reach = EconomyConstants.Reach;
        float best = float.NegativeInfinity;
        bestAt = default;
        for (float y = y0 - reach; y <= y1 + reach + 1e-4f; y += step)
            for (float x = x0 - reach; x <= x1 + reach + 1e-4f; x += step)
            {
                var p = new Vector2(x, y);
                if (DistanceToFootprint(g, p, anchor, def.FootprintWidth, def.FootprintHeight) > reach) continue;
                if (!g.WorldToCell(p, out int pcx, out int pcy) || !g.IsPassable(pcx, pcy)) continue;
                int ax = anchor % g.Width, ay = anchor / g.Width;
                bool ring = (pcx >= ax && pcx < ax + def.FootprintWidth) || (pcy >= ay && pcy < ay + def.FootprintHeight);
                if (region == 1 && !ring || region == 2 && ring) continue;
                float clear = float.PositiveInfinity;
                for (int cy = pcy - 1; cy <= pcy + 1; cy++)
                    for (int cx = pcx - 1; cx <= pcx + 1; cx++)
                    {
                        if (cx < 0 || cy < 0 || cx >= g.Width || cy >= g.Height) { continue; }
                        if (g.IsPassable(cx, cy)) continue;
                        var q = Vector2.Clamp(p, new Vector2(cx * cs, cy * cs), new Vector2((cx + 1) * cs, (cy + 1) * cs));
                        clear = MathF.Min(clear, Vector2.Distance(p, q) - r);
                    }
                for (int j = 0; j < u.Capacity && (region == 3 || clear > best); j++)
                {
                    if (j == i || !u.Alive[j]) continue;
                    clear = MathF.Min(clear, Vector2.Distance(p, u.Position[j]) - r - u.Radius[j]);
                }
                if (region == 3)
                {
                    float dist = Vector2.Distance(p, u.Position[i]);
                    if (clear >= 0f && (best == float.NegativeInfinity || dist < best)) (best, bestAt) = (dist, p);
                    continue;
                }
                if (clear > best) (best, bestAt) = (clear, p);
            }
        if (region == 3 && best == float.NegativeInfinity) return float.PositiveInfinity;
        return best;
    }

    // The row's own "someone between" (copied: GatherPocketStressTests keeps it private).
    private static bool SomeoneBetween(World w, int i)
    {
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        int n = u.GatherNode[i].Index;
        ResourceDef def = w.Data.Resources[w.Resources.TypeId[n]];
        int cell = w.Resources.Cell[n];
        const float cs = MapConstants.CellSize;
        float x0 = cell % g.Width * cs, y0 = cell / g.Width * cs;
        Vector2 min = new(x0, y0), max = new(x0 + def.FootprintWidth * cs, y0 + def.FootprintHeight * cs);
        Vector2 p = u.Position[i], q = Vector2.Clamp(p, min, max);
        float mine = Vector2.Distance(p, q);
        Vector2 d = q - p;
        float len2 = d.LengthSquared();
        for (int j = 0; j < u.Capacity; j++)
        {
            if (j == i || !u.Alive[j]) continue;
            Vector2 pj = u.Position[j];
            float s = len2 > 0f ? Math.Clamp(Vector2.Dot(pj - p, d) / len2, 0f, 1f) : 0f;
            float rr = u.Radius[i] + u.Radius[j];
            if (Vector2.DistanceSquared(pj, p + d * s) > rr * rr) continue;
            if (Vector2.Distance(pj, Vector2.Clamp(pj, min, max)) < mine) return true;
        }
        int steps = (int)(mine / 0.25f);
        for (int k = 1; k < steps; k++)
        {
            Vector2 at = p + d * (k / (float)steps);
            if (!g.WorldToCell(at, out int cx, out int cy)) continue;
            bool own = at.X >= min.X && at.X <= max.X && at.Y >= min.Y && at.Y <= max.Y;
            if (!own && !g.IsPassable(cx, cy)) return true;
        }
        return false;
    }

    /// <summary>
    /// BUG-0790 (open, S3): seeds 2 and 8 of the row at the shipped rates, every tick a worker is wedged past the row's
    /// bound (40 ticks) printed with the face: the free spot with the most clearance in reach, the same restricted to the
    /// 4-adjacent ring cells the sim offers, and the free spot nearest the stuck worker. Expected once fixed: no tick past
    /// the bound while a ring cell in reach has a free spot. Seen at 2026-10-16-2036: seed 2, 28 / 28 such ticks (a 0.80 m
    /// clear ring spot at (24.05, 11.2), a corner one 0.51 m from the worker); seed 8, 44 / 44 (a 0.17 m clear ring spot at
    /// (28.0, 10.6), 1.3 m from the worker, all 84 ticks). Un-skipped with M5-H3's fix (BUG-0790: corner-cell stand points
    /// and a free point voiding a cell's crowd cost in <c>EconomySystem.WalkToFootprint</c>): no wedge past the bound.
    /// </summary>
    [Theory]
    [InlineData(2ul)]
    [InlineData(8ul)]
    public void MineOneSide40_NoWorkerWedgedPastTheBound_WhileARingSpotInReachIsFree(ulong seed)
    {
        var (worst, unit, tick, ticks) = Replay(null, seed, 0);
        _out.WriteLine($"seed {seed}: worst {worst} (unit {unit}, tick {tick})");
        foreach (WedgeTick t in ticks)
            if (t.Unit == unit && t.Tick <= tick && t.Tick > tick - worst && (t.Run == 1 || t.Run % 10 == 0 || t.Tick == tick))
                _out.WriteLine($"  t{t.Tick} run {t.Run} pos {t.Pos} {t.State} inReach {t.InReach} bestFree {t.BestFree:F3} ring {t.RingFree:F3} at {t.FreeAt} corner {t.CornerFree:F3} nearestFree {t.NearestFree:F2} m at {t.NearestAt}");
        int over = ticks.Count(t => t.Run > Bound);
        int ringFree = ticks.Count(t => t.Run > Bound && t.RingFree >= 0f);
        _out.WriteLine($"ticks past the bound (any unit): {over}; with a free spot in a 4-adjacent ring cell in reach: {ringFree}");
        Assert.True(ringFree == 0, $"seed {seed}: {ringFree} wedged ticks past the bound beside a free ring spot in reach (worst run {worst}, unit {unit}, tick {tick})");
    }

    /// <summary>
    /// The ruling's control, kept live while the shipped-rate row is skipped (BUG-0790): <c>(MineOneSide, 40)</c> at the old
    /// gather rates 0.7 / 0.6, seeds 1-8, the row's own wedge rule and bound. Green at 2026-10-16-2036 (worst 24 ticks).
    /// </summary>
    [Fact]
    public void Control_MineOneSide40_AtTheOldGatherRates_NoWedgePastTheBound()
    {
        GameData old = AiScenes.DataWith(dir => dir.EditJson("common/rules.json", r =>
        {
            r["gatherRate"]!["gold"] = 0.7;
            r["gatherRate"]!["wood"] = 0.6;
        }));
        var parts = new List<string>();
        int worstAll = 0;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var (worst, unit, tick, _) = Replay(old, seed, int.MaxValue);
            parts.Add($"seed {seed}: {worst} (unit {unit}, tick {tick})");
            worstAll = Math.Max(worstAll, worst);
        }
        _out.WriteLine("old rates 0.7 / 0.6, MineOneSide x40: " + string.Join("; ", parts));
        Assert.True(worstAll <= Bound, $"the control is red too: {worstAll}");
    }
}
