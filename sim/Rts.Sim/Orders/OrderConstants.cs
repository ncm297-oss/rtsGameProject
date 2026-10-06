namespace Rts.Sim.Orders;

/// <summary>Order-queue tunables (docs/03 "Orders and unit states").</summary>
public static class OrderConstants
{
    /// <summary>Shift-queued orders one unit can hold; a queued order arriving at a full queue is dropped.</summary>
    public const int QueueCapacity = 8;
}
