using System;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Combat;

/// <summary>
/// Projectiles and splash (M4-2b, docs/03 "Implementation (M4-2b)"): a ranged attack fires at its wind-up end
/// (<see cref="Fire"/>, phase 10) toward where its target is then; every projectile flies one straight step a tick
/// (<see cref="Fly"/>, the start of phase 10) and lands in phase 11 (<see cref="Land"/>, after the queued melee hits).
/// An aimed shot at a unit walking no faster than its projectile's lead speed is aimed where that unit will be when it
/// lands (<see cref="Lead"/>, BUG-0183) and re-led every tick it flies while that unit stays that slow; a faster one is
/// shot where it is and can dodge. An aimed shot hits its target only if the target is still within its radius + the projectile's hit tolerance of the
/// impact point, else it lands harmlessly; a lob always explodes. Splash (<see cref="Splash"/>) falls off from 100 % to
/// 50 % at the edge, reaches own units only with friendly fire (at half), never own buildings.
/// </summary>
public static class ProjectileSystem
{
    /// <summary>
    /// Phase 10, before the swings: every led shot is re-led (<see cref="Track"/>), then every projectile in flight moves
    /// one step (one fired this tick waits for the next).
    /// </summary>
    public static void Fly(World world)
    {
        if (world.Projectiles.Count == 0) return;
        Track(world);
        world.Projectiles.Fly();
    }

    /// <summary>
    /// Re-leads every aimed shot in flight whose projectile leads (<see cref="ProjectileDef.LeadSpeedPerTick"/> above 0) at
    /// a live unit walking no faster than that (BUG-0183): its impact point becomes where the unit will be when it lands, at
    /// its step this tick, and it steers there over its remaining ticks (its landing tick is fixed at firing). A walker on
    /// a flow-field path bends its course at cell corners; one led only at firing would dodge the shots it bends under.
    /// A dead target, a building, or a unit now faster than the lead speed is not re-led: the shot keeps its course.
    /// </summary>
    private static void Track(World world)
    {
        ProjectileStore p = world.Projectiles;
        UnitStore u = world.Units;
        System.Collections.Immutable.ImmutableArray<ProjectileDef> projectiles = world.Data.Projectiles;
        int end = p.End;
        for (int k = 0; k < end; k++)
        {
            if (!p.Alive[k] || p.VictimIsBuilding[k]) continue;
            int left = p.TicksLeft[k];
            if (left <= 1) continue; // this step lands it: nothing left to steer (and nothing moves before it lands)
            ProjectileDef pd = projectiles[p.ProjectileTypeId[k]];
            float lead = pd.LeadSpeedPerTick;
            if (!(lead > 0f) || pd.Kind != ProjectileKind.Aimed) continue;
            EntityHandle v = p.Victim[k];
            if (!u.IsAlive(v)) continue;
            Vector2 step = u.Velocity[v.Index];
            if (!(step.LengthSquared() <= lead * lead)) continue;
            // It lands after `left` more Fly steps; the victim walks in the left - 1 ticks before the landing one.
            p.Steer(k, u.Position[v.Index] + step * (left - 1));
        }
    }

    /// <summary>
    /// Unit <paramref name="i"/>'s shot at its live target, at the wind-up end: a projectile from the unit's position to the
    /// target's position now (a building: the footprint point nearest the unit), led for a slow enough walking unit
    /// (<see cref="Lead"/>). Lost when the store is full.
    /// </summary>
    internal static void Fire(World world, int i)
    {
        UnitStore u = world.Units;
        int type = u.TypeId[i];
        ProjectileDef pd = world.Data.Projectiles[world.Data.Units[type].Attack.ProjectileTypeId];
        Vector2 from = u.Position[i], to = CombatSystem.TargetPoint(world, i);
        if (!u.TargetIsBuilding[i] && pd.Kind == ProjectileKind.Aimed)
            to = Lead(from, to, u.Velocity[u.Target[i].Index], pd);
        world.Projectiles.TrySpawn(from, to, pd.SpeedPerTick, pd.Id, u.Owner[i], type,
            new EntityHandle(i, u.Generation[i]), u.Target[i], u.TargetIsBuilding[i]);
    }

    /// <summary>
    /// Where an aimed <paramref name="pd"/> fired from <paramref name="from"/> at a unit at <paramref name="at"/> walking
    /// <paramref name="velocity"/> (m a tick, its last movement step) flies (BUG-0183, docs/03 "Implementation (M4-2b)"):
    /// a unit no faster than <see cref="ProjectileDef.LeadSpeedPerTick"/> is led to where it will be after the flight at
    /// that step, so one walking straight is hit whichever way it goes; a faster one (cavalry) is shot where it is now, so
    /// it dodges a long shot. The flight time depends on the aim point, so the estimate is refined a fixed
    /// <see cref="LeadPasses"/> times; the last refinement's error is at most a step or two of the walker.
    /// </summary>
    public static Vector2 Lead(Vector2 from, Vector2 at, Vector2 velocity, ProjectileDef pd)
    {
        float lead = pd.LeadSpeedPerTick;
        if (velocity == Vector2.Zero || !(velocity.LengthSquared() <= lead * lead)) return at;
        Vector2 aim = at;
        for (int pass = 0; pass < LeadPasses; pass++)
            aim = at + velocity * ProjectileStore.FlightTicks(Vector2.Distance(from, aim), pd.SpeedPerTick);
        return aim;
    }

    /// <summary>Refinements of a led aim point: the first uses the flight to where the target is, the next the flight to that aim.</summary>
    private const int LeadPasses = 2;

    /// <summary>
    /// Phase 11, after the queued hits: every projectile that reached its impact point this tick lands, in slot order, and
    /// is recorded in <see cref="World.Impacts"/>. A unit killed earlier in the phase is not hit again (an aimed shot at it
    /// misses).
    /// </summary>
    internal static void Land(World world)
    {
        ProjectileStore p = world.Projectiles;
        if (p.Count == 0) return;
        int end = p.End; // Free resets the bound when the store empties
        for (int k = 0; k < end; k++)
        {
            if (!p.Alive[k] || p.TicksLeft[k] != 0) continue;
            bool hit = Strike(world, k);
            world.RecordImpact(new ProjectileImpact(p.Target[k], p.ProjectileTypeId[k], p.Owner[k], hit));
            p.Free(k);
        }
    }

    /// <summary>Projectile slot <paramref name="k"/> at its impact point: a lob explodes; an aimed shot hits its target if it is near enough (with its splash, if any), else nothing. Returns whether it struck.</summary>
    private static bool Strike(World world, int k)
    {
        ProjectileStore p = world.Projectiles;
        GameData data = world.Data;
        ProjectileDef pd = data.Projectiles[p.ProjectileTypeId[k]];
        int attackerType = p.AttackerType[k], owner = p.Owner[k];
        Vector2 at = p.Target[k];
        if (pd.Kind == ProjectileKind.Lob)
        {
            Splash(world, at, attackerType, owner, p.Attacker[k], default, false);
            return true;
        }
        EntityHandle v = p.Victim[k];
        bool building = p.VictimIsBuilding[k];
        if (building)
        {
            // A building never moves: a shot at a live one always hits.
            if (!world.Buildings.IsAlive(v)) return false;
            int damage = CombatSystem.DamageToBuilding(world, attackerType, owner, v.Index);
            if (damage > 0) CombatSystem.HitBuilding(world, new PendingHit(p.Attacker[k], owner, v, true, damage));
        }
        else
        {
            UnitStore u = world.Units;
            if (!u.IsAlive(v)) return false;
            float reach = u.Radius[v.Index] + pd.HitTolerance;
            if (!(Vector2.DistanceSquared(u.Position[v.Index], at) <= reach * reach)) return false;
            CombatSystem.HitUnit(world, new PendingHit(p.Attacker[k], owner, v, false, CombatSystem.DamageToUnit(world, attackerType, owner, v.Index)));
        }
        Splash(world, at, attackerType, owner, p.Attacker[k], v, building);
        return true;
    }

    /// <summary>
    /// The splash of an attack of unit type <paramref name="attackerType"/> owned by <paramref name="owner"/> landing at
    /// <paramref name="at"/> (nothing when the attack has none): every other owner's unit whose center is within the
    /// radius, and with friendly fire every own unit (the attacker too) at <see cref="CombatConstants.FriendlyFireFactor"/>;
    /// every other owner's building whose footprint is within it, as structure; never an own building. Damage per victim
    /// is <see cref="DamageCalc.Compute(DamageTable, AttackDef, int, int, int)"/> with its class and armor, times
    /// <see cref="Falloff"/>, rounded half up, at least 1. <paramref name="skip"/> (the direct target an aimed or melee
    /// hit already struck) is left out. Units in slot order, then buildings in slot order.
    /// </summary>
    internal static void Splash(World world, Vector2 at, int attackerType, int owner, EntityHandle attacker, EntityHandle skip, bool skipIsBuilding)
    {
        GameData data = world.Data;
        AttackDef attack = data.Units[attackerType].Attack;
        float radius = attack.Splash;
        if (!(radius > 0f)) return;
        int attackBonus = DamageCalc.Points(world.Techs.Bonus(owner, attackerType, TechStat.Attack));
        UnitStore u = world.Units;
        int[] found = world.Neighbors; // movement's scratch: free in phase 11
        // The hash holds positions from its last rebuild (this tick's phase 1); no unit has walked more than a step since.
        int n = Math.Min(world.Spatial.QueryRadius(at, radius + world.MaxUnitSpeed + SplashQuerySlack, found), found.Length);
        for (int e = 0; e < n; e++)
        {
            int j = found[e];
            if (!u.Alive[j] || (!skipIsBuilding && skip.Index == j && skip.Generation == u.Generation[j])) continue;
            float d = Vector2.Distance(u.Position[j], at);
            if (!(d <= radius)) continue;
            bool friend = u.Owner[j] == owner;
            if (friend && !attack.FriendlyFire) continue;
            float factor = Falloff(d, radius) * (friend ? CombatConstants.FriendlyFireFactor : 1f);
            UnitDef vdef = data.Units[u.TypeId[j]];
            int armor = vdef.Armor + DamageCalc.Points(world.Techs.Bonus(u.Owner[j], u.TypeId[j], TechStat.Armor));
            int damage = Scale(DamageCalc.Compute(data.DamageTable, attack, attackBonus, vdef.ArmorClass, armor), factor);
            CombatSystem.HitUnit(world, new PendingHit(attacker, owner, new EntityHandle(j, u.Generation[j]), false, damage));
        }
        int structure = world.StructureClass;
        if (structure < 0) return;
        BuildingStore b = world.Buildings;
        for (int k = 0; k < world.CombatBuildingCount; k++)
        {
            int j = world.CombatBuildings[k];
            // Friendly fire never damages buildings (docs/02).
            if (!b.Alive[j] || b.Owner[j] == owner || (skipIsBuilding && skip.Index == j && skip.Generation == b.Generation[j])) continue;
            float d = MathF.Sqrt(CombatSystem.BuildingDistanceSquared(world, j, at));
            if (!(d <= radius)) continue;
            int damage = Scale(DamageCalc.Compute(data.DamageTable, attack, attackBonus, structure, data.Buildings[b.TypeId[j]].Armor), Falloff(d, radius));
            CombatSystem.HitBuilding(world, new PendingHit(attacker, owner, b.HandleOf(j), true, damage));
        }
    }

    /// <summary>
    /// The splash factor at <paramref name="distance"/> m from the impact in a splash of <paramref name="radius"/> m
    /// (docs/02 "Splash and friendly fire"): 1 within <see cref="CombatConstants.SplashFullFraction"/> of the radius, then
    /// linear down to <see cref="CombatConstants.SplashEdgeFactor"/> at the edge; 0 beyond it.
    /// </summary>
    public static float Falloff(float distance, float radius)
    {
        if (!(distance <= radius)) return 0f;
        float full = radius * CombatConstants.SplashFullFraction;
        if (distance <= full) return 1f;
        return 1f - (1f - CombatConstants.SplashEdgeFactor) * (distance - full) / (radius - full);
    }

    /// <summary><paramref name="damage"/> times <paramref name="factor"/>, rounded half up as the damage formula rounds, at least 1.</summary>
    internal static int Scale(int damage, float factor) =>
        factor >= 1f ? damage : Math.Max(1, (int)MathF.Floor(damage * factor + 0.5f));

    /// <summary>Meters added to a splash query past the radius and a unit's step: a shove can carry a unit a little further than its own speed.</summary>
    private const float SplashQuerySlack = 1f;
}
