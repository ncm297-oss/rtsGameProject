using Rts.Sim.Ai;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M5-9a round 2, QA focus (g): a save taken mid-stamp in a real match (Raraku seed 1 Normal v Normal, shipped data:
/// the first tick, between thinks, at which a seat's starved stamp is half a window old), loaded in a <b>second process</b>
/// (a child <c>dotnet test</c> of this row reading the bytes from a file), follows to the same first starved wave (seat and
/// tick), the same end tick and the same end state hash as the uninterrupted run in this process.
/// M6-SH2 re-staging (BUG-1462's row; old: Pale Hills seed 1, <c>PaleHillsSeed1_*</c>): since M6-S10 Pale Hills seed 1 ends at
/// 42,384 before any seat is stamped starved, so the row proved nothing; Raraku seed 1 (the same map sizing, <c>MapMatch</c>)
/// has its winner starved of wood from about 22,500 and waving on the stamp, which is the row's subject.
/// M6-S14 re-staging (the AI-play session; the checks unchanged, the name kept): on Raraku seed 1 seat 0 is stamped half a
/// window at 23,180 but its stamp lapses (the worker floor puts gatherers back on wood) and no seat waves starved, so the row
/// proved nothing ("seat 0 at -1"). It plays Pale Hills seed 5 (<c>MapMatch</c>): seat 1 stamped half a window at 26,360,
/// its first starved wave at 38,460, over at 47,547; the only seed of the three maps' 1-8 with a starved wave inside a match
/// that ends (Pale Hills 7 / 8 wave starved at 69,240 but do not end by 72,000).
/// </summary>
[Trait("Category", "Stress")]
public class StarvedSaveTwoProcessQaStressTests
{
    private const string Variable = "RTS_QA_STARVED_SAVE_CHILD", Marker = "STARVED-SAVE ";

    private readonly ITestOutputHelper _out;

    public StarvedSaveTwoProcessQaStressTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Watches every seat's thinks for its first wave or hunt started while its stamp was due; returns the tick per seat (-1
    /// none). Per seat since the developer's fix round 1 (BUG-1154): Pale Hills seed 1's seat 0 is now starved too and waves
    /// first (30,420), so "the first starved wave of either seat" no longer shows the saved seat's.
    /// </summary>
    private static int[] WatchFirstStarvedWave(Simulation sim)
    {
        var first = new[] { -1, -1 };
        for (int p = 0; p < 2; p++)
        {
            int seat = p, waves = sim.AiOf(p)!.Board.WavesStarted + sim.AiOf(p)!.Board.HuntsStarted;
            sim.AiOf(p)!.ThoughtForTests = a =>
            {
                int now = a.Board.WavesStarted + a.Board.HuntsStarted;
                if (first[seat] < 0 && now > waves && a.StarvedWaveDue) first[seat] = a.Now;
                waves = now;
            };
        }
        return first;
    }

    private static string Follow(Simulation sim)
    {
        int[] first = WatchFirstStarvedWave(sim);
        while (sim.TickNumber < 72_000 && !sim.MatchOver) sim.Tick();
        return $"first starved wave seat 0 at {first[0]}, seat 1 at {first[1]}; over {sim.MatchOver} at {sim.TickNumber}; end hash {sim.StateHash():X16}";
    }

    [Fact]
    public void RarakuSeed1_SavedMidStamp_LoadedInAChildProcess_WavesAndEndsTheSame()
    {
        string? path = Environment.GetEnvironmentVariable(Variable);
        if (path != null)
        {
            Simulation child = SaveTestRun.Load(TestSim.Data, File.ReadAllBytes(path));
            _out.WriteLine(Marker + Follow(child));
            return;
        }
        Simulation sim = MapFileTests.MapMatch(MapFileTests.PaleHills, 5, false, out _); // M6-S14: was ("raraku", 1)
        int window = sim.AiOf(0)!.Rules.StarvedWaveAfterTicks, savedAt = -1, seat = -1, stamp = -1;
        while (sim.TickNumber < 72_000 && !sim.MatchOver)
        {
            sim.Tick();
            for (int p = 0; p < 2 && savedAt < 0; p++)
            {
                AiPlayer ai = sim.AiOf(p)!;
                int s = ai.Board.StarvedSince;
                if (s >= 0 && sim.TickNumber - s >= window / 2 && sim.TickNumber % ai.ThinkTicks == 7) { savedAt = sim.TickNumber; seat = p; stamp = s; }
            }
            if (savedAt >= 0) break;
        }
        Assert.True(savedAt >= 0, "no seat was ever stamped half a window");
        byte[] bytes = SaveTestRun.Save(sim);
        string file = Path.Combine(Path.GetTempPath(), $"rts-qa-starved-{Environment.ProcessId}.sav");
        File.WriteAllBytes(file, bytes);
        try
        {
            Assert.Equal(stamp, SaveTestRun.Load(TestSim.Data, bytes).AiOf(seat)!.Board.StarvedSince);
            string mine = Follow(sim);
            _out.WriteLine($"saved at {savedAt} (seat {seat} stamped at {stamp}, {bytes.Length} bytes); this process: {mine}");
            string child = MapFileTests.RunChild(typeof(StarvedSaveTwoProcessQaStressTests), nameof(RarakuSeed1_SavedMidStamp_LoadedInAChildProcess_WavesAndEndsTheSame),
                Variable, Marker, seconds: 900, value: file);
            _out.WriteLine($"the child: {child}");
            Assert.Equal(mine, child);
            Assert.DoesNotContain($"seat {seat} at -1", mine); // the saved seat waves starved after the load
        }
        finally
        {
            File.Delete(file);
        }
    }
}
