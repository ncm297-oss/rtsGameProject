using System;
using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>Which building lies under a cursor (M3-V2): the cell lookup for the right-click context order and a ray pick against the drawn boxes for a click. Read-only, allocation-free.</summary>
/// <remarks>
/// <see cref="SlotAt"/> is a thin wrapper round <see cref="BuildingStore.SlotAt"/> that takes meters. <see cref="PickRay"/>
/// tests the camera ray against each live building's placeholder box as the view draws it: the footprint
/// (cells x <see cref="MapConstants.CellSize"/>) standing on the terrain height at its centre, <c>boxHeight</c> tall, or for a
/// site <c>max(siteMinShare, progress)</c> of that. View coordinates are Y-up meters (X = sim x, Y = elevation, Z = sim y).
/// </remarks>
public static class BuildingPicker
{
    /// <summary>The slot of the live building whose footprint covers the ground point (sim x, y in meters), or -1 (off the map, non-finite, or nothing there).</summary>
    public static int SlotAt(BuildingStore buildings, NavGrid grid, Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) return -1;
        return grid.WorldToCell(point, out int x, out int y) ? buildings.SlotAt(x, y) : -1;
    }

    /// <summary>Share of the box height a building's view draws: 1 when finished, <c>max(siteMinShare, Work / WorkNeeded)</c> for a site; 0 for a dead slot.</summary>
    public static float BoxRise(BuildingStore buildings, ImmutableArray<BuildingDef> defs, int slot, float siteMinShare)
    {
        if ((uint)slot >= (uint)buildings.Capacity || !buildings.Alive[slot]) return 0f;
        if (!buildings.UnderConstruction[slot]) return 1f;
        BuildingBars.Of(buildings, defs, slot, out float fill);
        return Math.Max(siteMinShare, fill);
    }

    /// <summary>
    /// The live building of <paramref name="owner"/> (any owner when negative) whose drawn box the ray meets first, or -1.
    /// </summary>
    /// <param name="buildings">The match's building store (read only).</param>
    /// <param name="defs">Building types, for footprints and site progress.</param>
    /// <param name="grid">The nav grid (cell indices to coordinates).</param>
    /// <param name="map">Terrain, for each box's base height (the terrain height at its footprint centre).</param>
    /// <param name="owner">Only this player's buildings, or every building when negative.</param>
    /// <param name="origin">Ray start (the camera), view coordinates.</param>
    /// <param name="direction">Ray direction; need not be normalized.</param>
    /// <param name="boxHeight">A finished building's box height in meters.</param>
    /// <param name="siteMinShare">Smallest share of <paramref name="boxHeight"/> a site's box has.</param>
    public static int PickRay(BuildingStore buildings, ImmutableArray<BuildingDef> defs, NavGrid grid, Heightmap map, int owner,
        Vector3 origin, Vector3 direction, float boxHeight, float siteMinShare)
    {
        if (!IsFinite(origin) || !IsFinite(direction) || direction == Vector3.Zero) return -1;
        const float cs = MapConstants.CellSize;
        int w = grid.Width, best = -1;
        float bestT = float.PositiveInfinity;
        ReadOnlySpan<bool> alive = buildings.Alive;
        for (int i = 0; i < buildings.Capacity; i++)
        {
            if (!alive[i] || (owner >= 0 && buildings.Owner[i] != owner)) continue;
            BuildingDef def = defs[buildings.TypeId[i]];
            int anchor = buildings.Cell[i];
            float x0 = anchor % w * cs, z0 = anchor / w * cs;
            float x1 = x0 + def.FootprintWidth * cs, z1 = z0 + def.FootprintHeight * cs;
            float baseY = TerrainHeight.At(map, (x0 + x1) / 2f, (z0 + z1) / 2f);
            float top = baseY + boxHeight * BoxRise(buildings, defs, i, siteMinShare);
            float t0 = 0f, t1 = bestT;
            if (!Slab(origin.X, direction.X, x0, x1, ref t0, ref t1)) continue;
            if (!Slab(origin.Y, direction.Y, baseY, top, ref t0, ref t1)) continue;
            if (!Slab(origin.Z, direction.Z, z0, z1, ref t0, ref t1)) continue;
            if (t0 < bestT)
            {
                bestT = t0;
                best = i;
            }
        }
        return best;
    }

    // Narrows [t0, t1] to where the ray is between lo and hi on one axis; false when that leaves nothing.
    private static bool Slab(float o, float d, float lo, float hi, ref float t0, ref float t1)
    {
        if (d == 0f) return o >= lo && o <= hi;
        float a = (lo - o) / d, b = (hi - o) / d;
        if (a > b) (a, b) = (b, a);
        if (a > t0) t0 = a;
        if (b < t1) t1 = b;
        return t0 <= t1;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
