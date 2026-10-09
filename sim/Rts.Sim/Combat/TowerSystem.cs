using System;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Vision;

namespace Rts.Sim.Combat;

/// <summary>
/// Buildings that shoot (M4-3b, docs/03 "Implementation (M4-3b)"): a finished building whose type has an attack
/// (<see cref="BuildingDef.Attack"/>: the Watch Tower's) picks an enemy unit in phase 7 (<see cref="Acquire"/>) and fires an
/// aimed projectile at it from its footprint's centre in phase 10 (<see cref="Attack"/>), under the unit rules: the
/// cooldown, the wind-up, the lead (<see cref="ProjectileSystem.Lead"/>) and the vision rule (its owner sees the target).
/// It never targets a building; a construction site never shoots.
/// </summary>
/// <remarks>
/// Both phases walk this tick's list of live building slots (<see cref="World.CombatBuildings"/>, collected by
/// <see cref="CombatSystem.Acquire"/>) and skip everything when no building type in the data has an attack
/// (<see cref="World.AnyBuildingAttack"/>). The scan is one spatial-hash query per due tower (<see cref="CombatConstants.ScanInterval"/>,
/// staggered by slot); nothing allocates.
/// </remarks>
public static class TowerSystem
{
    /// <summary>
    /// Phase 7, after the units' acquisition: a tower drops a target that died (any tick) or, on its scan tick, one out of
    /// range or that its owner no longer sees (never mid-wind-up: the shot is not thrown away); a tower with no target picks
    /// one on its scan tick (<c>(tick + slot) % ScanInterval == 0</c>).
    /// </summary>
    public static void Acquire(World world)
    {
        if (!world.AnyBuildingAttack) return;
        BuildingStore b = world.Buildings;
        UnitStore u = world.Units;
        GameData data = world.Data;
        EntityHandle[] targets = b.TowerTargets;
        int tick = world.TickNumber;
        for (int k = 0; k < world.CombatBuildingCount; k++)
        {
            int j = world.CombatBuildings[k];
            AttackDef? attack = data.Buildings[b.TypeId[j]].Attack;
            if (attack == null || b.UnderConstruction[j]) continue;
            bool due = (tick + j) % CombatConstants.ScanInterval == 0;
            EntityHandle t = targets[j];
            if (t.Generation != 0)
            {
                if (!u.IsAlive(t)) Drop(b, j);
                else if (due && b.TowerWindup[j] == 0 && (!InReach(attack, Gap(world, j, t.Index)) || !VisionSystem.BuildingSeesUnit(world, j, t.Index)))
                    Drop(b, j);
            }
            if (targets[j].Generation != 0 || !due || world.Spatial.OthersThan(b.Owner[j]) == 0) continue;
            int pick = Pick(world, j, attack);
            if (pick >= 0) targets[j] = new EntityHandle(pick, u.Generation[pick]);
        }
    }

    /// <summary>
    /// Phase 10, after the units' swings: every tower's cooldown counts down; a tower with a live target winds up a shot when
    /// the target is in reach and the cooldown is 0 (cooldown = the attack's, wind-up = the attack's), and at the wind-up's
    /// end fires if the target is within reach + <see cref="CombatConstants.WindupGrace"/> (else the shot is lost). A target
    /// out of reach between shots is dropped; the next scan picks again.
    /// </summary>
    public static void Attack(World world)
    {
        if (!world.AnyBuildingAttack) return;
        BuildingStore b = world.Buildings;
        UnitStore u = world.Units;
        GameData data = world.Data;
        EntityHandle[] targets = b.TowerTargets;
        int[] cooldown = b.TowerCooldown, windup = b.TowerWindup;
        for (int k = 0; k < world.CombatBuildingCount; k++)
        {
            int j = world.CombatBuildings[k];
            AttackDef? attack = data.Buildings[b.TypeId[j]].Attack;
            if (attack == null || b.UnderConstruction[j]) continue;
            if (cooldown[j] > 0) cooldown[j]--;
            EntityHandle t = targets[j];
            if (t.Generation == 0) continue;
            if (!u.IsAlive(t))
            {
                Drop(b, j);
                continue;
            }
            float gap = Gap(world, j, t.Index);
            if (windup[j] > 0)
            {
                if (--windup[j] == 0 && gap <= attack.Range + CombatConstants.WindupGrace && !(gap < attack.MinRange)) Fire(world, j, attack);
                continue;
            }
            if (!InReach(attack, gap))
            {
                Drop(b, j);
                continue;
            }
            if (cooldown[j] != 0) continue;
            cooldown[j] = attack.CooldownTicks;
            windup[j] = attack.WindupTicks;
            if (attack.WindupTicks == 0) Fire(world, j, attack);
        }
    }

    /// <summary>
    /// The best target for tower <paramref name="j"/>, -1 for none: an enemy unit in reach (edge to edge from the footprint,
    /// within the range and not inside the minimum range) that its owner sees, by the unit priority (docs/03 "Implementation
    /// (M4-1)"): units attacking this tower, then units that can attack, then other units; then nearest to the footprint,
    /// then lowest slot. Never a building.
    /// </summary>
    private static int Pick(World world, int j, AttackDef attack)
    {
        BuildingStore b = world.Buildings;
        UnitStore u = world.Units;
        int owner = b.Owner[j], gen = b.Generation[j];
        CombatSystem.BuildingRect(world, j, out Vector2 min, out Vector2 max);
        Vector2 centre = (min + max) * 0.5f;
        // Every unit whose edge is within range of the footprint has its centre within this of the footprint's centre.
        float radius = attack.Range + world.MaxUnitRadius + Vector2.Distance(centre, max);
        bool[] combatant = world.CombatantType;
        int[] found = world.Neighbors; // movement's scratch: free in phase 7
        int n = Math.Min(world.Spatial.QueryEnemies(centre, radius, owner, found), found.Length);
        int best = -1, bestTier = int.MaxValue;
        float bestD2 = float.MaxValue;
        for (int e = 0; e < n; e++)
        {
            int c = found[e];
            if (!u.Alive[c] || u.Owner[c] == owner) continue;
            EntityHandle ct = u.Target[c];
            int tier = ct.Index == j && ct.Generation == gen && u.TargetIsBuilding[c] ? 0 : combatant[u.TypeId[c]] ? 1 : 2;
            if (tier > bestTier) continue;
            Vector2 p = u.Position[c];
            float d2 = Vector2.DistanceSquared(p, Vector2.Clamp(p, min, max));
            if (!(tier < bestTier || d2 < bestD2 || (d2 == bestD2 && c < best))) continue;
            if (!InReach(attack, MathF.Sqrt(d2) - u.Radius[c])) continue;
            // Only what its owner sees: asked of a candidate that would win, as the units' scan does (BUG-0217).
            if (!VisionSystem.BuildingSeesUnit(world, j, c)) continue;
            best = c;
            bestTier = tier;
            bestD2 = d2;
        }
        return best;
    }

    /// <summary>Tower <paramref name="j"/>'s shot at its target unit: an aimed projectile from its footprint's centre, led as a unit's is. Lost when the store is full.</summary>
    private static void Fire(World world, int j, AttackDef attack)
    {
        BuildingStore b = world.Buildings;
        UnitStore u = world.Units;
        EntityHandle t = b.TowerTargets[j];
        ProjectileDef pd = world.Data.Projectiles[attack.ProjectileTypeId];
        Vector2 from = CombatSystem.BuildingCentre(world, j);
        Vector2 to = ProjectileSystem.Lead(from, u.Position[t.Index], u.Velocity[t.Index], pd);
        world.Projectiles.TrySpawn(from, to, pd.SpeedPerTick, pd.Id, b.Owner[j], b.TypeId[j], b.HandleOf(j), t, false,
            world.Fog.BuildingLevel(j), attackerIsBuilding: true);
    }

    /// <summary>Edge-to-edge distance (m) from tower <paramref name="j"/>'s footprint to unit <paramref name="unit"/>: to its nearest footprint point, less the unit's radius.</summary>
    private static float Gap(World world, int j, int unit) =>
        MathF.Sqrt(CombatSystem.BuildingDistanceSquared(world, j, world.Units.Position[unit])) - world.Units.Radius[unit];

    /// <summary>Within the attack's range and not inside its minimum range (<see cref="CombatSystem.InReach"/>).</summary>
    private static bool InReach(AttackDef attack, float gap) => CombatSystem.InReach(attack, gap);

    /// <summary>Tower <paramref name="j"/> drops its target and any shot it was winding up; the cooldown keeps counting.</summary>
    private static void Drop(BuildingStore b, int j)
    {
        b.TowerTargets[j] = default;
        b.TowerWindup[j] = 0;
    }
}
