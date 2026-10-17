using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Replays;

namespace Rts.Game;

/// <summary>
/// The Load Game screen (M6-V9, docs/02 "Game flow" 1): until save / load lands it lists the recorded replays in the
/// shell's replay folder (<see cref="ReplayStore"/>), newest first, one row each with the date, seed, players (faction and
/// You / Human / the AI's difficulty) and length (m:ss); files that can't be read are left out and counted in a one-line
/// note. Watch plays the picked row, Delete deletes its file, Back returns to the menu. Text from <c>ui.json</c> <c>load</c>
/// and faction data.
/// </summary>
/// <remarks>The screen only reads the folder and raises events; <see cref="Main"/> starts the playback.</remarks>
public partial class LoadGameScreen : CanvasLayer
{
    private UiText _ui = null!;
    private GameData _data = null!;
    private readonly List<ReplayEntry> _entries = new();

    /// <summary>Raised by Watch (or a double-click on a row) with the picked replay's path.</summary>
    public event Action<string>? WatchPressed;

    /// <summary>Raised by Back.</summary>
    public event Action? BackPressed;

    /// <summary>The replay list: one row per readable file, columns date, seed, players, length.</summary>
    public Tree List { get; private set; } = null!;

    /// <summary>The one-line note under the list: no replays, the files skipped, or a refused replay.</summary>
    public Label Note { get; private set; } = null!;

    /// <summary>The Watch button (greyed with no row picked).</summary>
    public Button WatchButton { get; private set; } = null!;

    /// <summary>The Delete button (greyed with no row picked).</summary>
    public Button DeleteButton { get; private set; } = null!;

    /// <summary>The Back button.</summary>
    public Button BackButton { get; private set; } = null!;

    /// <summary>The rows the list shows, newest first.</summary>
    public IReadOnlyList<ReplayEntry> Entries => _entries;

    /// <summary>Replay files the last <see cref="Refresh"/> could not read.</summary>
    public int Skipped { get; private set; }

    /// <summary>The folder the last <see cref="Refresh"/> read, or null (a shell with no replay folder).</summary>
    public string? Dir { get; private set; }

    /// <summary>The picked row's index into <see cref="Entries"/>, or -1.</summary>
    public int SelectedIndex => List.GetSelected() is TreeItem item ? (int)item.GetMetadata(0) : -1;

    /// <summary>Builds the screen's controls with <paramref name="ui"/>'s text; <paramref name="data"/> names the factions.</summary>
    public void Build(UiText ui, GameData data)
    {
        _ui = ui;
        _data = data;
        AddChild(ShellUi.Backdrop(new Color(0.09f, 0.08f, 0.07f)));
        VBoxContainer column = ShellUi.CentredColumn(this, 16);
        column.AddChild(ShellUi.Heading(ui.LoadGame(LoadText.Title)));
        List = new Tree
        {
            Name = "List", Columns = 4, ColumnTitlesVisible = true, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row,
            CustomMinimumSize = new Vector2(920f, 340f),
        };
        LoadText[] titles = { LoadText.Date, LoadText.Seed, LoadText.Seats, LoadText.Length };
        float[] widths = { 180f, 140f, 480f, 100f };
        for (int c = 0; c < titles.Length; c++)
        {
            List.SetColumnTitle(c, ui.LoadGame(titles[c]));
            List.SetColumnCustomMinimumWidth(c, (int)widths[c]);
            List.SetColumnExpand(c, c == 2);
        }
        List.ItemSelected += UpdateButtons;
        List.ItemActivated += Watch;
        column.AddChild(List);
        Note = ShellUi.Text("", 16);
        Note.Name = "Note";
        column.AddChild(Note);
        var buttons = new HBoxContainer { Name = "Buttons" };
        buttons.AddThemeConstantOverride("separation", 16);
        WatchButton = ShellUi.Button("Watch", ui.LoadGame(LoadText.Watch), 200f);
        WatchButton.Pressed += Watch;
        DeleteButton = ShellUi.Button("Delete", ui.LoadGame(LoadText.Delete), 200f);
        DeleteButton.Pressed += DeleteSelected;
        BackButton = ShellUi.Button("Back", ui.LoadGame(LoadText.Back), 200f);
        BackButton.Pressed += () => BackPressed?.Invoke();
        buttons.AddChild(WatchButton);
        buttons.AddChild(DeleteButton);
        buttons.AddChild(BackButton);
        column.AddChild(buttons);
        UpdateButtons();
    }

    /// <summary>Re-reads <paramref name="dir"/> (null: lists nothing) and rebuilds the rows, newest first; the note says what was skipped, or that there is nothing.</summary>
    public void Refresh(string? dir)
    {
        Dir = dir;
        _entries.Clear();
        _entries.AddRange(ReplayStore.List(dir, out int skipped));
        Skipped = skipped;
        List.Clear();
        TreeItem root = List.CreateItem();
        for (int i = 0; i < _entries.Count; i++)
        {
            ReplayEntry e = _entries[i];
            TreeItem row = List.CreateItem(root);
            row.SetText(0, _ui.ReplayDate(e.When));
            row.SetText(1, e.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            row.SetText(2, SeatsText(e.Seats));
            row.SetText(3, _ui.Clock(e.TickCount));
            row.SetMetadata(0, i);
        }
        Note.Text = skipped > 0 ? _ui.ReplaysSkipped(skipped) : _entries.Count == 0 ? _ui.LoadGame(LoadText.Empty) : "";
        if (_entries.Count > 0) Select(0);
        UpdateButtons();
    }

    /// <summary>Picks row <paramref name="index"/> (Watch and Delete act on it); false out of range.</summary>
    public bool Select(int index)
    {
        if ((uint)index >= (uint)_entries.Count || List.GetRoot() is not TreeItem root) return false;
        root.GetChild(index).Select(0);
        UpdateButtons();
        return true;
    }

    /// <summary>Shows the refusal note (<c>load.refused</c>): a replay that would not play stays listed for Delete.</summary>
    public void ShowRefused() => Note.Text = _ui.LoadGame(LoadText.Refused);

    /// <summary>
    /// The players cell for <paramref name="seats"/>: each seat <c>load.seat</c> with the faction's name and You (player 0, a
    /// human), the AI's difficulty or Human, joined by <c>load.seat_separator</c>.
    /// </summary>
    public string SeatsText(ImmutableArray<ReplaySeat> seats)
    {
        if (seats.IsDefaultOrEmpty) return "";
        var sb = new StringBuilder();
        for (int p = 0; p < seats.Length; p++)
        {
            ReplaySeat seat = seats[p];
            if (p > 0) sb.Append(_ui.LoadGame(LoadText.SeatSeparator));
            int f = seat.Faction == "-" ? -1 : _data.FindFaction(seat.Faction);
            string faction = f >= 0 ? _data.Factions[f].DisplayName : seat.Faction;
            string kind = seat.Controller == SeatController.Ai ? _ui.Setup(SkirmishSetup.DifficultyKeys[(int)seat.Difficulty])
                : p == SelectionController.LocalPlayer ? _ui.Setup(SetupText.SlotHuman) : _ui.MatchOver(MatchOverText.Human);
            sb.Append(_ui.ReplaySeat(faction, kind));
        }
        return sb.ToString();
    }

    private void Watch()
    {
        int i = SelectedIndex;
        if ((uint)i < (uint)_entries.Count) WatchPressed?.Invoke(_entries[i].Path);
    }

    private void DeleteSelected()
    {
        int i = SelectedIndex;
        if ((uint)i >= (uint)_entries.Count) return;
        ReplayStore.Delete(_entries[i].Path);
        Refresh(Dir);
    }

    private void UpdateButtons()
    {
        bool picked = SelectedIndex >= 0;
        WatchButton.Disabled = !picked;
        DeleteButton.Disabled = !picked;
    }
}
