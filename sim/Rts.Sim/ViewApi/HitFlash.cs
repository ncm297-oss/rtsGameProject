using System;

namespace Rts.Sim.ViewApi;

/// <summary>
/// Hit-flash bookkeeping for the unit views (M4-V1): a unit whose hit points fell since the last <see cref="Update"/> is lit
/// for <see cref="Seconds"/> of view time, and a later hit while lit starts the time again, so a unit under repeated hits
/// stays lit. No sim event is needed: each slot remembers the hp and generation it last saw. Presentation only (a timer per
/// slot); allocation-free after construction.
/// </summary>
public sealed class HitFlash
{
    /// <summary>Default flash length in seconds of view time.</summary>
    public const float DefaultSeconds = 0.15f;

    private readonly int[] _lastHp, _lastGen;
    private readonly float[] _left;
    private readonly bool[] _hit;

    /// <summary>Creates the bookkeeping for <paramref name="capacity"/> unit slots.</summary>
    public HitFlash(int capacity, float seconds = DefaultSeconds)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Seconds = seconds;
        _lastHp = new int[capacity];
        _lastGen = new int[capacity];
        _left = new float[capacity];
        _hit = new bool[capacity];
        Array.Fill(_lastGen, -1);
    }

    /// <summary>How long a hit keeps a unit lit, in seconds.</summary>
    public float Seconds { get; }

    /// <summary>Unit slots tracked.</summary>
    public int Capacity => _left.Length;

    /// <summary>Units lit after the last <see cref="Update"/>.</summary>
    public int LitCount { get; private set; }

    /// <summary>Units whose hp fell in the last <see cref="Update"/>.</summary>
    public int HitCount { get; private set; }

    /// <summary>True while slot <paramref name="slot"/> is lit.</summary>
    public bool IsLit(int slot) => (uint)slot < (uint)_left.Length && _left[slot] > 0f;

    /// <summary>Seconds of flash left on slot <paramref name="slot"/> (0 when unlit).</summary>
    public float Left(int slot) => (uint)slot < (uint)_left.Length ? _left[slot] : 0f;

    /// <summary>True if slot <paramref name="slot"/>'s hp fell in the last <see cref="Update"/>.</summary>
    public bool WasHit(int slot) => (uint)slot < (uint)_hit.Length && _hit[slot];

    /// <summary>
    /// One view frame, <paramref name="dt"/> seconds after the last: a slot holding the unit seen before (same generation)
    /// with less hp is lit afresh; any other lit slot loses <paramref name="dt"/>. A dead slot, or a new unit in it, starts
    /// unlit. The spans are the unit store's <c>Alive</c>, <c>Generation</c> and <c>Hp</c>. A negative or NaN
    /// <paramref name="dt"/> counts as 0.
    /// </summary>
    public void Update(ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation, ReadOnlySpan<int> hp, float dt)
    {
        if (!(dt > 0f)) dt = 0f;
        int n = Math.Min(_left.Length, Math.Min(alive.Length, Math.Min(generation.Length, hp.Length)));
        int lit = 0, hits = 0;
        for (int i = 0; i < n; i++)
        {
            bool hit = false;
            if (!alive[i])
            {
                _lastGen[i] = -1;
                _left[i] = 0f;
            }
            else if (generation[i] != _lastGen[i])
            {
                _lastGen[i] = generation[i];
                _lastHp[i] = hp[i];
                _left[i] = 0f;
            }
            else
            {
                hit = hp[i] < _lastHp[i];
                _lastHp[i] = hp[i];
                if (hit) _left[i] = Seconds;
                else if (_left[i] > 0f) _left[i] = Math.Max(0f, _left[i] - dt);
            }
            _hit[i] = hit;
            if (hit) hits++;
            if (_left[i] > 0f) lit++;
        }
        LitCount = lit;
        HitCount = hits;
    }
}
