using System.Numerics;
using System.Text;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S10 (BUG-1431): watches the economy fuzz (<see cref="GatherOverlapFuzzTests.Run"/>) for a <b>held</b> worker, the word the
/// brief's criteria use: a live unit with <c>GatherNode</c> set whose node is alive, 1,200 ticks without a delivery (the fuzz's
/// clock, which a new node or order starts again), that at the end of those ticks is within 2 m of where it was 600 ticks
/// earlier. A worker past 1,200 ticks that kept walking is a <see cref="Walker"/> (printed with its route), never a failure.
/// For each held worker it writes the diagnosis row (docs/03 "Implementation (M6-S10)", stage 0): its state, where it
/// stands against the footprint it is working toward and against its stand point, who holds the stand cell, and how each of
/// its walks in the last 600 ticks ended (arrived at the point, stopped touching a groupmate, gave up, or never walked).
/// Test-only: reads the sim after each tick, writes nothing.
/// </summary>
internal sealed class HeldWorkerProbe
{
    /// <summary>The held clock: 1,200 ticks without a delivery (60 s).</summary>
    internal const int HeldTicks = 1200;

    /// <summary>The look-back for "standing": 600 ticks.</summary>
    internal const int LookBack = 600;

    /// <summary>Within this many meters of where it stood <see cref="LookBack"/> ticks earlier: held.</summary>
    internal const float HeldRadius = 2f;

    private const int Ring = LookBack + 1;

    private readonly Vector2[,] _trail;
    private readonly UnitState[] _prevState;
    private readonly int[] _prevOrderTick;
    private readonly List<Walk>[] _walks;

    /// <summary>Every held worker seen (one row each, at the tick its clock passed <see cref="HeldTicks"/>).</summary>
    internal readonly List<string> Held = new();

    /// <summary>Workers past <see cref="HeldTicks"/> that had walked more than <see cref="HeldRadius"/> in the last 600 ticks.</summary>
    internal readonly List<string> Walkers = new();

    /// <summary>The longest clock seen (ticks).</summary>
    internal int Longest;

    /// <summary>One walk of a worker: when it started, its goal cell and point, and how it ended.</summary>
    private sealed record Walk(int Start, int Cell, Vector2 Goal, string Leg)
    {
        public int End = -1;
        public string How = "walking";
    }

    internal HeldWorkerProbe(int capacity)
    {
        _trail = new Vector2[capacity, Ring];
        _prevState = new UnitState[capacity];
        _prevOrderTick = new int[capacity];
        _walks = new List<Walk>[capacity];
        for (int i = 0; i < capacity; i++)
        {
            _walks[i] = new List<Walk>();
            _prevOrderTick[i] = -1;
        }
    }

    /// <summary>The fuzz's watch callback: after every tick, each worker's slot and its clock.</summary>
    internal void Watch(Simulation sim, int i, int since)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        int now = sim.TickNumber;
        _trail[i, now % Ring] = u.Position[i];
        // A walk starts: OrderSystem.Walk stamps OrderTick (a footprint walk or a re-issued one).
        if (u.State[i] == UnitState.Moving && (u.OrderTick[i] != _prevOrderTick[i] || _prevState[i] != UnitState.Moving))
        {
            CloseOpen(i, now, "re-walked before it ended");
            _walks[i].Add(new Walk(now, u.GoalCell[i], u.Goal[i], u.Cargo[i] >= w.Data.CarryOf(u.TypeId[i]) ? "drop-off" : "node"));
        }
        else if (u.OrderTick[i] != _prevOrderTick[i] && _prevOrderTick[i] >= 0)
        {
            // Walked and stopped in one tick (the economy phase starts the walk, movement ends it the same tick).
            CloseOpen(i, now, "re-walked before it ended");
            _walks[i].Add(new Walk(now, u.GoalCell[i], u.Goal[i], u.Cargo[i] >= w.Data.CarryOf(u.TypeId[i]) ? "drop-off" : "node") { End = now, How = "same tick: " + HowEnded(w, i) });
        }
        else if (_prevState[i] == UnitState.Moving && u.State[i] != UnitState.Moving)
        {
            CloseOpen(i, now, HowEnded(w, i));
        }
        _prevState[i] = u.State[i];
        _prevOrderTick[i] = u.OrderTick[i];
        Longest = Math.Max(Longest, since);
        if (since != HeldTicks + 1) return;
        if (u.GatherNode[i].Generation == 0 || !w.Resources.IsAlive(u.GatherNode[i])) return;
        Vector2 then = _trail[i, (now - LookBack) % Ring];
        float moved = Vector2.Distance(then, u.Position[i]);
        if (moved <= HeldRadius) Held.Add(Describe(sim, i, moved));
        else Walkers.Add($"unit {i} at tick {now}: {u.State[i]} at {Fmt(u.Position[i])}, {moved:F1} m from where it stood 600 ticks earlier; walks in the last 600 ticks: {Route(i, now)}");
    }

    private void CloseOpen(int i, int now, string how)
    {
        List<Walk> list = _walks[i];
        if (list.Count == 0 || list[^1].End >= 0) return;
        list[^1].End = now;
        list[^1].How = how;
    }

    /// <summary>Movement stopped the walk this tick: at its point, touching a groupmate (goal cell kept), or a give-up (goal cell dropped).</summary>
    private static string HowEnded(World w, int i)
    {
        UnitStore u = w.Units;
        if (u.GatherNode[i].Generation == 0) return "loop ended";
        if (u.GoalCell[i] < 0) return "gave up (goal cell dropped)";
        float toGoal = Vector2.Distance(u.Position[i], u.Goal[i]);
        if (toGoal <= MovementConstants.ArrivalDistance) return $"arrived at its point ({toGoal:F2} m)";
        int mate = TouchingMate(u, i);
        return mate >= 0
            ? $"stopped {toGoal:F2} m short, touching groupmate {mate} ({u.State[mate]}, nearer the point)"
            : $"stopped {toGoal:F2} m short (backed off and settled, or touching a mate that walked on)";
    }

    /// <summary>A standing unit of i's owner with i's goal cell, touching it (radii sum) and nearer i's goal than i: MovementSystem's crowded-arrival groupmate (QueuesBehind).</summary>
    private static int TouchingMate(UnitStore u, int i)
    {
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int j = 0; j < u.Capacity; j++)
        {
            if (j == i || !u.Alive[j] || u.Owner[j] != u.Owner[i] || u.State[j] == UnitState.Moving || u.GoalCell[j] != u.GoalCell[i]) continue;
            float d = Vector2.Distance(u.Position[i], u.Position[j]);
            if (d > u.Radius[i] + u.Radius[j] + 0.05f) continue;
            if (Vector2.DistanceSquared(u.Position[j], u.Goal[i]) >= Vector2.DistanceSquared(u.Position[i], u.Goal[i])) continue;
            if (d < bestD) { best = j; bestD = d; }
        }
        return best;
    }

    private string Route(int i, int now)
    {
        var sb = new StringBuilder();
        int shown = 0;
        foreach (Walk walk in _walks[i])
        {
            if (walk.Start < now - LookBack && (walk.End < 0 || walk.End < now - LookBack)) continue;
            if (shown++ > 0) sb.Append("; ");
            sb.Append($"t{walk.Start} {walk.Leg} cell {walk.Cell} -> {walk.How}{(walk.End >= 0 ? $" at t{walk.End}" : "")}");
        }
        return shown == 0 ? "none" : sb.ToString();
    }

    private string Describe(Simulation sim, int i, float moved)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        int now = sim.TickNumber;
        bool full = u.Cargo[i] >= w.Data.CarryOf(u.TypeId[i]);
        var sb = new StringBuilder();
        sb.Append($"unit {i} p{u.Owner[i]} {w.Data.Units[u.TypeId[i]].Id} (r {u.Radius[i]:F2}) at tick {now}: {u.State[i]}, cargo {u.Cargo[i]} {u.CargoKind[i]}{(full ? " (full: drop-off leg)" : " (node leg)")}, ");
        sb.Append($"at {Fmt(u.Position[i])} (moved {moved:F2} m in 600 ticks); ");
        (string what, int anchor, int fw, int fh) = full ? NearestDropOff(w, i) : NodeOf(w, i);
        float gap = FootprintGap(g, u.Position[i], anchor, fw, fh);
        sb.Append($"{gap:F2} m from its {what}'s footprint (reach {EconomyConstants.Reach}); ");
        sb.Append($"node footprint {FootprintGapOfNode(w, i):F2} m, drop-off footprint {FootprintGap(g, u.Position[i], NearestDropOff(w, i).Anchor, NearestDropOff(w, i).Fw, NearestDropOff(w, i).Fh):F2} m; ");
        int goalCell = u.GoalCell[i];
        sb.Append(goalCell < 0 ? "no goal cell; " : $"goal cell {goalCell % g.Width},{goalCell / g.Width}, stand point {Fmt(u.Goal[i])} {Vector2.Distance(u.Position[i], u.Goal[i]):F2} m off; ");
        int cell = goalCell >= 0 ? goalCell : (g.WorldToCell(u.Position[i], out int cx, out int cy) ? cy * g.Width + cx : -1);
        sb.Append("in that cell / holding it: ");
        int listed = 0;
        for (int j = 0; j < u.Capacity; j++)
        {
            if (j == i || !u.Alive[j]) continue;
            bool inCell = g.WorldToCell(u.Position[j], out int jx, out int jy) && jy * g.Width + jx == cell;
            bool holds = cell >= 0 && u.GoalCell[j] == cell && u.Owner[j] == u.Owner[i];
            if (!inCell && !holds) continue;
            if (listed++ > 0) sb.Append(", ");
            bool inNode = u.GatherNode[j].Generation != 0 && w.Resources.IsAlive(u.GatherNode[j]) && FootprintGapOfNode(w, j) <= EconomyConstants.Reach;
            bool inDrop = NearestDropOff(w, j).Anchor >= 0 && FootprintGap(g, u.Position[j], NearestDropOff(w, j).Anchor, NearestDropOff(w, j).Fw, NearestDropOff(w, j).Fh) <= EconomyConstants.Reach;
            sb.Append($"{j} p{u.Owner[j]} {u.State[j]}{(inCell ? " in it" : "")}{(holds ? " goal=it" : "")} {Vector2.Distance(u.Position[i], u.Position[j]):F2} m{(inNode ? " in node reach" : "")}{(inDrop ? " in drop-off reach" : "")}");
        }
        if (listed == 0) sb.Append("nobody");
        sb.Append($"; walks in the last 600 ticks: {Route(i, now)}");
        return sb.ToString();
    }

    private static (string What, int Anchor, int Fw, int Fh) NodeOf(World w, int i)
    {
        int n = w.Units.GatherNode[i].Index;
        ResourceDef def = w.Data.Resources[w.Resources.TypeId[n]];
        return ($"node {def.Id}", w.Resources.Cell[n], def.FootprintWidth, def.FootprintHeight);
    }

    private static float FootprintGapOfNode(World w, int i)
    {
        (_, int anchor, int fw, int fh) = NodeOf(w, i);
        return FootprintGap(w.NavGrid, w.Units.Position[i], anchor, fw, fh);
    }

    private static (string What, int Anchor, int Fw, int Fh) NearestDropOff(World w, int i)
    {
        BuildingStore b = w.Buildings;
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != w.Units.Owner[i] || !w.Data.Buildings[b.TypeId[k]].DropOff) continue;
            BuildingDef def = w.Data.Buildings[b.TypeId[k]];
            float d = Vector2.Distance(w.Units.Position[i], EconomySystem.Center(w.NavGrid, b.Cell[k], def.FootprintWidth, def.FootprintHeight));
            if (d < bestD) { best = k; bestD = d; }
        }
        if (best < 0) return ("drop-off (none)", -1, 0, 0);
        BuildingDef bd = w.Data.Buildings[b.TypeId[best]];
        return ($"drop-off {bd.Id}", b.Cell[best], bd.FootprintWidth, bd.FootprintHeight);
    }

    /// <summary>Meters from <paramref name="p"/> to the footprint rectangle (0 inside); +inf for no footprint.</summary>
    internal static float FootprintGap(NavGrid g, Vector2 p, int anchor, int fw, int fh)
    {
        if (anchor < 0) return float.PositiveInfinity;
        const float cs = MapConstants.CellSize;
        float x0 = anchor % g.Width * cs, y0 = anchor / g.Width * cs;
        float dx = MathF.Max(MathF.Max(x0 - p.X, p.X - (x0 + fw * cs)), 0f);
        float dy = MathF.Max(MathF.Max(y0 - p.Y, p.Y - (y0 + fh * cs)), 0f);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static string Fmt(Vector2 v) => $"({v.X:F2}, {v.Y:F2})";

    /// <summary>One fuzz run watched: the probe and the run's result.</summary>
    internal static (HeldWorkerProbe Probe, GatherOverlapFuzzTests.Result Result) Run(double overlap, ulong seed)
    {
        var probe = new HeldWorkerProbe(96);
        GatherOverlapFuzzTests.Result r = GatherOverlapFuzzTests.Run(overlap, seed, tripWindow: false, watch: probe.Watch);
        return (probe, r);
    }
}
