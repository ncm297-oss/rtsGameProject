using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>
/// One dropped queued order the view tells its player about (M6-V27, Request 58's view half; <see cref="OrderNotices.Collect"/>):
/// a Build refused at its turn, with its <see cref="Placement"/> error, or an order refused on arrival because the unit's
/// queue was full (<see cref="QueueFull"/>).
/// </summary>
/// <param name="Unit">The unit whose order it was, as the sim listed it.</param>
/// <param name="Alive">True when that unit is alive with that generation when read: an entry's unit may have died later in the
/// same tick, and its slot may hold another unit since (BUG-1662). The view draws nothing for an entry that is not alive.</param>
/// <param name="Kind">The order's kind (a queue-full entry carries the refused command's kind).</param>
/// <param name="Placement">A refused Build's error; <see cref="PlacementError.None"/> for a queue-full entry.</param>
/// <param name="QueueFull">True for an order refused because the unit's queue already held 8.</param>
public readonly record struct OrderNotice(EntityHandle Unit, bool Alive, CommandKind Kind, PlacementError Placement, bool QueueFull);
