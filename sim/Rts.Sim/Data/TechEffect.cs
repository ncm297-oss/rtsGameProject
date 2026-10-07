using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>
/// One effect of a tech (M3-5): <see cref="Amount"/> added to <see cref="Stat"/> of every unit type its filters match,
/// resolved to ints at load. A filter that is not set matches everything; set filters must all match.
/// </summary>
public readonly struct TechEffect
{
    /// <summary>The stat changed.</summary>
    public TechStat Stat { get; init; }

    /// <summary>The change in sim units: whole points for attack / armor / hp, meters for range, ticks for ability cooldown.</summary>
    public float Amount { get; init; }

    /// <summary>Damage type id (<see cref="DamageTable"/>) the unit's attack must have; -1 for any.</summary>
    public int AttackType { get; init; }

    /// <summary>Tag ids (indexes into <see cref="GameData.UnitTags"/>), ascending: the unit must carry at least one; empty for any.</summary>
    public ImmutableArray<int> Tags { get; init; }

    /// <summary>Unit type ids, ascending: the unit must be one of them; empty for any.</summary>
    public ImmutableArray<int> Units { get; init; }

    /// <summary>1: siege-slot units only; 0: every unit but the siege slot; -1: either.</summary>
    public int Siege { get; init; }
}
