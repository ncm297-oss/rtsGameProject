using System;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;

namespace Rts.Sim.Combat;

/// <summary>
/// Melee combat (M4-1, docs/03 "Implementation (M4-1)"): target acquisition and chasing in phase 7
/// (<see cref="Acquire"/>), swings in phase 10 (<see cref="Attack"/>), damage and death in phase 11 (<see cref="Resolve"/>).
/// </summary>
/// <remarks>
/// Every loop runs in slot order over preallocated scratch in <see cref="World"/>; unit queries go through the spatial
/// hash only (<see cref="Spatial.SpatialHash.QueryEnemies"/>), buildings through a list of live building slots rebuilt
/// once a tick (at most <see cref="SimConfig.BuildingCapacity"/>). Only attacks without a projectile fight in this slice:
/// projectiles arrive in M4-2, and until then ranged, caster and siege-projectile units neither scan nor swing.
/// <para>
/// Every walk combat starts (a chase, an attack-mover's leg again, a walk back home) starts in phase 7, before movement,
/// as every order does; phases 10 and 11 only stand units still (plant, stand down), so a unit never turns Moving after
/// it has moved in a tick.
/// </para>
/// <para>
/// "No target" is tested as <c>Generation == 0</c> (real unit and building handles start at generation 1, as
/// <c>EconomySystem.OnLoop</c> does): a record-struct <c>==</c> is a chain of calls in Debug, where the budget is measured.
/// </para>
/// </remarks>
public static class CombatSystem
{
    /// <summary>Phase 7, after the order queue: drops dead targets, settles finished engagements, applies the leash, lets due units scan, and starts chases.</summary>
    public static void Acquire(World world)
    {
        UnitStore u = world.Units;
        GameData data = world.Data;
        CollectBuildings(world);
        int tick = world.TickNumber;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            bool due = (tick + i) % CombatConstants.ScanInterval == 0;
            // The common case, first and cheap: nothing to fight, no engagement to settle, not due to scan.
            if (!due && u.Target[i].Generation == 0 && u.Mode[i] == CombatMode.None) continue;
            if (u.LastAttacker[i].Generation != 0 && !u.IsAlive(u.LastAttacker[i])) u.LastAttacker[i] = default;
            if (u.Target[i].Generation != 0 && !TargetAlive(world, i)) Disengage(u, i);
            if (u.Target[i].Generation == 0)
            {
                if (u.Mode[i] != CombatMode.None) Settle(world, i);
            }
            else if (u.Mode[i] == CombatMode.Retaliate)
            {
                float sight = data.Units[u.TypeId[i]].Sight;
                if (Vector2.DistanceSquared(u.Position[i], u.AnchorPosition[i]) > sight * sight)
                {
                    // Past the leash: walk back, eyes front, and don't come back for this one (BUG-0137).
                    GiveUp(u, i);
                    u.Mode[i] = CombatMode.Returning;
                    if (!OrderSystem.MoveTo(world, i, u.AnchorPosition[i])) EndMode(u, i);
                    continue;
                }
            }
            // A chase that gets no closer for GiveUpScans scans ends (BUG-0137): the target is out of reach (another
            // plateau), behind a detour that leads away, or as fast as the chaser. Standing in reach restarts the count.
            if (due && u.Target[i].Generation != 0 && !u.Hold[i] && u.WindupTicks[i] == 0)
            {
                float gap = Gap(world, i);
                if (gap <= data.Units[u.TypeId[i]].Attack.Range || gap < u.ChaseBest[i] - CombatConstants.ChaseProgress)
                {
                    u.ChaseBest[i] = gap;
                    u.ChaseStall[i] = 0;
                }
                else if (++u.ChaseStall[i] >= CombatConstants.GiveUpScans)
                {
                    GiveUp(u, i);
                    Settle(world, i);
                    continue;
                }
            }
            bool scanned = false;
            // Due, able to scan, and with an enemy unit or building somewhere (a one-player crowd skips the scan whole).
            if (due && world.CombatEnemyExists[u.Owner[i]] && Scans(world, i)
                // Mid-swing, or engaged in reach: keep fighting it (a swing is not thrown away for a better target).
                && !(u.Target[i].Generation != 0 && (u.WindupTicks[i] > 0 || Gap(world, i) <= data.Units[u.TypeId[i]].Attack.Range)))
            {
                scanned = true;
                int pick = PickTarget(world, i, out bool isBuilding);
                if (pick >= 0)
                {
                    Engage(world, i, isBuilding ? world.Buildings.HandleOf(pick) : new EntityHandle(pick, u.Generation[pick]), isBuilding);
                }
                else if (u.Target[i].Generation != 0)
                {
                    // Out of sight (or, holding, out of reach). Lost while the chaser wasn't gaining on it: given up
                    // (BUG-0137), so it isn't taken again the moment the walk back brings it into sight.
                    if (!u.Hold[i] && u.ChaseStall[i] > 0) GiveUp(u, i);
                    else Disengage(u, i);
                    Settle(world, i);
                }
            }
            // Chase: a unit standing with a target out of reach walks at once; a walking chaser is re-aimed on its scan
            // tick only, so a fleeing target changes its goal at most every ScanInterval ticks.
            if (u.Target[i].Generation == 0 || u.Hold[i] || u.State[i] == UnitState.Attacking) continue;
            if (u.State[i] == UnitState.Idle ? Gap(world, i) > data.Units[u.TypeId[i]].Attack.Range : scanned) Chase(world, i);
        }
    }

    /// <summary>Phase 10: cooldowns count down; units with a target plant and swing in reach, stand down out of it, and land wind-ups as queued hits.</summary>
    public static void Attack(World world)
    {
        UnitStore u = world.Units;
        GameData data = world.Data;
        world.HitCount = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (u.CooldownTicks[i] > 0) u.CooldownTicks[i]--;
            if (u.Target[i].Generation == 0) continue; // no target (real handles start at generation 1)
            if (!TargetAlive(world, i))
            {
                Disengage(u, i); // a site cancelled, a unit freed since phase 7
                continue;
            }
            AttackDef attack = data.Units[u.TypeId[i]].Attack;
            float gap = Gap(world, i);
            if (u.WindupTicks[i] > 0)
            {
                // Mid-swing the unit stands; the hit lands at the wind-up point if the target is still near enough.
                Face(world, i);
                if (--u.WindupTicks[i] == 0)
                {
                    if (gap <= attack.Range + CombatConstants.WindupGrace) QueueHit(world, i);
                    // The swing is over: out of reach, it stands down now, so an Attacking unit is always in reach or mid-swing.
                    if (gap > attack.Range) Stand(u, i, UnitState.Idle);
                }
                continue;
            }
            if (gap <= attack.Range)
            {
                if (u.State[i] != UnitState.Attacking) Plant(u, i);
                Face(world, i);
                if (u.CooldownTicks[i] == 0)
                {
                    u.CooldownTicks[i] = attack.CooldownTicks;
                    u.WindupTicks[i] = attack.WindupTicks;
                    if (attack.WindupTicks == 0) QueueHit(world, i);
                }
                continue;
            }
            // Out of reach: an Attacking unit stands down; it chases (or, holding, waits) from the next phase 7.
            if (u.State[i] == UnitState.Attacking) Stand(u, i, UnitState.Idle);
        }
    }

    /// <summary>
    /// Phase 11: applies the queued hits in attacker slot order (a unit killed this phase still lands its own queued
    /// hit, so equal fighters can kill each other), frees the dead, counts kills and losses, records death events, and
    /// clears every target and last attacker that died.
    /// </summary>
    public static void Resolve(World world)
    {
        UnitStore u = world.Units;
        int deathsBefore = world.DeathCount;
        for (int k = 0; k < world.HitCount; k++)
        {
            ref PendingHit hit = ref world.Hits[k];
            if (hit.IsBuilding) HitBuilding(world, in hit);
            else HitUnit(world, in hit);
        }
        world.HitCount = 0;
        if (world.DeathCount == deathsBefore) return;
        BuildingStore b = world.Buildings;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (u.LastAttacker[i].Generation != 0 && !u.IsAlive(u.LastAttacker[i])) u.LastAttacker[i] = default;
            if (u.Target[i].Generation != 0 && !TargetAlive(world, i)) Disengage(u, i);
            // A building destroyed here ends its builders' and repairers' orders now, not at their next phase 4.
            if (u.BuildTarget[i].Generation != 0 && !b.IsAlive(u.BuildTarget[i])) Economy.ConstructionSystem.End(u, i);
        }
    }

    /// <summary>
    /// Ends unit <paramref name="i"/>'s engagement for a new order (M4-1: any unqueued order, or a popped one): no target,
    /// no swing, no mode; an Attacking unit stands Idle. The cooldown keeps counting, so re-ordering never swings sooner.
    /// </summary>
    internal static void ClearForOrder(UnitStore u, int i)
    {
        Disengage(u, i);
        // A new order forgets what the unit gave up on (BUG-0137).
        u.Ignored[i] = default;
        u.IgnoredIsBuilding[i] = false;
        u.GiveUps[i] = 0;
        EndMode(u, i);
    }

    /// <summary>Unit <paramref name="i"/>'s order is now an attack-move leg to <paramref name="destination"/>: its goal once the Move rule has applied (so the leg is over when it stands with that goal).</summary>
    internal static void StartAttackMove(UnitStore u, int i, Vector2 destination)
    {
        u.Mode[i] = CombatMode.AttackMove;
        u.AnchorPosition[i] = destination;
    }

    /// <summary>True for a unit type that fights in this slice: an attack with a value and no projectile (M4-2 brings projectiles).</summary>
    internal static bool CanFight(UnitDef def) => def.Attack.Value > 0 && def.Attack.Projectile == null;

    /// <summary>True for a unit type that counts as "can attack" in the target priority: an attack with a value, workers aside.</summary>
    internal static bool IsCombatant(UnitDef def) => def.Attack.Value > 0 && def.Slot != UnitSlot.Worker;

    /// <summary>
    /// Whether unit <paramref name="i"/> looks for targets: a fighting type that is holding position, on an attack-move
    /// leg, retaliating (chasing, fighting, or walking back after its target died), or Idle with no engagement. Walking a
    /// plain Move, walking home on the leash, and a worker's gather or build loop never scan; a unit of the worker slot
    /// scans only on an attack-move leg.
    /// </summary>
    private static bool Scans(World world, int i)
    {
        UnitStore u = world.Units;
        UnitDef def = world.Data.Units[u.TypeId[i]];
        if (!CanFight(def)) return false;
        // Workers never fight on their own (Producer decision, BUG-0147): not Idle, not holding, not hit; only while the
        // player attack-moves them. The data hook is the unit's slot.
        if (def.Slot == UnitSlot.Worker && u.Mode[i] != CombatMode.AttackMove) return false;
        // On a gather, build or repair loop (even standing Idle a tick between its legs): the economy moves it, never combat.
        if (u.GatherNode[i].Generation != 0 || u.BuildTarget[i].Generation != 0) return false;
        UnitState s = u.State[i];
        if (s is not (UnitState.Idle or UnitState.Moving or UnitState.Attacking)) return false;
        if (u.Hold[i]) return s != UnitState.Moving;
        return u.Mode[i] switch
        {
            CombatMode.AttackMove => true,
            CombatMode.Retaliate => true,
            CombatMode.Returning => false,
            _ => s == UnitState.Idle,
        };
    }

    /// <summary>
    /// The best target for unit <paramref name="i"/> (slot, or building slot with <paramref name="isBuilding"/>), -1 for
    /// none: within sight (holding: within reach), by the docs/03 priority (enemies attacking it, then units that can
    /// attack, then other units, then buildings), then nearest, then lowest slot. One pass over the hash's enemies.
    /// </summary>
    private static int PickTarget(World world, int i, out bool isBuilding)
    {
        isBuilding = false;
        UnitStore u = world.Units;
        GameData data = world.Data;
        UnitDef def = data.Units[u.TypeId[i]];
        Vector2 pos = u.Position[i];
        int owner = u.Owner[i];
        // Holding, or after MaxGiveUps chases given up (BUG-0137): only what is in reach.
        bool hold = u.Hold[i] || u.GiveUps[i] >= CombatConstants.MaxGiveUps;
        float radius = hold ? def.Attack.Range + u.Radius[i] + world.MaxUnitRadius : def.Sight;
        // The target it last gave up (BUG-0137) is taken again only in reach; index -1 when that is none or a building.
        int ignoredGen = u.Ignored[i].Generation;
        int ignoredIndex = u.IgnoredIsBuilding[i] || ignoredGen == 0 ? -1 : u.Ignored[i].Index;
        float r2 = radius * radius;
        int myGen = u.Generation[i], lastIndex = u.LastAttacker[i].Index, lastGen = u.LastAttacker[i].Generation;
        bool[] combatant = world.CombatantType;
        int[] found = world.Neighbors; // movement's scratch: free in phase 7
        int n = Math.Min(world.Spatial.QueryEnemies(pos, radius, owner, found), found.Length);
        int best = -1, bestTier = int.MaxValue;
        float bestD2 = float.MaxValue;
        // Handles compared field by field and the "can attack" test read from a table: this loop runs for every enemy in
        // sight of every scanning unit, and Debug builds (where the budget is measured) don't inline.
        for (int k = 0; k < n; k++)
        {
            int j = found[k];
            if (!u.Alive[j] || u.Owner[j] == owner) continue;
            EntityHandle jt = u.Target[j];
            int tier = (jt.Index == i && jt.Generation == myGen && !u.TargetIsBuilding[j]) || (lastIndex == j && lastGen == u.Generation[j]) ? 0
                : combatant[u.TypeId[j]] ? 1 : 2;
            if (tier > bestTier) continue; // can't win: skip the distance
            float d2 = Vector2.DistanceSquared(pos, u.Position[j]);
            if (!(d2 <= r2)) continue;
            if (hold || (j == ignoredIndex && u.Generation[j] == ignoredGen))
            {
                float reach = def.Attack.Range + u.Radius[i] + u.Radius[j];
                if (!(d2 <= reach * reach)) continue;
            }
            if (tier < bestTier || d2 < bestD2 || (d2 == bestD2 && j < best))
            {
                best = j;
                bestTier = tier;
                bestD2 = d2;
            }
        }
        if (best >= 0) return best;
        if (world.CombatBuildingCount - world.CombatBuildingsOf[owner] <= 0) return -1;
        BuildingStore b = world.Buildings;
        int ignoredBuilding = u.IgnoredIsBuilding[i] ? u.Ignored[i].Index : -1;
        for (int k = 0; k < world.CombatBuildingCount; k++)
        {
            int j = world.CombatBuildings[k];
            if (b.Owner[j] == owner) continue;
            float d2 = BuildingDistanceSquared(world, j, pos);
            float limit = hold || (j == ignoredBuilding && b.Generation[j] == ignoredGen) ? def.Attack.Range + u.Radius[i] : radius;
            if (!(d2 <= limit * limit)) continue;
            if (d2 < bestD2 || (d2 == bestD2 && j < best))
            {
                best = j;
                bestD2 = d2;
            }
        }
        isBuilding = best >= 0;
        return best;
    }

    /// <summary>
    /// Fills the world's list of live building slots (slot order) and the count per owner, for this tick's scans, and for
    /// each player whether any other owner has a unit or a building at all.
    /// </summary>
    private static void CollectBuildings(World world)
    {
        BuildingStore b = world.Buildings;
        int[] perOwner = world.CombatBuildingsOf;
        Array.Clear(perOwner);
        int n = 0;
        if (b.Count > 0)
        {
            for (int j = 0; j < b.Capacity; j++)
            {
                if (!b.Alive[j]) continue;
                world.CombatBuildings[n++] = j;
                if ((uint)b.Owner[j] < (uint)perOwner.Length) perOwner[b.Owner[j]]++;
            }
        }
        world.CombatBuildingCount = n;
        bool[] enemy = world.CombatEnemyExists;
        for (int p = 0; p < enemy.Length; p++)
            enemy[p] = world.Spatial.OthersThan(p) > 0 || n > perOwner[p];
    }

    /// <summary>Takes <paramref name="target"/> as unit <paramref name="i"/>'s target (a new one loses any swing); an unengaged unit that may move starts a leashed retaliation from where it stands. The chase starts in phase 7.</summary>
    private static void Engage(World world, int i, EntityHandle target, bool isBuilding)
    {
        UnitStore u = world.Units;
        if (u.Target[i] == target && u.TargetIsBuilding[i] == isBuilding) return;
        u.Target[i] = target;
        u.TargetIsBuilding[i] = isBuilding;
        u.WindupTicks[i] = 0;
        u.ChaseBest[i] = Gap(world, i);
        u.ChaseStall[i] = 0;
        if (u.State[i] == UnitState.Attacking) Stand(u, i, UnitState.Idle);
        if (!u.Hold[i] && u.Mode[i] == CombatMode.None)
        {
            u.Mode[i] = CombatMode.Retaliate;
            u.AnchorPosition[i] = u.Position[i];
        }
    }

    /// <summary>Drops unit <paramref name="i"/>'s target and any swing; an Attacking unit stands Idle. What it does next is <see cref="Settle"/>'s, in phase 7.</summary>
    private static void Disengage(UnitStore u, int i)
    {
        u.Target[i] = default;
        u.TargetIsBuilding[i] = false;
        u.WindupTicks[i] = 0;
        u.ChaseBest[i] = 0f;
        u.ChaseStall[i] = 0;
        if (u.State[i] == UnitState.Attacking) Stand(u, i, UnitState.Idle);
    }

    /// <summary>
    /// Unit <paramref name="i"/> gives its target up (BUG-0137): it remembers it in <see cref="UnitStore.Ignored"/> (its
    /// scans take it again only in reach, until its next order), counts the give-up, and disengages.
    /// </summary>
    private static void GiveUp(UnitStore u, int i)
    {
        u.Ignored[i] = u.Target[i];
        u.IgnoredIsBuilding[i] = u.TargetIsBuilding[i];
        if (u.GiveUps[i] < CombatConstants.MaxGiveUps) u.GiveUps[i]++;
        Disengage(u, i);
    }

    /// <summary>
    /// Phase 7, for unit <paramref name="i"/> with no target: an attack-mover heads for its leg's end again and a
    /// retaliator for its anchor, at once (also one still walking a chase whose target is gone); one standing after it
    /// walked there (its goal is the anchor: arrived, or gave up on the way) ends the engagement, as does a unit the leash
    /// pulled back, once it stands.
    /// </summary>
    private static void Settle(World world, int i)
    {
        UnitStore u = world.Units;
        CombatMode mode = u.Mode[i];
        if (mode == CombatMode.None) return;
        // At the anchor, or as near as it can get: a building may cover the anchor's cell by now (BUG-0141), and the
        // Move rule then resolves it to the nearest passable cell's center.
        bool there = u.Goal[i] == u.AnchorPosition[i]
            || (OrderSystem.ResolveTarget(world.NavGrid, u.AnchorPosition[i], out _, out Vector2 resolved) && u.Goal[i] == resolved);
        if (u.State[i] == UnitState.Moving)
        {
            if (!there && !OrderSystem.MoveTo(world, i, u.AnchorPosition[i])) EndMode(u, i);
            return;
        }
        if (u.State[i] != UnitState.Idle) return;
        if (mode == CombatMode.Returning || u.Hold[i] || there || !OrderSystem.MoveTo(world, i, u.AnchorPosition[i]))
            EndMode(u, i);
    }

    /// <summary>Clears the unit's combat mode and anchor.</summary>
    private static void EndMode(UnitStore u, int i)
    {
        u.Mode[i] = CombatMode.None;
        u.AnchorPosition[i] = Vector2.Zero;
    }

    /// <summary>Sends unit <paramref name="i"/> toward its target through the usual movement path, unless it already walks to the target's cell.</summary>
    private static void Chase(World world, int i)
    {
        UnitStore u = world.Units;
        NavGrid grid = world.NavGrid;
        Vector2 point = TargetPoint(world, i);
        if (u.TargetIsBuilding[i])
        {
            // Just outside the footprint, on the unit's side: a standing point in reach (the footprint itself is blocked).
            Vector2 away = u.Position[i] - point;
            float d = away.Length();
            if (d > 0f) point += away * ((u.Radius[i] + CombatConstants.BuildingStandOff) / d);
        }
        if (!OrderSystem.ResolveTarget(grid, point, out int cell, out Vector2 goal)) return;
        if (u.State[i] == UnitState.Moving && u.GoalCell[i] == cell) return;
        OrderSystem.Walk(world, i, cell, goal);
    }

    /// <summary>Where unit <paramref name="i"/> heads for its target: a unit's position, or the nearest point of a building's footprint.</summary>
    private static Vector2 TargetPoint(World world, int i)
    {
        UnitStore u = world.Units;
        if (!u.TargetIsBuilding[i]) return u.Position[u.Target[i].Index];
        BuildingRect(world, u.Target[i].Index, out Vector2 min, out Vector2 max);
        return Vector2.Clamp(u.Position[i], min, max);
    }

    /// <summary>Edge-to-edge distance (m) from unit <paramref name="i"/> to its live target: centers less both radii, or to a building's footprint less its own radius.</summary>
    private static float Gap(World world, int i)
    {
        UnitStore u = world.Units;
        int j = u.Target[i].Index;
        if (u.TargetIsBuilding[i]) return MathF.Sqrt(BuildingDistanceSquared(world, j, u.Position[i])) - u.Radius[i];
        return Vector2.Distance(u.Position[i], u.Position[j]) - u.Radius[i] - u.Radius[j];
    }

    /// <summary>Squared distance (m^2) from <paramref name="p"/> to building slot <paramref name="j"/>'s footprint rectangle; 0 inside it.</summary>
    private static float BuildingDistanceSquared(World world, int j, Vector2 p)
    {
        BuildingRect(world, j, out Vector2 min, out Vector2 max);
        return Vector2.DistanceSquared(p, Vector2.Clamp(p, min, max));
    }

    /// <summary>Building slot <paramref name="j"/>'s footprint in meters.</summary>
    private static void BuildingRect(World world, int j, out Vector2 min, out Vector2 max)
    {
        BuildingStore b = world.Buildings;
        int w = world.NavGrid.Width;
        BuildingDef def = world.Data.Buildings[b.TypeId[j]];
        const float cs = MapConstants.CellSize;
        min = new Vector2(b.Cell[j] % w * cs, b.Cell[j] / w * cs);
        max = min + new Vector2(def.FootprintWidth * cs, def.FootprintHeight * cs);
    }

    private static bool TargetAlive(World world, int i)
    {
        UnitStore u = world.Units;
        return u.TargetIsBuilding[i] ? world.Buildings.IsAlive(u.Target[i]) : u.IsAlive(u.Target[i]);
    }

    /// <summary>Turns unit <paramref name="i"/> toward its target.</summary>
    private static void Face(World world, int i)
    {
        UnitStore u = world.Units;
        Vector2 d = TargetPoint(world, i) - u.Position[i];
        if (d != Vector2.Zero) u.Facing[i] = SimMath.Atan2(d.Y, d.X);
    }

    /// <summary>Stops unit <paramref name="i"/> to fight: Attacking, no goal, nothing to walk back to.</summary>
    private static void Plant(UnitStore u, int i)
    {
        Stand(u, i, UnitState.Attacking);
        u.GoalCell[i] = -1;
        u.WalkBack[i] = UnitStore.WalkBackNone;
    }

    private static void Stand(UnitStore u, int i, UnitState state)
    {
        u.State[i] = state;
        u.Velocity[i] = Vector2.Zero;
        u.StuckTicks[i] = 0;
        u.BestRemaining[i] = float.PositiveInfinity;
    }

    /// <summary>Queues unit <paramref name="i"/>'s hit on its target, its damage worked out now from both owners' techs.</summary>
    private static void QueueHit(World world, int i)
    {
        UnitStore u = world.Units;
        GameData data = world.Data;
        UnitDef def = data.Units[u.TypeId[i]];
        int attackBonus = DamageCalc.Points(world.Techs.Bonus(u.Owner[i], u.TypeId[i], TechStat.Attack));
        int damage;
        if (u.TargetIsBuilding[i])
        {
            int structure = world.StructureClass;
            if (structure < 0) return; // data without a structure class: buildings take no damage
            BuildingDef bdef = data.Buildings[world.Buildings.TypeId[u.Target[i].Index]];
            damage = DamageCalc.Compute(data.DamageTable, def.Attack, attackBonus, structure, bdef.Armor);
        }
        else
        {
            int j = u.Target[i].Index;
            UnitDef vdef = data.Units[u.TypeId[j]];
            int armor = vdef.Armor + DamageCalc.Points(world.Techs.Bonus(u.Owner[j], u.TypeId[j], TechStat.Armor));
            damage = DamageCalc.Compute(data.DamageTable, def.Attack, attackBonus, vdef.ArmorClass, armor);
        }
        u.GiveUps[i] = 0; // it lands one: the chases it gave up before no longer hold it back (BUG-0137)
        world.Hits[world.HitCount++] = new PendingHit(new EntityHandle(i, u.Generation[i]), u.Owner[i], u.Target[i], u.TargetIsBuilding[i], damage);
    }

    private static void HitUnit(World world, in PendingHit hit)
    {
        UnitStore u = world.Units;
        if (!u.IsAlive(hit.Victim)) return; // killed earlier this phase
        int v = hit.Victim.Index;
        u.Hp[v] -= hit.Damage;
        if (u.Hp[v] <= 0)
        {
            u.Hp[v] = 0;
            world.RecordDeath(new DeathEvent(hit.Victim, false, u.TypeId[v], u.Owner[v], hit.AttackerOwner, u.Position[v]));
            u.Free(hit.Victim);
            return;
        }
        if (!u.IsAlive(hit.Attacker)) return;
        u.LastAttacker[v] = hit.Attacker;
        // Retaliation: a unit that scans and has nothing to fight takes on whoever hit it, now rather than on its next scan.
        // Not one it gave up on (BUG-0137): a melee attacker is in reach, and its next scan takes it there.
        if (u.Target[v].Generation == 0 && Scans(world, v) && u.Ignored[v] != hit.Attacker) Engage(world, v, hit.Attacker, false);
    }

    private static void HitBuilding(World world, in PendingHit hit)
    {
        BuildingStore b = world.Buildings;
        if (!b.IsAlive(hit.Victim)) return;
        int j = hit.Victim.Index;
        if (hit.Damage >= b.Hp[j])
        {
            BuildingRect(world, j, out Vector2 min, out Vector2 max);
            world.RecordDeath(new DeathEvent(hit.Victim, true, b.TypeId[j], b.Owner[j], hit.AttackerOwner, (min + max) * 0.5f));
        }
        b.Damage(hit.Victim, hit.Damage);
    }
}
