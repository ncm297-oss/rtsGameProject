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

    /// <summary>
    /// Meters a stuck caster's swept corridor reaches past the two bodies' radii on each side (M5-H1, BUG-0511): an own
    /// fighter holds the caster's stuck count only with its centre within the two radii plus this of the line of travel.
    /// A little slack for jostling; a unit standing beside the caster (its centre a body's width or more off the line) is
    /// not in the way.
    /// </summary>
    public const float CastCorridorSlack = 0.1f;

    /// <summary>
    /// Largest turn (radians) the flank rule takes away from the aim (pathing slice 2): 135 degrees. A walker pressed
    /// against its own planted line sees the line's end at about 90-110 degrees (its own touching neighbour blocks the
    /// whole half-plane), past <see cref="MaxDetourTurn"/>; much more than this would be walking back the way it came.
    /// The brief's bound is 150 degrees.
    /// </summary>
    public const float MaxFlankTurn = 2.3561945f;

    /// <summary>
    /// A flank step holds the stuck count, except one tick in this many, which counts (pathing slice 2): walking along the
    /// line is no progress toward the aim, yet it is how the walker gets there; the count still rises, so a flank that
    /// leads nowhere ends (4: a walker gives up after about 4 s of flanking, 12 m at 3 m/s, not 1 s). The least stride: a
    /// longer line gets more (<see cref="FlankMembersPerStride"/>, BUG-0881).
    /// </summary>
    public const int FlankCountStride = 4;

    /// <summary>
    /// Ticks a flank is remembered after its last flank step (BUG-0881), plus <see cref="FlankMemoryTicksPerMember"/> a
    /// member of the line: 20 (1 s). Within it the walker keeps the side it took (also across a re-issued order), measures
    /// the line against the way to its goal rather than the field's next cell, and its no-progress ticks count one in the
    /// flank's stride (the walk back toward the goal past the line's end). Long enough to bridge a tick of queuing or jostle.
    /// </summary>
    public const int FlankMemoryTicks = GiveUpTicks;

    /// <summary>
    /// Ticks added to <see cref="FlankMemoryTicks"/> per member of the blocks flanked (BUG-0881): 3. The walk from a long
    /// line's end back toward a goal just beyond its middle is about half the line's length, none of it progress on the
    /// field's estimate until the walker is back level with where it stood: a 32-member line 1 m apart gives 116 ticks
    /// (measured need about 80), a 10-member one 50.
    /// </summary>
    public const int FlankMemoryTicksPerMember = 3;

    /// <summary>
    /// Largest turn (radians) from the aim a walker takes along the side it is already flanking or walking (BUG-0881): 180
    /// degrees, any turn short of straight back. Walking toward the end of a long line with its goal just beyond it, the
    /// aim swings back behind the walker and the end's tangent lies 140-171 degrees off it (measured: 170.6 one cell from a
    /// 24-line's end); under <see cref="MaxFlankTurn"/> the kept side was rejected there and the walker swung to the far
    /// end. The kept direction is still the tangent past the block's edge, so it walks along the line, never away from it.
    /// A flank on no side yet (standing, nothing kept) still turns at most <see cref="MaxFlankTurn"/>.
    /// </summary>
    public const float MaxFlankKeptTurn = 3.1415927f;

    /// <summary>
    /// The flank's stride grows with the line (BUG-0881): one count in max(<see cref="FlankCountStride"/>, members of the
    /// largest block met / this), so the budget is about as long as the walk round: a 32-member line gets 16 (16 s), a
    /// 10-member one 5.
    /// </summary>
    public const int FlankMembersPerStride = 2;

    /// <summary>
    /// The least member count of the line flanked for the flank to be remembered (<see cref="FlankMemoryTicks"/>: the kept
    /// side, the goal as reference, the walk back counted by the stride; BUG-0881): 11. Round a line of up to 10 (pathing
    /// slice 2's acceptance scenes) the walk is short and the slice-2 rule alone takes it; QA's swings began at 16. Kept off
    /// for a melee's small blocks, where it held Moves converging on a fight Moving and gave more of them up (QA's
    /// 4-point combat rows, seeds 1 / 2: 409 / 413 gave up against 389 / 382 with the rule off; 356 / 362 with this).
    /// </summary>
    public const int FlankKeepMinMembers = 11;
}
