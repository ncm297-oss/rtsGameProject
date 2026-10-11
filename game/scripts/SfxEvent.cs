namespace Rts.Game;

/// <summary>A sound the presentation can play; each value is a row of <see cref="Sfx"/>'s clip table, in this order.</summary>
public enum SfxEvent
{
    /// <summary>A player action left a changed, non-empty selection.</summary>
    Select,

    /// <summary>An order went out to at least one unit.</summary>
    Command,

    /// <summary>Something the player ordered didn't happen (M5-V1: an own cast was lost without a new order).</summary>
    Refused,
}
