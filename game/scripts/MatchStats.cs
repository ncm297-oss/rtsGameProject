using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;

namespace Rts.Game;

/// <summary>
/// The post-match stats screen (M6-V4, docs/02 "Game flow" 5): the match length, then one row per seat (colour swatch, faction
/// name, "You" or the AI's difficulty) with the columns the sim counts today (<see cref="Columns"/>). Text from <c>ui.json</c>
/// <c>match_over</c> and <c>setup</c>, faction names from data.
/// </summary>
/// <remarks>
/// Opened from the banner's Stats and, once the banner has shown, from the pause menu's; the tree is paused under it, so the
/// numbers are read once when it opens (<see cref="ShowFor"/>). It reads the sim (read-only) and the match's presentation
/// state (<see cref="Match.DefeatedAt"/>); it issues nothing. A new column is a <see cref="MatchOverText"/> label and a getter
/// in <see cref="Columns"/> (the sim's Requests 39 adds gathered totals, units trained, buildings built / lost ...).
/// </remarks>
public partial class MatchStats : CanvasLayer
{
    /// <summary>Drawn over the banner (22) and the pause menu (20).</summary>
    public const int DrawLayer = 23;

    /// <summary>One data column: its heading and its cell for a player.</summary>
    public readonly record struct Column(MatchOverText Label, Func<Match, World, UiText, int, string> Cell);

    /// <summary>The data columns, in order. Gold and wood are what the player holds now, not what it gathered (the sim doesn't count that yet).</summary>
    public static readonly Column[] Columns =
    {
        new(MatchOverText.Kills, (_, w, _, p) => Number(w.Kills[p])),
        new(MatchOverText.Losses, (_, w, _, p) => Number(w.Losses[p])),
        new(MatchOverText.GoldInHand, (_, w, _, p) => Number(w.Gold[p])),
        new(MatchOverText.WoodInHand, (_, w, _, p) => Number(w.Wood[p])),
        new(MatchOverText.DefeatedAt, (m, _, ui, p) => m.DefeatedAt(p) >= 0 ? ui.Clock(m.DefeatedAt(p)) : ui.MatchOver(MatchOverText.Standing)),
    };

    private UiText _ui = null!;
    private GameData _data = null!;
    private GridContainer _grid = null!;
    private readonly List<Label[]> _cells = new();
    private readonly List<Label> _names = new();
    private readonly List<Label> _seatLabels = new();
    private readonly List<ColorRect> _swatches = new();

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

    /// <summary>Builds the screen's fixed controls; hidden until <see cref="ShowFor"/>.</summary>
    public void Build(UiText ui, GameData data)
    {
        _ui = ui;
        _data = data;
        Layer = DrawLayer;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        AddChild(ShellUi.Backdrop(new Color(0.06f, 0.05f, 0.05f))); // opaque: the banner or pause menu under it would show through
        VBoxContainer column = ShellUi.CentredColumn(this);
        Title = ShellUi.Heading(ui.MatchOver(MatchOverText.StatsTitle), 44);
        column.AddChild(Title);
        Length = ShellUi.Text("", 22);
        Length.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(Length);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 12f) });
        _grid = new GridContainer { Name = "Table", Columns = 2 + Columns.Length };
        _grid.AddThemeConstantOverride("h_separation", 28);
        _grid.AddThemeConstantOverride("v_separation", 10);
        column.AddChild(_grid);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 12f) });
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
        Simulation? sim = match.Simulation;
        if (sim == null) return;
        World w = sim.World;
        Length.Text = _ui.MatchLength(sim.TickNumber);
        _grid.AddChild(Header(_ui.MatchOver(MatchOverText.Player)));
        _grid.AddChild(Header(""));
        foreach (Column c in Columns) _grid.AddChild(Header(_ui.MatchOver(c.Label)));
        var seats = match.Seats;
        for (int p = 0; p < w.Config.PlayerCount; p++)
        {
            var who = new HBoxContainer();
            who.AddThemeConstantOverride("separation", 10);
            uint rgb = p < match.PlayerRgb.Length ? match.PlayerRgb[p] : 0xFFFFFFu;
            var swatch = new ColorRect
            {
                Color = new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f),
                CustomMinimumSize = new Vector2(22f, 22f),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            who.AddChild(swatch);
            int faction = w.FactionOf(p);
            Label name = ShellUi.Text(faction >= 0 && faction < _data.Factions.Length ? _data.Factions[faction].DisplayName : "");
            who.AddChild(name);
            _grid.AddChild(who);
            Label seat = ShellUi.Text(SeatText(p, p < seats.Length ? seats[p] : default));
            _grid.AddChild(seat);
            var row = new Label[Columns.Length];
            for (int c = 0; c < Columns.Length; c++)
            {
                row[c] = ShellUi.Text(Columns[c].Cell(match, w, _ui, p));
                row[c].HorizontalAlignment = HorizontalAlignment.Right;
                _grid.AddChild(row[c]);
            }
            _cells.Add(row);
            _names.Add(name);
            _seatLabels.Add(seat);
            _swatches.Add(swatch);
        }
        Visible = true;
        BackButton.GrabFocus();
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

    private static Label Header(string text)
    {
        Label label = ShellUi.Text(text, 18);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.75f));
        return label;
    }

    private static string Number(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
