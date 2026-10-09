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
    /// <summary>
    /// Sight radius in meters (M4-3a): the entry's optional <c>sight</c>, else <c>rules.json</c>
    /// <see cref="RulesDef.BuildingSight"/>, resolved at load; 0 (sees nothing) in hand-built data.
    /// </summary>
    public float Sight { get; init; }
    /// <summary>
    /// The building's attack (M4-3b; the unit <c>attack</c> object, docs/02 "Buildings": the Watch Tower's), or null for a
    /// building that never shoots. A finished building with one scans for enemy units and fires an aimed projectile from its
    /// footprint's centre (<see cref="Combat.TowerSystem"/>); never at a building. The loader requires a range above 0 and an
    /// aimed projectile, and refuses <c>targets: buildings</c>.
    /// </summary>
    public AttackDef? Attack { get; init; }
    /// <summary>Detection radius in meters (M4-3b: <c>detector</c>, docs/02 "Stealth and detection"; above 0 and at most <see cref="DataLimits.MaxSight"/>); 0 for none. Stored only until stealth (M4-5).</summary>
    public float Detector { get; init; }
    /// <summary>Tech / building ids it requires, as written (M3-5: every id exists, checked at load). Empty when none. Kept for tools; the sim reads <see cref="RequiresTechs"/> / <see cref="RequiresBuildings"/>.</summary>
    public ImmutableArray<string> Requires { get; init; } = ImmutableArray<string>.Empty;
    /// <summary><see cref="Requires"/>' tech ids, resolved at load (M3-6), ascending: each must be researched to place the building.</summary>
    /// <remarks>Set once by the loader; never written afterwards. Empty in hand-built data.</remarks>
    public ImmutableArray<int> RequiresTechs { get; internal set; } = ImmutableArray<int>.Empty;
    /// <summary><see cref="Requires"/>' building type ids, resolved at load (M3-6), ascending: the player needs an own finished building of each type.</summary>
    /// <remarks>Set once by the loader; never written afterwards. Empty in hand-built data.</remarks>
    public ImmutableArray<int> RequiresBuildings { get; internal set; } = ImmutableArray<int>.Empty;
}
