using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S5 criterion 7: what shared vision costs a whole tick, timed as <see cref="VisionGatePerfTests"/>' fresh-process row
/// (10 warm-up ticks, then <see cref="MeasuredTicks"/>, in a child test process: the fast figure, about a second of load).
/// The scene is the 4-start 256 map with each seat's start base (hall, five workers) and 125 fighters of each seat brawling
/// round the centre (500 units), the two sides west and east of it: as 2v2 (teams 1, 1, 2, 2: the allies start west and
/// east, so a side's sight spans the map) and as a free-for-all (every seat team 0) for the reference.
/// </summary>
[Collection(SerialCollection.Name)]
public class SharedVisionPerfTests
{
    private const int MeasuredTicks = 300, PerSeat = 125, Size = 256;

    /// <summary>docs/03's tick budget for 500 units.</summary>
    private const double TickBudgetMs = 4.0;

    private readonly ITestOutputHelper _out;

    public SharedVisionPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// The brawl on seed 1's 4-start 256 map: seats of <paramref name="teams"/>; side A (players 0 and 1, allies with teams
    /// 1, 1, 2, 2) spawns west of the centre, side B (2 and 3) east, each seat's units interleaved in its side's half, and every
    /// fighter attack-moves 12 m past the centre into the other half.
    /// </summary>
    internal static Simulation Brawl256(params int[] teams)
    {
        ImmutableArray<PlayerSeat> seats = TeamTests.Seats(teams);
        var map = MapGenParams.Default with { Width = Size, Height = Size, Forests = 24, GoldMines = 16, StartLocations = 4 };
        var sim = new Simulation(new SimConfig(1, 4, 4 * PerSeat + 64, 8 * PerSeat + 256) { Data = TestSim.Data, Map = map, Seats = seats });
        SaveScenes.PlaceBases(sim, army: false);
        NavGrid g = sim.World.NavGrid;
        int center = MoveScenario.CentralCell(g);
        int cx = center % g.Width;
        Pathfinding.FlowField field = Pathfinding.FlowField.Build(g, center);
        var halves = new[] { new List<int>(), new List<int>() };
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            if (!(field.CostAt(c) <= 16f)) continue;
            if (c % g.Width < cx - 1) halves[0].Add(c);
            else if (c % g.Width > cx + 1) halves[1].Add(c);
        }
        var rng = new Determinism.SimRng(1, 73);
        for (int k = 0; k < 2 * PerSeat; k++)
        {
            for (int side = 0; side < 2; side++)
            {
                int owner = 2 * side + (k & 1);
                int cell = halves[side][rng.NextInt(0, halves[side].Count)];
                Vector2 corner = MoveScenario.Center(g, cell) - new Vector2(MapConstants.CellSize / 2);
                int type = side == 0 ? CombatScenes.HeavyInfantry : CombatScenes.Raider;
                sim.Enqueue(Command.SpawnUnit(owner, type, corner + new Vector2(0.05f + rng.NextFloat() * 1.9f, 0.05f + rng.NextFloat() * 1.9f)));
            }
        }
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        Vector2 c0 = MoveScenario.Center(g, center);
        int fighter0 = CombatScenes.HeavyInfantry, fighter1 = CombatScenes.Raider;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && (u.TypeId[i] == fighter0 || u.TypeId[i] == fighter1))
                sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), c0 + new Vector2(u.Owner[i] < 2 ? 12f : -12f, 0f)));
        return sim;
    }

    private static Simulation Scene(string name) => name switch
    {
        "ffa_250v250" => CombatScenes.MapBrawl(3, 250),
        "2v2_256" => Brawl256(1, 1, 2, 2),
        "ffa4_256" => Brawl256(0, 0, 0, 0),
        _ => throw new ArgumentException(name),
    };

    private static double AverageTickMs(Simulation sim)
    {
        for (int t = 0; t < 10; t++) sim.Tick();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long start = Stopwatch.GetTimestamp();
        for (int t = 0; t < MeasuredTicks; t++) sim.Tick();
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / MeasuredTicks;
    }

    private const string ChildVariable = "RTS_PERF_FRESH_CHILD_SCENE";
    private const string ChildLine = "FRESH-PROCESS scene avg ";

    /// <summary>The child's half: times the scene the parent named.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void FreshChild_TimesTheNamedScene()
    {
        string? scene = Environment.GetEnvironmentVariable(ChildVariable);
        if (string.IsNullOrEmpty(scene)) return; // only as a child
        double ms = AverageTickMs(Scene(scene));
        _out.WriteLine(FormattableString.Invariant($"{ChildLine}{ms:F3} ms"));
    }

    /// <summary>
    /// The 2v2 on the 4-seat 256 map stays under docs/03's 4 ms design budget in a fresh process (best of three children), and
    /// the free-for-all of the same layout is reported beside it.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void TwoVersusTwo_On256_FreshProcess_Under4Ms()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ChildVariable))) return;
        var teams = new List<double>();
        var ffa = new List<double>();
        for (int k = 0; k < 3; k++)
        {
            teams.Add(RunFreshChild("2v2_256"));
            ffa.Add(RunFreshChild("ffa4_256"));
        }
        _out.WriteLine($"4-seat 256 brawl, fresh process, {MeasuredTicks} ticks: 2v2 {Join(teams)} ms; free-for-all {Join(ffa)} ms (target 2v2 < {TickBudgetMs} ms)");
        Assert.True(teams.Min() < TickBudgetMs, $"2v2 best of three {teams.Min():F2} ms (docs/03: < {TickBudgetMs} ms for 500 units)");
    }

    /// <summary>The free-for-all 250 v 250 of <see cref="VisionGatePerfTests"/>, timed the same way: reported (its gate is that row's).</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void FreeForAll250v250_FreshProcess_Report()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ChildVariable))) return;
        var readings = new List<double>();
        for (int k = 0; k < 3; k++) readings.Add(RunFreshChild("ffa_250v250"));
        _out.WriteLine($"250 v 250 free-for-all, fresh process, {MeasuredTicks} ticks: {Join(readings)} ms");
        Assert.True(readings.Min() > 0);
    }

    /// <summary>
    /// The fog update alone (phase 12, every 4th tick) on the 4-seat 256 brawl after 100 ticks, 2v2 and free-for-all, 400
    /// updates each, best of three: reported (the A/B of the union designs reads it; the tick rows above gate).
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void FogUpdate_On256_2v2AndFreeForAll_Report()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ChildVariable))) return;
        double Time(params int[] teams)
        {
            Simulation sim = Brawl256(teams);
            for (int t = 0; t < 100; t++) sim.Tick();
            double best = double.MaxValue;
            for (int round = 0; round < 3; round++)
            {
                long start = Stopwatch.GetTimestamp();
                for (int k = 0; k < 400; k++) sim.World.Fog.Update();
                best = Math.Min(best, (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / 400);
            }
            return best;
        }
        Time(1, 1, 2, 2); // warm-up
        double teams = Time(1, 1, 2, 2), ffa = Time(0, 0, 0, 0);
        _out.WriteLine($"fog update on the 4-seat 256 brawl, ms per update (every 4th tick): 2v2 {teams:F3}, free-for-all {ffa:F3}");
        Assert.True(teams > 0 && ffa > 0);
    }

    private static string Join(List<double> xs) => string.Join(" / ", xs.Select(x => x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));

    private const int ChildTimeoutMs = 120_000;

    /// <summary>Runs <see cref="FreshChild_TimesTheNamedScene"/> for <paramref name="scene"/> as a child <c>dotnet test</c> of this assembly; its average.</summary>
    private static double RunFreshChild(string scene)
    {
        string dll = typeof(SharedVisionPerfTests).Assembly.Location;
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in new[] { "test", dll, "--filter", $"FullyQualifiedName={typeof(SharedVisionPerfTests).FullName}.{nameof(FreshChild_TimesTheNamedScene)}", "--logger", "console;verbosity=detailed" })
            info.ArgumentList.Add(a);
        info.Environment[ChildVariable] = scene;
        using Process p = Process.Start(info)!;
        System.Threading.Tasks.Task<string> err = p.StandardError.ReadToEndAsync();
        System.Threading.Tasks.Task<string> outTask = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(ChildTimeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            p.WaitForExit(5_000);
            Assert.Fail($"the fresh child dotnet test ({scene}) did not exit within {ChildTimeoutMs / 1000} s and was killed");
        }
        p.WaitForExit();
        string output = outTask.Result;
        int at = output.IndexOf(ChildLine, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the child ({scene}) printed no reading (exit {p.ExitCode}): {output} {err.Result}");
        int from = at + ChildLine.Length, to = output.IndexOf(" ms", from, StringComparison.Ordinal);
        return double.Parse(output[from..to], System.Globalization.CultureInfo.InvariantCulture);
    }
}
