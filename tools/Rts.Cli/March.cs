using System;
using System.Numerics;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Cli;

/// <summary>The CLI's scenario: each player's army spawns in its debug start block and marches to the far side of the map.</summary>
/// <remarks>
/// Everything here is RNG-free and goes through public sim API and <see cref="Command"/> factories
/// only, so a replay of the run rebuilds it exactly. Player 0 uses the game's west debug start block
/// (<see cref="StartLayout.Block"/>), player 1 the east one; with two players the units split
/// ceil/floor. Unit types go round-robin over every type in the data, in id order. Player 0's goal
/// is the passable cell with the greatest path cost from the middle of the west map edge (the
/// passable cell nearest <c>(1, height / 2)</c>), ties to the lowest cell index, so it lies on the
/// far east side; player 1's mirrors it from the east edge. Two armies cross through the centre.
/// </remarks>
internal static class March
{
    /// <summary>Each player's spawn points in its start block (as many of its share of <paramref name="units"/> as fit).</summary>
    public static Vector2[][] Blocks(Simulation sim, int units, int players)
    {
        float maxRadius = 0f;
        foreach (UnitDef def in sim.World.Data.Units) maxRadius = MathF.Max(maxRadius, def.Radius);
        var blocks = new Vector2[players][];
        for (int p = 0; p < players; p++)
        {
            int count = units / players + (p < units % players ? 1 : 0);
            blocks[p] = StartLayout.Block(sim.World.NavGrid, count, west: p == 0, maxRadius);
        }
        return blocks;
    }

    /// <summary>Enqueues one <see cref="Command.SpawnUnit"/> per point of each player's block; returns how many were enqueued.</summary>
    public static int EnqueueSpawns(Simulation sim, Vector2[][] blocks)
    {
        GameData data = sim.World.Data;
        int spawned = 0;
        for (int p = 0; p < blocks.Length; p++)
        {
            Vector2[] block = blocks[p];
            for (int k = 0; k < block.Length; k++)
                sim.Enqueue(Command.SpawnUnit(p, k % data.Units.Length, block[k]));
            spawned += block.Length;
        }
        return spawned;
    }

    /// <summary>Each player's goal cell (index into the nav grid): the passable cell farthest from the middle of its own map edge.</summary>
    public static int[] GoalCells(NavGrid g, int players)
    {
        var goals = new int[players];
        for (int p = 0; p < players; p++)
        {
            int edgeX = p == 0 ? 1 : g.Width - 2; // just inside the blocked border ring
            goals[p] = FarthestCell(g, FlowField.NearestPassable(g, g.Height / 2 * g.Width + edgeX));
        }
        return goals;
    }

    /// <summary>Enqueues one <see cref="Command.Move"/> per live unit, in slot order, to its owner's goal cell centre; slots marked in <paramref name="skip"/> (the economy's workers) stay.</summary>
    public static void EnqueueMoves(Simulation sim, int[] goalCells, bool[] skip)
    {
        NavGrid g = sim.World.NavGrid;
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || (i < skip.Length && skip[i])) continue;
            int goal = goalCells[u.Owner[i]];
            if (goal < 0) continue;
            Vector2 target = g.CellCenter(goal % g.Width, goal / g.Width);
            sim.Enqueue(Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), target));
        }
    }

    /// <summary>The passable cell with the greatest finite path cost from <paramref name="fromCell"/>, ties to the lowest index.</summary>
    public static int FarthestCell(NavGrid g, int fromCell)
    {
        FlowField field = FlowField.Build(g, fromCell);
        int best = fromCell;
        float far = -1f;
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            float cost = field.CostAt(c);
            if (float.IsPositiveInfinity(cost) || cost <= far) continue;
            far = cost;
            best = c;
        }
        return best;
    }
}
