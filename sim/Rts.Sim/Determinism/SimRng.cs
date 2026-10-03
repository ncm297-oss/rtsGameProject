using System;
using System.Numerics;

namespace Rts.Sim.Determinism;

/// <summary>PCG32 (XSH-RR) random generator: 64-bit state, a per-stream odd increment, 32-bit output.</summary>
/// <remarks>
/// A mutable struct: keep it in an array or field and access it by <c>ref</c>, never by copy,
/// or draws are lost. Seeding follows O'Neill's reference <c>pcg32_srandom_r</c>.
/// </remarks>
public struct SimRng
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private readonly ulong _increment;

    /// <summary>Seeds a generator; different stream ids give independent sequences for the same seed.</summary>
    public SimRng(ulong seed, ulong streamId)
    {
        _state = 0;
        _increment = (streamId << 1) | 1UL;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    /// <summary>Internal state word (for state hashing and save files).</summary>
    public readonly ulong State => _state;

    /// <summary>Stream increment (for state hashing and save files).</summary>
    public readonly ulong Increment => _increment;

    /// <summary>Next uniformly distributed 32-bit value.</summary>
    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked(old * Multiplier + _increment);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return BitOperations.RotateRight(xorShifted, rot);
    }

    /// <summary>Uniform integer in [minInclusive, maxExclusive), without modulo bias.</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        uint range = (uint)((long)maxExclusive - minInclusive);
        // Lemire's multiply-and-reject: unbiased, usually one draw.
        ulong m = (ulong)NextUInt() * range;
        uint low = (uint)m;
        if (low < range)
        {
            uint threshold = unchecked(0u - range) % range;
            while (low < threshold)
            {
                m = (ulong)NextUInt() * range;
                low = (uint)m;
            }
        }
        return (int)(minInclusive + (long)(m >> 32));
    }

    /// <summary>Uniform float in [0, 1), using the top 24 bits so every value is exact.</summary>
    public float NextFloat()
    {
        return (NextUInt() >> 8) * (1f / 16777216f);
    }
}
