namespace Rts.Game;

/// <summary>What a command card button does (M3-V2). Labels come from <c>ui.json</c> <c>commands</c> (<see cref="UiText"/>) or, for <see cref="Place"/>, <see cref="Train"/> and <see cref="Research"/>, the building's, unit's or tech's <c>displayName</c>.</summary>
public enum CardCommand
{
    /// <summary>An empty grid cell (hidden button).</summary>
    None = 0,
    /// <summary>Arms Move targeting (M): the next left click moves the selection there, ignoring enemies.</summary>
    Move,
    /// <summary>Arms attack-move targeting (A).</summary>
    AttackMove,
    /// <summary>Stops the selection (S).</summary>
    Stop,
    /// <summary>Holds position (H).</summary>
    Hold,
    /// <summary>Opens the worker's basic (Age I) build menu (B).</summary>
    BuildBasic,
    /// <summary>Opens the worker's advanced (Age II) build menu (V).</summary>
    BuildAdvanced,
    /// <summary>Cancels the selected construction site.</summary>
    Cancel,
    /// <summary>A build menu entry: shows the placement ghost for its building type.</summary>
    Place,
    /// <summary>A production card entry (M3-V3): queues its unit at the selected building (<c>Command.Train</c>).</summary>
    Train,
    /// <summary>A production card entry (M3-V3): queues its tech at the selected building (<c>Command.Research</c>).</summary>
    Research,
    /// <summary>An ability of the active subgroup's type (M4-V6a): arms ability targeting; labelled with the ability's <c>displayName</c>.</summary>
    Ability,
}
