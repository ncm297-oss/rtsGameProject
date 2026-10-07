using System.Numerics;
using System.Reflection;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M3-2b, session 2026-10-06-1744): independent checks for the "usable but stale" flow-field contract.
/// Reads the cache's slots by reflection so it sees every cached field, not just the ones a unit asks for.
/// </summary>
internal static class GridChangeOracle
{
    private const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>Every used cache slot's field (slot order).</summary>
    public static FlowField[] UsedFields(FlowFieldCache cache)
    {
        var fields = (FlowField[])typeof(FlowFieldCache).GetField("_fields", Priv)!.GetValue(cache)!;
        int count = (int)typeof(FlowFieldCache).GetField("_count", Priv)!.GetValue(cache)!;
        return fields.Take(count).ToArray();
    }

    /// <summary>
    /// For every cached field the cache calls usable (<c>PeekCached</c> hands it out): every cell with a direction
    /// is passable now, its step lands in a passable cell, and a diagonal step's two side cells are passable.
    /// Also: a field the cache hands out never has a different <c>BlockVersion</c> than the grid. Null if all hold.
    /// </summary>
    public static string? UsableFieldViolation(World w)
    {
        NavGrid g = w.NavGrid;
        FlowFieldCache cache = w.FlowFields;
        foreach (FlowField f in UsedFields(cache))
        {
            if (!ReferenceEquals(cache.PeekCached(f.RequestedCell), f)) continue;
            if (f.BlockVersion != g.BlockVersion) return $"field {f.RequestedCell} handed out with block version {f.BlockVersion} != grid {g.BlockVersion}";
            for (int c = 0; c < g.Width * g.Height; c++)
            {
                byte d = f.DirectionAt(c);
                if (d == FlowField.NoDirection) continue;
                int x = c % g.Width, y = c / g.Width;
                int dx = FlowField.OffsetX(d), dy = FlowField.OffsetY(d);
                if (!g.IsPassable(x, y))
                    return $"field {f.RequestedCell} (v{f.Version}/b{f.BlockVersion}, grid v{g.Version}/b{g.BlockVersion}) has direction {d} on blocked cell ({x}, {y})";
                if (!g.IsPassable(x + dx, y + dy))
                    return $"field {f.RequestedCell} (v{f.Version}/b{f.BlockVersion}) steps from ({x}, {y}) into blocked ({x + dx}, {y + dy})";
                if (dx != 0 && dy != 0 && !(g.IsPassable(x + dx, y) && g.IsPassable(x, y + dy)))
                    return $"field {f.RequestedCell} (v{f.Version}/b{f.BlockVersion}) cuts a blocked corner from ({x}, {y}) direction {d}";
            }
        }
        return null;
    }

    /// <summary>True if Moving unit <paramref name="i"/> stood still this tick waiting for its field (outside its goal cell, no usable field, or a stale one with no direction on its cell).</summary>
    public static bool WaitingForField(World w, int i)
    {
        UnitStore u = w.Units;
        if (!u.Alive[i] || u.State[i] != UnitState.Moving || u.Velocity[i] != Vector2.Zero) return false;
        NavGrid g = w.NavGrid;
        if (!g.WorldToCell(u.Position[i], out int x, out int y)) return false;
        int cell = y * g.Width + x;
        if (cell == u.GoalCell[i]) return false;
        FlowField? f = w.FlowFields.PeekCached(u.GoalCell[i]);
        return f == null || (f.Version != g.Version && f.DirectionAt(cell) == FlowField.NoDirection);
    }

    /// <summary>The first live unit with a non-finite position, or -1.</summary>
    public static int FirstNonFinite(World w)
    {
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y))) return i;
        return -1;
    }

    /// <summary>Live tree slots (wood nodes), in slot order.</summary>
    public static List<int> LiveTrees(World w)
    {
        var list = new List<int>();
        ResourceStore r = w.Resources;
        for (int n = 0; n < r.Capacity; n++)
            if (r.Alive[n] && w.Data.Resources[r.TypeId[n]].Resource == Data.ResourceKind.Wood) list.Add(n);
        return list;
    }

    /// <summary>True if a live unit's center lies in cell (x, y).</summary>
    public static bool UnitIn(World w, int x, int y)
    {
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && w.NavGrid.WorldToCell(u.Position[i], out int ux, out int uy) && ux == x && uy == y) return true;
        return false;
    }
}
