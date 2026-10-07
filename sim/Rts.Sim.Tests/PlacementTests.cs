using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-3 criterion 1: <see cref="World.CanPlace"/> gives the right reason for each rule, and <c>Build</c> applies exactly that rule.</summary>
[Collection(SerialCollection.Name)]
public class PlacementTests
{
    // 24 x 16: level 0 west, a level-1 plateau from x = 16 with a 3-wide ramp at x = 15, y 7-9.
    private static Heightmap Terrain()
    {
        var rows = new string[16];
        for (int y = 0; y < 16; y++)
            rows[y] = new string('0', 15) + (y is >= 7 and <= 9 ? "r" : "0") + new string('1', 8);
        return FromRows(rows);
    }

    private static PlacementError Reason(Simulation sim, int player, int type, int x, int y)
    {
        bool ok = sim.World.CanPlace(player, type, Cell(sim, x, y), out PlacementError reason);
        Assert.Equal(reason == PlacementError.None, ok);
        return reason;
    }

    [Fact]
    public void EveryRule_GivesItsReason_AndALegalSpotIsNone()
    {
        Simulation sim = BuildMaps.NewSim(Terrain(), players: 2);
        NavGrid g = sim.World.NavGrid;
        Assert.True((g.FlagsAt(16, 2) & NavFlags.Cliff) != 0 && (g.FlagsAt(15, 8) & NavFlags.Ramp) != 0);
        Spawn(sim.World, Tree, 5, 10, TreeWood);
        Spawn(sim.World, Tree, 1, 2, TreeWood);
        Building(sim, 8, 2);
        Unit(sim, At(sim, 3, 12), player: 1, type: Laborer);
        EntityHandle holder = Unit(sim, At(sim, 11, 12));
        sim.Enqueue(Command.HoldPosition(0, holder));
        Unit(sim, At(sim, 4, 7)); // own, not holding: pushed, not in the way
        Run(sim, 2);

        Assert.Equal(PlacementError.None, Reason(sim, 0, House, 3, 3));
        Assert.Equal(PlacementError.None, Reason(sim, 0, House, 4, 7));
        Assert.Equal(PlacementError.UnknownType, Reason(sim, 0, TestSim.Data.Buildings.Length, 3, 3));
        Assert.Equal(PlacementError.UnknownType, Reason(sim, 0, -1, 3, 3));
        Assert.Equal(PlacementError.WrongFaction, Reason(sim, 0, WhirlwindHouse, 3, 3));
        Assert.Equal(PlacementError.WrongFaction, Reason(sim, 5, House, 3, 3)); // no such player
        Assert.Equal(PlacementError.None, Reason(sim, 1, WhirlwindHouse, 3, 3));
        Assert.Equal(PlacementError.OffMap, Reason(sim, 0, House, 23, 3));
        Assert.Equal(PlacementError.OffMap, Reason(sim, 0, House, 3, 15));
        Assert.False(sim.World.CanPlace(0, House, -1, out PlacementError neg));
        Assert.Equal(PlacementError.OffMap, neg);
        Assert.Equal(PlacementError.Blocked, Reason(sim, 0, House, 15, 2));  // cliff
        Assert.Equal(PlacementError.Blocked, Reason(sim, 0, House, 14, 7));  // ramp
        Assert.Equal(PlacementError.Blocked, Reason(sim, 0, House, 15, 8));  // two levels (a ramp cell and the plateau)
        Assert.Equal(PlacementError.Blocked, Reason(sim, 0, House, 5, 9));   // tree
        Assert.Equal(PlacementError.Blocked, Reason(sim, 0, House, 9, 4));   // building
        Assert.Equal(PlacementError.Blocked, Reason(sim, 0, House, 0, 5));   // border
        Assert.Equal(PlacementError.SealsGround, Reason(sim, 0, House, 2, 1)); // walls (1, 1) in with the tree and the border
        Assert.Equal(PlacementError.UnitInTheWay, Reason(sim, 0, House, 3, 12)); // enemy
        Assert.Equal(PlacementError.UnitInTheWay, Reason(sim, 0, House, 10, 11)); // own holder
        Assert.Equal(PlacementError.UnitInTheWay, Reason(sim, 1, WhirlwindHouse, 10, 11)); // the holder is player 1's enemy
    }

    [Fact]
    public void CostAndStoreLimits_GiveTheirReasons()
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 32) with { BuildingCapacity = 1 }, Flat(30, 20));
        Assert.Equal(PlacementError.CannotAfford, Reason(sim, 0, Keep, 3, 3)); // 275 / 275 against 200 / 200
        SetTotals(sim, 0, 1000, 49);
        Assert.Equal(PlacementError.CannotAfford, Reason(sim, 0, House, 3, 3)); // 0 / 50
        SetTotals(sim, 0, 1000, 1000);
        Assert.Equal(PlacementError.None, Reason(sim, 0, House, 3, 3));
        Building(sim, 20, 10);
        Assert.Equal(PlacementError.StoreFull, Reason(sim, 0, House, 3, 3));
    }

    [Fact]
    public void CanPlace_ChangesNothing()
    {
        Simulation sim = BuildMaps.NewSim(Terrain());
        ulong before = sim.StateHash();
        for (int c = 0; c < 24 * 16; c++) sim.World.CanPlace(0, House, c, out _);
        Assert.Equal(before, sim.StateHash());
    }

    /// <summary>Criterion 1: over 2,000 random placements on 8 generated maps, a Build places a site exactly when CanPlace said None just before.</summary>
    [Theory]
    [InlineData(1UL)] [InlineData(2UL)] [InlineData(3UL)] [InlineData(4UL)]
    [InlineData(5UL)] [InlineData(6UL)] [InlineData(7UL)] [InlineData(8UL)]
    public void ABuildIsDroppedExactlyWhenCanPlaceSaysSo(ulong seed)
    {
        var config = TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 64) with { Map = MapGenParams.Default with { Forests = 12, GoldMines = 8 } };
        var sim = new Simulation(config);
        NavGrid g = sim.World.NavGrid;
        var rng = new SimRng(seed, 91);
        List<Vector2> open = FlowFieldOracle.PassableCells(g).Select(c => MoveScenario.Center(g, c)).ToList();
        var workers = new EntityHandle[2][];
        for (int p = 0; p < 2; p++)
        {
            Give(sim, p, 100_000, 100_000);
            workers[p] = Enumerable.Range(0, 12).Select(_ => Unit(sim, open[rng.NextInt(0, open.Count)], p)).ToArray();
        }
        int placed = 0, refused = 0;
        BuildingStore b = sim.World.Buildings;
        for (int k = 0; k < 250; k++)
        {
            int p = rng.NextInt(0, 2);
            EntityHandle w = workers[p][rng.NextInt(0, 12)];
            int type = rng.NextInt(0, 10) == 0 ? rng.NextInt(-1, TestSim.Data.Buildings.Length + 1) : FactionType(p, rng.NextInt(0, 10));
            // Half the anchors near a unit, so units in the way come up too.
            Vector2 near = rng.NextInt(0, 2) == 0 ? sim.World.Units.Position[workers[rng.NextInt(0, 2)][rng.NextInt(0, 12)].Index] : open[rng.NextInt(0, open.Count)];
            if (!g.WorldToCell(near + new Vector2(rng.NextInt(-3, 2), rng.NextInt(-3, 2)) * 2f, out int x, out int y)) continue;
            int anchor = y * g.Width + x;
            int site = b.SlotAt(x, y);
            if (site >= 0 && b.Cell[site] == anchor && b.TypeId[site] == type && b.Owner[site] == p && b.UnderConstruction[site]) continue; // a join, not a placement
            sim.Enqueue(Command.Build(p, w, type, g.CellCenter(x, y)));
            sim.Tick(); // the Build applies in phase 1 of the next tick: ask CanPlace on the state it will see
            bool ok = sim.World.CanPlace(p, type, anchor, out _);
            int count = b.Count, gold = sim.World.Gold[p], wood = sim.World.Wood[p];
            sim.Tick();
            Assert.True(ok == (b.Count == count + 1), $"seed {seed} placement {k}: CanPlace {ok}, count {count} -> {b.Count}");
            if (!ok) Assert.True(gold == sim.World.Gold[p] && wood == sim.World.Wood[p], "a dropped Build cost something");
            if (ok) placed++; else refused++;
            if (k % 25 == 24) Run(sim, 20); // let builders walk and some sites rise
        }
        Assert.True(placed >= 10 && refused >= 10, $"placed {placed}, refused {refused}");
    }

    private static int FactionType(int faction, int slot) =>
        TestSim.Data.Buildings.First(d => d.Faction == faction && (int)d.Slot == slot).Id;
}
