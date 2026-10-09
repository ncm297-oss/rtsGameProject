using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M4-1 criterion 7: two players on a flat 64 x 48 map with a base each, a mine and a grove, 3,000 ticks of random
/// AttackMove / Move / Stop / Hold / Gather / Attack (M4-2a: enemy and own units and buildings, live or stale; queued and
/// not) / Train, dev spawns, units and buildings freed between ticks,
/// buildings dropped, 6 seeds. After every tick: hit points in (0, max]; no live unit targets a dead unit or building; a
/// unit Attacking on two ticks running, not ordered in between and whose target the test didn't free, has not moved, nor has a
/// holder; population equals its
/// recount; kills and losses each sum to the deaths seen, per player too; every position finite; twins hash-identical.
/// </summary>
public class CombatFuzzTests
{
    private const int Ticks = 3000;
    private const int W = 64, H = 48;
    private readonly ITestOutputHelper _out;

    public CombatFuzzTests(ITestOutputHelper output) => _out = output;

    private static readonly int[] Fighters = TestSim.Data.Units.Where(d => CombatSystem.CanFight(d)).Select(d => d.Id).ToArray();
    private static readonly int[] Everyone = TestSim.Data.Units.Select(d => d.Id).ToArray();
    private static int Barracks => ProductionMaps.Barracks;
    private static int RaiderCamp => ProductionMaps.RaiderCamp;

    private static Simulation Setup()
    {
        var sim = new Simulation(TestSim.Config(Seed: 9, PlayerCount: 2, UnitCapacity: 160, CommandCapacity: 2048), Flat(W, H));
        World w = sim.World;
        Spawn(w, Mine, 30, 22, 5000);
        for (int x = 26; x < 38; x += 2) Spawn(w, Tree, x, 42, TreeWood);
        Building(sim, 4, 18);
        Building(sim, 4, 26, type: Barracks);
        Building(sim, 56, 18, player: 1, type: ProductionMaps.HolyCamp);
        Building(sim, 56, 26, player: 1, type: RaiderCamp);
        for (int n = 0; n < 24; n++)
        {
            sim.Enqueue(Command.SpawnUnit(0, n % 4 == 0 ? Laborer : Fighters[n % Fighters.Length], At(sim, 10 + n % 6, 14 + n / 6 * 2)));
            sim.Enqueue(Command.SpawnUnit(1, n % 4 == 0 ? ProductionMaps.CampFollower : Fighters[(n + 3) % Fighters.Length], At(sim, 53 - n % 6, 14 + n / 6 * 2)));
        }
        Run(sim, 2);
        BuildMaps.SetTotals(sim, 0, 50_000, 50_000);
        BuildMaps.SetTotals(sim, 1, 50_000, 50_000);
        return sim;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    public void RandomCombatMix_InvariantsHoldEveryTick_AndTwinsStayEqual(ulong seed)
    {
        Simulation a = Setup(), b = Setup();
        World w = a.World;
        UnitStore u = w.Units;
        var rng = new SimRng(seed, 404);
        int cap = u.Capacity;
        // Orders queued before a tick apply in the next one: ordered[i] marks the tick about to run, orderedNext[i] the one after.
        var ordered = new bool[cap];
        var orderedNext = new bool[cap];
        var wasAttacking = new bool[cap];
        var wasHolding = new bool[cap];
        var lastPos = new Vector2[cap];
        var lastGen = new int[cap];
        long deaths = 0;
        var lossesSeen = new long[2];
        int attackingTicks = 0, holdingChecks = 0, buildingDeaths = 0, attackOrders = 0, orderedTicks = 0;

        for (int t = 0; t < Ticks; t++)
        {
            (ordered, orderedNext) = (orderedNext, ordered);
            Array.Clear(orderedNext);
            int orders = rng.NextInt(0, 6);
            for (int k = 0; k < orders; k++)
            {
                int p = rng.NextInt(0, 2);
                int i = RandomUnit(u, p, rng.NextInt(0, cap));
                if (i < 0) continue;
                var h = new EntityHandle(i, u.Generation[i]);
                Vector2 target = new(2.5f + rng.NextFloat() * (W * 2 - 5), 2.5f + rng.NextFloat() * (H * 2 - 5));
                bool queued = rng.NextInt(0, 4) == 0;
                int roll = rng.NextInt(0, 100);
                Command c = roll < 32 ? Command.AttackMove(p, h, target, queued)
                    : roll < 48 ? Command.Move(p, h, target, queued)
                    : roll < 56 ? Command.Stop(p, h, queued)
                    : roll < 66 ? Command.HoldPosition(p, h, queued)
                    : roll < 76 ? Command.Gather(p, h, rng.NextInt(0, 2) == 0 ? At(a, 30, 22) : At(a, 26 + 2 * rng.NextInt(0, 6), 42), queued)
                    : roll < 92 ? AttackOrder(ref rng, w, p, h, queued)
                    : Command.Train(p, p == 0 ? At(a, 5, 27) : At(a, 57, 27), p == 0 ? GatherMaps.Infantry : ProductionMaps.Raider);
                if (c.Kind == CommandKind.Attack) attackOrders++;
                Both(a, b, c);
                if (c.Kind != CommandKind.Train) orderedNext[i] = true;
            }
            if (t % 40 == 7)
            {
                int p = rng.NextInt(0, 2);
                int type = rng.NextInt(0, 3) == 0 ? Everyone[rng.NextInt(0, Everyone.Length)] : Fighters[rng.NextInt(0, Fighters.Length)];
                Both(a, b, Command.SpawnUnit(p, type, new Vector2(20f + rng.NextFloat() * 88f, 10f + rng.NextFloat() * 76f)));
            }
            if (t % 97 == 50)
            {
                int i = RandomUnit(u, rng.NextInt(0, 2), rng.NextInt(0, cap));
                if (i >= 0)
                {
                    var h = new EntityHandle(i, u.Generation[i]);
                    MarkAttackersOf(u, h, false, ordered);
                    a.World.Units.Free(h);
                    b.World.Units.Free(h);
                }
            }
            if (t % 300 == 150)
            {
                int p = rng.NextInt(0, 2);
                int type = p == 0 ? BuildMaps.House : BuildMaps.WhirlwindHouse;
                Both(a, b, Command.SpawnBuilding(p, type, At(a, rng.NextInt(12, W - 12), rng.NextInt(4, H - 4))));
            }
            if (t % 450 == 400)
            {
                BuildingStore bs = w.Buildings;
                int start = rng.NextInt(0, bs.Capacity);
                for (int n = 0; n < bs.Capacity; n++)
                {
                    int k = (start + n) % bs.Capacity;
                    if (!bs.Alive[k]) continue;
                    MarkAttackersOf(u, bs.HandleOf(k), true, ordered);
                    a.World.Buildings.Free(bs.HandleOf(k));
                    b.World.Buildings.Free(b.World.Buildings.HandleOf(k));
                    break;
                }
            }

            int ran = a.TickNumber;
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {ran}");

            // Deaths of this tick, then the books.
            foreach (DeathEvent e in w.Deaths)
            {
                deaths++;
                lossesSeen[e.VictimOwner]++;
                if (e.IsBuilding) buildingDeaths++;
                Assert.False(e.IsBuilding ? w.Buildings.IsAlive(e.Victim) : u.IsAlive(e.Victim));
            }
            Assert.Equal(deaths, (long)w.Kills[0] + w.Kills[1]);
            Assert.Equal(deaths, (long)w.Losses[0] + w.Losses[1]);
            Assert.Equal(lossesSeen[0], w.Losses[0]);
            Assert.Equal(lossesSeen[1], w.Losses[1]);
            for (int p = 0; p < 2; p++)
                Assert.True(ProductionMaps.RecountHalfPop(w, p) == w.HalfPop[p], $"seed {seed} tick {ran}: player {p} pop {w.HalfPop[p]}, recount {ProductionMaps.RecountHalfPop(w, p)}");

            for (int i = 0; i < cap; i++)
            {
                if (!u.Alive[i])
                {
                    wasAttacking[i] = wasHolding[i] = false;
                    continue;
                }
                UnitDef def = w.Data.Units[u.TypeId[i]];
                Vector2 pos = u.Position[i];
                Assert.True(float.IsFinite(pos.X) && float.IsFinite(pos.Y), $"seed {seed} tick {ran}: unit {i} at {pos}");
                Assert.True(u.Hp[i] > 0 && u.Hp[i] <= def.Hp, $"seed {seed} tick {ran}: unit {i} hp {u.Hp[i]} of {def.Hp}");
                if (u.Target[i] != default)
                {
                    bool live = u.TargetIsBuilding[i] ? w.Buildings.IsAlive(u.Target[i]) : u.IsAlive(u.Target[i]);
                    // M4-3b: an ordered Attack holds a building its owner remembers (the last-known list) until the owner sees
                    // its ground; gone meanwhile, the unit walks on to it but never swings at it.
                    bool remembered = u.TargetIsBuilding[i] && u.Mode[i] == CombatMode.Ordered && w.Fog.HasGhost(u.Owner[i], u.Target[i]);
                    Assert.True(live || remembered, $"seed {seed} tick {ran}: unit {i} targets dead {(u.TargetIsBuilding[i] ? "building" : "unit")} {u.Target[i]}");
                    if (!live) Assert.True(u.State[i] != UnitState.Attacking && u.WindupTicks[i] == 0, $"seed {seed} tick {ran}: unit {i} swings at a gone building");
                }
                bool same = lastGen[i] == u.Generation[i] && !ordered[i];
                bool attacking = u.State[i] == UnitState.Attacking;
                if (attacking) attackingTicks++;
                if (u.Mode[i] == CombatMode.Ordered && u.Target[i] != default)
                {
                    orderedTicks++;
                    // An explicit Attack's target is one its attack.targets allows.
                    AttackTargets allowed = def.Attack.Targets;
                    Assert.True(allowed == AttackTargets.All || (allowed == AttackTargets.Buildings) == u.TargetIsBuilding[i], $"seed {seed} tick {ran}: unit {i} ({def.Key}) targets a kind its attack.targets forbids");
                }
                // Attacking at the end of both ticks and not ordered: nothing may have moved it in between.
                if (same && wasAttacking[i] && attacking)
                    Assert.True(pos == lastPos[i], $"seed {seed} tick {ran}: unit {i} ({def.Key}) moved while Attacking, {lastPos[i]} -> {pos}; "
                        + $"target {u.Target[i]} building {u.TargetIsBuilding[i]}, mode {u.Mode[i]}, windup {u.WindupTicks[i]}, cooldown {u.CooldownTicks[i]}, hold {u.Hold[i]}, "
                        + $"goal cell {u.GoalCell[i]}, velocity {u.Velocity[i]}, gather {u.GatherNode[i]}, build {u.BuildTarget[i]}, queue {u.QueueCount[i]}, prev {u.PrevPosition[i]}");
                if (same && wasHolding[i] && u.Hold[i])
                {
                    holdingChecks++;
                    Assert.True(pos == lastPos[i], $"seed {seed} tick {ran}: holder {i} moved, {lastPos[i]} -> {pos}");
                }
                wasAttacking[i] = attacking;
                wasHolding[i] = u.Hold[i];
                lastPos[i] = pos;
                lastGen[i] = u.Generation[i];
            }
        }
        _out.WriteLine($"seed {seed}: {deaths} deaths ({buildingDeaths} buildings), kills {w.Kills[0]}/{w.Kills[1]}, {attackingTicks} unit-ticks Attacking, {holdingChecks} holder checks, " +
            $"{attackOrders} Attack orders, {orderedTicks} unit-ticks under one, {u.Count} units left");
        Assert.True(deaths >= 20, $"seed {seed}: only {deaths} deaths; the mix is not fighting");
        Assert.True(attackingTicks > 0 && holdingChecks > 0);
        Assert.True(orderedTicks > 0, $"seed {seed}: no Attack order was ever taken");
    }

    /// <summary>M4-2a: an Attack on a random unit (mostly an enemy's) or, one time in four, a random building; one in six handles stale.</summary>
    private static Command AttackOrder(ref SimRng rng, World w, int p, EntityHandle h, bool queued)
    {
        bool building = rng.NextInt(0, 4) == 0;
        EntityHandle target = default;
        if (building)
        {
            BuildingStore bs = w.Buildings;
            int start = rng.NextInt(0, bs.Capacity);
            for (int n = 0; n < bs.Capacity && target == default; n++)
            {
                int k = (start + n) % bs.Capacity;
                if (bs.Alive[k]) target = bs.HandleOf(k);
            }
        }
        else
        {
            int j = RandomUnit(w.Units, rng.NextInt(0, 5) == 0 ? p : 1 - p, rng.NextInt(0, w.Units.Capacity));
            if (j >= 0) target = new EntityHandle(j, w.Units.Generation[j]);
        }
        if (rng.NextInt(0, 6) == 0) target = new EntityHandle(target.Index, target.Generation + 1);
        return Command.Attack(p, h, target, building, queued);
    }

    /// <summary>A target freed by the test between ticks is an outside event, like an order: its attackers may stand down and walk on the next tick.</summary>
    private static void MarkAttackersOf(UnitStore u, EntityHandle victim, bool building, bool[] ordered)
    {
        for (int j = 0; j < u.Capacity; j++)
            if (u.Alive[j] && u.Target[j] == victim && u.TargetIsBuilding[j] == building) ordered[j] = true;
    }

    private static void Both(Simulation a, Simulation b, Command c)
    {
        a.Enqueue(c);
        b.Enqueue(c);
    }

    /// <summary>The first live unit of <paramref name="player"/> from slot <paramref name="start"/> on (wrapping), or -1.</summary>
    private static int RandomUnit(UnitStore u, int player, int start)
    {
        for (int n = 0; n < u.Capacity; n++)
        {
            int i = (start + n) % u.Capacity;
            if (u.Alive[i] && u.Owner[i] == player) return i;
        }
        return -1;
    }
}
