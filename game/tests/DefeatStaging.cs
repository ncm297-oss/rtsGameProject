using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Sim;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V7 (STATE Requests 40): how the match-over scenes stage a defeat so it lands on the same tick under both win rules,
/// M5-4b's (a player is out when its last building, sites included, goes) and M5-4e's (out when it has no live unit and no
/// finished unit-producing building). Every loser owns exactly one building, a finished training hall (it trains a unit, so
/// it keeps its owner in under the new rule too), and has no live unit when that hall falls: then both rules defeat it on
/// the hall's tick. Never a House or a site (the new rule doesn't count them), never a loser with a worker alive (the old
/// rule doesn't count it).
/// </summary>
/// <remarks>
/// Simultaneous falls (a Draw, "0 + 1 fall together"): the halls are translated copies of one another, each shelled by an
/// identical battery of stationary siege shooters (<see cref="Battery"/>) at the same offsets, ordered on one tick. Combat
/// has no randomness and ignores terrain height, and the shooters never move, so each hall takes the same hits on the same
/// ticks and they fall together, provided gun k of every battery scans on the same ticks: a unit in slot s scans when
/// <c>(tick + s) % ScanInterval == 0</c>, so the batteries are <see cref="BatterySize"/> = <c>ScanInterval</c> guns spawned in
/// one go (consecutive slots: gun k of each battery on one phase), checked by <see cref="SamePhase"/>. A player that never built (a dev seat holding only the battery) is outside both rules'
/// match-over count, so it can be the executioner of a Draw.
/// </remarks>
public static class DefeatStaging
{
    /// <summary>Cells between two halls of a row (and so between two batteries): far enough that no splash crosses.</summary>
    public const int Stride = 14;

    /// <summary>Shooters per battery: one scan cycle, so batteries spawned back to back put gun k of each on one scan phase.</summary>
    public const int BatterySize = CombatConstants.ScanInterval;

    /// <summary>Whether gun k of every battery scans on the same ticks (slots congruent modulo <see cref="CombatConstants.ScanInterval"/>), so they fire together.</summary>
    public static bool SamePhase(EntityHandle[][] guns)
    {
        for (int b = 1; b < guns.Length; b++)
            for (int k = 0; k < guns[b].Length; k++)
                if ((guns[b][k].Index - guns[0][k].Index) % CombatConstants.ScanInterval != 0) return false;
        return true;
    }

    /// <summary>The training hall a loser owns: its faction's infantry hall (finished, it trains the Line slot).</summary>
    public static int Hall(GameData data, World w, int player) => StartBase.BuildingOfSlot(data, w.FactionOf(player), BuildingSlot.InfantryHall);

    /// <summary>A stationary siege shooter (the first Siege-slot unit with a range of 12 m or more: the catapult); a dev spawn may give it to any seat.</summary>
    public static int Shooter(GameData data)
    {
        for (int t = 0; t < data.Units.Length; t++)
            if (data.Units[t].Slot == UnitSlot.Siege && data.Units[t].Attack.Range >= 12f) return t;
        throw new InvalidOperationException("no ranged siege unit in the data");
    }

    /// <summary>Whether a dev <c>SpawnBuilding</c> of <paramref name="type"/> at <paramref name="anchor"/> lands: the placement rule's map checks pass (unexplored ground, cost and a defeat don't stop a dev spawn) and no live unit, own or not, stands on the footprint (the spawn refuses one).</summary>
    public static bool SpawnSpot(World w, int player, int type, int anchor)
    {
        if (!w.CanPlace(player, type, anchor, out PlacementError e) && e is not (PlacementError.Unexplored or PlacementError.CannotAfford or PlacementError.Defeated)) return false;
        NavGrid g = w.NavGrid;
        BuildingDef def = w.Data.Buildings[type];
        Vector2 min = g.CellCenter(anchor % g.Width, anchor / g.Width) - new Vector2(MapConstants.CellSize / 2), max = min + new Vector2(def.FootprintWidth, def.FootprintHeight) * MapConstants.CellSize;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Vector2 p = u.Position[i];
            if (p.X >= min.X && p.X < max.X && p.Y >= min.Y && p.Y < max.Y) return false;
        }
        return true;
    }

    /// <summary>The nearest anchor to (<paramref name="x0"/>, <paramref name="y0"/>) where <see cref="SpawnSpot"/> holds.</summary>
    public static int Anchor(World w, int player, int type, int x0, int y0)
    {
        NavGrid g = w.NavGrid;
        for (int r = 0; r < 40; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = x0 + dx, y = y0 + dy;
                    if (g.InBounds(x, y) && SpawnSpot(w, player, type, y * g.Width + x)) return y * g.Width + x;
                }
        throw new InvalidOperationException($"no spawn anchor for player {player} near ({x0}, {y0})");
    }

    /// <summary>A dev spawn of <paramref name="type"/> for <paramref name="player"/> at <paramref name="anchor"/>.</summary>
    public static void SpawnAt(Simulation sim, int player, int type, int anchor)
    {
        NavGrid g = sim.World.NavGrid;
        sim.Enqueue(Command.SpawnBuilding(player, type, g.CellCenter(anchor % g.Width, anchor / g.Width)));
    }

    /// <summary>The owners' halls (<see cref="Hall"/>), and whether they are alike for a simultaneous fall: one footprint, hp and armor, no attack of their own.</summary>
    public static int[] Halls(GameData data, World w, int[] owners, out bool alike)
    {
        var types = new int[owners.Length];
        for (int k = 0; k < owners.Length; k++) types[k] = Hall(data, w, owners[k]);
        BuildingDef a = data.Buildings[types[0]];
        alike = true;
        foreach (int t in types)
        {
            BuildingDef d = data.Buildings[t];
            alike &= d.FootprintWidth == a.FootprintWidth && d.FootprintHeight == a.FootprintHeight && d.Hp == a.Hp && d.Armor == a.Armor && d.Attack == null;
        }
        return types;
    }

    /// <summary>
    /// Anchors for one hall per owner in a row, <see cref="Stride"/> cells apart, hall k of <paramref name="types"/>[k] (alike
    /// footprints, <see cref="Halls"/>) a <see cref="SpawnSpot"/> for <paramref name="owners"/>[k], each with its battery's cells
    /// (and their neighbours) open: the first such row from the map's middle outwards.
    /// </summary>
    public static int[] Row(World w, int[] owners, int[] types)
    {
        NavGrid g = w.NavGrid;
        BuildingDef def = w.Data.Buildings[types[0]];
        int span = Stride * (owners.Length - 1) + def.FootprintWidth + 2;
        int cx = (g.Width - span) / 2, cy = g.Height / 2 - def.FootprintHeight;
        for (int r = 0; r < Math.Max(g.Width, g.Height); r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int bx = cx + dx, by = cy + dy;
                    if (Fits(w, owners, types, bx, by)) return Anchors(g, owners.Length, bx, by);
                }
        throw new InvalidOperationException($"no open row of {owners.Length} halls on the map");
    }

    /// <summary>Where battery <paramref name="k"/>'s shooters stand for a hall at <paramref name="anchor"/>: <see cref="BatterySize"/> cells two apart, four cells below the footprint (about 9 m from it: past the catapult's minimum range, inside its sight).</summary>
    public static Vector2[] Battery(NavGrid g, BuildingDef def, int anchor)
    {
        var at = new Vector2[BatterySize];
        int ax = anchor % g.Width, ay = anchor / g.Width;
        for (int k = 0; k < BatterySize; k++) at[k] = g.CellCenter(ax - 1 + 2 * k, ay + def.FootprintHeight + 4);
        return at;
    }

    /// <summary>The live unit of <paramref name="player"/> standing on <paramref name="at"/> (within 0.5 m), or default.</summary>
    public static EntityHandle UnitAt(World w, int player, Vector2 at)
    {
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == player && Vector2.Distance(u.Position[i], at) < 0.5f) return new EntityHandle(i, u.Generation[i]);
        return default;
    }

    /// <summary>The live building of <paramref name="player"/> anchored at <paramref name="anchor"/>, or default.</summary>
    public static EntityHandle BuildingAt(World w, int player, int anchor)
    {
        BuildingStore b = w.Buildings;
        int k = b.SlotAt(anchor % w.NavGrid.Width, anchor / w.NavGrid.Width);
        return k >= 0 && b.Owner[k] == player ? new EntityHandle(k, b.Generation[k]) : default;
    }

    /// <summary>Live units of <paramref name="player"/>.</summary>
    public static List<EntityHandle> UnitsOf(World w, int player)
    {
        var list = new List<EntityHandle>();
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == player) list.Add(new EntityHandle(i, u.Generation[i]));
        return list;
    }

    /// <summary>
    /// Three of <paramref name="killer"/>'s Line units beside each live unit of <paramref name="victim"/>, each ordered to attack
    /// it (enqueue now, then <see cref="AttackVictims"/> after a tick): how a loser loses its scout before its hall falls.
    /// </summary>
    public static void SpawnKillers(Simulation sim, GameData data, int killer, int victim)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int line = StartBase.UnitOfSlot(data, w.FactionOf(killer), UnitSlot.Line);
        foreach (EntityHandle v in UnitsOf(w, victim))
        {
            g.WorldToCell(w.Units.Position[v.Index], out int vx, out int vy);
            for (int k = 0; k < 3; k++)
            {
                int c = Rts.Sim.Pathfinding.FlowField.NearestPassable(g, Math.Clamp(vy + 1, 0, g.Height - 1) * g.Width + Math.Clamp(vx - 1 + k, 0, g.Width - 1));
                sim.Enqueue(Command.SpawnUnit(killer, line, g.CellCenter(c % g.Width, c / g.Width)));
            }
        }
    }

    /// <summary>Every live unit of <paramref name="killer"/> attacks the first live unit of <paramref name="victim"/>; false when the victim has none.</summary>
    public static bool AttackVictims(Simulation sim, int killer, int victim)
    {
        List<EntityHandle> theirs = UnitsOf(sim.World, victim);
        if (theirs.Count == 0) return false;
        foreach (EntityHandle u in UnitsOf(sim.World, killer)) sim.Enqueue(Command.Attack(killer, u, theirs[0], isBuilding: false));
        return true;
    }

    private static int[] Anchors(NavGrid g, int n, int bx, int by)
    {
        var a = new int[n];
        for (int k = 0; k < n; k++) a[k] = by * g.Width + bx + k * Stride;
        return a;
    }

    private static bool Fits(World w, int[] owners, int[] types, int bx, int by)
    {
        NavGrid g = w.NavGrid;
        for (int k = 0; k < owners.Length; k++)
        {
            BuildingDef def = w.Data.Buildings[types[k]];
            int ax = bx + k * Stride;
            if (!g.InBounds(ax - 2, by) || !g.InBounds(ax + def.FootprintWidth + 2, by + def.FootprintHeight + 6)) return false;
            if (!SpawnSpot(w, owners[k], types[k], by * g.Width + ax)) return false;
            foreach (Vector2 p in Battery(g, def, by * g.Width + ax))
            {
                g.WorldToCell(p, out int x, out int y);
                for (int ny = y - 1; ny <= y + 1; ny++)
                    for (int nx = x - 1; nx <= x + 1; nx++)
                        if (!g.IsPassable(nx, ny)) return false;
            }
        }
        return true;
    }
}
