using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-2a re-check (session 2026-10-08-0313, round 1): the BUG-0152 fix keeps a fight when an Attack or AttackMove is
/// re-issued. These rows attack what that keeping must not break: spam must not make a chase immortal (the give-up still
/// builds), a Move still pulls a unit out mid-swing, a re-issued Attack still replaces the queue, a reaffirmed hold
/// target is chased as ordered, and an attack-move elsewhere walks the new leg, not the old one. Every row states its bound.
/// </summary>
[Collection(SerialCollection.Name)]
public class FightReissueQaTests
{
    private readonly ITestOutputHelper _out;

    public FightReissueQaTests(ITestOutputHelper output) => _out = output;

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

    /// <summary>
    /// An attack-move along the foot of a cliff, with an enemy on top in sight and out of reach for good, re-issued to the
    /// same point every <paramref name="every"/> ticks (an AI refreshing its orders): the chase on the cliff target is
    /// still given up and the unit reaches the leg's end within 400 ticks of a single order's arrival. Before BUG-0152's
    /// fix every re-issue started a fresh chase, so the give-up never built.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void AttackMoveSpam_SameLeg_PastACliffTarget_StillGivesUp_AndReachesTheLegEnd(int every)
    {
        int single = LegArrival(0, out _);
        int spam = LegArrival(every, out int giveUps);
        _out.WriteLine($"leg end reached: one order {single} ticks; re-issued every {every} ticks {spam} ticks (give-ups {giveUps})");
        Assert.True(single < 1500, $"setup: one order never reached the leg end ({single})");
        Assert.True(spam <= single + 400, $"re-issued every {every}: leg end after {spam} ticks vs {single} for one order");
    }

    private static int LegArrival(int every, out int giveUps)
    {
        Simulation sim = OnRows(8, PlateauMap());
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 16, 2));
        EntityHandle t = Place(sim, 1, Laborer, At(sim, 21, 20));
        sim.Enqueue(Command.HoldPosition(1, t));
        sim.Tick();
        Vector2 end = At(sim, 16, 38);
        Command order = Command.AttackMove(0, a, end);
        sim.Enqueue(order);
        giveUps = 0;
        for (int k = 1; k <= 1500; k++)
        {
            if (every > 0 && k % every == 0) sim.Enqueue(order);
            sim.Tick();
            giveUps = Math.Max(giveUps, u.GiveUps[a.Index]);
            if (Vector2.Distance(u.Position[a.Index], end) < 1.5f) return k;
        }
        return 1500;
    }

    /// <summary>
    /// An explicit Attack on a cliff target re-issued every 3 ticks: under <see cref="CombatMode.Ordered"/> the chase
    /// memory stays, so the chase is still given up (the target cleared at least once) within 200 ticks.
    /// </summary>
    [Fact]
    public void AttackSpam_OnACliffTarget_ChaseIsStillGivenUp()
    {
        Simulation sim = OnRows(8, PlateauMap());
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 12, 20));
        EntityHandle t = Place(sim, 1, Laborer, At(sim, 23, 20));
        sim.Enqueue(Command.HoldPosition(1, t));
        sim.Tick();
        Command order = Command.Attack(0, a, t, false);
        sim.Enqueue(order);
        int gaveUpAt = -1;
        for (int k = 1; k <= 400 && gaveUpAt < 0; k++)
        {
            if (k % 3 == 0) sim.Enqueue(order);
            sim.Tick();
            if (k > 2 && u.Target[a.Index].Generation == 0) gaveUpAt = k;
        }
        _out.WriteLine($"spammed Attack on a cliff target: target dropped at tick {gaveUpAt}");
        Assert.True(gaveUpAt > 0 && gaveUpAt <= 200, $"spam kept an unreachable chase alive (dropped at {gaveUpAt})");
    }

    /// <summary>
    /// A Move given mid-swing still pulls the unit out: no hit lands after the Move applies, the target is dropped, and
    /// the unit walks away (more than 6 m within 60 ticks). Also after an attack-move elsewhere that kept the fight.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Move_MidSwing_PullsTheUnitOut_NoHitLandsAfter(bool attackMoveFirst)
    {
        Simulation sim = Flat(units: 8);
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 24));
        EntityHandle t = Place(sim, 1, Laborer, At(sim, 20, 24, dx: 1.2f));
        u.Hp[t.Index] = 100_000;
        sim.Enqueue(Command.HoldPosition(1, t));
        sim.Enqueue(Command.Attack(0, a, t, false));
        int swing = RunUntil(sim, () => u.WindupTicks[a.Index] > 0, 200);
        Assert.True(swing < 200, "setup: no wind-up");
        if (attackMoveFirst)
        {
            sim.Enqueue(Command.AttackMove(0, a, At(sim, 40, 24)));
            sim.Tick(); // commands apply on the tick after the one they were enqueued before
            sim.Tick();
            Assert.Equal(t, u.Target[a.Index]);
            Assert.Equal(CombatMode.AttackMove, u.Mode[a.Index]);
            RunUntil(sim, () => u.WindupTicks[a.Index] > 0, 200);
        }
        Vector2 from = u.Position[a.Index];
        int hp = u.Hp[t.Index];
        sim.Enqueue(Command.Move(0, a, At(sim, 4, 24)));
        for (int k = 0; k < 60; k++) sim.Tick();
        _out.WriteLine($"after the Move: hp lost {hp - u.Hp[t.Index]}, walked {Vector2.Distance(from, u.Position[a.Index]):F1} m, target {u.Target[a.Index]}");
        Assert.Equal(hp, u.Hp[t.Index]);
        Assert.Equal(0, u.Target[a.Index].Generation);
        Assert.True(Vector2.Distance(from, u.Position[a.Index]) > 6f, "the Move didn't pull the unit out");
    }

    /// <summary>
    /// Attack, then a queued Move, then the same Attack unqueued: the re-issue replaces the queue (as any unqueued order
    /// does), so after the kill the unit stands, Idle with no mode, and never walks to the dropped Move's point.
    /// </summary>
    [Fact]
    public void ReissuedAttack_ReplacesTheQueue_TheUnitStandsAfterTheKill()
    {
        Simulation sim = Flat(units: 8);
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 24));
        EntityHandle t = Place(sim, 1, Laborer, At(sim, 20, 24, dx: 1.2f));
        sim.Enqueue(Command.HoldPosition(1, t));
        sim.Enqueue(Command.Attack(0, a, t, false));
        sim.Enqueue(Command.Move(0, a, At(sim, 40, 24), queued: true));
        sim.Tick(); // commands apply on the tick after the one they were enqueued before
        sim.Tick();
        Assert.Equal(1, u.QueueCount[a.Index]);
        sim.Enqueue(Command.Attack(0, a, t, false));
        sim.Tick();
        sim.Tick();
        Assert.Equal(0, u.QueueCount[a.Index]);
        int kill = RunUntil(sim, () => !u.IsAlive(t), 600);
        Assert.True(kill < 600, "setup: no kill");
        Vector2 at = u.Position[a.Index];
        for (int k = 0; k < 100; k++) sim.Tick();
        Assert.True(Vector2.Distance(at, u.Position[a.Index]) < 1f, $"walked {Vector2.Distance(at, u.Position[a.Index]):F1} m after the kill");
        Assert.Equal(UnitState.Idle, u.State[a.Index]);
        Assert.Equal(CombatMode.None, u.Mode[a.Index]);
    }

    /// <summary>
    /// A holding unit fighting a target in reach, then an Attack on that same target: Hold is cleared and the target is
    /// held as ordered, so when the target steps off about 3 m the unit follows it (gap back under 2 m within 200 ticks).
    /// </summary>
    [Fact]
    public void ReissuedAttack_FromHold_ChasesTheTargetAsOrdered()
    {
        Simulation sim = Flat(units: 8);
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 24));
        EntityHandle t = Place(sim, 1, Laborer, At(sim, 20, 24, dx: 1.2f));
        u.Hp[t.Index] = 100_000;
        sim.Enqueue(Command.HoldPosition(0, a));
        int engaged = RunUntil(sim, () => u.Target[a.Index] == t, 40);
        Assert.True(engaged < 40, "setup: the holder never took the target in reach");
        sim.Enqueue(Command.Attack(0, a, t, false));
        sim.Tick(); // commands apply on the tick after the one they were enqueued before
        sim.Tick();
        Assert.False(u.Hold[a.Index]);
        Assert.Equal(CombatMode.Ordered, u.Mode[a.Index]);
        // A short step (about 3 m): a Laborer outruns a Heavy Infantry, and a longer flight is rightly given up.
        sim.Enqueue(Command.Move(1, t, At(sim, 22, 24)));
        for (int k = 0; k < 40; k++) sim.Tick();
        int caught = RunUntil(sim, () => Vector2.Distance(u.Position[a.Index], u.Position[t.Index]) < 2f, 200);
        _out.WriteLine($"caught up {caught} ticks after the target stopped; target {u.Target[a.Index]} mode {u.Mode[a.Index]}");
        Assert.True(caught < 200, "the reaffirmed target was not chased");
        Assert.Equal(t, u.Target[a.Index]);
    }

    /// <summary>
    /// A Heavy Infantry attack-moving into an enemy Tent hits it (1 damage a hit); an enemy Raider then attacks it. The
    /// player attack-moves the unit onto the Raider: it must take the Raider (tier 0 / tier 1 rank above buildings) within
    /// 20 ticks and live. Before BUG-0152's fix it took the Raider in 5 ticks and lived; since, an attack-move anywhere by
    /// a unit fighting in reach keeps the Tent and the unit dies hitting it.
    /// </summary>
    [Fact(Skip = "BUG-0154: a re-issued attack-move by a unit hitting a building in reach keeps the building; the player can't redirect it onto an attacker")]
    public void AttackMoveOntoAnAttacker_WhileHittingABuilding_TakesTheAttacker()
    {
        Simulation sim = Flat(units: 16);
        UnitStore u = sim.World.Units;
        int tentType = TestSim.Data.FindBuilding("whirlwind_tent");
        Assert.True(sim.World.Buildings.Spawn(1, tentType, 22 * sim.World.NavGrid.Width + 20, out EntityHandle tent));
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 17, 23));
        sim.Enqueue(Command.AttackMove(0, a, At(sim, 19, 23)));
        int hit = RunUntil(sim, () => sim.World.Buildings.Hp[tent.Index] < sim.World.Data.Buildings[tentType].Hp, 600);
        Assert.True(hit < 600, "setup: the Tent was never hit");
        EntityHandle e = Place(sim, 1, Raider, At(sim, 17, 28));
        sim.Enqueue(Command.Attack(1, e, a, false));
        int hurt = RunUntil(sim, () => u.Hp[a.Index] < sim.World.Data.Units[HeavyInfantry].Hp, 400);
        Assert.True(hurt < 400, "setup: the Raider never hit");
        sim.Enqueue(Command.AttackMove(0, a, u.Position[e.Index]));
        int took = RunUntil(sim, () => u.Target[a.Index] == e || !u.IsAlive(a), 3000);
        _out.WriteLine($"took the attacker {took} ticks after the attack-move; alive {u.IsAlive(a)}");
        Assert.True(u.IsAlive(a) && u.Target[a.Index] == e && took <= 20, $"after {took} ticks: alive {u.IsAlive(a)}, target {u.Target[a.Index]}");
    }

    /// <summary>
    /// A unit fighting in reach, attack-moved to point B and then (still in the same fight) to point C: after the kill it
    /// walks to C, the last order, within 300 ticks, and never ends at B.
    /// </summary>
    [Fact]
    public void AttackMoveElsewhere_Twice_WhileFighting_WalksTheLastLeg()
    {
        Simulation sim = Flat(units: 8);
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 24));
        EntityHandle t = Place(sim, 1, Laborer, At(sim, 20, 24, dx: 1.2f));
        sim.Enqueue(Command.HoldPosition(1, t));
        sim.Enqueue(Command.Attack(0, a, t, false));
        RunUntil(sim, () => u.WindupTicks[a.Index] > 0, 200);
        sim.Enqueue(Command.AttackMove(0, a, At(sim, 20, 4)));
        sim.Tick();
        sim.Tick();
        sim.Enqueue(Command.AttackMove(0, a, At(sim, 40, 24)));
        sim.Tick();
        sim.Tick();
        Assert.Equal(t, u.Target[a.Index]);
        Assert.Equal(CombatMode.AttackMove, u.Mode[a.Index]);
        int kill = RunUntil(sim, () => !u.IsAlive(t), 600);
        Assert.True(kill < 600, "setup: no kill");
        int arrive = RunUntil(sim, () => Vector2.Distance(u.Position[a.Index], At(sim, 40, 24)) < 1.5f, 300);
        _out.WriteLine($"kill after {kill} ticks, at C {arrive} ticks later");
        Assert.True(arrive < 300, $"not at the last leg's end: {u.Position[a.Index]} state {u.State[a.Index]} mode {u.Mode[a.Index]}");
    }
}
