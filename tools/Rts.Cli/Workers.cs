using System;
using System.Numerics;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Cli;

/// <summary>The CLI's economy scenario (<c>run --workers N</c>, M3-2): per player a Town Hall beside its start block and N workers gathering.</summary>
/// <remarks>
/// RNG-free and through public sim API and <see cref="Command"/> factories only, so a replay rebuilds it.
/// Player p plays faction <c>p % factions</c> (in id order). Its Town Hall (the faction's <c>town_hall</c>
/// slot) goes on the open 4 x 4 spot whose center is nearest its start block's center, ties toward the
/// map centre and then to the lower anchor cell; a spot is open when <see cref="BuildingStore.Fits"/> holds
/// and no march unit starts inside it or on the ring of cells round it. The workers (the faction's
/// <c>worker</c> slot) spawn one per cell on the passable cells nearest the Town Hall's ring, outside the
/// start block's spots. Once they exist, odd ones are ordered to the nearest gold mine and even ones to
/// the nearest tree (by center distance, ties to the lower slot); the sim redirects an unexposed tree to
/// an exposed one.
/// </remarks>
internal static class Workers
{
    /// <summary>Largest <c>--workers</c> (per player).</summary>
    public const int MaxWorkers = 200;

    /// <summary>
    /// Enqueues each player's Town Hall and workers after the march spawns (<paramref name="block"/> per player);
    /// returns which unit slots the workers will take. Spawns apply in (player, sequence) order into a fresh
    /// store, so player p's march takes the next block of slots, then its workers.
    /// </summary>
    public static bool[] EnqueueSetup(Simulation sim, int workers, int players, Vector2[][] block)
    {
        var isWorker = new bool[sim.World.Units.Capacity];
        int slot = 0;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        var taken = new bool[g.Width * g.Height];
        for (int p = 0; p < players; p++)
            foreach (Vector2 v in block[p])
                if (g.WorldToCell(v, out int x, out int y)) taken[y * g.Width + x] = true;

        for (int p = 0; p < players; p++)
        {
            slot += block[p].Length;
            int faction = p % w.Data.Factions.Length;
            int hall = FindBuilding(w.Data, faction, BuildingSlot.TownHall);
            int worker = FindUnit(w.Data, faction, UnitSlot.Worker);
            if (hall < 0 || worker < 0) continue;
            BuildingDef def = w.Data.Buildings[hall];
            int anchor = HallSpot(w, def, hall, Mean(block[p]), taken);
            if (anchor < 0) continue;
            sim.Enqueue(Command.SpawnBuilding(p, hall, g.CellCenter(anchor % g.Width, anchor / g.Width)));
            for (int y = anchor / g.Width - 1; y <= anchor / g.Width + def.FootprintHeight; y++)
                for (int x = anchor % g.Width - 1; x <= anchor % g.Width + def.FootprintWidth; x++)
                    if (g.InBounds(x, y)) taken[y * g.Width + x] = true;
            int spawned = SpawnWorkers(sim, p, worker, workers, anchor, def, taken);
            for (int k = 0; k < spawned; k++) isWorker[slot++] = true;
        }
        return isWorker;
    }

    /// <summary>Orders the setup's workers (<paramref name="isWorker"/> by slot) to gather: odd slots the nearest gold mine, even slots the nearest tree.</summary>
    public static void EnqueueGathers(Simulation sim, bool[] isWorker)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!isWorker[i] || !u.Alive[i]) continue;
            ResourceKind kind = i % 2 == 1 ? ResourceKind.Gold : ResourceKind.Wood;
            int node = NearestNode(w, u.Position[i], kind);
            if (node < 0) node = NearestNode(w, u.Position[i], kind == ResourceKind.Gold ? ResourceKind.Wood : ResourceKind.Gold);
            if (node < 0) continue;
            int cell = w.Resources.Cell[node];
            sim.Enqueue(Command.Gather(u.Owner[i], new EntityHandle(i, u.Generation[i]), w.NavGrid.CellCenter(cell % w.NavGrid.Width, cell / w.NavGrid.Width)));
        }
    }

    private static int SpawnWorkers(Simulation sim, int player, int type, int count, int anchor, BuildingDef def, bool[] taken)
    {
        NavGrid g = sim.World.NavGrid;
        int spawned = 0;
        // Rings of cells round the footprint, nearest first; one worker per free passable cell.
        for (int ring = 1; spawned < count && ring < Math.Max(g.Width, g.Height); ring++)
        {
            int x0 = anchor % g.Width - ring, y0 = anchor / g.Width - ring;
            int x1 = anchor % g.Width + def.FootprintWidth - 1 + ring, y1 = anchor / g.Width + def.FootprintHeight - 1 + ring;
            for (int y = y0; y <= y1 && spawned < count; y++)
            {
                for (int x = x0; x <= x1 && spawned < count; x++)
                {
                    if (x != x0 && x != x1 && y != y0 && y != y1) continue; // the ring only
                    // Ring 1 is the hall's own margin (marked taken above); farther out, skip other players' spots.
                    if (!g.IsPassable(x, y) || (ring > 1 && taken[y * g.Width + x])) continue;
                    taken[y * g.Width + x] = true;
                    sim.Enqueue(Command.SpawnUnit(player, type, g.CellCenter(x, y)));
                    spawned++;
                }
            }
        }
        return spawned;
    }

    private static int HallSpot(World w, BuildingDef def, int type, Vector2 near, bool[] taken)
    {
        NavGrid g = w.NavGrid;
        var mapCenter = new Vector2(g.Width, g.Height) * (MapConstants.CellSize / 2f);
        int best = -1;
        float bestD = float.PositiveInfinity, bestTie = float.PositiveInfinity;
        for (int cell = 0; cell < g.Width * g.Height; cell++)
        {
            if (!w.Buildings.Fits(type, cell) || Covers(g, cell, def, taken)) continue;
            Vector2 c = Center(g, cell, def);
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

    /// <summary>True if a taken cell lies in the footprint or on the ring round it.</summary>
    private static bool Covers(NavGrid g, int anchor, BuildingDef def, bool[] taken)
    {
        for (int y = anchor / g.Width - 1; y <= anchor / g.Width + def.FootprintHeight; y++)
            for (int x = anchor % g.Width - 1; x <= anchor % g.Width + def.FootprintWidth; x++)
                if (g.InBounds(x, y) && taken[y * g.Width + x]) return true;
        return false;
    }

    private static int NearestNode(World w, Vector2 from, ResourceKind kind)
    {
        ResourceStore r = w.Resources;
        int best = -1;
        float bestD2 = float.PositiveInfinity;
        for (int n = 0; n < r.Capacity; n++)
        {
            if (!r.Alive[n]) continue;
            ResourceDef def = w.Data.Resources[r.TypeId[n]];
            if (def.Resource != kind) continue;
            int cell = r.Cell[n];
            var c = new Vector2((cell % w.NavGrid.Width + def.FootprintWidth * 0.5f) * MapConstants.CellSize,
                (cell / w.NavGrid.Width + def.FootprintHeight * 0.5f) * MapConstants.CellSize);
            float d2 = Vector2.DistanceSquared(c, from);
            if (d2 < bestD2)
            {
                best = n;
                bestD2 = d2;
            }
        }
        return best;
    }

    private static Vector2 Center(NavGrid g, int anchor, BuildingDef def) =>
        new((anchor % g.Width + def.FootprintWidth * 0.5f) * MapConstants.CellSize, (anchor / g.Width + def.FootprintHeight * 0.5f) * MapConstants.CellSize);

    private static Vector2 Mean(Vector2[] points)
    {
        Vector2 sum = Vector2.Zero;
        foreach (Vector2 v in points) sum += v;
        return points.Length == 0 ? sum : sum / points.Length;
    }

    private static int FindBuilding(GameData data, int faction, BuildingSlot slot)
    {
        for (int b = 0; b < data.Buildings.Length; b++)
            if (data.Buildings[b].Faction == faction && data.Buildings[b].Slot == slot) return b;
        return -1;
    }

    private static int FindUnit(GameData data, int faction, UnitSlot slot)
    {
        for (int t = 0; t < data.Units.Length; t++)
            if (data.Units[t].Faction == faction && data.Units[t].Slot == slot) return t;
        return -1;
    }
}
