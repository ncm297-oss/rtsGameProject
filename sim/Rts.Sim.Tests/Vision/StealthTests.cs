using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-5 (M4 criterion 7): stealth and detection (docs/02 "Stealth and detection"). A stealthed unit is unseen and untargetable
/// by every enemy player that doesn't detect it; detection is a detector (a unit's or a finished building's <c>detector</c>
/// radius) or any enemy unit within <c>stealth.proximity</c>, and always needs the unit's cell visible to that player; an
/// attack landing or a cast resolving from stealth reveals the unit for <c>stealth.revealSeconds</c>. No shipped unit can
/// stealth before M8: the sources here are test-only (a selfAura ability, a status applied directly), and so is the
/// Laborer's 10 m <c>detector</c> (the shipped casters get theirs in the data track's D12); the Watchtower's 16 m is shipped.
/// </summary>
public class StealthTests
{
    /// <summary>
    /// Test data: the Malazan Heavy Infantry has <c>zz_test_stealth</c> (ability 0: Stealthed on itself for 60 s) and
    /// <c>zz_test_shout</c> (ability 1: Frenzied on itself for 1 s), both selfAura with no cast time and a 1 s cooldown; the
    /// Malazan Laborer is a 10 m detector.
    /// </summary>
    internal static GameData Data => s_data.Value;

    private static readonly Lazy<GameData> s_data = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        AddTo(dir);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    });

    /// <summary>Adds the test-only stealth sources and detector (see <see cref="Data"/>) to a data copy.</summary>
    internal static void AddTo(TestDataDir dir)
    {
        dir.EditJson("factions/malazan/abilities.json", root =>
        {
            JsonArray list = root["abilities"]!.AsArray();
            list.Add(SelfAura("zz_test_stealth", new JsonObject { ["kind"] = "applyStatus", ["status"] = "stealthed", ["duration"] = 60 }));
            list.Add(SelfAura("zz_test_shout", new JsonObject { ["kind"] = "applyStatus", ["status"] = "frenzied", ["magnitude"] = 0.5, ["duration"] = 1 }));
        });
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "abilities", "[\"zz_test_stealth\", \"zz_test_shout\"]");
        dir.SetUnitField("malazan", "malazan_laborer", "detector", "10");
    }

    private static JsonObject SelfAura(string id, JsonObject effect) => new()
    {
        ["id"] = id, ["displayName"] = "Test", ["description"] = "Test only.", ["kind"] = "selfAura", ["radius"] = 0.5,
        ["castTime"] = 0, ["cooldown"] = 1, ["affects"] = "own_units", ["autocast"] = false, ["effects"] = new JsonArray(effect),
    };

    private static int Stealthed => Data.FindStatus("stealthed");

    private static int Revealed => Data.FindStatus("revealed");

    private static int Infantry => Data.FindUnit("malazan_heavy_infantry");

    private static int Bowman => Data.FindUnit("malazan_crossbowman");

    private static int Detector => Data.FindUnit("malazan_laborer");

    /// <summary>The Whirlwind worker: a worker never scans, so as the hidden unit it never attacks (and never reveals itself).</summary>
    private static int Worker => Data.FindUnit("whirlwind_camp_follower");

    private static int Watchtower => Data.FindBuilding("malazan_watchtower");

    /// <summary>A sim of the test data on a flat map, combat on.</summary>
    internal static Simulation Scene(int players = 2, int size = 48, int units = 64, bool combat = true) =>
        new(new SimConfig(1, players, units, 8 * units + 32) { Data = Data, Combat = combat }, LocalMovementTests.Flat(size));

    /// <summary>Applies Stealthed to <paramref name="unit"/> for <paramref name="ticks"/> ticks (a test-only source).</summary>
    internal static void Stealth(Simulation sim, EntityHandle unit, int ticks = 10_000) =>
        StatusSystem.Apply(sim.World, unit.Index, sim.World.Data.FindStatus("stealthed"), 0f, ticks, sim.World.Units.Owner[unit.Index]);

    private static void Ticks(Simulation sim, int n)
    {
        for (int t = 0; t < n; t++) sim.Tick();
    }

    // ---------- criterion 1: unseen and untargetable ----------

    [Fact]
    public void TheData_StealthedAndRevealedLoad_AndOnlyTheTestSourcesExist()
    {
        Assert.Equal(StatusKind.Stealthed, Data.Statuses[Stealthed].Kind);
        Assert.Equal(StatusKind.Revealed, Data.Statuses[Revealed].Kind);
        Assert.Equal((3f, 60), (Data.Rules.StealthProximity, Data.Rules.RevealTicks));
        Assert.Equal(10f, Data.Units[Detector].Detector);
        Assert.Equal(16f, Data.Buildings[Watchtower].Detector);
        // Nothing shipped stealths (M8): no ability or passive applies Stealthed.
        int shippedStealthed = TestSim.Data.FindStatus("stealthed");
        foreach (AbilityDef a in TestSim.Data.Abilities)
            foreach (AbilityEffect e in a.Effects)
            {
                Assert.NotEqual(shippedStealthed, e.Status);
                foreach (ZoneStatus z in e.ZoneStatuses.IsDefault ? System.Collections.Immutable.ImmutableArray<ZoneStatus>.Empty : e.ZoneStatuses)
                    Assert.NotEqual(shippedStealthed, z.Status);
            }
        foreach (UnitDef u in TestSim.Data.Units)
            foreach (UnitPassive ps in u.Passives) Assert.NotEqual(shippedStealthed, ps.Status);
    }

    [Fact]
    public void AnUndetectedStealthedUnit_InSight_IsNeitherSeenNorTakenByAnyEnemy_ButItsOwnerSeesIt()
    {
        Simulation sim = Scene(players: 3);
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle hidden = Place(sim, 0, Worker, p);
        EntityHandle enemy = Place(sim, 1, Infantry, Off(p, 8f)); // inside its 14 m sight, outside the 3 m proximity
        EntityHandle third = Place(sim, 2, Infantry, Off(p, -8f)); // 16 m from the enemy: beyond each other's sight
        Stealth(sim, hidden);
        Ticks(sim, 2);
        Assert.True(w.IsStealthed(hidden.Index));
        Assert.True(w.Fog.IsVisible(1, w.Fog.CellOf(p)) && w.Fog.IsVisible(2, w.Fog.CellOf(p))); // its cell is in their sight
        Assert.False(w.Fog.CanSeeUnit(1, hidden.Index));
        Assert.False(w.Fog.CanSeeUnit(2, hidden.Index));
        Assert.True(w.Fog.CanSeeUnit(0, hidden.Index)); // its owner always does
        Assert.False(VisionSystem.UnitSeesUnit(w, enemy.Index, hidden.Index));
        Assert.False(VisionSystem.UnitSeesUnit(w, third.Index, hidden.Index));
        Assert.False(w.IsDetectedBy(1, hidden.Index) || w.IsDetectedBy(2, hidden.Index));

        int full = u.Hp[hidden.Index];
        for (int t = 0; t < 60; t++)
        {
            sim.Tick();
            Assert.NotEqual(hidden, u.Target[enemy.Index]); // no acquisition
            Assert.NotEqual(hidden, u.Target[third.Index]);
        }
        // An explicit Attack on it is dropped when it applies.
        sim.Enqueue(Command.Attack(1, enemy, hidden, isBuilding: false));
        Ticks(sim, 2);
        Assert.NotEqual(hidden, u.Target[enemy.Index]);
        // No retaliation either: a unit it hit before it vanished doesn't take it back.
        u.LastAttacker[enemy.Index] = hidden;
        for (int t = 0; t < 20; t++)
        {
            sim.Tick();
            Assert.NotEqual(hidden, u.Target[enemy.Index]);
        }
        Assert.Equal(full, u.Hp[hidden.Index]);
        // The control: revealed, the same enemy takes it within a scan.
        StatusSystem.Apply(w, hidden.Index, Revealed, 0f, 400, 0);
        Assert.False(w.IsStealthed(hidden.Index));
        RunUntil(sim, () => u.Target[enemy.Index] == hidden, CombatConstantsScan + 1);
        Assert.Equal(hidden, u.Target[enemy.Index]);
    }

    private const int CombatConstantsScan = Combat.CombatConstants.ScanInterval;

    // ---------- criterion 2: detection ----------

    [Theory]
    [InlineData(9.9f, true)]
    [InlineData(10f, true)] // the edge counts
    [InlineData(10.1f, false)]
    public void ADetectorUnit_DetectsWithinItsRadius_AndItsSideTakesTheUnitNormally(float distance, bool detected)
    {
        Simulation sim = Scene();
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle hidden = Place(sim, 0, Worker, p);
        EntityHandle spotter = Place(sim, 1, Detector, Off(p, distance)); // a worker: never fights
        EntityHandle fighter = Place(sim, 1, Infantry, Off(p, 0f, 6f));
        Stealth(sim, hidden);
        Ticks(sim, 1);
        Assert.True(w.Fog.IsVisible(1, w.Fog.CellOf(p)));
        Assert.Equal(detected, w.IsDetectedBy(1, hidden.Index));
        Assert.Equal(detected, w.Fog.CanSeeUnit(1, hidden.Index));
        Assert.Equal(detected, VisionSystem.UnitSeesUnit(w, fighter.Index, hidden.Index));
        int full = u.Hp[hidden.Index];
        RunUntil(sim, () => u.Hp[hidden.Index] < full, 200);
        Assert.Equal(detected, u.Hp[hidden.Index] < full); // acquired and hit like any visible unit, or never
        Assert.True(u.IsAlive(spotter));
    }

    [Theory]
    [InlineData(15.9f, true)]
    [InlineData(16.1f, false)]
    public void AWatchtower_Detects16mFromItsFootprintCentre_AndShootsOnlyWhatItDetects(float distance, bool detected)
    {
        Simulation sim = Scene(size: 64);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle tower = TowerTests.PlaceBuilding(sim, 1, Watchtower, 30, 30); // footprint 60-64 m: centre (62, 62)
        Vector2 centre = Combat.CombatSystem.BuildingCentre(w, tower.Index);
        Assert.Equal(new Vector2(62f, 62f), centre);
        EntityHandle hidden = Place(sim, 0, Worker, centre - new Vector2(distance, 0f)); // inside its 18 m range and 24 m sight
        sim.Enqueue(Command.HoldPosition(0, hidden));
        Stealth(sim, hidden);
        Ticks(sim, 1);
        Assert.True(w.Fog.IsVisible(1, w.Fog.CellOf(u.Position[hidden.Index])));
        Assert.Equal(detected, w.IsDetectedBy(1, hidden.Index));
        Assert.Equal(detected, VisionSystem.BuildingSeesUnit(w, tower.Index, hidden.Index));
        int full = u.Hp[hidden.Index];
        RunUntil(sim, () => u.Hp[hidden.Index] < full, 200);
        Assert.Equal(detected, u.Hp[hidden.Index] < full);
        if (!detected) Assert.Equal(default, w.Buildings.TowerTarget[tower.Index]); // never picked
    }

    [Fact]
    public void ASiteUnderConstruction_DetectsNothing()
    {
        Simulation sim = Scene(size: 64);
        World w = sim.World;
        EntityHandle site = TowerTests.PlaceBuilding(sim, 1, Watchtower, 30, 30, site: true);
        EntityHandle hidden = Place(sim, 0, Worker, Combat.CombatSystem.BuildingCentre(w, site.Index) - new Vector2(8f, 0f));
        sim.Enqueue(Command.HoldPosition(0, hidden));
        Stealth(sim, hidden);
        Ticks(sim, 1);
        Assert.True(w.Fog.IsVisible(1, w.Fog.CellOf(w.Units.Position[hidden.Index]))); // a site sees
        Assert.False(w.IsDetectedBy(1, hidden.Index)); // but detects only once finished
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HighGroundFromBelow_IsNotDetected_FromTheSameLevelItIs(bool detectorOnThePlateau)
    {
        var sim = new Simulation(new SimConfig(1, 2, 64, 544) { Data = Data }, FogMaps.TwoLevel());
        World w = sim.World;
        Vector2 high = FogMaps.Cell(23, 30); // on the plateau (x >= 20)
        EntityHandle hidden = Place(sim, 0, Worker, high);
        // 8 m away (inside the 10 m detector, outside the 4 m lip): below the cliff, or on the plateau's far side.
        Vector2 at = detectorOnThePlateau ? FogMaps.Cell(27, 30) : FogMaps.Cell(19, 30);
        EntityHandle spotter = Place(sim, 1, Detector, at);
        Assert.Equal(detectorOnThePlateau ? 1 : 0, w.Fog.LevelAt(at));
        Stealth(sim, hidden);
        Ticks(sim, 2);
        Assert.Equal(detectorOnThePlateau, w.Fog.IsVisible(1, w.Fog.CellOf(high)));
        Assert.Equal(detectorOnThePlateau, w.IsDetectedBy(1, hidden.Index));
        Assert.Equal(detectorOnThePlateau, w.Fog.CanSeeUnit(1, hidden.Index));
        Assert.True(w.Units.IsAlive(spotter));
    }

    [Theory]
    [InlineData(3f, true)] // the edge counts
    [InlineData(2.5f, true)]
    [InlineData(3.05f, false)]
    public void Proximity_AnyEnemyUnitWithin3m_Detects(float distance, bool detected)
    {
        Simulation sim = Scene();
        World w = sim.World;
        var p = new Vector2(41f, 41f);
        EntityHandle hidden = Place(sim, 0, Worker, p);
        EntityHandle near = Place(sim, 1, Worker, p + new Vector2(distance, 0f)); // no detector: proximity only
        Stealth(sim, hidden);
        Ticks(sim, 1);
        Assert.Equal(detected, w.IsDetectedBy(1, hidden.Index));
        Assert.Equal(detected, w.Fog.CanSeeUnit(1, hidden.Index));
        Assert.Equal(0f, Data.Units[w.Units.TypeId[near.Index]].Detector);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Proximity_NeedsTheCellSeen_ABlindedEnemyAt2_9m_DoesNotDetect(bool blinded)
    {
        Simulation sim = Scene();
        World w = sim.World;
        Vector2 e = At(sim, 10, 10);
        EntityHandle enemy = Place(sim, 1, Worker, e);
        // 2.9 m away diagonally, in the cell diagonal to the enemy's: outside a 2 m circle (cells within 1 of its own).
        EntityHandle hidden = Place(sim, 0, Worker, e + new Vector2(2.05f, 2.05f));
        Assert.True(Vector2.Distance(e, w.Units.Position[hidden.Index]) < 3f);
        if (blinded) StatusSystem.Apply(w, enemy.Index, Data.FindStatus("blinded"), 0f, 400, 0);
        Stealth(sim, hidden);
        Ticks(sim, 2);
        Assert.Equal(!blinded, w.Fog.IsVisible(1, w.Fog.CellOf(w.Units.Position[hidden.Index])));
        Assert.Equal(!blinded, w.IsDetectedBy(1, hidden.Index));
        Assert.Equal(!blinded, w.Fog.CanSeeUnit(1, hidden.Index));
    }

    // ---------- criterion 3: reveal ----------

    [Fact]
    public void AMeleeHitFromStealth_Reveals60Ticks_ToEveryEnemyWhoseSightCoversIt_ARehitRefreshes_ThenStealthedAgain()
    {
        Simulation sim = Scene(players: 3);
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle attacker = Place(sim, 0, Infantry, p);
        EntityHandle victim = Place(sim, 1, Worker, Off(p, 1.2f)); // a 0.4 m gap: in its 0.5 m reach
        EntityHandle watcher = Place(sim, 2, Worker, Off(p, 0f, 8f)); // its sight covers the attacker's cell
        sim.Enqueue(Command.HoldPosition(0, attacker)); // takes what stands in reach only
        Stealth(sim, attacker);
        int full = u.Hp[victim.Index];
        RunUntil(sim, () => u.Hp[victim.Index] < full, 100);
        Assert.True(u.Hp[victim.Index] < full, "never hit");
        Assert.Equal((0f, 60), StatusOf(sim, attacker, Revealed));
        Assert.False(w.IsStealthed(attacker.Index));
        Assert.True(w.Fog.CanSeeUnit(2, attacker.Index));
        Assert.True(w.Fog.IsVisible(2, w.Fog.CellOf(p)));
        // The second swing (1.5 s later) refreshes it to the full 3 s.
        int hp = u.Hp[victim.Index];
        RunUntil(sim, () => u.Hp[victim.Index] < hp, 100);
        Assert.Equal((0f, 60), StatusOf(sim, attacker, Revealed));
        u.Free(victim); // nothing left in reach: no third hit
        for (int k = 1; k < 60; k++)
        {
            sim.Tick();
            Assert.False(w.IsStealthed(attacker.Index), $"stealthed again after {k} ticks");
            Assert.True(w.Fog.CanSeeUnit(2, attacker.Index));
        }
        sim.Tick(); // the 60th tick ends it
        Assert.Equal((0f, 0), StatusOf(sim, attacker, Revealed));
        Assert.True(w.IsStealthed(attacker.Index));
        Assert.False(w.Fog.CanSeeUnit(2, attacker.Index));
        Assert.True(u.IsAlive(watcher));
    }

    [Fact]
    public void ARevealIsSeenOnlyWhereThePlayersSightCoversTheCell()
    {
        Simulation sim = Scene();
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 p = At(sim, 10, 10);
        EntityHandle hidden = Place(sim, 0, Worker, p);
        EntityHandle far = Place(sim, 1, Worker, At(sim, 40, 40));
        Stealth(sim, hidden);
        StatusSystem.Apply(w, hidden.Index, Revealed, 0f, 60, 0);
        Ticks(sim, 1);
        Assert.False(w.IsStealthed(hidden.Index));
        Assert.False(w.Fog.IsVisible(1, w.Fog.CellOf(p)));
        Assert.False(w.Fog.CanSeeUnit(1, hidden.Index)); // revealed, but nobody of player 1 looks there
        Assert.True(u.IsAlive(far));
    }

    [Fact]
    public void AProjectileFromStealth_RevealsWhenItLands_NotWhenItIsFired()
    {
        Simulation sim = Scene();
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle bowman = Place(sim, 0, Bowman, p);
        EntityHandle victim = Place(sim, 1, Worker, Off(p, 10f)); // in its 15 m range; outside the 3 m proximity
        sim.Enqueue(Command.HoldPosition(0, bowman));
        Stealth(sim, bowman);
        bool flying = false;
        int full = u.Hp[victim.Index];
        for (int t = 0; t < 200 && u.Hp[victim.Index] == full; t++)
        {
            sim.Tick();
            if (u.Hp[victim.Index] < full) break;
            if (w.Projectiles.Count > 0)
            {
                flying = true;
                Assert.True(w.IsStealthed(bowman.Index)); // in flight: not yet
                Assert.False(w.Fog.CanSeeUnit(1, bowman.Index));
            }
        }
        Assert.True(flying, "no shot seen in flight");
        Assert.True(u.Hp[victim.Index] < full, "never landed");
        Assert.Equal((0f, 60), StatusOf(sim, bowman, Revealed));
        Assert.True(w.Fog.CanSeeUnit(1, bowman.Index)); // the victim's sight covers the shooter's cell
    }

    [Fact]
    public void ACastResolvingFromStealth_Reveals60Ticks_ButAStealthCastFromTheOpenDoesNot()
    {
        Simulation sim = Scene(combat: false); // nobody swings: only the casts reveal
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle caster = Place(sim, 0, Infantry, p);
        EntityHandle watcher = Place(sim, 1, Worker, Off(p, 8f));
        sim.Enqueue(Command.HoldPosition(0, caster));
        Ticks(sim, 2);
        // The test-only stealth source: cast from the open, it stealths at once.
        sim.Enqueue(Command.UseAbility(0, caster, 0, p));
        TickOf(sim, caster, resolved: true);
        Assert.True(w.IsStealthed(caster.Index));
        Assert.Equal((0f, 0), StatusOf(sim, caster, Revealed));
        Ticks(sim, 1);
        Assert.False(w.Fog.CanSeeUnit(1, caster.Index));
        // Another cast, from stealth, resolves: Revealed for 60 ticks, seen by the watcher whose sight covers it.
        Ticks(sim, 20);
        sim.Enqueue(Command.UseAbility(0, caster, 1, p));
        TickOf(sim, caster, resolved: true);
        Assert.Equal((0f, 60), StatusOf(sim, caster, Revealed));
        Assert.True(w.Fog.CanSeeUnit(1, caster.Index));
        for (int k = 1; k < 60; k++)
        {
            sim.Tick();
            Assert.False(w.IsStealthed(caster.Index));
        }
        sim.Tick();
        Assert.True(w.IsStealthed(caster.Index)); // its Stealthed entry still lives
        Assert.True(u.IsAlive(watcher));
    }

    [Fact]
    public void RevealedOverridesAStealthedAppliedDuringIt_WhichStaysStoredDormant()
    {
        Simulation sim = Scene();
        World w = sim.World;
        Vector2 p = At(sim, 20, 20);
        EntityHandle unit = Place(sim, 0, Worker, p);
        EntityHandle watcher = Place(sim, 1, Worker, Off(p, 8f));
        StatusSystem.Apply(w, unit.Index, Revealed, 0f, 30, 0);
        Ticks(sim, 1);
        Stealth(sim, unit, 200);
        Assert.True(w.Units.Statuses.HasStealth(unit.Index));
        Assert.False(w.IsStealthed(unit.Index));
        int seen = 0;
        while (!w.IsStealthed(unit.Index))
        {
            Assert.True(w.Fog.CanSeeUnit(1, unit.Index));
            sim.Tick();
            Assert.True(++seen < 40, "never stealthed");
        }
        Assert.Equal(29, seen); // the rest of Revealed's 30 ticks
        Assert.False(w.Fog.CanSeeUnit(1, unit.Index));
        Assert.True(w.Units.IsAlive(watcher));
    }

    // ---------- criterion 4 ----------

    [Fact]
    public void ASummonedUnit_IgnoresStealth()
    {
        Simulation sim = Scene();
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 p = At(sim, 20, 20);
        Assert.True(u.TrySpawn(0, Worker, Data.Units[Worker], p, 1000, out EntityHandle summoned));
        EntityHandle watcher = Place(sim, 1, Worker, Off(p, 8f));
        Stealth(sim, summoned);
        Ticks(sim, 1);
        Assert.True(u.Statuses.HasStealth(summoned.Index));
        Assert.False(w.IsStealthed(summoned.Index));
        Assert.True(w.Fog.CanSeeUnit(1, summoned.Index));
        Assert.True(VisionSystem.UnitSeesUnit(w, watcher.Index, summoned.Index));
    }

    [Fact]
    public void ATargetThatGoesStealthedAndUndetected_IsDroppedByItsChaser_WithinAScan()
    {
        Simulation sim = Scene();
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle target = Place(sim, 0, Worker, p);
        EntityHandle chaser = Place(sim, 1, Infantry, Off(p, 9f));
        sim.Enqueue(Command.Move(0, target, Off(p, -30f))); // walks away
        sim.Enqueue(Command.Attack(1, chaser, target, isBuilding: false));
        Ticks(sim, 3);
        Assert.Equal(target, u.Target[chaser.Index]);
        Stealth(sim, target);
        int t = RunUntil(sim, () => u.Target[chaser.Index] == default, CombatConstantsScan + 1);
        Assert.True(u.Target[chaser.Index] == default, "still chasing");
        Assert.True(t <= CombatConstantsScan + 1);
        Ticks(sim, 40);
        Assert.NotEqual(target, u.Target[chaser.Index]); // never taken again
    }

    [Fact]
    public void TheStealthCounter_FollowsTheEntries_AndADeadUnitTakesItsAway()
    {
        Simulation sim = Scene();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle a = Place(sim, 0, Worker, At(sim, 10, 10));
        EntityHandle b = Place(sim, 0, Worker, At(sim, 14, 10));
        Assert.Equal(0, u.Statuses.StealthedUnits);
        Stealth(sim, a, 10);
        Stealth(sim, a, 30); // a refresh: still one
        Stealth(sim, b, 400);
        Assert.Equal(2, u.Statuses.StealthedUnits);
        Ticks(sim, 30); // a's ends
        Assert.Equal(1, u.Statuses.StealthedUnits);
        Assert.False(u.Statuses.HasStealth(a.Index));
        u.Free(b);
        Assert.Equal(0, u.Statuses.StealthedUnits);
    }
}
