using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Rts.Sim.Tests.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA M3-V4 (2026-10-07-2014), BUG-0125: <see cref="ResourcePicker.PickRay"/> with <see cref="PropShape"/> against an
/// independent reference that is exact rather than sampled: each node is the polyhedra <c>PropsView</c> really draws
/// (a 6-sided trunk prism and a 7-sided cone, Godot's <c>CylinderMesh</c> vertex layout, turned by
/// <see cref="PropLayout.YawOf"/>; a mine's two boxes), clipped against the ray by Cyrus-Beck half-planes; the visible
/// ground is <see cref="GroundPicker.TryPick"/>. The right click's intent is the first drawn node before the ground, else
/// the node under the ground point. Four ray families on four seeds, 3,000 rays each: general rays round nodes, forest
/// rays at the canopy band of a tree with another tree behind it (north, away from the camera), rays at a mine's block
/// and gold-block edges, and rays at nodes on a cliff lip. The pick tests true circles round the facets, so a ray that
/// passes within the circle but outside a facet (at most 8 cm, the documented approximation) is counted separately as a
/// "facet sliver", not as a wrong pick.
/// </summary>
[Collection(SerialCollection.Name)]
public class PickRayV4QaTests
{
    private const float Pitch = 55f * MathF.PI / 180f;
    private const float Tie = 0.002f; // ray-parameter ties (meters) between a node and the ground or two nodes: skipped
    private const float Sliver = 0.09f; // a ray this close to a cone / trunk circle's rim, outside the facets

    private readonly ITestOutputHelper _out;

    public PickRayV4QaTests(ITestOutputHelper output) => _out = output;

    private readonly record struct Plane(Vector3 N, float D); // inside when N.p <= D

    private sealed class Node
    {
        public int Index;
        public bool Gold;
        public float X0, X1, Z0, Z1, BaseY, Cx, Cz;
        public Plane[][] Solids = Array.Empty<Plane[]>();
    }

    private static Simulation World(ulong seed)
    {
        (Simulation sim, Vector2[][] blocks, StartBasePlan plan) = StartBaseTests.MatchSetup(seed, 5, 5);
        StartBaseTests.Apply(sim, blocks, plan);
        sim.Tick();
        return sim;
    }

    // The drawn solids of every live node, built from the mesh recipe (not from PropShape's code path).
    private static List<Node> Nodes(World w)
    {
        var list = new List<Node>();
        ResourceStore r = w.Resources;
        const float cs = MapConstants.CellSize;
        int gw = w.NavGrid.Width;
        for (int i = 0; i < r.Capacity; i++)
        {
            if (!r.Alive[i]) continue;
            ResourceDef def = w.Data.Resources[r.TypeId[i]];
            var n = new Node { Index = i, Gold = def.Resource == ResourceKind.Gold };
            n.X0 = r.Cell[i] % gw * cs;
            n.Z0 = r.Cell[i] / gw * cs;
            n.X1 = n.X0 + def.FootprintWidth * cs;
            n.Z1 = n.Z0 + def.FootprintHeight * cs;
            n.Cx = (n.X0 + n.X1) / 2f;
            n.Cz = (n.Z0 + n.Z1) / 2f;
            n.BaseY = TerrainHeight.At(w.Heightmap, n.Cx, n.Cz);
            if (n.Gold)
            {
                float hx = (n.X1 - n.X0) * 0.25f, hz = (n.Z1 - n.Z0) * 0.25f;
                n.Solids = new[]
                {
                    BoxPlanes(n.X0, n.X1, n.BaseY, n.BaseY + 1.6f, n.Z0, n.Z1),
                    BoxPlanes(n.Cx - hx, n.Cx + hx, n.BaseY + 1.6f, n.BaseY + 2.1f, n.Cz - hz, n.Cz + hz),
                };
            }
            else
            {
                float yaw = PropLayout.YawOf(r.Cell[i], def.FootprintWidth, def.FootprintHeight);
                float canopy = Math.Min(def.FootprintWidth, def.FootprintHeight) * cs * 0.8f / 2f;
                n.Solids = new[]
                {
                    Prism(n.Cx, n.Cz, yaw, 0.15f, 6, n.BaseY, n.BaseY + 1f),
                    Pyramid(n.Cx, n.Cz, yaw, canopy, 7, n.BaseY + 1f, n.BaseY + 3.5f),
                };
            }
            list.Add(n);
        }
        return list;
    }

    private static Plane[] BoxPlanes(float x0, float x1, float y0, float y1, float z0, float z1) => new[]
    {
        new Plane(new Vector3(1, 0, 0), x1), new Plane(new Vector3(-1, 0, 0), -x0),
        new Plane(new Vector3(0, 1, 0), y1), new Plane(new Vector3(0, -1, 0), -y0),
        new Plane(new Vector3(0, 0, 1), z1), new Plane(new Vector3(0, 0, -1), -z0),
    };

    // Godot CylinderMesh ring vertex k of `segments`: (sin u, cos u) * r, u = k / segments * tau; then PropLayout's yaw
    // (world x = c x + s z, world z = -s x + c z).
    private static Vector2 Ring(float cx, float cz, float yaw, float r, int k, int segments)
    {
        double u = k * 2.0 * Math.PI / segments;
        double x = Math.Sin(u) * r, z = Math.Cos(u) * r, c = Math.Cos(yaw), s = Math.Sin(yaw);
        return new Vector2((float)(cx + c * x + s * z), (float)(cz - s * x + c * z));
    }

    private static Plane SidePlane(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
    {
        Vector3 n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        float d = Vector3.Dot(n, a);
        if (Vector3.Dot(n, inside) > d) { n = -n; d = -d; }
        return new Plane(n, d);
    }

    private static Plane[] Prism(float cx, float cz, float yaw, float r, int seg, float y0, float y1)
    {
        var p = new List<Plane> { new(new Vector3(0, 1, 0), y1), new(new Vector3(0, -1, 0), -y0) };
        var inside = new Vector3(cx, (y0 + y1) / 2f, cz);
        for (int k = 0; k < seg; k++)
        {
            Vector2 a = Ring(cx, cz, yaw, r, k, seg), b = Ring(cx, cz, yaw, r, k + 1, seg);
            p.Add(SidePlane(new Vector3(a.X, y0, a.Y), new Vector3(b.X, y0, b.Y), new Vector3(a.X, y1, a.Y), inside));
        }
        return p.ToArray();
    }

    private static Plane[] Pyramid(float cx, float cz, float yaw, float r, int seg, float y0, float tip)
    {
        var p = new List<Plane> { new(new Vector3(0, -1, 0), -y0) };
        var apex = new Vector3(cx, tip, cz);
        var inside = new Vector3(cx, y0 + (tip - y0) * 0.25f, cz);
        for (int k = 0; k < seg; k++)
        {
            Vector2 a = Ring(cx, cz, yaw, r, k, seg), b = Ring(cx, cz, yaw, r, k + 1, seg);
            p.Add(SidePlane(new Vector3(a.X, y0, a.Y), new Vector3(b.X, y0, b.Y), apex, inside));
        }
        return p.ToArray();
    }

    // Cyrus-Beck: the ray parameter (unit ray, meters) where it enters the convex solid, +inf if it misses.
    private static float Enter(Plane[] solid, Vector3 o, Vector3 n)
    {
        double lo = 0, hi = double.PositiveInfinity;
        foreach (Plane p in solid)
        {
            double num = p.D - Vector3.Dot(p.N, o), den = Vector3.Dot(p.N, n);
            if (Math.Abs(den) < 1e-12) { if (num < 0) return float.PositiveInfinity; continue; }
            double t = num / den;
            if (den > 0) { if (t < hi) hi = t; }
            else if (t > lo) lo = t;
            if (lo > hi) return float.PositiveInfinity;
        }
        return (float)lo;
    }

    private static float EnterNode(Node b, Vector3 o, Vector3 n)
    {
        float e = float.PositiveInfinity;
        foreach (Plane[] s in b.Solids) e = MathF.Min(e, Enter(s, o, n));
        return e;
    }

    // Closest horizontal approach of the ray segment [0, sMax] to a tree's axis while inside the trunk / cone height band,
    // minus the circle radius there: <= 0 means the ray passes inside the circle the pick tests.
    private static float CircleGap(Node b, Vector3 o, Vector3 n, float sMax)
    {
        float best = float.PositiveInfinity;
        for (float s = 0f; s <= sMax; s += 0.004f)
        {
            Vector3 p = o + n * s;
            float h = p.Y - b.BaseY;
            if (h < 0f || h > 3.5f) continue;
            float rr = MathF.Sqrt((p.X - b.Cx) * (p.X - b.Cx) + (p.Z - b.Cz) * (p.Z - b.Cz));
            float radius = h <= 1f ? 0.15f : 0.8f * (3.5f - h) / 2.5f;
            best = MathF.Min(best, rr - radius);
        }
        return best;
    }

    private static (Vector3 O, Vector3 D) Ray(Random rng, World w, Vector3 target)
    {
        float zoom = 20f + (float)rng.NextDouble() * 40f;
        // The camera's focus somewhere round the target (the target off-centre on screen), 55 degree pitch, from the south.
        float fx = target.X + ((float)rng.NextDouble() - 0.5f) * 0.6f * zoom, fz = target.Z + ((float)rng.NextDouble() - 0.5f) * 0.5f * zoom;
        float fy = TerrainHeight.At(w.Heightmap, Math.Clamp(fx, 0.1f, w.Heightmap.Width * 2f - 0.1f), Math.Clamp(fz, 0.1f, w.Heightmap.Height * 2f - 0.1f));
        var o = new Vector3(fx, fy + zoom * MathF.Sin(Pitch), fz + zoom * MathF.Cos(Pitch));
        return (o, target - o);
    }

    private enum Family { General, ForestBand, MineEdges, CliffLip }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(21UL)]
    [InlineData(42UL)]
    public void DrawnPick_AgainstExactMeshes_NeverTakesGroundForANode(ulong seed)
    {
        (int groundAsNode, int nodeAsGround, int wrongNode, int canopyLost, string report) = Attack(seed, ResourcePickerTests.Shape, 3000);
        _out.WriteLine(report);
        Assert.True(groundAsNode == 0 && nodeAsGround == 0 && canopyLost == 0 && wrongNode == 0, report);
    }

    // The reference can see BUG-0125's kind of error: a pick told the canopy is 1.25 x its drawn radius (CanopyFill 1.0)
    // and the mine's gold block full-size takes open ground for a node, and the reference counts it.
    [Fact]
    public void Reference_SeesAnOversizedShape()
    {
        PropShape fat = ResourcePickerTests.Shape with { CanopyFill = 1f, GoldFill = 1f };
        (int groundAsNode, _, int wrongNode, _, string report) = Attack(1, fat, 1000);
        _out.WriteLine(report);
        Assert.True(groundAsNode > 100, report);
    }

    private (int GroundAsNode, int NodeAsGround, int WrongNode, int CanopyLost, string Report) Attack(ulong seed, PropShape shape, int perFamily)
    {
        Simulation sim = World(seed);
        World w = sim.World;
        ulong hash = sim.StateHash();
        List<Node> all = Nodes(w);
        var byCell = new Dictionary<(int, int), Node>();
        foreach (Node b in all) byCell[((int)(b.X0 / 2f), (int)(b.Z0 / 2f))] = b;
        List<Node> trees = all.FindAll(b => !b.Gold), mines = all.FindAll(b => b.Gold);
        // A tree with a tree behind it (north = -z, away from the camera).
        List<Node> fronts = trees.FindAll(b => byCell.TryGetValue(((int)(b.X0 / 2f), (int)(b.Z0 / 2f) - 1), out Node? t) && !t.Gold);
        // Nodes by a cliff lip: up to 24 with a ground-height step of 1 m or more 2-6 m from their centre (mapgen keeps
        // nodes off cliffs, so there are few).
        float Step(Node b)
        {
            float worst = 0f;
            for (int k = 0; k < 16; k++)
                for (float rad = 2f; rad <= 6f; rad += 1f)
                {
                    float a = k * MathF.PI / 8f, x = b.Cx + MathF.Cos(a) * rad, z = b.Cz + MathF.Sin(a) * rad;
                    if (x < 0 || z < 0 || x >= w.Heightmap.Width * 2f || z >= w.Heightmap.Height * 2f) continue;
                    worst = MathF.Max(worst, MathF.Abs(TerrainHeight.At(w.Heightmap, x, z) - b.BaseY));
                }
            return worst;
        }
        List<Node> lips = all.Where(b => Step(b) >= 1f).OrderByDescending(Step).ThenBy(b => b.Index).Take(24).ToList();
        float lipStep = lips.Count > 0 ? lips.Min(Step) : 0f;
        Assert.True(fronts.Count > 20 && mines.Count > 2 && lips.Count >= 2, $"coverage: {fronts.Count} forest pairs, {mines.Count} mines, {lips.Count} lip nodes");
        ResourceStore r = w.Resources;
        var rng = new Random((int)seed * 977 + 3);
        var report = new System.Text.StringBuilder();
        int totalGroundAsNode = 0, totalNodeAsGround = 0, totalWrongNode = 0, totalCanopyLost = 0;
        foreach (Family fam in Enum.GetValues<Family>())
        {
            int rays = 0, ties = 0, involved = 0, groundAsNode = 0, slivers = 0, nodeAsGround = 0, wrongNode = 0, canopyRays = 0, canopyLost = 0;
            string first = "";
            for (int k = 0; k < perFamily; k++)
            {
                Vector3 target;
                Node aim;
                switch (fam)
                {
                    case Family.General:
                        aim = all[rng.Next(all.Count)];
                        target = new Vector3(aim.Cx + ((float)rng.NextDouble() - 0.5f) * 8f, 0f, aim.Cz + ((float)rng.NextDouble() - 0.5f) * 8f);
                        target.Y = TerrainHeight.At(w.Heightmap, Math.Clamp(target.X, 0.1f, w.Heightmap.Width * 2f - 0.1f), Math.Clamp(target.Z, 0.1f, w.Heightmap.Height * 2f - 0.1f))
                            + (float)rng.NextDouble() * 3.6f;
                        break;
                    case Family.ForestBand:
                        aim = fronts[rng.Next(fronts.Count)];
                        // The front tree's upper canopy and the band just above it, where the back tree shows.
                        target = new Vector3(aim.Cx + ((float)rng.NextDouble() - 0.5f) * 1.8f, aim.BaseY + 2f + (float)rng.NextDouble() * 2.5f, aim.Cz + ((float)rng.NextDouble() - 0.5f) * 1.2f);
                        break;
                    case Family.MineEdges:
                    {
                        aim = mines[rng.Next(mines.Count)];
                        bool goldTop = rng.Next(2) == 0;
                        float hx = goldTop ? (aim.X1 - aim.X0) * 0.25f : (aim.X1 - aim.X0) / 2f, hz = goldTop ? (aim.Z1 - aim.Z0) * 0.25f : (aim.Z1 - aim.Z0) / 2f;
                        float y = aim.BaseY + (goldTop ? 2.1f : 1.6f) + ((float)rng.NextDouble() - 0.5f) * 0.2f;
                        // A point on the block's top rim, nudged up to 5 cm either side.
                        float along = ((float)rng.NextDouble() * 2f - 1f), off = ((float)rng.NextDouble() - 0.5f) * 0.1f;
                        target = rng.Next(4) switch
                        {
                            0 => new Vector3(aim.Cx + along * hx, y, aim.Cz - hz + off),
                            1 => new Vector3(aim.Cx + along * hx, y, aim.Cz + hz + off),
                            2 => new Vector3(aim.Cx - hx + off, y, aim.Cz + along * hz),
                            _ => new Vector3(aim.Cx + hx + off, y, aim.Cz + along * hz),
                        };
                        break;
                    }
                    default:
                        aim = lips[rng.Next(lips.Count)];
                        target = new Vector3(aim.Cx + ((float)rng.NextDouble() - 0.5f) * 5f, 0f, aim.Cz + ((float)rng.NextDouble() - 0.5f) * 5f);
                        target.Y = TerrainHeight.At(w.Heightmap, Math.Clamp(target.X, 0.1f, w.Heightmap.Width * 2f - 0.1f), Math.Clamp(target.Z, 0.1f, w.Heightmap.Height * 2f - 0.1f))
                            + (float)rng.NextDouble() * 3.6f;
                        break;
                }
                (Vector3 o, Vector3 d) = Ray(rng, w, target);
                rays++;
                float len = d.Length();
                Vector3 n = d / len;
                bool haveGround = GroundPicker.TryPick(w.Heightmap, o, d, out Vector3 gp);
                float gs = haveGround ? Vector3.Distance(o, gp) : float.PositiveInfinity;
                int groundNode = haveGround ? ResourcePicker.NodeAtPoint(w.NavGrid, w.Data.Resources, r.Alive, r.TypeId, r.Cell, new Vector2(gp.X, gp.Z)) : -1;
                int mesh = -1;
                float ms = float.PositiveInfinity, second = float.PositiveInfinity;
                foreach (Node b in all)
                {
                    if (MathF.Abs(b.Cx - target.X) > 40f || MathF.Abs(b.Cz - target.Z) > 60f) continue;
                    float e = EnterNode(b, o, n);
                    if (e < ms) { second = ms; ms = e; mesh = b.Index; }
                    else if (e < second) second = e;
                }
                if (ms >= gs) mesh = -1;
                if ((mesh >= 0 && (MathF.Abs(ms - gs) < Tie || MathF.Abs(second - ms) < Tie))) { ties++; continue; }
                int truth = mesh >= 0 ? mesh : groundNode;
                int pick = ResourcePicker.PickRay(w.NavGrid, w.Data.Resources, r.Alive, r.TypeId, r.Cell, w.Heightmap, o, d, shape, out float pt);
                int now = pick >= 0 ? pick : groundNode;
                if (truth < 0 && now < 0) continue;
                involved++;
                bool canopy = mesh >= 0 && groundNode != mesh; // a drawn node over ground that isn't its own footprint
                if (canopy) canopyRays++;
                if (now == truth) continue;
                // The pick's circle round a tree's facets: a pick whose tree the exact mesh misses by a sliver.
                Node? pickedTree = pick >= 0 ? all.Find(b => b.Index == pick && !b.Gold) : null;
                float gap = pickedTree != null ? CircleGap(pickedTree, o, n, MathF.Min(gs, pt * len + 0.5f)) : float.PositiveInfinity;
                bool sliver = pickedTree != null && EnterNode(pickedTree, o, n) == float.PositiveInfinity && gap > -Sliver && gap <= 0.005f;
                if (sliver) { slivers++; continue; }
                if (canopy) canopyLost++;
                if (truth < 0) groundAsNode++;
                else if (now < 0) nodeAsGround++;
                else wrongNode++;
                if (first.Length == 0) first = $" first: origin {o} dir {d}: truth {truth} (mesh {mesh} at {ms:0.###}, ground node {groundNode} at {gs:0.###}), pick {pick} (t {pt * len:0.###})";
            }
            totalGroundAsNode += groundAsNode;
            totalNodeAsGround += nodeAsGround;
            totalWrongNode += wrongNode;
            totalCanopyLost += canopyLost;
            report.AppendLine($"seed {seed} {fam}{(fam == Family.CliffLip ? $" ({lips.Count} nodes, height step >= {lipStep:0.0} m)" : "")}: {rays} rays, {ties} ties skipped, {involved} with a node; open ground taken for a node {groundAsNode}, " +
                $"facet slivers {slivers}, a drawn node taken for ground {nodeAsGround}, wrong node {wrongNode}; canopy over other ground {canopyRays}, not a Gather of it {canopyLost}{first}");
        }
        Assert.Equal(hash, sim.StateHash()); // read-only
        return (totalGroundAsNode, totalNodeAsGround, totalWrongNode, totalCanopyLost, report.ToString());
    }
}
