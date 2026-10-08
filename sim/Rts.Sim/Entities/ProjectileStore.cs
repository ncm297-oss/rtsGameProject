using System;
using System.Numerics;
using Rts.Sim.Determinism;

namespace Rts.Sim.Entities;

/// <summary>
/// Projectiles in flight (M4-2b, docs/03 "Implementation (M4-2b)"): flat structure-of-arrays storage, sized once from
/// <see cref="SimConfig.ProjectileSlots"/>. A projectile is fire-and-forget, so there are no handles: a shot takes the
/// lowest free slot, flies, lands and frees it, and every loop runs in slot order. The read-only spans are the view's
/// (M4-V3); only the sim writes.
/// </summary>
public sealed class ProjectileStore
{
    private readonly bool[] _alive;
    private readonly Vector2[] _position;
    private readonly Vector2[] _prevPosition;
    private readonly Vector2[] _velocity;
    private readonly Vector2[] _target;
    private readonly int[] _ticksLeft;
    private readonly int[] _typeId;
    private readonly int[] _owner;
    private readonly int[] _attackerType;
    private readonly EntityHandle[] _attacker;
    private readonly EntityHandle[] _victim;
    private readonly bool[] _victimIsBuilding;
    private readonly byte[] _level;

    /// <summary>Longest flight in ticks (an hour): a bound on the format, not a stat.</summary>
    private const int MaxFlightTicks = 72_000;

    /// <summary>No slot below this is free: where the next spawn starts looking. Follows from <see cref="Alive"/>; not state.</summary>
    private int _firstFree;

    /// <summary>Every live slot is below this (a loop bound); reset when the store empties. Not state.</summary>
    private int _end;

    /// <summary>Creates an empty store of <paramref name="capacity"/> slots.</summary>
    public ProjectileStore(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _alive = new bool[capacity];
        _position = new Vector2[capacity];
        _prevPosition = new Vector2[capacity];
        _velocity = new Vector2[capacity];
        _target = new Vector2[capacity];
        _ticksLeft = new int[capacity];
        _typeId = new int[capacity];
        _owner = new int[capacity];
        _attackerType = new int[capacity];
        _attacker = new EntityHandle[capacity];
        _victim = new EntityHandle[capacity];
        _victimIsBuilding = new bool[capacity];
        _level = new byte[capacity];
    }

    /// <summary>Number of slots.</summary>
    public int Capacity => _alive.Length;

    /// <summary>Projectiles in flight.</summary>
    public int Count { get; private set; }

    /// <summary>Per slot: in flight. Read the other spans only where this is true.</summary>
    public ReadOnlySpan<bool> Alive => _alive;

    /// <summary>Per slot: position (x, z) in meters at the end of the last tick.</summary>
    public ReadOnlySpan<Vector2> Position => _position;

    /// <summary>Per slot: position at the start of the last tick, for render interpolation (the launch point on the tick it was fired).</summary>
    public ReadOnlySpan<Vector2> PrevPosition => _prevPosition;

    /// <summary>
    /// Per slot: the impact point (x, z) in meters: where the target was when it was fired, or for a led shot where it
    /// will be when it lands (re-led each tick, BUG-0183). It flies toward it in a straight step each tick.
    /// </summary>
    public ReadOnlySpan<Vector2> Target => _target;

    /// <summary>Per slot: projectile type id (<see cref="Data.GameData.Projectiles"/>).</summary>
    public ReadOnlySpan<int> ProjectileTypeId => _typeId;

    /// <summary>Per slot: the player who fired it.</summary>
    public ReadOnlySpan<int> Owner => _owner;

    /// <summary>Per slot: ticks of flight left; 0 on the tick it reaches <see cref="Target"/> (it lands that tick).</summary>
    internal int[] TicksLeft => _ticksLeft;

    /// <summary>Per slot: the unit type whose attack it carries (its damage, splash and friendly fire).</summary>
    internal int[] AttackerType => _attackerType;

    /// <summary>Per slot: the unit that fired it (may be dead by the time it lands).</summary>
    internal EntityHandle[] Attacker => _attacker;

    /// <summary>Per slot: what it was fired at (an aimed shot hits only this); a building handle when <see cref="VictimIsBuilding"/>.</summary>
    internal EntityHandle[] Victim => _victim;

    /// <summary>Per slot: whether <see cref="Victim"/> is a building.</summary>
    internal bool[] VictimIsBuilding => _victimIsBuilding;

    /// <summary>Per slot: the level of the cell it was fired from (M4-3a): a hit from above its victim reveals the shooter.</summary>
    internal byte[] Level => _level;

    /// <summary>One past the highest slot that may be live: the loops' bound.</summary>
    internal int End => _end;

    /// <summary>
    /// Launches a projectile from <paramref name="from"/> toward <paramref name="to"/> at <paramref name="speedPerTick"/>: it
    /// takes <c>ceil(distance / speed)</c> ticks (at least 1), moving the same step each tick and the last one onto the
    /// impact point. False (nothing changes) when the store is full: the shot is lost.
    /// </summary>
    internal bool TrySpawn(Vector2 from, Vector2 to, float speedPerTick, int typeId, int owner, int attackerType,
        EntityHandle attacker, EntityHandle victim, bool victimIsBuilding, int level = 0)
    {
        int i = _firstFree;
        while (i < _alive.Length && _alive[i]) i++;
        if (i >= _alive.Length) return false;
        float distance = Vector2.Distance(from, to);
        int ticks = FlightTicks(distance, speedPerTick);
        _alive[i] = true;
        _position[i] = from;
        _prevPosition[i] = from;
        _velocity[i] = distance > 0f ? (to - from) * (speedPerTick / distance) : Vector2.Zero;
        _target[i] = to;
        _ticksLeft[i] = ticks;
        _typeId[i] = typeId;
        _owner[i] = owner;
        _attackerType[i] = attackerType;
        _attacker[i] = attacker;
        _victim[i] = victim;
        _victimIsBuilding[i] = victimIsBuilding;
        _level[i] = (byte)level;
        Count++;
        _firstFree = i + 1;
        if (i + 1 > _end) _end = i + 1;
        return true;
    }

    /// <summary>Ticks a shot <paramref name="distance"/> m long flies at <paramref name="speedPerTick"/>: <c>ceil(distance / speed)</c>, at least 1, at most an hour.</summary>
    internal static int FlightTicks(float distance, float speedPerTick)
    {
        float flight = distance / speedPerTick;
        // Cap the flight rather than overflow the tick count (the loader's speed floor keeps real data far below it).
        return flight < MaxFlightTicks ? Math.Max(1, (int)MathF.Ceiling(flight)) : MaxFlightTicks;
    }

    /// <summary>
    /// Re-aims slot <paramref name="k"/> at <paramref name="to"/>, keeping its landing tick: its step becomes the rest of the
    /// way over its ticks left. Called before <see cref="Fly"/>. The same point changes nothing (a shot at a standing
    /// target keeps its launch step).
    /// </summary>
    internal void Steer(int k, Vector2 to)
    {
        if (to == _target[k]) return;
        _target[k] = to;
        _velocity[k] = (to - _position[k]) / _ticksLeft[k];
    }

    /// <summary>Moves every projectile in flight one tick along its line (the last step lands exactly on its impact point).</summary>
    internal void Fly()
    {
        for (int i = 0; i < _end; i++)
        {
            if (!_alive[i]) continue;
            _prevPosition[i] = _position[i];
            if (--_ticksLeft[i] <= 0)
            {
                _ticksLeft[i] = 0;
                _position[i] = _target[i];
            }
            else
            {
                _position[i] += _velocity[i];
            }
        }
    }

    /// <summary>Frees slot <paramref name="i"/> (it landed), back to its never-used values.</summary>
    internal void Free(int i)
    {
        if (!_alive[i]) return;
        _alive[i] = false;
        _position[i] = _prevPosition[i] = _velocity[i] = _target[i] = Vector2.Zero;
        _ticksLeft[i] = _typeId[i] = _owner[i] = _attackerType[i] = 0;
        _attacker[i] = _victim[i] = default;
        _victimIsBuilding[i] = false;
        _level[i] = 0;
        Count--;
        if (i < _firstFree) _firstFree = i;
        if (Count == 0) _end = 0;
    }

    /// <summary>
    /// Adds the store to a state hash: nothing while it is empty (so a match without a shot hashes exactly as before
    /// M4-2b), else the count and every live slot's index and fields. Free slots hold their never-used values, and which
    /// slot a spawn takes follows from the live ones, so this is the whole state.
    /// </summary>
    internal void AddToHash(ref StateHasher h)
    {
        if (Count == 0) return;
        h.Add(Count);
        for (int i = 0; i < _end; i++)
        {
            if (!_alive[i]) continue;
            h.Add(i);
            h.Add(_position[i]);
            h.Add(_prevPosition[i]);
            h.Add(_velocity[i]);
            h.Add(_target[i]);
            h.Add(_ticksLeft[i]);
            h.Add(_typeId[i]);
            h.Add(_owner[i]);
            h.Add(_attackerType[i]);
            h.Add(_attacker[i].Index);
            h.Add(_attacker[i].Generation);
            h.Add(_victim[i].Index);
            h.Add((ulong)(uint)_victim[i].Generation | (_victimIsBuilding[i] ? 1UL << 32 : 0UL));
            h.Add((int)_level[i]); // M4-3a
        }
    }
}
