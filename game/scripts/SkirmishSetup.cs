using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Rts.Sim.Data;

namespace Rts.Game;

/// <summary>
/// The skirmish setup screen (M6-V1, M6-V2; docs/02 "Game flow" 2): the map seed (typed, or Random), the map size
/// (<see cref="LaunchOptions.MapSizes"/>), and two slots, "You" (player 0) and "AI" (player 1), each with a faction list,
/// the AI slot with a difficulty list. Text from <c>ui.json</c> <c>setup</c>, faction names from the data's <c>displayName</c>.
/// </summary>
/// <remarks>
/// The faction lists hold every loaded faction in id order (defaults: faction 0 for "You", faction 1 for "AI", the match's
/// old fixed assignment); the difficulty list Easy, Normal, Hard in <see cref="AiDifficulty"/> order (default Normal). The
/// screen holds only what the player picked; Start raises <see cref="StartPressed"/> with the picks as match flags
/// (<see cref="MatchArgs"/>) and <see cref="Main"/> starts the match through <see cref="LaunchOptions.Parse"/>.
/// </remarks>
public partial class SkirmishSetup : CanvasLayer
{
    /// <summary>The seed the screen shows first: the match's own default (<see cref="SimRunner.Seed"/>).</summary>
    public const ulong DefaultSeed = 1;

    /// <summary>Each difficulty list item's text key, indexed by <see cref="AiDifficulty"/>.</summary>
    public static readonly SetupText[] DifficultyKeys = { SetupText.DifficultyEasy, SetupText.DifficultyNormal, SetupText.DifficultyHard };

    private readonly RandomNumberGenerator _rng = new();
    private string[] _factionKeys = Array.Empty<string>();

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

    /// <summary>Each slot's faction list (one item per loaded faction, id order), slot 0 first.</summary>
    public OptionButton[] FactionOptions { get; } = new OptionButton[2];

    /// <summary>The AI slot's difficulty list (one item per <see cref="AiDifficulty"/>).</summary>
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

    /// <summary>The faction id picked for <paramref name="slot"/> (0 = "You", 1 = "AI"), or -1 with no factions loaded.</summary>
    public int FactionPick(int slot)
    {
        OptionButton list = FactionOptions[slot];
        return list.ItemCount == 0 ? -1 : Math.Clamp(list.Selected, 0, list.ItemCount - 1);
    }

    /// <summary>The AI slot's picked difficulty.</summary>
    public AiDifficulty DifficultyPick => (AiDifficulty)Math.Clamp(DifficultyOption.Selected, 0, DifficultyKeys.Length - 1);

    /// <summary>
    /// The picks as the command line a dev run would pass (<c>--seed &lt;n&gt; --map-size &lt;n&gt; --faction 1 &lt;id&gt;
    /// --faction 2 &lt;id&gt; --ai &lt;difficulty&gt;</c>); null while the seed field holds no number. A slot with no faction (no
    /// factions loaded) passes no <c>--faction</c>.
    /// </summary>
    public string[]? MatchArgs()
    {
        if (Seed is not ulong seed) return null;
        var args = new List<string> { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--map-size", MapSize.ToString(CultureInfo.InvariantCulture) };
        for (int slot = 0; slot < FactionOptions.Length; slot++)
        {
            int f = FactionPick(slot);
            if (f < 0 || f >= _factionKeys.Length) continue;
            args.Add("--faction");
            args.Add((slot + 1).ToString(CultureInfo.InvariantCulture));
            args.Add(_factionKeys[f]);
        }
        args.Add("--ai");
        args.Add(DataLimits.AiDifficultyIds[(int)DifficultyPick]);
        return args.ToArray();
    }

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
        MapSizeOption = List("MapSize");
        foreach (int size in LaunchOptions.MapSizes) MapSizeOption.AddItem(ui.MapSizeItem(size));
        MapSizeOption.Select(0);
        grid.AddChild(MapSizeOption);
        grid.AddChild(new Control());

        // The slots: a heading row, then one row per slot.
        grid.AddChild(new Control());
        grid.AddChild(ShellUi.Text(ui.Setup(SetupText.Faction), 16));
        grid.AddChild(ShellUi.Text(ui.Setup(SetupText.Difficulty), 16));
        _factionKeys = new string[data.Factions.Length];
        for (int f = 0; f < _factionKeys.Length; f++) _factionKeys[f] = data.Factions[f].Key;
        for (int slot = 0; slot < 2; slot++)
        {
            SlotLabels[slot] = ShellUi.Text(ui.Setup(slot == 0 ? SetupText.SlotHuman : SetupText.SlotAi));
            grid.AddChild(SlotLabels[slot]);
            FactionOptions[slot] = List($"Faction{slot}");
            foreach (FactionDef faction in data.Factions) FactionOptions[slot].AddItem(faction.DisplayName);
            // The defaults are the match's own seats: player p plays faction p mod factions.
            if (data.Factions.Length > 0) FactionOptions[slot].Select(slot % data.Factions.Length);
            grid.AddChild(FactionOptions[slot]);
            if (slot == 0)
            {
                grid.AddChild(new Control()); // the player's own slot has no difficulty
                continue;
            }
            DifficultyOption = List("Difficulty");
            foreach (SetupText key in DifficultyKeys) DifficultyOption.AddItem(ui.Setup(key));
            DifficultyOption.Select((int)AiDifficulty.Normal);
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
            if (MatchArgs() is string[] args) StartPressed?.Invoke(args);
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

    private static OptionButton List(string name)
    {
        var option = new OptionButton { Name = name, CustomMinimumSize = new Vector2(240f, 40f) };
        option.AddThemeFontSizeOverride("font_size", 20);
        return option;
    }
}
