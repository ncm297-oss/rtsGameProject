using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-3b criterion 2: buildings that shoot. A finished Watchtower kills an unprotected enemy walking through its 18 m; a
/// site never shoots; a tower never targets a building; a target its owner can't see (up a plateau) is ignored until a
/// spotter shows it; one shot every 2 s after a 0.4 s wind-up, from the footprint's centre, in <see cref="World.Projectiles"/>;
/// the unit priority; Ranged Weapons reaches towers; a tower's hit starts no retaliation (the victim takes the tower only
/// through its own scans, and only within its sight).
/// </summary>
public class TowerTests
{
    /// <summary>The Malazan Watchtower (player 0 plays Malazan): 2 x 2, attack 10 pierce / 2 s, range 18, sight 24.</summary>
    public static int Watchtower => TestSim.Data.FindBuilding("malazan_watchtower");

    /// <summary>The Whirlwind worker: 36 hp, Light, 4 m/s; a Move never scans, so it never fights back.</summary>
    public static int CampFollower => TestSim.Data.FindUnit("whirlwind_camp_follower");

    /// <summary>The Whirlwind House (2 x 2).</summary>
    public static int Tent => TestSim.Data.FindBuilding("whirlwind_tent");

    /// <summary>Places a building of <paramref name="type"/> for <paramref name="owner"/> anchored at cell (x, y), straight into the store (finished, or a site).</summary>
    public static EntityHandle PlaceBuilding(Simulation sim, int owner, int type, int x, int y, bool site = false)
    {
        Assert.True(sim.World.Buildings.Spawn(owner, type, y * sim.World.NavGrid.Width + x, out EntityHandle h, site));
        return h;
    }

    /// <summary>The shots in flight fired by <paramref name="owner"/>.</summary>
    private static int ShotsOf(World w, int owner)
    {
        int n = 0;
        for (int k = 0; k < w.Projectiles.Capacity; k++)
            if (w.Projectiles.Alive[k] && w.Projectiles.Owner[k] == owner) n++;
        return n;
    }

    /// <summary>The ticks on which <paramref name="owner"/> fired a new shot over <paramref name="ticks"/> ticks (a shot is new when its slot's previous position is its launch point).</summary>
    private static List<int> ShotTicks(Simulation sim, int owner, int ticks, Vector2? from = null)
    {
        World w = sim.World;
        var fired = new List<int>();
        for (int t = 0; t < ticks; t++)
        {
            int before = sim.TickNumber;
            var alive = new bool[w.Projectiles.Capacity];
            for (int k = 0; k < alive.Length; k++) alive[k] = w.Projectiles.Alive[k];
            sim.Tick();
            for (int k = 0; k < alive.Length; k++)
            {
                if (alive[k] || !w.Projectiles.Alive[k] || w.Projectiles.Owner[k] != owner) continue;
                fired.Add(before);
                if (from != null) Assert.Equal(from.Value, w.Projectiles.Position[k]); // a new shot hasn't moved yet
            }
        }
        return fired;
    }

    [Fact]
    public void AFinishedTower_KillsAnUnprotectedEnemyWalkingThroughIts18m()
    {
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        EntityHandle tower = PlaceBuilding(sim, 0, Watchtower, 30, 30); // footprint x, y 60-64 m
        // Walks east along y = 50 m (11 m south of the footprint), from 20 m west of it to 30 m east of it.
        EntityHandle walker = Place(sim, 1, CampFollower, new Vector2(40f, 50f));
        sim.Enqueue(Command.Move(1, walker, new Vector2(110f, 50f)));
        bool shot = false;
        int t = 0;
        for (; t < 400 && w.Units.IsAlive(walker); t++)
        {
            sim.Tick();
            shot |= ShotsOf(w, 0) > 0;
            if (w.Units.IsAlive(walker)) Assert.True(w.Units.Position[walker.Index].X < 100f, "it walked out of range alive");
        }
        Assert.True(shot, "the tower never fired");
        Assert.False(w.Units.IsAlive(walker), "the walker survived");
        Assert.Equal(1, w.Kills[0]);
        Assert.Equal(1, w.Losses[1]);
        Assert.True(w.Buildings.IsAlive(tower));
        Assert.Contains(w.Deaths.ToArray(), d => d.KillerOwner == 0 && d.VictimOwner == 1 && !d.IsBuilding);
    }

    [Fact]
    public void ASite_NeverShoots_AndShootsOnceFinished()
    {
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        EntityHandle site = PlaceBuilding(sim, 0, Watchtower, 30, 30, site: true);
        EntityHandle enemy = Place(sim, 1, CampFollower, new Vector2(62f, 52f)); // 8 m south of the footprint
        sim.Enqueue(Command.HoldPosition(1, enemy));
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            Assert.Equal(0, ShotsOf(w, 0));
            Assert.Equal(default, w.Buildings.TowerTarget[site.Index]);
            Assert.Equal(0, w.Buildings.TowerCooldown[site.Index]);
        }
        // Finished, it shoots.
        w.Buildings.SetWork(site.Index, w.Buildings.WorkNeeded(Watchtower));
        Assert.False(w.Buildings.UnderConstruction[site.Index]);
        Assert.NotEmpty(ShotTicks(sim, 0, 20));
    }

    [Fact]
    public void ATower_NeverTargetsABuilding()
    {
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        EntityHandle tower = PlaceBuilding(sim, 0, Watchtower, 30, 30);
        EntityHandle tent = PlaceBuilding(sim, 1, Tent, 30, 26); // 4 m south of the tower: well in range, in sight
        // An enemy unit out of range, so the tower does scan (with no enemy unit at all it skips the scan).
        EntityHandle away = Place(sim, 1, CampFollower, new Vector2(10f, 10f));
        sim.Enqueue(Command.HoldPosition(1, away));
        sim.World.Fog.Update();
        Assert.True(w.Fog.CanSeeBuilding(0, tent.Index));
        int hp = w.Buildings.Hp[tent.Index];
        for (int t = 0; t < 300; t++)
        {
            sim.Tick();
            Assert.Equal(0, ShotsOf(w, 0));
            Assert.Equal(default, w.Buildings.TowerTarget[tower.Index]);
        }
        Assert.Equal(hp, w.Buildings.Hp[tent.Index]);
        // An enemy unit beside it is shot at once: the tower looks at units only.
        EntityHandle enemy = Place(sim, 1, CampFollower, new Vector2(58f, 54f));
        sim.Enqueue(Command.HoldPosition(1, enemy));
        Assert.NotEmpty(ShotTicks(sim, 0, 20));
        Assert.Equal(enemy, w.Buildings.TowerTarget[tower.Index]);
        Assert.Equal(hp, w.Buildings.Hp[tent.Index]);
    }

    [Fact]
    public void ATargetItsOwnerCannotSee_UpAPlateau_IsIgnored_UntilASpotterShowsIt()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        // Level 0, footprint x 16-17 (32-36 m), y 30-31; the plateau starts at x 20 (40 m).
        EntityHandle tower = PlaceBuilding(sim, 0, Watchtower, 16, 30);
        EntityHandle high = Place(sim, 1, CampFollower, FogMaps.Cell(24, 31)); // 13 m east of the footprint, level 1
        sim.Enqueue(Command.HoldPosition(1, high));
        Assert.Equal(1, w.Fog.LevelAt(w.Units.Position[high.Index]));
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            Assert.Equal(0, ShotsOf(w, 0));
            Assert.Equal(default, w.Buildings.TowerTarget[tower.Index]);
        }
        Assert.False(w.Fog.IsExplored(0, 31 * FogMaps.Size + 24), "the plateau cell is explored");
        Assert.Equal(w.Units.Hp[high.Index], w.Data.Units[CampFollower].Hp);
        // A spotter of player 0 up there: the owner sees it, and the tower shoots.
        Spotter(sim, 0, FogMaps.Cell(27, 31));
        Assert.NotEmpty(ShotTicks(sim, 0, 20));
        Assert.Equal(high, w.Buildings.TowerTarget[tower.Index]);
    }

    [Fact]
    public void ATowerOnHighGround_ShootsDown()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        PlaceBuilding(sim, 0, Watchtower, 22, 30); // level 1, footprint x 44-48 m
        EntityHandle low = Place(sim, 1, CampFollower, FogMaps.Cell(14, 31)); // level 0, 15 m west
        sim.Enqueue(Command.HoldPosition(1, low));
        Assert.NotEmpty(ShotTicks(sim, 0, 30));
    }

    [Fact]
    public void OneShotEvery2s_AfterA04sWindup_FromTheFootprintCentre_InTheProjectileStore()
    {
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        EntityHandle tower = PlaceBuilding(sim, 0, Watchtower, 30, 30); // centre (62, 62)
        AttackDef attack = w.Data.Buildings[Watchtower].Attack!;
        // A sturdy target that never dies in the window: a holding Raider (108 hp; 10 pierce x 0.6 v heavy - 1 armor = 5).
        EntityHandle raider = Holder(sim, 1, Raider, new Vector2(62f, 48f)); // holding: it never walks to the tower
        int took = -1, hits = 0;
        var fired = new List<int>();
        for (int t = 0; t < 240; t++)
        {
            int tick = sim.TickNumber;
            var alive = new bool[w.Projectiles.Capacity];
            for (int k = 0; k < alive.Length; k++) alive[k] = w.Projectiles.Alive[k];
            sim.Tick();
            if (took < 0 && w.Buildings.TowerTarget[tower.Index].Generation != 0) took = tick;
            for (int k = 0; k < alive.Length; k++)
            {
                if (alive[k] || !w.Projectiles.Alive[k]) continue;
                Assert.Equal(0, w.Projectiles.Owner[k]);
                Assert.Equal(new Vector2(62f, 62f), w.Projectiles.PrevPosition[k]); // the launch point: the footprint's centre
                fired.Add(tick);
            }
            foreach (ProjectileImpact i in w.Impacts) if (i.Owner == 0 && i.Hit) hits++;
        }
        Assert.True(took >= 0 && took % CombatConstants.ScanInterval == 0, $"took its target on tick {took}"); // slot 0 scans on ticks 0, 4, 8, ...
        Assert.True(fired.Count >= 5, $"{fired.Count} shots");
        Assert.Equal(took + attack.WindupTicks, fired[0]); // the 0.4 s wind-up
        for (int k = 1; k < fired.Count; k++) Assert.Equal(attack.CooldownTicks, fired[k] - fired[k - 1]); // 2 s
        Assert.Equal(40, attack.CooldownTicks);
        Assert.True(hits >= fired.Count - 1, $"{hits} hits of {fired.Count} shots"); // a standing target: every landed shot hits
        Assert.Equal(w.Data.Units[Raider].Hp - 5 * hits, w.Units.Hp[raider.Index]);
        Assert.Equal(raider, w.Buildings.TowerTarget[tower.Index]);
    }

    /// <summary>Places a unit that holds position from now on (set in the store: no tick runs, so it never scans past its reach before the first tick).</summary>
    private static EntityHandle Holder(Simulation sim, int owner, int type, Vector2 at)
    {
        EntityHandle h = Place(sim, owner, type, at);
        sim.World.Units.Hold[h.Index] = true;
        return h;
    }

    [Fact]
    public void TheTowerPicksByTheUnitPriority_AnAttackerOfTheTowerFirst_ThenFightersBeforeWorkers_ThenNearest()
    {
        // A worker 4 m from the footprint, a Raider 12 m from it, both holding (out of their reach of the tower, so neither
        // targets it): the fighter first (tier 1 over tier 2).
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        EntityHandle tower = PlaceBuilding(sim, 0, Watchtower, 30, 30);
        Holder(sim, 1, CampFollower, new Vector2(62f, 56f));
        EntityHandle raider = Holder(sim, 1, Raider, new Vector2(62f, 48f));
        sim.Tick(); // tick 0: slot 0 scans
        Assert.Equal(raider, w.Buildings.TowerTarget[tower.Index]);

        // Two workers: the nearer one.
        Simulation s2 = Flat(size: 64);
        EntityHandle t2 = PlaceBuilding(s2, 0, Watchtower, 30, 30);
        Holder(s2, 1, CampFollower, new Vector2(62f, 48f));
        EntityHandle near = Holder(s2, 1, CampFollower, new Vector2(62f, 56f));
        s2.Tick();
        Assert.Equal(near, s2.World.Buildings.TowerTarget[t2.Index]);

        // A worker whose target is the tower beats a Raider standing nearer: tier 0.
        Simulation s3 = Flat(size: 64);
        EntityHandle t3 = PlaceBuilding(s3, 0, Watchtower, 30, 30);
        Holder(s3, 1, Raider, new Vector2(62f, 56f));
        EntityHandle w3 = Holder(s3, 1, CampFollower, new Vector2(62f, 48f));
        s3.World.Units.Target[w3.Index] = t3;
        s3.World.Units.TargetIsBuilding[w3.Index] = true;
        s3.World.Units.Mode[w3.Index] = CombatMode.Ordered;
        s3.Tick();
        Assert.Equal(w3, s3.World.Buildings.TowerTarget[t3.Index]);

        // Out of reach (18 m edge to edge), nothing: a worker 19 m off is not taken.
        Simulation s4 = Flat(size: 64);
        EntityHandle t4 = PlaceBuilding(s4, 0, Watchtower, 30, 30);
        Holder(s4, 1, CampFollower, new Vector2(62f, 40.6f)); // 19.4 m from the footprint, 19 m edge to edge
        for (int t = 0; t < 8; t++) s4.Tick();
        Assert.Equal(default, s4.World.Buildings.TowerTarget[t4.Index]);
    }

    [Fact]
    public void RangedWeapons_AddsToTheTowersAttack_MeleeWeaponsDoesNot()
    {
        GameData d = TestSim.Data;
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        int tower = Watchtower;
        Assert.Equal(0f, w.Techs.BuildingAttackBonus(0, tower));
        w.Techs.Set(0, d.FindTech("melee_weapons_1"), true);
        Assert.Equal(0f, w.Techs.BuildingAttackBonus(0, tower));
        w.Techs.Set(0, d.FindTech("ranged_weapons_1"), true);
        Assert.Equal(1f, w.Techs.BuildingAttackBonus(0, tower));
        w.Techs.Set(0, d.FindTech("ranged_weapons_2"), true);
        Assert.Equal(2f, w.Techs.BuildingAttackBonus(0, tower));
        Assert.Equal(0f, w.Techs.BuildingAttackBonus(1, tower)); // per player
        Assert.Equal(0f, w.Techs.BuildingAttackBonus(0, GatherMaps.Keep)); // no attack, no bonus
        // In a hit: (10 + 2) x 1.25 v light = 15 on a worker.
        PlaceBuilding(sim, 0, tower, 30, 30);
        EntityHandle worker = Place(sim, 1, CampFollower, new Vector2(62f, 56f));
        sim.Enqueue(Command.HoldPosition(1, worker));
        CombatScenes.RunUntil(sim, () => w.Units.Hp[worker.Index] < 36, 40);
        Assert.Equal(36 - 15, w.Units.Hp[worker.Index]);
    }

    [Fact]
    public void AUnitInSightOfTheTower_TakesItOnItsScan_AndATowerThatDiesStopsShooting()
    {
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        EntityHandle tower = PlaceBuilding(sim, 0, Watchtower, 30, 30); // footprint y 60-64 m
        // An Idle Raider (sight 14) 12 m south of the footprint: its scan finds no enemy unit and takes the tower.
        EntityHandle raider = Place(sim, 1, Raider, new Vector2(62f, 48f));
        CombatScenes.RunUntil(sim, () => w.Units.Target[raider.Index].Generation != 0, 8);
        Assert.Equal(tower, w.Units.Target[raider.Index]);
        Assert.True(w.Units.TargetIsBuilding[raider.Index]);
        float before = w.Units.Position[raider.Index].Y;
        for (int t = 0; t < 40; t++) sim.Tick();
        Assert.True(w.Units.Position[raider.Index].Y > before + 2f, "it doesn't walk to the tower");
        Assert.True(w.Units.Hp[raider.Index] < w.Data.Units[Raider].Hp, "the tower never hit it");
        Assert.Equal(default, w.Units.LastAttacker[raider.Index]); // a tower is never a unit's last attacker
        // The tower destroyed: no more shots once those in flight have landed.
        w.Buildings.Free(tower);
        for (int t = 0; t < 40; t++) sim.Tick();
        Assert.Equal(0, ShotsOf(w, 0));
    }

    [Fact]
    public void AUnitHitByATowerOutOfItsSight_DoesNotAnswerIt()
    {
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        PlaceBuilding(sim, 0, Watchtower, 30, 30);
        EntityHandle raider = Place(sim, 1, Raider, new Vector2(62f, 44f)); // sight 14, 16 m away, no spotter
        int hp = w.Units.Hp[raider.Index];
        CombatScenes.RunUntil(sim, () => w.Units.Hp[raider.Index] < hp, 120);
        Assert.True(w.Units.Hp[raider.Index] < hp, "never hit");
        Assert.Equal(default, w.Units.Target[raider.Index]);
        Assert.Equal(CombatMode.None, w.Units.Mode[raider.Index]);
    }

    [Fact]
    public void ATowerWithoutAnEnemyUnit_HoldsNoState_AndHashesAsABuildingThatNeverShot()
    {
        Simulation sim = Flat(size: 64), twin = Flat(size: 64);
        PlaceBuilding(sim, 0, Watchtower, 30, 30);
        PlaceBuilding(twin, 0, Watchtower, 30, 30);
        Place(sim, 1, CampFollower, new Vector2(10f, 10f));
        Place(twin, 1, CampFollower, new Vector2(10f, 10f));
        for (int t = 0; t < 100; t++)
        {
            sim.Tick();
            twin.Tick();
        }
        Assert.Equal(default, sim.World.Buildings.TowerTarget[0]);
        Assert.Equal(0, sim.World.Buildings.TowerCooldown[0]);
        Assert.Equal(sim.StateHash(), twin.StateHash());
    }
}
