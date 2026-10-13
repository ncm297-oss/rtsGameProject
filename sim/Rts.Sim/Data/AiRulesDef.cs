namespace Rts.Sim.Data;

/// <summary>
/// The AI's generic tunables from <c>common/rules.json</c> <c>ai</c> (M5-2, docs/03 "Implementation (M5-2)"), shared by every
/// faction and difficulty, converted to sim units (cells, half-pop, ticks; M5-3's distances stay meters). In
/// <see cref="GameData.ContentHash"/> since M5-3, with <c>ai.json</c>: a replay is tied to the AI data it was recorded with.
/// </summary>
public sealed class AiRulesDef
{
    internal AiRulesDef()
    {
    }

    /// <summary>The share of gathering workers an AI sends to gold, the rest to wood (<c>goldShare</c>, 0-1).</summary>
    public float GoldShare { get; init; }

    /// <summary>The AI starts a house when its population cap is at most this far above its population, in half-pop (<c>houseWhenPopWithin</c> pop).</summary>
    public int HouseHalfPopMargin { get; init; }

    /// <summary>How far from its centre (a Town Hall, a forest, a mine) the AI looks for a building site, in cells (<c>placementSearchRadius</c> m).</summary>
    public int PlacementSearchCells { get; init; }

    /// <summary>The free ring the AI keeps round its own buildings and gold mines when it places one, in cells (<c>buildingSpacing</c> m).</summary>
    public int SpacingCells { get; init; }

    /// <summary>An expansion's mine counts as taken while an own or seen Town Hall stands this close to it, in cells (<c>expandHallReach</c> m).</summary>
    public int ExpandReachCells { get; init; }

    /// <summary>M5-3: the AI sends its scout at the first think at or after this tick (<c>scoutAfter</c> s, 0-3,600).</summary>
    public int ScoutAfterTicks { get; init; }

    /// <summary>M5-3: the scout's ring of eight waypoints lies this far from the Town Hall's centre, in meters (<c>scoutRadius</c>, 1-64).</summary>
    public float ScoutRadius { get; init; }

    /// <summary>M5-3: a waypoint counts as visited once the scout is this close, in meters (<c>scoutReach</c>, 1-16).</summary>
    public float ScoutReach { get; init; }

    /// <summary>M5-3: the rally point lies this far from the Town Hall's centre toward the enemy, in meters (<c>rallyDistance</c>, 1-64).</summary>
    public float RallyDistance { get; init; }

    /// <summary>M5-3: an army unit this close to the rally point (or a wave member to its target) counts as there, in meters (<c>rallyReach</c>, 1-16).</summary>
    public float RallyReach { get; init; }

    /// <summary>M5-3: ticks from a wave's start, or its retreat, before the next wave may start (<c>waveCooldown</c> s, 0-3,600).</summary>
    public int WaveCooldownTicks { get; init; }
}
