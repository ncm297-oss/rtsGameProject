using System;
using Rts.Sim.Orders;

namespace Rts.Sim;

// M7-8 (Request 58, docs/03 "Implementation (M7-8)"): the shift-queued orders dropped in the last tick, for the view (the
// Refused sound and the placement text over the unit). Output, not state: not hashed, not saved.
public sealed partial class World
{
    // Sized in the constructor: a unit starts at most one queued order a tick (phase 7) and each command is applied once
    // (phase 1), so units + commands entries never fill; past that an entry is not recorded.
    private readonly DroppedOrder[] _droppedOrders;

    /// <summary>
    /// M7-8 (Request 58): the shift-queued orders dropped in the last tick run, in the order they were dropped (phase 1's queue-full
    /// refusals in command order, then phase 7's in slot order). Emptied at the start of every tick like <see cref="Deaths"/>.
    /// Output for views, not state: not hashed, not saved.
    /// </summary>
    public ReadOnlySpan<DroppedOrder> DroppedOrders => _droppedOrders.AsSpan(0, DroppedOrderCount);

    /// <summary>Number of entries in <see cref="DroppedOrders"/>.</summary>
    internal int DroppedOrderCount { get; private set; }

    /// <summary>Appends a dropped order to this tick's <see cref="DroppedOrders"/>; past capacity it is not recorded.</summary>
    internal void RecordDroppedOrder(in DroppedOrder e)
    {
        if (DroppedOrderCount < _droppedOrders.Length) _droppedOrders[DroppedOrderCount++] = e;
    }

    /// <summary>Empties <see cref="DroppedOrders"/> (start of a tick).</summary>
    internal void ClearDroppedOrders() => DroppedOrderCount = 0;
}
