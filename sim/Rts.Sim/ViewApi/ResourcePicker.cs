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
    /// The live resource node whose drawn prop the ray meets first, or -1 (M3-V3b): each node is a box over its footprint
    /// from the terrain at its centre up to <paramref name="woodHeight"/> or <paramref name="goldHeight"/> by its kind (the
    /// view's prop heights, passed in), so a click on a tree's canopy means the tree and not the ground behind it. Terrain
    /// occludes as in <see cref="BuildingPicker"/>'s ray pick. <paramref name="entry"/> is the ray parameter where it
    /// enters the picked box (+infinity for -1).
    /// </summary>
    /// <param name="grid">The nav grid (cell indices to coordinates).</param>
    /// <param name="defs">Resource types, for footprints and kinds.</param>
    /// <param name="alive">The node store's <c>Alive</c>.</param>
    /// <param name="typeId">The node store's <c>TypeId</c>.</param>
    /// <param name="anchorCell">The node store's <c>Cell</c> (footprint anchors).</param>
    /// <param name="map">Terrain, for each box's base height.</param>
    /// <param name="origin">Ray start (the camera), view coordinates.</param>
    /// <param name="direction">Ray direction; need not be normalized.</param>
    /// <param name="woodHeight">A wood node's prop height in meters.</param>
    /// <param name="goldHeight">A gold node's prop height in meters.</param>
    /// <param name="entry">Ray parameter of the hit.</param>
    public static int PickRay(NavGrid grid, ImmutableArray<ResourceDef> defs, ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId,
        ReadOnlySpan<int> anchorCell, Heightmap map, Vector3 origin, Vector3 direction, float woodHeight, float goldHeight, out float entry)
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
            float baseY = TerrainHeight.At(map, (x0 + x1) / 2f, (z0 + z1) / 2f);
            float top = baseY + (def.Resource == ResourceKind.Gold ? goldHeight : woodHeight);
            if (!BuildingPicker.Box(origin, direction, x0, x1, baseY, top, z0, z1, bestT, out float t0)) continue;
            if (BuildingPicker.Hidden(t0, groundT, ground, x0, x1, z0, z1)) continue;
            if (t0 < bestT) (bestT, best) = (t0, i);
        }
        if (best >= 0) entry = bestT;
        return best;
    }

    /// <summary>As <see cref="NodeAt"/> for a ground point in meters (sim x, y); -1 off the map or for a non-finite point.</summary>
    public static int NodeAtPoint(NavGrid grid, ImmutableArray<ResourceDef> defs, ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId,
        ReadOnlySpan<int> anchorCell, Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) return -1;
        return grid.WorldToCell(point, out int x, out int y) ? NodeAt(grid, defs, alive, typeId, anchorCell, y * grid.Width + x) : -1;
    }
}
