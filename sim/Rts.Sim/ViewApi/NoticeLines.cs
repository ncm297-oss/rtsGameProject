using System;
using Rts.Sim.Economy;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>
/// The short lines the view shows over its own units whose queued order was dropped (M6-V27, Request 58's view half; docs/03
/// "Implementation (M6-V27)"): one line a unit slot, lasting <see cref="LifetimeSeconds"/> of the view's clock (frame time
/// while the game is not paused, so it reads the same at every game speed and waits out a pause). A second notice for a unit
/// that still has a line restarts that line with the new text rather than stacking a second one. A fixed pool: when every
/// line is in use the oldest is replaced. Presentation only: the timer is not game state and nothing here reads or writes
/// the sim; allocation-free after construction.
/// </summary>
public sealed class NoticeLines
{
    /// <summary>Lines kept at most (builder's choice: far more than the own units a player sees refused at once).</summary>
    public const int DefaultCapacity = 64;

    /// <summary>A line shows for 2.5 s of the view's clock: long enough to read two words at a glance, gone before the next order.</summary>
    public const float LifetimeSeconds = 2.5f;

    /// <summary>The last 0.5 s of a line's life it fades out.</summary>
    public const float FadeSeconds = 0.5f;

    private readonly bool[] _active;
    private readonly EntityHandle[] _unit;
    private readonly PlacementError[] _placement;
    private readonly bool[] _queueFull;
    private readonly float[] _age;

    /// <summary>Creates a pool of <paramref name="capacity"/> lines.</summary>
    public NoticeLines(int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _active = new bool[capacity];
        _unit = new EntityHandle[capacity];
        _placement = new PlacementError[capacity];
        _queueFull = new bool[capacity];
        _age = new float[capacity];
    }

    /// <summary>Lines the pool holds at most.</summary>
    public int Capacity => _active.Length;

    /// <summary>Lines in use.</summary>
    public int Count { get; private set; }

    /// <summary>Lines ever started (a restart counts as one).</summary>
    public long Added { get; private set; }

    /// <summary>Notices that restarted a unit's live line instead of adding one.</summary>
    public long Restarted { get; private set; }

    /// <summary>Lines that replaced the oldest live line of another unit because the pool was full.</summary>
    public long Replaced { get; private set; }

    /// <summary>True while line slot i is in use.</summary>
    public ReadOnlySpan<bool> Active => _active;

    /// <summary>The unit each line is over (drawn only while that handle is alive).</summary>
    public ReadOnlySpan<EntityHandle> Unit => _unit;

    /// <summary>Each line's refused Build error (<see cref="PlacementError.None"/> for a queue-full line).</summary>
    public ReadOnlySpan<PlacementError> Placement => _placement;

    /// <summary>True for a line about a full queue.</summary>
    public ReadOnlySpan<bool> QueueFull => _queueFull;

    /// <summary>Each line's age in seconds of the view's clock.</summary>
    public ReadOnlySpan<float> Age => _age;

    /// <summary>
    /// Shows a line over <paramref name="unit"/>: restarts the live line of that unit slot (a new handle in the slot takes it
    /// over), else takes a free slot, else replaces the oldest line. Returns the slot.
    /// </summary>
    public int Show(EntityHandle unit, PlacementError placement, bool queueFull)
    {
        int slot = -1, free = -1, oldest = -1;
        for (int i = 0; i < _active.Length; i++)
        {
            if (!_active[i])
            {
                if (free < 0) free = i;
                continue;
            }
            if (_unit[i].Index == unit.Index)
            {
                slot = i;
                break;
            }
            if (oldest < 0 || _age[i] > _age[oldest]) oldest = i;
        }
        if (slot >= 0) Restarted++;
        else if (free >= 0)
        {
            slot = free;
            Count++;
        }
        else
        {
            slot = oldest;
            Replaced++;
        }
        _active[slot] = true;
        _unit[slot] = unit;
        _placement[slot] = placement;
        _queueFull[slot] = queueFull;
        _age[slot] = 0f;
        Added++;
        return slot;
    }

    /// <summary>Ages every line by <paramref name="seconds"/> (negative or NaN counts as 0) and removes those past <see cref="LifetimeSeconds"/>; returns how many were removed.</summary>
    public int Advance(float seconds)
    {
        float dt = seconds > 0f ? seconds : 0f;
        int removed = 0;
        for (int i = 0; i < _active.Length; i++)
        {
            if (!_active[i]) continue;
            _age[i] += dt;
            if (_age[i] < LifetimeSeconds) continue;
            _active[i] = false;
            Count--;
            removed++;
        }
        return removed;
    }

    /// <summary>Line <paramref name="slot"/>'s opacity: 1, then down to 0 over the last <see cref="FadeSeconds"/>.</summary>
    public float Opacity(int slot)
    {
        if ((uint)slot >= (uint)_active.Length || !_active[slot]) return 0f;
        float left = LifetimeSeconds - _age[slot];
        return left >= FadeSeconds ? 1f : Math.Max(0f, left / FadeSeconds);
    }

    /// <summary>Removes every line (a new match).</summary>
    public void Clear()
    {
        Array.Clear(_active);
        Count = 0;
    }
}
