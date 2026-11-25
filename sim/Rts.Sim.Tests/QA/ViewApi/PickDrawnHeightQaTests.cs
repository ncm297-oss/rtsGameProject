using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;
using Rts.Sim.Vision;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA for M6-VH9 (BUG-1630): <see cref="BuildingPicker"/>'s per-type drawn-height overloads. A site at 10 % and 90 % is
/// picked by its rising box only; two buildings on one ray, the nearer wins even when the farther is taller; degenerate
/// heights are skipped; with every span entry at the old box height the new overloads equal the old ones on thousands of
/// seeded rays; and picking leaves the state hash alone (the ViewApi read-only rule).
/// </summary>
[Collection(SerialCollection.Name)]
public class PickDrawnHeightQaTests
{
    private const float BoxHeight = 3f, SiteMin = 0.15f, KeepTop = 8.09f;

    private readonly ITestOutputHelper _out;

    public PickDrawnHeightQaTests(ITestOutputHelper output) => _out = output;

    // Per type the view's box heights (BoxHeight x scale), or `all` for every type when `useAll`.
    private static (float[] Tops, float[] Sites) Heights(World w, float all = 0f, bool useAll = false)
    {
        var tops = new float[w.Data.Buildings.Length];
        var sites = new float[tops.Length];
        for (int t = 0; t < tops.Length; t++) tops[t] = sites[t] = useAll ? all : BoxHeight * w.Data.Buildings[t].Scale;
        return (tops, sites);
    }

    private static Vector2 Centre(World w, int slot) =>
        StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[w.Buildings.TypeId[slot]], w.Buildings.Cell[slot]);

    // A horizontal ray from just west of the slot's footprint, `y` over its ground, along +X.
    private static int West(World w, int slot, float y, float[] tops, float[] sites, out float entry)
    {
        Vector2 c = Centre(w, slot);
        float half = w.Data.Buildings[w.Buildings.TypeId[slot]].FootprintWidth * MapConstants.CellSize / 2f;
        var from = new Vector3(c.X - half - 0.5f, TerrainHeight.At(w.Heightmap, c.X, c.Y) + y, c.Y);
        return BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, from, Vector3.UnitX, tops, sites, SiteMin,
            ReadOnlySpan<bool>.Empty, out entry);
    }

    [Theory]
    [InlineData(0.10f)]
    [InlineData(0.90f)]
    public void KeepSite_AtProgress_IsPickedByItsRisingBoxOnly(float progress)
    {
        Simulation sim = GatherMaps.NewSim(LocalMovementTests.Flat(32));
        World w = sim.World;
        int keep = GatherMaps.Keep;
        int slot = TowerTests.PlaceBuilding(sim, 0, keep, 12, 12, site: true).Index;
        w.Buildings.SetWork(slot, (int)(w.Buildings.WorkNeeded(keep) * progress));
        Assert.True(w.Buildings.UnderConstruction[slot]);
        (float[] tops, float[] sites) = Heights(w);
        tops[keep] = KeepTop; // the finished Keep's model top: a site never uses it
        float rise = Math.Max(SiteMin, progress), drawn = sites[keep] * rise;
        _out.WriteLine($"Keep site at {progress:P0}: rise {rise:0.###}, box {drawn:0.###} m");
        Assert.Equal(slot, West(w, slot, drawn * 0.95f, tops, sites, out _));
        Assert.Equal(-1, West(w, slot, drawn * 1.05f + 0.01f, tops, sites, out _));
        Assert.Equal(-1, West(w, slot, KeepTop - 0.1f, tops, sites, out _));
    }

    [Fact]
    public void TwoBuildingsOnOneRay_TheNearerWins_EvenWhenTheFartherIsTaller()
    {
        Simulation sim = GatherMaps.NewSim(LocalMovementTests.Flat(40));
        World w = sim.World;
        int keep = GatherMaps.Keep;
        int box = -1;
        for (int t = 0; t < w.Data.Buildings.Length && box < 0; t++)
            if (t != keep && w.Data.Buildings[t].FootprintHeight <= w.Data.Buildings[keep].FootprintHeight && w.Data.Buildings[t].FootprintWidth <= 4) box = t;
        Assert.True(box >= 0);
        int front = GatherMaps.Building(sim, 8, 14, type: box).Index;
        int back = GatherMaps.Building(sim, 20, 14, type: keep).Index;
        (float[] tops, float[] sites) = Heights(w);
        tops[keep] = KeepTop;
        // Along +X from west of the front building, at its row's centre line (the back Keep spans that row too).
        Vector2 fc = Centre(w, front), bc = Centre(w, back);
        float fHalf = w.Data.Buildings[box].FootprintWidth * MapConstants.CellSize / 2f;
        float g = TerrainHeight.At(w.Heightmap, fc.X, fc.Y);
        int Ray(float y) => BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1,
            new Vector3(fc.X - fHalf - 0.5f, g + y, fc.Y), Vector3.UnitX, tops, sites, SiteMin, ReadOnlySpan<bool>.Empty, out _);
        Assert.InRange(fc.Y, bc.Y - w.Data.Buildings[keep].FootprintHeight * MapConstants.CellSize / 2f, bc.Y + w.Data.Buildings[keep].FootprintHeight * MapConstants.CellSize / 2f);
        Assert.Equal(front, Ray(1f));
        Assert.Equal(front, Ray(tops[box] - 0.05f));
        Assert.Equal(back, Ray(tops[box] + 0.3f)); // over the front box: the Keep's tower behind it
        Assert.Equal(-1, Ray(KeepTop + 0.2f));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(0f)]
    [InlineData(-2f)]
    public void DegenerateHeights_AreNotPicked_AndDoNotThrow(float h)
    {
        Simulation sim = GatherMaps.NewSim(LocalMovementTests.Flat(32));
        World w = sim.World;
        int slot = GatherMaps.Building(sim, 12, 12).Index;
        (float[] tops, float[] sites) = Heights(w, h, useAll: true);
        Assert.Equal(-1, West(w, slot, 0.5f, tops, sites, out float entry));
        Assert.True(float.IsPositiveInfinity(entry));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(4UL)]
    public void SpansAtTheOldBoxHeight_EqualTheSingleHeightOverloads_OnSeededRays(ulong seed)
    {
        (Simulation sim, _, _, _) = BuildingPickerTests.Base(seed);
        World w = sim.World;
        (float[] tops, float[] sites) = Heights(w, BoxHeight, useAll: true);
        var ghosts = new BuildingGhost[w.Buildings.Capacity];
        var shown = new bool[w.Buildings.Capacity];
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k])
            {
                ghosts[k] = new BuildingGhost(1, w.Buildings.TypeId[k], w.Buildings.Cell[k], w.Buildings.Owner[k], w.Buildings.UnderConstruction[k]);
                shown[k] = true;
            }
        // Rays aimed at points round each live building from camera-like origins (an LCG: no System.Random needed).
        uint s = (uint)seed * 2654435761u + 1u;
        float Next() { s = s * 1664525u + 1013904223u; return (s >> 8) / 16777216f; }
        int hits = 0, ghostHits = 0, rays = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++)
        {
            if (!w.Buildings.Alive[k]) continue;
            Vector2 c = Centre(w, k);
            float g = TerrainHeight.At(w.Heightmap, c.X, c.Y);
            for (int r = 0; r < 600; r++, rays++)
            {
                var target = new Vector3(c.X + (Next() - 0.5f) * 16f, g + Next() * 5f, c.Y + (Next() - 0.5f) * 16f);
                var origin = target + new Vector3((Next() - 0.5f) * 30f, 10f + Next() * 50f, 10f + Next() * 40f);
                Vector3 d = target - origin;
                int owner = r % 3 == 0 ? 0 : -1;
                int a = BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, owner, origin, d, BoxHeight, SiteMin, ReadOnlySpan<bool>.Empty, out float ea);
                int b = BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, owner, origin, d, tops, sites, SiteMin, ReadOnlySpan<bool>.Empty, out float eb);
                Assert.Equal(a, b);
                Assert.Equal(ea, eb);
                int ga = BuildingPicker.PickGhostRay(shown, ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap, origin, d, BoxHeight, SiteMin, out float gea);
                int gb = BuildingPicker.PickGhostRay(shown, ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap, origin, d, tops, sites, SiteMin, out float geb);
                Assert.Equal(ga, gb);
                Assert.Equal(gea, geb);
                if (a >= 0) hits++;
                if (ga >= 0) ghostHits++;
            }
        }
        _out.WriteLine($"seed {seed}: {rays} rays, {hits} building hits, {ghostHits} ghost hits, old == new on every ray");
        Assert.True(hits > rays / 10 && ghostHits > rays / 10, "the rays mostly missed: the comparison would prove little");
    }

    [Fact]
    public void PerTypePicks_LeaveTheStateHashAlone()
    {
        (Simulation a, _, _, _) = BuildingPickerTests.Base(2);
        (Simulation b, _, _, _) = BuildingPickerTests.Base(2);
        World w = a.World;
        (float[] tops, float[] sites) = Heights(w);
        for (int t = 0; t < tops.Length; t++) tops[t] = 8.09f;
        var ghosts = new BuildingGhost[w.Buildings.Capacity];
        var shown = new bool[w.Buildings.Capacity];
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k])
            {
                ghosts[k] = new BuildingGhost(1, w.Buildings.TypeId[k], w.Buildings.Cell[k], w.Buildings.Owner[k], true);
                shown[k] = true;
            }
        Assert.Equal(b.StateHash(), a.StateHash());
        long sum = 0;
        for (int tick = 0; tick < 120; tick++)
        {
            for (int i = 0; i < 50; i++)
            {
                var o = new Vector3(i * 2.1f + tick * 0.3f, 55f, i * 1.7f + 40f);
                var d = new Vector3(0.05f, -1f, -0.7f);
                sum += BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, o, d, tops, sites, SiteMin, ReadOnlySpan<bool>.Empty, out _);
                sum += BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, 0, o, d, tops, sites, SiteMin, w.Buildings.Alive, out _);
                sum += BuildingPicker.PickGhostRay(shown, ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap, o, d, tops, sites, SiteMin, out _);
            }
            a.Tick();
            b.Tick();
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        _out.WriteLine($"120 ticks with 150 picks each: hashes equal throughout (checksum {sum})");
    }
}
