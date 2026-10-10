using System;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Abilities;

/// <summary>
/// Zones (M4-4b-2, docs/03 "Implementation (M4-4b-2)"): made by a resolving ability's <c>createZone</c> effect
/// (<see cref="Create"/>, phase 6), counted down and applied to the units inside in tick phase 5, after the statuses
/// counted down (<see cref="Run"/>). The vision half (a zone that blocks vision) is the fog update's and the target rule's
/// (<see cref="Vision.FogStore"/>, <see cref="Vision.VisionSystem"/>).
/// </summary>
/// <remarks>
/// A zone lives its ability's duration counting the tick it was made in: made in tick T's phase 6 with 240 ticks, it applies
/// its statuses in T and in every phase 5 from T + 1 to T + 239, and is freed in T + 240's phase 5. Each application
/// gives every unit inside that the ability's <c>affects</c> allows (relative to the zone's owner) each of the zone's
/// statuses by the stacking rule, which refreshes their duration. Applied after the statuses counted down, so a unit inside
/// ends every tick with the full duration (as an <c>applyStatus</c> leaves it) and keeps the status exactly that long after
/// the last tick it was inside: Sandstorm's 1 s statuses end on the 20th tick after it.
/// "Inside" is the unit's center within the radius (the edge counts), through the spatial hash. No allocation.
/// </remarks>
public static class ZoneSystem
{
    // The spatial hash holds positions from this tick's phase 1; nobody has walked since, but a little slack costs nothing.
    private const float QuerySlack = 0.5f;

    /// <summary>Phase 5, after the statuses counted down: every live zone counts down (freed when its time is up) or applies its statuses.</summary>
    public static void Run(World world)
    {
        ZoneStore z = world.Zones;
        if (z.Count == 0) return;
        ReadOnlySpan<bool> alive = z.Alive;
        for (int k = 0; k < z.End; k++)
        {
            if (!alive[k]) continue;
            if (!z.CountDown(k))
            {
                z.Free(k);
                continue;
            }
            Apply(world, k);
        }
    }

    /// <summary>
    /// The zone of <paramref name="ability"/> cast by <paramref name="owner"/> at <paramref name="point"/> (its resolve,
    /// phase 6): made in the lowest free slot and applied at once. A full store makes none (the cast still resolves and its
    /// cooldown starts; docs/03 "Implementation (M4-4b-2)").
    /// </summary>
    internal static void Create(World world, int owner, AbilityDef ability, Vector2 point)
    {
        int k = world.Zones.Add(owner, ability, point);
        if (k >= 0) Apply(world, k);
    }

    /// <summary>Live zone <paramref name="k"/>'s statuses on every live unit inside that its ability's <c>affects</c> allows, in hash order.</summary>
    private static void Apply(World world, int k)
    {
        ZoneStore z = world.Zones;
        UnitStore u = world.Units;
        AbilityDef a = world.Data.Abilities[z.AbilityId[k]];
        AbilityEffect fx = a.Effects[a.ZoneEffect];
        int owner = z.Owner[k];
        Vector2 center = z.Center[k];
        int[] found = world.Neighbors; // movement's scratch: free in phases 5 and 6
        int n = Math.Min(world.Spatial.QueryRadius(center, z.Radius(k) + QuerySlack, found), found.Length);
        for (int e = 0; e < n; e++)
        {
            int j = found[e];
            if (!u.Alive[j] || !z.Contains(k, u.Position[j])) continue;
            bool own = u.Owner[j] == owner;
            bool affected = a.Affects == AbilityAffects.EnemyUnits ? !own : a.Affects != AbilityAffects.OwnUnits || own;
            if (!affected) continue;
            foreach (ZoneStatus s in fx.ZoneStatuses)
                StatusSystem.Apply(world, j, s.Status, s.Magnitude, s.DurationTicks, owner);
        }
    }
}
