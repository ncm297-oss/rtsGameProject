using System;
using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>
/// The view's casting rules over a selection (M4-V6a, docs/02 "Ability system"): which selected unit casts when the player
/// clicks a target, and what the command card's ability button shows. Read-only, allocation-free, no <see cref="World"/>:
/// the unit store's arrays come in as spans (<c>Alive</c>, <c>Generation</c>, <c>TypeId</c>, <c>Position</c>,
/// <c>CastAbility</c>, <c>AbilityReadyTick</c>).
/// </summary>
/// <remarks>
/// "Only the nearest selected caster casts" is the view's choice (docs/03 "For the view (M4-V6)"): the sim casts for every
/// <c>UseAbility</c> it gets. A caster is <em>ready</em> when it is live, its type lists the ability and its cooldown is
/// over (<c>AbilityReadyTick &lt;= tick</c>). Among the ready ones a caster already casting or walking to cast that same
/// ability is passed over while another is free (a second click with two mages selected starts the second mage, not a
/// recast of the first); when every ready one is busy, the nearest busy one recasts (a plain click replaces a cast, docs/03
/// "Implementation (M4-4a)"). A queued click never picks a busy one: its queued cast would pop after the resolve, on
/// cooldown, and be dropped. Ties go to the earlier unit in the selection.
/// </remarks>
public static class AbilityCaster
{
    /// <summary>The index of ability <paramref name="abilityId"/> in <paramref name="def"/>'s list (what <c>UseAbility</c> names), or -1.</summary>
    public static int IndexOf(UnitDef def, int abilityId)
    {
        ImmutableArray<int> list = def.Abilities;
        for (int k = 0; k < list.Length; k++)
            if (list[k] == abilityId) return k;
        return -1;
    }

    /// <summary>Ticks until unit slot <paramref name="slot"/> may use its ability <paramref name="index"/> again at tick <paramref name="tick"/> (0 = ready); <paramref name="readyTick"/> is <c>UnitStore.AbilityReadyTick</c>.</summary>
    public static int CooldownLeft(ReadOnlySpan<int> readyTick, int slot, int index, int tick) =>
        Math.Max(0, readyTick[slot * DataLimits.MaxUnitAbilities + index] - tick);

    /// <summary>
    /// The selected unit that casts ability <paramref name="abilityId"/> at <paramref name="point"/> (rules in the remarks):
    /// its slot, with its list index in <paramref name="index"/>; -1 (index -1) when no selected unit is ready.
    /// </summary>
    public static int PickCaster(ReadOnlySpan<EntityHandle> selection, ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation,
        ReadOnlySpan<int> typeId, ReadOnlySpan<Vector2> position, ReadOnlySpan<int> castAbility, ReadOnlySpan<int> readyTick,
        ImmutableArray<UnitDef> defs, int abilityId, int tick, Vector2 point, bool queued, out int index)
    {
        int best = -1;
        index = -1;
        bool bestBusy = true;
        float bestD = float.PositiveInfinity;
        foreach (EntityHandle h in selection)
        {
            int i = h.Index;
            if ((uint)i >= (uint)alive.Length || !alive[i] || generation[i] != h.Generation) continue;
            int k = IndexOf(defs[typeId[i]], abilityId);
            if (k < 0 || CooldownLeft(readyTick, i, k, tick) > 0) continue;
            bool busy = castAbility[i] == k;
            if (busy && queued) continue;
            float d = Vector2.DistanceSquared(position[i], point);
            // A free caster beats a busy one at any distance; then the nearer; a tie keeps the earlier.
            if (best >= 0 && (busy && !bestBusy || busy == bestBusy && d >= bestD)) continue;
            best = i;
            index = k;
            bestBusy = busy;
            bestD = d;
        }
        return best;
    }

    /// <summary>
    /// What the ability button shows: 0 when some selected unit with ability <paramref name="abilityId"/> is off cooldown,
    /// else the fewest ticks until one is; -1 when no live selected unit has it.
    /// </summary>
    public static int SoonestReady(ReadOnlySpan<EntityHandle> selection, ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation,
        ReadOnlySpan<int> typeId, ReadOnlySpan<int> readyTick, ImmutableArray<UnitDef> defs, int abilityId, int tick)
    {
        int soonest = -1;
        foreach (EntityHandle h in selection)
        {
            int i = h.Index;
            if ((uint)i >= (uint)alive.Length || !alive[i] || generation[i] != h.Generation) continue;
            int k = IndexOf(defs[typeId[i]], abilityId);
            if (k < 0) continue;
            int left = CooldownLeft(readyTick, i, k, tick);
            if (soonest < 0 || left < soonest) soonest = left;
            if (soonest == 0) return 0;
        }
        return soonest;
    }

    /// <summary>A cast's progress from 0 (just started) to 1 (resolving): the cast bar's fill. <paramref name="ticksLeft"/> is <c>UnitStore.CastTicks</c>, <paramref name="castTicks"/> the def's.</summary>
    public static float Progress(int ticksLeft, int castTicks) =>
        castTicks <= 0 ? 1f : Math.Clamp(1f - (float)ticksLeft / castTicks, 0f, 1f);
}
