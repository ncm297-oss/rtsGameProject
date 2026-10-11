using System;
using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Abilities;

/// <summary>
/// Summons (M4-4b-3b, docs/03 "Implementation (M4-4b-3b)"): a <see cref="AbilityKind.Summon"/> ability's <c>spawn</c> effects
/// put temporary units beside the caster at the resolve (phase 6), and the expiry pass at the start of phase 5 takes them
/// away when their lifetime is over.
/// </summary>
/// <remarks>
/// A summoned unit is an ordinary unit (it moves, fights, takes statuses, dies) that costs nothing, takes no population and
/// carries <see cref="UnitStore.ExpiresAtTick"/>. Its expiry is a death without a killer: a <see cref="DeathEvent"/> with
/// <see cref="DeathEvent.Expired"/>, no kill or loss, no <c>onDeath</c> passive. Cost rule: the expiry pass returns at once
/// while no summoned unit lives (<see cref="UnitStore.SummonedUnits"/>); placement reads only the few cells round the caster
/// and borrows the flow-field builder's idle scratch. No allocation.
/// </remarks>
public static class SummonSystem
{
    /// <summary>
    /// How far round the caster's cell a summon looks for free cells, in rings (ring 1: the 8 cells next to it; 3 rings are
    /// 48 cells, 6 m each way). A cell farther than that is not "beside the caster": a summon in a crowd drops the units that
    /// find no cell within it. A rule of the mechanism.
    /// </summary>
    internal const int SearchRings = 3;

    /// <summary>
    /// Phase 5, first (before the passives and the status countdown): every live summoned unit whose
    /// <see cref="UnitStore.ExpiresAtTick"/> is this tick is taken away, in slot order, through the death path (see
    /// <see cref="Expire"/>).
    /// </summary>
    public static void Run(World world)
    {
        UnitStore u = world.Units;
        if (u.SummonedUnits == 0) return;
        int now = world.TickNumber;
        for (int i = 0; i < u.Capacity; i++)
        {
            int end = u.ExpiresAtTick[i];
            if (end == 0 || end > now || !u.Alive[i]) continue;
            Expire(world, i);
            if (u.SummonedUnits == 0) return;
        }
    }

    /// <summary>
    /// Live unit slot <paramref name="i"/>'s lifetime is over: a <see cref="DeathEvent"/> with <see cref="DeathEvent.Expired"/>
    /// (killer -1; no kill or loss counted, no <c>onDeath</c> passive) and the slot freed (its statuses, cast and target with it).
    /// </summary>
    private static void Expire(World world, int i)
    {
        UnitStore u = world.Units;
        var handle = new EntityHandle(i, u.Generation[i]);
        world.RecordDeath(new DeathEvent(handle, false, u.TypeId[i], u.Owner[i], -1, u.Position[i], Expired: true));
        u.Free(handle);
    }

    /// <summary>
    /// The resolve of summon <paramref name="a"/> cast by <paramref name="owner"/>'s unit standing at <paramref name="point"/>:
    /// each <c>spawn</c> effect in file order puts its count of units, one by one, on the free cell nearest the point within
    /// <see cref="SearchRings"/> rings of the caster's cell, on its plateau (<see cref="FreeCellSearch"/>: nearest ring first,
    /// then nearest the point, ties to the lower cell). Full hit points (the owner's hp techs included), no cost, no
    /// population; each ends in the phase 5 of the tick <c>lifetime</c> ticks after this one. A full unit store or no free
    /// cell left drops the rest. The new units go into the spatial hash at once, so later resolves this tick see them.
    /// </summary>
    internal static void Summon(World world, int owner, AbilityDef a, Vector2 point)
    {
        NavGrid g = world.NavGrid;
        if (!g.WorldToCell(point, out int cx, out int cy)) return;
        UnitStore u = world.Units;
        // The builder's per-cell scratch is idle outside a field build (as for production's spawns in phase 3); FreeCellSearch
        // caches each cell's answer in it (0 not asked yet), so the cells it may read are cleared first.
        int[] taken = world.FlowFields.BuildScratch;
        int x0 = Math.Max(0, cx - SearchRings), x1 = Math.Min(g.Width - 1, cx + SearchRings);
        for (int y = Math.Max(0, cy - SearchRings); y <= Math.Min(g.Height - 1, cy + SearchRings); y++)
            Array.Clear(taken, y * g.Width + x0, x1 - x0 + 1);
        int expires = world.TickNumber;
        bool spawned = false;
        foreach (AbilityEffect fx in a.Effects)
        {
            if (fx.Kind != AbilityEffectKind.Spawn) continue;
            UnitDef def = world.Data.Units[fx.Unit];
            for (int k = 0; k < fx.Count; k++)
            {
                if (u.FreeCount == 0) break;
                int cell = FreeCellSearch.NearestWithin(world, cx, cy, 1, 1, point, taken, SearchRings);
                if (cell < 0) break;
                u.TrySpawn(owner, fx.Unit, def, g.CellCenter(cell % g.Width, cell / g.Width), expires + fx.LifetimeTicks, out _);
                taken[cell] = 2;
                spawned = true;
            }
        }
        if (spawned) world.Spatial.Rebuild(u);
    }
}
