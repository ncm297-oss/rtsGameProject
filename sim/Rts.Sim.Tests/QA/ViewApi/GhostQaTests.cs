using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Rts.Sim.Vision;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA attacks on M4-V5 (session 2026-10-09-0724): <see cref="FogView"/>'s ghosts (CollectGhosts, GhostHandle) and
/// <see cref="BuildingPicker.PickGhostRay"/> against the sim's last-known list under a seeded fuzz of scouting, building,
/// destroying and slot reuse (two seeds, every tick); an Attack on the ghost of a building already gone (the unit walks
/// there, the order ends when the ground is seen); a building killed in plain sight (BUG-0310); 0 bytes at 2,000 units;
/// and the read-only proof with the pick in the loop.
/// </summary>
public class GhostQaTests
{
    private readonly ITestOutputHelper _out;

    public GhostQaTests(ITestOutputHelper output) => _out = output;

    private static void Refresh(FogView view, World w) =>
        view.Refresh(w.Fog, w.TickNumber, w.Units.Alive, w.Buildings.Alive, w.Buildings.Generation);

    // The independent rule (docs/03 "Implementation (M4-V5)"): a known entry whose building isn't the one drawn in its slot now.
    private static bool Unseen(World w, int player, int slot)
    {
        BuildingGhost g = w.Fog.Ghosts(player)[slot];
        return g.Known && !(w.Fog.CanSeeBuilding(player, slot) && w.Buildings.Generation[slot] == g.Generation);
    }

    private static bool AnyFootprintCellVisible(World w, int player, int type, int anchor)
    {
        var def = w.Data.Buildings[type];
        int width = w.NavGrid.Width;
        for (int dy = 0; dy < def.FootprintHeight; dy++)
            for (int dx = 0; dx < def.FootprintWidth; dx++)
                if (w.Fog.IsVisible(player, anchor + dy * width + dx)) return true;
        return false;
    }

    private static bool AnyFootprintCellExplored(World w, int player, int type, int anchor)
    {
        var def = w.Data.Buildings[type];
        int width = w.NavGrid.Width;
        for (int dy = 0; dy < def.FootprintHeight; dy++)
            for (int dx = 0; dx < def.FootprintWidth; dx++)
                if (w.Fog.IsExplored(player, anchor + dy * width + dx)) return true;
        return false;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    public void Ghosts_EqualTheSimsUnseenEntries_EveryTick_FuzzedScoutsBuildsKillsAndSlotReuse(ulong seed)
    {
        // A flat 96 map. Player 1 holds a field of Tents that is built, destroyed and rebuilt at random; player 0 has four
        // scouts sent to random points. Buildings are freed both in and out of sight and slots are reused by either player.
        Simulation sim = Flat(size: 96, units: 64, seed: seed);
        World w = sim.World;
        var rng = new SimRng(seed, 7919);
        var scouts = new List<EntityHandle>();
        for (int k = 0; k < 4; k++) scouts.Add(Spotter(sim, 0, new Vector2(10f + 4f * k, 10f)));
        foreach (EntityHandle s in scouts) w.Units.Hold[s.Index] = false;
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        var view1 = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 1);
        int ghostTicks = 0, maxGhosts = 0, reused = 0, killedUnseen = 0, picks = 0;
        var lastGen = new int[w.Buildings.Capacity];
        for (int t = 0; t < 3000; t++)
        {
            if (t % 120 == 0)
                foreach (EntityHandle s in scouts)
                    if (w.Units.IsAlive(s)) sim.Enqueue(Command.Move(0, s, new Vector2(rng.NextInt(4, 188), rng.NextInt(4, 188))));
            if (t % 25 == 0)
            {
                int owner = rng.NextInt(0, 4) == 0 ? 0 : 1;
                int x = rng.NextInt(4, 88), y = rng.NextInt(4, 88);
                if (w.CanPlace(owner, TowerTests.Tent, y * w.NavGrid.Width + x, out _) || owner == 1)
                    w.Buildings.Spawn(owner, TowerTests.Tent, y * w.NavGrid.Width + x, out _, rng.NextInt(0, 3) == 0);
            }
            if (t % 37 == 0)
            {
                int slot = rng.NextInt(0, w.Buildings.Capacity);
                if (w.Buildings.Alive[slot])
                {
                    if (w.Buildings.Owner[slot] == 1 && !w.Fog.CanSeeBuilding(0, slot)) killedUnseen++;
                    w.Buildings.Free(new EntityHandle(slot, w.Buildings.Generation[slot]));
                }
            }
            sim.Tick();
            Refresh(view, w);
            Refresh(view1, w);
            int want = 0;
            for (int i = 0; i < w.Buildings.Capacity; i++)
            {
                bool unseen = Unseen(w, 0, i);
                Assert.True(unseen == view.ShowsGhost(i), $"seed {seed} tick {sim.TickNumber}: slot {i} ghost {view.ShowsGhost(i)}, unseen entry {unseen}");
                Assert.True(Unseen(w, 1, i) == view1.ShowsGhost(i), $"seed {seed} tick {sim.TickNumber}: player 1 slot {i}");
                if (unseen)
                {
                    want++;
                    BuildingGhost g = view.Ghosts[i];
                    Assert.Equal(w.Fog.Ghosts(0)[i], g);
                    Assert.Equal(new EntityHandle(i, g.Generation), view.GhostHandle(i));
                    Assert.NotEqual(0, g.Owner); // never a ghost of one's own building
                    Assert.True(AnyFootprintCellExplored(w, 0, g.TypeId, g.Cell), $"seed {seed} tick {sim.TickNumber}: ghost {i} on wholly unexplored ground");
                }
                else Assert.Equal(default, view.GhostHandle(i));
                if (w.Buildings.Alive[i] && lastGen[i] != 0 && lastGen[i] != w.Buildings.Generation[i] && unseen) reused++;
                if (w.Buildings.Alive[i]) lastGen[i] = w.Buildings.Generation[i];
            }
            Assert.Equal(want, view.GhostCount);
            if (want > 0) ghostTicks++;
            maxGhosts = Math.Max(maxGhosts, want);
            // The pick: each drawn ghost is hit by a ray straight down onto its footprint centre (no other ghost is taller).
            if (t % 50 == 0)
                for (int i = 0; i < w.Buildings.Capacity; i++)
                {
                    if (!view.ShowsGhost(i)) continue;
                    BuildingGhost g = view.Ghosts[i];
                    Vector2 c = StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[g.TypeId], g.Cell);
                    int hit = BuildingPicker.PickGhostRay(view.GhostShown, view.Ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap,
                        new Vector3(c.X, 50f, c.Y), new Vector3(0f, -1f, 0f), 3f, out float entry);
                    Assert.True(hit >= 0 && float.IsFinite(entry), $"seed {seed} tick {sim.TickNumber}: ghost {i} at {c} not picked");
                    picks++;
                }
        }
        _out.WriteLine($"seed {seed}: {ghostTicks} ticks with ghosts (max {maxGhosts}), {reused} slot-ticks reused under a ghost, {killedUnseen} killed unseen, {picks} picks");
        Assert.True(ghostTicks > 500 && maxGhosts >= 3 && killedUnseen > 0 && reused > 0 && picks > 20,
            $"seed {seed}: the fuzz proved little ({ghostTicks} ghost ticks, max {maxGhosts}, {killedUnseen} killed unseen, {reused} reused, {picks} picks)");
    }

    [Fact]
    public void AttackOnTheGhostOfAGoneBuilding_UnitWalksThere_OrderEndsWhenTheGroundIsSeen()
    {
        Simulation sim = Flat(size: 96);
        World w = sim.World;
        EntityHandle tent = TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 70, 30);
        EntityHandle scout = Spotter(sim, 0, new Vector2(135f, 62f));
        FogMaps.RunThroughNextUpdate(sim);
        w.Units.Free(scout);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.True(w.Buildings.Free(tent)); // destroyed unseen
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        sim.Tick();
        Refresh(view, w);
        Assert.True(view.ShowsGhost(tent.Index));
        EntityHandle handle = view.GhostHandle(tent.Index);
        Assert.Equal(tent, handle);
        EntityHandle hi = Spawn(sim, (0, HeavyInfantry, new Vector2(20f, 62f)))[0];
        sim.Enqueue(Command.Attack(0, hi, handle, isBuilding: true));
        sim.Tick();
        sim.Tick(); // applied in phase 1, taken in a later phase 7 (as TowerGhostQaTests)
        Assert.Equal(handle, w.Units.Target[hi.Index]);
        Vector2 centre = StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[TowerTests.Tent], 30 * 96 + 70);
        float start = Vector2.Distance(w.Units.Position[hi.Index], centre);
        int endedAt = -1, ghostGoneAt = -1, seenAt = -1;
        for (int t = 0; t < 1200 && (endedAt < 0 || ghostGoneAt < 0); t++)
        {
            sim.Tick();
            Refresh(view, w);
            if (seenAt < 0 && AnyFootprintCellVisible(w, 0, TowerTests.Tent, 30 * 96 + 70)) seenAt = sim.TickNumber;
            if (endedAt < 0 && w.Units.Target[hi.Index] != handle) endedAt = sim.TickNumber;
            if (ghostGoneAt < 0 && !view.ShowsGhost(tent.Index)) ghostGoneAt = sim.TickNumber;
        }
        float end = Vector2.Distance(w.Units.Position[hi.Index], centre);
        _out.WriteLine($"start {start:F1} m, end {end:F1} m; ground seen {seenAt}, order ended {endedAt}, ghost gone {ghostGoneAt}");
        Assert.True(endedAt > 0 && ghostGoneAt > 0 && seenAt > 0, $"order ended {endedAt}, ghost gone {ghostGoneAt}, ground seen {seenAt}");
        Assert.True(end < start - 30f, $"the unit didn't walk to the ghost ({start} -> {end} m)");
        Assert.True(ghostGoneAt - seenAt <= VisionConstants.UpdateInterval, $"ghost lingered {ghostGoneAt - seenAt} ticks after the ground was seen");
        Assert.True(endedAt <= ghostGoneAt + 1, $"order outlived the ghost ({endedAt} vs {ghostGoneAt})");
        // Once known gone, a fresh Attack on the same handle is dropped (no ghost to name).
        sim.Enqueue(Command.Attack(0, hi, handle, isBuilding: true));
        sim.Tick();
        sim.Tick();
        Assert.NotEqual(handle, w.Units.Target[hi.Index]);
    }

    /// <summary>
    /// BUG-0310: docs/02 draws ghosts "in explored fog". A building that dies in plain sight keeps its last-known entry
    /// until the next fog update (up to 4 ticks), and CollectGhosts marks it a ghost meanwhile: a darkened box flashes
    /// over visible ground where the building just died, and the TargetRing keeps marking it.
    /// </summary>
    [Fact(Skip = "BUG-0310: a building destroyed in sight is drawn as a ghost over visible ground until the next fog update")]
    public void ABuildingDestroyedInSight_IsNeverAGhostOverVisibleGround()
    {
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        EntityHandle tent = TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 30, 30);
        Spotter(sim, 0, new Vector2(55f, 62f));
        FogMaps.RunThroughNextUpdate(sim); // seen: entry known, tick % 4 == 2 now
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        Refresh(view, w);
        Assert.False(view.ShowsGhost(tent.Index));
        Assert.True(w.Buildings.Free(tent)); // dies in sight between updates
        int flashTicks = 0;
        for (int t = 0; t < 8; t++)
        {
            sim.Tick();
            Refresh(view, w);
            if (view.ShowsGhost(tent.Index) && AnyFootprintCellVisible(w, 0, TowerTests.Tent, 30 * 64 + 30)) flashTicks++;
        }
        Assert.True(flashTicks == 0, $"a ghost drawn over visible ground for {flashTicks} ticks after the building died in sight");
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    public void GhostCalls_DoNotChangeTheSim(ulong seed)
    {
        Simulation bare = Flat(size: 64, seed: seed), viewed = Flat(size: 64, seed: seed);
        var scouts = new EntityHandle[2];
        Simulation[] both = { bare, viewed };
        for (int k = 0; k < 2; k++)
        {
            TowerTests.PlaceBuilding(both[k], 1, TowerTests.Tent, 30, 30);
            scouts[k] = Spotter(both[k], 0, new Vector2(55f, 62f));
            both[k].World.Units.Hold[scouts[k].Index] = false;
        }
        World w = viewed.World;
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        int ghosts = 0;
        for (int t = 0; t < 600; t++)
        {
            if (t % 100 == 0)
            {
                Vector2 goal = t / 100 % 2 == 0 ? new Vector2(8f, 62f) : new Vector2(54f, 62f);
                bare.Enqueue(Command.Move(0, scouts[0], goal));
                viewed.Enqueue(Command.Move(0, scouts[1], goal));
            }
            bare.Tick();
            viewed.Tick();
            Refresh(view, w);
            ghosts += view.CollectGhosts(w.Fog, w.Buildings.Generation);
            BuildingPicker.PickGhostRay(view.GhostShown, view.Ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap,
                new Vector3(62f, 40f, 82f), new Vector3(0f, -40f, -20f), 3f, out _);
            view.GhostHandle(1);
            Assert.Equal(bare.StateHash(), viewed.StateHash());
        }
        Assert.True(ghosts > 0, "no ghost was ever collected");
    }

    /// <summary>Allocation-measuring rows; they run alone in <see cref="SerialCollection"/>.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Fact]
        public void CollectGhostsAndPick_AllocateNothing_At2000Units_WithGhostsDrawn()
        {
            Simulation sim = Flat(size: 160, units: 2048);
            World w = sim.World;
            var tents = new List<EntityHandle>();
            for (int k = 0; k < 8; k++) tents.Add(TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 20 + 15 * k, 140));
            var spotters = new List<EntityHandle>();
            for (int k = 0; k < 8; k++) spotters.Add(Spotter(sim, 0, new Vector2(42f + 30f * k, 275f)));
            for (int i = 0; spotters.Count + i < 2000; i++)
                Place(sim, i % 2, HeavyInfantry, new Vector2(10f + i % 140 * 2f, 10f + i / 140 * 3f));
            FogMaps.RunThroughNextUpdate(sim);
            foreach (EntityHandle s in spotters) w.Units.Free(s);
            FogMaps.RunThroughNextUpdate(sim);
            for (int k = 0; k < 4; k++) w.Buildings.Free(tents[k]); // half gone unseen
            var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
            for (int t = 0; t < 8; t++) { sim.Tick(); Refresh(view, w); view.CollectGhosts(w.Fog, w.Buildings.Generation); }
            Assert.Equal(8, view.GhostCount);
            Assert.True(w.Units.Count >= 1990, $"{w.Units.Count} units");
            long bytes = 0;
            int sum = 0;
            for (int t = 0; t < 40; t++)
            {
                sim.Tick();
                long before = GC.GetAllocatedBytesForCurrentThread();
                Refresh(view, w);
                sum += view.CollectGhosts(w.Fog, w.Buildings.Generation);
                sum += BuildingPicker.PickGhostRay(view.GhostShown, view.Ghosts, w.Data.Buildings, w.NavGrid, w.Heightmap,
                    new Vector3(45f, 60f, 300f), new Vector3(0f, -60f, -20f), 3f, out _);
                sum += view.GhostHandle(tents[0].Index).Generation;
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            _out.WriteLine($"{w.Units.Count} units, {view.GhostCount} ghosts: {bytes} bytes over 40 ticks (sum {sum})");
            Assert.Equal(0, bytes);
        }
    }
}
