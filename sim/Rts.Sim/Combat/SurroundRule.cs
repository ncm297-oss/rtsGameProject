using System;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Combat;

/// <summary>
/// Pathing slice 1 (docs/03 "Implementation (pathing slice 1)"): where a melee attacker stands round a building it chases.
/// Each live building has a ring of standing spots, <see cref="CombatConstants.SurroundSpotsPerCell"/> per footprint cell
/// along each face plus one off each corner, every spot the attacker's radius plus
/// <see cref="CombatConstants.BuildingStandOff"/> out from the footprint (so in the first ring of cells round it, or the
/// second for a body too big for the first). An attacker claims the nearest spot no other own attacker claims or stands
/// on and walks there; it keeps the claim until another own unit stands on it, re-picks then, and a spot freed by a death
/// or a new order is free at once. With every spot held it waits behind them (its old walk to the nearest face point)
/// and takes the first spot that frees on a later re-pick.
/// </summary>
/// <remarks>
/// The spots are walked in one fixed order (south face west to east, north face, west face south to north, east face,
/// then the corners SW, SE, NW, NE), nearest first and the lower index on a tie, never through a dictionary: the pick is
/// deterministic. Claims sit in two arrays sized when the <see cref="World"/> is made (<see cref="World.SurroundClaim"/>,
/// <see cref="World.SurroundHolder"/>) and are checked both ways on read, so a dead, re-ordered or re-targeted holder
/// frees its spot without any clearing pass. A walk of the ring is at most <c>4 x (w + h + 1)</c> spots (36 for a 4x4
/// Town Hall), with one spatial-hash query per spot that would win.
/// </remarks>
internal static class SurroundRule
{
    /// <summary>The most standing spots any building of <paramref name="data"/> has (at least 1).</summary>
    internal static int MaxSpotsFor(GameData data)
    {
        int most = 1;
        foreach (BuildingDef b in data.Buildings) most = Math.Max(most, SpotCount(b.FootprintWidth, b.FootprintHeight));
        return most;
    }

    private static int SpotCount(int w, int h) => 2 * (w + h) * CombatConstants.SurroundSpotsPerCell + 4;

    /// <summary>
    /// Whether unit <paramref name="i"/>'s chase follows the rule: it chases a live building (not a remembered one) with an
    /// attack too short to strike from beyond the first ring of cells round it. Ranged attackers stop far out, where a
    /// ring spot is no help and a claim would only keep a melee unit out.
    /// </summary>
    internal static bool Applies(World world, int i)
    {
        UnitStore u = world.Units;
        return world.SurroundRuleOn && u.TargetIsBuilding[i] && world.Buildings.IsAlive(u.Target[i])
            && world.Data.Units[u.TypeId[i]].Attack.Range < MapConstants.CellSize;
    }

    /// <summary>
    /// The spot unit <paramref name="i"/> (<see cref="Applies"/>) walks to: the one it claims while no other own unit stands
    /// on it, else the nearest free one, claimed now. False when none is free; <paramref name="full"/> then says whether
    /// own units hold the spots (a queue that a death will open), rather than the ring being blocked ground.
    /// </summary>
    internal static bool TrySpot(World world, int i, out Vector2 point, out bool full)
    {
        UnitStore u = world.Units;
        int b = u.Target[i].Index;
        Ring(world, b, out Vector2 min, out Vector2 max, out int w, out int h);
        int count = SpotCount(w, h), first = b * world.SurroundSpots;
        int claim = world.SurroundClaim[i];
        if (claim >= first && claim < first + count && world.SurroundHolder[claim] == i + 1)
        {
            point = SpotPoint(min, max, w, h, claim - first, u.Radius[i] + CombatConstants.BuildingStandOff);
            full = false;
            // Kept while nobody stands there and its ground is still open (a building placed on it since closes it).
            if (Open(world, point) && !Occupied(world, i, point, min, max)) return true;
        }
        Release(world, i);
        float s = u.Radius[i] + CombatConstants.BuildingStandOff;
        Vector2 pos = u.Position[i];
        int best = -1;
        float bestD2 = float.MaxValue;
        point = default;
        full = false;
        for (int k = 0; k < count; k++)
        {
            Vector2 p = SpotPoint(min, max, w, h, k, s);
            if (!Open(world, p)) continue;
            if (HeldByOther(world, i, first + k)) { full = true; continue; }
            float d2 = Vector2.DistanceSquared(pos, p);
            if (!(d2 < bestD2)) continue;
            // The hash query only for a spot that would win: the best so far is always one nobody stands on.
            if (Occupied(world, i, p, min, max)) { full = true; continue; }
            best = k;
            bestD2 = d2;
            point = p;
        }
        if (best < 0) return false;
        full = false;
        world.SurroundClaim[i] = first + best;
        world.SurroundHolder[first + best] = i + 1;
        return true;
    }

    /// <summary>
    /// Unit <paramref name="i"/> stands striking its building (<see cref="Applies"/>): its claim follows it to the spot it
    /// stands on (a free one within its radius), or is let go, so no spot stays held by a unit striking from elsewhere.
    /// </summary>
    internal static void Planted(World world, int i)
    {
        UnitStore u = world.Units;
        int b = u.Target[i].Index;
        float r = u.Radius[i], s = r + CombatConstants.BuildingStandOff;
        Ring(world, b, out Vector2 min, out Vector2 max, out int w, out int h);
        int count = SpotCount(w, h), first = b * world.SurroundSpots;
        Vector2 pos = u.Position[i];
        int claim = world.SurroundClaim[i];
        if (claim >= first && claim < first + count && world.SurroundHolder[claim] == i + 1
            && Vector2.DistanceSquared(pos, SpotPoint(min, max, w, h, claim - first, s)) <= r * r)
            return;
        Release(world, i);
        int best = -1;
        float bestD2 = r * r;
        for (int k = 0; k < count; k++)
        {
            float d2 = Vector2.DistanceSquared(pos, SpotPoint(min, max, w, h, k, s));
            if (!(d2 <= bestD2) || HeldByOther(world, i, first + k)) continue;
            best = k;
            bestD2 = d2;
        }
        if (best < 0) return;
        world.SurroundClaim[i] = first + best;
        world.SurroundHolder[first + best] = i + 1;
    }

    /// <summary>Whether unit <paramref name="i"/> (<see cref="Applies"/> or not) holds a spot round its building and stands farther than its radius from it.</summary>
    internal static bool WalksToSpot(World world, int i)
    {
        if (!Applies(world, i)) return false;
        UnitStore u = world.Units;
        int b = u.Target[i].Index;
        Ring(world, b, out Vector2 min, out Vector2 max, out int w, out int h);
        int count = SpotCount(w, h), first = b * world.SurroundSpots, claim = world.SurroundClaim[i];
        if (claim < first || claim >= first + count || world.SurroundHolder[claim] != i + 1) return false;
        float r = u.Radius[i];
        return Vector2.DistanceSquared(u.Position[i], SpotPoint(min, max, w, h, claim - first, r + CombatConstants.BuildingStandOff)) > r * r;
    }

    /// <summary>Unit <paramref name="i"/> lets go of the spot it claims, if any.</summary>
    internal static void Release(World world, int i)
    {
        int claim = world.SurroundClaim[i];
        if (claim < 0) return;
        if (world.SurroundHolder[claim] == i + 1) world.SurroundHolder[claim] = 0;
        world.SurroundClaim[i] = -1;
    }

    /// <summary>Whether spot <paramref name="key"/> is claimed by a live unit other than <paramref name="i"/> that still chases the same building under the rule.</summary>
    private static bool HeldByOther(World world, int i, int key)
    {
        int h = world.SurroundHolder[key] - 1;
        if (h < 0 || h == i) return false;
        UnitStore u = world.Units;
        if (!u.Alive[h] || world.SurroundClaim[h] != key || !u.TargetIsBuilding[h]) return false;
        EntityHandle t = u.Target[h], mine = u.Target[i];
        return t.Index == mine.Index && t.Generation == mine.Generation;
    }

    /// <summary>
    /// Whether another unit of <paramref name="i"/>'s owner stands (not walking) on <paramref name="point"/> (centers nearer
    /// than both radii) within <paramref name="i"/>'s reach of the footprint: a spot already taken by one striking it.
    /// </summary>
    private static bool Occupied(World world, int i, Vector2 point, Vector2 min, Vector2 max)
    {
        UnitStore u = world.Units;
        int owner = u.Owner[i];
        float ri = u.Radius[i], reach = world.Data.Units[u.TypeId[i]].Attack.Range;
        int[] found = world.Neighbors; // movement's scratch: free in phase 7
        int n = Math.Min(world.Spatial.QueryRadius(point, ri + world.MaxUnitRadius, found), found.Length);
        for (int k = 0; k < n; k++)
        {
            int j = found[k];
            if (j == i || !u.Alive[j] || u.Owner[j] != owner || u.State[j] == UnitState.Moving) continue;
            float both = ri + u.Radius[j];
            if (!(Vector2.DistanceSquared(point, u.Position[j]) < both * both)) continue;
            Vector2 p = u.Position[j];
            float gap = Vector2.Distance(p, Vector2.Clamp(p, min, max)) - u.Radius[j];
            if (gap <= reach) return true;
        }
        return false;
    }

    /// <summary>Whether <paramref name="p"/> lies on the map in a passable cell (a spot there exists).</summary>
    private static bool Open(World world, Vector2 p) => world.NavGrid.WorldToCell(p, out int x, out int y) && world.NavGrid.IsPassable(x, y);

    /// <summary>Building slot <paramref name="b"/>'s footprint (m) and size in cells.</summary>
    private static void Ring(World world, int b, out Vector2 min, out Vector2 max, out int w, out int h)
    {
        CombatSystem.BuildingRect(world, b, out min, out max);
        BuildingDef def = world.Data.Buildings[world.Buildings.TypeId[b]];
        w = def.FootprintWidth;
        h = def.FootprintHeight;
    }

    /// <summary>Spot <paramref name="k"/> of a <paramref name="w"/> x <paramref name="h"/> footprint, <paramref name="s"/> m out from it (the ring order in the remarks).</summary>
    private static Vector2 SpotPoint(Vector2 min, Vector2 max, int w, int h, int k, float s)
    {
        const float step = MapConstants.CellSize / CombatConstants.SurroundSpotsPerCell;
        int nx = w * CombatConstants.SurroundSpotsPerCell, ny = h * CombatConstants.SurroundSpotsPerCell;
        if (k < nx) return new Vector2(min.X + (k + 0.5f) * step, min.Y - s);
        k -= nx;
        if (k < nx) return new Vector2(min.X + (k + 0.5f) * step, max.Y + s);
        k -= nx;
        if (k < ny) return new Vector2(min.X - s, min.Y + (k + 0.5f) * step);
        k -= ny;
        if (k < ny) return new Vector2(max.X + s, min.Y + (k + 0.5f) * step);
        k -= ny;
        float d = s * 0.70710677f; // the corner spot sits on the diagonal, s from the corner
        return new Vector2((k & 1) == 0 ? min.X - d : max.X + d, k < 2 ? min.Y - d : max.Y + d);
    }
}
