using System;
using System.Collections.Immutable;
using Rts.Sim.Data;

namespace Rts.Sim.ViewApi;

/// <summary>A finished building's production card entries (M3-V3): the units it trains, then the techs it researches. Read-only, allocation-free.</summary>
/// <remarks>
/// Units in <see cref="GameData.UnitsTrainedAt"/> order; then techs from <see cref="GameData.TechsResearchableAt"/>, the
/// common ones first and the faction's own after them, each in id order (a Forge shows the six Forge upgrades, then the
/// faction upgrade). The card's grid cells take them in this order.
/// </remarks>
public static class ProductionMenu
{
    /// <summary>Writes building type <paramref name="buildingType"/>'s entries into <paramref name="into"/>; returns how many (at most <c>into.Length</c>; 0 for an unknown type).</summary>
    public static int Entries(GameData data, int buildingType, Span<ProductionEntry> into)
    {
        if ((uint)buildingType >= (uint)data.Buildings.Length) return 0;
        int n = 0;
        ImmutableArray<int> units = data.UnitsTrainedAt(buildingType);
        for (int i = 0; i < units.Length && n < into.Length; i++) into[n++] = new ProductionEntry(units[i], false);
        ImmutableArray<int> techs = data.TechsResearchableAt(buildingType);
        // Two passes: common techs (faction -1), then the faction's own.
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < techs.Length && n < into.Length; i++)
            {
                bool common = data.Techs[techs[i]].Faction < 0;
                if (common == (pass == 0)) into[n++] = new ProductionEntry(techs[i], true);
            }
        return n;
    }

    /// <summary>The display name of a <c>requires</c> entry: the tech's or the building's <c>displayName</c>; the id itself if neither has it (the loader rejects such data).</summary>
    /// <remarks>Looks the id up by name; call it when building tooltips, not per frame.</remarks>
    public static string RequirementName(GameData data, string id)
    {
        int tech = data.FindTech(id);
        if (tech >= 0) return data.Techs[tech].DisplayName;
        int building = data.FindBuilding(id);
        return building >= 0 ? data.Buildings[building].DisplayName : id;
    }
}
