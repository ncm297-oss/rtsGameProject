using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>
/// M4-V6c: the zone discs (<see cref="ZoneDiscs"/>): an own storm always, for its whole life, fading in its last second;
/// an enemy storm only where the player sees it (an own unit inside, or sight reaching its rim), never on unexplored or
/// explored-only ground; slots ascending; allocation-free; and collecting never changes the sim.
/// </summary>
[Collection(SerialCollection.Name)]
public class ZoneDiscsTests
{
    private readonly ITestOutputHelper _out;

    public ZoneDiscsTests(ITestOutputHelper output) => _out = output;

    private static int Priest => TestSim.Data.FindUnit("whirlwind_priest");

    private static AbilityDef Sandstorm => TestSim.Data.Abilities[TestSim.Data.FindAbility("sandstorm")];

    private static int Collect(Simulation sim, int player, bool fogOn, ZoneDisc[] discs) =>
        ZoneDiscs.Collect(sim.World.Zones, sim.World.Fog, player, fogOn, discs);

    /// <summary>A 48 x 48 flat two-player map, combat off, the fog as the units make it (nothing explored up front).</summary>
    private static Simulation Unexplored(ulong seed = 1) =>
        new(TestSim.ConfigNoCombat(Seed: seed, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512), LocalMovementTests.Flat(48));

    /// <summary>Runs ticks until a fog update has run after the current one.</summary>
    private static void NextUpdate(Simulation sim)
    {
        do sim.Tick();
        while (!VisionSystem.IsUpdateTick(sim.TickNumber - 1));
    }

    [Fact]
    public void ARealSandstorm_OneOwnDiscWithTheDefsRadius_For240Ticks_FadingOverTheLast20_NoneAt241()
    {
        Simulation sim = NoFights();
        EntityHandle priest = Place(sim, 1, Priest, At(sim, 10, 24));
        Vector2 point = At(sim, 15, 24);
        sim.Enqueue(Command.UseAbility(1, priest, 0, point));
        var discs = new ZoneDisc[sim.World.Zones.Capacity];
        int before = 0;
        while (!Had(sim, priest, resolved: true))
        {
            Assert.Equal(0, Collect(sim, 1, true, discs));
            sim.Tick();
            Assert.True(++before < 400, "the Sandstorm never resolved");
        }
        var alphas = new List<float>();
        while (Collect(sim, 1, true, discs) == 1)
        {
            ZoneDisc d = discs[0];
            Assert.Equal(0, d.Slot);
            Assert.Equal(point, d.Center);
            Assert.Equal(Sandstorm.Radius, d.Radius);
            Assert.Equal(6f, d.Radius);
            Assert.True(d.Own);
            Assert.Equal(ZoneDiscs.Alpha(sim.World.Zones.TicksRemaining[0]), d.Alpha);
            alphas.Add(d.Alpha);
            // Another player with the fog off (--no-fog) sees it too, not as its own.
            Assert.Equal(1, Collect(sim, 0, false, discs));
            Assert.False(discs[0].Own);
            sim.Tick();
            Assert.True(alphas.Count < 400, "the storm never ended");
        }
        Assert.Equal(240, alphas.Count); // 12 s at 20 Hz; the 241st tick after the resolve has none
        Assert.Equal(0, sim.World.Zones.Count);
        Assert.Equal(0, Collect(sim, 0, false, discs));
        for (int t = 0; t < 220; t++) Assert.Equal(ZoneDiscs.DiscAlpha, alphas[t]);
        for (int t = 220; t < 240; t++) Assert.True(alphas[t] < alphas[t - 1], $"alpha {alphas[t]} at tick {t} of the storm is not below {alphas[t - 1]}");
        Assert.Equal(0f, alphas[239]);
        Assert.Equal(0.35f, ZoneDiscs.DiscAlpha);
        _out.WriteLine($"resolved after {before} ticks; alphas over the last 21 ticks: {string.Join(", ", alphas.GetRange(219, 21))}");
    }

    [Fact]
    public void AnEnemyStorm_UnexploredOrSightShort_None_ARimCellSeen_One_AnOwnUnitInside_One()
    {
        Simulation sim = Unexplored();
        FogStore fog = sim.World.Fog;
        var discs = new ZoneDisc[sim.World.Zones.Capacity];
        // A Wickan Lancer (fast, so the walk ends well inside the storm's 12 s); the cells its circle covers follow its sight.
        int lancer = TestSim.Data.FindUnit("malazan_wickan_lancer");
        int sightCells = (int)(TestSim.Data.Units[lancer].Sight / 2f);
        Assert.InRange(sightCells, 3, 12);
        // The storm at cell (30, 24); its rim ring reaches 4 cells (8 m), so cell (26, 24) is its nearest rim cell west.
        Vector2 centre = At(sim, 30, 24);
        // The Lancer's sight ends 10 m (5 cells) short of the rim.
        int start = 26 - 5 - sightCells, rim = 26 - sightCells;
        EntityHandle scout = Place(sim, 0, lancer, At(sim, start, 24));
        ZoneSystem.Create(sim.World, 1, Sandstorm, centre);
        NextUpdate(sim);
        Assert.Equal(VisionConstants.Visible, fog.Visibility(0)[24 * fog.Width + 21]); // its sight's east end
        Assert.Equal(VisionConstants.Unexplored, fog.Visibility(0)[24 * fog.Width + 22]);
        Assert.Equal(VisionConstants.Unexplored, fog.Visibility(0)[24 * fog.Width + 26]);
        AssertNoneVisibleWithin(sim, centre, 8f);
        Assert.Equal(0, Collect(sim, 0, true, discs));
        Assert.Equal(1, Collect(sim, 1, true, discs)); // the owner always
        Assert.Equal(1, Collect(sim, 0, false, discs)); // --no-fog

        // Walked to where its sight reaches the rim cell (26, 24) and no cell inside the radius.
        WalkTo(sim, scout, At(sim, rim, 24));
        NextUpdate(sim);
        Assert.Equal(VisionConstants.Visible, fog.Visibility(0)[24 * fog.Width + 26]);
        AssertNoneVisibleWithin(sim, centre, 6f);
        Assert.Equal(1, Collect(sim, 0, true, discs));
        Assert.False(discs[0].Own);
        Assert.Equal(centre, discs[0].Center);

        // Stepped inside, 2 m from the centre: Blinded (2 m), it sees no rim cell, only cells inside.
        Vector2 inside = At(sim, 29, 24);
        WalkTo(sim, scout, inside);
        Assert.True(sim.World.Zones.Count == 1, $"the storm ended before the walk did (tick {sim.TickNumber}, at {sim.World.Units.Position[scout.Index]}, want {inside}, state {sim.World.Units.State[scout.Index]})");
        NextUpdate(sim);
        Assert.True(sim.World.Units.Statuses.BlindOf(scout.Index) >= 0);
        AssertNoRimCellVisible(sim, centre, 6f);
        Assert.Equal(VisionConstants.Visible, fog.Visibility(0)[fog.CellOf(inside)]);
        Assert.Equal(1, Collect(sim, 0, true, discs));
        Assert.True(sim.World.Zones.TicksRemaining[0] > 20, "the inside row ran in the fade");
        _out.WriteLine($"rim seen at cell (26, 24) from ({rim}, 24); inside: its cell visible, Blinded, {sim.World.Zones.TicksRemaining[0]} ticks left");
    }

    [Fact]
    public void AnEnemyStorm_OnExploredOnlyGround_IsNotDrawn()
    {
        Simulation sim = Unexplored();
        FogStore fog = sim.World.Fog;
        var discs = new ZoneDisc[sim.World.Zones.Capacity];
        Vector2 centre = At(sim, 30, 24);
        // First the Crossbowman sees the storm's ground (the rim and in), then walks away so it is only explored.
        EntityHandle xbow = Place(sim, 0, Crossbowman, At(sim, 24, 24));
        NextUpdate(sim);
        Assert.Equal(VisionConstants.Visible, fog.Visibility(0)[fog.CellOf(centre)]);
        WalkTo(sim, xbow, At(sim, 6, 24));
        NextUpdate(sim);
        ZoneSystem.Create(sim.World, 1, Sandstorm, centre);
        NextUpdate(sim);
        Assert.Equal(VisionConstants.Explored, fog.Visibility(0)[fog.CellOf(centre)]);
        Assert.Equal(VisionConstants.Explored, fog.Visibility(0)[24 * fog.Width + 26]);
        AssertNoneVisibleWithin(sim, centre, 8f);
        for (int t = 0; t < 40; t++)
        {
            Assert.Equal(0, Collect(sim, 0, true, discs));
            sim.Tick();
        }
        Assert.Equal(1, Collect(sim, 0, false, discs));
    }

    /// <summary>A visible cell on the far side of the rim counts as well as the near one; a visible cell just past the rim ring (10 m out) does not.</summary>
    [Fact]
    public void SeesStorm_TheRimRingIsRadiusPlus2m_ByCellCentre()
    {
        const int w = 20, h = 20;
        var vis = new byte[w * h];
        var centre = new Vector2(20f, 20f); // the corner of four cells
        Assert.False(ZoneDiscs.SeesStorm(vis, w, h, centre, 6f));
        // Cell (14, 9): centre (29, 19), 9.06 m out: past 8 m.
        vis[9 * w + 14] = VisionConstants.Visible;
        Assert.False(ZoneDiscs.SeesStorm(vis, w, h, centre, 6f));
        // Cell (13, 9): centre (27, 19), 7.07 m out: in the rim ring.
        vis[9 * w + 13] = VisionConstants.Explored;
        Assert.False(ZoneDiscs.SeesStorm(vis, w, h, centre, 6f));
        vis[9 * w + 13] = VisionConstants.Visible;
        Assert.True(ZoneDiscs.SeesStorm(vis, w, h, centre, 6f));
        Assert.False(ZoneDiscs.SeesStorm(vis, w, h, centre, 4f)); // radius 4: the ring ends at 6 m
        Assert.False(ZoneDiscs.SeesStorm(new byte[3], w, h, centre, 6f)); // a grid of the wrong size: no such player
        // A storm off the map's edge clamps its box.
        Array.Clear(vis);
        vis[0] = VisionConstants.Visible;
        Assert.True(ZoneDiscs.SeesStorm(vis, w, h, new Vector2(-3f, -3f), 6f));
    }

    [Fact]
    public void Alpha_HoldsThenFallsOneStepATick_To0OnTheLastTick()
    {
        Assert.Equal(ZoneDiscs.DiscAlpha, ZoneDiscs.Alpha(240));
        Assert.Equal(ZoneDiscs.DiscAlpha, ZoneDiscs.Alpha(21));
        Assert.Equal(ZoneDiscs.DiscAlpha * 19f / 20f, ZoneDiscs.Alpha(20), 6);
        Assert.Equal(ZoneDiscs.DiscAlpha / 20f, ZoneDiscs.Alpha(2), 6);
        Assert.Equal(0f, ZoneDiscs.Alpha(1));
        Assert.Equal(0f, ZoneDiscs.Alpha(0));
        Assert.Equal(20, ZoneDiscs.FadeTicks);
    }

    [Fact]
    public void ThreeStorms_SlotsAscending_AShortOutputKeepsTheLowest_AFullStoreFitsTheOutput()
    {
        Simulation sim = NoFights();
        ZoneStore z = sim.World.Zones;
        var discs = new ZoneDisc[z.Capacity];
        ZoneSystem.Create(sim.World, 1, Sandstorm, At(sim, 10, 10));
        ZoneSystem.Create(sim.World, 0, Sandstorm, At(sim, 20, 20));
        ZoneSystem.Create(sim.World, 1, Sandstorm, At(sim, 30, 30));
        Assert.Equal(3, Collect(sim, 1, false, discs));
        Assert.Equal((0, true), (discs[0].Slot, discs[0].Own));
        Assert.Equal((1, false), (discs[1].Slot, discs[1].Own));
        Assert.Equal((2, true), (discs[2].Slot, discs[2].Own));
        Assert.Equal(At(sim, 20, 20), discs[1].Center);
        // A short output keeps the lowest slots.
        Assert.Equal(2, ZoneDiscs.Collect(z, sim.World.Fog, 1, false, discs.AsSpan(0, 2)));
        // Fill the store; an output of ZoneStore.Capacity drops none.
        for (int k = 3; k < z.Capacity; k++) ZoneSystem.Create(sim.World, k & 1, Sandstorm, At(sim, 4 + k % 40, 4 + k / 40 * 10));
        Assert.Equal(z.Capacity, z.Count);
        Assert.Equal(z.Capacity, Collect(sim, 0, false, discs));
        for (int k = 0; k < z.Capacity; k++) Assert.Equal(k, discs[k].Slot);
        // With the fog on (all explored, nothing seen) the owner sees exactly its own.
        Simulation fresh = NoFights();
        for (int k = 0; k < 6; k++) ZoneSystem.Create(fresh.World, k % 3 == 0 ? 0 : 1, Sandstorm, At(fresh, 6 + 6 * k, 24));
        int n = Collect(fresh, 0, true, discs);
        Assert.Equal(2, n);
        Assert.Equal((0, 3), (discs[0].Slot, discs[1].Slot));
    }

    [Fact]
    public void SixtyFourZones_TwoPlayers_FogOnAndOff_ZeroBytes()
    {
        Simulation sim = Unexplored();
        ZoneStore z = sim.World.Zones;
        Assert.Equal(64, z.Capacity);
        // Player 0's Crossbowmen in a row so some enemy storms are seen at the rim and some not.
        for (int x = 4; x < 44; x += 8) Place(sim, 0, Crossbowman, At(sim, x, 8));
        NextUpdate(sim);
        for (int k = 0; k < z.Capacity; k++) ZoneSystem.Create(sim.World, k & 1, Sandstorm, At(sim, 3 + k % 8 * 6, 4 + k / 8 * 5));
        NextUpdate(sim);
        Assert.Equal(64, z.Count);
        var discs = new ZoneDisc[z.Capacity];
        int seen0 = 0, seen1 = 0, all = 0;
        Action block = () =>
        {
            seen0 = Collect(sim, 0, true, discs);
            seen1 = Collect(sim, 1, true, discs);
            all = Collect(sim, 0, false, discs);
        };
        block();
        AllocationProbe.AssertZero(block, _out);
        Assert.True(seen0 > 32 && seen0 < 64, $"player 0 sees {seen0} of 64 (its 32 and some of the enemy's)");
        Assert.Equal(32, seen1); // no units: its own only
        Assert.Equal(64, all);
        _out.WriteLine($"64 zones: player 0 draws {seen0}, player 1 {seen1}, no fog {all}; 0 bytes");
    }

    /// <summary>The hash twin: collecting every tick for both players, fog on and off, while both sides cast storms, changes no state hash.</summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    public void CollectEveryTick_1200Ticks_StormsOnBothSides_HashEqualsABareTwin(ulong seed)
    {
        Simulation a = Unexplored(seed), b = Unexplored(seed);
        var discs = new ZoneDisc[a.World.Zones.Capacity];
        EntityHandle[] Stage(Simulation sim) => Spawn(sim,
            (0, Priest, At(sim, 8, 14)), (0, Priest, At(sim, 8, 24)), (0, Priest, At(sim, 8, 34)),
            (1, Priest, At(sim, 40, 14)), (1, Priest, At(sim, 40, 24)), (1, Priest, At(sim, 40, 34)),
            (0, Crossbowman, At(sim, 14, 20)), (0, Crossbowman, At(sim, 14, 28)),
            (1, Raider, At(sim, 34, 20)), (1, Raider, At(sim, 34, 28)));
        EntityHandle[] ua = Stage(a), ub = Stage(b);
        Assert.Equal(b.StateHash(), a.StateHash());
        void Both(Func<Simulation, EntityHandle[], Command> make)
        {
            a.Enqueue(make(a, ua));
            b.Enqueue(make(b, ub));
        }
        int maxDrawn = 0, enemySeen = 0, live = 0;
        for (int t = 0; t < 1200; t++)
        {
            int k = t / 150 % 3;
            if (t % 150 == 10)
            {
                // A priest of each side casts on the other's soldiers (each priest every 450 ticks; 45 s cooldown, so some wait).
                Both((s, u) => Command.UseAbility(0, u[k], 0, s.World.Units.Position[u[8 + (t / 150 & 1)].Index]));
                Both((s, u) => Command.UseAbility(1, u[3 + k], 0, s.World.Units.Position[u[6 + (t / 150 & 1)].Index]));
            }
            if (t % 200 == 100)
            {
                // The soldiers walk into and out of the storms.
                Both((s, u) => Command.Move(0, u[6], At(s, 20 + t / 200 * 3 % 18, 20)));
                Both((s, u) => Command.Move(1, u[8], At(s, 28 - t / 200 * 3 % 18, 20)));
            }
            ulong before = a.StateHash();
            int n0 = Collect(a, 0, true, discs);
            int n1 = Collect(a, 1, true, discs);
            int nf = Collect(a, 0, false, discs);
            for (int i = 0; i < n1; i++) if (!discs[i].Own) enemySeen++;
            maxDrawn = Math.Max(maxDrawn, Math.Max(n0, nf));
            live = Math.Max(live, a.World.Zones.Count);
            Assert.Equal(before, a.StateHash());
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed} tick {a.TickNumber}: the collecting sim diverged from its twin");
        }
        Assert.True(live >= 2 && maxDrawn >= 2, $"storms live at once {live}, drawn {maxDrawn}");
        _out.WriteLine($"seed {seed}: up to {live} storms at once, {maxDrawn} drawn, {enemySeen} enemy-disc collections for player 1; hashes equal");
    }

    /// <summary>
    /// Moves the unit into <paramref name="to"/>'s cell: a Move to a point 0.9 m past it along the walk (a move stops about
    /// 1 m short of its point), ticks until the unit is idle, and checks it stopped in that cell.
    /// </summary>
    private static void WalkTo(Simulation sim, EntityHandle unit, Vector2 to)
    {
        UnitStore u = sim.World.Units;
        Vector2 dir = Vector2.Normalize(to - u.Position[unit.Index]);
        sim.Enqueue(Command.Move(u.Owner[unit.Index], unit, to + dir * 0.9f));
        sim.Tick();
        sim.Tick();
        RunUntil(sim, () => u.State[unit.Index] == UnitState.Idle, 600);
        Assert.Equal(sim.World.Fog.CellOf(to), sim.World.Fog.CellOf(u.Position[unit.Index]));
    }

    private static void AssertNoneVisibleWithin(Simulation sim, Vector2 centre, float reach)
    {
        FogStore fog = sim.World.Fog;
        for (int y = 0; y < fog.Height; y++)
            for (int x = 0; x < fog.Width; x++)
                if (Vector2.Distance(At(sim, x, y), centre) <= reach)
                    Assert.False(fog.IsVisible(0, y * fog.Width + x), $"cell ({x}, {y}) within {reach} m of the storm is visible");
    }

    private static void AssertNoRimCellVisible(Simulation sim, Vector2 centre, float radius)
    {
        FogStore fog = sim.World.Fog;
        for (int y = 0; y < fog.Height; y++)
            for (int x = 0; x < fog.Width; x++)
            {
                float d = Vector2.Distance(At(sim, x, y), centre);
                if (d > radius && d <= radius + ZoneDiscs.RimWidth)
                    Assert.False(fog.IsVisible(0, y * fog.Width + x), $"rim cell ({x}, {y}) is visible");
            }
    }
}
