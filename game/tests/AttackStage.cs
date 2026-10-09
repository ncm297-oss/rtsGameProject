using System;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

namespace Rts.Game.Tests;

/// <summary>
/// The Attack-order scenes' staging (<c>AttackOrderViewTest</c>, <c>QaV6Test</c>), enqueued as commands. Since M4-3a an
/// explicit Attack on a target its owner doesn't see is dropped (docs/03 "Vision, detection, fog"; BUG-0218), so every
/// target those scenes click is staged inside player 0's sight: Heavy Infantry and Raiders in two-column lines two and
/// three cells either side of the central cell (checked: every Raider within the Heavy Infantry's sight less 1.5 m of an
/// own unit), the Tent's anchor 10 to 16 cells east of the centre (7 to 14 cells behind the Raider lines), and player 0's Billet beside the Tent as its spotter (its centre
/// cell at most 4 cells, 8 m, from a Tent cell: a building sees 12 m, rules.json <c>buildingSight</c>). Without fog (before
/// M4-3a) the same layout serves.
/// </summary>
public static class AttackStage
{
    /// <summary>Furthest a Tent cell may lie from the spotter Billet's centre cell, in cells (8 m, under a building's 12 m sight).</summary>
    public const int SpotterReach = 4;

    /// <summary>Enqueues the staging (applies on the next tick); returns the central cell's centre (m).</summary>
    public static System.Numerics.Vector2 Stage(Simulation sim, GameData data, int perSide)
    {
        NavGrid g = sim.World.NavGrid;
        BuildingStore b = sim.World.Buildings;
        int center = FlowField.NearestPassable(g, g.Height / 2 * g.Width + g.Width / 2);
        int cx = center % g.Width, cy = center / g.Width;
        FlowField field = FlowField.Build(g, center);
        int hi = data.FindUnit("malazan_heavy_infantry"), raider = data.FindUnit("whirlwind_raider");
        BuildingDef billet = data.Buildings[data.FindBuilding("malazan_billet")], tent = data.Buildings[data.FindBuilding("whirlwind_tent")];

        // The Tent: 10 to 16 cells east of the centre, nearest the centre row.
        int tentAnchor = -1;
        float best = float.MaxValue;
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            int x = c % g.Width, y = c / g.Width;
            if (x - cx < 10 || x - cx > 16 || !(field.CostAt(c) <= 30f) || !b.Fits(tent.Id, c)) continue;
            float d = field.CostAt(c) + Math.Abs(y - cy);
            if (d < best) { best = d; tentAnchor = c; }
        }
        if (tentAnchor < 0) throw new InvalidOperationException("no spot for the Tent");
        int tx = tentAnchor % g.Width, ty = tentAnchor / g.Width;

        // The spotter Billet: not touching the Tent (a cell between), its centre cell within SpotterReach of a Tent cell,
        // east of it (away from the fight) where it can be, nearest the Tent's row.
        int billetAnchor = -1;
        best = float.MaxValue;
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            int x = c % g.Width, y = c / g.Width;
            bool apart = x >= tx + tent.FootprintWidth + 1 || x + billet.FootprintWidth + 1 <= tx
                || y >= ty + tent.FootprintHeight + 1 || y + billet.FootprintHeight + 1 <= ty;
            if (!apart || !(field.CostAt(c) <= 40f) || !b.Fits(billet.Id, c)) continue;
            int mx = x + billet.FootprintWidth / 2, my = y + billet.FootprintHeight / 2;
            int gx = Math.Max(0, Math.Max(tx - mx, mx - (tx + tent.FootprintWidth - 1)));
            int gy = Math.Max(0, Math.Max(ty - my, my - (ty + tent.FootprintHeight - 1)));
            if (gx * gx + gy * gy > SpotterReach * SpotterReach) continue;
            float d = (x > tx ? 0f : 100f) + Math.Abs(y - ty) * 2f + Math.Abs(x - tx);
            if (d < best) { best = d; billetAnchor = c; }
        }
        if (billetAnchor < 0) throw new InvalidOperationException("no spot for the spotter Billet beside the Tent");
        sim.Enqueue(Command.SpawnBuilding(1, tent.Id, g.CellCenter(tx, ty)));
        sim.Enqueue(Command.SpawnBuilding(0, billet.Id, g.CellCenter(billetAnchor % g.Width, billetAnchor / g.Width)));

        var own = new System.Numerics.Vector2[perSide];
        var enemy = new System.Numerics.Vector2[perSide];
        for (int p = 0; p < 2; p++)
        {
            int dir = p == 0 ? -1 : 1, placed = 0;
            System.Numerics.Vector2[] at = p == 0 ? own : enemy;
            // A two-column line 2 and 3 cells out, filled from the centre row outwards (the nearer column first), skipping
            // what the seed's terrain blocks: the farthest Raider is 5 cells (10 m) across from a Heavy Infantry.
            var taken = new bool[g.Width * g.Height];
            for (; placed < perSide; placed++)
            {
                int pick = -1, bestD = int.MaxValue;
                for (int y = Math.Max(0, cy - 12); y <= Math.Min(g.Height - 1, cy + 12); y++)
                    for (int x = Math.Max(0, cx - 12); x <= Math.Min(g.Width - 1, cx + 12); x++)
                    {
                        int c = y * g.Width + x, dx = (x - cx) * dir, d = 4 * (y - cy) * (y - cy) + dx;
                        if (dx < 2 || dx > 3 || taken[c] || !(field.CostAt(c) <= 20f) || d >= bestD) continue;
                        bestD = d;
                        pick = c;
                    }
                if (pick < 0) break;
                taken[pick] = true;
                at[placed] = g.CellCenter(pick % g.Width, pick / g.Width);
                sim.Enqueue(Command.SpawnUnit(p, p == 0 ? hi : raider, at[placed]));
            }
            if (placed < perSide) throw new InvalidOperationException($"only {placed} spots for player {p}");
        }
        // Every Raider in sight of an own unit with a 1.5 m margin (cell rounding of the fog's circles, a few ticks of walking).
        float reach = data.Units[hi].Sight - 1.5f;
        foreach (System.Numerics.Vector2 e in enemy)
        {
            float near = float.MaxValue;
            foreach (System.Numerics.Vector2 o in own) near = Math.Min(near, System.Numerics.Vector2.Distance(e, o));
            if (near > reach) throw new InvalidOperationException($"staging: a Raider at {e} is {near:0.0} m from the nearest Heavy Infantry (sight {data.Units[hi].Sight} m); own {string.Join(" ", own)}");
        }
        return g.CellCenter(cx, cy);
    }
}
