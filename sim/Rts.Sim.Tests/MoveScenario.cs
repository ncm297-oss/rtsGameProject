using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Tests;

/// <summary>Builds move scenarios on the default generated map: units spawned on passable ground a bounded path away from a goal.</summary>
public static class MoveScenario
{
    /// <summary>The passable cell nearest the map center.</summary>
    public static int CentralCell(NavGrid g) => FlowField.NearestPassable(g, g.Height / 2 * g.Width + g.Width / 2);

    /// <summary>World position of a cell's center.</summary>
    public static Vector2 Center(NavGrid g, int cell) => g.CellCenter(cell % g.Width, cell / g.Width);

    /// <summary>A live unit's current handle.</summary>
    public static EntityHandle Handle(Simulation sim, int slot) => new(slot, sim.World.Units.Generation[slot]);

    /// <summary>
    /// A sim with <paramref name="units"/> units of mixed types (owners alternate 0/1), each at a random
    /// point of a random passable cell whose path cost to <paramref name="goalCell"/> is at most
    /// <paramref name="maxCost"/> cells. Spawns are applied before returning.
    /// </summary>
    public static Simulation Spawn(ulong seed, int units, float maxCost, out int goalCell, int capacity = 0)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2,
            UnitCapacity: Math.Max(units, capacity), CommandCapacity: 2 * Math.Max(units, capacity) + 8));
        NavGrid g = sim.World.NavGrid;
        goalCell = CentralCell(g);
        FlowField field = FlowField.Build(g, goalCell);
        var near = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (field.CostAt(c) <= maxCost) near.Add(c);
        var rng = new SimRng(seed, 31);
        for (int i = 0; i < units; i++)
        {
            int cell = near[rng.NextInt(0, near.Count)];
            // Stay 0.05 m inside the cell so float rounding can't put the spawn in a neighbor.
            var offset = new Vector2(0.05f + rng.NextFloat() * 1.9f, 0.05f + rng.NextFloat() * 1.9f);
            Vector2 corner = Center(g, cell) - new Vector2(MapConstants.CellSize / 2);
            sim.Enqueue(Command.SpawnUnit(i % 2, i % TestSim.UnitTypeCount, corner + offset));
        }
        sim.Tick();
        sim.Tick();
        Assert.Equal(units, sim.World.Units.Count);
        return sim;
    }

    /// <summary>Orders every live unit to <paramref name="target"/> with one Move per unit from its owner.</summary>
    public static void MoveAll(Simulation sim, Vector2 target)
    {
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.Move(u.Owner[i], Handle(sim, i), target));
    }

    /// <summary>The first live unit standing in a blocked cell or off the map, or -1.</summary>
    public static int FirstUnitOnBlockedGround(World w)
    {
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (!w.NavGrid.WorldToCell(u.Position[i], out int x, out int y) || !w.NavGrid.IsPassable(x, y)) return i;
        }
        return -1;
    }
}
