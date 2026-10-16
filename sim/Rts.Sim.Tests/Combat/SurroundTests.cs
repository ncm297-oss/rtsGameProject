using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// Pathing slice 1 (docs/03 "Implementation (pathing slice 1)"): melee attackers ordered at a building take a free standing
/// spot round its footprint instead of queueing behind the friends already striking it. The scene is D18 report 4's
/// staging (a lone enemy building, no defenders, flat, combat on, the attackers in blocks 10 m west of it) on the shipped
/// data, but every assertion is a count or an invariant, never seconds pinned to the data: the building's hit points are
/// topped up between ticks, so a damage change can't raze it before the late arrivals strike. Serial: one row probes
/// allocation.
/// </summary>
[Collection(SerialCollection.Name)]
public class SurroundTests
{
    private const int MapSize = 64;
    private const int AnchorX = 34, AnchorY = 30;
    private const int Window = 200; // 10 s

    private readonly ITestOutputHelper _out;

    public SurroundTests(ITestOutputHelper output) => _out = output;

    /// <summary>Faction <paramref name="faction"/>'s building of <paramref name="slot"/>.</summary>
    private static int BuildingOf(int faction, BuildingSlot slot)
    {
        GameData data = TestSim.Data;
        for (int b = 0; b < data.Buildings.Length; b++)
            if (data.Buildings[b].Faction == faction && data.Buildings[b].Slot == slot) return b;
        throw new InvalidOperationException($"no {slot} for faction {faction}");
    }

    /// <summary>The other faction's building of <paramref name="slot"/> (the attackers' enemy).</summary>
    private static int BuildingFor(int attackerType, BuildingSlot slot) => BuildingOf(1 - TestSim.Data.Units[attackerType].Faction, slot);

    /// <summary>
    /// D18's staging: player 1's building of <paramref name="buildingType"/> at (34, 30), a player-0 spotter past it, and
    /// <paramref name="n"/> attackers in blocks 5 wide, 1.8 m apart, the front rank 10 m west of the footprint, every one
    /// ordered to Attack it. <paramref name="setup"/> runs before the building is placed (walls, keepers).
    /// </summary>
    private static Simulation Scene(int attackerType, int n, int buildingType, bool rule, ulong seed, out EntityHandle[] attackers,
        out EntityHandle building, Action<Simulation>? setup = null, Vector2? spotter = null)
    {
        var sim = new Simulation(TestSim.Config(seed, 2, n + 40, 8 * n + 32), LocalMovementTests.Flat(MapSize));
        World w = sim.World;
        w.SurroundRuleOn = rule;
        TestSim.KeepIn(sim, 1); // rule 24: the building's owner has a unit first
        setup?.Invoke(sim);
        BuildingDef bd = w.Data.Buildings[buildingType];
        Assert.True(w.Buildings.Spawn(1, buildingType, AnchorY * MapSize + AnchorX, out building));
        Spotter(sim, 0, spotter ?? At(sim, AnchorX + bd.FootprintWidth + 3, AnchorY));
        Vector2 west = At(sim, AnchorX, AnchorY) - new Vector2(1f, 1f);
        float midY = west.Y + bd.FootprintHeight;
        const int width = 5;
        const float spacing = 1.8f;
        attackers = new EntityHandle[n];
        for (int k = 0; k < n; k++)
        {
            int rank = k / width, file = k % width;
            int across = Math.Min(width, n - rank * width);
            var at = new Vector2(west.X - 10f - rank * spacing, midY + (file - (across - 1) / 2f) * spacing);
            attackers[k] = Place(sim, 0, attackerType, at);
            sim.Enqueue(Command.Attack(0, attackers[k], building, isBuilding: true));
        }
        return sim;
    }

    /// <summary>One tick, the building's hit points topped up first (a scene about standing room, not damage).</summary>
    private static void Tick(Simulation sim, EntityHandle building)
    {
        BuildingStore b = sim.World.Buildings;
        if (b.IsAlive(building)) b.SetHp(building.Index, sim.World.Data.Buildings[b.TypeId[building.Index]].Hp);
        sim.Tick();
    }

    /// <summary>How many of the attackers started a swing within <see cref="Window"/> ticks (a cooldown that went up is a strike begun).</summary>
    private static int Struck(Simulation sim, EntityHandle[] attackers, EntityHandle building)
    {
        UnitStore u = sim.World.Units;
        var struck = new bool[attackers.Length];
        var before = new int[attackers.Length];
        for (int t = 0; t < Window; t++)
        {
            for (int k = 0; k < attackers.Length; k++) before[k] = u.IsAlive(attackers[k]) ? u.CooldownTicks[attackers[k].Index] : 0;
            Tick(sim, building);
            for (int k = 0; k < attackers.Length; k++)
                if (u.IsAlive(attackers[k]) && u.CooldownTicks[attackers[k].Index] > before[k]) struck[k] = true;
        }
        return struck.Count(s => s);
    }

    private static int Measure(int attackerType, BuildingSlot slot, bool rule)
    {
        Simulation sim = Scene(attackerType, 20, BuildingFor(attackerType, slot), rule, 7, out EntityHandle[] a, out EntityHandle b);
        return Struck(sim, a, b);
    }

    /// <summary>The spot key unit <paramref name="i"/> really holds (its claim, named back by the holder array), or -1.</summary>
    private static int HeldSpot(World w, int i)
    {
        int c = w.SurroundClaim[i];
        return c >= 0 && w.SurroundHolder[c] == i + 1 ? c : -1;
    }

    // BUG-0732: two owners (three seats) attack one House from one point: each claims in its own layer, so player 2's unit
    // takes the same nearest spot as player 0's (a rival's claim is not "held"; before, it took another spot as if its own
    // side held that one). Both keys name the House's slot in their owner's layer.
    [Fact]
    public void TwoOwnersFromOnePoint_EachClaimsTheNearestSpotInItsOwnLayer()
    {
        var sim = new Simulation(TestSim.Config(7, 3, 16, 64), LocalMovementTests.Flat(MapSize));
        World w = sim.World;
        TestSim.KeepIn(sim, 1);
        Assert.True(w.Buildings.Spawn(1, BuildingFor(HeavyInfantry, BuildingSlot.House), AnchorY * MapSize + AnchorX, out EntityHandle b));
        Spotter(sim, 0, At(sim, AnchorX + 5, AnchorY));
        Spotter(sim, 2, At(sim, AnchorX + 5, AnchorY + 1));
        Vector2 from = At(sim, AnchorX - 6, AnchorY) - new Vector2(0f, 0.4f); // nearest the west face's first spot
        EntityHandle a0 = Place(sim, 0, HeavyInfantry, from), a2 = Place(sim, 2, HeavyInfantry, from + new Vector2(0f, 0.01f));
        sim.Enqueue(Command.Attack(0, a0, b, isBuilding: true));
        sim.Enqueue(Command.Attack(2, a2, b, isBuilding: true));
        sim.Tick();
        sim.Tick(); // the orders apply and the chase picks
        int layer = w.Buildings.Capacity * w.SurroundSpots;
        int k0 = HeldSpot(w, a0.Index), k2 = HeldSpot(w, a2.Index);
        Assert.True(k0 >= 0 && k2 >= 0, $"claims {k0}, {k2}");
        Assert.Equal(b.Index, k0 % layer / w.SurroundSpots);
        Assert.Equal(b.Index, k2 % layer / w.SurroundSpots);
        Assert.Equal((0, 2), (k0 / layer, k2 / layer));
        Assert.Equal(k0 % layer, k2 % layer);
    }

    // (a) 20 line units at a 2x2 and a 3x3 building: at least 16 strike within 10 s (before: 9-11).
    [Theory]
    [InlineData("malazan_heavy_infantry", BuildingSlot.House)]
    [InlineData("malazan_heavy_infantry", BuildingSlot.InfantryHall)]
    [InlineData("whirlwind_raider", BuildingSlot.House)]
    [InlineData("whirlwind_raider", BuildingSlot.InfantryHall)]
    public void TwentyLineUnits_SixteenStrikeWithin10s(string attacker, BuildingSlot slot)
    {
        int type = TestSim.Data.FindUnit(attacker);
        int before = Measure(type, slot, rule: false);
        int after = Measure(type, slot, rule: true);
        _out.WriteLine($"{attacker} v {slot}: struck within {Window} ticks: before {before} / 20, after {after} / 20");
        Assert.True(after >= 16, $"{after} / 20 struck");
    }

    // The Town Hall row is reported, not gated (the brief): it has the most room, so it must not do worse.
    [Theory]
    [InlineData("malazan_heavy_infantry")]
    [InlineData("whirlwind_raider")]
    public void TwentyLineUnits_TownHall_Report(string attacker)
    {
        int type = TestSim.Data.FindUnit(attacker);
        int before = Measure(type, BuildingSlot.TownHall, rule: false);
        int after = Measure(type, BuildingSlot.TownHall, rule: true);
        _out.WriteLine($"{attacker} v TownHall: struck within {Window} ticks: before {before} / 20, after {after} / 20");
        Assert.True(after >= before, $"after {after} < before {before}");
    }

    // (b) No two attackers hold one spot on any tick, through deaths, re-orders to the other building and walks away.
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void NoTwoAttackersClaimOneSpot_Fuzz(ulong seed)
    {
        const int n = 30;
        var sim = new Simulation(TestSim.Config(seed, 2, n + 8, 16 * n + 64), LocalMovementTests.Flat(MapSize));
        World w = sim.World;
        UnitStore u = w.Units;
        TestSim.KeepIn(sim, 1);
        Assert.True(w.Buildings.Spawn(1, BuildingOf(1, BuildingSlot.House), 30 * MapSize + 28, out EntityHandle house));
        Assert.True(w.Buildings.Spawn(1, BuildingOf(1, BuildingSlot.InfantryHall), 30 * MapSize + 36, out EntityHandle hall));
        Spotter(sim, 0, At(sim, 33, 37));
        var rng = new Random((int)seed);
        var units = new EntityHandle[n];
        for (int k = 0; k < n; k++)
        {
            int type = k % 2 == 0 ? HeavyInfantry : Raider;
            var at = At(sim, 16 + rng.Next(8), 22 + rng.Next(18), (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f);
            units[k] = Place(sim, 0, type, at);
            sim.Enqueue(Command.Attack(0, units[k], rng.Next(2) == 0 ? house : hall, isBuilding: true));
        }
        var seen = new int[w.SurroundHolder.Length];
        int claimsSeen = 0;
        for (int t = 1; t <= 300; t++)
        {
            Tick(sim, house);
            if (w.Buildings.IsAlive(hall)) w.Buildings.SetHp(hall.Index, w.Data.Buildings[w.Buildings.TypeId[hall.Index]].Hp);
            int pick = rng.Next(n);
            if (t % 15 == 0 && u.IsAlive(units[pick])) u.Free(units[pick]); // a death between ticks
            else if (t % 7 == 0 && u.IsAlive(units[pick]))
            {
                bool onHouse = u.Target[units[pick].Index] == house;
                sim.Enqueue(Command.Attack(0, units[pick], onHouse ? hall : house, isBuilding: true));
            }
            else if (t % 23 == 0 && u.IsAlive(units[pick])) sim.Enqueue(Command.Move(0, units[pick], At(sim, 10, 10)));
            Array.Clear(seen);
            for (int i = 0; i < u.Capacity; i++)
            {
                // An active claim: named back by the holder array, on the live building the unit chases now.
                if (!u.Alive[i] || !u.TargetIsBuilding[i] || !w.Buildings.IsAlive(u.Target[i])) continue;
                int key = HeldSpot(w, i);
                if (key < 0 || key / w.SurroundSpots != u.Target[i].Index) continue;
                claimsSeen++;
                Assert.True(seen[key] == 0, $"seed {seed} tick {t}: units {seen[key] - 1} and {i} both hold spot {key}");
                seen[key] = i + 1;
            }
            // And no two walkers head for one spot point (the claim is what keeps them apart).
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.State[i] != UnitState.Moving || HeldSpot(w, i) < 0 || !u.TargetIsBuilding[i]) continue;
                for (int j = i + 1; j < u.Capacity; j++)
                    if (u.Alive[j] && u.State[j] == UnitState.Moving && HeldSpot(w, j) >= 0 && u.TargetIsBuilding[j] && u.Goal[j] == u.Goal[i])
                        Assert.Fail($"seed {seed} tick {t}: units {i} and {j} both walk to {u.Goal[i]}");
            }
        }
        Assert.True(claimsSeen > 1000, $"only {claimsSeen} claims seen: the fuzz didn't exercise the rule");
    }

    // (c) A spot freed by a death is taken by a waiting attacker within 40 ticks.
    [Fact]
    public void ASpotFreedByADeath_IsTakenByAWaiterWithin40Ticks()
    {
        int type = HeavyInfantry;
        Simulation sim = Scene(type, 28, BuildingFor(type, BuildingSlot.House), rule: true, 7, out EntityHandle[] a, out EntityHandle b);
        World w = sim.World;
        UnitStore u = w.Units;
        for (int t = 0; t < Window; t++) Tick(sim, b);
        // A striker on its spot, and at least one attacker still waiting (no spot).
        int victim = -1, waiters = 0;
        foreach (EntityHandle h in a)
        {
            if (!u.IsAlive(h)) continue;
            int key = HeldSpot(w, h.Index);
            if (key < 0) waiters++;
            else if (victim < 0 && u.State[h.Index] == UnitState.Attacking) victim = h.Index;
        }
        Assert.True(victim >= 0, "no striker holds a spot");
        Assert.True(waiters > 0, "nobody waits: the scene has room for all");
        int freed = HeldSpot(w, victim);
        var waiting = new bool[u.Capacity];
        foreach (EntityHandle h in a) waiting[h.Index] = u.IsAlive(h) && HeldSpot(w, h.Index) < 0;
        u.Free(new EntityHandle(victim, u.Generation[victim]));
        // Taken: a unit that was waiting at the death holds the spot and strikes from it (the first claimer may lose it to a
        // waiter that steps onto it first: the claim follows whoever stands there).
        int claimed = -1, taken = -1, by = -1;
        for (int t = 1; t <= 40 && taken < 0; t++)
        {
            Tick(sim, b);
            int h = w.SurroundHolder[freed] - 1;
            if (h < 0 || h == victim || !u.Alive[h] || HeldSpot(w, h) != freed) continue;
            if (claimed < 0) claimed = t;
            if (u.State[h] == UnitState.Attacking)
            {
                taken = t;
                by = h;
            }
        }
        _out.WriteLine($"spot {freed} freed by a death: claimed after {claimed} ticks, a waiter strikes from it after {taken} ({waiters} were waiting)");
        Assert.True(taken > 0, "the freed spot was not taken by a striker within 40 ticks");
        Assert.True(waiting[by], $"unit {by} took it, but it held another spot already");
    }

    // (d) One open side (the others walled by own buildings): it fills, and the rest wait without giving the order up.
    [Fact]
    public void OneOpenSide_Fills_AndTheRestWaitWithoutAGiveUp()
    {
        int type = HeavyInfantry;
        int wall = BuildingOf(TestSim.Data.Units[type].Faction, BuildingSlot.House);
        void Walls(Simulation s)
        {
            TestSim.KeepIn(s, 0, nearCorner: true);
            // Player 0's own Houses on the west, north and east of the target's (34, 30) 2x2: only its south row is open.
            foreach ((int x, int y) in new[] { (32, 30), (32, 32), (34, 32), (36, 32), (36, 30) })
                Assert.True(s.World.Buildings.Spawn(0, wall, y * MapSize + x, out _), $"wall at ({x}, {y})");
        }
        Simulation sim = Scene(type, 20, BuildingFor(type, BuildingSlot.House), rule: true, 7, out EntityHandle[] a, out EntityHandle b,
            Walls, spotter: null);
        World w = sim.World;
        UnitStore u = w.Units;
        int maxStriking = 0;
        Tick(sim, b); // the Attack orders apply on the second tick
        for (int t = 2; t <= 400; t++)
        {
            Tick(sim, b);
            int striking = 0;
            foreach (EntityHandle h in a)
            {
                Assert.True(u.IsAlive(h));
                int i = h.Index;
                // Every attacker still holds its order on the standing building: no give-up, no Idle without it.
                Assert.True(u.Target[i] == b && u.TargetIsBuilding[i] && u.Mode[i] == CombatMode.Ordered,
                    $"tick {t}: unit {i} dropped its order ({u.State[i]}, mode {u.Mode[i]})");
                if (u.State[i] == UnitState.Attacking) striking++;
            }
            maxStriking = Math.Max(maxStriking, striking);
        }
        _out.WriteLine($"one open side: at most {maxStriking} of 20 striking at once");
        Assert.True(maxStriking >= 4, $"the open side never filled: {maxStriking} striking");
    }

    // BUG-0731: attack-movers (the AI's waves) on open ground round a lone House. 20 attackers, 20 spots: one stalled behind
    // the strikers on the way to the free spot it already claims keeps the building (its walk reaches the spot), as an
    // ordered attacker does. Before the fix 4 of 20 gave it up (QA's SurroundQaTests row, seed 37).
    [Theory]
    [InlineData("malazan_heavy_infantry")]
    [InlineData("whirlwind_raider")]
    public void AttackMove_StalledOnTheWayToItsClaimedSpot_KeepsTheBuilding(string attacker)
    {
        int hi = TestSim.Data.FindUnit(attacker);
        var sim = new Simulation(TestSim.Config(37, 2, 2 + 40, 16 * 42 + 64), LocalMovementTests.Flat(MapSize));
        World w = sim.World;
        UnitStore u = w.Units;
        TestSim.KeepIn(sim, 1);
        Assert.True(w.Buildings.Spawn(1, BuildingFor(hi, BuildingSlot.House), AnchorY * MapSize + AnchorX, out EntityHandle b));
        Spotter(sim, 0, At(sim, 35, 27));
        var a = new EntityHandle[20];
        for (int k = 0; k < 20; k++)
        {
            a[k] = Place(sim, 0, hi, At(sim, 33 + k % 5, 22 - k / 5));
            sim.Enqueue(Command.AttackMove(0, a[k], At(sim, 35, 40)));
        }
        int engaged = 0;
        for (int t = 0; t < 400; t++)
        {
            Tick(sim, b);
            foreach (EntityHandle h in a)
            {
                int i = h.Index;
                if (u.GiveUps[i] > 0)
                    Assert.Fail($"{attacker} tick {sim.TickNumber}: unit {i} gave the House up (state {u.State[i]}, mode {u.Mode[i]}, held spot {HeldSpot(w, i)})");
                if (t == 399 && u.Target[i] == b) engaged++;
            }
        }
        _out.WriteLine($"{attacker}: {engaged} / 20 on the House at tick 400, no give-up");
        Assert.True(engaged >= 16, $"only {engaged} / 20 still on the House");
    }

    // Determinism: the same scene twice hashes equal on every tick, both groups.
    [Theory]
    [InlineData("malazan_heavy_infantry")]
    [InlineData("whirlwind_raider")]
    public void SurroundScene_HashTwins(string attacker)
    {
        int type = TestSim.Data.FindUnit(attacker);
        int bt = BuildingFor(type, BuildingSlot.House);
        Simulation x = Scene(type, 20, bt, true, 7, out _, out EntityHandle bx);
        Simulation y = Scene(type, 20, bt, true, 7, out _, out EntityHandle by);
        for (int t = 0; t < 300; t++)
        {
            Tick(x, bx);
            Tick(y, by);
            Assert.Equal(x.StateHash(), y.StateHash());
        }
    }

    /// <summary>The 40-attacker scene: 20 Heavy Infantry from the west and 20 Raiders from the south at an infantry hall.</summary>
    private static Simulation Forty(bool rule, out EntityHandle b)
    {
        int hi = HeavyInfantry, raider = Raider;
        Simulation sim = Scene(hi, 20, BuildingFor(hi, BuildingSlot.InfantryHall), rule, 7, out _, out b);
        for (int k = 0; k < 20; k++)
        {
            EntityHandle h = Place(sim, 0, raider, At(sim, AnchorX + 1 + k % 5, AnchorY - 6 - k / 5));
            sim.Enqueue(Command.Attack(0, h, b, isBuilding: true));
        }
        return sim;
    }

    // Cost, printed only (wall clock): the 40-attacker scene's ticks with the rule and without, over its first 400 ticks.
    [Fact]
    public void FortyAttackers_TickCost_Report()
    {
        var ms = new double[2];
        for (int pass = 0; pass < 3; pass++)
            for (int r = 0; r < 2; r++)
            {
                Simulation sim = Forty(r == 1, out EntityHandle b);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int t = 0; t < 400; t++) Tick(sim, b);
                if (pass > 0) ms[r] += watch.Elapsed.TotalMilliseconds / 400 / 2;
            }
        _out.WriteLine($"40 attackers at an infantry hall, mean tick: rule off {ms[0]:F3} ms, rule on {ms[1]:F3} ms");
    }

    // Perf: a 40-attacker surround (20 of each line unit at an infantry hall) allocates nothing per tick.
    [Fact]
    public void FortyAttackers_Surround_AllocatesNothing()
    {
        Simulation sim = Forty(true, out EntityHandle b);
        for (int t = 0; t < 120; t++) Tick(sim, b);
        BuildingStore bs = sim.World.Buildings;
        int full = sim.World.Data.Buildings[bs.TypeId[b.Index]].Hp;
        Action ticks = () =>
        {
            for (int t = 0; t < 40; t++)
            {
                bs.SetHp(b.Index, full);
                sim.Tick();
            }
        };
        AllocationProbe.AssertZero(ticks, _out);
        Assert.True(bs.IsAlive(b));
    }
}
