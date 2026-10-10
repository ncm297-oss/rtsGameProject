using System;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Abilities;

/// <summary>
/// Unit passives (M4-4b-3a, docs/03 "Implementation (M4-4b-3a)"): the <c>whenBelowHp</c> pass in tick phase 5, and the
/// <c>onDeath</c> trigger in the death path (<see cref="Combat.CombatSystem.HitUnit"/>), whatever phase the death is in.
/// </summary>
/// <remarks>
/// Cost rule: a unit whose type has no passive is never visited. The phase-5 pass returns at once when no live unit's
/// type has a <c>whenBelowHp</c> passive (<see cref="UnitStore.BelowHpUnits"/>), and otherwise looks only at the slots
/// counted at spawn; <c>onDeath</c> work happens only when such a unit dies. No allocation: the death's neighbour query
/// goes into movement's second scratch (<see cref="World.ShoveNeighbors"/>, free outside phases 8-9).
/// </remarks>
public static class PassiveSystem
{
    /// <summary>
    /// The duration a <c>whenBelowHp</c> status is applied with each phase 5, before the statuses count down: 1 tick is
    /// left after the countdown, so it is in force until the next phase 5 checks the hit points again. A rule of the
    /// mechanism, not a gameplay number.
    /// </summary>
    internal const int HeldTicks = 2;

    // The spatial hash holds positions from this tick's phase 1; a unit has walked at most a step since.
    private const float QuerySlack = 0.5f;

    /// <summary>
    /// Phase 5, before the statuses count down: every live unit whose type has a <c>whenBelowHp</c> passive and whose hit
    /// points are below its fraction of the unit's maximum (the type's <c>hp</c> plus its owner's hp techs) takes the
    /// passive's status for <see cref="HeldTicks"/> by the stacking rule (never shortening a longer entry). Slot order.
    /// </summary>
    public static void Run(World world)
    {
        UnitStore u = world.Units;
        if (u.BelowHpUnits == 0) return;
        GameData data = world.Data;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.HasBelowHpPassive(i) || !u.Alive[i]) continue;
            UnitDef def = data.Units[u.TypeId[i]];
            UnitPassive p = def.Passives[def.BelowHpPassive];
            if (!IsBelow(u.Hp[i], MaxHp(world, i), p.HpFraction)) continue;
            StatusSystem.Apply(world, i, p.Status, p.Magnitude, HeldTicks, u.Owner[i]);
        }
    }

    /// <summary>
    /// The boundary rule (docs/03 "Implementation (M4-4b-3a)"): <paramref name="hp"/> is below <paramref name="fraction"/>
    /// of <paramref name="maxHp"/> when <c>hp &lt; fraction x maxHp</c> in single precision; exactly at the fraction is not
    /// below (a Zealot at 31 / 63 is, at 32 / 63 is not: 31.5 is the line).
    /// </summary>
    internal static bool IsBelow(int hp, int maxHp, float fraction) => hp < fraction * maxHp;

    /// <summary>Unit slot <paramref name="i"/>'s maximum hit points: its type's <c>hp</c> plus its owner's hp tech bonus for its type (whole points).</summary>
    internal static int MaxHp(World world, int i)
    {
        UnitStore u = world.Units;
        return world.Data.Units[u.TypeId[i]].Hp + Combat.DamageCalc.Points(world.Techs.Bonus(u.Owner[i], u.TypeId[i], TechStat.Hp));
    }

    /// <summary>
    /// Unit slot <paramref name="v"/> (live, its hit points at 0, about to be freed) dies and its type has an <c>onDeath</c>
    /// passive: every other live unit of its owner whose type is in the passive's list and whose center is within the
    /// radius of <paramref name="v"/>'s position (the edge counts) takes the status for its duration by the stacking rule,
    /// the owner as source. Units dying later in the same tick are still live here and take it; ones freed earlier don't.
    /// Hash order; the exact distance test decides.
    /// </summary>
    internal static void OnDeath(World world, int v)
    {
        UnitStore u = world.Units;
        UnitDef def = world.Data.Units[u.TypeId[v]];
        UnitPassive p = def.Passives[def.DeathPassive];
        int owner = u.Owner[v];
        Vector2 at = u.Position[v];
        float r2 = p.Radius * p.Radius;
        int[] found = world.ShoveNeighbors; // movement's second scratch: free outside phases 8-9
        int n = Math.Min(world.Spatial.QueryRadius(at, p.Radius + world.MaxUnitSpeed + QuerySlack, found), found.Length);
        for (int e = 0; e < n; e++)
        {
            int j = found[e];
            if (j == v || !u.Alive[j] || u.Owner[j] != owner || Vector2.DistanceSquared(u.Position[j], at) > r2) continue;
            if (!Listed(p, u.TypeId[j])) continue;
            StatusSystem.Apply(world, j, p.Status, p.Magnitude, p.DurationTicks, owner);
        }
    }

    private static bool Listed(UnitPassive p, int type)
    {
        for (int k = 0; k < p.Units.Length; k++)
            if (p.Units[k] == type) return true;
        return false;
    }
}
