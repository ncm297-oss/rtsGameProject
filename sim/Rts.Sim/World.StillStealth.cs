namespace Rts.Sim;

/// <summary>
/// M8-2: each unit type's stand-still stealth (<c>units.json</c> <c>stillStealth</c>), read by
/// <see cref="Abilities.StillStealthSystem"/>. Derived from the data (in every save and replay header) at construction; never
/// saved or hashed. The per-unit state (<see cref="Entities.UnitStore.Still"/>) exists only when the data has such a type.
/// </summary>
public sealed partial class World
{
    /// <summary>Per unit type, its <see cref="Data.UnitDef.StillStealthStatus"/> (the status the rule holds), -1 for none.</summary>
    internal int[] StillStatusOfType { get; private set; } = System.Array.Empty<int>();

    /// <summary>Per unit type, its <see cref="Data.UnitDef.StillStealthDelayTicks"/>.</summary>
    internal int[] StillDelayOfType { get; private set; } = System.Array.Empty<int>();

    /// <summary>Whether the data has a unit type with the stand-still stealth: then the pass runs and the unit store keeps, hashes and saves its state.</summary>
    internal bool StillStealthInData { get; private set; }

    /// <summary>Fills the per-type tables from the data and allocates the per-unit state when a type carries the field.</summary>
    private void InitStillStealth(SimConfig config)
    {
        var units = config.Data.Units;
        StillStatusOfType = new int[units.Length];
        StillDelayOfType = new int[units.Length];
        for (int t = 0; t < units.Length; t++)
        {
            StillStatusOfType[t] = units[t].StillStealthStatus;
            StillDelayOfType[t] = units[t].StillStealthDelayTicks;
            if (units[t].HasStillStealth) StillStealthInData = true;
        }
        if (StillStealthInData) Units.EnableStillStealth();
    }

    /// <summary>
    /// M8-2: the stand-still part against the types and the entries (the unit store has read it and checked its ticks): only a
    /// live, non-summoned unit of a type with the field has state; a held unit carries an entry of its type's status with at
    /// least one held tick for each rule holding it (<see cref="Abilities.StatusSystem.Holders"/>). A forged part is refused,
    /// never trusted.
    /// </summary>
    private bool CheckStillState(Save.SaveReader r)
    {
        Abilities.StillStealthStore? still = Units.Still;
        if (still == null || r.Failed) return !r.Failed;
        for (int i = 0; i < Units.Capacity; i++)
        {
            if (!Units.Alive[i] || !still.HasState(i)) continue;
            int status = StillStatusOfType[Units.TypeId[i]];
            if (!r.Check(status >= 0 && !Units.IsSummoned(i))) return false;
            if (still.Held[i] == 0) continue;
            int at = Units.Statuses.IndexOf(i, status);
            if (!r.Check(at >= 0 && Units.Statuses.TicksRemaining[at] >= Abilities.StatusSystem.Holders(this, i, status))) return false;
        }
        return true;
    }
}
