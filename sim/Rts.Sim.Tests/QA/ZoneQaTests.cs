using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M4-4b-2, 2026-10-10-0215): zones, Blinded and Sandstorm at their edges: a zone expiring on a fog update tick vs.
/// between updates, two Whirlwind players' storms over one Malazan unit, a tower and a storm, a Blinded unit on the ramp
/// under the 4 m lip, a Blinded caster at the edge of its range, a Blinded archer and a building, the zone's radius edge
/// for statuses vs. the fog's cell rule, the replay recorder and the zone capacity, and 70 Sandstorms in one replay.
/// </summary>
[Collection(SerialCollection.Name)]
public class ZoneQaTests
{
    private static AbilityDef Sandstorm => TestSim.Data.Abilities[TestSim.Data.FindAbility("sandstorm")];
    private static int Blinded => TestSim.Data.FindStatus("blinded");
    private static int Priest => TestSim.Data.FindUnit("whirlwind_priest");
    private static int Archer => TestSim.Data.FindUnit("whirlwind_desert_archer");
    private static int CampFollower => TestSim.Data.FindUnit("whirlwind_camp_follower");

    private static Simulation Scene(bool combat, int players = 2, int size = 48) =>
        new(combat
            ? TestSim.Config(Seed: 1, PlayerCount: players, UnitCapacity: 64, CommandCapacity: 512)
            : TestSim.ConfigNoCombat(Seed: 1, PlayerCount: players, UnitCapacity: 64, CommandCapacity: 512), LocalMovementTests.Flat(size));

    private static void Blind(Simulation sim, EntityHandle h, int ticks) =>
        StatusSystem.Apply(sim.World, h.Index, Blinded, 0f, ticks, 1 - sim.World.Units.Owner[h.Index]);

    // ---------- expiry vs. the vision cadence ----------

    /// <summary>
    /// A zone freed in phase 5 of a fog update tick unhides in that tick's phase 12; one freed the tick after an update
    /// stays hidden for exactly 3 more ticks (the next update), and combat's own-sight rule unhides at the free in both.
    /// </summary>
    [Theory]
    [InlineData(2, 0)] // made between ticks when the next tick is 2 mod 4: freed on a tick that is 1 mod 4 (an update)
    [InlineData(3, 3)] // freed on a tick that is 2 mod 4: hidden until the update 3 ticks later
    public void AZoneExpiringOnAnUpdateTick_UnhidesThatTick_BetweenUpdates_AtTheNextOne(int startMod4, int hiddenTicksAfterFree)
    {
        Simulation sim = Scene(combat: false);
        World w = sim.World;
        Vector2 centre = At(sim, 24, 24);
        EntityHandle crossbow = Place(sim, 0, Crossbowman, Off(centre, -10f));
        EntityHandle raider = Place(sim, 1, Raider, centre);
        while (sim.TickNumber % 4 != startMod4) sim.Tick();
        ZoneSystem.Create(w, 1, Sandstorm, centre); // between ticks: 240 ticks, freed in phase 5 of the 240th tick from now
        int freeTick = sim.TickNumber + 239;
        while (sim.TickNumber <= freeTick - 1) sim.Tick();
        Assert.Equal(1, w.Zones.Count);
        Assert.False(w.Fog.CanSeeUnit(0, raider.Index));
        sim.Tick(); // the free tick
        Assert.Equal(freeTick, sim.TickNumber - 1);
        Assert.Equal(0, w.Zones.Count);
        Assert.False(VisionSystem.ZoneHides(w, 0, w.Units.Position[crossbow.Index], true, centre));
        Assert.True(VisionSystem.UnitSeesUnit(w, crossbow.Index, raider.Index)); // its own eyes: at once
        for (int t = 0; t < hiddenTicksAfterFree; t++)
        {
            Assert.False(w.Fog.CanSeeUnit(0, raider.Index), $"{t} ticks after the free: the fog should still hide it");
            sim.Tick();
        }
        Assert.True(w.Fog.CanSeeUnit(0, raider.Index), "the fog never unhid the zone's cells");
    }

    // ---------- two owners' storms over one unit ----------

    /// <summary>
    /// Two Whirlwind players (1, 2) drop overlapping Sandstorms on a Malazan Crossbowman: it carries one Blinded and one
    /// Slowed (not two of each), 20 ticks each, every tick; neither Whirlwind player's unit in the other's storm is spared;
    /// each Whirlwind player sees into its own storm but not the other's; the Malazan sees only its 2 m.
    /// </summary>
    [Fact]
    public void TwoWhirlwindPlayersOverlappingStorms_OverOneMalazanUnit()
    {
        Simulation sim = Scene(combat: false, players: 3);
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 a = At(sim, 22, 24), b = At(sim, 26, 24); // 8 m apart: overlap 4 m wide around (24, 24)
        EntityHandle mal = Place(sim, 0, Crossbowman, At(sim, 24, 24));
        EntityHandle r1 = Place(sim, 1, Raider, Off(b, 3f));  // inside player 2's storm only
        EntityHandle r2 = Place(sim, 2, Raider, Off(a, -3f)); // inside player 1's storm only
        EntityHandle w1 = Place(sim, 1, Raider, Off(a, -5f, 1f)); // player 1's, in its own storm
        ZoneSystem.Create(w, 1, Sandstorm, a);
        ZoneSystem.Create(w, 2, Sandstorm, b);
        for (int t = 0; t < 30; t++)
        {
            sim.Tick();
            Assert.Equal(2, u.Statuses.Count[mal.Index]);
            Assert.Equal((0f, 20), StatusOf(sim, mal, Blinded));
            Assert.Equal((0.3f, 20), StatusOf(sim, mal, Slowed));
            Assert.Equal(Blinded, u.Statuses.BlindOf(mal.Index));
            Assert.Equal((0f, 20), StatusOf(sim, r1, Blinded)); // player 2's storm takes player 1's Raider
            Assert.Equal((0f, 20), StatusOf(sim, r2, Blinded));
            Assert.Equal(0, u.Statuses.Count[w1.Index]);
        }
        FogMaps.RunThroughNextUpdate(sim);
        FogStore fog = w.Fog;
        // Player 1 sees its own storm's far edge (its Raider w1 is in it, sight 14) but not into player 2's beyond its
        // Blinded Raider r1's 2 m.
        Assert.True(fog.IsVisible(1, fog.CellOf(Off(a, -3f))));
        Assert.True(fog.CanSeeUnit(1, r2.Index));
        Assert.False(fog.IsVisible(1, fog.CellOf(Off(b, -1f, 4f))), "player 1 sees into player 2's storm");
        Assert.True(fog.IsVisible(1, fog.CellOf(Off(b, 3f)))); // r1's own cell
        // The Malazan unit in both: only its 2 m (both storms hide from player 0).
        Assert.False(fog.CanSeeUnit(0, r1.Index));
        Assert.False(fog.CanSeeUnit(0, r2.Index));
        Assert.True(fog.IsVisible(0, fog.CellOf(u.Position[mal.Index])));
        Assert.False(fog.IsVisible(0, fog.CellOf(Off(u.Position[mal.Index], 4f))));
    }

    // ---------- towers ----------

    /// <summary>A tower shooting a Raider stops once a Sandstorm covers it (at the next fog update at the latest), takes it again after the storm, and keeps shooting a Blinded unit outside any storm (a tower is never Blinded).</summary>
    [Fact]
    public void ATower_LosesItsTargetInsideAnEnemyStorm_AndShootsABlindedUnitOutsideOne()
    {
        Simulation sim = Scene(combat: true, size: 64);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle tower = TowerTests.PlaceBuilding(sim, 0, TowerTests.Watchtower, 30, 30); // centre (62, 62) m
        EntityHandle raider = Place(sim, 1, Raider, new Vector2(62f, 74f)); // 12 m south
        u.Hold[raider.Index] = true;
        RunUntil(sim, () => w.Buildings.TowerTarget[tower.Index] == raider, 40);
        Assert.Equal(raider, w.Buildings.TowerTarget[tower.Index]);
        ZoneSystem.Create(w, 1, Sandstorm, u.Position[raider.Index]);
        // A wind-up already started still fires (as before M4-4b-2: a wind-up checks only the gap); the target is dropped
        // at the tower's next due check after the fog hides the cell.
        RunUntil(sim, () => w.Buildings.TowerTarget[tower.Index] != raider, 30);
        Assert.NotEqual(raider, w.Buildings.TowerTarget[tower.Index]);
        GatherMaps.Run(sim, 30); // a shot in flight lands
        int hp = u.Hp[raider.Index];
        GatherMaps.Run(sim, 120);
        Assert.Equal(hp, u.Hp[raider.Index]);
        Assert.Equal(default, w.Buildings.TowerTarget[tower.Index]);
        // The storm ends: the tower takes it again.
        RunUntil(sim, () => w.Zones.Count == 0, 200);
        RunUntil(sim, () => w.Buildings.TowerTarget[tower.Index] == raider, 40);
        Assert.Equal(raider, w.Buildings.TowerTarget[tower.Index]);
        // Blinded outside any storm: still shot.
        Blind(sim, raider, 10_000);
        hp = u.Hp[raider.Index];
        RunUntil(sim, () => u.Hp[raider.Index] < hp, 120);
        Assert.True(u.Hp[raider.Index] < hp, "the tower stopped shooting a Blinded unit");
    }

    /// <summary>A Blinded Desert Archer ordered onto a Keep fires only from within 3 m of the footprint.</summary>
    [Fact]
    public void ABlindedArcher_OrderedOntoABuilding_FiresOnlyWithin3mOfItsFootprint()
    {
        Simulation sim = Scene(combat: true, size: 64);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle keep = TowerTests.PlaceBuilding(sim, 0, GatherMaps.Keep, 30, 30);
        EntityHandle archer = Place(sim, 1, Archer, new Vector2(62f, 80f));
        Spotter(sim, 1, new Vector2(62f, 76f));
        Blind(sim, archer, 10_000);
        sim.Enqueue(Command.Attack(1, archer, keep, isBuilding: true));
        bool fired = false;
        for (int t = 0; t < 400 && !fired; t++)
        {
            int shots = w.Projectiles.Count;
            sim.Tick();
            if (w.Projectiles.Count <= shots) continue;
            fired = true;
            float d = MathF.Sqrt(Combat.CombatSystem.BuildingDistanceSquared(w, keep.Index, u.Position[archer.Index]));
            Assert.True(d <= 3f + 1e-3f, $"fired at the Keep from {d} m of its footprint");
        }
        Assert.True(fired, "the Blinded archer never fired at the Keep");
    }

    // ---------- the ramp and the lip ----------

    /// <summary>
    /// A Blinded Crossbowman on the ramp's top cell (level 0, below the plateau): its fog shows the plateau cell 2 m away
    /// (the lip, cut to its 2 m) but not the one 4 m away; it shoots a Camp Follower on the plateau 2.5 m away and never one
    /// 3.5 m away.
    /// </summary>
    [Fact]
    public void ABlindedUnitOnTheRamp_SeesAndTakesUpTheLipOnlyWithinItsBlind()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel(), combat: true);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle xb = Place(sim, 0, Crossbowman, FogMaps.Cell(19, 19));      // (39, 39), the ramp's top
        u.Hold[xb.Index] = true;
        EntityHandle far = Place(sim, 1, CampFollower, new Vector2(42.5f, 39f)); // 3.5 m, up on the plateau
        u.Hold[far.Index] = true;
        Blind(sim, xb, 10_000);
        Assert.Equal(0, w.Fog.LevelAt(u.Position[xb.Index]));
        Assert.Equal(1, w.Fog.LevelAt(u.Position[far.Index]));
        int farHp = u.Hp[far.Index];
        GatherMaps.Run(sim, 80);
        Assert.Equal(farHp, u.Hp[far.Index]);
        Assert.Equal(0, u.Target[xb.Index].Generation);
        Assert.True(w.Fog.IsVisible(0, 19 * FogMaps.Size + 20));  // 2 m east, up
        Assert.False(w.Fog.IsVisible(0, 19 * FogMaps.Size + 21)); // 4 m east, up
        EntityHandle near = Place(sim, 1, CampFollower, new Vector2(41.5f, 39f)); // 2.5 m
        u.Hold[near.Index] = true;
        int nearHp = u.Hp[near.Index];
        RunUntil(sim, () => u.Hp[near.Index] < nearHp, 200);
        Assert.True(u.Hp[near.Index] < nearHp, "the Blinded Crossbowman never shot up the lip at 2.5 m");
        Assert.Equal(farHp, u.Hp[far.Index]);
    }

    // ---------- casting while Blinded ----------

    /// <summary>A Blinded Priest casts Sandstorm at 17.9 m (its full 18 m range) from where it stands.</summary>
    [Fact]
    public void ABlindedPriest_CastsAt17_9m_WithoutWalking()
    {
        Simulation sim = NoFights();
        EntityHandle priest = Place(sim, 1, Priest, At(sim, 10, 24));
        Blind(sim, priest, 10_000);
        Vector2 point = Off(At(sim, 10, 24), 17.9f);
        sim.Enqueue(Command.UseAbility(1, priest, 0, point));
        TickOf(sim, priest, resolved: true);
        Assert.Equal(At(sim, 10, 24), sim.World.Units.Position[priest.Index]);
        Assert.Equal(1, sim.World.Zones.Count);
        Assert.Equal(point, sim.World.Zones.Center[0]);
    }

    // ---------- the radius edge: statuses vs. the fog's cells ----------

    /// <summary>
    /// The statuses use a unit's center (6.0 in, 6.01 out); the blocker uses its cell's centre. docs/02 "Zones": a zone that
    /// hides its contents is seen into only by its owner and by an enemy's units inside it. A Raider standing 5.2 m from the
    /// centre (inside: an enemy there would be Blinded) should be hidden from a Crossbowman outside, and a Raider 6.01 m out
    /// (outside: not Blinded) should not be. BUG-0360.
    /// </summary>
    [Fact(Skip = "BUG-0360: the blocker hides by cell centre (2 m cells), so a unit inside the radius can stand visible and one outside hidden")]
    public void TheBlockerAndTheStatuses_AgreeOnWhoIsInside()
    {
        Simulation sim = Scene(combat: false);
        World w = sim.World;
        Vector2 centre = At(sim, 24, 24); // (49, 49)
        Place(sim, 0, Crossbowman, Off(centre, -10f));
        EntityHandle inside = Place(sim, 1, Raider, Off(centre, 5.1f, 1.1f));    // 5.2 m: its cell (27, 25) centre is 6.3 m out
        EntityHandle outside = Place(sim, 1, Raider, Off(centre, 0f, -6.01f));   // its cell (24, 21) centre is exactly 6 m: in
        Assert.True(w.Zones.Capacity > 0);
        ZoneSystem.Create(w, 1, Sandstorm, centre);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.False(w.Fog.CanSeeUnit(0, inside.Index), "a Raider inside the storm (5.2 m) is visible from outside");
        Assert.True(w.Fog.CanSeeUnit(0, outside.Index), "a Raider outside the storm (6.01 m) is hidden");
    }

    /// <summary>The current behaviour behind BUG-0360, pinned so a fix shows up: the edge disagreement both ways.</summary>
    [Fact]
    public void TheRadiusEdge_CurrentBehaviour_StatusesByCenter_BlockerByCell()
    {
        Simulation sim = Scene(combat: false);
        World w = sim.World;
        Vector2 centre = At(sim, 24, 24);
        Place(sim, 0, Crossbowman, Off(centre, -10f));
        EntityHandle inside = Place(sim, 0, Laborer, Off(centre, 5.1f, 1.1f)); // Malazan: Blinded by being inside
        EntityHandle insideRaider = Place(sim, 1, Raider, Off(centre, 5.1f, -1.1f));
        EntityHandle outsideRaider = Place(sim, 1, Raider, Off(centre, 0f, -6.01f));
        sim.Tick(); // the spatial hash learns the placed units
        ZoneSystem.Create(w, 1, Sandstorm, centre);
        Assert.Equal(Blinded, w.Units.Statuses.BlindOf(inside.Index));
        FogMaps.RunThroughNextUpdate(sim);
        Assert.True(w.Fog.CanSeeUnit(0, insideRaider.Index));   // inside the radius, yet seen from outside
        Assert.False(w.Fog.CanSeeUnit(0, outsideRaider.Index)); // outside the radius, yet hidden
    }

    // ---------- replays ----------

    private static List<int> MiddleCells(NavGrid g)
    {
        var open = new List<int>();
        for (int y = g.Height / 3; y < 2 * g.Height / 3; y++)
            for (int x = g.Width / 3; x < 2 * g.Width / 3; x++)
                if (g.IsPassable(x, y)) open.Add(y * g.Width + x);
        return open;
    }

    /// <summary>
    /// The recorder refuses a building or projectile capacity the replay format can't carry (BUG-0181); a zone capacity
    /// other than the default is the same case: playback rebuilds 64 slots, so a store-full cast makes a zone on playback
    /// that the recording didn't. BUG-0361.
    /// </summary>
    [Fact(Skip = "BUG-0361: ReplayRecorder accepts a non-default SimConfig.ZoneCapacity, which playback can't reproduce")]
    public void TheRecorder_RefusesANonDefaultZoneCapacity()
    {
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 64) with { ZoneCapacity = 1 });
        Assert.Throws<InvalidOperationException>(() => new ReplayRecorder(sim));
    }

    /// <summary>The consequence behind BUG-0361, pinned: a recorded run with ZoneCapacity 1 and a store-full cast fails its own playback.</summary>
    [Fact]
    public void ANonDefaultZoneCapacity_Recorded_FailsItsOwnPlayback()
    {
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 64) with { ZoneCapacity = 1 });
        var rec = new ReplayRecorder(sim, checkpointInterval: 10);
        NavGrid g = sim.World.NavGrid;
        List<int> open = MiddleCells(g);
        Vector2 p0 = g.CellCenter(open[0] % g.Width, open[0] / g.Width);
        Vector2 p1 = g.CellCenter(open[open.Count - 1] % g.Width, open[open.Count - 1] / g.Width);
        sim.Enqueue(Command.SpawnUnit(1, Priest, p0));
        sim.Enqueue(Command.SpawnUnit(1, Priest, p1));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        var priests = new List<EntityHandle>();
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) priests.Add(new EntityHandle(i, u.Generation[i]));
        Assert.Equal(2, priests.Count);
        sim.Enqueue(Command.UseAbility(1, priests[0], 0, u.Position[priests[0].Index]));
        sim.Enqueue(Command.UseAbility(1, priests[1], 0, u.Position[priests[1].Index]));
        for (int t = 0; t < 60; t++) sim.Tick();
        Assert.Equal(1, sim.World.Zones.Count); // the second cast found the store full
        ReplayResult result = ReplayPlayer.Run(rec.ToReplay(), TestSim.Data);
        Assert.False(result.Ok); // plays back with 64 slots: two zones, a different hash
        Assert.Equal(ReplayError.CheckpointMismatch, result.Error);
    }

    /// <summary>
    /// 70 Priests cast Sandstorm at once on a generated map (with combat): 64 zones are made (the store full), the other
    /// casts resolve without one, a second wave follows after the cooldown; twins hash-equal every tick; the recorded replay
    /// round-trips through the text format and plays back equal at every checkpoint.
    /// </summary>
    [Fact]
    public void SeventySandstorms_StoreFull_TwinsEqual_ReplayRoundTrips()
    {
        const int priests = 70;
        Simulation Make(out ReplayRecorder? r, bool record)
        {
            var s = new Simulation(TestSim.Config(Seed: 9, PlayerCount: 2, UnitCapacity: priests + 40, CommandCapacity: 1024));
            r = record ? new ReplayRecorder(s, checkpointInterval: 25) : null;
            NavGrid g = s.World.NavGrid;
            List<int> open = MiddleCells(g);
            var rng = new SimRng(9, 77);
            for (int k = 0; k < priests; k++)
            {
                int c = open[rng.NextInt(0, open.Count)];
                s.Enqueue(Command.SpawnUnit(1, Priest, g.CellCenter(c % g.Width, c / g.Width)));
            }
            for (int k = 0; k < 30; k++)
            {
                int c = open[rng.NextInt(0, open.Count)];
                s.Enqueue(Command.SpawnUnit(0, k % 2 == 0 ? Crossbowman : HeavyInfantry, g.CellCenter(c % g.Width, c / g.Width)));
            }
            return s;
        }
        Simulation a = Make(out ReplayRecorder? rec, true), b = Make(out _, false);
        int sandstorm = TestSim.Data.FindAbility("sandstorm");
        int resolved = 0, maxLive = 0;
        for (int t = 0; t < 1300; t++)
        {
            if (t == 2 || t == 950)
                foreach (Simulation s in new[] { a, b })
                {
                    UnitStore u = s.World.Units;
                    for (int i = 0; i < u.Capacity; i++)
                        if (u.Alive[i] && u.TypeId[i] == Priest)
                            s.Enqueue(Command.UseAbility(1, new EntityHandle(i, u.Generation[i]), 0, u.Position[i] + new Vector2(1f, 0f)));
                }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"twins differ after tick {t}");
            foreach (AbilityEvent e in a.World.AbilityEvents)
                if (e.Resolved && e.Ability == sandstorm) resolved++;
            maxLive = Math.Max(maxLive, a.World.Zones.Count);
            Assert.True(a.World.Zones.Count <= a.World.Zones.Capacity);
        }
        Assert.Equal(64, maxLive);
        Assert.True(resolved >= 100, $"only {resolved} Sandstorms resolved"); // two waves; Priests may die in between
        Replay replay = rec!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(result.Ok, $"replay {result.Error} at tick {result.Tick}");
    }
}
