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

    /// <summary>M5-3 (BUG-0593): after the ring and the mirror the scout covers the map by a grid of cells at most this wide, in meters (<c>scoutGrid</c>, 16-256).</summary>
    public float ScoutGrid { get; init; }

    /// <summary>M5-3: the rally point lies this far from the Town Hall's centre toward the enemy, in meters (<c>rallyDistance</c>, 1-64).</summary>
    public float RallyDistance { get; init; }

    /// <summary>M5-3: an army unit this close to the rally point (or a wave member to its target) counts as there, in meters (<c>rallyReach</c>, 1-16).</summary>
    public float RallyReach { get; init; }

    /// <summary>M5-3: ticks from a wave's start, or its retreat, before the next wave may start (<c>waveCooldown</c> s, 0-3,600).</summary>
    public int WaveCooldownTicks { get; init; }

    /// <summary>M5-4b: the AI defends when an own building within this distance of its hall's centre loses hp or an enemy non-worker unit is seen within it, in meters (<c>defendRadius</c>, 8-256).</summary>
    public float DefendRadius { get; init; }

    /// <summary>M5-4b: ticks the defense holds after its last trigger (<c>defendFor</c> s, 0-600).</summary>
    public int DefendForTicks { get; init; }

    /// <summary>M5-4b: own workers this close to the defense point flee to a drop-off, in meters (<c>workerFleeRadius</c>, 1-64).</summary>
    public float WorkerFleeRadius { get; init; }

    /// <summary>M5-4c: the AI casts an ability only where at least this many enemy units seen stand within its radius (<c>castMinTargets</c>, a whole number 1-50).</summary>
    public int CastMinTargets { get; init; }

    /// <summary>M5-4f: the defense answers a threat with defenders worth this many times its gold + wood cost (<c>defendRatio</c>, 0.5-10).</summary>
    public float DefendRatio { get; init; }

    /// <summary>M5-4f: the least worth (gold + wood) a defense answers with, and what a hit with no enemy seen counts as (<c>defendMinValue</c>, 0-5,000).</summary>
    public float DefendMinValue { get; init; }

    /// <summary>M5-4f: a hit on the hall that leaves it below this fraction of its max hp calls the whole wave home (<c>recallHallHp</c>, 0-1).</summary>
    public float RecallHallHp { get; init; }

    /// <summary>
    /// M5-4g: while the build order's reserved step is short of gold only (wood only), the AI's wanted gold share is
    /// <see cref="GoldShare"/> plus (minus) this, clamped to 0-1 (<c>reserveShift</c>, 0-1; 0 leaves the split alone).
    /// </summary>
    public float ReserveShift { get; init; }

    /// <summary>
    /// M5-4g (BUG-1021): until the opening's first wave has been sent, the build order names a reserved step only while the
    /// army's half-pop is at least this fraction of the wave's (<c>attack.minArmyPop</c> times the difficulty's
    /// <c>waveSizeMultiplier</c>): the timing attack is built first (<c>reserveArmyShare</c>, 0-1; 0 reserves from the start).
    /// </summary>
    public float ReserveArmyShare { get; init; }
}
