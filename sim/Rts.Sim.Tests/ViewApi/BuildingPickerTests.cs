using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Rts.Sim.Vision;
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
        TestSim.Explored(sim); // M4-3b: the scene's spots and the ghost sweeps reach past the start bases' sight
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

    // M3-V3b (view hardening): a box behind a ridge is hidden: the ray meets the ridge's wall first, outside the box's
    // footprint, so PickRay answers -1; the same box seen over the ridge, or with the ridge gone, is picked.
    [Fact]
    public void PickRay_ABoxBehindARidge_IsHidden_OverTheRidgeItIsPicked()
    {
        var rows = new string[32];
        for (int y = 0; y < 32; y++) rows[y] = new string('0', 10) + (y >= 2 && y < 30 ? "11" : "00") + new string('0', 20);
        Simulation sim = GatherMaps.NewSim(LocalMovementTests.Rows(rows));
        Simulation flat = GatherMaps.NewSim(LocalMovementTests.Flat(32));
        foreach ((Simulation s, bool ridge) in new[] { (sim, true), (flat, false) })
        {
            World w = s.World;
            int slot = GatherMaps.Building(s, 16, 14).Index;
            BuildingStore b = w.Buildings;
            Vector2 c = Center(w, slot);
            var mid = new Vector3(c.X, BoxHeight / 2f, c.Y);
            // West of the ridge (cells 10-11 = 20-24 m, 4 m high), 3 m up, straight at the box's middle: through the ridge.
            var low = new Vector3(8f, 3f, c.Y);
            int lowPick = BuildingPicker.PickRay(b, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, low, mid - low, BoxHeight, SiteMin);
            Assert.Equal(ridge ? -1 : slot, lowPick);
            // From 10 m up the ray clears the ridge (4.8 m over x = 24 m) and comes down on the box.
            var high = new Vector3(8f, 10f, c.Y);
            Assert.Equal(slot, BuildingPicker.PickRay(b, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, high, mid - high, BoxHeight, SiteMin));
            // Straight down on the box: its own footprint's ground never hides it.
            Assert.Equal(slot, BuildingPicker.PickRay(b, w.Data.Buildings, w.NavGrid, w.Heightmap, 0, new Vector3(c.X, 40f, c.Y), -Vector3.UnitY, BoxHeight, SiteMin));
        }
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

    // M6-VH9 (BUG-1630): a horizontal ray from the west at height `y` over the footprint's ground, through the middle.
    private static int WestRay(World w, int slot, float y, ReadOnlySpan<float> tops, ReadOnlySpan<float> sites)
    {
        Vector2 c = Center(w, slot);
        float half = w.Data.Buildings[w.Buildings.TypeId[slot]].FootprintWidth * MapConstants.CellSize / 2f;
        var from = new Vector3(c.X - half - 0.5f, TerrainHeight.At(w.Heightmap, c.X, c.Y) + y, c.Y);
        return BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, from, Vector3.UnitX, tops, sites, SiteMin, ReadOnlySpan<bool>.Empty, out _);
    }

    // Per type the view's drawn heights: a box type's DrawnHeight (BoxHeight x scale), with `modelType` given `modelTop` as a model's fitted top.
    private static (float[] Tops, float[] Sites) Heights(World w, int modelType = -1, float modelTop = 0f)
    {
        var tops = new float[w.Data.Buildings.Length];
        var sites = new float[tops.Length];
        for (int t = 0; t < tops.Length; t++) tops[t] = sites[t] = BoxHeight * w.Data.Buildings[t].Scale;
        if (modelType >= 0) tops[modelType] = modelTop;
        return (tops, sites);
    }

    [Fact]
    public void PickRay_PerTypeHeights_AModelTypeIsPickedUpToItsFittedTop_AndNoHigher()
    {
        Simulation sim = GatherMaps.NewSim(LocalMovementTests.Flat(32));
        World w = sim.World;
        int slot = GatherMaps.Building(sim, 12, 12).Index;
        int type = w.Buildings.TypeId[slot];
        (float[] tops, float[] sites) = Heights(w, type, 8.09f);
        // BUG-1630: the 8.09 m Keep model's tower at 6 m was over the 3 m box; now it is the building.
        Vector2 c = Center(w, slot);
        float half = w.Data.Buildings[type].FootprintWidth * MapConstants.CellSize / 2f;
        Assert.Equal(-1, BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1,
            new Vector3(c.X - half - 0.5f, TerrainHeight.At(w.Heightmap, c.X, c.Y) + 6f, c.Y), Vector3.UnitX, BoxHeight, SiteMin));
        Assert.Equal(slot, WestRay(w, slot, 6f, tops, sites));
        Assert.Equal(slot, WestRay(w, slot, 8.0f, tops, sites));
        Assert.Equal(-1, WestRay(w, slot, 8.2f, tops, sites));
        // BUG-1381 item 2, the other way: a building drawn 1.5 m tall is not picked in the air above it.
        tops[type] = 1.5f;
        Assert.Equal(slot, WestRay(w, slot, 1.4f, tops, sites));
        Assert.Equal(-1, WestRay(w, slot, 2.0f, tops, sites));
        // A type past the span's end is not picked (no out-of-range read).
        Assert.Equal(-1, WestRay(w, slot, 1f, tops.AsSpan(0, type), sites.AsSpan(0, type)));
    }

    [Fact]
    public void PickRay_PerTypeHeights_AScaledBoxType_FollowsItsDrawnHeight()
    {
        Simulation sim = GatherMaps.NewSim(LocalMovementTests.Flat(32));
        World w = sim.World;
        int scaled = -1;
        for (int t = 0; t < w.Data.Buildings.Length && scaled < 0; t++)
            if (w.Data.Buildings[t].Scale > 1.01f && w.Data.Buildings[t].FootprintWidth <= 4) scaled = t;
        Assert.True(scaled >= 0, "no building type with a data scale above 1 (the Teblor's 1.25)");
        int slot = GatherMaps.Building(sim, 12, 12, type: scaled).Index;
        (float[] tops, float[] sites) = Heights(w);
        float drawn = BoxHeight * w.Data.Buildings[scaled].Scale;
        _out.WriteLine($"{w.Data.Buildings[scaled].Key}: scale {w.Data.Buildings[scaled].Scale}, drawn {drawn} m");
        Assert.Equal(slot, WestRay(w, slot, drawn - 0.1f, tops, sites));
        Assert.Equal(-1, WestRay(w, slot, drawn + 0.1f, tops, sites));
        // The single-height overload kept the 3 m box: the same ray under the drawn top missed (the old row, for the record).
        Assert.True(drawn - 0.1f > BoxHeight);
    }

    [Fact]
    public void PickRay_PerTypeHeights_ASiteIsItsRisingBox_NotTheModelsTop()
    {
        Simulation sim = GatherMaps.NewSim(LocalMovementTests.Flat(32));
        World w = sim.World;
        int keep = GatherMaps.Keep;
        int slot = TowerTests.PlaceBuilding(sim, 0, keep, 12, 12, site: true).Index;
        Assert.True(w.Buildings.UnderConstruction[slot]);
        (float[] tops, float[] sites) = Heights(w, keep, 8.09f);
        float rise = BuildingPicker.BoxRise(w.Buildings, w.Data.Buildings, slot, SiteMin);
        float drawn = sites[keep] * rise;
        Assert.Equal(slot, WestRay(w, slot, drawn * 0.5f, tops, sites));
        Assert.Equal(-1, WestRay(w, slot, drawn + 0.2f, tops, sites));
        Assert.Equal(-1, WestRay(w, slot, 6f, tops, sites));
    }

    [Fact]
    public void PickGhostRay_PerTypeHeights_ARememberedModelAtItsTop_ARememberedSiteAsItsFoundation()
    {
        Simulation sim = GatherMaps.NewSim(LocalMovementTests.Flat(32));
        World w = sim.World;
        int keep = GatherMaps.Keep;
        (float[] tops, float[] sites) = Heights(w, keep, 8.09f);
        NavGrid g = w.NavGrid;
        int anchor = 12 * g.Width + 12, siteAnchor = 12 * g.Width + 22;
        var ghosts = new BuildingGhost[4];
        var shown = new bool[4];
        ghosts[1] = new BuildingGhost(3, keep, anchor, 1);
        ghosts[2] = new BuildingGhost(5, keep, siteAnchor, 1, Site: true);
        shown[1] = shown[2] = true;
        float half = w.Data.Buildings[keep].FootprintWidth * MapConstants.CellSize / 2f;
        int Ray(int a, float y, ReadOnlySpan<float> t, ReadOnlySpan<float> s)
        {
            Vector2 c = StartBase.FootprintCenter(g, w.Data.Buildings[keep], a);
            var from = new Vector3(c.X - half - 0.5f, TerrainHeight.At(w.Heightmap, c.X, c.Y) + y, c.Y);
            return BuildingPicker.PickGhostRay(shown, ghosts, w.Data.Buildings, g, w.Heightmap, from, Vector3.UnitX, t, s, SiteMin, out _);
        }
        Assert.Equal(1, Ray(anchor, 6f, tops, sites));
        Assert.Equal(-1, Ray(anchor, 8.2f, tops, sites));
        float foundation = sites[keep] * SiteMin;
        Assert.Equal(2, Ray(siteAnchor, foundation * 0.5f, tops, sites));
        Assert.Equal(-1, Ray(siteAnchor, foundation + 0.1f, tops, sites));
        // The single-height overload: the remembered Keep's tower at 6 m was missed (BUG-1630's ghost half).
        Vector2 kc = StartBase.FootprintCenter(g, w.Data.Buildings[keep], anchor);
        Assert.Equal(-1, BuildingPicker.PickGhostRay(shown, ghosts, w.Data.Buildings, g, w.Heightmap,
            new Vector3(kc.X - half - 0.5f, 6f, kc.Y), Vector3.UnitX, BoxHeight, SiteMin, out _));
    }

    [Fact]
    public void PickRay_PickGhostRay_PerTypeHeights_AllocateZeroBytes()
    {
        (Simulation sim, _, _, _) = Base(1);
        World w = sim.World;
        (float[] tops, float[] sites) = Heights(w, w.Buildings.TypeId[HallOf(w, 0)], 8.09f);
        var ghosts = new BuildingGhost[w.Buildings.Capacity];
        var shown = new bool[w.Buildings.Capacity];
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k])
            {
                ghosts[k] = new BuildingGhost(1, w.Buildings.TypeId[k], w.Buildings.Cell[k], w.Buildings.Owner[k], w.Buildings.UnderConstruction[k]);
                shown[k] = true;
            }
        long sum = 0;
        Action block = () =>
        {
            for (int i = 0; i < 200; i++)
            {
                var o = new Vector3(i * 1.3f, 60f, i * 1.1f);
                var d = new Vector3(0.1f, -1f, -0.6f);
                sum += BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, o, d, tops, sites, SiteMin, ReadOnlySpan<bool>.Empty, out _);
                sum += BuildingPicker.PickGhostRay(shown, ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap, o, d, tops, sites, SiteMin, out _);
            }
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"per-type picks: 0 bytes (runs {runs}), checksum {sum}");
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
