using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-3b): an independent transform oracle for <see cref="PropLayout"/> and the minimap resource layer over generated maps (seeds 1-20, 12 / 8 and the 64 / 64 caps, non-square maps) through a 600-tick Take churn; plateau heights; mine centres on cell corners; one relist per change; hash twin.</summary>
public class PropLayoutQaTests
{
    private const float Cs = MapConstants.CellSize;
    private static readonly uint[] Colors = { 0x3366CC, 0xE08020 };
    private readonly ITestOutputHelper _out;

    public PropLayoutQaTests(ITestOutputHelper output) => _out = output;

    private static Simulation Sim(ulong seed, int forests, int mines, int w = 128, int h = 128, int units = 64) =>
        new(TestSim.Config(seed, 2, units, 4096) with { Map = MapGenParams.Default with { Width = w, Height = h, Forests = forests, GoldMines = mines } });

    private static bool Refresh(PropLayout l, World w) =>
        l.Refresh(w.Heightmap, w.NavGrid, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell);

    private static bool Draw(MinimapRaster r, World w) =>
        r.DrawResources(w.Data.Resources, w.NavGrid.Version, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell);

    /// <summary>
    /// Independent oracle: rebuilds the expected per-type list (slot order) from the store and the raw heightmap
    /// only. Every footprint cell must share one elevation (the prop stands on flat ground), the origin is the
    /// footprint's geometric centre at that elevation, the basis is a pure yaw (orthonormal, up = +Y), a
    /// multi-cell footprint's yaw is a whole quarter turn (axis-aligned) and the rotated footprint covers the
    /// same cells. Returns the number of instances checked.
    /// </summary>
    internal static int AssertOracle(PropLayout l, World w, string what)
    {
        ResourceStore r = w.Resources;
        Heightmap map = w.Heightmap;
        int gw = map.Width, checkedCount = 0;
        Assert.Equal(w.Data.Resources.Length, l.TypeCount);
        for (int t = 0; t < l.TypeCount; t++)
        {
            ResourceDef def = w.Data.Resources[t];
            var slots = new List<int>();
            for (int s = 0; s < r.Capacity; s++) if (r.Alive[s] && r.TypeId[s] == t) slots.Add(s);
            Assert.True(slots.Count == l.CountOf(t), $"{what}: type {def.Key} has {l.CountOf(t)} instances, store has {slots.Count} alive");
            Assert.Equal(slots.ToArray(), l.SlotsOf(t).ToArray());
            ReadOnlySpan<float> b = l.TransformsOf(t);
            Assert.Equal(slots.Count * PropLayout.Stride, b.Length);
            for (int k = 0; k < slots.Count; k++)
            {
                int a = r.Cell[slots[k]], x0 = a % gw, y0 = a / gw;
                float elev = map.ElevationAt(x0, y0);
                for (int y = y0; y < y0 + def.FootprintHeight; y++)
                    for (int x = x0; x < x0 + def.FootprintWidth; x++)
                        Assert.True(map.ElevationAt(x, y) == elev && !map.IsRamp(x, y), $"{what}: node {slots[k]} footprint not flat at ({x}, {y})");
                double ex = (x0 + def.FootprintWidth * 0.5) * Cs, ez = (y0 + def.FootprintHeight * 0.5) * Cs;
                int o = k * PropLayout.Stride;
                Assert.True(Math.Abs(b[o + 3] - ex) <= 1e-3 && Math.Abs(b[o + 7] - elev) <= 1e-3 && Math.Abs(b[o + 11] - ez) <= 1e-3,
                    $"{what}: type {def.Key} slot {slots[k]} anchor ({x0}, {y0}): origin ({b[o + 3]}, {b[o + 7]}, {b[o + 11]}), want ({ex}, {elev}, {ez})");
                foreach (float f in b.Slice(o, PropLayout.Stride)) Assert.True(float.IsFinite(f), $"{what}: non-finite transform");
                // Rows (X, Y, Z) of a yaw: [c 0 s] [0 1 0] [-s 0 c].
                float c = b[o], si = b[o + 2];
                Assert.True(b[o + 1] == 0f && b[o + 4] == 0f && b[o + 5] == 1f && b[o + 6] == 0f && b[o + 9] == 0f, $"{what}: basis not a pure yaw");
                Assert.True(Math.Abs(b[o + 8] + si) < 1e-6 && Math.Abs(b[o + 10] - c) < 1e-6, $"{what}: basis not a rotation");
                Assert.True(Math.Abs(c * c + si * si - 1) < 1e-4, $"{what}: basis not unit length ({c}, {si})");
                if (def.FootprintWidth * def.FootprintHeight > 1)
                {
                    Assert.True((c is 0f or 1f or -1f) && (si is 0f or 1f or -1f), $"{what}: {def.Key} not axis-aligned ({c}, {si})");
                    // An oblong footprint may only half-turn, or its mesh pokes out of its cells.
                    if (def.FootprintWidth != def.FootprintHeight) Assert.True(si == 0f, $"{what}: oblong {def.Key} quarter-turned");
                }
                checkedCount++;
            }
        }
        return checkedCount;
    }

    /// <summary>Oracle for the minimap resource layer: footprint cells in the kind's colour, all else transparent.</summary>
    internal static void AssertMinimapLayer(MinimapRaster m, World w, string what)
    {
        ResourceStore r = w.Resources;
        var want = new uint[m.Width * m.Height];
        var set = new bool[want.Length];
        for (int s = 0; s < r.Capacity; s++)
        {
            if (!r.Alive[s]) continue;
            ResourceDef def = w.Data.Resources[r.TypeId[s]];
            uint rgb = def.Resource == ResourceKind.Gold ? 0xE6B422u : 0x1E5A1Eu; // the brief's colours, not the class's
            int x0 = r.Cell[s] % m.Width, y0 = r.Cell[s] / m.Width;
            for (int y = y0; y < y0 + def.FootprintHeight; y++)
                for (int x = x0; x < x0 + def.FootprintWidth; x++) { want[y * m.Width + x] = rgb; set[y * m.Width + x] = true; }
        }
        for (int p = 0; p < want.Length; p++)
        {
            int i = p * 4;
            if (set[p])
                Assert.True(m.Resources[i] == (byte)(want[p] >> 16) && m.Resources[i + 1] == (byte)(want[p] >> 8) && m.Resources[i + 2] == (byte)want[p] && m.Resources[i + 3] == 255,
                    $"{what}: minimap pixel ({p % m.Width}, {p / m.Width}) wrong");
            else Assert.True(m.Resources[i + 3] == 0, $"{what}: minimap pixel ({p % m.Width}, {p / m.Width}) opaque with no node");
        }
    }

    // Copies a type's current transforms keyed by slot, to prove survivors never move or turn across relists.
    private static Dictionary<int, float[]> Snapshot(PropLayout l)
    {
        var d = new Dictionary<int, float[]>();
        for (int t = 0; t < l.TypeCount; t++)
        {
            ReadOnlySpan<int> slots = l.SlotsOf(t);
            ReadOnlySpan<float> b = l.TransformsOf(t);
            for (int k = 0; k < slots.Length; k++) d[slots[k]] = b.Slice(k * PropLayout.Stride, PropLayout.Stride).ToArray();
        }
        return d;
    }

    private void Churn(ulong seed, int forests, int mines, int w = 128, int h = 128)
    {
        Simulation sim = Sim(seed, forests, mines, w, h);
        World world = sim.World;
        ResourceStore r = world.Resources;
        string what = $"seed {seed} {w}x{h} {forests}/{mines}";
        Assert.True(r.Count > 0, $"{what}: nothing placed ({world.ResourcePlacement})");
        var layout = new PropLayout(world.Data.Resources, r.Capacity);
        var mini = new MinimapRaster(world.Heightmap, world.NavGrid, Colors, 4);
        Assert.True(Refresh(layout, world));
        Assert.True(Draw(mini, world));
        AssertOracle(layout, world, what);
        AssertMinimapLayer(mini, world, what);
        Dictionary<int, float[]> first = Snapshot(layout);
        var rng = new SimRng(seed * 7919UL + (ulong)forests, 77);
        int lastVersion = world.NavGrid.Version, changes = 0, checks = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            // 0-3 takes per tick at random live slots: partial (no visible change), exact-to-0, overshoot,
            // a stale handle, non-positive amounts. Several fells in one tick must still be one relist.
            int takes = rng.NextInt(0, 4);
            for (int k = 0; k < takes && r.Count > 0; k++)
            {
                int slot = rng.NextInt(0, r.Capacity);
                while (!r.Alive[slot]) slot = (slot + 1) % r.Capacity;
                EntityHandle hnd = r.HandleOf(slot);
                switch (rng.NextInt(0, 5))
                {
                    case 0: r.Take(hnd, 1); break;
                    case 1: r.Take(hnd, r.Remaining[slot]); break;
                    case 2: r.Take(hnd, int.MaxValue); r.Take(hnd, 5); break; // second is on a stale handle
                    case 3: r.Take(hnd, 0); r.Take(hnd, -3); break;
                    default: break;
                }
            }
            sim.Tick();
            int rebuilds = layout.Rebuilds, draws = mini.ResourceDraws;
            bool changed = world.NavGrid.Version != lastVersion;
            for (int frame = 0; frame < 3; frame++) { Refresh(layout, world); Draw(mini, world); }
            Assert.True(layout.Rebuilds == rebuilds + (changed ? 1 : 0), $"{what} tick {tick}: changed {changed}, relists {layout.Rebuilds - rebuilds}");
            Assert.True(mini.ResourceDraws == draws + (changed ? 1 : 0), $"{what} tick {tick}: changed {changed}, minimap draws {mini.ResourceDraws - draws}");
            if (changed)
            {
                changes++;
                checks += AssertOracle(layout, world, $"{what} tick {tick}");
                AssertMinimapLayer(mini, world, $"{what} tick {tick}");
                lastVersion = world.NavGrid.Version;
                // Survivors keep the exact transform they had at the first fill (yaw is stable per anchor).
                foreach ((int slot, float[] tf) in Snapshot(layout))
                    Assert.True(first.TryGetValue(slot, out float[]? was) && was.AsSpan().SequenceEqual(tf), $"{what}: slot {slot} moved or turned across a relist");
            }
        }
        Assert.True(changes > 50, $"{what}: only {changes} passability changes in the churn");
        _out.WriteLine($"{what}: placed {world.ResourcePlacement}, {changes} changes, {checks} instances checked, {r.Count} left");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Oracle_ThroughA600TickTakeChurn_MatchesStoreAfterEveryChange_DefaultAndCaps(ulong seed)
    {
        Churn(seed, 12, 8);
        Churn(seed, MapGenParams.MaxResourceGroups, MapGenParams.MaxResourceGroups);
    }

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 20).Select(s => new object[] { (ulong)s });

    [Theory]
    [InlineData(3UL, 192, 96)]
    [InlineData(9UL, 96, 160)]
    [InlineData(11UL, 64, 64)]
    public void Oracle_NonSquareAndSmallMaps(ulong seed, int w, int h) => Churn(seed, 12, 8, w, h);

    [Fact]
    public void Level2Plateau_NodesStandAtLevel2Height_AndTerrainHeightAgrees()
    {
        int found = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            World w = Sim(seed, 64, 64).World;
            var l = new PropLayout(w.Data.Resources, w.Resources.Capacity);
            Refresh(l, w);
            for (int t = 0; t < l.TypeCount; t++)
            {
                ReadOnlySpan<int> slots = l.SlotsOf(t);
                ReadOnlySpan<float> b = l.TransformsOf(t);
                for (int k = 0; k < slots.Length; k++)
                {
                    int a = w.Resources.Cell[slots[k]];
                    int level = w.Heightmap.LevelAt(a % w.Heightmap.Width, a / w.Heightmap.Width);
                    float y = b[k * PropLayout.Stride + 7];
                    Assert.Equal(w.Heightmap.ElevationAt(a % w.Heightmap.Width, a / w.Heightmap.Width), y);
                    Assert.Equal(TerrainHeight.At(w.Heightmap, b[k * PropLayout.Stride + 3], b[k * PropLayout.Stride + 11]), y);
                    if (level == 2)
                    {
                        Assert.Equal(2 * MapConstants.LevelHeight, y, 3);
                        found++;
                    }
                }
            }
        }
        _out.WriteLine($"level-2 nodes checked: {found}");
        Assert.True(found > 0, "no node on a level-2 plateau in 20 seeds at 64 / 64");
    }

    [Fact]
    public void TwoByTwoMine_CentreIsACellCorner_NotACellCentre()
    {
        int mines = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            World w = Sim(seed, 12, 8).World;
            var l = new PropLayout(w.Data.Resources, w.Resources.Capacity);
            Refresh(l, w);
            int mine = w.Data.FindResource("gold_mine");
            Assert.Equal(2, w.Data.Resources[mine].FootprintWidth);
            Assert.Equal(2, w.Data.Resources[mine].FootprintHeight);
            ReadOnlySpan<float> b = l.TransformsOf(mine);
            ReadOnlySpan<int> slots = l.SlotsOf(mine);
            for (int k = 0; k < slots.Length; k++)
            {
                float gx = b[k * PropLayout.Stride + 3] / Cs, gz = b[k * PropLayout.Stride + 11] / Cs;
                Assert.True(gx == MathF.Round(gx) && gz == MathF.Round(gz), $"seed {seed}: mine centre ({gx}, {gz}) cells is not on a corner");
                int a = w.Resources.Cell[slots[k]];
                Assert.Equal((a % w.NavGrid.Width + 1, a / w.NavGrid.Width + 1), ((int)gx, (int)gz));
                mines++;
            }
        }
        Assert.True(mines >= 20 * 4, $"only {mines} mines over 20 seeds");
    }

    [Fact]
    public void FellEverything_LeavesEmptyLists_AndAFreshRespawnRelistsOnce()
    {
        World w = Sim(4, 12, 8).World;
        ResourceStore r = w.Resources;
        var l = new PropLayout(w.Data.Resources, r.Capacity);
        var m = new MinimapRaster(w.Heightmap, w.NavGrid, Colors, 4);
        Refresh(l, w);
        Draw(m, w);
        int tree = w.Data.FindResource("tree");
        int someCell = -1;
        for (int s = 0; s < r.Capacity; s++)
        {
            if (!r.Alive[s]) continue;
            if (r.TypeId[s] == tree) someCell = r.Cell[s];
            r.Take(r.HandleOf(s), int.MaxValue);
        }
        Assert.Equal(0, r.Count);
        Assert.True(Refresh(l, w));
        Assert.True(Draw(m, w));
        for (int t = 0; t < l.TypeCount; t++) { Assert.Equal(0, l.CountOf(t)); Assert.True(l.TransformsOf(t).IsEmpty); }
        Assert.DoesNotContain(m.Resources.Where((_, i) => i % 4 == 3), a => a != 0);
        // A node spawned again into a freed (higher-generation) slot is drawn again on the next change.
        Assert.True(r.Spawn(tree, someCell, 5, out _));
        Assert.True(Refresh(l, w));
        Assert.False(Refresh(l, w));
        Assert.Equal(1, AssertOracle(l, w, "respawn"));
        Assert.True(Draw(m, w));
        AssertMinimapLayer(m, w, "respawn");
    }

    [Fact]
    public void MinimapTerrain_UnderResources_EqualsTheBareMap_Seeds1To20_AtTheCaps()
    {
        // Resource-only blocked cells keep the ground colour; anything else (e.g. a pocket the forests seal)
        // would show as a terrain difference against the bare map of the same seed.
        var diffs = new List<string>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            World w = Sim(seed, 64, 64).World, bare = Sim(seed, 0, 0).World;
            Assert.Equal(bare.Heightmap.ContentHash(), w.Heightmap.ContentHash());
            byte[] a = new MinimapRaster(w.Heightmap, w.NavGrid, Colors, 1).Terrain, b = new MinimapRaster(bare.Heightmap, bare.NavGrid, Colors, 1).Terrain;
            int n = 0;
            for (int i = 0; i < a.Length; i += 4) if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2]) n++;
            if (n > 0) diffs.Add($"seed {seed}: {n} terrain pixels differ");
        }
        Assert.True(diffs.Count == 0, string.Join("; ", diffs));
    }

    [Fact]
    public void StartBlocks_OnTheMatchMap_NeverOnAResourceCell_ReportsHowManyTouchAForest()
    {
        // The debug start block (StartLayout) was written before forests were on in the Match: every spot must
        // still be an open cell; how many spots sit next to a node is reported (an army spawned inside a forest).
        float maxR = TestSim.Data.Units.Max(u => u.Radius);
        var report = new List<string>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            World w = Sim(seed, 12, 8).World;
            NavGrid g = w.NavGrid;
            for (int p = 0; p < 2; p++)
            {
                Vector2[] spots = StartLayout.Block(g, 100, p == 0, maxR);
                int touching = 0;
                foreach (Vector2 s in spots)
                {
                    int cx = (int)(s.X / Cs), cy = (int)(s.Y / Cs);
                    Assert.True((g.FlagsAt(cx, cy) & (NavFlags.Blocked | NavFlags.Resource)) == 0, $"seed {seed} p{p}: spot ({cx}, {cy}) on a blocked cell");
                    bool near = false;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if ((g.FlagsAt(cx + dx, cy + dy) & NavFlags.Resource) != 0) near = true;
                    if (near) touching++;
                }
                if (touching > 0) report.Add($"seed {seed} p{p}: {touching}/{spots.Length}");
            }
        }
        _out.WriteLine("start spots next to a resource node: " + (report.Count == 0 ? "none" : string.Join(", ", report)));
    }

    [Fact]
    public void HashTwin_PropsAndMinimapEveryFrame_2000Units_AtTheCaps_WithFells()
    {
        Simulation Make()
        {
            Simulation s = Sim(13, 64, 64, units: 2000);
            float maxR = TestSim.Data.Units.Max(u => u.Radius);
            for (int p = 0; p < 2; p++)
            {
                Vector2[] spots = StartLayout.Block(s.World.NavGrid, 1000, p == 0, maxR);
                for (int k = 0; k < spots.Length; k++) s.Enqueue(Command.SpawnUnit(p, k % TestSim.UnitTypeCount, spots[k]));
            }
            return s;
        }
        Simulation a = Make(), b = Make();
        World w = a.World;
        var l = new PropLayout(w.Data.Resources, w.Resources.Capacity);
        var m = new MinimapRaster(w.Heightmap, w.NavGrid, Colors, w.Units.Capacity);
        var rng = new SimRng(99, 77);
        for (int tick = 0; tick < 400; tick++)
        {
            if (tick % 100 == 3)
            {
                var goal = new Vector2(rng.NextInt(0, 200) + 28f, rng.NextInt(0, 200) + 28f);
                for (int i = 0; i < w.Units.Capacity; i++)
                {
                    if (!w.Units.Alive[i]) continue;
                    Command c = Command.Move(w.Units.Owner[i], new EntityHandle(i, w.Units.Generation[i]), goal);
                    a.Enqueue(c);
                    b.Enqueue(c);
                }
            }
            if (tick % 7 == 0)
            {
                // The same fell in both twins (slot picked from a, applied by slot to both).
                int slot = rng.NextInt(0, w.Resources.Capacity);
                if (a.World.Resources.Alive[slot])
                {
                    a.World.Resources.Take(a.World.Resources.HandleOf(slot), int.MaxValue);
                    b.World.Resources.Take(b.World.Resources.HandleOf(slot), int.MaxValue);
                }
            }
            ulong before = a.StateHash();
            Refresh(l, w);
            Draw(m, w);
            m.DrawDots(w.Units.Alive, w.Units.Position, w.Units.Owner);
            Assert.Equal(before, a.StateHash());
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"tick {tick}: view-read sim diverged");
        }
        Assert.Equal(2000, w.Units.Count);
    }
}
