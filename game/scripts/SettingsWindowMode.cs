namespace Rts.Game;

/// <summary>The settings' window mode (M6-V6); stored in <c>settings.cfg</c> as the member's snake_case name.</summary>
public enum SettingsWindowMode
{
    /// <summary>A normal window of the picked resolution.</summary>
    Windowed,
    /// <summary>A borderless window covering the screen (Godot's <c>WindowMode.Fullscreen</c>).</summary>
    Borderless,
    /// <summary>Exclusive fullscreen (Godot's <c>WindowMode.ExclusiveFullscreen</c>).</summary>
    Fullscreen,
}
