using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-2a (session 2026-10-08-0313): adversarial rows for the explicit <see cref="CommandKind.Attack"/> order and
/// <c>attack.targets</c>: livelocks (mutual orders, a cliff target, a faster fleeing target, 500 on one target), the
/// order applied on its target's death tick, a queued Attack whose target dies behind a Move, order spam, and every
/// path that must respect <c>attack.targets</c> (scan, retaliation, holding, attack-move, the explicit order) for a
/// buildings-only and a units-only attacker. Every row states its bound.
/// </summary>
[Collection(SerialCollection.Name)]
public class AttackOrderQaTests
{
    private readonly ITestOutputHelper _out;

    public AttackOrderQaTests(ITestOutputHelper output) => _out = output;

    private static int HorseRaider => TestSim.Data.FindUnit("whirlwind_horse_raider");

    private static int Ram => TestSim.Data.FindUnit("whirlwind_battering_ram");

    private static int Tent => TestSim.Data.FindBuilding("whirlwind_tent");

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

    private static EntityHandle PlaceBuilding(Simulation sim, int owner, int type, int x, int y)
    {
        Assert.True(sim.World.Buildings.Spawn(owner, type, y * sim.World.NavGrid.Width + x, out EntityHandle h));
        return h;
    }

    private static bool Settled(UnitStore u, EntityHandle a) =>
        u.Target[a.Index].Generation == 0 && u.Mode[a.Index] == CombatMode.None && u.State[a.Index] == UnitState.Idle;

    // ---------- livelocks ----------

    /// <summary>Two units ordered on each other from 20 m: they meet and fight within 200 ticks, and one dies within 3,000.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoUnitsOrderedOnEachOther_MeetFightAndOneDies(bool sameType)
    {
        Simulation sim = Flat(units: 8);
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 10, 24));
        EntityHandle b = Place(sim, 1, sameType ? HeavyInfantry : Raider, At(sim, 20, 24));
        sim.Enqueue(Command.Attack(0, a, b, false));
        sim.Enqueue(Command.Attack(1, b, a, false));
        int met = RunUntil(sim, () => u.State[a.Index] == UnitState.Attacking || u.State[b.Index] == UnitState.Attacking, 200);
        Assert.True(met < 200, $"no swing in 200 ticks: a {u.State[a.Index]} b {u.State[b.Index]}");
        int end = RunUntil(sim, () => !u.IsAlive(a) || !u.IsAlive(b), 3000);
        _out.WriteLine($"met after {met} ticks, a death {end} ticks later; a alive {u.IsAlive(a)}, b alive {u.IsAlive(b)}");
        Assert.True(end < 3000, "nobody died in 3,000 ticks");
        EntityHandle survivor = u.IsAlive(a) ? a : b;
        if (u.IsAlive(survivor))
        {
            int s = RunUntil(sim, () => Settled(u, survivor), 20);
            Assert.True(Settled(u, survivor), $"survivor not Idle with no mode 20 ticks after the kill: {u.State[survivor.Index]} {u.Mode[survivor.Index]}");
        }
    }

    /// <summary>An Attack on a unit on a sealed plateau: the chase is given up within 200 ticks, then the unit stands (no Moving tick in 2,000).</summary>
    [Fact]
    public void AttackOnAUnitOnACliff_IsGivenUp_ThenTheUnitStandsStill()
    {
        Simulation sim = OnRows(8, PlateauMap());
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 12, 20));
        EntityHandle t = Place(sim, 1, Laborer, At(sim, 23, 20));
        sim.Enqueue(Command.HoldPosition(1, t));
        sim.Tick();
        sim.Enqueue(Command.Attack(0, a, t, false));
        sim.Tick();
        sim.Tick();
        Assert.Equal(CombatMode.Ordered, u.Mode[a.Index]);
        int gaveUp = RunUntil(sim, () => Settled(u, a), 400);
        _out.WriteLine($"gave up after {gaveUp} ticks at {u.Position[a.Index]}; ignored {u.Ignored[a.Index]}");
        Assert.True(gaveUp <= 200, $"still chasing after {gaveUp} ticks: {u.State[a.Index]} {u.Mode[a.Index]}");
        int moving = 0;
        for (int k = 0; k < 2000; k++)
        {
            sim.Tick();
            if (u.State[a.Index] == UnitState.Moving) moving++;
        }
        Assert.Equal(0, moving);
    }

    /// <summary>
    /// An Attack on a Horse Raider (6.6 m/s) running straight away from a Heavy Infantry (3 m/s): the chase is given up
    /// (the leash doesn't apply, the give-up memory does) within 200 ticks, and the unit then stands.
    /// </summary>
    [Fact]
    public void AttackOnAFasterFleeingTarget_IsGivenUp_WithinTheGiveUpBound()
    {
        Simulation sim = Flat(size: 96, units: 8);
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 10, 48));
        EntityHandle t = Place(sim, 1, HorseRaider, At(sim, 16, 48));
        sim.Enqueue(Command.Attack(0, a, t, false));
        sim.Enqueue(Command.Move(1, t, At(sim, 90, 48)));
        sim.Tick();
        sim.Tick();
        Assert.Equal(CombatMode.Ordered, u.Mode[a.Index]);
        int ticks = RunUntil(sim, () => Settled(u, a) && u.Mode[a.Index] == CombatMode.None, 600);
        _out.WriteLine($"gave up after {ticks} ticks, {Vector2.Distance(u.Position[a.Index], At(sim, 10, 48)):F1} m from the start; target {u.Position[t.Index]}");
        Assert.True(ticks <= 200, $"still chasing a faster target after {ticks} ticks");
        Assert.True(u.IsAlive(t));
        int moving = 0;
        for (int k = 0; k < 1000; k++)
        {
            sim.Tick();
            if (u.State[a.Index] == UnitState.Moving) moving++;
        }
        Assert.Equal(0, moving);
    }

    /// <summary>
    /// The order applied on (or around) the tick its target dies: a Laborer at 1 hp next to an enemy holder that kills it.
    /// The Attack is enqueued on every tick offset from 6 before to 6 after the death; the attacker is Idle with no target
    /// and no mode within 10 ticks of the death, and its queued Move behind the Attack still runs.
    /// </summary>
    [Fact]
    public void AttackAppliedAroundItsTargetsDeathTick_EndsCleanly_AndTheQueueGoesOn()
    {
        // The death tick, unordered.
        int deathTick = DeathScene(-1, out _, out _, out _);
        Assert.True(deathTick > 0, "setup: the target never died");
        for (int offset = -6; offset <= 6; offset++)
        {
            int died = DeathScene(deathTick + offset, out Simulation sim, out EntityHandle a, out Vector2 moveTo);
            UnitStore u = sim.World.Units;
            Assert.Equal(deathTick, died);
            // A command enqueued between ticks applies two ticks later: let the Attack and the queued Move land first.
            sim.Tick();
            sim.Tick();
            int settle = RunUntil(sim, () => Settled(u, a) && u.QueueCount[a.Index] == 0, 400);
            Assert.True(Settled(u, a) || u.State[a.Index] == UnitState.Moving, $"offset {offset}: {u.State[a.Index]} {u.Mode[a.Index]} target {u.Target[a.Index]}");
            RunUntil(sim, () => u.State[a.Index] == UnitState.Idle && u.QueueCount[a.Index] == 0, 600);
            Assert.True(Vector2.Distance(u.Position[a.Index], moveTo) < 2f, $"offset {offset}: the queued Move behind the Attack never ran (at {u.Position[a.Index]})");
            Assert.True(Settled(u, a), $"offset {offset}: not settled: {u.State[a.Index]} {u.Mode[a.Index]}");
        }
    }

    /// <summary>Runs the death scene; with <paramref name="orderTick"/> &gt;= 0 the Attack (then a queued Move) is enqueued before that tick. Returns the target's death tick.</summary>
    private static int DeathScene(int orderTick, out Simulation sim, out EntityHandle a, out Vector2 moveTo)
    {
        sim = Flat(units: 8);
        UnitStore u = sim.World.Units;
        a = Place(sim, 0, HeavyInfantry, At(sim, 18, 24));
        EntityHandle t = Place(sim, 1, Laborer, At(sim, 20, 24));
        EntityHandle killer = Place(sim, 0, HeavyInfantry, At(sim, 20, 24, dx: 1f));
        u.Hp[t.Index] = 1;
        sim.Enqueue(Command.HoldPosition(1, t));
        sim.Enqueue(Command.HoldPosition(0, a)); // the attacker waits on Hold until ordered: it must not kill the target itself
        moveTo = At(sim, 10, 10);
        int died = -1;
        for (int k = 0; k < 400 && (died < 0 || sim.TickNumber < died + 2); k++)
        {
            if (sim.TickNumber + 1 == orderTick)
            {
                sim.Enqueue(Command.Attack(0, a, t, false));
                sim.Enqueue(Command.Move(0, a, moveTo, queued: true));
            }
            sim.Tick();
            if (died < 0 && !u.IsAlive(t)) died = sim.TickNumber;
        }
        // Make sure the order was issued even if the death came first.
        if (orderTick > sim.TickNumber)
        {
            while (sim.TickNumber + 1 < orderTick) sim.Tick();
            sim.Enqueue(Command.Attack(0, a, t, false));
            sim.Enqueue(Command.Move(0, a, moveTo, queued: true));
            sim.Tick();
        }
        return died;
    }

    /// <summary>
    /// 500 Heavy Infantry ordered on one enemy unit with a huge hit pool: the order terminates (every attacker settles within
    /// 60 ticks of the kill), a tick under the order allocates nothing; the average tick is reported (no wall-clock assert outside Perf).
    /// </summary>
    [Fact]
    public void FiveHundredOrderedOnOneUnit_Terminates_AllocatesNothing_ReportsTickCost()
    {
        const int n = 500;
        Simulation sim = Flat(size: 96, units: n + 8);
        UnitStore u = sim.World.Units;
        EntityHandle t = Place(sim, 1, Raider, At(sim, 48, 48));
        u.Hp[t.Index] = 1_000_000;
        sim.Enqueue(Command.HoldPosition(1, t));
        var hs = new EntityHandle[n];
        for (int k = 0; k < n; k++) hs[k] = Place(sim, 0, HeavyInfantry, At(sim, 8 + k % 25 * 1, 20 + k / 25 * 1) + new Vector2(0.3f * (k % 2), 0f));
        foreach (EntityHandle h in hs) sim.Enqueue(Command.Attack(0, h, t, false));
        for (int k = 0; k < 100; k++) sim.Tick();
        Action tick = sim.Tick;
        AllocationProbe.AssertZero(tick, _out);
        long start = Stopwatch.GetTimestamp();
        for (int k = 0; k < 200; k++) sim.Tick();
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / 200;
        int ordered = 0, attacking = 0, ignoredIt = 0;
        foreach (EntityHandle h in hs)
        {
            if (!u.IsAlive(h)) continue;
            if (u.Mode[h.Index] == CombatMode.Ordered) ordered++;
            if (u.State[h.Index] == UnitState.Attacking) attacking++;
            if (u.Ignored[h.Index] == t) ignoredIt++;
        }
        _out.WriteLine($"500 on one unit, tick 300: avg {ms:F3} ms/tick; still ordered {ordered}, attacking {attacking}, gave it up {ignoredIt}; target hp {u.Hp[t.Index]}");
        u.Hp[t.Index] = 1;
        int died = RunUntil(sim, () => !u.IsAlive(t), 400);
        Assert.False(u.IsAlive(t), "the target never died");
        int settle = RunUntil(sim, () =>
        {
            foreach (EntityHandle h in hs)
                if (u.IsAlive(h) && (u.Mode[h.Index] != CombatMode.None || u.Target[h.Index].Generation != 0)) return false;
            return true;
        }, 400);
        _out.WriteLine($"target died {died} ticks after its hp was set to 1; every attacker settled {settle} ticks later");
        Assert.True(settle <= 60, $"attackers still engaged {settle} ticks after the kill");
    }

    /// <summary>500 Heavy Infantry ordered on one enemy Tent: the per-tick cost, 0 B/tick, and how many give it up (reported).</summary>
    [Fact]
    public void FiveHundredOrderedOnOneBuilding_AllocatesNothing_ReportsGiveUps()
    {
        const int n = 500;
        Simulation sim = Flat(size: 96, units: n + 8);
        UnitStore u = sim.World.Units;
        EntityHandle tent = PlaceBuilding(sim, 1, Tent, 46, 46);
        var hs = new EntityHandle[n];
        for (int k = 0; k < n; k++) hs[k] = Place(sim, 0, HeavyInfantry, At(sim, 8 + k % 25, 20 + k / 25));
        foreach (EntityHandle h in hs) sim.Enqueue(Command.Attack(0, h, tent, true));
        int maxAttacking = 0;
        for (int k = 0; k < 300 && sim.World.Buildings.IsAlive(tent); k++)
        {
            sim.Tick();
            int attacking = 0;
            foreach (EntityHandle h in hs) if (u.State[h.Index] == UnitState.Attacking) attacking++;
            if (attacking > maxAttacking) maxAttacking = attacking;
        }
        int ignored = 0;
        foreach (EntityHandle h in hs) if (u.IgnoredIsBuilding[h.Index] && u.Ignored[h.Index] == tent) ignored++;
        _out.WriteLine($"500 on one Tent: at most {maxAttacking} attacking at once; {ignored} gave it up; tent alive {sim.World.Buildings.IsAlive(tent)}");
        if (sim.World.Buildings.IsAlive(tent))
        {
            Action tick = sim.Tick;
            AllocationProbe.AssertZero(tick, _out);
        }
    }

    /// <summary>A queued Attack behind a Move whose target dies while the unit walks: dropped when popped, and the Move queued after it runs.</summary>
    [Fact]
    public void QueuedAttackBehindAMove_TargetDiesOnTheWay_IsDropped_AndTheNextLegRuns()
    {
        Simulation sim = Flat(units: 8);
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 5, 5));
        EntityHandle t = Place(sim, 1, Laborer, At(sim, 40, 40));
        sim.Enqueue(Command.HoldPosition(1, t));
        Vector2 leg1 = At(sim, 25, 5), leg2 = At(sim, 5, 30);
        sim.Enqueue(Command.Move(0, a, leg1));
        sim.Enqueue(Command.Attack(0, a, t, false, queued: true));
        sim.Enqueue(Command.Move(0, a, leg2, queued: true));
        sim.Tick();
        sim.Tick();
        Assert.Equal(2, u.QueueCount[a.Index]);
        Assert.Equal(CommandKind.Attack, u.QueueKind[a.Index * Orders.OrderConstants.QueueCapacity]);
        Assert.Equal(t, u.QueuedTarget(a.Index * Orders.OrderConstants.QueueCapacity));
        sim.Tick();
        u.Free(t); // dies while the unit walks its first leg
        // A new player-1 unit takes the freed slot (a recycled handle): the queued Attack must not take it.
        EntityHandle newcomer = Place(sim, 1, Laborer, At(sim, 30, 10));
        Assert.Equal(t.Index, newcomer.Index);
        sim.Enqueue(Command.HoldPosition(1, newcomer));
        int ticks = RunUntil(sim, () => u.QueueCount[a.Index] == 0 && u.State[a.Index] == UnitState.Idle, 1000);
        _out.WriteLine($"queue drained after {ticks} ticks at {u.Position[a.Index]}; target {u.Target[a.Index]}");
        Assert.True(Vector2.Distance(u.Position[a.Index], leg2) < 2f, $"never ran the second leg: at {u.Position[a.Index]}");
        Assert.True(u.Target[a.Index].Generation == 0 && u.Mode[a.Index] == CombatMode.None);
        Assert.True(u.Hp[newcomer.Index] == sim.World.Data.Units[Laborer].Hp, "the recycled slot's new unit was attacked");
    }

    /// <summary>
    /// Re-issuing the same Attack (a player spam-clicking the target, an AI refreshing its orders) must not stop the unit
    /// fighting: over 600 ticks in reach the target takes at least half the hits it takes with a single order.
    /// </summary>
    [Theory(Skip = "BUG-0152: re-issuing the same Attack / AttackMove cancels the wind-up every time; spam does no damage")]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(5, false)]
    [InlineData(10, false)]
    [InlineData(3, true)]
    public void ReissuingTheSameAttack_EveryNTicks_StillLandsHits(int every, bool attackMove)
    {
        int once = DamageOver600(0, attackMove);
        int spam = DamageOver600(every, attackMove);
        _out.WriteLine($"damage over 600 ticks ({(attackMove ? "AttackMove" : "Attack")}): one order {once}, re-ordered every {every} ticks {spam}");
        Assert.True(once > 0, "setup: no damage with one order");
        Assert.True(spam * 2 >= once, $"re-ordering every {every} ticks: {spam} damage vs {once} with one order");
    }

    private static int DamageOver600(int every, bool attackMove)
    {
        Simulation sim = Flat(units: 8);
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 24));
        EntityHandle t = Place(sim, 1, Laborer, At(sim, 20, 24, dx: 1.2f));
        u.Hp[t.Index] = 100_000;
        sim.Enqueue(Command.HoldPosition(1, t));
        Vector2 at = u.Position[a.Index];
        Command order = attackMove ? Command.AttackMove(0, a, at) : Command.Attack(0, a, t, false);
        sim.Enqueue(order);
        for (int k = 1; k <= 600; k++)
        {
            if (every > 0 && k % every == 0) sim.Enqueue(order);
            sim.Tick();
        }
        // A worker held in place can't retaliate (workers don't), so the only damage is a's.
        return 100_000 - u.Hp[t.Index];
    }

    /// <summary>A queued Attack on a building whose slot is freed and re-used by another building (a recycled building handle): dropped.</summary>
    [Fact]
    public void QueuedAttackOnABuilding_WhoseSlotIsReused_IsDropped()
    {
        Simulation sim = Flat(units: 8);
        UnitStore u = sim.World.Units;
        BuildingStore b = sim.World.Buildings;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 5, 5));
        EntityHandle tent = PlaceBuilding(sim, 1, Tent, 30, 30);
        sim.Enqueue(Command.Move(0, a, At(sim, 20, 5)));
        sim.Enqueue(Command.Attack(0, a, tent, true, queued: true));
        sim.Tick();
        sim.Tick();
        Assert.Equal(1, u.QueueCount[a.Index]);
        b.Damage(tent, 1_000_000);
        sim.Tick();
        Assert.False(b.IsAlive(tent));
        EntityHandle other = PlaceBuilding(sim, 1, Tent, 10, 30);
        Assert.Equal(tent.Index, other.Index);
        int ticks = RunUntil(sim, () => u.QueueCount[a.Index] == 0 && u.State[a.Index] == UnitState.Idle, 600);
        sim.Tick();
        Assert.True(u.Target[a.Index].Generation == 0, $"took {u.Target[a.Index]} (building {u.TargetIsBuilding[a.Index]})");
        Assert.True(u.Mode[a.Index] != CombatMode.Ordered);
    }

    // ---------- attack.targets through every path ----------

    /// <summary>A ram among enemy units only: never targets one by scan, Hold, attack-move or retaliation (hit by them for 400 ticks).</summary>
    [Fact]
    public void Ram_AmongEnemyUnits_NeverTargetsOne_ByScanHoldAttackMoveOrRetaliation()
    {
        Simulation sim = Flat(units: 16);
        UnitStore u = sim.World.Units;
        EntityHandle idle = Place(sim, 1, Ram, At(sim, 10, 10));
        EntityHandle holder = Place(sim, 1, Ram, At(sim, 30, 10));
        EntityHandle mover = Place(sim, 1, Ram, At(sim, 10, 30));
        u.Hp[idle.Index] = u.Hp[holder.Index] = u.Hp[mover.Index] = 100_000;
        sim.Enqueue(Command.HoldPosition(1, holder));
        sim.Enqueue(Command.AttackMove(1, mover, At(sim, 40, 30)));
        // Enemy units hitting the idle ram and the holder, and enemy laborers standing on the attack-mover's path.
        EntityHandle h1 = Place(sim, 0, HeavyInfantry, At(sim, 10, 10, dx: 1.6f));
        EntityHandle h2 = Place(sim, 0, HeavyInfantry, At(sim, 30, 10, dx: 1.6f));
        for (int x = 15; x <= 35; x += 5) sim.Enqueue(Command.HoldPosition(0, Place(sim, 0, Laborer, At(sim, x, 30, dy: 1.2f))));
        int laborerHp = sim.World.Data.Units[Laborer].Hp, ramTargets = 0;
        for (int k = 0; k < 400; k++)
        {
            sim.Tick();
            foreach (EntityHandle r in new[] { idle, holder, mover })
                if (u.Target[r.Index].Generation != 0 && !u.TargetIsBuilding[r.Index]) ramTargets++;
        }
        Assert.True(u.Hp[idle.Index] < 100_000 && u.Hp[holder.Index] < 100_000, "setup: the rams were not hit");
        Assert.Equal(0, ramTargets);
        Assert.True(u.Hp[h1.Index] == sim.World.Data.Units[HeavyInfantry].Hp && u.Hp[h2.Index] == sim.World.Data.Units[HeavyInfantry].Hp);
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.TypeId[i] == Laborer) Assert.Equal(laborerHp, u.Hp[i]);
    }

    /// <summary>A ram attack-moving past enemy units to an enemy Tent: ignores the units, hits the Tent.</summary>
    [Fact]
    public void Ram_AttackMovingPastUnitsToABuilding_HitsTheBuildingOnly()
    {
        Simulation sim = Flat(units: 16);
        UnitStore u = sim.World.Units;
        EntityHandle tent = PlaceBuilding(sim, 0, Tent, 30, 22);
        EntityHandle ram = Place(sim, 1, Ram, At(sim, 10, 23));
        for (int x = 14; x <= 26; x += 4) sim.Enqueue(Command.HoldPosition(0, Place(sim, 0, Laborer, At(sim, x, 26))));
        sim.Enqueue(Command.AttackMove(1, ram, At(sim, 40, 23)));
        int hp0 = sim.World.Buildings.Hp[tent.Index];
        RunUntil(sim, () => sim.World.Buildings.Hp[tent.Index] < hp0, 1500);
        Assert.True(sim.World.Buildings.Hp[tent.Index] < hp0, "the ram never hit the Tent");
        int laborerHp = sim.World.Data.Units[Laborer].Hp;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.TypeId[i] == Laborer) Assert.Equal(laborerHp, u.Hp[i]);
    }

    /// <summary>
    /// A units-only attacker (Heavy Infantry with <c>attack.targets: "units"</c>): an Attack on a building is dropped; idle,
    /// holding or attack-moving beside an enemy building with no enemy unit around it never takes the building; an enemy
    /// unit is still taken.
    /// </summary>
    [Fact]
    public void UnitsOnlyAttacker_NeverTakesABuilding_ByAnyPath_ButStillFightsUnits()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "attack.targets", "\"units\"");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        GameData data = r.Data!;
        int hi = data.FindUnit("malazan_heavy_infantry"), raider = data.FindUnit("whirlwind_raider"), tentType = data.FindBuilding("whirlwind_tent");
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 160) with { Data = data }, LocalMovementTests.Flat(48));
        UnitStore u = sim.World.Units;
        BuildingStore b = sim.World.Buildings;
        Assert.True(b.Spawn(1, tentType, 22 * 48 + 22, out EntityHandle tent));
        Assert.True(u.TrySpawn(0, hi, data.Units[hi], At(sim, 21, 23), out EntityHandle idle));
        Assert.True(u.TrySpawn(0, hi, data.Units[hi], At(sim, 25, 23), out EntityHandle holder));
        Assert.True(u.TrySpawn(0, hi, data.Units[hi], At(sim, 10, 23), out EntityHandle mover));
        Assert.True(u.TrySpawn(0, hi, data.Units[hi], At(sim, 23, 26), out EntityHandle ordered));
        sim.Enqueue(Command.HoldPosition(0, holder));
        sim.Enqueue(Command.AttackMove(0, mover, At(sim, 40, 23)));
        sim.Enqueue(Command.Attack(0, ordered, tent, true));
        int hp0 = b.Hp[tent.Index], buildingTargets = 0;
        for (int k = 0; k < 600; k++)
        {
            sim.Tick();
            foreach (EntityHandle h in new[] { idle, holder, mover, ordered })
                if (u.TargetIsBuilding[h.Index] && u.Target[h.Index].Generation != 0) buildingTargets++;
        }
        Assert.Equal(0, buildingTargets);
        Assert.Equal(hp0, b.Hp[tent.Index]);
        // Still fights units: an enemy Raider beside the idle one is taken.
        Assert.True(u.TrySpawn(1, raider, data.Units[raider], At(sim, 21, 24, dx: 0f, dy: 1.2f), out EntityHandle enemy));
        int took = RunUntil(sim, () => u.Target[idle.Index] == enemy || u.Target[holder.Index] == enemy, 40);
        Assert.True(took < 40, "a units-only attacker ignored an enemy unit beside it");
    }
}
