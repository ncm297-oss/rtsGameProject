using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Game;

/// <summary>
/// The skirmish setup screen (M6-V1, M6-V2, M6-V3, M6-V5; docs/02 "Game flow" 2): the map seed (typed, or Random), the map
/// size (<see cref="LaunchOptions.MapSizes"/>), and 2 to <see cref="MaxSlots"/> player slots (free-for-all): slot 1 "You"
/// (player 0, always the local human), each other slot "AI" (player s - 1), each with a faction list and a colour list, the
/// AI slots with a difficulty list. Add player / Remove player show or hide the last slot. M6-V15: a game speed list
/// (<see cref="GameSpeeds"/>, default Normal) and the map generator's forests and gold mines counts (defaults
/// <see cref="LaunchOptions.DefaultForests"/> / <see cref="LaunchOptions.DefaultMines"/>; floors <see cref="MinForestsFor"/> /
/// <see cref="MinMinesFor"/> the shown slots, so every base keeps its start forest and mines; cap
/// <see cref="MapGenParams.MaxResourceGroups"/>). Text from <c>ui.json</c>
/// <c>setup</c> (the colours' names and swatches from its colour table), faction names from the data's <c>displayName</c>.
/// </summary>
/// <remarks>
/// The faction lists hold every loaded faction in id order (defaults: slot s plays faction <c>s mod factions</c>, the match's
/// old fixed assignment); each colour list the <see cref="UiText.ColourCount"/> colours in table order (default: slot s plays
/// colour s; colours are unique per match, so each list greys the colours the other shown slots hold and a pick of one moves
/// to the first free colour, <see cref="PickColour"/>, BUG-0631 item 3); each difficulty list Easy, Normal, Hard in
/// <see cref="AiDifficulty"/> order (default Normal). All four rows are built once and a removed slot is only hidden, so its
/// picks come back with it (its colour moved if a shown slot took it meanwhile). The screen holds only what the player
/// picked; Start raises <see cref="StartPressed"/> with the picks as match flags (<see cref="MatchArgs"/>) and
/// <see cref="Main"/> starts the match through <see cref="LaunchOptions.Parse"/>.
/// </remarks>
public partial class SkirmishSetup : CanvasLayer
{
    /// <summary>The seed the screen shows first: the match's own default (<see cref="SimRunner.Seed"/>).</summary>
    public const ulong DefaultSeed = 1;

    /// <summary>Most slots the screen offers (the generator's most start locations, <see cref="LaunchOptions.SlotCount"/>).</summary>
    public const int MaxSlots = LaunchOptions.SlotCount;

    /// <summary>Each difficulty list item's text key, indexed by <see cref="AiDifficulty"/>.</summary>
    public static readonly SetupText[] DifficultyKeys = { SetupText.DifficultyEasy, SetupText.DifficultyNormal, SetupText.DifficultyHard };

    /// <summary>The game speeds the setup offers (M6-V15; docs/02 "Game flow" 3: Slow 0.75x, Normal 1x, Fast 1.5x), passed as <c>--speed</c>.</summary>
    public static readonly double[] GameSpeeds = { 0.75, 1.0, 1.5 };

    /// <summary>Each game speed list item's text key, indexed like <see cref="GameSpeeds"/>.</summary>
    public static readonly SetupText[] SpeedKeys = { SetupText.SpeedSlow, SetupText.SpeedNormal, SetupText.SpeedFast };

    /// <summary>The game speed the list shows first: Normal (1x).</summary>
    public const int DefaultSpeedIndex = 1;

    /// <summary>
    /// Fewest forests the setup offers for <paramref name="players"/> start locations (M6-V15): one each, so every base gets its
    /// start forest (the placer gives start forests only when the count covers every location, M5-4a).
    /// </summary>
    public static int MinForestsFor(int players) => Math.Clamp(players, 0, MapGenParams.MaxResourceGroups);

    /// <summary>
    /// Fewest gold mines the setup offers for <paramref name="players"/> start locations (M6-V15): <paramref name="startMines"/>
    /// (<c>rules.json</c> <c>startMines.count</c>) each, so every base gets its start mines (the placer shorts every base in
    /// whole rounds below that, M5-H1).
    /// </summary>
    public static int MinMinesFor(int players, int startMines) => Math.Clamp(players * Math.Max(startMines, 0), 0, MapGenParams.MaxResourceGroups);

    private readonly RandomNumberGenerator _rng = new();
    private int _startMines;
    private string[] _factionKeys = Array.Empty<string>();
    private readonly Control[][] _rows = new Control[MaxSlots][];

    /// <summary>Raised by Start with the picks as match flags (<see cref="MatchArgs"/>).</summary>
    public event Action<string[]>? StartPressed;

    /// <summary>Raised by Back.</summary>
    public event Action? BackPressed;

    /// <summary>The seed field (digits; Start is disabled while it holds no number).</summary>
    public LineEdit SeedEdit { get; private set; } = null!;

    /// <summary>The button that writes a random seed into <see cref="SeedEdit"/>.</summary>
    public Button RandomButton { get; private set; } = null!;

    /// <summary>The map size list, one item per <see cref="LaunchOptions.MapSizes"/> entry.</summary>
    public OptionButton MapSizeOption { get; private set; } = null!;

    /// <summary>Each slot's faction list (one item per loaded faction, id order), slot 0 first; all <see cref="MaxSlots"/>, shown or not.</summary>
    public OptionButton[] FactionOptions { get; } = new OptionButton[MaxSlots];

    /// <summary>Each slot's colour list (one item per <c>ui.json</c> colour, table order), slot 0 first (M6-V3).</summary>
    public OptionButton[] ColourOptions { get; } = new OptionButton[MaxSlots];

    /// <summary>Each AI slot's difficulty list (one item per <see cref="AiDifficulty"/>); null for slot 0, the local player (M6-V5).</summary>
    public OptionButton?[] DifficultyOptions { get; } = new OptionButton?[MaxSlots];

    /// <summary>Slot 1's (the first AI slot's) difficulty list.</summary>
    public OptionButton DifficultyOption => DifficultyOptions[1]!;

    /// <summary>Each slot's name label ("You", "AI").</summary>
    public Label[] SlotLabels { get; } = new Label[MaxSlots];

    /// <summary>Slots shown, 2 to <see cref="MaxSlots"/> (M6-V5): the match's players.</summary>
    public int SlotCount { get; private set; } = LaunchOptions.MinPlayers;

    /// <summary>The button that shows one more slot (disabled at <see cref="MaxSlots"/>).</summary>
    public Button AddSlotButton { get; private set; } = null!;

    /// <summary>The button that hides the last slot (disabled at two).</summary>
    public Button RemoveSlotButton { get; private set; } = null!;

    /// <summary>The Start button.</summary>
    public Button StartButton { get; private set; } = null!;

    /// <summary>The Back button.</summary>
    public Button BackButton { get; private set; } = null!;

    /// <summary>The seed in <see cref="SeedEdit"/>, or null when it isn't a whole number from 0 to 2^64 - 1.</summary>
    public ulong? Seed =>
        ulong.TryParse(SeedEdit.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed) ? seed : null;

    /// <summary>The picked map side in cells.</summary>
    public int MapSize => LaunchOptions.MapSizes[Math.Clamp(MapSizeOption.Selected, 0, LaunchOptions.MapSizes.Length - 1)];

    /// <summary>The game speed list, one item per <see cref="GameSpeeds"/> entry (M6-V15).</summary>
    public OptionButton SpeedOption { get; private set; } = null!;

    /// <summary>The picked game speed (a <see cref="GameSpeeds"/> entry).</summary>
    public double SpeedPick => GameSpeeds[Math.Clamp(SpeedOption.Selected, 0, GameSpeeds.Length - 1)];

    /// <summary>The forests count (M6-V15): <see cref="MinForestsFor"/> the shown slots to <see cref="MapGenParams.MaxResourceGroups"/>.</summary>
    public SpinBox ForestsSpin { get; private set; } = null!;

    /// <summary>The gold mines count (M6-V15): <see cref="MinMinesFor"/> the shown slots to <see cref="MapGenParams.MaxResourceGroups"/>.</summary>
    public SpinBox MinesSpin { get; private set; } = null!;

    /// <summary>The picked forests count, inside the range the spin box allows.</summary>
    public int ForestsPick => (int)Math.Clamp(Math.Round(ForestsSpin.Value), ForestsSpin.MinValue, ForestsSpin.MaxValue);

    /// <summary>The picked gold mines count, inside the range the spin box allows.</summary>
    public int MinesPick => (int)Math.Clamp(Math.Round(MinesSpin.Value), MinesSpin.MinValue, MinesSpin.MaxValue);

    /// <summary>The faction id picked for <paramref name="slot"/> (0 = "You"), or -1 with no factions loaded.</summary>
    public int FactionPick(int slot)
    {
        OptionButton list = FactionOptions[slot];
        return list.ItemCount == 0 ? -1 : Math.Clamp(list.Selected, 0, list.ItemCount - 1);
    }

    /// <summary>The colour picked for <paramref name="slot"/> (0-based index into <c>ui.json</c>'s colour table).</summary>
    public int ColourPick(int slot) => Math.Clamp(ColourOptions[slot].Selected, 0, UiText.ColourCount - 1);

    /// <summary>
    /// Picks <paramref name="colour"/> (0-based) for <paramref name="slot"/>, as a click on its list does. Colours are unique per
    /// match (docs/02 "Game flow" 2, BUG-0631 item 3): a colour another shown slot holds is refused and the slot takes the first
    /// colour no other shown slot holds (<see cref="LaunchOptions.FirstFreeColour(ReadOnlySpan{bool})"/>; with two slots,
    /// <see cref="LaunchOptions.FirstFreeColour(int)"/>). Then each list greys the colours the other shown slots hold.
    /// </summary>
    public void PickColour(int slot, int colour)
    {
        Span<bool> held = stackalloc bool[UiText.ColourCount];
        HeldByOthers(slot, held);
        int c = Math.Clamp(colour, 0, UiText.ColourCount - 1);
        if (held[c]) c = LaunchOptions.FirstFreeColour(held);
        ColourOptions[slot].Select(c);
        GreyTakenColours();
    }

    /// <summary>Shows one more slot (an AI slot), up to <see cref="MaxSlots"/>; its colour moves to the first free one if a shown slot holds it.</summary>
    public void AddSlot()
    {
        if (SlotCount >= MaxSlots) return;
        int slot = SlotCount++;
        foreach (Control cell in _rows[slot]) cell.Visible = true;
        PickColour(slot, ColourPick(slot));
        UpdateSlotButtons();
    }

    /// <summary>Hides the last slot, down to two; its picks are kept for when it is shown again.</summary>
    public void RemoveSlot()
    {
        if (SlotCount <= LaunchOptions.MinPlayers) return;
        int slot = --SlotCount;
        foreach (Control cell in _rows[slot]) cell.Visible = false;
        GreyTakenColours();
        UpdateSlotButtons();
    }

    // Marks the colours the shown slots other than `slot` hold.
    private void HeldByOthers(int slot, Span<bool> held)
    {
        held.Clear();
        for (int s = 0; s < SlotCount; s++)
            if (s != slot) held[ColourPick(s)] = true;
    }

    // Each colour list greys (disables) the colours the other shown slots hold, so the popup can't pick them.
    private void GreyTakenColours()
    {
        Span<bool> held = stackalloc bool[UiText.ColourCount];
        for (int slot = 0; slot < ColourOptions.Length; slot++)
        {
            HeldByOthers(slot, held);
            OptionButton list = ColourOptions[slot];
            for (int c = 0; c < list.ItemCount; c++) list.SetItemDisabled(c, held[c]);
        }
    }

    private void UpdateSlotButtons()
    {
        AddSlotButton.Disabled = SlotCount >= MaxSlots;
        RemoveSlotButton.Disabled = SlotCount <= LaunchOptions.MinPlayers;
        // M6-V15: the resource counts' floors follow the shown slots (one start forest and the start mines a base); a count
        // under a raised floor is raised to it, a count kept over a lowered one stays.
        SetFloor(ForestsSpin, MinForestsFor(SlotCount));
        SetFloor(MinesSpin, MinMinesFor(SlotCount, _startMines));
    }

    private static void SetFloor(SpinBox spin, int floor)
    {
        spin.MinValue = floor;
        if (spin.Value < floor) spin.Value = floor;
    }

    /// <summary>The first AI slot's (slot 1's) picked difficulty.</summary>
    public AiDifficulty DifficultyPick => DifficultyPickOf(1);

    /// <summary>AI slot <paramref name="slot"/>'s (1 to <see cref="MaxSlots"/> - 1) picked difficulty.</summary>
    public AiDifficulty DifficultyPickOf(int slot) =>
        (AiDifficulty)Math.Clamp(DifficultyOptions[slot]?.Selected ?? (int)LaunchOptions.DefaultAi, 0, DifficultyKeys.Length - 1);

    /// <summary>
    /// The picks as the command line a dev run would pass (<c>--seed &lt;n&gt; --map-size &lt;n&gt; --units 0 --players &lt;n&gt;</c>,
    /// then per shown slot <c>--colour &lt;slot&gt; &lt;n&gt; --faction &lt;slot&gt; &lt;id&gt;</c>, then per AI slot
    /// <c>--ai &lt;slot&gt; &lt;difficulty&gt;</c>); null while the seed field holds no number. <c>--units 0</c>: the menu's match
    /// opens with a Town Hall and the starting workers a side, no dev army (M6-V3, BUG-0604). A slot with no faction (no
    /// factions loaded) passes no <c>--faction</c>. M6-V15: <c>--speed &lt;x&gt; --forests &lt;n&gt; --mines &lt;n&gt;</c> after
    /// <c>--players</c> (the speed list's and the two counts' picks).
    /// </summary>
    public string[]? MatchArgs()
    {
        // BUG-1031: a count typed without Enter is only taken on focus-out, which a one-frame click on Start delivers after
        // Start's Pressed; take it now, so the match uses the number the box shows.
        Commit(ForestsSpin);
        Commit(MinesSpin);
        if (Seed is not ulong seed) return null;
        var args = new List<string>
        {
            "--seed", seed.ToString(CultureInfo.InvariantCulture), "--map-size", MapSize.ToString(CultureInfo.InvariantCulture), "--units", "0",
            "--players", SlotCount.ToString(CultureInfo.InvariantCulture),
            "--speed", SpeedPick.ToString("0.##", CultureInfo.InvariantCulture),
            "--forests", ForestsPick.ToString(CultureInfo.InvariantCulture), "--mines", MinesPick.ToString(CultureInfo.InvariantCulture),
        };
        for (int slot = 0; slot < SlotCount; slot++)
        {
            string slotArg = (slot + 1).ToString(CultureInfo.InvariantCulture);
            args.Add("--colour");
            args.Add(slotArg);
            args.Add((ColourPick(slot) + 1).ToString(CultureInfo.InvariantCulture));
            int f = FactionPick(slot);
            if (f < 0 || f >= _factionKeys.Length) continue;
            args.Add("--faction");
            args.Add(slotArg);
            args.Add(_factionKeys[f]);
        }
        for (int slot = 1; slot < SlotCount; slot++)
        {
            args.Add("--ai");
            args.Add((slot + 1).ToString(CultureInfo.InvariantCulture));
            args.Add(DataLimits.AiDifficultyIds[(int)DifficultyPickOf(slot)]);
        }
        return args.ToArray();
    }

    /// <summary>Builds the screen's controls with <paramref name="ui"/>'s text and <paramref name="data"/>'s faction names.</summary>
    public void Build(UiText ui, GameData data)
    {
        _rng.Randomize();
        AddChild(ShellUi.Backdrop(new Color(0.09f, 0.08f, 0.07f)));
        VBoxContainer column = ShellUi.CentredColumn(this, 20);
        column.AddChild(ShellUi.Heading(ui.Setup(SetupText.Title)));

        var grid = new GridContainer { Name = "Grid", Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 12);
        column.AddChild(grid);

        grid.AddChild(ShellUi.Text(ui.Setup(SetupText.Seed)));
        SeedEdit = new LineEdit { Name = "Seed", Text = DefaultSeed.ToString(CultureInfo.InvariantCulture), CustomMinimumSize = new Vector2(240f, 40f), MaxLength = 20 };
        SeedEdit.AddThemeFontSizeOverride("font_size", 20);
        SeedEdit.TextChanged += _ => Validate();
        grid.AddChild(SeedEdit);
        RandomButton = ShellUi.Button("Random", ui.Setup(SetupText.Random), 160f);
        RandomButton.Pressed += RandomSeed;
        grid.AddChild(RandomButton);
        grid.AddChild(new Control());

        grid.AddChild(ShellUi.Text(ui.Setup(SetupText.MapSize)));
        MapSizeOption = List("MapSize");
        foreach (int size in LaunchOptions.MapSizes) MapSizeOption.AddItem(ui.MapSizeItem(size));
        MapSizeOption.Select(0);
        grid.AddChild(MapSizeOption);
        // M6-V15: the game speed beside the map size, the generator's two counts on a row of their own.
        grid.AddChild(ShellUi.Text(ui.Setup(SetupText.Speed)));
        SpeedOption = List("Speed");
        foreach (SetupText key in SpeedKeys) SpeedOption.AddItem(ui.Setup(key));
        SpeedOption.Select(DefaultSpeedIndex);
        grid.AddChild(SpeedOption);

        _startMines = data.Rules.StartMineCount;
        grid.AddChild(ShellUi.Text(ui.Setup(SetupText.Forests)));
        ForestsSpin = Count("Forests", LaunchOptions.DefaultForests);
        grid.AddChild(ForestsSpin);
        grid.AddChild(ShellUi.Text(ui.Setup(SetupText.Mines)));
        MinesSpin = Count("Mines", LaunchOptions.DefaultMines);
        grid.AddChild(MinesSpin);

        // The slots: a heading row, then one row per slot (all built; the ones past SlotCount hidden, which the grid skips).
        grid.AddChild(new Control());
        grid.AddChild(ShellUi.Text(ui.Setup(SetupText.Faction), 16));
        grid.AddChild(ShellUi.Text(ui.Setup(SetupText.Colour), 16));
        grid.AddChild(ShellUi.Text(ui.Setup(SetupText.Difficulty), 16));
        _factionKeys = new string[data.Factions.Length];
        for (int f = 0; f < _factionKeys.Length; f++) _factionKeys[f] = data.Factions[f].Key;
        for (int slot = 0; slot < MaxSlots; slot++)
        {
            SlotLabels[slot] = ShellUi.Text(ui.Setup(slot == 0 ? SetupText.SlotHuman : SetupText.SlotAi));
            FactionOptions[slot] = List($"Faction{slot}");
            foreach (FactionDef faction in data.Factions) FactionOptions[slot].AddItem(faction.DisplayName);
            // The defaults are the match's own seats: player p plays faction p mod factions.
            if (data.Factions.Length > 0) FactionOptions[slot].Select(slot % data.Factions.Length);
            ColourOptions[slot] = List($"Colour{slot}");
            for (int c = 0; c < UiText.ColourCount; c++) ColourOptions[slot].AddIconItem(Swatch(ui.ColourRgb(c)), ui.ColourName(c));
            // The defaults are the match's own: slot s plays colour s.
            ColourOptions[slot].Select(slot % UiText.ColourCount);
            int picker = slot;
            ColourOptions[slot].ItemSelected += index => PickColour(picker, (int)index);
            Control difficulty;
            if (slot == 0) difficulty = new Control(); // the player's own slot has no difficulty
            else
            {
                OptionButton list = List(slot == 1 ? "Difficulty" : $"Difficulty{slot}");
                foreach (SetupText key in DifficultyKeys) list.AddItem(ui.Setup(key));
                list.Select((int)LaunchOptions.DefaultAi); // --skirmish's seats too (BUG-0630)
                DifficultyOptions[slot] = list;
                difficulty = list;
            }
            _rows[slot] = new Control[] { SlotLabels[slot], FactionOptions[slot], ColourOptions[slot], difficulty };
            foreach (Control cell in _rows[slot])
            {
                cell.Visible = slot < SlotCount;
                grid.AddChild(cell);
            }
        }

        var slotButtons = new HBoxContainer { Name = "SlotButtons", Alignment = BoxContainer.AlignmentMode.Center };
        slotButtons.AddThemeConstantOverride("separation", 24);
        column.AddChild(slotButtons);
        AddSlotButton = ShellUi.Button("AddSlot", ui.Setup(SetupText.AddSlot), 220f);
        AddSlotButton.Pressed += AddSlot;
        slotButtons.AddChild(AddSlotButton);
        RemoveSlotButton = ShellUi.Button("RemoveSlot", ui.Setup(SetupText.RemoveSlot), 220f);
        RemoveSlotButton.Pressed += RemoveSlot;
        slotButtons.AddChild(RemoveSlotButton);

        var buttons = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 24);
        column.AddChild(buttons);
        BackButton = ShellUi.Button("Back", ui.Setup(SetupText.Back), 180f);
        BackButton.Pressed += () => BackPressed?.Invoke();
        buttons.AddChild(BackButton);
        StartButton = ShellUi.Button("Start", ui.Setup(SetupText.Start), 180f);
        StartButton.Pressed += () =>
        {
            if (MatchArgs() is string[] args) StartPressed?.Invoke(args);
        };
        buttons.AddChild(StartButton);
        GreyTakenColours();
        UpdateSlotButtons();
        Validate();
    }

    /// <summary>What Random does: a fresh seed in the field (view-side randomness: the seed is the sim's input, not its state).</summary>
    public void RandomSeed()
    {
        SeedEdit.Text = _rng.Randi().ToString(CultureInfo.InvariantCulture);
        Validate();
    }

    private void Validate() => StartButton.Disabled = Seed == null;

    // A small square of the colour for its list item.
    private static ImageTexture Swatch(uint rgb)
    {
        Image image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        image.Fill(UnitViews.ColorFromRgb(rgb));
        return ImageTexture.CreateFromImage(image);
    }

    private static OptionButton List(string name)
    {
        var option = new OptionButton { Name = name, CustomMinimumSize = new Vector2(240f, 40f) };
        option.AddThemeFontSizeOverride("font_size", 20);
        return option;
    }

    // A whole-number count up to the generator's cap; its floor is set by UpdateSlotButtons.
    private static SpinBox Count(string name, int value)
    {
        var spin = new SpinBox
        {
            Name = name, CustomMinimumSize = new Vector2(240f, 40f), MinValue = 0, MaxValue = MapGenParams.MaxResourceGroups, Step = 1,
            Rounded = true, Value = value,
        };
        LineEdit edit = spin.GetLineEdit();
        edit.AddThemeFontSizeOverride("font_size", 20);
        // BUG-1031 item 2: Enter on an emptied box keeps the value but leaves the box empty; show the count again. Connected
        // after the SpinBox's own handler, so this runs once it has taken (or refused) the text.
        edit.TextSubmitted += _ => Redraw(spin);
        // The SpinBox redraws its text after a value change only later in the frame; MatchArgs' Apply reads the text, so a
        // value set in code (a floor raised by Add player, a test) is shown at once, or the Apply would put the old one back.
        spin.ValueChanged += _ => Redraw(spin);
        return spin;
    }

    // Takes the box's typed text as the value (SpinBox.Apply: what Enter does), then redraws the box.
    private static void Commit(SpinBox spin)
    {
        spin.Apply();
        Redraw(spin);
    }

    // The box shows the value it holds (text the SpinBox refused, an empty one included, is replaced).
    private static void Redraw(SpinBox spin)
    {
        LineEdit edit = spin.GetLineEdit();
        string shown = ((long)Math.Round(spin.Value)).ToString(CultureInfo.InvariantCulture);
        if (edit.Text != shown) edit.Text = shown;
    }
}
