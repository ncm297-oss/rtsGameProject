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
}
