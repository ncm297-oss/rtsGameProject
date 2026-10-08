using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Tests;

/// <summary>M1-7: random mixes of every unit-order kind, queued and not, for determinism and replay tests.</summary>
public static class OrderMix
{
    /// <summary>
    /// A 2-player sim with <paramref name="units"/> mixed units spawned near the center (applied
    /// before returning), and the passable cells within <paramref name="reach"/> path cells of the
    /// center, from which <see cref="Issue"/> picks targets.
    /// </summary>
    public static Simulation Spawn(ulong seed, int units, float reach, out List<int> cells, int perTick = 6, bool combat = true)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: units, CommandCapacity: units + 2 * perTick + 8) with { Combat = combat });
        cells = Cells(sim.World.NavGrid, reach);
        var rng = new SimRng(seed, 41);
        SpawnInto(sim, units, cells, ref rng);
        sim.Tick();
        sim.Tick();
        return sim;
    }

    /// <summary>The passable cells within <paramref name="reach"/> path cells of the central cell.</summary>
    public static List<int> Cells(NavGrid g, float reach)
    {
        FlowField field = FlowField.Build(g, MoveScenario.CentralCell(g));
        var cells = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (field.CostAt(c) <= reach) cells.Add(c);
        return cells;
    }

    /// <summary>Enqueues <paramref name="units"/> spawns (owners alternating 0/1, mixed types) at random points of <paramref name="cells"/>.</summary>
    public static void SpawnInto(Simulation sim, int units, List<int> cells, ref SimRng rng)
    {
        NavGrid g = sim.World.NavGrid;
        for (int i = 0; i < units; i++)
        {
            Vector2 corner = MoveScenario.Center(g, cells[rng.NextInt(0, cells.Count)]) - new Vector2(MapConstants.CellSize / 2);
            sim.Enqueue(Command.SpawnUnit(i % 2, i % TestSim.UnitTypeCount, corner + new Vector2(0.05f + rng.NextFloat() * 1.9f, 0.05f + rng.NextFloat() * 1.9f)));
        }
    }

    /// <summary>
    /// Enqueues up to <paramref name="count"/> random unit orders: Move, AttackMove, Stop and
    /// HoldPosition, about half queued, to random target cells; a few to stale handles, from the wrong
    /// player, or off the map (all dropped by the sim, still logged).
    /// </summary>
    public static void Issue(Simulation sim, ref SimRng rng, List<int> cells, int count)
    {
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        int players = sim.World.Config.PlayerCount;
        for (int n = 0; n < count; n++)
        {
            int slot = rng.NextInt(0, u.Capacity);
            int generation = u.Generation[slot] + (rng.NextInt(0, 40) == 0 ? 1 : 0);
            int player = u.Owner[slot];
            if (rng.NextInt(0, 40) == 0) player = (player + 1) % players;
            var unit = new EntityHandle(slot, generation);
            bool queued = rng.NextInt(0, 2) == 0;
            Vector2 target = MoveScenario.Center(g, cells[rng.NextInt(0, cells.Count)]) + new Vector2(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f);
            if (rng.NextInt(0, 50) == 0) target = new Vector2(-5f, 3f);
            int roll = rng.NextInt(0, 100);
            Command c = roll switch
            {
                < 40 => Command.Move(player, unit, target, queued),
                < 70 => Command.AttackMove(player, unit, target, queued),
                < 85 => Command.Stop(player, unit, queued),
                _ => Command.HoldPosition(player, unit, queued),
            };
            sim.Enqueue(c);
        }
    }
}
