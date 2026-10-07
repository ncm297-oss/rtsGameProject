using System.Diagnostics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResearchMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-6 criterion 8: the requirement gates add little to a busy tick, and a flood of locked commands is cheap.</summary>
[Collection(SerialCollection.Name)]
public class RequirementPerfTests
{
    private readonly ITestOutputHelper _out;

    public RequirementPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>A building of <paramref name="type"/> for <paramref name="player"/> on the first anchor (from <paramref name="from"/>) where it can be placed.</summary>
    private static int PlaceSomewhere(Simulation sim, int player, int type, int from)
    {
        NavGrid g = sim.World.NavGrid;
        Give(sim, player, 1_000_000, 1_000_000);
        for (int c = from; c < g.Width * g.Height; c += 53)
        {
            if (!sim.World.CanPlace(player, type, c, out _)) continue;
            sim.Enqueue(Command.SpawnBuilding(player, type, g.CellCenter(c % g.Width, c / g.Width)));
            sim.Tick();
            sim.Tick();
            int k = sim.World.Buildings.SlotAt(c % g.Width, c / g.Width);
            Assert.True(k >= 0);
            return k;
        }
        throw new InvalidOperationException("nowhere to place it");
    }

    /// <summary>
    /// The M3-5 criterion-9 scene (500 marchers, 50 gatherers, 20 halls training, 10 Forges researching) with every kind
    /// of command going through its gate each tick: the halls' Trains and the Forges' Research as before (player 0 in Age
    /// II, so its level-2 and faction upgrades pass real requirement checks), plus 20 locked Zealot Trains and 10 locked
    /// Age II Researches for player 1 (no Age II, one hall slot) and 10 Builds for player 0 refused by placement.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void TheResearchSceneWithEveryCommandThroughTheThreeGates_AverageTickUnder1Point3Ms()
    {
        (Simulation sim, List<int> halls) = ProductionPerfTests.Scene();
        List<int> forges = ResearchPerfTests.AddForges(sim);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        w.Techs.Set(0, AgeII, true);
        int raiderCamp = PlaceSomewhere(sim, 1, RaiderCamp, 2_011);
        var workers = new List<EntityHandle>();
        int forge0 = forges.First(k => w.Buildings.Owner[k] == 0);
        int fx = w.Buildings.Cell[forge0] % g.Width, fy = w.Buildings.Cell[forge0] / g.Width;
        for (int n = 0; n < 10; n++) workers.Add(Unit(sim, At(sim, fx + n % 5, fy + 4 + n / 5)));
        // Anchored one cell left of the Forge: a 3 x 3 Barracks there overlaps it (Blocked, after the faction and requirement rules).
        System.Numerics.Vector2 blocked = At(sim, fx - 1, fy);
        Assert.Equal(PlacementError.Blocked, Gate(w, Barracks, fy * g.Width + fx - 1));
        Assert.Equal(TrainError.LockedByRequirement, TrainGate(w, raiderCamp));
        int holy = halls[0];

        void Commands()
        {
            ProductionPerfTests.TopUp(sim, halls);
            ResearchPerfTests.KeepResearching(sim, forges);
            for (int n = 0; n < 20; n++) sim.Enqueue(Command.Train(1, In(sim, raiderCamp), Zealot));
            for (int n = 0; n < 10; n++) sim.Enqueue(Command.Research(1, In(sim, holy), AgeII));
            foreach (EntityHandle h in workers) sim.Enqueue(Command.Build(0, h, Barracks, blocked));
        }

        for (int t = 0; t < 20; t++)
        {
            Commands();
            sim.Tick();
        }
        long ticks = 0;
        int researched = 0;
        for (int t = 0; t < 2000; t++)
        {
            Commands();
            int had = ResearchPerfTests.Researched(w);
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            ticks += Stopwatch.GetTimestamp() - start;
            researched += ResearchPerfTests.Researched(w) - had;
        }
        double ms = ticks * 1000.0 / Stopwatch.Frequency / 2000;
        _out.WriteLine($"research scene + 40 gated commands a tick: avg {ms:F3} ms per tick over 2,000; {researched} researches done");
        Assert.Equal(0, w.Buildings.QueueCount[raiderCamp]);
        Assert.False(w.HasTech(1, AgeII));
        Assert.True(researched >= 15, $"{researched} researches done");
        Assert.True(ms < 1.3, $"average tick {ms:F3} ms");
    }

    private static PlacementError Gate(World w, int type, int cell)
    {
        w.CanPlace(0, type, cell, out PlacementError why);
        return why;
    }

    private static TrainError TrainGate(World w, int k)
    {
        w.CanTrain(1, k, Zealot, out TrainError why);
        return why;
    }

    /// <summary>5,000 locked commands (Train, Build, Research) applying in one tick, on the gating fixture.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void FiveThousandLockedTrainBuildAndResearchCommands_InOneTick_Under3Ms()
    {
        GameData d = RequirementGatingTests.Fixture;
        var sim = new Simulation(new SimConfig(5, 1, 64, 8192) { Data = d }, ResourceMaps.Flat(64, 48));
        World w = sim.World;
        int keep = Building(sim, 4, 4).Index;
        int yard = Building(sim, 12, 4, type: EngineersYard).Index;
        int armory = Building(sim, 20, 4, type: Armory).Index;
        EntityHandle worker = Unit(sim, At(sim, 30, 20));
        Give(sim, 0, 1_000_000, 1_000_000);
        int tower = d.FindBuilding("malazan_cadre_tower");
        (int gold, int wood) = (w.Gold[0], w.Wood[0]);
        double worst = 0;
        for (int round = 0; round < 3; round++) // the first round JITs every path
        {
            for (int n = 0; n < 5000; n++)
            {
                switch (n % 4)
                {
                    case 0: sim.Enqueue(Command.Train(0, In(sim, yard), Sapper)); break;              // Age II
                    case 1: sim.Enqueue(Command.Research(0, In(sim, keep), AgeII)); break;            // one hall slot
                    case 2: sim.Enqueue(Command.Research(0, In(sim, armory), Melee2)); break;         // level 1 and Age II
                    default: sim.Enqueue(Command.Build(0, worker, tower, At(sim, 40, 10 + n % 20))); break; // Age II
                }
            }
            sim.Tick();
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            if (round > 0) worst = Math.Max(worst, ms);
        }
        _out.WriteLine($"5,000 locked commands applying in one tick: worst of the two timed rounds {worst:F3} ms");
        Assert.Equal((gold, wood), (w.Gold[0], w.Wood[0]));
        Assert.Equal((0, 0, 0), (w.Buildings.QueueCount[keep], w.Buildings.QueueCount[yard], w.Buildings.QueueCount[armory]));
        Assert.Equal(3, w.Buildings.Count);
        Assert.True(worst < 3.0, $"5,000 locked commands took {worst:F3} ms");
    }
}
