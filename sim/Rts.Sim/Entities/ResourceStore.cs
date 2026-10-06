using System;
using System.Collections.Immutable;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Map;

namespace Rts.Sim.Entities;

/// <summary>Structure-of-arrays storage for resource nodes (trees, gold mines), addressed by generational handles (docs/03 "Economy implementation").</summary>
/// <remarks>
/// Arrays are sized once from <see cref="SimConfig.ResourceCapacity"/> and never grow. A live node
/// covers its type's footprint from its anchor <see cref="Cell"/> (the footprint's lowest x, y cell)
/// and those cells are blocked in the <see cref="NavGrid"/> (<see cref="NavFlags.Resource"/>). Outside
/// the sim this is read-only: only the sim spawns nodes (map setup) and takes from them (gathering, M3-2).
/// A node taken to 0 is freed: its cells reopen, the grid's version bumps once, its slot goes on a LIFO
/// free list and its generation moves on, so old handles stop resolving.
/// </remarks>
public sealed class ResourceStore
{
    /// <summary>Default <see cref="SimConfig.ResourceCapacity"/>: room for every tree and mine of a full map.</summary>
    public const int DefaultCapacity = 4096;

    private readonly bool[] _alive;
    private readonly int[] _generation;
    private readonly int[] _typeId;
    private readonly int[] _cell;
    private readonly int[] _remaining;
    private readonly int[] _freeList;
    private readonly NavGrid _grid;
    private readonly ImmutableArray<ResourceDef> _defs;
    private int _freeCount;
    // One past the highest slot ever used. Slots from here on still hold their initial state and sit
    // at the bottom of the free list in a known order, so the state hash can skip them.
    private int _highWater;

    /// <summary>Creates a store with a fixed number of slots for nodes on <paramref name="grid"/>, with footprints from <paramref name="data"/>.</summary>
    internal ResourceStore(int capacity, NavGrid grid, GameData data)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _defs = data.Resources;
        _alive = new bool[capacity];
        _generation = new int[capacity];
        _typeId = new int[capacity];
        _cell = new int[capacity];
        _remaining = new int[capacity];
        _freeList = new int[capacity];
        // Push in reverse so the first spawns take slots 0, 1, 2...
        for (int i = 0; i < capacity; i++)
        {
            _freeList[i] = capacity - 1 - i;
            // Generation starts at 1 so default(EntityHandle) never resolves to a live node.
            _generation[i] = 1;
            _cell[i] = -1;
        }
        _freeCount = capacity;
    }

    /// <summary>Whether the slot holds a live node.</summary>
    public ReadOnlySpan<bool> Alive => _alive;

    /// <summary>Per-slot generation; a handle is valid only while it matches.</summary>
    public ReadOnlySpan<int> Generation => _generation;

    /// <summary>Resource type id (index into <see cref="GameData.Resources"/>) of each live slot.</summary>
    public ReadOnlySpan<int> TypeId => _typeId;

    /// <summary>Anchor nav cell (<c>y * Width + x</c>, the footprint's lowest x, y) of each live slot; -1 for a free slot.</summary>
    public ReadOnlySpan<int> Cell => _cell;

    /// <summary>Amount left in each live slot (wood or gold, by its type's <see cref="ResourceDef.Resource"/>); always at least 1 while alive.</summary>
    public ReadOnlySpan<int> Remaining => _remaining;

    /// <summary>Total number of slots.</summary>
    public int Capacity => _alive.Length;

    /// <summary>Number of live nodes.</summary>
    public int Count => Capacity - _freeCount;

    /// <summary>Number of slots currently on the free list.</summary>
    public int FreeCount => _freeCount;

    /// <summary>Slot index stored at position <paramref name="i"/> of the free list.</summary>
    public int FreeListAt(int i) => _freeList[i];

    /// <summary>Whether the handle still refers to a live node.</summary>
    public bool IsAlive(EntityHandle handle) =>
        (uint)handle.Index < (uint)Capacity && _alive[handle.Index] && _generation[handle.Index] == handle.Generation;

    /// <summary>The handle of a live slot (for views and tests iterating slots).</summary>
    public EntityHandle HandleOf(int index) => new(index, _generation[index]);

    /// <summary>
    /// True if a node of type <paramref name="typeId"/> fits with its anchor at <paramref name="cell"/>: the
    /// footprint lies in the map and every cell is open, flat (no ramp) ground of one level with no other
    /// node on it. Never throws.
    /// </summary>
    public bool Fits(int typeId, int cell)
    {
        if ((uint)typeId >= (uint)_defs.Length) return false;
        int w = _grid.Width;
        if ((uint)cell >= (uint)(w * _grid.Height)) return false;
        ResourceDef def = _defs[typeId];
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
    /// Places a node of type <paramref name="typeId"/> holding <paramref name="amount"/> with its anchor at
    /// <paramref name="cell"/> and blocks its footprint (one <see cref="NavGrid.Version"/> bump). Refuses
    /// (false, default handle) without throwing when the store is full, the type is unknown, the amount is
    /// below 1, or the footprint doesn't <see cref="Fits"/>.
    /// </summary>
    internal bool Spawn(int typeId, int cell, int amount, out EntityHandle handle)
    {
        handle = default;
        if (_freeCount == 0 || amount < 1 || !Fits(typeId, cell)) return false;
        int index = _freeList[--_freeCount];
        if (index >= _highWater) _highWater = index + 1;
        _alive[index] = true;
        _typeId[index] = typeId;
        _cell[index] = cell;
        _remaining[index] = amount;
        ResourceDef def = _defs[typeId];
        _grid.SetResource(cell % _grid.Width, cell / _grid.Width, def.FootprintWidth, def.FootprintHeight);
        handle = new EntityHandle(index, _generation[index]);
        return true;
    }

    /// <summary>
    /// Takes up to <paramref name="amount"/> from the node and returns how much was taken (clamped to what is
    /// left; 0 for a dead or stale handle or a non-positive amount). A node left at 0 is freed: its cells
    /// reopen, <see cref="NavGrid.Version"/> bumps once, and its generation moves on. Never allocates.
    /// </summary>
    /// <remarks>The primitive gathering (M3-2, tick phase 4) calls.</remarks>
    internal int Take(EntityHandle handle, int amount)
    {
        if (amount <= 0 || !IsAlive(handle)) return 0;
        int i = handle.Index;
        int taken = Math.Min(amount, _remaining[i]);
        _remaining[i] -= taken;
        if (_remaining[i] == 0) Free(i);
        return taken;
    }

    private void Free(int index)
    {
        ResourceDef def = _defs[_typeId[index]];
        int cell = _cell[index];
        _grid.ClearResource(cell % _grid.Width, cell / _grid.Width, def.FootprintWidth, def.FootprintHeight);
        _alive[index] = false;
        _typeId[index] = 0;
        _cell[index] = -1;
        _remaining[index] = 0;
        _generation[index]++;
        _freeList[_freeCount++] = index;
    }

    /// <summary>
    /// Mixes the store into a state hash: capacity, the high-water mark, each used slot's generation (and,
    /// when alive, its remaining amount, type and anchor cell), then the free list above the never-used
    /// slots, in slot order. Never-used slots are left out: they always hold generation 1, no node, and
    /// the bottom of the free list in a fixed order, so the high-water mark stands for them. Slots go in
    /// as whole words (<see cref="StateHasher.AddWord"/>), so a full 4,096-slot store hashes in well under
    /// 0.05 ms; a live slot's first word has its remaining amount (at least 1) in the high half and a
    /// free slot's has 0 there, so the alive flag is in the stream too.
    /// </summary>
    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Capacity);
        h.Add(_highWater);
        for (int i = 0; i < _highWater; i++)
        {
            if (!_alive[i])
            {
                h.AddWord((uint)_generation[i]);
                continue;
            }
            h.AddWord((uint)_generation[i] | ((ulong)(uint)_remaining[i] << 32));
            h.AddWord((uint)_typeId[i] | ((ulong)(uint)_cell[i] << 32));
        }
        h.Add(_freeCount);
        for (int k = Capacity - _highWater; k < _freeCount; k++)
            h.AddWord((uint)_freeList[k]);
    }
}
