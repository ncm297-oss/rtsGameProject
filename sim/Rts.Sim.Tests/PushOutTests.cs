using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-3 criterion 5: own units standing or walking in a new footprint are set down outside it in the apply tick, deterministically.</summary>
[Collection(SerialCollection.Name)]
public class PushOutTests
{
    /// <summary>A Keep site at (10, 8) over an Idle, a Moving and a Gathering unit of the builder's player; returns the sim after the apply tick and the units.</summary>
    private static (Simulation Sim, EntityHandle[] Inside) Scene()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        Spawn(sim.World, Tree, 12, 12, TreeWood); // touches the footprint's south edge
        EntityHandle idle = Unit(sim, At(sim, 11, 8));
        EntityHandle idle2 = Unit(sim, At(sim, 11, 8) + new Vector2(0.3f, 0.2f)); // same cell
        EntityHandle mover = Unit(sim, At(sim, 2, 10), type: Infantry);
        EntityHandle gatherer = Unit(sim, At(sim, 12, 11)); // in the footprint, in reach of the tree
        sim.Enqueue(Command.Gather(0, gatherer, At(sim, 12, 12)));
        sim.Enqueue(Command.Move(0, mover, At(sim, 27, 10)));
        EntityHandle builder = Unit(sim, At(sim, 5, 5));
        UnitStore u = sim.World.Units;
        for (int t = 0; t < 300 && !Inside(sim, mover); t++) sim.Tick();
        Assert.True(Inside(sim, mover) && Inside(sim, gatherer) && Inside(sim, idle), "not all in the footprint");
        Assert.Equal((UnitState.Moving, UnitState.Gathering), (u.State[mover.Index], u.State[gatherer.Index]));
        SetTotals(sim, 0, 300, 300);
        sim.Enqueue(Command.Build(0, builder, Keep, At(sim, 10, 8)));
        sim.Tick();
        Assert.True(SiteAt(sim, 10, 8) < 0);
        sim.Tick();
        Assert.True(SiteAt(sim, 10, 8) >= 0);
        return (sim, new[] { idle, idle2, mover, gatherer });
    }

    private static bool Inside(Simulation sim, EntityHandle h)
    {
        Vector2 p = sim.World.Units.Position[h.Index];
        return p.X >= 20f && p.X < 28f && p.Y >= 16f && p.Y < 24f; // cells 10-13 x 8-11
    }

    [Fact]
    public void OwnUnitsInTheFootprint_EndTheApplyTickOnFreePassableCellsOutside()
    {
        (Simulation sim, EntityHandle[] inside) = Scene();
        NavGrid g = sim.World.NavGrid;
        UnitStore u = sim.World.Units;
        var cells = new HashSet<int>();
        foreach (EntityHandle h in inside)
        {
            Assert.False(Inside(sim, h), $"slot {h.Index} still at {u.Position[h.Index]}");
            Assert.True(g.WorldToCell(u.Position[h.Index], out int x, out int y) && g.IsPassable(x, y));
            Assert.True((g.FlagsAt(x, y) & NavFlags.Building) == 0);
        }
        // The two Idle units shared a cell; each was set down on its own free cell next to the footprint.
        Assert.True(g.WorldToCell(u.Position[inside[0].Index], out int ax, out int ay));
        Assert.True(g.WorldToCell(u.Position[inside[1].Index], out int bx, out int by));
        Assert.NotEqual((ax, ay), (bx, by));
        Assert.Equal(u.Position[inside[0].Index], g.CellCenter(ax, ay));
    }

    [Fact]
    public void ThePush_IsTwinIdentical()
    {
        (Simulation a, _) = Scene();
        (Simulation b, _) = Scene();
        Assert.Equal(a.StateHash(), b.StateHash());
        for (int t = 0; t < 100; t++)
        {
            a.Tick();
            b.Tick();
        }
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void AHolderOrAnEnemyInTheFootprint_MakesThePlacementInvalid()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20), players: 2);
        EntityHandle holder = Unit(sim, At(sim, 5, 5));
        sim.Enqueue(Command.HoldPosition(0, holder));
        EntityHandle enemy = Unit(sim, At(sim, 15, 5), player: 1);
        CombatScenes.Spot(sim, 0, enemy); // BUG-0280: a hidden enemy is not in the way of the query; this one is seen
        EntityHandle w = Unit(sim, At(sim, 2, 15));
        Assert.False(sim.World.CanPlace(0, House, Cell(sim, 4, 4), out PlacementError a));
        Assert.False(sim.World.CanPlace(0, House, Cell(sim, 15, 5), out PlacementError b));
        Assert.Equal((PlacementError.UnitInTheWay, PlacementError.UnitInTheWay), (a, b));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 4, 4)));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 15, 5)));
        Run(sim, 2);
        Assert.Equal(0, sim.World.Buildings.Count);
        Assert.Equal(At(sim, 5, 5), sim.World.Units.Position[holder.Index]);
    }
}
