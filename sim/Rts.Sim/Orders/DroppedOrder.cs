using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;

namespace Rts.Sim.Orders;

/// <summary>
/// M7-8 (Request 58): one shift-queued order the sim dropped in the last tick run, read through
/// <see cref="World.DroppedOrders"/>: either at its turn (phase 7 popped it and it could not start) or, with
/// <paramref name="QueueFull"/>, at its arrival (phase 1: the unit's queue already held <see cref="OrderConstants.QueueCapacity"/>
/// orders, the command's <see cref="CommandRefusal.UnitQueueFull"/>).
/// </summary>
/// <param name="Unit">The unit whose order it was, alive when the order was dropped; it may have died later in the same tick
/// (phase 11's deaths come after phase 7's drops), so the handle can be stale by the time it is read, as
/// <c>DeathEvent.Victim</c>'s is: check <c>Units.IsAlive</c> before showing anything over the unit (BUG-1662).</param>
/// <param name="Owner">The unit's owner.</param>
/// <param name="Kind">The order's kind.</param>
/// <param name="Placement">For a Build that <c>ConstructionSystem.StartBuild</c> refused at its turn, why (the type's
/// <see cref="PlacementError"/>); <see cref="PlacementError.None"/> for every other entry.</param>
/// <param name="QueueFull">True for an order refused on arrival because the unit's queue was full; false for one dropped at its turn.</param>
public readonly record struct DroppedOrder(EntityHandle Unit, int Owner, CommandKind Kind, PlacementError Placement, bool QueueFull);
