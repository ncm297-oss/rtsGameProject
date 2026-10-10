using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Abilities;

/// <summary>
/// Tick phase 5 (M4-4a, docs/03 "Implementation (M4-4a)"): every unit's statuses count down, damage over time lands, and
/// the expired ones end. Also the one place a unit's derived speed follows its slows, and (M4-4b-2) its blind in force
/// follows its blind statuses.
/// </summary>
/// <remarks>
/// Damage over time lands in pulses, one every <see cref="SimConstants.TicksPerSecond"/> ticks from the first application,
/// on the entry's own pulse clock (<see cref="StatusStore.PulseTicks"/>), which a refresh does not reset (BUG-0301): a 4 s
/// Burning lands 4 pulses, the first a second after it was applied and the last on its final tick; a fraction of a second
/// left at the end lands nothing. A pulse is one hit through <see cref="DamageCalc"/> with the magnitude (damage per second) as the
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
            bool slowChanged = false, blindChanged = false;
            int k = 0;
            while (k < s.Count[i])
            {
                int at = i * StatusStore.PerUnit + k;
                StatusDef def = data.Statuses[s.StatusId[at]];
                int left = --s.TicksRemaining[at];
                if (def.Kind == StatusKind.DamageOverTime && --s.PulseTicks[at] == 0)
                {
                    s.PulseTicks[at] = SimConstants.TicksPerSecond;
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
                else if (def.Kind == StatusKind.Blind) blindChanged = true;
                s.RemoveAt(i, at);
            }
            if (slowChanged && u.Alive[i]) RecomputeSpeed(world, i);
            if (blindChanged && u.Alive[i]) RecomputeBlind(world, i);
        }
    }

    /// <summary>
    /// Applies status <paramref name="status"/> to unit slot <paramref name="i"/> by the stacking rule (<see cref="StatusStore.Apply"/>)
    /// and recomputes its speed when a slow changed, its blind in force when a blind was added (M4-4b-2).
    /// </summary>
    internal static void Apply(World world, int i, int status, float magnitude, int ticks, int source)
    {
        bool changed = world.Units.Statuses.Apply(i, status, magnitude, ticks, source);
        if (!changed) return;
        StatusKind kind = world.Data.Statuses[status].Kind;
        if (kind == StatusKind.Slow) RecomputeSpeed(world, i);
        else if (kind == StatusKind.Blind) RecomputeBlind(world, i);
    }

    /// <summary>
    /// Unit slot <paramref name="i"/>'s blind in force (M4-4b-2, <see cref="StatusStore.BlindOf"/>): of its blind statuses
    /// the one with the smallest sight, then the smallest reach, then the lowest id; none when it has no blind. Called when
    /// its blinds change, not every tick.
    /// </summary>
    internal static void RecomputeBlind(World world, int i)
    {
        StatusStore s = world.Units.Statuses;
        int best = -1;
        int head = i * StatusStore.PerUnit;
        for (int k = 0; k < s.Count[i]; k++)
        {
            int id = s.StatusId[head + k];
            StatusDef def = world.Data.Statuses[id];
            if (def.Kind != StatusKind.Blind) continue;
            if (best >= 0)
            {
                StatusDef b = world.Data.Statuses[best];
                if (def.Sight > b.Sight || (def.Sight == b.Sight && (def.Reach > b.Reach || (def.Reach == b.Reach && id > best)))) continue;
            }
            best = id;
        }
        s.SetBlind(i, best);
    }

    /// <summary>
    /// The farthest unit slot <paramref name="i"/> takes or strikes a target (M4-4b-2): its blind's <see cref="StatusDef.Reach"/>
    /// (center to center; to the footprint for a building), or infinity when it is not blinded.
    /// </summary>
    internal static float ReachOf(World world, int i)
    {
        int b = world.Units.Statuses.BlindOf(i);
        return b < 0 ? float.PositiveInfinity : world.Data.Statuses[b].Reach;
    }

    /// <summary>Unit slot <paramref name="i"/>'s sight now (M4-4b-2): its type's, or its blind's when that is smaller. Its fog circle; derived, never hashed.</summary>
    public static float SightOf(World world, int i)
    {
        UnitStore u = world.Units;
        float sight = world.Data.Units[u.TypeId[i]].Sight;
        int b = u.Statuses.BlindOf(i);
        return b >= 0 && world.Data.Statuses[b].Sight < sight ? world.Data.Statuses[b].Sight : sight;
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
