using System.Diagnostics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-SH2 item 0 (BUG-1520): the cost of the walking re-pick (<c>EconomySystem.WalksToSite</c>: two scans of the building
/// store for every loaded walker on every tick while any player has a drop-off site standing). 400 loaded Laborers walk their
/// loads to a far Keep across a 128 x 128 map with 60 other buildings standing, with and without one drop-off site placed:
/// the mean tick of each is printed, and the site may not add more than a quarter of a millisecond to the mean (the 500-unit
/// budget is 4 ms). Measured on the M6-SH2 tree, alone: 0.175 ms without the site, 1.167 ms with it (+0.99 ms): BUG-1538.
/// </summary>
[Trait("Category", "Stress")]
public class DropOffSiteScanQaStressTests(ITestOutputHelper output)
{
    private static Simulation Scene(bool site)
    {
        Simulation sim = NewSim(Flat(128, 128), units: 512, players: 2);
        World w = sim.World;
        int keep = TestSim.Data.FindBuilding("malazan_garrison_keep"), billet = TestSim.Data.FindBuilding("malazan_billet");
        Assert.True(w.Buildings.Spawn(0, keep, 4 * 128 + 4, out _));
        for (int k = 0; k < 60; k++) Assert.True(w.Buildings.Spawn(1, billet, (100 + k / 10 * 3) * 128 + 60 + k % 10 * 3, out _));
        if (site) Assert.True(w.Buildings.Spawn(0, TestSim.Data.FindBuilding("malazan_depot"), 120 * 128 + 4, out _, site: true));
        var trees = new List<EntityHandle>();
        for (int k = 0; k < 20; k++) trees.Add(Spawn(w, Tree, 110 + k % 4 * 3, 10 + k / 4 * 3, TreeWood * 50));
        int carry = w.Data.CarryOf(Laborer);
        for (int k = 0; k < 400; k++)
        {
            EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 70 + k % 20 * 2, 20 + k / 20 * 3));
            w.Units.CargoKind[h.Index] = ResourceKind.Wood;
            w.Units.Cargo[h.Index] = carry;
            int cell = w.Resources.Cell[trees[k % trees.Count].Index];
            sim.Enqueue(Command.Gather(0, h, w.NavGrid.CellCenter(cell % 128, cell / 128)));
        }
        return sim;
    }

    private static double MeanTickMs(Simulation sim)
    {
        Run(sim, 60); // warm: the walks start
        var sw = Stopwatch.StartNew();
        Run(sim, 300);
        return sw.Elapsed.TotalMilliseconds / 300;
    }

    [Fact(Skip = "BUG-1538: one drop-off site standing adds about 1 ms a tick to 400 loaded walkers (WalksToSite scans every building slot per walker per tick)")]
    public void FourHundredLoadedWalkers_ASiteStanding_CostsLittle()
    {
        double without = MeanTickMs(Scene(false)), with = MeanTickMs(Scene(true));
        without = Math.Min(without, MeanTickMs(Scene(false)));
        with = Math.Min(with, MeanTickMs(Scene(true)));
        output.WriteLine($"400 loaded walkers, 61 buildings: mean tick {without:F3} ms with no site, {with:F3} ms with one drop-off site standing (+{with - without:F3} ms)");
        Assert.True(with - without < 0.25, $"a drop-off site adds {with - without:F3} ms a tick");
    }
}
