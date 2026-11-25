using System;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Orders;

namespace Rts.Sim.ViewApi;

/// <summary>
/// Which of the sim's dropped queued orders the view tells a player about (M6-V27, Request 58's view half; docs/03
/// "Implementation (M6-V27)"). Reads the dropped-order list of the last tick run (<see cref="World.DroppedOrders"/>) and the
/// unit store's alive and generation arrays, all handed in by the view, and writes nothing back: no sim state, no tick, no hash.
/// Allocation-free.
/// </summary>
public static class OrderNotices
{
    /// <summary>
    /// True for an entry the view shows: a Build refused at its turn with a placement error, or any order refused on arrival
    /// because the unit's queue was full. Every other kind (a Move to nowhere, a Gather with its node gone, ...) has no text yet.
    /// </summary>
    public static bool IsNotice(in DroppedOrder entry) =>
        entry.QueueFull || (entry.Kind == CommandKind.Build && entry.Placement != PlacementError.None);

    /// <summary>
    /// Writes into <paramref name="into"/> (as many as fit) the notices of <paramref name="player"/>'s units in
    /// <paramref name="dropped"/>, in list order: only that owner's entries and only those <see cref="IsNotice"/> accepts, each
    /// with <see cref="OrderNotice.Alive"/> read from <paramref name="alive"/> and <paramref name="generation"/> (false when the
    /// unit died later in the tick or its slot was taken by another unit). Returns how many were written.
    /// </summary>
    public static int Collect(ReadOnlySpan<DroppedOrder> dropped, int player, ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation, Span<OrderNotice> into)
    {
        int n = 0;
        for (int i = 0; i < dropped.Length && n < into.Length; i++)
        {
            DroppedOrder e = dropped[i];
            if (e.Owner != player || !IsNotice(in e)) continue;
            into[n++] = new OrderNotice(e.Unit, IsLive(e.Unit, alive, generation), e.Kind, e.QueueFull ? PlacementError.None : e.Placement, e.QueueFull);
        }
        return n;
    }

    /// <summary>True when <paramref name="unit"/>'s slot is alive and still holds that generation.</summary>
    public static bool IsLive(EntityHandle unit, ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation) =>
        (uint)unit.Index < (uint)alive.Length && (uint)unit.Index < (uint)generation.Length && alive[unit.Index] && generation[unit.Index] == unit.Generation;
}
