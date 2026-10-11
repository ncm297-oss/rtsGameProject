using System;
using System.Globalization;
using Godot;
using Rts.Sim.Data;

namespace Rts.Game;

/// <summary>
/// The skirmish setup screen (M6-V1, docs/02 "Game flow" 2): the map seed (typed, or Random), the map size
/// (<see cref="LaunchOptions.MapSizes"/>), and two slots, "You" and "AI". Text from <c>ui.json</c> <c>setup</c>.
/// </summary>
/// <remarks>
/// Each slot shows the faction it plays today (player p plays faction p, as <c>World.FactionOf</c> rules until the lobby)
/// and the AI slot a difficulty, both disabled with the <c>coming_soon</c> text: M6-V2 wires them to the seats. The screen
/// holds only what the player picked; Start raises <see cref="StartPressed"/> and <see cref="Main"/> starts the match.
/// </remarks>
public partial class SkirmishSetup : CanvasLayer
{
    /// <summary>The seed the screen shows first: the match's own default (<see cref="SimRunner.Seed"/>).</summary>
    public const ulong DefaultSeed = 1;

    private readonly RandomNumberGenerator _rng = new();

    /// <summary>Raised by Start with the seed and the map side in cells.</summary>
    public event Action<ulong, int>? StartPressed;

    /// <summary>Raised by Back.</summary>
    public event Action? BackPressed;

    /// <summary>The seed field (digits; Start is disabled while it holds no number).</summary>
    public LineEdit SeedEdit { get; private set; } = null!;

    /// <summary>The button that writes a random seed into <see cref="SeedEdit"/>.</summary>
    public Button RandomButton { get; private set; } = null!;

    /// <summary>The map size list, one item per <see cref="LaunchOptions.MapSizes"/> entry.</summary>
    public OptionButton MapSizeOption { get; private set; } = null!;

    /// <summary>Each slot's faction list (disabled until M6-V2), slot 0 first.</summary>
    public OptionButton[] FactionOptions { get; } = new OptionButton[2];

    /// <summary>The AI slot's difficulty list (disabled until M6-V2).</summary>
    public OptionButton DifficultyOption { get; private set; } = null!;

    /// <summary>Each slot's name label ("You", "AI").</summary>
    public Label[] SlotLabels { get; } = new Label[2];

    /// <summary>The Start button.</summary>
    public Button StartButton { get; private set; } = null!;

    /// <summary>The Back button.</summary>
    public Button BackButton { get; private set; } = null!;

    /// <summary>The seed in <see cref="SeedEdit"/>, or null when it isn't a whole number from 0 to 2^64 - 1.</summary>
    public ulong? Seed =>
        ulong.TryParse(SeedEdit.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed) ? seed : null;

    /// <summary>The picked map side in cells.</summary>
    public int MapSize => LaunchOptions.MapSizes[Math.Clamp(MapSizeOption.Selected, 0, LaunchOptions.MapSizes.Length - 1)];

    /// <summary>Builds the screen's controls with <paramref name="ui"/>'s text and <paramref name="data"/>'s faction names.</summary>
    public void Build(UiText ui, GameData data)
    {
        _rng.Randomize();
        AddChild(ShellUi.Backdrop(new Color(0.09f, 0.08f, 0.07f)));
        VBoxContainer column = ShellUi.CentredColumn(this, 20);
        column.AddChild(ShellUi.Heading(ui.Setup(SetupText.Title)));

        var grid = new GridContainer { Name = "Grid", Columns = 3 };
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

        grid.AddChild(ShellUi.Text(ui.Setup(SetupText.MapSize)));
        MapSizeOption = new OptionButton { Name = "MapSize", CustomMinimumSize = new Vector2(240f, 40f) };
        MapSizeOption.AddThemeFontSizeOverride("font_size", 20);
        foreach (int size in LaunchOptions.MapSizes) MapSizeOption.AddItem($"{size} × {size}");
        MapSizeOption.Select(0);
        grid.AddChild(MapSizeOption);
        grid.AddChild(new Control());

        // The slots: a heading row, then one row per slot.
        grid.AddChild(new Control());
        grid.AddChild(ShellUi.Text(ui.Setup(SetupText.Faction), 16));
        grid.AddChild(ShellUi.Text(ui.Setup(SetupText.Difficulty), 16));
        string coming = ui.Setup(SetupText.ComingSoon);
        for (int slot = 0; slot < 2; slot++)
        {
            SlotLabels[slot] = ShellUi.Text(ui.Setup(slot == 0 ? SetupText.SlotHuman : SetupText.SlotAi));
            grid.AddChild(SlotLabels[slot]);
            // Today's fixed assignment: player p plays faction p (World.FactionOf).
            string faction = data.Factions.Length > 0 ? data.Factions[slot % data.Factions.Length].DisplayName : "";
            FactionOptions[slot] = Disabled($"Faction{slot}", faction, coming);
            grid.AddChild(FactionOptions[slot]);
            if (slot == 0)
            {
                grid.AddChild(new Control()); // the player's own slot has no difficulty
                continue;
            }
            DifficultyOption = Disabled("Difficulty", coming, coming);
            grid.AddChild(DifficultyOption);
        }

        var buttons = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 24);
        column.AddChild(buttons);
        BackButton = ShellUi.Button("Back", ui.Setup(SetupText.Back), 180f);
        BackButton.Pressed += () => BackPressed?.Invoke();
        buttons.AddChild(BackButton);
        StartButton = ShellUi.Button("Start", ui.Setup(SetupText.Start), 180f);
        StartButton.Pressed += () =>
        {
            if (Seed is ulong seed) StartPressed?.Invoke(seed, MapSize);
        };
        buttons.AddChild(StartButton);
        Validate();
    }

    /// <summary>What Random does: a fresh seed in the field (view-side randomness: the seed is the sim's input, not its state).</summary>
    public void RandomSeed()
    {
        SeedEdit.Text = _rng.Randi().ToString(CultureInfo.InvariantCulture);
        Validate();
    }

    private void Validate() => StartButton.Disabled = Seed == null;

    private static OptionButton Disabled(string name, string item, string tooltip)
    {
        var option = new OptionButton { Name = name, Disabled = true, TooltipText = tooltip, CustomMinimumSize = new Vector2(240f, 40f) };
        option.AddThemeFontSizeOverride("font_size", 20);
        option.AddItem(item);
        option.Select(0);
        return option;
    }
}
