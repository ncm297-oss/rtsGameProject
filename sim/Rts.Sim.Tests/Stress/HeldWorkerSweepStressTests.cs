using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-S10 criteria 2 and 4: the economy fuzz (<see cref="GatherOverlapFuzzTests.Run"/>, its scene, orders, seeds and window
/// unchanged) on seeds 2-56 under <see cref="HeldWorkerProbe"/>. With the knob off and at the shipped 0.5: <b>no held worker</b>
/// (a live worker on its loop, its node alive, 1,200 ticks without a delivery and within 2 m of where it stood 600 ticks
/// earlier). At 0.25 and 0.75 the held seeds are counted and printed against off (a finding, not a gate). Seeds the harness
/// says cannot be built (no node within its walk, a Keep refused: seed 13 among them) are printed and skipped. Every row
/// prints each seed's longest gap and each worker that went past 60 s still walking (with its route): not a failure (rule 66).
/// </summary>
[Trait("Category", "Stress")]
public class HeldWorkerSweepStressTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.5, true)]
    [InlineData(0.25, false)]
    [InlineData(0.75, false)]
    public void Seeds2To56_NoHeldWorker(double overlap, bool gate)
    {
        var heldSeeds = new List<ulong>();
        var lines = new List<string>();
        int built = 0;
        for (ulong seed = 2; seed <= 56; seed++)
        {
            HeldWorkerProbe probe;
            try
            {
                (probe, _) = HeldWorkerProbe.Run(overlap, seed);
            }
            catch (Exception e) when (e.Message.Contains("no node within") || e.Message.Contains("no Keep"))
            {
                output.WriteLine($"seed {seed}: can't be built ({e.Message.Split('\n')[0]})");
                continue;
            }
            built++;
            output.WriteLine($"seed {seed} at {overlap}: longest gap {probe.Longest / 20.0:F1} s; held {probe.Held.Count}; walking past 60 s {probe.Walkers.Count}");
            foreach (string h in probe.Held) lines.Add($"seed {seed} HELD {h}");
            foreach (string k in probe.Walkers) output.WriteLine($"  walking: {k}");
            if (probe.Held.Count > 0) heldSeeds.Add(seed);
        }
        foreach (string l in lines) output.WriteLine(l);
        output.WriteLine($"{built} seeds built at {overlap}; held seeds: {(heldSeeds.Count == 0 ? "none" : string.Join(" ", heldSeeds))}");
        if (gate) Assert.True(heldSeeds.Count == 0, $"held workers at {overlap} on seeds {string.Join(" ", heldSeeds)}:\n{string.Join("\n", lines)}");
    }
}
