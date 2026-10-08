namespace Rts.Sim.Combat;

/// <summary>Why a unit fights (M4-1): what it does when its target is gone, and what <see cref="Entities.UnitStore.AnchorPosition"/> means.</summary>
public enum CombatMode : byte
{
    /// <summary>No engagement: an Idle unit scans; a plain Move or a worker's loop never does.</summary>
    None = 0,

    /// <summary>On an <c>AttackMove</c> leg: the anchor is the leg's destination, walked again when nothing is left in sight.</summary>
    AttackMove = 1,

    /// <summary>
    /// An Idle unit that took a target: the anchor is where it stood. It chases no further than its sight radius from the
    /// anchor (then <see cref="Returning"/>). When its target dies or leaves its sight it walks back to the anchor,
    /// still scanning, and goes back to <see cref="None"/> once it stands.
    /// </summary>
    Retaliate = 2,

    /// <summary>A retaliating unit pulled back by the leash: walks to its anchor without scanning (so it can't be kited back and forth), then <see cref="None"/>.</summary>
    Returning = 3,
}
