using Rts.Sim.Determinism;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

/// <summary>Heightmap: ramp heights, constructor validation, content hash.</summary>
public class HeightmapTests
{
    private const float H = MapConstants.LevelHeight;

    private static Heightmap Generated(ulong seed)
    {
        var rng = new SimRng(seed, RngStream.MapGen);
        return MapGenerator.Generate(MapGenParams.Default, ref rng);
    }

    [Fact]
    public void GeneratedRamps_ClimbInEvenStepsFromTheirLevelToTheNext()
    {
        int len = MapGenParams.Default.RampLength;
        float step = H / (len + 1);
        for (ulong seed = 0; seed < 20; seed++)
        {
            Heightmap hm = Generated(seed);
            int ramps = 0;
            for (int y = 1; y < hm.Height - 1; y++)
            for (int x = 1; x < hm.Width - 1; x++)
            {
                if (!hm.IsRamp(x, y)) continue;
                ramps++;
                int level = hm.LevelAt(x, y);
                float e = hm.ElevationAt(x, y);
                Assert.InRange(e, level * H + step * 0.999f, (level + 1) * H - step * 0.999f);
                // Each ramp cell has a neighbor exactly one step higher: the next ramp cell or the plateau above.
                bool climbs = false;
                foreach ((int nx, int ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                    climbs |= Math.Abs(hm.ElevationAt(nx, ny) - (e + step)) < 1e-4f;
                Assert.True(climbs, $"seed {seed} ramp ({x}, {y}) at {e} m has no neighbor at {e + step} m");
            }
            Assert.True(ramps > 0, $"seed {seed} has no ramps");
        }
    }

    [Fact]
    public void HandMadeRamp_IsReadFromHeight()
    {
        // 1 x 3 strip: plateau 0, ramp, plateau 1.
        var hm = new Heightmap(3, 1, new byte[] { 0, 0, 1 }, new[] { 0f, H / 2, H });
        Assert.False(hm.IsRamp(0, 0));
        Assert.True(hm.IsRamp(1, 0));
        Assert.False(hm.IsRamp(2, 0));
        Assert.Equal(0, hm.LevelAt(1, 0)); // a ramp counts as its lower level (docs/02 "High ground")
    }

    [Theory]
    [InlineData(new byte[] { 3 }, new[] { 12f })]               // above MaxLevel
    [InlineData(new byte[] { 0 }, new[] { float.NaN })]
    [InlineData(new byte[] { 0 }, new[] { -1f })]               // below its level
    [InlineData(new byte[] { 0 }, new[] { 4f })]                // reaches the next level: should be level 1
    [InlineData(new byte[] { 2 }, new[] { 9f })]                // a ramp above the top level
    [InlineData(new byte[] { 0, 0 }, new[] { 0f })]             // length mismatch
    public void Constructor_RejectsInvalidCells(byte[] levels, float[] elevations)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Heightmap(1, 1, levels, elevations));
    }

    [Fact]
    public void Queries_OutsideTheMap_Throw()
    {
        var hm = new Heightmap(2, 2, new byte[4], new float[4]);
        Assert.Throws<ArgumentOutOfRangeException>(() => hm.LevelAt(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => hm.ElevationAt(0, -1));
    }

    [Fact]
    public void ContentHash_IsStable_AndSeesEveryCell()
    {
        Heightmap a = Generated(11);
        Assert.Equal(a.ContentHash(), Generated(11).ContentHash());
        Assert.Equal(a.ContentHash(), a.ContentHash());

        byte[] levels = a.Levels.ToArray();
        float[] elevations = a.Elevations.ToArray();
        var copy = new Heightmap(a.Width, a.Height, levels, elevations);
        Assert.Equal(a.ContentHash(), copy.ContentHash());

        // Nudge one ramp-free level-0 cell in the far corner into a ramp: the hash must change.
        int last = levels.Length - 1;
        Assert.Equal(0, levels[last]);
        elevations[last] = 0.5f;
        Assert.NotEqual(a.ContentHash(), new Heightmap(a.Width, a.Height, levels, elevations).ContentHash());

        // Same cells, transposed size: the hash covers the dimensions too.
        var wide = new Heightmap(4, 1, new byte[4], new float[4]);
        var tall = new Heightmap(1, 4, new byte[4], new float[4]);
        Assert.NotEqual(wide.ContentHash(), tall.ContentHash());
    }
}
