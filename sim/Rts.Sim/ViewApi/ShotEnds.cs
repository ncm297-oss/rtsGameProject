using System;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>
/// Who fired a live projectile and at whom (M7-VH1, BUG-1179): a copy of one slot's attacker type and victim handle, read
/// from <see cref="ProjectileStore"/>'s internal arrays for the view, which draws an aimed shot from its shooter's height to
/// its victim's ("Implementation (M7-VH1)"). Read-only: <see cref="Of"/> copies values out; nothing here writes the store,
/// the units or the tick, so drawing changes no state hash. No allocation (a struct).
/// </summary>
public readonly struct ShotEnds
{
    private ShotEnds(int attackerType, bool attackerIsBuilding, EntityHandle attacker, EntityHandle victim, bool victimIsBuilding)
    {
        Live = true;
        AttackerType = attackerType;
        AttackerIsBuilding = attackerIsBuilding;
        Attacker = attacker;
        Victim = victim;
        VictimIsBuilding = victimIsBuilding;
    }

    /// <summary>Slot <paramref name="slot"/>'s ends; <see cref="Live"/> false (and every field default) for a free or out-of-range slot.</summary>
    public static ShotEnds Of(ProjectileStore store, int slot)
    {
        if ((uint)slot >= (uint)store.Capacity || !store.Alive[slot]) return default;
        return new ShotEnds(store.AttackerType[slot], store.AttackerIsBuilding[slot], store.Attacker[slot],
            store.Victim[slot], store.VictimIsBuilding[slot]);
    }

    /// <summary>Whether the slot held a shot in flight when read.</summary>
    public bool Live { get; }

    /// <summary>The type whose attack the shot carries: a unit type id, or a building type id when <see cref="AttackerIsBuilding"/> (a tower).</summary>
    public int AttackerType { get; }

    /// <summary>Fired by a building (a tower).</summary>
    public bool AttackerIsBuilding { get; }

    /// <summary>The unit (or building) that fired it; it may be dead by now, its type is still <see cref="AttackerType"/>.</summary>
    public EntityHandle Attacker { get; }

    /// <summary>What it was fired at: a unit handle, or a building's when <see cref="VictimIsBuilding"/>.</summary>
    public EntityHandle Victim { get; }

    /// <summary>Whether <see cref="Victim"/> is a building.</summary>
    public bool VictimIsBuilding { get; }

    /// <summary>The unit type that fired it; -1 when it is not live or a building fired it.</summary>
    public int ShooterUnitType => Live && !AttackerIsBuilding ? AttackerType : -1;

    /// <summary>
    /// The victim's unit type: the live unit's, or a dead victim's while its slot has not been taken again (a freed unit slot
    /// keeps its type until <see cref="UnitStore.TryAlloc"/> resets it, and its generation moves on by exactly one at the
    /// death). -1 when not live, a building victim, or the victim's slot already holds another unit.
    /// </summary>
    /// <remarks>
    /// Units spawn in phases 3-5 and die from phase 6 on, so a view that reads every tick always sees a shot's victim alive
    /// or as its corpse on the shot's firing tick (the shot is fired in phase 10 at a live unit).
    /// </remarks>
    /// <param name="alive">The units' <c>Alive</c>.</param>
    /// <param name="generation">The units' <c>Generation</c>.</param>
    /// <param name="typeId">The units' <c>TypeId</c>.</param>
    public int VictimUnitType(ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation, ReadOnlySpan<int> typeId)
    {
        if (!Live || VictimIsBuilding) return -1;
        int i = Victim.Index;
        if ((uint)i >= (uint)alive.Length || i >= generation.Length || i >= typeId.Length) return -1;
        int g = generation[i];
        // Alive with the handle's generation: the victim; dead one generation on: its corpse, type untouched.
        bool victim = alive[i] ? g == Victim.Generation : g == Victim.Generation + 1;
        return victim ? typeId[i] : -1;
    }
}
