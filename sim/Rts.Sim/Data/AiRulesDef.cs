namespace Rts.Sim.Data;

/// <summary>
/// The AI's generic tunables from <c>common/rules.json</c> <c>ai</c> (M5-2, docs/03 "Implementation (M5-2)"), shared by every
/// faction and difficulty, converted to sim units (cells, half-pop). Not in <see cref="GameData.ContentHash"/> yet, with
/// <c>ai.json</c> (M5-3): nothing a replay plays back reads it (playback runs no AI).
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
}
