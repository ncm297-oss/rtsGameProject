using System;
using System.Globalization;
using Godot;

namespace Rts.Game;

/// <summary>
/// The stats screen's army value graph (M6-V11, docs/02 "Game flow" 5): one polyline per player in its colour over the
/// match time, from the sim's army-value samples (<c>World.ArmyValueSamples</c>), drawn in <see cref="_Draw"/>: one node for
/// the whole graph, none per sample. Title and axis titles from <c>ui.json</c> <c>match_over</c>, tick labels the clock format.
/// </summary>
/// <remarks>
/// It holds a copy of the samples taken when the stats screen opened (<see cref="SetData"/>), which is display state, not
/// gameplay state: the tree is paused under the screen. Sample k is the value at tick <c>(k + 1) x sampleTicks</c> (M6-S1), so
/// its x is that time over the match length; y is the value over the largest sample of any player (all on the baseline when
/// every sample is 0). No samples (a match shorter than one interval) draws the axes and the titles only.
/// BUG-0890: each line is drawn <see cref="OffsetStep"/> px apart from the others (<see cref="DrawOffset"/>; the points, for
/// tests, stay exact) over a light outline, so players at one value (three at 0) show as parallel lines and a dark colour
/// (the grey seat) reads on the backdrop; the time axis steps in seconds below a few minutes and labels the span's end.
/// BUG-0922 (M6-VH4): the time axis is drawn over the lines (at value 0 they lie on it), and its steps reach 3 h, so even the
/// replay cap's 24 h has at most nine labels.
/// </remarks>
public partial class ArmyValueGraph : Control
{
    private const int FontSize = 14;
    private const float Left = 64f, Right = 28f, Top = 46f, Bottom = 38f;
    // Label steps in seconds: the smallest with at most eight intervals over the span.
    // BUG-0922 item 3 (M6-VH4): 3 h, so a span up to the replay cap (24 h) keeps at most nine labels.
    private static readonly int[] SecondSteps = { 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 10800 };
    // An end label closer than this to the last step label would overlap it.
    private const float EndLabelGap = 56f;
    private const float LineWidth = 2f, OutlineWidth = 5f, DotRadius = 5f;

    /// <summary>Pixels between two players' drawn lines (<see cref="DrawOffset"/>).</summary>
    public const float OffsetStep = 3f;
    private static readonly Color AxisColour = new(0.85f, 0.82f, 0.74f);
    private static readonly Color GridColour = new(1f, 1f, 1f, 0.12f);
    private static readonly Color TextColour = new(1f, 0.93f, 0.75f);

    private UiText _ui = null!;
    private int[][] _samples = Array.Empty<int[]>();
    private Color[] _colours = Array.Empty<Color>();
    private Vector2[][] _points = Array.Empty<Vector2[]>();
    private int _sampleTicks = 1;
    private readonly System.Collections.Generic.List<int> _timeLabels = new();

    /// <summary>The graph's title (<c>match_over.army_value</c>).</summary>
    public string Title => _ui.MatchOver(MatchOverText.ArmyValue);

    /// <summary>Players drawn (one line each when they have a sample).</summary>
    public int LineCount => _samples.Length;

    /// <summary>The largest sample of any player: the top of the value axis (0 when there is none or all are 0).</summary>
    public int MaxValue { get; private set; }

    /// <summary>The match length the time axis spans, in ticks (at least one).</summary>
    public int SpanTicks { get; private set; } = 1;

    /// <summary>The plot area inside the control (local coordinates): x 0 at its left, value 0 on its bottom edge.</summary>
    public Rect2 PlotRect { get; private set; }

    /// <summary>Lines (polylines, or a dot for a single sample) the last <see cref="_Draw"/> drew.</summary>
    public int DrawnLines { get; private set; }

    /// <summary>The ticks the time axis labels, left to right (0 first), from the last layout.</summary>
    public System.Collections.Generic.IReadOnlyList<int> TimeLabelTicks => _timeLabels;

    /// <summary>How far player <paramref name="player"/>'s line is drawn off its points, in pixels down (lines at one value apart).</summary>
    /// <remarks>Centred on the points: player p of n is <c>(p - (n - 1) / 2) x OffsetStep</c>, so at most 4.5 px with four seats.</remarks>
    public float DrawOffset(int player) => (player - (_samples.Length - 1) / 2f) * OffsetStep;

    /// <summary>The colour drawn under every line as an outline, composited over the backdrop: light enough that the darkest seat colour reads.</summary>
    public static readonly Color OutlineColour = new(1f, 1f, 1f, 0.4f);

    /// <summary>Times <see cref="_Draw"/> ran (tests wait for a draw after <see cref="SetData"/>).</summary>
    public int DrawCount { get; private set; }

    /// <summary>
    /// For tests (BUG-0922 item 1): the lines the last <see cref="_Draw"/> had drawn when it drew the time axis; equal to
    /// <see cref="DrawnLines"/> when the axis goes over every line, so the lines' band at value 0 (four seats' offsets and
    /// outlines, ~16 px) never hides the baseline.
    /// </summary>
    public int LinesUnderAxis { get; private set; }

    /// <summary>For tests: the points of player <paramref name="player"/>'s line, one per sample, oldest first (local coordinates).</summary>
    public ReadOnlySpan<Vector2> GraphPoints(int player) => (uint)player < (uint)_points.Length ? _points[player] : ReadOnlySpan<Vector2>.Empty;

    /// <summary>The colour of player <paramref name="player"/>'s line.</summary>
    public Color LineColour(int player) => (uint)player < (uint)_colours.Length ? _colours[player] : Colors.White;

    /// <summary>Sets the text source; call once before the first <see cref="SetData"/>.</summary>
    public void Build(UiText ui)
    {
        _ui = ui;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>
    /// Shows <paramref name="samples"/> (per player, oldest first, one every <paramref name="sampleTicks"/> ticks) in
    /// <paramref name="colours"/> over a match of <paramref name="matchTicks"/> ticks. The arrays are kept, not copied.
    /// </summary>
    public void SetData(int[][] samples, Color[] colours, int sampleTicks, int matchTicks)
    {
        _samples = samples;
        _colours = colours;
        _sampleTicks = Math.Max(1, sampleTicks);
        int max = 0, longest = 0;
        foreach (int[] s in samples)
        {
            longest = Math.Max(longest, s.Length);
            foreach (int v in s) max = Math.Max(max, v);
        }
        MaxValue = max;
        SpanTicks = (int)Math.Clamp(Math.Max((long)matchTicks, (long)longest * _sampleTicks), 1L, int.MaxValue);
        Layout();
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            Layout();
            QueueRedraw();
        }
    }

    // The plot rect from the control's size, and every point from it.
    private void Layout()
    {
        Vector2 size = Size;
        PlotRect = new Rect2(Left, Top, Math.Max(1f, size.X - Left - Right), Math.Max(1f, size.Y - Top - Bottom));
        if (_points.Length != _samples.Length) _points = new Vector2[_samples.Length][];
        for (int p = 0; p < _samples.Length; p++)
        {
            int[] s = _samples[p];
            if (_points[p] == null || _points[p].Length != s.Length) _points[p] = new Vector2[s.Length];
            for (int k = 0; k < s.Length; k++) _points[p][k] = new Vector2(XFor((long)(k + 1) * _sampleTicks), YFor(s[k]));
        }
        _timeLabels.Clear();
        int perSecond = Rts.Sim.SimConstants.TicksPerSecond;
        int step = SecondSteps[^1];
        foreach (int sec in SecondSteps)
            if ((long)SpanTicks / ((long)sec * perSecond) <= 8) { step = sec; break; }
        for (long t = 0; t <= SpanTicks; t += (long)step * perSecond) _timeLabels.Add((int)t);
        // The span's end (the match length) as the last label when no step label is near it: a short match shows its scale.
        // (Only when it reads another clock: a sub-second span would label 0:00 twice.)
        if (XFor(SpanTicks) - XFor(_timeLabels[^1]) >= EndLabelGap && SpanTicks / perSecond > _timeLabels[^1] / perSecond) _timeLabels.Add(SpanTicks);
    }

    private float XFor(long ticks) => PlotRect.Position.X + (float)((double)ticks / SpanTicks) * PlotRect.Size.X;

    private float YFor(int value) => MaxValue <= 0 ? PlotRect.End.Y : PlotRect.End.Y - (float)((double)value / MaxValue) * PlotRect.Size.Y;

    public override void _Draw()
    {
        DrawCount++;
        DrawnLines = 0;
        LinesUnderAxis = 0;
        if (_ui == null) return;
        Font font = GetThemeDefaultFont();
        Rect2 plot = PlotRect;
        float width = Size.X;
        DrawString(font, new Vector2(0f, 16f), Title, HorizontalAlignment.Center, width, FontSize + 2, TextColour);
        DrawString(font, new Vector2(4f, Top - 10f), _ui.MatchOver(MatchOverText.GraphValue), HorizontalAlignment.Left, -1, FontSize - 1, TextColour);
        DrawString(font, new Vector2(0f, Size.Y - 2f), _ui.MatchOver(MatchOverText.GraphTime), HorizontalAlignment.Center, width, FontSize - 1, TextColour);

        // Value axis: 0, half and the top, with faint grid lines.
        int levels = MaxValue > 0 ? 2 : 0;
        for (int i = 0; i <= levels; i++)
        {
            int value = levels == 0 ? 0 : (int)((long)MaxValue * i / levels);
            float y = YFor(value);
            if (i > 0) DrawLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), GridColour, 1f);
            DrawString(font, new Vector2(0f, y + 5f), value.ToString(CultureInfo.InvariantCulture), HorizontalAlignment.Right, Left - 8f, FontSize, AxisColour);
        }

        // Time axis: the labels Layout chose (0:00 at the origin).
        foreach (int t in _timeLabels)
        {
            float x = XFor(t);
            DrawLine(new Vector2(x, plot.End.Y), new Vector2(x, plot.End.Y + 5f), AxisColour, 1f);
            DrawString(font, new Vector2(x - 40f, plot.End.Y + 19f), _ui.Clock(t), HorizontalAlignment.Center, 80f, FontSize, AxisColour);
        }
        DrawLine(plot.Position, new Vector2(plot.Position.X, plot.End.Y), AxisColour, 2f);

        // Outlines first, then the lines, each moved by its offset: a later outline never covers an earlier line.
        for (int pass = 0; pass < 2; pass++)
            for (int p = 0; p < _points.Length; p++)
            {
                Vector2[] pts = _points[p];
                if (pts.Length == 0) continue;
                DrawSetTransform(new Vector2(0f, DrawOffset(p)));
                Color colour = pass == 0 ? OutlineColour : LineColour(p);
                if (pts.Length == 1) DrawCircle(pts[0], pass == 0 ? DotRadius + 1.5f : DotRadius, colour);
                else DrawPolyline(pts, colour, pass == 0 ? OutlineWidth : LineWidth, antialiased: true);
                if (pass == 1) DrawnLines++;
            }
        DrawSetTransform(Vector2.Zero);
        // BUG-0922 item 1: the time axis over the lines; at value 0 they lie on it.
        DrawLine(new Vector2(plot.Position.X, plot.End.Y), plot.End, AxisColour, 2f);
        LinesUnderAxis = DrawnLines;
    }
}
