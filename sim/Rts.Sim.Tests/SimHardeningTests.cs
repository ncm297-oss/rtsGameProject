using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-H1 sim hardening: refused Builds stay cheap (BUG-0091), the M3-3 nits (BUG-0092), the M3-1 nits (BUG-0076) and the measured closing-change bound (BUG-0080).</summary>
[Collection(SerialCollection.Name)]
public class SimHardeningTests
{
    private readonly ITestOutputHelper _out;

    public SimHardeningTests(ITestOutputHelper output) => _out = output;

    // ------------------------------------------------------------------ BUG-0091: cheap checks first

    /// <summary>A 128 x 128 map with a one-tree wall down x = 64 (y 3-121): a House in the top gap has sides that meet only round the bottom, so its seal check floods most of the map.</summary>
    private static Simulation LongWall(int commands)
    {
        var sim = TestSim.Explored(new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 128, CommandCapacity: commands) with { ResourceCapacity = 256 }, Flat(128, 128)));
        for (int y = 3; y < 122; y++) Spawn(sim.World, Tree, 64, y, TreeWood);
        return sim;
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void HundredUnaffordableBuildsAtALongDetourAnchor_OneTick_Under2Ms()
    {
        Simulation sim = LongWall(256);
        SetTotals(sim, 0, 0, 0);
        EntityHandle[] workers = Enumerable.Range(0, 100).Select(k => Unit(sim, At(sim, 4 + k % 50, 20 + k / 50))).ToArray();
        int anchor = Cell(sim, 64, 1);
        Assert.False(sim.World.CanPlace(0, House, anchor, out PlacementError why));
        Assert.Equal(PlacementError.CannotAfford, why); // the reason order is unchanged: the flood passed, the cost didn't
        sim.Enqueue(Command.Build(0, workers[0], House, At(sim, 64, 1)));
        Run(sim, 2); // JIT warm-up of the apply path
        foreach (EntityHandle w in workers) sim.Enqueue(Command.Build(0, w, House, At(sim, 64, 1)));
        sim.Tick();
        long t0 = Stopwatch.GetTimestamp();
        sim.Tick();
        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        _out.WriteLine($"100 unaffordable Builds at the long-detour anchor: tick {ms:F3} ms (22 ms before M3-H1)");
        Assert.Equal(0, sim.World.Buildings.Count);
        Assert.True(ms < 2.0, $"{ms:F3} ms");
    }

    // ------------------------------------------------------------------ BUG-0092 (a)-(e)

    [Theory]
    [InlineData("rateFactor")]
    [InlineData("costFactor")]
    public void ARepairFactorBelow2ToTheMinus16_IsADataError_AndExactly2ToTheMinus16Loads(string field)
    {
        DataLoadResult Load(double value)
        {
            using TestDataDir dir = TestDataDir.CopyOfShipped();
            dir.EditJson("common/rules.json", r => r["repair"]![field] = JsonValue.Create(value));
            return DataLoader.LoadAll(dir.Path);
        }
        DataLoadResult tiny = Load(Math.Pow(2, -17));
        Assert.Contains(tiny.Errors, e => e.Path == "repair." + field && e.Message.Contains("2^-16"));
        Assert.Empty(Load(Math.Pow(2, -16)).Errors);
    }

    [Fact]
    public void AHoldingWorkersBuildOnItsOwnCell_IsAccepted_AndOtherHoldersStillBlock()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        Give(sim, 0, 1000, 1000);
        EntityHandle w = Unit(sim, At(sim, 10, 10));
        EntityHandle other = Unit(sim, At(sim, 20, 10));
        sim.Enqueue(Command.HoldPosition(0, w));
        sim.Enqueue(Command.HoldPosition(0, other));
        Run(sim, 2);
        Assert.Equal(PlacementError.UnitInTheWay, sim.World.CanPlace(0, House, Cell(sim, 10, 10), out PlacementError r) ? PlacementError.None : r);
        sim.Enqueue(Command.Build(0, w, House, At(sim, 10, 10)));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 20, 10))); // onto the other holder: refused, so it changes nothing
        Run(sim, 2);
        UnitStore u = sim.World.Units;
        Assert.True(SiteAt(sim, 10, 10) >= 0, "the holding worker's Build on its own cell was refused");
        Assert.True(SiteAt(sim, 20, 10) < 0, "another holder still blocks the worker's Build");
        Assert.False(u.Hold[w.Index]);
        Assert.True(u.Hold[other.Index]);
        Assert.False(InFootprint(u.Position[w.Index], 10, 10, 2, 2), "the worker stays inside its site");
    }

    private static bool InFootprint(Vector2 p, int x0, int y0, int fw, int fh)
    {
        const float cs = MapConstants.CellSize;
        return p.X >= x0 * cs && p.X < (x0 + fw) * cs && p.Y >= y0 * cs && p.Y < (y0 + fh) * cs;
    }

    /// <summary>90 own units stacked in a 2 x 2 site whose rings 1-8 are full of another player's units: every one is set down past ring 8, each on its own free cell.</summary>
    [Fact]
    public void NinetyUnitsInA2x2Site_RingsOneToEightFull_AllLandOnDistinctFreeCells()
    {
        const int x0 = 23, y0 = 23;
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 2, UnitCapacity: 420, CommandCapacity: 512), Flat(48, 48));
        NavGrid g = sim.World.NavGrid;
        int ringUnits = 0;
        for (int y = y0 - 8; y < y0 + 10; y++)
            for (int x = x0 - 8; x < x0 + 10; x++)
                if (!(x >= x0 && x < x0 + 2 && y >= y0 && y < y0 + 2))
                {
                    sim.Enqueue(Command.SpawnUnit(1, Laborer, g.CellCenter(x, y)));
                    ringUnits++;
                }
        for (int k = 0; k < 90; k++)
            sim.Enqueue(Command.SpawnUnit(0, Laborer, new Vector2((x0 + 0.1f + k % 9 * 0.2f) * 2f, (y0 + 0.1f + k / 9 * 0.18f) * 2f)));
        Run(sim, 2);
        Assert.Equal(320, ringUnits);
        UnitStore u = sim.World.Units;
        Give(sim, 0, 1000, 1000);
        sim.Enqueue(Command.Build(0, MoveScenario.Handle(sim, 0), House, g.CellCenter(x0, y0))); // commands apply player by player: slots 0-89 are player 0's
        Run(sim, 2);
        Assert.True(SiteAt(sim, x0, y0) >= 0);
        var cells = new HashSet<int>();
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Assert.True(g.WorldToCell(u.Position[i], out int cx, out int cy));
            Assert.True(g.IsPassable(cx, cy), $"unit {i} on blocked ground");
            Assert.True(cells.Add(cy * g.Width + cx), $"unit {i} shares cell ({cx}, {cy})");
            if (u.Owner[i] == 0) Assert.True(Math.Max(Math.Max(x0 - cx, cx - x0 - 1), Math.Max(y0 - cy, cy - y0 - 1)) > 8, $"unit {i} at ({cx}, {cy}) within ring 8");
        }
        Assert.Equal(410, cells.Count);
    }

    /// <summary>A Keep placed on 16 own units inside a blob of 400 more round it: the Build's apply (check, place, push-out) under 1 ms.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void PushOut_SixteenUnitsUnderAKeep_In400_Under1Ms()
    {
        (Simulation warm, int warmWorker) = Blob();
        Assert.True(ConstructionSystem.StartBuild(warm.World, warmWorker, Keep, At(warm, 60, 60), replaceQueue: true)); // JIT
        (Simulation sim, int worker) = Blob();
        long t0 = Stopwatch.GetTimestamp();
        bool placed = ConstructionSystem.StartBuild(sim.World, worker, Keep, At(sim, 60, 60), replaceQueue: true);
        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        _out.WriteLine($"Keep on 16 own units in a blob of 400: Build apply {ms:F3} ms (7.3 ms tick before M3-H1)");
        Assert.True(placed);
        UnitStore u = sim.World.Units;
        for (int i = 0; i < 16; i++) Assert.False(InFootprint(u.Position[i], 60, 60, 4, 4));
        Assert.True(ms < 1.0, $"{ms:F3} ms");
    }

    private static (Simulation Sim, int Worker) Blob()
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 2560, CommandCapacity: 4096), Flat(128, 128));
        NavGrid g = sim.World.NavGrid;
        const int x0 = 60, y0 = 60;
        for (int k = 0; k < 16; k++) sim.Enqueue(Command.SpawnUnit(0, Laborer, g.CellCenter(x0 + k % 4, y0 + k / 4)));
        int placed = 0;
        for (int r = 1; placed < 400; r++)
            for (int y = y0 - r; y < y0 + 4 + r && placed < 400; y++)
                for (int x = x0 - r; x < x0 + 4 + r && placed < 400; x++)
                {
                    if (!(y == y0 - r || y == y0 + 3 + r || x == x0 - r || x == x0 + 3 + r)) continue;
                    sim.Enqueue(Command.SpawnUnit(0, Laborer, g.CellCenter(x, y)));
                    placed++;
                }
        Run(sim, 2);
        SetTotals(sim, 0, 1000, 1000);
        return (sim, 0);
    }

    [Fact]
    public void APushedUnitsPreviousPosition_IsItsNewPosition()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        Give(sim, 0, 1000, 1000);
        EntityHandle idle = Unit(sim, At(sim, 11, 9));
        EntityHandle builder = Unit(sim, At(sim, 4, 4));
        sim.Enqueue(Command.Build(0, builder, Keep, At(sim, 10, 8)));
        Run(sim, 2);
        Assert.True(SiteAt(sim, 10, 8) >= 0);
        UnitStore u = sim.World.Units;
        Assert.False(InFootprint(u.Position[idle.Index], 10, 8, 4, 4));
        Assert.Equal(u.Position[idle.Index], u.PrevPosition[idle.Index]); // the view doesn't draw it sliding through the Keep
    }

    // ------------------------------------------------------------------ BUG-0076

    [Fact]
    public void NegativeZeroMineSpacing_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (MapGenParams.Default with { MineSpacing = -0f }).Validate());
        (MapGenParams.Default with { MineSpacing = 0f }).Validate();
    }

    // ------------------------------------------------------------------ BUG-0080: the measured bound

    /// <summary>
    /// Real closings (a House dropped every <paramref name="period"/> ticks off every path) with 8 goal groups ordered a
    /// tick apart: each closing makes every cached field unusable, and the build cap (2 a tick) rebuilds them oldest
    /// order first. So between closings only the 2 x period oldest groups get a field; the others wait for as long as
    /// the closings last; once period reaches ceil(groups / 2) every group gets one, the youngest after ceil(groups / 2) - 1
    /// ticks. docs/03 "Known limits" states this bound (the usable-stale alternative was not taken, see BUG-0080).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void ClosingsEveryPeriodTicks_OnlyThe2xPeriodOldestGroupsWalk(int period)
    {
        const int groups = 8, window = 48;
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 64, CommandCapacity: 256), Flat(128, 96));
        NavGrid g = sim.World.NavGrid;
        for (int k = 0; k < groups; k++)
            for (int m = 0; m < 4; m++)
                sim.Enqueue(Command.SpawnUnit(0, Infantry, g.CellCenter(3 + m, 6 + 6 * k)));
        Run(sim, 2);
        var goal = new int[groups];
        for (int k = 0; k < groups; k++)
        {
            for (int m = 0; m < 4; m++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 4 * k + m), g.CellCenter(120, 6 + 6 * k)));
            goal[k] = (6 + 6 * k) * g.Width + 120;
            sim.Tick(); // one order tick per group: group k is the k-th oldest
        }
        Run(sim, groups); // every group has its field
        var wait = new int[groups];
        var longest = new int[groups];
        int houses = 0;
        for (int t = 0; t < window; t++)
        {
            if (t % period == 0)
            {
                sim.Enqueue(Command.SpawnBuilding(0, House, g.CellCenter(4 + 3 * (houses % 38), houses < 38 ? 70 : 76)));
                houses++;
            }
            sim.Tick();
            for (int k = 0; k < groups; k++)
            {
                wait[k] = sim.World.FlowFields.PeekCached(goal[k]) == null ? wait[k] + 1 : 0;
                longest[k] = Math.Max(longest[k], wait[k]);
            }
        }
        _out.WriteLine($"closing every {period} tick(s), {houses} Houses, {groups} groups: longest waits by age {string.Join(", ", longest)}");
        sim.Tick(); // the last House lands
        Assert.Equal(houses, sim.World.Buildings.Count);
        int walking = Math.Min(groups, 2 * period);
        for (int k = 0; k < groups; k++)
        {
            if (k < walking) Assert.True(longest[k] <= k / 2, $"group {k}: longest wait {longest[k]}");
            else Assert.True(longest[k] >= window - period, $"group {k}: longest wait {longest[k]}, expected starved");
        }
    }
}
