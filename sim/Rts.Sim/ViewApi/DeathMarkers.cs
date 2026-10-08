using System;
using System.Numerics;
using Rts.Sim.Combat;

namespace Rts.Sim.ViewApi;

/// <summary>
/// The corpse and rubble markers the view leaves where things died (M4-V1, docs/02 "Death"): one per <see cref="DeathEvent"/>,
/// a unit's corpse for <see cref="UnitLifetimeTicks"/> (10 s of game time) and a building's rubble for
/// <see cref="BuildingLifetimeTicks"/> (20 s), timed in sim ticks so game speed scales them. A fixed pool used as a ring:
/// when every marker is in use the next death replaces the oldest one, so a death storm never grows it. A marker is
/// presentation only (a position, what it was and a timer); allocation-free after construction.
/// </summary>
public sealed class DeathMarkers
{
    /// <summary>Markers kept at most (Producer default, M4-V1).</summary>
    public const int DefaultCapacity = 2000;

    /// <summary>A unit's corpse lasts 10 s of game time (docs/02 "Death").</summary>
    public const int UnitLifetimeTicks = 10 * SimConstants.TicksPerSecond;

    /// <summary>A building's rubble lasts 20 s of game time (M4-V1 brief).</summary>
    public const int BuildingLifetimeTicks = 20 * SimConstants.TicksPerSecond;

    private readonly bool[] _active;
    private readonly bool[] _isBuilding;
    private readonly int[] _type, _owner;
    private readonly long[] _expires;
    private readonly Vector2[] _position;
    private int _head;
    private long _lastCollected = long.MinValue;

    /// <summary>Creates a pool of <paramref name="capacity"/> markers.</summary>
    public DeathMarkers(int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _active = new bool[capacity];
        _isBuilding = new bool[capacity];
        _type = new int[capacity];
        _owner = new int[capacity];
        _expires = new long[capacity];
        _position = new Vector2[capacity];
    }

    /// <summary>Markers the pool holds at most.</summary>
    public int Capacity => _active.Length;

    /// <summary>Markers in use.</summary>
    public int Count { get; private set; }

    /// <summary>Markers ever added.</summary>
    public long Added { get; private set; }

    /// <summary>Markers that replaced a live one because the pool was full.</summary>
    public long Replaced { get; private set; }

    /// <summary>Markers removed because their time ran out.</summary>
    public long Expired { get; private set; }

    /// <summary>The tick of the last <see cref="Collect"/> that took a tick's deaths (<see cref="long.MinValue"/> before any).</summary>
    public long LastCollectedTick => _lastCollected;

    /// <summary>True while marker slot i is in use.</summary>
    public ReadOnlySpan<bool> Active => _active;

    /// <summary>Where each marker lies (m): the death position.</summary>
    public ReadOnlySpan<Vector2> Position => _position;

    /// <summary>True for rubble (a building), false for a corpse (a unit).</summary>
    public ReadOnlySpan<bool> IsBuilding => _isBuilding;

    /// <summary>The dead unit's or building's type id.</summary>
    public ReadOnlySpan<int> Type => _type;

    /// <summary>The player who lost it.</summary>
    public ReadOnlySpan<int> Owner => _owner;

    /// <summary>The tick number from which each marker is gone.</summary>
    public ReadOnlySpan<long> ExpiresAt => _expires;

    /// <summary>Lifetime in ticks of a marker for a building (true) or a unit (false).</summary>
    public static int LifetimeTicks(bool isBuilding) => isBuilding ? BuildingLifetimeTicks : UnitLifetimeTicks;

    /// <summary>Adds one marker for <paramref name="death"/> seen at tick <paramref name="tick"/>; returns its slot (the oldest marker's when the pool is full).</summary>
    public int Add(in DeathEvent death, long tick)
    {
        int slot = _head;
        _head = _head + 1 == _active.Length ? 0 : _head + 1;
        if (_active[slot]) Replaced++;
        else Count++;
        _active[slot] = true;
        _isBuilding[slot] = death.IsBuilding;
        _type[slot] = death.VictimType;
        _owner[slot] = death.VictimOwner;
        _position[slot] = death.Position;
        _expires[slot] = tick + LifetimeTicks(death.IsBuilding);
        Added++;
        return slot;
    }

    /// <summary>
    /// Adds a marker for every death of tick number <paramref name="tick"/> (the sim's <c>Deaths</c>, read between ticks),
    /// once: a second call for the same tick adds nothing, so a per-tick hook and the frame loop may both call it. The
    /// slots written go to <paramref name="added"/> (as many as fit). Returns the number of deaths taken.
    /// </summary>
    public int Collect(ReadOnlySpan<DeathEvent> deaths, long tick, Span<int> added)
    {
        if (tick == _lastCollected) return 0;
        _lastCollected = tick;
        for (int i = 0; i < deaths.Length; i++)
        {
            int slot = Add(deaths[i], tick);
            if (i < added.Length) added[i] = slot;
        }
        return deaths.Length;
    }

    /// <summary>Removes every marker whose time is up at tick <paramref name="tick"/>; writes their slots to <paramref name="removed"/> (as many as fit) and returns how many were removed.</summary>
    public int Expire(long tick, Span<int> removed)
    {
        int n = 0;
        for (int i = 0; i < _active.Length; i++)
        {
            if (!_active[i] || _expires[i] > tick) continue;
            _active[i] = false;
            Count--;
            Expired++;
            if (n < removed.Length) removed[n] = i;
            n++;
        }
        return n;
    }
}
