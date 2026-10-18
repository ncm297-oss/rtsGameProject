namespace Rts.Game;

/// <summary>The attack alert texts (<c>ui.json</c> <c>alert</c>, M6-V12); each member's snake_case name is its key.</summary>
public enum AlertText
{
    /// <summary>One of the player's buildings was hit off screen.</summary>
    BaseUnderAttack,

    /// <summary>One of the player's units was hit off screen.</summary>
    UnitsUnderAttack,
}
