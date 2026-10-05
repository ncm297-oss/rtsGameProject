using System.Numerics;
using Rts.Sim.Determinism;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

/// <summary>NavGrid: cliff and ramp rules on hand-made terrain, pocket sealing, coordinate mapping, zero-allocation queries.</summary>
[Collection(SerialCollection.Name)]
public class NavGridTests
{
    private const float H = MapConstants.LevelHeight;

    /// <summary>Builds a heightmap from rows of chars: digit = plateau level, 'r' = ramp from level 0 halfway up to 1.</summary>
    private static Heightmap FromRows(params string[] rows)
    {
        int w = rows[0].Length, h = rows.Length;
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            char c = rows[y][x];
            int i = y * w + x;
            levels[i] = c == 'r' ? (byte)0 : (byte)(c - '0');
            elevations[i] = c == 'r' ? H / 2 : levels[i] * H;
        }
        return new Heightmap(w, h, levels, elevations);
    }

    [Fact]
    public void PlateauEdge_WithoutRamp_IsCliff_AndLowerSideStaysOpen()
    {
        var nav = new NavGrid(FromRows(
            "00000000",
            "00000000",
            "00111100",
            "00111100",
            "00111100",
            "00000000",
            "00000000"));
        Assert.Equal(NavFlags.Cliff | NavFlags.Blocked, nav.FlagsAt(2, 2)); // plateau rim
        Assert.Equal(NavFlags.Cliff | NavFlags.Blocked, nav.FlagsAt(5, 3));
        Assert.Equal(NavFlags.None, nav.FlagsAt(1, 3));                     // the low side of the cliff
        Assert.Equal(NavFlags.Blocked, nav.FlagsAt(0, 3));                  // border ring
        // Plateau interior (3,3),(4,3) is cut off by the cliff ring: a sealed pocket, not reachable.
        Assert.Equal(NavFlags.Blocked, nav.FlagsAt(3, 3));
        Assert.Equal(1, nav.LevelAt(3, 3));
    }

    [Fact]
    public void RampMouth_IsOpen_AndConnectsBothLevels()
    {
        var nav = new NavGrid(FromRows(
            "0000000",
            "0011100",
            "0011100",
            "0011100",
            "000r000",
            "0000000",
            "0000000"));
        Assert.Equal(NavFlags.Ramp, nav.FlagsAt(3, 4));
        Assert.True(nav.IsPassable(3, 3));                                  // mouth above the ramp
        Assert.True(nav.IsPassable(3, 2));                                  // plateau interior
        Assert.Equal(NavFlags.Cliff | NavFlags.Blocked, nav.FlagsAt(2, 3)); // rim beside the mouth
        Assert.Equal(1, nav.LevelAt(3, 2));
        Assert.Equal(0, nav.LevelAt(3, 4));
        Assert.Equal(MapAssert.FloodCount(nav), nav.PassableCount);
        Assert.Equal(MapConstants.CostPassable, nav.CostAt(3, 4));
        Assert.Equal(MapConstants.CostBlocked, nav.CostAt(2, 3));
    }

    /// <summary>A level-1 plateau with a ramp of the given width and length running down (south) from its edge.</summary>
    private static Heightmap PlateauWithRamp(int rampWidth, int rampLength)
    {
        const int w = 12;
        int h = 4 + rampLength + 4;
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x < w - 1; x++)
            {
                levels[y * w + x] = 1;
                elevations[y * w + x] = H;
            }
        for (int k = 1; k <= rampLength; k++)
            for (int j = 0; j < rampWidth; j++)
                elevations[(3 + k) * w + 4 + j] = H * (rampLength + 1 - k) / (rampLength + 1);
        return new Heightmap(w, h, levels, elevations);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 4)]
    public void RampFlanks_AreWalls_FootAndMouthStayOpen(int rampWidth, int rampLength)
    {
        Heightmap hm = PlateauWithRamp(rampWidth, rampLength);
        var nav = new NavGrid(hm);
        int left = 3, right = 4 + rampWidth, foot = 4 + rampLength;
        for (int k = 1; k <= rampLength; k++)
        {
            Assert.Equal(NavFlags.Cliff | NavFlags.Blocked, nav.FlagsAt(left, 3 + k));
            Assert.Equal(NavFlags.Cliff | NavFlags.Blocked, nav.FlagsAt(right, 3 + k));
            for (int j = 0; j < rampWidth; j++) Assert.Equal(NavFlags.Ramp, nav.FlagsAt(4 + j, 3 + k));
        }
        for (int j = 0; j < rampWidth; j++)
        {
            Assert.Equal(NavFlags.None, nav.FlagsAt(4 + j, 3));    // mouth
            Assert.Equal(NavFlags.None, nav.FlagsAt(4 + j, foot)); // foot
        }
        Assert.Equal(NavFlags.None, nav.FlagsAt(left, foot));      // diagonal to the bottom ramp cell: open ground
        Assert.Equal(NavFlags.Cliff | NavFlags.Blocked, nav.FlagsAt(left, 3)); // rim beside the mouth
        Assert.True(nav.IsPassable(5, 2));                          // plateau still reached through the ramp
        Assert.Null(MapAssert.FindViolation(hm, nav, requireAllLevels: false, minPassableFraction: 0f));
    }

    [Fact]
    public void TwoLevelDrop_IsCliff_EvenNextToARamp()
    {
        // The level-2 cell sits above a level-0 ramp: a ramp only bridges one level.
        var nav = new NavGrid(FromRows(
            "0000000",
            "0222220",
            "0222220",
            "0222220",
            "000r000",
            "0000000"));
        Assert.Equal(NavFlags.Cliff | NavFlags.Blocked, nav.FlagsAt(3, 3));
    }

    [Fact]
    public void Pockets_AreSealed_LargestRegionStays()
    {
        // A level-0 courtyard walled in by a cliff ring: unreachable, so blocked.
        var nav = new NavGrid(FromRows(
            "000000000",
            "011111000",
            "010001000",
            "010001000",
            "011111000",
            "000000000",
            "000000000"));
        Assert.False(nav.IsPassable(2, 2));
        Assert.Equal(0, nav.LevelAt(2, 2));
        Assert.True(nav.IsPassable(7, 3));
        Assert.Equal(MapAssert.FloodCount(nav), nav.PassableCount);
    }

    [Fact]
    public void Version_StartsAtZero()
    {
        Assert.Equal(0, new NavGrid(FromRows("000", "000", "000")).Version);
    }

    [Theory]
    [InlineData(0f, 0f, 0, 0)]
    [InlineData(1.999f, 0f, 0, 0)]
    [InlineData(2f, 0f, 1, 0)]
    [InlineData(255.99f, 255.99f, 127, 127)]
    [InlineData(-0f, 3f, 0, 1)]
    public void WorldToCell_InsideTheMap(float px, float py, int cx, int cy)
    {
        NavGrid nav = DefaultGrid;
        Assert.True(nav.WorldToCell(new Vector2(px, py), out int x, out int y));
        Assert.Equal((cx, cy), (x, y));
    }

    [Theory]
    [InlineData(-0.001f, 5f)]  // just left of the map: floors to -1, must not truncate to 0
    [InlineData(5f, -1.5f)]
    [InlineData(256f, 5f)]     // exactly the far edge is outside
    [InlineData(5f, 1e9f)]
    [InlineData(float.MaxValue, 0f)]
    [InlineData(float.MinValue, 0f)]
    [InlineData(float.NaN, 5f)]
    [InlineData(5f, float.NaN)]
    [InlineData(float.PositiveInfinity, 5f)]
    [InlineData(5f, float.NegativeInfinity)]
    public void WorldToCell_OutsideOrNotFinite_ReturnsFalse(float px, float py)
    {
        Assert.False(DefaultGrid.WorldToCell(new Vector2(px, py), out int x, out int y));
        Assert.Equal((-1, -1), (x, y));
    }

    [Fact]
    public void CellCenter_RoundTripsThroughWorldToCell()
    {
        NavGrid nav = DefaultGrid;
        Assert.Equal(new Vector2(1f, 1f), nav.CellCenter(0, 0));
        for (int y = 0; y < nav.Height; y += 7)
        for (int x = 0; x < nav.Width; x += 5)
        {
            Assert.True(nav.WorldToCell(nav.CellCenter(x, y), out int cx, out int cy));
            Assert.Equal((x, y), (cx, cy));
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(128, 0)]
    [InlineData(0, 128)]
    [InlineData(int.MinValue, int.MaxValue)]
    public void Queries_OutsideTheMap_ReadAsBlocked(int x, int y)
    {
        NavGrid nav = DefaultGrid;
        Assert.False(nav.InBounds(x, y));
        Assert.False(nav.IsPassable(x, y));
        Assert.Equal(-1, nav.LevelAt(x, y));
        Assert.Equal(NavFlags.Blocked, nav.FlagsAt(x, y));
        Assert.Equal(MapConstants.CostBlocked, nav.CostAt(x, y));
    }

    [Fact]
    public void HundredThousandMixedQueries_AllocateNothing()
    {
        NavGrid nav = DefaultGrid;
        int sink = 0;
        RunQueries(nav, 1000, ref sink); // JIT warm-up
        Action block = () => RunQueries(nav, 100_000, ref sink);
        AllocationProbe.AssertZero(block);
        Assert.NotEqual(0, sink);
    }

    private static void RunQueries(NavGrid nav, int count, ref int sink)
    {
        for (int i = 0; i < count; i++)
        {
            int x = (i * 37) % 140 - 6, y = (i * 91) % 140 - 6; // some outside the map
            if (nav.InBounds(x, y)) sink++;
            if (nav.IsPassable(x, y)) sink++;
            sink += nav.LevelAt(x, y) + nav.CostAt(x, y) + (int)nav.FlagsAt(x, y);
            Vector2 c = nav.CellCenter(x, y);
            if (nav.WorldToCell(c, out int cx, out int cy)) sink += cx + cy;
            sink += nav.Version + nav.PassableCount;
        }
    }

    private static readonly NavGrid DefaultGrid = BuildDefault();

    private static NavGrid BuildDefault()
    {
        var rng = new SimRng(1, RngStream.MapGen);
        return new NavGrid(MapGenerator.Generate(MapGenParams.Default, ref rng));
    }
}
