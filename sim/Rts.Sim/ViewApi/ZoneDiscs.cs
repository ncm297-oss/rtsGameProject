using System;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Map;
using Rts.Sim.Vision;

namespace Rts.Sim.ViewApi;

/// <summary>
/// Which zones the view draws as discs on the ground for a player (M4-V6c, docs/03 "Implementation (M4-V6c)"). A player's
/// own zone is always drawn. Another player's zone is drawn only where the player may see it (docs/02 "Zones": a hiding
/// zone is seen into only by its owner and by units inside it; from outside, the storm itself is seen where sight reaches
/// it): when a cell whose centre lies within the zone's radius plus <see cref="RimWidth"/> is visible to the player at the
/// last fog update. That covers an own unit inside (the cells it sees from inside are visible) and sight reaching the rim
/// ring outside. Explored-only ground shows nothing (explored fog shows remembered terrain, not live effects). Read-only
/// on the <see cref="ZoneStore"/> (only <c>Alive</c>, <c>Owner</c>, <c>Center</c>, <c>Radius</c>, <c>TicksRemaining</c>)
/// and the fog's visibility bytes; allocation-free.
/// </summary>
public static class ZoneDiscs
{
    /// <summary>A disc's opacity until its zone's last <see cref="FadeTicks"/> ticks.</summary>
    public const float DiscAlpha = 0.35f;

    /// <summary>A disc fades linearly over its zone's last second (20 ticks at 20 Hz).</summary>
    public const int FadeTicks = 20;

    /// <summary>How far past a zone's radius (m) a visible cell centre still shows the storm: its rim seen from outside.</summary>
    public const float RimWidth = 2f;

    /// <summary>
    /// Writes a <see cref="ZoneDisc"/> for every live zone <paramref name="player"/> may see into <paramref name="discs"/>,
    /// slots ascending, and returns how many (at most its length; <see cref="ZoneStore.Capacity"/> holds every case).
    /// </summary>
    /// <param name="zones">The world's zones (<c>World.Zones</c>).</param>
    /// <param name="fog">The world's fog (<c>World.Fog</c>): <paramref name="player"/>'s visibility bytes.</param>
    /// <param name="player">The viewing player (the local player).</param>
    /// <param name="fogOn">False with <c>--no-fog</c>: every live zone is drawn.</param>
    /// <param name="discs">Output.</param>
    public static int Collect(ZoneStore zones, FogStore fog, int player, bool fogOn, Span<ZoneDisc> discs)
    {
        ReadOnlySpan<bool> alive = zones.Alive;
        ReadOnlySpan<int> owner = zones.Owner, left = zones.TicksRemaining;
        ReadOnlySpan<Vector2> center = zones.Center;
        ReadOnlySpan<byte> vis = fogOn ? fog.Visibility(player) : ReadOnlySpan<byte>.Empty;
        int live = zones.Count, seen = 0, n = 0;
        for (int k = 0; k < alive.Length && seen < live && n < discs.Length; k++)
        {
            if (!alive[k]) continue;
            seen++;
            bool own = owner[k] == player;
            float r = zones.Radius(k);
            if (fogOn && !own && !SeesStorm(vis, fog.Width, fog.Height, center[k], r)) continue;
            discs[n++] = new ZoneDisc(k, center[k], r, Alpha(left[k]), own);
        }
        return n;
    }

    /// <summary>
    /// A disc's opacity for a zone with <paramref name="ticksRemaining"/> ticks left (between ticks: 1 on its last tick):
    /// <see cref="DiscAlpha"/> above <see cref="FadeTicks"/>, then falling linearly to 0 at 1, one step a tick.
    /// </summary>
    public static float Alpha(int ticksRemaining)
    {
        if (ticksRemaining > FadeTicks) return DiscAlpha;
        if (ticksRemaining <= 1) return 0f;
        return DiscAlpha * (ticksRemaining - 1) / FadeTicks;
    }

    /// <summary>
    /// Whether any cell whose centre is within <paramref name="radius"/> + <see cref="RimWidth"/> of
    /// <paramref name="center"/> is visible in <paramref name="vis"/> (a <paramref name="width"/> x
    /// <paramref name="height"/> row-major fog grid). False for a grid of the wrong size (no such player).
    /// </summary>
    public static bool SeesStorm(ReadOnlySpan<byte> vis, int width, int height, Vector2 center, float radius)
    {
        if (vis.Length != width * height || width <= 0) return false;
        float reach = radius + RimWidth, reach2 = reach * reach;
        const float cell = MapConstants.CellSize;
        int x0 = Math.Max(0, (int)MathF.Floor((center.X - reach) / cell));
        int y0 = Math.Max(0, (int)MathF.Floor((center.Y - reach) / cell));
        int x1 = Math.Min(width - 1, (int)MathF.Floor((center.X + reach) / cell));
        int y1 = Math.Min(height - 1, (int)MathF.Floor((center.Y + reach) / cell));
        for (int y = y0; y <= y1; y++)
        {
            float dy = (y + 0.5f) * cell - center.Y;
            int row = y * width;
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + 0.5f) * cell - center.X;
                if (dx * dx + dy * dy <= reach2 && vis[row + x] == VisionConstants.Visible) return true;
            }
        }
        return false;
    }
}
