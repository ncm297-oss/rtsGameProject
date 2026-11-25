namespace Rts.Game;

/// <summary>The texts about a unit's own orders (<c>ui.json</c> <c>orders</c>, M6-V27); each member's snake_case name is its key.</summary>
public enum OrdersText
{
    /// <summary>The line over a unit whose queued order was refused because its queue already held 8 (<c>orders.queue_full</c>).</summary>
    QueueFull,
}
