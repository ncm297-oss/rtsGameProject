using System;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Combat;

/// <summary>
/// M8-3: the one factor a unit's hit is multiplied by (docs/03 "Implementation (M8-3)"), the product of three in a fixed
/// order: its status damage bonus (M7-4, <see cref="StatusSystem.DamageFactor"/>), its attack's first shot from stealth
/// (<see cref="AttackDef.FromStealthBonus"/> while <see cref="World.IsStealthed"/>), and its type's pack bonus (the
/// <c>nearbyOwn</c> passive). Every place that works out a unit's hit calls <see cref="Of"/> (the melee blow at the strike,
/// a shot and its splash at the landing, a melee splash), so no path can leave one out (BUG-1281's lesson). Read fresh at
/// each hit from hashed state and the data; nothing is stored.
/// </summary>
internal static class HitFactor
{
    /// <summary>
    /// Metres added to the pack's grid query past the radius and a unit's step: the hash holds positions from this tick's
    /// phase 1, and a shove can carry a unit a little further than its own speed (the splash query's slack).
    /// </summary>
    private const float QuerySlack = 1f;

    /// <summary>
    /// The factor live unit slot <paramref name="i"/>'s hit is multiplied by now: <c>status x (1 + fromStealthBonus, when
    /// Stealthed) x (1 + min(n, maxStacks) x damageBonus)</c>, multiplied in that order. On data without either field
    /// (<see cref="World.HitBonusInData"/> false) it is the status factor alone after one flag test.
    /// </summary>
    internal static float Of(World world, int i)
    {
        UnitStore u = world.Units;
        float factor = u.Statuses.Count[i] == 0 ? 1f : StatusSystem.DamageFactor(world, i);
        if (!world.HitBonusInData) return factor;
        UnitDef def = world.Data.Units[u.TypeId[i]];
        // From any stealth (the forest, the stand-still rule, an ability's): the one predicate, Stealthed and not Revealed.
        float stealth = def.Attack.FromStealthBonus;
        if (stealth > 0f && world.IsStealthed(i)) factor *= 1f + stealth;
        if (def.NearbyPassive >= 0) factor *= PackFactor(world, i, def.Passives.ItemRef(def.NearbyPassive));
        return factor;
    }

    /// <summary>The pack factor of <paramref name="p"/> for slot <paramref name="i"/>: <c>1 + min(n, maxStacks) x damageBonus</c> (<see cref="PackCount"/>), exactly 1 with none counted.</summary>
    internal static float PackFactor(World world, int i, in UnitPassive p)
    {
        int n = PackCount(world, i, in p);
        return n == 0 ? 1f : 1f + n * p.DamageBonus;
    }

    /// <summary>
    /// How many of slot <paramref name="i"/>'s owner's other live units of <paramref name="p"/>'s listed types have their
    /// centre within its radius of <paramref name="i"/>'s centre (the edge counts), at most its <c>maxStacks</c>. Through the
    /// spatial grid (positions from phase 1, so the query reaches a step and a slack further) with the exact distance on
    /// the current positions deciding; the grid lists ascending slots and only a count comes out, so the order cannot leak.
    /// An ally's units, enemies, other types, the unit itself and a dead slot never count. Uses movement's second scratch
    /// (<see cref="World.ShoveNeighbors"/>, free outside phases 8-9; every caller reads the factor before a hit is applied,
    /// so no death passive is iterating it).
    /// </summary>
    internal static int PackCount(World world, int i, in UnitPassive p)
    {
        UnitStore u = world.Units;
        int owner = u.Owner[i];
        Vector2 at = u.Position[i];
        float r2 = p.Radius * p.Radius;
        int[] found = world.ShoveNeighbors;
        int n = Math.Min(world.Spatial.QueryRadius(at, p.Radius + world.MaxUnitSpeed + QuerySlack, found), found.Length);
        int count = 0;
        for (int e = 0; e < n; e++)
        {
            int j = found[e];
            if (j == i || !u.Alive[j] || u.Owner[j] != owner || Vector2.DistanceSquared(u.Position[j], at) > r2) continue;
            if (!Listed(p, u.TypeId[j])) continue;
            if (++count == p.MaxStacks) break;
        }
        return count;
    }

    private static bool Listed(in UnitPassive p, int type)
    {
        for (int k = 0; k < p.Units.Length; k++)
            if (p.Units[k] == type) return true;
        return false;
    }
}
