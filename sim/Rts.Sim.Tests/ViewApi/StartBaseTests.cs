using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M3-V1: the default match's start bases (<see cref="StartBase"/>): a Town Hall per player beside its start block and workers round it.</summary>
public class StartBaseTests
{
    private readonly ITestOutputHelper _out;

    public StartBaseTests(ITestOutputHelper output) => _out = output;

    /// <summary>The Match's setup: the 128 map with 12 forests / 8 mines, <paramref name="perPlayer"/> march units per player in their blocks.</summary>
    internal static (Simulation Sim, Vector2[][] Blocks, StartBasePlan Plan) MatchSetup(ulong seed, int perPlayer, int workers,
        int forests = 12, int mines = 8, int units = 2010)
    {
        Simulation sim = PropLayoutTests.MatchSim(seed, units, forests, mines);
        World w = sim.World;
        var blocks = new Vector2[2][];
        float maxR = MaxRadius(w.Data);
        for (int p = 0; p < 2; p++)
        {
            FactionDef f = w.Data.Factions[w.FactionOf(p)];
            float r = 0f;
            foreach (int t in f.Units) r = Math.Max(r, w.Data.Units[t].Radius);
            blocks[p] = StartLayout.Block(w.NavGrid, perPlayer, west: p == 0, r);
        }
        StartBasePlan plan = ViewReads.Plan(w, blocks, workers, maxR);
        return (sim, blocks, plan);
    }

    internal static float MaxRadius(GameData data) => data.Units.Max(u => u.Radius);

    /// <summary>Enqueues what Match.Start enqueues (march, hall, workers per player) and runs two ticks.</summary>
    internal static void Apply(Simulation sim, Vector2[][] blocks, StartBasePlan plan)
    {
        World w = sim.World;
        for (int p = 0; p < blocks.Length; p++)
        {
            FactionDef f = w.Data.Factions[w.FactionOf(p)];
            for (int k = 0; k < blocks[p].Length; k++) sim.Enqueue(Command.SpawnUnit(p, f.Units[k % f.Units.Length], blocks[p][k]));
            if (plan.HallAnchor[p] < 0) continue;
            int a = plan.HallAnchor[p];
            sim.Enqueue(Command.SpawnBuilding(p, plan.HallType[p], w.NavGrid.CellCenter(a % w.NavGrid.Width, a / w.NavGrid.Width)));
            foreach (Vector2 v in plan.Workers[p]) sim.Enqueue(Command.SpawnUnit(p, plan.WorkerType[p], v));
        }
        sim.Tick();
        sim.Tick();
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(31UL)]
    public void DefaultMatch_EachPlayerGetsItsTownHallAndFiveWorkers_AllApplied(ulong seed)
    {
        (Simulation sim, Vector2[][] blocks, StartBasePlan plan) = MatchSetup(seed, 100, 5);
        World w = sim.World;
        Apply(sim, blocks, plan);
        BuildingStore b = w.Buildings;
        Assert.Equal(2, b.Count);
        for (int p = 0; p < 2; p++)
        {
            int faction = w.FactionOf(p);
            Assert.True(plan.HallAnchor[p] >= 0, $"seed {seed} player {p}: no spot");
            BuildingDef def = w.Data.Buildings[plan.HallType[p]];
            Assert.Equal(BuildingSlot.TownHall, def.Slot);
            Assert.Equal(faction, def.Faction);
            int slot = -1;
            for (int k = 0; k < b.Capacity; k++) if (b.Alive[k] && b.Owner[k] == p) slot = k;
            Assert.True(slot >= 0);
            Assert.Equal(plan.HallAnchor[p], b.Cell[slot]);
            Assert.False(b.UnderConstruction[slot]);
            Assert.Equal(def.Hp, b.Hp[slot]);
            // On the block's level.
            w.NavGrid.WorldToCell(blocks[p][0], out int bx, out int by);
            Assert.Equal(w.NavGrid.LevelAt(bx, by), w.NavGrid.LevelAt(plan.HallAnchor[p] % w.NavGrid.Width, plan.HallAnchor[p] / w.NavGrid.Width));
            Assert.Equal(5, plan.Workers[p].Length);
            int workers = 0;
            UnitStore u = w.Units;
            Vector2 c = StartBase.FootprintCenter(w.NavGrid, def, plan.HallAnchor[p]);
            foreach (Vector2 spot in plan.Workers[p])
            {
                // Beside the hall: on its first ring (the ring is all passable, so 5 always fit there).
                Assert.True(MathF.Abs(spot.X - c.X) <= def.FootprintWidth + MapConstants.CellSize
                    && MathF.Abs(spot.Y - c.Y) <= def.FootprintHeight + MapConstants.CellSize, $"worker spot {spot}, hall centre {c}");
                for (int i = 0; i < u.Capacity; i++)
                    if (u.Alive[i] && u.Owner[i] == p && u.Position[i] == spot && u.TypeId[i] == plan.WorkerType[p]) workers++;
            }
            // On the hall's side facing the nearest mine: each nearer the mine than the hall's centre is.
            Assert.True(ViewReads.NearestMine(w, c, out Vector2 mineCenter));
            foreach (Vector2 spot in plan.Workers[p])
                Assert.True(Vector2.Distance(spot, mineCenter) < Vector2.Distance(c, mineCenter), $"worker spot {spot} faces away from the mine at {mineCenter}");
            Assert.Equal(UnitSlot.Worker, w.Data.Units[plan.WorkerType[p]].Slot);
            Assert.Equal(faction, w.Data.Units[plan.WorkerType[p]].Faction);
            Assert.Equal(5, workers);
        }
        Assert.Equal(210, w.Units.Count);
        DescribeNearestNodes(w, plan, seed);
    }

    private void DescribeNearestNodes(World w, StartBasePlan plan, ulong seed)
    {
        for (int p = 0; p < 2; p++)
        {
            BuildingDef def = w.Data.Buildings[plan.HallType[p]];
            Vector2 c = StartBase.FootprintCenter(w.NavGrid, def, plan.HallAnchor[p]);
            float mine = float.PositiveInfinity, tree = float.PositiveInfinity;
            for (int i = 0; i < w.Resources.Capacity; i++)
            {
                if (!w.Resources.Alive[i]) continue;
                ResourceDef rd = w.Data.Resources[w.Resources.TypeId[i]];
                Vector2 nc = new((w.Resources.Cell[i] % w.NavGrid.Width + rd.FootprintWidth * 0.5f) * 2f, (w.Resources.Cell[i] / w.NavGrid.Width + rd.FootprintHeight * 0.5f) * 2f);
                float d = Vector2.Distance(nc, c);
                if (rd.Resource == ResourceKind.Gold) mine = Math.Min(mine, d); else tree = Math.Min(tree, d);
            }
            _out.WriteLine($"seed {seed} player {p}: hall at {plan.HallAnchor[p] % w.NavGrid.Width},{plan.HallAnchor[p] / w.NavGrid.Width}, nearest mine {mine:0.0} m, tree {tree:0.0} m");
        }
    }

    [Fact]
    public void Fuzz50Seeds_EveryPlannedHallIsAccepted_OnItsSide_WithAPassableRing_AndWorkersBesideIt()
    {
        int noSpot = 0;
        for (ulong seed = 1; seed <= 50; seed++)
        {
            (Simulation sim, Vector2[][] blocks, StartBasePlan plan) = MatchSetup(seed, 100, 5);
            World w = sim.World;
            NavGrid g = w.NavGrid;
            // The plan is taken before anything spawns: check the ring on the bare grid.
            for (int p = 0; p < 2; p++)
            {
                int a = plan.HallAnchor[p];
                if (a < 0)
                {
                    noSpot++;
                    Assert.Empty(plan.Workers[p]);
                    continue;
                }
                BuildingDef def = w.Data.Buildings[plan.HallType[p]];
                int x0 = a % g.Width, y0 = a / g.Width, mid = g.Width / 2;
                for (int y = y0 - 1; y <= y0 + def.FootprintHeight; y++)
                    for (int x = x0 - 1; x <= x0 + def.FootprintWidth; x++)
                        Assert.True(g.IsPassable(x, y), $"seed {seed} player {p}: ({x}, {y}) of the hall or its ring is blocked");
                if (p == 0) Assert.True(x0 + def.FootprintWidth <= mid - StartLayout.HalfGapCells - 1, $"seed {seed}: west hall at x {x0} reaches the gap");
                else Assert.True(x0 - 1 >= mid + StartLayout.HalfGapCells, $"seed {seed}: east hall at x {x0} reaches the gap");
                Assert.Equal(5, plan.Workers[p].Length);
            }
            Apply(sim, blocks, plan);
            int halls = (plan.HallAnchor[0] >= 0 ? 1 : 0) + (plan.HallAnchor[1] >= 0 ? 1 : 0);
            Assert.True(halls == w.Buildings.Count, $"seed {seed}: {w.Buildings.Count} of {halls} planned halls stand");
            Assert.Equal(200 + 5 * halls, w.Units.Count);
        }
        _out.WriteLine($"50 seeds: {noSpot} players without a spot");
    }

    [Fact]
    public void NoRoomForAHall_PlanHasNoAnchorAndNoWorkers()
    {
        // A 7 x 7 map: the border leaves a 5 x 5 interior, too small for 4 x 4 plus its ring.
        Simulation sim = ResourceMaps.NewSim(ResourceMaps.Flat(7, 7));
        World w = sim.World;
        StartBasePlan plan = ViewReads.Plan(w, new[] { new[] { w.NavGrid.CellCenter(3, 3) } }, 5, 0.5f);
        Assert.Equal(-1, plan.HallAnchor[0]);
        Assert.Empty(plan.Workers[0]);
        Assert.True(plan.HallType[0] >= 0 && plan.WorkerType[0] >= 0);
    }

    [Fact]
    public void ATreeOnTheRing_RulesTheSpotOut_AndTakenCellsDo()
    {
        Simulation sim = ResourceMaps.NewSim(ResourceMaps.Flat(20, 20));
        World w = sim.World;
        int keep = GatherMaps.Keep, width = w.NavGrid.Width;
        int anchor = 8 * width + 8;
        Assert.True(ViewReads.IsHallSpot(w, keep, anchor, 0, null));
        Assert.False(ViewReads.IsHallSpot(w, keep, anchor, 1, null)); // another level
        var taken = new bool[width * w.NavGrid.Height];
        taken[7 * width + 7] = true; // the ring's corner
        Assert.False(ViewReads.IsHallSpot(w, keep, anchor, 0, taken));
        Assert.False(ViewReads.IsHallSpot(w, keep, anchor, 0, null, minX: 8)); // the ring starts at x 7
        Assert.False(ViewReads.IsHallSpot(w, keep, anchor, 0, null, maxX: 11)); // and ends at x 12
        Assert.True(ViewReads.IsHallSpot(w, keep, anchor, 0, null, minX: 7, maxX: 12));
        ResourceMaps.Spawn(w, ResourceMaps.Tree, 12, 10, 100); // east ring cell
        Assert.False(ViewReads.IsHallSpot(w, keep, anchor, 0, null));
        // The nearest spot moves off the tree: one cell west, so the ring ends at x 11.
        Assert.Equal(8 * width + 7, ViewReads.HallSpot(w, keep, w.NavGrid.CellCenter(10, 10), 0, null));
    }

    [Fact]
    public void WorkerSpots_TakeRingOneFirst_NearestTheTargetFirst_SkipTakenAndBlockedCells_AndMarkThem()
    {
        Simulation sim = ResourceMaps.NewSim(ResourceMaps.Flat(20, 20));
        World w = sim.World;
        NavGrid g = w.NavGrid;
        BuildingDef def = w.Data.Buildings[GatherMaps.Keep];
        int anchor = 8 * g.Width + 8; // footprint x, y 8-11; ring 1 at 7 and 12
        var taken = new bool[g.Width * g.Height];
        taken[9 * g.Width + 12] = true;
        ResourceMaps.Spawn(w, ResourceMaps.Tree, 12, 10, 100);
        var spots = new Vector2[30];
        // Toward the east: ring 1's east column first, minus its taken and tree cells.
        Vector2 east = g.CellCenter(18, 10);
        int n = StartBase.WorkerSpots(g, def, anchor, 30, taken, spots, east);
        Assert.Equal(30, n);
        Assert.Equal(g.CellCenter(12, 11), spots[0]); // (12, 10) nearest, but a tree; (12, 9) ties with (12, 11), but is taken
        float last = 0f;
        for (int k = 0; k < 18; k++)
        {
            g.WorldToCell(spots[k], out int x, out int y);
            Assert.True(x is 7 or 12 || y is 7 or 12, $"spot {k} at ({x}, {y}) is not on ring 1");
            Assert.True(taken[y * g.Width + x]);
            float d = Vector2.DistanceSquared(spots[k], east);
            Assert.True(d >= last, $"spot {k} is nearer the target than spot {k - 1}");
            last = d;
        }
        g.WorldToCell(spots[18], out int x18, out int y18);
        Assert.True(x18 is 6 or 13 || y18 is 6 or 13);
        Assert.Equal(spots.Length, spots.Distinct().Count());
    }
}
