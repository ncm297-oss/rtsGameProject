namespace Rts.Game;

/// <summary>The settings' graphics quality preset (M6-V6, docs/03 "Implementation (M6-V6)"); stored in <c>settings.cfg</c> as the member's snake_case name.</summary>
public enum SettingsQuality
{
    /// <summary>Cheapest: smaller, hard-edged sun shadows, no MSAA, 3D drawn at 75 % and scaled up.</summary>
    Low,
    /// <summary>The game's look before settings existed: Godot's default shadows, no MSAA, full resolution.</summary>
    Medium,
    /// <summary>Softer shadows and 4x MSAA at full resolution.</summary>
    High,
}
