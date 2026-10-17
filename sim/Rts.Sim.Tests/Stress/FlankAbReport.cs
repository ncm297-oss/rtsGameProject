using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M5-P2: the rows QA reads as paired A/B (base and change, alternated, one fresh <c>dotnet test</c> process per sample):
/// tick cost of the 2,500 tight blob, 1,000 walkers crossing a 1,500 blob, the 500 v 500 melee and mixed brawls (ticks
/// 5-205 and 205-605), and who is alive after 5 minutes (6,000 ticks) of each brawl. Report only: it prints, it asserts no
/// budget (the budgets live in CrowdPerfTests / CombatPerfTests). Uses no API newer than the M5-P2 base, so the same file
/// runs on both sides of a pair.
/// </summary>
[Collection(SerialCollection.Name)]
[Trait("Category", "Perf")]
public class FlankAbReport
{
    private readonly ITestOutputHelper _out;

    public FlankAbReport(ITestOutputHelper output) => _out = output;

    private static double Avg(Simulation sim, int ticks)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long freq = Stopwatch.Frequency, total = 0;
        for (int t = 0; t < ticks; t++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            total += Stopwatch.GetTimestamp() - start;
        }
        return total * 1000.0 / freq / ticks;
    }

    [Fact]
    public void TightBlob2500_And_Crossing1000Through1500_Ms()
    {
        Simulation blob = MoveScenario.Spawn(seed: 99, units: 2500, maxCost: 12f, out int goalCell, players: 1);
        MoveScenario.MoveAll(blob, MoveScenario.Center(blob.World.NavGrid, goalCell));
        blob.Tick();
        for (int t = 0; t < 5; t++) blob.Tick();
        double b = Avg(blob, 300);
        Simulation cross = QA.ShoveQaTests.Crossing(73, 1500, 1000, "same", out _);
        for (int t = 0; t < 5; t++) cross.Tick();
        double c = Avg(cross, 300);
        _out.WriteLine($"AB tight blob 2,500: {b:F2} ms; 1,000 crossing 1,500: {c:F2} ms");
    }

    [Fact]
    public void Brawls500v500_Ms_And_AliveAfter5Min()
    {
        foreach (bool mixed in new[] { false, true })
        {
            Simulation sim = Flat(size: 128, units: 1000);
            if (mixed) MixedBrawl(sim, 500, new Vector2(128f, 128f), 8f);
            else FlatBrawl(sim, 500, new Vector2(128f, 128f), 8f, ranks: 10);
            for (int t = 0; t < 5; t++) sim.Tick();
            double first = Avg(sim, 200), second = Avg(sim, 400);
            for (int t = 605; t < 6000; t++) sim.Tick();
            UnitStore u = sim.World.Units;
            int a0 = 0, a1 = 0, attacking = 0, idle = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                if (u.Owner[i] == 0) a0++; else a1++;
                if (u.State[i] == UnitState.Attacking) attacking++;
                if (u.State[i] == UnitState.Idle) idle++;
            }
            _out.WriteLine($"AB {(mixed ? "mixed" : "melee")} brawl 500 v 500: ticks 5-205 {first:F2} ms, 205-605 {second:F2} ms; alive at 5 min {a0} v {a1} (Attacking {attacking}, Idle {idle}), hash {sim.StateHash():X16}");
        }
    }
}
