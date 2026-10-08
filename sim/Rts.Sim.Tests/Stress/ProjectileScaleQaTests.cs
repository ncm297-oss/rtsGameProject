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

    /// <summary>
    /// The BUG-0183 re-lead pass at its worst: 1,000 bolts in flight at 1,000 different walking Heavy Infantry, every one
    /// re-led and steered every tick (data copy: bolt speed 1 m/s, the loader's floor, so the shots fly 300 ticks). Combat
    /// is off so the sim never calls <see cref="ProjectileSystem.Fly"/>; the row times it alone, and once more with
    /// <c>leadSpeed</c> 0 (no re-lead) as the control. Budget: the developer's 1,000-in-flight row, 0.3 ms a tick, and 0 B.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void ThousandLedShots_AtThousandWalkers_ReLeadEveryTick_UnderPoint3Ms_AndAllocateNothing()
    {
        (double avg, double worst, long bytes, int steered) Run(double leadSpeed)
        {
            using TestDataDir dir = TestDataDir.CopyOfShipped();
            dir.EditJson("common/projectiles.json", root =>
            {
                foreach (System.Text.Json.Nodes.JsonNode? p in root["projectiles"]!.AsArray())
                    if ((string?)p!["id"] == "bolt") { p["speed"] = 1; p["leadSpeed"] = leadSpeed; }
            });
            Data.DataLoadResult r = Data.DataLoader.LoadAll(dir.Path);
            Assert.True(r.Ok, string.Join("\n", r.Errors));
            var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 1100, CommandCapacity: 4096)
                with { Data = r.Data!, Combat = false, ProjectileCapacity = 1000 }, LocalMovementTests.Flat(160));
            World w = sim.World;
            UnitStore u = w.Units;
            int hi = r.Data!.FindUnit("malazan_heavy_infantry"), xbow = r.Data.FindUnit("malazan_crossbowman");
            var walkers = new List<EntityHandle>();
            for (int k = 0; k < 1000; k++)
            {
                var at = new Vector2(40f + k % 25 * 2.5f, 20f + k / 25 * 7f);
                EntityHandle h = Place(sim, 1, hi, at);
                walkers.Add(h);
                // 8 shared goals far east (1,000 distinct goals would thrash the flow-field cache, BUG-0025, and most would wait).
                sim.Enqueue(Command.Move(1, h, new Vector2(300f, 20f + k / 25 % 8 * 25f)));
            }
            EntityHandle s = Place(sim, 0, xbow, new Vector2(10f, 10f));
            for (int t = 0; t < 10; t++) sim.Tick();
            foreach (EntityHandle h in walkers)
            {
                u.Position[s.Index] = u.Position[h.Index] - new Vector2(15f, 0f);
                u.Target[s.Index] = h;
                ProjectileSystem.Fire(w, s.Index);
            }
            u.Target[s.Index] = default;
            Assert.Equal(1000, w.Projectiles.Count);
            var before = new Vector2[w.Projectiles.Capacity];
            var times = new double[100];
            long bytes = 0;
            int steered = 0;
            ProjectileSystem.Fly(w); // warm
            for (int t = 0; t < times.Length; t++)
            {
                sim.Tick(); // walkers step; no Fly (combat off)
                w.Projectiles.Target.CopyTo(before);
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                long s0 = Stopwatch.GetTimestamp();
                ProjectileSystem.Fly(w);
                times[t] = Ms(Stopwatch.GetTimestamp() - s0);
                bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
                for (int k = 0; k < before.Length; k++)
                    if (w.Projectiles.Alive[k] && w.Projectiles.Target[k] != before[k]) steered++;
            }
            Assert.Equal(1000, w.Projectiles.Count);
            return (times.Average(), times.Max(), bytes, steered);
        }
        var led = Run(5);
        var control = Run(0);
        _out.WriteLine($"1,000 led shots: Fly {led.avg:F4} ms avg, worst {led.worst:F4}, {led.bytes} B, {led.steered} steers over 100 ticks; " +
            $"control (no lead): {control.avg:F4} ms avg, worst {control.worst:F4}, {control.steered} steers");
        Assert.True(led.steered > 50_000, $"setup: only {led.steered} steers (of 100,000 shot-ticks)");
        Assert.Equal(0, control.steered);
        Assert.Equal(0L, led.bytes);
        Assert.True(led.avg < 0.3, $"re-lead pass {led.avg:F3} ms a tick");
    }
}
