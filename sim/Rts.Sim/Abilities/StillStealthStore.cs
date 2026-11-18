using System;
using System.Numerics;
using Rts.Sim.Save;

namespace Rts.Sim.Abilities;

/// <summary>
/// M8-2: the stand-still stealth's per-unit state (docs/03 "Implementation (M8-2)"), owned by <see cref="Entities.UnitStore"/>
/// (<see cref="Entities.UnitStore.Still"/>), which clears a slot's state when it is freed or allocated. It exists only when
/// the data has a unit type with <c>stillStealth</c>, so a match on other data carries, hashes and saves none of it. Only a
/// live, non-summoned unit of such a type ever leaves the default.
/// </summary>
public sealed class StillStealthStore
{
    /// <summary>
    /// Per slot, the tick the unit's position last changed as phase 5 dates it (<see cref="StillStealthSystem"/>): the tick
    /// of the pass that first saw the unit, or the tick before the pass that saw it somewhere new (movement runs after
    /// phase 5). -1 until a pass has seen it. Hashed with the rest; saved.
    /// </summary>
    public readonly int[] MovedTick;

    /// <summary>Per slot, where the last pass saw the unit (meters); default until a pass has seen it. Hashed with the rest; saved.</summary>
    public readonly Vector2[] StillAt;

    /// <summary>Per slot, the tick the unit's attack last landed or its cast last resolved (the Revealed rule's moment), -1 for never. Saved.</summary>
    public readonly int[] LastAttackTick;

    /// <summary>
    /// Per slot, the tick the unit last lost hit points (a hit that takes none does not count), -1 for never. Its own, not
    /// <see cref="Entities.UnitStore.LastDamagedTick"/>, which a regenerating faction records on every hit. Saved.
    /// </summary>
    public readonly int[] LastHurtTick;

    /// <summary>
    /// Per slot, 1 while the stand-still stealth holds the unit's Stealthed entry, else 0. While held, the entry's
    /// <see cref="StatusStore.TicksRemaining"/> carries one held tick for this rule on top of what the timed applications
    /// (and the forest, M8-1) left it, and never runs out. Saved.
    /// </summary>
    public readonly byte[] Held;

    /// <summary>A store for <paramref name="capacity"/> unit slots, every slot at its default (never seen, attacked or hurt, not held).</summary>
    public StillStealthStore(int capacity)
    {
        MovedTick = new int[capacity];
        Array.Fill(MovedTick, -1);
        StillAt = new Vector2[capacity];
        LastAttackTick = new int[capacity];
        Array.Fill(LastAttackTick, -1);
        LastHurtTick = new int[capacity];
        Array.Fill(LastHurtTick, -1);
        Held = new byte[capacity];
    }

    /// <summary>Puts unit slot <paramref name="unit"/> back at its default (its death, or a fresh allocation).</summary>
    internal void Clear(int unit)
    {
        MovedTick[unit] = -1;
        StillAt[unit] = default;
        LastAttackTick[unit] = -1;
        LastHurtTick[unit] = -1;
        Held[unit] = 0;
    }

    /// <summary>Whether unit slot <paramref name="unit"/> is off its default (the state hash adds it only then).</summary>
    internal bool HasState(int unit) =>
        MovedTick[unit] != -1 || StillAt[unit] != default || LastAttackTick[unit] != -1 || LastHurtTick[unit] != -1 || Held[unit] != 0;

    /// <summary>Writes the save's <c>STIL</c> part: the tag, then every array.</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagStill);
        w.WriteArray(MovedTick);
        w.WriteArray(StillAt);
        w.WriteArray(LastAttackTick);
        w.WriteArray(LastHurtTick);
        w.WriteArray(Held);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote into a store of the same capacity (the unit store and the world check the values).</summary>
    internal void ReadFrom(SaveReader r)
    {
        if (!r.Expect(SaveFormat.TagStill)) return;
        r.ReadArray(MovedTick);
        r.ReadArray(StillAt);
        r.ReadArray(LastAttackTick);
        r.ReadArray(LastHurtTick);
        r.ReadArray(Held);
    }
}
