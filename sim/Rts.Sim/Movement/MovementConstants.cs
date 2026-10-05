using Rts.Sim.Map;

namespace Rts.Sim.Movement;

/// <summary>Fixed movement geometry and steering tuning (docs/03 "Local movement"); per-unit numbers (speed, radius) come from data.</summary>
public static class MovementConstants
{
    /// <summary>A moving unit this close to its goal (meters) has arrived: half a cell.</summary>
    public const float ArrivalDistance = MapConstants.CellSize / 2f;

    /// <summary>Flow fields built per tick at most, oldest orders first; units whose field isn't cached past that wait a tick (tick-cost cap, docs/03).</summary>
    public const int MaxFieldBuildsPerTick = 2;

    /// <summary>
    /// Consecutive stuck ticks after which a Moving unit gives up and goes Idle: 20 ticks = 1 s, "blocked
    /// for a short time". A tick in which a groupmate ahead within <see cref="AvoidRange"/> made progress
    /// last tick is a queued tick, not a stuck one: the count holds (M1-5, a crowd at a ramp or gap).
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
}
