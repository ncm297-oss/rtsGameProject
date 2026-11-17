using System.Numerics;
using System.Text;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-S12 criteria 3 and 6 (BUG-1461, BUG-1460): Raraku Normal v Normal seed 1. Report only. Prints every change of each
/// seat's rally point (tick, point, cell, and whether a one-cell passage cell lies within <c>ai.rallyReach</c> of it), the
/// terrain round seat 0's first point, and over the first 12,000 ticks how long any worker of either seat is held (the
/// Producer's definition, <see cref="HeldWorkerProbe"/>: on its loop, node alive, 1,200 ticks without a delivery, within 2 m
/// of where it stood 600 ticks earlier and never in reach of its node or a finished drop-off in those 600 ticks). Then the
/// match to 72,000 ticks: its end, or each seat's state every 6,000 ticks (the probe's account of why it does not end).
/// </summary>
[Trait("Category", "Stress")]
public class M6S12RarakuProbeStressTests(ITestOutputHelper output)
{
    private const int Window = 12_000, Limit = 72_000;

    [Theory]
    [InlineData(1UL)]
    public void RarakuSeed1_RallyHeldWorkersAndEnd_Report(ulong seed)
    {
        Simulation sim = MapFileTests.MapMatch("raraku", seed, false, out _);
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        var rally = new Vector2[2];
        var versions = new int[2];
        bool drawn = false;
        const int Ring = HeldWorkerProbe.LookBack + 1;
        var trail = new Vector2[u.Capacity, Ring];
        var inReach = new bool[u.Capacity, Ring];
        var clock = new int[u.Capacity];
        var cargo = new int[u.Capacity];
        var node = new EntityHandle[u.Capacity];
        var gen = new int[u.Capacity];
        var heldTicks = new int[u.Capacity];
        var heldFrom = new int[u.Capacity];
        Array.Fill(heldFrom, -1);
        var stretches = new List<string>();
        int scouts = 0;
        var phase = new byte[2];
        for (int p = 0; p < 2; p++)
        {
            AiPlayer ai = sim.AiOf(p)!;
            int seat = p;
            ai.ThoughtForTests = a =>
            {
                if (a.Board.RallyVersion != versions[seat])
                {
                    versions[seat] = a.Board.RallyVersion;
                    rally[seat] = a.Board.Rally;
                    int cx = (int)(rally[seat].X / MapConstants.CellSize), cy = (int)(rally[seat].Y / MapConstants.CellSize);
                    output.WriteLine($"tick {a.Now}: p{seat} rally v{a.Board.RallyVersion} at {rally[seat]} cell ({cx}, {cy}), {Vector2.Distance(rally[seat], a.HallPoint):F1} m from the hall {a.HallPoint} (nominal {a.Rules.RallyDistance} m: {(MathF.Abs(Vector2.Distance(rally[seat], a.HallPoint) - a.Rules.RallyDistance) < 0.5f ? "yes" : "shifted")}); "
                        + $"one-cell passage cell within rallyReach: {(NarrowNear(g, rally[seat], a.Rules.RallyReach, out int nx, out int ny) ? $"yes ({nx}, {ny})" : "no")}");
                }
                if (phase[seat] == 2 && a.Board.ScoutPhase == 1)
                {
                    scouts++;
                    output.WriteLine($"tick {a.Now}: p{seat} re-scout (BUG-1460) with unit {a.Board.ScoutSlot}");
                }
                phase[seat] = a.Board.ScoutPhase;
            };
        }
        CriterionMatch.Result r = CriterionMatch.Play(sim, Limit, checks: false, onTick: s =>
        {
            int now = s.TickNumber;
            if (!drawn && now == 6000)
            {
                drawn = true;
                output.WriteLine(Draw(g, rally[0], 12));
            }
            if (now <= Window)
            {
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i] || !w.Data.Units[u.TypeId[i]].IsWorker) continue;
                    if (u.Generation[i] != gen[i]) { gen[i] = u.Generation[i]; clock[i] = now; node[i] = default; cargo[i] = 0; heldFrom[i] = -1; }
                    trail[i, now % Ring] = u.Position[i];
                    inReach[i, now % Ring] = InReachOfWork(w, i);
                    EntityHandle nd = u.GatherNode[i];
                    if (nd != node[i] || nd.Generation == 0 || !w.Resources.IsAlive(nd)) clock[i] = now;
                    if (cargo[i] > 0 && u.Cargo[i] == 0) clock[i] = now;
                    cargo[i] = u.Cargo[i];
                    node[i] = nd;
                    bool held = false;
                    if (now - clock[i] > HeldWorkerProbe.HeldTicks)
                    {
                        bool reached = false;
                        for (int k = 0; k < Ring && !reached; k++) reached = inReach[i, k];
                        held = !reached && Vector2.Distance(trail[i, (now - HeldWorkerProbe.LookBack) % Ring], u.Position[i]) <= HeldWorkerProbe.HeldRadius;
                    }
                    if (held)
                    {
                        heldTicks[i]++;
                        if (heldFrom[i] < 0) heldFrom[i] = now;
                    }
                    else if (heldFrom[i] >= 0)
                    {
                        stretches.Add($"unit {i} p{u.Owner[i]}: held {heldFrom[i]}..{now - 1} ({now - heldFrom[i]} ticks) at {u.Position[i]}");
                        heldFrom[i] = -1;
                    }
                }
                if (now == Window)
                    for (int i = 0; i < u.Capacity; i++)
                        if (heldFrom[i] >= 0) stretches.Add($"unit {i} p{u.Owner[i]}: held {heldFrom[i]}..{now} (still) at {u.Position[i]}");
            }
            if (now % 6000 != 0) return;
            var line = new StringBuilder($"tick {now}:");
            for (int p = 0; p < 2; p++)
            {
                int workers = 0, loop = 0, army = 0;
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i] || u.Owner[i] != p) continue;
                    if (w.Data.Units[u.TypeId[i]].IsWorker) { workers++; if (u.GatherNode[i].Generation != 0) loop++; }
                    else army++;
                }
                AiPlayer ai = s.AiOf(p)!;
                line.Append($" p{p}: gold {w.Gold[p]} wood {w.Wood[p]}, workers {workers} (loop {loop}), army {army}, scout phase {ai.Board.ScoutPhase}, starved since {ai.Board.StarvedSince};");
            }
            output.WriteLine(line.ToString());
        });
        int total = 0, longest = 0;
        for (int i = 0; i < u.Capacity; i++) total += heldTicks[i];
        foreach (string st in stretches) output.WriteLine("  HELD " + st);
        foreach (string st in stretches)
        {
            int open = st.IndexOf('('), close = st.IndexOf(" ticks");
            if (open >= 0 && close > open && int.TryParse(st.AsSpan(open + 1, close - open - 1), out int n)) longest = Math.Max(longest, n);
        }
        output.WriteLine($"seed {seed}: held worker-ticks in the first {Window} ticks {total} ({stretches.Count} stretches, longest closed {longest}); "
            + $"over {r.Over} at {r.EndTick}, winner {r.Winner}, waves {r.Waves}, hunts {r.Hunts}; scouts {scouts}");
    }

    /// <summary>Whether a one-cell passage cell (open terrain, terrain-blocked on both sides along x or along y) has its centre within <paramref name="reach"/> of <paramref name="p"/>.</summary>
    internal static bool NarrowNear(NavGrid g, Vector2 p, float reach, out int nx, out int ny)
    {
        nx = ny = -1;
        int r = (int)MathF.Ceiling(reach / MapConstants.CellSize) + 1;
        int cx = (int)(p.X / MapConstants.CellSize), cy = (int)(p.Y / MapConstants.CellSize);
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (!Open(g, x, y) || Vector2.DistanceSquared(g.CellCenter(x, y), p) > reach * reach) continue;
                if (!(Open(g, x - 1, y) || Open(g, x + 1, y)) || !(Open(g, x, y - 1) || Open(g, x, y + 1)))
                {
                    (nx, ny) = (x, y);
                    return true;
                }
            }
        return false;
    }

    private static bool Open(NavGrid g, int x, int y) =>
        x >= 0 && y >= 0 && x < g.Width && y < g.Height && (g.FlagsAt(x, y) & NavFlags.Blocked) == 0;

    private static string Draw(NavGrid g, Vector2 p, int r)
    {
        int cx = (int)(p.X / MapConstants.CellSize), cy = (int)(p.Y / MapConstants.CellSize);
        var sb = new StringBuilder($"terrain round ({cx}, {cy}) (# blocked terrain, B building, R node, ^ ramp, * the point):\n");
        for (int y = cy - r; y <= cy + r; y++)
        {
            sb.Append($"{y,4} ");
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || y < 0 || x >= g.Width || y >= g.Height) { sb.Append(' '); continue; }
                NavFlags f = g.FlagsAt(x, y);
                sb.Append(x == cx && y == cy ? '*' : (f & NavFlags.Building) != 0 ? 'B' : (f & NavFlags.Resource) != 0 ? 'R' : (f & NavFlags.Blocked) != 0 ? '#' : (f & NavFlags.Ramp) != 0 ? '^' : '.');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static bool InReachOfWork(World w, int i)
    {
        UnitStore u = w.Units;
        EntityHandle nd = u.GatherNode[i];
        if (nd.Generation != 0 && w.Resources.IsAlive(nd))
        {
            ResourceDef rd = w.Data.Resources[w.Resources.TypeId[nd.Index]];
            if (EconomySystem.InReach(w.NavGrid, u.Position[i], w.Resources.Cell[nd.Index], rd.FootprintWidth, rd.FootprintHeight)) return true;
        }
        for (int k = 0; k < w.Buildings.Capacity; k++)
        {
            if (!w.Buildings.Alive[k] || w.Buildings.Owner[k] != u.Owner[i] || w.Buildings.UnderConstruction[k]) continue;
            BuildingDef bd = w.Data.Buildings[w.Buildings.TypeId[k]];
            if (bd.DropOff && EconomySystem.InReach(w.NavGrid, u.Position[i], w.Buildings.Cell[k], bd.FootprintWidth, bd.FootprintHeight)) return true;
        }
        return false;
    }
}
