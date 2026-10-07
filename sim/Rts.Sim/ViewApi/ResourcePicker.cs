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

    /// <summary>As <see cref="NodeAt"/> for a ground point in meters (sim x, y); -1 off the map or for a non-finite point.</summary>
    public static int NodeAtPoint(NavGrid grid, ImmutableArray<ResourceDef> defs, ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId,
        ReadOnlySpan<int> anchorCell, Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) return -1;
        return grid.WorldToCell(point, out int x, out int y) ? NodeAt(grid, defs, alive, typeId, anchorCell, y * grid.Width + x) : -1;
    }
}
