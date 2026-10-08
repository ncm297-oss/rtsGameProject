using System;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Orders;

namespace Rts.Sim.Entities;

/// <summary>Structure-of-arrays storage for units, addressed by generational handles.</summary>
/// <remarks>
/// Arrays are sized once from the configured capacity and never grow. Freed slots go on a LIFO
/// free list; each free bumps the slot's generation so old handles stop resolving.
/// </remarks>
public sealed class UnitStore
{
    /// <summary>Current position (x, z) in meters.</summary>
    public readonly Vector2[] Position;
    /// <summary>Position at the start of the tick, for render interpolation.</summary>
    public readonly Vector2[] PrevPosition;
    /// <summary>Velocity in meters per tick.</summary>
    public readonly Vector2[] Velocity;
    /// <summary>Facing angle in radians.</summary>
    public readonly float[] Facing;
    /// <summary>Facing at the start of the tick, for render interpolation; derived, not hashed.</summary>
    public readonly float[] PrevFacing;
    /// <summary>Owning player index.</summary>
    public readonly int[] Owner;
    /// <summary>Unit type id, resolved from data at load.</summary>
    public readonly int[] TypeId;
    /// <summary>Movement speed in meters per tick, copied from the type's <c>UnitDef.SpeedPerTick</c> at spawn.</summary>
    public readonly float[] Speed;
    /// <summary>Collision radius in meters, copied from the type's <c>UnitDef.Radius</c> at spawn.</summary>
    public readonly float[] Radius;
    /// <summary>Current activity.</summary>
    public readonly UnitState[] State;
    /// <summary>Move destination (x, z) in meters; meaningful while <see cref="State"/> is Moving.</summary>
    public readonly Vector2[] Goal;
    /// <summary>
    /// Nav cell index (<c>y * Width + x</c>) whose flow field leads to <see cref="Goal"/>; -1 with no goal.
    /// Kept after arriving, so an arrived Idle unit anchors its group's blob (crowded arrival); set
    /// to -1 when the unit gives up or stops off the map or on blocked ground.
    /// </summary>
    public readonly int[] GoalCell;
    /// <summary>
    /// Tick number on which the unit's current Move applied; the flow-field build pass serves the
    /// oldest orders first (BUG-0022). Meaningful while Moving; arriving or stopping leaves it as is.
    /// </summary>
    public readonly int[] OrderTick;
    /// <summary>
    /// Consecutive ticks a Moving unit walked without progress (see <see cref="BestRemaining"/>); at
    /// <c>MovementConstants.GiveUpTicks</c> it goes Idle. Reset by a Move, a tick with progress, and stopping.
    /// </summary>
    public readonly int[] StuckTicks;
    /// <summary>
    /// Shortest estimated path (meters) left to the goal that the unit has reached on its current
    /// order; a tick makes progress when it beats this by <c>MovementConstants.StuckFraction</c> x
    /// speed. Infinity until the first walking tick, after a Move, and when stopped.
    /// </summary>
    public readonly float[] BestRemaining;
    /// <summary>
    /// Walk-back state on the current order (M1-4d-3): <see cref="WalkBackNone"/>, <see cref="WalkBackUsed"/>,
    /// or pending: a shove carried the unit off its blob and the anchor re-check dropped its goal cell;
    /// the value is <see cref="WalkBackPending"/> plus the ticks since it was last shoved, and it walks
    /// back to <see cref="Goal"/> once that reaches <c>MovementConstants.WalkBackDelayTicks</c>. One
    /// walk-back per order, so a corridor can't bounce a unit back and forth forever. Reset by a new order.
    /// </summary>
    public readonly int[] WalkBack;
    /// <summary>Holding position (<see cref="CommandKind.HoldPosition"/>): never shoved and never walks back. Cleared by the next unqueued order.</summary>
    public readonly bool[] Hold;
    /// <summary>Number of shift-queued orders waiting (0 to <see cref="OrderConstants.QueueCapacity"/>); slot i's queue is entries <c>i * QueueCapacity</c> on.</summary>
    public readonly int[] QueueCount;
    /// <summary>Queued order kinds, <see cref="OrderConstants.QueueCapacity"/> per slot, head first; entries past <see cref="QueueCount"/> are always default.</summary>
    public readonly CommandKind[] QueueKind;
    /// <summary>
    /// Queued order targets (x, z) in meters, parallel to <see cref="QueueKind"/>; zero for Stop and HoldPosition and past
    /// <see cref="QueueCount"/>. Not a point on an <see cref="CommandKind.Attack"/> entry: read that one's target with
    /// <see cref="QueuedTarget"/> (a queued-waypoint overlay must not draw it as a position).
    /// </summary>
    public readonly Vector2[] QueuePosition;
    /// <summary>Queued building types (<see cref="CommandKind.Build"/> entries, M3-3), parallel to <see cref="QueueKind"/>; on an <see cref="CommandKind.Attack"/> entry 1 when its target is a building (<see cref="QueuedTarget"/>); 0 for every other kind and past <see cref="QueueCount"/>.</summary>
    public readonly int[] QueueTypeId;
    /// <summary>The building a worker builds or repairs (M3-3); default when it has no such order.</summary>
    public readonly EntityHandle[] BuildTarget;
    /// <summary>The resource node a worker's gather loop works (M3-2); default when it has no gather order.</summary>
    public readonly EntityHandle[] GatherNode;
    /// <summary>Center (m) of <see cref="GatherNode"/>'s footprint, kept after the node is gone: the depleted-node search starts here.</summary>
    public readonly Vector2[] GatherSite;
    /// <summary>Fraction of the next resource unit gathered so far (0 to 1, from the data rate per tick in reach).</summary>
    public readonly float[] GatherProgress;
    /// <summary>Resources the unit carries (0 to <c>RulesDef.WorkerCarry</c>).</summary>
    public readonly int[] Cargo;
    /// <summary>What <see cref="Cargo"/> is; on a gather loop also the kind of node it works.</summary>
    public readonly ResourceKind[] CargoKind;
    /// <summary>Hit points (M4-1): the type's <c>hp</c> at spawn; the unit dies when a hit takes them to 0. 0 for a slot allocated without a type.</summary>
    public readonly int[] Hp;
    /// <summary>What the unit fights (M4-1): a unit handle, or a building handle when <see cref="TargetIsBuilding"/>; default for none.</summary>
    public readonly EntityHandle[] Target;
    /// <summary>True when <see cref="Target"/> is a building handle.</summary>
    public readonly bool[] TargetIsBuilding;
    /// <summary>Ticks until the unit may start its next attack (M4-1); counts down every tick, target or not.</summary>
    public readonly int[] CooldownTicks;
    /// <summary>Ticks left of the current swing's wind-up (M4-1); the hit lands on the tick it reaches 0. 0 between swings.</summary>
    public readonly int[] WindupTicks;
    /// <summary>The last enemy unit that hit this one (M4-1): first in the target priority, cleared when it dies; default for none.</summary>
    public readonly EntityHandle[] LastAttacker;
    /// <summary>
    /// The engagement's anchor (M4-1, see <see cref="Combat.CombatMode"/>): an attack-move leg's destination, or where an
    /// Idle unit stood when it took a target; zero with <see cref="Combat.CombatMode.None"/>.
    /// </summary>
    public readonly Vector2[] AnchorPosition;
    /// <summary>Why the unit fights (M4-1): none, an attack-move leg, or an Idle unit's leashed retaliation.</summary>
    public readonly Combat.CombatMode[] Mode;
    /// <summary>
    /// The nearest edge-to-edge gap (m) to its target this chase (BUG-0137): set when it takes a target or stands in
    /// reach, lowered when a scan finds it closer; 0 with no target.
    /// </summary>
    public readonly float[] ChaseBest;
    /// <summary>Scans in a row a chaser got no closer than <see cref="ChaseBest"/> (BUG-0137); at <see cref="Combat.CombatConstants.GiveUpScans"/> it gives the target up.</summary>
    public readonly int[] ChaseStall;
    /// <summary>
    /// The last target the unit gave up on (BUG-0137): a unit, or a building with <see cref="IgnoredIsBuilding"/>. Its
    /// scans skip it unless it is in reach, until the unit's next order; default for none.
    /// </summary>
    public readonly EntityHandle[] Ignored;
    /// <summary>True when <see cref="Ignored"/> is a building handle.</summary>
    public readonly bool[] IgnoredIsBuilding;
    /// <summary>
    /// Chases given up since the unit's last order or last landed hit (BUG-0137); at
    /// <see cref="Combat.CombatConstants.MaxGiveUps"/> its scans take only targets in reach.
    /// </summary>
    public readonly int[] GiveUps;
    /// <summary>Whether the slot holds a live unit.</summary>
    public readonly bool[] Alive;

    /// <summary><see cref="WalkBack"/>: the order has not been walked back to yet.</summary>
    public const int WalkBackNone = 0;
    /// <summary><see cref="WalkBack"/>: this order's one walk-back has started.</summary>
    public const int WalkBackUsed = 1;
    /// <summary><see cref="WalkBack"/>: un-anchored by a shove this tick; each later tick without a shove adds one.</summary>
    public const int WalkBackPending = 2;
    /// <summary>Per-slot generation; a handle is valid only while it matches.</summary>
    public readonly int[] Generation;

    private readonly int[] _freeList;
    // Per slot: the half-pop CountPop added for it (M3-4), so Free gives back exactly that; derived, not hashed.
    private readonly int[] _countedHalfPop;
    private readonly Economy.PlayerLedger? _ledger;
    private int _freeCount;

    /// <summary>Creates a store with a fixed number of slots (no population tracking: <see cref="CountPop"/> does nothing).</summary>
    public UnitStore(int capacity) : this(capacity, null)
    {
    }

    /// <summary>A store whose units count toward <paramref name="ledger"/>'s population (the world's store).</summary>
    internal UnitStore(int capacity, Economy.PlayerLedger? ledger)
    {
        _ledger = ledger;
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Position = new Vector2[capacity];
        PrevPosition = new Vector2[capacity];
        Velocity = new Vector2[capacity];
        Facing = new float[capacity];
        PrevFacing = new float[capacity];
        Owner = new int[capacity];
        TypeId = new int[capacity];
        Speed = new float[capacity];
        Radius = new float[capacity];
        State = new UnitState[capacity];
        Goal = new Vector2[capacity];
        GoalCell = new int[capacity];
        OrderTick = new int[capacity];
        StuckTicks = new int[capacity];
        BestRemaining = new float[capacity];
        WalkBack = new int[capacity];
        Hold = new bool[capacity];
        QueueCount = new int[capacity];
        QueueKind = new CommandKind[capacity * OrderConstants.QueueCapacity];
        QueuePosition = new Vector2[capacity * OrderConstants.QueueCapacity];
        QueueTypeId = new int[capacity * OrderConstants.QueueCapacity];
        BuildTarget = new EntityHandle[capacity];
        GatherNode = new EntityHandle[capacity];
        GatherSite = new Vector2[capacity];
        GatherProgress = new float[capacity];
        Cargo = new int[capacity];
        CargoKind = new ResourceKind[capacity];
        Hp = new int[capacity];
        Target = new EntityHandle[capacity];
        TargetIsBuilding = new bool[capacity];
        CooldownTicks = new int[capacity];
        WindupTicks = new int[capacity];
        LastAttacker = new EntityHandle[capacity];
        AnchorPosition = new Vector2[capacity];
        Mode = new Combat.CombatMode[capacity];
        ChaseBest = new float[capacity];
        ChaseStall = new int[capacity];
        Ignored = new EntityHandle[capacity];
        IgnoredIsBuilding = new bool[capacity];
        GiveUps = new int[capacity];
        Alive = new bool[capacity];
        Generation = new int[capacity];
        _freeList = new int[capacity];
        _countedHalfPop = new int[capacity];
        // Push in reverse so the first allocations take slots 0, 1, 2...
        for (int i = 0; i < capacity; i++)
        {
            _freeList[i] = capacity - 1 - i;
            // Generation starts at 1 so default(EntityHandle) never resolves to a live unit.
            Generation[i] = 1;
        }
        _freeCount = capacity;
    }

    /// <summary>Total number of slots.</summary>
    public int Capacity => Alive.Length;

    /// <summary>Number of live units.</summary>
    public int Count => Capacity - _freeCount;

    /// <summary>Number of slots currently on the free list.</summary>
    public int FreeCount => _freeCount;

    /// <summary>Slot index stored at position <paramref name="i"/> of the free list (for state hashing).</summary>
    public int FreeListAt(int i) => _freeList[i];

    /// <summary>Allocates a slot with all fields reset; returns false when the store is full.</summary>
    public bool TryAlloc(out EntityHandle handle)
    {
        if (_freeCount == 0)
        {
            handle = default;
            return false;
        }
        int index = _freeList[--_freeCount];
        Position[index] = default;
        PrevPosition[index] = default;
        Velocity[index] = default;
        Facing[index] = 0f;
        PrevFacing[index] = 0f;
        Owner[index] = 0;
        TypeId[index] = 0;
        Speed[index] = 0f;
        Radius[index] = 0f;
        State[index] = UnitState.Idle;
        Goal[index] = default;
        GoalCell[index] = -1;
        OrderTick[index] = 0;
        StuckTicks[index] = 0;
        BestRemaining[index] = float.PositiveInfinity;
        WalkBack[index] = WalkBackNone;
        Hold[index] = false;
        ClearQueue(index);
        ClearEconomy(index);
        ClearCombat(index);
        Hp[index] = 0;
        _countedHalfPop[index] = 0;
        Alive[index] = true;
        handle = new EntityHandle(index, Generation[index]);
        return true;
    }

    /// <summary>
    /// Allocates a unit of type <paramref name="typeId"/> (<paramref name="def"/>) for <paramref name="owner"/> at <paramref name="position"/> (the dev
    /// <c>SpawnUnit</c> command and production, M3-4): position, owner, type, speed and radius set, the rest as
    /// <see cref="TryAlloc"/> leaves it, and its population counted. False when the store is full.
    /// </summary>
    internal bool TrySpawn(int owner, int typeId, UnitDef def, Vector2 position, out EntityHandle handle)
    {
        if (!TryAlloc(out handle)) return false;
        int i = handle.Index;
        Position[i] = position;
        PrevPosition[i] = position;
        Owner[i] = owner;
        TypeId[i] = typeId;
        Speed[i] = def.SpeedPerTick;
        Radius[i] = def.Radius;
        Hp[i] = def.Hp;
        CountPop(i, def.HalfPop);
        return true;
    }

    /// <summary>Counts <paramref name="halfPop"/> toward the owner's population for live slot <paramref name="index"/>; <see cref="Free"/> gives it back (M3-4).</summary>
    internal void CountPop(int index, int halfPop)
    {
        if (_ledger == null || !_ledger.Has(Owner[index])) return;
        _countedHalfPop[index] += halfPop;
        _ledger.AddHalfPop(Owner[index], halfPop);
    }

    /// <summary>Allocates a slot; throws when the store is full rather than growing.</summary>
    public EntityHandle Alloc()
    {
        if (!TryAlloc(out EntityHandle handle))
            throw new InvalidOperationException($"UnitStore is full (capacity {Capacity}).");
        return handle;
    }

    /// <summary>Whether the handle still refers to a live unit.</summary>
    public bool IsAlive(EntityHandle handle)
    {
        return (uint)handle.Index < (uint)Capacity
            && Alive[handle.Index]
            && Generation[handle.Index] == handle.Generation;
    }

    /// <summary>Frees the unit's slot and invalidates every handle to it, releasing the population it counted (M3-4); throws on a stale handle.</summary>
    public void Free(EntityHandle handle)
    {
        if (!IsAlive(handle))
            throw new ArgumentException($"Handle {handle} is not alive.", nameof(handle));
        if (_countedHalfPop[handle.Index] != 0)
        {
            _ledger?.AddHalfPop(Owner[handle.Index], -_countedHalfPop[handle.Index]);
            _countedHalfPop[handle.Index] = 0;
        }
        Alive[handle.Index] = false;
        Hold[handle.Index] = false;
        ClearQueue(handle.Index);
        ClearEconomy(handle.Index);
        ClearCombat(handle.Index);
        Hp[handle.Index] = 0;
        // A freed slot stands still: since units die (M4-1), a scan over slots must not count a dead walker as Moving.
        State[handle.Index] = UnitState.Idle;
        Velocity[handle.Index] = default;
        Generation[handle.Index]++;
        _freeList[_freeCount++] = handle.Index;
    }

    /// <summary>
    /// The target of queue entry <paramref name="entry"/> (an index into the flat queue arrays), an <see cref="CommandKind.Attack"/>
    /// entry (M4-2a): an Attack has no target point, so its <see cref="QueuePosition"/> holds the target's slot (x) and
    /// generation (y) as whole numbers, exact in a float below 2^24 (a generation counts frees of one slot: a 24-hour
    /// match can't reach that), and its <see cref="QueueTypeId"/> is 1 for a building. Kept in the existing arrays, not a
    /// new one, so the queue costs no more memory (a 1,024-cell world with 4,096 slots stays inside its 228 MB bound).
    /// </summary>
    public EntityHandle QueuedTarget(int entry) => new((int)QueuePosition[entry].X, (int)QueuePosition[entry].Y);

    /// <summary>Writes <see cref="QueuedTarget"/> for entry <paramref name="entry"/>.</summary>
    internal void SetQueuedTarget(int entry, EntityHandle target, bool isBuilding)
    {
        QueuePosition[entry] = new Vector2(target.Index, target.Generation);
        QueueTypeId[entry] = isBuilding ? 1 : 0;
    }

    /// <summary>Drops every queued order of slot <paramref name="index"/>, resetting all its entries so unused ones stay default.</summary>
    internal void ClearQueue(int index)
    {
        int start = index * OrderConstants.QueueCapacity;
        Array.Clear(QueueKind, start, OrderConstants.QueueCapacity);
        Array.Clear(QueuePosition, start, OrderConstants.QueueCapacity);
        Array.Clear(QueueTypeId, start, OrderConstants.QueueCapacity);
        QueueCount[index] = 0;
    }

    /// <summary>Resets slot <paramref name="index"/>'s gather loop and cargo (M3-2) and build target (M3-3) to the empty state.</summary>
    private void ClearEconomy(int index)
    {
        BuildTarget[index] = default;
        GatherNode[index] = default;
        GatherSite[index] = default;
        GatherProgress[index] = 0f;
        Cargo[index] = 0;
        CargoKind[index] = default;
    }

    /// <summary>Resets slot <paramref name="index"/>'s combat state (M4-1), hit points aside, to the empty state.</summary>
    private void ClearCombat(int index)
    {
        Target[index] = default;
        TargetIsBuilding[index] = false;
        CooldownTicks[index] = 0;
        WindupTicks[index] = 0;
        LastAttacker[index] = default;
        AnchorPosition[index] = default;
        Mode[index] = Combat.CombatMode.None;
        ChaseBest[index] = 0f;
        ChaseStall[index] = 0;
        Ignored[index] = default;
        IgnoredIsBuilding[index] = false;
        GiveUps[index] = 0;
    }

    /// <summary>
    /// True when slot <paramref name="index"/> stands its ground: holding position, or Attacking (M4-1). Movement never
    /// shoves such a unit and treats it as a hard wall, and the placement rule never pushes it out of a footprint.
    /// </summary>
    public bool IsPlanted(int index) => Hold[index] || State[index] == UnitState.Attacking;

    /// <summary>Copies Position into PrevPosition and Facing into PrevFacing for every live unit (start of tick).</summary>
    public void SnapshotPrevPositions()
    {
        for (int i = 0; i < Alive.Length; i++)
        {
            if (!Alive[i]) continue;
            PrevPosition[i] = Position[i];
            PrevFacing[i] = Facing[i];
        }
    }
}
