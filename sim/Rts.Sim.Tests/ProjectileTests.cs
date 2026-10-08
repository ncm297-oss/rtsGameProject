using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-2b criterion 2 (docs/02 "Projectiles", docs/03 "Implementation (M4-2b)"): a ranged attack fires at its wind-up end,
/// the bolt flies a straight line to where the target was (led, for a unit walking no faster than the projectile's lead
/// speed: BUG-0183) and lands after <c>ceil(distance / step)</c> ticks; an aimed shot hits only a target still within its radius + the hit tolerance of the impact point; a lob always explodes; the
/// view's spans and <see cref="World.Impacts"/>.
/// </summary>
public class ProjectileTests
{
    private readonly ITestOutputHelper _out;

    public ProjectileTests(ITestOutputHelper output) => _out = output;

    public static int Catapult => TestSim.Data.FindUnit("malazan_catapult");
    public static int HorseRaider => TestSim.Data.FindUnit("whirlwind_horse_raider");
    private static int Bolt => TestSim.Data.FindProjectile("bolt");

    /// <summary>Runs ticks until a projectile lands (<see cref="World.Impacts"/> non-empty) or <paramref name="max"/> ticks; returns the ticks run.</summary>
    public static int RunUntilImpact(Simulation sim, int max = 200) => RunUntil(sim, () => sim.World.Impacts.Length > 0, max);

    /// <summary>A Crossbowman and a holding Raider 8 m apart (centers), as in docs/02's worked example.</summary>
    private static (Simulation Sim, EntityHandle Shooter, EntityHandle Raider) WorkedExample()
    {
        Simulation sim = Flat();
        EntityHandle x = Place(sim, 0, Crossbowman, At(sim, 20, 20));
        EntityHandle r = Place(sim, 1, Raider, At(sim, 20, 20, dx: 8f));
        sim.Enqueue(Command.HoldPosition(1, r));
        return (sim, x, r);
    }

    [Fact]
    public void CrossbowmanAt8m_FiresAtTheWindupEnd_TheBoltArrivesAfterCeil8Over25Over005Ticks_AndTheRaiderTakesTheWorkedExample()
    {
        (Simulation sim, EntityHandle x, EntityHandle r) = WorkedExample();
        World w = sim.World;
        UnitStore u = w.Units;
        int full = u.Hp[r.Index];
        AttackDef attack = TestSim.Data.Units[Crossbowman].Attack;
        int swing = -1, fired = -1, landed = -1;
        for (int t = 1; t <= 300 && landed < 0; t++)
        {
            sim.Tick();
            if (swing < 0 && u.WindupTicks[x.Index] == attack.WindupTicks) swing = t;
            if (fired < 0 && w.Projectiles.Count == 1) fired = t;
            if (u.Hp[r.Index] < full) landed = t;
            if (fired > 0 && landed < 0) Assert.Equal(full, u.Hp[r.Index]); // nothing before it lands
        }
        _out.WriteLine($"swing tick {swing}, fired {fired}, landed {landed}");
        Assert.True(swing > 0 && fired > 0 && landed > 0);
        Assert.Equal(swing + attack.WindupTicks, fired); // the wind-up end
        Assert.Equal((int)Math.Ceiling(8.0 / 25.0 / 0.05), landed - fired); // 7
        Assert.Equal(7, landed - fired);
        // docs/02: 9 x 0.6 (pierce v heavy) x 1.3 (bonus v heavy) = 7.02 -> 7, less armor 1 = 6.
        Assert.Equal(6, full - u.Hp[r.Index]);
        Assert.Equal(x, u.LastAttacker[r.Index]);
        Assert.Equal(0, w.Projectiles.Count);
    }

    [Fact]
    public void TheViewSpans_ShowTheLaunchTheStepsAndTheImpact_AndImpactsHoldOneTickOnly()
    {
        (Simulation sim, EntityHandle x, EntityHandle r) = WorkedExample();
        World w = sim.World;
        ProjectileStore p = w.Projectiles;
        Assert.Equal(64, p.Capacity); // the default: the shooters two population caps allow (200), at most the 64 unit slots
        RunUntil(sim, () => p.Count == 1, 300);
        int k = p.Alive.IndexOf(true);
        Vector2 from = w.Units.Position[x.Index], to = w.Units.Position[r.Index];
        Assert.Equal(0, k);
        Assert.Equal(from, p.Position[k]);
        Assert.Equal(from, p.PrevPosition[k]);
        Assert.Equal(to, p.Target[k]);
        Assert.Equal(Bolt, p.ProjectileTypeId[k]);
        Assert.Equal(0, p.Owner[k]);
        sim.Tick();
        Assert.Equal(from, p.PrevPosition[k]);
        Assert.Equal(1.25f, Vector2.Distance(from, p.Position[k]), 4); // 25 m/s x 0.05 s
        Assert.True(w.Impacts.IsEmpty);
        RunUntilImpact(sim);
        Assert.Equal(1, w.Impacts.Length);
        Assert.Equal(new ProjectileImpact(to, Bolt, 0, true), w.Impacts[0]);
        Assert.False(p.Alive[k]);
        sim.Tick();
        Assert.True(w.Impacts.IsEmpty);
    }

    /// <summary>
    /// Waits for the worked example's bolt, then moves the Raider <paramref name="off"/> m sideways off the impact point on
    /// the tick before it lands (after its last re-lead: a shot tracks a slow target, BUG-0183).
    /// </summary>
    [Theory]
    [InlineData(0.65f, true)]
    [InlineData(0.75f, false)]
    [InlineData(3f, false)]
    public void AnAimedShot_HitsOnlyATargetWithinItsRadiusPlusTheToleranceOfTheImpactPoint(float off, bool hits)
    {
        (Simulation sim, EntityHandle x, EntityHandle r) = WorkedExample();
        World w = sim.World;
        UnitStore u = w.Units;
        int full = u.Hp[r.Index];
        RunUntil(sim, () => w.Projectiles.Count == 1, 300);
        RunUntil(sim, () => w.Projectiles.TicksLeft[0] == 1, 20);
        u.Position[r.Index] += new Vector2(0f, off); // 0.4 m radius + 0.3 m tolerance = 0.7 m
        RunUntilImpact(sim);
        Assert.Equal(hits, w.Impacts[0].Hit);
        Assert.Equal(hits ? 6 : 0, full - u.Hp[r.Index]);
        Assert.Equal(hits ? x : default, u.LastAttacker[r.Index]); // a miss is no attack: no event, no retaliation
        Assert.True(w.Deaths.IsEmpty);
        Assert.Equal(0, w.Projectiles.Count); // a miss lands too, harmlessly
    }

    [Fact]
    public void AShotAtATargetThatDiedInFlight_Misses()
    {
        (Simulation sim, _, EntityHandle r) = WorkedExample();
        World w = sim.World;
        RunUntil(sim, () => w.Projectiles.Count == 1, 300);
        w.Units.Free(r);
        EntityHandle other = Place(sim, 1, Raider, w.Projectiles.Target[0]); // the slot reused, at the impact point
        Assert.Equal(r.Index, other.Index);
        int full = w.Units.Hp[other.Index];
        RunUntilImpact(sim);
        Assert.False(w.Impacts[0].Hit);
        Assert.Equal(full, w.Units.Hp[other.Index]);
    }

    /// <summary>
    /// One shot from a holding Crossbowman (its own swings blocked) at a <paramref name="type"/> walking in direction
    /// <paramref name="headingDeg"/> (0 = straight away from the shooter, 90 = across, 180 = toward it) that is
    /// <paramref name="distance"/> m away when the bolt is fired. Returns whether the bolt hit, and the distance at firing.
    /// </summary>
    public static (bool Hit, float Distance) ShotAtAWalker(int type, float distance, float headingDeg) =>
        ShotAtAWalker(type, distance, headingDeg, 12);

    /// <summary>As <see cref="ShotAtAWalker(int, float, float)"/>, the target walking <paramref name="warmup"/> ticks before the shot.</summary>
    public static (bool Hit, float Distance) ShotAtAWalker(int type, float distance, float headingDeg, int warmup)
    {
        Simulation sim = Flat(size: 64, units: 8);
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 shooterAt = At(sim, 32, 32);
        EntityHandle s = Place(sim, 0, Crossbowman, shooterAt);
        u.CooldownTicks[s.Index] = 1_000_000; // only the test's shot
        sim.Enqueue(Command.HoldPosition(0, s));
        float rad = headingDeg * MathF.PI / 180f;
        var dir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
        float step = TestSim.Data.Units[type].SpeedPerTick;
        Vector2 atFire = shooterAt + new Vector2(distance, 0f);
        Vector2 start = atFire - dir * (step * warmup);
        EntityHandle t = Place(sim, 1, type, start);
        // 30 m: past the warm-up and the longest flight even at a gallop, and on the 128 m map from the longest shot.
        sim.Enqueue(Command.Move(1, t, start + dir * 30f));
        for (int k = 0; k < warmup; k++) sim.Tick();
        Assert.Equal(UnitState.Moving, u.State[t.Index]);
        u.Target[s.Index] = t;
        u.TargetIsBuilding[s.Index] = false;
        ProjectileSystem.Fire(w, s.Index);
        float d = Vector2.Distance(u.Position[s.Index], u.Position[t.Index]);
        RunUntilImpact(sim, 60);
        Assert.Equal(1, w.Impacts.Length);
        return (w.Impacts[0].Hit, d);
    }

    /// <summary>A walking Heavy Infantry close in (2 to 4.5 m), 100 shots, every heading: hit at least 95 % (criterion 2).</summary>
    [Fact]
    public void HundredShotsAtAWalkingHeavyInfantry_InEveryDirection_Within4Point5m_HitAtLeast95Percent()
    {
        int hits = 0;
        for (int k = 0; k < 100; k++)
            if (ShotAtAWalker(HeavyInfantry, 2f + k % 6 * 0.5f, k * 3.6f).Hit) hits++;
        _out.WriteLine($"walking Heavy Infantry, 2-4.5 m, 100 headings: {hits} hits");
        Assert.True(hits >= 95, $"{hits} / 100");
    }

    /// <summary>
    /// The crossbow's longest shot: its range plus both radii plus the wind-up grace (the target may step that far out
    /// while the bolt is drawn), center to center.
    /// </summary>
    public static float MaxShot(int target) =>
        TestSim.Data.Units[Crossbowman].Attack.Range + TestSim.Data.Units[Crossbowman].Radius + TestSim.Data.Units[target].Radius
        + CombatConstants.WindupGrace;

    /// <summary>
    /// Criterion 2 at engagement range (BUG-0183): a walking Heavy Infantry (3 m/s, under the bolt's 5 m/s lead speed) is
    /// led, so 100 shots in every heading at 8 m, 12 m and the crossbow's longest shot hit at least 95 %. Before the lead
    /// rule it dodged every one past 5 m.
    /// </summary>
    [Theory]
    [InlineData(8f)]
    [InlineData(12f)]
    [InlineData(-1f)] // the crossbow's longest shot (MaxShot)
    public void HundredShotsAtAWalkingHeavyInfantry_InEveryDirection_AtEngagementRange_HitAtLeast95Percent(float distance)
    {
        if (distance < 0f) distance = MaxShot(HeavyInfantry);
        int hits = 0;
        for (int k = 0; k < 100; k++)
            if (ShotAtAWalker(HeavyInfantry, distance, k * 3.6f).Hit) hits++;
        _out.WriteLine($"walking Heavy Infantry at {distance} m, 100 headings: {hits} hits");
        Assert.True(hits >= 95, $"{hits} / 100 at {distance} m");
    }

    /// <summary>A galloping Horse Raider (6.6 m/s, over the lead speed) crossing at 5 m, 6 m, 8 m and the longest shot: under 50 % at each.</summary>
    [Theory]
    [InlineData(5f)]
    [InlineData(6f)]
    [InlineData(8f)]
    [InlineData(-1f)]
    public void AGallopingHorseRaiderCrossing_IsNotLed_AndDodgesMostShots(float distance)
    {
        if (distance < 0f) distance = MaxShot(HorseRaider);
        int hits = 0;
        for (int k = 0; k < 100; k++)
        {
            float heading = (k % 2 == 0 ? 90f : 270f) + (k % 7 - 3) * 5f; // across the line of fire, +-15 degrees
            if (ShotAtAWalker(HorseRaider, distance, heading).Hit) hits++;
        }
        _out.WriteLine($"galloping Horse Raider crossing at {distance} m: {hits} hits");
        Assert.True(hits < 50, $"{hits} / 100 at {distance} m");
    }

    [Fact]
    public void Lead_AimsWhereASlowWalkerWillBe_ButNotAFastOneOrAStandingOne()
    {
        ProjectileDef bolt = TestSim.Data.Projectiles[Bolt];
        Assert.Equal(5f / 20f, bolt.LeadSpeedPerTick, 5); // data: 5 m/s
        Vector2 from = Vector2.Zero, at = new(10f, 0f);
        Vector2 walk = new(0f, 0.15f); // 3 m/s across
        Vector2 led = ProjectileSystem.Lead(from, at, walk, bolt);
        int flight = (int)MathF.Ceiling(Vector2.Distance(from, led) / bolt.SpeedPerTick);
        Assert.Equal(at + walk * flight, led); // where it will be when the bolt lands
        // Exactly the lead speed is led; just over it is not.
        Assert.NotEqual(at, ProjectileSystem.Lead(from, at, new Vector2(0f, bolt.LeadSpeedPerTick), bolt));
        Assert.Equal(at, ProjectileSystem.Lead(from, at, new Vector2(0f, bolt.LeadSpeedPerTick * 1.01f), bolt));
        Assert.Equal(at, ProjectileSystem.Lead(from, at, new Vector2(0f, 0.33f), bolt)); // a galloping Horse Raider
        Assert.Equal(at, ProjectileSystem.Lead(from, at, Vector2.Zero, bolt));
    }

    /// <summary>
    /// A led shot is re-led every tick it flies (BUG-0183): a Heavy Infantry that turns 90 degrees just after the bolt is
    /// fired is still hit, and the bolt lands on the tick fixed at firing. Led only at firing it would land about 1.4 m
    /// off its course (12 ticks x 0.15 m x sqrt 2 x ...) and miss.
    /// </summary>
    [Fact]
    public void ALedShot_FollowsAWalkerThatTurnsInFlight()
    {
        Simulation sim = Flat(size: 64, units: 8);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle s = Place(sim, 0, Crossbowman, At(sim, 20, 32));
        u.CooldownTicks[s.Index] = 1_000_000;
        sim.Enqueue(Command.HoldPosition(0, s));
        EntityHandle t = Place(sim, 1, HeavyInfantry, At(sim, 27, 28));
        sim.Enqueue(Command.Move(1, t, At(sim, 27, 50)));
        for (int k = 0; k < 10; k++) sim.Tick();
        u.Target[s.Index] = t;
        ProjectileSystem.Fire(w, s.Index);
        int ticks = w.Projectiles.TicksLeft[0];
        Vector2 firstAim = w.Projectiles.Target[0];
        Assert.True(ticks >= 10, $"setup: a {ticks}-tick flight");
        sim.Enqueue(Command.Move(1, t, u.Position[t.Index] + new Vector2(30f, 0f))); // turns from walking +y to +x
        int ran = RunUntilImpact(sim, 60);
        Assert.Equal(ticks, ran);
        Assert.True(Vector2.Distance(firstAim, u.Position[t.Index]) > 0.7f + 0.4f, "setup: the turn would have dodged the first aim");
        Assert.True(w.Impacts[0].Hit);
    }

    /// <summary>A galloping Horse Raider is not re-led either: the bolt keeps the course it was fired on.</summary>
    [Fact]
    public void AShotAtAGallopingUnit_KeepsItsCourse()
    {
        Simulation sim = Flat(size: 64, units: 8);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle s = Place(sim, 0, Crossbowman, At(sim, 20, 32));
        u.CooldownTicks[s.Index] = 1_000_000;
        sim.Enqueue(Command.HoldPosition(0, s));
        EntityHandle t = Place(sim, 1, HorseRaider, At(sim, 25, 28));
        sim.Enqueue(Command.Move(1, t, At(sim, 25, 50)));
        for (int k = 0; k < 10; k++) sim.Tick();
        u.Target[s.Index] = t;
        ProjectileSystem.Fire(w, s.Index);
        Vector2 aim = w.Projectiles.Target[0];
        Assert.Equal(u.Position[t.Index], aim);
        for (int k = 0; k < 3; k++)
        {
            sim.Tick();
            Assert.Equal(aim, w.Projectiles.Target[0]);
        }
    }

    [Fact]
    public void ALobIsNeverLed_EvenAtASlowWalker()
    {
        Simulation sim = Flat(size: 64, units: 8);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle c = Place(sim, 0, Catapult, At(sim, 20, 32));
        EntityHandle t = Place(sim, 1, HeavyInfantry, At(sim, 32, 32));
        sim.Enqueue(Command.Move(1, t, At(sim, 32, 60)));
        for (int k = 0; k < 10; k++) sim.Tick();
        Assert.NotEqual(Vector2.Zero, u.Velocity[t.Index]);
        u.Target[c.Index] = t;
        ProjectileSystem.Fire(w, c.Index);
        Assert.Equal(u.Position[t.Index], w.Projectiles.Target[0]);
    }

    [Fact]
    public void HundredShotsAtAGallopingHorseRaiderCrossing_At5To15m_HitUnder50Percent()
    {
        int hits = 0;
        for (int k = 0; k < 100; k++)
        {
            float heading = (k % 2 == 0 ? 90f : 270f) + (k % 7 - 3) * 5f; // across the line of fire, +-15 degrees
            if (ShotAtAWalker(HorseRaider, 5f + k % 11, heading).Hit) hits++;
        }
        _out.WriteLine($"galloping Horse Raider crossing at 5-15 m: {hits} hits");
        Assert.True(hits < 50, $"{hits} / 100");
    }

    /// <summary>
    /// Report row (not a gate): hit rates by distance for a walking Heavy Infantry (across, toward, away) and a
    /// galloping Horse Raider (across, toward), with the shipped numbers (25 m/s, radius + 0.3 m, lead up to 5 m/s); the
    /// last column is the crossbow's longest shot. For the data track's balance pass.
    /// </summary>
    [Fact]
    public void HitRateByDistance_Report()
    {
        foreach ((string name, int type, float[] headings) in new[]
        {
            ("Heavy Infantry across", HeavyInfantry, new[] { 80f, 90f, 100f, 260f, 270f, 280f }),
            ("Heavy Infantry toward", HeavyInfantry, new[] { 170f, 180f, 190f }),
            ("Heavy Infantry away", HeavyInfantry, new[] { -10f, 0f, 10f }),
            ("Horse Raider across", HorseRaider, new[] { 80f, 90f, 100f, 260f, 270f, 280f }),
            ("Horse Raider toward", HorseRaider, new[] { 170f, 180f, 190f }),
        })
        {
            var line = new System.Text.StringBuilder(name + ":");
            foreach (float d in new[] { 3f, 4f, 5f, 6f, 8f, 10f, 12f, 15f, MaxShot(type) })
            {
                int hits = headings.Count(h => ShotAtAWalker(type, d, h).Hit);
                line.Append($" {d} m {100 * hits / headings.Length}%");
            }
            _out.WriteLine(line.ToString());
        }
    }

    [Fact]
    public void ACatapultStone_AlwaysExplodesAtTheImpactPoint_EvenWhenItsTargetLeft()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle c = Place(sim, 0, Catapult, At(sim, 10, 20));
        EntityHandle target = Place(sim, 1, Laborer, At(sim, 10, 20, dx: 15f));
        sim.Enqueue(Command.HoldPosition(1, target));
        sim.Enqueue(Command.Attack(0, c, target, isBuilding: false));
        RunUntil(sim, () => w.Projectiles.Count == 1, 300);
        Vector2 impact = w.Projectiles.Target[0];
        Assert.Equal(TestSim.Data.FindProjectile("catapult_stone"), w.Projectiles.ProjectileTypeId[0]);
        u.Position[target.Index] += new Vector2(0f, 5f); // the target walks off
        EntityHandle bystander = Place(sim, 1, Laborer, impact); // someone else stands there
        int full = u.Hp[bystander.Index];
        int ticks = RunUntilImpact(sim);
        Assert.Equal((int)Math.Ceiling(Vector2.Distance(u.Position[c.Index], impact) / (12f / 20f)), ticks);
        Assert.True(w.Impacts[0].Hit); // a lob "hits" by exploding
        Assert.Equal(impact, w.Impacts[0].Position);
        // 50 siege x 0.5 (v light) = 25, armor 0, at the center of the splash: full damage.
        Assert.Equal(25, full - u.Hp[bystander.Index]);
        Assert.Equal(TestSim.Data.Units[Laborer].Hp, u.Hp[target.Index]);
    }

    [Theory]
    [InlineData(2, 64, 64)]
    [InlineData(2, 1000, 200)] // 2 x popCap 100 (200 half-pop) / the smallest shooter's 2 half-pop
    [InlineData(2, 4096, 200)]
    [InlineData(4, 4096, 400)]
    [InlineData(1, 4096, 100)]
    public void TheDefaultCapacity_IsTheShootersThePopulationCapsAllow_AtMostTheUnitSlots(int players, int units, int expected)
    {
        SimConfig config = TestSim.Config(Seed: 1, PlayerCount: players, UnitCapacity: units, CommandCapacity: 8);
        Assert.Equal(expected, config.ProjectileSlots);
        Assert.Equal(7, (config with { ProjectileCapacity = 7 }).ProjectileSlots);
    }

    /// <summary>
    /// The default capacity's premise: every shipped shot lands before its shooter can fire again (its longest flight,
    /// range plus both radii at most, is shorter than its cooldown), so a shooter has at most one projectile in the air.
    /// </summary>
    [Fact]
    public void EveryShippedShooter_HasAtMostOneShotInTheAir()
    {
        GameData d = TestSim.Data;
        float maxRadius = d.Units.Max(x => x.Radius);
        foreach (UnitDef u in d.Units.Where(x => x.Attack.ProjectileTypeId >= 0))
        {
            ProjectileDef p = d.Projectiles[u.Attack.ProjectileTypeId];
            int flight = (int)Math.Ceiling((u.Attack.Range + CombatConstants.WindupGrace + u.Radius + maxRadius) / p.SpeedPerTick);
            Assert.True(flight < u.Attack.CooldownTicks, $"{u.Key}: a {flight}-tick flight against a {u.Attack.CooldownTicks}-tick cooldown");
        }
    }

    [Fact]
    public void AStoreFull_LosesTheShot_AndNothingElseChanges()
    {
        var config = TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 600) with { ProjectileCapacity = 1 };
        var sim = new Simulation(config, LocalMovementTests.Flat(48));
        World w = sim.World;
        Assert.Equal(1, w.Projectiles.Capacity);
        EntityHandle a = Place(sim, 0, Crossbowman, At(sim, 20, 20));
        EntityHandle b = Place(sim, 0, Crossbowman, At(sim, 20, 22));
        EntityHandle r = Place(sim, 1, Raider, At(sim, 20, 20, dx: 8f));
        UnitStore u = w.Units;
        u.Target[a.Index] = u.Target[b.Index] = r;
        ProjectileSystem.Fire(w, a.Index);
        ProjectileSystem.Fire(w, b.Index);
        Assert.Equal(1, w.Projectiles.Count);
        Assert.Equal(a, w.Projectiles.Attacker[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => (config with { ProjectileCapacity = -1 }).Validate());
    }

    [Fact]
    public void ShotsTakeTheLowestFreeSlot_AndLandInSlotOrder()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle near = Place(sim, 1, Raider, At(sim, 20, 20, dx: 4f));
        EntityHandle far = Place(sim, 1, Raider, At(sim, 20, 20, dx: 12f));
        EntityHandle s = Place(sim, 0, Crossbowman, At(sim, 20, 20));
        u.Target[s.Index] = far;
        ProjectileSystem.Fire(w, s.Index); // slot 0, 10 ticks
        u.Target[s.Index] = near;
        ProjectileSystem.Fire(w, s.Index); // slot 1, 4 ticks
        u.Target[s.Index] = default;
        u.CooldownTicks[s.Index] = 1_000_000;
        sim.Enqueue(Command.HoldPosition(0, s));
        RunUntilImpact(sim);
        Assert.Equal(1, w.Projectiles.Count);
        Assert.True(w.Projectiles.Alive[0] && !w.Projectiles.Alive[1]);
        u.Target[s.Index] = near;
        ProjectileSystem.Fire(w, s.Index);
        Assert.True(w.Projectiles.Alive[1]); // the freed slot is taken again
    }
}
