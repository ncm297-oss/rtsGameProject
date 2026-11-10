namespace Rts.Sim;

/// <summary>
/// M7-1: each player's faction regeneration (<c>faction.json</c> <c>regen</c>), read by <see cref="Combat.RegenSystem"/>.
/// Derived from the seats and the data (both in every save and replay header) at construction; never saved or hashed.
/// </summary>
public sealed partial class World
{
    /// <summary>Per player, its faction's <see cref="Data.FactionDef.RegenPerTick"/> (fixed-point HP a tick), 0 for none.</summary>
    internal int[] RegenPerTickOf { get; private set; } = System.Array.Empty<int>();

    /// <summary>Per player, its faction's <see cref="Data.FactionDef.RegenDelayTicks"/>.</summary>
    internal int[] RegenDelayOf { get; private set; } = System.Array.Empty<int>();

    /// <summary>Whether any player's faction regenerates: with none and no regenerating status, phase 5's regeneration pass does nothing.</summary>
    internal bool AnyFactionRegen { get; private set; }

    /// <summary>Fills <see cref="RegenPerTickOf"/>, <see cref="RegenDelayOf"/> and <see cref="AnyFactionRegen"/> from the seats' factions.</summary>
    private void InitRegen(SimConfig config)
    {
        RegenPerTickOf = new int[config.PlayerCount];
        RegenDelayOf = new int[config.PlayerCount];
        for (int p = 0; p < config.PlayerCount; p++)
        {
            int f = config.SeatOf(p).Faction;
            if ((uint)f >= (uint)config.Data.Factions.Length) continue;
            RegenPerTickOf[p] = config.Data.Factions[f].RegenPerTick;
            RegenDelayOf[p] = config.Data.Factions[f].RegenDelayTicks;
            AnyFactionRegen |= RegenPerTickOf[p] > 0;
        }
    }
}
