using System;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;

namespace Rts.Sim.Economy;

/// <summary>
/// Tick phase 4 (docs/03 "Economy implementation", M3-2): the worker gather / return loop with automatic
/// drop-off, plus the apply step of <see cref="CommandKind.Gather"/> and the dev command
/// <see cref="CommandKind.SpawnBuilding"/>.
/// </summary>
/// <remarks>
/// A worker is on a gather loop while <see cref="UnitStore.GatherNode"/> is set. It walks with the Move
/// machinery (state <see cref="UnitState.Moving"/>) to the nearest passable cell 4-adjacent to the node,
/// works while its center is within <see cref="EconomyConstants.Reach"/> of the footprint
/// (<see cref="UnitState.Gathering"/>), carries a full load to the nearest own drop-off by straight-line
/// distance, deposits into the player's total and goes back. Standing out of reach (behind other
/// workers, or after giving up) it walks again every <see cref="EconomyConstants.RetryTicks"/>. Only
/// exposed nodes (a passable cell 4-adjacent to the footprint) are gathered, so a forest is eaten from
/// the outside and felling never leaves a pocket (BUG-0075). Every search is a bounded scan in slot or
/// cell order: no allocation, no dictionaries.
/// </remarks>
public static class EconomySystem
{
    /// <summary>Phase 4: advances every live unit on a gather loop, in slot order.</summary>
    public static void Run(World world)
    {
        UnitStore u = world.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (u.Alive[i] && OnLoop(u, i)) Step(world, i);
        }
    }

    /// <summary>True while unit <paramref name="i"/> has a gather order (a node handle; real handles never have generation 0).</summary>
    public static bool OnLoop(UnitStore units, int i) => units.GatherNode[i].Generation != 0;

    /// <summary>True if the unit's type fills the worker slot (only workers gather).</summary>
    internal static bool IsWorker(World world, int i) => world.Data.Units[world.Units.TypeId[i]].Slot == UnitSlot.Worker;

    private static void Step(World world, int i)
    {
        UnitStore u = world.Units;
        bool full = u.Cargo[i] >= world.Data.Rules.WorkerCarry;
        if (u.State[i] == UnitState.Moving)
        {
            // Walking a leg. A node gone while the worker walks to it is replaced now, not on arrival.
            if (!full && !world.Resources.IsAlive(u.GatherNode[i]))
            {
                if (Retarget(world, i)) WalkToNode(world, i);
                else EndLoop(u, i);
            }
            return;
        }
        if (full)
        {
            if (InReachOfDropOff(world, i)) Deposit(world, i);
            else Wait(world, i, UnitState.Returning);
            return;
        }
        bool retargeted = false;
        if (!world.Resources.IsAlive(u.GatherNode[i]) || !IsExposed(world, u.GatherNode[i].Index))
        {
            if (!Retarget(world, i))
            {
                EndLoop(u, i);
                return;
            }
            retargeted = true;
        }
        if (InReachOfNode(world, i, u.GatherNode[i].Index)) Work(world, i);
        else if (retargeted) WalkToNode(world, i);
        else Wait(world, i, UnitState.Gathering);
    }

    /// <summary>One tick of work in reach: progress at the data rate; each whole unit is taken from the node into the cargo.</summary>
    private static void Work(World world, int i)
    {
        UnitStore u = world.Units;
        RulesDef rules = world.Data.Rules;
        EntityHandle node = u.GatherNode[i];
        if (u.State[i] != UnitState.Gathering)
        {
            Vector2 to = u.GatherSite[i] - u.Position[i];
            if (to != Vector2.Zero) u.Facing[i] = SimMath.Atan2(to.Y, to.X);
        }
        Stand(u, i, UnitState.Gathering);
        float progress = u.GatherProgress[i] + (u.CargoKind[i] == ResourceKind.Gold ? rules.GoldPerTick : rules.WoodPerTick);
        if (progress >= 1f)
        {
            int whole = progress >= int.MaxValue ? int.MaxValue : (int)progress;
            int taken = world.Resources.Take(node, Math.Min(whole, rules.WorkerCarry - u.Cargo[i]));
            u.Cargo[i] += taken;
            progress -= whole;
        }
        u.GatherProgress[i] = progress;
        if (u.Cargo[i] >= rules.WorkerCarry)
        {
            u.GatherProgress[i] = 0f;
            StartReturn(world, i);
        }
    }

    /// <summary>A full load: walk to the player's nearest live drop-off (straight line, chosen now); none → Idle, cargo kept.</summary>
    private static void StartReturn(World world, int i)
    {
        UnitStore u = world.Units;
        if (InReachOfDropOff(world, i))
        {
            Stand(u, i, UnitState.Returning); // deposits next tick
            return;
        }
        int b = NearestDropOff(world, i);
        if (b < 0)
        {
            EndLoop(u, i);
            return;
        }
        BuildingDef def = world.Data.Buildings[world.Buildings.TypeId[b]];
        if (!WalkToFootprint(world, i, world.Buildings.Cell[b], def.FootprintWidth, def.FootprintHeight)) Stand(u, i, UnitState.Returning);
    }

    private static void Deposit(World world, int i)
    {
        UnitStore u = world.Units;
        world.AddToTotal(u.Owner[i], u.CargoKind[i], u.Cargo[i]);
        u.Cargo[i] = 0;
        // Back to the same node if it still stands exposed, else the depleted-node rule.
        EntityHandle node = u.GatherNode[i];
        if (!world.Resources.IsAlive(node) || !IsExposed(world, node.Index))
        {
            if (!Retarget(world, i))
            {
                EndLoop(u, i);
                return;
            }
        }
        if (InReachOfNode(world, i, u.GatherNode[i].Index)) Stand(u, i, UnitState.Gathering);
        else WalkToNode(world, i);
    }

    /// <summary>Standing out of reach on a leg: count up, and walk the leg again every <see cref="EconomyConstants.RetryTicks"/>.</summary>
    private static void Wait(World world, int i, UnitState state)
    {
        UnitStore u = world.Units;
        if (u.State[i] != state)
        {
            // Just arrived or gave up (Idle), or shoved out of reach while working.
            u.State[i] = state;
            u.StuckTicks[i] = 0;
            return;
        }
        if (++u.StuckTicks[i] < EconomyConstants.RetryTicks) return;
        u.StuckTicks[i] = 0;
        if (state == UnitState.Returning) StartReturn(world, i);
        else WalkToNode(world, i);
    }

    /// <summary>Stands the worker in <paramref name="state"/>: still, its count for <see cref="Wait"/> at 0.</summary>
    internal static void Stand(UnitStore u, int i, UnitState state)
    {
        u.State[i] = state;
        u.Velocity[i] = Vector2.Zero;
        u.StuckTicks[i] = 0;
        u.BestRemaining[i] = float.PositiveInfinity;
    }

    /// <summary>The loop ends: Idle and goal-less like a Stop, cargo kept.</summary>
    private static void EndLoop(UnitStore u, int i)
    {
        Stand(u, i, UnitState.Idle);
        u.GoalCell[i] = -1;
        u.WalkBack[i] = UnitStore.WalkBackNone;
        u.GatherNode[i] = default;
    }

    private static void WalkToNode(World world, int i)
    {
        UnitStore u = world.Units;
        int n = u.GatherNode[i].Index;
        ResourceDef def = world.Data.Resources[world.Resources.TypeId[n]];
        if (!WalkToFootprint(world, i, world.Resources.Cell[n], def.FootprintWidth, def.FootprintHeight)) EndLoop(u, i);
    }

    /// <summary>
    /// Walks unit <paramref name="i"/> (Move machinery, a fresh order) to a stand point beside the footprint. The cell is
    /// the passable cell 4-adjacent to the footprint with the least distance from the worker to its center plus
    /// <c>CellSize</c> for every other unit standing in it (spatial hash), ties to the lower cell index: workers spread
    /// round a crowded mine. In that cell the worker takes one of <see cref="EconomyConstants.StandPointsPerCell"/> stand
    /// points along the shared edge (BUG-0146): the edge's middle and <see cref="EconomyConstants.StandPointSpacing"/>
    /// walker radii to either side, each <see cref="EconomyConstants.GoalInset"/> outside the edge; the one with the
    /// fewest other units on it (center within the walker's radius), then the nearest, then the middle first. So several
    /// workers stand side by side at a node open on one side, and one that waited out of reach takes a free point on
    /// its next retry. False if no such cell is passable.
    /// </summary>
    internal static bool WalkToFootprint(World world, int i, int anchor, int fw, int fh)
    {
        NavGrid g = world.NavGrid;
        UnitStore u = world.Units;
        Vector2 pos = u.Position[i];
        int w = g.Width, x0 = anchor % w, y0 = anchor / w;
        int best = -1;
        float bestScore = float.PositiveInfinity;
        for (int k = 0; k < 2 * (fw + fh); k++)
        {
            RingCell(x0, y0, fw, fh, k, out int x, out int y);
            if (!g.IsPassable(x, y)) continue;
            int cell = y * w + x;
            float score = Vector2.Distance(pos, g.CellCenter(x, y)) + CellCrowd(world, i, x, y) * MapConstants.CellSize;
            if (score < bestScore || (score == bestScore && cell < best))
            {
                best = cell;
                bestScore = score;
            }
        }
        if (best < 0) return false;
        OrderSystem.Walk(world, i, best, StandPoint(world, i, best % w, best / w, x0, y0, fw, fh));
        return true;
    }

    /// <summary>
    /// The stand point unit <paramref name="i"/> takes in ring cell (bx, by) of the footprint at (x0, y0): of the cell's
    /// <see cref="EconomyConstants.StandPointsPerCell"/> points along the shared edge, the one with the fewest other
    /// units on it, then the nearest to the worker, ties to the middle (tried first), then the lower side.
    /// </summary>
    private static Vector2 StandPoint(World world, int i, int bx, int by, int x0, int y0, int fw, int fh)
    {
        const float cs = MapConstants.CellSize, inset = EconomyConstants.GoalInset;
        Vector2 middle = world.NavGrid.CellCenter(bx, by);
        bool vertical = bx < x0 || bx >= x0 + fw; // the cell shares a vertical edge (left or right of the footprint)
        if (bx < x0) middle.X = x0 * cs - inset;
        else if (bx >= x0 + fw) middle.X = (x0 + fw) * cs + inset;
        else if (by < y0) middle.Y = y0 * cs - inset;
        else middle.Y = (y0 + fh) * cs + inset;
        // The outer points stay inside the cell, so the goal point is always in the goal cell.
        UnitStore u = world.Units;
        float gap = MathF.Min(EconomyConstants.StandPointSpacing * u.Radius[i], cs * 0.5f - inset);
        Vector2 along = vertical ? new Vector2(0f, gap) : new Vector2(gap, 0f);
        Vector2 pos = u.Position[i], best = middle;
        int bestCrowd = int.MaxValue;
        float bestDistance = float.PositiveInfinity;
        for (int p = 0; p < EconomyConstants.StandPointsPerCell; p++)
        {
            // Middle first, then the lower side, then the upper: p = 0, 1, 2 -> offsets 0, -1, +1.
            Vector2 point = middle + along * (p == 0 ? 0f : p == 1 ? -1f : 1f);
            int crowd = PointCrowd(world, i, point);
            if (crowd > bestCrowd) continue;
            float distance = Vector2.Distance(pos, point);
            if (crowd < bestCrowd || distance < bestDistance)
            {
                best = point;
                bestCrowd = crowd;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Live units other than <paramref name="i"/> whose center lies in cell (x, y), from the spatial hash (rebuilt after this tick's commands).</summary>
    private static int CellCrowd(World world, int i, int x, int y)
    {
        const float cs = MapConstants.CellSize;
        var min = new Vector2(x * cs, y * cs);
        var max = new Vector2((x + 1) * cs, (y + 1) * cs);
        int[] near = world.Neighbors; // movement's scratch: free in phases 1 to 7
        int count = world.Spatial.QueryRect(min, max, near), crowd = 0;
        UnitStore u = world.Units;
        for (int k = 0; k < count; k++)
        {
            int j = near[k];
            if (j == i) continue;
            Vector2 p = u.Position[j];
            if (p.X >= min.X && p.X < max.X && p.Y >= min.Y && p.Y < max.Y) crowd++;
        }
        return crowd;
    }

    /// <summary>Live units other than <paramref name="i"/> standing on <paramref name="point"/> (center within i's radius of it), from the spatial hash.</summary>
    private static int PointCrowd(World world, int i, Vector2 point)
    {
        int[] near = world.Neighbors;
        int count = world.Spatial.QueryRadius(point, world.Units.Radius[i], near), crowd = 0;
        for (int k = 0; k < count; k++)
        {
            if (near[k] != i) crowd++;
        }
        return crowd;
    }

    /// <summary>The <paramref name="k"/>-th cell (0 to 2 (w + h) - 1) 4-adjacent to the footprint: the row above, the row below, the column left, the column right.</summary>
    private static void RingCell(int x0, int y0, int fw, int fh, int k, out int x, out int y)
    {
        if (k < fw) { x = x0 + k; y = y0 - 1; return; }
        k -= fw;
        if (k < fw) { x = x0 + k; y = y0 + fh; return; }
        k -= fw;
        if (k < fh) { x = x0 - 1; y = y0 + k; return; }
        x = x0 + fw;
        y = y0 + k - fh;
    }

    /// <summary>True if node slot <paramref name="n"/> has a passable cell 4-adjacent to its footprint (the exposure rule, BUG-0075).</summary>
    internal static bool IsExposed(World world, int n)
    {
        NavGrid g = world.NavGrid;
        ResourceDef def = world.Data.Resources[world.Resources.TypeId[n]];
        int anchor = world.Resources.Cell[n], x0 = anchor % g.Width, y0 = anchor / g.Width;
        for (int k = 0; k < 2 * (def.FootprintWidth + def.FootprintHeight); k++)
        {
            RingCell(x0, y0, def.FootprintWidth, def.FootprintHeight, k, out int x, out int y);
            if (g.IsPassable(x, y)) return true;
        }
        return false;
    }

    /// <summary>Distance (m) from <paramref name="p"/> to the footprint rectangle at <paramref name="anchor"/> is at most <see cref="EconomyConstants.Reach"/>.</summary>
    internal static bool InReach(NavGrid g, Vector2 p, int anchor, int fw, int fh)
    {
        const float cs = MapConstants.CellSize;
        float x0 = anchor % g.Width * cs, y0 = anchor / g.Width * cs;
        float dx = MathF.Max(MathF.Max(x0 - p.X, p.X - (x0 + fw * cs)), 0f);
        float dy = MathF.Max(MathF.Max(y0 - p.Y, p.Y - (y0 + fh * cs)), 0f);
        return dx * dx + dy * dy <= EconomyConstants.Reach * EconomyConstants.Reach;
    }

    private static bool InReachOfNode(World world, int i, int n)
    {
        ResourceDef def = world.Data.Resources[world.Resources.TypeId[n]];
        return InReach(world.NavGrid, world.Units.Position[i], world.Resources.Cell[n], def.FootprintWidth, def.FootprintHeight);
    }

    private static bool InReachOfDropOff(World world, int i)
    {
        BuildingStore b = world.Buildings;
        int owner = world.Units.Owner[i];
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != owner) continue;
            BuildingDef def = world.Data.Buildings[b.TypeId[k]];
            if (def.DropOff && InReach(world.NavGrid, world.Units.Position[i], b.Cell[k], def.FootprintWidth, def.FootprintHeight)) return true;
        }
        return false;
    }

    /// <summary>Slot of the player's live drop-off whose footprint center is nearest unit <paramref name="i"/> (ties to the lower slot), or -1.</summary>
    private static int NearestDropOff(World world, int i)
    {
        BuildingStore b = world.Buildings;
        int owner = world.Units.Owner[i], best = -1;
        float bestD2 = float.PositiveInfinity;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != owner) continue;
            BuildingDef def = world.Data.Buildings[b.TypeId[k]];
            if (!def.DropOff) continue;
            float d2 = Vector2.DistanceSquared(world.Units.Position[i], Center(world.NavGrid, b.Cell[k], def.FootprintWidth, def.FootprintHeight));
            if (d2 < bestD2)
            {
                best = k;
                bestD2 = d2;
            }
        }
        return best;
    }

    /// <summary>Center (m) of the footprint at <paramref name="anchor"/>.</summary>
    internal static Vector2 Center(NavGrid g, int anchor, int fw, int fh) =>
        new((anchor % g.Width + fw * 0.5f) * MapConstants.CellSize, (anchor / g.Width + fh * 0.5f) * MapConstants.CellSize);

    /// <summary>The depleted-node rule: the nearest exposed node of the loop's kind within <c>nodeSearchRadius</c> of the old node's center becomes the loop's node.</summary>
    private static bool Retarget(World world, int i)
    {
        UnitStore u = world.Units;
        int n = NearestExposedNode(world, u.GatherSite[i], (int)u.CargoKind[i]);
        if (n < 0) return false;
        SetNode(world, i, n);
        return true;
    }

    private static void SetNode(World world, int i, int n)
    {
        ResourceStore r = world.Resources;
        ResourceDef def = world.Data.Resources[r.TypeId[n]];
        world.Units.GatherNode[i] = r.HandleOf(n);
        world.Units.GatherSite[i] = Center(world.NavGrid, r.Cell[n], def.FootprintWidth, def.FootprintHeight);
    }

    /// <summary>Slot of the live exposed node (of <paramref name="kind"/>, or any kind for -1) whose center is nearest <paramref name="from"/> and within <c>nodeSearchRadius</c>, ties to the lower slot; -1 if none.</summary>
    private static int NearestExposedNode(World world, Vector2 from, int kind)
    {
        ResourceStore r = world.Resources;
        float radius = world.Data.Rules.NodeSearchRadius, bestD2 = radius * radius;
        int best = -1;
        for (int n = 0; n < r.Capacity; n++)
        {
            if (!r.Alive[n]) continue;
            ResourceDef def = world.Data.Resources[r.TypeId[n]];
            if (kind >= 0 && (int)def.Resource != kind) continue;
            float d2 = Vector2.DistanceSquared(from, Center(world.NavGrid, r.Cell[n], def.FootprintWidth, def.FootprintHeight));
            if (d2 > bestD2 || (d2 == bestD2 && best >= 0) || !IsExposed(world, n)) continue;
            best = n;
            bestD2 = d2;
        }
        return best;
    }

    /// <summary>
    /// The node a Gather at <paramref name="target"/> works: the node covering that cell if it is exposed; if it
    /// isn't, the nearest exposed node of its kind from its center; on a cell without a node, the nearest
    /// exposed node of either kind from the target. All within <c>nodeSearchRadius</c>; -1 if none.
    /// </summary>
    internal static int ResolveNode(World world, Vector2 target)
    {
        NavGrid g = world.NavGrid;
        if (!g.WorldToCell(target, out int x, out int y)) return -1;
        if ((g.FlagsAt(x, y) & NavFlags.Resource) == 0) return NearestExposedNode(world, target, -1);
        ResourceStore r = world.Resources;
        for (int n = 0; n < r.Capacity; n++)
        {
            if (!r.Alive[n]) continue;
            ResourceDef def = world.Data.Resources[r.TypeId[n]];
            int ax = r.Cell[n] % g.Width, ay = r.Cell[n] / g.Width;
            if (x < ax || y < ay || x >= ax + def.FootprintWidth || y >= ay + def.FootprintHeight) continue;
            if (IsExposed(world, n)) return n;
            return NearestExposedNode(world, Center(g, r.Cell[n], def.FootprintWidth, def.FootprintHeight), (int)def.Resource);
        }
        return -1;
    }

    /// <summary>
    /// Starts unit <paramref name="i"/>'s gather loop on node slot <paramref name="n"/> (a resolved Gather):
    /// cargo of the other kind is discarded; a full load goes to a drop-off first; in reach it works at once,
    /// else it walks.
    /// </summary>
    internal static void StartGather(World world, int i, int n)
    {
        UnitStore u = world.Units;
        ResourceKind kind = world.Data.Resources[world.Resources.TypeId[n]].Resource;
        if (u.CargoKind[i] != kind)
        {
            u.Cargo[i] = 0; // AoE II rule: a load of the other resource is dropped (docs/03 "Implementation (M3-2)")
            u.GatherProgress[i] = 0f;
            u.CargoKind[i] = kind;
        }
        SetNode(world, i, n);
        u.Hold[i] = false;
        u.BuildTarget[i] = default; // a Gather ends building or repairing (M3-3)
        u.WalkBack[i] = UnitStore.WalkBackNone;
        if (u.Cargo[i] >= world.Data.Rules.WorkerCarry) StartReturn(world, i);
        else if (InReachOfNode(world, i, n))
        {
            Stand(u, i, UnitState.Gathering);
            u.GoalCell[i] = -1;
        }
        else WalkToNode(world, i);
    }

    /// <summary>
    /// Applies <see cref="CommandKind.SpawnBuilding"/> (dev / tests): a building of <see cref="Command.TypeId"/>
    /// for the player with its anchor (lowest x, y) cell at <see cref="Command.Position"/>. Dropped for an
    /// unknown type, a full store, a footprint that isn't open ground (<see cref="BuildingStore.Fits"/>), a
    /// footprint that would seal ground off (the M3-3 never-seal rule, BUG-0078), or a live unit whose center
    /// lies in the footprint. No faction or cost check: a dev command.
    /// </summary>
    internal static void ApplySpawnBuilding(World world, in Command command)
    {
        NavGrid g = world.NavGrid;
        if ((uint)command.TypeId >= (uint)world.Data.Buildings.Length) return;
        if (!g.WorldToCell(command.Position, out int x, out int y)) return;
        int anchor = y * g.Width + x;
        if (!world.Buildings.Fits(command.TypeId, anchor)) return;
        BuildingDef def = world.Data.Buildings[command.TypeId];
        if (!world.Seal.KeepsConnected(x, y, def.FootprintWidth, def.FootprintHeight)) return;
        const float cs = MapConstants.CellSize;
        float x0 = x * cs, y0 = y * cs, x1 = (x + def.FootprintWidth) * cs, y1 = (y + def.FootprintHeight) * cs;
        UnitStore u = world.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Vector2 p = u.Position[i];
            if (p.X >= x0 && p.X < x1 && p.Y >= y0 && p.Y < y1) return;
        }
        world.Buildings.Spawn(command.Player, command.TypeId, anchor, out _);
    }
}
