using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Rts.Sim.Tests.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA M3-V3b (2026-10-07-1715): <see cref="BuildingPicker.PickRay(BuildingStore, System.Collections.Immutable.ImmutableArray{BuildingDef}, NavGrid, Heightmap, int, Vector3, Vector3, float, float, out float)"/>
/// and <see cref="ResourcePicker.PickRay"/> (terrain occlusion, entry parameter) against an independent brute-force
/// ray march over the same geometry: each live building / node is its box (footprint x [centre height, centre height +
/// its drawn height]), the terrain is each cell's surface plane (<see cref="TerrainHeight.InCell"/>), and the first of
/// them the ray reaches is what the pixel shows. Rays are RTS-camera-like (55 degree pitch, zoom 20-60 m, off-centre
/// pixels) and aimed near buildings / nodes on real generated maps with extra houses spawned on random fitting cells,
/// many of them on slopes and below cliffs. Grazing rays (box and ground within 0.1 m along the ray) are skipped.
/// </summary>
[Collection(SerialCollection.Name)]
public class PickRayQaTests
{
    private const float BoxHeight = 3f, SiteMin = 0.15f; // BuildingViews (game side)
    private const float WoodHeight = 3.5f, GoldHeight = 2.1f; // PropsView: TreeHeight, MineHeight + GoldHeight
    private const float Pitch = 55f * MathF.PI / 180f; // CameraLimits.PitchDegrees
    private const float Step = 0.02f, Grazing = 0.1f;

    private readonly ITestOutputHelper _out;

    public PickRayQaTests(ITestOutputHelper output) => _out = output;

    private readonly record struct Box(int Index, float X0, float X1, float Y0, float Y1, float Z0, float Z1);

    // A real match plus up to `extra` houses for players 0 / 1 on random fitting cells (dev spawns ignore requirements).
    private static Simulation World(ulong seed, int extra)
    {
        (Simulation sim, Vector2[][] blocks, StartBasePlan plan) = StartBaseTests.MatchSetup(seed, 5, 5);
        StartBaseTests.Apply(sim, blocks, plan);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        var rng = new Random((int)seed * 31 + 7);
        int house = StartBase.BuildingOfSlot(w.Data, w.FactionOf(0), BuildingSlot.House);
        for (int k = 0, tries = 0; k < extra && tries < 20000; tries++)
        {
            int cell = rng.Next(g.Width * g.Height);
            if (!w.Buildings.Fits(house, cell)) continue;
            sim.Enqueue(Command.SpawnBuilding(k % 2, house, g.CellCenter(cell % g.Width, cell / g.Width)));
            sim.Tick();
            k++;
        }
        // Houses just north of a rise (the camera looks north, so the rise stands between it and the box): the cells
        // whose ground 2-4 m south is at least 2 m higher than the footprint centre.
        BuildingDef hd = w.Data.Buildings[house];
        const float cs = MapConstants.CellSize;
        for (int cell = 0, placed = 0; cell < g.Width * g.Height && placed < extra / 2; cell++)
        {
            int x = cell % g.Width, y = cell / g.Width;
            if (y + hd.FootprintHeight + 3 >= g.Height || !w.Buildings.Fits(house, cell)) continue;
            float h0 = TerrainHeight.At(w.Heightmap, (x + hd.FootprintWidth / 2f) * cs, (y + hd.FootprintHeight / 2f) * cs);
            float front = TerrainHeight.At(w.Heightmap, (x + hd.FootprintWidth / 2f) * cs, (y + hd.FootprintHeight + 1.5f) * cs);
            if (front - h0 < 2f) continue;
            sim.Enqueue(Command.SpawnBuilding(0, house, g.CellCenter(x, y)));
            sim.Tick();
            placed++;
        }
        sim.Tick();
        sim.Tick();
        return sim;
    }

    private static List<Box> BuildingBoxes(World w)
    {
        var list = new List<Box>();
        BuildingStore b = w.Buildings;
        const float cs = MapConstants.CellSize;
        for (int i = 0; i < b.Capacity; i++)
        {
            if (!b.Alive[i]) continue;
            BuildingDef def = w.Data.Buildings[b.TypeId[i]];
            float x0 = b.Cell[i] % w.NavGrid.Width * cs, z0 = b.Cell[i] / w.NavGrid.Width * cs;
            float x1 = x0 + def.FootprintWidth * cs, z1 = z0 + def.FootprintHeight * cs;
            float y0 = TerrainHeight.At(w.Heightmap, (x0 + x1) / 2f, (z0 + z1) / 2f);
            list.Add(new Box(i, x0, x1, y0, y0 + BoxHeight * BuildingPicker.BoxRise(b, w.Data.Buildings, i, SiteMin), z0, z1));
        }
        return list;
    }

    private static List<Box> NodeBoxes(World w)
    {
        var list = new List<Box>();
        ResourceStore r = w.Resources;
        const float cs = MapConstants.CellSize;
        for (int i = 0; i < r.Capacity; i++)
        {
            if (!r.Alive[i]) continue;
            ResourceDef def = w.Data.Resources[r.TypeId[i]];
            float x0 = r.Cell[i] % w.NavGrid.Width * cs, z0 = r.Cell[i] / w.NavGrid.Width * cs;
            float x1 = x0 + def.FootprintWidth * cs, z1 = z0 + def.FootprintHeight * cs;
            float y0 = TerrainHeight.At(w.Heightmap, (x0 + x1) / 2f, (z0 + z1) / 2f);
            list.Add(new Box(i, x0, x1, y0, y0 + (def.Resource == ResourceKind.Gold ? GoldHeight : WoodHeight), z0, z1));
        }
        return list;
    }

    // The reference answer: each box's exact entry along the ray (slab test), the terrain by a brute-force march of
    // the cell surface planes (independent of GroundPicker's DDA); the nearest box entered before the march meets the
    // terrain, -1 if the terrain comes first or nothing is met. `grazing` when the box entry and the terrain (or two box
    // entries) are within Grazing meters along the ray; `tBox` the entry as a parameter of `d`.
    private static int March(World w, List<Box> boxes, Vector3 o, Vector3 d, out bool grazing, out float tBox)
    {
        float len = d.Length();
        Vector3 n = d / len;
        const float cs = MapConstants.CellSize;
        Heightmap map = w.Heightmap;
        float lid = MapConstants.MaxLevel * MapConstants.LevelHeight + 2f;
        float start = n.Y < 0f && o.Y > lid ? (o.Y - lid) / -n.Y : 0f;
        float groundS = float.PositiveInfinity;
        var clearance = new List<(float S, float Above)>();
        for (float s = start; s < start + 400f; s += Step)
        {
            Vector3 p = o + n * s;
            if (p.Y < -2f) break;
            if (p.X < 0f || p.Z < 0f || p.X >= map.Width * cs || p.Z >= map.Height * cs) continue; // off the map (the camera may be)
            float above = p.Y - TerrainHeight.InCell(map, (int)(p.X / cs), (int)(p.Z / cs), p.X, p.Z);
            if (above < 0f) { groundS = s; break; }
            clearance.Add((s, above));
        }
        float best = float.PositiveInfinity, second = float.PositiveInfinity;
        int bestIndex = -1;
        foreach (Box b in boxes)
        {
            if (!Entry(o, n, b, out float e)) continue;
            if (e < best) { second = best; best = e; bestIndex = b.Index; }
            else if (e < second) second = e;
        }
        grazing = MathF.Abs(best - groundS) < Grazing || MathF.Abs(second - best) < Grazing && second < groundS;
        // A ray that skims the terrain (a cliff rim) within a few cm before the box: the march's step can miss the clip.
        foreach ((float cs2, float above) in clearance)
            if (best < groundS && cs2 < best && above < 0.05f) { grazing = true; break; }
        tBox = float.PositiveInfinity;
        if (bestIndex < 0 || best >= groundS) return -1;
        tBox = best / len;
        return bestIndex;
    }

    private static bool Entry(Vector3 o, Vector3 n, Box b, out float t0)
    {
        t0 = 0f;
        float t1 = float.PositiveInfinity;
        return Slab(o.X, n.X, b.X0, b.X1, ref t0, ref t1) && Slab(o.Y, n.Y, b.Y0, b.Y1, ref t0, ref t1) && Slab(o.Z, n.Z, b.Z0, b.Z1, ref t0, ref t1);
    }

    // An RTS-camera ray toward a point near `aim`: the camera sits `zoom` up and zoom / tan(pitch) south, moved
    // sideways / along by up to 40 % of the zoom for off-centre pixels.
    private static (Vector3 O, Vector3 D) CameraRay(Random rng, World w, Vector2 aim)
    {
        float zoom = 20f + (float)rng.NextDouble() * 40f;
        var p = new Vector2(aim.X + ((float)rng.NextDouble() - 0.5f) * 12f, aim.Y + ((float)rng.NextDouble() - 0.5f) * 12f);
        float gy = TerrainHeight.At(w.Heightmap, Math.Clamp(p.X, 0.1f, w.Heightmap.Width * 2f - 0.1f), Math.Clamp(p.Y, 0.1f, w.Heightmap.Height * 2f - 0.1f));
        var target = new Vector3(p.X, gy + (float)(rng.NextDouble() * rng.NextDouble()) * 3.5f, p.Y);
        var o = new Vector3(target.X + ((float)rng.NextDouble() - 0.5f) * 0.8f * zoom, zoom + gy,
            target.Z + zoom / MathF.Tan(Pitch) + ((float)rng.NextDouble() - 0.5f) * 0.8f * zoom);
        return (o, target - o);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(21UL)]
    [InlineData(31UL)]
    public void BuildingPickRay_EqualsABruteForceMarch_WithTerrainOcclusion(ulong seed)
    {
        Simulation sim = World(seed, 60);
        World w = sim.World;
        List<Box> boxes = BuildingBoxes(w);
        Assert.True(boxes.Count >= 30, $"only {boxes.Count} buildings");
        var rng = new Random((int)seed);
        int rays = 0, picked = 0, ground = 0, grazing = 0, wrong = 0, occludedSeen = 0;
        string first = "";
        ulong hash = sim.StateHash();
        while (rays < 2500)
        {
            Box near = boxes[rng.Next(boxes.Count)];
            (Vector3 o, Vector3 d) = CameraRay(rng, w, new Vector2((near.X0 + near.X1) / 2f, (near.Z0 + near.Z1) / 2f));
            rays++;
            int want = March(w, boxes, o, d, out bool graze, out float tWant);
            if (graze) { grazing++; continue; }
            int got = BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, o, d, BoxHeight, SiteMin, out float t);
            if (want >= 0) picked++; else ground++;
            // A ray whose line meets some box but that the terrain hides first.
            if (want < 0)
                foreach (Box b in boxes)
                    if (Enters(o, d, b)) { occludedSeen++; break; }
            bool ok = got == want && (want < 0 ? float.IsPositiveInfinity(t) : MathF.Abs(t - tWant) * d.Length() <= Step * 2f);
            if (!ok && wrong++ == 0) first = $"origin {o} dir {d}: march {want} (t {tWant}), PickRay {got} (t {t})";
        }
        Assert.Equal(hash, sim.StateHash());
        _out.WriteLine($"seed {seed}: {rays} rays, {picked} box, {ground} ground ({occludedSeen} with a box hidden behind terrain), {grazing} grazing skipped, {wrong} wrong {first}");
        Assert.True(wrong == 0, $"{wrong} of {rays - grazing} rays disagree; first: {first}");
        Assert.True(picked > 500 && ground > 300 && occludedSeen > 20, $"coverage: {picked} box, {ground} ground, {occludedSeen} hidden by terrain");
    }

    // ---- resource nodes: the drawn shapes (M3-V4, BUG-0125) ----
    // The reference for nodes is sampled, not solved: along the ray, inside each node's footprint column, every 2 mm the
    // point is tested against the drawn mesh's solid (tree: trunk r 0.15 m to 1 m, then the cone r 0.8 (3.5 - h) / 2.5;
    // mine: the 1.6 m block, then the half-size gold block to 2.1 m) with a signed margin (meters inside, negative
    // outside). A ray whose best margin through a node is within GrazeMargin of 0 grazes it and is skipped (sampling can't
    // tell a 1 cm touch from a miss); the 7- and 6-sided meshes are inside these circles.
    private const float NodeStep = 0.002f, GrazeMargin = 0.01f;

    // A node's column entry and exit along unit ray n (distances), false if the ray misses the column.
    private static bool Chord(Vector3 o, Vector3 n, Box b, out float s0, out float s1)
    {
        s0 = 0f;
        s1 = float.PositiveInfinity;
        return Slab(o.X, n.X, b.X0, b.X1, ref s0, ref s1) && Slab(o.Y, n.Y, b.Y0, b.Y1, ref s0, ref s1) && Slab(o.Z, n.Z, b.Z0, b.Z1, ref s0, ref s1);
    }

    // Signed distance-like margin of p inside node b's drawn solid: > 0 inside, < 0 outside.
    private static float Margin(World w, Box b, Vector3 p)
    {
        float cx = (b.X0 + b.X1) / 2f, cz = (b.Z0 + b.Z1) / 2f, h = p.Y - b.Y0;
        if (w.Data.Resources[w.Resources.TypeId[b.Index]].Resource == ResourceKind.Gold)
        {
            float block = MathF.Min(MathF.Min(p.X - b.X0, b.X1 - p.X), MathF.Min(MathF.Min(p.Z - b.Z0, b.Z1 - p.Z), MathF.Min(h, 1.6f - h)));
            float hx = (b.X1 - b.X0) / 4f, hz = (b.Z1 - b.Z0) / 4f;
            float gold = MathF.Min(MathF.Min(hx - MathF.Abs(p.X - cx), hz - MathF.Abs(p.Z - cz)), MathF.Min(h - 1.6f, 2.1f - h));
            return MathF.Max(block, gold);
        }
        float rr = MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz));
        float trunk = MathF.Min(0.15f - rr, MathF.Min(h, 1f - h));
        float cone = MathF.Min(0.8f * (3.5f - h) / 2.5f - rr, MathF.Min(h - 1f, 3.5f - h));
        return MathF.Max(trunk, cone);
    }

    // Samples node b along unit ray n up to distance sMax: the first inside sample (+infinity if none) and the best margin.
    private static float Drawn(World w, Box b, Vector3 o, Vector3 n, float sMax, out float best)
    {
        best = float.NegativeInfinity;
        if (!Chord(o, n, b, out float s0, out float s1)) return float.PositiveInfinity;
        float entry = float.PositiveInfinity;
        for (float s = s0; s <= MathF.Min(s1, sMax); s += NodeStep)
        {
            float m = Margin(w, b, o + n * s);
            if (m > best) best = m;
            if (m >= 0f && entry == float.PositiveInfinity) entry = s;
        }
        return entry;
    }

    // The terrain along unit ray n by a brute-force march of the cell planes: the distance and point of the first hit.
    private static float GroundMarch(World w, Vector3 o, Vector3 n, out Vector3 at, out float skim, float before)
    {
        const float cs = MapConstants.CellSize;
        Heightmap map = w.Heightmap;
        float lid = MapConstants.MaxLevel * MapConstants.LevelHeight + 2f;
        float start = n.Y < 0f && o.Y > lid ? (o.Y - lid) / -n.Y : 0f;
        at = default;
        skim = float.PositiveInfinity;
        for (float s = start; s < start + 400f; s += Step)
        {
            Vector3 p = o + n * s;
            if (p.Y < -2f) break;
            if (p.X < 0f || p.Z < 0f || p.X >= map.Width * cs || p.Z >= map.Height * cs) continue;
            float above = p.Y - TerrainHeight.InCell(map, (int)(p.X / cs), (int)(p.Z / cs), p.X, p.Z);
            if (above < 0f) { at = p; return s; }
            if (s < before && above < skim) skim = above;
        }
        return float.PositiveInfinity;
    }

    // The reference node pick: the node whose drawn solid the ray enters first, before the terrain, or after it when the
    // terrain hit lies inside that node's own footprint (the documented occlusion rule, as for buildings); -1 otherwise.
    private static int MarchNodes(World w, List<Box> boxes, Vector3 o, Vector3 d, out bool grazing, out float tNode)
    {
        float len = d.Length();
        Vector3 n = d / len;
        float groundS = GroundMarch(w, o, n, out Vector3 gp, out _, float.PositiveInfinity);
        float best = float.PositiveInfinity, second = float.PositiveInfinity;
        int bestIndex = -1;
        grazing = false;
        foreach (Box b in boxes)
        {
            float e = Drawn(w, b, o, n, float.PositiveInfinity, out float margin);
            if (MathF.Abs(margin) < GrazeMargin) grazing = true;
            if (e == float.PositiveInfinity) continue;
            bool ownGround = gp.X >= b.X0 && gp.X <= b.X1 && gp.Z >= b.Z0 && gp.Z <= b.Z1;
            if (e >= groundS && !ownGround) continue;
            if (e < best) { second = best; best = e; bestIndex = b.Index; }
            else if (e < second) second = e;
        }
        if (MathF.Abs(best - groundS) < Grazing || MathF.Abs(second - best) < Grazing) grazing = true;
        GroundMarch(w, o, n, out _, out float skim, best);
        if (bestIndex >= 0 && skim < 0.05f) grazing = true; // skims a cliff rim before the node
        tNode = bestIndex < 0 ? float.PositiveInfinity : best / len;
        return bestIndex;
    }

    private static List<Box> Near(List<Box> all, Box near) =>
        all.FindAll(b => MathF.Abs((b.X0 + b.X1) / 2f - near.X0) < 30f && MathF.Abs((b.Z0 + b.Z1) / 2f - near.Z0) < 60f);

    private static int Pick(World w, Vector3 o, Vector3 d, out float t)
    {
        ResourceStore r = w.Resources;
        return ResourcePicker.PickRay(w.NavGrid, w.Data.Resources, r.Alive, r.TypeId, r.Cell, w.Heightmap, o, d, ResourcePickerTests.Shape, out t);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(21UL)]
    public void ResourcePickRay_EqualsABruteForceMarch_OfTheDrawnShapes_WithTerrainOcclusion(ulong seed)
    {
        Simulation sim = World(seed, 0);
        World w = sim.World;
        List<Box> all = NodeBoxes(w);
        var rng = new Random((int)seed + 100);
        int rays = 0, picked = 0, grazing = 0, wrong = 0;
        string first = "";
        ulong hash = sim.StateHash();
        while (rays < 1500)
        {
            Box near = all[rng.Next(all.Count)];
            (Vector3 o, Vector3 d) = CameraRay(rng, w, new Vector2((near.X0 + near.X1) / 2f, (near.Z0 + near.Z1) / 2f));
            rays++;
            int want = MarchNodes(w, Near(all, near), o, d, out bool graze, out float tWant);
            if (graze) { grazing++; continue; }
            int got = Pick(w, o, d, out float t);
            if (want >= 0) picked++;
            bool ok = got == want && (want < 0 ? float.IsPositiveInfinity(t) : MathF.Abs(t - tWant) * d.Length() <= NodeStep * 2f);
            if (!ok && wrong++ == 0) first = $"origin {o} dir {d}: march {want} (t {tWant}), PickRay {got} (t {t})";
        }
        Assert.Equal(hash, sim.StateHash()); // read-only
        _out.WriteLine($"seed {seed}: {rays} rays, {picked} node, {grazing} grazing skipped, {wrong} wrong {first}");
        Assert.True(wrong == 0, $"{wrong} of {rays - grazing} rays disagree; first: {first}");
        Assert.True(picked > 300 && grazing < rays / 5, $"coverage: {picked} node picks, {grazing} grazing");
    }

    // BUG-0125 (was a measurement row): every tree the pick names is one whose drawn trunk or cone the ray touches. The
    // M3-V3b column pick named 1,691 of 2,180 trees whose mesh the ray missed (77.6 %).
    [Fact]
    public void ResourcePickRay_EveryTreePick_TouchesTheDrawnTree()
    {
        Simulation sim = World(1, 0);
        World w = sim.World;
        ResourceStore r = w.Resources;
        var rng = new Random(77);
        int treePicks = 0, misses = 0, grazes = 0;
        List<Box> all = NodeBoxes(w);
        for (int k = 0; k < 4000; k++)
        {
            Box near = all[rng.Next(all.Count)];
            (Vector3 o, Vector3 d) = CameraRay(rng, w, new Vector2((near.X0 + near.X1) / 2f, (near.Z0 + near.Z1) / 2f));
            int got = Pick(w, o, d, out _);
            if (got < 0 || w.Data.Resources[r.TypeId[got]].Resource != ResourceKind.Wood) continue;
            treePicks++;
            Box b = all.Find(x => x.Index == got);
            Drawn(w, b, o, Vector3.Normalize(d), float.PositiveInfinity, out float margin);
            if (MathF.Abs(margin) < GrazeMargin) grazes++;
            else if (margin < 0f) misses++;
        }
        _out.WriteLine($"tree picks {treePicks}, of which the ray misses the drawn tree {misses} ({100.0 * misses / Math.Max(1, treePicks):0.#} %), grazing {grazes}");
        Assert.True(treePicks > 500, $"only {treePicks} tree picks");
        Assert.True(misses == 0, $"{misses} of {treePicks} tree picks miss the drawn tree");
    }

    // The right click's intent for a pixel: the node whose drawn mesh the ray meets first (before the visible ground),
    // else the node whose footprint holds the visible ground point, else none. The M3-V4 pick (drawn shape first, else the
    // ground point's node) against it, with the M3-V3 ground-point-only pick for comparison. BUG-0125 (was a measurement
    // row; the M3-V3b column pick took open ground for a node in 339 of 3,000 rays and was wrong in 68.1 % of the 1,613
    // rays with a node involved): open ground is never taken for a node, a drawn node never for open ground.
    [Fact]
    public void RightClickNodeIntent_DrawnPick_NeverConfusesGroundAndNode()
    {
        Simulation sim = World(1, 0);
        World w = sim.World;
        ResourceStore r = w.Resources;
        NavGrid g = w.NavGrid;
        List<Box> all = NodeBoxes(w);
        var rng = new Random(91);
        int rays = 0, involved = 0, grazing = 0, newWrong = 0, oldWrong = 0;
        int newGroundAsNode = 0, newNodeAsGround = 0, oldGroundAsNode = 0, oldNodeAsGround = 0;
        string first = "";
        for (int k = 0; k < 3000; k++)
        {
            Box near = all[rng.Next(all.Count)];
            (Vector3 o, Vector3 d) = CameraRay(rng, w, new Vector2((near.X0 + near.X1) / 2f, (near.Z0 + near.Z1) / 2f));
            rays++;
            if (!GroundPicker.TryPick(w.Heightmap, o, d, out Vector3 gp)) continue;
            int groundNode = ResourcePicker.NodeAtPoint(g, w.Data.Resources, r.Alive, r.TypeId, r.Cell, new Vector2(gp.X, gp.Z));
            float len = d.Length();
            Vector3 n = d / len;
            float gs = Vector3.Dot(gp - o, d) / d.LengthSquared() * len;
            // Truth: the drawn meshes of the nodes near the aim, sampled along the ray up to the ground hit.
            int meshNode = -1;
            float meshS = float.PositiveInfinity;
            bool graze = false;
            foreach (Box b in all.FindAll(b => MathF.Abs(b.X0 - near.X0) < 24f && MathF.Abs(b.Z0 - near.Z0) < 40f))
            {
                float e = Drawn(w, b, o, n, gs, out float margin);
                if (MathF.Abs(margin) < GrazeMargin) graze = true;
                if (e < meshS) (meshS, meshNode) = (e, b.Index);
            }
            if (meshNode >= 0 && gs - meshS < Grazing) graze = true;
            int truth = meshNode >= 0 ? meshNode : groundNode;
            int pick = Pick(w, o, d, out _);
            int now = pick >= 0 ? pick : groundNode;
            if (truth < 0 && now < 0 && groundNode < 0) continue;
            if (graze) { grazing++; continue; }
            involved++;
            if (now != truth && newWrong++ == 0) first = $"origin {o} dir {d}: truth {truth} (mesh {meshNode}, ground {groundNode}), pick {pick}";
            if (groundNode != truth) oldWrong++;
            if (truth < 0 && now >= 0) newGroundAsNode++;
            if (truth >= 0 && now < 0) newNodeAsGround++;
            if (truth < 0 && groundNode >= 0) oldGroundAsNode++;
            if (truth >= 0 && groundNode < 0) oldNodeAsGround++;
        }
        _out.WriteLine($"{rays} rays, {involved} with a node involved ({grazing} grazing skipped): drawn pick wrong {newWrong} ({100.0 * newWrong / Math.Max(1, involved):0.#} %), ground pick wrong {oldWrong} ({100.0 * oldWrong / Math.Max(1, involved):0.#} %); open ground taken for a node: drawn {newGroundAsNode}, ground pick {oldGroundAsNode}; a drawn node taken for open ground: drawn {newNodeAsGround}, ground pick {oldNodeAsGround} {first}");
        Assert.True(involved > 1000, $"only {involved} rays with a node involved");
        Assert.True(newGroundAsNode == 0, $"open ground taken for a node {newGroundAsNode} times; first wrong: {first}");
        Assert.True(newNodeAsGround == 0, $"a drawn node taken for open ground {newNodeAsGround} times; first wrong: {first}");
        Assert.True(newWrong * 100 < involved, $"drawn pick wrong in {newWrong} of {involved}; first: {first}");
    }

    private static bool Enters(Vector3 o, Vector3 d, Box b)
    {
        float t0 = 0f, t1 = float.PositiveInfinity;
        return Slab(o.X, d.X, b.X0, b.X1, ref t0, ref t1) && Slab(o.Y, d.Y, b.Y0, b.Y1, ref t0, ref t1) && Slab(o.Z, d.Z, b.Z0, b.Z1, ref t0, ref t1);
    }

    private static bool Slab(float o, float d, float lo, float hi, ref float t0, ref float t1)
    {
        if (d == 0f) return o >= lo && o <= hi;
        float a = (lo - o) / d, c = (hi - o) / d;
        if (a > c) (a, c) = (c, a);
        t0 = MathF.Max(t0, a);
        t1 = MathF.Min(t1, c);
        return t0 <= t1;
    }
}
