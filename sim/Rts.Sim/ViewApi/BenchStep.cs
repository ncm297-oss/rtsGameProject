namespace Rts.Sim.ViewApi;

/// <summary>One action of the <c>--bench</c> script (M2-7); the view carries it out through its real selection, order and camera code.</summary>
public enum BenchStep
{
    /// <summary>Camera to the local army's centre at the start zoom (what a double-tapped group recall does).</summary>
    FocusArmy,

    /// <summary>Box-select the whole screen (the local army is on it after <see cref="FocusArmy"/>).</summary>
    BoxSelectArmy,

    /// <summary>Move the selection to the far side of the map.</summary>
    OrderAcross,

    /// <summary>Minimap click on map corner <c>arg</c> (0 north-west, 1 north-east, 2 south-east, 3 south-west).</summary>
    MinimapJump,

    /// <summary>Zoom to the closest limit (20 m).</summary>
    ZoomIn,

    /// <summary>Zoom to the farthest limit (60 m).</summary>
    ZoomOut,

    /// <summary>A, then a left click on the map.</summary>
    AttackMove,

    /// <summary>Shift + right click on queue point <c>arg</c> (0-2).</summary>
    QueuePoint,

    /// <summary>H.</summary>
    Hold,

    /// <summary>S.</summary>
    Stop,
}
