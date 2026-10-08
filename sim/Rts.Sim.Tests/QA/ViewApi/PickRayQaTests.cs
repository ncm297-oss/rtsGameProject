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

    [Theory]
    [InlineData(1UL)]
    [InlineData(21UL)]
    public void ResourcePickRay_EqualsABruteForceMarch_WithTerrainOcclusion(ulong seed)
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
            // Only nodes within 30 m of the aim matter (speed); the march sees the same ones the picker could hit.
            var boxes = all.FindAll(b => MathF.Abs((b.X0 + b.X1) / 2f - near.X0) < 30f && MathF.Abs((b.Z0 + b.Z1) / 2f - near.Z0) < 60f);
            int want = March(w, boxes, o, d, out bool graze, out float tWant);
            if (graze) { grazing++; continue; }
            ResourceStore r = w.Resources;
            int got = ResourcePicker.PickRay(w.NavGrid, w.Data.Resources, r.Alive, r.TypeId, r.Cell, w.Heightmap, o, d, WoodHeight, GoldHeight, out float t);
            if (want >= 0) picked++;
            bool ok = got == want && (want < 0 ? float.IsPositiveInfinity(t) : MathF.Abs(t - tWant) * d.Length() <= Step * 2f);
            if (!ok && wrong++ == 0) first = $"origin {o} dir {d}: march {want} (t {tWant}), PickRay {got} (t {t})";
        }
        Assert.Equal(hash, sim.StateHash()); // read-only
        _out.WriteLine($"seed {seed}: {rays} rays, {picked} node, {grazing} grazing skipped, {wrong} wrong {first}");
        Assert.True(wrong == 0, $"{wrong} of {rays - grazing} rays disagree; first: {first}");
        Assert.True(picked > 300, $"coverage: {picked} node picks");
    }

    // Measures how often the resource pick names a tree whose drawn mesh (PropsView: a 0.15 m trunk to 1 m, then a cone
    // of radius 0.8 m tapering to the 3.5 m top; the 7-sided cone is inside this circle) the ray never touches. A pick
    // there means the pixel shows the ground or a node behind, not that tree.
    [Fact]
    public void ResourcePickRay_TreeColumnVersusDrawnTree_Measure()
    {
        Simulation sim = World(1, 0);
        World w = sim.World;
        ResourceStore r = w.Resources;
        var rng = new Random(77);
        int treePicks = 0, emptyColumn = 0;
        List<Box> all = NodeBoxes(w);
        for (int k = 0; k < 4000; k++)
        {
            Box near = all[rng.Next(all.Count)];
            (Vector3 o, Vector3 d) = CameraRay(rng, w, new Vector2((near.X0 + near.X1) / 2f, (near.Z0 + near.Z1) / 2f));
            int got = ResourcePicker.PickRay(w.NavGrid, w.Data.Resources, r.Alive, r.TypeId, r.Cell, w.Heightmap, o, d, WoodHeight, GoldHeight, out _);
            if (got < 0 || w.Data.Resources[r.TypeId[got]].Resource != ResourceKind.Wood) continue;
            treePicks++;
            Box b = all.Find(x => x.Index == got);
            if (!TouchesTree(o, d, b)) emptyColumn++;
        }
        _out.WriteLine($"tree picks {treePicks}, of which the ray misses the drawn tree {emptyColumn} ({100.0 * emptyColumn / Math.Max(1, treePicks):0.#} %)");
        Assert.True(treePicks > 500);
    }

    // The right click's intent for a pixel: the node whose drawn mesh (tree: trunk + cone; mine: the 1.6 m block and the
    // half-size gold block to 2.1 m) the ray meets first, else the node whose footprint holds the visible ground point,
    // else none. Compares the M3-V3b pick (box column first, else the ground point's node) and the M3-V3 one (ground
    // point's node only) with it, counting the pixels each gets wrong. A measurement row: it prints, it asserts only
    // coverage (see the QA report for the numbers).
    [Fact]
    public void RightClickNodeIntent_ColumnPickVersusGroundPick_Measure()
    {
        Simulation sim = World(1, 0);
        World w = sim.World;
        ResourceStore r = w.Resources;
        NavGrid g = w.NavGrid;
        List<Box> all = NodeBoxes(w);
        var rng = new Random(91);
        int rays = 0, involved = 0, newWrong = 0, oldWrong = 0, newWrongShowsGround = 0;
        int newGroundAsNode = 0, newNodeAsGround = 0, oldGroundAsNode = 0, oldNodeAsGround = 0;
        for (int k = 0; k < 3000; k++)
        {
            Box near = all[rng.Next(all.Count)];
            (Vector3 o, Vector3 d) = CameraRay(rng, w, new Vector2((near.X0 + near.X1) / 2f, (near.Z0 + near.Z1) / 2f));
            rays++;
            if (!GroundPicker.TryPick(w.Heightmap, o, d, out Vector3 gp)) continue;
            int groundNode = ResourcePicker.NodeAtPoint(g, w.Data.Resources, r.Alive, r.TypeId, r.Cell, new Vector2(gp.X, gp.Z));
            float gt = Vector3.Dot(gp - o, d) / d.LengthSquared();
            // Truth: drawn meshes of the nodes near the aim, sampled along the ray up to the ground hit.
            var nearBoxes = all.FindAll(b => MathF.Abs(b.X0 - near.X0) < 24f && MathF.Abs(b.Z0 - near.Z0) < 40f);
            int meshNode = -1;
            float len = d.Length();
            for (float s = 0f; s < gt * len && meshNode < 0; s += 0.01f)
            {
                Vector3 p = o + d / len * s;
                foreach (Box b in nearBoxes)
                {
                    if (p.X < b.X0 || p.X > b.X1 || p.Z < b.Z0 || p.Z > b.Z1 || p.Y < b.Y0 || p.Y > b.Y1) continue;
                    if (InMesh(w, b, p)) { meshNode = b.Index; break; }
                }
            }
            int truth = meshNode >= 0 ? meshNode : groundNode;
            int col = ResourcePicker.PickRay(g, w.Data.Resources, r.Alive, r.TypeId, r.Cell, w.Heightmap, o, d, WoodHeight, GoldHeight, out _);
            int now = col >= 0 ? col : groundNode;
            if (truth < 0 && now < 0 && groundNode < 0) continue;
            involved++;
            if (now != truth) { newWrong++; if (meshNode < 0) newWrongShowsGround++; }
            if (groundNode != truth) oldWrong++;
            if (truth < 0 && now >= 0) newGroundAsNode++;
            if (truth >= 0 && now < 0) newNodeAsGround++;
            if (truth < 0 && groundNode >= 0) oldGroundAsNode++;
            if (truth >= 0 && groundNode < 0) oldNodeAsGround++;
        }
        _out.WriteLine($"{rays} rays, {involved} with a node involved: column pick wrong {newWrong} ({100.0 * newWrong / involved:0.#} %, of which the pixel shows bare ground {newWrongShowsGround}), ground pick wrong {oldWrong} ({100.0 * oldWrong / involved:0.#} %); open ground taken for a node: column {newGroundAsNode}, ground pick {oldGroundAsNode}; a drawn node taken for open ground: column {newNodeAsGround}, ground pick {oldNodeAsGround}");
        Assert.True(involved > 1000);
    }

    // True if p (inside node box b) is inside the node's drawn mesh.
    private static bool InMesh(World w, Box b, Vector3 p)
    {
        float cx = (b.X0 + b.X1) / 2f, cz = (b.Z0 + b.Z1) / 2f, h = p.Y - b.Y0;
        if (w.Data.Resources[w.Resources.TypeId[b.Index]].Resource == ResourceKind.Gold)
        {
            if (h <= 1.6f) return true;
            return MathF.Abs(p.X - cx) <= (b.X1 - b.X0) / 4f && MathF.Abs(p.Z - cz) <= (b.Z1 - b.Z0) / 4f;
        }
        float rr = MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz));
        return h <= 1f ? rr <= 0.15f : rr <= 0.8f * (3.5f - h) / 2.5f;
    }

    private static bool TouchesTree(Vector3 o, Vector3 d, Box b)
    {
        Vector3 n = Vector3.Normalize(d);
        float cx = (b.X0 + b.X1) / 2f, cz = (b.Z0 + b.Z1) / 2f, baseY = b.Y0;
        for (float s = 0f; s < 400f; s += 0.01f)
        {
            Vector3 p = o + n * s;
            if (p.Y < baseY) return false;
            float h = p.Y - baseY;
            float rr = MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz));
            if (h <= 1f && rr <= 0.15f) return true;
            if (h > 1f && h <= 3.5f && rr <= 0.8f * (3.5f - h) / 2.5f) return true;
        }
        return false;
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
