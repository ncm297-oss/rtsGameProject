namespace Rts.Sim.Ai;

/// <summary>
/// An AI command sent but not yet judged (M5-2): the AI can't hear whether a command was accepted, so at the first think
/// after it applied (<see cref="AppliesAt"/>) it reads its own state through the view: a site of the type at the anchor, an
/// item more in the building's queue, the tech researched or queued. Bookkeeping only (<see cref="Blackboard"/>).
/// </summary>
internal struct PendingOrder
{
    /// <summary>What was sent; <see cref="PendingKind.None"/> for no pending command.</summary>
    public PendingKind Kind;

    /// <summary>The tick the command applies in (its stamp): judged at a think with a later tick number.</summary>
    public int AppliesAt;

    /// <summary>The building, unit or tech type sent.</summary>
    public int TypeId;

    /// <summary>A Build's anchor cell.</summary>
    public int Cell;

    /// <summary>A Train's building slot.</summary>
    public int Slot;

    /// <summary>A Train's building generation.</summary>
    public int Generation;

    /// <summary>A Train: items of its type in the building's queue when sent.</summary>
    public int QueuedOfType;

    /// <summary>A Train: the tick from which the queue's head (of the same type when sent) may have finished, else int.MaxValue.</summary>
    public int HeadMayPopAt;
}
