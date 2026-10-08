using System;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Combat;

/// <summary>
/// Projectiles and splash (M4-2b, docs/03 "Implementation (M4-2b)"): a ranged attack fires at its wind-up end
/// (<see cref="Fire"/>, phase 10) toward where its target is then; every projectile flies one straight step a tick
/// (<see cref="Fly"/>, the start of phase 10) and lands in phase 11 (<see cref="Land"/>, after the queued melee hits).
/// An aimed shot hits its target only if the target is still within its radius + the projectile's hit tolerance of the
/// impact point, else it lands harmlessly; a lob always explodes. Splash (<see cref="Splash"/>) falls off from 100 % to
/// 50 % at the edge, reaches own units only with friendly fire (at half), never own buildings.
/// </summary>
public static class ProjectileSystem
{
    /// <summary>Phase 10, before the swings: every projectile in flight moves one step (one fired this tick waits for the next).</summary>
    public static void Fly(World world)
    {
        if (world.Projectiles.Count > 0) world.Projectiles.Fly();
    }

    /// <summary>
    /// Unit <paramref name="i"/>'s shot at its live target, at the wind-up end: a projectile from the unit's position to the
    /// target's position now (a building: the footprint point nearest the unit). Lost when the store is full.
    /// </summary>
    internal static void Fire(World world, int i)
    {
        UnitStore u = world.Units;
        int type = u.TypeId[i];
        ProjectileDef pd = world.Data.Projectiles[world.Data.Units[type].Attack.ProjectileTypeId];
        world.Projectiles.TrySpawn(u.Position[i], CombatSystem.TargetPoint(world, i), pd.SpeedPerTick, pd.Id, u.Owner[i], type,
            new EntityHandle(i, u.Generation[i]), u.Target[i], u.TargetIsBuilding[i]);
    }

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
