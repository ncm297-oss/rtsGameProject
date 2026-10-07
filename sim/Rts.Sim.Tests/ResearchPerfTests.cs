using System.Diagnostics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ProductionMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-5 criterion 9: research adds little to a busy tick, and the bonus query is one array read.</summary>
[Collection(SerialCollection.Name)]
public class ResearchPerfTests
{
    private readonly ITestOutputHelper _out;

    public ResearchPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>Five Armories for player 0 and five Smithies for player 1, placed in the M3-4 criterion-10 scene.</summary>
    private static List<int> AddForges(Simulation sim)
    {
        NavGrid g = sim.World.NavGrid;
        BuildMaps.Give(sim, 0, 10_000_000, 10_000_000);
        var placed = new List<(int Cell, int Player)>();
        for (int p = 0; p < 2; p++)
        {
            int type = p == 0 ? ResearchMaps.Armory : ResearchMaps.Smithy;
            for (int c = 45 + 31 * p; c < g.Width * g.Height && placed.Count(x => x.Player == p) < 5; c += 97)
            {
                if (!sim.World.CanPlace(p, type, c, out _)) continue;
                if (!placed.All(a => Math.Abs(a.Cell % g.Width - c % g.Width) > 5 || Math.Abs(a.Cell / g.Width - c / g.Width) > 5)) continue;
                placed.Add((c, p));
                sim.Enqueue(Command.SpawnBuilding(p, type, g.CellCenter(c % g.Width, c / g.Width)));
            }
        }
        Assert.Equal(10, placed.Count);
        sim.Tick();
        sim.Tick();
        var forges = new List<int>();
        BuildingStore b = sim.World.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && sim.World.Data.Buildings[b.TypeId[k]].Slot == BuildingSlot.Forge) forges.Add(k);
        Assert.Equal(10, forges.Count);
        return forges;
    }

    /// <summary>Between ticks: every idle Forge researches the next tech its owner may queue; once a player has run out, its researched flags are cleared (test seam) so research never stops.</summary>
    private static void KeepResearching(Simulation sim, List<int> forges)
    {
        World w = sim.World;
        BuildingStore b = w.Buildings;
        foreach (int k in forges)
        {
            if (b.QueueCount[k] > 0) continue;
            int p = b.Owner[k];
            int pick = -1;
            foreach (int t in w.Data.TechsResearchableAt(b.TypeId[k]))
                if (pick < 0 && w.CanResearch(p, k, t, out _)) pick = t;
            if (pick < 0)
            {
                foreach (int t in w.Data.TechsResearchableAt(b.TypeId[k])) w.Techs.Set(p, t, false);
                continue;
            }
            sim.Enqueue(Command.Research(p, In(sim, k), pick));
        }
    }

    /// <summary>Researched flags set, over both players.</summary>
    private static int Researched(World w)
    {
        int n = 0;
        for (int p = 0; p < 2; p++)
            for (int t = 0; t < w.Data.Techs.Length; t++) n += w.HasTech(p, t) ? 1 : 0;
        return n;
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void TheProductionSceneWithTenForgesResearching_AverageTickUnder1Point3Ms()
    {
        (Simulation sim, List<int> halls) = ProductionPerfTests.Scene();
        List<int> forges = AddForges(sim);
        for (int t = 0; t < 20; t++)
        {
            ProductionPerfTests.TopUp(sim, halls);
            KeepResearching(sim, forges);
            sim.Tick();
        }
        long ticks = 0;
        int busy = 0, researched = 0;
        for (int t = 0; t < 2000; t++)
        {
            ProductionPerfTests.TopUp(sim, halls);
            KeepResearching(sim, forges);
            int had = Researched(sim.World);
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            ticks += Stopwatch.GetTimestamp() - start;
            foreach (int k in forges) busy += sim.World.Buildings.QueueCount[k] > 0 ? 1 : 0;
            researched += Researched(sim.World) - had;
        }
        double ms = ticks * 1000.0 / Stopwatch.Frequency / 2000;
        _out.WriteLine($"500 marching + 50 gathering + 20 halls training + 10 forges researching: avg {ms:F3} ms per tick over 2,000; forges busy {100.0 * busy / (10 * 2000):F1}% of forge-ticks; {researched} researches done");
        Assert.True(busy >= 10 * 2000 * 9 / 10, $"forges busy only {busy} of {10 * 2000} forge-ticks");
        Assert.True(researched >= 15, $"{researched} researches done"); // about 2,000 / 800 ticks per Forge, less the waits for a free tech
        Assert.True(ms < 1.3, $"average tick {ms:F3} ms");
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void TechBonus_For500Units_TwoStats_Under0Point05Ms()
    {
        (Simulation sim, _) = ProductionPerfTests.Scene();
        World w = sim.World;
        for (int t = 0; t < w.Data.Techs.Length; t++)
        {
            w.Techs.Set(0, t, true);
            w.Techs.Set(1, t, true);
        }
        UnitStore u = w.Units;
        Assert.True(u.Count >= 500, $"{u.Count} units");
        float sink = 0f;
        int calls = 0;
        void Pass()
        {
            int n = 0;
            for (int i = 0; i < u.Capacity && n < 500; i++)
            {
                if (!u.Alive[i]) continue;
                sink += w.TechBonus(u.Owner[i], u.TypeId[i], TechStat.Attack);
                sink += w.TechBonus(u.Owner[i], u.TypeId[i], TechStat.Armor);
                n++;
                calls += 2;
            }
        }
        for (int r = 0; r < 50; r++) Pass(); // warm-up
        calls = 0;
        const int runs = 1000;
        long start = Stopwatch.GetTimestamp();
        for (int r = 0; r < runs; r++) Pass();
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / runs;
        _out.WriteLine($"TechBonus, 500 units x 2 stats: {ms:F4} ms per pass ({calls / runs} calls); sink {sink}");
        Assert.Equal(1000, calls / runs);
        Assert.True(sink > 0f);
        Assert.True(ms < 0.05, $"{ms:F4} ms for 500 units x 2 stats");
    }
}
