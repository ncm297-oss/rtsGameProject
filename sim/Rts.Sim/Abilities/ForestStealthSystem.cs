using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Abilities;

/// <summary>
/// M8-1: a faction's forest stealth (docs/factions/shadow.md "Children of Shadow", docs/02 "Stealth and detection", docs/03
/// "Implementation (M8-1)"). A live, non-summoned unit of a faction whose data carries <c>forestStealth</c>, of a slot it does
/// not except, is Stealthed while (i) its own cell or one of the 8 round it holds a standing tree, (ii) at least the delay
/// has passed since its attack last landed or its cast last resolved, and (iii) at least the delay since it last lost hit
/// points. The stealth is the faction's status held on the unit through the status store, so the one predicate
/// (<see cref="World.IsStealthed"/>, <see cref="World.HiddenFrom"/>), detection, Revealed, targeting and the fog work as for
/// any Stealthed entry.
/// </summary>
/// <remarks>
/// <para>
/// <b>The held entry.</b> The status store keeps one entry per status id, so the forest shares its entry with any timed
/// application of the same status (an ability's). While the forest holds it (<see cref="ForestStealthStore.Held"/>), the entry's
/// <c>TicksRemaining</c> is one more than the timed applications left it (1 = none), counts down as ever but never below 1,
/// and a timed application onto it adds one (<see cref="StatusSystem.Hold"/>). Letting go takes the one back: the entry ends
/// at once when nothing timed is left, else runs out exactly when the timed application would have. So the forest never
/// shortens or lengthens a timed Stealthed, and a held entry always shows at least one tick left. M8-2: the stand-still
/// stealth (<see cref="StillStealthSystem"/>) may hold the same entry too and adds a tick of its own; the count of holders is
/// <see cref="StatusSystem.Holders"/>.
/// </para>
/// <para>
/// <b>When.</b> Phase 5, after the countdown, regeneration and zones, before <see cref="StealthSystem.Run"/>: a unit that
/// qualifies is held, one that no longer does is let go that tick (no linger). Losing hit points lets go at once
/// (<see cref="OnHpLost"/>, from <c>CombatSystem.HitUnit</c>), so a hidden unit hit in phase 11 is seen for the rest of that
/// tick. An attack landing from stealth reveals the unit (the M4-5 rule) and the next phase 5 lets go. The tree test reads 9
/// cells of the resource store's wood bits (<see cref="ResourceStore.IsWoodCell"/>), no spatial query; positions are as
/// movement left them in the last tick, so a unit that walks out of the trees in phase 9 is let go in the next tick's phase 5.
/// Returns at once in a match whose seats have no forest stealth. No allocation.
/// </para>
/// </remarks>
public static class ForestStealthSystem
{
    /// <summary>Phase 5, before the detection pass: every live unit of a seat with the forest stealth is held or let go, in slot order.</summary>
    public static void Run(World world)
    {
        if (!world.AnyForestStealth) return;
        UnitStore u = world.Units;
        byte[] held = u.Forest!.Held;
        int[] statusOf = world.ForestStatusOf;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            int owner = u.Owner[i];
            int status = (uint)owner < (uint)statusOf.Length ? statusOf[owner] : -1;
            if (status < 0) continue;
            bool qualifies = Qualifies(world, i, owner);
            if (qualifies && held[i] == 0) Hold(world, i, status);
            else if (!qualifies && held[i] != 0) Release(world, i, status);
        }
    }

    /// <summary>
    /// Whether live unit slot <paramref name="i"/> of <paramref name="owner"/> (a seat with the forest stealth) qualifies now:
    /// a slot not excepted, not summoned, the delay gone since its last landed attack or resolved cast and since its last hp
    /// loss (never counts as gone), and a standing tree in its cell or one of the 8 round it.
    /// </summary>
    internal static bool Qualifies(World world, int i, int owner)
    {
        UnitStore u = world.Units;
        if ((world.ForestExceptOf[owner] & (1 << (int)world.Data.Units[u.TypeId[i]].Slot)) != 0 || u.IsSummoned(i)) return false;
        int tick = world.TickNumber, delay = world.ForestDelayOf[owner];
        int attacked = u.Forest!.LastAttackTick[i], hurt = u.LastDamagedTick[i];
        if (attacked >= 0 && tick - attacked < delay) return false;
        if (hurt >= 0 && tick - hurt < delay) return false;
        return BesideTree(world, u.Position[i]);
    }

    /// <summary>Whether the cell holding <paramref name="position"/> or one of the 8 round it holds a standing tree: 9 bit reads.</summary>
    internal static bool BesideTree(World world, System.Numerics.Vector2 position)
    {
        NavGrid g = world.NavGrid;
        if (!g.WorldToCell(position, out int cx, out int cy)) return false;
        ResourceStore r = world.Resources;
        for (int y = cy - 1; y <= cy + 1; y++)
        {
            if ((uint)y >= (uint)g.Height) continue;
            for (int x = cx - 1; x <= cx + 1; x++)
                if ((uint)x < (uint)g.Width && r.IsWoodCell(y * g.Width + x)) return true;
        }
        return false;
    }

    /// <summary>
    /// The forest takes hold of unit slot <paramref name="i"/>: an entry of <paramref name="status"/> it already carries gains
    /// the one held tick; else a new entry of one tick is applied (counted and detected at once, as any first Stealthed).
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
        u.Forest!.Held[i] = 1;
    }

    /// <summary>
    /// The forest lets go of unit slot <paramref name="i"/>: its held entry loses the held tick and ends now if no timed
    /// application of <paramref name="status"/> is left on it (the stealth count and detection follow at once).
    /// </summary>
    private static void Release(World world, int i, int status)
    {
        UnitStore u = world.Units;
        StatusStore s = u.Statuses;
        u.Forest!.Held[i] = 0;
        int at = s.IndexOf(i, status);
        if (at >= 0 && --s.TicksRemaining[at] <= 0) StatusSystem.RemoveEntry(world, i, at);
    }

    /// <summary>
    /// <c>CombatSystem.HitUnit</c>: unit slot <paramref name="i"/> lost hit points (its <see cref="UnitStore.LastDamagedTick"/>
    /// is this tick's). A held unit is let go now, so it is seen for the rest of the tick; the next passes hold it again only
    /// once the delay has passed. Callers test <see cref="World.AnyForestStealth"/> first.
    /// </summary>
    internal static void OnHpLost(World world, int i)
    {
        UnitStore u = world.Units;
        if (u.Forest!.Held[i] == 0) return;
        Release(world, i, world.ForestStatusOf[u.Owner[i]]);
    }

    /// <summary>
    /// Unit slot <paramref name="i"/>'s attack landed or its cast resolved (the moments <see cref="StealthSystem.OnAttackOrCast"/>
    /// reveals at): noted for a unit of a seat with the forest stealth. Callers test <see cref="World.AnyForestStealth"/> first.
    /// </summary>
    internal static void OnAttackOrCast(World world, int i)
    {
        UnitStore u = world.Units;
        int owner = u.Owner[i];
        if ((uint)owner < (uint)world.ForestStatusOf.Length && world.ForestStatusOf[owner] >= 0) u.Forest!.LastAttackTick[i] = world.TickNumber;
    }
}
