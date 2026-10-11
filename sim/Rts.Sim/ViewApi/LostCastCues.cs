using System;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>
/// The lost-cast cues the view shows (M5-V1, docs/03 "Implementation (M5-V1)"): one per <see cref="AbilityEvent"/> with
/// <c>Dropped</c> true (M4-H3: a cast lost without a new order) whose caster belongs to the watching player, for
/// <see cref="LifetimeTicks"/> (0.5 s of game time). A start, a resolve and a cast the player cancelled with a new order
/// (which records no event) add nothing. A fixed pool used as a ring like <see cref="ResolveFlashes"/>: each cue takes the
/// next slot, replacing the cue there if it is still live. A cue is kept until a frame has drawn it (<see cref="MarkDrawn"/>),
/// and its fade runs from that frame, so every drop shows once even when frames lag the ticks. Presentation only; reads the
/// event list it is given, writes nothing back; allocation-free after construction.
/// </summary>
public sealed class LostCastCues
{
    /// <summary>Cues kept at most (builder's choice: more than a dozen selected casters all losing their casts on one tick).</summary>
    public const int DefaultCapacity = 32;

    /// <summary>A cue lasts 0.5 s of game time (M5-V1 brief: "a short red flash (0.5 s)").</summary>
    public const int LifetimeTicks = 10;

    private readonly bool[] _active, _drawn;
    private readonly EntityHandle[] _caster;
    private readonly int[] _ability;
    private readonly Vector2[] _point;
    private readonly long[] _start, _origin;
    private int _head;
    private long _lastCollected = long.MinValue;

    /// <summary>Creates a pool of <paramref name="capacity"/> cues.</summary>
    public LostCastCues(int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _active = new bool[capacity];
        _drawn = new bool[capacity];
        _caster = new EntityHandle[capacity];
        _ability = new int[capacity];
        _point = new Vector2[capacity];
        _start = new long[capacity];
        _origin = new long[capacity];
    }

    /// <summary>Cues the pool holds at most.</summary>
    public int Capacity => _active.Length;

    /// <summary>Cues in use.</summary>
    public int Count { get; private set; }

    /// <summary>Cues ever added: one per own dropped cast collected.</summary>
    public long Added { get; private set; }

    /// <summary>Cues that replaced a live one.</summary>
    public long Replaced { get; private set; }

    /// <summary>True while cue slot i is in use.</summary>
    public ReadOnlySpan<bool> Active => _active;

    /// <summary>True once a frame has reached cue slot i (<see cref="MarkDrawn"/>): its fade has started.</summary>
    public ReadOnlySpan<bool> Drawn => _drawn;

    /// <summary>The caster whose cast was lost, per cue (the cue is drawn on it while the handle is alive).</summary>
    public ReadOnlySpan<EntityHandle> Caster => _caster;

    /// <summary>The lost ability of each cue (index into <c>GameData.Abilities</c>).</summary>
    public ReadOnlySpan<int> Ability => _ability;

    /// <summary>The lost cast's point (m), per cue.</summary>
    public ReadOnlySpan<Vector2> Point => _point;

    /// <summary>The tick number each cue was added on.</summary>
    public ReadOnlySpan<long> StartTick => _start;

    /// <summary>
    /// Adds a cue for every dropped event of tick number <paramref name="tick"/> whose owner is <paramref name="player"/>
    /// (the sim's <c>AbilityEvents</c>, read between ticks), once: a second call for the same tick adds nothing. Returns the
    /// number added.
    /// </summary>
    public int Collect(ReadOnlySpan<AbilityEvent> events, long tick, int player)
    {
        if (tick == _lastCollected) return 0;
        _lastCollected = tick;
        int n = 0;
        for (int i = 0; i < events.Length; i++)
        {
            AbilityEvent e = events[i];
            if (!e.Dropped || e.Resolved || e.Owner != player) continue;
            Add(e.Caster, e.Ability, e.Point, tick);
            n++;
        }
        return n;
    }

    /// <summary>Adds one cue at the next ring slot (replacing a live one there); returns its slot.</summary>
    public int Add(EntityHandle caster, int ability, Vector2 point, long tick)
    {
        int slot = _head;
        _head = _head + 1 == _active.Length ? 0 : _head + 1;
        if (_active[slot]) Replaced++;
        else Count++;
        _active[slot] = true;
        _drawn[slot] = false;
        _caster[slot] = caster;
        _ability[slot] = ability;
        _point[slot] = point;
        _start[slot] = tick;
        Added++;
        return slot;
    }

    /// <summary>Records that a frame at tick <paramref name="tick"/> reached cue <paramref name="slot"/>: its fade runs from one tick before this frame at most (the <see cref="ResolveFlashes.MarkDrawn"/> rule). Later calls change nothing.</summary>
    public void MarkDrawn(int slot, long tick)
    {
        if ((uint)slot >= (uint)_drawn.Length || !_active[slot] || _drawn[slot]) return;
        _drawn[slot] = true;
        _origin[slot] = Math.Max(_start[slot], tick - 1);
    }

    /// <summary>How far through its life cue <paramref name="slot"/> is at tick <paramref name="tick"/> plus render <paramref name="alpha"/>, in [0, 1].</summary>
    public float Age(int slot, long tick, float alpha)
    {
        float a = alpha > 0f ? MathF.Min(alpha, 1f) : 0f;
        long start = _drawn[slot] ? _origin[slot] : Math.Max(_start[slot], tick - 1);
        float t = ((tick - start) + a) / LifetimeTicks;
        return t > 0f ? MathF.Min(t, 1f) : 0f;
    }

    /// <summary>Removes every drawn cue whose time is up at tick <paramref name="tick"/>; writes their slots to <paramref name="removed"/> (as many as fit) and returns how many were removed.</summary>
    public int Expire(long tick, Span<int> removed)
    {
        int n = 0;
        for (int i = 0; i < _active.Length; i++)
        {
            if (!_active[i] || !_drawn[i] || _origin[i] + LifetimeTicks > tick) continue;
            _active[i] = false;
            _drawn[i] = false;
            Count--;
            if (n < removed.Length) removed[n] = i;
            n++;
        }
        return n;
    }
}
