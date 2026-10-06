using System;
using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.ViewApi;

/// <summary>The debug overlay's flow-arrow layer (M2-5): which cells inside a square window around the camera get an arrow, and which way it points.</summary>
/// <remarks>
/// <see cref="Refresh"/> peeks the goal's field with <see cref="FlowFieldCache.PeekCached"/> on every call
/// (a peek changes nothing in the sim) and keeps no <see cref="FlowField"/> reference: a peeked
/// field is only valid for the current tick. It relists the arrows only when the goal cell, the
/// field's presence or <see cref="FlowField.Version"/>, or the window changes. A field's contents
/// depend only on the grid version and its target cell, so the same key means the same arrows; an
/// evicted field peeks as null and lists nothing. One arrow per window cell whose
/// <see cref="FlowField.DirectionAt"/> is not <see cref="FlowField.NoDirection"/> (blocked, unreached and target
/// cells have none). Allocates nothing after construction.
/// </remarks>
public sealed class FlowArrowLayout
{
    /// <summary>Window side in cells when none is given (80 m at 2 m cells).</summary>
    public const int DefaultWindow = 40;

    private readonly int[] _cells;
    private readonly byte[] _directions;
    private bool _valid;
    private int _goal, _version, _x0, _y0, _x1, _y1;

    /// <summary>Creates a layout for a square window of <paramref name="window"/> cells per side.</summary>
    public FlowArrowLayout(int window = DefaultWindow)
    {
        if (window < 1) throw new ArgumentOutOfRangeException(nameof(window));
        Window = window;
        _cells = new int[window * window];
        _directions = new byte[window * window];
    }

    /// <summary>Window side in cells.</summary>
    public int Window { get; }

    /// <summary>Arrows listed by the last refresh.</summary>
    public int Count { get; private set; }

    /// <summary>Cell index (<c>y * Width + x</c>) of each arrow, row-major inside the window.</summary>
    public ReadOnlySpan<int> Cells => _cells.AsSpan(0, Count);

    /// <summary>Direction (0-7, <see cref="FlowField"/>'s numbering) of each arrow, parallel to <see cref="Cells"/>.</summary>
    public ReadOnlySpan<byte> Directions => _directions.AsSpan(0, Count);

    /// <summary>The field's <see cref="FlowField.TargetCell"/> if it lies in the window, else -1: the cell the arrows lead to.</summary>
    public int MarkedCell { get; private set; } = -1;

    /// <summary>True if the last refresh found a current cached field for the goal.</summary>
    public bool HasField { get; private set; }

    /// <summary>Goal cell of the last refresh (-1 for none).</summary>
    public int Goal => _valid ? _goal : -1;

    /// <summary>Window of the last refresh: cells x in [<see cref="MinX"/>, <see cref="MaxX"/>), y in [<see cref="MinY"/>, <see cref="MaxY"/>).</summary>
    public int MinX => _x0;

    /// <inheritdoc cref="MinX"/>
    public int MinY => _y0;

    /// <inheritdoc cref="MinX"/>
    public int MaxX => _x1;

    /// <inheritdoc cref="MinX"/>
    public int MaxY => _y1;

    /// <summary>Times the arrows were relisted (test and debug readout).</summary>
    public int Rebuilds { get; private set; }

    /// <summary>The goal cell whose field the overlay shows: the goal cell (<c>UnitStore.GoalCell</c>) of the lowest-slot live selected unit that has one (&gt;= 0), else -1.</summary>
    /// <param name="selection">Selected handles, any order.</param>
    /// <param name="alive">The unit store's <c>Alive</c>.</param>
    /// <param name="generation">The unit store's <c>Generation</c>; a handle of another generation is skipped.</param>
    /// <param name="goalCell">The unit store's <c>GoalCell</c>.</param>
    public static int GoalOf(ReadOnlySpan<EntityHandle> selection, ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation, ReadOnlySpan<int> goalCell)
    {
        int bestSlot = int.MaxValue, goal = -1;
        for (int i = 0; i < selection.Length; i++)
        {
            EntityHandle h = selection[i];
            int slot = h.Index;
            if (slot >= bestSlot || (uint)slot >= (uint)alive.Length || (uint)slot >= (uint)generation.Length
                || (uint)slot >= (uint)goalCell.Length || !alive[slot] || generation[slot] != h.Generation)
                continue;
            int g = goalCell[slot];
            if (g < 0) continue;
            bestSlot = slot;
            goal = g;
        }
        return goal;
    }

    /// <summary>Unit ground vector (x, y in sim axes) of direction <paramref name="direction"/>, from the sim's own offset table.</summary>
    public static Vector2 DirectionVector(int direction) =>
        Vector2.Normalize(new Vector2(FlowField.OffsetX(direction), FlowField.OffsetY(direction)));

    /// <summary>Peeks <paramref name="goal"/>'s cached field and relists the arrows in the <see cref="Window"/>-cell square centred on the cell under <paramref name="focus"/> (meters), clipped to the map; returns true if the list changed.</summary>
    /// <param name="cache">The sim's field cache; only <see cref="FlowFieldCache.PeekCached"/> is called.</param>
    /// <param name="grid">The sim's grid (its size; the window is clipped to it).</param>
    /// <param name="goal">Goal cell from <see cref="GoalOf"/>; -1 (or any cell off the map) shows nothing.</param>
    /// <param name="focus">Camera focus in sim meters; non-finite values count as the map's corner (0, 0).</param>
    public bool Refresh(FlowFieldCache cache, NavGrid grid, int goal, Vector2 focus)
    {
        int w = grid.Width, h = grid.Height;
        int cx = CellOf(focus.X, w), cy = CellOf(focus.Y, h);
        int x0 = Math.Max(cx - Window / 2, 0), y0 = Math.Max(cy - Window / 2, 0);
        int x1 = Math.Min(cx - Window / 2 + Window, w), y1 = Math.Min(cy - Window / 2 + Window, h);
        FlowField? field = goal >= 0 ? cache.PeekCached(goal) : null;
        int version = field?.Version ?? -1;
        if (_valid && goal == _goal && version == _version && x0 == _x0 && y0 == _y0 && x1 == _x1 && y1 == _y1)
            return false;

        _valid = true;
        _goal = goal;
        _version = version;
        _x0 = x0;
        _y0 = y0;
        _x1 = x1;
        _y1 = y1;
        HasField = field != null;
        MarkedCell = -1;
        int n = 0;
        if (field != null)
        {
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int cell = y * w + x;
                    byte d = field.DirectionAt(cell);
                    if (d == FlowField.NoDirection) continue;
                    _cells[n] = cell;
                    _directions[n] = d;
                    n++;
                }
            }
            int t = field.TargetCell;
            if (t >= 0 && t % w >= x0 && t % w < x1 && t / w >= y0 && t / w < y1) MarkedCell = t;
        }
        Count = n;
        Rebuilds++;
        return true;
    }

    /// <summary>Forgets the last refresh, so the next one relists (call when the overlay turns on).</summary>
    public void Invalidate() => _valid = false;

    // Floor to a cell, clamped onto the map; NaN goes to 0.
    private static int CellOf(float meters, int size)
    {
        float f = meters / MapConstants.CellSize;
        return f >= 0f ? (int)MathF.Min(f, size - 1) : 0;
    }
}
