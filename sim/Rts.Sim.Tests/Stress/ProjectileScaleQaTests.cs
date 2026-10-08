using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M4-2b (session 2026-10-08-0913): scale probes for projectiles and splash, report rows with a stall guard only
/// (the budgets are the developer's rows in <c>CombatPerfTests</c>). 2x and 5x the 500-unit design number in a mixed
/// brawl, and the worst landing tick: every Catapult stone of a 400-Catapult battery landing in one tick on a dense blob.
/// </summary>
[Collection(SerialCollection.Name)]
public class ProjectileScaleQaTests
{
    private readonly ITestOutputHelper _out;

    public ProjectileScaleQaTests(ITestOutputHelper output) => _out = output;

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(500)]
    [InlineData(1250)]
    public void MixedBrawl_At2xAnd5x_ScalesRoughlyLinearly(int perSide)
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 2 * perSide, CommandCapacity: 16 * perSide + 64) with { ProjectileCapacity = 2 * perSide },
            LocalMovementTests.Flat(192));
        MixedBrawl(sim, perSide, new Vector2(192f, 192f), gap: 8f);
        for (int t = 0; t < 5; t++) sim.Tick();
        var times = new double[300];
        int peak = 0;
        for (int t = 0; t < times.Length; t++)
        {
            long s = Stopwatch.GetTimestamp();
            sim.Tick();
            times[t] = Ms(Stopwatch.GetTimestamp() - s);
            peak = Math.Max(peak, sim.World.Projectiles.Count);
        }
        World w = sim.World;
        double avg = times.Average();
        _out.WriteLine($"{perSide} v {perSide} mixed: avg {avg:F3} ms, worst {times.Max():F3} ms, {avg / (2 * perSide) * 1000:F2} us a unit, peak {peak} in flight, {w.Kills[0] + w.Kills[1]} dead");
        Assert.True(w.Kills[0] + w.Kills[1] > 0);
        Assert.True(avg < 50, $"stall: avg {avg:F1} ms");
    }

    /// <summary>400 Catapult stones landing on the same tick into a 1,000-unit blob (every stone a full splash query): the landing tick's cost.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void FourHundredStones_LandingTheSameTick_InADenseBlob_TickCost()
    {
        int catapult = TestSim.Data.FindUnit("malazan_catapult");
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 1400, CommandCapacity: 4096) with { ProjectileCapacity = 400 },
            LocalMovementTests.Flat(128));
        World w = sim.World;
        UnitStore u = w.Units;
        var blob = new List<EntityHandle>();
        for (int k = 0; k < 1000; k++) blob.Add(Place(sim, 1, HeavyInfantry, new Vector2(110f + k % 40 * 0.9f, 110f + k / 40 * 0.9f)));
        var cats = new List<EntityHandle>();
        for (int k = 0; k < 400; k++) cats.Add(Place(sim, 0, catapult, new Vector2(20f + k % 20 * 2f, 20f + k / 20 * 2f)));
        foreach (EntityHandle h in blob) sim.Enqueue(Command.HoldPosition(1, h));
        foreach (EntityHandle h in cats) sim.Enqueue(Command.HoldPosition(0, h));
        sim.Tick();
        sim.Tick();
        // Every stone aimed at a blob unit, all from one launch point so they land together.
        Vector2 from = new(60f, 60f);
        for (int k = 0; k < 400; k++)
        {
            EntityHandle c = cats[k];
            u.Position[c.Index] = from;
            u.Target[c.Index] = blob[(k * 7) % blob.Count];
            ProjectileSystem.Fire(w, c.Index);
            u.Target[c.Index] = default;
            u.CooldownTicks[c.Index] = 1_000_000;
        }
        Assert.Equal(400, w.Projectiles.Count);
        double worst = 0, landing = 0;
        for (int t = 0; t < 200 && w.Projectiles.Count > 0; t++)
        {
            long s = Stopwatch.GetTimestamp();
            sim.Tick();
            double ms = Ms(Stopwatch.GetTimestamp() - s);
            if (w.Impacts.Length > 0) landing = Math.Max(landing, ms);
            worst = Math.Max(worst, ms);
        }
        _out.WriteLine($"400 stones into a 1,000-unit blob: landing tick {landing:F3} ms, worst {worst:F3} ms, {w.Deaths.Length} died on it, {w.Kills[0]} kills");
        Assert.Equal(0, w.Projectiles.Count);
        Assert.True(landing < 100, $"stall: {landing:F1} ms");
    }
}
