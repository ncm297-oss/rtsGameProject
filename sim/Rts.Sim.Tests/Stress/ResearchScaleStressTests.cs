using System.Diagnostics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResearchMaps;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M3-5 (2026-10-07-1131): the "already queued" rule scans every queue of the player for each Research command. A
/// flood (5,000 Research commands in one tick) against 64 own buildings with full queues: exactly one is accepted per
/// tech, and the tick's cost is printed (no wall-clock threshold here: it is far past any design number).
/// </summary>
[Collection(SerialCollection.Name)]
public class ResearchScaleStressTests
{
    private readonly ITestOutputHelper _out;

    public ResearchScaleStressTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void AResearchFlood_AgainstSixtyFourFullQueues_AcceptsOneOfEachTech()
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 64, CommandCapacity: 8192), ResourceMaps.Flat(96, 96));
        World w = sim.World;
        BuildingStore b = w.Buildings;
        var halls = new List<int>();
        var forges = new List<int>();
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                bool forge = (x + y) % 4 == 0;
                int k = Building(sim, 2 + 11 * x, 2 + 11 * y, type: forge ? Armory : Keep).Index;
                (forge ? forges : halls).Add(k);
            }
        Give(sim, 0, 1_000_000, 1_000_000);
        // M3-6: with Age II the faction upgrade opens; the level-2 upgrades still need their level 1 researched.
        w.Techs.Set(0, AgeII, true);
        // Fill every hall's queue with units (the cap stops them: nothing starts beyond the first few).
        foreach (int k in halls)
            for (int q = 0; q < 5; q++) sim.Enqueue(Command.Train(0, In(sim, k), Laborer));
        Run(sim, 2);
        int techs = w.Data.Techs.Length;
        for (int n = 0; n < 5000; n++)
        {
            int k = (n / techs) % 3 == 0 ? halls[(n / techs) % halls.Count] : forges[(n / techs) % forges.Count];
            sim.Enqueue(Command.Research(0, In(sim, k), n % techs));
        }
        sim.Tick();
        long start = Stopwatch.GetTimestamp();
        sim.Tick();
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        int queuedTechs = 0;
        var seen = new HashSet<int>();
        for (int k = 0; k < b.Capacity; k++)
            for (int q = 0; q < b.QueueCount[k]; q++)
                if (b.QueueIsTechAt(k, q))
                {
                    queuedTechs++;
                    Assert.True(seen.Add(b.QueueTypeAt(k, q)), $"{w.Data.Techs[b.QueueTypeAt(k, q)].Key} queued twice");
                }
        _out.WriteLine($"5,000 Research commands, 64 buildings ({halls.Count} halls with full queues): {queuedTechs} accepted, tick {ms:F2} ms");
        // Age II is researched (and couldn't fit a full hall queue); the three level-1 upgrades and Moranth Supply go to
        // Forges; the level-2 upgrades are refused (Requires: level 1 not researched yet, M3-6).
        Assert.Equal(4, queuedTechs);
        Assert.DoesNotContain(Dryjhna, seen);
    }
}
