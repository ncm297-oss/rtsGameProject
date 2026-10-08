using System;
using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Map;

namespace Rts.Sim.Entities;

/// <summary>Structure-of-arrays storage for buildings, addressed by generational handles (docs/03 "Entity model", M3-2).</summary>
/// <remarks>
/// Arrays are sized once from <see cref="SimConfig.BuildingCapacity"/> and never grow. A live building
/// covers its type's footprint from its anchor <see cref="Cell"/> (the footprint's lowest x, y cell), and
/// those cells are blocked in the <see cref="NavGrid"/> (<see cref="NavFlags.Building"/>), so to movement a
/// building is a wall. Read-only outside the sim. Since M3-3 a worker's <c>Command.Build</c> places a
/// construction site (<see cref="UnderConstruction"/>, growing with <see cref="Work"/>); the dev/test
/// command <c>Command.SpawnBuilding</c> still places a finished building at full hit points. Since M3-4 a finished
/// building provides its type's population and has a production queue (<see cref="QueueCount"/>,
/// <see cref="QueueTypeAt"/>, <see cref="Progress"/>) and a rally point (<see cref="HasRally"/>, <see cref="RallyPosition"/>).
/// Since M3-5 a queue item is a unit or a tech (<see cref="QueueIsTechAt"/>, <see cref="ItemTicks"/>). Since M3-6 the
/// world's ledger counts each player's finished buildings per type and slot as they spawn finished, complete or go
/// (derived, for the requirement gates).
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
    private readonly int[] _queueCount;
    private readonly int[] _queueTypeId;
    private readonly bool[] _queueIsTech;
    private readonly int[] _progress;
    private readonly bool[] _hasRally;
    private readonly Vector2[] _rallyPosition;
    private readonly int[] _freeList;
    private readonly NavGrid _grid;
    private readonly ImmutableArray<BuildingDef> _defs;
    private readonly ImmutableArray<UnitDef> _units;
    private readonly ImmutableArray<TechDef> _techs;
    private readonly PlayerLedger? _ledger;
    private int _freeCount;
    // One past the highest slot ever used; slots from here on still hold their initial state (see ResourceStore).
    private int _highWater;

    /// <summary>Creates a store with a fixed number of slots for buildings on <paramref name="grid"/>, with footprints from <paramref name="data"/>.</summary>
    internal BuildingStore(int capacity, NavGrid grid, GameData data) : this(capacity, grid, data, null)
    {
    }

    /// <summary>The world's store: finished buildings provide population to, and freed queues refund into, <paramref name="ledger"/> (M3-4).</summary>
    internal BuildingStore(int capacity, NavGrid grid, GameData data, PlayerLedger? ledger)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _defs = data.Buildings;
        _units = data.Units;
        _techs = data.Techs;
        _ledger = ledger;
        _queueCount = new int[capacity];
        _queueTypeId = new int[capacity * EconomyConstants.ProductionQueueCapacity];
        _queueIsTech = new bool[capacity * EconomyConstants.ProductionQueueCapacity];
        _progress = new int[capacity];
        _hasRally = new bool[capacity];
        _rallyPosition = new Vector2[capacity];
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

    /// <summary>Number of items in each live slot's production queue (0 to <see cref="EconomyConstants.ProductionQueueCapacity"/>; always 0 for a site), M3-4.</summary>
    public ReadOnlySpan<int> QueueCount => _queueCount;

    /// <summary>
    /// The type id of item <paramref name="item"/> (0 = the head, in progress) of slot <paramref name="slot"/>'s queue: a
    /// unit type id, or a tech id when <see cref="QueueIsTechAt"/> says so (M3-5); -1 past <see cref="QueueCount"/> or out of range.
    /// </summary>
    public int QueueTypeAt(int slot, int item)
    {
        if ((uint)slot >= (uint)Capacity || (uint)item >= (uint)_queueCount[slot]) return -1;
        return _queueTypeId[slot * EconomyConstants.ProductionQueueCapacity + item];
    }

    /// <summary>True if item <paramref name="item"/> of slot <paramref name="slot"/>'s queue is a tech being researched (M3-5), false for a unit or past <see cref="QueueCount"/>.</summary>
    public bool QueueIsTechAt(int slot, int item)
    {
        if ((uint)slot >= (uint)Capacity || (uint)item >= (uint)_queueCount[slot]) return false;
        return _queueIsTech[slot * EconomyConstants.ProductionQueueCapacity + item];
    }

    /// <summary>
    /// Ticks of work done on each slot's head item (M3-4): 0 while it hasn't started (a unit waiting for population), 1 to
    /// <see cref="ItemTicks"/> once started; a started unit head holds its population reservation (a tech reserves none,
    /// M3-5). At <see cref="ItemTicks"/> a unit is complete and waits for a free cell to spawn on; a tech completes at once.
    /// </summary>
    public ReadOnlySpan<int> Progress => _progress;

    /// <summary>Ticks a unit of <paramref name="unitType"/> takes to train (its <c>trainTime</c>); 0 for an unknown type.</summary>
    public int TrainTicks(int unitType) => (uint)unitType < (uint)_units.Length ? _units[unitType].TrainTicks : 0;

    /// <summary>Ticks item <paramref name="item"/> of slot <paramref name="slot"/>'s queue takes: its unit's train ticks or its tech's research ticks (M3-5); 0 past <see cref="QueueCount"/>.</summary>
    public int ItemTicks(int slot, int item)
    {
        int type = QueueTypeAt(slot, item);
        if (type < 0) return 0;
        if (!QueueIsTechAt(slot, item)) return TrainTicks(type);
        return (uint)type < (uint)_techs.Length ? _techs[type].ResearchTicks : 0;
    }

    /// <summary>Whether each live slot has a rally point (M3-4): units it trains walk to <see cref="RallyPosition"/>.</summary>
    public ReadOnlySpan<bool> HasRally => _hasRally;

    /// <summary>Each slot's rally point (x, z) in meters; zero without one.</summary>
    public ReadOnlySpan<Vector2> RallyPosition => _rallyPosition;

    /// <summary>The half-pop slot <paramref name="slot"/>'s started head item reserves (0 when nothing has started).</summary>
    public int ReservedHalfPop(int slot)
    {
        if ((uint)slot >= (uint)Capacity || _progress[slot] == 0 || _queueCount[slot] == 0) return 0;
        if (_queueIsTech[slot * EconomyConstants.ProductionQueueCapacity]) return 0; // research takes no population
        int type = _queueTypeId[slot * EconomyConstants.ProductionQueueCapacity];
        return (uint)type < (uint)_units.Length ? _units[type].HalfPop : 0;
    }

    /// <summary>Raw queue entry <paramref name="item"/> of slot <paramref name="slot"/> (production system, hash tests).</summary>
    internal ref int QueueEntry(int slot, int item) => ref _queueTypeId[slot * EconomyConstants.ProductionQueueCapacity + item];

    /// <summary>Raw tech flag of queue entry <paramref name="item"/> of slot <paramref name="slot"/> (hash tests).</summary>
    internal ref bool QueueIsTechEntry(int slot, int item) => ref _queueIsTech[slot * EconomyConstants.ProductionQueueCapacity + item];

    /// <summary>True if <paramref name="player"/> has <paramref name="tech"/> queued at any of its live buildings (a tech is queued once at a time, M3-5).</summary>
    internal bool IsTechQueued(int player, int tech)
    {
        for (int k = 0; k < _highWater; k++)
        {
            if (!_alive[k] || _owner[k] != player) continue;
            int head = k * EconomyConstants.ProductionQueueCapacity;
            for (int q = 0; q < _queueCount[k]; q++)
                if (_queueIsTech[head + q] && _queueTypeId[head + q] == tech) return true;
        }
        return false;
    }

    /// <summary>Writable <see cref="QueueCount"/> of a slot (production system, tests).</summary>
    internal ref int QueueCountOf(int slot) => ref _queueCount[slot];

    /// <summary>Writable <see cref="Progress"/> of a slot (production system, tests).</summary>
    internal ref int ProgressOf(int slot) => ref _progress[slot];

    /// <summary>Sets or (<paramref name="has"/> false) clears slot <paramref name="slot"/>'s rally point; a cleared one goes back to zero.</summary>
    internal void SetRally(int slot, bool has, Vector2 position)
    {
        _hasRally[slot] = has;
        _rallyPosition[slot] = has ? position : Vector2.Zero;
    }

    /// <summary>Appends unit type <paramref name="unitType"/> to slot <paramref name="slot"/>'s queue; the caller checked the room and took the cost.</summary>
    internal void Enqueue(int slot, int unitType) => Enqueue(slot, unitType, false);

    /// <summary>Appends tech <paramref name="tech"/> to slot <paramref name="slot"/>'s queue (M3-5); the caller checked the rules and took the cost.</summary>
    internal void EnqueueTech(int slot, int tech) => Enqueue(slot, tech, true);

    private void Enqueue(int slot, int type, bool tech)
    {
        int n = _queueCount[slot];
        _queueTypeId[slot * EconomyConstants.ProductionQueueCapacity + n] = type;
        _queueIsTech[slot * EconomyConstants.ProductionQueueCapacity + n] = tech;
        _queueCount[slot] = n + 1;
    }

    /// <summary>
    /// Removes item <paramref name="item"/> of slot <paramref name="slot"/>'s queue, a unit or a tech: its full cost goes back
    /// to the owner and, for a started head, its progress (and a unit's population reservation) is dropped; later items
    /// shift down and the freed last entry goes back to default. The caller checks the index.
    /// </summary>
    internal void RemoveQueued(int slot, int item)
    {
        int e = slot * EconomyConstants.ProductionQueueCapacity + item;
        if (_queueIsTech[e])
        {
            TechDef tech = _techs[_queueTypeId[e]];
            _ledger?.Refund(_owner[slot], tech.CostGold, tech.CostWood);
        }
        else
        {
            UnitDef def = _units[_queueTypeId[e]];
            _ledger?.Refund(_owner[slot], def.CostGold, def.CostWood);
        }
        if (item == 0) ReleaseHead(slot);
        Shift(slot, item);
    }

    /// <summary>The head item is done (a unit spawned, whose own population count takes over the reservation, or a tech researched): progress 0, reservation released, the rest shift down.</summary>
    internal void PopTrained(int slot)
    {
        ReleaseHead(slot);
        Shift(slot, 0);
    }

    /// <summary>The head item's training ends: progress 0, its reservation released.</summary>
    private void ReleaseHead(int slot)
    {
        if (_progress[slot] == 0) return;
        _ledger?.AddHalfPop(_owner[slot], -ReservedHalfPop(slot));
        _progress[slot] = 0;
    }

    /// <summary>Drops entry <paramref name="item"/>: later entries move down one, the freed last entry goes back to default.</summary>
    private void Shift(int slot, int item)
    {
        int head = slot * EconomyConstants.ProductionQueueCapacity, n = _queueCount[slot];
        for (int k = item + 1; k < n; k++)
        {
            _queueTypeId[head + k - 1] = _queueTypeId[head + k];
            _queueIsTech[head + k - 1] = _queueIsTech[head + k];
        }
        _queueTypeId[head + n - 1] = 0;
        _queueIsTech[head + n - 1] = false;
        _queueCount[slot] = n - 1;
    }

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

    /// <summary>
    /// Sets a site's work and adds the hit points that work grows (<c>max(1, maxHp x work / needed)</c> less the same at
    /// the old work, capped at the maximum); at the work needed it completes. For the construction system and tests.
    /// </summary>
    /// <remarks>
    /// Added, not recomputed (BUG-0138): damage a site took stays taken, so a builder doesn't make it immune. The
    /// increments telescope, so an undamaged site reads exactly <c>max(1, maxHp x work / needed)</c>, as before, and
    /// completes at full hit points.
    /// </remarks>
    internal void SetWork(int index, int work)
    {
        int needed = WorkNeeded(_typeId[index]);
        int max = _defs[_typeId[index]].Hp;
        int old = _work[index];
        int grown = SiteHp(max, Math.Min(work, needed), needed) - SiteHp(max, old, needed);
        if (grown > 0) _hp[index] = (int)Math.Min(max, (long)_hp[index] + grown);
        if (work >= needed)
        {
            _work[index] = needed;
            // Completion: the building starts providing its population (M3-4) and meeting requirements (M3-6).
            if (_underConstruction[index])
            {
                _ledger?.AddProvided(_owner[index], _defs[_typeId[index]].HalfPopProvided);
                _ledger?.AddFinished(_owner[index], _typeId[index], _defs[_typeId[index]].Slot, 1);
            }
            _underConstruction[index] = false;
            return;
        }
        _work[index] = work;
    }

    /// <summary>An undamaged site's hit points at <paramref name="work"/> of <paramref name="needed"/>: <c>max(1, max x work / needed)</c>, the full <paramref name="max"/> at completion.</summary>
    private static int SiteHp(int max, int work, int needed) =>
        work >= needed ? max : (int)Math.Max(1L, (long)max * work / needed);

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
        if (!site)
        {
            _ledger?.AddProvided(owner, def.HalfPopProvided);
            _ledger?.AddFinished(owner, typeId, def.Slot, 1);
        }
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

    /// <summary>
    /// Removes a live building (destroyed, or a cancelled site) and its generation moves on. Its cells are freed by the
    /// grid's pocket rule (BUG-0093): they reopen (one <see cref="NavGrid.Version"/> bump, an opening change) when they
    /// touch open ground, else they stay blocked as <see cref="NavFlags.Pocket"/> cells with no bump. A finished building
    /// stops providing population (nothing dies; training elsewhere may pause), every item of its queue is refunded in
    /// full and a started head's reservation released, and its rally point goes (M3-4). False for a dead or stale handle.
    /// </summary>
    internal bool Free(EntityHandle handle)
    {
        if (!IsAlive(handle)) return false;
        int index = handle.Index;
        BuildingDef def = _defs[_typeId[index]];
        while (_queueCount[index] > 0) RemoveQueued(index, _queueCount[index] - 1);
        if (!_underConstruction[index])
        {
            _ledger?.AddProvided(_owner[index], -def.HalfPopProvided);
            _ledger?.AddFinished(_owner[index], _typeId[index], def.Slot, -1);
        }
        SetRally(index, false, Vector2.Zero);
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
    /// work and repair accumulators, and the production state of M3-4), then the free list above the never-used slots.
    /// </summary>
    /// <remarks>
    /// Production (M3-4) rides in the high half of the construction-state word (<see cref="ProductionBits"/>): zero for a
    /// building with an empty queue, no progress and no rally, which so hashes exactly as before M3-4; the flagged words
    /// follow it.
    /// </remarks>
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
            uint bits = ProductionBits(i);
            h.Add((_underConstruction[i] ? 1UL : 0UL) | ((ulong)bits << 32));
            if (bits != 0) AddProductionToHash(ref h, i, bits);
            h.Add(_work[i]);
            h.Add((ulong)_repairProgress[i]);
            h.Add((ulong)_repairGold[i]);
            h.Add((ulong)_repairWood[i]);
        }
        h.Add(_freeCount);
        for (int k = Capacity - _highWater; k < _freeCount; k++)
            h.Add(_freeList[k]);
    }

    /// <summary>
    /// Slot <paramref name="i"/>'s production summary: bit 0 a queue, bit 1 progress, bit 2 a rally, bit 3 a non-zero rally
    /// point, bit 4 + k queue entry k non-zero, bit 9 + k queue entry k a tech (M3-5; the flag itself, no word follows).
    /// </summary>
    private uint ProductionBits(int i)
    {
        uint bits = 0;
        if (_queueCount[i] != 0) bits |= 1u;
        if (_progress[i] != 0) bits |= 2u;
        if (_hasRally[i]) bits |= 4u;
        if (_rallyPosition[i] != Vector2.Zero) bits |= 8u;
        int head = i * EconomyConstants.ProductionQueueCapacity;
        for (int k = 0; k < EconomyConstants.ProductionQueueCapacity; k++)
        {
            if (_queueTypeId[head + k] != 0) bits |= 16u << k;
            if (_queueIsTech[head + k]) bits |= (16u << EconomyConstants.ProductionQueueCapacity) << k;
        }
        return bits;
    }

    /// <summary>The words <see cref="ProductionBits"/> flags (already hashed), in a fixed order.</summary>
    private void AddProductionToHash(ref StateHasher h, int i, uint bits)
    {
        if ((bits & 1u) != 0) h.Add(_queueCount[i]);
        if ((bits & 2u) != 0) h.Add(_progress[i]);
        if ((bits & 8u) != 0) h.Add(_rallyPosition[i]);
        int head = i * EconomyConstants.ProductionQueueCapacity;
        for (int k = 0; k < EconomyConstants.ProductionQueueCapacity; k++)
            if ((bits & (16u << k)) != 0) h.Add(_queueTypeId[head + k]);
    }
}
