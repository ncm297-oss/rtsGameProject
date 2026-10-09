using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M3-V2 read-only proof: every new view read (<see cref="BuildingPicker"/>, <see cref="PlacementGhost"/>, <see cref="BuildMenu"/>) and the ghost's <c>World.CanPlace</c>, every tick, never change the state hash.</summary>
public class CommandCardHashTwinTests
{
    private const float BoxHeight = 3f, SiteMin = 0.15f;

    [Fact]
    public void CommandCardReadsEveryTick_HashEqualsABareTwin_400Ticks()
    {
        (Simulation a, int house, int site, int enemy) = BuildingPickerTests.Base(1);
        (Simulation b, _, _, _) = BuildingPickerTests.Base(1);
        // M4-3b: both twins explored, so the ghost's sweep round the house (up to 22 m off) meets green spots, as before.
        TestSim.Explored(a);
        TestSim.Explored(b);
        World w = a.World;
        NavGrid g = w.NavGrid;
        BuildingStore bs = w.Buildings;
        Assert.Equal(b.StateHash(), a.StateHash());

        // Player 0's workers: three join the site (a right-click on it), one repairs the house after it is damaged,
        // one places a house of its own (a green ghost click), cancelled later through the site's Cancel.
        var workers = new List<EntityHandle>();
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker) workers.Add(new EntityHandle(i, w.Units.Generation[i]));
        Assert.True(workers.Count >= 5);
        int houseType = StartBase.BuildingOfSlot(w.Data, w.FactionOf(0), BuildingSlot.House);
        Vector2 siteAnchor = PlacementGhost.AnchorPoint(g, bs.Cell[site]);
        Vector2 housePoint = StartBase.FootprintCenter(g, w.Data.Buildings[bs.TypeId[house]], bs.Cell[house]);
        int ghostAnchor = -1;
        for (int cell = 0; cell < g.Width * g.Height && ghostAnchor < 0; cell += 3)
        {
            if (Vector2.Distance(g.CellCenter(cell % g.Width, cell / g.Width), housePoint) > 40f) continue;
            if (Vector2.Distance(g.CellCenter(cell % g.Width, cell / g.Width), housePoint) < 12f) continue;
            if (w.CanPlace(0, houseType, cell, out _)) ghostAnchor = cell;
        }
        Assert.True(ghostAnchor >= 0);
        Vector2 ghostPoint = PlacementGhost.AnchorPoint(g, ghostAnchor);
        var orders = new List<Command>
        {
            Command.Build(0, workers[0], bs.TypeId[site], siteAnchor), Command.Build(0, workers[1], bs.TypeId[site], siteAnchor),
            Command.Build(0, workers[2], bs.TypeId[site], siteAnchor, queued: true), Command.Build(0, workers[3], houseType, ghostPoint),
        };
        foreach (Command c in orders)
        {
            a.Enqueue(c);
            b.Enqueue(c);
        }

        var types = new int[15];
        BuildingSlot[] basic = { BuildingSlot.House, BuildingSlot.Camp, BuildingSlot.InfantryHall, BuildingSlot.RangedHall, BuildingSlot.ShockHall, BuildingSlot.Forge };
        long sink = 0;
        int sawSite = 0, green = 0, red = 0;
        for (int tick = 0; tick < 400; tick++)
        {
            if (tick == 30)
            {
                // Damage the house in both twins alike, then send a repairer.
                a.World.Buildings.Damage(a.World.Buildings.HandleOf(house), 200);
                b.World.Buildings.Damage(b.World.Buildings.HandleOf(house), 200);
                a.Enqueue(Command.Repair(0, workers[4], housePoint));
                b.Enqueue(Command.Repair(0, workers[4], housePoint));
            }
            if (tick == 250)
            {
                a.Enqueue(Command.Cancel(0, ghostPoint + new Vector2(1f, 1f)));
                b.Enqueue(Command.Cancel(0, ghostPoint + new Vector2(1f, 1f)));
            }
            ulong before = a.StateHash();
            // The card's reads: a stripe of cell lookups (right-click), box picks (left click), rises (pick heights),
            // the ghost's anchor for a sweep of cursor points and its one CanPlace, the menus.
            for (int cell = tick % 11; cell < g.Width * g.Height; cell += 11)
            {
                int s = BuildingPicker.SlotAt(bs, g, g.CellCenter(cell % g.Width, cell / g.Width));
                if (s >= 0 && bs.UnderConstruction[s]) sawSite++;
                sink += s;
            }
            for (int r = 0; r < 20; r++)
            {
                var o = new Vector3(housePoint.X + r - 10f, 50f, housePoint.Y + 30f);
                sink += BuildingPicker.PickRay(bs, w.Data.Buildings, g, w.Heightmap, r % 3 - 1, o, new Vector3(0f, -1f, -0.8f), BoxHeight, SiteMin);
            }
            for (int k = 0; k < bs.Capacity; k++) sink += (long)(BuildingPicker.BoxRise(bs, w.Data.Buildings, k, SiteMin) * 100);
            int anchor = PlacementGhost.Anchor(g, w.Data.Buildings[houseType], housePoint + new Vector2(tick % 40 - 20, tick % 17 - 8));
            if (anchor >= 0)
            {
                if (w.CanPlace(0, houseType, anchor, out PlacementError why)) green++;
                else red++;
                sink += (int)why;
            }
            sink += BuildMenu.Entries(w.Data.Buildings, w.FactionOf(0), basic, types) + types[0];
            Assert.Equal(before, a.StateHash());

            a.Tick();
            b.Tick();
            Assert.True(b.StateHash() == a.StateHash(), $"tick {a.TickNumber}: view-read sim diverged from its twin");
        }
        Assert.True(sink != 0);
        Assert.True(sawSite > 0 && green > 0 && red > 0, $"site seen {sawSite}, green {green}, red {red}");
        Assert.True(bs.Alive[enemy]);
    }
}
