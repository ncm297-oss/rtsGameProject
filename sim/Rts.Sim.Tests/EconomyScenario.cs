using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

/// <summary>M3-2: a Keep and gathering workers on a generated map with resources, next to whatever else a test runs.</summary>
public static class EconomyScenario
{
    /// <summary>
    /// Places a Keep for <paramref name="player"/> on the open spot whose center is 10-16 m from gold mine
    /// <paramref name="mineIndex"/> (the n-th live mine in slot order), nearest that band's inside, then
    /// <paramref name="workers"/> workers on free cells round it, and orders them to gather (odd ones the
    /// mine, even ones the nearest tree). Applies everything (three ticks); returns the worker handles.
    /// </summary>
    public static EntityHandle[] Setup(Simulation sim, int workers, int player = 0, int mineIndex = 0)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int mine = NthMine(w, mineIndex);
        Assert.True(mine >= 0, "no gold mine on the map");
        Vector2 mineCenter = Center(g, w.Resources.Cell[mine], 2, 2);
        BuildingDef keep = w.Data.Buildings[GatherMaps.Keep];
        int anchor = -1;
        float best = float.PositiveInfinity;
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            float d = Vector2.Distance(Center(g, c, keep.FootprintWidth, keep.FootprintHeight), mineCenter);
            if (d < 10f || d > 16f || d >= best) continue;
            if (!w.Buildings.Fits(GatherMaps.Keep, c) || UnitNear(w, c, keep)) continue;
            best = d;
            anchor = c;
        }
        Assert.True(anchor >= 0, "no spot for a Keep near the mine");
        sim.Enqueue(Command.SpawnBuilding(player, GatherMaps.Keep, g.CellCenter(anchor % g.Width, anchor / g.Width)));

        var spots = new List<Vector2>();
        int ax = anchor % g.Width, ay = anchor / g.Width;
        for (int ring = 1; spots.Count < workers && ring < 30; ring++)
        {
            for (int y = ay - ring; y <= ay + 3 + ring && spots.Count < workers; y++)
                for (int x = ax - ring; x <= ax + 3 + ring && spots.Count < workers; x++)
                {
                    if (x != ax - ring && x != ax + 3 + ring && y != ay - ring && y != ay + 3 + ring) continue;
                    if (!g.IsPassable(x, y) || UnitIn(w, x, y)) continue;
                    spots.Add(g.CellCenter(x, y));
                }
        }
        Assert.Equal(workers, spots.Count);
        UnitStore u = w.Units;
        var before = (bool[])u.Alive.Clone();
        foreach (Vector2 p in spots) sim.Enqueue(Command.SpawnUnit(player, GatherMaps.Laborer, p));
        sim.Tick();
        sim.Tick();
        var handles = new List<EntityHandle>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !before[i]) handles.Add(new EntityHandle(i, u.Generation[i]));
        Assert.Equal(workers, handles.Count);
        int tree = NearestTree(w, mineCenter);
        for (int k = 0; k < handles.Count; k++)
        {
            int node = k % 2 == 1 || tree < 0 ? mine : tree;
            int cell = w.Resources.Cell[node];
            sim.Enqueue(Command.Gather(player, handles[k], g.CellCenter(cell % g.Width, cell / g.Width)));
        }
        sim.Tick();
        return handles.ToArray();
    }

    private static int NthMine(World w, int n)
    {
        for (int k = 0; k < w.Resources.Capacity; k++)
            if (w.Resources.Alive[k] && w.Data.Resources[w.Resources.TypeId[k]].Resource == ResourceKind.Gold && n-- == 0) return k;
        return -1;
    }

    private static int NearestTree(World w, Vector2 from)
    {
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int k = 0; k < w.Resources.Capacity; k++)
        {
            if (!w.Resources.Alive[k] || w.Data.Resources[w.Resources.TypeId[k]].Resource != ResourceKind.Wood) continue;
            float d = Vector2.Distance(Center(w.NavGrid, w.Resources.Cell[k], 1, 1), from);
            if (d < bestD) { bestD = d; best = k; }
        }
        return best;
    }

    private static bool UnitNear(World w, int anchor, BuildingDef def)
    {
        int ax = anchor % w.NavGrid.Width, ay = anchor / w.NavGrid.Width;
        for (int y = ay - 1; y <= ay + def.FootprintHeight; y++)
            for (int x = ax - 1; x <= ax + def.FootprintWidth; x++)
                if (UnitIn(w, x, y)) return true;
        return false;
    }

    private static bool UnitIn(World w, int x, int y)
    {
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && w.NavGrid.WorldToCell(u.Position[i], out int ux, out int uy) && ux == x && uy == y) return true;
        return false;
    }

    private static Vector2 Center(NavGrid g, int anchor, int fw, int fh) =>
        new((anchor % g.Width + fw * 0.5f) * MapConstants.CellSize, (anchor / g.Width + fh * 0.5f) * MapConstants.CellSize);
}
