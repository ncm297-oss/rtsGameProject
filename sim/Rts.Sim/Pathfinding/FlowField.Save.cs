using System;
using Rts.Sim.Save;

namespace Rts.Sim.Pathfinding;

// M6-S2: one cached field in a save (docs/03 "Save/load and replays": store the contents, don't rebuild them from the key,
// BUG-0081). Its keys and versions, its direction bytes, and its costs: written raw only when they don't follow from the
// directions. Dijkstra gives each reached cell its cost as exactly the float cost[next] + step of the neighbour its direction
// names (a tie only re-points it at a neighbour giving the same float), so the costs are rebuilt bit for bit from the
// directions; the writer checks that rule on every cell first and falls back to the raw costs if any cell breaks it.
public sealed partial class FlowField
{
    private const byte CostsFromDirections = 0, CostsRaw = 1;

    /// <summary>Writes the keys, versions, directions and (when they don't follow from the directions) the costs (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Write(RequestedCell);
        w.Write(TargetCell);
        w.Write(Version);
        w.Write(BlockVersion);
        bool derived = CostsFollowDirections();
        w.Write(derived ? CostsFromDirections : CostsRaw);
        w.WriteArray(_direction);
        if (!derived) w.WriteArray(_cost);
    }

    /// <summary>
    /// Reads what <see cref="WriteTo"/> wrote into this field of the same size; rebuilds the costs from the directions when
    /// they were left out, failing on a direction off the map or a cycle. <paramref name="stack"/> and <paramref name="state"/>
    /// are the load's cell-sized scratch.
    /// </summary>
    internal void ReadFrom(SaveReader r, int[] stack, byte[] state)
    {
        int cells = _cost.Length;
        RequestedCell = r.ReadInt32In(0, cells - 1);
        TargetCell = r.ReadInt32In(-1, cells - 1);
        Version = r.ReadInt32();
        BlockVersion = r.ReadInt32();
        byte mode = r.ReadByte();
        r.ReadArray(_direction);
        if (r.Failed) return;
        if (mode == CostsRaw)
        {
            r.ReadArray(_cost);
            return;
        }
        if (!r.Check(mode == CostsFromDirections)) return;
        if (!r.Check(RebuildCosts(stack, state))) return;
    }

    /// <summary>
    /// Whether no cell of the outer ring has a direction. The nav grid's outer ring is always blocked and a blocked cell
    /// never gets one, so this holds for every built field; with it, a direction from any other cell steps to a cell of the
    /// map (one cell over, never off an edge or round to the next row), so the loops below need no bounds check.
    /// </summary>
    private bool RingHasNoDirection()
    {
        int w = Width, h = Height;
        for (int x = 0; x < w; x++)
            if (_direction[x] != NoDirection || _direction[(h - 1) * w + x] != NoDirection) return false;
        for (int y = 0; y < h; y++)
            if (_direction[y * w] != NoDirection || _direction[y * w + w - 1] != NoDirection) return false;
        return true;
    }

    /// <summary>The index offset of a step in each direction.</summary>
    private void Offsets(Span<int> offset)
    {
        for (int d = 0; d < DirectionCount; d++) offset[d] = DirY[d] * Width + DirX[d];
    }

    /// <summary>
    /// Whether every cell's cost is what <see cref="RebuildCosts"/> would give it: the target exactly +0, every other cell
    /// without a direction +infinity, every cell with one (none on the outer ring) finite and exactly the float
    /// cost[next] + step. Costs then fall strictly along every direction chain, so each chain ends at the target and the
    /// rebuild reproduces every cost bit for bit (two finite positive floats are equal exactly when their bits are).
    /// </summary>
    private bool CostsFollowDirections()
    {
        if (!RingHasNoDirection()) return false;
        Span<int> offset = stackalloc int[DirectionCount];
        Offsets(offset);
        float[] cost = _cost;
        byte[] direction = _direction;
        for (int c = 0; c < cost.Length; c++)
        {
            int d = direction[c];
            float have = cost[c];
            if (d == NoDirection)
            {
                if (c == TargetCell ? BitConverter.SingleToInt32Bits(have) != 0 : !float.IsPositiveInfinity(have)) return false;
                continue;
            }
            if (d >= DirectionCount || !(have < float.PositiveInfinity)) return false;
            if (have != cost[c + offset[d]] + ((d & 1) == 1 ? DiagonalCost : 1f)) return false;
        }
        return true;
    }

    /// <summary>
    /// Costs from the directions (see <see cref="CostsFollowDirections"/>): each cell's chain is followed to a cell already
    /// done or without a direction, then unwound adding one step at a time, so every cell is visited about once. False on a
    /// direction on the outer ring or past 7, a target with a direction, or a cycle (none of which a built field has).
    /// </summary>
    private bool RebuildCosts(int[] stack, byte[] state)
    {
        const byte onChain = 1, done = 2; // 0: not reached yet
        int cells = _cost.Length;
        if (TargetCell >= 0 && _direction[TargetCell] != NoDirection) return false;
        if (!RingHasNoDirection()) return false;
        Span<int> offset = stackalloc int[DirectionCount];
        Offsets(offset);
        float[] cost = _cost;
        byte[] direction = _direction;
        Array.Clear(state, 0, cells);
        for (int c = 0; c < cells; c++)
        {
            if (state[c] == done) continue;
            int sp = 0, cur = c;
            while (state[cur] != done)
            {
                if (state[cur] == onChain) return false; // a cycle: no field has one
                int d = direction[cur];
                if (d == NoDirection)
                {
                    cost[cur] = cur == TargetCell ? 0f : float.PositiveInfinity;
                    state[cur] = done;
                    break;
                }
                if (d >= DirectionCount) return false;
                state[cur] = onChain;
                stack[sp++] = cur;
                cur += offset[d];
            }
            // Unwind from the chain's end: each cell is the cell it points at plus one step, the sum Dijkstra formed.
            float next = cost[cur];
            while (sp > 0)
            {
                int x = stack[--sp];
                next += (direction[x] & 1) == 1 ? DiagonalCost : 1f;
                cost[x] = next;
                state[x] = done;
            }
        }
        return true;
    }

    /// <summary>Test seam (M6-S2): overwrites one cell's cost, to make a field whose costs don't follow its directions.</summary>
    internal void SetCostForTests(int cell, float cost) => _cost[cell] = cost;
}
