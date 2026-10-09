namespace Rts.Game;

/// <summary>A HUD label from <c>ui.json</c> <c>hud</c> (M3-V3); the key is the member's snake_case name (<see cref="UiText.Key{T}"/>).</summary>
public enum HudText
{
    /// <summary>The resource bar's population label ("Pop").</summary>
    Pop,
    /// <summary>The selection panel's hit points label.</summary>
    Hp,
    /// <summary>The selection panel's attack label.</summary>
    Attack,
    /// <summary>The selection panel's armor label.</summary>
    Armor,
    /// <summary>The selection panel's attack range label.</summary>
    Range,
    /// <summary>The selection panel's speed label.</summary>
    Speed,
    /// <summary>The tooltip's lead-in to a button's requirements ("Needs").</summary>
    Needs,
    /// <summary>The resource bar's kill count label ("K"; M4-V1).</summary>
    Kills,
    /// <summary>The resource bar's loss count label ("L"; M4-V1).</summary>
    Losses,
    /// <summary>The ability tooltip's effect radius label ("Radius"; M4-V6a).</summary>
    Radius,
    /// <summary>The ability tooltip's cooldown label ("Cooldown"; M4-V6a).</summary>
    Cooldown,
    /// <summary>The unit after a number of seconds ("s"; M4-V6a: the ability tooltip and the cooldown left on its button).</summary>
    Seconds,
    /// <summary>The unit after a distance ("m"; M4-V6a: the ability tooltip's range and radius).</summary>
    Meters,
}
