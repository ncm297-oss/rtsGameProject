using System;
using Rts.Sim.Determinism;
using Rts.Sim.Map;

namespace Rts.Sim.Pathfinding;

/// <summary>Least-recently-used cache of flow fields keyed by target cell and tagged with <see cref="NavGrid.Version"/> and <see cref="NavGrid.BlockVersion"/> (docs/03 "Flow fields").</summary>
/// <remarks>
/// A cached field is *current* when its <see cref="FlowField.Version"/> matches the grid's and *usable* when its
/// <see cref="FlowField.BlockVersion"/> does: only cells have opened since it was built, so it never points into a
/// blocked cell, though it may miss a shorter way through the opened ones (M3-2b). Lookups answer for usable fields;
/// the movement build pass refreshes usable-but-stale ones after the missing ones, under the same cap.
/// <para>
/// Every field, the Dijkstra queue and the bookkeeping are allocated in the constructor, so a miss
/// that builds a field inside a tick allocates nothing. Memory is about 5 bytes x cells x capacity, plus 4 bytes x cells for the lookup index
/// (2.6 MB for 32 fields on a 128 x 128 map). A field's contents depend only on the grid and its
/// target, but under the per-tick build cap *which* fields are cached decides which units wait, so
/// the cache's keys, versions and LRU stamps are sim state: <see cref="AddToHash"/> covers them, and
/// only the sim may call <see cref="Get"/> or <see cref="TryGetCached"/> (BUG-0021). Views use
/// <see cref="PeekCached"/>, which is not a use.
/// </para>
/// </remarks>
public sealed class FlowFieldCache
{
    /// <summary>Number of fields kept when no capacity is given, and the floor of <see cref="CapacityFor"/> (docs/03).</summary>
    public const int DefaultCapacity = 32;

    /// <summary>Ceiling of <see cref="CapacityFor"/>'s unit-based capacity.</summary>
    public const int MaxCapacity = 128;

    /// <summary>Unit slots per cached field in <see cref="CapacityFor"/>.</summary>
    public const int UnitsPerField = 8;

    /// <summary>Memory one field takes per nav cell: a float cost and a direction byte.</summary>
    public const int BytesPerCell = 5;

    /// <summary>Field memory above which <see cref="CapacityFor"/> stops adding fields beyond <see cref="DefaultCapacity"/>: 64 MiB.</summary>
    public const long MemoryBudgetBytes = 64L * 1024 * 1024;

    private readonly NavGrid _grid;
    private readonly FlowField[] _fields;
    private readonly long[] _lastUse;
    // Requested cell -> slot holding it, -1 for none: O(1) lookups, since up to 128 slots are
    // searched for every goal group twice per tick.
    private readonly int[] _slotOfCell;
    private readonly CellQueue _queue;
    private readonly byte[] _steps;
    private int _stepsVersion;
    private int _count;
    private long _clock;

    /// <summary>Creates a cache for one grid with room for <paramref name="capacity"/> fields.</summary>
    public FlowFieldCache(NavGrid grid, int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _grid = grid;
        _fields = new FlowField[capacity];
        for (int i = 0; i < capacity; i++)
            _fields[i] = new FlowField(grid.Width, grid.Height);
        _lastUse = new long[capacity];
        _slotOfCell = new int[grid.Width * grid.Height];
        Array.Fill(_slotOfCell, -1);
        _queue = new CellQueue(grid.Width * grid.Height);
        _steps = new byte[grid.Width * grid.Height];
        FlowField.ComputeSteps(grid, _steps);
        _stepsVersion = grid.Version;
    }

    /// <summary>
    /// Cache capacity for a world: <c>clamp(unitCapacity / 8, 32, 128)</c>, then at most
    /// <c>max(32, 64 MiB / (5 x cellCount))</c> so big maps don't multiply memory (docs/03 "Flow fields").
    /// </summary>
    public static int CapacityFor(int unitCapacity, int cellCount)
    {
        if (unitCapacity < 1) throw new ArgumentOutOfRangeException(nameof(unitCapacity));
        if (cellCount < 1) throw new ArgumentOutOfRangeException(nameof(cellCount));
        int byUnits = Math.Clamp(unitCapacity / UnitsPerField, DefaultCapacity, MaxCapacity);
        long byMemory = Math.Max(DefaultCapacity, MemoryBudgetBytes / ((long)BytesPerCell * cellCount));
        return (int)Math.Min(byUnits, byMemory);
    }

    /// <summary>Maximum number of cached fields.</summary>
    public int Capacity => _fields.Length;

    /// <summary>Number of fields built so far.</summary>
    public int Count => _count;

    /// <summary>Total field builds (misses plus stale rebuilds); for tests and profiling.</summary>
    public int BuildCount { get; private set; }

    /// <summary>True if a usable field for the target is cached (current or only stale by opened cells, see the class remarks); does not count as a use.</summary>
    public bool Contains(int targetCell) => Find(targetCell) >= 0;

    /// <summary>Read-only peek for views and debug tools: the field units follow to the target, i.e. the cached field if it is usable (see the class remarks), else null (also for an out-of-range cell).</summary>
    /// <remarks>
    /// Not a use: it touches no LRU stamp, clock or count, so it never changes <see cref="Simulation.StateHash"/>
    /// and never builds. The instance is valid until the next <see cref="Simulation.Tick"/>, which may
    /// rebuild it in place for another target; read it through <see cref="FlowField.DirectionAt"/> and
    /// <see cref="FlowField.CostAt"/> and don't keep it across ticks.
    /// </remarks>
    public FlowField? PeekCached(int targetCell)
    {
        int slot = Find(targetCell);
        return slot < 0 ? null : _fields[slot];
    }

    /// <summary>The cached usable field for the target (it may be stale by opened cells: compare <see cref="FlowField.Version"/>), marked most recently used; null (and nothing built) on a miss.</summary>
    /// <remarks>Sim-only: a hit moves the hashed LRU stamp.</remarks>
    internal FlowField? TryGetCached(int targetCell)
    {
        int slot = Find(targetCell);
        if (slot < 0) return null;
        _lastUse[slot] = ++_clock;
        return _fields[slot];
    }

    /// <summary>The field leading to <paramref name="targetCell"/>: cached if current, otherwise built now (a stale or unusable slot for the target is rebuilt in place), evicting the least recently used field when full.</summary>
    /// <remarks>Sim-only: a call changes the hashed LRU state, so views and AI must never call it.</remarks>
    internal FlowField Get(int targetCell)
    {
        if ((uint)targetCell >= (uint)(_grid.Width * _grid.Height))
            throw new ArgumentOutOfRangeException(nameof(targetCell));
        _clock++;
        int slot = _slotOfCell[targetCell];
        if (slot >= 0 && _fields[slot].Version == _grid.Version)
        {
            _lastUse[slot] = _clock;
            return _fields[slot];
        }
        if (slot < 0)
        {
            slot = _count < _fields.Length ? _count++ : LeastRecentlyUsed();
            int evicted = _fields[slot].RequestedCell;
            if (evicted >= 0) _slotOfCell[evicted] = -1;
            _slotOfCell[targetCell] = slot;
        }
        if (_stepsVersion != _grid.Version)
        {
            FlowField.ComputeSteps(_grid, _steps);
            _stepsVersion = _grid.Version;
        }
        _fields[slot].Build(_grid, targetCell, _queue, _steps);
        BuildCount++;
        _lastUse[slot] = _clock;
        return _fields[slot];
    }

    /// <summary>Mixes the cache's sim-visible state into a state hash: clock, count, and each used slot's requested cell, version, block version and last use, in slot order.</summary>
    internal void AddToHash(ref StateHasher h)
    {
        h.Add((ulong)_clock);
        h.Add(_count);
        for (int i = 0; i < _count; i++)
        {
            h.Add(_fields[i].RequestedCell);
            h.Add(_fields[i].Version);
            h.Add(_fields[i].BlockVersion);
            h.Add((ulong)_lastUse[i]);
        }
    }

    private int Find(int targetCell)
    {
        if ((uint)targetCell >= (uint)_slotOfCell.Length) return -1;
        int slot = _slotOfCell[targetCell];
        return slot >= 0 && _fields[slot].BlockVersion == _grid.BlockVersion ? slot : -1;
    }

    private int LeastRecentlyUsed()
    {
        int lru = 0;
        for (int i = 1; i < _count; i++)
        {
            if (_lastUse[i] < _lastUse[lru]) lru = i;
        }
        return lru;
    }
}
