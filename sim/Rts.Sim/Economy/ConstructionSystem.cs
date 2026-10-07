using System;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Economy;

/// <summary>
/// Building placement, construction and repair (docs/03 "Implementation (M3-3)"): the placement rule
/// (<see cref="World.CanPlace"/>), the apply steps of <see cref="CommandKind.Build"/>, <see cref="CommandKind.Cancel"/>
/// and <see cref="CommandKind.Repair"/>, and the per-tick work of builders and repairers (tick phase 4, after gathering).
/// </summary>
/// <remarks>
/// A worker builds or repairs while <see cref="UnitStore.BuildTarget"/> holds a live building: a site under
/// construction is built, a finished one below its maximum hit points is repaired. It walks to a passable cell
/// 4-adjacent to the footprint like a gatherer, works while within <see cref="EconomyConstants.Reach"/>
/// (<see cref="UnitState.Building"/>), and standing out of reach walks again every <see cref="EconomyConstants.RetryTicks"/>.
/// Work is counted per building first (builders in reach, slot order), then applied per building in slot order, so the
/// result never depends on which builder came first. Bounded scans only: no allocation, no dictionaries.
/// </remarks>
public static class ConstructionSystem
{
    /// <summary>The placement rule behind <see cref="World.CanPlace"/>: the first rule broken, or <see cref="PlacementError.None"/>.</summary>
    internal static PlacementError Check(World world, int player, int typeId, int anchorCell)
    {
        GameData data = world.Data;
        if ((uint)typeId >= (uint)data.Buildings.Length) return PlacementError.UnknownType;
        BuildingDef def = data.Buildings[typeId];
        if (world.FactionOf(player) != def.Faction) return PlacementError.WrongFaction;
        NavGrid g = world.NavGrid;
        int w = g.Width;
        if ((uint)anchorCell >= (uint)(w * g.Height)) return PlacementError.OffMap;
        int x0 = anchorCell % w, y0 = anchorCell / w;
        if (x0 + def.FootprintWidth > w || y0 + def.FootprintHeight > g.Height) return PlacementError.OffMap;
        // Terrain, nodes and buildings: the store's rule (passable, no ramp, one level).
        if (!world.Buildings.Fits(typeId, anchorCell)) return PlacementError.Blocked;
        if (!world.Seal.KeepsConnected(x0, y0, def.FootprintWidth, def.FootprintHeight)) return PlacementError.SealsGround;
        UnitStore u = world.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || (u.Owner[i] == player && !u.Hold[i])) continue;
            if (Inside(u.Position[i], x0, y0, def)) return PlacementError.UnitInTheWay;
        }
        if (world.Gold[player] < def.CostGold || world.Wood[player] < def.CostWood) return PlacementError.CannotAfford;
        if (world.Buildings.FreeCount == 0) return PlacementError.StoreFull;
        return PlacementError.None;
    }

    /// <summary>True if <paramref name="p"/> (meters) lies in the footprint of <paramref name="def"/> anchored at (<paramref name="x0"/>, <paramref name="y0"/>).</summary>
    private static bool Inside(Vector2 p, int x0, int y0, BuildingDef def)
    {
        const float cs = MapConstants.CellSize;
        return p.X >= x0 * cs && p.X < (x0 + def.FootprintWidth) * cs && p.Y >= y0 * cs && p.Y < (y0 + def.FootprintHeight) * cs;
    }

    /// <summary>
    /// A Build for worker <paramref name="i"/> (unqueued, or popped from its queue): joins the owner's site of
    /// <paramref name="typeId"/> anchored at <paramref name="target"/>'s cell, or places one there if <see cref="World.CanPlace"/>
    /// allows (paying, pushing own units out of the footprint). False, with nothing changed, if neither.
    /// <paramref name="replaceQueue"/> clears the worker's queue once the order is taken.
    /// </summary>
    internal static bool StartBuild(World world, int i, int typeId, Vector2 target, bool replaceQueue)
    {
        NavGrid g = world.NavGrid;
        if (!g.WorldToCell(target, out int x, out int y)) return false;
        int anchor = y * g.Width + x, player = world.Units.Owner[i];
        BuildingStore b = world.Buildings;
        int site = b.SlotAt(x, y);
        if (site >= 0)
        {
            // Only a site of this type anchored exactly here is joined; anything else there is no place to build.
            if (b.Owner[site] != player || b.TypeId[site] != typeId || b.Cell[site] != anchor || !b.UnderConstruction[site]) return false;
        }
        else
        {
            if (Check(world, player, typeId, anchor) != PlacementError.None) return false;
            BuildingDef def = world.Data.Buildings[typeId];
            world.TrySpend(player, def.CostGold, def.CostWood);
            b.Spawn(player, typeId, anchor, out EntityHandle h, site: true);
            site = h.Index;
            PushOut(world, player, x, y, def);
        }
        if (replaceQueue) world.Units.ClearQueue(i);
        Assign(world, i, b.HandleOf(site));
        return true;
    }

    /// <summary>
    /// A Repair for worker <paramref name="i"/>: the owner's finished building covering <paramref name="target"/>'s cell,
    /// below its maximum hit points. False, with nothing changed, for anything else.
    /// </summary>
    internal static bool StartRepair(World world, int i, Vector2 target, bool replaceQueue)
    {
        NavGrid g = world.NavGrid;
        if (!g.WorldToCell(target, out int x, out int y)) return false;
        BuildingStore b = world.Buildings;
        int k = b.SlotAt(x, y);
        if (k < 0 || b.Owner[k] != world.Units.Owner[i] || b.UnderConstruction[k] || b.Hp[k] >= world.Data.Buildings[b.TypeId[k]].Hp) return false;
        if (replaceQueue) world.Units.ClearQueue(i);
        Assign(world, i, b.HandleOf(k));
        return true;
    }

    /// <summary>Gives worker <paramref name="i"/> its building: Hold and any gather loop end (cargo kept); in reach it stands to work, else it walks.</summary>
    private static void Assign(World world, int i, EntityHandle building)
    {
        UnitStore u = world.Units;
        u.Hold[i] = false;
        u.GatherNode[i] = default;
        u.WalkBack[i] = UnitStore.WalkBackNone;
        u.BuildTarget[i] = building;
        if (InReach(world, i, building.Index))
        {
            EconomySystem.Stand(u, i, UnitState.Building);
            u.GoalCell[i] = -1;
        }
        else WalkTo(world, i, building.Index);
    }

    /// <summary>
    /// Moves every unit of <paramref name="player"/> whose center lies in the new footprint (none of them holding: the
    /// placement rule refused those) to the nearest free cell outside it, slot order: the rings of cells round the
    /// footprint, nearest first, on the footprint's level, passable, with no other unit's center in it; nearest to the
    /// unit's center, ties to the lower cell. A position set, not a walk. Past <see cref="EconomyConstants.PushRings"/>
    /// rings: the nearest passable cell (<see cref="FlowField.NearestPassable"/>).
    /// </summary>
    private static void PushOut(World world, int player, int x0, int y0, BuildingDef def)
    {
        UnitStore u = world.Units;
        NavGrid g = world.NavGrid;
        int level = g.LevelAt(x0, y0);
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != player || !Inside(u.Position[i], x0, y0, def)) continue;
            int best = -1;
            float bestD2 = float.PositiveInfinity;
            for (int r = 1; r <= EconomyConstants.PushRings && best < 0; r++)
            {
                for (int y = y0 - r; y < y0 + def.FootprintHeight + r; y++)
                {
                    for (int x = x0 - r; x < x0 + def.FootprintWidth + r; x++)
                    {
                        bool edge = y == y0 - r || y == y0 + def.FootprintHeight + r - 1 || x == x0 - r || x == x0 + def.FootprintWidth + r - 1;
                        if (!edge || !g.IsPassable(x, y) || g.LevelAt(x, y) != level || Occupied(u, i, x, y)) continue;
                        float d2 = Vector2.DistanceSquared(u.Position[i], g.CellCenter(x, y));
                        if (d2 < bestD2) // cells come in index order, so a tie keeps the lower one
                        {
                            best = y * g.Width + x;
                            bestD2 = d2;
                        }
                    }
                }
            }
            if (best < 0) best = FlowField.NearestPassable(g, y0 * g.Width + x0);
            if (best >= 0) u.Position[i] = g.CellCenter(best % g.Width, best / g.Width);
        }
    }

    /// <summary>True if a live unit other than <paramref name="i"/> has its center in cell (x, y).</summary>
    private static bool Occupied(UnitStore u, int i, int x, int y)
    {
        const float cs = MapConstants.CellSize;
        for (int j = 0; j < u.Capacity; j++)
        {
            if (j == i || !u.Alive[j]) continue;
            Vector2 p = u.Position[j];
            if (p.X >= x * cs && p.X < (x + 1) * cs && p.Y >= y * cs && p.Y < (y + 1) * cs) return true;
        }
        return false;
    }

    /// <summary>
    /// Applies <see cref="CommandKind.Cancel"/>: the player's own site covering the position is removed (an opening
    /// change), <c>floor(cost x (needed - work) / needed)</c> of each resource comes back, and its builders go Idle.
    /// Dropped for anything that isn't an own site under construction.
    /// </summary>
    internal static void ApplyCancel(World world, in Command command)
    {
        NavGrid g = world.NavGrid;
        if (!g.WorldToCell(command.Position, out int x, out int y)) return;
        BuildingStore b = world.Buildings;
        int k = b.SlotAt(x, y);
        if (k < 0 || b.Owner[k] != command.Player || !b.UnderConstruction[k]) return;
        BuildingDef def = world.Data.Buildings[b.TypeId[k]];
        long needed = b.WorkNeeded(b.TypeId[k]), left = needed - b.Work[k];
        world.AddToTotal(command.Player, ResourceKind.Gold, (int)(def.CostGold * left / needed));
        world.AddToTotal(command.Player, ResourceKind.Wood, (int)(def.CostWood * left / needed));
        EntityHandle h = b.HandleOf(k);
        UnitStore u = world.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.BuildTarget[i] == h) End(u, i);
        b.Free(h);
    }

    /// <summary>Phase 4, after gathering: every builder and repairer steps (slot order), then every building with workers in reach gains work or hit points (slot order).</summary>
    public static void Run(World world)
    {
        UnitStore u = world.Units;
        BuildingStore b = world.Buildings;
        int[] workers = world.SiteWorkers;
        Array.Clear(workers);
        bool anyEnded = false;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (u.Alive[i] && u.BuildTarget[i].Generation != 0) Step(world, i);
        }
        for (int k = 0; k < b.Capacity; k++)
        {
            if (workers[k] == 0 || !b.Alive[k]) continue;
            if (b.UnderConstruction[k])
            {
                // docs/02: n builders take t x 3 / (n + 2), i.e. WorkNeeded = 3 t gained at n + 2 a tick.
                long work = (long)b.Work[k] + workers[k] + EconomyConstants.BuildWorkBase;
                b.SetWork(k, (int)Math.Min(work, int.MaxValue));
                if (b.UnderConstruction[k]) continue;
            }
            else if (Repair(world, k, workers[k])) continue;
            workers[k] = -1; // finished, fully repaired, or out of money: its workers stop
            anyEnded = true;
        }
        if (!anyEnded) return;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (u.Alive[i] && u.BuildTarget[i].Generation != 0 && workers[u.BuildTarget[i].Index] < 0) End(u, i);
        }
    }

    /// <summary>One builder's tick: stop for a gone or finished-and-whole building; walking, nothing; in reach, count it; else wait and retry.</summary>
    private static void Step(World world, int i)
    {
        UnitStore u = world.Units;
        BuildingStore b = world.Buildings;
        EntityHandle h = u.BuildTarget[i];
        int k = h.Index;
        if (!b.IsAlive(h) || (!b.UnderConstruction[k] && b.Hp[k] >= world.Data.Buildings[b.TypeId[k]].Hp))
        {
            End(u, i);
            return;
        }
        if (u.State[i] == UnitState.Moving) return;
        if (InReach(world, i, k))
        {
            if (u.State[i] != UnitState.Building)
            {
                Vector2 to = Center(world, k) - u.Position[i];
                if (to != Vector2.Zero) u.Facing[i] = SimMath.Atan2(to.Y, to.X);
            }
            EconomySystem.Stand(u, i, UnitState.Building);
            world.SiteWorkers[k]++;
            return;
        }
        // Out of reach (arrived behind others, gave up, or shoved off): stand, and walk again every RetryTicks.
        if (u.State[i] != UnitState.Building)
        {
            EconomySystem.Stand(u, i, UnitState.Building);
            return;
        }
        if (++u.StuckTicks[i] < EconomyConstants.RetryTicks) return;
        u.StuckTicks[i] = 0;
        WalkTo(world, i, k);
    }

    /// <summary>
    /// One tick of repair on finished building <paramref name="k"/> by <paramref name="n"/> workers in reach: each restores
    /// <c>maxHp / buildTicks x repair.rateFactor</c> hit points a tick and the owner pays <c>repair.costFactor x cost x
    /// restored / maxHp</c>, both through fixed-point accumulators so whole units are exact. False when the workers stop:
    /// full hit points, or a resource the repair costs would go below 0 (a total of 0 stops it at once).
    /// </summary>
    private static bool Repair(World world, int k, int n)
    {
        BuildingStore b = world.Buildings;
        BuildingDef def = world.Data.Buildings[b.TypeId[k]];
        RulesDef rules = world.Data.Rules;
        int player = b.Owner[k], hp = b.Hp[k];
        if (hp >= def.Hp) return false;
        long one = EconomyConstants.RepairFixedOne;
        long rate = (long)MathF.Round(rules.RepairRateFactor * one), costRate = (long)MathF.Round(rules.RepairCostFactor * one);
        long perHp = def.BuildTicks * one;           // accumulator units per hit point
        long progress = b.RepairProgress(k) + (long)n * def.Hp * rate;
        long restored = Math.Min(progress / perHp, def.Hp - hp);
        long perUnit = def.Hp * one;                 // owed units per resource unit
        long owedGold = b.RepairGold(k) + restored * def.CostGold * costRate;
        long owedWood = b.RepairWood(k) + restored * def.CostWood * costRate;
        long payGold = owedGold / perUnit, payWood = owedWood / perUnit;
        int gold = world.Gold[player], wood = world.Wood[player];
        if ((def.CostGold > 0 && gold == 0) || (def.CostWood > 0 && wood == 0) || payGold > gold || payWood > wood) return false;
        world.TrySpend(player, (int)payGold, (int)payWood);
        b.SetHp(k, hp + (int)restored);
        b.RepairProgress(k) = progress - restored * perHp;
        b.RepairGold(k) = owedGold - payGold * perUnit;
        b.RepairWood(k) = owedWood - payWood * perUnit;
        if (hp + restored < def.Hp) return true;
        // Whole again: the fractions left over are dropped, so the next repair starts clean.
        b.RepairProgress(k) = 0;
        b.RepairGold(k) = 0;
        b.RepairWood(k) = 0;
        return false;
    }

    /// <summary>The worker's building order ends: Idle and goal-less like a Stop, cargo kept.</summary>
    internal static void End(UnitStore u, int i)
    {
        EconomySystem.Stand(u, i, UnitState.Idle);
        u.GoalCell[i] = -1;
        u.WalkBack[i] = UnitStore.WalkBackNone;
        u.BuildTarget[i] = default;
    }

    private static void WalkTo(World world, int i, int k)
    {
        BuildingStore b = world.Buildings;
        BuildingDef def = world.Data.Buildings[b.TypeId[k]];
        if (!EconomySystem.WalkToFootprint(world, i, b.Cell[k], def.FootprintWidth, def.FootprintHeight)) End(world.Units, i);
    }

    private static bool InReach(World world, int i, int k)
    {
        BuildingStore b = world.Buildings;
        BuildingDef def = world.Data.Buildings[b.TypeId[k]];
        return EconomySystem.InReach(world.NavGrid, world.Units.Position[i], b.Cell[k], def.FootprintWidth, def.FootprintHeight);
    }

    private static Vector2 Center(World world, int k)
    {
        BuildingStore b = world.Buildings;
        BuildingDef def = world.Data.Buildings[b.TypeId[k]];
        return EconomySystem.Center(world.NavGrid, b.Cell[k], def.FootprintWidth, def.FootprintHeight);
    }
}
