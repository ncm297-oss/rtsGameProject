using System;
using System.Numerics;
using Rts.Sim.Abilities;

namespace Rts.Sim.ViewApi;

/// <summary>
/// The ground bursts the view shows where an ability resolved (M4-V6b, docs/03 "Implementation (M4-V6b)"): one per
/// <see cref="AbilityEvent"/> with <c>Resolved</c> true, at its point, for <see cref="LifetimeTicks"/> (0.5 s of game time).
/// A cast start adds nothing. A fixed pool used as a ring like <see cref="ImpactMarks"/>: each flash takes the next slot,
/// replacing the flash there if it is still live (the oldest added), so a storm of resolves never grows it. A flash that
/// ran out of time before any frame drew it is kept until one has, so every resolve shows at least once. The first frame
/// that reaches a flash decides it once (M4-VH2, BUG-0371): whether it is shown at all (a resolve the fog hid then is never
/// drawn later, even if its cell comes into sight mid-fade) and where its age starts (a flash first drawn several ticks
/// late runs its whole fade from that frame, with no jump). Presentation only; allocation-free after construction.
/// </summary>
public sealed class ResolveFlashes
{
    /// <summary>Flashes kept at most (builder's choice: past the 200 resolves of QA's one-tick storm).</summary>
    public const int DefaultCapacity = 256;

    /// <summary>A flash lasts 0.5 s of game time (M4-V6b brief: "fades within ~0.5 s").</summary>
    public const int LifetimeTicks = 10;

    private readonly bool[] _active, _drawn, _shown;
    private readonly int[] _ability, _owner;
    private readonly Vector2[] _point;
    private readonly long[] _start, _origin;
    private int _head;
    private long _lastCollected = long.MinValue;

    /// <summary>Creates a pool of <paramref name="capacity"/> flashes.</summary>
    public ResolveFlashes(int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _active = new bool[capacity];
        _drawn = new bool[capacity];
        _shown = new bool[capacity];
        _ability = new int[capacity];
        _owner = new int[capacity];
        _point = new Vector2[capacity];
        _start = new long[capacity];
        _origin = new long[capacity];
    }

    /// <summary>Flashes the pool holds at most.</summary>
    public int Capacity => _active.Length;

    /// <summary>Flashes in use.</summary>
    public int Count { get; private set; }

    /// <summary>Flashes ever added.</summary>
    public long Added { get; private set; }

    /// <summary>Flashes that replaced a live one.</summary>
    public long Replaced { get; private set; }

    /// <summary>Flashes removed because their time ran out.</summary>
    public long Expired { get; private set; }

    /// <summary>True while flash slot i is in use.</summary>
    public ReadOnlySpan<bool> Active => _active;

    /// <summary>The resolved ability of each flash (index into <c>GameData.Abilities</c>).</summary>
    public ReadOnlySpan<int> Ability => _ability;

    /// <summary>The caster's owner of each flash.</summary>
    public ReadOnlySpan<int> Owner => _owner;

    /// <summary>Where each flash is (m): the cast point.</summary>
    public ReadOnlySpan<Vector2> Point => _point;

    /// <summary>The tick number each flash was added on.</summary>
    public ReadOnlySpan<long> StartTick => _start;

    /// <summary>True once a frame has reached flash slot i (<see cref="MarkDrawn"/>): its <see cref="Shown"/> and age origin are fixed.</summary>
    public ReadOnlySpan<bool> Drawn => _drawn;

    /// <summary>Whether flash slot i is drawn, as its first frame decided (false: the fog hid its point then; it expires unseen).</summary>
    public ReadOnlySpan<bool> Shown => _shown;

    /// <summary>
    /// Adds a flash for every resolved event of tick number <paramref name="tick"/> (the sim's <c>AbilityEvents</c>, read
    /// between ticks), once: a second call for the same tick adds nothing. Returns the number added.
    /// </summary>
    public int Collect(ReadOnlySpan<AbilityEvent> events, long tick)
    {
        if (tick == _lastCollected) return 0;
        _lastCollected = tick;
        int n = 0;
        for (int i = 0; i < events.Length; i++)
        {
            if (!events[i].Resolved) continue;
            Add(events[i].Ability, events[i].Owner, events[i].Point, tick);
            n++;
        }
        return n;
    }

    /// <summary>Adds one flash at the next ring slot (replacing a live one there); returns its slot.</summary>
    public int Add(int ability, int owner, Vector2 point, long tick)
    {
        int slot = _head;
        _head = _head + 1 == _active.Length ? 0 : _head + 1;
        if (_active[slot]) Replaced++;
        else Count++;
        _active[slot] = true;
        _drawn[slot] = false;
        _shown[slot] = false;
        _ability[slot] = ability;
        _owner[slot] = owner;
        _point[slot] = point;
        _start[slot] = tick;
        Added++;
        return slot;
    }

    /// <summary>
    /// Records that a frame at tick <paramref name="tick"/> reached flash <paramref name="slot"/>, so it can expire. The first
    /// call decides it: <paramref name="shown"/> (false when the fog hid its point) is kept for its whole life, and its age
    /// runs from one tick before this frame at most (<see cref="Age"/>). Later calls change nothing.
    /// </summary>
    public void MarkDrawn(int slot, long tick, bool shown = true)
    {
        if ((uint)slot >= (uint)_drawn.Length || !_active[slot] || _drawn[slot]) return;
        _drawn[slot] = true;
        _shown[slot] = shown;
        _origin[slot] = Math.Max(_start[slot], tick - 1);
    }

    /// <summary>How far through its life flash <paramref name="slot"/> is at tick <paramref name="tick"/> plus render <paramref name="alpha"/>, in [0, 1]: from one tick before its first frame at most (the <see cref="ImpactMarks.Age"/> rule, then kept, BUG-0371), so a late first draw starts near 0 and runs on without a jump.</summary>
    public float Age(int slot, long tick, float alpha)
    {
        float a = alpha > 0f ? MathF.Min(alpha, 1f) : 0f;
        long start = _drawn[slot] ? _origin[slot] : Math.Max(_start[slot], tick - 1);
        float t = ((tick - start) + a) / LifetimeTicks;
        return t > 0f ? MathF.Min(t, 1f) : 0f;
    }

    /// <summary>Removes every drawn flash whose time (from its age origin) is up at tick <paramref name="tick"/>; writes their slots to <paramref name="removed"/> (as many as fit) and returns how many were removed.</summary>
    public int Expire(long tick, Span<int> removed)
    {
        int n = 0;
        for (int i = 0; i < _active.Length; i++)
        {
            if (!_active[i] || !_drawn[i] || _origin[i] + LifetimeTicks > tick) continue;
            _active[i] = false;
            _drawn[i] = false;
            _shown[i] = false;
            Count--;
            Expired++;
            if (n < removed.Length) removed[n] = i;
            n++;
        }
        return n;
    }
}
