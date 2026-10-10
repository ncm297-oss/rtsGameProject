using System;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>
/// The casters the view sent a <c>UseAbility</c> whose command has not applied yet (M4-VH2, BUG-0370). A command enqueued
/// at <c>TickNumber</c> T is stamped for tick T + 1 (<c>Simulation.Enqueue</c>) and applies when that tick runs, so for two
/// <c>Tick()</c> calls neither <c>CastAbility</c> nor the unit's order queue shows it, and a second click in that window
/// would pick the same caster. <see cref="AbilityCaster.PickCaster"/> counts the casters <see cref="For"/> returns as busy.
/// View input memory, not gameplay state; allocation-free after construction.
/// </summary>
public sealed class SentCasts
{
    /// <summary>Casters remembered at most (a click each within two ticks; far past what a hand clicks in 100 ms).</summary>
    public const int DefaultCapacity = 32;

    private readonly EntityHandle[] _caster, _matching;
    private readonly int[] _ability;
    private readonly long[] _applyTick;
    private int _count;

    /// <summary>Creates a memory of <paramref name="capacity"/> casters.</summary>
    public SentCasts(int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _caster = new EntityHandle[capacity];
        _matching = new EntityHandle[capacity];
        _ability = new int[capacity];
        _applyTick = new long[capacity];
    }

    /// <summary>Casters remembered now (stale ones go at the next <see cref="For"/>).</summary>
    public int Count => _count;

    /// <summary>
    /// Records that <paramref name="caster"/> was sent ability <paramref name="abilityId"/> in a command that applies in tick
    /// <paramref name="applyTick"/> (the <c>TickNumber + 1</c> it was enqueued for). When full, the oldest entry makes room.
    /// </summary>
    public void Note(long applyTick, int abilityId, EntityHandle caster)
    {
        if (_count == _caster.Length) Remove(0);
        _caster[_count] = caster;
        _ability[_count] = abilityId;
        _applyTick[_count] = applyTick;
        _count++;
    }

    /// <summary>
    /// The casters sent ability <paramref name="abilityId"/> whose commands have not applied by <c>TickNumber</c>
    /// <paramref name="tick"/> (apply tick &gt;= <paramref name="tick"/>: the tick to run next has not run yet). Drops the
    /// applied ones first; the span is valid until the next call.
    /// </summary>
    public ReadOnlySpan<EntityHandle> For(long tick, int abilityId)
    {
        for (int k = _count - 1; k >= 0; k--)
            if (_applyTick[k] < tick) Remove(k);
        int n = 0;
        for (int k = 0; k < _count; k++)
            if (_ability[k] == abilityId) _matching[n++] = _caster[k];
        return new ReadOnlySpan<EntityHandle>(_matching, 0, n);
    }

    /// <summary>Forgets every caster (a new match).</summary>
    public void Clear() => _count = 0;

    private void Remove(int k)
    {
        for (int j = k + 1; j < _count; j++)
        {
            _caster[j - 1] = _caster[j];
            _ability[j - 1] = _ability[j];
            _applyTick[j - 1] = _applyTick[j];
        }
        _count--;
    }
}
