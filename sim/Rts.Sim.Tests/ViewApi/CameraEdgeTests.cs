using System.Numerics;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M6-V7: the edge pan band (<see cref="CameraLimits.EdgeDirection"/>): the last pixel pans, outside the window doesn't.</summary>
public class CameraEdgeTests
{
    private static readonly Vector2 Size = new(1152, 648);
    private const float Band = CameraLimits.EdgePanPixels;

    [Theory]
    [InlineData(1151f, 300f, 1f, 0f)] // the last pixel column: where a confined cursor stops
    [InlineData(1152f, 300f, 1f, 0f)] // the border itself
    [InlineData(1144f, 300f, 1f, 0f)] // the band's first pixel on the right (size - band)
    [InlineData(1143.9f, 300f, 0f, 0f)]
    [InlineData(0f, 300f, -1f, 0f)] // the first pixel column
    [InlineData(7.9f, 300f, -1f, 0f)]
    [InlineData(8f, 300f, 0f, 0f)]
    [InlineData(500f, 647f, 0f, 1f)] // the last pixel row
    [InlineData(500f, 648f, 0f, 1f)]
    [InlineData(500f, 0f, 0f, -1f)]
    [InlineData(0f, 0f, -1f, -1f)] // corners pan both ways
    [InlineData(1151f, 647f, 1f, 1f)]
    [InlineData(576f, 324f, 0f, 0f)] // the middle
    public void InsideTheWindow_TheBandPans(float x, float y, float dx, float dy)
    {
        Assert.Equal(new Vector2(dx, dy), CameraLimits.EdgeDirection(new Vector2(x, y), Size, Band));
    }

    [Theory]
    [InlineData(-1f, 300f)] // on the monitor left of the window
    [InlineData(1153f, 300f)] // on the monitor right of it (the owner's playtest note 1, unconfined)
    [InlineData(500f, -0.5f)]
    [InlineData(500f, 649f)]
    [InlineData(float.NaN, 300f)]
    [InlineData(500f, float.NaN)]
    public void OutsideTheWindow_NoPan(float x, float y)
    {
        Assert.Equal(Vector2.Zero, CameraLimits.EdgeDirection(new Vector2(x, y), Size, Band));
    }
}
