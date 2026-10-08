using System;
using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>Which unit's drawn body lies under a cursor (M4-V2): a ray pick against the placeholder capsules, for the right-click and A + click Attack order. Read-only, allocation-free.</summary>
/// <remarks>
/// A unit is drawn as a vertical capsule of its <c>Radius</c> and height <c>2 x radius + extraHeight</c>
/// (the view's <c>UnitViews.BodyHeight</c>) standing on the terrain at its interpolated position
/// (<c>lerp(PrevPosition, Position, alpha)</c>, alpha clamped to [0, 1], as <c>UnitViews.GroundPoint</c>). The pick is the
/// live unit whose capsule the ray enters first. Terrain occludes as in <see cref="BuildingPicker.PickRay(BuildingStore, System.Collections.Immutable.ImmutableArray{Data.BuildingDef}, NavGrid, Heightmap, int, Vector3, Vector3, float, float, out float)"/>:
/// a capsule entered only after the ray met the ground farther than the unit's radius from its centre is hidden. View
/// coordinates are Y-up meters (X = sim x, Y = elevation, Z = sim y). Every unit is a candidate: the caller decides what
/// an own unit under the cursor means (the controller treats it as no target, so an own unit in front of an enemy hides
/// it, as the player sees it).
/// </remarks>
public static class UnitPicker
{
    /// <summary>The live unit whose drawn capsule the ray meets first, or -1; <paramref name="entry"/> is the ray parameter of the hit (<c>origin + entry * direction</c>; +infinity for -1), comparable with <see cref="BuildingPicker"/>'s.</summary>
    /// <param name="alive">The unit store's <c>Alive</c>.</param>
    /// <param name="prevPosition">The unit store's <c>PrevPosition</c>.</param>
    /// <param name="position">The unit store's <c>Position</c>.</param>
    /// <param name="radius">The unit store's <c>Radius</c>.</param>
    /// <param name="map">Terrain, for each body's base height and for occlusion.</param>
    /// <param name="alpha">The view's interpolation factor between the previous and the current tick (clamped to [0, 1]; NaN reads 1).</param>
    /// <param name="origin">Ray start (the camera), view coordinates.</param>
    /// <param name="direction">Ray direction; need not be normalized.</param>
    /// <param name="extraHeight">Body height above twice the radius (the view's <c>UnitViews.ExtraBodyHeight</c>).</param>
    /// <param name="entry">Ray parameter of the hit.</param>
    public static int PickRay(ReadOnlySpan<bool> alive, ReadOnlySpan<Vector2> prevPosition, ReadOnlySpan<Vector2> position, ReadOnlySpan<float> radius,
        Heightmap map, float alpha, Vector3 origin, Vector3 direction, float extraHeight, out float entry)
    {
        entry = float.PositiveInfinity;
        if (!IsFinite(origin) || !IsFinite(direction) || direction == Vector3.Zero || !float.IsFinite(extraHeight)) return -1;
        float len = direction.Length();
        Vector3 rd = direction / len;
        float a = Math.Clamp(float.IsNaN(alpha) ? 1f : alpha, 0f, 1f);
        float groundT = BuildingPicker.GroundEntry(map, origin, direction, out Vector3 ground);
        float groundM = float.IsPositiveInfinity(groundT) ? float.PositiveInfinity : groundT * len;
        int n = Math.Min(Math.Min(alive.Length, prevPosition.Length), Math.Min(position.Length, radius.Length));
        int best = -1;
        float bestM = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            if (!alive[i]) continue;
            Vector2 p = Vector2.Lerp(prevPosition[i], position[i], a);
            float r = radius[i];
            if (!(r > 0f)) continue;
            float y = TerrainHeight.At(map, p.X, p.Y);
            var bottom = new Vector3(p.X, y + r, p.Y);
            var top = new Vector3(p.X, y + r + Math.Max(0f, extraHeight), p.Y);
            float t = Capsule(origin, rd, bottom, top, r);
            if (!(t >= 0f) || t >= bestM) continue;
            // Behind a ridge: the ray met the ground before the body, and not at the unit's own feet.
            if (t > groundM + Occlusion)
            {
                float dx = ground.X - p.X, dz = ground.Z - p.Y;
                if (dx * dx + dz * dz > r * r) continue;
            }
            bestM = t;
            best = i;
        }
        if (best >= 0) entry = bestM / len;
        return best;
    }

    /// <summary>
    /// What a click means for the Attack order (M4-V2), from three ray picks of the same ray: the unit
    /// (<see cref="PickRay"/>), the building (<see cref="BuildingPicker"/>, any owner) and the resource prop
    /// (<see cref="ResourcePicker"/>) with their entry parameters. The nearest of the three wins; true when it is a live
    /// unit or building of another player than <paramref name="localPlayer"/> (its handle in <paramref name="target"/>,
    /// <paramref name="isBuilding"/> for a building). An own unit or building, or a prop, in front means no target. Ties go to
    /// the unit, then the building. The unit spans are the unit store's <c>Alive</c>, <c>Owner</c> and <c>Generation</c>.
    /// </summary>
    public static bool ResolveEnemy(ReadOnlySpan<bool> unitAlive, ReadOnlySpan<int> unitOwner, ReadOnlySpan<int> unitGeneration, int unit, float unitT,
        BuildingStore buildings, int building, float buildingT, float nodeT, int localPlayer, out EntityHandle target, out bool isBuilding)
    {
        target = default;
        isBuilding = false;
        if ((uint)unit < (uint)unitAlive.Length && unit < unitOwner.Length && unit < unitGeneration.Length && unitAlive[unit] && unitT <= buildingT && unitT <= nodeT)
        {
            if (unitOwner[unit] == localPlayer) return false;
            target = new EntityHandle(unit, unitGeneration[unit]);
            return true;
        }
        if ((uint)building < (uint)buildings.Capacity && buildings.Alive[building] && buildingT <= nodeT)
        {
            if (buildings.Owner[building] == localPlayer) return false;
            target = new EntityHandle(building, buildings.Generation[building]);
            isBuilding = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Distance along a unit ray (<paramref name="rd"/> normalized) from <paramref name="ro"/> to where it enters the capsule
    /// round segment <paramref name="pa"/>-<paramref name="pb"/> of radius <paramref name="r"/>; negative when it misses
    /// (or starts inside, which a camera never does).
    /// </summary>
    public static float Capsule(Vector3 ro, Vector3 rd, Vector3 pa, Vector3 pb, float r)
    {
        Vector3 ba = pb - pa, oa = ro - pa;
        float baba = Vector3.Dot(ba, ba), bard = Vector3.Dot(ba, rd), baoa = Vector3.Dot(ba, oa), rdoa = Vector3.Dot(rd, oa), oaoa = Vector3.Dot(oa, oa);
        float best = -1f;
        float qa = baba - bard * bard;
        // The side: only when the ray isn't parallel to the axis (a vertical ray meets a cap first anyway).
        if (baba > 0f && qa > 1e-9f * baba)
        {
            float qb = baba * rdoa - baoa * bard;
            float qc = baba * oaoa - baoa * baoa - r * r * baba;
            float h = qb * qb - qa * qc;
            if (h >= 0f)
            {
                float t = (-qb - MathF.Sqrt(h)) / qa;
                float along = baoa + t * bard;
                if (t >= 0f && along > 0f && along < baba) best = t;
            }
        }
        // The caps (spheres at the ends); the nearest entry wins.
        best = Nearer(best, Sphere(ro, rd, pa, r));
        best = Nearer(best, Sphere(ro, rd, pb, r));
        return best;
    }

    private static float Sphere(Vector3 ro, Vector3 rd, Vector3 c, float r)
    {
        Vector3 oc = ro - c;
        float b = Vector3.Dot(rd, oc), cc = Vector3.Dot(oc, oc) - r * r;
        float h = b * b - cc;
        if (h < 0f) return -1f;
        float t = -b - MathF.Sqrt(h);
        return t >= 0f ? t : -1f;
    }

    private static float Nearer(float a, float b) => a < 0f ? b : b < 0f ? a : Math.Min(a, b);

    // Slack, in meters along the ray, before a ground hit counts as in front of a body (float error at its feet).
    private const float Occlusion = 1e-3f;

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
