using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M5-3 re-check round 1, session 2026-10-12-1930): BUG-0593's map grid at its worst. <c>ai.scoutGrid</c> at its floor
/// (16 m) on the largest map the generator takes (1,024 x 1,024 cells, 2,048 m): 128 x 128 grid points after the 9 of the
/// head, scanned every scout think (visited / explored marks, nearest unvisited). One AI seat (Normal) scouts from the map's
/// middle on flat ground, the enemy hall far in a corner, for 4,000 ticks; its think is timed by hand. Gated: a think's
/// average under the 1 ms budget (docs/03, Debug) and a scout think allocates nothing.
/// </summary>
[Collection(SerialCollection.Name)]
public class AiScoutGridCostQaTests
{
    private const int Size = 1024;
    private readonly ITestOutputHelper _out;

    public AiScoutGridCostQaTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("common/rules.json", r =>
        {
            r["ai"]!["scoutGrid"] = 16;
            r["ai"]!["scoutAfter"] = 0;
        });
        dir.EditJson("factions/malazan/ai.json", root => root["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1)));
    }));

    private static (Simulation Sim, AiPlayer Ai) Scene()
    {
        GameData d = s_data.Value;
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 256) with { Data = d, Combat = false },
            LocalMovementTests.Flat(Size));
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, StartBase.BuildingOfSlot(d, 0, BuildingSlot.TownHall), 510 * Size + 510, out _));
        Assert.True(w.Buildings.Spawn(1, StartBase.BuildingOfSlot(d, 1, BuildingSlot.TownHall), 1000 * Size + 1000, out _));
        int worker = StartBase.UnitOfSlot(d, 0, UnitSlot.Worker);
        for (int k = 0; k < 5; k++) CombatScenes.Place(sim, 0, worker, w.NavGrid.CellCenter(508 + k, 516));
        sim.Tick();
        var ai = new AiPlayer(new PlayerView(w, 0), d, d.Ai[0], AiDifficulty.Normal, new SimRng(5, (ulong)RngStream.Ai(0)), sim);
        return (sim, ai);
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void ScoutGridAtItsFloor_OnTheLargestMap_AThinkUnder1Ms()
    {
        (Simulation sim, AiPlayer ai) = Scene();
        Assert.Equal(Blackboard.ScoutHead + 128 * 128, ai.Board.ScoutPoints.Length);
        var times = new List<double>();
        var sw = new Stopwatch();
        int think = ai.ThinkTicks;
        for (int t = 0; t < 4000; t++)
        {
            if (sim.TickNumber % think == 0)
            {
                sw.Restart();
                ai.Think();
                sw.Stop();
                if (t >= 10 * think && ai.Board.ScoutPhase == 1) times.Add(sw.Elapsed.TotalMilliseconds);
            }
            sim.Tick();
        }
        int visited = ai.Board.ScoutVisited.Count(v => v);
        double avg = times.Count > 0 ? times.Average() : 0, worst = times.Count > 0 ? times.Max() : 0;
        _out.WriteLine($"{ai.Board.ScoutPoints.Length} points, {visited} visited; scout phase {ai.Board.ScoutPhase}; {times.Count} scout thinks: avg {avg:F3} ms, worst {worst:F3} ms");
        Assert.True(times.Count > 50, "the scout wasn't sweeping: the row proves little");
        Assert.True(avg < 1.0, $"avg {avg:F3} ms over the 1 ms budget");
    }

    [Fact]
    public void AScoutThinkOnTheLargestGrid_AllocatesNothing()
    {
        (Simulation sim, AiPlayer ai) = Scene();
        int think = ai.ThinkTicks;
        for (int t = 0; t < 40 * think; t++)
        {
            if (sim.TickNumber % think == 0) ai.Think();
            sim.Tick();
        }
        while (sim.TickNumber % think != 0) sim.Tick();
        Assert.Equal(1, ai.Board.ScoutPhase);
        AllocationProbe.AssertZero(() => ai.Think(), _out);
    }
}
