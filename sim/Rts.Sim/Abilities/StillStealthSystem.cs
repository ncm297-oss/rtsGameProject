using System.Numerics;
using Rts.Sim.Entities;

namespace Rts.Sim.Abilities;

/// <summary>
/// M8-2: a unit type's stand-still stealth (docs/factions/shadow.md "Unit notes", the Shadow Archer; docs/02 "Stealth and
/// detection"; docs/03 "Implementation (M8-2)"). A live, non-summoned unit whose type carries <c>stillStealth</c> is
/// Stealthed while at least the type's delay has passed since each of: (i) its position last changed, whatever the cause
/// (a walk, a chase, a shove; turning on the spot is not moving); (ii) its attack last landed or its cast last resolved;
/// (iii) it last lost hit points. The stealth is the type's status held on the unit through the status store, so the one
/// predicate (<see cref="World.IsStealthed"/>), detection, Revealed, targeting and the fog work as for any Stealthed entry.
/// </summary>
/// <remarks>
/// <para>
/// <b>The held entry.</b> As <see cref="ForestStealthSystem"/>'s: the rule shares its entry with the forest (when its faction
/// has the bonus and the status is the same) and with any timed application of the status. Each rule holding the entry adds
/// one tick to it (<see cref="StatusSystem.Holders"/>), which the countdown never takes and a timed application keeps on top
/// of its own duration; letting go takes its one back, and the entry ends only when no rule holds it and nothing timed is left.
/// </para>
/// <para>
/// <b>When.</b> Phase 5, after the forest's pass, before <see cref="StealthSystem.Run"/>. The pass compares each such unit's
/// position with where the last pass saw it (<see cref="StillStealthStore.StillAt"/>): a change dates the move to the tick
/// before (movement runs in phase 9, after phase 5), so a unit that last moved in tick t is hidden from tick t + delay's
/// phase 5; a unit seen for the first time (its spawn, or the first tick) is dated this tick. A unit whose position changed
/// since the last pass is never hidden in this one, whatever the delay. Losing hit points lets go at once
/// (<see cref="OnHpLost"/>, from <c>CombatSystem.HitUnit</c>), so a hidden unit hit in phase 11 is seen for the rest of the
/// tick. An attack landing or a cast resolving from stealth reveals the unit (the M4-5 rule), and the next phase 5 lets go.
/// Returns at once on data without the field. No allocation.
/// </para>
/// </remarks>
public static class StillStealthSystem
{
    /// <summary>Phase 5, after the forest's pass: every live unit of a type with the field is seen, then held or let go, in slot order.</summary>
    public static void Run(World world)
    {
        UnitStore u = world.Units;
        StillStealthStore? st = u.Still;
        if (st == null) return;
        int[] statusOf = world.StillStatusOfType;
        int tick = world.TickNumber;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            int status = statusOf[u.TypeId[i]];
            if (status < 0 || u.IsSummoned(i)) continue; // a summoned unit is never hidden: it keeps no state
            Vector2 at = u.Position[i];
            bool moved = false;
            if (st.MovedTick[i] < 0)
            {
                st.StillAt[i] = at;
                st.MovedTick[i] = tick;
            }
            else if (at != st.StillAt[i])
            {
                st.StillAt[i] = at;
                st.MovedTick[i] = tick - 1;
                moved = true;
            }
            bool qualifies = !moved && Qualifies(world, i, tick);
            if (qualifies && st.Held[i] == 0) Hold(world, i, status);
            else if (!qualifies && st.Held[i] != 0) Release(world, i, status);
        }
    }

    /// <summary>
    /// Whether live, non-summoned unit slot <paramref name="i"/> of a type with the field, seen this pass where it stood
    /// before, qualifies at <paramref name="tick"/>: the type's delay gone since its last move, its last landed attack or
    /// resolved cast, and its last hp loss (the last two never count as gone when they happened).
    /// </summary>
    internal static bool Qualifies(World world, int i, int tick)
    {
        StillStealthStore st = world.Units.Still!;
        int delay = world.StillDelayOfType[world.Units.TypeId[i]];
        if (tick - st.MovedTick[i] < delay) return false;
        int attacked = st.LastAttackTick[i], hurt = st.LastHurtTick[i];
        if (attacked >= 0 && tick - attacked < delay) return false;
        return hurt < 0 || tick - hurt >= delay;
    }

    /// <summary>
    /// The rule takes hold of unit slot <paramref name="i"/>: an entry of <paramref name="status"/> it already carries gains
    /// the rule's held tick; else a new entry of one tick is applied (counted and detected at once, as any first Stealthed).
    /// On a unit already carrying <see cref="StatusStore.PerUnit"/> other statuses the entry is dropped (the store's rule) and
    /// the unit is not held; the next pass tries again.
    /// </summary>
    private static void Hold(World world, int i, int status)
    {
        UnitStore u = world.Units;
        StatusStore s = u.Statuses;
        int at = s.IndexOf(i, status);
        if (at >= 0) s.TicksRemaining[at]++;
        else
        {
            StatusSystem.Hold(world, i, status, 0f, 1, u.Owner[i]);
            if (s.IndexOf(i, status) < 0) return;
        }
        u.Still!.Held[i] = 1;
    }

    /// <summary>
    /// The rule lets go of unit slot <paramref name="i"/>: its held entry loses the rule's held tick and ends now if no
    /// other rule holds it and no timed application of <paramref name="status"/> is left on it.
    /// </summary>
    private static void Release(World world, int i, int status)
    {
        UnitStore u = world.Units;
        StatusStore s = u.Statuses;
        u.Still!.Held[i] = 0;
        int at = s.IndexOf(i, status);
        if (at >= 0 && --s.TicksRemaining[at] <= 0) StatusSystem.RemoveEntry(world, i, at);
    }

    /// <summary>
    /// <c>CombatSystem.HitUnit</c>: unit slot <paramref name="i"/> lost hit points. For a non-summoned unit of a type with the
    /// field the tick is noted, and a held unit is let go now, so it is seen for the rest of the tick. Callers test
    /// <see cref="World.StillStealthInData"/> and that the hit took hit points.
    /// </summary>
    internal static void OnHpLost(World world, int i)
    {
        UnitStore u = world.Units;
        int status = world.StillStatusOfType[u.TypeId[i]];
        if (status < 0 || u.IsSummoned(i)) return;
        StillStealthStore st = u.Still!;
        st.LastHurtTick[i] = world.TickNumber;
        if (st.Held[i] != 0) Release(world, i, status);
    }

    /// <summary>
    /// Unit slot <paramref name="i"/>'s attack landed or its cast resolved (the moments <see cref="StealthSystem.OnAttackOrCast"/>
    /// reveals at): noted for a non-summoned unit of a type with the field. Callers test <see cref="World.StillStealthInData"/>.
    /// </summary>
    internal static void OnAttackOrCast(World world, int i)
    {
        UnitStore u = world.Units;
        if (world.StillStatusOfType[u.TypeId[i]] >= 0 && !u.IsSummoned(i)) u.Still!.LastAttackTick[i] = world.TickNumber;
    }
}
