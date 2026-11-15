using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-S10 stage 0 (BUG-1431): the diagnosis rows of docs/03 "Implementation (M6-S10)". Each row runs the economy fuzz
/// (<see cref="GatherOverlapFuzzTests.Run"/>, its scene, orders and seed unchanged) under <see cref="HeldWorkerProbe"/> and prints
/// every held worker (state, where it stands against the footprint and its stand point, who holds the stand cell, and how each
/// of its walks in the last 600 ticks ended). A report: no bound (the sweeps of criteria 2 and 4 are the gates).
/// Reproduce one row: <c>dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~KeepCornerWedgeDiagnosisTests" --logger "console;verbosity=detailed"</c>.
/// </summary>
[Trait("Category", "Stress")]
public class KeepCornerWedgeDiagnosisTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0.0, 4UL)]
    [InlineData(0.0, 22UL)]
    [InlineData(0.0, 27UL)]
    [InlineData(0.0, 35UL)]
    [InlineData(0.0, 44UL)]
    [InlineData(0.0, 56UL)]
    [InlineData(0.25, 3UL)]
    [InlineData(0.75, 7UL)]
    [InlineData(0.5, 23UL)]
    [InlineData(0.5, 27UL)]
    public void HeldWorker_Traced(double overlap, ulong seed)
    {
        (HeldWorkerProbe probe, GatherOverlapFuzzTests.Result r) = HeldWorkerProbe.Run(overlap, seed);
        output.WriteLine($"seed {seed} at {overlap}: longest without a trip {probe.Longest / 20.0:F1} s; held {probe.Held.Count}, walking past 60 s {probe.Walkers.Count}");
        foreach (string h in probe.Held) output.WriteLine("HELD " + h);
        foreach (string k in probe.Walkers) output.WriteLine("WALKING " + k);
        output.WriteLine(r.Line);
    }
}
