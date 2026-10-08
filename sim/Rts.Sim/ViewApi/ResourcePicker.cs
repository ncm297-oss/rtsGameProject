using System;
using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>Which resource node lies under a cursor's ground point, for the right-click Gather (M3-V1). Read-only, allocation-free.</summary>
/// <remarks>
/// Takes the node store's spans (<c>Alive</c>, <c>TypeId</c>, <c>Cell</c>) like <see cref="PropLayout"/>. The store has
/// no cell lookup, so a cell first has to carry <see cref="NavFlags.Resource"/> in the nav grid (only a live node's
/// footprint cells do); only then are the live nodes scanned for the one whose footprint covers it. A click on open
/// ground so costs one flag read, a click on a node one scan of the store.
/// </remarks>
public static class ResourcePicker
{
    /// <summary>The slot of the live resource node whose footprint covers nav cell <paramref name="cell"/> (<c>y * Width + x</c>), or -1 (off the map, or no node there).</summary>
    /// <param name="grid">The match's nav grid.</param>
    /// <param name="defs">Resource types (<see cref="GameData.Resources"/>), for footprints.</param>
    /// <param name="alive">The node store's <c>Alive</c>.</param>
    /// <param name="typeId">The node store's <c>TypeId</c>.</param>
    /// <param name="anchorCell">The node store's <c>Cell</c> (footprint anchors).</param>
    /// <param name="cell">The cell asked about.</param>
    public static int NodeAt(NavGrid grid, ImmutableArray<ResourceDef> defs, ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId,
        ReadOnlySpan<int> anchorCell, int cell)
    {
        int w = grid.Width;
        if ((uint)cell >= (uint)(w * grid.Height)) return -1;
        int x = cell % w, y = cell / w;
        if ((grid.FlagsAt(x, y) & NavFlags.Resource) == 0) return -1;
        int n = Math.Min(alive.Length, Math.Min(typeId.Length, anchorCell.Length));
        for (int i = 0; i < n; i++)
        {
            if (!alive[i]) continue;
            ResourceDef def = defs[typeId[i]];
            int ax = anchorCell[i] % w, ay = anchorCell[i] / w;
            if (x >= ax && y >= ay && x < ax + def.FootprintWidth && y < ay + def.FootprintHeight) return i;
        }
        return -1;
    }

    /// <summary>
    /// The live resource node whose drawn prop the ray meets first, or -1: since M3-V4 (BUG-0125) each node is the shape the
    /// view draws (<paramref name="shape"/>: a tree's trunk and cone, a mine's block and gold block), no longer a box over its
    /// whole footprint, so a click on a tree's canopy means the tree and a click on open ground beside or behind it means the
    /// ground. Terrain occludes as in <see cref="BuildingPicker"/>'s ray pick. <paramref name="entry"/> is the ray parameter
    /// where it enters the picked shape (+infinity for -1).
    /// </summary>
    /// <remarks>
    /// The cone and trunk are tested as true circles; the view's 7- and 6-sided meshes lie inside them (a facet's middle is
    /// at most 8 cm inside the cone's base circle). Each tree first gets a cheap box test round its drawn shape.
    /// </remarks>
    /// <param name="grid">The nav grid (cell indices to coordinates).</param>
    /// <param name="defs">Resource types, for footprints and kinds.</param>
    /// <param name="alive">The node store's <c>Alive</c>.</param>
    /// <param name="typeId">The node store's <c>TypeId</c>.</param>
    /// <param name="anchorCell">The node store's <c>Cell</c> (footprint anchors).</param>
    /// <param name="map">Terrain, for each prop's base height.</param>
    /// <param name="origin">Ray start (the camera), view coordinates.</param>
    /// <param name="direction">Ray direction; need not be normalized.</param>
    /// <param name="shape">The props' drawn dimensions (the view's <c>PropsView.Shape</c>).</param>
    /// <param name="entry">Ray parameter of the hit.</param>
    public static int PickRay(NavGrid grid, ImmutableArray<ResourceDef> defs, ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId,
        ReadOnlySpan<int> anchorCell, Heightmap map, Vector3 origin, Vector3 direction, in PropShape shape, out float entry)
    {
        entry = float.PositiveInfinity;
        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || !float.IsFinite(origin.Z)
            || !float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z) || direction == Vector3.Zero) return -1;
        const float cs = MapConstants.CellSize;
        float groundT = BuildingPicker.GroundEntry(map, origin, direction, out Vector3 ground);
        int w = grid.Width, best = -1, n = Math.Min(alive.Length, Math.Min(typeId.Length, anchorCell.Length));
        float bestT = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            if (!alive[i] || (uint)typeId[i] >= (uint)defs.Length) continue;
            ResourceDef def = defs[typeId[i]];
            float x0 = anchorCell[i] % w * cs, z0 = anchorCell[i] / w * cs;
            float x1 = x0 + def.FootprintWidth * cs, z1 = z0 + def.FootprintHeight * cs;
            float cx = (x0 + x1) / 2f, cz = (z0 + z1) / 2f;
            float baseY = TerrainHeight.At(map, cx, cz);
            float t0;
            if (def.Resource == ResourceKind.Gold)
            {
                if (!MineEntry(origin, direction, x0, x1, z0, z1, baseY, shape, bestT, out t0)) continue;
            }
            else
            {
                float canopy = Math.Min(def.FootprintWidth, def.FootprintHeight) * cs * shape.CanopyFill / 2f;
                if (!TreeEntry(origin, direction, cx, cz, baseY, canopy, shape, bestT, out t0)) continue;
            }
            if (BuildingPicker.Hidden(t0, groundT, ground, x0, x1, z0, z1)) continue;
            if (t0 < bestT) (bestT, best) = (t0, i);
        }
        if (best >= 0) entry = bestT;
        return best;
    }

    // Where the ray (t in [0, tMax]) enters a mine: the footprint block or the centred gold block on top.
    private static bool MineEntry(Vector3 o, Vector3 d, float x0, float x1, float z0, float z1, float baseY, in PropShape s, float tMax, out float t)
    {
        float top = baseY + s.MineHeight;
        bool hit = BuildingPicker.Box(o, d, x0, x1, baseY, top, z0, z1, tMax, out t);
        float hx = (x1 - x0) * s.GoldFill / 2f, hz = (z1 - z0) * s.GoldFill / 2f, cx = (x0 + x1) / 2f, cz = (z0 + z1) / 2f;
        if (BuildingPicker.Box(o, d, cx - hx, cx + hx, top, top + s.GoldHeight, cz - hz, cz + hz, hit ? t : tMax, out float tg) && (!hit || tg < t))
        {
            t = tg;
            hit = true;
        }
        return hit;
    }

    // Where the ray (t in [0, tMax]) enters a tree: the trunk cylinder up to TrunkHeight, then the cone from the canopy
    // radius at TrunkHeight to a point at TreeHeight, both round (cx, cz).
    private static bool TreeEntry(Vector3 o, Vector3 d, float cx, float cz, float baseY, float canopy, in PropShape s, float tMax, out float t)
    {
        t = 0f;
        float r = Math.Max(canopy, s.TrunkRadius);
        // Cheap reject: the box round the whole drawn tree.
        if (!BuildingPicker.Box(o, d, cx - r, cx + r, baseY, baseY + s.TreeHeight, cz - r, cz + r, tMax, out _)) return false;
        double ox = o.X - (double)cx, oz = o.Z - (double)cz, dx = d.X, dz = d.Z;
        double a = dx * dx + dz * dz, b = 2.0 * (ox * dx + oz * dz), c = ox * ox + oz * oz;
        double best = double.PositiveInfinity;
        float trunkTop = baseY + s.TrunkHeight;
        // Trunk: horizontal distance <= radius, inside its height.
        if (YRange(o.Y, d.Y, baseY, trunkTop, tMax, out double lo, out double hi)
            && FirstInside(a, b, c - (double)s.TrunkRadius * s.TrunkRadius, lo, hi, out double tt)) best = tt;
        // Cone: horizontal distance <= k (tip - y) for y in [trunkTop, tip]. Below the tip that is the lower nappe only, a
        // convex solid, so the ray's points inside it form one interval.
        float tip = baseY + s.TreeHeight;
        if (tip > trunkTop && canopy > 0f && YRange(o.Y, d.Y, trunkTop, tip, tMax, out lo, out hi))
        {
            double k = canopy / (double)(tip - trunkTop), k2 = k * k, h0 = tip - (double)o.Y, dy = d.Y;
            if (FirstInside(a - k2 * dy * dy, b + 2.0 * k2 * h0 * dy, c - k2 * h0 * h0, lo, hi, out double tc) && tc < best) best = tc;
        }
        if (!(best <= tMax)) return false;
        t = (float)best;
        return true;
    }

    // The ray parameters in [0, tMax] where o.y + t d.y lies in [y0, y1]; false when there are none.
    private static bool YRange(float oy, float dy, float y0, float y1, float tMax, out double lo, out double hi)
    {
        lo = 0.0;
        hi = tMax;
        if (dy == 0f) return oy >= y0 && oy <= y1;
        double a = (y0 - (double)oy) / dy, b = (y1 - (double)oy) / dy;
        if (a > b) (a, b) = (b, a);
        if (a > lo) lo = a;
        if (b < hi) hi = b;
        return lo <= hi;
    }

    // The smallest t in [lo, hi] with a t^2 + b t + c <= 0, given those t form one interval there (a convex solid); false
    // when there is none. That interval starts at lo itself or at a root of the quadratic.
    private static bool FirstInside(double a, double b, double c, double lo, double hi, out double t)
    {
        t = lo;
        if ((a * lo + b) * lo + c <= 0.0) return true;
        double r1, r2;
        if (Math.Abs(a) < 1e-12)
        {
            if (b == 0.0) return false;
            r1 = r2 = -c / b;
        }
        else
        {
            double disc = b * b - 4.0 * a * c;
            if (disc < 0.0) return false;
            double sq = Math.Sqrt(disc);
            r1 = (-b - sq) / (2.0 * a);
            r2 = (-b + sq) / (2.0 * a);
            if (r1 > r2) (r1, r2) = (r2, r1);
        }
        if (r1 >= lo && r1 <= hi) t = r1;
        else if (r2 >= lo && r2 <= hi) t = r2;
        else return false;
        return true;
    }

    /// <summary>As <see cref="NodeAt"/> for a ground point in meters (sim x, y); -1 off the map or for a non-finite point.</summary>
    public static int NodeAtPoint(NavGrid grid, ImmutableArray<ResourceDef> defs, ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId,
        ReadOnlySpan<int> anchorCell, Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) return -1;
        return grid.WorldToCell(point, out int x, out int y) ? NodeAt(grid, defs, alive, typeId, anchorCell, y * grid.Width + x) : -1;
    }
}
