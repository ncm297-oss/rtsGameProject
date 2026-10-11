using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using Xunit.Abstractions;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-5 (session 2026-10-10-1954): attacks on stealth / detection beyond the developer's rows. The hook's completeness
/// (scan, explicit Attack, retaliation re-pick, kept chase, tower scan and kept target, the placement query) for every
/// (hider, enemy) pair of three players in both slot orders; detector edges (16.0 m from a tower's centre, a detector that
/// dies, a walk between two detector circles on fog-update and in-between ticks against a brute-force oracle); reveal timing
/// (a hit on the tick Stealthed expires, a caster that resolves and dies the same tick); Sandstorm; a stealthed status
/// applied from the death path (BUG-0480); a data set without a revealed status (BUG-0481); and a 3-seed x 2,000-tick fuzz
/// with explicit Attacks on hidden units whose detection is checked against a brute-force oracle every tick.
/// </summary>
public class StealthQaTests
{
    private readonly ITestOutputHelper _out;

    public StealthQaTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => StealthTests.Data;

    private static int Stealthed => Data.FindStatus("stealthed");

    private static int Revealed => Data.FindStatus("revealed");

    private static int Infantry => Data.FindUnit("malazan_heavy_infantry");

    private static int Bowman => Data.FindUnit("malazan_crossbowman");

    private static int Spotter10 => Data.FindUnit("malazan_laborer"); // a 10 m detector in StealthTests.Data

    private static int Worker => Data.FindUnit("whirlwind_camp_follower"); // never scans

    private static int Watchtower => Data.FindBuilding("malazan_watchtower");

    private static void Ticks(Simulation sim, int n)
    {
        for (int t = 0; t < n; t++) sim.Tick();
    }

    private static Simulation Scene(int players = 3, int size = 64, bool combat = true, GameData? data = null) =>
        new(new SimConfig(1, players, 96, 8 * 96 + 32) { Data = data ?? Data, Combat = combat }, LocalMovementTests.Flat(size));

    // ---------------------------------------------------------------- the hook, every pair of three players, both slot orders

    public static IEnumerable<object[]> Pairs()
    {
        for (int hider = 0; hider < 3; hider++)
            for (int enemy = 0; enemy < 3; enemy++)
                if (enemy != hider)
                {
                    yield return new object[] { hider, enemy, true };
                    yield return new object[] { hider, enemy, false };
                }
    }

    /// <summary>
    /// A stealthed worker 6 m from an enemy Heavy Infantry (in its sight, outside 3 m) and 12 m from a Crossbowman of the
    /// third player (both holding): no scan takes it, an explicit Attack is dropped (also shift-queued), the LastAttacker re-pick doesn't
    /// take it, its hit points never move over 120 ticks; the owner sees it. Then it is Revealed: the same infantry takes it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void EveryTargetingPath_SkipsAnUndetectedStealthedEnemy(int hider, int enemy, bool hiderFirst)
    {
        Simulation sim = Scene();
        World w = sim.World;
        UnitStore u = w.Units;
        int third = 3 - hider - enemy;
        Vector2 p = At(sim, 30, 30);
        EntityHandle hidden = default, fighter = default, shooter = default;
        if (hiderFirst) hidden = Place(sim, hider, Worker, p);
        fighter = Place(sim, enemy, Infantry, Off(p, 0f, 6f));
        shooter = Place(sim, third, Bowman, Off(p, 0f, -12f)); // 18 m from the fighter: out of each other's reach
        if (!hiderFirst) hidden = Place(sim, hider, Worker, p);
        Assert.Equal(hiderFirst, hidden.Index < fighter.Index);
        sim.Enqueue(Command.HoldPosition(enemy, fighter));
        sim.Enqueue(Command.HoldPosition(third, shooter));
        sim.Enqueue(Command.HoldPosition(hider, hidden));
        StealthTests.Stealth(sim, hidden);
        Ticks(sim, 2);
        int full = u.Hp[hidden.Index];
        for (int t = 0; t < 120; t++)
        {
            if (t == 10) sim.Enqueue(Command.Attack(enemy, fighter, hidden, isBuilding: false));
            if (t == 20) sim.Enqueue(Command.Attack(third, shooter, hidden, isBuilding: false));
            if (t == 25) sim.Enqueue(Command.Attack(third, shooter, hidden, isBuilding: false, queued: true));
            if (t == 30) u.LastAttacker[fighter.Index] = hidden; // the BUG-0156 re-pick's memory
            sim.Tick();
            Assert.NotEqual(hidden, u.Target[fighter.Index]);
            Assert.NotEqual(hidden, u.Target[shooter.Index]);
            Assert.False(w.Fog.CanSeeUnit(enemy, hidden.Index));
            Assert.False(w.Fog.CanSeeUnit(third, hidden.Index));
            Assert.True(w.Fog.CanSeeUnit(hider, hidden.Index));
            Assert.False(VisionSystem.UnitSeesUnit(w, fighter.Index, hidden.Index));
            Assert.False(VisionSystem.UnitSeesUnit(w, shooter.Index, hidden.Index));
        }
        Assert.Equal(full, u.Hp[hidden.Index]);
        Assert.Equal(0, w.Projectiles.Count);
        StatusSystem.Apply(w, hidden.Index, Revealed, 0f, 400, hider);
        RunUntil(sim, () => u.Hp[hidden.Index] < full, 100);
        Assert.True(u.Hp[hidden.Index] < full, $"revealed, still never hit: fighter hold {u.Hold[fighter.Index]} state {u.State[fighter.Index]} target {u.Target[fighter.Index]} ignored {u.Ignored[fighter.Index]} giveups {u.GiveUps[fighter.Index]}; shooter hold {u.Hold[shooter.Index]} state {u.State[shooter.Index]} target {u.Target[shooter.Index]} ignored {u.Ignored[shooter.Index]} giveups {u.GiveUps[shooter.Index]}; seen {w.Fog.CanSeeUnit(third, hidden.Index)} {VisionSystem.UnitSeesUnit(w, shooter.Index, hidden.Index)} stealthed {w.IsStealthed(hidden.Index)} dist {Vector2.Distance(u.Position[shooter.Index], u.Position[hidden.Index])}");
    }

    /// <summary>The placement query (sealLast false) ignores an undetected stealthed enemy in the footprint; detected, it is in the way.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    public void PlacementQuery_IgnoresAnUndetectedStealthedEnemy_ButNotADetectedOne(int hider, int builder)
    {
        Simulation sim = TestSim.Explored(Scene());
        World w = sim.World;
        BuildMaps.Give(sim, builder, 100_000, 100_000);
        int type = -1;
        for (int k = 0; k < Data.Buildings.Length && type < 0; k++)
        {
            BuildingDef d = Data.Buildings[k];
            if (d.Faction == w.FactionOf(builder) && d.RequiresTechs.Length == 0 && d.RequiresBuildings.Length == 0
                && d.FootprintWidth == 2 && d.FootprintHeight == 2) type = k;
        }
        Assert.True(type >= 0);
        // Builder's eyes 8 m off; the hider stands in cell (30, 30), the footprint's anchor.
        Place(sim, builder, Worker, At(sim, 34, 30));
        EntityHandle hidden = Place(sim, hider, Worker, At(sim, 30, 30));
        sim.Enqueue(Command.HoldPosition(hider, hidden));
        StealthTests.Stealth(sim, hidden);
        Ticks(sim, 2);
        int anchor = 30 * w.NavGrid.Width + 30;
        Assert.True(w.Fog.IsVisible(builder, w.Fog.CellOf(w.Units.Position[hidden.Index])));
        w.CanPlace(builder, type, anchor, out PlacementError hiddenReason);
        Assert.NotEqual(PlacementError.UnitInTheWay, hiddenReason);
        StatusSystem.Apply(w, hidden.Index, Revealed, 0f, 400, hider);
        w.CanPlace(builder, type, anchor, out PlacementError seenReason);
        Assert.Equal(PlacementError.UnitInTheWay, seenReason);
    }

    // ---------------------------------------------------------------- tower edges

    /// <summary>Exactly 16.0 m from the Watchtower's footprint centre is detected (the edge counts), 16.0 m + 1 float ulp is not.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Watchtower_Exactly16m_IsDetected_OneUlpBeyondIsNot(bool beyond, bool detected)
    {
        Simulation sim = Scene(players: 2);
        World w = sim.World;
        EntityHandle tower = TowerTests.PlaceBuilding(sim, 1, Watchtower, 30, 30);
        Vector2 c = CombatSystem.BuildingCentre(w, tower.Index);
        float x = c.X - 16f;
        if (beyond) x = MathF.BitDecrement(x);
        EntityHandle hidden = Place(sim, 0, Worker, new Vector2(x, c.Y));
        Assert.Equal(beyond, Vector2.DistanceSquared(c, new Vector2(x, c.Y)) > 256f);
        sim.Enqueue(Command.HoldPosition(0, hidden));
        StealthTests.Stealth(sim, hidden);
        Ticks(sim, 2);
        Assert.True(w.Fog.IsVisible(1, w.Fog.CellOf(w.Units.Position[hidden.Index])));
        Assert.Equal(detected, w.IsDetectedBy(1, hidden.Index));
        Assert.Equal(detected, VisionSystem.BuildingSeesUnit(w, tower.Index, hidden.Index));
    }

    /// <summary>
    /// A tower shooting a unit 17 m away (in its 18 m range, outside its 16 m detector) drops it once it goes stealthed (on
    /// its next scan out of wind-up) and lands no hit on it after that.
    /// </summary>
    [Fact]
    public void Tower_KeptTarget_ThatGoesStealthedOutsideItsDetector_IsDropped()
    {
        Simulation sim = Scene(players: 2);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle tower = TowerTests.PlaceBuilding(sim, 1, Watchtower, 30, 30);
        Vector2 c = CombatSystem.BuildingCentre(w, tower.Index);
        EntityHandle target = Place(sim, 0, Worker, c - new Vector2(17f, 0f));
        sim.Enqueue(Command.HoldPosition(0, target));
        RunUntil(sim, () => w.Buildings.TowerTarget[tower.Index] == target, 200);
        Assert.Equal(target, w.Buildings.TowerTarget[tower.Index]);
        StealthTests.Stealth(sim, target);
        int dropped = RunUntil(sim, () => w.Buildings.TowerTarget[tower.Index] == default, 200);
        Assert.Equal(default, w.Buildings.TowerTarget[tower.Index]);
        _out.WriteLine($"dropped after {dropped} ticks");
        Ticks(sim, 30); // any shot already fired lands
        int hp = u.Hp[target.Index];
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            Assert.NotEqual(target, w.Buildings.TowerTarget[tower.Index]);
        }
        Assert.Equal(hp, u.Hp[target.Index]);
    }

    // ---------------------------------------------------------------- detectors that die, walks between circles

    /// <summary>
    /// A 10 m detector is the only thing that shows a stealthed worker to an infantry 8 m from it, which attacks it; the
    /// detector is killed mid-tick (phase 11): from the next tick's phase 5 the worker is hidden again and the infantry
    /// drops it within a scan.
    /// </summary>
    [Fact]
    public void ADetectorThatDies_StopsDetectingFromTheNextTick_AndTheChaserDropsTheTarget()
    {
        Simulation sim = Scene(players: 3);
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 p = At(sim, 30, 30);
        EntityHandle hidden = Place(sim, 0, Worker, p);
        EntityHandle spotter = Place(sim, 1, Spotter10, Off(p, -6f));
        EntityHandle chaser = Place(sim, 1, Infantry, Off(p, 8f));
        EntityHandle killer = Place(sim, 2, Infantry, Off(p, -6f, 1.2f)); // in reach of the spotter only
        u.Hp[hidden.Index] = 10_000;
        sim.Enqueue(Command.HoldPosition(0, hidden));
        sim.Enqueue(Command.HoldPosition(1, spotter));
        sim.Enqueue(Command.HoldPosition(2, killer));
        StealthTests.Stealth(sim, hidden);
        Ticks(sim, 1);
        Assert.True(w.IsDetectedBy(1, hidden.Index));
        RunUntil(sim, () => u.Target[chaser.Index] == hidden, 40);
        Assert.Equal(hidden, u.Target[chaser.Index]);
        u.Hp[spotter.Index] = 1;
        RunUntil(sim, () => !u.IsAlive(spotter), 200);
        Assert.False(u.IsAlive(spotter));
        sim.Tick();
        Assert.False(w.IsDetectedBy(1, hidden.Index));
        int t = RunUntil(sim, () => u.Target[chaser.Index] != hidden, CombatConstants.ScanInterval + 1);
        Assert.NotEqual(hidden, u.Target[chaser.Index]);
        Assert.True(u.IsAlive(killer));
        _out.WriteLine($"dropped {t} ticks after");
    }

    /// <summary>
    /// A stealthed worker walks at its own speed along a line past two 10 m detectors whose circles leave a
    /// gap, through 4 tick phases (start tick 0..3 mod 4, so the circle crossings fall on and between fog updates). After
    /// every tick its detection equals the brute-force oracle from the positions and visible cells before the tick.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void WalkingBetweenTwoDetectorCircles_MatchesTheOracleEveryTick(int phase)
    {
        Simulation sim = Scene(players: 2, combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        Ticks(sim, phase);
        Vector2 start = At(sim, 8, 30);
        EntityHandle walker = Place(sim, 0, Worker, start);
        Place(sim, 1, Spotter10, At(sim, 18, 34)); // circles centred 20 m apart along x, 8 m off the line: the gap is ~8 m
        Place(sim, 1, Spotter10, At(sim, 34, 34));
        Place(sim, 1, Worker, At(sim, 26, 22)); // extra eyes: the gap's cells are seen
        StealthTests.Stealth(sim, walker);
        sim.Enqueue(Command.Move(0, walker, At(sim, 50, 30)));
        int on = 0, off = 0, flips = 0;
        bool last = false;
        for (int t = 0; t < 400; t++)
        {
            bool expect = Oracle(w, 1, walker.Index);
            sim.Tick();
            if (!u.IsAlive(walker)) break;
            Assert.True(expect == w.IsDetectedBy(1, walker.Index), $"tick {w.TickNumber - 1}: oracle {expect}, sim {!expect}");
            if (expect) on++;
            else off++;
            if (t > 0 && expect != last) flips++;
            last = expect;
        }
        _out.WriteLine($"phase {phase}: {on} detected, {off} hidden ticks, {flips} flips");
        Assert.True(flips >= 3, $"only {flips} flips: the walk missed a circle");
    }

    /// <summary>
    /// The brute-force detection rule, from the state between ticks (= phase 5's: positions move in phase 9, the fog in phase
    /// 12; no zones): <paramref name="player"/> sees unit <paramref name="j"/>'s cell and a live unit of it stands within the
    /// proximity or its own detector radius, or a finished building of it within its detector of the footprint centre.
    /// </summary>
    internal static bool Oracle(World w, int player, int j)
    {
        UnitStore u = w.Units;
        GameData d = w.Data;
        if (!u.Alive[j] || u.Owner[j] == player) return false;
        if (!w.Fog.IsVisible(player, w.Fog.CellOf(u.Position[j]))) return false;
        float prox2 = d.Rules.StealthProximity * d.Rules.StealthProximity;
        for (int q = 0; q < u.Capacity; q++)
        {
            if (!u.Alive[q] || u.Owner[q] != player) continue;
            float d2 = Vector2.DistanceSquared(u.Position[j], u.Position[q]);
            float det = d.Units[u.TypeId[q]].Detector;
            if (d2 <= prox2 || (det > 0f && d2 <= det * det)) return true;
        }
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.UnderConstruction[k] || b.Owner[k] != player) continue;
            float det = d.Buildings[b.TypeId[k]].Detector;
            if (det > 0f && Vector2.DistanceSquared(CombatSystem.BuildingCentre(w, k), u.Position[j]) <= det * det) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- reveal timing

    /// <summary>
    /// A stealthed infantry's swing lands on tick H. Stealthed set to end exactly in phase 5 of tick H: no Revealed (the hit
    /// comes from a unit no longer carrying stealth). Set to end one tick later: Revealed for exactly 60 ticks, which outlives
    /// the Stealthed entry (harmless: nothing to override) and keeps a fresh stealth dormant until it ends.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void AHitOnTheTickStealthedExpires_DoesNotReveal_OneTickEarlierDoes(int extra, bool revealed)
    {
        // First run: find the tick the first hit lands.
        int hitTick = FirstHitTick(stealthTicks: 10_000, out _);
        // Stealthed applied between ticks at tick T0 for n ticks ends in phase 5 of tick T0 + n - 1.
        int n = hitTick - Start + 1 + extra;
        int again = FirstHitTick(n, out Simulation sim);
        Assert.Equal(hitTick, again);
        UnitStore u = sim.World.Units;
        EntityHandle attacker = new(0, u.Generation[0]);
        Assert.False(u.Statuses.HasStealth(attacker.Index) && extra == 0);
        Assert.Equal(revealed ? (0f, 60) : (0f, 0), StatusOf(sim, attacker, Revealed));
        if (!revealed) return;
        Ticks(sim, 1);
        Assert.False(u.Statuses.HasStealth(attacker.Index)); // the Stealthed entry has ended
        StealthTests.Stealth(sim, attacker, 400); // a fresh stealth during the reveal stays dormant
        Assert.False(sim.World.IsStealthed(attacker.Index));
    }

    private const int Start = 2;

    private static int FirstHitTick(int stealthTicks, out Simulation sim)
    {
        sim = Scene(players: 2);
        UnitStore u = sim.World.Units;
        Vector2 p = At(sim, 30, 30);
        EntityHandle attacker = Place(sim, 0, Infantry, p);
        EntityHandle victim = Place(sim, 1, Worker, Off(p, 1.2f));
        u.Hp[victim.Index] = 10_000;
        sim.Enqueue(Command.HoldPosition(0, attacker));
        sim.Enqueue(Command.HoldPosition(1, victim));
        Ticks(sim, Start);
        Assert.Equal(Start, sim.World.TickNumber);
        StealthTests.Stealth(sim, attacker, stealthTicks);
        int full = u.Hp[victim.Index];
        while (u.Hp[victim.Index] == full && sim.World.TickNumber < 400) sim.Tick();
        Assert.True(u.Hp[victim.Index] < full);
        return sim.World.TickNumber - 1;
    }

    /// <summary>
    /// A stealthed (revealed) infantry at 1 hp resolves the test-only shout (Revealed refreshed) on the tick it is killed in phase 11: the slot
    /// is freed with its counters right (StealthedUnits 0, UnitsWithStatuses 0), and a unit spawned into the slot is neither
    /// stealthed nor revealed.
    /// </summary>
    [Fact]
    public void ACasterThatResolvesFromStealthAndDiesTheSameTick_LeavesCleanCounters()
    {
        // Find a cast start whose resolve (phase 6) lands on the tick the killer's swing (phase 11) kills the 1 hp caster.
        for (int start = 1; start < 80; start++)
        {
            Simulation sim = CasterScene(out EntityHandle caster);
            World w = sim.World;
            UnitStore u = w.Units;
            Ticks(sim, start);
            if (!u.IsAlive(caster)) continue;
            sim.Enqueue(Command.UseAbility(0, caster, 1, u.Position[caster.Index]));
            bool sameTick = false;
            for (int t = 0; t < 200 && u.IsAlive(caster); t++)
            {
                sim.Tick();
                if (u.IsAlive(caster)) continue;
                foreach (AbilityEvent e in w.AbilityEvents) sameTick |= e.Resolved && e.Caster == caster;
            }
            if (!sameTick) continue;
            _out.WriteLine($"cast started after {start} ticks resolves on the death tick {w.TickNumber - 1}");
            Assert.Equal(0, u.Statuses.StealthedUnits);
            Assert.False(u.Statuses.HasStealth(caster.Index));
            Assert.Equal(0, u.Statuses.Count[caster.Index]);
            EntityHandle reuse = Place(sim, 0, Worker, At(sim, 10, 10));
            Assert.Equal(caster.Index, reuse.Index);
            Assert.False(w.IsStealthed(reuse.Index));
            Assert.Equal(0, u.Statuses.Count[reuse.Index]);
            Assert.Equal(0, u.Statuses.StealthedUnits);
            return;
        }
        Assert.Fail("no cast start resolves on the death tick");
    }

    private static Simulation CasterScene(out EntityHandle caster)
    {
        Simulation sim = Scene(players: 2);
        UnitStore u = sim.World.Units;
        Vector2 p = At(sim, 30, 30);
        caster = Place(sim, 0, Infantry, p);
        EntityHandle killer = Place(sim, 1, Infantry, Off(p, 1.2f));
        u.Hp[killer.Index] = 10_000;
        u.Hp[caster.Index] = 1;
        sim.Enqueue(Command.HoldPosition(0, caster));
        sim.Enqueue(Command.HoldPosition(1, killer));
        StealthTests.Stealth(sim, caster);
        // Revealed for long enough that the killer sees it and swings (else nothing kills it).
        StatusSystem.Apply(sim.World, caster.Index, Revealed, 0f, 4000, 0);
        return sim;
    }

    // ---------------------------------------------------------------- Sandstorm

    /// <summary>
    /// A stealthed worker (player 0) stands 4 m inside player 1's Sandstorm (radius 6, centre 4 m south of it). Player 1's
    /// worker 2.9 m north of it (outside the storm) detects it (its own storm hides nothing from it); player 2's worker
    /// 2.9 m away beside that one (also outside) and a player 2 10 m detector 9 m away (outside) don't: the storm hides its
    /// cell from player 2.
    /// </summary>
    [Fact]
    public void InsideASandstorm_OnlyTheStormOwnersProximityDetects()
    {
        Simulation sim = Scene(players: 3);
        World w = sim.World;
        UnitStore u = w.Units;
        AbilityDef storm = Data.Abilities[Data.FindAbility("sandstorm")];
        Vector2 p = At(sim, 30, 30);
        EntityHandle hidden = Place(sim, 0, Worker, p);
        float edge = storm.Radius;
        Vector2 centre = Off(p, 0f, -4f);
        Vector2[] eyes = { Off(p, -1.5f, 2.5f), Off(p, 1.5f, 2.5f), Off(p, -9f, 0f) };
        Place(sim, 1, Worker, eyes[0]);
        Place(sim, 2, Worker, eyes[1]);
        EntityHandle eyes2 = Place(sim, 2, Spotter10, eyes[2]);
        foreach (Vector2 e in eyes) Assert.True(Vector2.Distance(e, centre) > edge);
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Hold[i] = true;
        ZoneSystem.Create(w, 1, storm, centre);
        StealthTests.Stealth(sim, hidden);
        Ticks(sim, 8);
        Assert.True(w.Zones.BlockerCount > 0);
        Assert.True(w.IsDetectedBy(1, hidden.Index), "the storm's owner beside it doesn't detect it");
        Assert.True(w.Fog.CanSeeUnit(1, hidden.Index));
        Assert.False(w.IsDetectedBy(2, hidden.Index), "player 2 detects through the storm");
        Assert.False(w.Fog.CanSeeUnit(2, hidden.Index));
        Assert.True(u.IsAlive(eyes2));
    }

    // ---------------------------------------------------------------- BUG-0480: stealth applied from the death path

    /// <summary>
    /// BUG-0480: the Zealot's on-death passive set to apply <c>stealthed</c> (a data shape the loader accepts: Shadow's M8
    /// content could have one). The dying Zealot has five own Zealots within 6 m and enemy workers about; every one of the
    /// five must take the status, as they do with the shipped Frenzied. <see cref="PassiveSystem.OnDeath"/> iterates its
    /// neighbour query in <see cref="World.ShoveNeighbors"/>, and the first Zealot's new stealth runs
    /// <see cref="StealthSystem.OnStealthStarted"/>, which overwrites that same scratch with its enemy query.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOnDeathPassiveApplyingStealthed_ReachesEveryListedFriend(bool stealth)
    {
        GameData data = stealth ? ZealotStealthData.Value : TestSim.Data;
        int zealot = data.FindUnit("whirlwind_zealot");
        int status = data.FindStatus(stealth ? "stealthed" : "frenzied");
        var sim = new Simulation(new SimConfig(1, 2, 64, 544) { Data = data, Combat = false }, LocalMovementTests.Flat(48));
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 p = At(sim, 24, 24);
        EntityHandle dying = Place(sim, 0, zealot, p);
        var friends = new List<EntityHandle>();
        foreach (Vector2 o in new[] { new Vector2(1.5f, 0f), new Vector2(-1.5f, 0f), new Vector2(0f, 1.5f), new Vector2(0f, -1.5f), new Vector2(3f, 3f) })
            friends.Add(Place(sim, 0, zealot, p + o));
        int worker = data.FindUnit("malazan_laborer");
        for (int k = 0; k < 8; k++) Place(sim, 1, worker, p + new Vector2(-6f + 1.5f * k, 7f));
        sim.Tick(); // the spatial hash holds them
        PassiveSystem.OnDeath(w, dying.Index);
        int got = 0;
        foreach (EntityHandle f in friends) if (u.Statuses.IndexOf(f.Index, status) >= 0) got++;
        Assert.Equal(friends.Count, got);
    }

    private static readonly Lazy<GameData> ZealotStealthData = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        StealthTests.AddTo(dir);
        dir.EditJson("factions/whirlwind/units.json", root =>
        {
            foreach (JsonNode? n in root["units"]!.AsArray())
            {
                if ((string)n!["id"]! != "whirlwind_zealot") continue;
                JsonObject death = n["passives"]![1]!.AsObject();
                death["status"] = "stealthed";
                death.Remove("magnitude");
            }
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    });

    // ---------------------------------------------------------------- BUG-0481: no revealed status in the data

    /// <summary>
    /// BUG-0481: data with a <c>stealthed</c> status but no <c>revealed</c> one must not load, since "attacking or casting
    /// reveals" (docs/02) silently stops working (StealthSystem.OnAttackOrCast returns when RevealedStatus is -1).
    /// </summary>
    [Fact]
    public void StealthedWithoutARevealedStatus_IsRefusedAtLoad()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/statuses.json", root =>
        {
            JsonArray list = root["statuses"]!.AsArray();
            for (int i = list.Count - 1; i >= 0; i--)
                if ((string)list[i]!["id"]! == "revealed") list.RemoveAt(i);
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.False(r.Ok, "loads with a stealthed status and no revealed one: attacks from stealth never reveal");
        Assert.Contains(r.Errors, e => e.ToString().Contains("revealed", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- BUG-0482: a full status store drops the reveal

    /// <summary>
    /// BUG-0482 (S4): a stealthed infantry carrying seven other statuses (the store holds 8) lands a melee hit: docs/02 says
    /// it is Revealed for 3 s, but the Revealed application is the ninth status and is dropped, so it keeps attacking unseen.
    /// Not reachable with shipped data (six status ids); test-only slows fill the store.
    /// </summary>
    [Fact]
    public void AStealthedAttackerWithAFullStatusStore_IsStillRevealed()
    {
        GameData data = FullStoreData.Value;
        var sim = new Simulation(new SimConfig(1, 2, 64, 544) { Data = data, Combat = true }, LocalMovementTests.Flat(48));
        World w = sim.World;
        UnitStore u = w.Units;
        int infantry = data.FindUnit("malazan_heavy_infantry"), worker = data.FindUnit("whirlwind_camp_follower");
        Vector2 p = At(sim, 20, 20);
        EntityHandle attacker = Place(sim, 0, infantry, p);
        EntityHandle victim = Place(sim, 1, worker, Off(p, 1.2f));
        u.Hp[victim.Index] = 10_000;
        sim.Enqueue(Command.HoldPosition(0, attacker));
        sim.Enqueue(Command.HoldPosition(1, victim));
        StatusSystem.Apply(w, attacker.Index, data.FindStatus("stealthed"), 0f, 4000, 0);
        for (int k = 0; k < 7; k++) StatusSystem.Apply(w, attacker.Index, data.FindStatus($"zz_qa_slow_{k}"), 0.01f, 4000, 1);
        Assert.Equal(StatusStore.PerUnit, u.Statuses.Count[attacker.Index]);
        int full = u.Hp[victim.Index];
        RunUntil(sim, () => u.Hp[victim.Index] < full, 200);
        Assert.True(u.Hp[victim.Index] < full, "never hit");
        Assert.False(w.IsStealthed(attacker.Index), "hit from stealth and still stealthed");
    }

    private static readonly Lazy<GameData> FullStoreData = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/statuses.json", root =>
        {
            JsonArray list = root["statuses"]!.AsArray();
            for (int k = 0; k < 7; k++)
                list.Add(new JsonObject { ["id"] = $"zz_qa_slow_{k}", ["displayName"] = "Test", ["description"] = "Test only.", ["kind"] = "slow" });
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        return r.Data!;
    });

    // ---------------------------------------------------------------- oracle fuzz with explicit Attacks

    /// <summary>
    /// Three players on a 64-cell flat map: Heavy Infantry, Crossbowmen, 10 m detector Laborers and one Watchtower each.
    /// Every tick, before it runs, each player's random units get random orders (move, attack-move, an explicit Attack on a
    /// random enemy unit, hidden or not, stop) and random units get or lose stealth (Stealthed / Revealed applied directly).
    /// After every tick: twins hash-equal; every stealth carrier's detection equals the brute-force oracle from the state
    /// before the tick; an undetected stealthed unit is seen by no enemy (CanSeeUnit, UnitSeesUnit of every enemy unit,
    /// BuildingSeesUnit of every enemy tower); no tower holds a newly taken hidden target, and a unit only one an explicit
    /// Attack took in phase 1 while it was seen and that went hidden in the same tick's phase 5 (dropped within two scans);
    /// StealthedUnits equals the recount.
    /// </summary>
    [Theory]
    [InlineData(11UL)]
    [InlineData(12UL)]
    [InlineData(13UL)]
    public void OracleFuzz_WithExplicitAttacksOnHiddenUnits_ThreePlayers(ulong seed)
    {
        Simulation a = FuzzSim(seed), b = FuzzSim(seed);
        World w = a.World;
        UnitStore u = w.Units;
        BuildingStore bs = w.Buildings;
        var rngA = new SimRng(seed, 4801);
        var rngB = new SimRng(seed, 4801);
        var expected = new bool[u.Capacity * 3];
        var preHidden = new bool[u.Capacity * 3];
        var ordered = new int[u.Capacity];
        var keptHidden = new int[u.Capacity];
        int lateHides = 0;
        var carrier = new bool[u.Capacity];
        var prevTarget = new EntityHandle[u.Capacity];
        var prevTower = new EntityHandle[bs.Capacity];
        int checkedPairs = 0, detected = 0, hidden = 0, attacksOnHidden = 0, reveals = 0;
        for (int t = 0; t < 2000; t++)
        {
            Array.Fill(ordered, -1);
            attacksOnHidden += FuzzOrders(a, ref rngA, ordered);
            FuzzOrders(b, ref rngB, new int[u.Capacity]);
            for (int j = 0; j < u.Capacity; j++)
                for (int p = 0; p < 3; p++) preHidden[j * 3 + p] = u.Alive[j] && w.HiddenFrom(p, j);
            for (int j = 0; j < u.Capacity; j++)
            {
                carrier[j] = u.Alive[j] && u.Statuses.HasStealth(j);
                for (int p = 0; p < 3; p++) expected[j * 3 + p] = carrier[j] && Oracle(w, p, j);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            int carriers = 0;
            for (int j = 0; j < u.Capacity; j++)
            {
                if (!u.Alive[j]) continue;
                if (u.Statuses.HasStealth(j)) carriers++;
                int r = u.Statuses.IndexOf(j, Revealed);
                if (r >= 0 && u.Statuses.TicksRemaining[r] == Data.Rules.RevealTicks) reveals++;
                if (!carrier[j] || !u.Statuses.HasStealth(j)) continue;
                for (int p = 0; p < 3; p++)
                {
                    if (p == u.Owner[j]) continue;
                    checkedPairs++;
                    Assert.True(expected[j * 3 + p] == w.IsDetectedBy(p, j), $"seed {seed} tick {t}: unit {j} by player {p}: oracle {expected[j * 3 + p]}");
                    if (!w.IsStealthed(j)) continue;
                    if (w.IsDetectedBy(p, j)) { detected++; continue; }
                    hidden++;
                    Assert.False(w.Fog.CanSeeUnit(p, j));
                    for (int i = 0; i < u.Capacity; i++)
                        if (u.Alive[i] && u.Owner[i] == p) Assert.False(VisionSystem.UnitSeesUnit(w, i, j), $"seed {seed} tick {t}: {i} sees hidden {j}");
                    for (int k = 0; k < bs.Capacity; k++)
                        if (bs.Alive[k] && bs.Owner[k] == p) Assert.False(VisionSystem.BuildingSeesUnit(w, k, j));
                }
            }
            Assert.Equal(carriers, u.Statuses.StealthedUnits);
            for (int i = 0; i < u.Capacity; i++)
            {
                EntityHandle target = u.Alive[i] && !u.TargetIsBuilding[i] ? u.Target[i] : default;
                bool live = target.Generation != 0 && u.IsAlive(target);
                bool hiddenNow = live && w.HiddenFrom(u.Owner[i], target.Index);
                // A new target hidden now is allowed only when an explicit Attack applied in phase 1 while it was seen and it
                // went hidden in this tick's phase 5 (a Revealed ending, a detector gone): the lost-from-sight rule drops it.
                if (live && target != prevTarget[i] && hiddenNow)
                {
                    lateHides++;
                    Assert.True(!preHidden[target.Index * 3 + u.Owner[i]] && u.Mode[i] == CombatMode.Ordered,
                        $"seed {seed} tick {t}: unit {i} (owner {u.Owner[i]}, mode {u.Mode[i]}) took hidden {target.Index}: hidden before the tick {preHidden[target.Index * 3 + u.Owner[i]]}, prev target {prevTarget[i]}");
                }
                keptHidden[i] = hiddenNow ? keptHidden[i] + 1 : 0;
                Assert.True(keptHidden[i] <= 2 * CombatConstants.ScanInterval, $"seed {seed} tick {t}: unit {i} keeps hidden {target.Index}");
                prevTarget[i] = live ? target : default;
            }
            for (int k = 0; k < bs.Capacity; k++)
            {
                EntityHandle target = bs.Alive[k] ? bs.TowerTarget[k] : default;
                bool live = target.Generation != 0 && u.IsAlive(target);
                if (live && target != prevTower[k]) Assert.False(w.HiddenFrom(bs.Owner[k], target.Index), $"seed {seed} tick {t}: tower {k} took hidden {target.Index}");
                prevTower[k] = live ? target : default;
            }
        }
        _out.WriteLine($"seed {seed}: {checkedPairs} oracle pairs, {detected} detected / {hidden} hidden stealthed pairs, {attacksOnHidden} Attacks on hidden, {lateHides} ordered targets hidden in phase 5 of their tick, {reveals} reveals, {u.Count} units left");
        Assert.True(detected > 100 && hidden > 100, $"{detected} detected, {hidden} hidden");
        Assert.True(attacksOnHidden > 50, $"{attacksOnHidden} attacks on hidden units");
        Assert.True(reveals > 0, "no reveal");
    }

    private static Simulation FuzzSim(ulong seed)
    {
        var sim = new Simulation(new SimConfig(seed, 3, 120, 2048) { Data = Data }, LocalMovementTests.Flat(64));
        var rng = new SimRng(seed, 4800);
        int[] types = { Infantry, Infantry, Infantry, Bowman, Bowman, Spotter10, Worker };
        for (int p = 0; p < 3; p++)
        {
            int cx = 16 + 16 * p, cy = p == 1 ? 40 : 24;
            TowerTests.PlaceBuilding(sim, p, Watchtower, cx, cy + (p == 1 ? 8 : -10));
            for (int k = 0; k < 22; k++)
            {
                EntityHandle h = Place(sim, p, types[k % types.Length], At(sim, cx + rng.NextInt(-6, 7), cy + rng.NextInt(-6, 7), rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f));
                sim.World.Units.Hp[h.Index] *= 4; // long fights
            }
        }
        return sim;
    }

    /// <summary>Random orders and stealth changes for this tick (identical for twins); returns the Attacks given on a unit hidden from the attacker's owner.</summary>
    private static int FuzzOrders(Simulation sim, ref SimRng rng, int[] ordered)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        int onHidden = 0;
        for (int n = 0; n < 6; n++)
        {
            int i = rng.NextInt(0, u.Capacity);
            int kind = rng.NextInt(0, 10);
            if (!u.Alive[i]) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            int owner = u.Owner[i];
            if (kind <= 1)
            {
                int ticks = rng.NextInt(1, 400);
                if (u.TypeId[i] != Spotter10) StealthTests.Stealth(sim, h, ticks);
                continue;
            }
            if (kind == 2)
            {
                StatusSystem.Apply(w, i, Revealed, 0f, rng.NextInt(1, 80), owner);
                continue;
            }
            Vector2 to = At(sim, rng.NextInt(4, 60), rng.NextInt(4, 60));
            if (kind <= 5)
            {
                int j = rng.NextInt(0, u.Capacity);
                if (!u.Alive[j] || u.Owner[j] == owner) continue;
                if (w.HiddenFrom(owner, j)) onHidden++;
                ordered[i] = j;
                sim.Enqueue(Command.Attack(owner, h, new EntityHandle(j, u.Generation[j]), isBuilding: false, queued: kind == 5));
            }
            else if (kind <= 7) sim.Enqueue(Command.AttackMove(owner, h, to));
            else if (kind == 8) sim.Enqueue(Command.Move(owner, h, to));
            else sim.Enqueue(Command.Stop(owner, h));
        }
        return onHidden;
    }
}
