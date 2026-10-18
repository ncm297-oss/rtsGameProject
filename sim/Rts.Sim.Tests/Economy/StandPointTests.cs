using System.Numerics;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-H3, BUG-0790: <c>EconomySystem.WalkToFootprint</c>'s two stand-point rules, on the face of QA's one-sided mine (a 2 x 2
/// mine at cells 12-13 x 3-4, trees west, east and north, open to the south: ring cells (12, 5) and (13, 5), diagonal corner
/// cells (11, 5) and (14, 5)). Before the fix a worker standing in a corner cell was always sent to a ring cell (no corner
/// point existed), and the worker's own crowded cell cost its units however many of its points were free.
/// </summary>
public class StandPointTests
{
    private const int MapW = 32, MapH = 26;

    private static (Simulation Sim, int Anchor) MineFace()
    {
        Simulation sim = GatherMaps.NewSim(Flat(MapW, MapH), units: 16);
        World w = sim.World;
        TestSim.KeepIn(sim, 0);
        for (int x = 10; x <= 15; x++) Spawn(w, Tree, x, 2, TreeWood);
        for (int y = 3; y <= 4; y++)
        {
            Spawn(w, Tree, 10, y, TreeWood); Spawn(w, Tree, 11, y, TreeWood);
            Spawn(w, Tree, 14, y, TreeWood); Spawn(w, Tree, 15, y, TreeWood);
        }
        EntityHandle mine = Spawn(w, Mine, 12, 3, 100_000);
        return (sim, w.Resources.Cell[mine.Index]);
    }

    /// <summary>A holding worker straight into the store (it never moves, so the points stay exactly held).</summary>
    private static void Holder(Simulation sim, Vector2 at) => sim.World.Units.Hold[CombatScenes.Place(sim, 0, Laborer, at).Index] = true;

    /// <summary>
    /// Both ring cells' six stand points held (workers standing on them), a worker in the corner cell (14, 5), 1.7 m off the
    /// footprint's corner: it is sent to the corner cell's own point, 0.125 m out from the corner on both axes and in reach,
    /// not to a taken ring point.
    /// </summary>
    [Fact]
    public void AWorkerInACornerCell_WithTheRingCellsFull_IsSentToTheCornerPointInReach()
    {
        (Simulation sim, int anchor) = MineFace();
        World w = sim.World;
        UnitStore u = w.Units;
        // The ring points (y 10.125; x 24.4, 25, 25.6 and 26.4, 27, 27.6 for 0.4 m workers), each held.
        foreach (float x in new[] { 24.4f, 25f, 25.6f, 26.4f, 27f, 27.6f }) Holder(sim, new Vector2(x, 10.125f));
        EntityHandle worker = CombatScenes.Place(sim, 0, Laborer, new Vector2(28.55f, 11.63f));
        Run(sim, 1); // the spatial hash holds them
        Assert.True(EconomySystem.WalkToFootprint(w, worker.Index, anchor, 2, 2));
        Assert.Equal(5 * MapW + 14, u.GoalCell[worker.Index]);
        Assert.Equal(new Vector2(28f + EconomyConstants.GoalInset, 10f + EconomyConstants.GoalInset), u.Goal[worker.Index]);
        Assert.True(DistanceToFootprint(w.NavGrid, u.Goal[worker.Index], anchor, 2, 2) <= EconomyConstants.Reach);
    }

    /// <summary>
    /// The worker's own ring cell (13, 5) holds two workers but its middle point is free; the other ring cell (12, 5) is empty
    /// and 2 m farther. Counted by units alone (+2 cells' worth) the empty cell won, behind the queue; its own cell with a
    /// free point costs nothing extra, so the worker takes the free middle point beside it.
    /// </summary>
    [Fact]
    public void TheWorkersOwnCrowdedCellWithAFreePoint_CostsNoMoreThanAnEmptyOne()
    {
        (Simulation sim, int anchor) = MineFace();
        World w = sim.World;
        UnitStore u = w.Units;
        Holder(sim, new Vector2(26.4f, 10.125f));
        Holder(sim, new Vector2(27.6f, 10.125f));
        EntityHandle worker = CombatScenes.Place(sim, 0, Laborer, new Vector2(27f, 11.8f));
        Run(sim, 1);
        Assert.True(EconomySystem.WalkToFootprint(w, worker.Index, anchor, 2, 2));
        Assert.Equal(5 * MapW + 13, u.GoalCell[worker.Index]);
        Assert.Equal(new Vector2(27f, 10f + EconomyConstants.GoalInset), u.Goal[worker.Index]);
    }
}
