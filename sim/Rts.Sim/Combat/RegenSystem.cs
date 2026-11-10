using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Combat;

/// <summary>
/// Tick phase 5, after <see cref="StatusSystem.Run"/> (M7-1, docs/03 "Implementation (M7-1)"): regeneration. Every live
/// unit, in slot order, whose rate is above 0 and whose hit points are below its maximum (<see cref="World.MaxHp"/>, its
/// owner's hp techs included) gains hit points through an integer fixed-point accumulator
/// (<see cref="CombatConstants.RegenFixedOne"/> units = 1 HP), never above the maximum; at the maximum the accumulator is
/// cleared.
/// </summary>
/// <remarks>
/// The rate is the sum of two parts. The faction's (<c>faction.json</c> <c>regen</c>, <see cref="FactionDef.RegenPerTick"/>)
/// applies once the unit has gone <see cref="FactionDef.RegenDelayTicks"/> ticks without damage
/// (<see cref="UnitStore.LastDamagedTick"/>: a unit hit in tick t heals from tick t + delay on), or always if it was never
/// damaged. Each regenerating status's (<see cref="StatusKind.Regenerating"/>, magnitude HP per second) applies whenever the
/// unit carries it, through the faction's delay. A hit doesn't clear the accumulator: the fraction already gained is kept.
/// Skipped in O(1) when no faction of the match regenerates and no unit carries a regenerating status, so a match without
/// either pays nothing and hashes as before. No allocation.
/// </remarks>
public static class RegenSystem
{
    /// <summary>Phase 5: every live unit below its maximum gains its regeneration for this tick.</summary>
    public static void Run(World world)
    {
        UnitStore u = world.Units;
        StatusStore s = u.Statuses;
        if (!world.AnyFactionRegen && s.RegeneratingUnits == 0) return;
        int tick = world.TickNumber;
        int[] rateOf = world.RegenPerTickOf, delayOf = world.RegenDelayOf;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            int owner = u.Owner[i];
            int rate = s.HasRegen(i) ? StatusRate(world, i) : 0;
            if ((uint)owner < (uint)rateOf.Length && rateOf[owner] > 0
                && (u.LastDamagedTick[i] < 0 || tick - u.LastDamagedTick[i] >= delayOf[owner]))
                rate += rateOf[owner];
            if (rate == 0 && u.RegenAccum[i] == 0) continue;
            int max = world.MaxHp(i);
            if (u.Hp[i] >= max)
            {
                u.RegenAccum[i] = 0;
                continue;
            }
            if (rate == 0) continue; // waiting out the delay: the fraction gained so far is kept
            int acc = u.RegenAccum[i] + rate;
            int hp = u.Hp[i] + acc / CombatConstants.RegenFixedOne;
            acc %= CombatConstants.RegenFixedOne;
            if (hp >= max)
            {
                hp = max;
                acc = 0;
            }
            u.Hp[i] = hp;
            u.RegenAccum[i] = acc;
        }
    }

    /// <summary>
    /// Unit slot <paramref name="i"/>'s regenerating statuses' rate in fixed-point HP a tick: each entry's magnitude (HP per
    /// second) converted as the loader converts a faction's (<see cref="DataLoader.RegenPerTick"/>), summed.
    /// </summary>
    internal static int StatusRate(World world, int i)
    {
        StatusStore s = world.Units.Statuses;
        int rate = 0;
        int head = i * StatusStore.PerUnit;
        for (int k = 0; k < s.Count[i]; k++)
            if (world.Data.Statuses[s.StatusId[head + k]].Kind == StatusKind.Regenerating)
                rate += DataLoader.RegenPerTick(s.Magnitude[head + k]);
        return rate;
    }
}
