namespace Rts.Game;

/// <summary>A sound the presentation can play; each value is a row of <see cref="Sfx"/>'s clip table, in this order.</summary>
/// <remarks>
/// M6-V12: the clip files are <c>res://assets/audio/sfx/&lt;category&gt;/&lt;event&gt;_&lt;n&gt;.wav</c> (the event's name in
/// snake_case, n = 1-3). The world events (<see cref="MeleeHit"/>, <see cref="Shot"/>, <see cref="UnitDeath"/>,
/// <see cref="BuildingRazed"/>, <see cref="Cast"/>) play only for what the local player sees on screen and share a cap; the
/// others are the player's own feedback and always play.
/// </remarks>
public enum SfxEvent
{
    /// <summary>A player action left a changed, non-empty selection.</summary>
    Select,

    /// <summary>An order went out to at least one unit.</summary>
    Command,

    /// <summary>Something the player ordered didn't happen (M5-V1: an own cast was lost without a new order).</summary>
    Refused,

    /// <summary>A melee blow landed on a unit on screen (a hit with no projectile arriving at it that tick).</summary>
    MeleeHit,

    /// <summary>A projectile (an arrow, a bolt, a stone) was loosed on screen.</summary>
    Shot,

    /// <summary>A unit died on screen.</summary>
    UnitDeath,

    /// <summary>One of the local player's construction sites appeared.</summary>
    BuildingPlaced,

    /// <summary>One of the local player's buildings finished.</summary>
    BuildingDone,

    /// <summary>A building was razed on screen.</summary>
    BuildingRazed,

    /// <summary>The local player's production finished a unit or a tech.</summary>
    Ready,

    /// <summary>An ability resolved on screen.</summary>
    Cast,

    /// <summary>An attack alert (the local player's units or base were hit; see <see cref="AlertHud"/>).</summary>
    Alert,
}
