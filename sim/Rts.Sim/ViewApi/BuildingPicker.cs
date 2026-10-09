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
    /// <remarks>
    /// Terrain occludes: a box the ray enters only after it has met the ground (<see cref="GroundPicker.TryPick"/>)
    /// outside that box's footprint is hidden behind a ridge or a cliff and is not picked (a click there means the ground
    /// in front). Ground met inside the footprint (a box on a slope, whose uphill cells rise above its base) doesn't hide it.
    /// </remarks>
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
        Vector3 origin, Vector3 direction, float boxHeight, float siteMinShare) =>
        PickRay(buildings, defs, grid, map, owner, origin, direction, boxHeight, siteMinShare, out _);

    /// <summary>As the overload without <paramref name="entry"/>, also giving the ray parameter where the ray enters the picked box (<c>origin + entry * direction</c>; +infinity for -1), so a caller can compare it with another picker's.</summary>
    /// <param name="buildings">The match's building store (read only).</param>
    /// <param name="defs">Building types, for footprints and site progress.</param>
    /// <param name="grid">The nav grid (cell indices to coordinates).</param>
    /// <param name="map">Terrain, for each box's base height.</param>
    /// <param name="owner">Only this player's buildings, or every building when negative.</param>
    /// <param name="origin">Ray start (the camera), view coordinates.</param>
    /// <param name="direction">Ray direction; need not be normalized.</param>
    /// <param name="boxHeight">A finished building's box height in meters.</param>
    /// <param name="siteMinShare">Smallest share of <paramref name="boxHeight"/> a site's box has.</param>
    /// <param name="entry">Ray parameter of the hit.</param>
    public static int PickRay(BuildingStore buildings, ImmutableArray<BuildingDef> defs, NavGrid grid, Heightmap map, int owner,
        Vector3 origin, Vector3 direction, float boxHeight, float siteMinShare, out float entry) =>
        PickRay(buildings, defs, grid, map, owner, origin, direction, boxHeight, siteMinShare, ReadOnlySpan<bool>.Empty, out entry);

    /// <summary>
    /// As the overload without <paramref name="shown"/>, but only among the slots <paramref name="shown"/> marks (M4-V4: the
    /// buildings the screen draws, <see cref="FogView.BuildingShown"/>; a building hidden by the fog is not under the cursor).
    /// An empty span means every live building.
    /// </summary>
    /// <param name="buildings">The match's building store (read only).</param>
    /// <param name="defs">Building types, for footprints and site progress.</param>
    /// <param name="grid">The nav grid (cell indices to coordinates).</param>
    /// <param name="map">Terrain, for each box's base height.</param>
    /// <param name="owner">Only this player's buildings, or every building when negative.</param>
    /// <param name="origin">Ray start (the camera), view coordinates.</param>
    /// <param name="direction">Ray direction; need not be normalized.</param>
    /// <param name="boxHeight">A finished building's box height in meters.</param>
    /// <param name="siteMinShare">Smallest share of <paramref name="boxHeight"/> a site's box has.</param>
    /// <param name="shown">Per building slot, whether it is drawn (a slot past its end is not); empty for every slot.</param>
    /// <param name="entry">Ray parameter of the hit.</param>
    public static int PickRay(BuildingStore buildings, ImmutableArray<BuildingDef> defs, NavGrid grid, Heightmap map, int owner,
        Vector3 origin, Vector3 direction, float boxHeight, float siteMinShare, ReadOnlySpan<bool> shown, out float entry)
    {
        entry = float.PositiveInfinity;
        if (!IsFinite(origin) || !IsFinite(direction) || direction == Vector3.Zero) return -1;
        const float cs = MapConstants.CellSize;
        int w = grid.Width, best = -1;
        float bestT = float.PositiveInfinity;
        // Where the ray meets the terrain, as a ray parameter; boxes entered beyond it are hidden unless the ground hit
        // is in their own footprint.
        float groundT = GroundEntry(map, origin, direction, out Vector3 ground);
        ReadOnlySpan<bool> alive = buildings.Alive;
        for (int i = 0; i < buildings.Capacity; i++)
        {
            if (!alive[i] || (owner >= 0 && buildings.Owner[i] != owner)) continue;
            if (!shown.IsEmpty && (i >= shown.Length || !shown[i])) continue;
            BuildingDef def = defs[buildings.TypeId[i]];
            int anchor = buildings.Cell[i];
            float x0 = anchor % w * cs, z0 = anchor / w * cs;
            float x1 = x0 + def.FootprintWidth * cs, z1 = z0 + def.FootprintHeight * cs;
            float baseY = TerrainHeight.At(map, (x0 + x1) / 2f, (z0 + z1) / 2f);
            float top = baseY + boxHeight * BoxRise(buildings, defs, i, siteMinShare);
            if (!Box(origin, direction, x0, x1, baseY, top, z0, z1, bestT, out float t0)) continue;
            if (Hidden(t0, groundT, ground, x0, x1, z0, z1)) continue;
            if (t0 < bestT)
            {
                bestT = t0;
                best = i;
            }
        }
        if (best >= 0) entry = bestT;
        return best;
    }

    // Ray parameter where the ray meets the terrain (+infinity if it misses the map) and the hit point. Shared with
    // ResourcePicker.PickRay.
    internal static float GroundEntry(Heightmap map, Vector3 origin, Vector3 direction, out Vector3 ground) =>
        GroundPicker.TryPick(map, origin, direction, out ground) ? Vector3.Dot(ground - origin, direction) / direction.LengthSquared() : float.PositiveInfinity;

    // True when a box over [x0, x1] x [z0, z1] entered at t0 lies behind the ground met at groundT outside that footprint.
    internal static bool Hidden(float t0, float groundT, Vector3 ground, float x0, float x1, float z0, float z1) =>
        t0 > groundT + Occlusion && !(ground.X >= x0 && ground.X <= x1 && ground.Z >= z0 && ground.Z <= z1);

    // Where the ray (from t = 0, up to tMax) enters the axis-aligned box; false if it misses it.
    internal static bool Box(Vector3 o, Vector3 d, float x0, float x1, float y0, float y1, float z0, float z1, float tMax, out float t0)
    {
        t0 = 0f;
        float t1 = tMax;
        return Slab(o.X, d.X, x0, x1, ref t0, ref t1) && Slab(o.Y, d.Y, y0, y1, ref t0, ref t1) && Slab(o.Z, d.Z, z0, z1, ref t0, ref t1);
    }

    // Ray-parameter slack before the ground hit counts as in front of a box (float error at a box's base edge).
    private const float Occlusion = 1e-3f;

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
