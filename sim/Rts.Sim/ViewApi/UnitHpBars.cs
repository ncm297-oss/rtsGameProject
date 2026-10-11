using System;
using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Data;

namespace Rts.Sim.ViewApi;

/// <summary>Which units show an hp bar, how full, and in what colour (M4-V1). Read-only, allocation-free.</summary>
/// <remarks>
/// A bar shows only on a live unit whose hit points are below its maximum (as the building bars do, M3-V1); the fill is
/// <c>Hp / max</c> clamped to [0, 1]. Since hp techs raise a unit's hit points (M4-4b-3b, BUG-0421) the maximum is the
/// world's <c>MaxHp(slot)</c> (the type's <c>hp</c> plus its owner's hp techs): the view copies it per slot each frame and
/// passes it to <see cref="Collect(ReadOnlySpan{bool}, ReadOnlySpan{int}, ReadOnlySpan{int}, Span{int})"/> (M4-V7,
/// BUG-0450: a Prophecy Zealot at 70 / 83 is hurt). The form with the unit defs takes each type's <c>hp</c>, no techs.
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
    /// The view's form (M4-V7, BUG-0450): writes the slots set in <paramref name="shown"/> (the store's <c>Alive</c>, or the
    /// fog's shown list) whose <paramref name="hp"/> is below their <paramref name="maxHp"/> (per slot: the world's
    /// <c>MaxHp(slot)</c>, hp techs included), ascending, into <paramref name="slots"/> and returns how many (at most its
    /// length). A slot past any span's end is skipped. Allocation-free.
    /// </summary>
    public static int Collect(ReadOnlySpan<bool> shown, ReadOnlySpan<int> hp, ReadOnlySpan<int> maxHp, Span<int> slots)
    {
        int n = Math.Min(shown.Length, Math.Min(hp.Length, maxHp.Length)), k = 0;
        for (int i = 0; i < n && k < slots.Length; i++)
        {
            if (!shown[i]) continue;
            int max = maxHp[i];
            if (max <= 0 || hp[i] >= max) continue;
            slots[k++] = i;
        }
        return k;
    }

    /// <summary>
    /// The M4-V1 form, with each type's <c>hp</c> as the maximum (no hp techs): writes the slots of every live unit below it,
    /// ascending, into <paramref name="slots"/> and returns how many (at most its length). The spans are the unit store's
    /// <c>Alive</c>, <c>TypeId</c> and <c>Hp</c>. Equal to the per-slot form only while no hp tech is researched; a match's
    /// view uses that one (BUG-0450).
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
