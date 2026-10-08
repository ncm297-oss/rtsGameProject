using System;
using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Data;

namespace Rts.Sim.ViewApi;

/// <summary>
/// The short marks the view shows where projectiles land (M4-V3): one per <see cref="ProjectileImpact"/>, a
/// <see cref="ImpactMarkKind.Flash"/> for an aimed hit (<see cref="FlashTicks"/>, 0.2 s), a <see cref="ImpactMarkKind.Dust"/>
/// puff for an aimed miss (<see cref="DustTicks"/>, 0.3 s) and a <see cref="ImpactMarkKind.Burst"/> for a lob
/// (<see cref="BurstTicks"/>, 0.4 s), timed in sim ticks so game speed scales them. A fixed pool used as a ring like
/// <see cref="DeathMarkers"/>: when every mark is in use the next one replaces the oldest. A mark that ran out of time
/// before any frame drew it (several ticks in one frame at 8x) is kept until one has, so every landing shows at least once.
/// Presentation only; allocation-free after construction.
/// </summary>
public sealed class ImpactMarks
{
    /// <summary>Marks kept at most (Producer default, M4-V3).</summary>
    public const int DefaultCapacity = 512;

    /// <summary>A hit's flash lasts 0.2 s of game time (M4-V3 brief).</summary>
    public const int FlashTicks = 4;

    /// <summary>A miss's dust puff lasts 0.3 s of game time (M4-V3 brief).</summary>
    public const int DustTicks = 6;

    /// <summary>A lob's burst lasts 0.4 s of game time (builder's choice; the brief names no time).</summary>
    public const int BurstTicks = 8;

    private readonly bool[] _active, _drawn;
    private readonly ImpactMarkKind[] _kind;
    private readonly Vector2[] _position;
    private readonly int[] _type, _owner;
    private readonly long[] _start, _expires;
    private int _head;
    private long _lastCollected = long.MinValue;

    /// <summary>Creates a pool of <paramref name="capacity"/> marks.</summary>
    public ImpactMarks(int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _active = new bool[capacity];
        _drawn = new bool[capacity];
        _kind = new ImpactMarkKind[capacity];
        _position = new Vector2[capacity];
        _type = new int[capacity];
        _owner = new int[capacity];
        _start = new long[capacity];
        _expires = new long[capacity];
    }

    /// <summary>Marks the pool holds at most.</summary>
    public int Capacity => _active.Length;

    /// <summary>Marks in use.</summary>
    public int Count { get; private set; }

    /// <summary>Marks ever added.</summary>
    public long Added { get; private set; }

    /// <summary>Marks that replaced a live one because the pool was full.</summary>
    public long Replaced { get; private set; }

    /// <summary>Marks removed because their time ran out.</summary>
    public long Expired { get; private set; }

    /// <summary>The tick of the last <see cref="Collect"/> that took a tick's impacts (<see cref="long.MinValue"/> before any).</summary>
    public long LastCollectedTick => _lastCollected;

    /// <summary>True while mark slot i is in use.</summary>
    public ReadOnlySpan<bool> Active => _active;

    /// <summary>True once a frame has drawn mark slot i (it can expire from then on).</summary>
    public ReadOnlySpan<bool> Drawn => _drawn;

    /// <summary>What each mark shows.</summary>
    public ReadOnlySpan<ImpactMarkKind> Kind => _kind;

    /// <summary>Where each mark is (m): the impact point.</summary>
    public ReadOnlySpan<Vector2> Position => _position;

    /// <summary>The projectile type that landed (<see cref="GameData.Projectiles"/>).</summary>
    public ReadOnlySpan<int> ProjectileType => _type;

    /// <summary>The player who fired it.</summary>
    public ReadOnlySpan<int> Owner => _owner;

    /// <summary>The tick number each mark was added on.</summary>
    public ReadOnlySpan<long> StartTick => _start;

    /// <summary>The tick number from which each mark may go (once drawn).</summary>
    public ReadOnlySpan<long> ExpiresAt => _expires;

    /// <summary>Lifetime in ticks of a mark of <paramref name="kind"/>.</summary>
    public static int LifetimeTicks(ImpactMarkKind kind) => kind switch
    {
        ImpactMarkKind.Flash => FlashTicks,
        ImpactMarkKind.Dust => DustTicks,
        ImpactMarkKind.Burst => BurstTicks,
        _ => 0,
    };

    /// <summary>The mark an impact makes: a lob bursts, an aimed hit flashes, an aimed miss puffs dust (an unknown type counts as aimed).</summary>
    public static ImpactMarkKind KindOf(in ProjectileImpact impact, ReadOnlySpan<ProjectileDef> defs)
    {
        int t = impact.ProjectileTypeId;
        if ((uint)t < (uint)defs.Length && defs[t].Kind == ProjectileKind.Lob) return ImpactMarkKind.Burst;
        return impact.Hit ? ImpactMarkKind.Flash : ImpactMarkKind.Dust;
    }

    /// <summary>Adds one mark for <paramref name="impact"/> seen at tick <paramref name="tick"/>; returns its slot (the oldest mark's when the pool is full).</summary>
    public int Add(in ProjectileImpact impact, ImpactMarkKind kind, long tick)
    {
        int slot = _head;
        _head = _head + 1 == _active.Length ? 0 : _head + 1;
        if (_active[slot]) Replaced++;
        else Count++;
        _active[slot] = true;
        _drawn[slot] = false;
        _kind[slot] = kind;
        _position[slot] = impact.Position;
        _type[slot] = impact.ProjectileTypeId;
        _owner[slot] = impact.Owner;
        _start[slot] = tick;
        _expires[slot] = tick + LifetimeTicks(kind);
        Added++;
        return slot;
    }

    /// <summary>
    /// Adds a mark for every impact of tick number <paramref name="tick"/> (the sim's <c>Impacts</c>, read between ticks),
    /// once: a second call for the same tick adds nothing. The slots written go to <paramref name="added"/> (as many as
    /// fit). Returns the number of impacts taken.
    /// </summary>
    public int Collect(ReadOnlySpan<ProjectileImpact> impacts, ReadOnlySpan<ProjectileDef> defs, long tick, Span<int> added)
    {
        if (tick == _lastCollected) return 0;
        _lastCollected = tick;
        for (int i = 0; i < impacts.Length; i++)
        {
            int slot = Add(impacts[i], KindOf(impacts[i], defs), tick);
            if (i < added.Length) added[i] = slot;
        }
        return impacts.Length;
    }

    /// <summary>Records that a frame drew mark <paramref name="slot"/>.</summary>
    public void MarkDrawn(int slot)
    {
        if ((uint)slot < (uint)_drawn.Length && _active[slot]) _drawn[slot] = true;
    }

    /// <summary>How far through its life mark <paramref name="slot"/> is at tick <paramref name="tick"/> plus render <paramref name="alpha"/>, in [0, 1].</summary>
    public float Age(int slot, long tick, float alpha)
    {
        int life = LifetimeTicks(_kind[slot]);
        if (life <= 0) return 1f;
        float a = alpha > 0f ? MathF.Min(alpha, 1f) : 0f;
        float t = ((tick - _start[slot]) + a) / life;
        return t > 0f ? MathF.Min(t, 1f) : 0f;
    }

    /// <summary>Removes every drawn mark whose time is up at tick <paramref name="tick"/>; writes their slots to <paramref name="removed"/> (as many as fit) and returns how many were removed.</summary>
    public int Expire(long tick, Span<int> removed)
    {
        int n = 0;
        for (int i = 0; i < _active.Length; i++)
        {
            if (!_active[i] || !_drawn[i] || _expires[i] > tick) continue;
            _active[i] = false;
            _drawn[i] = false;
            _kind[i] = ImpactMarkKind.None;
            Count--;
            Expired++;
            if (n < removed.Length) removed[n] = i;
            n++;
        }
        return n;
    }
}
