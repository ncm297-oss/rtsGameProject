using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// BUG-0146 (M4-2a): workers queued on a 1 x 1 tree open on one side wedged out of reach for the rest of the match. The
/// front worker stopped ArrivalDistance short of its stand point, the next one stopped touching it, out of reach; when
/// the front one left, the second one's retries "arrived" at once against the queue behind it. Hand-built from the
/// bug's map (seed 21, tree (57, 45)): trees north, west and east of the tree, a Depot to the south-west.
/// </summary>
public class GatherWedgeTests
{
    private readonly ITestOutputHelper _out;

    public GatherWedgeTests(ITestOutputHelper output) => _out = output;

    // The bug's map, shifted: tree T = (11, 6); trees on row 4 (x 9-13), row 5 (x 9-12) and row 6 (x 10-12);
    // the Depot (2 x 2) at cells 7-8 x 5-6. T is exposed only through (11, 7).
    private const int TreeX = 11, TreeY = 6;

    private static int Depot => TestSim.Data.FindBuilding("malazan_depot");

    private static (Simulation Sim, EntityHandle Tree, EntityHandle[] Workers) OneSidedTree(int workers)
    {
        Simulation sim = GatherMaps.NewSim(Flat(24, 16), units: 16);
        World w = sim.World;
        EntityHandle tree = default;
        for (int x = 9; x <= 13; x++) Spawn(w, Tree, x, 4, TreeWood);
        for (int x = 9; x <= 12; x++) Spawn(w, Tree, x, 5, TreeWood);
        for (int x = 10; x <= 12; x++)
        {
            EntityHandle h = Spawn(w, Tree, x, 6, TreeWood);
            if (x == TreeX) tree = h;
        }
        Building(sim, 7, 5, type: Depot);
        var hs = new EntityHandle[workers];
        for (int k = 0; k < workers; k++) hs[k] = Unit(sim, At(sim, 10 + k % 3, 10 + k / 3));
        // Only (11, 7) of T's ring is open.
        NavGrid g = w.NavGrid;
        Assert.False(g.IsPassable(TreeX, TreeY - 1));
        Assert.False(g.IsPassable(TreeX - 1, TreeY));
        Assert.False(g.IsPassable(TreeX + 1, TreeY));
        Assert.True(g.IsPassable(TreeX, TreeY + 1));
        return (sim, tree, hs);
    }

    [Fact]
    public void FourWorkersQueuedOnATreeOpenOnOneSide_EveryOneDelivers_NoneWedgesOutOfReach()
    {
        (Simulation sim, EntityHandle tree, EntityHandle[] hs) = OneSidedTree(4);
        World w = sim.World;
        UnitStore u = w.Units;
        foreach (EntityHandle h in hs) sim.Enqueue(Command.Gather(0, h, At(sim, TreeX, TreeY)));
        var deliveries = new int[hs.Length];
        var lastCargo = new int[hs.Length];
        var wedged = new WedgeClock(hs.Length);
        int worstWedge = 0, worstUnit = -1, worstTick = 0, wood = w.Wood[0];
        for (int t = 1; t <= 600; t++)
        {
            sim.Tick();
            for (int k = 0; k < hs.Length; k++)
            {
                int i = hs[k].Index;
                Assert.True(u.IsAlive(hs[k]));
                if (lastCargo[k] > 0 && u.Cargo[i] == 0)
                {
                    deliveries[k]++;
                    // Wood rises by the whole load on every delivery.
                    Assert.Equal(wood + lastCargo[k], w.Wood[0]);
                    wood = w.Wood[0];
                }
                lastCargo[k] = u.Cargo[i];
                int run = wedged.Tick(w, k, i);
                if (run > worstWedge) (worstWedge, worstUnit, worstTick) = (run, i, t);
            }
        }
        Assert.Equal(w.Wood[0], wood); // nothing else moved the total
        _out.WriteLine($"deliveries {string.Join(", ", deliveries)}; wood {w.Wood[0]}; tree left {w.Resources.Remaining[tree.Index]}; " +
            $"longest out-of-reach stand with nobody between: unit {worstUnit}, {worstWedge} ticks (to tick {worstTick})");
        Assert.True(worstWedge <= 2 * EconomyConstants.RetryTicks,
            $"unit {worstUnit} stood Gathering out of reach with nobody between it and the tree for {worstWedge} ticks (to tick {worstTick})");
        for (int k = 0; k < hs.Length; k++)
            Assert.True(deliveries[k] >= 1, $"worker {k} (unit {hs[k].Index}) delivered nothing in 600 ticks");
    }

    [Fact]
    public void FrontWorkerLeaving_TheNextOneStepsIntoReach_EvenWithTheQueueTouchingItFromBehind()
    {
        // The bug's column: workers ordered one after another from straight south of the tree, each arriving behind
        // the last. When the front one leaves with a load, the next one is at the front with the queue touching it from
        // behind; before the fix its every retry "arrived" there at once, out of reach, for the rest of the match.
        (Simulation sim, EntityHandle _, EntityHandle[] hs) = OneSidedTree(0);
        World w = sim.World;
        UnitStore u = w.Units;
        hs = new EntityHandle[Column];
        for (int k = 0; k < Column; k++) hs[k] = Unit(sim, At(sim, TreeX, 9 + k));
        int worst = 0, worstUnit = -1, worstTick = 0;
        var wedged = new WedgeClock(Column);
        for (int t = 1; t <= 2000; t++)
        {
            if (t <= Column * OrderGap && t % OrderGap == 1) sim.Enqueue(Command.Gather(0, hs[t / OrderGap], At(sim, TreeX, TreeY)));
            sim.Tick();
            for (int k = 0; k < Column; k++)
            {
                int i = hs[k].Index;
                int run = wedged.Tick(w, k, i);
                if (run > worst) (worst, worstUnit, worstTick) = (run, i, t);
            }
        }
        _out.WriteLine($"longest out-of-reach stand with nobody between: unit {worstUnit}, {worst} ticks (to tick {worstTick}); wood {w.Wood[0]}");
        Assert.True(worst <= 2 * EconomyConstants.RetryTicks, $"unit {worstUnit} stood Gathering out of reach with nobody between it and the tree for {worst} ticks (to tick {worstTick})");
    }

    private const int Column = 4, OrderGap = 40;

    /// <summary>
    /// Per worker: ticks in a row it has stood on its node leg (a gather loop, cargo not full) out of reach of the node,
    /// with nobody between it and the node, within 0.5 m of where the count started. Position-based, not state-based: a
    /// wedged worker's 20-tick retries flicker its state (Gathering, Moving, Idle) for a tick and end on the same spot.
    /// </summary>
    private sealed class WedgeClock
    {
        private readonly int[] _run;
        private readonly Vector2[] _anchor;

        public WedgeClock(int n)
        {
            _run = new int[n];
            _anchor = new Vector2[n];
        }

        public int Tick(World w, int k, int i)
        {
            UnitStore u = w.Units;
            bool nodeLeg = EconomySystem.OnLoop(u, i) && w.Resources.IsAlive(u.GatherNode[i]) && u.Cargo[i] < w.Data.Rules.WorkerCarry;
            if (!nodeLeg || InReach(w, i) || SomeoneBetween(w, i) || Vector2.Distance(u.Position[i], _anchor[k]) > 0.5f)
            {
                _anchor[k] = u.Position[i];
                return _run[k] = 0;
            }
            return ++_run[k];
        }
    }

    /// <summary>Unit i's center is within <see cref="EconomyConstants.Reach"/> of its node's footprint.</summary>
    private static bool InReach(World w, int i)
    {
        int n = w.Units.GatherNode[i].Index;
        return DistanceToFootprint(w.NavGrid, w.Units.Position[i], w.Resources.Cell[n], 1, 1) <= EconomyConstants.Reach;
    }

    /// <summary>Another live unit touches unit i (radii + 0.1 m) and stands nearer i's node footprint: i queues behind it.</summary>
    private static bool SomeoneBetween(World w, int i)
    {
        UnitStore u = w.Units;
        int n = u.GatherNode[i].Index;
        float mine = DistanceToFootprint(w.NavGrid, u.Position[i], w.Resources.Cell[n], 1, 1);
        for (int j = 0; j < u.Capacity; j++)
        {
            if (j == i || !u.Alive[j]) continue;
            float touch = u.Radius[i] + u.Radius[j] + 0.1f;
            if (Vector2.DistanceSquared(u.Position[i], u.Position[j]) > touch * touch) continue;
            if (DistanceToFootprint(w.NavGrid, u.Position[j], w.Resources.Cell[n], 1, 1) < mine) return true;
        }
        return false;
    }
}
