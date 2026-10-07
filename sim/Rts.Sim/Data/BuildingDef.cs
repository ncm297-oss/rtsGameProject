using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>One building type from <c>factions/&lt;id&gt;/buildings.json</c> (docs/02 "Buildings"), converted to sim units (ticks, half-pop).</summary>
public sealed class BuildingDef
{
    internal BuildingDef()
    {
    }

    /// <summary>Dense building id (index into <see cref="GameData.Buildings"/>).</summary>
    public int Id { get; init; }
    /// <summary>String id, e.g. <c>malazan_garrison_keep</c>.</summary>
    public required string Key { get; init; }
    /// <summary>Owning faction id.</summary>
    public int Faction { get; init; }
    /// <summary>Template slot.</summary>
    public BuildingSlot Slot { get; init; }
    /// <summary>Player-facing name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Player-facing tooltip text.</summary>
    public required string Description { get; init; }
    /// <summary>Footprint width in nav cells (x), 1 to <see cref="DataLimits.MaxFootprint"/>.</summary>
    public int FootprintWidth { get; init; }
    /// <summary>Footprint height in nav cells (y), 1 to <see cref="DataLimits.MaxFootprint"/>.</summary>
    public int FootprintHeight { get; init; }
    /// <summary>Maximum hit points.</summary>
    public int Hp { get; init; }
    /// <summary>Flat armor.</summary>
    public int Armor { get; init; }
    /// <summary>Gold cost.</summary>
    public int CostGold { get; init; }
    /// <summary>Wood cost.</summary>
    public int CostWood { get; init; }
    /// <summary>Ticks to build with one worker (docs/02: n workers take <c>t x 3 / (n + 2)</c>).</summary>
    public int BuildTicks { get; init; }
    /// <summary>Population provided, in half-pop units (pop 10 = 20); unused until population, M3-4.</summary>
    public int HalfPopProvided { get; init; }
    /// <summary>Workers deposit cargo here (Town Hall, Camp).</summary>
    public bool DropOff { get; init; }
    /// <summary>Tech / building ids it requires (M3-5: every id exists, checked at load; gating in placement is M3-6). Empty when none.</summary>
    public ImmutableArray<string> Requires { get; init; } = ImmutableArray<string>.Empty;
}
