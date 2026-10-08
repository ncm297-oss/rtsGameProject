namespace Rts.Sim.Entities;

/// <summary>What a unit is doing (docs/03 "Orders and unit states"); more states arrive with later systems.</summary>
public enum UnitState : byte
{
    /// <summary>Standing still with no order.</summary>
    Idle = 0,

    /// <summary>Walking toward <see cref="UnitStore.Goal"/> along a flow field.</summary>
    Moving = 1,

    /// <summary>A worker standing at its node on the gather loop (M3-2): working while in reach, else waiting to walk in again.</summary>
    Gathering = 2,

    /// <summary>A worker standing with a full load on the gather loop (M3-2), out of reach of a drop-off and waiting to walk again.</summary>
    Returning = 3,

    /// <summary>A worker standing at its <see cref="UnitStore.BuildTarget"/> (M3-3): building or repairing while in reach, else waiting to walk in again.</summary>
    Building = 4,

    /// <summary>
    /// Standing at its <see cref="UnitStore.Target"/> in reach (M4-1): winding up a swing or waiting out the cooldown. Never
    /// moves (no shove, no walk-back) and is a hard wall to everyone, like a unit holding position.
    /// </summary>
    /// <remarks>Chasing is not a state of its own: a chasing unit is <see cref="Moving"/> with a live target, so movement walks it unchanged.</remarks>
    Attacking = 5,
}
