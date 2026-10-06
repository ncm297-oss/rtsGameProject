using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M2-3b rows of the minimap raster: the resource layer (trees dark green, mines gold) under the dots, redrawn once per grid version, a depleted node's cells back to the terrain.</summary>
public class MinimapRasterResourceTests
{
    private const float Cs = MapConstants.CellSize;
    private static readonly uint[] Colors = { 0x3366CC, 0xE08020 };

    private static MinimapRaster RasterOf(World w, int maxDots = 16) => new(w.Heightmap, w.NavGrid, Colors, maxDots);

    internal static bool DrawResources(MinimapRaster r, World w) =>
        r.DrawResources(w.Data.Resources, w.NavGrid.Version, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell);

    /// <summary>Oracle: every footprint cell of a live node in its kind's colour, every other pixel transparent.</summary>
    internal static void AssertResourceLayer(MinimapRaster r, World w)
    {
        var want = new uint?[r.Width * r.Height];
        ResourceStore s = w.Resources;
        for (int i = 0; i < s.Capacity; i++)
        {
            if (!s.Alive[i]) continue;
            ResourceDef def = w.Data.Resources[s.TypeId[i]];
            int x0 = s.Cell[i] % r.Width, y0 = s.Cell[i] / r.Width;
            for (int y = y0; y < y0 + def.FootprintHeight; y++)
                for (int x = x0; x < x0 + def.FootprintWidth; x++) want[y * r.Width + x] = MinimapRaster.ResourceRgb(def.Resource);
        }
        for (int p = 0; p < want.Length; p++)
        {
            int i = p * 4;
            if (want[p] is uint rgb)
                Assert.True((r.Resources[i], r.Resources[i + 1], r.Resources[i + 2], r.Resources[i + 3]) == ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)255),
                    $"resource pixel ({p % r.Width}, {p / r.Width}) is {r.Resources[i]},{r.Resources[i + 1]},{r.Resources[i + 2]},{r.Resources[i + 3]}, want {rgb:X6}");
            else Assert.True(r.Resources[i + 3] == 0, $"pixel ({p % r.Width}, {p / r.Width}) has no node but is opaque");
        }
    }

    [Fact]
    public void ResourceColours_TreesDarkGreen_MinesGold()
    {
        uint wood = MinimapRaster.ResourceRgb(ResourceKind.Wood), gold = MinimapRaster.ResourceRgb(ResourceKind.Gold);
        Assert.True(wood == MinimapRaster.WoodRgb && (wood >> 8 & 0xFF) > 2 * (wood >> 16 & 0xFF) && (wood >> 8 & 0xFF) < 128, $"wood {wood:X6} is not a dark green");
        Assert.True(gold == MinimapRaster.GoldRgb && (gold >> 16 & 0xFF) > 200 && (gold >> 8 & 0xFF) > 150 && (gold & 0xFF) < 80, $"gold {gold:X6} is not gold");
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(4UL)]
    public void ResourceLayer_PaintsEveryFootprintCell_AndTheTerrainUnderItIsTheBareMaps(ulong seed)
    {
        World w = PropLayoutTests.MatchSim(seed).World;
        World bare = PropLayoutTests.MatchSim(seed, forests: 0, mines: 0).World;
        MinimapRaster r = RasterOf(w);
        Assert.True(DrawResources(r, w));
        AssertResourceLayer(r, w);
        // A node's cells keep their ground colour in the terrain layer (not the darkened "impassable"
        // shade), so the ground shows once the node is gone.
        Assert.Equal(RasterOf(bare).Terrain, r.Terrain);
    }

    [Fact]
    public void ResourceLayer_SitsUnderTheDots_AndDotRedrawsNeverTouchIt()
    {
        World w = PropLayoutTests.MatchSim(2).World;
        ResourceStore s = w.Resources;
        MinimapRaster r = RasterOf(w);
        DrawResources(r, w);
        byte[] before = (byte[])r.Resources.Clone();
        int slot = Enumerable.Range(0, s.Capacity).First(i => s.Alive[i] && s.TypeId[i] == ResourceMaps.Mine);
        int mx = s.Cell[slot] % r.Width, my = s.Cell[slot] / r.Width;
        r.DrawDots(new[] { true }, new[] { new Vector2((mx + 0.5f) * Cs, (my + 0.5f) * Cs) }, new[] { 0 });
        int d = (my * r.Width + mx) * 4;
        Assert.Equal(((byte)0x33, (byte)0x66, (byte)0xCC, (byte)255), (r.Dots[d], r.Dots[d + 1], r.Dots[d + 2], r.Dots[d + 3]));
        Assert.Equal(before, r.Resources); // the mine is still there under the dot
        r.DrawDots(new[] { true }, new[] { new Vector2(5f, 5f) }, new[] { 1 }); // the dot moves away: its old block is cleared
        Assert.Equal(0, r.Dots[d + 3]);
        Assert.Equal(before, r.Resources);
        Assert.False(DrawResources(r, w)); // same version: nothing redrawn
        Assert.Equal(1, r.ResourceDraws);
    }

    [Fact]
    public void DepletedNode_ItsCellsShowTheTerrainAgain_OnTheNextRefresh()
    {
        World w = PropLayoutTests.MatchSim(3).World;
        ResourceStore s = w.Resources;
        MinimapRaster r = RasterOf(w);
        DrawResources(r, w);
        foreach (int type in new[] { ResourceMaps.Mine, ResourceMaps.Tree })
        {
            int slot = Enumerable.Range(0, s.Capacity).First(i => s.Alive[i] && s.TypeId[i] == type);
            int cell = s.Cell[slot];
            s.Take(s.HandleOf(slot), int.MaxValue);
            int draws = r.ResourceDraws;
            Assert.True(DrawResources(r, w));
            Assert.Equal(draws + 1, r.ResourceDraws);
            Assert.Equal(0, r.Resources[cell * 4 + 3]);
            AssertResourceLayer(r, w);
        }
    }
}
