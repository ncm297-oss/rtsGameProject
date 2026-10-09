using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA for M4-V6a's <see cref="AbilityCaster"/> (ViewApi): proof it is read-only (a twin sim that never calls it keeps the same
/// state hash every tick while both get the same commands), and a seeded fuzz of its picks against a brute-force oracle
/// with casters dying, cooldowns running and stale handles in the selection.
/// </summary>
[Collection(SerialCollection.Name)]
public class AbilityCasterQaTests
{
    private readonly ITestOutputHelper _out;

    public AbilityCasterQaTests(ITestOutputHelper output) => _out = output;

    private static int TelasFire => TestSim.Data.FindAbility("telas_fire");

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    [InlineData(1234UL)]
    public void ViewReads_NeverChangeTheHash_AndPicksMatchTheOracle_UnderFuzz(ulong seed)
    {
        Simulation a = NoFights(units: 64, seed: seed), b = NoFights(units: 64, seed: seed);
        var rng = new Random((int)seed);
        var handlesA = new List<EntityHandle>();
        // Both players: mages and Raiders mixed on the 48 x 48 map; the same placements in both twins.
        for (int k = 0; k < 24; k++)
        {
            int owner = k % 2, type = k % 3 == 0 ? Raider : Mage;
            Vector2 at = At(a, 6 + rng.Next(36), 6 + rng.Next(36));
            handlesA.Add(Place(a, owner, type, at));
            Place(b, owner, type, at);
        }
        int ability = TelasFire;
        UnitStore u = a.World.Units;
        int sent = 0, picks = 0, deadInSelection = 0, readyMismatch = 0;
        for (int tick = 0; tick < 1500; tick++)
        {
            // A random selection of player 0's handles (some stale once units die), sometimes with duplicates.
            int n = 1 + rng.Next(8);
            var sel = new EntityHandle[n];
            for (int s = 0; s < n; s++) sel[s] = handlesA[rng.Next(handlesA.Count)];
            Vector2 point = At(a, 2 + rng.Next(44), 2 + rng.Next(44), (float)rng.NextDouble(), (float)rng.NextDouble());
            bool queued = rng.Next(4) == 0;
            int tickNow = a.World.TickNumber;

            int picked = AbilityCaster.PickCaster(sel, u.Alive, u.Generation, u.TypeId, u.Position, u.CastAbility, u.AbilityReadyTick,
                a.World.Data.Units, ability, tickNow, point, queued, out int index);
            int soonest = AbilityCaster.SoonestReady(sel, u.Alive, u.Generation, u.TypeId, u.AbilityReadyTick, a.World.Data.Units, ability, tickNow);
            int oracle = Oracle(a, sel, ability, point, queued, out int oracleIndex);
            Assert.Equal(oracle, picked);
            Assert.Equal(oracleIndex, index);
            Assert.Equal(OracleSoonest(a, sel, ability), soonest);
            // The button is live exactly when a plain click would send something.
            int plain = AbilityCaster.PickCaster(sel, u.Alive, u.Generation, u.TypeId, u.Position, u.CastAbility, u.AbilityReadyTick,
                a.World.Data.Units, ability, tickNow, point, false, out _);
            if ((soonest == 0) != (plain >= 0)) readyMismatch++;
            foreach (EntityHandle h in sel) if (!u.IsAlive(h)) deadInSelection++;

            if (picked >= 0)
            {
                picks++;
                Assert.True(u.Alive[picked] && AbilityCaster.CooldownLeft(u.AbilityReadyTick, picked, index, tickNow) == 0);
                var h = new EntityHandle(picked, u.Generation[picked]);
                Command c = Command.UseAbility(u.Owner[picked], h, index, point, queued);
                a.Enqueue(c);
                b.Enqueue(c);
                sent++;
            }
            // Player 1's mages cast too (straight commands, no ViewApi), so player 0's units burn and die.
            if (tick % 7 == 0)
            {
                EntityHandle e = handlesA[1 + 2 * rng.Next(handlesA.Count / 2)];
                if (a.World.Units.IsAlive(e) && a.World.Units.TypeId[e.Index] == Mage)
                {
                    Command c = Command.UseAbility(1, e, 0, At(a, 2 + rng.Next(44), 2 + rng.Next(44)));
                    a.Enqueue(c);
                    b.Enqueue(c);
                }
            }
            a.Tick();
            b.Tick();
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        Assert.Equal(0, readyMismatch);
        Assert.True(picks > 20, $"only {picks} picks: the fuzz proves little");
        _out.WriteLine($"seed {seed}: 1500 ticks, {sent} UseAbility from picks, {deadInSelection} dead handles seen in selections, hash equal every tick");
    }

    [Fact]
    public void PickCaster_ManyCalls_SameTick_ChangeNothing()
    {
        // 500 picks in one frame (a click spam between two ticks) leave every store array the hash covers unchanged.
        Simulation sim = NoFights();
        EntityHandle m1 = Place(sim, 0, Mage, At(sim, 10, 20)), m2 = Place(sim, 0, Mage, At(sim, 30, 20));
        sim.Tick();
        ulong before = sim.StateHash();
        int tick = sim.World.TickNumber;
        UnitStore u = sim.World.Units;
        var sel = new[] { m1, m2 };
        for (int k = 0; k < 500; k++)
        {
            AbilityCaster.PickCaster(sel, u.Alive, u.Generation, u.TypeId, u.Position, u.CastAbility, u.AbilityReadyTick,
                sim.World.Data.Units, TelasFire, tick, At(sim, k % 48, 20), k % 2 == 0, out _);
            AbilityCaster.SoonestReady(sel, u.Alive, u.Generation, u.TypeId, u.AbilityReadyTick, sim.World.Data.Units, TelasFire, tick);
        }
        Assert.Equal(before, sim.StateHash());
        Assert.Equal(tick, sim.World.TickNumber);
    }

    // Brute force of the documented rule: live, has the ability, off cooldown; busy (casting that ability) only when no free
    // one is ready and never when queued; nearest; tie = earlier in the selection.
    private static int Oracle(Simulation sim, EntityHandle[] sel, int ability, Vector2 point, bool queued, out int index)
    {
        UnitStore u = sim.World.Units;
        int bestFree = -1, bestBusy = -1, freeK = -1, busyK = -1;
        float dFree = float.PositiveInfinity, dBusy = float.PositiveInfinity;
        foreach (EntityHandle h in sel)
        {
            if (!u.IsAlive(h)) continue;
            UnitDef def = sim.World.Data.Units[u.TypeId[h.Index]];
            int k = def.Abilities.IndexOf(ability);
            if (k < 0) continue;
            if (u.AbilityReadyTick[h.Index * DataLimits.MaxUnitAbilities + k] > sim.World.TickNumber) continue;
            float d = Vector2.DistanceSquared(u.Position[h.Index], point);
            if (u.CastAbility[h.Index] == k)
            {
                if (queued) continue;
                if (d < dBusy) { dBusy = d; bestBusy = h.Index; busyK = k; }
            }
            else if (d < dFree) { dFree = d; bestFree = h.Index; freeK = k; }
        }
        if (bestFree >= 0) { index = freeK; return bestFree; }
        index = busyK;
        return bestBusy;
    }

    private static int OracleSoonest(Simulation sim, EntityHandle[] sel, int ability)
    {
        UnitStore u = sim.World.Units;
        int best = -1;
        foreach (EntityHandle h in sel)
        {
            if (!u.IsAlive(h)) continue;
            int k = sim.World.Data.Units[u.TypeId[h.Index]].Abilities.IndexOf(ability);
            if (k < 0) continue;
            int left = Math.Max(0, u.AbilityReadyTick[h.Index * DataLimits.MaxUnitAbilities + k] - sim.World.TickNumber);
            if (best < 0 || left < best) best = left;
        }
        return best;
    }
}
