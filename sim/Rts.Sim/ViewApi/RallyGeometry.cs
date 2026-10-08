using System;
using System.Numerics;

namespace Rts.Sim.ViewApi;

/// <summary>Where the rally line starts (M3-V3): the point on a building's footprint edge facing its rally point. Pure.</summary>
/// <remarks>Ground coordinates in meters (sim x, y). The line runs from <see cref="EdgePoint"/> to the rally point; a rally point inside the footprint has no line.</remarks>
public static class RallyGeometry
{
    /// <summary>
    /// Where the segment from the centre of the rectangle [<paramref name="min"/>, <paramref name="max"/>] to
    /// <paramref name="target"/> leaves the rectangle; <paramref name="target"/> itself when it is inside (or on the edge),
    /// and the centre for non-finite input.
    /// </summary>
    public static Vector2 EdgePoint(Vector2 min, Vector2 max, Vector2 target)
    {
        Vector2 c = (min + max) / 2f;
        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y)) return c;
        if (target.X >= min.X && target.X <= max.X && target.Y >= min.Y && target.Y <= max.Y) return target;
        Vector2 d = target - c;
        Vector2 half = (max - min) / 2f;
        // The smallest share of d that reaches a side: x sides at half.X / |d.X|, y sides at half.Y / |d.Y|.
        float s = 1f;
        if (d.X != 0f) s = Math.Min(s, half.X / Math.Abs(d.X));
        if (d.Y != 0f) s = Math.Min(s, half.Y / Math.Abs(d.Y));
        return c + d * s;
    }

    /// <summary>The rally line: from <see cref="EdgePoint"/> to <paramref name="target"/>; returns its length in meters (0: no line to draw).</summary>
    public static float Line(Vector2 min, Vector2 max, Vector2 target, out Vector2 from)
    {
        from = EdgePoint(min, max, target);
        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y)) return 0f;
        return Vector2.Distance(from, target);
    }
}
