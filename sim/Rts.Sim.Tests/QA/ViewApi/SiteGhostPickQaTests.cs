using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Rts.Sim.Vision;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA attacks on M6-V1 (session 2026-10-11-0634): the <see cref="BuildingPicker.PickGhostRay(System.ReadOnlySpan{bool}, System.ReadOnlySpan{BuildingGhost}, System.Collections.Immutable.ImmutableArray{Rts.Sim.Data.BuildingDef}, Rts.Sim.Map.NavGrid, Rts.Sim.Map.Heightmap, Vector3, Vector3, float, float, out float)"/>
/// overload with <c>siteShare</c> (remembered sites picked as foundations). Read-only proof with a real remembered site in
/// the loop (hash twin every tick); the new overload equals the old one for every non-site ghost and for share 1 over a
/// seeded fuzz of rays; a top-down ray enters a site foundation at its top; junk shares (0, negative, NaN, infinity)
/// never throw; 0 bytes allocated.
/// </summary>
public class SiteGhostPickQaTests
{
    private readonly ITestOutputHelper _out;

    public SiteGhostPickQaTests(ITestOutputHelper output) => _out = output;

    private static void Refresh(FogView view, World w) =>
        view.Refresh(w.Fog, w.TickNumber, w.Units.Alive, w.Buildings.Alive, w.Buildings.Generation);

    // Player 1 has a finished Tent at (30, 30) and a Tent site at (40, 30) on a flat 64 map; player 0's spotter sees both,
    // then is freed, so both become ghosts (the site one with Site set).
    private static (Simulation Sim, EntityHandle Hall, EntityHandle Site, FogView View) Scene(ulong seed = 1)
    {
        Simulation sim = Flat(size: 64, seed: seed);
        EntityHandle tent = TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 30, 30);
        EntityHandle site = TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 40, 30, site: true);
        EntityHandle scout = Spotter(sim, 0, new Vector2(70f, 62f));
        FogMaps.RunThroughNextUpdate(sim);
        World w = sim.World;
        w.Units.Free(scout);
        FogMaps.RunThroughNextUpdate(sim);
        sim.Tick();
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        Refresh(view, w);
        return (sim, tent, site, view);
    }

    [Fact]
    public void Scene_HasARealRememberedSite_AndAFinishedGhost()
    {
        (Simulation sim, EntityHandle hall, EntityHandle site, FogView view) = Scene();
        Assert.True(view.ShowsGhost(hall.Index) && view.ShowsGhost(site.Index), "both ghosts drawn");
        Assert.False(view.Ghosts[hall.Index].Site);
        Assert.True(view.Ghosts[site.Index].Site, "the sim did not remember the site as a site");
        Assert.True(sim.World.Buildings.IsAlive(site));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    public void SiteSharePick_DoesNotChangeTheSim_EveryTick(ulong seed)
    {
        (Simulation viewed, _, EntityHandle site, FogView view) = Scene(seed);
        (Simulation bare, _, _, _) = Scene(seed);
        World w = viewed.World;
        Assert.Equal(bare.StateHash(), viewed.StateHash());
        var rng = new SimRng(seed, 31);
        int hits = 0;
        for (int t = 0; t < 400; t++)
        {
            bare.Tick();
            viewed.Tick();
            Refresh(view, w);
            float x = rng.NextInt(40, 110), z = rng.NextInt(40, 80);
            float y = TerrainHeight.At(w.Heightmap, x, z);
            int hit = BuildingPicker.PickGhostRay(view.GhostShown, view.Ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap,
                new Vector3(x, y + 30f, z + 15f), new Vector3(0f, -30f, -15f), 3f, 0.15f, out _);
            if (hit == site.Index) hits++;
            BuildingPicker.PickGhostRay(view.GhostShown, view.Ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap,
                new Vector3(x, y + 30f, z), new Vector3(0f, -1f, 0f), 3f, float.NaN, out _);
            Assert.Equal(bare.StateHash(), viewed.StateHash());
        }
        _out.WriteLine($"seed {seed}: {hits} picks of the remembered site");
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(21UL)]
    public void NewOverload_EqualsOldOne_ForEveryNonSiteGhost_AndForShareOne(ulong seed)
    {
        (Simulation sim, EntityHandle hall, EntityHandle site, FogView view) = Scene(seed);
        World w = sim.World;
        BuildingGhost[] ghosts = view.Ghosts.ToArray();
        BuildingGhost[] noSites = view.Ghosts.ToArray();
        noSites[site.Index] = noSites[site.Index] with { Site = false };
        var rng = new SimRng(seed, 77);
        int hits = 0, siteDiffers = 0;
        for (int k = 0; k < 4000; k++)
        {
            var origin = new Vector3(rng.NextInt(0, 128), rng.NextInt(0, 40) + 0.37f, rng.NextInt(0, 128));
            var target = new Vector3(rng.NextInt(50, 100) + 0.5f, rng.NextInt(0, 4) * 0.5f, rng.NextInt(50, 80) + 0.5f);
            Vector3 dir = target - origin;
            if (dir == Vector3.Zero) continue;
            int oldHit = BuildingPicker.PickGhostRay(view.GhostShown, ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap, origin, dir, 3f, out float oldT);
            int one = BuildingPicker.PickGhostRay(view.GhostShown, ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap, origin, dir, 3f, 1f, out float oneT);
            Assert.Equal(oldHit, one);
            Assert.Equal(oldT, oneT);
            // With no site flags the share is ignored entirely.
            int a = BuildingPicker.PickGhostRay(view.GhostShown, noSites, w.Data.Buildings, w.NavGrid, w.Heightmap, origin, dir, 3f, 0.15f, out float aT);
            int b = BuildingPicker.PickGhostRay(view.GhostShown, noSites, w.Data.Buildings, w.NavGrid, w.Heightmap, origin, dir, 3f, out float bT);
            Assert.Equal(b, a);
            Assert.Equal(bT, aT);
            // A low share can only lose (or delay) the site, never add a pick of it, and never changes a hall pick into something else.
            int low = BuildingPicker.PickGhostRay(view.GhostShown, ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap, origin, dir, 3f, 0.15f, out float lowT);
            if (low == site.Index) Assert.Equal(site.Index, oldHit);
            if (low == hall.Index) Assert.True(oldHit == hall.Index || oldHit == site.Index, $"low {low}, old {oldHit}");
            if (low >= 0) Assert.True(lowT >= oldT - 1e-4f, $"low share entered earlier: {lowT} < {oldT}");
            if (low != oldHit) siteDiffers++;
            if (oldHit >= 0) hits++;
        }
        _out.WriteLine($"seed {seed}: {hits} hits, {siteDiffers} rays where the foundation changed the pick");
        Assert.True(hits > 100, $"only {hits} rays hit a ghost");
        Assert.True(siteDiffers > 0, "no ray was affected by the foundation height");
    }

    [Fact]
    public void TopDownRay_EntersTheFoundationAtItsTop()
    {
        (Simulation sim, _, EntityHandle site, FogView view) = Scene();
        World w = sim.World;
        BuildingGhost g = view.Ghosts[site.Index];
        var def = w.Data.Buildings[g.TypeId];
        float cs = Rts.Sim.Map.MapConstants.CellSize;
        float cx = (g.Cell % w.NavGrid.Width + def.FootprintWidth / 2f) * cs, cz = (g.Cell / w.NavGrid.Width + def.FootprintHeight / 2f) * cs;
        float baseY = TerrainHeight.At(w.Heightmap, cx, cz);
        var origin = new Vector3(cx, baseY + 10f, cz);
        int hit = BuildingPicker.PickGhostRay(view.GhostShown, view.Ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap, origin, -Vector3.UnitY, 3f, 0.15f, out float t);
        Assert.Equal(site.Index, hit);
        Assert.True(MathF.Abs(t - (10f - 0.45f)) < 1e-3f, $"entry {t}, want {10f - 0.45f}");
        int full = BuildingPicker.PickGhostRay(view.GhostShown, view.Ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap, origin, -Vector3.UnitY, 3f, out float tf);
        Assert.Equal(site.Index, full);
        Assert.True(MathF.Abs(tf - 7f) < 1e-3f, $"old overload entry {tf}, want 7");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(1e30f)]
    public void JunkShares_NeverThrow_AndNeverPickTheHallDifferently(float share)
    {
        (Simulation sim, EntityHandle hall, _, FogView view) = Scene();
        World w = sim.World;
        float baseY = TerrainHeight.At(w.Heightmap, 62f, 62f);
        // Level with the hall's middle, 1 m up, from the east: the old overload picks the hall (the site is at x 80..).
        var origin = new Vector3(62f, baseY + 1f, 120f);
        var dir = new Vector3(0f, 0f, -1f);
        int oldHit = BuildingPicker.PickGhostRay(view.GhostShown, view.Ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap, origin, dir, 3f, out _);
        int hit = BuildingPicker.PickGhostRay(view.GhostShown, view.Ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap, origin, dir, 3f, share, out float t);
        Assert.Equal(hall.Index, oldHit);
        Assert.Equal(hall.Index, hit);
        Assert.True(float.IsFinite(t));
        // Straight down onto the site with a junk share: no throw, and either a finite entry or a clean miss.
        Assert.True(sim.World.Buildings.Capacity > 0);
        for (int x = 70; x < 100; x += 3)
        {
            int h = BuildingPicker.PickGhostRay(view.GhostShown, view.Ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap,
                new Vector3(x, 30f, 62f), -Vector3.UnitY, 3f, share, out float e);
            Assert.True(h < 0 ? float.IsPositiveInfinity(e) : float.IsFinite(e), $"x {x}: hit {h} entry {e}");
        }
    }

    /// <summary>Allocation-measuring rows; they run alone in <see cref="SerialCollection"/>.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        [Fact]
        public void SiteSharePick_AllocatesNothing()
        {
            (Simulation sim, _, _, FogView view) = Scene();
            World w = sim.World;
            int sum = BuildingPicker.PickGhostRay(view.GhostShown, view.Ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap,
                new Vector3(80f, 30f, 70f), new Vector3(0f, -30f, -5f), 3f, 0.15f, out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 2000; k++)
                sum += BuildingPicker.PickGhostRay(view.GhostShown, view.Ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap,
                    new Vector3(60f + k % 40, 30f, 70f), new Vector3(0f, -30f, -5f), 3f, 0.15f, out _);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(bytes == 0, $"{bytes} bytes over 2,000 picks (sum {sum})");
        }
    }
}
