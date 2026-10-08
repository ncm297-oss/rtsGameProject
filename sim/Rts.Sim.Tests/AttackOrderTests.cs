using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Orders;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-2a: the explicit <see cref="CommandKind.Attack"/> order (docs/03 "Implementation (M4-2a)"): drop rules, Shift-queue,
/// the held target, no leash, the give-up memory, Idle after the kill, buildings, workers, and <c>attack.targets</c>.
/// </summary>
public class AttackOrderTests
{
    private static int Ram => TestSim.Data.FindUnit("whirlwind_battering_ram");

    private static int Tent => TestSim.Data.FindBuilding("whirlwind_tent");

    /// <summary>Runs the two ticks after which a command enqueued now has applied.</summary>
    private static void Apply(Simulation sim)
    {
        sim.Tick();
        sim.Tick();
    }

    /// <summary>Places a player-1 Tent with its anchor at cell (x, y) and returns its handle.</summary>
    private static EntityHandle PlaceTent(Simulation sim, int x, int y, int owner = 1)
    {
        BuildingStore b = sim.World.Buildings;
        Assert.True(b.Spawn(owner, Tent, y * sim.World.NavGrid.Width + x, out EntityHandle h));
        return h;
    }

    // ---------- the command ----------

    [Fact]
    public void Attack_IsKind16_AQueueableUnitOrder_AndATargetOnAnyOtherKindIsMalformed()
    {
        Assert.Equal(16, (int)CommandKind.Attack);
        var unit = new EntityHandle(3, 1);
        var target = new EntityHandle(7, 2);
        Command c = Command.Attack(0, unit, target, isBuilding: true, queued: true);
        Assert.Equal(CommandKind.Attack, c.Kind);
        Assert.Equal(unit, c.Unit);
        Assert.Equal(target, c.Target);
        Assert.True(c.TargetIsBuilding);
        Assert.True(c.IsQueued && c.IsUnitOrder && c.IsWellFormed());
        Command move = Command.Move(0, unit, Vector2.One);
        move.Target = target;
        Assert.False(move.IsWellFormed());
        move.Target = default;
        move.TargetIsBuilding = true;
        Assert.False(move.IsWellFormed());
    }

    // ---------- drop rules ----------

    public enum Drop { Dead, Recycled, Own, ForbiddenByTargets, CannotFight, CombatOff }

    [Theory]
    [InlineData(Drop.Dead)]
    [InlineData(Drop.Recycled)]
    [InlineData(Drop.Own)]
    [InlineData(Drop.ForbiddenByTargets)]
    [InlineData(Drop.CannotFight)]
    [InlineData(Drop.CombatOff)]
    public void AnAttackOnSomethingItMayNotFight_IsDropped_QueuedOrNot_AndTheOldOrderGoesOn(Drop why)
    {
        Simulation sim = why == Drop.CombatOff
            ? new Simulation(TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 64), LocalMovementTests.Flat(48))
            : Flat(units: 16);
        UnitStore u = sim.World.Units;
        int attackerType = why switch { Drop.ForbiddenByTargets => Ram, Drop.CannotFight => Crossbowman, _ => HeavyInfantry };
        EntityHandle a = Place(sim, 0, attackerType, At(sim, 10, 24));
        EntityHandle target = Place(sim, why == Drop.Own ? 0 : 1, Laborer, At(sim, 30, 24));
        sim.Enqueue(Command.HoldPosition(1, target));
        if (why is Drop.Dead or Drop.Recycled)
        {
            u.Free(target);
            if (why == Drop.Recycled)
            {
                EntityHandle again = Place(sim, 1, Laborer, At(sim, 30, 24));
                Assert.Equal(target.Index, again.Index);
                Assert.NotEqual(target.Generation, again.Generation);
            }
        }
        Vector2 goal = At(sim, 10, 40);
        sim.Enqueue(Command.Move(0, a, goal));
        Apply(sim);
        Assert.Equal(UnitState.Moving, u.State[a.Index]);
        sim.Enqueue(Command.Attack(0, a, target, isBuilding: false));
        sim.Enqueue(Command.Attack(0, a, target, isBuilding: false, queued: true));
        Apply(sim);
        Assert.Equal(default, u.Target[a.Index]);
        Assert.Equal(CombatMode.None, u.Mode[a.Index]);
        Assert.Equal(0, u.QueueCount[a.Index]);
        Assert.Equal(UnitState.Moving, u.State[a.Index]); // still walking its Move
        Assert.Equal(goal, u.Goal[a.Index]);
    }

    [Fact]
    public void AnAttackOnAnEnemyBuilding_ByAUnitsOnlyAttacker_WouldBeDropped_ButTheShippedDataHasNone()
    {
        // attack.targets "units" exists in the schema; no shipped unit uses it (the loader rows cover it).
        Assert.DoesNotContain(TestSim.Data.Units, d => d.Attack.Targets == AttackTargets.Units);
    }

    // ---------- the order ----------

    [Fact]
    public void Attack_ChasesAndKillsTheTarget_ThenStandsIdleWhereItIs()
    {
        Simulation sim = Flat();
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 10, 24));
        EntityHandle b = Place(sim, 1, Laborer, At(sim, 20, 24));
        sim.Enqueue(Command.HoldPosition(1, b)); // a worker holding never fights back
        sim.Enqueue(Command.Attack(0, a, b, isBuilding: false));
        Apply(sim);
        Assert.Equal(b, u.Target[a.Index]);
        Assert.Equal(CombatMode.Ordered, u.Mode[a.Index]);
        RunUntil(sim, () => !u.IsAlive(b), 2000);
        Assert.False(u.IsAlive(b), "never killed");
        Vector2 at = u.Position[a.Index];
        RunUntil(sim, () => false, 60);
        Assert.Equal(UnitState.Idle, u.State[a.Index]);
        Assert.Equal(CombatMode.None, u.Mode[a.Index]);
        Assert.Equal(default, u.Target[a.Index]);
        Assert.True(Vector2.Distance(at, u.Position[a.Index]) < 0.5f, "walked off after the kill");
    }

    [Fact]
    public void Attack_HoldsItsTarget_WhileANearerEnemyStandsBy()
    {
        Simulation sim = Flat();
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 10, 24));
        EntityHandle near = Place(sim, 1, Raider, At(sim, 10, 24, dx: 3f));
        EntityHandle far = Place(sim, 1, Laborer, At(sim, 10, 24, dy: -12f));
        sim.Enqueue(Command.HoldPosition(1, near)); // in sight, never in reach of a's path: it stays a "nearer enemy"
        sim.Enqueue(Command.HoldPosition(1, far));
        sim.Enqueue(Command.Attack(0, a, far, isBuilding: false));
        Apply(sim);
        int ticks = 0;
        while (u.IsAlive(far) && ticks++ < 2000)
        {
            Assert.Equal(far, u.Target[a.Index]);
            sim.Tick();
        }
        Assert.False(u.IsAlive(far), "never killed its target");
        Assert.True(u.IsAlive(near));
    }

    [Fact]
    public void Attack_TheRetaliationLeashDoesNotApply_ItChasesFortyMetresFromWhereItStood()
    {
        Simulation sim = Flat(size: 64);
        UnitStore u = sim.World.Units;
        Vector2 start = At(sim, 6, 32);
        EntityHandle a = Place(sim, 0, HeavyInfantry, start);
        EntityHandle b = Place(sim, 1, Laborer, At(sim, 29, 32)); // 46 m off, far past a's sight (14 m)
        sim.Enqueue(Command.HoldPosition(1, b));
        sim.Enqueue(Command.Attack(0, a, b, isBuilding: false));
        Apply(sim);
        float farthest = 0f;
        int ticks = 0;
        while (u.IsAlive(b) && ticks++ < 3000)
        {
            Assert.Equal(b, u.Target[a.Index]);
            Assert.NotEqual(CombatMode.Returning, u.Mode[a.Index]);
            farthest = MathF.Max(farthest, Vector2.Distance(start, u.Position[a.Index]));
            sim.Tick();
        }
        Assert.False(u.IsAlive(b), "never killed its target");
        Assert.True(farthest >= 40f, $"chased only {farthest} m from where it stood");
        Assert.True(farthest > TestSim.Data.Units[HeavyInfantry].Sight);
    }

    [Fact]
    public void Attack_OnAnUnreachableTarget_IsGivenUp_AndTheUnitStandsIdleWhereItGaveUp()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        // b stands in a one-cell pocket walled in by trees: in sight, never in reach.
        for (int y = 22; y <= 26; y++)
            for (int x = 22; x <= 26; x++)
                if (x != 24 || y != 24) ResourceMaps.Spawn(w, ResourceMaps.Tree, x, y, ResourceMaps.TreeWood);
        EntityHandle b = Place(sim, 1, Laborer, At(sim, 24, 24));
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 14, 24));
        sim.Enqueue(Command.HoldPosition(1, b));
        sim.Enqueue(Command.Attack(0, a, b, isBuilding: false));
        Apply(sim);
        Assert.Equal(b, u.Target[a.Index]);
        RunUntil(sim, () => u.Target[a.Index] == default, 2000);
        Assert.Equal(default, u.Target[a.Index]);
        Assert.Equal(b, u.Ignored[a.Index]);
        Assert.Equal(CombatMode.None, u.Mode[a.Index]);
        RunUntil(sim, () => u.State[a.Index] == UnitState.Idle, 40);
        Vector2 at = u.Position[a.Index];
        for (int k = 0; k < 200; k++)
        {
            sim.Tick();
            Assert.Equal(default, u.Target[a.Index]); // the given-up target is taken again only in reach
            Assert.NotEqual(CombatMode.Returning, u.Mode[a.Index]);
        }
        Assert.True(Vector2.Distance(at, u.Position[a.Index]) < 0.5f, "walked off after giving up");
        Assert.True(u.IsAlive(b));
    }

    [Fact]
    public void Attack_OnABuilding_HitsItAsAStructure_HoldingItOverANearerUnit()
    {
        Simulation sim = Flat();
        UnitStore u = sim.World.Units;
        BuildingStore bs = sim.World.Buildings;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 20));
        EntityHandle tent = PlaceTent(sim, 20, 27);
        EntityHandle near = Place(sim, 1, Laborer, At(sim, 20, 20, dx: 3f));
        sim.Enqueue(Command.HoldPosition(1, near));
        sim.Enqueue(Command.Attack(0, a, tent, isBuilding: true));
        Apply(sim);
        Assert.Equal(tent, u.Target[a.Index]);
        Assert.True(u.TargetIsBuilding[a.Index]);
        int full = bs.Hp[tent.Index];
        RunUntil(sim, () => bs.Hp[tent.Index] < full, 600);
        Assert.True(bs.Hp[tent.Index] < full, "never hit the building");
        Assert.Equal(tent, u.Target[a.Index]);
        Assert.Equal(UnitState.Attacking, u.State[a.Index]);
        Assert.True(u.IsAlive(near));
    }

    [Fact]
    public void Attack_ByAWorker_IsObeyed_EndingItsGatherLoop()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle tree = ResourceMaps.Spawn(w, ResourceMaps.Tree, 10, 30, ResourceMaps.TreeWood);
        EntityHandle worker = Place(sim, 0, Laborer, At(sim, 10, 28));
        EntityHandle b = Place(sim, 1, Laborer, At(sim, 16, 28));
        sim.Enqueue(Command.HoldPosition(1, b));
        sim.Enqueue(Command.Gather(0, worker, At(sim, 10, 30)));
        Apply(sim);
        Assert.True(Economy.EconomySystem.OnLoop(u, worker.Index));
        sim.Enqueue(Command.Attack(0, worker, b, isBuilding: false));
        Apply(sim);
        Assert.False(Economy.EconomySystem.OnLoop(u, worker.Index));
        Assert.Equal(b, u.Target[worker.Index]);
        int full = u.Hp[b.Index];
        RunUntil(sim, () => u.Hp[b.Index] < full, 400);
        Assert.True(u.Hp[b.Index] < full, "the worker never hit its target");
        Assert.True(w.Resources.IsAlive(tree));
    }

    [Fact]
    public void Attack_HitByAnotherEnemy_KeepsItsTarget()
    {
        Simulation sim = Flat();
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 10, 24));
        EntityHandle b = Place(sim, 1, HeavyInfantry, At(sim, 10, 24, dx: 3f)); // holds and fights back once a arrives
        EntityHandle c = Place(sim, 1, Raider, At(sim, 10, 24, dy: 3f));
        sim.Enqueue(Command.HoldPosition(1, b));
        sim.Enqueue(Command.Attack(0, a, b, isBuilding: false));
        sim.Enqueue(Command.Attack(1, c, a, isBuilding: false));
        Apply(sim);
        int full = u.Hp[a.Index];
        bool hit = false;
        for (int t = 0; t < 600 && u.IsAlive(a) && u.IsAlive(b); t++)
        {
            sim.Tick();
            hit |= u.IsAlive(a) && u.Hp[a.Index] < full;
            if (u.IsAlive(a) && u.IsAlive(b)) Assert.Equal(b, u.Target[a.Index]);
        }
        Assert.True(hit, "setup: the raider never hit a");
    }

    // ---------- Shift-queue ----------

    [Fact]
    public void QueuedAttack_KeepsItsHandleInTheQueue_AndStartsWhenTheUnitIsIdle()
    {
        Simulation sim = Flat();
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 10, 24));
        EntityHandle b = Place(sim, 1, Laborer, At(sim, 24, 30));
        EntityHandle tent = PlaceTent(sim, 30, 10);
        sim.Enqueue(Command.HoldPosition(1, b));
        sim.Enqueue(Command.Move(0, a, At(sim, 16, 24)));
        sim.Enqueue(Command.Attack(0, a, b, isBuilding: false, queued: true));
        sim.Enqueue(Command.Attack(0, a, tent, isBuilding: true, queued: true));
        Apply(sim);
        int head = a.Index * OrderConstants.QueueCapacity;
        Assert.Equal(2, u.QueueCount[a.Index]);
        Assert.Equal(CommandKind.Attack, u.QueueKind[head]);
        Assert.Equal(b, u.QueuedTarget(head));
        Assert.Equal(0, u.QueueTypeId[head]);
        Assert.Equal(tent, u.QueuedTarget(head + 1));
        Assert.Equal(1, u.QueueTypeId[head + 1]);
        Assert.Equal(default, u.Target[a.Index]); // walking its Move first
        RunUntil(sim, () => u.Target[a.Index] != default, 400);
        Assert.Equal(b, u.Target[a.Index]);
        Assert.Equal(CombatMode.Ordered, u.Mode[a.Index]);
        Assert.Equal(1, u.QueueCount[a.Index]);
        Assert.Equal(tent, u.QueuedTarget(head)); // shifted forward
        Assert.Equal(1, u.QueueTypeId[head]);
        Assert.Equal(Vector2.Zero, u.QueuePosition[head + 1]); // the freed entry is default again
        Assert.Equal(0, u.QueueTypeId[head + 1]);
        RunUntil(sim, () => !u.IsAlive(b), 2000);
        Assert.False(u.IsAlive(b));
        RunUntil(sim, () => u.Target[a.Index] == tent, 20);
        Assert.Equal(tent, u.Target[a.Index]);
        Assert.True(u.TargetIsBuilding[a.Index]);
        Assert.Equal(0, u.QueueCount[a.Index]);
    }

    [Fact]
    public void QueuedAttack_WhoseTargetDiedWhileItWaited_IsDropped_AndTheNextOrderStarts()
    {
        Simulation sim = Flat();
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 10, 24));
        EntityHandle b = Place(sim, 1, Laborer, At(sim, 24, 30));
        Vector2 last = At(sim, 10, 34);
        sim.Enqueue(Command.Move(0, a, At(sim, 16, 24)));
        sim.Enqueue(Command.Attack(0, a, b, isBuilding: false, queued: true));
        sim.Enqueue(Command.Move(0, a, last, queued: true));
        Apply(sim);
        u.Free(b);
        RunUntil(sim, () => u.QueueCount[a.Index] == 0, 400);
        Assert.Equal(default, u.Target[a.Index]);
        RunUntil(sim, () => u.State[a.Index] == UnitState.Idle, 600);
        Assert.True(Vector2.Distance(last, u.Position[a.Index]) < 1.5f);
    }

    [Fact]
    public void Attack_ThenAQueuedMove_TheMoveStartsAfterTheKill()
    {
        Simulation sim = Flat();
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 10, 24));
        EntityHandle b = Place(sim, 1, Laborer, At(sim, 14, 24));
        Vector2 next = At(sim, 10, 10);
        sim.Enqueue(Command.HoldPosition(1, b));
        sim.Enqueue(Command.Attack(0, a, b, isBuilding: false));
        sim.Enqueue(Command.Move(0, a, next, queued: true));
        Apply(sim);
        Assert.Equal(1, u.QueueCount[a.Index]);
        RunUntil(sim, () => !u.IsAlive(b), 2000);
        Assert.False(u.IsAlive(b));
        RunUntil(sim, () => u.QueueCount[a.Index] == 0, 10);
        Assert.Equal(0, u.QueueCount[a.Index]);
        RunUntil(sim, () => u.State[a.Index] == UnitState.Idle, 600);
        Assert.True(Vector2.Distance(next, u.Position[a.Index]) < 1.5f);
    }

    // ---------- attack.targets: the Battering Ram ----------

    [Fact]
    public void Ram_NeverTakesOrRetaliatesAgainstAUnit_DropsAnAttackOnOne_AndHitsBuildings()
    {
        Assert.Equal(AttackTargets.Buildings, TestSim.Data.Units[Ram].Attack.Targets);
        Simulation sim = Flat();
        UnitStore u = sim.World.Units;
        EntityHandle ram = Place(sim, 1, Ram, At(sim, 20, 20));
        EntityHandle hi = Place(sim, 0, HeavyInfantry, At(sim, 20, 20, dx: 1.6f));
        sim.Enqueue(Command.Attack(0, hi, ram, isBuilding: false)); // hits the ram: no retaliation
        sim.Enqueue(Command.Attack(1, ram, hi, isBuilding: false)); // dropped
        Apply(sim);
        int full = u.Hp[ram.Index];
        for (int t = 0; t < 300 && u.IsAlive(ram); t++)
        {
            sim.Tick();
            if (!u.IsAlive(ram)) break;
            Assert.Equal(default, u.Target[ram.Index]);
            Assert.Equal(CombatMode.None, u.Mode[ram.Index]);
        }
        Assert.True(!u.IsAlive(ram) || u.Hp[ram.Index] < full, "setup: the ram was never hit");

        // A fresh ram with only a building in sight takes it, and an Attack on a building is obeyed.
        Simulation s2 = Flat();
        EntityHandle r2 = Place(s2, 1, Ram, At(s2, 20, 20));
        Assert.True(s2.World.Buildings.Spawn(0, GatherMaps.Keep, 24 * s2.World.NavGrid.Width + 20, out EntityHandle keep));
        RunUntil(s2, () => s2.World.Units.Target[r2.Index] != default, 12);
        Assert.Equal(keep, s2.World.Units.Target[r2.Index]);
        Assert.True(s2.World.Units.TargetIsBuilding[r2.Index]);
        int keepHp = s2.World.Buildings.Hp[keep.Index];
        RunUntil(s2, () => s2.World.Buildings.Hp[keep.Index] < keepHp, 600);
        Assert.True(s2.World.Buildings.Hp[keep.Index] < keepHp, "the ram never hit the building");
    }

    // ---------- re-issuing an order (BUG-0152) ----------

    /// <summary>
    /// The damage a Heavy Infantry deals in 600 ticks to a held enemy Laborer in reach (100,000 hp), given
    /// <paramref name="order"/> once (null: none, it fights by its own scan) and again every <paramref name="every"/> ticks.
    /// </summary>
    private static int DamageOver600(Func<EntityHandle, EntityHandle, Command>? order, int every, out EntityHandle a, out Simulation sim)
    {
        sim = Flat(units: 8);
        UnitStore u = sim.World.Units;
        a = Place(sim, 0, HeavyInfantry, At(sim, 20, 24));
        EntityHandle t = Place(sim, 1, Laborer, At(sim, 20, 24, dx: 1.2f));
        u.Hp[t.Index] = 100_000;
        sim.Enqueue(Command.HoldPosition(1, t)); // a worker never fights back
        if (order != null) sim.Enqueue(order(a, t));
        for (int k = 1; k <= 600; k++)
        {
            if (order != null && every > 0 && k % every == 0) sim.Enqueue(order(a, t));
            sim.Tick();
        }
        return 100_000 - u.Hp[t.Index];
    }

    [Fact]
    public void ReissuedAttack_OnTheTargetItHolds_EveryTick_DealsWhatOneOrderDeals()
    {
        Func<EntityHandle, EntityHandle, Command> attack = (a, t) => Command.Attack(0, a, t, false);
        int once = DamageOver600(attack, 0, out _, out _);
        int spam = DamageOver600(attack, 1, out EntityHandle a, out Simulation sim);
        Assert.True(once > 0, "setup: no damage");
        Assert.Equal(once, spam);
        Assert.Equal(CombatMode.Ordered, sim.World.Units.Mode[a.Index]);
    }

    [Fact]
    public void Attack_OnTheTargetItAlreadyFightsByItself_KeepsTheSwing_AndHoldsItAsOrdered()
    {
        int byScan = DamageOver600(null, 0, out _, out _);
        int spam = DamageOver600((a, t) => Command.Attack(0, a, t, false), 1, out EntityHandle a, out Simulation sim);
        Assert.True(byScan > 0, "setup: no damage by scan");
        // The first order lands after the scan took the target: every swing it starts by itself is kept.
        Assert.True(spam >= byScan - 20, $"{spam} damage spammed vs {byScan} by scan");
        Assert.Equal(CombatMode.Ordered, sim.World.Units.Mode[a.Index]);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    public void ReissuedAttackMove_WhileFightingInReach_DealsWhatOneOrderDeals(int every, bool farPoint)
    {
        // Cells are 2 m: (41, 49) is the attacker's own cell center (20, 24), (80, 80) is 50 m off.
        Func<EntityHandle, EntityHandle, Command> order = (a, t) => Command.AttackMove(0, a, farPoint ? new Vector2(80f, 80f) : new Vector2(41f, 49f));
        int once = DamageOver600(order, 0, out _, out _);
        int spam = DamageOver600(order, every, out _, out _);
        Assert.True(once > 0, "setup: no damage");
        Assert.True(spam * 10 >= once * 9, $"re-ordered every {every} ticks: {spam} damage vs {once} with one order");
    }

    [Fact]
    public void AttackMove_ElsewhereWhileFightingInReach_KeepsTheTarget_ThenWalksTheLeg()
    {
        Simulation sim = Flat(units: 8);
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 24));
        EntityHandle t = Place(sim, 1, Laborer, At(sim, 20, 24, dx: 1.2f));
        sim.Enqueue(Command.HoldPosition(1, t));
        RunUntil(sim, () => u.WindupTicks[a.Index] > 0, 200); // its own scan took the laborer: mid-swing
        Assert.Equal(t, u.Target[a.Index]);
        Vector2 leg = At(sim, 30, 30);
        sim.Enqueue(Command.AttackMove(0, a, leg));
        Apply(sim);
        Assert.Equal(t, u.Target[a.Index]);
        Assert.Equal(CombatMode.AttackMove, u.Mode[a.Index]);
        RunUntil(sim, () => !u.IsAlive(t), 2000);
        Assert.False(u.IsAlive(t), "never killed");
        RunUntil(sim, () => u.Mode[a.Index] == CombatMode.None && u.State[a.Index] == UnitState.Idle, 600);
        Assert.True(Vector2.Distance(u.Position[a.Index], leg) < 1.5f, $"never walked the leg: at {u.Position[a.Index]}");
    }
}
