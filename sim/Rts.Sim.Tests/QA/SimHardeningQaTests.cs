using System.Diagnostics;
using System.Numerics;
using Rts.Cli;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M3-H1, session 2026-10-07-0800): the Build apply path's cheap-first order against <see cref="World.CanPlace"/>
/// (pass / fail on random states, every reason), refused-Build cost by reason at a long-detour anchor, push-out of 400
/// units stacked in a Keep (ring by ring, never two on a cell), and the CLI's early <c>--record</c> open.
/// </summary>
[Collection(SerialCollection.Name)]
public class SimHardeningQaTests
{
    private readonly ITestOutputHelper _out;

    public SimHardeningQaTests(ITestOutputHelper output) => _out = output;

    private static int Barracks => TestSim.Data.FindBuilding("malazan_barracks");

    // ------------------------------------------------------------------ CanPlace vs Build

    /// <summary>
    /// 300 random probes per seed on a generated 64 x 64 map with forests, mines, two players' units (some holding)
    /// and a building store that fills up: for each anchor and type (own, other faction's, unknown, off the map edge)
    /// and totals (rich, none, exactly the cost, one short), <see cref="World.CanPlace"/>'s pass / fail equals the Build
    /// apply path's (<see cref="ConstructionSystem.StartBuild"/>) for a worker that isn't holding. Every reason turns up.
    /// </summary>
    [Theory]
    [InlineData(11UL)]
    [InlineData(12UL)]
    [InlineData(13UL)]
    [InlineData(14UL)]
    public void CanPlaceAndBuild_AgreeOnPassFail_RandomStates(ulong seed)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 256) with
        {
            BuildingCapacity = 12,
            Map = MapGenParams.Default with { Width = 64, Height = 64, Forests = 14, GoldMines = 4, MineSpacing = 8f },
        });
        World w = sim.World;
        NavGrid g = w.NavGrid;
        var rng = new SimRng(seed, 41);
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++) if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        for (int k = 0; k < 48; k++)
        {
            int c = open[rng.NextInt(0, open.Count)];
            sim.Enqueue(Command.SpawnUnit(k % 2, Laborer, g.CellCenter(c % g.Width, c / g.Width)));
        }
        Run(sim, 2);
        UnitStore u = w.Units;
        int worker = -1;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (worker < 0 && u.Owner[i] == 0) { worker = i; continue; }
            if (rng.NextInt(0, 4) == 0) sim.Enqueue(Command.HoldPosition(u.Owner[i], new EntityHandle(i, u.Generation[i])));
        }
        Run(sim, 2);
        var reasons = new int[16];
        int probes = 0, placed = 0;
        int[] types = { House, House, Keep, Barracks, WhirlwindHouse, 999 };
        for (int k = 0; k < 300; k++)
        {
            int type = types[rng.NextInt(0, types.Length)];
            int x, y;
            if (rng.NextInt(0, 3) == 0 && u.Alive[worker] && g.WorldToCell(u.Position[rng.NextInt(0, 3) == 0 ? worker : rng.NextInt(0, 48)], out x, out y)) { }
            else { x = rng.NextInt(0, g.Width); y = rng.NextInt(0, g.Height); }
            if (w.Buildings.SlotAt(x, y) >= 0) continue; // the join path, not a placement
            int cost = (uint)type < (uint)w.Data.Buildings.Length ? w.Data.Buildings[type].CostWood : 0;
            int gold = (uint)type < (uint)w.Data.Buildings.Length ? w.Data.Buildings[type].CostGold : 0;
            switch (rng.NextInt(0, 4))
            {
                case 0: SetTotals(sim, 0, 1_000_000, 1_000_000); break;
                case 1: SetTotals(sim, 0, 0, 0); break;
                case 2: SetTotals(sim, 0, gold, cost); break;
                default: SetTotals(sim, 0, gold, Math.Max(0, cost - 1)); break;
            }
            if (u.Hold[worker]) u.Hold[worker] = false;
            bool can = w.CanPlace(0, type, y * g.Width + x, out PlacementError why);
            reasons[(int)why]++;
            probes++;
            bool did = ConstructionSystem.StartBuild(w, worker, type, g.CellCenter(x, y), replaceQueue: true);
            Assert.True(can == did, $"seed {seed} probe {k}: CanPlace {can} ({why}) but Build {did} for type {type} at ({x}, {y})");
            if (did)
            {
                placed++;
                int site = w.Buildings.SlotAt(x, y);
                if (rng.NextInt(0, 2) == 0) w.Buildings.Damage(w.Buildings.HandleOf(site), 1_000_000);
            }
            if (rng.NextInt(0, 8) == 0) sim.Tick();
            Assert.Null(Stress.ResourceOracle.Reach(g));
        }
        string summary = string.Join(", ", Enumerable.Range(0, reasons.Length).Where(r => reasons[r] > 0).Select(r => $"{(PlacementError)r} {reasons[r]}"));
        _out.WriteLine($"seed {seed}: {probes} probes, {placed} placed; CanPlace reasons: {summary}");
    }

    /// <summary>
    /// BUG-0092 (b)'s documented divergence, pinned: a holding worker standing in the footprint gets its own Build
    /// accepted while <see cref="World.CanPlace"/> (no worker) says <see cref="PlacementError.UnitInTheWay"/>; the
    /// same worker holding outside the footprint changes nothing.
    /// </summary>
    [Fact]
    public void HoldingWorkerInsideTheFootprint_TheOnlyCanPlaceBuildDivergence()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        Give(sim, 0, 1000, 1000);
        EntityHandle w = Unit(sim, At(sim, 10, 10));
        sim.Enqueue(Command.HoldPosition(0, w));
        Run(sim, 2);
        Assert.False(sim.World.CanPlace(0, House, Cell(sim, 15, 10), out _) == false);
        Assert.False(sim.World.CanPlace(0, House, Cell(sim, 10, 10), out PlacementError why));
        Assert.Equal(PlacementError.UnitInTheWay, why);
        Assert.True(ConstructionSystem.StartBuild(sim.World, w.Index, House, At(sim, 10, 10), replaceQueue: true));
    }

    // ------------------------------------------------------------------ refused Builds by reason

    /// <summary>The dev's long wall (one tree wide down x = 64 from y 3), optionally down to the bottom border so the top gap is the only way round.</summary>
    private static Simulation LongWall(bool closedAtTheBottom, int buildingCapacity = 64)
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 2, UnitCapacity: 160, CommandCapacity: 512) with { ResourceCapacity = 256, BuildingCapacity = buildingCapacity }, Flat(128, 128));
        for (int y = 3; y < (closedAtTheBottom ? 127 : 122); y++) Spawn(sim.World, Tree, 64, y, TreeWood);
        return sim;
    }

    private double HundredBuilds(Simulation sim, PlacementError expected, string label)
    {
        EntityHandle[] workers = Enumerable.Range(0, 100).Select(k => Unit(sim, At(sim, 4 + k % 50, 20 + k / 50))).ToArray();
        if (expected != PlacementError.UnitInTheWay) Assert.False(sim.World.CanPlace(0, House, Cell(sim, 64, 1), out PlacementError _));
        int before = sim.World.Buildings.Count;
        sim.Enqueue(Command.Build(0, workers[0], House, At(sim, 64, 1)));
        Run(sim, 2); // JIT
        foreach (EntityHandle wk in workers) sim.Enqueue(Command.Build(0, wk, House, At(sim, 64, 1)));
        sim.Tick();
        sim.World.Seal.ForgetForTests(); // the JIT tick's answer is kept (BUG-0096 memo): time the first flood too
        long t0 = Stopwatch.GetTimestamp();
        sim.Tick();
        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        Assert.Equal(before, sim.World.Buildings.Count);
        _out.WriteLine($"100 Builds refused ({label}) at the long-detour anchor: tick {ms:F3} ms");
        return ms;
    }

    /// <summary>The cheap reasons other than CannotAfford (the dev's row): an enemy unit in the footprint, a full building store. Each 100 in a tick under 2 ms Debug.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void HundredBuildsRefusedForUnitInTheWayOrStoreFull_AtALongDetourAnchor_Under2Ms()
    {
        Simulation inTheWay = LongWall(closedAtTheBottom: false);
        Give(inTheWay, 0, 1_000_000, 1_000_000);
        Unit(inTheWay, At(inTheWay, 65, 2), player: 1);
        Assert.False(inTheWay.World.CanPlace(0, House, Cell(inTheWay, 64, 1), out PlacementError why));
        Assert.Equal(PlacementError.UnitInTheWay, why);
        double a = HundredBuilds(inTheWay, PlacementError.UnitInTheWay, "enemy unit in the way");

        Simulation full = LongWall(closedAtTheBottom: false, buildingCapacity: 1);
        Give(full, 0, 1_000_000, 1_000_000);
        Building(full, 20, 60, type: House);
        Assert.False(full.World.CanPlace(0, House, Cell(full, 64, 1), out why));
        Assert.Equal(PlacementError.StoreFull, why);
        double b = HundredBuilds(full, PlacementError.StoreFull, "store full");
        Assert.True(a < 2.0 && b < 2.0, $"{a:F3} / {b:F3} ms");
    }

    /// <summary>
    /// The one reason the cheap-first order can't make cheap. With the wall closed at the bottom a House in the top gap
    /// seals the map, so a refused Build floods one side (~7,900 cells) to prove it. BUG-0096 (fixed M3-H2): the answer
    /// is kept per (footprint, grid version), so 100 in a tick flood once (was 33 ms, Debug). Was <c>..._Report</c>.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void HundredBuildsRefusedForSealsGround_OneTick_Under2Ms()
    {
        Simulation sim = LongWall(closedAtTheBottom: true);
        Give(sim, 0, 1_000_000, 1_000_000);
        Assert.False(sim.World.CanPlace(0, House, Cell(sim, 64, 1), out PlacementError why));
        Assert.Equal(PlacementError.SealsGround, why);
        double ms = HundredBuilds(sim, PlacementError.SealsGround, "SealsGround");
        Assert.True(ms < 2.0, $"{ms:F3} ms");
    }

    // ------------------------------------------------------------------ push-out

    /// <summary>
    /// 400 own units stacked inside a Keep's 4 x 4 footprint on a flat 128 x 128 map (positions set as a test seam), the
    /// Keep built on them. Every unit leaves the footprint for its own passable cell, and ring by ring: a unit set down
    /// on ring R means every cell of rings 1 to R - 1 holds a unit. Times the Build's apply (Debug).
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void FourHundredUnitsStackedInAKeep_PushedRingByRing_DistinctCells_Timed()
    {
        (Simulation warm, int warmWorker) = Stack400();
        Assert.True(ConstructionSystem.StartBuild(warm.World, warmWorker, Keep, At(warm, 60, 60), replaceQueue: true)); // JIT
        (Simulation sim, int worker) = Stack400();
        long t0 = Stopwatch.GetTimestamp();
        bool placed = ConstructionSystem.StartBuild(sim.World, worker, Keep, At(sim, 60, 60), replaceQueue: true);
        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        Assert.True(placed);
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        var occupied = new HashSet<int>();
        int maxRing = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || i == worker) continue; // the worker stood far off, outside the footprint
            Assert.True(g.WorldToCell(u.Position[i], out int cx, out int cy));
            Assert.True(g.IsPassable(cx, cy), $"unit {i} on blocked ({cx}, {cy})");
            Assert.True(occupied.Add(cy * g.Width + cx), $"unit {i} shares ({cx}, {cy})");
            maxRing = Math.Max(maxRing, Ring(cx, cy));
        }
        for (int y = 60 - maxRing + 1; y < 64 + maxRing - 1; y++)
            for (int x = 60 - maxRing + 1; x < 64 + maxRing - 1; x++)
                if (Ring(x, y) > 0 && Ring(x, y) < maxRing) Assert.True(occupied.Contains(y * g.Width + x), $"({x}, {y}) on ring {Ring(x, y)} left free while units went to ring {maxRing}");
        _out.WriteLine($"400 units stacked in a Keep: Build apply {ms:F3} ms, outermost ring {maxRing}");
        Assert.True(ms < 5.0, $"{ms:F3} ms"); // a generous ceiling for 25x the dev row's pushed units; the number is the report

        static int Ring(int x, int y) => Math.Max(Math.Max(60 - x, x - 63), Math.Max(60 - y, y - 63));
    }

    private static (Simulation Sim, int Worker) Stack400()
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 512, CommandCapacity: 1024), Flat(128, 128));
        NavGrid g = sim.World.NavGrid;
        for (int k = 0; k < 401; k++) sim.Enqueue(Command.SpawnUnit(0, Laborer, g.CellCenter(4 + k % 100, 4 + k / 100 * 2)));
        Run(sim, 2);
        UnitStore u = sim.World.Units;
        int n = 0;
        for (int i = 0; i < u.Capacity && n < 400; i++)
        {
            if (!u.Alive[i] || i == 400) continue;
            u.Position[i] = u.PrevPosition[i] = new Vector2(120.1f + 0.39f * (n % 20), 120.1f + 0.39f * (n / 20));
            n++;
        }
        SetTotals(sim, 0, 1000, 1000);
        return (sim, 400);
    }

    /// <summary>
    /// A House built on 30 own units on a small level-1 plateau with 13 free cells. BUG-0095 / BUG-0097 (fixed M3-H2):
    /// rings stop at the plateau's box, the plateau is found full once, and the 17 leftovers spread over its cells (one
    /// each before any cell takes a second, each pass off the center), all on the plateau, no two on one point. Was
    /// <c>PushOutOnAFullSmallPlateau_ScansTheMap_Report</c> (18 on one cell, 153 identical pairs). The cost is now a bound.
    /// </summary>
    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(128)]
    [InlineData(256)]
    public void PushOutOnAFullSmallPlateau_CostsThePlateau_Under1Ms(int size)
    {
        (Simulation warm, int ww) = Plateau(size);
        ConstructionSystem.StartBuild(warm.World, ww, House, At(warm, 61, 61), replaceQueue: true);
        (Simulation sim, int worker) = Plateau(size);
        long t0 = Stopwatch.GetTimestamp();
        bool placed = ConstructionSystem.StartBuild(sim.World, worker, House, At(sim, 61, 61), replaceQueue: true);
        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        Assert.True(placed);
        _out.WriteLine($"{size} x {size}: 30 units on a plateau with {PlateauFree(sim.World.NavGrid)} free cells: Build apply {ms:F3} ms");
        Assert.True(ms < 1.0, $"{ms:F3} ms");
    }

    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    public void PushOutOnAFullSmallPlateau_LeftoversSpreadOnThePlateau_NoTwoOnOnePoint(int size)
    {
        (Simulation sim, int worker) = Plateau(size);
        Assert.True(ConstructionSystem.StartBuild(sim.World, worker, House, At(sim, 61, 61), replaceQueue: true));
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        int free = PlateauFree(g);
        var share = new Dictionary<int, int>();
        int same = 0, maxShare = 0;
        for (int i = 0; i < 30; i++)
        {
            Assert.True(g.WorldToCell(u.Position[i], out int cx, out int cy));
            Assert.True(g.IsPassable(cx, cy) && g.LevelAt(cx, cy) == 1, $"unit {i} left the plateau for ({cx}, {cy})");
            share[cy * g.Width + cx] = share.GetValueOrDefault(cy * g.Width + cx) + 1;
            maxShare = Math.Max(maxShare, share[cy * g.Width + cx]);
            for (int j = i + 1; j < 30; j++) if (u.Position[i] == u.Position[j]) same++;
        }
        _out.WriteLine($"{size} x {size}: 30 units, {free} free plateau cells: {share.Count} cells used, most units on one cell {maxShare}, identical-position pairs {same}");
        Assert.Equal(free, share.Count);                  // every plateau cell takes one before any takes more
        Assert.True(maxShare <= (30 + free - 1) / free, $"{maxShare} on one cell"); // as even as the pigeonhole allows
        Assert.Equal(0, same);
        // Local movement keeps them finite and on passable ground.
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y), $"tick {t}: unit {i} at {u.Position[i]}");
                Assert.True(g.WorldToCell(u.Position[i], out int cx, out int cy) && g.IsPassable(cx, cy), $"tick {t}: unit {i} at {u.Position[i]} on blocked ground");
            }
        }
    }

    private static int PlateauFree(NavGrid g)
    {
        int n = 0;
        for (int y = 0; y < g.Height; y++) for (int x = 0; x < g.Width; x++) if (g.IsPassable(x, y) && g.LevelAt(x, y) == 1) n++;
        return n;
    }

    private static (Simulation Sim, int Worker) Plateau(int size)
    {
        // A size x size level-0 map with a 6 x 6 level-1 block at (58-63, 58-63) and a ramp down its west side at y 60.
        var rows = new string[size];
        for (int y = 0; y < size; y++)
        {
            var row = new char[size];
            for (int x = 0; x < size; x++) row[x] = x >= 58 && x <= 63 && y >= 58 && y <= 63 ? '1' : '0';
            if (y == 60) row[57] = 'r';
            rows[y] = new string(row);
        }
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 64, CommandCapacity: 128), FromRows(rows));
        NavGrid g = sim.World.NavGrid;
        for (int k = 0; k < 31; k++) sim.Enqueue(Command.SpawnUnit(0, Laborer, g.CellCenter(4 + k, 4)));
        Run(sim, 2);
        UnitStore u = sim.World.Units;
        for (int i = 0; i < 30; i++) u.Position[i] = u.PrevPosition[i] = new Vector2(122.2f + 0.25f * (i % 6), 122.2f + 0.6f * (i / 6));
        SetTotals(sim, 0, 1000, 1000);
        return (sim, 30);
    }

    // ------------------------------------------------------------------ CLI --record

    /// <summary>BUG-0072: an existing read-only replay file is refused before the first tick (no header, no checkpoint), and keeps its contents.</summary>
    [Fact]
    public void Run_RecordOntoAReadOnlyFile_FailsBeforeTheFirstTick_AndLeavesItIntact()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rts-cli-qa", "h1-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "locked" + ReplayFormat.FileExtension);
        File.WriteAllText(path, "keep me");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int exit = CliRunner.Run(new[] { "run", "--seed", "1", "--units", "5", "--ticks", "200", "--record", path, "--data", TestDataDir.Shipped }, stdout, stderr);
            _out.WriteLine(stderr.ToString().Trim());
            Assert.Equal(1, exit);
            Assert.Equal("", stdout.ToString());
            Assert.StartsWith($"error: cannot write replay '{path}'", stderr.ToString());
            Assert.Equal("keep me", File.ReadAllText(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }
}
