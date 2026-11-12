namespace Rts.Sim.Data;

/// <summary>The unit stat a tech effect changes (docs/03 "Data format", <c>techs.json</c>, M3-5).</summary>
/// <remarks>Order matches <see cref="DataLimits.TechStatIds"/>, which holds the JSON spelling.</remarks>
public enum TechStat
{
    /// <summary>Attack value, whole points.</summary>
    Attack = 0,
    /// <summary>Flat armor, whole points.</summary>
    Armor = 1,
    /// <summary>Attack range, meters.</summary>
    Range = 2,
    /// <summary>Maximum hit points, whole points.</summary>
    Hp = 3,
    /// <summary>Ability cooldown, ticks (seconds in the data; negative shortens it).</summary>
    AbilityCooldown = 4,
    /// <summary>
    /// M7-4: the duration of the statuses the effect names (<see cref="TechEffect.Statuses"/>), ticks (seconds in the data;
    /// negative shortens it). Not a unit stat: its effects name no units and it has no per-unit bonus
    /// (<c>World.TechBonus</c> is 0 for it); <c>TechState.StatusDurationTicks</c> reads it.
    /// </summary>
    StatusDuration = 5,
}
