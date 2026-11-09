using System.Collections.Immutable;

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
    /// M5-4g (BUG-1021, BUG-1025): the build order names a reserved step only while the base is not defended and the army's
    /// half-pop and gold + wood are each at least this fraction of the wave's (<c>attack.minArmyPop</c> / <c>minArmyCost</c>
    /// times the difficulty's <c>waveSizeMultiplier</c>): the waves are built first (<c>reserveArmyShare</c>, 0-1; 0 reserves
    /// whatever the army, the defense aside).
    /// </summary>
    public float ReserveArmyShare { get; init; }

    /// <summary>
    /// M6-S5 (BUG-1045): until the scout is done, the AI counts only nodes within this distance of an own finished drop-off
    /// as ones to gather from, and its scout's "found enough" only a forest and a mine within it of the hall, in meters
    /// (<c>gatherReach</c>, 1-<see cref="DataLimits.MaxAiGatherReach"/>).
    /// </summary>
    public float GatherReach { get; init; }

    /// <summary>
    /// M6-SH1 (BUG-1052 / 1053): a lost scout is replaced, or a scout sent again for a resource kind it knows no node of, only
    /// while the player's own unit losses are at most this (<c>scoutReplacements</c>, a whole number 0-<see cref="DataLimits.MaxAiScoutReplacements"/>):
    /// a seat whose scouts die at its door stops feeding workers to the role.
    /// </summary>
    public int ScoutReplacements { get; init; }

    /// <summary>
    /// M6-SH1 (BUG-1051, was <c>ProductionManager.SpendDownQueue</c>): while the AI spends down (BUG-1050) a producer takes a
    /// Train while its queue holds fewer than this (<c>spendDownQueue</c>, a whole number 1-<see cref="DataLimits.MaxAiSpendDownQueue"/>).
    /// The default 2 is the unit in work and one waiting, so a producer that finishes between thinks starts the next at once:
    /// with only empty queues (as at <c>standard</c>) a <c>high</c> Malazan side still floated 380-1,300 at 10:00 on three seeds.
    /// </summary>
    public int SpendDownQueue { get; init; }

    /// <summary>
    /// M5-4d: once no enemy building is known, the army hunts the last enemy unit seen while that sighting is at most this
    /// old; with none that fresh it sweeps the map grid instead (<c>huntMemory</c> s, 0-3,600, to ticks).
    /// </summary>
    public int HuntMemoryTicks { get; init; }

    /// <summary>
    /// M5-4c slice 2: the cap on a Hard seat's fighter's focus reach (<c>focusFire</c>; BUG-1121: the reach is its attack range +
    /// its radius + a cell, at most this): a fighter focuses an enemy unit seen within its reach (<c>focusReach</c> m, 1-64).
    /// </summary>
    public float FocusReach { get; init; }

    /// <summary>
    /// M5-9a: a sighting of enemy units of one slot fades linearly to nothing over this many ticks in the AI's counter memory
    /// (<c>counterMemory</c> s, above 0, to ticks; docs/03 "Implementation (M5-9a)").
    /// </summary>
    public int CounterMemoryTicks { get; init; }

    /// <summary>
    /// M5-9a round 2 (BUG-1153, BUG-1068 candidate (b)): a seat starved this long running (it can pay for no unit of its
    /// composition, knows no node of a short kind it may gather and has no worker on one, and has at least
    /// <see cref="StarvedMinArmy"/> free army units) waves with what it has, whatever the opening's threshold
    /// (<c>starvedWaveAfter</c> s, 0-600, to ticks; <b>0 turns the rule off</b>: the seat is never stamped starved).
    /// </summary>
    public int StarvedWaveAfterTicks { get; init; }

    /// <summary>M5-9a round 2: the fewest free army units a starved seat needs to count as starved (<c>starvedMinArmy</c>, a whole number 1-50).</summary>
    public int StarvedMinArmy { get; init; }

    /// <summary>
    /// M5-9a: <c>ai.counters</c> as bit masks, indexed by the enemy's <see cref="UnitSlot"/>: bit <c>s</c> is set when own slot
    /// <c>s</c> counters it (docs/02 "Faction template", the "Countered by" column). Default (no <c>ai</c>) or all 0: no shift.
    /// </summary>
    public ImmutableArray<byte> CounterMasks { get; init; }

    /// <summary>M5-9a: whether own slot <paramref name="own"/> counters enemy slot <paramref name="enemy"/> (<see cref="CounterMasks"/>).</summary>
    public bool Counters(UnitSlot own, UnitSlot enemy) =>
        !CounterMasks.IsDefault && (uint)enemy < (uint)CounterMasks.Length && (CounterMasks[(int)enemy] & (1 << (int)own)) != 0;
}
