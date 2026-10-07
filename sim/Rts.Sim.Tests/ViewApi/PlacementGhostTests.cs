using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M3-V2: <see cref="PlacementGhost"/>, the placement ghost's anchor maths.</summary>
[Collection(SerialCollection.Name)]
public class PlacementGhostTests
{
    private readonly ITestOutputHelper _out;

    public PlacementGhostTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(2, 3)]
    [InlineData(5, 1)]
    public void Anchor_IsTheCursorCellMinusHalfTheFootprint_ClampedOntoTheMap_EveryCell(int fw, int fh)
    {
        NavGrid g = PropLayoutTests.MatchSim(1).World.NavGrid;
        for (int cy = 0; cy < g.Height; cy++)
        {
            for (int cx = 0; cx < g.Width; cx++)
            {
                Vector2 c = g.CellCenter(cx, cy);
                int ax = Math.Clamp(cx - fw / 2, 0, g.Width - fw), ay = Math.Clamp(cy - fh / 2, 0, g.Height - fh);
                foreach (Vector2 off in new[] { Vector2.Zero, new Vector2(0.99f, 0.99f), new Vector2(-0.99f, -0.99f), new Vector2(0.99f, -0.99f) })
                {
                    int a = PlacementGhost.Anchor(g, fw, fh, c + off);
                    Assert.True(a == ay * g.Width + ax, $"{fw}x{fh} at cell ({cx},{cy}) {off}: anchor {a % g.Width},{a / g.Width}, want {ax},{ay}");
                }
                // Away from the edges the footprint covers the cursor cell and its centre is within half a cell of the cursor cell's centre.
                if (cx - fw / 2 < 0 || cy - fh / 2 < 0 || cx - fw / 2 + fw > g.Width || cy - fh / 2 + fh > g.Height) continue;
                Assert.True(cx >= ax && cx < ax + fw && cy >= ay && cy < ay + fh);
                float centreX = (ax + fw * 0.5f) * MapConstants.CellSize, centreY = (ay + fh * 0.5f) * MapConstants.CellSize;
                Assert.True(MathF.Abs(centreX - c.X) <= MapConstants.CellSize / 2f + 1e-4f && MathF.Abs(centreY - c.Y) <= MapConstants.CellSize / 2f + 1e-4f);
            }
        }
    }

    [Fact]
    public void Anchor_OffMapNonFiniteOrTooLarge_IsMinusOne_AndTheDefOverloadMatches()
    {
        World w = PropLayoutTests.MatchSim(1).World;
        NavGrid g = w.NavGrid;
        float size = g.Width * MapConstants.CellSize;
        foreach (Vector2 p in new[] { new Vector2(-0.01f, 5f), new Vector2(5f, size), new Vector2(float.NaN, 5f), new Vector2(5f, float.NegativeInfinity) })
            Assert.Equal(-1, PlacementGhost.Anchor(g, 3, 3, p));
        Assert.Equal(-1, PlacementGhost.Anchor(g, g.Width + 1, 1, new Vector2(10f, 10f)));
        Assert.Equal(-1, PlacementGhost.Anchor(g, 0, 2, new Vector2(10f, 10f)));
        Assert.Equal(0, PlacementGhost.Anchor(g, g.Width, g.Height, new Vector2(size / 2, size / 2)));
        foreach (BuildingDef def in w.Data.Buildings)
        {
            var p = new Vector2(77.3f, 101.9f);
            Assert.Equal(PlacementGhost.Anchor(g, def.FootprintWidth, def.FootprintHeight, p), PlacementGhost.Anchor(g, def, p));
        }
    }

    [Fact]
    public void AnchorPoint_IsTheAnchorCellCentre_AndMapsBackToTheAnchor()
    {
        NavGrid g = PropLayoutTests.MatchSim(1).World.NavGrid;
        for (int a = 0; a < g.Width * g.Height; a += 37)
        {
            Vector2 p = PlacementGhost.AnchorPoint(g, a);
            Assert.Equal(g.CellCenter(a % g.Width, a / g.Width), p);
            Assert.True(g.WorldToCell(p, out int x, out int y) && y * g.Width + x == a);
        }
    }

    [Fact]
    public void Anchor_AllocatesZeroBytes()
    {
        NavGrid g = PropLayoutTests.MatchSim(1).World.NavGrid;
        long sum = 0;
        Action block = () =>
        {
            for (int i = 0; i < 5000; i++) sum += PlacementGhost.Anchor(g, 3, 3, new Vector2(i * 0.05f, i * 0.037f));
            sum += PlacementGhost.AnchorPoint(g, 1234).Length() > 0 ? 1 : 0;
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"PlacementGhost: 0 bytes (runs {runs}), checksum {sum}");
    }
}
