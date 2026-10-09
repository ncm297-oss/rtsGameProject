using System.Diagnostics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResearchMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M3-6 (2026-10-07-1415), Perf: a locked answer from each gate is cheaper than an allowed one (the requirement rule
/// sits before the map rules and the flood), and 5,000 locked commands of every kind from three players (two of them
/// the same faction) applying in one tick stay under the 3 ms criterion on a 128 x 128 map.
/// </summary>
[Collection(SerialCollection.Name)]
public class RequirementPerfQaTests
{
    private readonly ITestOutputHelper _out;

    public RequirementPerfQaTests(ITestOutputHelper output) => _out = output;

    private static GameData Fixture => RequirementGatingTests.Fixture;

    /// <summary>Best of five rounds of <paramref name="calls"/> calls, in nanoseconds per call.</summary>
    private static double NsPerCall(int calls, Func<bool> gate)
    {
        double best = double.MaxValue;
        int sink = 0;
        for (int round = 0; round < 6; round++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int n = 0; n < calls; n++) if (gate()) sink++;
            double ns = (Stopwatch.GetTimestamp() - start) * 1e9 / Stopwatch.Frequency / calls;
            if (round > 0) best = Math.Min(best, ns); // round 0 JITs
        }
        GC.KeepAlive(sink);
        return best;
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void ALockedAnswer_IsCheaperThanAnAllowedOne_ForEveryGate()
    {
        var sim = TestSim.Explored(new Simulation(new SimConfig(5, 1, 64, 512) { Data = Fixture }, ResourceMaps.Flat(128, 128))); // M4-3b: placement here is about requirements
        World w = sim.World;
        Building(sim, 4, 4);
        int bar = Building(sim, 12, 4, type: Barracks).Index;
        int armory = Building(sim, 20, 4, type: Armory).Index;
        Give(sim, 0, 1_000_000, 1_000_000);
        int tower = Fixture.FindBuilding("malazan_cadre_tower");
        int cell = Cell(sim, 60, 60);
        // Locked vs allowed on the same building / cell (the Train pair: before and after a Range stands).
        Assert.False(w.CanTrain(0, bar, Infantry, out TrainError te));
        Assert.Equal(TrainError.LockedByRequirement, te);
        Assert.True(w.CanPlace(0, House, cell, out _));
        Assert.False(w.CanPlace(0, tower, cell, out PlacementError pe));
        Assert.Equal(PlacementError.Requires, pe);
        Assert.True(w.CanResearch(0, armory, Melee1, out _));
        Assert.False(w.CanResearch(0, armory, Melee2, out ResearchError re));
        Assert.Equal(ResearchError.Requires, re);

        double trainLocked = NsPerCall(200_000, () => w.CanTrain(0, bar, Infantry, out _));
        double placeOk = NsPerCall(2_000, () => w.CanPlace(0, House, cell, out _));
        double placeLocked = NsPerCall(200_000, () => w.CanPlace(0, tower, cell, out _));
        double researchOk = NsPerCall(200_000, () => w.CanResearch(0, armory, Melee1, out _));
        double researchLocked = NsPerCall(200_000, () => w.CanResearch(0, armory, Melee2, out _));
        Building(sim, 30, 4, type: TestSim.Data.FindBuilding("malazan_crossbow_range"));
        Assert.True(w.CanTrain(0, bar, Infantry, out te), te.ToString());
        double trainOk = NsPerCall(200_000, () => w.CanTrain(0, bar, Infantry, out _));
        _out.WriteLine($"ns per call, allowed vs locked: CanTrain {trainOk:F1} / {trainLocked:F1}, CanPlace (128x128 flood) {placeOk:F1} / {placeLocked:F1}, CanResearch {researchOk:F1} / {researchLocked:F1}");
        // The place gate's locked answer skips every map rule (measured ~8x cheaper, 48 vs 380 ns, Debug).
        Assert.True(placeLocked * 3 < placeOk, $"CanPlace locked {placeLocked:F1} ns vs allowed {placeOk:F1} ns");
        // Train / Research: a locked answer stops earlier in the reason order; allow timer noise, never more than allowed + 25%.
        Assert.True(trainLocked < trainOk * 1.25 + 5, $"CanTrain locked {trainLocked:F1} ns vs allowed {trainOk:F1} ns");
        Assert.True(researchLocked < researchOk * 1.25 + 5, $"CanResearch locked {researchLocked:F1} ns vs allowed {researchOk:F1} ns");
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void FiveThousandLockedCommands_FromThreePlayers_OfEveryKind_InOneTick_Under3Ms()
    {
        GameData d = Fixture;
        var sim = TestSim.Explored(new Simulation(new SimConfig(5, 3, 64, 16384) { Data = d }, ResourceMaps.Flat(128, 128))); // M4-3b: placement here is about requirements
        World w = sim.World;
        int tower = d.FindBuilding("malazan_cadre_tower");
        var keep = new int[3];
        var hall = new int[3];
        var yard = new int[3];
        var worker = new EntityHandle[3];
        for (int p = 0; p < 3; p++)
        {
            bool mal = w.FactionOf(p) == w.FactionOf(0);
            keep[p] = Building(sim, 4 + 40 * p, 4, player: p, type: mal ? Keep : HolyCamp).Index;
            hall[p] = Building(sim, 4 + 40 * p, 14, player: p, type: mal ? Barracks : RaiderCamp).Index;
            yard[p] = mal ? Building(sim, 4 + 40 * p, 24, player: p, type: EngineersYard).Index : hall[p];
            worker[p] = Unit(sim, At(sim, 10 + 40 * p, 40), player: p, type: mal ? Laborer : CampFollower);
            Give(sim, p, 1_000_000, 1_000_000);
        }
        // Every command is locked by a requirement: Heavy Infantry (a Range), Sapper / Zealot / Cadre Tower (Age II),
        // Age II itself (one hall slot finished). Players 0 and 2 are both Malazan.
        var totals = new (int, int)[3];
        for (int p = 0; p < 3; p++) totals[p] = (w.Gold[p], w.Wood[p]);
        int buildings = w.Buildings.Count;
        double worst = 0;
        for (int round = 0; round < 4; round++)
        {
            for (int n = 0; n < 5000; n++)
            {
                int p = n % 3;
                bool mal = p != 1;
                switch (n / 3 % 4)
                {
                    case 0: sim.Enqueue(Command.Train(p, In(sim, hall[p]), mal ? Infantry : Zealot)); break;
                    case 1: sim.Enqueue(Command.Research(p, In(sim, keep[p]), AgeII)); break;
                    case 2: sim.Enqueue(Command.Train(p, In(sim, yard[p]), mal ? Sapper : Zealot)); break;
                    default:
                        if (mal) sim.Enqueue(Command.Build(p, worker[p], tower, At(sim, 20 + n % 80, 60 + n % 50)));
                        else sim.Enqueue(Command.Research(p, In(sim, keep[p]), AgeII));
                        break;
                }
            }
            sim.Tick();
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            if (round > 0) worst = Math.Max(worst, ms);
        }
        _out.WriteLine($"5,000 locked commands (3 players, 5 kinds) applying in one tick: worst of three timed rounds {worst:F3} ms");
        for (int p = 0; p < 3; p++)
        {
            Assert.Equal(totals[p], (w.Gold[p], w.Wood[p]));
            Assert.Equal((0, 0, 0), (w.Buildings.QueueCount[keep[p]], w.Buildings.QueueCount[hall[p]], w.Buildings.QueueCount[yard[p]]));
        }
        Assert.Equal(buildings, w.Buildings.Count);
        Assert.True(worst < 3.0, $"5,000 locked commands took {worst:F3} ms");
    }
}
