using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Rts.Sim;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V11: the open stats screen against the sim it was opened on (the tree paused, so the spans are those of
/// <see cref="MatchStats.ShowFor"/>): every counter column equal to its span, the Age II cell, and the army value graph
/// (a point per sample, the last point where the last sample maps, one line colour per swatch). Shared by the scenes that
/// open the screen; each returns its failure lines, empty when everything holds.
/// </summary>
public static class StatsScreenCheck
{
    /// <summary>The columns docs/02 "Game flow" 5 lists, in the brief's order.</summary>
    public static readonly MatchOverText[] Order =
    {
        MatchOverText.GatheredGold, MatchOverText.GatheredWood, MatchOverText.UnitsTrained, MatchOverText.UnitsKilled,
        MatchOverText.UnitsLost, MatchOverText.BuildingsBuilt, MatchOverText.BuildingsKilled, MatchOverText.BuildingsLost,
        MatchOverText.AgeIiAt, MatchOverText.DefeatedAt,
    };

    /// <summary>A <c>--size WxH</c> user arg (the screenshots' window, e.g. 1280x720); false when absent or malformed.</summary>
    public static bool TryWindow(string[] args, out Vector2I size)
    {
        size = default;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] != "--size") continue;
            string[] wh = args[i + 1].Split('x');
            if (wh.Length == 2 && int.TryParse(wh[0], NumberStyles.None, CultureInfo.InvariantCulture, out int w) && int.TryParse(wh[1], NumberStyles.None, CultureInfo.InvariantCulture, out int h)
                && w >= 640 && h >= 360)
            {
                size = new Vector2I(w, h);
                return true;
            }
        }
        return false;
    }

    /// <summary>The heading, the table, the graph and Back all inside <paramref name="screen"/> (a frame after the screen opened).</summary>
    public static List<string> Fits(MatchStats stats, Rect2 screen, string how)
    {
        var bad = new List<string>();
        foreach (Control c in new Control[] { stats.Title, stats.Table, stats.Graph, stats.BackButton })
        {
            Rect2 r = c.GetGlobalRect();
            if (!screen.Grow(0.5f).Encloses(r)) bad.Add($"{how}: {c.Name} {r} leaves the {screen.Size} screen");
        }
        if (stats.Table.GetGlobalRect().End.Y > stats.Graph.GetGlobalRect().Position.Y + 0.5f) bad.Add($"{how}: the table overlaps the graph");
        // M6-V13: the "opened with" lines, between the table and the graph, inside the screen.
        foreach (Label l in stats.OpeningLines)
        {
            Rect2 r = l.GetGlobalRect();
            if (!screen.Grow(0.5f).Encloses(r)) bad.Add($"{how}: opening line '{l.Text}' {r} leaves the {screen.Size} screen");
            if (r.Position.Y < stats.Table.GetGlobalRect().End.Y - 0.5f || r.End.Y > stats.Graph.GetGlobalRect().Position.Y + 0.5f)
                bad.Add($"{how}: opening line '{l.Text}' {r} is not between the table and the graph");
        }
        return bad;
    }

    /// <summary>
    /// M6-V13: one "opened with" line per AI seat, in seat order: <c>match_over.opening</c> of the seat's faction name and the
    /// <c>displayName</c> of opening <paramref name="want"/>[p] (-1: a human seat, no line); the tooltip is its description.
    /// </summary>
    public static List<string> Openings(MatchStats stats, Rts.Sim.Data.GameData data, World w, UiText ui, int[] want, string how)
    {
        var bad = new List<string>();
        int line = 0;
        for (int p = 0; p < want.Length; p++)
        {
            if (want[p] < 0)
            {
                if (stats.OpeningText(p) != "") bad.Add($"{how}: human seat {p} shows '{stats.OpeningText(p)}'");
                continue;
            }
            Rts.Sim.Data.AiOpening o = data.Ai[w.FactionOf(p)].Openings[want[p]];
            string text = ui.OpeningLine(data.Factions[w.FactionOf(p)].DisplayName, o.DisplayName);
            if (stats.OpeningText(p) != text) bad.Add($"{how}: seat {p} line '{stats.OpeningText(p)}' (want '{text}')");
            if (line >= stats.OpeningLines.Count || stats.OpeningLines[line].Text != text || stats.OpeningLines[line].TooltipText != o.Description)
                bad.Add($"{how}: line {line} for seat {p}: '{(line < stats.OpeningLines.Count ? stats.OpeningLines[line].Text : "<none>")}'");
            line++;
        }
        if (stats.OpeningLines.Count != line) bad.Add($"{how}: {stats.OpeningLines.Count} opening lines (want {line}, one per AI seat)");
        return bad;
    }

    /// <summary>The column index of <paramref name="label"/> in <see cref="MatchStats.Columns"/>, -1 if none.</summary>
    public static int Column(MatchOverText label) => Array.FindIndex(MatchStats.Columns, c => c.Label == label);

    /// <summary>The Age II cell the sim says: the clock of <c>TechResearchedTick(p, AgeTechs[0])</c>, else <c>not_reached</c>.</summary>
    public static string AgeIi(World w, UiText ui, int p)
    {
        int tick = w.TechResearchedTick(p, w.Data.AgeTechs[0]);
        return tick >= 0 ? ui.Clock(tick) : ui.MatchOver(MatchOverText.NotReached);
    }

    /// <summary>Every counter column of every row against the sim's spans (the defeat column is the scene's to check).</summary>
    public static List<string> Columns(MatchStats stats, World w, UiText ui, string how)
    {
        var bad = new List<string>();
        for (int c = 0; c < Order.Length; c++)
            if (c >= MatchStats.Columns.Length || MatchStats.Columns[c].Label != Order[c]) bad.Add($"{how}: column {c} is {(c < MatchStats.Columns.Length ? MatchStats.Columns[c].Label : "missing")} (want {Order[c]})");
        if (MatchStats.Columns.Length != Order.Length) bad.Add($"{how}: {MatchStats.Columns.Length} columns (want {Order.Length})");
        int rows = Math.Min(stats.RowCount, w.Config.PlayerCount);
        if (stats.RowCount != w.Config.PlayerCount) bad.Add($"{how}: {stats.RowCount} rows for {w.Config.PlayerCount} players");
        for (int p = 0; p < rows; p++)
        {
            Want(MatchOverText.GatheredGold, w.GatheredGold[p]);
            Want(MatchOverText.GatheredWood, w.GatheredWood[p]);
            Want(MatchOverText.UnitsTrained, w.UnitsTrained[p]);
            Want(MatchOverText.UnitsKilled, w.UnitKills[p]);
            Want(MatchOverText.UnitsLost, w.UnitLosses[p]);
            Want(MatchOverText.BuildingsBuilt, w.BuildingsBuilt[p]);
            Want(MatchOverText.BuildingsKilled, w.BuildingKills[p]);
            Want(MatchOverText.BuildingsLost, w.BuildingsLost[p]);
            WantText(MatchOverText.AgeIiAt, AgeIi(w, ui, p));

            void Want(MatchOverText label, int value) => WantText(label, value.ToString(CultureInfo.InvariantCulture));
            void WantText(MatchOverText label, string want)
            {
                int col = Column(label);
                string got = col >= 0 ? stats.Cell(p, col) : "(no column)";
                if (got != want) bad.Add($"{how}: row {p} {label} '{got}' (want '{want}')");
            }
        }
        return bad;
    }

    /// <summary>
    /// The graph against <c>ArmyValueSamples</c>: one line per player, a point per sample, the scale the largest sample of any
    /// player, the last point at the last sample's time and value, the line colours the swatches and <see cref="Match.PlayerRgb"/>;
    /// with <paramref name="drawn"/> (a frame has passed since it opened) the last draw drew exactly the players with a sample.
    /// </summary>
    public static List<string> Graph(MatchStats stats, Match match, bool drawn, string how)
    {
        var bad = new List<string>();
        World w = match.Simulation!.World;
        ArmyValueGraph g = stats.Graph;
        int players = w.Config.PlayerCount, max = 0, withSamples = 0;
        for (int p = 0; p < players; p++)
        {
            foreach (int v in w.ArmyValueSamples(p)) max = Math.Max(max, v);
            if (w.ArmyValueSamples(p).Length > 0) withSamples++;
        }
        if (g.LineCount != players) bad.Add($"{how}: graph has {g.LineCount} lines for {players} players");
        if (g.MaxValue != max) bad.Add($"{how}: graph scale {g.MaxValue} (want the largest sample, {max})");
        if (g.SpanTicks != Math.Max(1, match.Simulation.TickNumber)) bad.Add($"{how}: graph spans {g.SpanTicks} ticks (match {match.Simulation.TickNumber})");
        Rect2 r = g.PlotRect;
        for (int p = 0; p < Math.Min(players, g.LineCount); p++)
        {
            ReadOnlySpan<int> s = w.ArmyValueSamples(p);
            ReadOnlySpan<Vector2> pts = g.GraphPoints(p);
            if (pts.Length != s.Length) bad.Add($"{how}: player {p}: {pts.Length} graph points for {s.Length} samples");
            else if (s.Length > 0)
            {
                float wantY = max > 0 ? r.End.Y - (float)s[^1] / max * r.Size.Y : r.End.Y;
                float wantX = r.Position.X + (float)((double)s.Length * w.StatsSampleTicks / g.SpanTicks) * r.Size.X;
                if (Math.Abs(pts[^1].Y - wantY) > 0.5f || Math.Abs(pts[^1].X - wantX) > 0.5f)
                    bad.Add($"{how}: player {p}'s last point {pts[^1]} (want ({wantX}, {wantY}) for sample {s[^1]} of {max} at tick {s.Length * w.StatsSampleTicks})");
                for (int k = 1; k < pts.Length; k++)
                    if (pts[k].X <= pts[k - 1].X) { bad.Add($"{how}: player {p}'s points not left to right at {k}"); break; }
            }
            uint line = g.LineColour(p).ToRgba32() >> 8;
            if (line != match.PlayerRgb[p] || (p < stats.RowCount && line != stats.Swatch(p).ToRgba32() >> 8))
                bad.Add($"{how}: player {p}'s line {line:X6}, rgb {match.PlayerRgb[p]:X6}, swatch {(p < stats.RowCount ? stats.Swatch(p).ToHtml(false) : "-")}");
        }
        if (drawn && (g.DrawCount == 0 || g.DrawnLines != withSamples))
            bad.Add($"{how}: graph drawn {g.DrawCount} times, {g.DrawnLines} lines last time (want {withSamples})");
        bad.AddRange(Readable(stats, players, drawn, how));
        return bad;
    }

    /// <summary>The time-axis labels of a span: 0 first, rising, at most ten, all inside it, the last at least half-way once the span reaches 10 s.</summary>
    public static List<string> TimeAxis(ArmyValueGraph g, string how)
    {
        var bad = new List<string>();
        var t = g.TimeLabelTicks;
        bool rising = true;
        for (int i = 1; i < t.Count; i++) rising &= t[i] > t[i - 1];
        if (t.Count == 0 || t[0] != 0 || !rising || t.Count > 10 || t[^1] > g.SpanTicks
            || (g.SpanTicks >= 10 * SimConstants.TicksPerSecond && t[^1] * 2 < g.SpanTicks))
            bad.Add($"{how}: time axis labels at ticks [{string.Join(", ", t)}] over a {g.SpanTicks}-tick span (want 0 first, rising, at most 10, the last past half the span)");
        return bad;
    }

    /// <summary>
    /// Contrast a line core needs against its own outline to read as a line inside the halo (BUG-0922 item 2). The shipped
    /// palette's weakest core is Violet at 1.45:1 (2.63:1 on the backdrop), then Iron at 1.62:1 (2.35:1).
    /// </summary>
    public const double CoreOnOutline = 1.4;

    /// <summary>
    /// BUG-0890 / BUG-0922 item 2: a graph line reads when it stands 3:1 off the stats backdrop, or when its outline does and the
    /// line stands <see cref="CoreOnOutline"/>:1 off the outline (a darker core in a light halo, the grey seat's case). The first
    /// form passed whenever the outline alone did, which it always does, so any colour passed.
    /// </summary>
    public static bool LineReadable(Color line, out string why)
    {
        Color back = MatchStats.BackdropColour;
        Color outline = back.Lerp(new Color(ArmyValueGraph.OutlineColour, 1f), ArmyValueGraph.OutlineColour.A);
        double onBack = Contrast(line, back), onOutline = Contrast(line, outline), edge = Contrast(outline, back);
        why = $"{onBack:F2}:1 on the backdrop, {onOutline:F2}:1 on its outline, the outline {edge:F2}:1 on the backdrop (want 3:1, or 3:1 outline + {CoreOnOutline}:1 core)";
        return onBack >= 3.0 || (edge >= 3.0 && onOutline >= CoreOnOutline);
    }

    /// <summary>WCAG contrast ratio of two opaque colours (1 to 21).</summary>
    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Lin(float v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    // BUG-0890: the time axis has a scale on a short match; lines at one value are drawn apart; every line reads on the backdrop
    // (LineReadable); the time axis is drawn over the lines (BUG-0922); every heading's text sits on the heading row's bottom line.
    private static List<string> Readable(MatchStats stats, int players, bool drawn, string how)
    {
        ArmyValueGraph g = stats.Graph;
        var bad = TimeAxis(g, how);
        for (int p = 0; p < players; p++)
            for (int q = p + 1; q < players; q++)
                if (Math.Abs(g.DrawOffset(p) - g.DrawOffset(q)) < 2f)
                    bad.Add($"{how}: players {p} and {q} drawn {g.DrawOffset(p)} / {g.DrawOffset(q)} px off their points: equal values would overlap");
        for (int p = 0; p < players; p++)
            if (!LineReadable(g.LineColour(p), out string why))
                bad.Add($"{how}: player {p}'s line {g.LineColour(p).ToHtml(false)}: {why}");
        if (drawn && g.DrawnLines > 0 && g.LinesUnderAxis != g.DrawnLines)
            bad.Add($"{how}: the time axis was drawn under {g.DrawnLines - g.LinesUnderAxis} of {g.DrawnLines} lines (BUG-0922: a zero band hides it)");
        if (drawn && stats.Headings.Count > 0)
        {
            float bottom = 0f;
            foreach (Label h in stats.Headings) bottom = Math.Max(bottom, h.GetGlobalRect().End.Y);
            foreach (Label h in stats.Headings)
            {
                Rect2 r = h.GetGlobalRect();
                float slack = r.Size.Y - h.GetCombinedMinimumSize().Y;
                if (h.Text.Length > 0 && (Math.Abs(r.End.Y - bottom) > 1f || slack > 1f))
                    bad.Add($"{how}: heading '{h.Text}' {r} ({slack:F0} px over its text) is not on the row's bottom line at {bottom}");
            }
        }
        return bad;
    }
}
