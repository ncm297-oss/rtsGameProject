using Rts.Sim.Map;

namespace Rts.Sim.Movement;

/// <summary>Fixed movement geometry and steering tuning (docs/03 "Local movement"); per-unit numbers (speed, radius) come from data.</summary>
public static class MovementConstants
{
    /// <summary>A moving unit this close to its goal (meters) has arrived: half a cell.</summary>
    public const float ArrivalDistance = MapConstants.CellSize / 2f;

    /// <summary>Flow fields built per tick at most, oldest orders first; units whose field isn't cached past that wait a tick (tick-cost cap, docs/03).</summary>
    public const int MaxFieldBuildsPerTick = 2;

    /// <summary>Squared meters within which a chaser counts as at its goal (M4-1): about on it, not <see cref="ArrivalDistance"/>.</summary>
    public const float ChaseArrival2 = 0.05f * 0.05f;

    /// <summary>
    /// Consecutive stuck ticks after which a Moving unit gives up and goes Idle: 20 ticks = 1 s, "blocked
    /// for a short time". A tick in which a walker ahead (or touching) within <see cref="QueueRange"/>
    /// made progress last tick, or a unit ahead waits for its field, is a queued tick, not a stuck one:
    /// the count holds (M1-5, a crowd at a ramp or gap; M1-4d-3, crossing traffic and field waits).
    /// </summary>
    public const int GiveUpTicks = 20;

    /// <summary>
    /// Stuck ticks after which a walker may also shove a friendly unit standing alone at another
    /// goal (half of <see cref="GiveUpTicks"/>): long enough that walkers able to get round don't,
    /// short enough to get through before giving up. While it moves only by pushing, the count holds.
    /// </summary>
    public const int PushAfterStuckTicks = 10;

    /// <summary>A tick counts as progress only if it beats the unit's best estimated path left by this fraction of its speed: less is jostling in place, not walking.</summary>
    public const float StuckFraction = 0.25f;

    /// <summary>
    /// A unit may stop only if every neighbor's center is at least this fraction of the two radii's
    /// sum away: stopped units overlap by at most 40%, a tight but readable blob.
    /// </summary>
    public const float ArrivalSpacing = 0.6f;

    /// <summary>
    /// A shoved unit may be squeezed toward a standing neighbor until their centers are this
    /// fraction of the two radii's sum apart, no closer (half: the pack limit). Tighter than
    /// <see cref="ArrivalSpacing"/> so a blob can give way a little; looser lets walkers press
    /// friendly units on top of each other.
    /// </summary>
    public const float ShoveSpacing = 0.5f;

    /// <summary>Share of an overlap a unit removes per tick against a Moving neighbor: half, since the neighbor removes the other half.</summary>
    public const float SeparationShareMoving = 0.5f;

    /// <summary>Share of an overlap a unit removes per tick against an arrived groupmate: all of it, since the groupmate won't move. (Other standing units are walls, not pushes.)</summary>
    public const float SeparationShareStill = 1f;

    /// <summary>
    /// Extra gap (meters) beyond touching at which a unit starts sidestepping a standing or oncoming
    /// unit ahead: about three ticks of two units closing head-on, enough to slip past before contact.
    /// </summary>
    public const float AvoidRange = 1f;

    /// <summary>
    /// Radians a detour passes clear of a standing unit's edge (M1-4d-3): a tangent exactly grazing
    /// it would clip it on the next tick's slight turn; about 3 degrees.
    /// </summary>
    public const float DetourMargin = 0.05f;

    /// <summary>
    /// Largest turn (radians) a detour takes away from the aim: 90 degrees. Past that the walker would
    /// walk away from its aim to get round; it presses on and the wall rules decide instead.
    /// </summary>
    public const float MaxDetourTurn = 1.5707964f;

    /// <summary>
    /// Units a walker's shove moves at most, the shoved one included, along a line of touching
    /// friendly Idle units ahead of it (chain shove, M1-4d-3): enough for a pair parked in a 1-cell
    /// corridor and one more; a longer line stays a wall, so a crowd isn't bulldozed and the cost
    /// stays bounded.
    /// </summary>
    public const int MaxChainShove = 3;

    /// <summary>
    /// Ticks a unit cut off its blob by a shove must go unshoved before it walks back to its point
    /// (M1-4d-3): <see cref="GiveUpTicks"/>, since a walker still pushing it shoves it again or gives
    /// up within that time, so the walk-back never starts head-on into the push.
    /// </summary>
    public const int WalkBackDelayTicks = GiveUpTicks;

    /// <summary>
    /// Gap (meters) beyond touching within which a walker ahead that made progress last tick makes a
    /// no-progress tick a queued one (the count holds). Two of <see cref="AvoidRange"/> (M1-4d-3): a
    /// unit pinned in a corner while its group streams past a step away read the stream as out of
    /// reach at one and gave up (cross-map seed 1).
    /// </summary>
    public const float QueueRange = 2f * AvoidRange;

    /// <summary>
    /// Behind a unit waiting for its field, a no-progress tick counts toward giving up only one tick
    /// in this many (BUG-0048): queuing behind the wait is fair, but waits under more live goals than
    /// cache slots never end, so the count must still rise. 4: a 1 s give-up becomes 4 s.
    /// </summary>
    public const int QueueOnWaitStride = 4;

    /// <summary>
    /// Most hard units a plug can have (BUG-0045): a cluster of standing enemies (or holders) too close
    /// together to pass between that reaches blocked ground on two opposite sides plugs a passage, if it
    /// has at most this many members. 32 covers a line of the smallest units (0.8 m) two deep across a
    /// 6 m ramp (16 members) with room to spare; a bigger cluster is an army's blob, where sliding round
    /// enemies is how two-player crowds flow. Also bounds one search's cost.
    /// </summary>
    public const int MaxPlugCluster = 32;
}
