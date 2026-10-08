using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M3-V1: <see cref="ResourcePicker"/>, the right-click Gather's cursor-to-node lookup.</summary>
[Collection(SerialCollection.Name)]
public class ResourcePickerTests
{
    private readonly ITestOutputHelper _out;

    public ResourcePickerTests(ITestOutputHelper output) => _out = output;

    /// <summary>Oracle: per cell, the live node whose footprint covers it, else -1.</summary>
    private static int[] Owners(World w)
    {
        NavGrid g = w.NavGrid;
        var owner = new int[g.Width * g.Height];
        Array.Fill(owner, -1);
        ResourceStore r = w.Resources;
        for (int i = 0; i < r.Capacity; i++)
        {
            if (!r.Alive[i]) continue;
            ResourceDef d = w.Data.Resources[r.TypeId[i]];
            int ax = r.Cell[i] % g.Width, ay = r.Cell[i] / g.Width;
            for (int y = ay; y < ay + d.FootprintHeight; y++)
            {
                for (int x = ax; x < ax + d.FootprintWidth; x++)
                {
                    Assert.Equal(-1, owner[y * g.Width + x]); // footprints never overlap
                    owner[y * g.Width + x] = i;
                }
            }
        }
        return owner;
    }

    [Fact]
    public void EveryCellOfEveryNode_ResolvesToIt_AndEveryOtherCellIsMinusOne_50Maps()
    {
        int nodes = 0;
        for (ulong seed = 1; seed <= 50; seed++)
        {
            World w = PropLayoutTests.MatchSim(seed, units: 8).World;
            NavGrid g = w.NavGrid;
            int[] owner = Owners(w);
            for (int cell = 0; cell < owner.Length; cell++)
            {
                int got = ViewReads.NodeAt(w, cell);
                Assert.True(owner[cell] == got, $"seed {seed} cell {cell}: picker {got}, oracle {owner[cell]}");
                if (got < 0) continue;
                nodes++;
                // Any point of the cell gives the same answer.
                Vector2 c = g.CellCenter(cell % g.Width, cell / g.Width);
                Assert.Equal(got, ViewReads.NodeAtPoint(w, c + new Vector2(0.9f, -0.9f)));
            }
        }
        _out.WriteLine($"{nodes} node cells checked over 50 maps");
        Assert.True(nodes > 10_000, $"only {nodes} node cells");
    }

    [Fact]
    public void OffMapCells_NonFinitePoints_AndPointsOffTheMap_AreMinusOne()
    {
        World w = PropLayoutTests.MatchSim(1, units: 8).World;
        int cells = w.NavGrid.Width * w.NavGrid.Height;
        Assert.Equal(-1, ViewReads.NodeAt(w, -1));
        Assert.Equal(-1, ViewReads.NodeAt(w, cells));
        Assert.Equal(-1, ViewReads.NodeAt(w, int.MinValue));
        Assert.Equal(-1, ViewReads.NodeAtPoint(w, new Vector2(float.NaN, 3f)));
        Assert.Equal(-1, ViewReads.NodeAtPoint(w, new Vector2(3f, float.PositiveInfinity)));
        Assert.Equal(-1, ViewReads.NodeAtPoint(w, new Vector2(-5f, 3f)));
        Assert.Equal(-1, ViewReads.NodeAtPoint(w, new Vector2(1e6f, 3f)));
    }

    [Fact]
    public void AFelledNode_NoLongerResolves()
    {
        Simulation sim = ResourceMaps.NewSim(ResourceMaps.Flat(20, 20));
        World w = sim.World;
        EntityHandle mine = ResourceMaps.Spawn(w, ResourceMaps.Mine, 5, 5, 100);
        EntityHandle tree = ResourceMaps.Spawn(w, ResourceMaps.Tree, 10, 10, 100);
        int width = w.NavGrid.Width;
        Assert.Equal(mine.Index, ViewReads.NodeAt(w, 6 * width + 6));
        Assert.Equal(tree.Index, ViewReads.NodeAt(w, 10 * width + 10));
        Assert.Equal(-1, ViewReads.NodeAt(w, 10 * width + 11));
        Assert.Equal(100, w.Resources.Take(tree, 100));
        Assert.Equal(-1, ViewReads.NodeAt(w, 10 * width + 10));
        Assert.Equal(mine.Index, ViewReads.NodeAt(w, 5 * width + 5));
    }

    [Fact]
    public void Picker_BuildingBars_InState_AllocateZeroBytes()
    {
        (Simulation sim, Vector2[][] blocks, StartBasePlan plan) = StartBaseTests.MatchSetup(1, 100, 5);
        StartBaseTests.Apply(sim, blocks, plan);
        World w = sim.World;
        int cells = w.NavGrid.Width * w.NavGrid.Height;
        long sum = 0;
        Action block = () =>
        {
            for (int c = 0; c < cells; c += 7) sum += ViewReads.NodeAt(w, c);
            sum += ViewReads.NodeAtPoint(w, new Vector2(100.5f, 120.25f));
            for (int k = 0; k < w.Buildings.Capacity; k++) sum += (int)ViewReads.Bar(w, k, out float f) + (int)f;
            sum += DebugCounts.InState(w.Units.Alive, w.Units.State, UnitState.Gathering);
            sum += DebugCounts.Moving(w.Units.Alive, w.Units.State);
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"picker + bars + counts: 0 bytes (runs {runs}), checksum {sum}");
    }

    private const float WoodHeight = 3.5f, GoldHeight = 2.1f; // PropsView: TreeHeight, MineHeight + GoldHeight (game side)

    /// <summary>PropsView.Shape (game side): trunk 1 m x 0.15 m, tip 3.5 m, canopy 0.8 of the cell; mine 1.6 m + a half-size 0.5 m gold block.</summary>
    internal static readonly PropShape Shape = new(1f, 0.15f, 3.5f, 0.8f, 1.6f, 0.5f, 0.5f);

    private static int Pick(World w, Vector3 o, Vector3 d, out float t) =>
        ResourcePicker.PickRay(w.NavGrid, w.Data.Resources, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, w.Heightmap, o, d, Shape, out t);

    // M3-V3b, M3-V4: the ray pick sees each node's drawn prop (a tree's trunk and cone, a mine's block and gold block): a
    // ray through a tree's canopy picks the tree although its ground point lies behind it; over the top it misses; the
    // nearer of two nodes on a line wins; a mine is only GoldHeight tall; open ground beside or behind a prop is not the
    // prop (BUG-0125); a node behind a ridge is hidden; bad rays are -1.
    [Fact]
    public void PickRay_SeesTheDrawnProp_NearestFirst_AndTerrainHides()
    {
        var rows = new string[32];
        for (int y = 0; y < 32; y++) rows[y] = new string('0', 24) + (y >= 2 && y < 30 ? "11" : "00") + "000000";
        World w = ResourceMaps.NewSim(LocalMovementTests.Rows(rows)).World;
        EntityHandle tree = ResourceMaps.Spawn(w, ResourceMaps.Tree, 10, 10, 100);
        EntityHandle tree2 = ResourceMaps.Spawn(w, ResourceMaps.Tree, 10, 6, 100);
        EntityHandle mine = ResourceMaps.Spawn(w, ResourceMaps.Mine, 16, 10, 1000);
        EntityHandle hidden = ResourceMaps.Spawn(w, ResourceMaps.Tree, 28, 12, 100); // east of the ridge (cells 24-25)
        var tc = new Vector3(21f, 0f, 21f); // the tree's cell (10, 10) centre in meters
        // A camera-like ray from south and above, aimed 3 m up the tree's column: picks the tree; its ground point is north of it.
        var cam = new Vector3(21f, 30f, 45f);
        Vector3 aim = tc + new Vector3(0f, 3f, 0f);
        Assert.Equal(tree.Index, Pick(w, cam, aim - cam, out float t));
        Assert.True(GroundPicker.TryPick(w.Heightmap, cam, aim - cam, out Vector3 g) && g.Z < 20f, $"the ground behind is at {g}");
        Assert.InRange(t, 0f, 1f);
        // Aimed at 0.3 m over the top of the canopy, at the column's far edge: passes over it and over the tree behind.
        Assert.NotEqual(tree.Index, Pick(w, cam, new Vector3(21f, WoodHeight + 0.3f, 20f) - cam, out _));
        // Two trees on a north-south line: from the south the nearer (10, 10), from the north the nearer (10, 6).
        Assert.Equal(tree.Index, Pick(w, new Vector3(21f, 1f, 40f), -Vector3.UnitZ, out _));
        Assert.Equal(tree2.Index, Pick(w, new Vector3(21f, 1f, 4f), Vector3.UnitZ, out _));
        // The mine: a horizontal ray at 1.5 m hits it, at GoldHeight + 0.2 m it doesn't.
        float mx = 16 * 2f - 1f;
        Assert.Equal(mine.Index, Pick(w, new Vector3(mx, 1.5f, 21f), Vector3.UnitX, out _));
        Assert.Equal(-1, Pick(w, new Vector3(mx, GoldHeight + 0.2f, 21f), Vector3.UnitX, out _));
        // Behind the 4 m ridge: a low ray from the west meets the wall first; a high one clears it and picks the tree.
        var target = new Vector3(57f, 2f, 25f);
        var low = new Vector3(40f, 3f, 25f);
        Assert.Equal(-1, Pick(w, low, target - low, out _));
        var high = new Vector3(40f, 12f, 25f);
        Assert.Equal(hidden.Index, Pick(w, high, target - high, out _));
        // BUG-0125: the pick is the drawn shape, not the footprint's 3.5 m column. A camera ray to open ground 2.5 m north of
        // the tree, 0.6 m east of its axis, crosses the old column between 1.7 m and the top but misses the trunk and cone:
        // the ground (a Move), not the tree. The same ray moved onto the axis goes through the cone.
        var north = new Vector3(21.6f, 0f, 18.5f);
        var cam2 = new Vector3(21.6f, 30f, 45f);
        Assert.True(OldColumn(cam2, north - cam2, 20f, 22f, 0f, WoodHeight, 20f, 22f), "the ray must cross the old column");
        Assert.Equal(-1, Pick(w, cam2, north - cam2, out t));
        Assert.True(float.IsPositiveInfinity(t));
        Assert.Equal(tree.Index, Pick(w, cam2 - new Vector3(0.6f, 0f, 0f), north - cam2, out _));
        // Beside the trunk under the canopy (0.5 m off the axis, 0.5 m up): open ground seen under the cone's rim.
        Assert.Equal(-1, Pick(w, new Vector3(21.5f, 0.5f, 40f), -Vector3.UnitZ, out _));
        Assert.Equal(tree.Index, Pick(w, new Vector3(21.1f, 0.5f, 40f), -Vector3.UnitZ, out _));
        // The cone's slope: at 2 m its radius is 0.8 x 1.5 / 2.5 = 0.48 m; 0.45 m off the axis hits, 0.52 m misses.
        Assert.Equal(tree.Index, Pick(w, new Vector3(21.45f, 2f, 40f), -Vector3.UnitZ, out _));
        Assert.Equal(-1, Pick(w, new Vector3(21.52f, 2f, 40f), -Vector3.UnitZ, out _));
        // The mine above its 1.6 m block is only the centred 2 x 2 m gold block: 1.9 m up, 0.5 m inside the footprint's
        // north edge, misses (the old column hit it); through the gold block's middle it hits.
        Assert.Equal(-1, Pick(w, new Vector3(mx, 1.9f, 20.5f), Vector3.UnitX, out _));
        Assert.Equal(mine.Index, Pick(w, new Vector3(mx, 1.9f, 22f), Vector3.UnitX, out _));
        // A felled node is gone.
        w.Resources.Take(tree, 100);
        Assert.NotEqual(tree.Index, Pick(w, cam, aim - cam, out _));
        // Bad rays.
        Assert.Equal(-1, Pick(w, cam, Vector3.Zero, out t));
        Assert.True(float.IsPositiveInfinity(t));
        Assert.Equal(-1, Pick(w, new Vector3(float.NaN, 1f, 1f), -Vector3.UnitY, out _));
        Assert.Equal(-1, Pick(w, cam, new Vector3(0f, float.PositiveInfinity, 0f), out _));
    }

    // True if the ray enters the axis-aligned box (the M3-V3b column a node used to be picked by).
    private static bool OldColumn(Vector3 o, Vector3 d, float x0, float x1, float y0, float y1, float z0, float z1)
    {
        float t0 = 0f, t1 = float.PositiveInfinity;
        foreach ((float oc, float dc, float lo, float hi) in new[] { (o.X, d.X, x0, x1), (o.Y, d.Y, y0, y1), (o.Z, d.Z, z0, z1) })
        {
            if (dc == 0f) { if (oc < lo || oc > hi) return false; continue; }
            float a = (lo - oc) / dc, b = (hi - oc) / dc;
            t0 = MathF.Max(t0, MathF.Min(a, b));
            t1 = MathF.Min(t1, MathF.Max(a, b));
        }
        return t0 <= t1;
    }

    [Fact]
    public void PickRay_Resources_AndBuildingsWithEntry_AllocateZeroBytes()
    {
        (Simulation sim, Vector2[][] blocks, StartBasePlan plan) = StartBaseTests.MatchSetup(1, 20, 5);
        StartBaseTests.Apply(sim, blocks, plan);
        World w = sim.World;
        float sum = 0f;
        Action block = () =>
        {
            for (int i = 0; i < 40; i++)
            {
                var o = new Vector3(i * 5.3f, 50f, i * 4.1f + 30f);
                var d = new Vector3(0.05f, -1f, -0.7f);
                sum += Pick(w, o, d, out float t) + (float.IsFinite(t) ? t : 0f);
                sum += BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, o, d, 3f, 0.15f, out float bt) + (float.IsFinite(bt) ? bt : 0f);
            }
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"resource + building ray picks: 0 bytes (runs {runs}), checksum {sum}");
    }
}
