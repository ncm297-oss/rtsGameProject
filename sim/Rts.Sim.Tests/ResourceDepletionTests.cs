using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M3-1 criterion 3: a depleted tree reopens its cell, cached flow fields through it go stale and
/// rebuild shorter, and a walking unit takes the new gap; deterministic (hash twins).
/// </summary>
public class ResourceDepletionTests
{
    // 24 x 16 flat map; a tree line at x = 12 from y = 1 to 12 leaves a gap at y = 13-14 (y = 15 is the border).
    private const int LineX = 12, LineTop = 1, LineBottom = 12, Row = 3;
    private const int StartX = 4, GoalX = 20;

    private static Simulation TreeLineSim(out EntityHandle[] line)
    {
        Simulation sim = NewSim(Flat(24, 16));
        line = new EntityHandle[LineBottom - LineTop + 1];
        for (int y = LineTop; y <= LineBottom; y++)
            line[y - LineTop] = Spawn(sim.World, Tree, LineX, y, TreeWood);
        return sim;
    }

    private static int Cell(NavGrid g, int x, int y) => y * g.Width + x;

    [Fact]
    public void CachedFieldBehindATreeLine_IsStaleAfterTheTreeFalls_AndRebuildsWithALowerCost()
    {
        Simulation sim = TreeLineSim(out EntityHandle[] line);
        NavGrid g = sim.World.NavGrid;
        FlowFieldCache cache = sim.World.FlowFields;
        int goal = Cell(g, GoalX, Row), start = Cell(g, StartX, Row);

        FlowField before = cache.Get(goal);
        float detour = before.CostAt(start);
        Assert.True(detour > GoalX - StartX + 8, $"detour cost {detour}"); // round the bottom of the line, 10 rows down
        Assert.Same(before, cache.PeekCached(goal));
        int builds = cache.BuildCount, version = g.Version;

        Assert.Equal(TreeWood, sim.World.Resources.Take(line[Row - LineTop], TreeWood));
        Assert.Equal(version + 1, g.Version);
        Assert.False(cache.Contains(goal));
        Assert.Null(cache.PeekCached(goal)); // stale fields are never handed out

        FlowField after = cache.Get(goal);
        Assert.Equal(builds + 1, cache.BuildCount);
        Assert.Equal(g.Version, after.Version);
        Assert.Equal(GoalX - StartX, after.CostAt(start)); // straight through the gap
        Assert.True(after.CostAt(start) < detour);
        Assert.NotEqual(FlowField.NoDirection, after.DirectionAt(Cell(g, LineX, Row)));
        Assert.Equal(float.PositiveInfinity, after.CostAt(Cell(g, LineX, Row + 1))); // its neighbors still stand
    }

    /// <summary>Runs one unit from the start cell to the goal; the tree on its row falls on <paramref name="fellAtTick"/> (-1: never). Returns the hash after every tick.</summary>
    private static ulong[] Walk(int fellAtTick, out int crossedAtRow, out int arrivedTick)
    {
        Simulation sim = TreeLineSim(out EntityHandle[] line);
        NavGrid g = sim.World.NavGrid;
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.SpawnUnit(0, 0, g.CellCenter(StartX, Row)));
        sim.Tick();
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), g.CellCenter(GoalX, Row)));
        var hashes = new ulong[1200];
        crossedAtRow = -1;
        arrivedTick = -1;
        for (int t = 0; t < hashes.Length; t++)
        {
            // Between ticks, as gathering will do it inside phase 4 (M3-2).
            if (t == fellAtTick) Assert.Equal(TreeWood, sim.World.Resources.Take(line[Row - LineTop], TreeWood));
            sim.Tick();
            hashes[t] = sim.StateHash();
            if (crossedAtRow < 0 && g.WorldToCell(u.Position[0], out int x, out int y) && x == LineX) crossedAtRow = y;
            if (arrivedTick < 0 && t > 1 && u.State[0] == UnitState.Idle) arrivedTick = t;
        }
        Assert.Equal(0, MoveScenario.FirstUnitOnBlockedGround(sim.World) + 1); // -1: nobody stands in a tree
        Assert.True(Vector2.Distance(u.Position[0], g.CellCenter(GoalX, Row)) < 1f, $"unit ended at {u.Position[0]}");
        return hashes;
    }

    [Fact]
    public void UnitAcrossATreeLine_DetoursWhileItStands_AndWalksStraightThroughOnceItFallsMidWalk()
    {
        ulong[] standing = Walk(-1, out int detourRow, out int detourArrival);
        Assert.True(detourRow >= LineBottom + 1, $"crossed the line at row {detourRow}, inside the tree line");

        const int fell = 15;
        ulong[] felled = Walk(fell, out int gapRow, out int gapArrival);
        Assert.True(gapRow >= 0 && gapRow <= Row + 3, $"crossed at row {gapRow}, not through the fallen tree's gap");
        Assert.True(gapArrival > 0 && gapArrival < detourArrival, $"arrived at {gapArrival}, detour at {detourArrival}");

        // Hash twin every tick, and the fall itself is visible in the hash from that tick on.
        Assert.Equal(felled, Walk(fell, out _, out _));
        Assert.Equal(standing[..fell], felled[..fell]);
        Assert.NotEqual(standing[fell], felled[fell]);
    }
}
