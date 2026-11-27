using Godot;
using Rts.Sim;

namespace Rts.Game;

/// <summary>
/// The draw countdown (M6-V29, docs/02 "Game flow" 4): a line at the top centre of the HUD while the sim's draw clock runs
/// (<c>World.DrawTicksLeft</c> &gt;= 0, M6-S13), <c>ui.json</c> <c>hud.draw_countdown</c> with the time left rounded up to a
/// whole second in the match clock's format. It tells the player why the match is about to end and what stops it.
/// </summary>
/// <remarks>
/// Read after every tick the runner runs (<see cref="SimRunner.Ticked"/>, so a fast frame of several ticks skips none and a
/// replay shows it on the same ticks) and once when wired (a match loaded mid-clock shows it before its first tick). It
/// stores only what it shows (the second on the label); the label's text is rewritten only when that second changes. It
/// takes no click (<see cref="Control.MouseFilterEnum.Ignore"/>) and sits under the alert text, clear of the resource bar,
/// the minimap and the command card. Nothing without a HUD or <c>ui.json</c>.
/// </remarks>
public partial class DrawNotice : Node
{
    /// <summary>The label's top edge, in pixels from the top of the screen: under the replay mark (8-40) and the alert text (46-80).</summary>
    public const float Top = 86f;

    /// <summary>The label's height in pixels.</summary>
    public const float Height = 30f;

    /// <summary>
    /// The label's width in pixels, centred: the English line at its font size is 594 px wide (measured, M6-V29); 1152 px
    /// leaves 176 px each side. M6-VH10 (BUG-1792): a longer text (a translation, a rename) wraps inside this width and the
    /// label grows downward, so it stays centred and on screen.
    /// </summary>
    public const float Width = 800f;

    private SimRunner? _runner;
    private UiText? _ui;
    private Label? _label;

    /// <summary>The countdown label (null without a HUD or <c>ui.json</c>).</summary>
    public Label? TextLabel => _label;

    /// <summary>True while the notice is on screen.</summary>
    public bool Shown => _label != null && _label.Visible;

    /// <summary>The whole seconds the label shows (-1 before it first showed).</summary>
    public int ShownSeconds { get; private set; } = -1;

    /// <summary>Times the label's text was written (once per change of <see cref="ShownSeconds"/>), for tests.</summary>
    public int TextWrites { get; private set; }

    /// <summary>The whole seconds shown for <paramref name="ticksLeft"/> ticks left: rounded up (1 tick is 0:01); -1 for a clock that does not run.</summary>
    public static int SecondsFor(int ticksLeft) =>
        ticksLeft < 0 ? -1 : (ticksLeft + SimConstants.TicksPerSecond - 1) / SimConstants.TicksPerSecond;

    /// <summary>Connects the notice to the match: the runner's sim, the HUD layer it draws on and the text (either null: nothing shows).</summary>
    public void Init(SimRunner runner, CanvasLayer? hud, UiText? ui)
    {
        _runner = runner;
        _ui = ui;
        runner.Ticked += OnTicked;
        if (hud == null || ui == null) return;
        _label = MakeLabel();
        hud.AddChild(_label);
        if (runner.Simulation != null) Sync(runner.Simulation.World);
    }

    public override void _ExitTree()
    {
        if (_runner != null) _runner.Ticked -= OnTicked;
    }

    private void OnTicked(Simulation sim) => Sync(sim.World);

    /// <summary>Shows the notice exactly while <paramref name="w"/>'s draw clock runs, with the time left; tests may call it.</summary>
    public void Sync(World w)
    {
        if (_label == null || _ui == null) return;
        int seconds = SecondsFor(w.DrawTicksLeft);
        bool show = seconds >= 0;
        if (show && seconds != ShownSeconds)
        {
            _label.Text = _ui.DrawCountdown(seconds);
            ShownSeconds = seconds;
            TextWrites++;
        }
        if (_label.Visible != show) _label.Visible = show;
    }

    private static Label MakeLabel()
    {
        // BUG-1792: wrapped (a word wider than the line is split), growing down from Top and both ways if ever wider, so a
        // long text keeps its centre instead of growing right from the left edge.
        var label = new Label
        {
            Name = "DrawNotice", Visible = false, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, GrowHorizontal = Control.GrowDirection.Both, GrowVertical = Control.GrowDirection.End,
        };
        label.AddThemeFontSizeOverride("font_size", 20);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.75f));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        label.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        label.OffsetLeft = -Width / 2;
        label.OffsetRight = Width / 2;
        label.OffsetTop = Top;
        label.OffsetBottom = Top + Height;
        return label;
    }
}
