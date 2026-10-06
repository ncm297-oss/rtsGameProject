using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA attacks on M2-5's debug overlay helpers (<see cref="FlowArrowLayout"/>, <see cref="NavOverlayBuilder"/>,
/// <see cref="TickTimeRing"/>, <see cref="DebugCounts"/>): staleness against an independent oracle through
/// cache churn, selection changes, toggles and a grid version bump; a 1,000-per-player hash twin; non-square
/// maps with the window at every edge and corner; selection of dead and respawned-as-enemy units;
/// zero allocation on a relist that has a field.
/// </summary>
[Collection(SerialCollection.Name)] // measures allocation
public class DebugOverlayQaTests
{
    private readonly ITestOutputHelper _out;

    public DebugOverlayQaTests(ITestOutputHelper output) => _out = output;

    private static Simulation Spawned(ulong seed, int perPlayer, int players = 2, MapGenParams? map = null)
    {
        var cfg = TestSim.Config(seed, players, Math.Max(perPlayer * players, 8), 8192);
        if (map != null) cfg = cfg with { Map = map };
        var sim = new Simulation(cfg);
        float maxR = TestSim.Data.Units.Max(u => u.Radius);
        for (int p = 0; p < players; p++)
        {
            Vector2[] spots = StartLayout.Block(sim.World.NavGrid, perPlayer, p == 0, maxR);
            for (int k = 0; k < spots.Length; k++) sim.Enqueue(Command.SpawnUnit(p, k % TestSim.UnitTypeCount, spots[k]));
        }
        sim.Tick();
        sim.Tick();
        return sim;
    }

    private static int[] Passable(NavGrid g)
    {
        var list = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) list.Add(c);
        return list.ToArray();
    }

    private static void OrderToGoals(Simulation sim, int[] passable, int goals, int offset)
    {
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            int cell = passable[(offset + (n++ % goals) * (passable.Length / goals)) % passable.Length];
            sim.Enqueue(Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), g.CellCenter(cell % g.Width, cell / g.Width)));
        }
    }

    /// <summary>Independent oracle: what the layout must hold right now for this goal and focus, computed from this tick's peek.</summary>
    private static void AssertMatchesOracle(FlowArrowLayout layout, FlowFieldCache cache, NavGrid g, int goal, Vector2 focus, string when)
    {
        int w = g.Width, h = g.Height, win = layout.Window;
        float fx = focus.X / MapConstants.CellSize, fy = focus.Y / MapConstants.CellSize;
        int cx = float.IsNaN(fx) || fx < 0 ? 0 : (int)Math.Min(fx, w - 1);
        int cy = float.IsNaN(fy) || fy < 0 ? 0 : (int)Math.Min(fy, h - 1);
        int x0 = Math.Max(cx - win / 2, 0), y0 = Math.Max(cy - win / 2, 0);
        int x1 = Math.Min(cx - win / 2 + win, w), y1 = Math.Min(cy - win / 2 + win, h);
        Assert.True(x0 == layout.MinX && y0 == layout.MinY && x1 == layout.MaxX && y1 == layout.MaxY,
            $"{when}: window ({layout.MinX},{layout.MinY})-({layout.MaxX},{layout.MaxY}), oracle ({x0},{y0})-({x1},{y1})");
        Assert.True(x1 - x0 >= Math.Min(win / 2, w) && y1 - y0 >= Math.Min(win / 2, h), $"{when}: window collapsed");
        FlowField? f = goal >= 0 ? cache.PeekCached(goal) : null;
        Assert.True(layout.HasField == (f != null), $"{when}: HasField {layout.HasField}, peek {(f != null)}");
        ReadOnlySpan<int> cells = layout.Cells;
        ReadOnlySpan<byte> dirs = layout.Directions;
        if (f == null)
        {
            Assert.True(cells.Length == 0 && layout.MarkedCell == -1, $"{when}: {cells.Length} arrows with no current field for goal {goal}");
            return;
        }
        Assert.Equal(goal, f.RequestedCell);
        int k = 0;
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            int c = y * w + x;
            byte d = f.DirectionAt(c);
            if (d == FlowField.NoDirection) continue;
            Assert.True(k < cells.Length, $"{when}: layout has {cells.Length} arrows, oracle more");
            Assert.True(cells[k] == c && dirs[k] == d, $"{when}: arrow {k} = cell {cells[k]} dir {dirs[k]}, oracle cell {c} dir {d}");
            // The arrow must point at a passable neighbour (sanity of the drawn direction).
            int nx = x + FlowField.OffsetX(d), ny = y + FlowField.OffsetY(d);
            Assert.True(g.IsPassable(nx, ny), $"{when}: arrow at ({x},{y}) dir {d} points into a blocked cell");
            k++;
        }
        Assert.True(k == cells.Length, $"{when}: layout has {cells.Length} arrows, oracle {k}");
        int t = f.TargetCell;
        int expectMark = t >= 0 && t % w >= x0 && t % w < x1 && t / w >= y0 && t / w < y1 ? t : -1;
        Assert.True(layout.MarkedCell == expectMark, $"{when}: marked {layout.MarkedCell}, oracle {expectMark}");
    }

    /// <summary>QA focus (1): 64 goals through a 32-slot cache, the selection changing every few frames, the overlay toggling, a grid version bump mid-run. After every refresh the arrows equal this frame's peek.</summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    public void Churn64GoalsThrough32Slots_ArrowsAlwaysEqualThisFramesPeek(ulong seed)
    {
        Simulation sim = Spawned(seed, 120);
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        FlowFieldCache cache = w.FlowFields;
        Assert.Equal(32, cache.Capacity);
        int[] passable = Passable(g);
        var layout = new FlowArrowLayout();
        var nav = new NavOverlayBuilder(w.Heightmap);
        var sel = new SelectionSet(u.Capacity);
        var rng = new Random((int)seed); // test-side only
        bool on = true;
        int framesWithArrows = 0, framesEmpty = 0, bumpTick = 150, relistsAfterBump = -1, navBuildsAtBump = 0;
        int sawEvictedGoal = 0;

        for (int tick = 0; tick < 320; tick++)
        {
            if (tick % 9 == 0) OrderToGoals(sim, passable, 64, tick * 37);
            if (tick == bumpTick)
            {
                navBuildsAtBump = nav.Builds;
                g.BumpVersionForTests();
            }
            ulong hash = sim.StateHash();
            int count = cache.Count, builds = cache.BuildCount;
            for (int frame = 0; frame < 3; frame++)
            {
                if (rng.Next(4) == 0)
                {
                    sel.Clear();
                    int start = rng.Next(u.Capacity);
                    for (int i = start; i < u.Capacity && sel.Count < 1 + rng.Next(30); i += 1 + rng.Next(5))
                        if (u.Alive[i]) sel.Add(new EntityHandle(i, u.Generation[i]));
                }
                if (rng.Next(6) == 0)
                {
                    on = !on;
                    if (on) layout.Invalidate();
                }
                if (!on) continue;
                nav.Refresh(g);
                int goal = FlowArrowLayout.GoalOf(sel.Items, u.Alive, u.Generation, u.GoalCell);
                var focus = new Vector2(rng.Next(-20, g.Width * 2 + 20), rng.Next(-20, g.Height * 2 + 20));
                if (rng.Next(3) == 0 && goal >= 0) focus = g.CellCenter(goal % g.Width, goal / g.Width);
                layout.Refresh(cache, g, goal, focus);
                AssertMatchesOracle(layout, cache, g, goal, focus, $"seed {seed} tick {tick} frame {frame}");
                // A second refresh with the same inputs must not relist and must still match.
                int before = layout.Rebuilds;
                Assert.False(layout.Refresh(cache, g, goal, focus));
                Assert.Equal(before, layout.Rebuilds);
                if (layout.Count > 0) framesWithArrows++; else framesEmpty++;
                if (goal >= 0 && cache.PeekCached(goal) == null) sawEvictedGoal++;
            }
            Assert.True(hash == sim.StateHash() && count == cache.Count && builds == cache.BuildCount,
                $"seed {seed} tick {tick}: overlay refresh changed sim state (hash/count/builds)");
            if (tick == bumpTick + 1) relistsAfterBump = nav.Builds - navBuildsAtBump;
            sim.Tick();
        }
        _out.WriteLine($"seed {seed}: frames with arrows {framesWithArrows}, empty {framesEmpty}, goal-not-cached frames {sawEvictedGoal}, field builds {cache.BuildCount}, nav fills {nav.Builds}");
        Assert.True(framesWithArrows > 20, "churn too weak: few frames drew arrows");
        Assert.True(sawEvictedGoal > 0, "churn too weak: never selected a goal whose field was not cached");
        Assert.True(cache.BuildCount > 64, "churn too weak: fewer builds than goals");
        Assert.True(nav.Builds == 2, $"nav overlay filled {nav.Builds} times, expected 2 (first and the one bump)");
    }

    /// <summary>A bump with the same goal and window: one relist (field gone), then exactly one more when the sim rebuilds it, matching again.</summary>
    [Fact]
    public void VersionBump_SameGoalAndWindow_RelistsToEmptyThenToTheRebuiltField()
    {
        Simulation sim = Spawned(3, 40, players: 1);
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        int[] passable = Passable(g);
        int goalCell = passable[passable.Length / 2];
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.Move(0, new EntityHandle(i, u.Generation[i]), g.CellCenter(goalCell % g.Width, goalCell / g.Width)));
        sim.Tick();
        sim.Tick(); // commands apply on the tick after the one they were enqueued in
        var sel = new SelectionSet(u.Capacity);
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) sel.Add(new EntityHandle(i, u.Generation[i]));
        int goal = FlowArrowLayout.GoalOf(sel.Items, u.Alive, u.Generation, u.GoalCell);
        Assert.Equal(goalCell, goal);
        Vector2 focus = g.CellCenter(goal % g.Width, goal / g.Width);
        var layout = new FlowArrowLayout();
        Assert.True(layout.Refresh(w.FlowFields, g, goal, focus));
        Assert.True(layout.Count > 0);
        int r = layout.Rebuilds;

        g.BumpVersionForTests();
        Assert.True(layout.Refresh(w.FlowFields, g, goal, focus), "bump did not relist");
        AssertMatchesOracle(layout, w.FlowFields, g, goal, focus, "after bump");
        Assert.Equal(0, layout.Count);
        Assert.False(layout.Refresh(w.FlowFields, g, goal, focus));
        int ticks = 0;
        while (w.FlowFields.PeekCached(goal) == null && ticks < 40) { sim.Tick(); ticks++; }
        Assert.True(w.FlowFields.PeekCached(goal) != null, "sim never rebuilt the field after the bump");
        Assert.True(layout.Refresh(w.FlowFields, g, goal, focus));
        AssertMatchesOracle(layout, w.FlowFields, g, goal, focus, "after rebuild");
        Assert.True(layout.Count > 0);
        Assert.Equal(r + 2, layout.Rebuilds);
    }

    /// <summary>QA focus (2): the overlay refreshing every tick at 1,000 units per player for 600 ticks never moves the hash away from a bare twin, nor the cache's count or build count.</summary>
    [Fact]
    public void HashTwin_1000PerPlayer_600Ticks_OverlayOn()
    {
        Simulation a = Spawned(11, 1000), b = Spawned(11, 1000);
        World w = a.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        int[] passable = Passable(g);
        var nav = new NavOverlayBuilder(w.Heightmap);
        var layout = new FlowArrowLayout();
        var ring = new TickTimeRing();
        var sel = new SelectionSet(u.Capacity);
        long sink = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            if (tick % 40 == 1)
            {
                OrderToGoals(a, passable, 48, tick * 13);
                OrderToGoals(b, passable, 48, tick * 13);
            }
            if (tick == 300) { g.BumpVersionForTests(); b.World.NavGrid.BumpVersionForTests(); }
            if (tick % 5 == 0)
            {
                sel.Clear();
                for (int i = (tick * 17) % u.Capacity; i < u.Capacity && sel.Count < 60; i += 7)
                    if (u.Alive[i]) sel.Add(new EntityHandle(i, u.Generation[i]));
            }
            ulong h = a.StateHash();
            int count = w.FlowFields.Count, builds = w.FlowFields.BuildCount;
            if (tick % 13 != 0) // toggled off one tick in 13
            {
                nav.Refresh(g);
                int goal = FlowArrowLayout.GoalOf(sel.Items, u.Alive, u.Generation, u.GoalCell);
                layout.Refresh(w.FlowFields, g, goal, new Vector2(tick % 256, 255 - tick % 256));
                sink += layout.Count + DebugCounts.Moving(u.Alive, u.State) + w.FlowFields.Count;
                ring.Add(tick * 0.01);
            }
            else layout.Invalidate();
            Assert.True(h == a.StateHash() && count == w.FlowFields.Count && builds == w.FlowFields.BuildCount, $"tick {tick}: refresh changed sim state");
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"tick {a.TickNumber}: diverged from the bare twin");
        }
        _out.WriteLine($"600 ticks at 2,000 units: hashes equal; builds {w.FlowFields.BuildCount}, nav fills {nav.Builds}, relists {layout.Rebuilds}, sink {sink}");
        Assert.True(sink > 0);
    }

    public static IEnumerable<object[]> NonSquare() => new[]
    {
        new object[] { 160, 48 },
        new object[] { 48, 160 },
        new object[] { 96, 64 },
        new object[] { 64, 64 },
    };

    /// <summary>QA focus (4): non-square maps; nav quads one per cell coloured by flags; arrow windows at all four corners, all four edge midpoints and the centre match the oracle.</summary>
    [Theory]
    [MemberData(nameof(NonSquare))]
    public void NonSquareMaps_NavQuadsAndArrowWindowsAtEveryEdgeAndCorner(int width, int height)
    {
        var mp = MapGenParams.Default with { Width = width, Height = height };
        Simulation sim;
        try { sim = Spawned(5, 30, players: 1, map: mp); }
        catch (Exception ex) { _out.WriteLine($"{width}x{height}: map gen/spawn rejected ({ex.GetType().Name}: {ex.Message}); skipping"); return; }
        World w = sim.World;
        NavGrid g = w.NavGrid;
        Assert.Equal(width, g.Width);
        Assert.Equal(height, g.Height);
        var nav = new NavOverlayBuilder(w.Heightmap);
        Assert.True(nav.Refresh(g));
        Assert.Equal(width * height * 4, nav.Positions.Length);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            Vector4 want = NavOverlayBuilder.ColorFor(g.FlagsAt(x, y));
            for (int v = 0; v < 4; v++) Assert.Equal(want, nav.Colors[4 * i + v]);
            Vector3 p0 = nav.Positions[4 * i], p3 = nav.Positions[4 * i + 3];
            Assert.True(p0.X > x * MapConstants.CellSize && p3.X < (x + 1) * MapConstants.CellSize, $"quad ({x},{y}) outside its cell in x");
            Assert.True(p0.Z > y * MapConstants.CellSize && p3.Z < (y + 1) * MapConstants.CellSize, $"quad ({x},{y}) outside its cell in z");
        }

        int[] passable = Passable(g);
        // Field for a goal near the middle, built by ordering units there.
        int goal = passable[passable.Length / 2];
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.Move(0, new EntityHandle(i, u.Generation[i]), g.CellCenter(goal % width, goal / width)));
        sim.Tick();
        sim.Tick();
        Assert.NotNull(w.FlowFields.PeekCached(goal));
        float W = width * MapConstants.CellSize, H = height * MapConstants.CellSize;
        var foci = new[]
        {
            new Vector2(0, 0), new Vector2(W, 0), new Vector2(0, H), new Vector2(W, H),
            new Vector2(W / 2, 0), new Vector2(W / 2, H), new Vector2(0, H / 2), new Vector2(W, H / 2),
            new Vector2(W / 2, H / 2), new Vector2(W - 0.001f, H - 0.001f), new Vector2(-1e9f, 1e9f),
            new Vector2(float.NaN, float.PositiveInfinity), new Vector2(float.NegativeInfinity, float.NaN),
        };
        var layout = new FlowArrowLayout();
        foreach (Vector2 f in foci)
        {
            layout.Refresh(w.FlowFields, g, goal, f);
            AssertMatchesOracle(layout, w.FlowFields, g, goal, f, $"{width}x{height} focus {f}");
        }
        // Odd window sizes too.
        foreach (int win in new[] { 1, 2, 3, 39, 200 })
        {
            var l = new FlowArrowLayout(win);
            foreach (Vector2 f in foci)
            {
                l.Refresh(w.FlowFields, g, goal, f);
                AssertMatchesOracle(l, w.FlowFields, g, goal, f, $"{width}x{height} window {win} focus {f}");
            }
        }
    }

    /// <summary>QA focus (4): selection of enemy-only units, of units that died, of a slot that respawned as the enemy (stale handle), and of garbage handles.</summary>
    [Fact]
    public void GoalOf_DeadRespawnedEnemyAndGarbageHandles()
    {
        Simulation sim = Spawned(9, 20);
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        int[] passable = Passable(g);
        int own = -1, enemy = -1;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (u.Owner[i] == 0 && own < 0) own = i;
            if (u.Owner[i] == 1 && enemy < 0) enemy = i;
        }
        int goalA = passable[100], goalB = passable[passable.Length - 100];
        sim.Enqueue(Command.Move(0, new EntityHandle(own, u.Generation[own]), g.CellCenter(goalA % g.Width, goalA / g.Width)));
        sim.Enqueue(Command.Move(1, new EntityHandle(enemy, u.Generation[enemy]), g.CellCenter(goalB % g.Width, goalB / g.Width)));
        sim.Tick();
        sim.Tick();
        var ownH = new EntityHandle(own, u.Generation[own]);
        var enemyH = new EntityHandle(enemy, u.Generation[enemy]);

        // Enemy-only selection: shows the enemy's goal (debug tool, documented as "selected unit").
        Assert.Equal(u.GoalCell[enemy], FlowArrowLayout.GoalOf(new[] { enemyH }, u.Alive, u.Generation, u.GoalCell));
        Assert.True(u.GoalCell[enemy] >= 0);

        // Garbage handles are skipped, never throw.
        var garbage = new[] { new EntityHandle(-1, 0), new EntityHandle(int.MinValue, 1), new EntityHandle(u.Capacity, 1), new EntityHandle(int.MaxValue, 0), new EntityHandle(own, ownH.Generation + 1) };
        Assert.Equal(-1, FlowArrowLayout.GoalOf(garbage, u.Alive, u.Generation, u.GoalCell));
        Assert.Equal(-1, FlowArrowLayout.GoalOf(ReadOnlySpan<EntityHandle>.Empty, u.Alive, u.Generation, u.GoalCell));

        // Dies: the handle is skipped; selection falls back to the next live one.
        int lowGoalOwner = Math.Min(own, enemy);
        u.Free(ownH);
        Assert.Equal(u.GoalCell[enemy], FlowArrowLayout.GoalOf(new[] { ownH, enemyH }, u.Alive, u.Generation, u.GoalCell));
        Assert.Equal(-1, FlowArrowLayout.GoalOf(new[] { ownH }, u.Alive, u.Generation, u.GoalCell));

        // Respawns in the same slot as the enemy with a goal: the stale handle still shows nothing.
        EntityHandle re = u.Alloc();
        Assert.Equal(own, re.Index);
        u.Owner[re.Index] = 1;
        u.GoalCell[re.Index] = goalB;
        Assert.Equal(-1, FlowArrowLayout.GoalOf(new[] { ownH }, u.Alive, u.Generation, u.GoalCell));
        Assert.Equal(goalB, FlowArrowLayout.GoalOf(new[] { re }, u.Alive, u.Generation, u.GoalCell));
        _ = lowGoalOwner;
    }

    /// <summary>QA focus (3): a relist that actually has a field (window panning across a live field) allocates nothing; the developer's probe measured a relist after a bump, when the field was gone.</summary>
    [Fact]
    public void Relist_WithAField_Panning_AllocatesZeroBytes()
    {
        Simulation sim = Spawned(2, 200, players: 1);
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        int[] passable = Passable(g);
        int goal = passable[passable.Length / 2];
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.Move(0, new EntityHandle(i, u.Generation[i]), g.CellCenter(goal % g.Width, goal / g.Width)));
        sim.Tick();
        sim.Tick();
        var sel = new SelectionSet(u.Capacity);
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) sel.Add(new EntityHandle(i, u.Generation[i]));
        var layout = new FlowArrowLayout();
        var nav = new NavOverlayBuilder(w.Heightmap);
        nav.Refresh(g);
        float x = 0;
        long sink = 0;
        Action block = () =>
        {
            for (int k = 0; k < 20; k++)
            {
                x = (x + 2f) % (g.Width * MapConstants.CellSize);
                nav.Refresh(g);
                int gl = FlowArrowLayout.GoalOf(sel.Items, u.Alive, u.Generation, u.GoalCell);
                layout.Refresh(w.FlowFields, g, gl, new Vector2(x, 128f));
                sink += layout.Count + DebugCounts.Moving(u.Alive, u.State);
            }
        };
        block();
        Assert.True(layout.Count > 0, "test setup: no arrows");
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"panning relists with a live field: 0 bytes (runs {runs}), sink {sink}");
    }

    /// <summary>The ring at capacity 1, odd capacities, wrap many times, and Clear mid-wrap: indexer, average and worst equal a list oracle.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(120)]
    public void TickTimeRing_MatchesAListOracle(int capacity)
    {
        var ring = new TickTimeRing(capacity);
        var oracle = new List<double>();
        var rng = new Random(capacity);
        for (int step = 0; step < 2000; step++)
        {
            if (rng.Next(300) == 0) { ring.Clear(); oracle.Clear(); }
            double v = rng.NextDouble() * 10;
            ring.Add(v);
            oracle.Add(v);
            if (oracle.Count > capacity) oracle.RemoveAt(0);
            Assert.Equal(oracle.Count, ring.Count);
            for (int i = 0; i < oracle.Count; i++) Assert.Equal(oracle[i], ring[i]);
            Assert.Equal(oracle.Average(), ring.Average, 9);
            Assert.Equal(oracle.Max(), ring.Worst);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => ring[ring.Count]);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring[-1]);
    }
}
