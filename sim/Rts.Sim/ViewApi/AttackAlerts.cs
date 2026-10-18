using System;
using System.Numerics;

namespace Rts.Sim.ViewApi;

/// <summary>
/// Attack alerts for the HUD (M6-V12, docs/02 "HUD layout": "Your base is under attack"): one player's own unit or building
/// whose hit points fell since the last <see cref="Update"/> raises an alert at its position, unless an alert raised in the
/// last <see cref="Seconds"/> of view time lies within <see cref="Radius"/> metres of it (then the hit is counted on that
/// alert). Buildings are scanned before units, so a raid that hits both raises a <see cref="AttackAlertKind.Base"/> alert.
/// Like <see cref="HitFlash"/> no sim event is needed: each slot remembers the hp and generation it last saw, a new unit in a
/// slot (a new generation, trained or rallied hurt) raises nothing, and a dead slot is forgotten. Presentation only;
/// allocation-free after construction.
/// </summary>
public sealed class AttackAlerts
{
    /// <summary>Default suppression radius in metres: hits this close to a recent alert raise no new one.</summary>
    public const float DefaultRadius = 30f;

    /// <summary>Default suppression time in seconds of view time.</summary>
    public const float DefaultSeconds = 10f;

    /// <summary>Default number of recent alerts kept (oldest replaced).</summary>
    public const int DefaultCapacity = 32;

    private readonly int[] _unitHp, _unitGen, _buildingHp, _buildingGen;
    private readonly Vector2[] _position;
    private readonly float[] _raisedAt;
    private readonly AttackAlertKind[] _kind;
    private readonly int[] _hits;
    private readonly int[] _new;
    private int _next, _count, _newCount;

    /// <summary>Creates the bookkeeping for the unit and building stores' capacities.</summary>
    public AttackAlerts(int unitCapacity, int buildingCapacity, float radius = DefaultRadius, float seconds = DefaultSeconds, int capacity = DefaultCapacity)
    {
        if (unitCapacity < 0) throw new ArgumentOutOfRangeException(nameof(unitCapacity));
        if (buildingCapacity < 0) throw new ArgumentOutOfRangeException(nameof(buildingCapacity));
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Radius = radius;
        Seconds = seconds;
        _unitHp = new int[unitCapacity];
        _unitGen = new int[unitCapacity];
        _buildingHp = new int[buildingCapacity];
        _buildingGen = new int[buildingCapacity];
        Array.Fill(_unitGen, -1);
        Array.Fill(_buildingGen, -1);
        _position = new Vector2[capacity];
        _raisedAt = new float[capacity];
        _kind = new AttackAlertKind[capacity];
        _hits = new int[capacity];
        _new = new int[capacity];
    }

    /// <summary>Hits within this many metres of a recent alert raise none.</summary>
    public float Radius { get; }

    /// <summary>How long an alert suppresses others near it, in seconds of view time.</summary>
    public float Seconds { get; }

    /// <summary>View time in seconds: the sum of every <see cref="Update"/>'s <c>dt</c>.</summary>
    public float Now { get; private set; }

    /// <summary>Alerts raised so far.</summary>
    public long Raised { get; private set; }

    /// <summary>Hits of own units and buildings seen so far (raising or not).</summary>
    public long Hits { get; private set; }

    /// <summary>Alerts kept (at most the capacity), newest last in <see cref="Recent"/> order.</summary>
    public int Count => _count;

    /// <summary>Alerts raised by the last <see cref="Update"/>.</summary>
    public int NewCount => _newCount;

    /// <summary>The <paramref name="k"/>-th alert raised by the last <see cref="Update"/> (0 .. <see cref="NewCount"/> - 1), as an index for <see cref="Kind"/>, <see cref="Position"/>, <see cref="HitsOn"/>, <see cref="RaisedAt"/>.</summary>
    public int New(int k) => _new[k];

    /// <summary>True once any alert was raised; then <see cref="LastIndex"/> is the newest.</summary>
    public bool HasLast => Raised > 0;

    /// <summary>The newest alert's index (meaningful when <see cref="HasLast"/>).</summary>
    public int LastIndex => (_next + _position.Length - 1) % _position.Length;

    /// <summary>The <paramref name="k"/>-th kept alert's index, oldest first (0 .. <see cref="Count"/> - 1).</summary>
    public int Recent(int k) => (_next - _count + k + 2 * _position.Length) % _position.Length;

    /// <summary>A unit or a building was hit.</summary>
    public AttackAlertKind Kind(int index) => _kind[index];

    /// <summary>Where (m): the hit unit's position or the hit building's footprint centre.</summary>
    public Vector2 Position(int index) => _position[index];

    /// <summary>View time the alert was raised at.</summary>
    public float RaisedAt(int index) => _raisedAt[index];

    /// <summary>Hits counted on the alert: the one that raised it and those near it it suppressed since.</summary>
    public int HitsOn(int index) => _hits[index];

    /// <summary>
    /// One view frame, <paramref name="dt"/> seconds (negative or NaN: 0) after the last: <paramref name="player"/>'s
    /// buildings, then units, whose hp fell since the last call raise or join alerts. The spans are the stores' <c>Alive</c>,
    /// <c>Generation</c>, <c>Hp</c>, <c>Owner</c> and positions (units' <c>Position</c>; buildings' footprint centres, which
    /// the view fills). Returns the alerts raised (<see cref="NewCount"/>). No allocation.
    /// </summary>
    public int Update(int player, float dt,
        ReadOnlySpan<bool> unitAlive, ReadOnlySpan<int> unitGeneration, ReadOnlySpan<int> unitHp, ReadOnlySpan<int> unitOwner, ReadOnlySpan<Vector2> unitPosition,
        ReadOnlySpan<bool> buildingAlive, ReadOnlySpan<int> buildingGeneration, ReadOnlySpan<int> buildingHp, ReadOnlySpan<int> buildingOwner, ReadOnlySpan<Vector2> buildingPosition)
    {
        if (dt > 0f) Now += dt;
        _newCount = 0;
        Scan(player, AttackAlertKind.Base, _buildingHp, _buildingGen, buildingAlive, buildingGeneration, buildingHp, buildingOwner, buildingPosition);
        Scan(player, AttackAlertKind.Units, _unitHp, _unitGen, unitAlive, unitGeneration, unitHp, unitOwner, unitPosition);
        return _newCount;
    }

    private void Scan(int player, AttackAlertKind kind, int[] lastHp, int[] lastGen,
        ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation, ReadOnlySpan<int> hp, ReadOnlySpan<int> owner, ReadOnlySpan<Vector2> position)
    {
        int n = Math.Min(lastHp.Length, Math.Min(alive.Length, Math.Min(generation.Length, Math.Min(hp.Length, Math.Min(owner.Length, position.Length)))));
        for (int i = 0; i < n; i++)
        {
            if (!alive[i] || owner[i] != player)
            {
                lastGen[i] = -1;
                continue;
            }
            if (generation[i] != lastGen[i])
            {
                lastGen[i] = generation[i];
                lastHp[i] = hp[i];
                continue;
            }
            bool hit = hp[i] < lastHp[i];
            lastHp[i] = hp[i];
            if (hit) Hit(kind, position[i]);
        }
    }

    private void Hit(AttackAlertKind kind, Vector2 at)
    {
        Hits++;
        float r2 = Radius * Radius;
        for (int k = 0; k < _count; k++)
        {
            int a = Recent(k);
            if (Now - _raisedAt[a] < Seconds && Vector2.DistanceSquared(_position[a], at) <= r2)
            {
                _hits[a]++;
                return;
            }
        }
        int slot = _next;
        _next = (_next + 1) % _position.Length;
        if (_count < _position.Length) _count++;
        _position[slot] = at;
        _raisedAt[slot] = Now;
        _kind[slot] = kind;
        _hits[slot] = 1;
        if (_newCount < _new.Length) _new[_newCount++] = slot;
        Raised++;
    }
}
