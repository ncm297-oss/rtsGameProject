using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-SH2 (BUG-1520's drop-off rule and BUG-1465's retrain in whole matches). (1) QA focus 6: Raraku seed 1, Normal v
/// Normal (the match the retrain was found on) to 40,000 ticks: a save on every 500th tick, each loaded, saved byte-identical
/// and run 300 ticks against the uninterrupted run's per-tick hashes. (2) QA focus 0: the AI's economy on fresh matches,
/// gathered gold and wood per seat at 2:00 / 4:00 / 6:00 / 10:00, printed (run on the base and this tree to compare).
/// </summary>
[Trait("Category", "Stress")]
public class M6Sh2QaStressTests(ITestOutputHelper output)
{
    private const int Limit = 40_000, Every = 500, Follow = 300;

    [Fact]
    public void RarakuSeed1_ASaveEvery500Ticks_FollowsTheRun()
    {
        Simulation reference = MapFileTests.MapMatch("raraku", 1, false, out _);
        var hashes = new ulong[Limit + Follow + 1];
        while (reference.TickNumber < Limit + Follow && !reference.MatchOver)
        {
            reference.Tick();
            hashes[reference.TickNumber] = reference.StateHash();
        }
        int end = reference.TickNumber;
        Simulation a = MapFileTests.MapMatch("raraku", 1, false, out _);
        int saves = 0;
        while (a.TickNumber < Limit && a.TickNumber < end)
        {
            a.Tick();
            Assert.True(hashes[a.TickNumber] == a.StateHash(), $"the second run differs from the first at tick {a.TickNumber}");
            if (a.TickNumber % Every != 0) continue;
            byte[] bytes = SaveTestRun.Save(a);
            Simulation loaded = SaveTestRun.Load(TestSim.Data, bytes);
            Assert.Equal(bytes, SaveTestRun.Save(loaded));
            Assert.True(loaded.StateHash() == a.StateHash(), $"the save at {a.TickNumber} loads to another hash");
            for (int t = 0; t < Follow && loaded.TickNumber < end; t++)
            {
                loaded.Tick();
                Assert.True(hashes[loaded.TickNumber] == loaded.StateHash(), $"the save at {a.TickNumber} diverged at tick {loaded.TickNumber}");
            }
            saves++;
        }
        output.WriteLine($"Raraku seed 1: {saves} saves, each followed {Follow} ticks, run to {end} (match over {reference.MatchOver})");
        Assert.True(saves >= 60);
    }

    [Theory]
    [InlineData("raraku", 3UL)]
    [InlineData("pale_hills", 1UL)]
    [InlineData("vathar_crossing", 7UL)]
    public void FreshMatch_GatheredByTheClock_Printed(string map, ulong seed)
    {
        Simulation sim = MapFileTests.MapMatch(map, seed, false, out _);
        int[] marks = { 2400, 4800, 7200, 12_000 };
        foreach (int mark in marks)
        {
            while (sim.TickNumber < mark && !sim.MatchOver) sim.Tick();
            for (int p = 0; p < 2; p++)
                output.WriteLine($"ECON {map} seed {seed} tick {mark} seat {p}: gathered gold {sim.World.GatheredGold[p]} wood {sim.World.GatheredWood[p]}");
        }
        Assert.True(sim.World.GatheredWood[0] > 0 && sim.World.GatheredWood[1] > 0, "a seat gathered no wood in 10 minutes");
    }
}
