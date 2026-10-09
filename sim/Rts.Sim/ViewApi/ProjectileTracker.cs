using System;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>
/// The view's memory of each projectile slot's flight (M4-V3, docs/03 "Implementation (M4-V3)"): the launch point, the
/// flight length and, for a lob, the span its drawn arc covers, recorded the first time a slot is seen alive and dropped
/// when it dies. View state only: it reads <see cref="ProjectileStore"/>'s spans and never writes the sim. Fixed arrays
/// sized at construction, so <see cref="Observe"/> allocates nothing.
/// </summary>
/// <remarks>
/// <para>The sim keeps no launch point (a projectile is a position and a step), so a lob's progress along its arc needs
/// this. On the tick a shot is fired <c>PrevPosition == Position ==</c> the launch point, and one tick later
/// <c>PrevPosition</c> is still the launch point, so a slot first seen within a tick of its firing gets its exact launch
/// point; one first seen later starts its arc where it is (no harm: it is drawn from there).</para>
/// <para>A projectile slot is freed in phase 11 and can be taken again in phase 10 of the next tick, so an observer that
/// runs every tick always sees it dead in between. One that skips ticks (a test that ticks the sim itself, or a frame
/// loop) may see a different shot in a slot it still tracks: that shows as a position jump, so a tracked slot starts
/// over when it stands still (a firing tick), changes type or owner, a lob's impact point moves (a lob never re-aims),
/// a lob is off the line from its recorded launch point to its impact point (a lob flies straight, so a same-target lob
/// fired from elsewhere shows here, BUG-0222), its last position is not where it was on the previous tick, or it is
/// nearer its launch point than before.</para>
/// </remarks>
public sealed class ProjectileTracker
{
    /// <summary>A lob's drawn arc peaks at this share of its flight length (a placeholder curve until M6).</summary>
    public const float ApexPerMeter = 0.25f;

    /// <summary>The lowest and highest arc peaks in meters, so a short Sapper throw still reads as a lob and a long Catapult shot stays on screen.</summary>
    public const float MinApex = 1f, MaxApex = 6f;

    // Slack for "nearer its launch point than before" (float rounding of a straight flight).
    private const float BackwardSlack = 1e-3f;

    // Slack (m) for "a lob is on the line from its launch to its impact point": float rounding of launch + j x step over a
    // long flight stays far under a centimetre, and a shot from another launch point is metres off.
    private const float OffLineSlack = 1e-2f;

    private readonly bool[] _tracked, _lob;
    private readonly Vector2[] _launch, _target, _lastSeen;
    private readonly float[] _length, _arcLength, _lastFlown;
    private readonly int[] _type, _owner;
    private readonly long[] _lastTick;

    /// <summary>Creates a tracker for a store of <paramref name="capacity"/> slots (<see cref="ProjectileStore.Capacity"/>).</summary>
    public ProjectileTracker(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _tracked = new bool[capacity];
        _lob = new bool[capacity];
        _launch = new Vector2[capacity];
        _target = new Vector2[capacity];
        _lastSeen = new Vector2[capacity];
        _length = new float[capacity];
        _arcLength = new float[capacity];
        _lastFlown = new float[capacity];
        _type = new int[capacity];
        _owner = new int[capacity];
        _lastTick = new long[capacity];
    }

    /// <summary>Slots tracked at most (the store's capacity).</summary>
    public int Capacity => _tracked.Length;

    /// <summary>Slots tracked now (live shots seen at the last <see cref="Observe"/>).</summary>
    public int Count { get; private set; }

    /// <summary>Shots seen start so far (each one's launch recorded).</summary>
    public long Started { get; private set; }

    /// <summary>Of <see cref="Started"/>, the ones found in a slot still tracked for an earlier shot (the slot was reused between two observations).</summary>
    public long Reused { get; private set; }

    /// <summary>Tracked slots dropped because their shot was gone.</summary>
    public long Dropped { get; private set; }

    /// <summary>The tick number of the last <see cref="Observe"/> that ran (<see cref="long.MinValue"/> before any).</summary>
    public long LastObservedTick { get; private set; } = long.MinValue;

    /// <summary>Per slot: a live shot is tracked.</summary>
    public ReadOnlySpan<bool> Tracked => _tracked;

    /// <summary>Per slot: the launch point (m) of the tracked shot.</summary>
    public ReadOnlySpan<Vector2> Launch => _launch;

    /// <summary>Per slot: the flight length (m), launch point to impact point, when the shot was first seen.</summary>
    public ReadOnlySpan<float> Length => _length;

    /// <summary>Per slot: the tracked shot is a lob (drawn on an arc).</summary>
    public ReadOnlySpan<bool> IsLob => _lob;

    /// <summary>
    /// Brings the tracker up to the store's state at tick <paramref name="tick"/>: a live slot not tracked (or holding a new
    /// shot) records its launch, a dead slot is dropped. Once per tick: a second call for the same tick does nothing, so a
    /// per-tick hook and the frame loop may both call it. No allocation.
    /// </summary>
    public void Observe(ProjectileStore store, ReadOnlySpan<ProjectileDef> defs, long tick)
    {
        if (store.Capacity != Capacity) throw new ArgumentException($"store has {store.Capacity} slots, tracker {Capacity}", nameof(store));
        if (tick == LastObservedTick) return;
        ReadOnlySpan<bool> alive = store.Alive;
        ReadOnlySpan<Vector2> position = store.Position, prev = store.PrevPosition, target = store.Target;
        ReadOnlySpan<int> type = store.ProjectileTypeId, owner = store.Owner;
        for (int i = 0; i < alive.Length; i++)
        {
            if (!alive[i])
            {
                if (_tracked[i])
                {
                    _tracked[i] = false;
                    Count--;
                    Dropped++;
                }
                continue;
            }
            Vector2 pos = position[i];
            bool fresh = !_tracked[i]
                || pos == prev[i]
                || type[i] != _type[i]
                || owner[i] != _owner[i]
                || (_lob[i] && target[i] != _target[i])
                || (_lob[i] && OffLine(_launch[i], _target[i], pos))
                || (_lastTick[i] == tick - 1 && prev[i] != _lastSeen[i])
                || Vector2.Distance(pos, _launch[i]) < _lastFlown[i] - BackwardSlack;
            if (fresh)
            {
                if (_tracked[i]) Reused++;
                else Count++;
                Start(i, prev[i], target[i], type[i], owner[i], defs);
            }
            _lastSeen[i] = pos;
            _lastTick[i] = tick;
            _lastFlown[i] = Vector2.Distance(pos, _launch[i]);
        }
        LastObservedTick = tick;
    }

    // Distance of p from the line through a and b is over OffLineSlack (a and b equal: from a).
    private static bool OffLine(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 d = b - a, r = p - a;
        float len = d.Length();
        if (!(len > OffLineSlack)) return r.Length() > OffLineSlack;
        return MathF.Abs(d.X * r.Y - d.Y * r.X) / len > OffLineSlack;
    }

    private void Start(int i, Vector2 launch, Vector2 target, int type, int owner, ReadOnlySpan<ProjectileDef> defs)
    {
        _tracked[i] = true;
        _launch[i] = launch;
        _target[i] = target;
        _type[i] = type;
        _owner[i] = owner;
        float length = Vector2.Distance(launch, target);
        _length[i] = length;
        ProjectileDef? def = (uint)type < (uint)defs.Length ? defs[type] : null;
        _lob[i] = def != null && def.Kind == ProjectileKind.Lob;
        // Positions launch + j x step are drawn for j = 0 .. n-1 (it lands on tick n and is freed that tick). The arc ends one
        // step early, so the last drawn tick is on the ground at any alpha, and the burst follows a step on.
        int n = def != null && def.SpeedPerTick > 0f ? ProjectileStore.FlightTicks(length, def.SpeedPerTick) : 1;
        _arcLength[i] = _lob[i] ? Math.Max(0, n - 2) * def!.SpeedPerTick : 0f;
        Started++;
    }

    /// <summary>
    /// How far along its drawn arc slot <paramref name="slot"/>'s shot is when drawn at <paramref name="at"/>: 0 at the
    /// launch point, 1 at the end of the arc (and on the ground from there); 1 for a lob too short to arc; 0 for a slot not
    /// tracked or not a lob.
    /// </summary>
    public float Progress(int slot, Vector2 at)
    {
        if ((uint)slot >= (uint)_tracked.Length || !_tracked[slot] || !_lob[slot]) return 0f;
        float span = _arcLength[slot];
        if (!(span > 0f)) return 1f;
        float t = Vector2.Distance(at, _launch[slot]) / span;
        return t > 0f ? MathF.Min(t, 1f) : 0f; // NaN to 0
    }

    /// <summary>Height (m) of slot <paramref name="slot"/>'s drawn arc above its ground line at <paramref name="at"/>: <c>apex x 4t(1-t)</c>; 0 for an aimed shot or an untracked slot.</summary>
    public float ArcHeight(int slot, Vector2 at)
    {
        if ((uint)slot >= (uint)_tracked.Length || !_tracked[slot] || !_lob[slot]) return 0f;
        float t = Progress(slot, at);
        return Apex(_length[slot]) * 4f * t * (1f - t);
    }

    /// <summary>The arc's peak (m) for a lob <paramref name="length"/> m long: <see cref="ApexPerMeter"/> of it, within [<see cref="MinApex"/>, <see cref="MaxApex"/>].</summary>
    public static float Apex(float length) => length > 0f ? Math.Clamp(length * ApexPerMeter, MinApex, MaxApex) : MinApex;
}
