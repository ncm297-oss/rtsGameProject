namespace Rts.Sim.Entities;

/// <summary>What a unit is doing (docs/03 "Orders and unit states"); more states arrive with combat and economy.</summary>
public enum UnitState : byte
{
    /// <summary>Standing still with no order.</summary>
    Idle = 0,

    /// <summary>Walking toward <see cref="UnitStore.Goal"/> along a flow field.</summary>
    Moving = 1,
}
