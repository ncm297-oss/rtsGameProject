using System;
using System.Numerics;

namespace Rts.Sim.ViewApi;

/// <summary>Screen-space unit picking for click and box selection (docs/03 "Picking without physics").</summary>
/// <remarks>
/// Inputs are parallel spans indexed by unit slot: each unit's projected screen centre in pixels,
/// its projected radius in pixels, and whether it may be picked at all (alive, own, in front of
/// the camera; the caller decides). Pure and allocation-free; NaN centres are never picked.
/// </remarks>
public static class ScreenPicker
{
    /// <summary>Smallest click radius in pixels, so distant or small units stay clickable.</summary>
    public const float MinPickRadiusPx = 12f;

    /// <summary>Mouse travel in pixels from the press at which a click becomes a box drag.</summary>
    public const float DragThresholdPx = 4f;

    /// <summary>True once the cursor has moved far enough from the press to count as a box drag.</summary>
    public static bool IsDrag(Vector2 press, Vector2 now) =>
        Vector2.DistanceSquared(press, now) >= DragThresholdPx * DragThresholdPx;

    /// <summary>The slot clicked at <paramref name="click"/>, or -1.</summary>
    /// <remarks>
    /// A candidate is hit when the click lies within max(its radius, <see cref="MinPickRadiusPx"/>)
    /// of its centre. Among hits the nearest centre wins; an exact tie goes to the lowest slot.
    /// </remarks>
    public static int PickClick(ReadOnlySpan<Vector2> centers, ReadOnlySpan<float> radiiPx, ReadOnlySpan<bool> candidate, Vector2 click)
    {
        int best = -1;
        float bestDistSq = float.PositiveInfinity;
        for (int i = 0; i < candidate.Length; i++)
        {
            if (!candidate[i]) continue;
            float r = MathF.Max(radiiPx[i], MinPickRadiusPx);
            float d = Vector2.DistanceSquared(centers[i], click);
            // Written so NaN fails both tests; strict < keeps the lowest slot on a tie.
            if (d <= r * r && d < bestDistSq)
            {
                best = i;
                bestDistSq = d;
            }
        }
        return best;
    }

    /// <summary>Writes every candidate slot whose centre lies inside the box spanned by two corners (edges inclusive, any corner order) into <paramref name="picked"/>, ascending; returns the count.</summary>
    /// <remarks><paramref name="picked"/> must hold at least as many entries as there are candidates.</remarks>
    public static int PickBox(ReadOnlySpan<Vector2> centers, ReadOnlySpan<bool> candidate, Vector2 cornerA, Vector2 cornerB, Span<int> picked)
    {
        Vector2 lo = Vector2.Min(cornerA, cornerB), hi = Vector2.Max(cornerA, cornerB);
        int n = 0;
        for (int i = 0; i < candidate.Length; i++)
        {
            if (!candidate[i]) continue;
            Vector2 c = centers[i];
            if (c.X >= lo.X && c.X <= hi.X && c.Y >= lo.Y && c.Y <= hi.Y)
                picked[n++] = i;
        }
        return n;
    }
}
