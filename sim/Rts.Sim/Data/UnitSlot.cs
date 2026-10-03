namespace Rts.Sim.Data;

/// <summary>The seven template unit slots every faction fills (docs/02 "Faction template").</summary>
/// <remarks>Order matches <see cref="DataLimits.SlotIds"/>, which holds the JSON spelling.</remarks>
public enum UnitSlot
{
    /// <summary>Gathers, builds, repairs.</summary>
    Worker = 0,
    /// <summary>Cheap melee core.</summary>
    Line = 1,
    /// <summary>Pierce damage from range.</summary>
    Ranged = 2,
    /// <summary>Fast flanker.</summary>
    Shock = 3,
    /// <summary>Warren magic and the signature ability.</summary>
    Caster = 4,
    /// <summary>Kills buildings.</summary>
    Siege = 5,
    /// <summary>The faction's identity unit.</summary>
    Unique = 6,
}
