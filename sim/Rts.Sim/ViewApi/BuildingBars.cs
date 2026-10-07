using System;
using System.Collections.Immutable;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>The bar a building view draws: a site's progress, or a damaged building's hit points (M3-V1). Read-only, allocation-free.</summary>
public static class BuildingBars
{
    /// <summary>The bar for building slot <paramref name="slot"/> and its fill in [0, 1]; <see cref="BuildingBarKind.None"/> (fill 0) for a dead slot or a building at full hit points.</summary>
    /// <param name="buildings">The match's building store (read only).</param>
    /// <param name="defs">Building types (<see cref="GameData.Buildings"/>), for maximum hit points.</param>
    /// <param name="slot">The building slot.</param>
    /// <param name="fill">Share of the bar filled: <c>Work / WorkNeeded</c> for a site, <c>Hp / max</c> for a damaged building.</param>
    public static BuildingBarKind Of(BuildingStore buildings, ImmutableArray<BuildingDef> defs, int slot, out float fill)
    {
        fill = 0f;
        if ((uint)slot >= (uint)buildings.Capacity || !buildings.Alive[slot]) return BuildingBarKind.None;
        int type = buildings.TypeId[slot];
        if (buildings.UnderConstruction[slot])
        {
            int needed = buildings.WorkNeeded(type);
            fill = needed > 0 ? Math.Clamp((float)buildings.Work[slot] / needed, 0f, 1f) : 0f;
            return BuildingBarKind.Progress;
        }
        BuildingDef def = defs[type];
        if (buildings.Hp[slot] >= def.Hp) return BuildingBarKind.None;
        fill = def.Hp > 0 ? Math.Clamp((float)buildings.Hp[slot] / def.Hp, 0f, 1f) : 0f;
        return BuildingBarKind.HitPoints;
    }
}
