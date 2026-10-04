using System;
using System.Numerics;

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
    /// <summary>Nav cell index (<c>y * Width + x</c>) whose flow field leads to <see cref="Goal"/>; -1 with no goal.</summary>
    public readonly int[] GoalCell;
    /// <summary>Whether the slot holds a live unit.</summary>
    public readonly bool[] Alive;
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
        Owner = new int[capacity];
        TypeId = new int[capacity];
        Speed = new float[capacity];
        Radius = new float[capacity];
        State = new UnitState[capacity];
        Goal = new Vector2[capacity];
        GoalCell = new int[capacity];
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
        Owner[index] = 0;
        TypeId[index] = 0;
        Speed[index] = 0f;
        Radius[index] = 0f;
        State[index] = UnitState.Idle;
        Goal[index] = default;
        GoalCell[index] = -1;
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
        Generation[handle.Index]++;
        _freeList[_freeCount++] = handle.Index;
    }

    /// <summary>Copies Position into PrevPosition for every live unit (start of tick).</summary>
    public void SnapshotPrevPositions()
    {
        for (int i = 0; i < Alive.Length; i++)
        {
            if (Alive[i])
                PrevPosition[i] = Position[i];
        }
    }
}
