using System;
using Rts.Sim.Determinism;

namespace Rts.Sim.Abilities;

/// <summary>
/// Every unit's active statuses (M4-4a, docs/03 "Abilities, statuses, zones"): up to <see cref="PerUnit"/> per unit slot,
/// each <c>(statusId, magnitude, ticksRemaining, pulseTicks, sourcePlayer)</c>, kept in the order they were first applied. Owned by
/// <see cref="Entities.UnitStore"/>, which clears a slot's statuses when it is freed or allocated. Hashed.
/// </summary>
/// <remarks>
/// Stacking (docs/02 "Status effects"): reapplying a status the unit has keeps the longer of the two remaining durations
/// (a refresh never shortens it) and the stronger magnitude; the source is the stronger application's (on a tie, the
/// latest). Different statuses stack. A ninth different status is dropped.
/// </remarks>
public sealed class StatusStore
{
    /// <summary>Most statuses one unit carries at once.</summary>
    public const int PerUnit = 8;

    /// <summary>Number of statuses each unit slot carries; its entries are <c>slot * PerUnit</c> to <c>slot * PerUnit + Count - 1</c>.</summary>
    public readonly int[] Count;
    /// <summary>Status id (index into <c>GameData.Statuses</c>) of each entry.</summary>
    public readonly int[] StatusId;
    /// <summary>Each entry's magnitude: damage per second for damage over time, the speed fraction taken for a slow.</summary>
    public readonly float[] Magnitude;
    /// <summary>Ticks each entry has left; it ends on the tick this reaches 0.</summary>
    public readonly int[] TicksRemaining;
    /// <summary>
    /// Ticks until each entry's next damage-over-time pulse. Set to a second when the status is first applied and kept across
    /// a refresh, so a status reapplied faster than once a second still pulses once a second (BUG-0301).
    /// </summary>
    public readonly int[] PulseTicks;
    /// <summary>The player whose ability applied each entry; credited with a kill its damage lands.</summary>
    public readonly int[] SourcePlayer;

    /// <summary>Unit slots with at least one status, so phase 5 skips its scan when none; derived, not hashed.</summary>
    internal int UnitsWithStatuses;

    /// <summary>A store for <paramref name="capacity"/> unit slots, all empty.</summary>
    public StatusStore(int capacity)
    {
        Count = new int[capacity];
        StatusId = new int[capacity * PerUnit];
        Magnitude = new float[capacity * PerUnit];
        TicksRemaining = new int[capacity * PerUnit];
        PulseTicks = new int[capacity * PerUnit];
        SourcePlayer = new int[capacity * PerUnit];
    }

    /// <summary>The entry index of status <paramref name="status"/> on unit slot <paramref name="unit"/>, or -1.</summary>
    public int IndexOf(int unit, int status)
    {
        int head = unit * PerUnit;
        for (int k = 0; k < Count[unit]; k++)
            if (StatusId[head + k] == status) return head + k;
        return -1;
    }

    /// <summary>
    /// Applies <paramref name="status"/> to unit slot <paramref name="unit"/> by the stacking rule. Returns whether the unit's
    /// set or a magnitude changed (so derived stats are recomputed); false also when the unit already has
    /// <see cref="PerUnit"/> other statuses and this one is dropped.
    /// </summary>
    internal bool Apply(int unit, int status, float magnitude, int ticks, int source)
    {
        int at = IndexOf(unit, status);
        if (at >= 0)
        {
            if (ticks > TicksRemaining[at]) TicksRemaining[at] = ticks;
            if (magnitude < Magnitude[at]) return false;
            bool stronger = magnitude > Magnitude[at];
            Magnitude[at] = magnitude;
            SourcePlayer[at] = source;
            return stronger;
        }
        int n = Count[unit];
        if (n >= PerUnit) return false;
        if (n == 0) UnitsWithStatuses++;
        at = unit * PerUnit + n;
        StatusId[at] = status;
        Magnitude[at] = magnitude;
        TicksRemaining[at] = ticks;
        PulseTicks[at] = SimConstants.TicksPerSecond;
        SourcePlayer[at] = source;
        Count[unit] = n + 1;
        return true;
    }

    /// <summary>Removes entry <paramref name="at"/> of unit slot <paramref name="unit"/>, keeping the others in order.</summary>
    internal void RemoveAt(int unit, int at)
    {
        int last = unit * PerUnit + Count[unit] - 1;
        for (int k = at; k < last; k++)
        {
            StatusId[k] = StatusId[k + 1];
            Magnitude[k] = Magnitude[k + 1];
            TicksRemaining[k] = TicksRemaining[k + 1];
            PulseTicks[k] = PulseTicks[k + 1];
            SourcePlayer[k] = SourcePlayer[k + 1];
        }
        StatusId[last] = 0;
        Magnitude[last] = 0f;
        TicksRemaining[last] = 0;
        PulseTicks[last] = 0;
        SourcePlayer[last] = 0;
        if (--Count[unit] == 0) UnitsWithStatuses--;
    }

    /// <summary>Removes every status of unit slot <paramref name="unit"/> (its death, or a fresh allocation).</summary>
    internal void Clear(int unit)
    {
        if (Count[unit] > 0) UnitsWithStatuses--;
        Count[unit] = 0;
        int head = unit * PerUnit;
        Array.Clear(StatusId, head, PerUnit);
        Array.Clear(Magnitude, head, PerUnit);
        Array.Clear(TicksRemaining, head, PerUnit);
        Array.Clear(PulseTicks, head, PerUnit);
        Array.Clear(SourcePlayer, head, PerUnit);
    }

    /// <summary>Unit slot <paramref name="unit"/>'s count and every field of each of its entries.</summary>
    internal void AddToHash(ref StateHasher h, int unit)
    {
        h.Add(Count[unit]);
        int head = unit * PerUnit;
        for (int k = 0; k < Count[unit]; k++)
        {
            h.Add(StatusId[head + k]);
            h.Add(Magnitude[head + k]);
            h.Add(TicksRemaining[head + k]);
            h.Add(PulseTicks[head + k]);
            h.Add(SourcePlayer[head + k]);
        }
    }
}
