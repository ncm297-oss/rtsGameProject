using System.Numerics;
using System.Reflection;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M6-S10 (session 2026-11-15-0506, QA full): attacks on the Keep-corner cure (<c>MovementSystem.QueuesBehind</c> +
/// <c>EconomyConstants.QueueArrival</c>, BUG-1431) away from the economy fuzz's own scene, at the knob off and at the shipped
/// 0.5. Every staged shape runs 6,000 ticks and asserts, at the shipped 0.5, the brief's word "held" never happens (a live
/// worker on its loop, its node alive, 1,200 ticks without a delivery, within 2 m of where it stood 600 ticks earlier; checked
/// on every tick past the clock) and that every worker delivers; the knob-off rows print the same as the control (<see cref="Gate"/>).
/// Every row asserts the per-tick invariants (finite positions, nobody on blocked ground). The longest gap is printed. Red at
/// 0.5 on the M6-S10 tree by BUG-1463 (the map-edge Keep, the 2 x 2 depot with Gatherers, the hard-shape shuffles, fuzz seed 72). Shapes: a 2 x 2 depot, a
/// Keep on the map edge and against a cliff, two Keeps side by side, a mine touching the Keep (face and corner), 0.75 m
/// Gatherers and a mixed crowd, 40 workers on one mine, a drop-off destroyed while workers return, a mine that runs out
/// mid-queue, spawn-order shuffles, the knob switched on one tick late, builders at a Keep's corner, and the fuzz on seeds
/// 57-120 at 0 and 0.5.
/// M6-S12 (the Producer's 2026-11-16 decision, no fourth red session): the five rows still red at 0.5 (<c>HardShapes</c>
/// cliff28_25 / depot22_20 / edge1_15, <c>TwoByTwoDepot</c> (22, 20) Gatherers, <c>KeepOnTheMapEdge</c> (1, 15)) are skipped as
/// known-bug rows (BUG-1463), bodies and bounds unchanged; the per-shape trace is in docs/03 "Implementation (M6-S12)".
/// </summary>
[Trait("Category", "Stress")]
public class KeepCornerCureQaStressTests(ITestOutputHelper output)
{
    private const int Ticks = 6000, HeldTicks = 1200, LookBack = 600;

    // DataAt(0.5) hashes as the shipped data on the M6-S10 tree (GatherOverlapShippedTests), and exists on the base too (an A/B run).
    private static GameData Data(double overlap) => GatherOverlapScenes.DataAt(overlap);

    private static int Depot => TestSim.Data.FindBuilding("malazan_depot");

    /// <summary>What a staged run came to.</summary>
    internal sealed record Outcome(List<string> Held, int Longest, int Trips, int Idle, int NeverDelivered, string Line);

    /// <summary>Spawns <paramref name="count"/> workers on free cells of the rectangle (row by row, optionally shuffled) and orders each to gather at <paramref name="node"/>.</summary>
    private static EntityHandle[] Workers(Simulation sim, int count, GatherOverlapScenes.Workers kind, (int X0, int Y0, int X1, int Y1) area, Vector2 node, ulong shuffle = 0)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        var points = new List<Vector2>();
        for (int y = area.Y0; y <= area.Y1 && points.Count < count; y++)
            for (int x = area.X0; x <= area.X1 && points.Count < count; x++)
                if (g.IsPassable(x, y)) points.Add(g.CellCenter(x, y));
        Assert.Equal(count, points.Count);
        var order = new int[count];
        for (int k = 0; k < count; k++) order[k] = k;
        if (shuffle != 0)
        {
            var rng = new SimRng(shuffle, 4242);
            for (int k = count - 1; k > 0; k--)
            {
                int m = rng.NextInt(0, k + 1);
                (order[k], order[m]) = (order[m], order[k]);
            }
        }
        UnitStore u = w.Units;
        var before = (bool[])u.Alive.Clone();
        foreach (int k in order) sim.Enqueue(Command.SpawnUnit(0, GatherOverlapScenes.TypeOf(kind, k), points[k]));
        Run(sim, 2);
        var hs = new List<EntityHandle>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !before[i]) hs.Add(new EntityHandle(i, u.Generation[i]));
        Assert.Equal(count, hs.Count);
        foreach (EntityHandle h in hs) sim.Enqueue(Command.Gather(0, h, node));
        sim.Tick();
        return hs.ToArray();
    }

    /// <summary>The centre of a 2 x 2 mine spawned with its anchor at (x, y).</summary>
    private static Vector2 MineAt(Simulation sim, int x, int y, int gold)
    {
        Spawn(sim.World, Mine, x, y, gold);
        return At(sim, x, y);
    }

    /// <summary>
    /// Ticks the scene and watches every worker for the brief's "held"; <paramref name="atTick"/> runs before each tick (a scene
    /// event: a drop-off razed, the knob switched).
    /// </summary>
    private Outcome Watch(string name, Simulation sim, EntityHandle[] hs, int ticks = Ticks, Action<Simulation, int>? atTick = null)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        int n = hs.Length;
        var trail = new Vector2[n, LookBack + 1];
        var clock = new int[n];
        var cargo = new int[n];
        var node = new EntityHandle[n];
        var delivered = new bool[n];
        var flagged = new int[n];
        Array.Fill(flagged, -1);
        var held = new List<string>();
        int longest = 0, trips = 0;
        for (int k = 0; k < n; k++)
        {
            clock[k] = sim.TickNumber;
            cargo[k] = u.Cargo[hs[k].Index];
            node[k] = u.GatherNode[hs[k].Index];
        }
        for (int t = 0; t < ticks; t++)
        {
            atTick?.Invoke(sim, t);
            sim.Tick();
            int now = sim.TickNumber;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 p = u.Position[i];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"{name}: unit {i} not finite at tick {now}");
            }
            int blocked = MoveScenario.FirstUnitOnBlockedGround(w);
            Assert.True(blocked < 0, $"{name}: unit {blocked} on blocked ground at tick {now}");
            for (int k = 0; k < n; k++)
            {
                int i = hs[k].Index;
                Assert.True(u.IsAlive(hs[k]), $"{name}: worker {i} died");
                trail[k, now % (LookBack + 1)] = u.Position[i];
                if (cargo[k] > 0 && u.Cargo[i] == 0)
                {
                    trips++;
                    delivered[k] = true;
                    clock[k] = now;
                }
                cargo[k] = u.Cargo[i];
                EntityHandle nd = u.GatherNode[i];
                if (nd != node[k] || nd.Generation == 0 || !w.Resources.IsAlive(nd)) clock[k] = now;
                node[k] = nd;
                int since = now - clock[k];
                longest = Math.Max(longest, since);
                // Checked on every tick past the clock, not only the tick it passes 1,200 (HeldWorkerProbe's check): a worker that
                // walked for its first minute and then stood for four is held too. One row per stretch.
                if (since <= HeldTicks || flagged[k] == clock[k]) continue;
                float moved = Vector2.Distance(trail[k, (now - LookBack) % (LookBack + 1)], u.Position[i]);
                if (moved > 2f) continue;
                flagged[k] = clock[k];
                held.Add($"worker {i} (r {u.Radius[i]:F2}) at tick {now} ({since} ticks without a delivery): {u.State[i]} at {u.Position[i]}, cargo {u.Cargo[i]}, goal {u.Goal[i]} cell {u.GoalCell[i]}, moved {moved:F2} m in 600 ticks");
            }
        }
        int idle = 0, never = 0;
        for (int k = 0; k < n; k++)
        {
            if (u.GatherNode[hs[k].Index].Generation == 0) idle++;
            if (delivered[k]) continue;
            never++;
            int i = hs[k].Index;
            held.Add($"never delivered: worker {i} (r {u.Radius[i]:F2}) at the end: {u.State[i]} at {u.Position[i]}, cargo {u.Cargo[i]}, goal {u.Goal[i]} cell {u.GoalCell[i]}, stuck {u.StuckTicks[i]}; "
                + $"its path over the last 600 ticks spans {Span(trail, k, LookBack + 1):F2} m");
        }
        string line = $"{name}: {n} workers, {trips} trips, longest gap {longest / 20.0:F1} s, held {held.Count - never}, loop-less at the end {idle}, never delivered {never}";
        output.WriteLine(line);
        foreach (string h in held) output.WriteLine("  HELD " + h);
        return new Outcome(held, longest, trips, idle, never, line);
    }

    /// <summary>The diagonal of the box a worker's trail fills.</summary>
    private static float Span(Vector2[,] trail, int k, int len)
    {
        Vector2 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (int t = 0; t < len; t++)
        {
            lo = Vector2.Min(lo, trail[k, t]);
            hi = Vector2.Max(hi, trail[k, t]);
        }
        return Vector2.Distance(lo, hi);
    }

    private static void AssertHealthy(Outcome o)
    {
        Assert.True(o.Held.Count == 0, o.Line + "\n" + string.Join("\n", o.Held));
        Assert.True(o.NeverDelivered == 0, o.Line);
    }

    /// <summary>
    /// Asserted at the shipped value only: the knob-off rows are the control (the game before M6-S10 shipped 0.5) and print
    /// their figures. On the M6-S10 tree the off rows of the edge, cliff, 2 x 2 depot, 40-worker and two corner shapes still
    /// hold a worker (BUG-1463's table): a report, since off no longer ships.
    /// </summary>
    private static void Gate(double overlap, Outcome o)
    {
        if (overlap > 0) AssertHealthy(o);
    }

    // ---- Shapes ----------------------------------------------------------------------------------------------------------

    /// <summary>A Keep at (20, 20) (cells 20-23) and a 2 x 2 mine at one of several spots around its south-east corner.</summary>
    [Theory]
    [InlineData(0.0, 25, 25, GatherOverlapScenes.Workers.Laborer, 16)] // diagonal, one cell gap
    [InlineData(0.5, 25, 25, GatherOverlapScenes.Workers.Laborer, 16)]
    [InlineData(0.0, 24, 24, GatherOverlapScenes.Workers.Laborer, 16)] // touching at the corner
    [InlineData(0.5, 24, 24, GatherOverlapScenes.Workers.Laborer, 16)]
    [InlineData(0.0, 24, 23, GatherOverlapScenes.Workers.Mixed, 16)] // touching the east face, straddling the corner
    [InlineData(0.5, 24, 23, GatherOverlapScenes.Workers.Mixed, 16)]
    [InlineData(0.0, 24, 21, GatherOverlapScenes.Workers.Gatherer, 12)] // flush on the east face
    [InlineData(0.5, 24, 21, GatherOverlapScenes.Workers.Gatherer, 12)]
    [InlineData(0.0, 25, 24, GatherOverlapScenes.Workers.Gatherer, 12)]
    [InlineData(0.5, 25, 24, GatherOverlapScenes.Workers.Gatherer, 12)]
    [InlineData(0.0, 26, 25, GatherOverlapScenes.Workers.Mixed, 24)]
    [InlineData(0.5, 26, 25, GatherOverlapScenes.Workers.Mixed, 24)]
    public void MineAtAKeepCorner_NoWorkerHeld(double overlap, int mx, int my, GatherOverlapScenes.Workers kind, int count)
    {
        Simulation sim = GatherOverlapScenes.NewSim(Data(overlap), 48, width: 48, height: 44);
        Vector2 site = MineAt(sim, mx, my, 50_000);
        Building(sim, 20, 20);
        EntityHandle[] hs = Workers(sim, count, kind, (4, 8, 12, 36), site);
        Gate(overlap, Watch($"mine ({mx},{my}) at a Keep corner, {kind} x{count}, {overlap}", sim, hs));
    }

    /// <summary>40 workers (Laborers / mixed) on one mine, flush at a Keep's corner and 10 m off its face.</summary>
    [Theory]
    [InlineData(0.0, GatherOverlapScenes.Workers.Laborer, true)]
    [InlineData(0.5, GatherOverlapScenes.Workers.Laborer, true)]
    [InlineData(0.0, GatherOverlapScenes.Workers.Mixed, true)]
    [InlineData(0.5, GatherOverlapScenes.Workers.Mixed, true)]
    [InlineData(0.0, GatherOverlapScenes.Workers.Mixed, false)]
    [InlineData(0.5, GatherOverlapScenes.Workers.Mixed, false)]
    public void FortyWorkersOnOneMine_NoWorkerHeld(double overlap, GatherOverlapScenes.Workers kind, bool corner)
    {
        Simulation sim = GatherOverlapScenes.NewSim(Data(overlap), 64, width: 56, height: 48);
        Vector2 site = corner ? MineAt(sim, 24, 24, 100_000) : MineAt(sim, 29, 21, 100_000);
        Building(sim, 20, 20);
        EntityHandle[] hs = Workers(sim, 40, kind, (3, 6, 12, 42), site);
        Gate(overlap, Watch($"40 {kind} on one mine ({(corner ? "Keep corner" : "10 m off the face")}), {overlap}", sim, hs));
    }

    /// <summary>A 2 x 2 depot (the only drop-off near the mine; the Keep 25 m off) with the mine diagonal at its corner.</summary>
    [Theory]
    [InlineData(0.0, 23, 23, GatherOverlapScenes.Workers.Laborer, 16)]
    [InlineData(0.5, 23, 23, GatherOverlapScenes.Workers.Laborer, 16)]
    [InlineData(0.0, 22, 22, GatherOverlapScenes.Workers.Mixed, 16)]
    [InlineData(0.5, 22, 22, GatherOverlapScenes.Workers.Mixed, 16)]
    [InlineData(0.0, 22, 20, GatherOverlapScenes.Workers.Gatherer, 12)]
    [InlineData(0.5, 22, 20, GatherOverlapScenes.Workers.Gatherer, 12, Skip = "BUG-1463: loaded 0.75 m Gatherers east of a mine flush on the depot must pass the mine's corners, whose stand points mates hold while gathering (give-up loop; M6-S12 trace in docs/03)")]
    public void TwoByTwoDepot_MineAtItsCorner_NoWorkerHeld(double overlap, int mx, int my, GatherOverlapScenes.Workers kind, int count)
    {
        Simulation sim = GatherOverlapScenes.NewSim(Data(overlap), 48, width: 56, height: 44);
        Vector2 site = MineAt(sim, mx, my, 50_000);
        Building(sim, 44, 30); // the Keep, far
        Building(sim, 20, 20, type: Depot);
        EntityHandle[] hs = Workers(sim, count, kind, (4, 8, 12, 36), site);
        Gate(overlap, Watch($"2x2 depot, mine ({mx},{my}), {kind} x{count}, {overlap}", sim, hs));
    }

    /// <summary>A Keep on the map's west edge (anchor x = 1, the border ring blocked), the mine below it against the edge.</summary>
    [Theory]
    [InlineData(0.0, 1, 15)]
    [InlineData(0.5, 1, 15, Skip = "BUG-1463: a returning worker wedged in the one-cell gap between the Keep's face and the mine's face against the map edge, beside a mate working at the mine (frozen, give-up loop; M6-S12 trace in docs/03)")]
    [InlineData(0.0, 1, 14)]
    [InlineData(0.5, 1, 14)]
    [InlineData(0.0, 5, 14)]
    [InlineData(0.5, 5, 14)]
    public void KeepOnTheMapEdge_NoWorkerHeld(double overlap, int mx, int my)
    {
        Simulation sim = GatherOverlapScenes.NewSim(Data(overlap), 40, width: 40, height: 40);
        Vector2 site = MineAt(sim, mx, my, 50_000);
        Building(sim, 1, 10);
        EntityHandle[] hs = Workers(sim, 16, GatherOverlapScenes.Workers.Mixed, (14, 4, 22, 34), site);
        Gate(overlap, Watch($"Keep on the edge, mine ({mx},{my}), {overlap}", sim, hs));
    }

    /// <summary>A Keep flush against a cliff (a level-1 plateau from column 30), the mine at its corner against the cliff too.</summary>
    [Theory]
    [InlineData(0.0, 28, 25)]
    [InlineData(0.5, 28, 25)]
    [InlineData(0.0, 28, 24)]
    [InlineData(0.5, 28, 24)]
    public void KeepAgainstACliff_NoWorkerHeld(double overlap, int mx, int my)
    {
        var rows = new string[44];
        for (int y = 0; y < rows.Length; y++) rows[y] = new string('0', 30) + new string('1', 18);
        var sim = new Simulation(new SimConfig(5, 1, 40, 192) { Data = Data(overlap) }, FromRows(rows));
        Vector2 site = MineAt(sim, mx, my, 50_000);
        Building(sim, 26, 20);
        Assert.False(sim.World.NavGrid.IsPassable(30, 21)); // the cliff face beside the Keep
        EntityHandle[] hs = Workers(sim, 16, GatherOverlapScenes.Workers.Mixed, (4, 8, 12, 36), site);
        Gate(overlap, Watch($"Keep against a cliff, mine ({mx},{my}), {overlap}", sim, hs));
    }

    /// <summary>Two Keeps side by side (cells 16-19 and 20-23), the mine under their seam; and two Keeps diagonal with the mine in the notch.</summary>
    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.5, false)]
    [InlineData(0.0, true)]
    [InlineData(0.5, true)]
    public void TwoKeepsSideBySide_NoWorkerHeld(double overlap, bool diagonal)
    {
        Simulation sim = GatherOverlapScenes.NewSim(Data(overlap), 48, width: 48, height: 44);
        Vector2 site = diagonal ? MineAt(sim, 20, 24, 50_000) : MineAt(sim, 19, 24, 50_000);
        Building(sim, 16, 20);
        Building(sim, diagonal ? 22 : 20, diagonal ? 26 : 20);
        EntityHandle[] hs = Workers(sim, 20, GatherOverlapScenes.Workers.Mixed, (3, 6, 11, 38), site);
        Gate(overlap, Watch($"two Keeps {(diagonal ? "diagonal" : "side by side")}, {overlap}", sim, hs));
    }

    /// <summary>A depot near the mine razed 1,500 ticks in, while workers walk to it with loads: they re-route to the Keep and keep delivering.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void ADropOffRazedWhileWorkersReturn_TheyGoOnToTheKeep(double overlap)
    {
        Simulation sim = GatherOverlapScenes.NewSim(Data(overlap), 48, width: 56, height: 44);
        Vector2 site = MineAt(sim, 30, 22, 50_000);
        Building(sim, 10, 20); // the Keep, 16 m west
        EntityHandle depot = Building(sim, 33, 25, type: Depot); // at the mine's corner
        EntityHandle[] hs = Workers(sim, 16, GatherOverlapScenes.Workers.Mixed, (40, 6, 50, 38), site);
        World w = sim.World;
        int returning = -1, trips0 = 0;
        Outcome o = Watch($"depot razed at 1,500, {overlap}", sim, hs, atTick: (s, t) =>
        {
            if (t != 1500) return;
            int loaded = 0;
            foreach (EntityHandle h in hs) if (s.World.Units.Cargo[h.Index] > 0 && s.World.Units.State[h.Index] == UnitState.Moving) loaded++;
            returning = loaded;
            s.World.Buildings.Damage(depot, 1_000_000);
        });
        Assert.False(w.Buildings.IsAlive(depot));
        output.WriteLine($"  {returning} loaded walkers when the depot fell");
        Assert.True(returning > 0, "no worker was walking with a load at the razing: the scene did not test it");
        Gate(overlap, o);
        _ = trips0;
    }

    /// <summary>A small mine at a Keep's corner that runs out with a queue at it, a second mine 12 m off: nobody held, nobody left on a dead node.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void AMineRunsOutMidQueue_TheQueueMovesOnCleanly(double overlap)
    {
        Simulation sim = GatherOverlapScenes.NewSim(Data(overlap), 48, width: 56, height: 44);
        Vector2 site = MineAt(sim, 24, 24, 800);
        MineAt(sim, 24, 34, 50_000);
        Building(sim, 20, 20);
        EntityHandle[] hs = Workers(sim, 20, GatherOverlapScenes.Workers.Mixed, (4, 8, 12, 36), site);
        World w = sim.World;
        Outcome o = Watch($"mine runs out mid-queue, {overlap}", sim, hs);
        int onDead = 0;
        foreach (EntityHandle h in hs)
        {
            EntityHandle nd = w.Units.GatherNode[h.Index];
            if (nd.Generation != 0 && !w.Resources.IsAlive(nd)) onDead++;
        }
        Assert.Equal(0, onDead);
        Gate(overlap, o);
    }

    /// <summary>The corner scene with the spawn list shuffled by five seeds: the cure must not depend on slot order.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void SpawnOrderShuffles_AtAKeepCorner_NoWorkerHeld(double overlap)
    {
        var lines = new List<string>();
        foreach (ulong shuffle in new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            Simulation sim = GatherOverlapScenes.NewSim(Data(overlap), 48, width: 48, height: 44);
            Vector2 site = MineAt(sim, 24, 24, 50_000);
            Building(sim, 20, 20);
            EntityHandle[] hs = Workers(sim, 20, GatherOverlapScenes.Workers.Mixed, (4, 8, 12, 36), site, shuffle);
            Outcome o = Watch($"shuffle {shuffle}, {overlap}", sim, hs);
            if (o.Held.Count > 0 || o.NeverDelivered > 0) lines.Add(o.Line + "\n" + string.Join("\n", o.Held));
        }
        output.WriteLine($"{lines.Count} of 8 shuffles hold a worker at {overlap}");
        if (overlap > 0) Assert.True(lines.Count == 0, string.Join("\n", lines)); // off: the control, printed (Gate)
    }

    /// <summary>Sets the World's derived knob flags (test-only, by reflection; QA's M6-S9 seam).</summary>
    private static void SetKnob(World w, float overlap)
    {
        const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(World).GetField("<GatherOverlapOn>k__BackingField", f)!.SetValue(w, overlap > 0f);
        typeof(World).GetField("<GatherPairScale>k__BackingField", f)!.SetValue(w, 1f - overlap);
    }

    /// <summary>The knob switched on one tick late (off data, 0.5 from the second tick on) and switched off mid-run: the fuzz seeds that were held before the cure.</summary>
    [Theory]
    [InlineData(4UL)]
    [InlineData(22UL)]
    [InlineData(23UL)]
    [InlineData(27UL)]
    [InlineData(35UL)]
    [InlineData(44UL)]
    public void KnobOnOneTickLate_ThenOffMidRun_NoWorkerHeld(ulong seed)
    {
        foreach (int offAt in new[] { int.MaxValue, 3000 })
        {
            var probe = new HeldWorkerProbe(96);
            int state = 0;
            GatherOverlapFuzzTests.Run(0, seed, tripWindow: false, watch: (sim, i, since) =>
            {
                if (state == 0) { SetKnob(sim.World, 0.5f); state = 1; }
                if (state == 1 && sim.TickNumber >= offAt) { SetKnob(sim.World, 0f); state = 2; }
                probe.Watch(sim, i, since);
            });
            output.WriteLine($"seed {seed}, knob on a tick late, off at {(offAt == int.MaxValue ? "never" : offAt.ToString())}: longest {probe.Longest / 20.0:F1} s; held {probe.Held.Count}");
            foreach (string h in probe.Held) output.WriteLine("  HELD " + h);
            Assert.True(probe.Held.Count == 0, string.Join("\n", probe.Held));
        }
    }

    /// <summary>
    /// The cure reads every footprint walk, builders included (<c>OnFootprintWalk</c>: <c>BuildTarget</c> set). Twelve Laborers
    /// build a House at a Keep's corner while a mine crowd works beside it: the House finishes and no builder stands out of reach
    /// of the site for 60 s.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void BuildersAtAKeepCorner_AllReachTheSite(double overlap)
    {
        Simulation sim = TestSim.Explored(GatherOverlapScenes.NewSim(Data(overlap), 64, width: 56, height: 44));
        World w = sim.World;
        Vector2 site = MineAt(sim, 26, 20, 50_000);
        Building(sim, 20, 20);
        w.AddToTotal(0, ResourceKind.Gold, 10_000);
        w.AddToTotal(0, ResourceKind.Wood, 10_000);
        EntityHandle[] gatherers = Workers(sim, 16, GatherOverlapScenes.Workers.Mixed, (4, 8, 12, 36), site);
        EntityHandle[] builders = Workers(sim, 12, GatherOverlapScenes.Workers.Laborer, (4, 38, 16, 41), site);
        int house = w.Data.FindBuilding("malazan_billet");
        BuildingDef hd = w.Data.Buildings[house];
        int hx = 24, hy = 24;
        foreach (EntityHandle h in builders) sim.Enqueue(Command.Build(0, h, house, At(sim, hx, hy)));
        int anchor = hy * w.NavGrid.Width + hx;
        var outOfReach = new int[builders.Length];
        int worst = 0, done = -1;
        for (int t = 0; t < 4000 && done < 0; t++)
        {
            sim.Tick();
            for (int k = 0; k < builders.Length; k++)
            {
                int i = builders[k].Index;
                bool standing = w.Units.State[i] != UnitState.Moving;
                bool far = DistanceToFootprint(w.NavGrid, w.Units.Position[i], anchor, hd.FootprintWidth, hd.FootprintHeight) > Economy.EconomyConstants.Reach;
                outOfReach[k] = w.Units.BuildTarget[i].Generation != 0 && standing && far ? outOfReach[k] + 1 : 0;
                worst = Math.Max(worst, outOfReach[k]);
            }
            for (int b = 0; b < w.Buildings.Capacity; b++)
                if (w.Buildings.Alive[b] && w.Buildings.Cell[b] == anchor && !w.Buildings.UnderConstruction[b]) done = sim.TickNumber;
        }
        output.WriteLine($"House at the Keep corner, {overlap}: finished at {(done < 0 ? "never" : done.ToString())}; longest builder standing out of reach {worst} ticks");
        Assert.True(done > 0, "the House never finished");
        Assert.True(worst < HeldTicks, $"a builder stood out of reach of its site for {worst} ticks");
    }

    /// <summary>Criteria 2 / 4 past the developer's sweep: the fuzz (scene, orders, window unchanged) on seeds 57-120, knob off and at 0.5: no held worker.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void FuzzSeeds57To120_NoHeldWorker(double overlap)
    {
        var heldSeeds = new List<string>();
        var walkers = new List<string>();
        int built = 0, worst = 0;
        ulong worstSeed = 0;
        for (ulong seed = 57; seed <= 120; seed++)
        {
            HeldWorkerProbe probe;
            try
            {
                (probe, _) = HeldWorkerProbe.Run(overlap, seed);
            }
            catch (Exception e) when (e.Message.Contains("no node within") || e.Message.Contains("no Keep"))
            {
                output.WriteLine($"seed {seed}: can't be built");
                continue;
            }
            built++;
            if (probe.Longest > worst) (worst, worstSeed) = (probe.Longest, seed);
            if (probe.Longest > 1200) output.WriteLine($"seed {seed} at {overlap}: longest {probe.Longest / 20.0:F1} s; held {probe.Held.Count}; walking {probe.Walkers.Count}");
            foreach (string h in probe.Held) heldSeeds.Add($"seed {seed}: {h}");
            foreach (string k in probe.Walkers) walkers.Add($"seed {seed}: {k}");
            // HeldWorkerProbe looks once, the tick a clock passes 1,200; a worker that walked its first minute and then stood is
            // missed there. Any gap past 90 s is re-checked on every tick (FuzzContinuous).
            if (probe.Longest > 1800)
                foreach (string h in FuzzContinuous(overlap, seed)) heldSeeds.Add($"seed {seed} (continuous): {h}");
        }
        output.WriteLine($"{built} seeds built at {overlap}; longest gap {worst / 20.0:F1} s (seed {worstSeed}); held {heldSeeds.Count}; walking past 60 s {walkers.Count}");
        foreach (string k in walkers) output.WriteLine("  walking: " + k);
        foreach (string h in heldSeeds) output.WriteLine("  HELD " + h);
        Assert.True(heldSeeds.Count == 0, string.Join("\n", heldSeeds));
    }

    /// <summary>
    /// Seed 72 at 0.5 (QA's 57-120 sweep: the one held worker; BUG-1463): the held worker's neighbourhood when its clock passes
    /// 1,200 ticks: every unit within 3 m with its state, loop and radius, and the nearest building footprints. Report only;
    /// run at 0 too for the control.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(0.0)]
    public void Seed72_TheHeldWorkersNeighbourhood_Report(double overlap)
    {
        var probe = new HeldWorkerProbe(96);
        var seen = new List<string>();
        GatherOverlapFuzzTests.Run(overlap, 72, tripWindow: false, watch: (sim, i, since) =>
        {
            probe.Watch(sim, i, since);
            if (since != HeldTicks + 1 || probe.Held.Count == seen.Count) return;
            UnitStore u = sim.World.Units;
            var sb = new System.Text.StringBuilder($"tick {sim.TickNumber} unit {i} {u.State[i]} at {u.Position[i]} stuck {u.StuckTicks[i]}; near:");
            for (int j = 0; j < u.Capacity; j++)
            {
                if (j == i || !u.Alive[j]) continue;
                float d = Vector2.Distance(u.Position[i], u.Position[j]);
                if (d > 3f) continue;
                sb.Append($" [{j} p{u.Owner[j]} r{u.Radius[j]:F2} {u.State[j]} loop {(u.GatherNode[j].Generation != 0 ? "yes" : "no")} cargo {u.Cargo[j]} {d:F2} m at {u.Position[j]}]");
            }
            seen.Add(sb.ToString());
        });
        output.WriteLine($"seed 72 at {overlap}: longest {probe.Longest / 20.0:F1} s, held {probe.Held.Count}");
        foreach (string s in seen) output.WriteLine("  " + s);
    }

    /// <summary>
    /// The developer's open point: <c>save_ai_seed2</c>'s normalized length grew 16,405 bytes (1,230,475 -> 1,246,880) with the
    /// format unchanged. Prints each section's offset and length (by its four-byte tag) so the grown section is named. Report only.
    /// </summary>
    [Fact]
    public void SaveAiSeed2_SectionLengths_Report()
    {
        byte[] save = Save.SaveTestRun.Save(Save.PreMapFixtures.SaveScene("save_ai_seed2"));
        string[] tags = { "HMAP", "WRLD", "RNGS", "GRID", "RSSC", "BLDG", "UNIT", "REGN", "FRST", "LEDR", "STAT", "TECH", "PROJ", "ZONE", "FOG ", "SPAT", "FLOW", "SURR", "SLTH", "EVNT", "CMDS", "AIST", "HUNT", "CTRM", "END!" };
        var found = new List<(int At, string Tag)>();
        int from = 0;
        foreach (string tag in tags)
        {
            byte[] t = System.Text.Encoding.ASCII.GetBytes(tag);
            int at = save.AsSpan(from).IndexOf(t);
            if (at < 0) continue;
            found.Add((from + at, tag));
            from += at + 4;
        }
        for (int k = 0; k < found.Count; k++)
        {
            int end = k + 1 < found.Count ? found[k + 1].At : save.Length;
            output.WriteLine($"{found[k].Tag} at {found[k].At}: {end - found[k].At} bytes");
        }
        output.WriteLine($"total {save.Length}");
    }

    /// <summary>The fuzz on one seed, every worker past 1,200 ticks without a trip checked on every tick for "within 2 m of where it stood 600 ticks earlier".</summary>
    private static List<string> FuzzContinuous(double overlap, ulong seed)
    {
        var trail = new Vector2[96, LookBack + 1];
        var flagged = new int[96];
        Array.Fill(flagged, -1);
        var rows = new List<string>();
        GatherOverlapFuzzTests.Run(overlap, seed, tripWindow: false, watch: (sim, i, since) =>
        {
            int now = sim.TickNumber;
            UnitStore u = sim.World.Units;
            trail[i, now % (LookBack + 1)] = u.Position[i];
            int start = now - since;
            if (since <= HeldTicks || flagged[i] == start) return;
            if (u.GatherNode[i].Generation == 0 || !sim.World.Resources.IsAlive(u.GatherNode[i])) return;
            float moved = Vector2.Distance(trail[i, (now - LookBack) % (LookBack + 1)], u.Position[i]);
            if (moved > 2f) return;
            flagged[i] = start;
            rows.Add($"unit {i} at tick {now} ({since} ticks without a trip): {u.State[i]} at {u.Position[i]}, cargo {u.Cargo[i]}, goal {u.Goal[i]} cell {u.GoalCell[i]}, moved {moved:F2} m in 600 ticks");
        });
        return rows;
    }

    /// <summary>
    /// The shapes where a worker was still held or never delivered on the M6-S10 tree, each with its spawn list shuffled by eight
    /// seeds: how often a run holds a worker at 0 and at 0.5 (a count, so a single unlucky layout is not the whole story).
    /// Asserted: no run holds a worker at the shipped 0.5.
    /// </summary>
    [Theory]
    [InlineData("depot22_20", 0.0)]
    [InlineData("depot22_20", 0.5, Skip = "BUG-1463: loaded 0.75 m Gatherers east of a mine flush on the depot must pass the mine's corners, whose stand points mates hold while gathering (give-up loop; M6-S12 trace in docs/03)")]
    [InlineData("depot22_20_laborers", 0.0)]
    [InlineData("depot22_20_laborers", 0.5)]
    [InlineData("lodge_trees", 0.0)]
    [InlineData("lodge_trees", 0.5)]
    [InlineData("edge1_15", 0.0)]
    [InlineData("edge1_15", 0.5, Skip = "BUG-1463: a returning worker wedged in the one-cell gap between the Keep's face and the mine's face against the map edge, beside a mate working at the mine (frozen, give-up loop; M6-S12 trace in docs/03)")]
    [InlineData("cliff28_25", 0.0)]
    [InlineData("cliff28_25", 0.5, Skip = "BUG-1463: a returning Laborer wedged in the one-cell gap between the Keep's face and the mine's face closed by the cliff, under a mate working at the mine (frozen, give-up loop; M6-S12 trace in docs/03)")]
    public void HardShapes_EightShuffles_HeldRuns(string shape, double overlap)
    {
        int bad = 0;
        var rows = new List<string>();
        for (ulong shuffle = 0; shuffle < 8; shuffle++)
        {
            Simulation sim;
            EntityHandle[] hs;
            switch (shape)
            {
                case "depot22_20":
                {
                    sim = GatherOverlapScenes.NewSim(Data(overlap), 48, width: 56, height: 44);
                    Vector2 site = MineAt(sim, 22, 20, 50_000);
                    Building(sim, 44, 30);
                    Building(sim, 20, 20, type: Depot);
                    hs = Workers(sim, 12, GatherOverlapScenes.Workers.Gatherer, (4, 8, 12, 36), site, shuffle);
                    break;
                }
                case "depot22_20_laborers":
                {
                    sim = GatherOverlapScenes.NewSim(Data(overlap), 48, width: 56, height: 44);
                    Vector2 site = MineAt(sim, 22, 20, 50_000);
                    Building(sim, 44, 30);
                    Building(sim, 20, 20, type: Depot);
                    hs = Workers(sim, 16, GatherOverlapScenes.Workers.Laborer, (4, 8, 12, 36), site, shuffle);
                    break;
                }
                case "lodge_trees":
                {
                    // A 2 x 2 drop-off flush against a two-deep tree line (the Teblor woodcutters' lodge placement), Gatherers.
                    sim = GatherOverlapScenes.NewSim(Data(overlap), 48, width: 56, height: 44);
                    for (int y = 8; y <= 34; y++)
                        for (int x = 22; x <= 23; x++)
                            Spawn(sim.World, Tree, x, y, sim.World.Data.Rules.TreeWood * 50);
                    Building(sim, 44, 30);
                    Building(sim, 20, 20, type: Depot);
                    hs = Workers(sim, 12, GatherOverlapScenes.Workers.Gatherer, (4, 8, 12, 36), At(sim, 22, 21), shuffle);
                    break;
                }
                case "edge1_15":
                {
                    sim = GatherOverlapScenes.NewSim(Data(overlap), 40, width: 40, height: 40);
                    Vector2 site = MineAt(sim, 1, 15, 50_000);
                    Building(sim, 1, 10);
                    hs = Workers(sim, 16, GatherOverlapScenes.Workers.Mixed, (14, 4, 22, 34), site, shuffle);
                    break;
                }
                default:
                {
                    var map = new string[44];
                    for (int y = 0; y < map.Length; y++) map[y] = new string('0', 30) + new string('1', 18);
                    sim = new Simulation(new SimConfig(5, 1, 40, 192) { Data = Data(overlap) }, FromRows(map));
                    Vector2 site = MineAt(sim, 28, 25, 50_000);
                    Building(sim, 26, 20);
                    hs = Workers(sim, 16, GatherOverlapScenes.Workers.Mixed, (4, 8, 12, 36), site, shuffle);
                    break;
                }
            }
            Outcome o = Watch($"{shape} shuffle {shuffle}, {overlap}", sim, hs);
            if (o.Held.Count > 0) { bad++; rows.Add(o.Line); }
        }
        output.WriteLine($"{shape} at {overlap}: {bad} of 8 shuffles hold a worker");
        if (overlap == 0.5) Assert.True(bad == 0, string.Join("\n", rows));
    }
}
