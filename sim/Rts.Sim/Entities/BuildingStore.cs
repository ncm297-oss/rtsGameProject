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
/// building is a wall. Read-only outside the sim. Since M3-3 a worker's <c>Command.Build</c> places a
/// construction site (<see cref="UnderConstruction"/>, growing with <see cref="Work"/>); the dev/test
/// command <c>Command.SpawnBuilding</c> still places a finished building at full hit points.
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
    private readonly bool[] _underConstruction;
    private readonly int[] _work;
    private readonly long[] _repairProgress;
    private readonly long[] _repairGold;
    private readonly long[] _repairWood;
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
        _underConstruction = new bool[capacity];
        _work = new int[capacity];
        _repairProgress = new long[capacity];
        _repairGold = new long[capacity];
        _repairWood = new long[capacity];
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

    /// <summary>Hit points of each live slot: a site's grow with its <see cref="Work"/> (at least 1); a finished building's start at its type's maximum.</summary>
    public ReadOnlySpan<int> Hp => _hp;

    /// <summary>Whether each live slot is a construction site (M3-3): it blocks its cells, provides nothing, and completes at <see cref="WorkNeeded"/>.</summary>
    public ReadOnlySpan<bool> UnderConstruction => _underConstruction;

    /// <summary>Construction work done on each site (see <see cref="WorkNeeded"/>).</summary>
    public ReadOnlySpan<int> Work => _work;

    /// <summary>Work a building of type <paramref name="typeId"/> needs: <c>EconomyConstants.BuildWorkScale</c> (3) x its one-worker build ticks, so n builders adding n + 2 a tick take <c>t x 3 / (n + 2)</c> ticks (docs/02).</summary>
    public int WorkNeeded(int typeId) => Economy.EconomyConstants.BuildWorkScale * _defs[typeId].BuildTicks;

    /// <summary>The slot of the live building whose footprint covers cell (<paramref name="x"/>, <paramref name="y"/>), or -1.</summary>
    public int SlotAt(int x, int y)
    {
        int w = _grid.Width;
        for (int i = 0; i < _highWater; i++)
        {
            if (!_alive[i]) continue;
            BuildingDef def = _defs[_typeId[i]];
            int ax = _cell[i] % w, ay = _cell[i] / w;
            if (x >= ax && y >= ay && x < ax + def.FootprintWidth && y < ay + def.FootprintHeight) return i;
        }
        return -1;
    }

    /// <summary>Repair accumulator of slot <paramref name="index"/> (M3-3): hit points restored, in fixed point.</summary>
    internal ref long RepairProgress(int index) => ref _repairProgress[index];

    /// <summary>Gold owed for repair so far, fixed point (see <see cref="RepairProgress"/>).</summary>
    internal ref long RepairGold(int index) => ref _repairGold[index];

    /// <summary>Wood owed for repair so far, fixed point (see <see cref="RepairProgress"/>).</summary>
    internal ref long RepairWood(int index) => ref _repairWood[index];

    /// <summary>Sets a site's work and its hit points from it (<c>max(1, maxHp x work / needed)</c>); at the work needed it completes at full hit points. For the construction system and tests.</summary>
    internal void SetWork(int index, int work)
    {
        int needed = WorkNeeded(_typeId[index]);
        int max = _defs[_typeId[index]].Hp;
        if (work >= needed)
        {
            _work[index] = needed;
            _underConstruction[index] = false;
            _hp[index] = max;
            return;
        }
        _work[index] = work;
        _hp[index] = (int)Math.Max(1L, (long)max * work / needed);
    }

    /// <summary>Sets slot <paramref name="index"/>'s hit points (repair, tests); the caller keeps them between 1 and the type's maximum.</summary>
    internal void SetHp(int index, int hp) => _hp[index] = hp;

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
    /// <paramref name="cell"/>, at full hit points (or, with <paramref name="site"/>, as a construction site with no
    /// work and 1 hit point), and blocks its footprint (one <see cref="NavGrid.Version"/>
    /// bump). Refuses (false, default handle) without throwing when the store is full or it doesn't <see cref="Fits"/>.
    /// </summary>
    internal bool Spawn(int owner, int typeId, int cell, out EntityHandle handle, bool site = false)
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
        _hp[index] = site ? 1 : def.Hp;
        _underConstruction[index] = site;
        _work[index] = 0;
        _grid.SetBuilding(cell % _grid.Width, cell / _grid.Width, def.FootprintWidth, def.FootprintHeight);
        handle = new EntityHandle(index, _generation[index]);
        return true;
    }

    /// <summary>
    /// Takes <paramref name="amount"/> hit points from a live building (the damage seam combat uses in M4); at 0 it is
    /// <see cref="Free"/>d, an opening change. Nothing for a dead or stale handle or an amount below 1.
    /// </summary>
    internal void Damage(EntityHandle handle, int amount)
    {
        if (amount < 1 || !IsAlive(handle)) return;
        int i = handle.Index;
        _hp[i] = amount >= _hp[i] ? 0 : _hp[i] - amount;
        if (_hp[i] == 0) Free(handle);
    }

    /// <summary>Removes a live building (destroyed, or a cancelled site): its cells reopen (one <see cref="NavGrid.Version"/> bump, an opening change) and its generation moves on. False for a dead or stale handle.</summary>
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
        _underConstruction[index] = false;
        _work[index] = 0;
        _repairProgress[index] = 0;
        _repairGold[index] = 0;
        _repairWood[index] = 0;
        _generation[index]++;
        _freeList[_freeCount++] = index;
        return true;
    }

    /// <summary>
    /// Mixes the store into a state hash the way <see cref="ResourceStore"/> does: capacity, the high-water
    /// mark, each used slot's generation (and, when alive, owner, type, anchor cell, hit points, construction state,
    /// work and repair accumulators), then the free list above the never-used slots.
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
            h.Add(_underConstruction[i]);
            h.Add(_work[i]);
            h.Add((ulong)_repairProgress[i]);
            h.Add((ulong)_repairGold[i]);
            h.Add((ulong)_repairWood[i]);
        }
        h.Add(_freeCount);
        for (int k = Capacity - _highWater; k < _freeCount; k++)
            h.Add(_freeList[k]);
    }
}
