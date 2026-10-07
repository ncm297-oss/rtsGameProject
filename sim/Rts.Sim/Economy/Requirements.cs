using System.Collections.Immutable;
using Rts.Sim.Data;

namespace Rts.Sim.Economy;

/// <summary>
/// M3-6: whether a player meets a def's resolved <c>requires</c> (and a tech's <c>requiresAnyOf</c>). One rule for the
/// three gates (<see cref="World.CanTrain"/>, <see cref="World.CanPlace"/>, <see cref="World.CanResearch"/>); read-only,
/// allocation-free, bounded by the def's own lists.
/// </summary>
/// <remarks>
/// A tech requirement is met once the player has researched it; a building requirement by one of the player's own
/// <b>finished</b> buildings of that type (a site doesn't count, nor an enemy's), read from the ledger's derived counts.
/// The gates check this when a command applies (queue time), never again at completion: an item already queued survives
/// the loss of what unlocked it (Age of Empires' rule).
/// </remarks>
internal static class Requirements
{
    /// <summary>True if <paramref name="player"/> has researched every tech in <paramref name="techs"/> and owns a finished building of every type in <paramref name="buildings"/>.</summary>
    public static bool Met(World world, int player, ImmutableArray<int> techs, ImmutableArray<int> buildings)
    {
        foreach (int t in techs)
            if (!world.HasTech(player, t)) return false;
        PlayerLedger ledger = world.Ledger;
        foreach (int b in buildings)
            if (ledger.FinishedOfType(player, b) == 0) return false;
        return true;
    }

    /// <summary>
    /// True if <paramref name="tech"/> has no any-of rule, or <paramref name="player"/> owns a finished building in at
    /// least <see cref="TechDef.RequiresAnyOfCount"/> of its distinct listed slots (two in one slot count once).
    /// </summary>
    public static bool AnyOfMet(World world, int player, TechDef tech)
    {
        int needed = tech.RequiresAnyOfCount;
        if (needed <= 0) return true;
        PlayerLedger ledger = world.Ledger;
        int have = 0;
        foreach (int slot in tech.RequiresAnyOfSlots)
            if (ledger.FinishedInSlot(player, slot) > 0 && ++have >= needed) return true;
        return false;
    }
}
