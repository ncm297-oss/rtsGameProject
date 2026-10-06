using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>The ViewApi helpers the view calls every frame or on every click allocate nothing.</summary>
[Collection(SerialCollection.Name)]
public class ViewApiAllocationTests
{
    private readonly ITestOutputHelper _out;

    public ViewApiAllocationTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void TerrainHeight_GroundPicker_ScreenPicker_SelectionPrune_AllocateZeroBytes()
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(3);
        const int n = 2000;
        var centers = new Vector2[n];
        var radii = new float[n];
        var ok = new bool[n];
        var picked = new int[n];
        var store = new UnitStore(n);
        var selection = new SelectionSet(n);
        for (int i = 0; i < n; i++)
        {
            centers[i] = new Vector2(i % 50 * 20, i / 50 * 15);
            radii[i] = 10f;
            ok[i] = i % 3 != 0;
            selection.Add(store.Alloc());
        }
        float sum = 0f;
        int count = 0;
        Action block = () =>
        {
            for (int i = 0; i < n; i++) sum += TerrainHeight.At(map, i * 0.127f, i * 0.113f);
            for (int i = 0; i < 50; i++)
            {
                if (GroundPicker.TryPick(map, new Vector3(i * 5f, 60f, 100f), new Vector3(0.1f, -1f, -0.7f), out Vector3 hit)) sum += hit.Y;
            }
            count += ScreenPicker.PickClick(centers, radii, ok, new Vector2(400, 300));
            count += ScreenPicker.PickBox(centers, ok, new Vector2(0, 0), new Vector2(1152, 648), picked);
            count += selection.Prune(store.Alive, store.Generation);
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"ViewApi per-frame helpers: 0 bytes (runs {runs}), checksum {sum + count}");
    }

    [Fact]
    public void ControlGroups_Subgroups_2000Units_AllocateZeroBytes()
    {
        const int n = 2000;
        var store = new UnitStore(n);
        var all = new EntityHandle[n];
        for (int i = 0; i < n; i++)
        {
            all[i] = store.Alloc();
            store.TypeId[i] = i % 7;
            store.Position[i] = new Vector2(i % 50, i / 50);
        }
        var groups = new ControlGroups(n);
        var selection = new SelectionSet(n);
        var sub = new Subgroups(7);
        for (int g = 0; g < ControlGroups.Count; g++) groups.Assign(g, all);
        double now = 0;
        int count = 0;
        Action block = () =>
        {
            // A frame's prunes and refresh, then the key handlers: recall, double-tap mean, add, assign, Tab.
            count += groups.Prune(store.Alive, store.Generation);
            count += groups.Recall(3, selection, store.Alive, store.Generation);
            sub.Update(selection.Items, store.TypeId, reset: true);
            sub.Update(selection.Items, store.TypeId, reset: false);
            sub.Next();
            if (groups.Tap(3, now += 0.1) && groups.TryMean(3, store.Position, store.Alive, store.Generation, out Vector2 m)) count += (int)m.X;
            groups.Add(4, selection.Items);
            groups.Assign(5, selection.Items);
            count += sub.ActiveType;
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"Control groups + subgroups (2,000 units): 0 bytes (runs {runs}), checksum {count}");
    }

    [Fact]
    public void MinimapDots_2000Units_AndTransform_AllocateZeroBytes()
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(3);
        const int n = 2000;
        var raster = new MinimapRaster(map, new NavGrid(map), new uint[] { 0x3366CC, 0xE08020 }, n);
        var alive = new bool[n];
        var pos = new Vector2[n];
        var owner = new int[n];
        for (int i = 0; i < n; i++)
        {
            alive[i] = i % 7 != 0;
            pos[i] = new Vector2(i * 0.127f % (map.Width * 2), i * 0.311f % (map.Height * 2));
            owner[i] = i % 2;
        }
        var fit = new MinimapTransform(new Vector2(220, 220), new Vector2(map.Width * 2, map.Height * 2));
        float sum = 0f;
        int count = 0;
        Action block = () =>
        {
            count += raster.DrawDots(alive, pos, owner);
            for (int i = 0; i < 4; i++)
            {
                Vector2 g = MinimapTransform.RayToGround(new Vector3(i * 10f, 60f, 100f), new Vector3(0.3f, -1f, -0.7f), 4f);
                sum += fit.ToPixel(fit.ClampToMap(g)).X;
                if (fit.TryToMap(new Vector2(i * 50f, 100f), out Vector2 m)) sum += m.Y;
            }
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"Minimap dots (2,000 units) + transform: 0 bytes (runs {runs}), checksum {sum + count}");
    }
    [Fact]
    public void DebugOverlay_NavRefill_Arrows_Counts_Ring_2000Units_AllocateZeroBytes()
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(3);
        var grid = new NavGrid(map);
        var cache = new Rts.Sim.Pathfinding.FlowFieldCache(grid);
        const int n = 2000;
        var store = new UnitStore(n);
        var selection = new SelectionSet(n);
        int goal = Rts.Sim.Pathfinding.FlowField.NearestPassable(grid, 64 * grid.Width + 64);
        for (int i = 0; i < n; i++)
        {
            EntityHandle h = store.Alloc();
            store.GoalCell[i] = i % 5 == 0 ? goal : -1;
            store.State[i] = i % 3 == 0 ? UnitState.Moving : UnitState.Idle;
            selection.Add(h);
        }
        cache.Get(goal);
        var nav = new NavOverlayBuilder(map);
        var arrows = new FlowArrowLayout();
        var ring = new TickTimeRing();
        nav.Refresh(grid);
        long count = 0;
        double sum = 0;
        Action setup = grid.BumpVersionForTests;
        Action block = () =>
        {
            // One overlay frame after a passability change (the worst case: nav refill and a relist), then the steady frame.
            count += nav.Refresh(grid) ? 1 : 0;
            int g = FlowArrowLayout.GoalOf(selection.Items, store.Alive, store.Generation, store.GoalCell);
            arrows.Invalidate();
            count += arrows.Refresh(cache, grid, g, new Vector2(128f, 128f)) ? arrows.Count : 0;
            count += arrows.Refresh(cache, grid, g, new Vector2(130f, 128f)) ? 1 : 0;
            count += DebugCounts.Moving(store.Alive, store.State) + cache.Count;
            for (int i = 0; i < 130; i++) ring.Add(i * 0.01);
            sum += ring.Average + ring.Worst + ring[ring.Count - 1];
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out, setup);
        _out.WriteLine($"Debug overlay helpers (2,000 units): 0 bytes (runs {runs}), checksum {count + sum}");
    }
}
