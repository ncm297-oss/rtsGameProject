using System.Globalization;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The HUD's resource bar, top right (docs/02 "HUD layout"; M3-V1, population M3-V3): the local player's gold, wood and population.</summary>
/// <remarks>
/// Reads <see cref="World.Gold"/>, <see cref="World.Wood"/>, <see cref="World.HalfPop"/> and <see cref="World.HalfPopCap"/>
/// every frame. This label shows "&lt;gold name&gt; N  &lt;wood name&gt; N" (the faction's <c>resources.gold / wood.displayName</c>);
/// its child <see cref="PopLabel"/> shows "&lt;Pop&gt; a / b" (<c>ui.json</c> <c>hud.pop</c>, numbers from
/// <see cref="PopText"/>: half-pop / 2, ".5" for odd), red at the cap. When the player's age rises (<see cref="World.Age"/>)
/// <see cref="AgeLabel"/> flashes the age tech's <c>displayName</c> ("Age II") for <see cref="FlashSeconds"/> of view time.
/// Its child <see cref="KillsLabel"/> (M4-V1) shows "&lt;K&gt; n / &lt;L&gt; n": the player's <see cref="World.Kills"/> and
/// <see cref="World.Losses"/> (labels <c>ui.json</c> <c>hud.kills</c> / <c>hud.losses</c>).
/// CLAUDE.md rule 8: no player-facing literal here. Building text allocates, so each label is rebuilt only when a number
/// it shows changes (the M2-H2 overlay rule); a steady frame is a few int compares.
/// </remarks>
public partial class ResourceBar : Label
{
    /// <summary>How long the age flash stays up, in seconds of view time.</summary>
    public const double FlashSeconds = 4.0;

    /// <summary>Width of the population label, in pixels, to the right of the gold and wood text.</summary>
    public const float PopWidth = 130f;

    /// <summary>Width of the kills / losses label, in pixels, under the population label.</summary>
    public const float KillsWidth = 130f;

    private static readonly StringName FontColor = "font_color";
    private static readonly Color PopColor = new(1f, 0.93f, 0.75f);
    private static readonly Color CapColor = new(1f, 0.3f, 0.25f);

    private SimRunner? _runner;
    private GameData? _data;
    private int _player;
    private string _goldName = "", _woodName = "", _popName = "", _killsName = "", _lossesName = "";
    private int _gold, _wood, _halfPop, _halfPopCap, _age, _kills, _losses;
    private int _popBuilds, _killBuilds;
    private bool _atCap;
    private double _flashLeft;

    /// <summary>Times the gold / wood text was rebuilt (only when a shown number changes).</summary>
    public int Builds { get; private set; }

    /// <summary>Times the population text was rebuilt (only when a shown number changes).</summary>
    public int PopBuilds => _popBuilds;

    /// <summary>The gold amount on show.</summary>
    public int ShownGold => _gold;

    /// <summary>The wood amount on show.</summary>
    public int ShownWood => _wood;

    /// <summary>Times the kills / losses text was rebuilt (only when a shown number changes).</summary>
    public int KillBuilds => _killBuilds;

    /// <summary>The kill count on show.</summary>
    public int ShownKills => _kills;

    /// <summary>The loss count on show.</summary>
    public int ShownLosses => _losses;

    /// <summary>The kills / losses label ("K n / L n"; M4-V1).</summary>
    public Label KillsLabel { get; private set; } = null!;

    /// <summary>The half-pop on show.</summary>
    public int ShownHalfPop => _halfPop;

    /// <summary>The half-pop cap on show.</summary>
    public int ShownHalfPopCap => _halfPopCap;

    /// <summary>True while the population is drawn red (at or over the cap).</summary>
    public bool ShownAtCap => _atCap;

    /// <summary>The population label ("Pop a / b").</summary>
    public Label PopLabel { get; private set; } = null!;

    /// <summary>The age flash label, under the bar; visible while it flashes.</summary>
    public Label AgeLabel { get; private set; } = null!;

    public override void _Ready()
    {
        PopLabel = new Label
        {
            Name = "Pop", MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(Size.X + 12f, 0f), Size = new Vector2(PopWidth, Size.Y),
        };
        PopLabel.AddThemeColorOverride(FontColor, PopColor);
        PopLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        PopLabel.AddThemeConstantOverride("outline_size", 4);
        PopLabel.AddThemeFontSizeOverride("font_size", 18);
        AgeLabel = new Label
        {
            // Below the kills / losses line (M4-V1), so the two never overlap.
            Name = "AgeFlash", MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(0f, 2f * Size.Y + 4f), Size = new Vector2(Size.X + 12f + PopWidth, 32f),
            HorizontalAlignment = HorizontalAlignment.Right, Visible = false,
        };
        AgeLabel.AddThemeColorOverride(FontColor, new Color(1f, 0.85f, 0.3f));
        AgeLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        AgeLabel.AddThemeConstantOverride("outline_size", 6);
        AgeLabel.AddThemeFontSizeOverride("font_size", 26);
        // Under the population, so the bar keeps its width at the screen's right edge.
        KillsLabel = new Label
        {
            Name = "Kills", MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(Size.X + 12f, Size.Y), Size = new Vector2(KillsWidth, Size.Y),
        };
        KillsLabel.AddThemeColorOverride(FontColor, PopColor);
        KillsLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        KillsLabel.AddThemeConstantOverride("outline_size", 4);
        KillsLabel.AddThemeFontSizeOverride("font_size", 16);
        AddChild(PopLabel);
        AddChild(KillsLabel);
        AddChild(AgeLabel);
    }

    /// <summary>Connects the bar to the match; call once after the sim exists.</summary>
    /// <param name="goldName">The faction's display name for gold (data).</param>
    /// <param name="woodName">The faction's display name for wood (data).</param>
    /// <param name="popName">The population label (<c>ui.json</c> <c>hud.pop</c>); empty shows the numbers alone.</param>
    /// <param name="killsName">The kills label (<c>ui.json</c> <c>hud.kills</c>); empty shows the number alone.</param>
    /// <param name="lossesName">The losses label (<c>ui.json</c> <c>hud.losses</c>); empty shows the number alone.</param>
    public void Init(SimRunner runner, int player, string goldName, string woodName, string popName = "", string killsName = "", string lossesName = "")
    {
        _killsName = killsName;
        _lossesName = lossesName;
        _killBuilds = 0;
        _runner = runner;
        _player = player;
        _goldName = goldName;
        _woodName = woodName;
        _popName = popName;
        Builds = 0;
        _popBuilds = 0;
        _flashLeft = 0;
        if (runner.Simulation is Simulation sim)
        {
            _data = sim.World.Data;
            _age = sim.World.Age(player);
            Sync(sim.World);
        }
    }

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is not Simulation sim) return;
        Sync(sim.World);
        Fade(delta);
    }

    /// <summary>Shows the player's current totals, rebuilding a text only if one of its numbers changed since the last build; starts the age flash when the age rose.</summary>
    public void Sync(World world)
    {
        if ((uint)_player >= (uint)world.Gold.Length) return;
        int gold = world.Gold[_player], wood = world.Wood[_player];
        if (Builds == 0 || gold != _gold || wood != _wood)
        {
            _gold = gold;
            _wood = wood;
            Builds++;
            Text = string.Create(CultureInfo.InvariantCulture, $"{_goldName} {gold}  {_woodName} {wood}");
        }
        int half = world.HalfPop[_player], cap = world.HalfPopCap[_player];
        if (_popBuilds == 0 || half != _halfPop || cap != _halfPopCap)
        {
            _halfPop = half;
            _halfPopCap = cap;
            _popBuilds++;
            string numbers = $"{PopText.Format(half)} / {PopText.Format(cap)}";
            PopLabel.Text = _popName.Length > 0 ? $"{_popName} {numbers}" : numbers;
            bool atCap = PopText.AtCap(half, cap);
            if (atCap != _atCap || _popBuilds == 1) PopLabel.AddThemeColorOverride(FontColor, atCap ? CapColor : PopColor);
            _atCap = atCap;
        }
        int kills = (uint)_player < (uint)world.Kills.Length ? world.Kills[_player] : 0;
        int losses = (uint)_player < (uint)world.Losses.Length ? world.Losses[_player] : 0;
        if (_killBuilds == 0 || kills != _kills || losses != _losses)
        {
            _kills = kills;
            _losses = losses;
            _killBuilds++;
            KillsLabel.Text = string.Create(CultureInfo.InvariantCulture, $"{Join(_killsName, kills)} / {Join(_lossesName, losses)}");
        }
        int age = world.Age(_player);
        if (age > _age && _data != null)
        {
            int k = age - 2; // AgeTechs entry k researched = Age k + 2 (docs/03 M3-5)
            if ((uint)k < (uint)_data.AgeTechs.Length)
            {
                AgeLabel.Text = _data.Techs[_data.AgeTechs[k]].DisplayName;
                AgeLabel.Visible = true;
                _flashLeft = FlashSeconds;
                AgeFlashes++;
            }
        }
        _age = age;
    }

    private static string Join(string label, int n) =>
        label.Length > 0 ? string.Create(CultureInfo.InvariantCulture, $"{label} {n}") : n.ToString(CultureInfo.InvariantCulture);

    /// <summary>Times the age flash started.</summary>
    public int AgeFlashes { get; private set; }

    // Pulses the flash and hides it after FlashSeconds (view time: presentation only).
    private void Fade(double delta)
    {
        if (_flashLeft <= 0) return;
        _flashLeft -= delta;
        if (_flashLeft <= 0)
        {
            AgeLabel.Visible = false;
            return;
        }
        float a = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin((float)(_flashLeft * 4.0)));
        AgeLabel.Modulate = new Color(1f, 1f, 1f, a);
    }
}
