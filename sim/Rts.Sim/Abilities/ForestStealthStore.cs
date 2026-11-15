using System;
using Rts.Sim.Save;

namespace Rts.Sim.Abilities;

/// <summary>
/// M8-1: the forest stealth's per-unit state (docs/03 "Implementation (M8-1)"), owned by <see cref="Entities.UnitStore"/>
/// (<see cref="Entities.UnitStore.Forest"/>), which clears a slot's state when it is freed or allocated. It exists only when
/// the data has a faction with <c>forestStealth</c>, so a match on other data carries, hashes and saves none of it.
/// </summary>
public sealed class ForestStealthStore
{
    /// <summary>
    /// Per slot, the number of the tick in which the unit's attack last landed or its cast last resolved (the moment the
    /// Revealed rule uses), -1 for never; recorded only for a unit of a seat with the forest stealth. Hashed when not -1; saved.
    /// </summary>
    public readonly int[] LastAttackTick;

    /// <summary>
    /// Per slot, 1 while the forest stealth holds the unit's Stealthed entry (<see cref="ForestStealthSystem"/>), else 0. While
    /// held, the entry's <see cref="StatusStore.TicksRemaining"/> is one more than the timed applications of that status left
    /// it, and never runs out. Hashed when 1; saved.
    /// </summary>
    public readonly byte[] Held;

    /// <summary>A store for <paramref name="capacity"/> unit slots, every slot at its default (never attacked, not held).</summary>
    public ForestStealthStore(int capacity)
    {
        LastAttackTick = new int[capacity];
        Array.Fill(LastAttackTick, -1);
        Held = new byte[capacity];
    }

    /// <summary>Puts unit slot <paramref name="unit"/> back at its default (its death, or a fresh allocation).</summary>
    internal void Clear(int unit)
    {
        LastAttackTick[unit] = -1;
        Held[unit] = 0;
    }

    /// <summary>Whether unit slot <paramref name="unit"/> is off its default (the state hash adds it only then).</summary>
    internal bool HasState(int unit) => LastAttackTick[unit] != -1 || Held[unit] != 0;

    /// <summary>Writes the save's <c>FRST</c> part: the tag, then both arrays.</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagForest);
        w.WriteArray(LastAttackTick);
        w.WriteArray(Held);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote into a store of the same capacity (the unit store checks the values).</summary>
    internal void ReadFrom(SaveReader r)
    {
        if (!r.Expect(SaveFormat.TagForest)) return;
        r.ReadArray(LastAttackTick);
        r.ReadArray(Held);
    }
}
