using System;
using System.Numerics;

namespace Rts.Sim.Determinism;

/// <summary>FNV-1a 64-bit hash accumulator for state hashes (stable across processes, unlike HashCode).</summary>
/// <remarks>Create with <c>new StateHasher()</c>; <c>default</c> skips the offset basis.</remarks>
public struct StateHasher
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private ulong _hash;

    /// <summary>Starts a hash at the FNV offset basis.</summary>
    public StateHasher()
    {
        _hash = OffsetBasis;
    }

    /// <summary>The hash of everything added so far.</summary>
    public readonly ulong Value => _hash;

    /// <summary>Mixes in a 64-bit value, one byte at a time (little-endian).</summary>
    public void Add(ulong value)
    {
        for (int i = 0; i < 8; i++)
        {
            _hash ^= (byte)(value >> (i * 8));
            _hash = unchecked(_hash * Prime);
        }
    }

    /// <summary>Mixes in a 32-bit integer.</summary>
    public void Add(int value) => Add((ulong)(uint)value);

    /// <summary>Mixes in a bool.</summary>
    public void Add(bool value) => Add(value ? 1UL : 0UL);

    /// <summary>Mixes in a float by its exact bit pattern.</summary>
    public void Add(float value) => Add(BitConverter.SingleToInt32Bits(value));

    /// <summary>Mixes in a string as its length then each UTF-16 char; null hashes as length -1 (never string.GetHashCode).</summary>
    public void Add(string? value)
    {
        if (value == null)
        {
            Add(-1);
            return;
        }
        Add(value.Length);
        for (int i = 0; i < value.Length; i++)
            Add((ulong)value[i]);
    }

    /// <summary>Mixes in both components of a vector.</summary>
    public void Add(Vector2 value)
    {
        Add(value.X);
        Add(value.Y);
    }
}
