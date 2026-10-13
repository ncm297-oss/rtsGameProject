using System;
using System.Numerics;
using Rts.Sim;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// The 10 s bench's march as QaH2Test and QaM27Test measure it (BUG-0661): the units the bench ordered across, their centre's
/// shift, and the bound that shift is held to, derived from the units' speeds and the script's march window.
/// </summary>
/// <remarks>
/// Why the marching set and not every local unit: since M5-4a the start workers stand by the Town Hall on the player's start
/// location, far from the army block and off the bench's screen, so the box select never takes them and they never move; and a
/// block in a narrow clearing (seed 6: a column 80 m tall) is only partly on screen, so only part of it is ordered. A centre
/// over every local unit measured those staging facts, not the march.
/// </remarks>
public static class BenchMarch
{
    /// <summary>
    /// Fraction of a free march (each marching unit at its own speed for the whole window, in a straight line) the set's centre
    /// must cover. Half, because the path crosses the idle enemy block (combat off: the bodies push through, which slows the
    /// front) and bends round cliffs and forests to the far target. Measured at BUG-0661 (2026-10-13): seeds 1, 6, 7, 21, 31
    /// cover 0.63, 0.79, 0.72, 0.60, 0.91 of it (seed 21 least); 0.5 keeps a sixth below the least for frame timing under load.
    /// A stalled or half-ordered march (the bug class BUG-0101 / BUG-0104 guard) covers well under it.
    /// </summary>
    public const float Slack = 0.5f;

    /// <summary>Seconds the script's army marches: from the <see cref="BenchStep.OrderAcross"/> step to the <see cref="BenchStep.Hold"/> step (8.75 s today).</summary>
    public static double WindowSeconds()
    {
        double at = 0, across = -1;
        foreach (BenchScript.Entry e in BenchScript.Sequence)
        {
            if (e.Step == BenchStep.OrderAcross && across < 0) across = at;
            if (e.Step == BenchStep.Hold && across >= 0) return at - across;
            at += e.Seconds;
        }
        throw new InvalidOperationException("the bench script has no OrderAcross before a Hold");
    }

    /// <summary>The slots of the current selection (taken right after the bench's across order: the units it sent).</summary>
    public static int[] Marching(SelectionSet selection)
    {
        ReadOnlySpan<EntityHandle> items = selection.Items;
        var slots = new int[items.Length];
        for (int i = 0; i < items.Length; i++) slots[i] = items[i].Index;
        return slots;
    }

    /// <summary>Centre (meters) of the live units among <paramref name="slots"/>.</summary>
    public static Vector2 Centre(UnitStore u, int[] slots)
    {
        var sum = Vector2.Zero;
        int n = 0;
        foreach (int i in slots)
        {
            if (!u.Alive[i]) continue;
            sum += u.Position[i];
            n++;
        }
        return n > 0 ? sum / n : sum;
    }

    /// <summary>Meters the set's centre would move if every unit walked straight at its own speed for <see cref="WindowSeconds"/>.</summary>
    public static float FreeMarch(UnitStore u, int[] slots)
    {
        if (slots.Length == 0) return 0f;
        float speed = 0f; // meters per tick, summed
        foreach (int i in slots) speed += u.Speed[i];
        return speed / slots.Length * SimConstants.TicksPerSecond * (float)WindowSeconds();
    }

    /// <summary>The bound a bench march's centre shift is held to: <see cref="Slack"/> of <see cref="FreeMarch"/>.</summary>
    public static float MinShift(UnitStore u, int[] slots) => Slack * FreeMarch(u, slots);
}
