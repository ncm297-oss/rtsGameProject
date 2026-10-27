using System;
using System.Numerics;

namespace Rts.Sim.ViewApi;

/// <summary>
/// Attack alerts for the HUD (M6-V12, docs/02 "HUD layout": "Your base is under attack"): one player's own unit or building
/// whose hit points fell since the last <see cref="Update"/> raises an alert at its position, unless an alert raised in the
/// last <see cref="Seconds"/> of the alerts' clock (<see cref="Now"/>: the HUD drives it with game time, one step per sim
/// tick) lies within <see cref="Radius"/> metres of it (then the hit is counted on that
/// alert). Buildings are scanned before units, so a raid that hits both raises a <see cref="AttackAlertKind.Base"/> alert;
/// a building hit that joins an earlier <see cref="AttackAlertKind.Units"/> alert turns it into a Base one
/// (<see cref="Upgraded"/>, BUG-0954 item 1), and a unit hit never turns a Base alert back.
/// Like <see cref="HitFlash"/> no sim event is needed: each slot remembers the hp and generation it last saw, a new unit in a
/// slot (a new generation, trained or rallied hurt) raises nothing, and a dead slot is forgotten. Presentation only;
/// allocation-free after construction.
/// </summary>
public sealed class AttackAlerts
{
    /// <summary>Default suppression radius in metres: hits this close to a recent alert raise no new one.</summary>
    public const float DefaultRadius = 30f;

    /// <summary>Default suppression time in seconds of the alerts' clock (game time in the HUD).</summary>
    public const float DefaultSeconds = 10f;

    /// <summary>Default number of recent alerts kept (oldest replaced).</summary>
    public const int DefaultCapacity = 32;

    private readonly int[] _unitHp, _unitGen, _buildingHp, _buildingGen;
    private readonly Vector2[] _position;
    private readonly float[] _raisedAt;
    private readonly AttackAlertKind[] _kind;
    private readonly int[] _hits;
    private readonly int[] _new;
    private readonly int[] _upgraded;
    private int _next, _count, _newCount, _upgradedCount;

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
        _upgraded = new int[capacity];
    }

    /// <summary>Hits within this many metres of a recent alert raise none.</summary>
    public float Radius { get; }

    /// <summary>How long an alert suppresses others near it, in seconds of the alerts' clock.</summary>
    public float Seconds { get; }

    /// <summary>The alerts' clock in seconds: the sum of every <see cref="Update"/>'s <c>dt</c>, or the last <see cref="SetNow"/> plus those since.</summary>
    public float Now { get; private set; }

    /// <summary>
    /// Sets the clock to <paramref name="now"/> seconds (negative, NaN or infinite: ignored). The HUD sets the sim tick times the
    /// tick length before each tick's <see cref="Update"/> (dt 0), so suppression is game time whatever the game speed or frame
    /// rate, a replay sees the same alerts at the same ticks (BUG-0952), and no float rounding piles up over a long match.
    /// </summary>
    public void SetNow(float now)
    {
        if (float.IsFinite(now) && now >= 0f) Now = now;
    }

    /// <summary>Alerts raised so far.</summary>
    public long Raised { get; private set; }

    /// <summary>Hits of own units and buildings seen so far (raising or not).</summary>
    public long Hits { get; private set; }

    /// <summary>Alerts kept (at most the capacity), newest last in <see cref="Recent"/> order.</summary>
    public int Count => _count;

    /// <summary>Alerts raised by the last <see cref="Update"/>.</summary>
    public int NewCount => _newCount;

    /// <summary>The <paramref name="k"/>-th alert raised by the last <see cref="Update"/> (0 .. <see cref="NewCount"/> - 1), as an index for <see cref="Kind"/>, <see cref="Position"/>, <see cref="HitsOn"/>, <see cref="RaisedAt"/>.</summary>
    /// <remarks>If one Update raised more alerts than the ring holds, the indices are the ring's slots, which by then hold the
    /// newest ones in rotated order: the set is right, the order is not raise order (BUG-0954; capacity 32 makes it rare).</remarks>
    public int New(int k) => _new[k];

    /// <summary>BUG-0954 item 1 (M6-V13): alerts the last <see cref="Update"/> turned from <see cref="AttackAlertKind.Units"/> to <see cref="AttackAlertKind.Base"/>.</summary>
    public int UpgradedCount => _upgradedCount;

    /// <summary>The <paramref name="k"/>-th alert the last <see cref="Update"/> upgraded to Base (0 .. <see cref="UpgradedCount"/> - 1), an index as <see cref="New"/>'s.</summary>
    /// <remarks>BUG-0970: a raise on a full ring replaces the oldest alert this Update did not upgrade, so every index here still
    /// names its upgraded (Base) alert.</remarks>
    public int Upgraded(int k) => _upgraded[k];

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

    /// <summary>The alerts' clock (<see cref="Now"/>) when the alert was raised.</summary>
    public float RaisedAt(int index) => _raisedAt[index];

    /// <summary>Hits counted on the alert: the one that raised it and those near it it suppressed since.</summary>
    public int HitsOn(int index) => _hits[index];

    /// <summary>
    /// One step, <paramref name="dt"/> seconds of the clock (negative or NaN: 0) after the last (the HUD: one per sim tick): <paramref name="player"/>'s
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
        _upgradedCount = 0;
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
                // BUG-0954 item 1: a raid that reaches the base joins the units' alert; the alert now says base. Never the
                // other way, so an alert upgrades at most once.
                if (kind == AttackAlertKind.Base && _kind[a] == AttackAlertKind.Units)
                {
                    _kind[a] = AttackAlertKind.Base;
                    if (_upgradedCount < _upgraded.Length) _upgraded[_upgradedCount++] = a;
                }
                return;
            }
        }
        if (_count == _position.Length && IsUpgraded(_next)) KeepUpgradedOldest();
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

    private bool IsUpgraded(int slot)
    {
        for (int k = 0; k < _upgradedCount; k++)
            if (_upgraded[k] == slot) return true;
        return false;
    }

    // BUG-0970: the ring is full and its oldest alert (the slot a raise takes) was upgraded by this Update, so Upgraded(k)
    // would name the new alert. The oldest alert this Update did not upgrade goes instead: the alerts older than it move up
    // one slot (oldest-first order kept, New / Upgraded indices moved with them), freeing the raise's slot. If this Update
    // upgraded every alert, the oldest one goes as before and its Upgraded entry with it.
    private void KeepUpgradedOldest()
    {
        int n = _position.Length, j = 1;
        while (j < n && IsUpgraded((_next + j) % n)) j++;
        if (j == n)
        {
            Remove(_upgraded, ref _upgradedCount, _next);
            return;
        }
        Remove(_new, ref _newCount, (_next + j) % n);
        for (int i = j; i > 0; i--)
        {
            int to = (_next + i) % n, from = (_next + i - 1) % n;
            _position[to] = _position[from];
            _raisedAt[to] = _raisedAt[from];
            _kind[to] = _kind[from];
            _hits[to] = _hits[from];
            Move(_upgraded, _upgradedCount, from, to);
            Move(_new, _newCount, from, to);
        }
    }

    private static void Remove(int[] list, ref int count, int slot)
    {
        int w = 0;
        for (int k = 0; k < count; k++)
            if (list[k] != slot) list[w++] = list[k];
        count = w;
    }

    private static void Move(int[] list, int count, int from, int to)
    {
        for (int k = 0; k < count; k++)
            if (list[k] == from) list[k] = to;
    }
}
