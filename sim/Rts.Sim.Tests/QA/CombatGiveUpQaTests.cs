using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-1 re-check (fix round 1): the BUG-0137 give-up memory (<c>ChaseStall</c>, <c>Ignored</c>, <c>GiveUps</c>) must
/// not make a new livelock, and the Producer's worker rule (workers never fight on their own) must hold in real
/// openings. Bounds are stated in each row.
/// </summary>
public class CombatGiveUpQaTests
{
    private readonly ITestOutputHelper _out;

    public CombatGiveUpQaTests(ITestOutputHelper output) => _out = output;

    private static Simulation OnRows(int units, params string[] rows) =>
        new(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: units, CommandCapacity: 8 * units + 32), LocalMovementTests.Rows(rows));

    /// <summary>40 x 40 ground with a level-1 plateau at x 20-35, y 5-34 and no ramp: unreachable from the ground.</summary>
    private static string[] PlateauMap()
    {
        var rows = new string[40];
        for (int y = 0; y < 40; y++)
        {
            var c = new char[40];
            for (int x = 0; x < 40; x++) c[x] = x >= 20 && x <= 35 && y >= 5 && y <= 34 ? '1' : '0';
            rows[y] = new string(c);
        }
        return rows;
    }

    /// <summary>Counts engagements (target taken from none), Moving ticks and fresh walks (order-tick changes) of one unit over a tick window.</summary>
    private sealed class Watch
    {
        private readonly UnitStore _u;
        private readonly int _i;
        private bool _hadTarget;
        private int _lastOrder;
        public int Engagements, Moving, Walks;

        public Watch(UnitStore u, EntityHandle h)
        {
            _u = u;
            _i = h.Index;
            _lastOrder = u.OrderTick[_i];
        }

        public void Step()
        {
            bool has = _u.Target[_i] != default;
            if (has && !_hadTarget) Engagements++;
            _hadTarget = has;
            if (_u.State[_i] == UnitState.Moving) Moving++;
            if (_u.OrderTick[_i] != _lastOrder) Walks++;
            _lastOrder = _u.OrderTick[_i];
        }

        public void Reset() => Engagements = Moving = Walks = 0;
    }

    /// <summary>
    /// Two (then three) enemies on a cliff-top, all in sight and none reachable: the "last given up" memory holds only
    /// one, so they could take turns forever. Bound: at most <see cref="CombatConstants.MaxGiveUps"/> engagements in
    /// all, Idle with no combat mode within 400 ticks (20 s) of the first scan, within 1 m of where it stood, and then
    /// no engagement, Moving tick or fresh walk for 3,000 ticks.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void UnreachableEnemiesOnACliff_TakingTurns_EndAfterMaxGiveUps_AndTheUnitSettlesAtHome(int enemies)
    {
        Simulation sim = OnRows(8, PlateauMap());
        Vector2 home = At(sim, 17, 20);
        EntityHandle a = Place(sim, 0, HeavyInfantry, home);
        int[] ys = { 17, 23, 20 };
        for (int k = 0; k < enemies; k++)
        {
            EntityHandle e = Place(sim, 1, Raider, At(sim, 22, ys[k])); // a melee holder on the cliff: never in reach, never swings (M4-2b: ranged units shoot now)
            sim.Enqueue(Command.HoldPosition(1, e));
            Assert.True(Vector2.Distance(home, sim.World.Units.Position[e.Index]) < TestSim.Data.Units[HeavyInfantry].Sight);
        }
        UnitStore u = sim.World.Units;
        var watch = new Watch(u, a);
        int settled = -1;
        for (int t = 0; t < 400; t++)
        {
            sim.Tick();
            watch.Step();
            if (settled < 0 && watch.Engagements > 0 && u.Target[a.Index] == default && u.State[a.Index] == UnitState.Idle
                && u.Mode[a.Index] == CombatMode.None && u.GiveUps[a.Index] >= CombatConstants.MaxGiveUps)
                settled = t;
        }
        _out.WriteLine($"{enemies} enemies: {watch.Engagements} engagements, settled on tick {settled}, {Vector2.Distance(u.Position[a.Index], home):F2} m from home, give-ups {u.GiveUps[a.Index]}");
        Assert.True(watch.Engagements >= 1 && watch.Engagements <= CombatConstants.MaxGiveUps, $"{watch.Engagements} engagements");
        Assert.True(settled >= 0, $"not settled in 400 ticks: {u.State[a.Index]} mode {u.Mode[a.Index]} target {u.Target[a.Index]}");
        Assert.True(Vector2.Distance(u.Position[a.Index], home) < 1f, $"{Vector2.Distance(u.Position[a.Index], home):F2} m from home");
        watch.Reset();
        for (int t = 0; t < 3000; t++)
        {
            sim.Tick();
            watch.Step();
        }
        Assert.True(watch.Engagements == 0 && watch.Moving == 0 && watch.Walks == 0,
            $"after settling: {watch.Engagements} engagements, Moving on {watch.Moving} ticks, {watch.Walks} walks");
    }

    /// <summary>
    /// A unit already at <see cref="CombatConstants.MaxGiveUps"/> (it gave up three cliff-top enemies) is attacked by a
    /// reachable enemy that walks into reach: it fights back and kills it, and its landed hit resets the count. The
    /// cliff-top enemies are still there, so it may chase them again, but at most MaxGiveUps more times, and it is
    /// Idle at home within 600 ticks (30 s) of the kill, then still for 2,000 ticks.
    /// </summary>
    [Fact]
    public void AtMaxGiveUps_AReachableAttackerIsFought_TheResetDoesNotRestartAnEndlessCycle()
    {
        Simulation sim = OnRows(8, PlateauMap());
        Vector2 home = At(sim, 17, 20);
        EntityHandle a = Place(sim, 0, HeavyInfantry, home);
        foreach (int y in new[] { 17, 23, 20 })
            sim.Enqueue(Command.HoldPosition(1, Place(sim, 1, Raider, At(sim, 22, y))));
        UnitStore u = sim.World.Units;
        RunUntil(sim, () => u.GiveUps[a.Index] >= CombatConstants.MaxGiveUps && u.Mode[a.Index] == CombatMode.None
            && u.State[a.Index] == UnitState.Idle, 600);
        Assert.Equal(CombatConstants.MaxGiveUps, u.GiveUps[a.Index]);
        // A weakened Raider comes at it from the open ground.
        EntityHandle r = Place(sim, 1, Raider, At(sim, 8, 20));
        u.Hp[r.Index] = 25;
        sim.Enqueue(Command.AttackMove(1, r, home));
        bool fought = false, reset = false;
        int kill = RunUntil(sim, () =>
        {
            fought |= u.Target[a.Index] == r;
            reset |= fought && u.GiveUps[a.Index] == 0;
            return !u.IsAlive(r) || !u.IsAlive(a);
        }, 1200);
        _out.WriteLine($"raider dead after {kill} ticks; a fought {fought}, count reset {reset}, a hp {u.Hp[a.Index]}");
        Assert.True(u.IsAlive(a) && !u.IsAlive(r), "the raider was not killed");
        Assert.True(fought && reset);
        var watch = new Watch(u, a);
        int settled = RunUntil(sim, () =>
        {
            watch.Step();
            return u.State[a.Index] == UnitState.Idle && u.Mode[a.Index] == CombatMode.None && u.Target[a.Index] == default
                && u.GiveUps[a.Index] >= CombatConstants.MaxGiveUps;
        }, 600);
        _out.WriteLine($"after the kill: {watch.Engagements} engagements, settled after {settled} ticks, {Vector2.Distance(u.Position[a.Index], home):F2} m from home");
        Assert.True(settled < 600, $"not settled: {u.State[a.Index]} mode {u.Mode[a.Index]} target {u.Target[a.Index]} give-ups {u.GiveUps[a.Index]}");
        Assert.True(watch.Engagements <= CombatConstants.MaxGiveUps, $"{watch.Engagements} engagements after the kill");
        Assert.True(Vector2.Distance(u.Position[a.Index], home) < 4f, $"{Vector2.Distance(u.Position[a.Index], home):F2} m from home");
        watch.Reset();
        for (int t = 0; t < 2000; t++)
        {
            sim.Tick();
            watch.Step();
        }
        Assert.True(watch.Engagements == 0 && watch.Moving == 0 && watch.Walks == 0,
            $"after settling: {watch.Engagements} engagements, Moving on {watch.Moving} ticks, {watch.Walks} walks");
    }

    /// <summary>
    /// An attack-move along the foot of a cliff past five cliff-top enemies (more than <see cref="CombatConstants.MaxGiveUps"/>),
    /// all in sight on the way, none reachable: it arrives within 2 m of its destination within 900 ticks (45 s; the
    /// walk alone is about 23 s) and ends its mode there.
    /// </summary>
    [Fact]
    public void AttackMovePastFiveCliffTopEnemies_Arrives()
    {
        Simulation sim = OnRows(12, PlateauMap());
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 17, 36));
        foreach (int y in new[] { 8, 14, 20, 26, 32 })
            sim.Enqueue(Command.HoldPosition(1, Place(sim, 1, Raider, At(sim, 21, y))));
        UnitStore u = sim.World.Units;
        Vector2 dest = At(sim, 17, 2);
        sim.Enqueue(Command.AttackMove(0, a, dest));
        var watch = new Watch(u, a);
        int t = RunUntil(sim, () =>
        {
            watch.Step();
            return sim.TickNumber > 5 && u.State[a.Index] == UnitState.Idle && u.Mode[a.Index] == CombatMode.None;
        }, 900);
        _out.WriteLine($"{t} ticks, {watch.Engagements} engagements; {Vector2.Distance(u.Position[a.Index], dest):F1} m from the destination");
        Assert.True(t < 900 && Vector2.Distance(u.Position[a.Index], dest) < 2f,
            $"after {t} ticks: {u.State[a.Index]} mode {u.Mode[a.Index]}, {Vector2.Distance(u.Position[a.Index], dest):F1} m short");
    }

    /// <summary>
    /// An Idle unit sees an enemy holder 12 m away behind a wall (the way round 30+ m): it may try, but it is Idle,
    /// with no combat mode, within 1 m of where it stood within 300 ticks (15 s), and then never engages or walks again
    /// in 2,000 ticks.
    /// </summary>
    [Fact]
    public void RetaliatorBehindAWall_IsIdleAtItsAnchorWithin300Ticks_AndStaysThere()
    {
        var rows = new string[40];
        for (int y = 0; y < 40; y++)
        {
            var c = new char[40];
            for (int x = 0; x < 40; x++) c[x] = (x == 19 || x == 20) && y >= 6 ? '1' : '0';
            rows[y] = new string(c);
        }
        Simulation sim = OnRows(8, rows);
        Vector2 home = At(sim, 16, 30);
        EntityHandle a = Place(sim, 0, HeavyInfantry, home);
        EntityHandle e = Place(sim, 1, Raider, At(sim, 22, 30));
        sim.Enqueue(Command.HoldPosition(1, e));
        UnitStore u = sim.World.Units;
        var watch = new Watch(u, a);
        bool engaged = false;
        int t = RunUntil(sim, () =>
        {
            watch.Step();
            engaged |= u.Mode[a.Index] != CombatMode.None;
            return engaged && u.State[a.Index] == UnitState.Idle && u.Mode[a.Index] == CombatMode.None
                && Vector2.Distance(u.Position[a.Index], home) < 1f;
        }, 300);
        _out.WriteLine($"engaged {engaged}; Idle at home after {t} ticks ({watch.Engagements} engagements), {Vector2.Distance(u.Position[a.Index], home):F2} m off");
        Assert.True(engaged, "setup: it never engaged");
        Assert.True(t < 300, $"not Idle at home in 300 ticks: {u.State[a.Index]} mode {u.Mode[a.Index]}, {Vector2.Distance(u.Position[a.Index], home):F2} m off");
        watch.Reset();
        for (int k = 0; k < 2000; k++)
        {
            sim.Tick();
            watch.Step();
        }
        Assert.True(watch.Engagements == 0 && watch.Moving == 0 && watch.Walks == 0,
            $"afterwards: {watch.Engagements} engagements, Moving on {watch.Moving} ticks, {watch.Walks} walks");
    }

    // ---------- the worker rule in real openings ----------

    /// <summary>
    /// The match's own start (<c>Match.SpawnBases</c>: <see cref="StartBase.Plan"/> on a generated map with 8 gold mines and 12 forests, the data's
    /// starting workers round each Town Hall, no army), on four seeds; once with nobody given an order and once with every
    /// worker sent to its nearest gold mine. 6,000 ticks (5 min): no death, no worker ever takes a target or a combat mode.
    /// </summary>
    [Theory]
    [InlineData(1UL, false)]
    [InlineData(2UL, false)]
    [InlineData(3UL, true)]
    [InlineData(4UL, true)]
    public void MatchStartBases_NoArmy_IdleOrGathering_NoDeathsNoTargetsIn6000Ticks(ulong seed, bool gather)
    {
        var sim = new Simulation(TestSim.Config(seed, PlayerCount: 2, UnitCapacity: 200, CommandCapacity: 512)
            with { Map = MapGenParams.Default with { Forests = 12, GoldMines = 8 } });
        World w = sim.World;
        int workers = w.Data.Rules.StartingWorkers;
        Assert.True(workers > 0);
        var blocks = new[] { Array.Empty<Vector2>(), Array.Empty<Vector2>() };
        StartBasePlan plan = ViewReads.Plan(w, blocks, workers, StartBaseTests.MaxRadius(w.Data));
        NavGrid g = w.NavGrid;
        for (int p = 0; p < 2; p++)
        {
            Assert.True(plan.HallAnchor[p] >= 0, $"seed {seed}: no hall spot for player {p}");
            sim.Enqueue(Command.SpawnBuilding(p, plan.HallType[p], g.CellCenter(plan.HallAnchor[p] % g.Width, plan.HallAnchor[p] / g.Width)));
            foreach (Vector2 spot in plan.Workers[p]) sim.Enqueue(Command.SpawnUnit(p, plan.WorkerType[p], spot));
        }
        sim.Tick();
        sim.Tick();
        UnitStore u = w.Units;
        int live = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            live++;
            if (gather && ViewReads.NearestMine(w, u.Position[i], out Vector2 mine))
                sim.Enqueue(Command.Gather(u.Owner[i], new EntityHandle(i, u.Generation[i]), mine));
        }
        Assert.Equal(plan.Workers[0].Length + plan.Workers[1].Length, live);
        long gold0 = w.Ledger.Gold[0] + w.Ledger.Gold[1];
        for (int t = 0; t < 6000; t++)
        {
            sim.Tick();
            Assert.True(w.Deaths.Length == 0, $"seed {seed} tick {t}: a death");
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && (u.Target[i] != default || u.Mode[i] != CombatMode.None))
                    Assert.Fail($"seed {seed} tick {t}: worker {i} of player {u.Owner[i]} took {u.Target[i]} mode {u.Mode[i]}");
        }
        _out.WriteLine($"seed {seed}: {live} workers, hall gap {Math.Abs(plan.HallAnchor[0] % g.Width - plan.HallAnchor[1] % g.Width)} cells; gold {gold0} -> {w.Ledger.Gold[0] + w.Ledger.Gold[1]}");
        if (gather) Assert.True(w.Ledger.Gold[0] + w.Ledger.Gold[1] > gold0, "setup: nobody gathered");
    }

    /// <summary>
    /// Both owners' workers gather from one gold mine (side by side for 3,000 ticks), then a player-1 Heavy Infantry
    /// attack-moves through player 0's gatherers: no gatherer of either side ever takes a target, a combat mode or the
    /// Attacking state (they are hit and keep gathering), and the attack-mover does take them.
    /// </summary>
    [Fact]
    public void GatherersOfBothOwnersAtOneMine_NeverFight_EvenWhenAnAttackMoverHitsThem()
    {
        Simulation sim = GatherMaps.NewSim(ResourceMaps.Flat(48, 32), units: 32, players: 2);
        World w = sim.World;
        ResourceMaps.Spawn(w, ResourceMaps.Mine, 24, 16, 100_000);
        GatherMaps.Building(sim, 14, 15, player: 0, type: GatherMaps.Keep);
        GatherMaps.Building(sim, 32, 15, player: 1, type: TestSim.Data.FindBuilding("whirlwind_holy_camp"));
        var gatherers = new List<EntityHandle>();
        int p1Worker = TestSim.Data.FindUnit("whirlwind_camp_follower");
        for (int k = 0; k < 4; k++)
        {
            gatherers.Add(GatherMaps.Unit(sim, GatherMaps.At(sim, 20, 12 + 2 * k), player: 0, type: Laborer));
            gatherers.Add(GatherMaps.Unit(sim, GatherMaps.At(sim, 29, 12 + 2 * k), player: 1, type: p1Worker));
        }
        UnitStore u = w.Units;
        foreach (EntityHandle h in gatherers) sim.Enqueue(Command.Gather(u.Owner[h.Index], h, GatherMaps.At(sim, 24, 16)));
        bool hitSeen = false, moverTook = false;
        EntityHandle mover = default;
        for (int t = 0; t < 4500; t++)
        {
            if (t == 3000)
            {
                mover = GatherMaps.Unit(sim, GatherMaps.At(sim, 24, 28), player: 1, type: Raider);
                sim.Enqueue(Command.AttackMove(1, mover, GatherMaps.At(sim, 16, 8)));
            }
            int[] hpBefore = gatherers.Select(h => u.IsAlive(h) ? u.Hp[h.Index] : 0).ToArray();
            sim.Tick();
            if (mover.Generation != 0 && u.IsAlive(mover) && u.Target[mover.Index] != default) moverTook = true;
            for (int k = 0; k < gatherers.Count; k++)
            {
                EntityHandle h = gatherers[k];
                if (!u.IsAlive(h)) continue;
                if (u.Hp[h.Index] < hpBefore[k]) hitSeen = true;
                Assert.True(u.Target[h.Index] == default && u.Mode[h.Index] == CombatMode.None && u.State[h.Index] != UnitState.Attacking,
                    $"tick {t}: gatherer {h} of player {u.Owner[h.Index]}: target {u.Target[h.Index]} mode {u.Mode[h.Index]} {u.State[h.Index]}");
            }
            if (t < 3000) Assert.True(w.Deaths.Length == 0, $"tick {t}: a death before the attack-mover came");
        }
        _out.WriteLine($"attack-mover took a target {moverTook}, a gatherer was hit {hitSeen}; gatherers alive {gatherers.Count(u.IsAlive)} / {gatherers.Count}");
        Assert.True(moverTook && hitSeen, "setup: the attack-mover never fought");
    }


    /// <summary>
    /// The crowd sweep's 500 units to four close points (one player a point, plain Moves) on combat: the crowds fight at
    /// the points (so the movement rows run combat-off), and everything must still stop: no unit Moving for 200 ticks in a
    /// row by tick 8,000 (measured: seed 2 stops by tick 4,000; 2,500 units on seed 7 by 6,000 and stay still to 20,000).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(5UL)]
    public void Crowd500ToFourPoints_OnCombat_EveryoneStops(ulong seed)
    {
        Simulation sim = MoveScenario.Spawn(seed, 500, 40f, out int goalCell, players: 2);
        World w = sim.World;
        Vector2[] four = CrowdRows.FourPoints(MoveScenario.Center(w.NavGrid, goalCell));
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++) sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), four[CrowdRows.PointOf(u, i, true)]));
        int still = 0, t = 0;
        for (; t < 8000 && still < 200; t++)
        {
            sim.Tick();
            bool moving = false;
            for (int i = 0; i < u.Capacity && !moving; i++) moving = u.Alive[i] && u.State[i] == UnitState.Moving;
            still = moving ? 0 : still + 1;
        }
        _out.WriteLine($"seed {seed}: still for 200 ticks by tick {t}; deaths {w.Ledger.Kills[0] + w.Ledger.Kills[1]}");
        Assert.True(still >= 200, $"seed {seed}: units still Moving at tick 8000");
    }

    // ---------- brawls must be fought to a finish (BUG-0143) ----------

    /// <summary>
    /// <paramref name="perSide"/> Heavy Infantry v as many Raiders attack-moved into each other on open flat ground
    /// (<see cref="CombatScenes.FlatBrawl"/>), or the generated-map brawl when <paramref name="ranks"/> is 0: the fight
    /// ends (one side wiped) within <paramref name="limit"/> ticks, and no unit ever stands Idle with no target for
    /// 40 ticks in a row (2 s, ten scans) while an enemy is inside its sight. Before the fix round (f2879b9) these
    /// scenes end by ticks 980-1,935 with no such unit; with the MaxGiveUps cap, crowd-blocked chasers give up three
    /// times, take only targets in reach from then on, and both armies stand 2-9 m apart (500 v 500: 213 v 190 still
    /// standing at 5 min).
    /// </summary>
    [Theory]
    [InlineData(40, 10, 2500)]
    [InlineData(40, 5, 2500)]
    [InlineData(100, 10, 3000)]
    [InlineData(200, 0, 3500)]
    public void Brawl_IsFoughtToAFinish_NoUnitStandsIdleInSightOfAnEnemy(int perSide, int ranks, int limit)
    {
        Simulation sim;
        if (ranks > 0)
        {
            sim = Flat(size: 64, units: 2 * perSide);
            FlatBrawl(sim, perSide, new Vector2(64f, 64f), gap: 8f, ranks: ranks);
        }
        else sim = MapBrawl(1, perSide);
        UnitStore u = sim.World.Units;
        var standing = new int[u.Capacity];
        int worst = 0, worstUnit = -1, worstTick = -1, decided = -1;
        for (int t = 0; t < limit && decided < 0; t++)
        {
            sim.Tick();
            int alive0 = 0, alive1 = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) { standing[i] = 0; continue; }
                if (u.Owner[i] == 0) alive0++; else alive1++;
                bool idleInSight = false;
                if (u.State[i] == UnitState.Idle && u.Target[i] == default)
                {
                    float sight = TestSim.Data.Units[u.TypeId[i]].Sight;
                    for (int j = 0; j < u.Capacity && !idleInSight; j++)
                        idleInSight = u.Alive[j] && u.Owner[j] != u.Owner[i] && Vector2.Distance(u.Position[i], u.Position[j]) <= sight;
                }
                standing[i] = idleInSight ? standing[i] + 1 : 0;
                if (standing[i] > worst) (worst, worstUnit, worstTick) = (standing[i], i, t);
            }
            if (alive0 == 0 || alive1 == 0) decided = t;
        }
        _out.WriteLine($"{perSide} a side, ranks {ranks}: decided on tick {decided}; longest Idle in sight of an enemy {worst} ticks (unit {worstUnit}, tick {worstTick})");
        Assert.True(worst < 40, $"unit {worstUnit} stood Idle with an enemy in sight for {worst} ticks (to tick {worstTick})");
        Assert.True(decided >= 0, $"not decided in {limit} ticks");
    }

    /// <summary>
    /// The worker rule under a hostile command fuzz: 20 workers and 12 fighters a side round one gold mine between the two
    /// halls, random Gather / Move / AttackMove / Stop / Hold (queued or not) every 2 ticks for 1,500 ticks. After every
    /// tick, a unit of the worker slot with a target or swinging is on an attack-move leg, and it is never in
    /// <c>Retaliate</c> or <c>Returning</c> mode. Attack-moved workers must have fought (the rule is not vacuous).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void WorkerRule_HoldsEveryTick_UnderAHostileCommandFuzz(ulong seed)
    {
        Simulation sim = GatherMaps.NewSim(ResourceMaps.Flat(64, 48), units: 96, players: 2, seed: seed);
        World w = sim.World;
        ResourceMaps.Spawn(w, ResourceMaps.Mine, 31, 23, 1_000_000);
        GatherMaps.Building(sim, 14, 22, player: 0, type: GatherMaps.Keep);
        GatherMaps.Building(sim, 46, 22, player: 1, type: ProductionMaps.HolyCamp);
        int[] worker = { Laborer, ProductionMaps.CampFollower };
        int[] fighter = { HeavyInfantry, Raider };
        var rng = new Determinism.SimRng(seed, 909);
        for (int p = 0; p < 2; p++)
            for (int k = 0; k < 32; k++)
                GatherMaps.Unit(sim, GatherMaps.At(sim, (p == 0 ? 20 : 37) + k % 6, 16 + k / 6 * 2), player: p, type: k < 20 ? worker[p] : fighter[p]);
        UnitStore u = w.Units;
        int workerFightTicks = 0;
        for (int t = 0; t < 1500; t++)
        {
            if (t % 2 == 0)
            {
                for (int n = 0; n < 6; n++)
                {
                    int i = rng.NextInt(0, u.Capacity);
                    if (!u.Alive[i]) continue;
                    var h = new EntityHandle(i, u.Generation[i]);
                    int p = u.Owner[i];
                    Vector2 to = GatherMaps.At(sim, rng.NextInt(4, 60), rng.NextInt(4, 44));
                    bool q = rng.NextInt(0, 4) == 0;
                    int roll = rng.NextInt(0, 100);
                    sim.Enqueue(roll < 30 ? Command.Gather(p, h, GatherMaps.At(sim, 31, 23), q)
                        : roll < 50 ? Command.Move(p, h, to, q)
                        : roll < 75 ? Command.AttackMove(p, h, GatherMaps.At(sim, p == 0 ? 40 : 22, rng.NextInt(14, 32)), q)
                        : roll < 87 ? Command.Stop(p, h, q)
                        : Command.HoldPosition(p, h, q));
                }
            }
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || TestSim.Data.Units[u.TypeId[i]].Slot != Data.UnitSlot.Worker) continue;
                CombatMode m = u.Mode[i];
                bool fighting = u.Target[i] != default || u.State[i] == UnitState.Attacking;
                if (fighting) workerFightTicks++;
                Assert.True(m is CombatMode.None or CombatMode.AttackMove, $"seed {seed} tick {t}: worker {i} in mode {m}");
                Assert.True(!fighting || m == CombatMode.AttackMove,
                    $"seed {seed} tick {t}: worker {i} of player {u.Owner[i]} {u.State[i]} target {u.Target[i]} mode {m}, hold {u.Hold[i]}");
            }
        }
        _out.WriteLine($"seed {seed}: worker-ticks fighting on an attack-move {workerFightTicks}; deaths {w.Kills[0] + w.Kills[1]}");
        Assert.True(workerFightTicks > 0, "setup: no attack-moved worker ever fought");
    }
}
