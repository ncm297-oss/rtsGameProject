using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-H2): the bench's across target on 200 match seeds (blocked far points, reachability, both sides), start blocks on seeds 1-40 at 100 and 1,000 per player against an independent clearing oracle, minimap 2 x 2 dots at 100 / 990 units against a property oracle, PropLayout.Changed under random fells, and a hash twin for the changed ViewApi reads.</summary>
/// <remarks>
/// Every oracle here is written from docs/03 (M2-H2 paragraphs) and the M2-H2 brief, not from the code:
/// open = passable, not a ramp, no Blocked / Resource cell (off-map counts as Blocked) among the 8 neighbours;
/// a block = one 4-connected clearing of open cells on one level; a dot = the 2 x 2 cells around the cell
/// corner nearest the unit in the owner colour inside a one-cell rim, the unit's own cell drawn last.
/// </remarks>
public class ViewH2QaTests
{
    private const float Cs = MapConstants.CellSize;
    private readonly ITestOutputHelper _out;

    public ViewH2QaTests(ITestOutputHelper output) => _out = output;

    private static float FactionMaxRadius(GameData data, int p)
    {
        float r = 0f;
        foreach (int t in data.Factions[p % data.Factions.Length].Units) r = Math.Max(r, data.Units[t].Radius);
        return r;
    }

    private static Vector2 Centre(Vector2[] spots)
    {
        var s = Vector2.Zero;
        foreach (Vector2 v in spots) s += v;
        return s / spots.Length;
    }

    // ---------------- BenchTarget ----------------

    [Fact]
    public void BenchTarget_200MatchSeeds_BothSides_TargetIsPassableReachableOppositeAndFar()
    {
        int blockedFar = 0, cliffFar = 0, resourceFar = 0, otherFar = 0, fallbacks = 0, under100 = 0, unreachable = 0;
        float closest = float.MaxValue;
        var notes = new List<string>();
        for (ulong seed = 1; seed <= 200; seed++)
        {
            Simulation sim = PropLayoutTests.MatchSim(seed);
            NavGrid g = sim.World.NavGrid;
            ulong hash = sim.StateHash();
            float w = g.Width * Cs, h = g.Height * Cs;
            for (int p = 0; p < 2; p++)
            {
                bool west = p == 0;
                Vector2[] block = StartLayout.Block(g, 100, west, FactionMaxRadius(sim.World.Data, p));
                Assert.Equal(100, block.Length);
                Vector2 army = Centre(block);
                var far = new Vector2(west ? 0.85f * w : 0.15f * w, 0.5f * h);
                int fx = (int)(far.X / Cs), fy = (int)(far.Y / Cs);
                NavFlags ff = g.FlagsAt(fx, fy);
                if ((ff & NavFlags.Blocked) != 0)
                {
                    blockedFar++;
                    if ((ff & NavFlags.Cliff) != 0) cliffFar++;
                    else if ((ff & NavFlags.Resource) != 0) resourceFar++;
                    else otherFar++;
                    if (notes.Count < 12) notes.Add($"seed {seed} {(west ? "W" : "E")}: far cell ({fx},{fy}) {ff}");
                }
                // Independent fallback decision: any passable cell within 8 cells (Chebyshev) of the far cell?
                bool near = false;
                for (int y = fy - 8; y <= fy + 8 && !near; y++)
                    for (int x = fx - 8; x <= fx + 8 && !near; x++)
                        if (g.IsPassable(x, y)) near = true;
                if (!near) fallbacks++;

                Assert.True(BenchTarget.TryAcross(g, army, out Vector2 to), $"seed {seed}: no target");
                Assert.True(g.WorldToCell(to, out int tx, out int ty) && g.IsPassable(tx, ty), $"seed {seed}: {to} not passable");
                Assert.True(west ? to.X > w / 2 : to.X < w / 2, $"seed {seed} {(west ? "W" : "E")}: target {to} on the army's own side");
                float d = Vector2.Distance(army, to);
                closest = MathF.Min(closest, d);
                if (d < 100f) { under100++; notes.Add($"seed {seed} {(west ? "W" : "E")}: target only {d:F1} m"); }
                // Reachable: a flow field to the target gives the army's cells a finite cost.
                FlowField f = FlowField.Build(g, ty * g.Width + tx);
                int bad = 0;
                foreach (Vector2 s in block)
                {
                    g.WorldToCell(s, out int sx, out int sy);
                    if (!float.IsFinite(f.CostAt(sy * g.Width + sx)) || f.CostAt(sy * g.Width + sx) >= float.MaxValue / 2) bad++;
                }
                if (f.TargetCell != ty * g.Width + tx || bad > 0)
                {
                    unreachable++;
                    notes.Add($"seed {seed} {(west ? "W" : "E")}: target ({tx},{ty}) field target {f.TargetCell}, {bad} army cells with no path");
                }
            }
            Assert.Equal(hash, sim.StateHash());
        }
        _out.WriteLine($"far point blocked {blockedFar}/400 (cliff {cliffFar}, resource {resourceFar}, other {otherFar}); corner fallbacks {fallbacks}; under 100 m {under100}; closest {closest:F1} m; unreachable {unreachable}");
        foreach (string n in notes) _out.WriteLine(n);
        Assert.True(blockedFar > 0, "no seed in 1-200 puts the far point on a blocked cell: widen the sweep");
        Assert.Equal(0, unreachable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BenchTarget_CornerFallback_EastAndWestArmies_HandMap(bool armyWest)
    {
        const int n = 40;
        var g = new NavGrid(new Heightmap(n, n, new byte[n * n], new float[n * n]));
        float w = n * Cs;
        // Wall off 17 x 17 cells round both far points (0.85 W and 0.15 W at mid height).
        int west = (int)(0.15f * w / Cs), east = (int)(0.85f * w / Cs);
        g.SetBuilding(Math.Max(1, west - 8), n / 2 - 8, 17, 17);
        g.SetBuilding(Math.Min(n - 18, east - 8), n / 2 - 8, 17, 17);
        foreach (bool north in new[] { true, false })
        {
            var army = new Vector2(armyWest ? 0.3f * w : 0.7f * w, north ? 0.2f * w : 0.8f * w);
            Assert.True(BenchTarget.TryAcross(g, army, out Vector2 to));
            g.WorldToCell(to, out int x, out int y);
            // The corner opposite the army, inside the 1-cell border ring.
            Assert.Equal(armyWest ? n - 2 : 1, x);
            Assert.Equal(north ? n - 2 : 1, y);
        }
    }

    [Fact]
    public void BenchTarget_ArmyExactlyOnTheCentreLine_AndNonFiniteCentre_DoNotThrow()
    {
        Simulation sim = PropLayoutTests.MatchSim(1);
        NavGrid g = sim.World.NavGrid;
        float w = g.Width * Cs, h = g.Height * Cs;
        Assert.True(BenchTarget.TryAcross(g, new Vector2(w / 2, h / 2), out Vector2 to));
        Assert.True(to.X > w / 2, $"an army on the centre line counts as west, so goes east: {to}");
        foreach (Vector2 odd in new[] { new Vector2(float.NaN, 5f), new Vector2(float.PositiveInfinity, float.NegativeInfinity), new Vector2(-1e30f, 1e30f) })
        {
            bool ok = BenchTarget.TryAcross(g, odd, out Vector2 t);
            if (ok) Assert.True(g.WorldToCell(t, out int x, out int y) && g.IsPassable(x, y), $"{odd} -> {t}");
        }
    }

    // ---------------- StartLayout ----------------

    private static bool OpenOracle(NavGrid g, int x, int y)
    {
        if (!g.InBounds(x, y) || (g.FlagsAt(x, y) & NavFlags.Blocked) != 0 || (g.FlagsAt(x, y) & NavFlags.Ramp) != 0) return false;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (!g.InBounds(nx, ny)) return false;
                if ((g.FlagsAt(nx, ny) & (NavFlags.Blocked | NavFlags.Resource | NavFlags.Cliff)) != 0) return false;
            }
        return true;
    }

    // The 4-connected open, same-level component of (x, y), by BFS.
    private static HashSet<int> Clearing(NavGrid g, int x, int y)
    {
        var seen = new HashSet<int> { y * g.Width + x };
        var q = new Queue<int>();
        q.Enqueue(y * g.Width + x);
        int level = g.LevelAt(x, y);
        while (q.Count > 0)
        {
            int c = q.Dequeue(), cx = c % g.Width, cy = c / g.Width;
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = cx + dx, ny = cy + dy;
                if (!g.InBounds(nx, ny) || g.LevelAt(nx, ny) != level || !OpenOracle(g, nx, ny)) continue;
                if (seen.Add(ny * g.Width + nx)) q.Enqueue(ny * g.Width + nx);
            }
        }
        return seen;
    }

    public static IEnumerable<object[]> Seeds1To40()
    {
        for (ulong s = 1; s <= 40; s++) yield return new object[] { s };
    }

    [Theory]
    [MemberData(nameof(Seeds1To40))]
    public void StartBlocks_Seeds1To40_100And1000PerSide_FullOpenOneLevelOneClearingSpacedAndDeterministic(ulong seed)
    {
        Simulation sim = PropLayoutTests.MatchSim(seed);
        NavGrid g = sim.World.NavGrid;
        ulong hash = sim.StateHash();
        foreach (int count in new[] { 100, 1000 })
        {
            var used = new HashSet<int>();
            for (int p = 0; p < 2; p++)
            {
                float r = FactionMaxRadius(sim.World.Data, p);
                Vector2[] spots = StartLayout.Block(g, count, p == 0, r);
                Assert.Equal(count, spots.Length);
                Assert.Equal(spots, StartLayout.Block(g, count, p == 0, r));
                Assert.True(g.WorldToCell(spots[0], out int x0, out int y0));
                HashSet<int> clearing = Clearing(g, x0, y0);
                int stride = Math.Max(1, (int)MathF.Ceiling(2f * r / Cs - 1e-4f));
                foreach (Vector2 s in spots)
                {
                    Assert.True(g.WorldToCell(s, out int x, out int y), $"seed {seed} p{p} n{count}: {s} off map");
                    Assert.Equal(g.CellCenter(x, y), s);
                    Assert.True(OpenOracle(g, x, y), $"seed {seed} p{p} n{count}: ({x},{y}) not open, flags {g.FlagsAt(x, y)}");
                    Assert.True(clearing.Contains(y * g.Width + x), $"seed {seed} p{p} n{count}: ({x},{y}) outside the first spot's clearing");
                    Assert.True(p == 0 ? s.X < g.Width * Cs / 2 : s.X > g.Width * Cs / 2, $"seed {seed} p{p}: ({x},{y}) on the wrong half");
                    Assert.True(used.Add(y * g.Width + x), $"seed {seed} n{count}: cell ({x},{y}) used twice");
                    // Lattice spacing: no two spots of a block closer than 2r.
                    for (int dy = -stride + 1; dy < stride; dy++)
                        for (int dx = -stride + 1; dx < stride; dx++)
                            if ((dx | dy) != 0 && used.Contains((y + dy) * g.Width + x + dx) && spots.Contains(g.CellCenter(x + dx, y + dy)))
                                Assert.Fail($"seed {seed} p{p}: spots ({x},{y}) and ({x + dx},{y + dy}) under 2r apart");
                }
            }
        }
        Assert.Equal(hash, sim.StateHash());
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(5UL)]
    [InlineData(42UL)]
    public void StartBlocks_CliMarchMap_NoResources_StillFill1000(ulong seed)
    {
        // The CLI march (tools/Rts.Cli/March.cs) uses the same blocks on a map without forests and mines.
        Simulation sim = PropLayoutTests.MatchSim(seed, forests: 0, mines: 0);
        for (int p = 0; p < 2; p++)
            Assert.Equal(1000, StartLayout.Block(sim.World.NavGrid, 1000, p == 0, FactionMaxRadius(sim.World.Data, p)).Length);
    }

    [Fact]
    public void StartBlocks_DegenerateCounts_AndAnOversizedRequest_StayInOneClearing()
    {
        Simulation sim = PropLayoutTests.MatchSim(3);
        NavGrid g = sim.World.NavGrid;
        Assert.Empty(StartLayout.Block(g, 0, true, 0.5f));
        Assert.Empty(StartLayout.Block(g, -5, false, 0.5f));
        Assert.Single(StartLayout.Block(g, 1, true, 0.5f));
        // More than a half can hold: the roomiest clearing, every spot still open and connected.
        Vector2[] big = StartLayout.Block(g, 20000, true, 0.5f);
        Assert.True(big.Length > 1000 && big.Length < 20000, $"{big.Length}");
        g.WorldToCell(big[0], out int x0, out int y0);
        HashSet<int> clearing = Clearing(g, x0, y0);
        foreach (Vector2 s in big)
        {
            g.WorldToCell(s, out int x, out int y);
            Assert.True(OpenOracle(g, x, y) && clearing.Contains(y * g.Width + x));
        }
        _out.WriteLine($"20,000 asked on seed 3 west: {big.Length} fit (clearing {clearing.Count} cells)");
    }

    [Fact]
    public void StartBlocks_TinyMapWithNoOpenCell_ReturnEmpty_NoThrow()
    {
        // 4 x 4: the border ring is blocked, so every inner cell touches it.
        var g = new NavGrid(new Heightmap(4, 4, new byte[16], new float[16]));
        Assert.Empty(StartLayout.Block(g, 10, true, 0.5f));
        Assert.Empty(StartLayout.Block(g, 10, false, 0.5f));
    }

    // ---------------- Minimap 2 x 2 dots ----------------

    private static readonly uint[] Owners = { 0x4B4F55, 0xC8892E, 0x2050D0 };

    private static uint Rgb(MinimapRaster r, int x, int y, out byte a)
    {
        int i = (y * r.Width + x) * 4;
        a = r.Dots[i + 3];
        return (uint)(r.Dots[i] << 16 | r.Dots[i + 1] << 8 | r.Dots[i + 2]);
    }

    [Theory]
    [InlineData(1UL, 100)]
    [InlineData(2UL, 990)]
    [InlineData(3UL, 990)]
    public void Dots_RandomCrowdsWithEdgeRampAndCliffLipUnits_MatchThePropertyOracle(ulong seed, int units)
    {
        Simulation sim = PropLayoutTests.MatchSim(seed);
        Heightmap map = sim.World.Heightmap;
        NavGrid g = sim.World.NavGrid;
        var raster = new MinimapRaster(map, g, Owners, 2000);
        var rng = new Random((int)seed * 31 + units);
        var ramps = new List<int>();
        var lips = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            NavFlags f = g.FlagsAt(c % g.Width, c / g.Width);
            if ((f & NavFlags.Ramp) != 0) ramps.Add(c);
            if ((f & NavFlags.Cliff) != 0) lips.Add(c);
        }
        Assert.NotEmpty(ramps);
        Assert.NotEmpty(lips);
        for (int round = 0; round < 6; round++)
        {
            int n = round % 2 == 0 ? units : units / 3; // shrinking crowds test the clear
            var alive = new bool[n];
            var pos = new Vector2[n];
            var own = new int[n];
            for (int s = 0; s < n; s++)
            {
                alive[s] = rng.Next(10) != 0;
                own[s] = rng.Next(Owners.Length);
                float fx = (float)rng.NextDouble(), fy = (float)rng.NextDouble();
                switch (rng.Next(6))
                {
                    case 0: pos[s] = new Vector2(fx * 2 * Cs, fy * g.Height * Cs); break;                     // west border strip
                    case 1: pos[s] = new Vector2(g.Width * Cs - fx * 2 * Cs, g.Height * Cs - fy * 2 * Cs); break; // far corner
                    case 2: { int c = ramps[rng.Next(ramps.Count)]; pos[s] = new Vector2((c % g.Width + fx) * Cs, (c / g.Width + fy) * Cs); break; }
                    case 3: { int c = lips[rng.Next(lips.Count)]; pos[s] = new Vector2((c % g.Width + fx) * Cs, (c / g.Width + fy) * Cs); break; }
                    default: pos[s] = new Vector2(fx * g.Width * Cs, fy * g.Height * Cs); break;
                }
            }
            int drawn = raster.DrawDots(alive, pos, own);
            Assert.Equal(alive.Count(a => a), drawn);

            int w = raster.Width, h = raster.Height;
            var block = new int[w * h];   // how many dots' 4 x 4 blocks cover a pixel
            var ownLast = new int[w * h]; // last slot whose own cell is the pixel
            Array.Fill(ownLast, -1);
            var cells = new (int x, int y, int kx, int ky)[n];
            for (int s = 0; s < n; s++)
            {
                if (!alive[s]) continue;
                int x = Math.Clamp((int)MathF.Floor(pos[s].X / Cs), 0, w - 1), y = Math.Clamp((int)MathF.Floor(pos[s].Y / Cs), 0, h - 1);
                int kx = Math.Clamp((int)MathF.Floor(pos[s].X / Cs + 0.5f), 1, w - 1), ky = Math.Clamp((int)MathF.Floor(pos[s].Y / Cs + 0.5f), 1, h - 1);
                Assert.True(x >= kx - 1 && x <= kx && y >= ky - 1 && y <= ky, $"own cell ({x},{y}) outside its centre at corner ({kx},{ky})");
                cells[s] = (x, y, kx, ky);
                for (int yy = ky - 2; yy <= ky + 1; yy++)
                    for (int xx = kx - 2; xx <= kx + 1; xx++)
                        if (xx >= 0 && yy >= 0 && xx < w && yy < h) block[yy * w + xx]++;
                ownLast[y * w + x] = s;
            }
            for (int i = 0; i < w * h; i++)
            {
                Rgb(raster, i % w, i / w, out byte a);
                Assert.True((block[i] > 0) == (a == 255), $"round {round}: pixel ({i % w},{i / w}) alpha {a}, covered by {block[i]} blocks");
                if (block[i] == 0) Assert.Equal(0, a);
                if (ownLast[i] >= 0) Assert.Equal(Owners[own[ownLast[i]]], Rgb(raster, i % w, i / w, out _));
            }
            // Lone dots (their 4 x 4 block overlaps no other): exact picture.
            int lone = 0;
            for (int s = 0; s < n; s++)
            {
                if (!alive[s]) continue;
                (int _, int _, int kx, int ky) = cells[s];
                bool alone = true;
                for (int yy = ky - 2; yy <= ky + 1 && alone; yy++)
                    for (int xx = kx - 2; xx <= kx + 1 && alone; xx++)
                        if (xx >= 0 && yy >= 0 && xx < w && yy < h && block[yy * w + xx] > 1) alone = false;
                if (!alone) continue;
                lone++;
                for (int yy = ky - 2; yy <= ky + 1; yy++)
                    for (int xx = kx - 2; xx <= kx + 1; xx++)
                    {
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                        bool centre = xx >= kx - 1 && xx <= kx && yy >= ky - 1 && yy <= ky;
                        uint want = centre ? Owners[own[s]] : MinimapRaster.RimFor(Owners[own[s]]);
                        Assert.Equal(want, Rgb(raster, xx, yy, out _));
                    }
            }
            if (round == 0) _out.WriteLine($"seed {seed}, {units} units: {drawn} drawn, {lone} lone dots checked exactly");
        }
    }

    [Fact]
    public void Dots_AllFourMapCorners_TheCentreClampsOntoTheMap_AndHoldsTheUnitsCell()
    {
        Simulation sim = PropLayoutTests.MatchSim(1);
        var raster = new MinimapRaster(sim.World.Heightmap, sim.World.NavGrid, Owners, 4);
        int w = raster.Width, h = raster.Height;
        float W = w * Cs, H = h * Cs;
        foreach (Vector2 p in new[] { new Vector2(0f, 0f), new Vector2(W - 1e-3f, 0f), new Vector2(0f, H - 1e-3f), new Vector2(W - 1e-3f, H - 1e-3f), new Vector2(-50f, H + 50f) })
        {
            raster.DrawDots(new[] { true }, new[] { p }, new[] { 1 });
            Assert.True(raster.TryPixelOf(p, out int x, out int y));
            Assert.Equal(Owners[1], Rgb(raster, x, y, out byte a));
            Assert.Equal(255, a);
            int c = raster.CentreOf(p), cx = c % w, cy = c / w;
            int owner = 0;
            for (int yy = cy; yy <= cy + 1; yy++)
                for (int xx = cx; xx <= cx + 1; xx++)
                    if (Rgb(raster, xx, yy, out _) == Owners[1]) owner++;
            Assert.Equal(4, owner); // a corner dot keeps its full 2 x 2 owner centre on the map
        }
    }

    // ---------------- PropLayout.Changed ----------------

    [Theory]
    [InlineData(1UL)]
    [InlineData(9UL)]
    public void PropLayoutChanged_RandomFells_IsTrueExactlyWhenTheTypesTransformsChanged(ulong seed)
    {
        Simulation sim = PropLayoutTests.MatchSim(seed);
        World w = sim.World;
        ResourceStore r = w.Resources;
        PropLayout l = PropLayoutTests.Layout(w);
        Assert.True(PropLayoutTests.Refresh(l, w));
        var rng = new Random((int)seed);
        float[][] last = new float[l.TypeCount][];
        for (int t = 0; t < l.TypeCount; t++) last[t] = l.TransformsOf(t).ToArray();
        int relists = 0, changedSeen = 0;
        for (int step = 0; step < 150; step++)
        {
            int kind = rng.Next(5);
            if (kind == 0) w.NavGrid.BumpVersionForTests();
            else
            {
                // Fell 1-3 nodes of one type (or both), partial takes too.
                int fells = rng.Next(1, 4);
                for (int k = 0; k < fells; k++)
                {
                    int t = rng.Next(l.TypeCount);
                    if (l.CountOf(t) == 0) continue;
                    int slot = l.SlotsOf(t)[rng.Next(l.CountOf(t))];
                    r.Take(r.HandleOf(slot), rng.Next(3) == 0 ? 1 : int.MaxValue);
                }
            }
            if (!PropLayoutTests.Refresh(l, w)) continue;
            relists++;
            PropLayoutTests.AssertMatchesStore(l, w);
            for (int t = 0; t < l.TypeCount; t++)
            {
                float[] now = l.TransformsOf(t).ToArray();
                bool differs = !now.AsSpan().SequenceEqual(last[t]);
                Assert.True(differs == l.Changed(t), $"step {step} type {t}: transforms {(differs ? "changed" : "same")} but Changed = {l.Changed(t)}");
                if (differs) changedSeen++;
                last[t] = now;
            }
        }
        _out.WriteLine($"seed {seed}: {relists} relists, {changedSeen} type changes");
        Assert.True(relists > 50 && changedSeen > 20);
    }

    // ---------------- Read-only proof ----------------

    [Fact]
    public void ChangedViewApiReads_EveryTick_LeaveTheHashEqualToABareTwin()
    {
        var cfg = TestSim.Config(4, 2, 2000, 4096) with { Map = MapGenParams.Default with { Forests = 12, GoldMines = 8 } };
        var a = new Simulation(cfg);
        var b = new Simulation(cfg);
        World w = a.World;
        for (int p = 0; p < 2; p++)
        {
            FactionDef f = w.Data.Factions[p];
            Vector2[] spots = StartLayout.Block(w.NavGrid, 300, p == 0, FactionMaxRadius(w.Data, p));
            for (int k = 0; k < spots.Length; k++)
            {
                Command c = Command.SpawnUnit(p, f.Units[k % f.Units.Length], spots[k]);
                a.Enqueue(c);
                b.Enqueue(c);
            }
        }
        var raster = new MinimapRaster(w.Heightmap, w.NavGrid, Owners, w.Units.Capacity);
        var props = PropLayoutTests.Layout(w);
        UnitStore u = w.Units;
        for (int tick = 0; tick < 400; tick++)
        {
            if (tick == 3)
            {
                // Order player 0's army across, the way the bench does.
                var sum = Vector2.Zero;
                int n = 0;
                for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == 0) { sum += u.Position[i]; n++; }
                Assert.True(BenchTarget.TryAcross(w.NavGrid, sum / n, out Vector2 to));
                for (int i = 0; i < u.Capacity; i++)
                    if (u.Alive[i] && u.Owner[i] == 0)
                    {
                        Command c = Command.Move(0, new EntityHandle(i, u.Generation[i]), to);
                        a.Enqueue(c);
                        b.Enqueue(c);
                    }
            }
            ulong before = a.StateHash();
            raster.DrawDots(u.Alive, u.Position, u.Owner);
            PropLayoutTests.Refresh(props, w);
            BenchTarget.TryAcross(w.NavGrid, new Vector2(tick, 128f), out _);
            _ = FlowArrowLayout.ArrowGround(w.Heightmap, new Vector2(tick % 250 + 1, 100f), FlowArrowLayout.DirectionVector(tick % 8));
            if (tick % 50 == 0) _ = StartLayout.Block(w.NavGrid, 50, tick % 100 == 0, 0.5f);
            Assert.Equal(before, a.StateHash());
            a.Tick();
            b.Tick();
            Assert.Equal(b.StateHash(), a.StateHash());
        }
    }
}
