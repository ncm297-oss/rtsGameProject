using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M4-5 (session 2026-10-10-1954): the phase-5 detection pass at 1x, 2x and 5x the 500-unit design, worst case for its
/// shape: half the units (one player) stealthed, the other half 10 m detectors, both packed at 1 m in one square blob, so
/// every stealthed unit's query returns ~300 enemies. The pass alone (<see cref="StealthSystem.Run"/>, 200 calls) must
/// scale linearly in the stealthed units (cost per stealthed unit at 5x at most 2.5x the 1x one: the density is the same),
/// allocate nothing, and leave the state hash unchanged (it writes only derived bits).
/// </summary>
[Collection(SerialCollection.Name)]
public class StealthScaleQaTests
{
    private readonly ITestOutputHelper _out;

    public StealthScaleQaTests(ITestOutputHelper output) => _out = output;

    private static Simulation Blob(int units)
    {
        GameData d = StealthTests.Data;
        int side = (int)Math.Ceiling(Math.Sqrt(units));
        var sim = new Simulation(new SimConfig(1, 2, units + 8, 64) { Data = d, Combat = false }, LocalMovementTests.Flat(160));
        int worker = d.FindUnit("whirlwind_camp_follower"), laborer = d.FindUnit("malazan_laborer");
        var origin = new Vector2(80f, 80f);
        for (int k = 0; k < units; k++)
        {
            int x = k % side, y = k / side;
            bool hider = (x + y) % 2 == 0;
            EntityHandle h = CombatScenes.Place(sim, hider ? 0 : 1, hider ? worker : laborer, origin + new Vector2(x, y));
            sim.World.Units.Hold[h.Index] = true;
            if (hider) StealthTests.Stealth(sim, h);
        }
        sim.Tick();
        sim.Tick();
        return sim;
    }

    private double PerStealthedUs(int units)
    {
        Simulation sim = Blob(units);
        World w = sim.World;
        int stealthed = w.Units.Statuses.StealthedUnits;
        for (int k = 0; k < 20; k++) StealthSystem.Run(w);
        ulong hash = sim.StateHash();
        const int passes = 200;
        Action run = () => { for (int k = 0; k < passes; k++) StealthSystem.Run(w); };
        AllocationProbe.AssertZero(run, _out);
        var sw = Stopwatch.StartNew();
        run();
        sw.Stop();
        Assert.Equal(hash, sim.StateHash());
        double passMs = sw.Elapsed.TotalMilliseconds / passes;
        int detected = 0;
        for (int j = 0; j < w.Units.Capacity; j++) if (w.Units.Alive[j] && w.IsDetectedBy(1, j)) detected++;
        _out.WriteLine($"{units} units, {stealthed} stealthed, {detected} detected by player 1: pass {passMs:F3} ms, {passMs * 1000 / stealthed:F2} us per stealthed unit");
        return passMs * 1000 / stealthed;
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void DetectionPass_1x2x5xDesign_ScalesLinearly_AllocatesNothing_HashUnchanged()
    {
        double one = PerStealthedUs(500);
        double two = PerStealthedUs(1000);
        double five = PerStealthedUs(2500);
        _out.WriteLine($"per stealthed unit: 1x {one:F2} us, 2x {two:F2} us, 5x {five:F2} us");
        Assert.True(five <= 2.5 * one, $"super-linear: {five:F2} us per stealthed unit at 5x vs {one:F2} at 1x");
    }
}
