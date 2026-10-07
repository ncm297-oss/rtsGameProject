using System;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>Where the placement ghost stands for a cursor point (M3-V2). Pure, allocation-free.</summary>
/// <remarks>
/// The anchor (the footprint's lowest x, y cell, what <c>Command.Build</c> and <c>World.CanPlace</c> take) is the cell
/// under the cursor minus half the footprint (integer halves), so the box centres on the cursor: exactly for odd
/// footprints, within a cell for even ones. It is clamped so the whole footprint stays on the map: at the edge the ghost
/// stops instead of hanging off it (the border cells are blocked, so it shows red there).
/// </remarks>
public static class PlacementGhost
{
    /// <summary>The anchor cell (<c>y * Width + x</c>) for a footprint of <paramref name="width"/> x <paramref name="height"/> cells centred on the ground point (sim x, y in meters); -1 for a point off the map or non-finite, or a footprint larger than the map.</summary>
    public static int Anchor(NavGrid grid, int width, int height, Vector2 point)
    {
        if (width < 1 || height < 1 || width > grid.Width || height > grid.Height) return -1;
        if (!grid.WorldToCell(point, out int cx, out int cy)) return -1;
        int ax = Math.Clamp(cx - width / 2, 0, grid.Width - width);
        int ay = Math.Clamp(cy - height / 2, 0, grid.Height - height);
        return ay * grid.Width + ax;
    }

    /// <summary>As <see cref="Anchor(NavGrid, int, int, Vector2)"/> for building type <paramref name="def"/>'s footprint.</summary>
    public static int Anchor(NavGrid grid, BuildingDef def, Vector2 point) => Anchor(grid, def.FootprintWidth, def.FootprintHeight, point);

    /// <summary>The point (meters) a <c>Command.Build</c> for <paramref name="anchor"/> carries: the anchor cell's centre (the sim takes the cell holding it).</summary>
    public static Vector2 AnchorPoint(NavGrid grid, int anchor) => grid.CellCenter(anchor % grid.Width, anchor / grid.Width);
}
