using System;
using Rts.Sim.Economy;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>The production queue strip's numbers (M3-V3): how many items a building's queue shows, the head's progress, and where item k sits. Read-only, allocation-free.</summary>
/// <remarks>
/// Item k of the strip is queue entry k (0 = the head, in progress; <see cref="BuildingStore.QueueTypeAt"/>,
/// <see cref="BuildingStore.QueueIsTechAt"/>); a click on it is <c>Command.CancelTrain(player, building, k)</c>. Items
/// stand in a row from the left, <c>size</c> wide with <c>gap</c> between them.
/// </remarks>
public static class QueueStrip
{
    /// <summary>Most items a strip shows: the queue's capacity (docs/02 "Production": 5).</summary>
    public const int MaxItems = EconomyConstants.ProductionQueueCapacity;

    /// <summary>Items in slot <paramref name="slot"/>'s queue (0 for a dead or out-of-range slot), at most <see cref="MaxItems"/>.</summary>
    public static int Count(BuildingStore buildings, int slot)
    {
        if ((uint)slot >= (uint)buildings.Capacity || !buildings.Alive[slot]) return 0;
        return Math.Clamp(buildings.QueueCount[slot], 0, MaxItems);
    }

    /// <summary>Share of the head item done: <c>Progress / ItemTicks(slot, 0)</c> clamped to [0, 1]; 0 for an empty queue or a head not started.</summary>
    public static float HeadFill(BuildingStore buildings, int slot)
    {
        if (Count(buildings, slot) == 0) return 0f;
        int ticks = buildings.ItemTicks(slot, 0);
        if (ticks <= 0) return 0f;
        return Math.Clamp(buildings.Progress[slot] / (float)ticks, 0f, 1f);
    }

    /// <summary>Left edge of item <paramref name="item"/> in pixels from the strip's left.</summary>
    public static float ItemX(int item, float size, float gap) => item * (size + gap);

    /// <summary>Total width of a strip of <paramref name="count"/> items in pixels (0 for none).</summary>
    public static float Width(int count, float size, float gap) => count <= 0 ? 0f : count * size + (count - 1) * gap;

    /// <summary>The item under pixel <paramref name="x"/> from the strip's left, or -1 (a gap, outside, or past <paramref name="count"/>).</summary>
    public static int ItemAt(float x, int count, float size, float gap)
    {
        if (!float.IsFinite(x) || x < 0f || size <= 0f) return -1;
        float pitch = size + Math.Max(gap, 0f);
        int k = (int)(x / pitch);
        if (k >= Math.Min(count, MaxItems)) return -1;
        return x - k * pitch < size ? k : -1;
    }
}
