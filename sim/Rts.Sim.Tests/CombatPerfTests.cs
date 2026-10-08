using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>M4-1 criterion 8: what melee combat costs a tick (Debug, timed one tick at a time).</summary>
[Collection(SerialCollection.Name)]
public class CombatPerfTests
{
    private const double BudgetMs = 4.0;
    private readonly ITestOutputHelper _out;

    public CombatPerfTests(ITestOutputHelper output) => _out = output;

    private static double[] Time(Simulation sim, int ticks)
    {
        var ms = new double[ticks];
        long freq = Stopwatch.Frequency;
        for (int t = 0; t < ticks; t++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            ms[t] = (Stopwatch.GetTimestamp() - start) * 1000.0 / freq;
        }
        return ms;
    }

    private static int CountState(UnitStore u, UnitState s)
    {
        int n = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == s) n++;
        return n;
    }

    /// <summary>
    /// 500 Heavy Infantry against 500 Raiders on a flat 128 x 128 map, two battle lines 10 ranks deep and 50 m wide, 8 m
    /// apart, attack-moved into each other: the first 200 ticks after a 5-tick warm-up (the closing, the clash and the first
    /// deaths) average under 4 ms; ticks 205-605 are reported.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Brawl500v500_First200Ticks_AverageUnder4Ms()
    {
        (double avg, string line) = Brawl(ranks: 10);
        _out.WriteLine(line);
        Assert.True(avg < BudgetMs, $"avg {avg:F3} ms over the {BudgetMs} ms budget");
    }

    /// <summary>
    /// M4-2b criterion 8: 500 v 500 mixed armies (<see cref="CombatScenes.MixedBrawl"/>: line and shock in front, crossbows
    /// / archers, casters, Sappers, Catapults and Zealots behind) on the flat 128 x 128 map, 8 m apart: projectiles,
    /// splash and friendly fire in the tick. The first 200 ticks after a 5-tick warm-up average under 4 ms; ticks 205-605
    /// reported with the projectiles in flight.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void MixedBrawl500v500_First200Ticks_AverageUnder4Ms()
    {
        // Dev-placed armies past the population caps: up to 350 shooters, more than the default store (200) holds.
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 1000, CommandCapacity: 8032) with { ProjectileCapacity = 1000 },
            LocalMovementTests.Flat(128));
        CombatScenes.MixedBrawl(sim, 500, new Vector2(128f, 128f), gap: 8f);
        for (int t = 0; t < 5; t++) sim.Tick();
        World w = sim.World;
        int peak = 0;
        double[] first = Time(sim, 200);
        int kills = w.Kills[0] + w.Kills[1], inFlight = w.Projectiles.Count;
        var later = new double[400];
        long freq = Stopwatch.Frequency;
        for (int t = 0; t < later.Length; t++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            later[t] = (Stopwatch.GetTimestamp() - start) * 1000.0 / freq;
            peak = Math.Max(peak, w.Projectiles.Count);
        }
        double avg = first.Average();
        _out.WriteLine($"500 v 500 mixed brawl, ticks 5-205: avg {avg:F3} ms, worst {first.Max():F3} ms ({kills} dead, {inFlight} projectiles in flight at tick 205); "
            + $"ticks 205-605: avg {later.Average():F3} ms, worst {later.Max():F3} ms ({w.Kills[0] + w.Kills[1]} dead, {w.Units.Count} alive, up to {peak} projectiles in flight)");
        Assert.True(kills > 0, "nobody died: not a brawl");
        Assert.True(avg < BudgetMs, $"avg {avg:F3} ms over the {BudgetMs} ms budget");
    }

    /// <summary>
    /// M4-2b criterion 8: 1,000 bolts in flight (a holding Crossbowman's, at a Raider 230 m off, fired between ticks) cost
    /// under 0.3 ms a tick over 100 ticks of flight, and those ticks allocate nothing; the tick they all land on is reported.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void ThousandProjectilesInFlight_UnderPoint3MsATick_AndAllocateNothing()
    {
        var config = TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 64) with { ProjectileCapacity = 1000 };
        var sim = new Simulation(config, LocalMovementTests.Flat(128));
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle s = CombatScenes.Place(sim, 0, CombatScenes.Crossbowman, new Vector2(10f, 128f));
        EntityHandle r = CombatScenes.Place(sim, 1, CombatScenes.Raider, new Vector2(240f, 128f));
        sim.Enqueue(Command.HoldPosition(0, s));
        sim.Enqueue(Command.HoldPosition(1, r));
        sim.Tick();
        sim.Tick();
        u.CooldownTicks[s.Index] = 1_000_000;
        u.Target[s.Index] = r;
        for (int k = 0; k < 1000; k++) Combat.ProjectileSystem.Fire(w, s.Index);
        u.Target[s.Index] = default;
        Assert.Equal(1000, w.Projectiles.Count);
        sim.Tick(); // warm-up
        double[] ms = Time(sim, 100);
        Assert.Equal(1000, w.Projectiles.Count);
        AllocationProbe.AssertZero(() =>
        {
            for (int t = 0; t < 20; t++) sim.Tick();
        }, _out);
        Assert.Equal(1000, w.Projectiles.Count);
        long freq = Stopwatch.Frequency;
        double landing = 0;
        int ticks = 0;
        while (w.Projectiles.Count > 0 && ticks++ < 300) // they all land on one tick
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            landing = Math.Max(landing, (Stopwatch.GetTimestamp() - start) * 1000.0 / freq);
        }
        _out.WriteLine($"1,000 bolts in flight: avg {ms.Average():F4} ms, worst {ms.Max():F4} ms a tick; the landing tick {landing:F3} ms; raider alive {u.IsAlive(r)}");
        Assert.True(ms.Average() < 0.3, $"avg {ms.Average():F4} ms over the 0.3 ms budget");
    }

    /// <summary>
    /// Reported, not asserted: the same armies as columns 25 ranks deep and 20 m wide, where most units stand pressing
    /// behind their own planted front rank; movement, not combat, is most of that tick.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Brawl500v500_DeepColumns_Report()
    {
        (_, string line) = Brawl(ranks: 25);
        _out.WriteLine(line);
    }

    private static (double First200Avg, string Line) Brawl(int ranks)
    {
        Simulation sim = CombatScenes.Flat(size: 128, units: 1000);
        CombatScenes.FlatBrawl(sim, 500, new Vector2(128f, 128f), gap: 8f, ranks: ranks);
        for (int t = 0; t < 5; t++) sim.Tick();
        double[] first = Time(sim, 200);
        World w = sim.World;
        int attacking = CountState(w.Units, UnitState.Attacking), kills = w.Kills[0] + w.Kills[1];
        double[] later = Time(sim, 400);
        double avg = first.Average();
        string line = $"500 v 500 brawl, {ranks} ranks deep, ticks 5-205: avg {avg:F3} ms, worst {first.Max():F3} ms ({attacking} Attacking, {kills} dead at tick 205); "
            + $"ticks 205-605: avg {later.Average():F3} ms, worst {later.Max():F3} ms ({w.Kills[0] + w.Kills[1]} dead, {w.Units.Count} alive)";
        Assert.True(w.Kills[0] + w.Kills[1] > 0, "nobody died: not a brawl");
        return (avg, line);
    }

    /// <summary>
    /// The chase question (brief): 250 Heavy Infantry attack-moving after 250 Crossbowmen (faster, and not fighting until
    /// M4-2) fleeing across a flat map. Reported: tick cost, flow-field builds, and how many chasers stand waiting for a
    /// field on an average tick; guarded at 4 ms.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Chase250After250_Report()
    {
        Simulation sim = CombatScenes.Flat(size: 128, units: 500);
        UnitStore u = sim.World.Units;
        for (int k = 0; k < 250; k++)
        {
            CombatScenes.Place(sim, 0, CombatScenes.HeavyInfantry, new Vector2(60f + k % 25 * 1.2f, 100f + k / 25 * 1.2f));
            CombatScenes.Place(sim, 1, CombatScenes.Crossbowman, new Vector2(92f + k % 25 * 1.2f, 100f + k / 25 * 1.2f));
        }
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            // Each crossbowman runs to its own point, so the chasers' targets spread over many cells.
            sim.Enqueue(u.Owner[i] == 0 ? Command.AttackMove(0, h, new Vector2(240f, 110f)) : Command.Move(1, h, new Vector2(240f, 20f + (i % 50) * 4f)));
        }
        for (int t = 0; t < 5; t++) sim.Tick();
        int builds0 = sim.World.FlowFields.BuildCount;
        long waiting = 0, chasing = 0;
        var ms = new double[300];
        long freq = Stopwatch.Frequency;
        for (int t = 0; t < ms.Length; t++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            ms[t] = (Stopwatch.GetTimestamp() - start) * 1000.0 / freq;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.Owner[i] != 0 || u.Target[i] == default) continue;
                chasing++;
                if (u.State[i] == UnitState.Moving && u.Velocity[i] == Vector2.Zero) waiting++;
            }
        }
        int builds = sim.World.FlowFields.BuildCount - builds0;
        _out.WriteLine($"250 chasers after 250 fleeing, 300 ticks: avg {ms.Average():F3} ms, worst {ms.Max():F3} ms; {builds} field builds ({builds / 300.0:F2} a tick); "
            + $"chaser-ticks with a target {chasing}, of them standing still while Moving {waiting} ({(chasing == 0 ? 0 : 100.0 * waiting / chasing):F1}%)");
        Assert.True(chasing > 0);
        Assert.True(ms.Average() < BudgetMs);
    }
}
