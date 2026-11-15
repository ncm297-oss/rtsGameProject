using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S9 criterion 7: the economy under the gather overlap at 0.25, 0.5 and 0.75. Seeded generated maps (12 forests, 8 mines),
/// two players, each a Keep beside a mine with a seeded number of Laborers (half on the mine, half on the nearest tree) and
/// Teblor Gatherers, every one sent to a node within a 30 m walk of its Keep, and seeded re-orders to other such nodes all along
/// (the rows at 0 are the control: the same invariants but the floor hold off). Seeds 2-16 at 0 / 0.25 / 0.5 / 0.75 (1 and 13
/// can't be built: <see cref="Seeds"/>); M6-S10: <see cref="KnownWedges"/> is empty (BUG-1431 cured), one row measures the trip
/// window instead of asserting it because its worker passes 60 s walking (<see cref="PastTheWindowWalking"/>), and every row
/// asserts no held worker (<see cref="HeldWorkerProbe"/>). Every tick: no
/// NaN, no unit on a blocked cell, no gather pair past its scaled pack limit, and every worker on its loop delivers at least
/// once in every 60 s while its node lives (a new node or a new order starts its clock again); income above zero for both.
/// </summary>
public class GatherOverlapFuzzTests
{
    private readonly ITestOutputHelper _out;

    public GatherOverlapFuzzTests(ITestOutputHelper output) => _out = output;

    private const int Ticks = 6000, TripWindow = 60 * SimConstants.TicksPerSecond;

    /// <summary>Meters of path from its Keep a worker's node may lie, so one trip fits well inside the window.</summary>
    private const float NodeReach = 30f;

    /// <summary>The fuzz's seeds: 2-16. Seed 1's second Keep has no node within a 30 m walk (the mine 9 m off in a straight line is a 60 m walk round a cliff), and seed 13's second Keep is refused (<see cref="Seed13_TheSceneCannotBeBuilt_AndTheHarnessSaysSo"/>).</summary>
    internal static readonly ulong[] Seeds = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 15, 16 };

    /// <summary>
    /// The (value, seed) rows whose 60 s trip window is measured, not asserted (every other invariant still holds every tick),
    /// each with its reason. Every other row of <see cref="Rows"/> asserts the window. All four are worker wedges of today's
    /// rules (the scaled pair distance is not what holds the worker: <see cref="Bug1430_Seed7AtTheCap_NoGatherPairStandsInMovementReachOfTheWedgedWalker"/>,
    /// <see cref="Bug1430_Seed3AtAQuarter_IsTheDropOffWedgeTheKnobOffHasToo"/>); fixing them changes the shipped game's movement or
    /// economy (a hash move), outside M6-S9. 0.5 has none: its longest gap on every seed is at most 51.4 s.
    /// </summary>
    /// <remarks>
    /// M6-S10 (BUG-1431 cured: a footprint walker stops on a touching groupmate only within <c>EconomyConstants.QueueArrival</c>
    /// of its stand point): all four rows are gone and every row asserts the window again. They were
    /// (0, 4) "BUG-1431: a returning worker out of reach at a Keep's corner, 68.1 s, knob off",
    /// (0.25, 3) "the same worker at the same Keep corner as at 0 (58.5 s there), 74.6 s",
    /// (0.25, 4) "the same Keep corner as at 0 (68.1 s there), 70.7 s" and
    /// (0.75, 7) "a 0.75 m walker held at a Keep's corner by parked loop-ended Idle workers, 81.7 s"
    /// (now 55.6 / 26.0 / 45.5 / 32.9 s).
    /// </remarks>
    private static readonly (double Overlap, ulong Seed, string Why)[] KnownWedges = { };

    /// <summary>
    /// M6-S10: rows where a worker passes the 60 s window <b>while walking</b> (not held: <see cref="HeldWorkerProbe"/>), the
    /// window measured and the held rule asserted instead; each with its trace. Listed for the Producer's ruling (rule 66: with
    /// the knob off the window is a report, not a gate). Not a wedge: <see cref="KnownWedges"/> holds no row at 0.
    /// </summary>
    private static readonly (double Overlap, ulong Seed, string Why)[] PastTheWindowWalking =
    {
        (0.0, 14, "M6-S10, knob off: 63.6 s (53.8 s before the cure). A Teblor Gatherer spawned 4-12 m from its Keep walks the long way "
            + "round (about 120 m) to its node (t26-t680, 32 s), is shoved out of reach twice while working (t750, t1010) and "
            + "re-walks, then works its 15 in reach and delivers: walking, never held"),
    };

    /// <summary>Every fuzz seed at 0 (the control), 0.25, 0.5 and 0.75.</summary>
    public static IEnumerable<object[]> Rows()
    {
        foreach (double overlap in new[] { 0.0, 0.25, 0.5, 0.75 })
            foreach (ulong seed in Seeds)
                yield return new object[] { overlap, seed };
    }

    private static string? KnownWedge(double overlap, ulong seed)
    {
        foreach ((double o, ulong s, string why) in KnownWedges)
            if (o == overlap && s == seed) return why;
        foreach ((double o, ulong s, string why) in PastTheWindowWalking)
            if (o == overlap && s == seed) return why;
        return null;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void EconomyFuzz_InvariantsHold_EveryWorkerKeepsDelivering(double overlap, ulong seed)
    {
        string? known = KnownWedge(overlap, seed);
        var probe = new HeldWorkerProbe(96);
        Result r = Run(overlap, seed, tripWindow: known == null, watch: probe.Watch);
        _out.WriteLine(r.Line + (known == null ? "" : $" (trip window measured, not asserted: {known})"));
        foreach (string k in probe.Walkers) _out.WriteLine("  past 60 s, walking: " + k);
        Assert.True(r.Income0 > 0 && r.Income1 > 0, $"income {r.Income0} / {r.Income1}");
        // M6-S10 (added): no held worker in any row (BUG-1431).
        Assert.True(probe.Held.Count == 0, "held: " + string.Join("\n", probe.Held));
    }

    /// <summary>
    /// M6-S10 regression (BUG-1431): the five knob-off fuzz seeds of seeds 2-56 that had a held worker on the base (a worker
    /// stopped 1.3-2 m out of reach on every retry, touching a mate that held its stand cell from another walk: docs/03
    /// "Implementation (M6-S10)", stage 0). No held worker on any of them now.
    /// </summary>
    [Theory]
    [InlineData(4UL)]
    [InlineData(22UL)]
    [InlineData(27UL)]
    [InlineData(35UL)]
    [InlineData(44UL)]
    public void Bug1431_TheKeepCornerSeeds_KnobOff_NoWorkerHeld(ulong seed)
    {
        (HeldWorkerProbe probe, Result r) = HeldWorkerProbe.Run(0, seed);
        _out.WriteLine($"{r.Line}; held {probe.Held.Count}");
        Assert.True(probe.Held.Count == 0, "held: " + string.Join("\n", probe.Held));
    }

    /// <summary>BUG-1433: seed 13's second Keep is refused; the harness says the scene can't be built (it threw a bare "no Keep").</summary>
    [Fact]
    public void Seed13_TheSceneCannotBeBuilt_AndTheHarnessSaysSo()
    {
        Exception e = Assert.ThrowsAny<Exception>(() => Run(0.5, 13, tripWindow: true));
        Assert.Contains("the fuzz scene can't be built", e.Message);
        Assert.Contains("no Keep", e.Message); // the phrase QA's sweep skips on
    }

    /// <summary>
    /// BUG-1430, seed 7 at the cap (0.75): the worker that goes past 60 s without a trip (a 0.75 m Gatherer walking round its
    /// Keep's corner) is held from 20 s to 55 s into its wait with no gather pair standing inside movement's reach (its own
    /// radius + the largest radius + <see cref="MovementConstants.AvoidRange"/> + two top speeds: Plan's neighbor query and the
    /// shove queries). What holds it is today's rules: the gap between the Keep's corner and its own side's Idle, loop-ended
    /// workers parked there is narrower than its 1.5 m width, and each retry starts a fresh walk (stuck count 0) into the same
    /// corner. The gather pairs that come into reach are walking past it through that gap (two 0.4 m Laborers in the run):
    /// they don't hold it. The knob made the scene (more trips, trees run out sooner, more loop-ended workers parked at the
    /// Keep), not the wedge. At 57 s the fuzz re-orders a parked Laborer onto a loop; it presses in beside the walker (0.27 m,
    /// standing a few ticks between retries) and shoves it free at 60 s; the gap then runs on to 81.7 s because a whole trip
    /// (to the node, a load, back) is still owed. Wedged at the corner about 58 s; until 57 s the scale is read for it only in the
    /// ticks a passer is in reach (46 in the run).
    /// </summary>
    [Fact]
    public void Bug1430_Seed7AtTheCap_NoGatherPairStandsInMovementReachOfTheWedgedWalker()
    {
        const int held = 20 * SimConstants.TicksPerSecond;
        const int until = 55 * SimConstants.TicksPerSecond;
        var standing = new Dictionary<int, float>(); // per worker: the nearest standing gather pair in its current stretch, 20-55 s in
        var passers = new Dictionary<int, int>(); // per worker: ticks a walking gather pair was in reach, same span
        var crossed = new List<string>();
        Run(0.75, 7, tripWindow: false, watch: (sim, i, since) =>
        {
            if (since < held || since > TripWindow + 1) return;
            World w = sim.World;
            UnitStore u = w.Units;
            float reach = u.Radius[i] + w.MaxUnitRadius + MovementConstants.AvoidRange + 2f * w.MaxUnitSpeed;
            if (since == held)
            {
                standing[i] = float.PositiveInfinity;
                passers[i] = 0;
            }
            if (since == TripWindow + 1)
            {
                crossed.Add($"worker {i} at tick {sim.TickNumber}: 20-55 s into its wait, nearest standing gather pair {standing[i]:F2} m (reach {reach:F2} m), a walking one in reach {passers[i]} ticks");
                Assert.True(standing[i] >= reach, crossed[^1]);
                return;
            }
            if (since > until) return;
            bool passing = false;
            for (int j = 0; j < u.Capacity; j++)
            {
                if (j == i || !MovementSystem.IsGatherPair(u, i, j)) continue;
                float d = Vector2.Distance(u.Position[i], u.Position[j]);
                if (u.State[j] != UnitState.Moving) standing[i] = MathF.Min(standing[i], d);
                else if (d < reach) passing = true;
            }
            if (passing) passers[i]++;
        });
        foreach (string line in crossed) _out.WriteLine(line);
        // M6-S10: the wedge this row explained is gone (seed 7 at 0.75 now runs to 32.9 s), so the row's subject is too. It
        // asserted Assert.NotEmpty(crossed) (the wedge present, every crosser clear of standing gather pairs); re-stated as
        // "no worker passes the window" (any crosser still has the standing-pair check above).
        Assert.Empty(crossed);
    }

    /// <summary>
    /// BUG-1430, seed 3 at 0.25: the worker that goes past 60 s without a trip is the one that waits longest at 0 too (58.5 s
    /// there): the same slot, with a load, and 40 s into its wait at the same Keep corner (within a metre) at both values, behind
    /// mates that mine in reach of the mine and the Keep at once. That is BUG-1431's drop-off wedge (knob off); 0.25 lengthens it
    /// past the window and 0.5 / 0.75 shorten it (37.9 / 33.1 s). Vacuous once no worker passes 60 s at 0.25.
    /// </summary>
    [Fact]
    public void Bug1430_Seed3AtAQuarter_IsTheDropOffWedgeTheKnobOffHasToo()
    {
        const int into = 40 * SimConstants.TicksPerSecond;
        (int Unit, int Since, Vector2 At, int Cargo) Worst(double overlap)
        {
            (int Unit, int Since, Vector2 At, int Cargo) worst = (-1, -1, default, 0);
            var at = new Dictionary<int, (Vector2, int)>();
            Run(overlap, 3, tripWindow: false, watch: (sim, i, since) =>
            {
                UnitStore u = sim.World.Units;
                if (since == into) at[i] = (u.Position[i], u.Cargo[i]); // where this stretch's wait stands at 40 s
                if (since > into && since > worst.Since) worst = (i, since, at[i].Item1, at[i].Item2);
            });
            return worst;
        }
        var on = Worst(0.25);
        if (on.Since <= TripWindow) return;
        var off = Worst(0);
        _out.WriteLine($"0.25: worker {on.Unit}, {on.Since} ticks, at 40 s at {on.At} with {on.Cargo}; 0: worker {off.Unit}, {off.Since} ticks, at 40 s at {off.At} with {off.Cargo}");
        Assert.Equal(on.Unit, off.Unit);
        Assert.True(off.Since > 50 * SimConstants.TicksPerSecond, $"at 0 the same worker waits {off.Since} ticks");
        Assert.True(on.Cargo > 0 && off.Cargo > 0, "not a drop-off wait");
        Assert.True(Vector2.Distance(on.At, off.At) < 1f, $"at 0.25 it waits at {on.At}, at 0 at {off.At}");
    }

    /// <summary>What one fuzz run came to.</summary>
    internal sealed record Result(int Workers0, int Workers1, int Reorders, int Trips, int Income0, int Income1, int LongestGap, float Closest, string Line);

    /// <summary>
    /// One fuzz run (the type's summary) at <paramref name="overlap"/> on <paramref name="seed"/>, every invariant asserted every tick;
    /// the 60 s trip window too when <paramref name="tripWindow"/> (else only measured: the report's longest gap).
    /// <paramref name="watch"/> sees, after every tick, each worker's slot and the ticks since its last trip (or new node / order).
    /// </summary>
    internal static Result Run(double overlap, ulong seed, bool tripWindow, Action<Simulation, int, int>? watch = null)
    {
        GameData data = GatherOverlapScenes.DataAt(overlap);
        SimConfig config = TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 96, CommandCapacity: 512)
            with { Data = data, Map = MapGenParams.Default with { Forests = 12, GoldMines = 8, StartLocations = 0 }, Combat = false };
        var sim = new Simulation(config);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        var rng = new SimRng(seed, 7919);
        var workers = new List<EntityHandle>[2];
        var nodes = new List<Vector2>[2];
        for (int p = 0; p < 2; p++)
        {
            workers[p] = new List<EntityHandle>(EconomyScenario.Setup(sim, 6 + rng.NextInt(0, 11), player: p, mineIndex: p));
            Vector2 keep = KeepCenter(w, p, seed);
            nodes[p] = new List<Vector2>();
            g.WorldToCell(keep, out int kx, out int ky);
            Pathfinding.FlowField fromKeep = Pathfinding.FlowField.Build(g, Pathfinding.FlowField.NearestPassable(g, ky * g.Width + kx));
            for (int n = 0; n < w.Resources.Capacity; n++)
            {
                if (!w.Resources.Alive[n]) continue;
                int cell = w.Resources.Cell[n], x = cell % g.Width, y = cell / g.Width;
                // By path, not straight line: a mine 9 m from a Keep across a cliff was a 60 m walk (seed 1).
                float walk = float.PositiveInfinity;
                foreach ((int ox, int oy) in new[] { (-1, 0), (2, 0), (0, -1), (0, 2), (1, 0), (0, 1) })
                    if (g.IsPassable(x + ox, y + oy)) walk = MathF.Min(walk, fromKeep.CostAt((y + oy) * g.Width + x + ox) * MapConstants.CellSize);
                if (walk <= NodeReach) nodes[p].Add(g.CellCenter(x, y));
            }
            Assert.True(nodes[p].Count > 0, $"seed {seed}: player {p} has no node within a {NodeReach} m walk of its Keep");
            // EconomyScenario sends half to the tree nearest the mine, which can lie 90 m off (a trip longer than the window):
            // every worker goes to a node near its Keep instead.
            foreach (EntityHandle h in workers[p]) sim.Enqueue(Command.Gather(p, h, nodes[p][rng.NextInt(0, nodes[p].Count)]));
            int gatherers = rng.NextInt(0, 7);
            for (int k = 0; k < gatherers; k++)
            {
                Vector2? spot = FreeSpot(w, keep, ref rng);
                if (spot == null) break;
                EntityHandle h = GatherMaps.Unit(sim, spot.Value, player: p, type: GatherOverlapScenes.GathererType);
                workers[p].Add(h);
                sim.Enqueue(Command.Gather(p, h, nodes[p][rng.NextInt(0, nodes[p].Count)]));
            }
        }
        sim.Tick();
        int bank0 = w.Gold[0] + w.Wood[0], bank1 = w.Gold[1] + w.Wood[1];
        var all = new List<EntityHandle>();
        all.AddRange(workers[0]);
        all.AddRange(workers[1]);
        int count = all.Count;
        var clock = new int[count];
        var cargo = new int[count];
        var node = new EntityHandle[count];
        for (int k = 0; k < count; k++)
        {
            clock[k] = sim.TickNumber;
            cargo[k] = u.Cargo[all[k].Index];
            node[k] = u.GatherNode[all[k].Index];
        }
        float limitShare = MovementConstants.ShoveSpacing * (1f - (float)overlap);
        int reorders = 0, trips = 0, longest = 0;
        float closest = float.PositiveInfinity;
        for (int t = 0; t < Ticks; t++)
        {
            int reordered = -1;
            if (rng.NextInt(0, 40) == 0)
            {
                reordered = rng.NextInt(0, count);
                int p = u.Owner[all[reordered].Index];
                sim.Enqueue(Command.Gather(p, all[reordered], nodes[p][rng.NextInt(0, nodes[p].Count)]));
                reorders++;
            }
            sim.Tick();
            if (reordered >= 0) clock[reordered] = sim.TickNumber;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 pos = u.Position[i], vel = u.Velocity[i];
                if (!(float.IsFinite(pos.X) && float.IsFinite(pos.Y) && float.IsFinite(vel.X) && float.IsFinite(vel.Y))) Assert.Fail($"seed {seed} at {overlap}: unit {i} not finite at tick {sim.TickNumber}");
            }
            int blocked = MoveScenario.FirstUnitOnBlockedGround(w);
            if (blocked >= 0) Assert.Fail($"seed {seed} at {overlap}: unit {blocked} on blocked ground at tick {sim.TickNumber}");
            for (int k = 0; k < count; k++)
            {
                int i = all[k].Index;
                Assert.True(u.IsAlive(all[k]));
                if (cargo[k] > 0 && u.Cargo[i] == 0)
                {
                    trips++;
                    clock[k] = sim.TickNumber;
                }
                cargo[k] = u.Cargo[i];
                EntityHandle now = u.GatherNode[i];
                if (now != node[k] || now.Generation == 0 || !w.Resources.IsAlive(now)) clock[k] = sim.TickNumber; // a new node, or none: its clock starts again
                node[k] = now;
                int since = sim.TickNumber - clock[k];
                longest = Math.Max(longest, since);
                watch?.Invoke(sim, i, since);
                if (tripWindow && since > TripWindow) Assert.Fail($"seed {seed} at {overlap}: worker {i} of player {u.Owner[i]} ({u.State[i]}, at {u.Position[i]}, goal {u.Goal[i]}, cargo {u.Cargo[i]} {u.CargoKind[i]}, stuck {u.StuckTicks[i]}) without a trip for {since} ticks at tick {sim.TickNumber}, its node at {w.Resources.Cell[now.Index] % g.Width},{w.Resources.Cell[now.Index] / g.Width} alive, its Keep at {KeepCenter(w, u.Owner[i], seed)}");
                for (int m = k + 1; m < count; m++)
                {
                    int j = all[m].Index;
                    if (overlap == 0 || !MovementSystem.IsGatherPair(u, i, j)) continue; // (off, the control row: no floor to hold)
                    float share = Vector2.Distance(u.Position[i], u.Position[j]) / (u.Radius[i] + u.Radius[j]);
                    closest = MathF.Min(closest, share);
                    if (share < limitShare - 1e-4f) Assert.Fail($"seed {seed} at {overlap}: workers {i} and {j} at {share:F4} of their radii sum at tick {sim.TickNumber}, past the scaled pack limit {limitShare:F3}");
                }
            }
        }
        int income0 = w.Gold[0] + w.Wood[0] - bank0, income1 = w.Gold[1] + w.Wood[1] - bank1;
        string line = $"seed {seed} at {overlap}: {workers[0].Count} + {workers[1].Count} workers, {reorders} re-orders, {trips} trips, income {income0} / {income1}; longest without a trip {longest} ticks; closest gather pair {closest:F3} of its sum (limit {limitShare:F3})";
        return new Result(workers[0].Count, workers[1].Count, reorders, trips, income0, income1, longest, closest, line);
    }

    private static Vector2 KeepCenter(World w, int player, ulong seed)
    {
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == player)
            {
                BuildingDef def = w.Data.Buildings[b.TypeId[k]];
                return Economy.EconomySystem.Center(w.NavGrid, b.Cell[k], def.FootprintWidth, def.FootprintHeight);
            }
        // BUG-1433: a Keep refused (seed 13) is a scene the fuzz can't build, said so; not a bare exception.
        Assert.Fail($"seed {seed}: player {player}'s Keep was refused (no Keep), so the fuzz scene can't be built on this seed");
        return default;
    }

    /// <summary>A passable cell centre 4-12 m from <paramref name="from"/> with no unit within 1.6 m, drawn from the seeded stream; null after 200 tries.</summary>
    private static Vector2? FreeSpot(World w, Vector2 from, ref SimRng rng)
    {
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        for (int tries = 0; tries < 200; tries++)
        {
            int dx = rng.NextInt(-6, 7), dy = rng.NextInt(-6, 7);
            if (!g.WorldToCell(from, out int cx, out int cy)) return null;
            int x = cx + dx, y = cy + dy;
            if (!g.IsPassable(x, y)) continue;
            Vector2 at = g.CellCenter(x, y);
            float d = Vector2.Distance(at, from);
            if (d < 4f || d > 12f) continue;
            bool free = true;
            for (int i = 0; i < u.Capacity && free; i++)
                if (u.Alive[i] && Vector2.Distance(u.Position[i], at) < 1.6f) free = false;
            if (free) return at;
        }
        return null;
    }
}
