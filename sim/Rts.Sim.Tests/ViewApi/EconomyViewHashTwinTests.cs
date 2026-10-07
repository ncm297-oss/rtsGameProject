using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M3-V1 read-only proof: every new view read (<see cref="StartBase"/>, <see cref="ResourcePicker"/>, <see cref="BuildingBars"/>, <see cref="DebugCounts.InState"/>) and the resource bar's and worker views' reads, every tick, never change the state hash.</summary>
public class EconomyViewHashTwinTests
{
    [Fact]
    public void EconomyViewReadsEveryTick_HashEqualsABareTwin_400Ticks()
    {
        (Simulation a, Vector2[][] blocks, StartBasePlan plan) = StartBaseTests.MatchSetup(1, 100, 5);
        (Simulation b, _, _) = StartBaseTests.MatchSetup(1, 100, 5);
        StartBaseTests.Apply(a, blocks, plan);
        StartBaseTests.Apply(b, blocks, plan);
        World w = a.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        Assert.Equal(b.StateHash(), a.StateHash());

        // Player 0's five workers: two to the nearest mine (a right-click on it: a Gather per worker),
        // two to a tree, one builds a house beside the hall (cancelled half way).
        var workers = new List<EntityHandle>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && u.TypeId[i] == plan.WorkerType[0] && plan.Workers[0].Contains(u.Position[i]))
                workers.Add(new EntityHandle(i, u.Generation[i]));
        Assert.Equal(5, workers.Count);
        Vector2 hall = StartBase.FootprintCenter(g, w.Data.Buildings[plan.HallType[0]], plan.HallAnchor[0]);
        int mine = Nearest(w, hall, gold: true), tree = Nearest(w, hall, gold: false);
        Vector2 minePoint = g.CellCenter(w.Resources.Cell[mine] % g.Width, w.Resources.Cell[mine] / g.Width);
        Vector2 treePoint = g.CellCenter(w.Resources.Cell[tree] % g.Width, w.Resources.Cell[tree] / g.Width);
        Assert.Equal(mine, ViewReads.NodeAtPoint(w, minePoint));
        Assert.Equal(tree, ViewReads.NodeAtPoint(w, treePoint));
        int house = StartBase.BuildingOfSlot(w.Data, w.FactionOf(0), Rts.Sim.Data.BuildingSlot.House);
        int site = -1;
        for (int cell = 0; cell < g.Width * g.Height && site < 0; cell++)
            if (w.CanPlace(0, house, cell, out _) && Vector2.Distance(g.CellCenter(cell % g.Width, cell / g.Width), hall) < 20f) site = cell;
        Assert.True(site >= 0);
        Vector2 sitePoint = g.CellCenter(site % g.Width, site / g.Width);
        var orders = new[]
        {
            Command.Gather(0, workers[0], minePoint), Command.Gather(0, workers[1], minePoint),
            Command.Gather(0, workers[2], treePoint), Command.Gather(0, workers[3], treePoint, queued: true),
            Command.Build(0, workers[4], house, sitePoint),
        };
        foreach (Command c in orders)
        {
            a.Enqueue(c);
            b.Enqueue(c);
        }

        long sink = 0;
        int sawProgress = 0, sawGathering = 0, sawCargo = 0;
        for (int tick = 0; tick < 400; tick++)
        {
            if (tick == 250)
            {
                a.Enqueue(Command.Cancel(0, sitePoint));
                b.Enqueue(Command.Cancel(0, sitePoint));
            }
            ulong before = a.StateHash();
            // The plan (match start), the picker over node cells and a stripe of others (right-click),
            // the bars (BuildingViews), the state counts (F12), the totals (resource bar), cargo (markers).
            StartBasePlan again = ViewReads.Plan(w, blocks, 5, StartBaseTests.MaxRadius(w.Data));
            sink += again.HallAnchor[0] + again.Workers[1].Length;
            for (int cell = tick % 13; cell < g.Width * g.Height; cell += 13) sink += ViewReads.NodeAt(w, cell);
            sink += ViewReads.NodeAtPoint(w, minePoint) + ViewReads.NodeAtPoint(w, treePoint);
            for (int k = 0; k < w.Buildings.Capacity; k++)
            {
                BuildingBarKind bar = ViewReads.Bar(w, k, out float fill);
                if (bar == BuildingBarKind.Progress && fill > 0f) sawProgress++;
                sink += (int)bar;
            }
            int gathering = DebugCounts.InState(u.Alive, u.State, UnitState.Gathering);
            sawGathering += gathering;
            sink += gathering + DebugCounts.InState(u.Alive, u.State, UnitState.Returning) + DebugCounts.InState(u.Alive, u.State, UnitState.Building);
            sink += w.Gold[0] + w.Wood[0];
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.Cargo[i] == 0) continue;
                sawCargo++;
                sink += (int)u.CargoKind[i];
            }
            Assert.Equal(before, a.StateHash());

            a.Tick();
            b.Tick();
            Assert.True(b.StateHash() == a.StateHash(), $"tick {a.TickNumber}: view-read sim diverged from its twin");
        }
        Assert.True(sink != 0);
        Assert.True(sawProgress > 0 && sawGathering > 0 && sawCargo > 0, $"progress {sawProgress}, gathering {sawGathering}, cargo {sawCargo}");
        Assert.Equal(2, w.Buildings.Count); // the site was cancelled
    }

    private static int Nearest(World w, Vector2 from, bool gold)
    {
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < w.Resources.Capacity; i++)
        {
            if (!w.Resources.Alive[i]) continue;
            bool isGold = w.Data.Resources[w.Resources.TypeId[i]].Resource == Rts.Sim.Data.ResourceKind.Gold;
            if (isGold != gold) continue;
            int c = w.Resources.Cell[i];
            float d = Vector2.Distance(w.NavGrid.CellCenter(c % w.NavGrid.Width, c / w.NavGrid.Width), from);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }
}
