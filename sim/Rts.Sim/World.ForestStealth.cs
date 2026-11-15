namespace Rts.Sim;

/// <summary>
/// M8-1: each player's faction forest stealth (<c>faction.json</c> <c>forestStealth</c>), read by
/// <see cref="Abilities.ForestStealthSystem"/>. Derived from the seats and the data (both in every save and replay header) at
/// construction; never saved or hashed. The per-unit state (<see cref="Entities.UnitStore.Forest"/>)
/// exists only when the data has a faction with the bonus.
/// </summary>
public sealed partial class World
{
    /// <summary>Per player, its faction's <see cref="Data.FactionDef.ForestStealthStatus"/> (the status the forest holds), -1 for none.</summary>
    internal int[] ForestStatusOf { get; private set; } = System.Array.Empty<int>();

    /// <summary>Per player, its faction's <see cref="Data.FactionDef.ForestStealthDelayTicks"/>.</summary>
    internal int[] ForestDelayOf { get; private set; } = System.Array.Empty<int>();

    /// <summary>Per player, its faction's <see cref="Data.FactionDef.ForestStealthExceptSlots"/> (a bit per unit slot).</summary>
    internal int[] ForestExceptOf { get; private set; } = System.Array.Empty<int>();

    /// <summary>Whether any seat's faction has the forest stealth: without one, phase 5's forest pass returns at once and no attack is noted.</summary>
    internal bool AnyForestStealth { get; private set; }

    /// <summary>Whether the data has a faction with the forest stealth: then the unit store keeps, hashes and saves the forest's per-unit state.</summary>
    internal bool ForestStealthInData { get; private set; }

    /// <summary>Fills the per-player forest tables from the seats' factions and allocates the per-unit state when the data has the bonus.</summary>
    private void InitForestStealth(SimConfig config)
    {
        int players = config.PlayerCount;
        ForestStatusOf = new int[players];
        ForestDelayOf = new int[players];
        ForestExceptOf = new int[players];
        for (int p = 0; p < players; p++)
        {
            ForestStatusOf[p] = -1;
            int f = config.SeatOf(p).Faction;
            if ((uint)f >= (uint)config.Data.Factions.Length) continue;
            var def = config.Data.Factions[f];
            if (!def.HasForestStealth) continue;
            ForestStatusOf[p] = def.ForestStealthStatus;
            ForestDelayOf[p] = def.ForestStealthDelayTicks;
            ForestExceptOf[p] = def.ForestStealthExceptSlots;
            AnyForestStealth = true;
        }
        ForestStealthInData = config.Data.AnyForestStealth();
        if (ForestStealthInData) Units.EnableForestStealth();
    }
}
