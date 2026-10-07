using System;
using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>Debug start bases until real start locations exist (M6): where the default match puts each player's Town Hall and starting workers (M3-V1).</summary>
/// <remarks>
/// Deterministic from the grid, the stores and the data alone (no RNG) and read-only; like the other view reads it takes
/// the stores' read-only parts, never the world. A Town Hall spot is an anchor whose footprint
/// <see cref="BuildingStore.Fits"/> (open, flat, one level) on the start block's level, whose 8-ring (the cells
/// 8-adjacent to the footprint) has no blocked cell (no tree, mine, cliff, border or building), and where no cell of
/// footprint or ring is <c>taken</c> (another player's base or a start-block unit), and which stays on its own block's
/// side of the centre gap (<see cref="StartLayout.HalfGapCells"/>; ring included), so no hall stands between the armies.
/// An all-passable ring forms a loop
/// round the footprint, so the hall can never seal ground off (the never-seal rule <c>SpawnBuilding</c> applies).
/// Among the spots the one whose footprint centre is nearest the block wins, ties toward the map centre, then the lower
/// anchor cell (the CLI's <c>run --workers</c> order). Workers stand one per passable, untaken cell on the rings round
/// the footprint, nearest ring first; within a ring nearest the gold mine closest to the hall (else the start block), so
/// the first trip is short.
/// </remarks>
public static class StartBase
{
    /// <summary>The building type of faction <paramref name="faction"/> in template slot <paramref name="slot"/>, or -1.</summary>
    public static int BuildingOfSlot(GameData data, int faction, BuildingSlot slot)
    {
        for (int b = 0; b < data.Buildings.Length; b++)
            if (data.Buildings[b].Faction == faction && data.Buildings[b].Slot == slot) return b;
        return -1;
    }

    /// <summary>The unit type of faction <paramref name="faction"/> in template slot <paramref name="slot"/>, or -1.</summary>
    public static int UnitOfSlot(GameData data, int faction, UnitSlot slot)
    {
        for (int t = 0; t < data.Units.Length; t++)
            if (data.Units[t].Faction == faction && data.Units[t].Slot == slot) return t;
        return -1;
    }

    /// <summary>
    /// Each player's start base beside its start block (<paramref name="blocks"/>[p], the positions
    /// <see cref="StartLayout.Block"/> gave; empty for no army, then a one-unit block stands in for it): player p plays
    /// faction <paramref name="factionOf"/>[p] (the world's <c>FactionOf</c>); its Town Hall goes on the <see cref="HallSpot"/> nearest the block's mean on
    /// the level of its first (best) spot and on its side of the centre gap (player 0 west, player 1 east, as the blocks;
    /// no side for more players), every start unit's cell and earlier bases taken; then up to
    /// <paramref name="workers"/> <see cref="WorkerSpots"/>. Used once at match start, so it allocates.
    /// </summary>
    /// <param name="grid">The match's nav grid.</param>
    /// <param name="data">The match's data (building, unit and resource types).</param>
    /// <param name="buildings">The building store (read only: <see cref="BuildingStore.Fits"/>).</param>
    /// <param name="nodeAlive">The node store's <c>Alive</c>.</param>
    /// <param name="nodeType">The node store's <c>TypeId</c>.</param>
    /// <param name="nodeCell">The node store's <c>Cell</c>.</param>
    /// <param name="factionOf">Per player, the faction it plays (-1 for none).</param>
    /// <param name="blocks">Per player, its army's start positions (meters).</param>
    /// <param name="workers">Workers wanted per player.</param>
    /// <param name="maxRadius">Largest unit radius, for the stand-in block (as <see cref="StartLayout.Block"/>).</param>
    /// <remarks>Workers stand on the hall's side facing the nearest gold mine (<see cref="NearestMine"/>), or the block without one.</remarks>
    public static StartBasePlan Plan(NavGrid grid, GameData data, BuildingStore buildings, ReadOnlySpan<bool> nodeAlive,
        ReadOnlySpan<int> nodeType, ReadOnlySpan<int> nodeCell, int[] factionOf, Vector2[][] blocks, int workers, float maxRadius)
    {
        NavGrid g = grid;
        int players = blocks.Length;
        var plan = new StartBasePlan(players);
        var taken = new bool[g.Width * g.Height];
        foreach (Vector2[] block in blocks)
            foreach (Vector2 v in block)
                if (g.WorldToCell(v, out int x, out int y)) taken[y * g.Width + x] = true;
        for (int p = 0; p < players; p++)
        {
            int faction = p < factionOf.Length ? factionOf[p] : -1;
            plan.HallType[p] = faction < 0 ? -1 : BuildingOfSlot(data, faction, BuildingSlot.TownHall);
            plan.WorkerType[p] = faction < 0 ? -1 : UnitOfSlot(data, faction, UnitSlot.Worker);
            plan.HallAnchor[p] = -1;
            plan.Workers[p] = Array.Empty<Vector2>();
            Vector2[] block = blocks[p].Length > 0 ? blocks[p] : StartLayout.Block(g, 1, west: p == 0, maxRadius);
            if (plan.HallType[p] < 0 || block.Length == 0 || !g.WorldToCell(block[0], out int bx, out int by)) continue;
            Vector2 near = Vector2.Zero;
            foreach (Vector2 v in block) near += v;
            near /= block.Length;
            int mid = g.Width / 2;
            int minX = p == 1 ? mid + StartLayout.HalfGapCells : 0;
            int maxX = p == 0 ? mid - StartLayout.HalfGapCells - 1 : g.Width - 1;
            int anchor = HallSpot(g, buildings, data.Buildings, plan.HallType[p], near, g.LevelAt(bx, by), taken, minX, maxX);
            if (anchor < 0) continue;
            plan.HallAnchor[p] = anchor;
            BuildingDef def = data.Buildings[plan.HallType[p]];
            if (plan.WorkerType[p] >= 0 && workers > 0)
            {
                var spots = new Vector2[workers];
                Vector2 toward = NearestMine(g, data.Resources, nodeAlive, nodeType, nodeCell, FootprintCenter(g, def, anchor), out Vector2 mine)
                    ? mine : near;
                int n = WorkerSpots(g, def, anchor, workers, taken, spots, toward);
                plan.Workers[p] = spots.AsSpan(0, n).ToArray();
            }
            MarkTaken(g, def, anchor, taken);
        }
        return plan;
    }

    /// <summary>True if a building of <paramref name="typeId"/> anchored at <paramref name="anchor"/> is a start-base spot on <paramref name="level"/> (see the remarks).</summary>
    /// <param name="taken">Per nav cell, true where another base or a start unit stands; null for none.</param>
    /// <param name="minX">Lowest cell column the footprint's ring may use.</param>
    /// <param name="maxX">Highest cell column the footprint's ring may use.</param>
    public static bool IsHallSpot(NavGrid grid, BuildingStore buildings, ImmutableArray<BuildingDef> defs, int typeId, int anchor, int level,
        bool[]? taken, int minX = 0, int maxX = int.MaxValue)
    {
        NavGrid g = grid;
        if ((uint)typeId >= (uint)defs.Length || !buildings.Fits(typeId, anchor)) return false;
        int w = g.Width, x0 = anchor % w, y0 = anchor / w;
        if (g.LevelAt(x0, y0) != level) return false;
        BuildingDef def = defs[typeId];
        if (x0 - 1 < minX || x0 + def.FootprintWidth > maxX) return false;
        for (int y = y0 - 1; y <= y0 + def.FootprintHeight; y++)
        {
            for (int x = x0 - 1; x <= x0 + def.FootprintWidth; x++)
            {
                // Off the map counts as blocked (FlagsAt), so a footprint against the border has no full ring.
                if ((g.FlagsAt(x, y) & NavFlags.Blocked) != 0) return false;
                if (taken != null && taken[y * w + x]) return false;
            }
        }
        return true;
    }

    /// <summary>The anchor cell of the start-base spot nearest <paramref name="near"/> (meters) on <paramref name="level"/>, or -1 if none fits.</summary>
    /// <param name="taken">Per nav cell, true where another base or a start unit stands; null for none.</param>
    /// <param name="minX">Lowest cell column the footprint's ring may use.</param>
    /// <param name="maxX">Highest cell column the footprint's ring may use.</param>
    public static int HallSpot(NavGrid grid, BuildingStore buildings, ImmutableArray<BuildingDef> defs, int typeId, Vector2 near, int level,
        bool[]? taken, int minX = 0, int maxX = int.MaxValue)
    {
        if ((uint)typeId >= (uint)defs.Length) return -1;
        NavGrid g = grid;
        BuildingDef def = defs[typeId];
        var mapCenter = new Vector2(g.Width, g.Height) * (MapConstants.CellSize / 2f);
        int best = -1;
        float bestD = float.PositiveInfinity, bestTie = float.PositiveInfinity;
        for (int cell = 0; cell < g.Width * g.Height; cell++)
        {
            if (!IsHallSpot(g, buildings, defs, typeId, cell, level, taken, minX, maxX)) continue;
            Vector2 c = FootprintCenter(g, def, cell);
            float d = Vector2.Distance(c, near), tie = Vector2.Distance(c, mapCenter);
            if (d < bestD || (d == bestD && tie < bestTie))
            {
                best = cell;
                bestD = d;
                bestTie = tie;
            }
        }
        return best;
    }

    /// <summary>
    /// Up to <paramref name="count"/> worker positions (cell centres, meters) round the footprint anchored at
    /// <paramref name="anchor"/>: one per passable cell not in <paramref name="taken"/>, nearest ring first; within a ring
    /// the cells nearest <paramref name="toward"/> (meters) first, ties to the lower cell index. The cells used are marked
    /// in <paramref name="taken"/>; returns how many were written to <paramref name="spots"/>. Allocates one ring's list.
    /// </summary>
    public static int WorkerSpots(NavGrid grid, BuildingDef def, int anchor, int count, bool[] taken, Span<Vector2> spots, Vector2 toward)
    {
        int w = grid.Width, ax = anchor % w, ay = anchor / w;
        int want = Math.Min(count, spots.Length), n = 0;
        for (int ring = 1; n < want && ring < Math.Max(grid.Width, grid.Height); ring++)
        {
            int x0 = ax - ring, y0 = ay - ring, x1 = ax + def.FootprintWidth - 1 + ring, y1 = ay + def.FootprintHeight - 1 + ring;
            var cells = new System.Collections.Generic.List<(float D, int Cell)>();
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (x != x0 && x != x1 && y != y0 && y != y1) continue; // the ring only
                    if (!grid.IsPassable(x, y) || taken[y * w + x]) continue;
                    cells.Add((Vector2.DistanceSquared(grid.CellCenter(x, y), toward), y * w + x));
                }
            }
            cells.Sort();
            for (int k = 0; k < cells.Count && n < want; k++)
            {
                int c = cells[k].Cell;
                taken[c] = true;
                spots[n++] = grid.CellCenter(c % w, c / w);
            }
        }
        return n;
    }

    /// <summary>The footprint centre (meters) of the live gold-kind node nearest <paramref name="from"/> (centre to centre, ties to the lower slot); false if there is none.</summary>
    public static bool NearestMine(NavGrid grid, ImmutableArray<ResourceDef> defs, ReadOnlySpan<bool> alive, ReadOnlySpan<int> type,
        ReadOnlySpan<int> cell, Vector2 from, out Vector2 center)
    {
        center = default;
        NavGrid g = grid;
        float best = float.PositiveInfinity;
        int n = Math.Min(alive.Length, Math.Min(type.Length, cell.Length));
        for (int i = 0; i < n; i++)
        {
            if (!alive[i]) continue;
            ResourceDef def = defs[type[i]];
            if (def.Resource != ResourceKind.Gold) continue;
            var c = new Vector2((cell[i] % g.Width + def.FootprintWidth * 0.5f) * MapConstants.CellSize,
                (cell[i] / g.Width + def.FootprintHeight * 0.5f) * MapConstants.CellSize);
            float d = Vector2.DistanceSquared(c, from);
            if (d < best)
            {
                best = d;
                center = c;
            }
        }
        return best < float.PositiveInfinity;
    }

    /// <summary>Marks the footprint anchored at <paramref name="anchor"/> and its 8-ring in <paramref name="taken"/>, so the next base keeps clear of it.</summary>
    public static void MarkTaken(NavGrid grid, BuildingDef def, int anchor, bool[] taken)
    {
        int w = grid.Width, x0 = anchor % w, y0 = anchor / w;
        for (int y = y0 - 1; y <= y0 + def.FootprintHeight; y++)
            for (int x = x0 - 1; x <= x0 + def.FootprintWidth; x++)
                if (grid.InBounds(x, y)) taken[y * w + x] = true;
    }

    /// <summary>The centre (meters) of a footprint of <paramref name="def"/> anchored at <paramref name="anchor"/>.</summary>
    public static Vector2 FootprintCenter(NavGrid grid, BuildingDef def, int anchor) =>
        new((anchor % grid.Width + def.FootprintWidth * 0.5f) * MapConstants.CellSize,
            (anchor / grid.Width + def.FootprintHeight * 0.5f) * MapConstants.CellSize);
}
