using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Abilities;

/// <summary>
/// Tick phase 5 (M4-4a, docs/03 "Implementation (M4-4a)"): every unit's statuses count down, damage over time lands, and
/// the expired ones end. Also the one place a unit's derived speed follows its slows.
/// </summary>
/// <remarks>
/// Damage over time lands in pulses, one each time an entry's remaining ticks reach a multiple of
/// <see cref="SimConstants.TicksPerSecond"/> (the last on its final tick): a 4 s Burning lands 4 pulses, the first a second
/// after it was applied. A pulse is one hit through <see cref="DamageCalc"/> with the magnitude (damage per second) as the
/// attack value, of the status's damage type, against the victim's armor class and armor (Magic ignores armor), so its
/// rounding is the combat rule's. The kill is credited to the entry's source player; nobody retaliates on a pulse. Units in
/// slot order, each unit's entries in order; no allocation.
/// </remarks>
public static class StatusSystem
{
    /// <summary>Phase 5: counts every status down, lands damage-over-time pulses, and ends the expired ones.</summary>
    public static void Run(World world)
    {
        UnitStore u = world.Units;
        StatusStore s = u.Statuses;
        GameData data = world.Data;
        if (s.UnitsWithStatuses == 0) return;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (s.Count[i] == 0 || !u.Alive[i]) continue;
            int generation = u.Generation[i];
            bool slowChanged = false;
            int k = 0;
            while (k < s.Count[i])
            {
                int at = i * StatusStore.PerUnit + k;
                StatusDef def = data.Statuses[s.StatusId[at]];
                int left = --s.TicksRemaining[at];
                if (def.Kind == StatusKind.DamageOverTime && left % SimConstants.TicksPerSecond == 0)
                {
                    int damage = PulseDamage(world, i, def.DamageType, s.Magnitude[at]);
                    CombatSystem.HitUnit(world, new PendingHit(default, s.SourcePlayer[at], new EntityHandle(i, generation), false, damage));
                    if (!u.Alive[i]) break; // died: the free cleared its statuses
                }
                if (left > 0)
                {
                    k++;
                    continue;
                }
                if (def.Kind == StatusKind.Slow) slowChanged = true;
                s.RemoveAt(i, at);
            }
            if (slowChanged && u.Alive[i]) RecomputeSpeed(world, i);
        }
    }

    /// <summary>
    /// Applies status <paramref name="status"/> to unit slot <paramref name="i"/> by the stacking rule (<see cref="StatusStore.Apply"/>)
    /// and recomputes its speed when a slow changed.
    /// </summary>
    internal static void Apply(World world, int i, int status, float magnitude, int ticks, int source)
    {
        bool changed = world.Units.Statuses.Apply(i, status, magnitude, ticks, source);
        if (changed && world.Data.Statuses[status].Kind == StatusKind.Slow) RecomputeSpeed(world, i);
    }

    /// <summary>
    /// Unit slot <paramref name="i"/>'s speed from its type and its strongest slow: <c>speed x (1 - magnitude)</c> (docs/02
    /// "Status effects"). Called when its slows change, not every tick.
    /// </summary>
    internal static void RecomputeSpeed(World world, int i)
    {
        UnitStore u = world.Units;
        StatusStore s = u.Statuses;
        float slow = 0f;
        int head = i * StatusStore.PerUnit;
        for (int k = 0; k < s.Count[i]; k++)
            if (world.Data.Statuses[s.StatusId[head + k]].Kind == StatusKind.Slow && s.Magnitude[head + k] > slow) slow = s.Magnitude[head + k];
        u.Speed[i] = world.Data.Units[u.TypeId[i]].SpeedPerTick * (1f - slow);
    }

    /// <summary>One damage-over-time pulse on unit slot <paramref name="i"/>: <paramref name="perSecond"/> as an attack of <paramref name="damageType"/>, its armor techs applied.</summary>
    private static int PulseDamage(World world, int i, int damageType, float perSecond)
    {
        UnitStore u = world.Units;
        UnitDef vdef = world.Data.Units[u.TypeId[i]];
        int armor = vdef.Armor + DamageCalc.Points(world.Techs.Bonus(u.Owner[i], u.TypeId[i], TechStat.Armor));
        return DamageCalc.Compute(world.Data.DamageTable, damageType, vdef.ArmorClass, DamageCalc.Points(perSecond), 1f, armor);
    }
}
