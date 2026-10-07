using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M3-V2: <see cref="BuildingPicker"/>, the right-click context order's building lookup and the left click's box pick.</summary>
[Collection(SerialCollection.Name)]
public class BuildingPickerTests
{
    private const float BoxHeight = 3f, SiteMin = 0.15f; // BuildingViews' constants (game side)

    private readonly ITestOutputHelper _out;

    public BuildingPickerTests(ITestOutputHelper output) => _out = output;

    /// <summary>The default match (halls and workers) plus, for player 0, a finished house and an infantry hall site, and a house for player 1.</summary>
    internal static (Simulation Sim, int House, int Site, int EnemyHouse) Base(ulong seed)
    {
        (Simulation sim, Vector2[][] blocks, StartBasePlan plan) = StartBaseTests.MatchSetup(seed, 20, 5);
        StartBaseTests.Apply(sim, blocks, plan);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int house0 = StartBase.BuildingOfSlot(w.Data, w.FactionOf(0), BuildingSlot.House);
        int barracks0 = StartBase.BuildingOfSlot(w.Data, w.FactionOf(0), BuildingSlot.InfantryHall);
        int house1 = StartBase.BuildingOfSlot(w.Data, w.FactionOf(1), BuildingSlot.House);
        Vector2 hall0 = StartBase.FootprintCenter(g, w.Data.Buildings[plan.HallType[0]], plan.HallAnchor[0]);
        Vector2 hall1 = StartBase.FootprintCenter(g, w.Data.Buildings[plan.HallType[1]], plan.HallAnchor[1]);
        int a = Spot(w, 0, house0, hall0, -1);
        sim.Enqueue(Command.SpawnBuilding(0, house0, g.CellCenter(a % g.Width, a / g.Width)));
        int c = Spot(w, 1, house1, hall1, -1);
        sim.Enqueue(Command.SpawnBuilding(1, house1, g.CellCenter(c % g.Width, c / g.Width)));
        sim.Tick();
        sim.Tick();
        EntityHandle worker = default;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Units.TypeId[i] == plan.WorkerType[0]) worker = new EntityHandle(i, w.Units.Generation[i]);
        int s = Spot(w, 0, barracks0, hall0, a);
        sim.Enqueue(Command.Build(0, worker, barracks0, g.CellCenter(s % g.Width, s / g.Width)));
        sim.Tick();
        sim.Tick();
        BuildingStore b = w.Buildings;
        int houseSlot = b.SlotAt(a % g.Width, a / g.Width), siteSlot = b.SlotAt(s % g.Width, s / g.Width), enemy = b.SlotAt(c % g.Width, c / g.Width);
        Assert.True(houseSlot >= 0 && !b.UnderConstruction[houseSlot]);
        Assert.True(siteSlot >= 0 && b.UnderConstruction[siteSlot]);
        Assert.True(enemy >= 0 && b.Owner[enemy] == 1);
        return (sim, houseSlot, siteSlot, enemy);
    }

    // The nearest anchor to `near` (8-30 m away) CanPlace accepts for `player`, not touching anchor `avoid`'s 3 x 3 neighbourhood.
    private static int Spot(World w, int player, int type, Vector2 near, int avoid)
    {
        NavGrid g = w.NavGrid;
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int cell = 0; cell < g.Width * g.Height; cell++)
        {
            float d = Vector2.Distance(StartBase.FootprintCenter(g, w.Data.Buildings[type], cell), near);
            if (d >= bestD || d < 8f || d > 30f) continue;
            if (avoid >= 0 && Math.Abs(cell % g.Width - avoid % g.Width) < 6 && Math.Abs(cell / g.Width - avoid / g.Width) < 6) continue;
            if (w.CanPlace(player, type, cell, out _)) (best, bestD) = (cell, d);
        }
        Assert.True(best >= 0, "no spot");
        return best;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(31UL)]
    public void SlotAt_EveryCell_EqualsAFootprintScan(ulong seed)
    {
        (Simulation sim, _, _, _) = Base(seed);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        BuildingStore b = w.Buildings;
        var owner = new int[g.Width * g.Height];
        Array.Fill(owner, -1);
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k]) continue;
            BuildingDef d = w.Data.Buildings[b.TypeId[k]];
            for (int y = b.Cell[k] / g.Width; y < b.Cell[k] / g.Width + d.FootprintHeight; y++)
                for (int x = b.Cell[k] % g.Width; x < b.Cell[k] % g.Width + d.FootprintWidth; x++) owner[y * g.Width + x] = k;
        }
        int hits = 0;
        for (int cell = 0; cell < owner.Length; cell++)
        {
            Vector2 c = g.CellCenter(cell % g.Width, cell / g.Width);
            foreach (Vector2 off in new[] { Vector2.Zero, new Vector2(0.95f, 0.95f), new Vector2(-0.95f, -0.95f) })
                Assert.True(owner[cell] == BuildingPicker.SlotAt(b, g, c + off), $"seed {seed} cell {cell} offset {off}: {BuildingPicker.SlotAt(b, g, c + off)}, want {owner[cell]}");
            if (owner[cell] >= 0) hits++;
        }
        int area = 0;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k]) area += w.Data.Buildings[b.TypeId[k]].FootprintWidth * w.Data.Buildings[b.TypeId[k]].FootprintHeight;
        Assert.True(hits == area && b.Count >= 3, $"{hits} building cells for {b.Count} buildings covering {area}");
        foreach (Vector2 bad in new[] { new Vector2(-1f, 5f), new Vector2(5f, g.Height * MapConstants.CellSize + 1f), new Vector2(float.NaN, 3f), new Vector2(3f, float.PositiveInfinity) })
            Assert.Equal(-1, BuildingPicker.SlotAt(b, g, bad));
    }

    [Fact]
    public void PickRay_StraightDown_HitsTheOwnBoxAboveEachCell_AndOnlyOwnWhenFiltered()
    {
        (Simulation sim, int house, int site, int enemy) = Base(1);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        BuildingStore b = w.Buildings;
        for (int cell = 0; cell < g.Width * g.Height; cell++)
        {
            Vector2 c = g.CellCenter(cell % g.Width, cell / g.Width);
            var origin = new Vector3(c.X, 80f, c.Y);
            int under = b.SlotAt(cell % g.Width, cell / g.Width);
            int any = BuildingPicker.PickRay(b, w.Data.Buildings, g, w.Heightmap, -1, origin, -Vector3.UnitY, BoxHeight, SiteMin);
            int own = BuildingPicker.PickRay(b, w.Data.Buildings, g, w.Heightmap, 0, origin, -Vector3.UnitY, BoxHeight, SiteMin);
            Assert.Equal(under, any);
            Assert.Equal(under >= 0 && b.Owner[under] == 0 ? under : -1, own);
        }
        Assert.True(b.Alive[house] && b.Alive[site] && b.Owner[enemy] == 1);
    }

    [Fact]
    public void PickRay_Oblique_TakesTheNearestBox_AndASiteOnlyUpToItsDrawnHeight()
    {
        (Simulation sim, int house, int site, _) = Base(1);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        BuildingStore b = w.Buildings;
        Vector2 hc = Center(w, house);
        float baseY = TerrainHeight.At(w.Heightmap, hc.X, hc.Y);
        // From a camera-like spot south and above: aimed at the box's middle it hits; aimed just over its top it misses.
        var cam = new Vector3(hc.X, baseY + 30f, hc.Y + 25f);
        var mid = new Vector3(hc.X, baseY + BoxHeight / 2f, hc.Y);
        Assert.Equal(house, BuildingPicker.PickRay(b, w.Data.Buildings, g, w.Heightmap, 0, cam, mid - cam, BoxHeight, SiteMin));
        var over = new Vector3(hc.X, baseY + BoxHeight + 0.5f, hc.Y - w.Data.Buildings[b.TypeId[house]].FootprintHeight * MapConstants.CellSize);
        Assert.NotEqual(house, BuildingPicker.PickRay(b, w.Data.Buildings, g, w.Heightmap, 0, cam, over - cam, BoxHeight, SiteMin));

        // The site is drawn at max(SiteMin, progress) of the height: a horizontal ray above that misses, below it hits.
        float rise = BuildingPicker.BoxRise(b, w.Data.Buildings, site, SiteMin);
        Assert.True(rise >= SiteMin && rise < 1f, $"site rise {rise}");
        Vector2 sc = Center(w, site);
        float sy = TerrainHeight.At(w.Heightmap, sc.X, sc.Y);
        float halfWidth = w.Data.Buildings[b.TypeId[site]].FootprintWidth * MapConstants.CellSize / 2f;
        var west = new Vector3(sc.X - halfWidth - 0.5f, sy + BoxHeight * rise * 0.5f, sc.Y);
        Assert.Equal(site, BuildingPicker.PickRay(b, w.Data.Buildings, g, w.Heightmap, 0, west, Vector3.UnitX, BoxHeight, SiteMin));
        west.Y = sy + BoxHeight * rise + 0.2f;
        Assert.NotEqual(site, BuildingPicker.PickRay(b, w.Data.Buildings, g, w.Heightmap, 0, west, Vector3.UnitX, BoxHeight, SiteMin));

        // Two boxes on one line (the hall and the house), seen from outside beyond each end: the nearer wins.
        Vector2 hall = Center(w, HallOf(w, 0));
        var a3 = new Vector3(hall.X, TerrainHeight.At(w.Heightmap, hall.X, hall.Y) + 1f, hall.Y);
        var h3 = new Vector3(hc.X, baseY + 1f, hc.Y);
        Vector3 dir = Vector3.Normalize(h3 - a3);
        Assert.Equal(HallOf(w, 0), BuildingPicker.PickRay(b, w.Data.Buildings, g, w.Heightmap, 0, a3 - dir * 12f, dir, BoxHeight, SiteMin));
        Assert.Equal(house, BuildingPicker.PickRay(b, w.Data.Buildings, g, w.Heightmap, 0, h3 + dir * 12f, -dir, BoxHeight, SiteMin));

        // Bad rays.
        Assert.Equal(-1, BuildingPicker.PickRay(b, w.Data.Buildings, g, w.Heightmap, 0, cam, Vector3.Zero, BoxHeight, SiteMin));
        Assert.Equal(-1, BuildingPicker.PickRay(b, w.Data.Buildings, g, w.Heightmap, 0, new Vector3(float.NaN, 1f, 1f), -Vector3.UnitY, BoxHeight, SiteMin));
        Assert.Equal(-1, BuildingPicker.PickRay(b, w.Data.Buildings, g, w.Heightmap, 0, cam, new Vector3(0f, 1f, 0f), BoxHeight, SiteMin));
    }

    [Fact]
    public void BoxRise_FinishedIsOne_SiteFollowsWork_DeadIsZero()
    {
        (Simulation sim, int house, int site, _) = Base(6);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        Assert.Equal(1f, BuildingPicker.BoxRise(b, w.Data.Buildings, house, SiteMin));
        float last = BuildingPicker.BoxRise(b, w.Data.Buildings, site, SiteMin);
        Assert.True(last >= SiteMin);
        for (int t = 0; t < 400 && b.UnderConstruction[site]; t++)
        {
            sim.Tick();
            if (!b.UnderConstruction[site]) break;
            float r = BuildingPicker.BoxRise(b, w.Data.Buildings, site, SiteMin);
            float want = Math.Max(SiteMin, (float)b.Work[site] / b.WorkNeeded(b.TypeId[site]));
            Assert.Equal(want, r, 5);
            Assert.True(r >= last);
            last = r;
        }
        Assert.Equal(0f, BuildingPicker.BoxRise(b, w.Data.Buildings, -1, SiteMin));
        Assert.Equal(0f, BuildingPicker.BoxRise(b, w.Data.Buildings, b.Capacity, SiteMin));
        int free = -1;
        for (int k = 0; k < b.Capacity && free < 0; k++) if (!b.Alive[k]) free = k;
        Assert.Equal(0f, BuildingPicker.BoxRise(b, w.Data.Buildings, free, SiteMin));
    }

    [Fact]
    public void SlotAt_PickRay_BoxRise_AllocateZeroBytes()
    {
        (Simulation sim, _, int site, _) = Base(1);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        BuildingStore b = w.Buildings;
        long sum = 0;
        Action block = () =>
        {
            for (int cell = 0; cell < g.Width * g.Height; cell += 7) sum += BuildingPicker.SlotAt(b, g, g.CellCenter(cell % g.Width, cell / g.Width));
            for (int i = 0; i < 200; i++)
                sum += BuildingPicker.PickRay(b, w.Data.Buildings, g, w.Heightmap, 0, new Vector3(i * 1.3f, 60f, i * 1.1f), new Vector3(0.1f, -1f, -0.6f), BoxHeight, SiteMin);
            sum += (long)(100 * BuildingPicker.BoxRise(b, w.Data.Buildings, site, SiteMin));
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"BuildingPicker: 0 bytes (runs {runs}), checksum {sum}");
    }

    private static Vector2 Center(World w, int slot) =>
        StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[w.Buildings.TypeId[slot]], w.Buildings.Cell[slot]);

    private static int HallOf(World w, int player)
    {
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == player && w.Data.Buildings[b.TypeId[k]].Slot == BuildingSlot.TownHall) return k;
        return -1;
    }
}
