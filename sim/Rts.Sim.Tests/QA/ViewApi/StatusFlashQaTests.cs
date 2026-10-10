using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA attacks on M4-V6b (session 2026-10-10-0215): <see cref="StatusMarkers"/>, <see cref="ResolveFlashes"/> and the
/// BUG-0310 ghost rule in <see cref="FogView.CollectGhosts"/>. The read-only proof (a sim read by every new helper every
/// tick hashes the same as a bare twin), markers against an independent rule at the fog edge and through deaths and slot
/// reuse, and a 200-resolves-a-tick storm through the flash pool (cap, no leak, every flash expires).
/// </summary>
public class StatusFlashQaTests
{
    private readonly ITestOutputHelper _out;

    public StatusFlashQaTests(ITestOutputHelper output) => _out = output;

    private static int StatusCount => TestSim.Data.Statuses.Length;

    // Two mages and a spotter for player 0, Raiders and a mage for player 1, combat on, real fog.
    private static (EntityHandle[] Mages, EntityHandle EnemyMage) Stage(Simulation sim)
    {
        var mages = new[] { Place(sim, 0, Mage, At(sim, 10, 30)), Place(sim, 0, Mage, At(sim, 10, 34)) };
        Spotter(sim, 0, At(sim, 12, 32));
        for (int k = 0; k < 6; k++) Place(sim, 1, Raider, At(sim, 17 + k % 2, 29 + k));
        EntityHandle enemyMage = Place(sim, 1, Mage, At(sim, 40, 10));
        return (mages, enemyMage);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    public void NewViewApiHelpers_DoNotChangeTheSim_HashEqualToABareTwinEveryTick(ulong seed)
    {
        Simulation bare = Flat(size: 64, seed: seed), viewed = Flat(size: 64, seed: seed);
        (EntityHandle[] magesA, EntityHandle enemyA) = Stage(bare);
        (EntityHandle[] magesB, EntityHandle enemyB) = Stage(viewed);
        World w = viewed.World;
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        var marks = new StatusMark[w.Units.Capacity * StatusStore.PerUnit];
        var flashes = new ResolveFlashes(16);
        var removed = new int[16];
        var sent = new SentCasts(); // M4-VH2 (BUG-0370): the pick's queue scan and the sent-cast memory
        int telas = TestSim.Data.FindAbility("telas_fire");
        int markTotal = 0, flashTotal = 0, pickTotal = 0;
        for (int t = 0; t < 900; t++)
        {
            if (t % 60 == 0)
            {
                for (int k = 0; k < 2; k++)
                {
                    Vector2 p = At(bare, 17, 30 + 2 * k);
                    bare.Enqueue(Command.UseAbility(0, magesA[k], 0, p, queued: k == 1));
                    viewed.Enqueue(Command.UseAbility(0, magesB[k], 0, p, queued: k == 1));
                }
                bare.Enqueue(Command.UseAbility(1, enemyA, 0, At(bare, 44, 10)));
                viewed.Enqueue(Command.UseAbility(1, enemyB, 0, At(viewed, 44, 10)));
            }
            bare.Tick();
            viewed.Tick();
            ulong before = viewed.StateHash();
            view.Refresh(w.Fog, w.TickNumber, w.Units.Alive, w.Buildings.Alive, w.Buildings.Generation);
            view.CollectGhosts(w.Fog, w.Buildings.Generation);
            markTotal += StatusMarkers.Collect(w.Units.Statuses, view.UnitShown, StatusCount, marks);
            flashTotal += flashes.Collect(w.AbilityEvents, w.TickNumber);
            for (int i = 0; i < flashes.Capacity; i++)
            {
                if (!flashes.Active[i]) continue;
                view.ShowsPoint(w.Fog, flashes.Point[i]);
                flashes.Age(i, w.TickNumber, 0.5f);
                flashes.MarkDrawn(i, w.TickNumber);
            }
            flashes.Expire(w.TickNumber, removed);
            int picked = AbilityCaster.PickCaster(magesB, w.Units.Alive, w.Units.Generation, w.Units.TypeId, w.Units.Position, w.Units.CastAbility,
                w.Units.AbilityReadyTick, w.Units.QueueCount, w.Units.QueueKind, w.Units.QueueTypeId, sent.For(w.TickNumber, telas), w.Data.Units, telas, w.TickNumber,
                At(viewed, 17, 31), queued: t % 2 == 0, out _);
            if (picked >= 0)
            {
                pickTotal++;
                sent.Note(w.TickNumber + 1, telas, new EntityHandle(picked, w.Units.Generation[picked]));
            }
            AbilityCaster.HasQueuedCast(w.Units.QueueCount, w.Units.QueueKind, w.Units.QueueTypeId, magesB[1].Index, 0);
            Assert.Equal(before, viewed.StateHash());
            Assert.Equal(bare.StateHash(), viewed.StateHash());
        }
        _out.WriteLine($"seed {seed}: {markTotal} markers, {flashTotal} flashes, {pickTotal} picks over 900 ticks, hashes equal");
        Assert.True(markTotal > 0 && flashTotal >= 2 && pickTotal > 0, $"{markTotal} markers, {flashTotal} flashes, {pickTotal} picks: the run must exercise all three");
    }

    /// <summary>
    /// Every tick: the marks equal (alive, Fog.CanSeeUnit, entry with ticks left) per unit, in a fight where Burning Raiders
    /// die and an own scout walks in and out of sight of them (the fog edge across update ticks); a reused slot carries no
    /// marker of the dead unit's statuses.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(21UL)]
    public void Markers_MatchTheIndependentRule_EveryTick_AtTheFogEdge_ThroughDeathsAndSlotReuse(ulong seed)
    {
        Simulation sim = Flat(size: 64, units: 96, seed: seed);
        World w = sim.World;
        UnitStore u = w.Units;
        var rng = new Random((int)seed); // test-side fuzz only
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 6, 32));
        u.Hold[mage.Index] = true;
        EntityHandle scout = Place(sim, 0, Laborer, At(sim, 22, 32)); // cells are 2 m: from cell 28 it sees the Raiders, from 22 none
        var enemies = new List<EntityHandle>();
        for (int k = 0; k < 16; k++) enemies.Add(Place(sim, 1, Raider, At(sim, 30 + k % 4 * 2, 26 + k / 4 * 3)));
        foreach (EntityHandle e in enemies) u.Hold[e.Index] = true; // they stand: the scout's walks decide what is seen
        var view = new FogView(w.Fog.Width, w.Fog.Height, u.Capacity, w.Buildings.Capacity, 0);
        var marks = new StatusMark[u.Capacity * StatusStore.PerUnit];
        int markedTicks = 0, edgeFlips = 0, deathsWithStatus = 0, reused = 0;
        var shownBefore = new bool[u.Capacity];
        var statusAtDeath = new bool[u.Capacity];
        for (int t = 0; t < 1600; t++)
        {
            // The scout walks to and fro across the enemies' edge of sight; statuses land at random (test staging).
            if (t % 300 == 0) sim.Enqueue(Command.Move(0, scout, At(sim, t / 300 % 2 == 0 ? 28 + rng.Next(0, 2) : 22, 30 + rng.Next(0, 5))));
            if (t % 7 == 0)
                for (int i = 0; i < u.Capacity; i++)
                    if (u.Alive[i] && u.Owner[i] == 1 && rng.Next(0, 4) == 0)
                        u.Statuses.Apply(i, rng.Next(0, 2) == 0 ? Burning : Slowed, rng.Next(0, 2) == 0 ? 1f : 0.3f, rng.Next(1, 60), 0);
            for (int i = 0; i < u.Capacity; i++) statusAtDeath[i] = u.Alive[i] && u.Statuses.Count[i] > 0;
            var genBefore = (int[])u.Generation.Clone();
            // Now and then one statused enemy dies and a fresh one is placed, reusing the lowest free slot (often that one).
            if (t % 40 == 20)
                for (int i = 0; i < u.Capacity; i++)
                    if (u.Alive[i] && u.Owner[i] == 1 && u.Statuses.Count[i] > 0)
                    {
                        u.Free(new EntityHandle(i, u.Generation[i]));
                        EntityHandle fresh = Place(sim, 1, Raider, At(sim, 30 + rng.Next(0, 6), 26 + rng.Next(0, 10)));
                        u.Hold[fresh.Index] = true;
                        break;
                    }
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (statusAtDeath[i] && (!u.Alive[i] || u.Generation[i] != genBefore[i])) deathsWithStatus++;
                if (u.Alive[i] && u.Generation[i] != genBefore[i])
                {
                    reused++;
                    Assert.True(u.Statuses.Count[i] == 0, $"tick {w.TickNumber}: reused slot {i} starts with {u.Statuses.Count[i]} statuses");
                }
            }
            view.Refresh(w.Fog, w.TickNumber, u.Alive, w.Buildings.Alive, w.Buildings.Generation);
            int n = StatusMarkers.Collect(u.Statuses, view.UnitShown, StatusCount, marks);
            if (n > 0) markedTicks++;
            var perUnit = new int[u.Capacity];
            for (int k = 0; k < n; k++)
            {
                StatusMark m = marks[k];
                Assert.True(u.Alive[m.Unit], $"tick {w.TickNumber}: a marker over dead slot {m.Unit}");
                Assert.True(w.Fog.CanSeeUnit(0, m.Unit), $"tick {w.TickNumber}: a marker over hidden unit {m.Unit}");
                int at = u.Statuses.IndexOf(m.Unit, m.Status);
                Assert.True(at >= 0 && u.Statuses.TicksRemaining[at] > 0, $"tick {w.TickNumber}: marker {m} without a live entry");
                Assert.InRange(m.Place, 0, m.Row - 1);
                perUnit[m.Unit]++;
            }
            for (int i = 0; i < u.Capacity; i++)
            {
                int want = 0;
                if (u.Alive[i] && w.Fog.CanSeeUnit(0, i))
                    for (int e = 0; e < u.Statuses.Count[i]; e++)
                        if (u.Statuses.TicksRemaining[i * StatusStore.PerUnit + e] > 0) want++;
                Assert.True(want == perUnit[i], $"tick {w.TickNumber}: unit {i} wants {want} markers, drew {perUnit[i]}");
                bool shownNow = view.ShowsUnit(i);
                if (u.Alive[i] && u.Owner[i] == 1 && u.Statuses.Count[i] > 0 && shownNow != shownBefore[i]) edgeFlips++;
                shownBefore[i] = shownNow;
            }
        }
        _out.WriteLine($"seed {seed}: markers on {markedTicks} ticks, {edgeFlips} fog-edge flips of statused units, {deathsWithStatus} deaths with a status, {reused} reused slots");
        Assert.True(markedTicks > 100 && edgeFlips > 0 && reused > 0, $"the run must cross the fog edge with statuses ({edgeFlips} flips, {markedTicks} marked ticks)");
    }

    /// <summary>200 resolves a tick for 40 ticks (half under the fog): the pool never grows past its cap, every flash is accounted for, and all expire once the storm ends.</summary>
    [Fact]
    public void TwoHundredResolvesATick_ForFortyTicks_CappedAccountedForAndAllExpire()
    {
        var flashes = new ResolveFlashes();
        var removed = new int[flashes.Capacity];
        var events = new AbilityEvent[260];
        for (int i = 0; i < events.Length; i++)
            events[i] = new AbilityEvent(new EntityHandle(i, 1), i % 2, 0, new Vector2(i, i), Resolved: i < 200 || i % 2 == 0);
        int resolvesPerTick = 0;
        foreach (AbilityEvent e in events) if (e.Resolved) resolvesPerTick++;
        long tick = 0, maxCount = 0;
        for (; tick < 40; tick++)
        {
            Assert.Equal(resolvesPerTick, flashes.Collect(events, tick));
            Assert.Equal(0, flashes.Collect(events, tick));
            for (int i = 0; i < flashes.Capacity; i++)
                if (flashes.Active[i]) { Assert.InRange(flashes.Age(i, tick, 0.3f), 0f, 1f); flashes.MarkDrawn(i, tick); }
            flashes.Expire(tick, removed);
            maxCount = Math.Max(maxCount, flashes.Count);
            Assert.InRange(flashes.Count, 0, flashes.Capacity);
            Assert.Equal(flashes.Added, flashes.Replaced + flashes.Expired + flashes.Count);
        }
        for (int k = 0; k <= ResolveFlashes.LifetimeTicks; k++, tick++)
        {
            Assert.Equal(0, flashes.Collect(ReadOnlySpan<AbilityEvent>.Empty, tick));
            for (int i = 0; i < flashes.Capacity; i++) if (flashes.Active[i]) flashes.MarkDrawn(i, tick);
            flashes.Expire(tick, removed);
        }
        int active = 0;
        foreach (bool a in flashes.Active) if (a) active++;
        _out.WriteLine($"{flashes.Added} added, {flashes.Replaced} replaced, {flashes.Expired} expired, max live {maxCount}, left {flashes.Count}");
        Assert.Equal(40L * resolvesPerTick, flashes.Added);
        Assert.Equal(0, flashes.Count);
        Assert.Equal(0, active);
        Assert.Equal(flashes.Added, flashes.Replaced + flashes.Expired);
    }

    /// <summary>Edges of the pool: a capacity of 1 (each flash replaces the last), a flash added at a tick before the current one (no negative age), a removed buffer shorter than the expiries (count still right, no throw).</summary>
    [Fact]
    public void FlashPool_Edges_CapacityOne_OldTick_ShortRemovedBuffer()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResolveFlashes(0));
        var one = new ResolveFlashes(1);
        Assert.Equal(0, one.Add(0, 0, Vector2.Zero, 5));
        Assert.Equal(0, one.Add(1, 0, Vector2.One, 5));
        Assert.Equal(1, one.Count);
        Assert.Equal(1, one.Replaced);
        Assert.Equal(1, one.Ability[0]);
        Assert.Equal(0f, one.Age(0, 2, 0f)); // a tick before the start: clamped, not negative
        one.MarkDrawn(0, 5);
        Assert.Equal(1f, one.Age(0, 100, float.NaN)); // NaN render alpha: no NaN age
        Assert.Equal(0f, one.Age(0, 5, float.NaN));
        var pool = new ResolveFlashes(8);
        for (int i = 0; i < 8; i++) { pool.Add(0, 0, Vector2.Zero, 0); pool.MarkDrawn(i, 0); }
        var tiny = new int[3];
        Assert.Equal(8, pool.Expire(ResolveFlashes.LifetimeTicks, tiny));
        Assert.Equal(0, pool.Count);
        pool.MarkDrawn(-1, 0);
        pool.MarkDrawn(99, 0);
    }

    /// <summary>BUG-0310 rule against every footprint cell, every tick, in a fuzz where a building dies in sight, out of sight and with a scout on the footprint's edge: never a ghost over a visible cell, and every known unseen entry over fully hidden ground is a ghost.</summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    public void Ghosts_NeverOverAVisibleFootprintCell_AndStillShownOverHiddenGround(ulong seed)
    {
        Simulation sim = Flat(size: 64, seed: seed);
        World w = sim.World;
        var rng = new Random((int)seed);
        var tents = new List<EntityHandle>();
        for (int k = 0; k < 4; k++) tents.Add(TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 20 + k * 8, 30));
        EntityHandle scout = Spotter(sim, 0, new Vector2(8f, 62f));
        w.Units.Hold[scout.Index] = false;
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        int ghostTicks = 0, killed = 0;
        for (int t = 0; t < 1200; t++)
        {
            if (t % 80 == 0) sim.Enqueue(Command.Move(0, scout, new Vector2(4f + rng.Next(0, 56), 50f + rng.Next(0, 14))));
            if (t % 150 == 149 && killed < tents.Count) { w.Buildings.Free(tents[killed]); killed++; }
            sim.Tick();
            view.Refresh(w.Fog, w.TickNumber, w.Units.Alive, w.Buildings.Alive, w.Buildings.Generation);
            ReadOnlySpan<Rts.Sim.Vision.BuildingGhost> list = w.Fog.Ghosts(0);
            for (int i = 0; i < w.Buildings.Capacity; i++)
            {
                var g = list[i];
                bool visibleCell = false;
                if (g.Known)
                {
                    var def = w.Data.Buildings[g.TypeId];
                    for (int dy = 0; dy < def.FootprintHeight; dy++)
                        for (int dx = 0; dx < def.FootprintWidth; dx++)
                            visibleCell |= w.Fog.IsVisible(0, g.Cell + dy * w.NavGrid.Width + dx);
                }
                bool drawn = w.Fog.CanSeeBuilding(0, i) && w.Buildings.Generation[i] == g.Generation;
                Assert.False(view.ShowsGhost(i) && visibleCell, $"tick {w.TickNumber}: ghost {i} over a visible footprint cell");
                Assert.Equal(g.Known && !drawn && !visibleCell, view.ShowsGhost(i));
                if (view.ShowsGhost(i)) ghostTicks++;
            }
        }
        _out.WriteLine($"seed {seed}: {ghostTicks} ghost-ticks, {killed} tents killed");
    }
}

/// <summary>
/// QA (M4-V6b) on the Shift-armed ability (BUG-0342's fix): two selected ready mages walking under a Move; two Shift
/// clicks. The second click should go to the other mage (the first's cast already sits in its queue), not queue a second
/// cast on the same mage that its cooldown then drops.
/// </summary>
public class ShiftQueuedCastQaTests
{
    private readonly ITestOutputHelper _out;

    public ShiftQueuedCastQaTests(ITestOutputHelper output) => _out = output;

    private static int Pick(Simulation sim, EntityHandle[] sel, Vector2 point)
    {
        UnitStore u = sim.World.Units;
        return AbilityCaster.PickCaster(sel, u.Alive, u.Generation, u.TypeId, u.Position, u.CastAbility, u.AbilityReadyTick, u.QueueCount, u.QueueKind, u.QueueTypeId, ReadOnlySpan<EntityHandle>.Empty, sim.World.Data.Units, TestSim.Data.FindAbility("telas_fire"),
            sim.World.TickNumber, point, queued: true, out _);
    }

    [Fact]
    public void TwoShiftClicks_OnWalkingMages_GoToBothMages_BothCastsResolve()
    {
        Simulation sim = NoFights();
        EntityHandle a = Place(sim, 0, Mage, At(sim, 10, 20)), b = Place(sim, 0, Mage, At(sim, 10, 26));
        var sel = new[] { a, b };
        sim.Enqueue(Command.Move(0, a, At(sim, 40, 20)));
        sim.Enqueue(Command.Move(0, b, At(sim, 40, 26)));
        sim.Tick();
        sim.Tick();
        Vector2 p1 = At(sim, 30, 21), p2 = At(sim, 30, 23);
        int first = Pick(sim, sel, p1);
        EntityHandle h1 = first == a.Index ? a : b;
        sim.Enqueue(Command.UseAbility(0, h1, 0, p1, queued: true));
        sim.Tick();
        sim.Tick();
        int second = Pick(sim, sel, p2);
        _out.WriteLine($"first click -> slot {first} (queue {sim.World.Units.QueueCount[first]}, cast {sim.World.Units.CastAbility[first]}), second click -> slot {second}");
        Assert.NotEqual(first, second);
        EntityHandle h2 = second == a.Index ? a : b;
        sim.Enqueue(Command.UseAbility(0, h2, 0, p2, queued: true));
        int resolves = 0;
        for (int t = 0; t < 600; t++)
        {
            sim.Tick();
            foreach (AbilityEvent e in sim.World.AbilityEvents) if (e.Resolved) resolves++;
        }
        Assert.Equal(2, resolves);
    }
}
