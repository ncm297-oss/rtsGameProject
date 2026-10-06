using Godot;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Debug overlay layer: bar graph of the last 120 tick costs with the 4 ms budget line (M2-5); the numbers are in <see cref="DebugOverlay"/>'s label.</summary>
/// <remarks>
/// Draws <see cref="SimRunner.TickTimes"/> oldest to newest, left to right, one bar per sample. The
/// vertical scale is twice the budget, or the worst sample if higher, so the budget line sits at
/// mid-height until a spike. A hidden Control never draws, so this costs nothing while the overlay
/// is off; <see cref="DebugOverlay"/> queues a redraw each frame while it is on. No text built per frame.
/// </remarks>
public partial class TickGraph : Control
{
    private static readonly Color Backdrop = new(0f, 0f, 0f, 0.55f);
    private static readonly Color BarColor = new(0.35f, 0.9f, 0.45f);
    private static readonly Color OverColor = new(1f, 0.35f, 0.25f);
    private static readonly Color BudgetColor = new(1f, 1f, 1f, 0.8f);
    private static readonly string BudgetText = $"{TickTimeRing.BudgetMs:0} ms";

    /// <summary>The samples to draw; set by <see cref="DebugOverlay"/>.</summary>
    public TickTimeRing? Samples { get; set; }

    /// <summary>Milliseconds at the top edge in the last draw.</summary>
    public double ScaleMs { get; private set; }

    /// <summary>Bars drawn in the last draw.</summary>
    public int DrawnBars { get; private set; }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Backdrop);
        TickTimeRing? ring = Samples;
        if (ring == null) return;
        double scale = System.Math.Max(2 * TickTimeRing.BudgetMs, ring.Worst);
        float barW = Size.X / ring.Capacity;
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            double ms = ring[i];
            float h = (float)(System.Math.Clamp(ms / scale, 0, 1) * Size.Y);
            DrawRect(new Rect2(i * barW, Size.Y - h, System.Math.Max(barW - 1f, 1f), h), ms > TickTimeRing.BudgetMs ? OverColor : BarColor);
        }
        float budgetY = Size.Y - (float)(TickTimeRing.BudgetMs / scale * Size.Y);
        DrawLine(new Vector2(0, budgetY), new Vector2(Size.X, budgetY), BudgetColor, 1f);
        DrawString(ThemeDB.FallbackFont, new Vector2(Size.X - 34, budgetY - 3), BudgetText, HorizontalAlignment.Left, -1, 11, BudgetColor);
        ScaleMs = scale;
        DrawnBars = n;
    }
}
