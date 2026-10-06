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
    /// <summary>Queued order targets (x, z) in meters, parallel to <see cref="QueueKind"/>; zero for Stop and HoldPosition and past <see cref="QueueCount"/>.</summary>
    public readonly Vector2[] QueuePosition;
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
    private int _freeCount;

    /// <summary>Creates a store with a fixed number of slots.</summary>
    public UnitStore(int capacity)
    {
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
        GatherNode = new EntityHandle[capacity];
        GatherSite = new Vector2[capacity];
        GatherProgress = new float[capacity];
        Cargo = new int[capacity];
        CargoKind = new ResourceKind[capacity];
        Alive = new bool[capacity];
        Generation = new int[capacity];
        _freeList = new int[capacity];
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
        Alive[index] = true;
        handle = new EntityHandle(index, Generation[index]);
        return true;
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

    /// <summary>Frees the unit's slot and invalidates every handle to it; throws on a stale handle.</summary>
    public void Free(EntityHandle handle)
    {
        if (!IsAlive(handle))
            throw new ArgumentException($"Handle {handle} is not alive.", nameof(handle));
        Alive[handle.Index] = false;
        Hold[handle.Index] = false;
        ClearQueue(handle.Index);
        ClearEconomy(handle.Index);
        Generation[handle.Index]++;
        _freeList[_freeCount++] = handle.Index;
    }

    /// <summary>Drops every queued order of slot <paramref name="index"/>, resetting all its entries so unused ones stay default.</summary>
    internal void ClearQueue(int index)
    {
        int start = index * OrderConstants.QueueCapacity;
        Array.Clear(QueueKind, start, OrderConstants.QueueCapacity);
        Array.Clear(QueuePosition, start, OrderConstants.QueueCapacity);
        QueueCount[index] = 0;
    }

    /// <summary>Resets slot <paramref name="index"/>'s gather loop and cargo (M3-2) to the empty state.</summary>
    private void ClearEconomy(int index)
    {
        GatherNode[index] = default;
        GatherSite[index] = default;
        GatherProgress[index] = 0f;
        Cargo[index] = 0;
        CargoKind[index] = default;
    }

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
