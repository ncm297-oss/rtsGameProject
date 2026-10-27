using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S4 (docs/02 "Game flow" 2 and 4, docs/03 "Implementation (M6-S4)"): teams. A seat's team (0: its own side, 1-8: a
/// named team) makes its teammates allies through one predicate, <see cref="World.AreAllied"/>: allies never target each
/// other (scans, retaliation, towers, explicit Attacks), friendly fire treats an ally's unit as an own one (50 %) and never
/// harms its buildings, a stealthed ally is seen inside the ally's own vision, an ally's vision-blocking zone neither blinds
/// nor hides from its allies, and the last side standing wins.
/// </summary>
public class TeamTests
{
    /// <summary>Seats of the given teams: player p plays faction p mod 2, human, Normal.</summary>
    internal static ImmutableArray<PlayerSeat> Seats(params int[] teams) =>
        teams.Select((t, p) => new PlayerSeat(p % TestSim.Data.Factions.Length, SeatController.Human, AiDifficulty.Normal, t)).ToImmutableArray();

    /// <summary>A sim on a flat <paramref name="size"/> map whose seats have these teams.</summary>
    private static Simulation Flat(int size, bool combat, params int[] teams) =>
        new(TestSim.Config(Seed: 1, PlayerCount: teams.Length, UnitCapacity: 160, CommandCapacity: 2048) with { Seats = Seats(teams), Combat = combat },
            LocalMovementTests.Flat(size));

    // ---------- the predicate ----------

    [Fact]
    public void TeamOf_AndAreAllied_FollowTheSeats_Team0IsItsOwnSide()
    {
        World w = Flat(48, true, 0, 1, 1, 2, 0, 8, 8).World;
        Assert.Equal(new[] { 0, 1, 1, 2, 0, 8, 8 }, Enumerable.Range(0, 7).Select(w.TeamOf).ToArray());
        Assert.Equal(0, w.TeamOf(-1));
        Assert.Equal(0, w.TeamOf(7));
        for (int a = 0; a < 7; a++)
            for (int b = 0; b < 7; b++)
            {
                bool want = a == b || (a, b) is (1, 2) or (2, 1) or (5, 6) or (6, 5);
                Assert.True(want == w.AreAllied(a, b), $"AreAllied({a}, {b})");
                Assert.Equal(!want, w.IsEnemy(a, b));
            }
        Assert.True(w.AreAllied(0, 0) && w.AreAllied(4, 4), "a team-0 player is its own ally");
        Assert.False(w.AreAllied(0, 4), "two team-0 players are enemies");
        Assert.False(w.AreAllied(0, 99) || w.AreAllied(-1, 1) || w.AreAllied(1, 7));
        Assert.True(w.AreAllied(99, 99), "an unknown player is allied only with itself");
        Assert.Equal(new[] { false, true, true, false, false, true, true }, Enumerable.Range(0, 7).Select(w.HasAlly).ToArray());
        Assert.True(w.HasAlliances);

        World ffa = Flat(48, true, 0, 0, 0).World;
        Assert.False(ffa.HasAlliances);
        Assert.False(ffa.AreAllied(0, 1) || ffa.AreAllied(1, 2) || ffa.AreAllied(0, 2));
        World defaults = CombatScenes.Flat(players: 3).World; // no seats: every team 0
        Assert.False(defaults.HasAlliances || defaults.AreAllied(0, 1));
        Assert.Equal(0, defaults.TeamOf(2));
    }

    // ---------- combat ----------

    /// <summary>
    /// A mixed 2v2 (teams 1, 1, 2, 2) of Heavy Infantry, Crossbowmen and Catapults, each side's two players' units interleaved,
    /// attack-moving through each other: over 2,000 ticks no unit's target is ever an ally's unit or building, no unit's last
    /// attacker is an ally, and both sides lose units (the fight happened).
    /// </summary>
    [Fact]
    public void AMixed2v2Brawl_NoUnitEverTargetsAnAlly_Over2000Ticks()
    {
        Simulation sim = Flat(64, true, 1, 1, 2, 2);
        World w = sim.World;
        UnitStore u = w.Units;
        int catapult = ProjectileTests.Catapult;
        int[] types = { HeavyInfantry, Crossbowman, HeavyInfantry, catapult, Crossbowman };
        var west = new Vector2(30f, 64f);
        var east = new Vector2(98f, 64f);
        var all = new List<EntityHandle>();
        for (int k = 0; k < 20; k++)
        {
            int type = types[k % types.Length];
            var row = new Vector2(2f * (k / 5), 2.2f * (k % 5) - 4.4f);
            // Players 0 / 1 alternate in the west block, 2 / 3 in the east block.
            EntityHandle a = Place(sim, k % 2, type, west - row);
            EntityHandle b = Place(sim, 2 + k % 2, type, east + row);
            sim.Enqueue(Command.AttackMove(k % 2, a, east));
            sim.Enqueue(Command.AttackMove(2 + k % 2, b, west));
            all.Add(a);
            all.Add(b);
        }
        // Each side's two houses next to its block: buildings an ally could take.
        Assert.True(w.Buildings.Spawn(0, TowerTests.Tent, 30 * 64 + 8, out _));
        Assert.True(w.Buildings.Spawn(1, TowerTests.Tent, 33 * 64 + 8, out _));
        Assert.True(w.Buildings.Spawn(2, TowerTests.Tent, 30 * 64 + 54, out _));
        Assert.True(w.Buildings.Spawn(3, TowerTests.Tent, 33 * 64 + 54, out _));
        int targets = 0;
        for (int t = 0; t < 2000; t++)
        {
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                EntityHandle target = u.Target[i];
                if (target.Generation != 0)
                {
                    if (u.TargetIsBuilding[i])
                    {
                        if (w.Buildings.IsAlive(target)) Assert.True(w.IsEnemy(u.Owner[i], w.Buildings.Owner[target.Index]), $"tick {sim.TickNumber}: unit {i} targets an allied building");
                    }
                    else if (u.IsAlive(target))
                    {
                        targets++;
                        Assert.True(w.IsEnemy(u.Owner[i], u.Owner[target.Index]), $"tick {sim.TickNumber}: unit {i} (player {u.Owner[i]}) targets an ally's unit {target.Index} (player {u.Owner[target.Index]})");
                    }
                }
                EntityHandle last = u.LastAttacker[i];
                if (last.Generation != 0 && u.IsAlive(last)) Assert.True(w.IsEnemy(u.Owner[i], u.Owner[last.Index]), $"tick {sim.TickNumber}: unit {i}'s last attacker is an ally");
            }
        }
        Assert.True(targets > 1000, $"only {targets} unit-ticks of targeting: no fight");
        Assert.True(w.Losses[0] + w.Losses[1] > 0 && w.Losses[2] + w.Losses[3] > 0, "a side lost nothing");
    }

    [Fact]
    public void ATower_SkipsAnAllysUnit_InReach_AndTakesTheEnemyBehindIt()
    {
        Simulation sim = Flat(64, true, 1, 1, 0);
        World w = sim.World;
        UnitStore u = w.Units;
        Assert.True(w.Buildings.Spawn(0, TowerTests.Watchtower, 30 * 64 + 30, out EntityHandle tower)); // footprint 60-64 m
        EntityHandle keeper = Place(sim, 0, Laborer, At(sim, 32, 22)); // keeps player 0 in
        u.Hold[keeper.Index] = true;
        EntityHandle ally = Place(sim, 1, TowerTests.CampFollower, new Vector2(62f, 72f)); // 8 m south of the footprint
        EntityHandle enemy = Place(sim, 2, TowerTests.CampFollower, new Vector2(62f, 76f)); // 12 m south
        u.Hold[ally.Index] = u.Hold[enemy.Index] = true;
        int allyHp = u.Hp[ally.Index];
        RunUntil(sim, () => w.Buildings.TowerTargets[tower.Index].Generation != 0, 100);
        Assert.Equal(enemy, w.Buildings.TowerTargets[tower.Index]);
        RunUntil(sim, () => !u.IsAlive(enemy), 600);
        Assert.False(u.IsAlive(enemy), "the tower never killed the enemy");
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            Assert.Equal(0, w.Buildings.TowerTargets[tower.Index].Generation);
        }
        Assert.Equal(allyHp, u.Hp[ally.Index]);
    }

    /// <summary>
    /// A Catapult (player 0) fires at a holding enemy Laborer (player 2); a Laborer of <paramref name="victim"/> stands
    /// <paramref name="fraction"/> x the splash radius east of it. Returns its damage from the one stone. Teams 1, 1, 0.
    /// </summary>
    private static int StoneDamage(int victim, float fraction)
    {
        Simulation sim = Flat(48, true, 1, 1, 0);
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 center = At(sim, 24, 24);
        EntityHandle c = Place(sim, 0, ProjectileTests.Catapult, center - new Vector2(20f, 0f));
        EntityHandle target = Place(sim, 2, Laborer, center);
        float radius = TestSim.Data.Units[ProjectileTests.Catapult].Attack.Splash;
        EntityHandle v = Place(sim, victim, Laborer, center + new Vector2(fraction * radius, 0f));
        sim.Enqueue(Command.HoldPosition(2, target));
        sim.Enqueue(Command.HoldPosition(victim, v));
        Spot(sim, 0, target);
        sim.Enqueue(Command.Attack(0, c, target, isBuilding: false));
        int full = u.Hp[v.Index];
        RunUntil(sim, () => w.Projectiles.Count == 1, 300);
        ProjectileTests.RunUntilImpact(sim);
        Assert.True(w.Impacts[0].Hit);
        Assert.True(u.LastAttacker[v.Index].Generation == 0 || victim == 2, "friendly fire set a last attacker");
        return full - u.Hp[v.Index];
    }

    [Theory]
    [InlineData(0f, 13, 25)] // 50 siege x 0.5 v light = 25; an ally's at 50 %: 12.5
    [InlineData(0.7f, 9, 19)]
    [InlineData(1f, 6, 13)]
    [InlineData(1.004f, 0, 0)]
    public void ACatapultStone_HurtsAnAllysUnitAtHalf_AsAnOwnOne(float fraction, int friendly, int enemy)
    {
        Assert.Equal(friendly, StoneDamage(1, fraction)); // the ally
        Assert.Equal(friendly, StoneDamage(0, fraction)); // own, as before
        Assert.Equal(enemy, StoneDamage(2, fraction)); // the enemy, in full
    }

    [Fact]
    public void ACatapultStone_NeverDamagesAnAllysBuilding()
    {
        Simulation sim = Flat(48, true, 1, 1, 0);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        Place(sim, 1, Laborer, At(sim, 4, 4)); // keeps players 1 and 2 in
        Place(sim, 2, Laborer, At(sim, 44, 44));
        Assert.True(b.Spawn(2, TowerTests.Tent, 24 * w.NavGrid.Width + 24, out EntityHandle tent));
        Assert.True(b.Spawn(1, TestSim.Data.FindBuilding("malazan_billet"), 24 * w.NavGrid.Width + 22, out EntityHandle billet));
        EntityHandle c = Place(sim, 0, ProjectileTests.Catapult, At(sim, 24, 34, dx: -1f));
        sim.Enqueue(Command.Attack(0, c, tent, isBuilding: true));
        int tentHp = b.Hp[tent.Index], billetHp = b.Hp[billet.Index];
        RunUntil(sim, () => w.Projectiles.Count == 1, 300);
        Assert.True(CombatSystem.BuildingDistanceSquared(w, billet.Index, w.Projectiles.Target[0]) <= 2.5f * 2.5f, "setup: the Billet is not in the splash");
        ProjectileTests.RunUntilImpact(sim);
        Assert.Equal(149, tentHp - b.Hp[tent.Index]);
        Assert.Equal(billetHp, b.Hp[billet.Index]);
    }

    /// <summary>An explicit Attack on an ally's unit or building is dropped as one on an own unit; one on the enemy beside it is taken.</summary>
    [Fact]
    public void AnExplicitAttack_OnAnAllysUnitOrBuilding_IsDropped()
    {
        Simulation sim = Flat(48, true, 1, 1, 0);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle heavy = Place(sim, 0, HeavyInfantry, At(sim, 20, 20));
        EntityHandle ally = Place(sim, 1, Laborer, At(sim, 22, 20));
        u.Hold[ally.Index] = true;
        Assert.True(w.Buildings.Spawn(1, TowerTests.Tent, 16 * w.NavGrid.Width + 20, out EntityHandle tent));
        EntityHandle enemy = Place(sim, 2, Laborer, At(sim, 40, 40));
        u.Hold[enemy.Index] = true;
        int allyHp = u.Hp[ally.Index], tentHp = w.Buildings.Hp[tent.Index];
        sim.Tick();
        Assert.True(w.Fog.CanSeeUnit(0, ally.Index) && w.Fog.CanSeeBuilding(0, tent.Index), "setup: the heavy sees both");
        Assert.False(CombatSystem.MayAttack(w, heavy.Index, ally, false));
        Assert.False(CombatSystem.MayAttack(w, heavy.Index, tent, true));
        sim.Enqueue(Command.Attack(0, heavy, ally, isBuilding: false));
        sim.Tick();
        sim.Tick();
        Assert.Equal(0, u.Target[heavy.Index].Generation);
        sim.Enqueue(Command.Attack(0, heavy, tent, isBuilding: true));
        for (int t = 0; t < 100; t++)
        {
            sim.Tick();
            Assert.Equal(0, u.Target[heavy.Index].Generation);
        }
        Assert.Equal(allyHp, u.Hp[ally.Index]);
        Assert.Equal(tentHp, w.Buildings.Hp[tent.Index]);
        // The same order on the enemy is taken (spotted: it stands out of sight).
        Spot(sim, 0, enemy);
        sim.Enqueue(Command.Attack(0, heavy, enemy, isBuilding: false));
        sim.Tick();
        sim.Tick();
        Assert.Equal(enemy, u.Target[heavy.Index]);
    }

    /// <summary>
    /// An AttackMove past an ally's holding army walks on, never taking an ally, and strikes the enemy that stands beyond it.
    /// </summary>
    [Fact]
    public void AnAttackMove_ThroughAnAlliedArmy_WalksOnAndStrikesTheEnemyBehindIt()
    {
        Simulation sim = Flat(64, true, 1, 1, 0);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle heavy = Place(sim, 0, HeavyInfantry, new Vector2(20f, 64f));
        var allies = new List<EntityHandle>();
        for (int k = 0; k < 6; k++)
        {
            EntityHandle a = Place(sim, 1, HeavyInfantry, new Vector2(44f + 2f * (k / 3), 61.5f + 2.5f * (k % 3))); // across its path
            u.Hold[a.Index] = true;
            allies.Add(a);
        }
        EntityHandle enemy = Place(sim, 2, Laborer, new Vector2(80f, 64f));
        u.Hold[enemy.Index] = true;
        int enemyHp = u.Hp[enemy.Index];
        sim.Enqueue(Command.AttackMove(0, heavy, new Vector2(100f, 64f)));
        bool struck = false;
        for (int t = 0; t < 1500 && !struck; t++)
        {
            sim.Tick();
            EntityHandle target = u.Target[heavy.Index];
            Assert.False(allies.Contains(target) && !u.TargetIsBuilding[heavy.Index], $"tick {sim.TickNumber}: the heavy took an ally");
            foreach (EntityHandle a in allies) Assert.False(u.Target[a.Index] == heavy && !u.TargetIsBuilding[a.Index], "an ally took the heavy");
            struck = !u.IsAlive(enemy) || u.Hp[enemy.Index] < enemyHp;
        }
        Assert.True(struck, "the heavy never struck the enemy beyond its ally's army");
        foreach (EntityHandle a in allies) Assert.Equal(TestSim.Data.Units[HeavyInfantry].Hp, u.Hp[a.Index]);
    }

    // ---------- vision ----------

    /// <summary>
    /// A stealthed unit of player 0 stands 8 m from its ally's (player 1) Infantry and 8 m from an enemy's (player 2), outside
    /// the 3 m proximity: the ally sees it (inside its own vision), the enemy doesn't; a second ally (player 3) far away doesn't
    /// see it either (no shared vision, M6-S5).
    /// </summary>
    [Fact]
    public void AStealthedAlly_IsSeenInsideTheAllysVision_AndHiddenFromTheEnemy()
    {
        var config = new SimConfig(1, 4, 64, 544) { Data = StealthTests.Data, Seats = Seats(1, 1, 2, 1) };
        var sim = new Simulation(config, LocalMovementTests.Flat(64));
        World w = sim.World;
        Vector2 p = At(sim, 20, 20);
        int worker = StealthTests.Data.FindUnit("whirlwind_camp_follower"), infantry = StealthTests.Data.FindUnit("malazan_heavy_infantry");
        EntityHandle hidden = Place(sim, 0, worker, p);
        EntityHandle ally = Place(sim, 1, infantry, p + new Vector2(8f, 0f));
        EntityHandle enemy = Place(sim, 2, infantry, p - new Vector2(8f, 0f));
        Place(sim, 3, infantry, At(sim, 56, 56));
        w.Units.Hold[ally.Index] = w.Units.Hold[enemy.Index] = true;
        StealthTests.Stealth(sim, hidden);
        for (int t = 0; t < 8; t++) sim.Tick();
        Assert.True(w.IsStealthed(hidden.Index));
        int cell = w.Fog.CellOf(p);
        Assert.True(w.Fog.IsVisible(1, cell) && w.Fog.IsVisible(2, cell), "setup: both see its cell");
        Assert.True(w.Fog.CanSeeUnit(1, hidden.Index), "the ally doesn't see its stealthed ally");
        Assert.True(new PlayerView(w, 1).TryGetUnit(hidden.Index, out SeenUnit seen) && seen.Owner == 0);
        Assert.False(w.Fog.CanSeeUnit(2, hidden.Index), "the enemy sees the stealthed unit");
        Assert.False(new PlayerView(w, 2).TryGetUnit(hidden.Index, out _));
        Assert.False(w.Fog.CanSeeUnit(3, hidden.Index), "an ally far away sees it: shared vision is M6-S5");
        Assert.False(w.IsDetectedBy(1, hidden.Index), "an ally needs no detection");
    }

    /// <summary>
    /// A Sandstorm of player 1 (teams 1, 1, 0, 0): its ally's (player 0) unit inside is neither Blinded nor slowed and its ally
    /// sees into it; an enemy's (player 3) unit inside is Blinded, and the enemy doesn't see into it.
    /// </summary>
    [Fact]
    public void AnAllysSandstorm_NeitherBlindsNorHidesFromItsAlly()
    {
        Simulation sim = Flat(48, false, 1, 1, 0, 0);
        World w = sim.World;
        UnitStore u = w.Units;
        GameData d = w.Data;
        Vector2 point = At(sim, 15, 24);
        EntityHandle priest = Place(sim, 1, ZoneSystemTests.Priest, At(sim, 10, 24));
        EntityHandle allyInside = Place(sim, 0, Crossbowman, point + new Vector2(2f, 0f));
        EntityHandle enemyInside = Place(sim, 3, Crossbowman, point + new Vector2(-4f, 3f)); // 6.4 m from the target: past a blind's 2 m
        EntityHandle target = Place(sim, 2, Laborer, point + new Vector2(0f, -2f)); // a third party's unit in the storm
        EntityHandle allyOut = Place(sim, 0, Crossbowman, point + new Vector2(0f, 10f)); // both 10 m south: in sight, outside
        EntityHandle enemyOut = Place(sim, 3, Crossbowman, point + new Vector2(1f, 10f));
        foreach (EntityHandle h in new[] { allyInside, enemyInside, target, allyOut, enemyOut }) u.Hold[h.Index] = true;
        sim.Enqueue(Command.UseAbility(1, priest, 0, point));
        RunUntil(sim, () => w.Zones.Count > 0, 200);
        Assert.Equal(1, w.Zones.Count);
        for (int t = 0; t < 8; t++) sim.Tick();
        int blinded = d.FindStatus("blinded");
        Assert.True(u.Statuses.IndexOf(enemyInside.Index, blinded) >= 0, "setup: the enemy inside is not blinded");
        Assert.True(u.Statuses.IndexOf(allyInside.Index, blinded) < 0, "the storm blinded its owner's ally");
        Assert.Equal(0, u.Statuses.Count[allyInside.Index]);
        // Seeing into it: player 0 (the ally) from outside sees the third party's unit inside; player 3 (an enemy) doesn't.
        Assert.True(w.Fog.CanSeeUnit(0, target.Index), "the ally's storm hid a unit inside it from the ally");
        Assert.False(w.Fog.CanSeeUnit(3, target.Index), "setup: the enemy sees into the storm");
        Assert.True(VisionSystem.UnitSeesUnit(w, allyOut.Index, target.Index));
        Assert.False(VisionSystem.UnitSeesUnit(w, enemyOut.Index, target.Index));
    }

    // ---------- the result ----------

    /// <summary>A flat sim (combat off) where each player has a House and one worker far from the others; returns the workers.</summary>
    private static (Simulation Sim, EntityHandle[] Workers) Sides(params int[] teams)
    {
        Simulation sim = Flat(64, false, teams);
        World w = sim.World;
        int house = TestSim.Data.FindBuilding("malazan_billet");
        var workers = new EntityHandle[teams.Length];
        for (int p = 0; p < teams.Length; p++)
        {
            int x = 6 + 12 * p, y = 10;
            Assert.True(w.Buildings.Spawn(p, house, y * 64 + x, out _));
            workers[p] = Place(sim, p, Laborer, At(sim, x, 40));
        }
        sim.Tick();
        for (int p = 0; p < teams.Length; p++) Assert.True(w.EverBuilt(p));
        Assert.False(sim.MatchOver);
        return (sim, workers);
    }

    /// <summary>Frees player <paramref name="p"/>'s worker and runs the tick that defeats it.</summary>
    private static void Out(Simulation sim, EntityHandle[] workers, int p)
    {
        sim.World.Units.Free(workers[p]);
        sim.Tick();
        Assert.True(sim.World.IsDefeated(p), $"player {p} is not out");
    }

    [Fact]
    public void TwoVersusTwo_OneOfEachSideOut_NotOver_BothOfOneSide_Over()
    {
        (Simulation sim, EntityHandle[] workers) = Sides(1, 1, 2, 2);
        Out(sim, workers, 0);
        Assert.False(sim.MatchOver);
        Out(sim, workers, 3);
        Assert.False(sim.MatchOver, "one player of each side out ended the match");
        Out(sim, workers, 1);
        Assert.True(sim.MatchOver);
        Assert.Equal(new[] { true, true, false, true }, Enumerable.Range(0, 4).Select(sim.World.IsDefeated).ToArray());
        Assert.True(sim.World.MatchOverLatched);
        for (int t = 0; t < 10; t++) sim.Tick();
        Assert.True(sim.MatchOver);
    }

    [Fact]
    public void FreeForAll_IsUnchanged_TheLastPlayerStanding()
    {
        (Simulation sim, EntityHandle[] workers) = Sides(0, 0, 0, 0);
        Out(sim, workers, 0);
        Out(sim, workers, 1);
        Assert.False(sim.MatchOver);
        Out(sim, workers, 2);
        Assert.True(sim.MatchOver);
    }

    [Fact]
    public void ThreePlayers_TwoVersusOne()
    {
        // The lone player falls: the pair wins.
        (Simulation sim, EntityHandle[] workers) = Sides(1, 1, 0);
        Out(sim, workers, 2);
        Assert.True(sim.MatchOver);
        // The pair falls one by one: over only with the second.
        (sim, workers) = Sides(1, 1, 0);
        Out(sim, workers, 0);
        Assert.False(sim.MatchOver);
        Out(sim, workers, 1);
        Assert.True(sim.MatchOver);
    }

    [Fact]
    public void FourPlayers_OneVersusOneVersusTwo()
    {
        (Simulation sim, EntityHandle[] workers) = Sides(0, 0, 1, 1);
        Out(sim, workers, 2);
        Assert.False(sim.MatchOver);
        Out(sim, workers, 0);
        Assert.False(sim.MatchOver, "player 1 and player 3 still stand on two sides");
        Out(sim, workers, 1);
        Assert.True(sim.MatchOver);
        Assert.False(sim.World.IsDefeated(3));
    }

    [Fact]
    public void OnlyAlliesBuilt_IsNoResult_UntilAnEnemyBuilds()
    {
        Simulation sim = Flat(64, false, 1, 1, 2);
        World w = sim.World;
        int house = TestSim.Data.FindBuilding("malazan_billet");
        Assert.True(w.Buildings.Spawn(0, house, 10 * 64 + 6, out _));
        Assert.True(w.Buildings.Spawn(1, house, 10 * 64 + 18, out _));
        EntityHandle worker = Place(sim, 0, Laborer, At(sim, 6, 40));
        Place(sim, 1, Laborer, At(sim, 18, 40));
        for (int t = 0; t < 5; t++) sim.Tick();
        Assert.False(sim.MatchOver, "two allies that built are one side: no result");
        Assert.True(w.Buildings.Spawn(2, house, 10 * 64 + 40, out _));
        EntityHandle enemy = Place(sim, 2, Laborer, At(sim, 40, 40));
        sim.Tick();
        Assert.False(sim.MatchOver);
        w.Units.Free(enemy);
        sim.Tick();
        Assert.True(w.IsDefeated(2));
        Assert.True(sim.MatchOver);
        Assert.False(w.IsDefeated(0) || w.IsDefeated(1));
        _ = worker;
    }
}
