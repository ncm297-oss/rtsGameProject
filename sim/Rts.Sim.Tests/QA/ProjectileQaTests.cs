using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-2b (session 2026-10-08-0913): attacks on the projectile hit / miss rule, splash and friendly fire, minimum
/// range, the store's overflow, and what stays out of the state hash. Every row drives the shipped data.
/// </summary>
public class ProjectileQaTests
{
    private readonly ITestOutputHelper _out;

    public ProjectileQaTests(ITestOutputHelper output) => _out = output;

    private static int Catapult => TestSim.Data.FindUnit("malazan_catapult");
    private static int Archer => TestSim.Data.FindUnit("whirlwind_desert_archer");
    private static int Tent => TestSim.Data.FindBuilding("whirlwind_tent");
    private static int Billet => TestSim.Data.FindBuilding("malazan_billet");

    /// <summary>Points <paramref name="s"/> at <paramref name="t"/> and fires one shot now (between ticks), its own swings blocked.</summary>
    private static void FireAt(World w, EntityHandle s, EntityHandle t, bool building = false)
    {
        UnitStore u = w.Units;
        u.Target[s.Index] = t;
        u.TargetIsBuilding[s.Index] = building;
        ProjectileSystem.Fire(w, s.Index);
        u.Target[s.Index] = default;
        u.TargetIsBuilding[s.Index] = false;
        u.CooldownTicks[s.Index] = 1_000_000;
    }

    // ---------- the hit / miss boundary ----------

    /// <summary>
    /// A holding Raider (radius 0.4) moved <paramref name="off"/> m off the impact point while the bolt flies: the rule is
    /// "within radius + 0.3 m", inclusive. Just inside hits, just outside misses, in every direction.
    /// </summary>
    [Theory]
    [InlineData(0.699f, true)]
    [InlineData(0.701f, false)]
    public void AimedShot_AtTheToleranceBoundary_InEveryDirection(float off, bool hits)
    {
        for (int deg = 0; deg < 360; deg += 45)
        {
            Simulation sim = Flat();
            World w = sim.World;
            UnitStore u = w.Units;
            EntityHandle s = Place(sim, 0, Crossbowman, At(sim, 20, 20));
            EntityHandle r = Place(sim, 1, Raider, At(sim, 20, 20, dx: 8f));
            sim.Enqueue(Command.HoldPosition(0, s));
            sim.Enqueue(Command.HoldPosition(1, r));
            sim.Tick();
            int full = u.Hp[r.Index];
            FireAt(w, s, r);
            float rad = deg * MathF.PI / 180f;
            u.Position[r.Index] = w.Projectiles.Target[0] + off * new Vector2(MathF.Cos(rad), MathF.Sin(rad));
            ProjectileTests.RunUntilImpact(sim);
            Assert.Equal(hits, w.Impacts[0].Hit);
            Assert.Equal(hits, u.Hp[r.Index] < full);
        }
    }

    /// <summary>
    /// Criterion 2's row "100 shots at a walking Heavy Infantry hit >= 95 %" at the criterion's own 8 m (and the
    /// Crossbowman's typical 8-15 m), every heading: docs/02 "slow units almost never [dodge]". The developer's row runs at
    /// 2-4.5 m only.
    /// </summary>
    [Theory]
    [InlineData(8f, Skip = "BUG-0183: a walking Heavy Infantry dodges every bolt past 5 m (0 / 100 at 8 m)")]
    [InlineData(12f, Skip = "BUG-0183: a walking Heavy Infantry dodges every bolt past 5 m (0 / 100 at 12 m)")]
    public void HundredShotsAtAWalkingHeavyInfantry_AtEngagementRange_HitAtLeast95Percent(float distance)
    {
        int hits = 0;
        for (int k = 0; k < 100; k++)
            if (ProjectileTests.ShotAtAWalker(HeavyInfantry, distance, k * 3.6f).Hit) hits++;
        _out.WriteLine($"walking Heavy Infantry at {distance} m, 100 headings: {hits} hits");
        Assert.True(hits >= 95, $"{hits} / 100 at {distance} m");
    }

    // ---------- deaths in flight, same-tick deaths, mutual kills ----------

    /// <summary>Two bolts land on one 1-hp Raider in the same tick: the first kills it, the second is a miss; one death, one kill.</summary>
    [Fact]
    public void TwoBoltsLandingTheSameTick_OnATargetTheFirstKills_TheSecondMisses_OneDeath()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle a = Place(sim, 0, Crossbowman, At(sim, 20, 20));
        EntityHandle b = Place(sim, 0, Crossbowman, At(sim, 28, 28));
        EntityHandle r = Place(sim, 1, Raider, At(sim, 24, 24));
        foreach ((int p, EntityHandle h) in new[] { (0, a), (0, b), (1, r) }) sim.Enqueue(Command.HoldPosition(p, h));
        sim.Tick();
        u.Hp[r.Index] = 1;
        FireAt(w, a, r);
        FireAt(w, b, r);
        Assert.Equal(w.Projectiles.TicksLeft[0], w.Projectiles.TicksLeft[1]);
        ProjectileTests.RunUntilImpact(sim);
        Assert.Equal(2, w.Impacts.Length);
        Assert.True(w.Impacts[0].Hit);
        Assert.False(w.Impacts[1].Hit);
        Assert.Equal(1, w.Deaths.Length);
        Assert.Equal(1, w.Kills[0]);
        Assert.Equal(1, w.Losses[1]);
        Assert.False(u.IsAlive(r));
    }

    /// <summary>Two 1-hp Crossbowmen fire at each other on the same tick: both bolts land, both die, kills and losses balance.</summary>
    [Fact]
    public void CrossingBolts_MutualKill_BothDie()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle a = Place(sim, 0, Crossbowman, At(sim, 20, 20));
        EntityHandle b = Place(sim, 1, Crossbowman, At(sim, 20, 20, dx: 10f));
        sim.Enqueue(Command.HoldPosition(0, a));
        sim.Enqueue(Command.HoldPosition(1, b));
        sim.Tick();
        u.Hp[a.Index] = 1;
        u.Hp[b.Index] = 1;
        FireAt(w, a, b);
        FireAt(w, b, a);
        ProjectileTests.RunUntilImpact(sim);
        Assert.Equal(2, w.Impacts.Length);
        Assert.True(w.Impacts[0].Hit && w.Impacts[1].Hit);
        Assert.False(u.IsAlive(a));
        Assert.False(u.IsAlive(b));
        Assert.Equal(2, w.Deaths.Length);
        Assert.Equal(1, w.Kills[0]);
        Assert.Equal(1, w.Kills[1]);
        Assert.Equal(1, w.Losses[0]);
        Assert.Equal(1, w.Losses[1]);
    }

    /// <summary>
    /// The shooter dies in flight and an enemy unit takes its slot: the bolt still lands for player 0, the victim does not
    /// remember the slot's new occupant as its attacker, nor retaliate on it, and a kill goes to player 0.
    /// </summary>
    [Fact]
    public void ShooterDiesInFlight_SlotReusedByAnEnemy_TheBoltStillLandsForItsOwner_AndBlamesNobody()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle s = Place(sim, 0, Crossbowman, At(sim, 20, 20));
        EntityHandle r = Place(sim, 1, Laborer, At(sim, 20, 20, dx: 12f));
        EntityHandle r2 = Place(sim, 1, Laborer, At(sim, 20, 20, dx: 12f, dy: 3f));
        sim.Enqueue(Command.HoldPosition(1, r));
        sim.Enqueue(Command.HoldPosition(1, r2));
        sim.Tick();
        FireAt(w, s, r);
        u.Free(s);
        EntityHandle squatter = Place(sim, 1, Laborer, At(sim, 5, 5));
        Assert.Equal(s.Index, squatter.Index);
        int full = u.Hp[r.Index];
        ProjectileTests.RunUntilImpact(sim);
        Assert.True(w.Impacts[0].Hit);
        Assert.Equal(0, w.Impacts[0].Owner);
        Assert.True(u.Hp[r.Index] < full);
        Assert.Equal(default, u.LastAttacker[r.Index]);
        Assert.Equal(default, u.Target[r.Index]);
        // And a killing shot from a dead shooter credits its owner.
        sim.Tick();
        EntityHandle s2 = Place(sim, 0, Crossbowman, At(sim, 20, 30));
        FireAt(w, s2, r2);
        u.Free(s2);
        u.Hp[r2.Index] = 1;
        ProjectileTests.RunUntilImpact(sim);
        Assert.False(u.IsAlive(r2));
        Assert.Equal(1, w.Kills[0]);
        Assert.Equal(1, w.Losses[1]);
    }

    // ---------- splash ----------

    /// <summary>
    /// Two stones land on one tick on the same Laborer group: every victim takes both splashes (when alive), each death is
    /// one event, and kills / losses balance.
    /// </summary>
    [Fact]
    public void TwoStonesTheSameTick_VictimsTakeBoth_AndDieOnce()
    {
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 center = At(sim, 32, 32);
        EntityHandle c1 = Place(sim, 0, Catapult, center - new Vector2(16f, 0f));
        EntityHandle c2 = Place(sim, 0, Catapult, center + new Vector2(16f, 0f));
        EntityHandle target = Place(sim, 1, Laborer, center);
        EntityHandle survivor = Place(sim, 1, HeavyInfantry, center + new Vector2(0f, 0.8f)); // 50 x 0.75 - 3 = 35 a stone
        EntityHandle weak = Place(sim, 1, Laborer, center + new Vector2(0f, -0.8f));
        foreach ((int p, EntityHandle h) in new[] { (0, c1), (0, c2), (1, target), (1, survivor), (1, weak) }) sim.Enqueue(Command.HoldPosition(p, h));
        sim.Tick();
        u.Hp[weak.Index] = 1;
        int full = u.Hp[survivor.Index];
        FireAt(w, c1, target);
        FireAt(w, c2, target);
        ProjectileTests.RunUntilImpact(sim);
        Assert.Equal(2, w.Impacts.Length);
        Assert.Equal(2 * 35, full - u.Hp[survivor.Index]);
        Assert.False(u.IsAlive(weak));
        int weakDeaths = w.Deaths.ToArray().Count(e => e.Victim == weak);
        Assert.Equal(1, weakDeaths);
        Assert.Equal(w.Deaths.Length, w.Kills[0]);
        Assert.Equal(w.Deaths.Length, w.Losses[1]);
    }

    [Theory]
    [InlineData(1, 0.5f, 1)]
    [InlineData(1, 0.25f, 1)]
    [InlineData(2, 0.25f, 1)]
    [InlineData(3, 0.25f, 1)]
    [InlineData(3, 0.5f, 2)]
    [InlineData(1, 0.0001f, 1)]
    public void Scale_FloorIsOne_ForFriendlyFireOfOne(int damage, float factor, int expected)
    {
        Assert.Equal(expected, ProjectileSystem.Scale(damage, factor));
    }

    /// <summary>
    /// A Catapult whose stone does 1 (data: attack.value 1): an own Laborer at the splash edge takes 1 x 0.5 x 0.5 and
    /// still takes the floor, 1; an enemy there takes 1.
    /// </summary>
    [Fact]
    public void FriendlyFire_OfAOneDamageStone_AtTheEdge_TakesTheFloorOfOne()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_catapult", "attack.value", "1");
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("\n", load.Errors));
        var sim = new Simulation(new SimConfig(1, 2, 16, 160) { Data = load.Data! }, LocalMovementTests.Flat(48));
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 center = At(sim, 24, 24);
        EntityHandle c = Place(sim, 0, Catapult, center - new Vector2(16f, 0f));
        EntityHandle target = Place(sim, 1, Laborer, center);
        EntityHandle own = Place(sim, 0, Laborer, center + new Vector2(0f, 2.5f));
        EntityHandle enemy = Place(sim, 1, Laborer, center + new Vector2(0f, -2.5f));
        foreach ((int p, EntityHandle h) in new[] { (0, c), (1, target), (0, own), (1, enemy) }) sim.Enqueue(Command.HoldPosition(p, h));
        sim.Tick();
        int full = u.Hp[own.Index];
        FireAt(w, c, target);
        ProjectileTests.RunUntilImpact(sim);
        Assert.Equal(1, full - u.Hp[own.Index]);
        Assert.Equal(1, full - u.Hp[enemy.Index]);
        Assert.Equal(1, full - u.Hp[target.Index]);
    }

    /// <summary>A stone landing between an enemy construction site and an own one: the enemy site takes structure damage, the own one none.</summary>
    [Fact]
    public void AStone_DamagesAnEnemySite_NeverAnOwnSite()
    {
        Simulation sim = Flat();
        World w = sim.World;
        BuildingStore b = w.Buildings;
        int width = w.NavGrid.Width;
        Assert.True(b.Spawn(1, Tent, 24 * width + 24, out EntityHandle enemySite, site: true));
        Assert.True(b.Spawn(0, Billet, 24 * width + 21, out EntityHandle ownSite, site: true));
        Assert.True(b.UnderConstruction[enemySite.Index] && b.UnderConstruction[ownSite.Index]);
        // Both sites at full test hp so the stone can't simply destroy them.
        int enemyHp = b.Hp[enemySite.Index], ownHp = b.Hp[ownSite.Index];
        EntityHandle c = Place(sim, 0, Catapult, At(sim, 23, 36));
        EntityHandle target = Place(sim, 1, Laborer, At(sim, 23, 24, dx: 0.5f)); // in the 1-cell gap between the sites
        sim.Enqueue(Command.HoldPosition(1, target));
        sim.Tick();
        FireAt(w, c, target);
        Vector2 impact = w.Projectiles.Target[0];
        Assert.True(CombatSystem.BuildingDistanceSquared(w, enemySite.Index, impact) <= 2.5f * 2.5f, "setup: enemy site out of the splash");
        Assert.True(CombatSystem.BuildingDistanceSquared(w, ownSite.Index, impact) <= 2.5f * 2.5f, "setup: own site out of the splash");
        ProjectileTests.RunUntilImpact(sim);
        _out.WriteLine($"enemy site hp {enemyHp} -> {(b.IsAlive(enemySite) ? b.Hp[enemySite.Index] : 0)}, own site hp {ownHp} -> {b.Hp[ownSite.Index]}");
        Assert.True(!b.IsAlive(enemySite) || b.Hp[enemySite.Index] < enemyHp, "the enemy site took nothing");
        Assert.True(b.IsAlive(ownSite));
        Assert.Equal(ownHp, b.Hp[ownSite.Index]);
    }

    // ---------- minimum range ----------

    /// <summary>
    /// A Raider teleported in and out of the Catapult's 6 m minimum range every <paramref name="period"/> ticks under an
    /// explicit Attack: a stone is only ever fired while the Raider is outside it, the target is kept, and over 600 ticks
    /// the Catapult still fires (not starved by the flicker).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(9)]
    public void MinRange_TargetFlickeringAcross6m_NeverFiredAtInside_AndStillFiredAt(int period)
    {
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle c = Place(sim, 0, Catapult, At(sim, 20, 32));
        float minRange = TestSim.Data.Units[Catapult].Attack.MinRange;
        float centers = minRange + u.Radius[c.Index] + TestSim.Data.Units[Raider].Radius;
        EntityHandle r = Place(sim, 1, Raider, u.Position[c.Index] + new Vector2(centers + 0.1f, 0f));
        sim.Enqueue(Command.HoldPosition(1, r));
        sim.Enqueue(Command.Attack(0, c, r, isBuilding: false));
        sim.Tick(); // the order applies on the next tick
        sim.Tick();
        Assert.Equal(r, u.Target[c.Index]);
        int fired = 0;
        for (int t = 0; t < 600; t++)
        {
            bool inside = t / period % 2 == 0;
            u.Position[r.Index] = u.Position[c.Index] + new Vector2(centers + (inside ? -0.1f : 0.1f), 0f);
            u.Hp[r.Index] = TestSim.Data.Units[Raider].Hp; // keep it alive
            int before = w.Projectiles.Count;
            sim.Tick();
            if (w.Projectiles.Count > before)
            {
                fired++;
                Assert.False(inside, $"tick {t}: fired at a target inside the minimum range");
            }
            Assert.True(r == u.Target[c.Index], $"period {period} tick {t} ({(inside ? "inside" : "outside")}): the Attack target was dropped; mode {u.Mode[c.Index]}, state {u.State[c.Index]}, queue {u.QueueCount[c.Index]}");
        }
        _out.WriteLine($"period {period}: {fired} stones in 600 ticks (cooldown {TestSim.Data.Units[Catapult].Attack.CooldownTicks})");
        Assert.True(fired >= 2, $"period {period}: only {fired} stones fired");
    }

    // ---------- overflow ----------

    /// <summary>
    /// 500 archers against 500 holding Heavy Infantry with the default store (200 slots for two players): shots past the
    /// capacity are lost, nothing throws, the store never holds more than its capacity, landings never exceed it, twins
    /// stay equal. Reports how many were lost against a store big enough.
    /// </summary>
    [Fact]
    public void FiveHundredArchers_DefaultStore_Overflows_Cleanly_AndDeterministically()
    {
        long Run(int capacity, bool twin, out int maxInFlight)
        {
            SimConfig cfg = TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: 1000, CommandCapacity: 4096) with { ProjectileCapacity = capacity };
            var a = new Simulation(cfg, LocalMovementTests.Flat(96));
            Simulation? b = twin ? new Simulation(cfg, LocalMovementTests.Flat(96)) : null;
            foreach (Simulation s in b == null ? new[] { a } : new[] { a, b })
            {
                for (int k = 0; k < 500; k++)
                {
                    // Five rows of 100, 1 m apart, the fronts 8 m apart: every archer has a Heavy Infantry within 12 m.
                    Place(s, 0, Archer, new Vector2(40f + k % 100 * 1.0f, 90f - k / 100 * 1.0f));
                    Place(s, 1, HeavyInfantry, new Vector2(40f + k % 100 * 1.0f, 98f + k / 100 * 1.0f));
                }
                UnitStore us = s.World.Units;
                for (int i = 0; i < us.Capacity; i++)
                    if (us.Alive[i]) s.Enqueue(Command.HoldPosition(us.Owner[i], new EntityHandle(i, us.Generation[i])));
            }
            maxInFlight = 0;
            for (int t = 0; t < 300; t++)
            {
                a.Tick();
                b?.Tick();
                Assert.True(a.World.Projectiles.Count <= a.World.Projectiles.Capacity);
                Assert.True(a.World.Impacts.Length <= a.World.Projectiles.Capacity);
                maxInFlight = Math.Max(maxInFlight, a.World.Projectiles.Count);
                if (b != null) Assert.Equal(a.StateHash(), b.StateHash());
            }
            long hp = 0;
            UnitStore u = a.World.Units;
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == 1) hp += u.Hp[i];
            return hp;
        }
        long def = Run(0, twin: true, out int inFlightDefault);
        long big = Run(1000, twin: false, out int inFlightBig);
        _out.WriteLine($"enemy hp left after 300 ticks: default store (200) {def}, peak {inFlightDefault} in flight; 1,000 slots {big}, peak {inFlightBig}");
        Assert.True(inFlightDefault <= 200);
        Assert.True(inFlightBig > 200, "setup: 500 archers never had more than 200 shots in the air");
    }

    // ---------- hashing / replay ----------

    /// <summary>World.Impacts is output: emptying it (as the next tick does) leaves the hash unchanged; reading the view spans too.</summary>
    [Fact]
    public void Impacts_AndViewSpans_AreNotInTheHash()
    {
        Simulation sim = Flat();
        World w = sim.World;
        EntityHandle s = Place(sim, 0, Crossbowman, At(sim, 20, 20));
        EntityHandle r = Place(sim, 1, Raider, At(sim, 20, 20, dx: 8f));
        sim.Enqueue(Command.HoldPosition(1, r));
        sim.Enqueue(Command.Attack(0, s, r, isBuilding: false));
        RunUntil(sim, () => w.Projectiles.Count == 1, 300);
        ulong inFlight = sim.StateHash();
        ProjectileStore p = w.Projectiles;
        float sum = 0;
        for (int k = 0; k < p.Capacity; k++) sum += p.Position[k].X + p.PrevPosition[k].Y + p.Target[k].X + p.ProjectileTypeId[k] + p.Owner[k] + (p.Alive[k] ? 1 : 0);
        Assert.Equal(inFlight, sim.StateHash());
        ProjectileTests.RunUntilImpact(sim);
        Assert.Equal(1, w.Impacts.Length);
        ulong landed = sim.StateHash();
        w.ClearImpacts();
        Assert.Equal(landed, sim.StateHash());
        _out.WriteLine($"span sum {sum}");
    }

    /// <summary>
    /// The replay header has no projectile-capacity line (docs/03, "a replay plays with the default"). A recorder must
    /// then refuse a sim with another capacity, as it does for the building capacity, or its replay must still play back.
    /// </summary>
    [Fact(Skip = "BUG-0181: ReplayRecorder accepts a non-default ProjectileCapacity; the replay plays with the default and fails its first checkpoint")]
    public void ARecordingOfASimWithANonDefaultProjectileCapacity_IsRefusedOrPlaysBack()
    {
        SimConfig cfg = TestSim.Config(Seed: 2, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 256) with { ProjectileCapacity = 1 };
        var sim = new Simulation(cfg);
        ReplayRecorder rec;
        try
        {
            rec = new ReplayRecorder(sim, checkpointInterval: 20);
        }
        catch (InvalidOperationException)
        {
            return; // refused: fine
        }
        int center = MoveScenario.CentralCell(sim.World.NavGrid);
        Vector2 at = MoveScenario.Center(sim.World.NavGrid, center);
        for (int k = 0; k < 6; k++)
        {
            sim.Enqueue(Command.SpawnUnit(0, Crossbowman, at + new Vector2(-6f, k - 3f)));
            sim.Enqueue(Command.SpawnUnit(1, Raider, at + new Vector2(6f, k - 3f)));
        }
        for (int t = 0; t < 200; t++) sim.Tick();
        ReplayResult played = ReplayPlayer.Run(rec.ToReplay(), TestSim.Data);
        Assert.True(played.Ok, $"recorded with ProjectileCapacity 1, played with the default: {played}");
    }

    // ---------- BUG-0156 fix scope ----------

    /// <summary>
    /// BUG-0156's fix re-picks a building-hitter "once an enemy unit has hit it" (LastAttacker set). LastAttacker is cleared
    /// only when that attacker dies, so a hit long ago by a Raider that walked away decides whether a Heavy Infantry hitting
    /// a Tent leaves it for a Laborer walking past. Control: a never-hit Heavy Infantry keeps the Tent.
    /// </summary>
    [Fact(Skip = "BUG-0180: the BUG-0156 re-pick keys on any live LastAttacker, so a long-gone attacker makes a building-hitter leave the building for a passer-by")]
    public void BuildingHitter_WithAStaleLastAttacker_BehavesLikeANeverHitOne()
    {
        bool LeavesTheTent(bool hitBefore)
        {
            Simulation sim = Flat(size: 64, units: 16);
            World w = sim.World;
            UnitStore u = w.Units;
            Assert.True(w.Buildings.Spawn(1, Tent, 30 * w.NavGrid.Width + 30, out EntityHandle tent));
            EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 28, 30));
            sim.Enqueue(Command.HoldPosition(0, a));
            sim.Tick();
            if (hitBefore)
            {
                // A real hit: an enemy archer 12 m off shoots it once, then walks far away (alive, out of sight).
                EntityHandle archer = Place(sim, 1, Archer, u.Position[a.Index] + new Vector2(0f, -12f));
                int hp = u.Hp[a.Index];
                sim.Enqueue(Command.Attack(1, archer, a, isBuilding: false));
                RunUntil(sim, () => u.Hp[a.Index] < hp, 200);
                Assert.True(u.Hp[a.Index] < hp, "setup: the archer never hit");
                sim.Enqueue(Command.Move(1, archer, new Vector2(8f, 8f)));
                RunUntil(sim, () => Vector2.Distance(u.Position[archer.Index], u.Position[a.Index]) > 40f, 600);
                Assert.Equal(archer, u.LastAttacker[a.Index]);
            }
            sim.Enqueue(Command.Attack(0, a, tent, isBuilding: true));
            int full = w.Buildings.Hp[tent.Index];
            RunUntil(sim, () => w.Buildings.Hp[tent.Index] < full, 400);
            Assert.True(w.Buildings.Hp[tent.Index] < full, "setup: never hit the Tent");
            // An attack-move on the spot: it scans and keeps the Tent.
            sim.Enqueue(Command.AttackMove(0, a, u.Position[a.Index]));
            RunUntil(sim, () => u.Mode[a.Index] == CombatMode.AttackMove && u.TargetIsBuilding[a.Index] && u.Target[a.Index] == tent, 100);
            for (int k = 0; k < 10; k++) sim.Tick(); // past the new leg's re-pick (BUG-0154)
            Assert.True(u.Mode[a.Index] == CombatMode.AttackMove && u.Target[a.Index] == tent, "setup: the attack-mover did not keep the Tent");
            Assert.Equal(hitBefore, u.LastAttacker[a.Index] != default);
            EntityHandle walker = Place(sim, 1, Laborer, u.Position[a.Index] + new Vector2(-10f, 0f));
            sim.Enqueue(Command.Move(1, walker, u.Position[a.Index] + new Vector2(-10f, 20f)));
            for (int t = 0; t < 60; t++)
            {
                sim.Tick();
                if (!u.TargetIsBuilding[a.Index] && u.Target[a.Index] == walker) return true;
            }
            return false;
        }
        bool control = LeavesTheTent(false), stale = LeavesTheTent(true);
        _out.WriteLine($"never hit: leaves the Tent = {control}; hit once long ago (attacker alive, gone): leaves = {stale}");
        Assert.Equal(control, stale);
    }
}
