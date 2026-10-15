namespace Rts.Game;

/// <summary>A settings screen text from <c>ui.json</c> <c>settings</c> (M6-V6); the key is the member's snake_case name (<see cref="UiText.Key{T}"/>).</summary>
public enum SettingsText
{
    /// <summary>The screen's heading.</summary>
    Title,
    /// <summary>The resolution's label.</summary>
    Resolution,
    /// <summary>One resolution list item: a format whose <c>{0}</c> is the width and <c>{1}</c> the height in pixels.</summary>
    ResolutionItem,
    /// <summary>The window mode's label.</summary>
    WindowMode,
    /// <summary>Window mode: a normal window.</summary>
    WindowWindowed,
    /// <summary>Window mode: a borderless window over the whole screen.</summary>
    WindowBorderless,
    /// <summary>Window mode: exclusive fullscreen.</summary>
    WindowFullscreen,
    /// <summary>The VSync label.</summary>
    Vsync,
    /// <summary>VSync on.</summary>
    VsyncOn,
    /// <summary>VSync off.</summary>
    VsyncOff,
    /// <summary>The graphics quality label.</summary>
    Quality,
    /// <summary>The Low quality preset.</summary>
    QualityLow,
    /// <summary>The Medium quality preset.</summary>
    QualityMedium,
    /// <summary>The High quality preset.</summary>
    QualityHigh,
    /// <summary>The master volume label.</summary>
    MasterVolume,
    /// <summary>The music volume label.</summary>
    MusicVolume,
    /// <summary>The sound effects volume label.</summary>
    SfxVolume,
    /// <summary>The edge pan speed label.</summary>
    EdgePanSpeed,
    /// <summary>The "confine cursor to window" check box's label (M6-V7).</summary>
    ConfineCursor,
    /// <summary>The button that applies and saves the picks and stays on the screen.</summary>
    Apply,
    /// <summary>The button that resets every pick to its default (applied and saved).</summary>
    Defaults,
    /// <summary>The button that applies and saves the picks and leaves the screen.</summary>
    Back,
}
