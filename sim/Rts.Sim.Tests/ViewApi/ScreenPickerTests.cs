using System.Numerics;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>Click and box selection geometry on projected unit centres.</summary>
public class ScreenPickerTests
{
    private static (Vector2[] C, float[] R, bool[] Ok) Units(params (float X, float Y, float R)[] units)
    {
        var c = new Vector2[units.Length];
        var r = new float[units.Length];
        var ok = new bool[units.Length];
        for (int i = 0; i < units.Length; i++)
        {
            c[i] = new Vector2(units[i].X, units[i].Y);
            r[i] = units[i].R;
            ok[i] = true;
        }
        return (c, r, ok);
    }

    [Fact]
    public void Click_InsideTheProjectedRadius_PicksTheUnit_OutsideMissesIt()
    {
        var (c, r, ok) = Units((100, 100, 30));
        Assert.Equal(0, ScreenPicker.PickClick(c, r, ok, new Vector2(129, 100)));
        Assert.Equal(0, ScreenPicker.PickClick(c, r, ok, new Vector2(100, 130))); // edge inclusive
        Assert.Equal(-1, ScreenPicker.PickClick(c, r, ok, new Vector2(131, 100)));
    }

    [Fact]
    public void Click_SmallOrDistantUnit_UsesTheTwelvePixelMinimum()
    {
        var (c, r, ok) = Units((100, 100, 2));
        Assert.Equal(0, ScreenPicker.PickClick(c, r, ok, new Vector2(111.9f, 100)));
        Assert.Equal(-1, ScreenPicker.PickClick(c, r, ok, new Vector2(112.1f, 100)));
    }

    [Fact]
    public void Click_OverlappingUnits_NearestCentreWins_ExactTieGoesToTheLowestSlot()
    {
        var (c, r, ok) = Units((100, 100, 30), (120, 100, 30), (110, 100, 30));
        Assert.Equal(1, ScreenPicker.PickClick(c, r, ok, new Vector2(122, 100)));
        Assert.Equal(2, ScreenPicker.PickClick(c, r, ok, new Vector2(110, 101)));
        // (105, 100) is 5 px from slot 0 and from slot 2: slot 0.
        Assert.Equal(0, ScreenPicker.PickClick(c, r, ok, new Vector2(105, 100)));
        // Order of the arrays doesn't matter, only the slot numbers do.
        var (c2, r2, ok2) = Units((110, 100, 30), (120, 100, 30), (100, 100, 30));
        Assert.Equal(0, ScreenPicker.PickClick(c2, r2, ok2, new Vector2(105, 100)));
    }

    [Fact]
    public void Click_NonCandidates_AndNaNCentres_AreNeverPicked()
    {
        var (c, r, ok) = Units((100, 100, 30), (100, 100, 30), (float.NaN, float.NaN, 30));
        ok[0] = false; // e.g. an enemy, a dead slot, or behind the camera
        Assert.Equal(1, ScreenPicker.PickClick(c, r, ok, new Vector2(100, 100)));
        ok[1] = false;
        Assert.Equal(-1, ScreenPicker.PickClick(c, r, ok, new Vector2(100, 100)));
        Assert.Equal(-1, ScreenPicker.PickClick(c, r, ok, new Vector2(float.NaN, float.NaN)));
    }

    [Fact]
    public void Box_PicksCentresInside_EdgesInclusive_AscendingSlots()
    {
        var (c, _, ok) = Units((10, 10, 5), (50, 50, 5), (100, 100, 5), (100, 101, 5), (49.99f, 75, 5));
        var picked = new int[c.Length];
        int n = ScreenPicker.PickBox(c, ok, new Vector2(10, 10), new Vector2(100, 100), picked);
        Assert.Equal(new[] { 0, 1, 2, 4 }, picked[..n]);
    }

    [Fact]
    public void Box_InvertedCorners_GiveTheSameResult()
    {
        var (c, _, ok) = Units((10, 10, 5), (50, 50, 5), (90, 20, 5), (200, 200, 5));
        var a = new int[4];
        var b = new int[4];
        var d = new int[4];
        int na = ScreenPicker.PickBox(c, ok, new Vector2(0, 0), new Vector2(100, 60), a);
        int nb = ScreenPicker.PickBox(c, ok, new Vector2(100, 60), new Vector2(0, 0), b);
        int nd = ScreenPicker.PickBox(c, ok, new Vector2(0, 60), new Vector2(100, 0), d);
        Assert.Equal(3, na);
        Assert.Equal(a[..na], b[..nb]);
        Assert.Equal(a[..na], d[..nd]);
    }

    [Fact]
    public void Box_ZeroArea_PicksOnlyACentreExactlyOnIt()
    {
        var (c, _, ok) = Units((40, 40, 5), (40.01f, 40, 5));
        var picked = new int[2];
        int n = ScreenPicker.PickBox(c, ok, new Vector2(40, 40), new Vector2(40, 40), picked);
        Assert.Equal(1, n);
        Assert.Equal(0, picked[0]);
    }

    [Fact]
    public void Box_OffScreenAndNonCandidateCentres_AreNotPicked()
    {
        var (c, _, ok) = Units((-5, 50, 5), (50, -1, 5), (1200, 50, 5), (50, 50, 5), (60, 60, 5), (float.NaN, 50, 5));
        ok[4] = false;
        var picked = new int[c.Length];
        int n = ScreenPicker.PickBox(c, ok, new Vector2(0, 0), new Vector2(1152, 648), picked);
        Assert.Equal(new[] { 3 }, picked[..n]);
    }

    [Fact]
    public void TwoThousandUnitsInOnePixel_ClickPicksTheLowestSlot_BoxPicksThemAll()
    {
        const int n = 2000;
        var c = new Vector2[n];
        var r = new float[n];
        var ok = new bool[n];
        for (int i = 0; i < n; i++)
        {
            c[i] = new Vector2(300, 200);
            r[i] = 1f;
            ok[i] = i >= 7;
        }
        Assert.Equal(7, ScreenPicker.PickClick(c, r, ok, new Vector2(305, 200)));
        var picked = new int[n];
        Assert.Equal(n - 7, ScreenPicker.PickBox(c, ok, new Vector2(299, 199), new Vector2(301, 201), picked));
    }

    [Theory]
    [InlineData(3.9f, false)]
    [InlineData(4f, true)]
    [InlineData(40f, true)]
    public void Drag_StartsAtFourPixels(float distance, bool drag)
    {
        Assert.Equal(drag, ScreenPicker.IsDrag(new Vector2(10, 10), new Vector2(10, 10 + distance)));
    }
}
