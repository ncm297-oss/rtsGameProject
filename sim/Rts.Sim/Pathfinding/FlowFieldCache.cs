using System;
using Rts.Sim.Map;

namespace Rts.Sim.Pathfinding;

/// <summary>Least-recently-used cache of flow fields keyed by target cell and tagged with <see cref="NavGrid.Version"/> (docs/03 "Flow fields").</summary>
/// <remarks>
/// Every field, the Dijkstra queue and the bookkeeping are allocated in the constructor, so a miss
/// that builds a field inside a tick allocates nothing. Memory is about 5 bytes x cells x capacity
/// (2.6 MB for 32 fields on a 128 x 128 map). Fields are derived state: which ones are cached
/// never changes a result, because a field depends only on the grid and its target.
/// </remarks>
public sealed class FlowFieldCache
{
    /// <summary>Number of fields kept when no capacity is given (docs/03).</summary>
    public const int DefaultCapacity = 32;

    private readonly NavGrid _grid;
    private readonly FlowField[] _fields;
    private readonly long[] _lastUse;
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
        _queue = new CellQueue(grid.Width * grid.Height);
        _steps = new byte[grid.Width * grid.Height];
        FlowField.ComputeSteps(grid, _steps);
        _stepsVersion = grid.Version;
    }

    /// <summary>Maximum number of cached fields.</summary>
    public int Capacity => _fields.Length;

    /// <summary>Number of fields built so far.</summary>
    public int Count => _count;

    /// <summary>Total field builds (misses plus stale rebuilds); for tests and profiling.</summary>
    public int BuildCount { get; private set; }

    /// <summary>True if an up-to-date field for the target is cached; does not count as a use.</summary>
    public bool Contains(int targetCell) => Find(targetCell) >= 0;

    /// <summary>The cached, up-to-date field for the target, marked most recently used; null (and nothing built) on a miss.</summary>
    public FlowField? TryGetCached(int targetCell)
    {
        int slot = Find(targetCell);
        if (slot < 0) return null;
        _lastUse[slot] = ++_clock;
        return _fields[slot];
    }

    /// <summary>The field leading to <paramref name="targetCell"/>: cached if current, otherwise built now, evicting the least recently used field when full.</summary>
    public FlowField Get(int targetCell)
    {
        if ((uint)targetCell >= (uint)(_grid.Width * _grid.Height))
            throw new ArgumentOutOfRangeException(nameof(targetCell));
        _clock++;
        int slot = -1;
        for (int i = 0; i < _count; i++)
        {
            if (_fields[i].RequestedCell == targetCell)
            {
                slot = i;
                break;
            }
        }
        if (slot >= 0 && _fields[slot].Version == _grid.Version)
        {
            _lastUse[slot] = _clock;
            return _fields[slot];
        }
        if (slot < 0)
            slot = _count < _fields.Length ? _count++ : LeastRecentlyUsed();
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

    private int Find(int targetCell)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_fields[i].RequestedCell == targetCell && _fields[i].Version == _grid.Version)
                return i;
        }
        return -1;
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
