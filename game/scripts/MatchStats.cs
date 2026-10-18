using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;

namespace Rts.Game;

/// <summary>
/// The post-match stats screen (M6-V4, docs/02 "Game flow" 5): the match length, then one row per seat (colour swatch, faction
/// name, "You" or the AI's difficulty) with the sim's M6-S1 counters (<see cref="Columns"/>), and under the table the army
/// value graph (<see cref="Graph"/>, M6-V11); between them, one "opened with" line per AI seat (<see cref="OpeningLines"/>,
/// M6-V13). Text from <c>ui.json</c> <c>match_over</c> and <c>setup</c>, faction and opening names from data.
/// </summary>
/// <remarks>
/// Opened from the banner's Stats and, once the banner has shown, from the pause menu's; the tree is paused under it, so the
/// numbers are read once when it opens (<see cref="ShowFor"/>). It reads the sim (read-only) and the match's presentation
/// state (<see cref="Match.DefeatedAt"/>); it issues nothing. A new column is a <see cref="MatchOverText"/> label and a getter
/// in <see cref="Columns"/>. Sized for 1280 x 720 with four seats: headings wrap to two lines in a fixed column width.
/// </remarks>
public partial class MatchStats : CanvasLayer
{
    /// <summary>Drawn over the banner (22) and the pause menu (20).</summary>
    public const int DrawLayer = 23;

    /// <summary>One data column: its heading and its cell for a player.</summary>
    public readonly record struct Column(MatchOverText Label, Func<Match, World, UiText, int, string> Cell);

    /// <summary>
    /// The data columns, in order (M6-V11: docs/02 "Game flow" 5's list from the sim's M6-S1 counters, "Implementation
    /// (M6-S1)"): gathered gold and wood (drop-offs only), units trained (queues only), units killed / lost, buildings built
    /// (sites completed), killed and lost, the time to Age II, the defeat time.
    /// </summary>
    public static readonly Column[] Columns =
    {
        new(MatchOverText.GatheredGold, (_, w, _, p) => Number(w.GatheredGold[p])),
        new(MatchOverText.GatheredWood, (_, w, _, p) => Number(w.GatheredWood[p])),
        new(MatchOverText.UnitsTrained, (_, w, _, p) => Number(w.UnitsTrained[p])),
        new(MatchOverText.UnitsKilled, (_, w, _, p) => Number(w.UnitKills[p])),
        new(MatchOverText.UnitsLost, (_, w, _, p) => Number(w.UnitLosses[p])),
        new(MatchOverText.BuildingsBuilt, (_, w, _, p) => Number(w.BuildingsBuilt[p])),
        new(MatchOverText.BuildingsKilled, (_, w, _, p) => Number(w.BuildingKills[p])),
        new(MatchOverText.BuildingsLost, (_, w, _, p) => Number(w.BuildingsLost[p])),
        new(MatchOverText.AgeIiAt, (_, w, ui, p) => AgeIiCell(w, ui, p)),
        new(MatchOverText.DefeatedAt, (m, _, ui, p) => m.DefeatedAt(p) >= 0 ? ui.Clock(m.DefeatedAt(p)) : ui.MatchOver(MatchOverText.Standing)),
    };

    /// <summary>
    /// The Age II cell: <c>match_over.clock</c> of the tick player <paramref name="p"/> got Age II (the first age tech,
    /// <c>GameData.AgeTechs[0]</c>; <c>World.TechResearchedTick</c>, ticks of 50 ms), else <c>match_over.not_reached</c>.
    /// </summary>
    public static string AgeIiCell(World w, UiText ui, int p)
    {
        int tick = w.Data.AgeTechs.Length > 0 ? w.TechResearchedTick(p, w.Data.AgeTechs[0]) : -1;
        return tick >= 0 ? ui.Clock(tick) : ui.MatchOver(MatchOverText.NotReached);
    }

    private UiText _ui = null!;
    private GameData _data = null!;
    private GridContainer _grid = null!;
    private readonly List<Label[]> _cells = new();
    private readonly List<Label> _names = new();
    private readonly List<Label> _seatLabels = new();
    private readonly List<ColorRect> _swatches = new();
    private readonly List<Label> _headings = new();
    private VBoxContainer _openings = null!;
    private readonly List<Label> _openingLines = new();
    private readonly List<string> _openingText = new();

    /// <summary>Raised by the Back button (and Esc, through <see cref="Main"/>).</summary>
    public event Action? BackPressed;

    /// <summary>The heading.</summary>
    public Label Title { get; private set; } = null!;

    /// <summary>The match length line.</summary>
    public Label Length { get; private set; } = null!;

    /// <summary>The Back button.</summary>
    public Button BackButton { get; private set; } = null!;

    /// <summary>Rows shown (one per seat) since the last <see cref="ShowFor"/>.</summary>
    public int RowCount => _cells.Count;

    /// <summary>The text of player <paramref name="row"/>'s cell in data column <paramref name="column"/> (<see cref="Columns"/> order).</summary>
    public string Cell(int row, int column) => _cells[row][column].Text;

    /// <summary>The faction name shown in <paramref name="row"/>.</summary>
    public string FactionName(int row) => _names[row].Text;

    /// <summary>"You", the AI's difficulty or "Human", shown in <paramref name="row"/>.</summary>
    public string SeatName(int row) => _seatLabels[row].Text;

    /// <summary>The swatch colour of <paramref name="row"/>.</summary>
    public Color Swatch(int row) => _swatches[row].Color;

    /// <summary>The table's heading cells (the player column's, the seat column's, then one per data column), for layout checks.</summary>
    public IReadOnlyList<Label> Headings => _headings;

    /// <summary>M6-V13: player <paramref name="row"/>'s "opened with" line (<c>match_over.opening</c>), or "" for a human seat (it shows none).</summary>
    public string OpeningText(int row) => _openingText[row];

    /// <summary>M6-V13: the "opened with" lines under the table, one per AI seat in seat order, for layout checks.</summary>
    public IReadOnlyList<Label> OpeningLines => _openingLines;

    /// <summary>
    /// M6-V13: the index in its faction's <c>ai.json</c> openings that seat <paramref name="player"/> played, or -1 for a human
    /// seat. A live sim says (<see cref="Simulation.AiOpening"/>); a watched replay's sim runs every seat human, so for a seat
    /// played as an AI (<see cref="Match.Seats"/>) the pick is worked out from the seed (<see cref="Rts.Sim.ViewApi.AiOpeningLookup"/>).
    /// </summary>
    public static int OpeningOf(Simulation sim, GameData data, int player, PlayerSeat seat)
    {
        if (seat.Controller != SeatController.Ai) return -1;
        int live = sim.AiOpening(player);
        if (live >= 0) return live;
        int faction = sim.World.FactionOf(player);
        if ((uint)faction >= (uint)data.Ai.Length || data.Ai[faction] == null || data.Ai[faction].Openings.IsDefaultOrEmpty) return -1;
        return Rts.Sim.ViewApi.AiOpeningLookup.Pick(sim.World.Config.Seed, player, seat.Difficulty, data.Ai[faction].Openings.Length);
    }

    /// <summary>The screen's opaque backdrop colour (the graph's lines are read against it).</summary>
    public static readonly Color BackdropColour = new(0.06f, 0.05f, 0.05f);

    /// <summary>The table (headings and one row per seat), for layout checks.</summary>
    public Control Table => _grid;

    /// <summary>The army value graph under the table (M6-V11).</summary>
    public ArmyValueGraph Graph { get; private set; } = null!;

    /// <summary>Width of every data column, in pixels: ten of them and two seat columns fit 1280 px (headings wrap).</summary>
    public const float ColumnWidth = 74f;

    /// <summary>Builds the screen's fixed controls; hidden until <see cref="ShowFor"/>.</summary>
    public void Build(UiText ui, GameData data)
    {
        _ui = ui;
        _data = data;
        Layer = DrawLayer;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        AddChild(ShellUi.Backdrop(BackdropColour)); // opaque: the banner or pause menu under it would show through
        VBoxContainer column = ShellUi.CentredColumn(this, 8);
        Title = ShellUi.Heading(ui.MatchOver(MatchOverText.StatsTitle), 34);
        column.AddChild(Title);
        Length = ShellUi.Text("", 18);
        Length.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(Length);
        _grid = new GridContainer { Name = "Table", Columns = 2 + Columns.Length };
        _grid.AddThemeConstantOverride("h_separation", 10);
        _grid.AddThemeConstantOverride("v_separation", 6);
        var table = new CenterContainer { Name = "TableCentre" };
        table.AddChild(_grid);
        column.AddChild(table);
        _openings = new VBoxContainer { Name = "Openings" };
        _openings.AddThemeConstantOverride("separation", 2);
        var openings = new CenterContainer { Name = "OpeningsCentre" };
        openings.AddChild(_openings);
        column.AddChild(openings);
        Graph = new ArmyValueGraph { Name = "ArmyValueGraph", CustomMinimumSize = new Vector2(820f, 220f) };
        Graph.Build(ui);
        column.AddChild(Graph);
        BackButton = ShellUi.Button("Back", ui.MatchOver(MatchOverText.Back));
        BackButton.Pressed += () => BackPressed?.Invoke();
        var centre = new CenterContainer();
        centre.AddChild(BackButton);
        column.AddChild(centre);
    }

    /// <summary>Fills the table from <paramref name="match"/>'s sim as it is now and shows the screen.</summary>
    public void ShowFor(Match match)
    {
        foreach (Node child in _grid.GetChildren())
        {
            _grid.RemoveChild(child);
            child.QueueFree();
        }
        _cells.Clear();
        _names.Clear();
        _seatLabels.Clear();
        _swatches.Clear();
        _headings.Clear();
        foreach (Node child in _openings.GetChildren())
        {
            _openings.RemoveChild(child);
            child.QueueFree();
        }
        _openingLines.Clear();
        _openingText.Clear();
        Simulation? sim = match.Simulation;
        if (sim == null) return;
        World w = sim.World;
        Length.Text = _ui.MatchLength(sim.TickNumber);
        _headings.Add(Header(_ui.MatchOver(MatchOverText.Player), 0f));
        _headings.Add(Header("", 0f));
        foreach (Column c in Columns) _headings.Add(Header(_ui.MatchOver(c.Label), ColumnWidth));
        foreach (Label h in _headings) _grid.AddChild(h);
        var seats = match.Seats;
        int players = w.Config.PlayerCount;
        var samples = new int[players][];
        var colours = new Color[players];
        for (int p = 0; p < players; p++)
        {
            var who = new HBoxContainer();
            who.AddThemeConstantOverride("separation", 8);
            colours[p] = Rgb(p < match.PlayerRgb.Length ? match.PlayerRgb[p] : 0xFFFFFFu);
            var swatch = new ColorRect
            {
                Color = colours[p],
                CustomMinimumSize = new Vector2(20f, 20f),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            who.AddChild(swatch);
            int faction = w.FactionOf(p);
            Label name = ShellUi.Text(faction >= 0 && faction < _data.Factions.Length ? _data.Factions[faction].DisplayName : "", 17);
            who.AddChild(name);
            _grid.AddChild(who);
            Label seat = ShellUi.Text(SeatText(p, p < seats.Length ? seats[p] : default), 17);
            _grid.AddChild(seat);
            var row = new Label[Columns.Length];
            for (int c = 0; c < Columns.Length; c++)
            {
                row[c] = ShellUi.Text(Columns[c].Cell(match, w, _ui, p), 17);
                row[c].HorizontalAlignment = HorizontalAlignment.Right;
                row[c].CustomMinimumSize = new Vector2(ColumnWidth, 0f);
                _grid.AddChild(row[c]);
            }
            _cells.Add(row);
            _names.Add(name);
            _seatLabels.Add(seat);
            _swatches.Add(swatch);
            samples[p] = w.ArmyValueSamples(p).ToArray(); // a copy: the span is valid until the next tick only
            AddOpening(sim, p, p < seats.Length ? seats[p] : default, name.Text, colours[p]);
        }
        Graph.SetData(samples, colours, w.StatsSampleTicks, sim.TickNumber);
        Visible = true;
        BackButton.GrabFocus();
    }

    // M6-V13: an AI seat's "<faction> opened with <displayName>" line under the table, behind its swatch (two seats of one
    // faction tell apart by colour), the opening's description as the tooltip. A human seat gets no line.
    private void AddOpening(Simulation sim, int p, PlayerSeat seat, string faction, Color colour)
    {
        int index = OpeningOf(sim, _data, p, seat);
        int f = sim.World.FactionOf(p);
        if (index < 0 || (uint)f >= (uint)_data.Ai.Length || index >= _data.Ai[f].Openings.Length)
        {
            _openingText.Add("");
            return;
        }
        Rts.Sim.Data.AiOpening opening = _data.Ai[f].Openings[index];
        string text = _ui.OpeningLine(faction, opening.DisplayName);
        _openingText.Add(text);
        var line = new HBoxContainer { Name = $"Opening{p}" };
        line.AddThemeConstantOverride("separation", 6);
        line.AddChild(new ColorRect { Color = colour, CustomMinimumSize = new Vector2(12f, 12f), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        Label label = ShellUi.Text(text, 15);
        label.TooltipText = opening.Description;
        label.MouseFilter = Control.MouseFilterEnum.Stop; // a Label ignores the mouse by default: no tooltip without this
        line.AddChild(label);
        _openings.AddChild(line);
        _openingLines.Add(label);
    }

    // "You" for the local player, the difficulty's name for an AI seat, "Human" for another human (dev runs).
    private string SeatText(int player, PlayerSeat seat)
    {
        if (player == SelectionController.LocalPlayer) return _ui.Setup(SetupText.SlotHuman);
        if (seat.Controller != SeatController.Ai) return _ui.MatchOver(MatchOverText.Human);
        return _ui.Setup(seat.Difficulty switch
        {
            AiDifficulty.Easy => SetupText.DifficultyEasy,
            AiDifficulty.Hard => SetupText.DifficultyHard,
            _ => SetupText.DifficultyNormal,
        });
    }

    // A heading cell; with a width it wraps to two lines and right-aligns over its numbers. ShrinkEnd puts a one-line heading on
    // the row's bottom line beside the two-line ones (the grid centres a cell's minimum height otherwise; BUG-0890).
    private static Label Header(string text, float width)
    {
        Label label = ShellUi.Text(text, 15);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.75f));
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        if (width > 0f)
        {
            label.CustomMinimumSize = new Vector2(width, 0f);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = VerticalAlignment.Bottom;
        }
        return label;
    }

    /// <summary>A 0xRRGGBB player colour (<see cref="Match.PlayerRgb"/>) as a Godot colour: the swatch's and the graph line's.</summary>
    public static Color Rgb(uint rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    private static string Number(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
