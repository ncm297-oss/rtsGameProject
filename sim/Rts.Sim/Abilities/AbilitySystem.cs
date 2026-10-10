using System;
using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Orders;

namespace Rts.Sim.Abilities;

/// <summary>
/// Abilities (M4-4a, docs/03 "Implementation (M4-4a)"): the <c>UseAbility</c> checks and start in phase 1 (and when a queued
/// one is popped in phase 7), and in tick phase 6 every caster's walk into range, cast timer and resolve.
/// </summary>
/// <remarks>
/// A caster out of range walks toward the point (a Move) and starts casting on the first phase 6 it is within
/// <see cref="AbilityDef.Range"/> of it; if it stops walking first (arrived as near as the ground allows, stuck) the cast is
/// dropped with no cooldown. Casting, it stands (<see cref="UnitState.Casting"/>: planted, no scans, its queue waits) for
/// <see cref="AbilityDef.CastTicks"/> ticks counting the tick it starts; any new order cancels the cast with no cooldown.
/// On the last tick the effects land on every unit whose center is within <see cref="AbilityDef.Radius"/> of the point and
/// that <see cref="AbilityDef.Affects"/> allows (M4-4b-1: and other players' buildings, and own units at a fraction, for a
/// damage effect that says so), the cooldown starts (with the owner's <c>abilityCooldown</c> techs), and the caster stands Idle.
/// </remarks>
public static class AbilitySystem
{
    // The spatial hash holds positions from this tick's phase 1; nobody has walked since, but a little slack costs nothing.
    private const float QuerySlack = 0.5f;

    /// <summary>
    /// Whether unit slot <paramref name="i"/> (live) may use ability <paramref name="slot"/> of its type at
    /// <paramref name="point"/> now: the type has that ability, it is off cooldown, and the point is on the map.
    /// </summary>
    internal static bool CanUse(World world, int i, int slot, Vector2 point)
    {
        UnitStore u = world.Units;
        UnitDef def = world.Data.Units[u.TypeId[i]];
        if ((uint)slot >= (uint)def.Abilities.Length) return false;
        if (world.TickNumber < u.AbilityReadyTick[i * DataLimits.MaxUnitAbilities + slot]) return false;
        return world.NavGrid.WorldToCell(point, out _, out _);
    }

    /// <summary>
    /// Ability <paramref name="slot"/> of unit slot <paramref name="i"/>'s type (checked with <see cref="CanUse"/>; the
    /// caller ended the unit's other orders) aimed at <paramref name="point"/>: in range it starts casting now, else it walks.
    /// </summary>
    internal static void Begin(World world, int i, int slot, Vector2 point)
    {
        UnitStore u = world.Units;
        if (u.CastAbility[i] < 0) u.CasterCount++;
        u.CastAbility[i] = slot;
        u.CastPoint[i] = point;
        u.CastTicks[i] = 0;
        AbilityDef a = AbilityOf(world, i);
        if (InRange(u, i, a)) StartCasting(world, i, a);
        else if (!OrderSystem.MoveTo(world, i, point)) Cancel(u, i);
    }

    /// <summary>Ends unit slot <paramref name="i"/>'s cast or walk to one, with no cooldown (a new order, a lost walk); a casting unit stands Idle.</summary>
    internal static void Cancel(UnitStore u, int i)
    {
        if (u.CastAbility[i] < 0) return;
        u.CasterCount--;
        u.CastAbility[i] = -1;
        u.CastTicks[i] = 0;
        u.CastPoint[i] = default;
        if (u.State[i] == UnitState.Casting) u.State[i] = UnitState.Idle;
    }

    /// <summary>Phase 6: every caster in slot order walks into range, counts its cast down, or resolves.</summary>
    public static void Run(World world)
    {
        UnitStore u = world.Units;
        if (u.CasterCount == 0) return;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (u.CastAbility[i] < 0 || !u.Alive[i]) continue;
            AbilityDef a = AbilityOf(world, i);
            if (u.CastTicks[i] == 0)
            {
                if (InRange(u, i, a)) StartCasting(world, i, a);
                else
                {
                    // Stopped short (arrived as near as the ground allows, or stuck): no cast, no cooldown.
                    if (u.State[i] != UnitState.Moving) Cancel(u, i);
                    continue;
                }
            }
            else if (u.State[i] != UnitState.Casting)
            {
                // Something other than an order moved it off its cast: it is lost like a cancelled one.
                Cancel(u, i);
                continue;
            }
            if (--u.CastTicks[i] > 0) continue;
            int slot = u.CastAbility[i];
            Vector2 point = u.CastPoint[i];
            EntityHandle caster = new(i, u.Generation[i]);
            u.CastAbility[i] = -1;
            u.CasterCount--;
            u.CastPoint[i] = default;
            u.State[i] = UnitState.Idle;
            u.AbilityReadyTick[i * DataLimits.MaxUnitAbilities + slot] = world.TickNumber + CooldownOf(world, i, a);
            world.RecordAbilityEvent(new AbilityEvent(caster, u.Owner[i], a.Id, point, true));
            Resolve(world, caster, u.Owner[i], a, point);
        }
    }

    /// <summary>
    /// The cooldown <paramref name="a"/> starts for unit slot <paramref name="i"/> (M4-4b-1): its own ticks plus its owner's
    /// <see cref="TechStat.AbilityCooldown"/> bonus for its type (negative shortens it), at least 1 tick. Read at the resolve,
    /// so a tech finishing while a cooldown runs changes the next one, not that one.
    /// </summary>
    internal static int CooldownOf(World world, int i, AbilityDef a)
    {
        UnitStore u = world.Units;
        float bonus = world.Techs.Bonus(u.Owner[i], u.TypeId[i], TechStat.AbilityCooldown);
        return Math.Max(1, a.CooldownTicks + (int)MathF.Round(bonus, MidpointRounding.AwayFromZero));
    }

    /// <summary>The ability unit slot <paramref name="i"/> is casting (its <see cref="UnitStore.CastAbility"/> entry of its type's list).</summary>
    private static AbilityDef AbilityOf(World world, int i)
    {
        UnitStore u = world.Units;
        return world.Data.Abilities[world.Data.Units[u.TypeId[i]].Abilities[u.CastAbility[i]]];
    }

    private static bool InRange(UnitStore u, int i, AbilityDef a) =>
        Vector2.DistanceSquared(u.Position[i], u.CastPoint[i]) <= a.Range * a.Range;

    /// <summary>Unit slot <paramref name="i"/> stands to cast <paramref name="a"/>: planted, its walk ended, the timer set (this tick counts as the first).</summary>
    private static void StartCasting(World world, int i, AbilityDef a)
    {
        UnitStore u = world.Units;
        u.State[i] = UnitState.Casting;
        u.GoalCell[i] = -1;
        u.Velocity[i] = Vector2.Zero;
        u.StuckTicks[i] = 0;
        u.BestRemaining[i] = float.PositiveInfinity;
        u.WalkBack[i] = UnitStore.WalkBackNone;
        u.CastTicks[i] = Math.Max(1, a.CastTicks);
        world.RecordAbilityEvent(new AbilityEvent(new EntityHandle(i, u.Generation[i]), u.Owner[i], a.Id, u.CastPoint[i], false));
    }

    /// <summary>
    /// The effects of <paramref name="a"/> cast by <paramref name="owner"/>'s <paramref name="caster"/> at
    /// <paramref name="point"/>: on each unit in the radius, in hash order, every effect in file order, where
    /// <see cref="AbilityDef.Affects"/> allows the unit or (M4-4b-1) the effect is a damage effect with friendly fire and the
    /// unit is the caster owner's; then each other player's building whose footprint is within the radius, in slot order,
    /// takes every damage effect with <see cref="AbilityEffect.Buildings"/>. A damage effect is one hit through
    /// <see cref="DamageCalc"/> with no falloff (an own unit's scaled by the friendly-fire fraction, rounded like splash); a
    /// unit it kills takes no later effect. M4-4b-2: then the ability's zone, if it leaves one (<see cref="ZoneSystem.Create"/>).
    /// </summary>
    private static void Resolve(World world, EntityHandle caster, int owner, AbilityDef a, Vector2 point)
    {
        UnitStore u = world.Units;
        GameData data = world.Data;
        int[] found = world.Neighbors; // movement's scratch: free in phase 6
        int n = Math.Min(world.Spatial.QueryRadius(point, a.Radius + QuerySlack, found), found.Length);
        float r2 = a.Radius * a.Radius;
        for (int e = 0; e < n; e++)
        {
            int j = found[e];
            if (!u.Alive[j] || Vector2.DistanceSquared(u.Position[j], point) > r2) continue;
            bool own = u.Owner[j] == owner;
            bool affected = a.Affects == AbilityAffects.EnemyUnits ? !own : a.Affects != AbilityAffects.OwnUnits || own;
            EntityHandle victim = new(j, u.Generation[j]);
            foreach (AbilityEffect fx in a.Effects)
            {
                if (!u.IsAlive(victim)) break;
                if (fx.Kind == AbilityEffectKind.Damage)
                {
                    // Friendly fire only reaches own units an enemy_units ability passes over (the loader refuses it elsewhere).
                    float factor = affected ? 1f : own && fx.FriendlyFire > 0f ? fx.FriendlyFire : 0f;
                    if (factor <= 0f) continue;
                    UnitDef vdef = data.Units[u.TypeId[j]];
                    int armor = vdef.Armor + DamageCalc.Points(world.Techs.Bonus(u.Owner[j], u.TypeId[j], TechStat.Armor));
                    int damage = ProjectileSystem.Scale(DamageCalc.Compute(data.DamageTable, fx.DamageType, vdef.ArmorClass, fx.Amount, 1f, armor), factor);
                    CombatSystem.HitUnit(world, new PendingHit(caster, owner, victim, false, damage));
                }
                else if (affected && fx.Kind == AbilityEffectKind.ApplyStatus) StatusSystem.Apply(world, j, fx.Status, fx.Magnitude, fx.DurationTicks, owner);
            }
        }
        // M4-4b-2: the zone, after the units took the other effects (it applies its statuses at once).
        if (a.ZoneEffect >= 0) ZoneSystem.Create(world, owner, a, point);
        if (!a.HitsBuildings || world.StructureClass < 0) return;
        BuildingStore b = world.Buildings;
        ReadOnlySpan<bool> alive = b.Alive;
        for (int j = 0; j < alive.Length; j++)
        {
            // Never an own building (docs/02 "Splash and friendly fire").
            if (!alive[j] || b.Owner[j] == owner || CombatSystem.BuildingDistanceSquared(world, j, point) > r2) continue;
            EntityHandle victim = b.HandleOf(j);
            int armor = data.Buildings[b.TypeId[j]].Armor;
            foreach (AbilityEffect fx in a.Effects)
            {
                if (!b.IsAlive(victim)) break;
                if (fx.Kind != AbilityEffectKind.Damage || !fx.Buildings) continue;
                int damage = DamageCalc.Compute(data.DamageTable, fx.DamageType, world.StructureClass, fx.Amount, 1f, armor);
                CombatSystem.HitBuilding(world, new PendingHit(caster, owner, victim, true, damage));
            }
        }
    }
}
