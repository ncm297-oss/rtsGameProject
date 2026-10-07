using System;
using System.Collections.Immutable;
using Rts.Sim.Data;

namespace Rts.Sim.ViewApi;

/// <summary>A worker build menu's entries (M3-V2): the faction's building type for each listed slot, in list order. Pure, allocation-free.</summary>
/// <remarks>The slot lists themselves (basic = Age I, advanced = Age II) are view data (<c>game/data/common/ui.json</c> <c>buildMenus</c>).</remarks>
public static class BuildMenu
{
    /// <summary>Writes into <paramref name="types"/> the building type id <paramref name="faction"/> has for each slot of <paramref name="slots"/>, in order, skipping slots it lacks; returns how many were written (at most <c>types.Length</c>).</summary>
    public static int Entries(ImmutableArray<BuildingDef> defs, int faction, ReadOnlySpan<BuildingSlot> slots, Span<int> types)
    {
        int n = 0;
        foreach (BuildingSlot slot in slots)
        {
            if (n >= types.Length) break;
            for (int t = 0; t < defs.Length; t++)
            {
                if (defs[t].Faction != faction || defs[t].Slot != slot) continue;
                types[n++] = t;
                break;
            }
        }
        return n;
    }

    /// <summary>The slot whose JSON id (<see cref="DataLimits.BuildingSlotIds"/>, e.g. <c>infantry_hall</c>) is <paramref name="id"/>; false for an unknown id.</summary>
    public static bool TryParseSlot(string id, out BuildingSlot slot)
    {
        int i = DataLimits.BuildingSlotIds.IndexOf(id);
        slot = (BuildingSlot)Math.Max(i, 0);
        return i >= 0;
    }
}
