using System;
using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Data;

namespace Rts.Sim.ViewApi;

/// <summary>Which units show an hp bar, how full, and in what colour (M4-V1). Read-only, allocation-free.</summary>
/// <remarks>
/// A bar shows only on a live unit whose hit points are below its type's <c>hp</c> (as the building bars do, M3-V1); the
/// fill is <c>Hp / max</c> clamped to [0, 1]. Hp tech bonuses are not applied by combat yet (docs/03 "Implementation
/// (M4-1)"), so the type's <c>hp</c> is the maximum.
/// </remarks>
public static class UnitHpBars
{
    /// <summary>The bar colour at full hit points (green), linear RGB in [0, 1].</summary>
    public static readonly Vector3 Full = new(0.30f, 0.90f, 0.35f);

    /// <summary>The bar colour at half hit points (yellow).</summary>
    public static readonly Vector3 Half = new(0.95f, 0.85f, 0.25f);

    /// <summary>The bar colour at no hit points (red).</summary>
    public static readonly Vector3 Empty = new(0.95f, 0.15f, 0.10f);

    /// <summary>True when a unit with <paramref name="hp"/> of <paramref name="maxHp"/> shows a bar; <paramref name="fill"/> is its share in [0, 1] (0 when none).</summary>
    public static bool Shows(int hp, int maxHp, out float fill)
    {
        fill = 0f;
        if (maxHp <= 0 || hp >= maxHp) return false;
        fill = Math.Clamp((float)hp / maxHp, 0f, 1f);
        return true;
    }

    /// <summary>The bar colour for <paramref name="fill"/>: green at 1, yellow at 0.5, red at 0, blended linearly between (NaN reads as 0).</summary>
    public static Vector3 Color(float fill)
    {
        float f = float.IsNaN(fill) ? 0f : Math.Clamp(fill, 0f, 1f);
        return f >= 0.5f ? Vector3.Lerp(Half, Full, (f - 0.5f) * 2f) : Vector3.Lerp(Empty, Half, f * 2f);
    }

    /// <summary>
    /// Writes the slots of every live hurt unit, ascending, into <paramref name="slots"/> and returns how many (at most its
    /// length). The spans are the unit store's <c>Alive</c>, <c>TypeId</c> and <c>Hp</c>.
    /// </summary>
    public static int Collect(ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId, ReadOnlySpan<int> hp, ImmutableArray<UnitDef> defs, Span<int> slots)
    {
        int n = Math.Min(alive.Length, Math.Min(typeId.Length, hp.Length)), k = 0;
        for (int i = 0; i < n && k < slots.Length; i++)
        {
            if (!alive[i]) continue;
            int t = typeId[i];
            if ((uint)t >= (uint)defs.Length) continue;
            int max = defs[t].Hp;
            if (max <= 0 || hp[i] >= max) continue;
            slots[k++] = i;
        }
        return k;
    }
}
