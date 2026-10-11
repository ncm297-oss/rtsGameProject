using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA attacks on M4-VH3 (session 2026-10-10-2334). <see cref="SeenResources"/> (BUG-0400 item 3) against an independent
/// "only what was seen" rule while trees fall and freed slots are reused elsewhere (in and out of sight), and its hash
/// twin with the reuse; <see cref="GroundPatch"/> bounds: <see cref="GroundPatch.MaxCells"/> always fits what
/// <see cref="GroundPatch.Collect"/> wants (centres on exact cell edges and random), a disc centred exactly on a cliff lip
/// lists cells of both levels, and the inner-over-outer and huge-radius edges.
/// </summary>
[Collection(SerialCollection.Name)]
public class ViewHardening3QaTests
{
    private const int W = 64;
    private const float Cs = MapConstants.CellSize;
    private readonly ITestOutputHelper _out;

    public ViewHardening3QaTests(ITestOutputHelper output) => _out = output;

    private static (Simulation Sim, EntityHandle Scout, List<EntityHandle> Trees) Forest(ulong seed)
    {
        Simulation sim = TestSim.Explored(ResourceMaps.NewSim(ResourceMaps.Flat(W, W), units: 8, seed: seed));
        World w = sim.World;
        var trees = new List<EntityHandle>();
        var rng = new Random((int)seed); // test data only
        for (int k = 0; k < 120; k++)
        {
            int x = rng.Next(2, 61), y = rng.Next(2, 61);
            if (w.Resources.Spawn(ResourceMaps.Tree, y * W + x, 50, out EntityHandle h)) trees.Add(h);
        }
        EntityHandle scout = Place(sim, 0, Laborer, At(sim, 8, 8));
        sim.World.Fog.Update();
        return (sim, scout, trees);
    }

    private static bool Sees(World w, int type, int anchor)
    {
        var def = w.Data.Resources[type];
        int ax = anchor % W, ay = anchor / W;
        for (int y = ay; y < ay + def.FootprintHeight; y++)
            for (int x = ax; x < ax + def.FootprintWidth; x++)
                if (w.Fog.IsVisible(0, y * W + x)) return true;
        return false;
    }

    // Fells one random live tree and (every other call) respawns a tree in a free slot at a random cell: the store reuses
    // the lowest free slot, so a slot felled earlier comes back elsewhere. Returns whether a reuse happened.
    private static bool Churn(World w, List<EntityHandle> trees, Random rng, bool respawn)
    {
        EntityHandle h = trees[rng.Next(trees.Count)];
        if (w.Resources.IsAlive(h)) w.Resources.Take(h, 1000);
        if (!respawn) return false;
        for (int tries = 0; tries < 20; tries++)
        {
            int x = rng.Next(2, 61), y = rng.Next(2, 61);
            if (w.Resources.Spawn(ResourceMaps.Tree, y * W + x, 50, out EntityHandle again))
            {
                trees.Add(again);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// BUG-0400 item 3 under churn: trees fall and freed slots are reused at random cells while a scout walks. After every
    /// update: (1) every node the copy shows alive (slot, type, cell) was the store's node in that slot at an update when
    /// its footprint was visible (or at the first fill), so the copy never shows an unseen node; (2) a slot changes in the
    /// copy only when the remembered or the live footprint is visible; (3) a slot whose live node is in sight matches the
    /// store; (4) a remembered node whose footprint is in sight is not stale.
    /// </summary>
    [Theory]
    [InlineData(3UL)]
    [InlineData(17UL)]
    [InlineData(91UL)]
    [InlineData(2026UL)]
    public void ReusedSlots_TheCopyShowsOnlySeenNodes_AndChangesOnlyInSight(ulong seed)
    {
        (Simulation sim, EntityHandle scout, List<EntityHandle> trees) = Forest(seed);
        World w = sim.World;
        var seen = new SeenResources(w.Data.Resources, w.Resources.Capacity);
        int n = w.Resources.Capacity;
        var everSeen = new HashSet<(int Slot, int Type, int Cell)>();
        var rng = new Random((int)seed + 1);
        var prevAlive = new bool[n];
        var prevType = new int[n];
        var prevCell = new int[n];
        int reuses = 0, removalsOnly = 0, staleSlotTicks = 0;
        for (int t = 0; t < 2500; t++)
        {
            if (t % 40 == 0) sim.Enqueue(Command.Move(0, scout, At(sim, rng.Next(3, 60), rng.Next(3, 60))));
            if (t % 5 == 2 && Churn(w, trees, rng, respawn: t % 10 == 2)) reuses++;
            sim.Tick();
            for (int s = 0; s < n; s++) { prevAlive[s] = seen.Alive[s]; prevType[s] = seen.TypeId[s]; prevCell[s] = seen.Cell[s]; }
            bool first = t == 0;
            seen.Update(w.NavGrid.Version, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, w.Fog, 0, true, w.NavGrid.Width);
            for (int s = 0; s < n; s++)
            {
                bool live = w.Resources.Alive[s];
                if (live && (first || Sees(w, w.Resources.TypeId[s], w.Resources.Cell[s])))
                    everSeen.Add((s, w.Resources.TypeId[s], w.Resources.Cell[s]));
                if (seen.Alive[s])
                    Assert.True(everSeen.Contains((s, seen.TypeId[s], seen.Cell[s])),
                        $"seed {seed} tick {t}: slot {s} shows a tree at cell {seen.Cell[s]} the player never saw (store: alive {live} cell {w.Resources.Cell[s]})");
                bool moved = prevAlive[s] != seen.Alive[s] || seen.Alive[s] && (prevType[s] != seen.TypeId[s] || prevCell[s] != seen.Cell[s]);
                bool oldSeen = prevAlive[s] && Sees(w, prevType[s], prevCell[s]);
                bool newSeen = live && Sees(w, w.Resources.TypeId[s], w.Resources.Cell[s]);
                if (!first && moved)
                {
                    Assert.True(oldSeen || newSeen, $"seed {seed} tick {t}: slot {s} changed in the copy out of sight");
                    if (prevAlive[s] && !seen.Alive[s] && live) removalsOnly++;
                }
                bool differs = live != seen.Alive[s] || live && (w.Resources.TypeId[s] != seen.TypeId[s] || w.Resources.Cell[s] != seen.Cell[s]);
                bool copySeen = seen.Alive[s] && Sees(w, seen.TypeId[s], seen.Cell[s]);
                Assert.False(differs && newSeen, $"seed {seed} tick {t}: slot {s}'s live node is in sight but the copy differs");
                Assert.False(differs && copySeen, $"seed {seed} tick {t}: slot {s}'s remembered node is in sight but stale");
                if (differs) staleSlotTicks++;
            }
        }
        _out.WriteLine($"seed {seed}: {reuses} slot reuses, {removalsOnly} removal-only steps (old footprint seen, new node not), {staleSlotTicks} stale slot-ticks, {everSeen.Count} seen nodes");
        Assert.True(reuses > 100 && staleSlotTicks > 0, "the run must reuse slots and keep some stale");
    }

    /// <summary>The hash twin with slot reuse (the M4-VH2 twin never respawned): SeenResources updated every tick, fog on and off alternately, changes no state hash.</summary>
    [Theory]
    [InlineData(5UL)]
    [InlineData(23UL)]
    public void SeenResources_WithSlotReuse_HashEqualToABareTwinEveryTick(ulong seed)
    {
        (Simulation bare, EntityHandle scoutA, List<EntityHandle> treesA) = Forest(seed);
        (Simulation viewed, EntityHandle scoutB, List<EntityHandle> treesB) = Forest(seed);
        World w = viewed.World;
        var seen = new SeenResources(w.Data.Resources, w.Resources.Capacity);
        var rngA = new Random((int)seed + 9);
        var rngB = new Random((int)seed + 9);
        var moves = new Random((int)seed);
        for (int t = 0; t < 1200; t++)
        {
            if (t % 50 == 0)
            {
                Vector2 p = At(bare, moves.Next(3, 60), moves.Next(3, 60));
                bare.Enqueue(Command.Move(0, scoutA, p));
                viewed.Enqueue(Command.Move(0, scoutB, p));
            }
            if (t % 5 == 2)
            {
                Churn(bare.World, treesA, rngA, respawn: t % 10 == 2);
                Churn(w, treesB, rngB, respawn: t % 10 == 2);
            }
            bare.Tick();
            viewed.Tick();
            ulong before = viewed.StateHash();
            seen.Update(w.NavGrid.Version, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, w.Fog, 0, (t & 1) == 0, w.NavGrid.Width);
            Assert.Equal(before, viewed.StateHash());
            Assert.Equal(bare.StateHash(), viewed.StateHash());
        }
        Assert.True(seen.Version > 0);
    }

    /// <summary>MaxCells always fits Collect: centres on exact cell edges and corners, random centres and radii (0.01 to 60 m), discs and rings, on a large flat map; a buffer of exactly MaxCells never truncates.</summary>
    [Fact]
    public void MaxCells_AlwaysFitsWhatCollectWants()
    {
        Heightmap map = ResourceMaps.Flat(256, 256);
        var big = new GroundQuad[GroundPatch.MaxCells(70f) * 2];
        var rng = new Random(77);
        int cases = 0, tight = 0;
        for (int i = 0; i < 20000; i++)
        {
            float r = i % 4 == 0 ? rng.Next(1, 30) * Cs * 0.5f : 0.01f + (float)rng.NextDouble() * 60f;
            float x = i % 3 == 0 ? rng.Next(40, 216) * Cs : 80f + (float)rng.NextDouble() * 340f;
            float y = i % 5 == 0 ? rng.Next(40, 216) * Cs : 80f + (float)rng.NextDouble() * 340f;
            float inner = i % 2 == 0 ? 0f : MathF.Max(0f, r - 0.22f);
            int want = GroundPatch.Collect(map, x, y, r, inner, big);
            int max = GroundPatch.MaxCells(r);
            Assert.True(want <= max, $"disc ({x}, {y}) r {r} inner {inner}: Collect wants {want} cells, MaxCells {max}");
            if (inner == 0f)
            {
                // A disc's whole bounding box can be wanted; check a buffer of exactly MaxCells holds it all.
                var exact = new GroundQuad[max];
                Assert.Equal(want, GroundPatch.Collect(map, x, y, r, 0f, exact));
                if (want == max) tight++;
            }
            cases++;
        }
        _out.WriteLine($"{cases} discs and rings: every Collect fit MaxCells ({tight} exactly full)");
    }

    // 16 x 12: columns 0-7 a level-1 plateau, 8-15 level 0 (a cliff at x = 16 m); row 9's column 8 a half-height ramp.
    private static Heightmap TwoLevels()
    {
        const int w = 16, h = 12;
        var levels = new byte[w * h];
        var elev = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool high = x < 8;
                levels[y * w + x] = (byte)(high ? 1 : 0);
                elev[y * w + x] = high ? MapConstants.LevelHeight : 0f;
            }
        elev[9 * w + 8] = MapConstants.LevelHeight / 2f;
        return new Heightmap(w, h, levels, elev);
    }

    /// <summary>
    /// A disc centred exactly on the cliff lip (x = 16 m) and one cell off it on each side lists cells of both levels and
    /// draws every interior point (not just the rim) on the terrain within 0.05 m; a disc centred on the ramp cell too. No
    /// quad's corners differ by more than one level (none spans the cliff wall: cells share no vertices).
    /// </summary>
    [Theory]
    [InlineData(16f, 12f)]   // exactly on the lip
    [InlineData(14f, 12f)]   // one cell onto the plateau
    [InlineData(18f, 12f)]   // one cell off on the low ground
    [InlineData(17f, 19f)]   // on the ramp cell's centre
    [InlineData(16f, 18f)]   // on the lip at the ramp's corner
    public void LipRampAndOneCellOff_EveryInteriorPointOnTheTerrain(float x, float y)
    {
        Heightmap map = TwoLevels();
        foreach (float r in new[] { 1f, 3f, 6f })
        {
            var quads = new GroundQuad[GroundPatch.MaxCells(r)];
            int n = GroundPatch.Collect(map, x, y, r, 0f, quads);
            bool high = false, low = false;
            for (int k = 0; k < n; k++)
            {
                GroundQuad q = quads[k];
                float lo = MathF.Min(MathF.Min(q.H00, q.H10), MathF.Min(q.H01, q.H11));
                float hi = MathF.Max(MathF.Max(q.H00, q.H10), MathF.Max(q.H01, q.H11));
                Assert.True(hi - lo <= MapConstants.LevelHeight + 1e-4f, $"({x}, {y}) r {r}: cell ({q.X}, {q.Y}) corners span {lo}-{hi}");
                if (q.X < 8) high = true; else low = true;
            }
            if (MathF.Abs(x - 16f) <= 2f && r >= 3f) Assert.True(high && low, $"({x}, {y}) r {r}: cells on the plateau {high}, low ground {low}");
            int points = 0;
            for (float px = x - r; px <= x + r; px += 0.137f)
                for (float py = y - r; py <= y + r; py += 0.137f)
                {
                    if (Vector2.Distance(new Vector2(px, py), new Vector2(x, y)) >= r * 0.999f) continue;
                    if (px < 0f || py < 0f || px >= map.Width * Cs || py >= map.Height * Cs) continue;
                    float fx = px / Cs - MathF.Floor(px / Cs), fy = py / Cs - MathF.Floor(py / Cs);
                    if (fx < 0.001f || fx > 0.999f || fy < 0.001f || fy > 0.999f) continue;
                    int cx = (int)MathF.Floor(px / Cs), cy = (int)MathF.Floor(py / Cs), k = -1;
                    for (int j = 0; j < n && k < 0; j++) if (quads[j].X == cx && quads[j].Y == cy) k = j;
                    Assert.True(k >= 0, $"({x}, {y}) r {r}: interior point ({px}, {py}) has no quad");
                    float drawn = GroundPatch.HeightIn(quads[k], px, py), ground = TerrainHeight.At(map, px, py);
                    Assert.True(MathF.Abs(drawn - ground) <= 0.05f, $"({x}, {y}) r {r}: ({px}, {py}) drawn {drawn}, ground {ground}");
                    points++;
                }
            Assert.True(points > 30);
        }
    }

    /// <summary>Edges: an inner radius past the outer keeps no more than the disc; a radius past the 4,096 m clamp is clamped in both MaxCells and Collect (the buffer size stays bounded by the map in practice); a centre off the map whose disc reaches onto it keeps the on-map cells.</summary>
    [Fact]
    public void Edges_InnerPastOuter_HugeRadius_CentreOffTheMap()
    {
        Heightmap map = TwoLevels();
        var quads = new GroundQuad[256];
        // Inner past outer: the documented rule (nearest < outer and farthest > inner) keeps a few cells; never more than the plain disc's (no caller passes it).
        Assert.True(GroundPatch.Collect(map, 10f, 10f, 3f, 5f, quads) <= GroundPatch.Collect(map, 10f, 10f, 3f, 0f, quads));
        Assert.Equal(GroundPatch.MaxCells(4096f), GroundPatch.MaxCells(1e9f));
        Assert.Equal(16 * 12, GroundPatch.Collect(map, 16f, 12f, 1e9f, 0f, quads));
        Assert.True(GroundPatch.Collect(map, -1f, 12f, 3f, 0f, quads) > 0);
        Assert.Equal(0, GroundPatch.Collect(map, -1f, 12f, 0.9f, 0f, quads));
        Assert.Equal(0, GroundPatch.Collect(map, float.PositiveInfinity, 12f, 3f, 0f, quads));
        Assert.Equal(0, GroundPatch.Collect(map, 16f, 12f, -3f, 0f, quads));
    }
}
