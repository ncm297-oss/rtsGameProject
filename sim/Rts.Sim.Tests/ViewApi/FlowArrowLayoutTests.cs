using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M2-5 flow arrows: the listed arrows are exactly the current cached field's directions inside the window, nothing without a field, never stale.</summary>
public class FlowArrowLayoutTests
{
    private const float Cs = MapConstants.CellSize;

    private static NavGrid Grid(ulong seed) => new(TerrainHeightTests.GeneratedMap(seed));

    private static int PassableCellNear(NavGrid g, int x, int y) => FlowField.NearestPassable(g, y * g.Width + x);

    private static Vector2 CentreOf(NavGrid g, int cell) => g.CellCenter(cell % g.Width, cell / g.Width);

    /// <summary>Every window cell has an arrow iff the field gives it a direction, with that direction; nothing outside the window.</summary>
    private static void AssertMatchesField(FlowArrowLayout layout, FlowField field, NavGrid g)
    {
        var listed = new Dictionary<int, byte>();
        for (int k = 0; k < layout.Count; k++) listed.Add(layout.Cells[k], layout.Directions[k]);
        for (int y = 0; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                int cell = y * g.Width + x;
                bool inWindow = x >= layout.MinX && x < layout.MaxX && y >= layout.MinY && y < layout.MaxY;
                byte d = field.DirectionAt(cell);
                if (inWindow && d != FlowField.NoDirection)
                {
                    Assert.True(listed.TryGetValue(cell, out byte got), $"cell ({x}, {y}) has direction {d} but no arrow");
                    Assert.Equal(d, got);
                }
                else
                {
                    Assert.False(listed.ContainsKey(cell), $"cell ({x}, {y}) has an arrow (in window {inWindow}, direction {d})");
                }
            }
        }
    }

    [Fact]
    public void DirectionVector_IsTheSimsOffsetTable_ForAll8Directions()
    {
        // FlowField: 0 = east, then clockwise with +y down the rows; odd = diagonal.
        var want = new (int X, int Y)[] { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) };
        for (int d = 0; d < FlowField.DirectionCount; d++)
        {
            Assert.Equal(want[d].X, FlowField.OffsetX(d));
            Assert.Equal(want[d].Y, FlowField.OffsetY(d));
            Vector2 v = FlowArrowLayout.DirectionVector(d);
            Assert.Equal(1f, v.Length(), 5);
            Assert.Equal(Vector2.Normalize(new Vector2(want[d].X, want[d].Y)), v);
        }
    }

    [Fact]
    public void EveryArrow_PointsAtTheCheaperNeighbourTheSimWouldWalkTo_AndAll8DirectionsOccur()
    {
        NavGrid g = Grid(1);
        var cache = new FlowFieldCache(g);
        int goal = PassableCellNear(g, 64, 64);
        FlowField field = cache.Get(goal);
        var layout = new FlowArrowLayout(g.Width); // window covers the whole map from its centre
        Assert.True(layout.Refresh(cache, g, goal, CentreOf(g, goal)));
        Assert.Equal(goal, layout.MarkedCell);
        Assert.True(layout.Count > 1000);
        var seen = new bool[FlowField.DirectionCount];
        for (int k = 0; k < layout.Count; k++)
        {
            int c = layout.Cells[k], d = layout.Directions[k];
            seen[d] = true;
            Vector2 v = FlowArrowLayout.DirectionVector(d);
            // The arrow, one cell long, lands in the neighbour the field steps to, and that cell is closer to the goal.
            Vector2 tip = CentreOf(g, c) + v * Cs * ((d & 1) == 1 ? MathF.Sqrt(2f) : 1f);
            Assert.True(g.WorldToCell(tip, out int tx, out int ty));
            int next = ty * g.Width + tx;
            Assert.Equal(c + FlowField.OffsetY(d) * g.Width + FlowField.OffsetX(d), next);
            Assert.True(field.CostAt(next) < field.CostAt(c), $"arrow at {c} points uphill");
            Assert.True(g.IsPassable(tx, ty));
        }
        Assert.All(seen, Assert.True);
        // The goal cell itself has no arrow.
        Assert.False(layout.Cells.Contains(goal));
    }

    [Theory]
    [InlineData(64, 64)]   // middle
    [InlineData(0, 0)]     // map corners: the window is clipped
    [InlineData(127, 0)]
    [InlineData(0, 127)]
    [InlineData(127, 127)]
    [InlineData(3, 90)]    // an edge
    public void Arrows_MatchDirectionAt_InTheWindow_ClippedAtTheMapEdges(int fx, int fy)
    {
        NavGrid g = Grid(4);
        var cache = new FlowFieldCache(g);
        int goal = PassableCellNear(g, 30, 40);
        FlowField field = cache.Get(goal);
        var layout = new FlowArrowLayout();
        layout.Refresh(cache, g, goal, new Vector2((fx + 0.5f) * Cs, (fy + 0.5f) * Cs));
        int half = FlowArrowLayout.DefaultWindow / 2;
        Assert.Equal(Math.Max(fx - half, 0), layout.MinX);
        Assert.Equal(Math.Max(fy - half, 0), layout.MinY);
        Assert.Equal(Math.Min(fx + half, g.Width), layout.MaxX);
        Assert.Equal(Math.Min(fy + half, g.Height), layout.MaxY);
        Assert.True(layout.Count <= FlowArrowLayout.DefaultWindow * FlowArrowLayout.DefaultWindow);
        AssertMatchesField(layout, field, g);
    }

    [Fact]
    public void FocusOffTheMapOrNonFinite_ClampsOntoTheMap()
    {
        NavGrid g = Grid(1);
        var cache = new FlowFieldCache(g);
        var layout = new FlowArrowLayout();
        layout.Refresh(cache, g, -1, new Vector2(-500f, 1e9f));
        Assert.Equal((0, g.Height - 21, 20, g.Height), (layout.MinX, layout.MinY, layout.MaxX, layout.MaxY));
        layout.Refresh(cache, g, -1, new Vector2(float.NaN, float.PositiveInfinity));
        Assert.Equal((0, g.Height - 21, 20, g.Height), (layout.MinX, layout.MinY, layout.MaxX, layout.MaxY));
    }

    [Fact]
    public void NoCachedField_OrNoGoal_ListsNothing()
    {
        NavGrid g = Grid(1);
        var cache = new FlowFieldCache(g);
        int goal = PassableCellNear(g, 64, 64);
        var layout = new FlowArrowLayout();
        Vector2 focus = CentreOf(g, goal);
        layout.Refresh(cache, g, goal, focus);
        Assert.Equal(0, layout.Count);
        Assert.False(layout.HasField);
        Assert.Equal(-1, layout.MarkedCell);
        cache.Get(goal);
        Assert.True(layout.Refresh(cache, g, goal, focus));
        Assert.True(layout.Count > 0);
        // No goal, or a goal off the map: nothing.
        Assert.True(layout.Refresh(cache, g, -1, focus));
        Assert.Equal(0, layout.Count);
        layout.Refresh(cache, g, g.Width * g.Height, focus);
        Assert.Equal(0, layout.Count);
        layout.Refresh(cache, g, int.MaxValue, focus);
        Assert.Equal(0, layout.Count);
    }

    [Fact]
    public void Refresh_RelistsOnlyWhenGoalFieldOrWindowChanges()
    {
        NavGrid g = Grid(1);
        var cache = new FlowFieldCache(g);
        int goal = PassableCellNear(g, 64, 64);
        cache.Get(goal);
        var layout = new FlowArrowLayout();
        Vector2 focus = CentreOf(g, goal);
        Assert.True(layout.Refresh(cache, g, goal, focus));
        int rebuilds = layout.Rebuilds;
        // Same cell, different point inside it: no relist.
        Assert.False(layout.Refresh(cache, g, goal, focus + new Vector2(0.4f, -0.4f)));
        Assert.Equal(rebuilds, layout.Rebuilds);
        // Next cell over: the window moved.
        Assert.True(layout.Refresh(cache, g, goal, focus + new Vector2(Cs, 0f)));
        // Invalidate (overlay turned on again) forces one relist.
        layout.Invalidate();
        Assert.True(layout.Refresh(cache, g, goal, focus + new Vector2(Cs, 0f)));
        Assert.False(layout.Refresh(cache, g, goal, focus + new Vector2(Cs, 0f)));
    }

    [Fact]
    public void AFieldGoneStale_OrEvicted_IsNeverDrawn_AndARebuildIsDrawnAgain()
    {
        NavGrid g = Grid(1);
        var cache = new FlowFieldCache(g, capacity: 32);
        int goal = PassableCellNear(g, 64, 64);
        cache.Get(goal);
        var layout = new FlowArrowLayout();
        Vector2 focus = CentreOf(g, goal);
        layout.Refresh(cache, g, goal, focus);
        Assert.True(layout.Count > 0);

        // Passability changed: the cached field is stale and PeekCached hides it.
        g.BumpVersionForTests();
        Assert.True(layout.Refresh(cache, g, goal, focus));
        Assert.Equal(0, layout.Count);
        Assert.False(layout.HasField);
        // Rebuilt at the new version: drawn again, matching.
        FlowField rebuilt = cache.Get(goal);
        Assert.True(layout.Refresh(cache, g, goal, focus));
        AssertMatchesField(layout, rebuilt, g);

        // Evicted by 32 other goals: the slot now holds another target, so nothing is drawn.
        int filled = 0;
        for (int c = 0; filled < 32; c += 97)
        {
            if (c == goal || !g.IsPassable(c % g.Width, c / g.Width)) continue;
            cache.Get(c);
            filled++;
        }
        Assert.False(cache.Contains(goal));
        Assert.True(layout.Refresh(cache, g, goal, focus));
        Assert.Equal(0, layout.Count);
        FlowField again = cache.Get(goal);
        Assert.True(layout.Refresh(cache, g, goal, focus));
        AssertMatchesField(layout, again, g);
    }

    [Fact]
    public void GoalOf_IsTheLowestLiveSelectedSlotWithAGoal()
    {
        var u = new UnitStore(8);
        var h = new EntityHandle[8];
        for (int i = 0; i < 8; i++) h[i] = u.Alloc();
        u.GoalCell[1] = -1;
        u.GoalCell[2] = 500;
        u.GoalCell[3] = 300;
        u.GoalCell[5] = 100;
        Assert.Equal(-1, FlowArrowLayout.GoalOf(ReadOnlySpan<EntityHandle>.Empty, u.Alive, u.Generation, u.GoalCell));
        Assert.Equal(-1, FlowArrowLayout.GoalOf(new[] { h[1], h[0] }, u.Alive, u.Generation, u.GoalCell)); // slot 0 never had a goal (Alloc sets -1)
        // Selection order doesn't matter, only the slot.
        Assert.Equal(300, FlowArrowLayout.GoalOf(new[] { h[5], h[3], h[1] }, u.Alive, u.Generation, u.GoalCell));
        Assert.Equal(500, FlowArrowLayout.GoalOf(new[] { h[5], h[3], h[2] }, u.Alive, u.Generation, u.GoalCell));
        // Dead or recycled handles are skipped.
        u.Free(h[2]);
        Assert.Equal(300, FlowArrowLayout.GoalOf(new[] { h[5], h[3], h[2] }, u.Alive, u.Generation, u.GoalCell));
        EntityHandle again = u.Alloc(); // slot 2 again, new generation
        u.GoalCell[again.Index] = 42;
        Assert.Equal(300, FlowArrowLayout.GoalOf(new[] { h[5], h[3], h[2] }, u.Alive, u.Generation, u.GoalCell));
        Assert.Equal(42, FlowArrowLayout.GoalOf(new[] { h[5], h[3], again }, u.Alive, u.Generation, u.GoalCell));
        // Slots outside the arrays are skipped, not thrown on.
        Assert.Equal(300, FlowArrowLayout.GoalOf(new[] { new EntityHandle(-1, 1), new EntityHandle(99, 1), h[3] }, u.Alive, u.Generation, u.GoalCell));
    }

    [Fact]
    public void DebugCounts_Moving_CountsLiveMovingUnitsOnly()
    {
        var alive = new[] { true, true, false, true, true };
        var state = new[] { UnitState.Moving, UnitState.Idle, UnitState.Moving, UnitState.Moving, UnitState.Idle };
        Assert.Equal(2, DebugCounts.Moving(alive, state));
        Assert.Equal(0, DebugCounts.Moving(ReadOnlySpan<bool>.Empty, state));
    }

    [Fact]
    public void ArrowGround_IsAtLeastTheTerrainUnderEveryArrowVertex_OnRampsToo()
    {
        // BUG-0084: arrows lifted 0.3 m above the centre's height dipped up to 9 cm into the steepest ramps.
        // Every vertex of the flat arrow (and each edge midpoint) must sit no lower than the terrain under it
        // minus 2 cm when drawn at ArrowGround (the view adds 0.3 m on top).
        int oldDips = 0, vertices = 0;
        float worst = float.MinValue;
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            Heightmap map = TerrainHeightTests.GeneratedMap(seed);
            var g = new NavGrid(map);
            for (int y = 0; y < g.Height; y++)
                for (int x = 0; x < g.Width; x++)
                {
                    if (!g.IsPassable(x, y)) continue;
                    Vector2 c = g.CellCenter(x, y);
                    for (int d = 0; d < 8; d++)
                    {
                        Vector2 v = FlowArrowLayout.DirectionVector(d), side = new(-v.Y, v.X);
                        float ground = FlowArrowLayout.ArrowGround(map, c, v), centre = TerrainHeight.At(map, c.X, c.Y);
                        Assert.True(ground >= centre);
                        Vector2[] shape =
                        {
                            c + v * FlowArrowLayout.ArrowTail + side * FlowArrowLayout.ArrowShaft, c + v * FlowArrowLayout.ArrowNeck + side * FlowArrowLayout.ArrowShaft,
                            c + v * FlowArrowLayout.ArrowNeck + side * FlowArrowLayout.ArrowHead, c + v * FlowArrowLayout.ArrowTip,
                            c + v * FlowArrowLayout.ArrowNeck - side * FlowArrowLayout.ArrowHead, c + v * FlowArrowLayout.ArrowNeck - side * FlowArrowLayout.ArrowShaft,
                            c + v * FlowArrowLayout.ArrowTail - side * FlowArrowLayout.ArrowShaft,
                        };
                        for (int k = 0; k < shape.Length; k++)
                        {
                            foreach (Vector2 p in new[] { shape[k], (shape[k] + shape[(k + 1) % shape.Length]) / 2 })
                            {
                                float t = TerrainHeight.At(map, p.X, p.Y);
                                vertices++;
                                worst = MathF.Max(worst, t - ground);
                                Assert.True(t <= ground + 0.02f, $"seed {seed} cell ({x}, {y}) dir {d}: ground {t} at {p} is {t - ground:F3} m above the arrow's base");
                                if (t > centre + 0.3f) oldDips++; // where the old rule (centre + 0.3 m) went under
                            }
                        }
                    }
                }
        }
        Assert.True(oldDips > 0, "no arrow point dips under the old rule: the maps no longer exercise steep ramps");
        Assert.True(worst <= 0.02f, $"worst {worst}");
    }
}
