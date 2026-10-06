using System;
using System.Collections.Immutable;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Map;

namespace Rts.Sim.Entities;

/// <summary>Structure-of-arrays storage for buildings, addressed by generational handles (docs/03 "Entity model", M3-2).</summary>
/// <remarks>
/// Arrays are sized once from <see cref="SimConfig.BuildingCapacity"/> and never grow. A live building
/// covers its type's footprint from its anchor <see cref="Cell"/> (the footprint's lowest x, y cell), and
/// those cells are blocked in the <see cref="NavGrid"/> (<see cref="NavFlags.Building"/>), so to movement a
/// building is a wall. Read-only outside the sim. Until construction arrives (M3-3) the only way in is
/// the dev/test command <c>Command.SpawnBuilding</c>, and a building stands at full hit points.
/// </remarks>
public sealed class BuildingStore
{
    /// <summary>Default <see cref="SimConfig.BuildingCapacity"/>.</summary>
    public const int DefaultCapacity = 256;

    private readonly bool[] _alive;
    private readonly int[] _generation;
    private readonly int[] _owner;
    private readonly int[] _typeId;
    private readonly int[] _cell;
    private readonly int[] _hp;
    private readonly int[] _freeList;
    private readonly NavGrid _grid;
    private readonly ImmutableArray<BuildingDef> _defs;
    private int _freeCount;
    // One past the highest slot ever used; slots from here on still hold their initial state (see ResourceStore).
    private int _highWater;

    /// <summary>Creates a store with a fixed number of slots for buildings on <paramref name="grid"/>, with footprints from <paramref name="data"/>.</summary>
    internal BuildingStore(int capacity, NavGrid grid, GameData data)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _defs = data.Buildings;
        _alive = new bool[capacity];
        _generation = new int[capacity];
        _owner = new int[capacity];
        _typeId = new int[capacity];
        _cell = new int[capacity];
        _hp = new int[capacity];
        _freeList = new int[capacity];
        // Push in reverse so the first spawns take slots 0, 1, 2...
        for (int i = 0; i < capacity; i++)
        {
            _freeList[i] = capacity - 1 - i;
            // Generation starts at 1 so default(EntityHandle) never resolves to a live building.
            _generation[i] = 1;
            _cell[i] = -1;
        }
        _freeCount = capacity;
    }

    /// <summary>Whether the slot holds a live building.</summary>
    public ReadOnlySpan<bool> Alive => _alive;

    /// <summary>Per-slot generation; a handle is valid only while it matches.</summary>
    public ReadOnlySpan<int> Generation => _generation;

    /// <summary>Owning player of each live slot.</summary>
    public ReadOnlySpan<int> Owner => _owner;

    /// <summary>Building type id (index into <see cref="GameData.Buildings"/>) of each live slot.</summary>
    public ReadOnlySpan<int> TypeId => _typeId;

    /// <summary>Anchor nav cell (<c>y * Width + x</c>, the footprint's lowest x, y) of each live slot; -1 for a free slot.</summary>
    public ReadOnlySpan<int> Cell => _cell;

    /// <summary>Hit points of each live slot (its type's maximum until combat and construction, M3-3 / M4).</summary>
    public ReadOnlySpan<int> Hp => _hp;

    /// <summary>Total number of slots.</summary>
    public int Capacity => _alive.Length;

    /// <summary>Number of live buildings.</summary>
    public int Count => Capacity - _freeCount;

    /// <summary>Number of slots currently on the free list.</summary>
    public int FreeCount => _freeCount;

    /// <summary>Slot index stored at position <paramref name="i"/> of the free list.</summary>
    public int FreeListAt(int i) => _freeList[i];

    /// <summary>Whether the handle still refers to a live building.</summary>
    public bool IsAlive(EntityHandle handle) =>
        (uint)handle.Index < (uint)Capacity && _alive[handle.Index] && _generation[handle.Index] == handle.Generation;

    /// <summary>The handle of a slot (for views and tests iterating slots).</summary>
    public EntityHandle HandleOf(int index) => new(index, _generation[index]);

    /// <summary>
    /// True if a building of type <paramref name="typeId"/> fits with its anchor at <paramref name="cell"/> as far
    /// as terrain goes: the resource-node rule (<see cref="ResourceStore.Fits"/>): every footprint cell in the
    /// map, passable (no cliff, border, sealed pocket, node or building), not a ramp, and all on one level.
    /// Units are not checked here (<c>EconomySystem</c> does at apply). Never throws.
    /// </summary>
    public bool Fits(int typeId, int cell)
    {
        if ((uint)typeId >= (uint)_defs.Length) return false;
        int w = _grid.Width;
        if ((uint)cell >= (uint)(w * _grid.Height)) return false;
        BuildingDef def = _defs[typeId];
        int x0 = cell % w, y0 = cell / w;
        if (x0 + def.FootprintWidth > w || y0 + def.FootprintHeight > _grid.Height) return false;
        int level = _grid.LevelAt(x0, y0);
        for (int y = y0; y < y0 + def.FootprintHeight; y++)
        {
            for (int x = x0; x < x0 + def.FootprintWidth; x++)
            {
                if (!_grid.CanTakeResource(x, y) || _grid.LevelAt(x, y) != level) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Places a building of type <paramref name="typeId"/> for <paramref name="owner"/> with its anchor at
    /// <paramref name="cell"/>, at full hit points, and blocks its footprint (one <see cref="NavGrid.Version"/>
    /// bump). Refuses (false, default handle) without throwing when the store is full or it doesn't <see cref="Fits"/>.
    /// </summary>
    internal bool Spawn(int owner, int typeId, int cell, out EntityHandle handle)
    {
        handle = default;
        if (_freeCount == 0 || !Fits(typeId, cell)) return false;
        int index = _freeList[--_freeCount];
        if (index >= _highWater) _highWater = index + 1;
        BuildingDef def = _defs[typeId];
        _alive[index] = true;
        _owner[index] = owner;
        _typeId[index] = typeId;
        _cell[index] = cell;
        _hp[index] = def.Hp;
        _grid.SetBuilding(cell % _grid.Width, cell / _grid.Width, def.FootprintWidth, def.FootprintHeight);
        handle = new EntityHandle(index, _generation[index]);
        return true;
    }

    /// <summary>Removes a live building: its cells reopen (one <see cref="NavGrid.Version"/> bump) and its generation moves on. False for a dead or stale handle. Test seam until destruction (M3-3 / M4).</summary>
    internal bool Free(EntityHandle handle)
    {
        if (!IsAlive(handle)) return false;
        int index = handle.Index;
        BuildingDef def = _defs[_typeId[index]];
        int cell = _cell[index];
        _grid.ClearBuilding(cell % _grid.Width, cell / _grid.Width, def.FootprintWidth, def.FootprintHeight);
        _alive[index] = false;
        _owner[index] = 0;
        _typeId[index] = 0;
        _cell[index] = -1;
        _hp[index] = 0;
        _generation[index]++;
        _freeList[_freeCount++] = index;
        return true;
    }

    /// <summary>
    /// Mixes the store into a state hash the way <see cref="ResourceStore"/> does: capacity, the high-water
    /// mark, each used slot's generation (and, when alive, owner, type, anchor cell and hit points), then the
    /// free list above the never-used slots.
    /// </summary>
    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Capacity);
        h.Add(_highWater);
        for (int i = 0; i < _highWater; i++)
        {
            h.Add(_generation[i]);
            h.Add(_alive[i]);
            if (!_alive[i]) continue;
            h.Add(_owner[i]);
            h.Add(_typeId[i]);
            h.Add(_cell[i]);
            h.Add(_hp[i]);
        }
        h.Add(_freeCount);
        for (int k = Capacity - _highWater; k < _freeCount; k++)
            h.Add(_freeList[k]);
    }
}
