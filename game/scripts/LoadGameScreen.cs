using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.Save;

namespace Rts.Game;

/// <summary>
/// The Load Game screen (M6-V9, M6-V14; docs/02 "Game flow" 1): one list of the saves in the shell's save folder
/// (<see cref="SaveStore"/>) and the recorded replays in its replay folder (<see cref="ReplayStore"/>), newest first, one row
/// each with the date, seed, players (faction and You / Human / the AI's difficulty), length (a save: the match clock it was
/// made at; a replay: its recording's length, m:ss) and kind (<c>load.save_tag</c> / <c>load.replay_tag</c>), read from each
/// file's header; files whose header can't be read follow as rows of their own (the file's write time,
/// <c>load.unreadable</c>), counted in a one-line note (BUG-0830: listed so Delete can remove them). Load resumes the picked
/// save, Watch plays the picked replay (each greyed on any other row), Delete deletes the picked file (a save with the
/// recording beside it), Back returns to the menu. Text from <c>ui.json</c> <c>load</c>, <c>save</c> and faction data.
/// </summary>
/// <remarks>The screen only reads the folders and raises events; <see cref="Main"/> starts the playback or the loaded match.</remarks>
public partial class LoadGameScreen : CanvasLayer
{
    /// <summary>What a list row is.</summary>
    public enum RowKind
    {
        /// <summary>A replay whose header reads (<see cref="Entries"/>).</summary>
        Replay,
        /// <summary>A save whose header reads (<see cref="Saves"/>).</summary>
        Save,
        /// <summary>A <c>.replay</c> file whose header doesn't read (<see cref="Unreadable"/>).</summary>
        UnreadableReplay,
        /// <summary>A <c>.sav</c> file whose header doesn't read (<see cref="UnreadableSaves"/>).</summary>
        UnreadableSave,
    }

    private UiText _ui = null!;
    private GameData _data = null!;
    private readonly List<ReplayEntry> _entries = new();
    private readonly List<string> _unreadable = new();
    private readonly List<SaveEntry> _saves = new();
    private readonly List<string> _unreadableSaves = new();
    // The rows in display order: (kind, index into that kind's list).
    private readonly List<(RowKind Kind, int Index)> _rows = new();

    /// <summary>Raised by Watch (or a double-click on a replay row) with the picked replay's path.</summary>
    public event Action<string>? WatchPressed;

    /// <summary>Raised by Load (or a double-click on a save row) with the picked save's path (M6-V14).</summary>
    public event Action<string>? LoadPressed;

    /// <summary>Raised by Back.</summary>
    public event Action? BackPressed;

    /// <summary>The list: one row per readable save or replay, newest first (columns date, seed, players, length, kind), then one per unreadable file.</summary>
    public Tree List { get; private set; } = null!;

    /// <summary>The one-line note under the list: nothing to list, the files skipped, a refused replay or a refused save's reason.</summary>
    public Label Note { get; private set; } = null!;

    /// <summary>The Load button (M6-V14; greyed unless a readable save row is picked).</summary>
    public Button LoadButton { get; private set; } = null!;

    /// <summary>The Watch button (greyed unless a readable replay row is picked).</summary>
    public Button WatchButton { get; private set; } = null!;

    /// <summary>The Delete button (greyed with no row picked).</summary>
    public Button DeleteButton { get; private set; } = null!;

    /// <summary>The Back button.</summary>
    public Button BackButton { get; private set; } = null!;

    /// <summary>The readable replays, newest first.</summary>
    public IReadOnlyList<ReplayEntry> Entries => _entries;

    /// <summary>The readable saves, newest first (M6-V14).</summary>
    public IReadOnlyList<SaveEntry> Saves => _saves;

    /// <summary>Files (replays and saves) the last <see cref="Refresh(string?, string?)"/> could not read (listed after the readable rows).</summary>
    public int Skipped { get; private set; }

    /// <summary>The unreadable replay files' paths, newest first.</summary>
    public IReadOnlyList<string> Unreadable => _unreadable;

    /// <summary>The unreadable save files' paths, newest first (M6-V14).</summary>
    public IReadOnlyList<string> UnreadableSaves => _unreadableSaves;

    /// <summary>The replay folder the last refresh read, or null (a shell with no replay folder).</summary>
    public string? Dir { get; private set; }

    /// <summary>The save folder the last refresh read, or null (a shell with no save folder; M6-V14).</summary>
    public string? SaveDir { get; private set; }

    /// <summary>Rows the list shows.</summary>
    public int RowCount => _rows.Count;

    /// <summary>
    /// The picked row's index in display order, or -1. Readable rows (saves and replays, merged newest first) come first,
    /// then the unreadable ones; with no save in the list this is M6-V9's numbering (an index into <see cref="Entries"/>,
    /// then <c>Entries.Count + i</c> for <see cref="Unreadable"/>'s i).
    /// </summary>
    public int SelectedIndex => List.GetSelected() is TreeItem item ? (int)item.GetMetadata(0) : -1;

    /// <summary>What row <paramref name="row"/> (display order) is; out of range reads as an unreadable replay with no path (<see cref="RowPath"/> null).</summary>
    public RowKind KindOf(int row) => (uint)row < (uint)_rows.Count ? _rows[row].Kind : RowKind.UnreadableReplay;

    /// <summary>The file of row <paramref name="row"/> (display order), or null out of range.</summary>
    public string? RowPath(int row)
    {
        if ((uint)row >= (uint)_rows.Count) return null;
        (RowKind kind, int i) = _rows[row];
        return kind switch
        {
            RowKind.Replay => _entries[i].Path,
            RowKind.Save => _saves[i].Path,
            RowKind.UnreadableReplay => _unreadable[i],
            _ => _unreadableSaves[i],
        };
    }

    /// <summary>The display row of the file at <paramref name="path"/> (compared as full paths, ignoring case), or -1.</summary>
    public int RowOf(string path)
    {
        string want = System.IO.Path.GetFullPath(path);
        for (int r = 0; r < _rows.Count; r++)
            if (string.Equals(System.IO.Path.GetFullPath(RowPath(r)!), want, StringComparison.OrdinalIgnoreCase)) return r;
        return -1;
    }

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
            Name = "List", Columns = 5, ColumnTitlesVisible = true, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row,
            CustomMinimumSize = new Vector2(1000f, 340f),
        };
        LoadText[] titles = { LoadText.Date, LoadText.Seed, LoadText.Seats, LoadText.Length, LoadText.Kind };
        float[] widths = { 180f, 140f, 440f, 100f, 100f }; // under the list's 1,000 px: no horizontal scroll bar
        for (int c = 0; c < titles.Length; c++)
        {
            List.SetColumnTitle(c, ui.LoadGame(titles[c]));
            List.SetColumnCustomMinimumWidth(c, (int)widths[c]);
            List.SetColumnExpand(c, c == 2);
        }
        List.ItemSelected += UpdateButtons;
        List.ItemActivated += Activate;
        column.AddChild(List);
        Note = ShellUi.Text("", 16);
        Note.Name = "Note";
        column.AddChild(Note);
        var buttons = new HBoxContainer { Name = "Buttons" };
        buttons.AddThemeConstantOverride("separation", 16);
        LoadButton = ShellUi.Button("Load", ui.LoadGame(LoadText.Load), 200f);
        LoadButton.Pressed += LoadSelected;
        WatchButton = ShellUi.Button("Watch", ui.LoadGame(LoadText.Watch), 200f);
        WatchButton.Pressed += Watch;
        DeleteButton = ShellUi.Button("Delete", ui.LoadGame(LoadText.Delete), 200f);
        DeleteButton.Pressed += DeleteSelected;
        BackButton = ShellUi.Button("Back", ui.LoadGame(LoadText.Back), 200f);
        BackButton.Pressed += () => BackPressed?.Invoke();
        buttons.AddChild(LoadButton);
        buttons.AddChild(WatchButton);
        buttons.AddChild(DeleteButton);
        buttons.AddChild(BackButton);
        column.AddChild(buttons);
        UpdateButtons();
    }

    /// <summary>Re-reads replay folder <paramref name="dir"/> and the save folder of the last refresh (null: lists none of it).</summary>
    public void Refresh(string? dir) => Refresh(dir, SaveDir);

    /// <summary>
    /// Re-reads replay folder <paramref name="dir"/> and save folder <paramref name="saveDir"/> (either null: none of it)
    /// and rebuilds the rows, newest first; the note says what was skipped, or that there is nothing.
    /// </summary>
    public void Refresh(string? dir, string? saveDir)
    {
        Dir = dir;
        SaveDir = saveDir;
        _entries.Clear();
        _unreadable.Clear();
        _saves.Clear();
        _unreadableSaves.Clear();
        _rows.Clear();
        _entries.AddRange(ReplayStore.List(dir, _unreadable, out _));
        _saves.AddRange(SaveStore.List(saveDir, _unreadableSaves));
        Skipped = _unreadable.Count + _unreadableSaves.Count;
        // Merge the two newest-first lists (each sorted by date, then path), saves first on a tie.
        for (int r = 0, s = 0; r < _entries.Count || s < _saves.Count;)
        {
            bool save = s < _saves.Count && (r >= _entries.Count || Newer(_saves[s].When, _saves[s].Path, _entries[r].When, _entries[r].Path));
            _rows.Add(save ? (RowKind.Save, s++) : (RowKind.Replay, r++));
        }
        for (int r = 0, s = 0; r < _unreadable.Count || s < _unreadableSaves.Count;)
        {
            bool save = s < _unreadableSaves.Count && (r >= _unreadable.Count
                || Newer(ReplayStore.DateOf(_unreadableSaves[s]), _unreadableSaves[s], ReplayStore.DateOf(_unreadable[r]), _unreadable[r]));
            _rows.Add(save ? (RowKind.UnreadableSave, s++) : (RowKind.UnreadableReplay, r++));
        }
        List.Clear();
        TreeItem root = List.CreateItem();
        for (int i = 0; i < _rows.Count; i++) AddRow(root, i);
        int readable = _entries.Count + _saves.Count;
        Note.Text = Skipped > 0 ? _ui.ReplaysSkipped(Skipped) : readable == 0 ? _ui.LoadGame(LoadText.Empty) : "";
        if (readable > 0) Select(0);
        UpdateButtons();
    }

    // a before b in the newest-first order: the later date, then the greater path (ReplayStore.List's tie rule); equal is not before.
    private static bool Newer(DateTime a, string pathA, DateTime b, string pathB) => a != b ? a > b : string.CompareOrdinal(pathA, pathB) >= 0;

    private void AddRow(TreeItem root, int row)
    {
        (RowKind kind, int i) = _rows[row];
        TreeItem item = List.CreateItem(root);
        switch (kind)
        {
            case RowKind.Replay:
                ReplayEntry e = _entries[i];
                item.SetText(0, _ui.ReplayDate(e.When));
                item.SetText(1, e.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
                item.SetText(2, SeatsText(e.Seats));
                item.SetText(3, _ui.Clock(e.TickCount));
                item.SetText(4, _ui.LoadGame(LoadText.ReplayTag));
                break;
            case RowKind.Save:
                SaveEntry s = _saves[i];
                item.SetText(0, _ui.ReplayDate(s.When));
                item.SetText(1, s.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
                item.SetText(2, SeatsText(s.Seats));
                item.SetText(3, _ui.Clock(s.Tick));
                item.SetText(4, _ui.LoadGame(LoadText.SaveTag));
                break;
            default:
                string path = RowPath(row)!;
                item.SetText(0, _ui.ReplayDate(ReplayStore.DateOf(path)));
                item.SetText(2, _ui.LoadGame(LoadText.Unreadable));
                item.SetCustomColor(2, new Color(0.75f, 0.7f, 0.65f));
                item.SetText(4, _ui.LoadGame(kind == RowKind.UnreadableSave ? LoadText.SaveTag : LoadText.ReplayTag));
                break;
        }
        item.SetMetadata(0, row);
    }

    /// <summary>Picks row <paramref name="index"/> (display order; Load, Watch and Delete act on it); false out of range.</summary>
    public bool Select(int index)
    {
        if ((uint)index >= (uint)RowCount || List.GetRoot() is not TreeItem root) return false;
        root.GetChild(index).Select(0);
        UpdateButtons();
        return true;
    }

    /// <summary>Shows the refusal note (<c>load.refused</c>): a replay that would not play stays listed for Delete.</summary>
    public void ShowRefused() => Note.Text = _ui.LoadGame(LoadText.Refused);

    /// <summary>Shows a refused save's reason (M6-V14): <paramref name="error"/>'s <c>save.*</c> text, or <c>save.read_failed</c> for a file that would not open (null).</summary>
    public void ShowSaveRefused(SaveError? error) =>
        Note.Text = error is SaveError e && UiText.SaveTextOf(e) != null ? _ui.SaveErrorText(e) : _ui.Save(SaveText.ReadFailed);

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

    // A double-click: Load on a save row, Watch on a replay row, nothing on an unreadable one.
    private void Activate()
    {
        if (KindOf(SelectedIndex) == RowKind.Save) LoadSelected();
        else Watch();
    }

    private void Watch()
    {
        int row = SelectedIndex;
        if ((uint)row < (uint)_rows.Count && _rows[row].Kind == RowKind.Replay) WatchPressed?.Invoke(_entries[_rows[row].Index].Path);
    }

    private void LoadSelected()
    {
        int row = SelectedIndex;
        if ((uint)row < (uint)_rows.Count && _rows[row].Kind == RowKind.Save) LoadPressed?.Invoke(_saves[_rows[row].Index].Path);
    }

    private void DeleteSelected()
    {
        int row = SelectedIndex;
        if (RowPath(row) is not string path) return;
        if (KindOf(row) is RowKind.Save or RowKind.UnreadableSave) SaveStore.Delete(path);
        else ReplayStore.Delete(path);
        Refresh(Dir, SaveDir);
    }

    private void UpdateButtons()
    {
        int row = SelectedIndex;
        bool picked = (uint)row < (uint)_rows.Count;
        LoadButton.Disabled = !picked || _rows[row].Kind != RowKind.Save;
        WatchButton.Disabled = !picked || _rows[row].Kind != RowKind.Replay; // an unreadable row can only be deleted
        DeleteButton.Disabled = !picked;
    }
}
