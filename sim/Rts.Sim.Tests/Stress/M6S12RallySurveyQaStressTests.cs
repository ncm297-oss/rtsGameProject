using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-S12 (BUG-1461 / 1460, QA focus "full"): a survey of the AI's rally point and blind re-scout on the three hand-made
/// maps and on generated maps of 2, 3 and 4 seats, seeds 1-8, Normal seats, the first <see cref="Window"/> ticks. Per seat it
/// records, at every think: each change of the rally point (a <b>flap</b> is a change back to a point the seat used before
/// while its hall stood still), whether the point's cell is closed in truth (terrain, a building footprint, a node), its
/// distance to the hall, whether a one-cell passage cell (truth) is within <c>ai.rallyReach</c> of it; and the "rallied"
/// check (an army unit sent to the current version, Idle, yet farther than <c>ai.rallyReach</c> + 2 m from the point: it never
/// reached it). Per tick: held worker-ticks (the Producer's definition, as <c>M6S12RarakuProbeStressTests</c>) and idle army
/// unit-ticks within 1.5 m of an own finished drop-off's footprint (the parked army blocking its own deliveries). Every
/// blind re-scout (phase 2 -> 1) is counted. Written so it also compiles on the base (95186a7) for the before column; only
/// truth reads, nothing written.
/// Asserts (the rule's own guarantees after M6-S12): a point that is not the nominal distance from the hall (shifted) is open
/// ground; the point never goes back and forth within 400 ticks while the seat's count of known enemy buildings holds.
/// Measured 2026-11-16-2036 (QA, the 120 seat-runs, base 95186a7 / M6-S12): rally changes 460 / 597, shifted points 0 / 222
/// (none on closed ground), nominal points on a building cell 145 / 80, points with a one-cell passage within reach (truth)
/// 173 / 83, held worker-ticks 30,603 / 4,118, idle-army unit-ticks within 1.5 m of an own drop-off 96,918 / 62,829, re-scouts
/// 0 / 4. Seen on M6-S12: a shifted point 6 m from the hall's centre (generated seed 4, three and four seats, p0), a fall-back
/// to a nominal point on a building, and every-think flapping while an enemy site at 1 hp flickers in and out of sight
/// (generated three seats seed 6, p0, ticks 10,340-10,580): BUG-1558.
/// </summary>
[Trait("Category", "Stress")]
public class M6S12RallySurveyQaStressTests(ITestOutputHelper output)
{
    internal const int Window = 12_000;

    public static IEnumerable<object[]> Rows()
    {
        string[] maps = { "raraku", "pale_hills", "vathar_crossing", "generated:2", "generated:3", "generated:4" };
        foreach (string m in maps)
            for (ulong s = 1; s <= 8; s++)
                yield return new object[] { m, s };
    }

    internal sealed class SeatTally
    {
        public int Changes, Flaps, Shifted, ShiftedClosed, NominalClosed, ClosedBuilding, ClosedTerrain, ClosedNode, NearPassage, Rescouts;
        public float MinHall = float.MaxValue;
        public long Held, ParkedAtDropOff, NotReached, NotReachedShifted;
        public readonly List<Vector2> Points = new();
        public readonly List<string> Notes = new();
        public readonly List<string> ChangeLog = new();
        public readonly List<(int Tick, Vector2 Point, int Known)> History = new();

        /// <summary>Changes back to the point before last within 400 ticks while the seat's count of known enemy buildings held (the rule's own flapping; a target that flickers moves the nominal point too, so those are not counted).</summary>
        public int StableTargetFlaps()
        {
            int n = 0;
            for (int i = 2; i < History.Count; i++)
                if (History[i].Point == History[i - 2].Point && History[i].Tick - History[i - 2].Tick <= 400
                    && History[i].Known == History[i - 1].Known && History[i - 1].Known == History[i - 2].Known) n++;
            return n;
        }
    }

    internal static Simulation Build(string map, ulong seed)
    {
        if (!map.StartsWith("generated:")) return MapFileTests.MapMatch(map, seed, false, out _);
        int players = int.Parse(map.AsSpan("generated:".Length));
        return CriterionMatch.Build("generated", seed, players);
    }

    /// <summary>Plays one row and returns the per-seat tallies (see the type's summary).</summary>
    internal static SeatTally[] Survey(Simulation sim, int window)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        int n = w.Config.PlayerCount;
        var tally = new SeatTally[n];
        var hallAt = new Vector2[n];
        var version = new int[n];
        var phase = new byte[n];
        var everEnded = new bool[n];
        for (int p = 0; p < n; p++)
        {
            tally[p] = new SeatTally();
            AiPlayer ai = sim.AiOf(p)!;
            int seat = p;
            ai.ThoughtForTests = a =>
            {
                SeatTally t = tally[seat];
                Blackboard b = a.Board;
                if (b.ScoutPhase == 2) everEnded[seat] = true;
                if (everEnded[seat] && phase[seat] == 2 && b.ScoutPhase == 1) t.Rescouts++;
                phase[seat] = b.ScoutPhase;
                if (!a.TryHallPoint(out Vector2 hall)) return;
                if (b.RallyVersion != version[seat])
                {
                    version[seat] = b.RallyVersion;
                    Vector2 r = b.Rally;
                    t.Changes++;
                    bool sameHall = hall == hallAt[seat];
                    if (sameHall && t.Points.Count >= 2 && t.Points.IndexOf(r) >= 0 && t.Points.IndexOf(r) < t.Points.Count - 1)
                    {
                        t.Flaps++;
                        t.Notes.Add($"flap at tick {a.Now} back to {r}");
                    }
                    if (!sameHall) t.Points.Clear();
                    hallAt[seat] = hall;
                    t.Points.Add(r);
                    float dh = Vector2.Distance(r, hall);
                    t.MinHall = MathF.Min(t.MinHall, dh);
                    bool shifted = MathF.Abs(dh - a.Rules.RallyDistance) > 0.5f && a.ClampToMap(r) == r && !NearEdge(g, r);
                    string closed = Closed(g, r);
                    if (shifted) t.Shifted++;
                    if (closed != "")
                    {
                        if (shifted) t.ShiftedClosed++; else t.NominalClosed++;
                        if (closed == "building") t.ClosedBuilding++; else if (closed == "node") t.ClosedNode++; else t.ClosedTerrain++;
                        t.Notes.Add($"tick {a.Now}: point {r} ({(shifted ? "shifted" : "nominal")}) on a {closed} cell");
                    }
                    if (NarrowNear(g, r, a.Rules.RallyReach)) t.NearPassage++;
                    t.History.Add((a.Now, r, b.KnownEnemyBuildings));
                    t.ChangeLog.Add($"tick {a.Now}: v{b.RallyVersion} {r} cell ({(int)(r.X / MapConstants.CellSize)}, {(int)(r.Y / MapConstants.CellSize)}), {dh:F1} m from the hall {hall}{(shifted ? " (shifted)" : "")}, known enemy buildings {b.KnownEnemyBuildings}");
                }
                // The "rallied" check: units sent to this version, Idle, still out of reach.
                float far = a.Rules.RallyReach + 2f;
                bool shiftedNow = MathF.Abs(Vector2.Distance(b.Rally, hall) - a.Rules.RallyDistance) > 0.5f;
                if (b.WaveActive) return;
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i] || u.Owner[i] != seat || w.Data.Units[u.TypeId[i]].IsWorker) continue;
                    if (b.RalliedVersion[i] != b.RallyVersion || u.State[i] != UnitState.Idle) continue;
                    if (Vector2.Distance(u.Position[i], b.Rally) <= far) continue;
                    t.NotReached++;
                    if (shiftedNow) t.NotReachedShifted++;
                }
            };
        }
        // Held workers (the probe's definition) and parked army at drop-offs.
        const int Ring = HeldWorkerProbe.LookBack + 1;
        var trail = new Vector2[u.Capacity, Ring];
        var inReach = new bool[u.Capacity, Ring];
        var clock = new int[u.Capacity];
        var cargo = new int[u.Capacity];
        var node = new EntityHandle[u.Capacity];
        var gen = new int[u.Capacity];
        CriterionMatch.Result res = CriterionMatch.Play(sim, window, checks: false, onTick: s =>
        {
            int now = s.TickNumber;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                UnitDef def = w.Data.Units[u.TypeId[i]];
                if (!def.IsWorker)
                {
                    if (u.State[i] == UnitState.Idle && NearOwnDropOff(w, u.Owner[i], u.Position[i], 1.5f)) tally[u.Owner[i]].ParkedAtDropOff++;
                    continue;
                }
                if (u.Generation[i] != gen[i]) { gen[i] = u.Generation[i]; clock[i] = now; node[i] = default; cargo[i] = 0; }
                trail[i, now % Ring] = u.Position[i];
                inReach[i, now % Ring] = InReachOfWork(w, i);
                EntityHandle nd = u.GatherNode[i];
                if (nd != node[i] || nd.Generation == 0 || !w.Resources.IsAlive(nd)) clock[i] = now;
                if (cargo[i] > 0 && u.Cargo[i] == 0) clock[i] = now;
                cargo[i] = u.Cargo[i];
                node[i] = nd;
                if (now - clock[i] <= HeldWorkerProbe.HeldTicks) continue;
                bool reached = false;
                for (int k = 0; k < Ring && !reached; k++) reached = inReach[i, k];
                if (!reached && Vector2.Distance(trail[i, (now - HeldWorkerProbe.LookBack) % Ring], u.Position[i]) <= HeldWorkerProbe.HeldRadius)
                    tally[u.Owner[i]].Held++;
            }
        });
        for (int p = 0; p < n; p++) sim.AiOf(p)!.ThoughtForTests = null;
        return tally;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void RallyAndRescout_Survey(string map, ulong seed)
    {
        Simulation sim = Build(map, seed);
        SeatTally[] t = Survey(sim, Window);
        for (int p = 0; p < t.Length; p++)
        {
            SeatTally s = t[p];
            output.WriteLine($"SURVEY {map} seed {seed} p{p}: changes {s.Changes}, flaps {s.Flaps}, shifted {s.Shifted}, closed nominal {s.NominalClosed} / shifted {s.ShiftedClosed} "
                + $"(building {s.ClosedBuilding}, terrain {s.ClosedTerrain}, node {s.ClosedNode}), near a passage {s.NearPassage}, min to hall {(s.MinHall == float.MaxValue ? -1 : s.MinHall):F1} m, "
                + $"held worker-ticks {s.Held}, parked at drop-off {s.ParkedAtDropOff}, not reached {s.NotReached} (shifted {s.NotReachedShifted}), re-scouts {s.Rescouts}");
            foreach (string note in s.Notes.Take(12)) output.WriteLine("  " + note);
            foreach (string c in s.ChangeLog.Take(40)) output.WriteLine("  rally " + c);
        }
        for (int p = 0; p < t.Length; p++)
        {
            Assert.True(t[p].ShiftedClosed == 0, $"{map} seed {seed} p{p}: a shifted rally point on closed ground ({t[p].ShiftedClosed})");
            // Not asserted: Flaps (a change back to an earlier point at any distance in time; the base shows 50 in these rows, the
            // target moves). Asserted: no back-and-forth within 400 ticks while the target count held (0 on the base and on M6-S12).
            Assert.True(t[p].StableTargetFlaps() == 0, $"{map} seed {seed} p{p}: the rally point flapped with its target unchanged: {string.Join("; ", t[p].ChangeLog)}");
        }
    }

    private static bool NearEdge(NavGrid g, Vector2 p) =>
        p.X < 3f || p.Y < 3f || p.X > g.Width * MapConstants.CellSize - 3f || p.Y > g.Height * MapConstants.CellSize - 3f;

    /// <summary>"" when the point's cell is open in truth, else what closes it.</summary>
    internal static string Closed(NavGrid g, Vector2 p)
    {
        int x = (int)(p.X / MapConstants.CellSize), y = (int)(p.Y / MapConstants.CellSize);
        if (x < 0 || y < 0 || x >= g.Width || y >= g.Height) return "off-map";
        NavFlags f = g.FlagsAt(x, y);
        if ((f & NavFlags.Blocked) == 0) return "";
        if ((f & NavFlags.Building) != 0) return "building";
        if ((f & NavFlags.Resource) != 0) return "node";
        return "terrain";
    }

    /// <summary>Whether an open cell (truth) closed on both sides along one axis and open on both along the other has its centre within <paramref name="reach"/>.</summary>
    internal static bool NarrowNear(NavGrid g, Vector2 p, float reach)
    {
        int r = (int)MathF.Ceiling(reach / MapConstants.CellSize) + 1;
        int cx = (int)(p.X / MapConstants.CellSize), cy = (int)(p.Y / MapConstants.CellSize);
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (!Open(g, x, y) || Vector2.DistanceSquared(g.CellCenter(x, y), p) > reach * reach) continue;
                bool we = !Open(g, x - 1, y) && !Open(g, x + 1, y), ns = !Open(g, x, y - 1) && !Open(g, x, y + 1);
                if (we && Open(g, x, y - 1) && Open(g, x, y + 1) || ns && Open(g, x - 1, y) && Open(g, x + 1, y)) return true;
            }
        return false;
    }

    private static bool Open(NavGrid g, int x, int y) =>
        x >= 0 && y >= 0 && x < g.Width && y < g.Height && (g.FlagsAt(x, y) & NavFlags.Blocked) == 0;

    private static bool NearOwnDropOff(World w, int owner, Vector2 p, float within)
    {
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != owner || b.UnderConstruction[k]) continue;
            BuildingDef def = w.Data.Buildings[b.TypeId[k]];
            if (def.DropOff && HeldWorkerProbe.FootprintGap(w.NavGrid, p, b.Cell[k], def.FootprintWidth, def.FootprintHeight) <= within) return true;
        }
        return false;
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
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != u.Owner[i] || b.UnderConstruction[k]) continue;
            BuildingDef bd = w.Data.Buildings[b.TypeId[k]];
            if (bd.DropOff && EconomySystem.InReach(w.NavGrid, u.Position[i], b.Cell[k], bd.FootprintWidth, bd.FootprintHeight)) return true;
        }
        return false;
    }
}
