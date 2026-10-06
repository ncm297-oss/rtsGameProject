namespace Rts.Sim.Entities;

/// <summary>What a unit is doing (docs/03 "Orders and unit states"); more states arrive with combat and economy.</summary>
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
}
