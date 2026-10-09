using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-1 (combat slice 1): edges of acquisition, chase, leash, hold, workers, buildings as targets, and the
/// phase-11 symmetry. Bug regressions are named after their bug file.
/// </summary>
public class CombatQaTests
{
    private readonly ITestOutputHelper _out;

    public CombatQaTests(ITestOutputHelper output) => _out = output;

    private static UnitDef Def(int type) => TestSim.Data.Units[type];

    /// <summary>A sim on a map built from rows of level digits (a level change is a cliff; the ring is blocked).</summary>
    private static Simulation OnRows(int units, params string[] rows) =>
        new(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: units, CommandCapacity: 8 * units + 32), LocalMovementTests.Rows(rows));

    private static string[] PlateauMap()
    {
        // 40 x 40 ground with a level-1 plateau at x 20-35, y 5-34, no ramp: unreachable from the ground.
        var rows = new string[40];
        for (int y = 0; y < 40; y++)
        {
            var c = new char[40];
            for (int x = 0; x < 40; x++) c[x] = x >= 20 && x <= 35 && y >= 5 && y <= 34 ? '1' : '0';
            rows[y] = new string(c);
        }
        return rows;
    }

    // ---------- phase-11 symmetry: who is in which slot must not decide a fair duel ----------

    /// <summary>
    /// Two Heavy Infantry attack-moved into each other from 40 m, in every slot arrangement (either side first, 0-3
    /// filler units in between so the two land in every pair of scan phases): both die, on the same tick, and that tick
    /// is the same in every arrangement.
    /// </summary>
    [Fact]
    public void AttackMoveDuel_AnySlotOrder_BothDieOnTheSameTick_TheSameTickInEveryArrangement()
    {
        var deathTicks = new List<(bool P1First, int Fillers, int TickA, int TickB)>();
        foreach (bool p1First in new[] { false, true })
        {
            for (int fillers = 0; fillers < 4; fillers++)
            {
                Simulation sim = Flat();
                EntityHandle a = default, b = default;
                void PlaceA() => a = Place(sim, 0, HeavyInfantry, At(sim, 10, 24));
                void PlaceB() => b = Place(sim, 1, HeavyInfantry, At(sim, 30, 24));
                if (p1First) PlaceB(); else PlaceA();
                for (int k = 0; k < fillers; k++) Place(sim, 0, Laborer, At(sim, 2 + k, 2)); // far corner, out of sight
                if (p1First) PlaceA(); else PlaceB();
                UnitStore u = sim.World.Units;
                Vector2 pa = u.Position[a.Index], pb = u.Position[b.Index];
                // Orders in the same slot-independent order: player 0's first.
                sim.Enqueue(Command.AttackMove(0, a, pb));
                sim.Enqueue(Command.AttackMove(1, b, pa));
                int ta = -1, tb = -1;
                for (int t = 0; t < 3000 && (ta < 0 || tb < 0); t++)
                {
                    sim.Tick();
                    if (ta < 0 && !u.IsAlive(a)) ta = t;
                    if (tb < 0 && !u.IsAlive(b)) tb = t;
                }
                deathTicks.Add((p1First, fillers, ta, tb));
            }
        }
        foreach (var d in deathTicks) _out.WriteLine($"p1First {d.P1First} fillers {d.Fillers}: a dies {d.TickA}, b dies {d.TickB}");
        Assert.All(deathTicks, d => Assert.True(d.TickA >= 0 && d.TickA == d.TickB, $"p1First {d.P1First} fillers {d.Fillers}: {d.TickA} vs {d.TickB}"));
        Assert.Single(deathTicks.Select(d => d.TickA).Distinct());
    }

    /// <summary>
    /// Report: two equal Heavy Infantry spawned already in reach of each other, Idle, in slots whose scan phases differ.
    /// The first to scan swings first; the brief's staggered scan makes the slot decide the duel. Not asserted (the
    /// stagger is the brief's rule); the report shows how much it decides.
    /// </summary>
    [Fact]
    public void Report_IdleDuelInReach_SlotScanPhaseDecidesTheWinner()
    {
        foreach (int fillers in new[] { 0, 1, 2, 3 })
        {
            Simulation sim = Flat();
            EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 20));
            for (int k = 0; k < fillers; k++) Place(sim, 0, Laborer, At(sim, 2 + k, 2));
            EntityHandle b = Place(sim, 1, HeavyInfantry, At(sim, 20, 20, dx: 1f));
            UnitStore u = sim.World.Units;
            int t = RunUntil(sim, () => !u.IsAlive(a) || !u.IsAlive(b), 3000);
            _out.WriteLine($"slots {a.Index} v {b.Index}: after {t} ticks a {(u.IsAlive(a) ? $"alive {u.Hp[a.Index]} hp" : "dead")}, b {(u.IsAlive(b) ? $"alive {u.Hp[b.Index]} hp" : "dead")}");
        }
    }

    // ---------- unreachable targets ----------

    /// <summary>
    /// An Idle Heavy Infantry on the ground sees an enemy on a cliff-top it cannot reach (12 m, inside sight). It must
    /// not walk and give up forever: in the last 1,000 of 2,000 ticks neither unit is Moving, and neither keeps issuing
    /// walks (a fresh walk every few ticks re-requests a flow field).
    /// </summary>
    [Fact] // regression: BUG-0137
    public void UnreachableEnemyInSight_ChaserDoesNotWalkAndGiveUpForever()
    {
        Simulation sim = OnRows(8, PlateauMap());
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 17, 20));
        EntityHandle b = Place(sim, 1, HeavyInfantry, At(sim, 23, 20));
        Spot(sim, 0, b); // M4-3a: up the cliff, a level-0 unit can't see it: spotted, so it is "in sight" as the row means
        UnitStore u = sim.World.Units;
        Assert.True(Vector2.Distance(u.Position[a.Index], u.Position[b.Index]) < Def(HeavyInfantry).Sight);
        int movingLate = 0, walksLate = 0, lastOrderA = u.OrderTick[a.Index], lastOrderB = u.OrderTick[b.Index];
        for (int t = 0; t < 2000; t++)
        {
            sim.Tick();
            if (t < 1000) { lastOrderA = u.OrderTick[a.Index]; lastOrderB = u.OrderTick[b.Index]; continue; }
            if (u.State[a.Index] == UnitState.Moving || u.State[b.Index] == UnitState.Moving) movingLate++;
            if (u.OrderTick[a.Index] != lastOrderA) walksLate++;
            if (u.OrderTick[b.Index] != lastOrderB) walksLate++;
            lastOrderA = u.OrderTick[a.Index];
            lastOrderB = u.OrderTick[b.Index];
        }
        _out.WriteLine($"last 1000 ticks: moving on {movingLate}, fresh walks {walksLate}; a {u.State[a.Index]} mode {u.Mode[a.Index]} target {u.Target[a.Index]}; b {u.State[b.Index]} mode {u.Mode[b.Index]}");
        Assert.True(movingLate == 0 && walksLate == 0, $"moving on {movingLate} of the last 1000 ticks, {walksLate} fresh walks");
    }

    // ---------- leash edges ----------

    /// <summary>
    /// A retaliating unit whose anchor cell is covered by one of its own new buildings while it fights: once the fight
    /// is over it walks back as near as it can and the engagement ends (mode None) within 30 s; it does not stay in
    /// Retaliate forever re-issuing a walk to a blocked anchor.
    /// </summary>
    [Fact] // regression: BUG-0141
    public void AnchorCellBlockedByANewBuilding_EngagementStillEnds()
    {
        Simulation sim = Flat();
        Vector2 home = At(sim, 20, 20);
        EntityHandle a = Place(sim, 0, HeavyInfantry, home);
        EntityHandle v = Place(sim, 1, Laborer, At(sim, 26, 20));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.HoldPosition(1, v)); // it stands; a comes to it
        RunUntil(sim, () => u.Mode[a.Index] == CombatMode.Retaliate && Vector2.Distance(u.Position[a.Index], home) > 4f, 200);
        Assert.Equal(CombatMode.Retaliate, u.Mode[a.Index]);
        Assert.Equal(home, u.AnchorPosition[a.Index]);
        // A 2 x 2 building of player 0 over the anchor cell (19-20, 19-20).
        int tent = TestSim.Data.FindBuilding("malazan_billet");
        GatherMaps.Building(sim, 19, 19, player: 0, type: tent);
        Assert.False(sim.World.NavGrid.IsPassable(20, 20));
        RunUntil(sim, () => !u.IsAlive(v), 1500);
        Assert.False(u.IsAlive(v));
        int t = RunUntil(sim, () => u.Mode[a.Index] == CombatMode.None, 600);
        _out.WriteLine($"after the kill: {t} ticks; a {u.State[a.Index]} mode {u.Mode[a.Index]} at {u.Position[a.Index]} goal {u.Goal[a.Index]} anchor {u.AnchorPosition[a.Index]}");
        Assert.Equal(CombatMode.None, u.Mode[a.Index]);
    }

    /// <summary>
    /// The same leash, after the target is killed by someone else before the retaliator ever plants: a unit that had
    /// walked somewhere, stands Idle, is hit and retaliates, whose attacker then dies, ends its engagement.
    /// </summary>
    [Fact]
    public void RetaliatorWhoseAttackerDiesBeforeItPlants_EndsItsEngagement()
    {
        Simulation sim = Flat();
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 10, 20));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, a, At(sim, 20, 20)));
        RunUntil(sim, () => u.State[a.Index] == UnitState.Idle && sim.TickNumber > 5, 400);
        Assert.Equal(UnitState.Idle, u.State[a.Index]);
        // An enemy appears in reach, takes a hit, is finished off by a second friendly.
        EntityHandle e = Place(sim, 1, Laborer, u.Position[a.Index] + new Vector2(1f, 0f));
        u.Hp[e.Index] = 1;
        RunUntil(sim, () => !u.IsAlive(e), 200);
        Assert.False(u.IsAlive(e));
        int t = RunUntil(sim, () => u.Mode[a.Index] == CombatMode.None && u.State[a.Index] == UnitState.Idle, 600);
        _out.WriteLine($"{t} ticks; a {u.State[a.Index]} mode {u.Mode[a.Index]} goal {u.Goal[a.Index]} anchor {u.AnchorPosition[a.Index]}");
        Assert.Equal(CombatMode.None, u.Mode[a.Index]);
    }

    // ---------- holders ----------

    /// <summary>
    /// A line of own holders filling a one-cell corridor, attacked by a column of enemies attack-moving through it: no
    /// holder ever moves (position bit-identical while alive), whatever hits it.
    /// </summary>
    [Fact]
    public void HoldersInACorridorUnderAttack_NeverMove()
    {
        // 30 x 12, a 1-cell corridor at y 6, x 8-21, walls (level 1) above and below.
        var rows = new string[12];
        for (int y = 0; y < 12; y++)
        {
            var c = new char[30];
            for (int x = 0; x < 30; x++) c[x] = x >= 8 && x <= 21 && y != 6 ? '1' : '0';
            rows[y] = new string(c);
        }
        Simulation sim = OnRows(64, rows);
        UnitStore u = sim.World.Units;
        var holders = new List<EntityHandle>();
        for (int k = 0; k < 4; k++) holders.Add(Place(sim, 0, HeavyInfantry, At(sim, 14 + k, 6)));
        var enemies = new List<EntityHandle>();
        for (int k = 0; k < 12; k++) enemies.Add(Place(sim, 1, Raider, At(sim, 24 + k % 3, 3 + k / 3)));
        foreach (EntityHandle h in holders) sim.Enqueue(Command.HoldPosition(0, h));
        foreach (EntityHandle e in enemies) sim.Enqueue(Command.AttackMove(1, e, At(sim, 3, 6)));
        sim.Tick();
        sim.Tick(); // the orders apply
        var at = holders.Select(h => u.Position[h.Index]).ToArray();
        bool fought = false;
        for (int t = 0; t < 3000; t++)
        {
            sim.Tick();
            for (int k = 0; k < holders.Count; k++)
            {
                if (!u.IsAlive(holders[k])) continue;
                Assert.True(u.Position[holders[k].Index] == at[k], $"tick {t}: holder {k} moved {at[k]} -> {u.Position[holders[k].Index]}");
                Assert.True(u.Hold[holders[k].Index]);
                fought |= u.State[holders[k].Index] == UnitState.Attacking;
            }
        }
        Assert.True(fought);
    }

    // ---------- workers ----------

    /// <summary>
    /// A worker walking to a build site is hit by an enemy holding beside its path, then hit again while it builds:
    /// it never takes a target or a combat mode, never stands Attacking, and keeps its build order.
    /// </summary>
    [Fact]
    public void WorkerOnABuildOrder_IsHit_NeverRetaliates()
    {
        Simulation sim = TestSim.Explored(Flat()); // M4-3b: the site is 48 m from the worker
        sim.World.Ledger.Gold[0] = 10_000;
        sim.World.Ledger.Wood[0] = 10_000;
        EntityHandle w = Place(sim, 0, Laborer, At(sim, 6, 20));
        UnitStore u = sim.World.Units;
        // An Idle enemy Horse Raider (faster than the worker) 3 m off the path, out of sight at first: it rides the worker down.
        Place(sim, 1, TestSim.Data.FindUnit("whirlwind_horse_raider"), At(sim, 18, 20, dy: 3f));
        int billet = TestSim.Data.FindBuilding("malazan_billet");
        sim.Enqueue(Command.Build(0, w, billet, At(sim, 30, 20)));
        sim.Tick();
        sim.Tick();
        Assert.NotEqual(default, u.BuildTarget[w.Index]);
        int hitsWalking = 0, hitsBuilding = 0, prevHp = u.Hp[w.Index];
        bool placedSecond = false;
        for (int t = 0; t < 1500 && u.IsAlive(w); t++)
        {
            sim.Tick();
            if (!u.IsAlive(w)) break;
            if (u.Hp[w.Index] < prevHp)
            {
                if (u.State[w.Index] == UnitState.Building) hitsBuilding++; else hitsWalking++;
                u.Hp[w.Index] = Def(Laborer).Hp; // keep it alive: the subject is its behavior
            }
            prevHp = u.Hp[w.Index];
            if (u.BuildTarget[w.Index] == default) break; // finished
            if (!placedSecond && u.State[w.Index] == UnitState.Building)
            {
                placedSecond = true;
                // A fighter, not a Laborer: since the M4-1 worker rule (Producer decision) a holding worker never swings.
                EntityHandle e2 = Place(sim, 1, HeavyInfantry, u.Position[w.Index] + new Vector2(0f, 1f));
                sim.Enqueue(Command.HoldPosition(1, e2));
            }
            Assert.True(u.Target[w.Index] == default, $"tick {t}: worker took target {u.Target[w.Index]} ({u.State[w.Index]})");
            Assert.Equal(CombatMode.None, u.Mode[w.Index]);
            Assert.NotEqual(UnitState.Attacking, u.State[w.Index]);
        }
        _out.WriteLine($"hits taken walking {hitsWalking}, building {hitsBuilding}");
        Assert.True(hitsWalking > 0 && hitsBuilding > 0, $"setup: hits walking {hitsWalking}, building {hitsBuilding}");
    }

    // ---------- data conformance ----------

    /// <summary>docs/factions/whirlwind.md: "Battering Ram: attacks buildings only." A ram next to an enemy worker must not hit it.</summary>
    [Fact] // BUG-0139, closed by attack.targets (M4-2a)
    public void BatteringRam_AttacksBuildingsOnly_NeverAUnit()
    {
        Simulation sim = Flat();
        int ram = TestSim.Data.FindUnit("whirlwind_battering_ram");
        EntityHandle r = Place(sim, 1, ram, At(sim, 20, 20));
        EntityHandle w = Place(sim, 0, Laborer, At(sim, 20, 20, dx: 1.5f));
        sim.Enqueue(Command.HoldPosition(0, w));
        UnitStore u = sim.World.Units;
        int full = u.Hp[w.Index];
        for (int t = 0; t < 400; t++) sim.Tick();
        _out.WriteLine($"laborer {(u.IsAlive(w) ? $"{u.Hp[w.Index]} / {full} hp" : "dead")}; ram target {u.Target[r.Index]}");
        Assert.True(u.IsAlive(w) && u.Hp[w.Index] == full, "the ram hit a unit");
    }

    // ---------- buildings as targets ----------

    /// <summary>
    /// Damage to a site under construction with a builder on it must stick: a 100-point hit (the path every melee hit on
    /// a building takes, <c>BuildingStore.Damage</c>) leaves the site below where it stood before the hit after the
    /// next tick's building work (docs/03 "Economy implementation": hit points grow with progress), and a site that
    /// took more damage than its full hit points while being built does not complete at full hit points.
    /// </summary>
    [Fact] // regression: BUG-0138
    public void SiteBeingBuilt_DamageSticks_TheNextBuildTickDoesNotUndoIt()
    {
        Simulation sim = Flat(units: 16);
        sim.World.Ledger.Wood[1] = 10_000;
        int tent = TestSim.Data.FindBuilding("whirlwind_tent");
        EntityHandle w = Place(sim, 1, TestSim.Data.FindUnit("whirlwind_camp_follower"), At(sim, 22, 20));
        sim.Enqueue(Command.Build(1, w, tent, At(sim, 24, 20)));
        UnitStore u = sim.World.Units;
        BuildingStore b = sim.World.Buildings;
        RunUntil(sim, () => b.Count == 1, 20);
        int site = -1;
        for (int k = 0; k < b.Capacity; k++) if (b.Alive[k]) site = k;
        EntityHandle sh = b.HandleOf(site);
        RunUntil(sim, () => b.Hp[site] > 200, 1000);
        Assert.True(b.UnderConstruction[site] && b.Hp[site] > 200);
        int before = b.Hp[site];
        b.Damage(sh, 100);
        sim.Tick();
        int after = b.Hp[site];
        long dealt = 100;
        while (b.IsAlive(sh) && b.UnderConstruction[site])
        {
            if (b.Hp[site] > 150) { b.Damage(sh, 100); dealt += 100; }
            sim.Tick();
        }
        _out.WriteLine($"hp {before} -> hit 100 -> next tick {after}; damage dealt while building {dealt} (max hp {TestSim.Data.Buildings[tent].Hp}); site {(b.IsAlive(sh) ? $"completed at {b.Hp[site]} hp" : "destroyed")}");
        Assert.True(after < before, $"a 100-point hit on a {before}-hp site was undone by the next build tick ({after} hp)");
        Assert.False(b.IsAlive(sh) && b.Hp[site] == TestSim.Data.Buildings[tent].Hp, $"took {dealt} damage while being built and completed at full hit points");
    }

    // ---------- unreachable targets on the attack-move ----------

    /// <summary>
    /// An attack-move along the foot of a cliff whose top holds an enemy in sight: the unit cannot reach it, so it must
    /// not stop for good; it arrives at its destination within 60 s.
    /// </summary>
    [Fact] // regression: BUG-0137
    public void AttackMovePastAnUnreachableEnemyOnACliff_StillArrives()
    {
        Simulation sim = OnRows(8, PlateauMap());
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 17, 36));
        EntityHandle top = Place(sim, 1, Crossbowman, At(sim, 22, 20)); // a target that never swings
        Spot(sim, 0, top); // M4-3a: up the cliff, a level-0 unit can't see it: spotted, so it is "in sight" as the row means
        UnitStore u = sim.World.Units;
        Vector2 dest = At(sim, 17, 2);
        sim.Enqueue(Command.AttackMove(0, a, dest));
        int t = RunUntil(sim, () => sim.TickNumber > 5 && u.State[a.Index] == UnitState.Idle && u.Mode[a.Index] == CombatMode.None, 1200);
        _out.WriteLine($"{t} ticks: a {u.State[a.Index]} mode {u.Mode[a.Index]} target {u.Target[a.Index]} at {u.Position[a.Index]}, {Vector2.Distance(u.Position[a.Index], dest):F1} m from the destination");
        Assert.True(Vector2.Distance(u.Position[a.Index], dest) < 2f, $"stuck {Vector2.Distance(u.Position[a.Index], dest):F1} m short, at {u.Position[a.Index]}, target {u.Target[a.Index]}");
    }

    /// <summary>
    /// A wall between an Idle Heavy Infantry and an enemy holder 12 m away (inside sight), the only way round 30+ m
    /// long. Whatever the unit does about it (chase and give up at the leash, or ignore it), it must settle: in ticks
    /// 2,000-4,000 it is never Moving. Before the fix it chases, loses sight or hits the leash, walks home, sees the
    /// enemy again and starts over, forever.
    /// </summary>
    [Fact] // regression: BUG-0137
    public void EnemyInSightBehindAWall_IdleUnitSettles_NoChaseAndReturnCycleForever()
    {
        // 40 x 40, a 2-wide wall (level 1) at x 19-20 from y 6 to the bottom ring: the way round is over the top.
        var rows = new string[40];
        for (int y = 0; y < 40; y++)
        {
            var c = new char[40];
            for (int x = 0; x < 40; x++) c[x] = (x == 19 || x == 20) && y >= 6 ? '1' : '0';
            rows[y] = new string(c);
        }
        Simulation sim = OnRows(8, rows);
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 16, 30));
        EntityHandle e = Place(sim, 1, Crossbowman, At(sim, 22, 30)); // never swings in this slice
        sim.Enqueue(Command.HoldPosition(1, e));
        UnitStore u = sim.World.Units;
        int movingLate = 0, cyclesLate = 0;
        CombatMode prev = CombatMode.None;
        for (int t = 0; t < 4000; t++)
        {
            sim.Tick();
            if (t < 2000) { prev = u.Mode[a.Index]; continue; }
            if (u.State[a.Index] == UnitState.Moving) movingLate++;
            if (prev == CombatMode.None && u.Mode[a.Index] != CombatMode.None) cyclesLate++;
            prev = u.Mode[a.Index];
        }
        _out.WriteLine($"ticks 2000-4000: Moving on {movingLate}, engagements started {cyclesLate}; a {u.State[a.Index]} {u.Mode[a.Index]} at {u.Position[a.Index]}");
        Assert.True(movingLate == 0, $"still chasing and walking home: Moving on {movingLate} of 2,000 ticks, {cyclesLate} new engagements");
    }
    // ---------- hash scheme on a unit already mid-fight ----------

    /// <summary>
    /// The dev's audit flips each combat field on a unit at its spawn values. Here the unit is mid-fight (flag bit already
    /// set, several fields non-default): each field changed alone still flips the hash, and the per-player kills and
    /// losses of both players do too.
    /// </summary>
    [Fact]
    public void Hash_EveryCombatField_FlipsOnAUnitAlreadyMidFight()
    {
        Simulation sim = Flat();
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 20));
        EntityHandle b = Place(sim, 1, HeavyInfantry, At(sim, 20, 20, dx: 1f));
        UnitStore u = sim.World.Units;
        RunUntil(sim, () => u.Hp[a.Index] < Def(HeavyInfantry).Hp && u.Target[a.Index] == b && u.LastAttacker[a.Index] == b, 200);
        Assert.Equal(b, u.Target[a.Index]);
        Assert.Equal(b, u.LastAttacker[a.Index]);
        Assert.NotEqual(CombatMode.None, u.Mode[a.Index]);
        ulong h0 = sim.StateHash();
        int i = a.Index;
        void Flip(string name, Action change, Action undo)
        {
            change();
            Assert.True(sim.StateHash() != h0, $"{name} changed on a unit mid-fight but the hash did not");
            undo();
            Assert.Equal(h0, sim.StateHash());
        }
        int hp = u.Hp[i], cd = u.CooldownTicks[i], wu = u.WindupTicks[i];
        EntityHandle tg = u.Target[i], la = u.LastAttacker[i];
        Vector2 an = u.AnchorPosition[i];
        CombatMode mo = u.Mode[i];
        Flip("Hp", () => u.Hp[i] = hp - 1, () => u.Hp[i] = hp);
        Flip("Target.Index", () => u.Target[i] = new EntityHandle(tg.Index + 1, tg.Generation), () => u.Target[i] = tg);
        Flip("Target.Generation", () => u.Target[i] = new EntityHandle(tg.Index, tg.Generation + 1), () => u.Target[i] = tg);
        Flip("TargetIsBuilding", () => u.TargetIsBuilding[i] = true, () => u.TargetIsBuilding[i] = false);
        Flip("CooldownTicks", () => u.CooldownTicks[i] = cd + 1, () => u.CooldownTicks[i] = cd);
        Flip("WindupTicks", () => u.WindupTicks[i] = wu + 1, () => u.WindupTicks[i] = wu);
        Flip("LastAttacker.Index", () => u.LastAttacker[i] = new EntityHandle(la.Index + 1, la.Generation), () => u.LastAttacker[i] = la);
        Flip("LastAttacker.Generation", () => u.LastAttacker[i] = new EntityHandle(la.Index, la.Generation + 1), () => u.LastAttacker[i] = la);
        Flip("AnchorPosition", () => u.AnchorPosition[i] = an + new Vector2(0f, 1e-3f), () => u.AnchorPosition[i] = an);
        Flip("Mode", () => u.Mode[i] = mo == CombatMode.Returning ? CombatMode.AttackMove : CombatMode.Returning, () => u.Mode[i] = mo);
        var ledger = sim.World.Ledger;
        for (int p = 0; p < 2; p++)
        {
            int q = p;
            Flip($"Kills[{p}]", () => ledger.Kills[q]++, () => ledger.Kills[q]--);
            Flip($"Losses[{p}]", () => ledger.Losses[q]++, () => ledger.Losses[q]--);
        }
    }

    // ---------- buildings as targets ----------

    /// <summary>
    /// An enemy site under attack is cancelled by its owner while the attacker's swing is winding up: no hit lands
    /// anywhere, no death event, no kill, and the attacker drops the dead handle the same tick.
    /// </summary>
    [Fact]
    public void BuildingCancelledMidWindup_NoHitNoKill_AttackerDropsIt()
    {
        Simulation sim = TestSim.Explored(Flat()); // M4-3b: the site is 44 m from the worker
        int tent = TestSim.Data.FindBuilding("whirlwind_tent");
        sim.World.Ledger.Wood[1] = 10_000;
        EntityHandle w = Place(sim, 1, TestSim.Data.FindUnit("whirlwind_camp_follower"), At(sim, 2, 2));
        sim.Enqueue(Command.Build(1, w, tent, At(sim, 24, 20)));
        BuildingStore b = sim.World.Buildings;
        RunUntil(sim, () => b.Count == 1, 10);
        sim.Enqueue(Command.Stop(1, w)); // a site with nobody on it
        int site = -1;
        for (int k = 0; k < b.Capacity; k++) if (b.Alive[k]) site = k;
        EntityHandle sh = b.HandleOf(site);
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 23, 20, dx: -0.2f));
        UnitStore u = sim.World.Units;
        RunUntil(sim, () => u.Target[a.Index] == sh && u.WindupTicks[a.Index] > 2, 100);
        Assert.True(u.WindupTicks[a.Index] > 2, "setup: never wound up on the site");
        Assert.True(u.TargetIsBuilding[a.Index]);
        sim.Enqueue(Command.Cancel(1, At(sim, 24, 20)));
        sim.Tick();
        sim.Tick(); // the Cancel applies
        Assert.False(b.IsAlive(sh));
        Assert.Equal(default, u.Target[a.Index]);
        Assert.Equal(0, u.WindupTicks[a.Index]);
        for (int t = 0; t < 20; t++) sim.Tick();
        Assert.Equal(0, sim.World.Kills[0]);
        Assert.Equal(0, sim.World.Losses[1]);
    }

    /// <summary>Two attackers whose hits land on a 1-hp building the same tick: one death event, one kill, one loss.</summary>
    [Fact]
    public void BuildingKilledByTwoHitsInOneTick_OneDeathEvent_OneKill()
    {
        Simulation sim = Flat();
        int tent = TestSim.Data.FindBuilding("whirlwind_tent");
        EntityHandle site = GatherMaps.Building(sim, 24, 19, player: 1, type: tent);
        BuildingStore b = sim.World.Buildings;
        // Slots 0 and 4: same scan phase, so both swing on the same tick.
        EntityHandle a1 = Place(sim, 0, HeavyInfantry, At(sim, 23, 19, dx: 0.4f));
        for (int k = 0; k < 3; k++) Place(sim, 0, Laborer, At(sim, 2 + k, 2));
        EntityHandle a2 = Place(sim, 0, HeavyInfantry, At(sim, 23, 20, dx: 0.4f));
        Assert.Equal(a1.Index + 4, a2.Index);
        UnitStore u = sim.World.Units;
        RunUntil(sim, () => u.WindupTicks[a1.Index] == 1 && u.WindupTicks[a2.Index] == 1, 100);
        Assert.True(u.WindupTicks[a1.Index] == 1 && u.WindupTicks[a2.Index] == 1, "setup: the two swings do not land together");
        b.SetHp(site.Index, 1);
        sim.Tick();
        Assert.False(b.IsAlive(site));
        Assert.Single(sim.World.Deaths.ToArray());
        Assert.Equal(1, sim.World.Kills[0]);
        Assert.Equal(1, sim.World.Losses[1]);
    }

    // ---------- leash anchored on a ramp ----------

    /// <summary>
    /// A retaliator standing on a ramp (the M1 RampCorridor map: plateau rows 1-6, a 3-wide ramp at x 6-8 down rows 7-10)
    /// chases a faster enemy off the ramp past its leash, walks back and ends Idle, mode None, on its ramp cell.
    /// </summary>
    [Fact]
    public void LeashAnchoredOnARamp_WalksBackAndEndsOnTheRampCell()
    {
        const int w = 16, h = 18, rampLength = 4;
        const float H = MapConstants.LevelHeight;
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        for (int y = 1; y <= 6; y++)
            for (int x = 1; x < w - 1; x++)
            {
                levels[y * w + x] = 1;
                elevations[y * w + x] = H;
            }
        for (int k = 1; k <= rampLength; k++)
            for (int j = 0; j < 3; j++)
                elevations[(6 + k) * w + 6 + j] = H * (rampLength + 1 - k) / (rampLength + 1);
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 64), new Heightmap(w, h, levels, elevations));
        Assert.True(sim.World.NavGrid.IsPassable(7, 8));
        Vector2 home = At(sim, 7, 8);
        EntityHandle a = Place(sim, 0, HeavyInfantry, home);
        EntityHandle c = Place(sim, 1, Crossbowman, At(sim, 7, 8, dy: 3f));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(1, c, At(sim, 14, 16)));
        bool returned = false;
        int t = 0;
        for (; t < 1500; t++)
        {
            sim.Tick();
            returned |= u.Mode[a.Index] == CombatMode.Returning;
            if (t > 20 && u.State[a.Index] == UnitState.Idle && u.Mode[a.Index] == CombatMode.None) break;
        }
        _out.WriteLine($"{t} ticks, returned {returned}: a {u.State[a.Index]} {u.Mode[a.Index]} at {u.Position[a.Index]} (home {home})");
        Assert.Equal(UnitState.Idle, u.State[a.Index]);
        Assert.Equal(CombatMode.None, u.Mode[a.Index]);
        Assert.True(sim.World.NavGrid.WorldToCell(u.Position[a.Index], out int ax, out int ay));
        Assert.Equal((7, 8), (ax, ay));
    }

    // ---------- placement vs a fighting worker ----------

    /// <summary>
    /// The view's placement answer (CanPlace) and the Build it previews must agree: the player's own worker stands
    /// Attacking inside a footprint and is given the Build there.
    /// </summary>
    [Fact(Skip = "BUG-0142 item 1: CanPlace counts the issuing worker, Build does not")]
    public void CanPlace_AgreesWithBuild_WhenTheIssuingWorkerIsAttackingInsideTheFootprint()
    {
        Simulation sim = Flat();
        sim.World.Ledger.Gold[0] = 10_000;
        sim.World.Ledger.Wood[0] = 10_000;
        int billet = TestSim.Data.FindBuilding("malazan_billet");
        EntityHandle w = Place(sim, 0, Laborer, At(sim, 20, 20));
        Place(sim, 1, HeavyInfantry, At(sim, 20, 20, dx: 1f));
        UnitStore u = sim.World.Units;
        u.Hp[w.Index] = int.MaxValue / 2;
        RunUntil(sim, () => u.State[w.Index] == UnitState.Attacking, 40);
        Assert.Equal(UnitState.Attacking, u.State[w.Index]);
        NavGrid g = sim.World.NavGrid;
        int anchor = 20 * g.Width + 19; // a 2 x 2 at (19-20, 20-21) covers the worker's cell
        sim.Enqueue(Command.Build(0, w, billet, At(sim, 19, 20)));
        sim.Tick(); // the Build applies on the next tick: ask CanPlace on the state it will see
        bool ok = sim.World.CanPlace(0, billet, anchor, out PlacementError why);
        int before = sim.World.Buildings.Count;
        sim.Tick();
        bool placed = sim.World.Buildings.Count == before + 1;
        _out.WriteLine($"CanPlace {ok} ({why}), Build placed {placed}");
        Assert.Equal(ok, placed);
    }
}
