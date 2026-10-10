using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA attacks on M4-VH2 (session 2026-10-10-0624): the Shift-queued cast picks (BUG-0370: three clicks with two mages, a
/// caster dying mid-queue, a caster on cooldown, clicks spread over the apply window), <see cref="SeenResources"/> against
/// an independent last-seen rule while trees fall in and out of sight, and the read-only proof (hash twin) for the helpers
/// the M4-V6b twin doesn't cover: <see cref="SeenResources"/>, the <see cref="PropLayout"/> version overload and
/// <see cref="MinimapRaster.DrawnEnemyDotAt"/>.
/// </summary>
[Collection(SerialCollection.Name)]
public class ViewHardening2QaTests
{
    private readonly ITestOutputHelper _out;

    public ViewHardening2QaTests(ITestOutputHelper output) => _out = output;

    private static int Telas => TestSim.Data.FindAbility("telas_fire");

    // Mirrors SelectionController.AbilityOrder: pick with the sent memory, enqueue, note for TickNumber + 1.
    private static int Click(Simulation sim, EntityHandle[] sel, SentCasts sent, Vector2 p, bool queued)
    {
        UnitStore u = sim.World.Units;
        int tick = sim.World.TickNumber;
        int c = AbilityCaster.PickCaster(sel, u.Alive, u.Generation, u.TypeId, u.Position, u.CastAbility, u.AbilityReadyTick,
            u.QueueCount, u.QueueKind, u.QueueTypeId, sent.For(tick, Telas), sim.World.Data.Units, Telas, tick, p, queued, out int k);
        if (c < 0) return -1;
        var h = new EntityHandle(c, u.Generation[c]);
        sim.Enqueue(Command.UseAbility(0, h, k, p, queued));
        sent.Note(tick + 1, Telas, h);
        return c;
    }

    private static (int A, int B) RunResolves(Simulation sim, EntityHandle a, EntityHandle b, int ticks = 600)
    {
        int ra = 0, rb = 0;
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            if (Had(sim, a, resolved: true)) ra++;
            if (Had(sim, b, resolved: true)) rb++;
        }
        return (ra, rb);
    }

    private static (Simulation Sim, EntityHandle A, EntityHandle B) Walking()
    {
        Simulation sim = NoFights();
        EntityHandle a = Place(sim, 0, Mage, At(sim, 10, 20)), b = Place(sim, 0, Mage, At(sim, 10, 26));
        sim.Enqueue(Command.Move(0, a, At(sim, 40, 20)));
        sim.Enqueue(Command.Move(0, b, At(sim, 40, 26)));
        sim.Tick();
        sim.Tick();
        Assert.Equal((UnitState.Moving, UnitState.Moving), (sim.World.Units.State[a.Index], sim.World.Units.State[b.Index]));
        return (sim, a, b);
    }

    /// <summary>For every gap between clicks (same tick, one tick, two, ten), three Shift clicks with two walking mages: the first two go to different mages, the third is refused (nothing enqueued), and exactly one cast per mage resolves.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void ThreeShiftClicks_TwoWalkingMages_OneCastEach_ThirdRefused_NoneLost(int gap)
    {
        (Simulation sim, EntityHandle a, EntityHandle b) = Walking();
        var sel = new[] { a, b };
        var sent = new SentCasts();
        int[] picks = new int[3];
        int pendingBefore = 0;
        for (int c = 0; c < 3; c++)
        {
            if (c == 2) pendingBefore = sim.PendingCommandCount;
            picks[c] = Click(sim, sel, sent, At(sim, 30, 21 + c), queued: true);
            for (int t = 0; t < gap; t++) sim.Tick();
        }
        _out.WriteLine($"gap {gap}: picks {picks[0]}, {picks[1]}, {picks[2]}");
        Assert.NotEqual(-1, picks[0]);
        Assert.NotEqual(-1, picks[1]);
        Assert.NotEqual(picks[0], picks[1]);
        Assert.Equal(-1, picks[2]);
        if (gap == 0) Assert.Equal(pendingBefore, sim.PendingCommandCount); // the refused click sent nothing
        Assert.Equal((1, 1), RunResolves(sim, a, b));
    }

    /// <summary>A mage with a Shift-queued cast dies mid-queue: the next Shift click goes to the live one, never the dead slot (also not through the sent memory), and that cast resolves.</summary>
    [Fact]
    public void ACasterDyingMidQueue_IsNeverPicked_TheOtherCasts()
    {
        (Simulation sim, EntityHandle a, EntityHandle b) = Walking();
        var sel = new[] { a, b };
        var sent = new SentCasts();
        // a is nearer: it takes the first click.
        Assert.Equal(a.Index, Click(sim, sel, sent, At(sim, 30, 20), queued: true));
        sim.Tick();
        sim.Tick();
        Assert.True(AbilityCaster.HasQueuedCast(sim.World.Units.QueueCount, sim.World.Units.QueueKind, sim.World.Units.QueueTypeId, a.Index, 0));
        sim.World.Units.Free(a);
        // Dead and its queue cleared or not, a plain and a queued click both go to b (the nearest live ready one).
        Assert.Equal(b.Index, Click(sim, sel, sent, At(sim, 30, 20), queued: true));
        Assert.Equal(-1, Click(sim, sel, sent, At(sim, 30, 20), queued: true)); // b now sent: no third caster
        Assert.Equal((0, 1), RunResolves(sim, a, b));
    }

    /// <summary>A mage on cooldown (just resolved) is never picked for a Shift click: with two, the other takes it; alone, the click is refused (so no cast is queued for the sim to drop when it pops on cooldown).</summary>
    [Fact]
    public void ACasterOnCooldown_IsPassedOver_AloneTheQueuedClickIsRefused()
    {
        Simulation sim = NoFights();
        EntityHandle a = Place(sim, 0, Mage, At(sim, 10, 20)), b = Place(sim, 0, Mage, At(sim, 10, 30));
        var sent = new SentCasts();
        Assert.Equal(a.Index, Click(sim, new[] { a }, sent, At(sim, 12, 20), queued: false));
        TickOf(sim, a, resolved: true);
        Assert.True(AbilityCaster.CooldownLeft(sim.World.Units.AbilityReadyTick, a.Index, 0, sim.World.TickNumber) > 0);
        Assert.Equal(-1, Click(sim, new[] { a }, sent, At(sim, 12, 21), queued: true));
        Assert.Equal(-1, Click(sim, new[] { a }, sent, At(sim, 12, 21), queued: false));
        Assert.Equal(b.Index, Click(sim, new[] { a, b }, sent, At(sim, 12, 21), queued: true));
        Assert.Equal((0, 1), RunResolves(sim, a, b));
    }

    /// <summary>The sent memory is per handle: a recycled slot (new generation) is a different caster, not busy.</summary>
    [Fact]
    public void SentMemory_IsPerGeneration_ARecycledSlotIsFree()
    {
        Simulation sim = NoFights();
        EntityHandle a = Place(sim, 0, Mage, At(sim, 10, 20));
        var sent = new SentCasts();
        Assert.Equal(a.Index, Click(sim, new[] { a }, sent, At(sim, 12, 20), queued: true));
        sim.World.Units.Free(a);
        EntityHandle again = Place(sim, 0, Mage, At(sim, 10, 20));
        Assert.Equal(a.Index, again.Index);
        Assert.NotEqual(a.Generation, again.Generation);
        Assert.Equal(again.Index, Click(sim, new[] { again }, sent, At(sim, 12, 20), queued: true));
    }

    // Trees on a 64 x 64 map, a scout walking around: player 0 sees some, has explored all.
    private static (Simulation Sim, EntityHandle Scout, List<EntityHandle> Trees) Forest(ulong seed)
    {
        Simulation sim = TestSim.Explored(ResourceMaps.NewSim(ResourceMaps.Flat(64, 64), units: 8, seed: seed));
        World w = sim.World;
        var trees = new List<EntityHandle>();
        var rng = new Random((int)seed);
        for (int k = 0; k < 120; k++)
        {
            int x = rng.Next(2, 61), y = rng.Next(2, 61);
            if (w.Resources.Spawn(ResourceMaps.Tree, y * 64 + x, 50, out EntityHandle h)) trees.Add(h);
        }
        if (w.Resources.Spawn(ResourceMaps.Mine, 30 * 64 + 30, 500, out EntityHandle mine)) trees.Add(mine);
        EntityHandle scout = Place(sim, 0, Laborer, At(sim, 8, 8));
        sim.World.Fog.Update();
        return (sim, scout, trees);
    }

    private static bool Sees(World w, int type, int anchor)
    {
        var def = w.Data.Resources[type];
        int ax = anchor % 64, ay = anchor / 64;
        for (int y = ay; y < ay + def.FootprintHeight; y++)
            for (int x = ax; x < ax + def.FootprintWidth; x++)
                if (w.Fog.IsVisible(0, y * 64 + x)) return true;
        return false;
    }

    /// <summary>
    /// SeenResources against an independent rule while a scout walks and trees fall in and out of sight: after every update,
    /// (1) a slot changes in the copy only when the remembered or the live footprint is visible; (2) any slot with a visible
    /// footprint matches the store; (3) the props relist exactly once per copy version (none on a fell out of sight).
    /// </summary>
    [Theory]
    [InlineData(3UL)]
    [InlineData(17UL)]
    [InlineData(91UL)]
    public void LastSeenCopy_ChangesOnlyInSight_MatchesTheStoreWhereSeen_RelistsOncePerVersion(ulong seed)
    {
        (Simulation sim, EntityHandle scout, List<EntityHandle> trees) = Forest(seed);
        World w = sim.World;
        var seen = new SeenResources(w.Data.Resources, w.Resources.Capacity);
        var layout = new PropLayout(w.Data.Resources, w.Resources.Capacity);
        var rng = new Random((int)seed + 1);
        int n = w.Resources.Capacity;
        var prevAlive = new bool[n];
        var prevType = new int[n];
        var prevCell = new int[n];
        int fellsUnseen = 0, fellsSeen = 0, versions = 0, relists = 0, keptStale = 0;
        for (int t = 0; t < 2000; t++)
        {
            if (t % 50 == 0) sim.Enqueue(Command.Move(0, scout, At(sim, rng.Next(3, 60), rng.Next(3, 60))));
            if (t % 7 == 3)
            {
                EntityHandle h = trees[rng.Next(trees.Count)];
                if (w.Resources.IsAlive(h))
                {
                    if (Sees(w, w.Resources.TypeId[h.Index], w.Resources.Cell[h.Index])) fellsSeen++; else fellsUnseen++;
                    w.Resources.Take(h, 1000);
                }
            }
            sim.Tick();
            for (int s = 0; s < n; s++) { prevAlive[s] = seen.Alive[s]; prevType[s] = seen.TypeId[s]; prevCell[s] = seen.Cell[s]; }
            int v0 = seen.Version;
            bool changed = seen.Update(w.NavGrid.Version, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, w.Fog, 0, true, w.NavGrid.Width);
            if (changed) versions++;
            Assert.Equal(changed, seen.Version != v0);
            if (layout.Refresh(w.Heightmap, w.NavGrid.Width, seen.Version, seen.Alive, seen.TypeId, seen.Cell)) relists++;
            for (int s = 0; s < n; s++)
            {
                bool moved = prevAlive[s] != seen.Alive[s] || seen.Alive[s] && (prevType[s] != seen.TypeId[s] || prevCell[s] != seen.Cell[s]);
                bool oldSeen = prevAlive[s] && Sees(w, prevType[s], prevCell[s]);
                bool newSeen = w.Resources.Alive[s] && Sees(w, w.Resources.TypeId[s], w.Resources.Cell[s]);
                if (t > 0 && moved) Assert.True(oldSeen || newSeen, $"seed {seed} tick {t}: slot {s} changed in the copy out of sight");
                bool differs = w.Resources.Alive[s] != seen.Alive[s]
                    || w.Resources.Alive[s] && (w.Resources.TypeId[s] != seen.TypeId[s] || w.Resources.Cell[s] != seen.Cell[s]);
                bool copySeen = seen.Alive[s] && Sees(w, seen.TypeId[s], seen.Cell[s]);
                Assert.False(differs && (copySeen || newSeen), $"seed {seed} tick {t}: slot {s} stale in sight");
                if (differs) keptStale++;
            }
        }
        _out.WriteLine($"seed {seed}: {fellsSeen} fells in sight, {fellsUnseen} out of sight, {versions} versions, {relists} relists, {keptStale} stale slot-ticks");
        Assert.Equal(versions, relists); // the first fill (version -1 to 0) counts as a change, then once per version
        Assert.True(fellsUnseen > 0 && fellsSeen > 0 && keptStale > 0, "the run must fell in and out of sight");
    }

    /// <summary>The M4-VH2 helpers the M4-V6b twin doesn't call (SeenResources, the PropLayout version overload, the minimap's drawn-dot pick) read a sim every tick: its hash equals a bare twin's every tick.</summary>
    [Theory]
    [InlineData(5UL)]
    [InlineData(23UL)]
    public void NewViewApiHelpers_VH2_DoNotChangeTheSim_HashEqualToABareTwinEveryTick(ulong seed)
    {
        (Simulation bare, EntityHandle scoutA, List<EntityHandle> treesA) = Forest(seed);
        (Simulation viewed, EntityHandle scoutB, List<EntityHandle> treesB) = Forest(seed);
        World w = viewed.World;
        var seen = new SeenResources(w.Data.Resources, w.Resources.Capacity);
        var layout = new PropLayout(w.Data.Resources, w.Resources.Capacity);
        var raster = new MinimapRaster(w.Heightmap, w.NavGrid, new uint[] { 0x4B4F55 }, w.Units.Capacity);
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        var rng = new Random((int)seed);
        int picks = 0;
        for (int t = 0; t < 1200; t++)
        {
            if (t % 50 == 0)
            {
                Vector2 p = At(bare, rng.Next(3, 60), rng.Next(3, 60));
                bare.Enqueue(Command.Move(0, scoutA, p));
                viewed.Enqueue(Command.Move(0, scoutB, p));
            }
            if (t % 9 == 4)
            {
                int k = rng.Next(treesA.Count);
                bare.World.Resources.Take(treesA[k], 1000);
                w.Resources.Take(treesB[k], 1000);
            }
            bare.Tick();
            viewed.Tick();
            ulong before = viewed.StateHash();
            view.Refresh(w.Fog, w.TickNumber, w.Units.Alive, w.Buildings.Alive, w.Buildings.Generation);
            seen.Update(w.NavGrid.Version, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, w.Fog, 0, (t & 1) == 0, w.NavGrid.Width);
            layout.Refresh(w.Heightmap, w.NavGrid.Width, seen.Version, seen.Alive, seen.TypeId, seen.Cell);
            raster.DrawResources(w.Data.Resources, seen.Version, seen.Alive, seen.TypeId, seen.Cell);
            raster.DrawDots(view.UnitShown, w.Units.Position, w.Units.Owner);
            if (raster.DrawnEnemyDotAt(w.Units.Position[scoutB.Index], 1) >= 0) picks++;
            Assert.Equal(before, viewed.StateHash());
            Assert.Equal(bare.StateHash(), viewed.StateHash());
        }
        _out.WriteLine($"seed {seed}: {picks} drawn-dot picks, {seen.Version} copy versions, hashes equal");
        Assert.True(picks > 0 && seen.Version > 0, "the run must exercise the pick and the copy");
    }
}
