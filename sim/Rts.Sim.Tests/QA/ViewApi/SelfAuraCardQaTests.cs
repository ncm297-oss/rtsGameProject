using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA for M4-V6d's no-target card path in ViewApi (<see cref="AbilityCaster.Lead"/> then <see cref="AbilityCaster.PickCaster"/>
/// at the lead, one <c>UseAbility</c> at the caster's own position): a seeded fuzz on the selfAura test data against a
/// brute-force oracle, with Heavies dying in a brawl, stale and foreign handles in the selection and cooldowns running,
/// while a twin that never calls ViewApi gets the same commands and keeps the same state hash every tick.
/// </summary>
[Collection(SerialCollection.Name)]
public class SelfAuraCardQaTests
{
    private readonly ITestOutputHelper _out;

    public SelfAuraCardQaTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => SelfAuraTests.Data;

    private static Simulation Sim(ulong seed) =>
        TestSim.Explored(new Simulation(new SimConfig(seed, 2, 96, 1024) { Data = Data, Combat = true }, LocalMovementTests.Flat(48)));

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(42UL)]
    public void LeadThenPick_MatchesTheOracle_SendsAtTheCaster_AndNeverMovesTheHash(ulong seed)
    {
        Simulation a = Sim(seed), b = Sim(seed);
        int heavy = Data.FindUnit("malazan_heavy_infantry"), crossbow = Data.FindUnit("malazan_crossbowman"), raider = Data.FindUnit("whirlwind_raider");
        int rally = Data.FindAbility("zz_test_rally"), zone = Data.FindAbility("zz_test_aura_zone");
        var rng = new Random((int)seed);
        var own = new List<EntityHandle>();
        for (int k = 0; k < 30; k++)
        {
            int type = k % 4 == 3 ? crossbow : heavy;
            Vector2 at = At(a, 8 + rng.Next(32), 8 + rng.Next(32));
            own.Add(Place(a, 0, type, at));
            Place(b, 0, type, at);
        }
        for (int k = 0; k < 14; k++)
        {
            Vector2 at = At(a, 8 + rng.Next(32), 8 + rng.Next(32));
            Place(a, 1, raider, at);
            Place(b, 1, raider, at);
        }
        UnitStore u = a.World.Units;
        int sent = 0, leadCast = 0, none = 0, stale = 0;
        for (int tick = 0; tick < 1200; tick++)
        {
            int n = 1 + rng.Next(6);
            var sel = new EntityHandle[n];
            for (int s = 0; s < n; s++)
            {
                EntityHandle h = own[rng.Next(own.Count)];
                // now and then a handle the slot has outlived, or one out of the store's range
                if (rng.Next(10) == 0) h = new EntityHandle(h.Index, h.Generation + 1);
                if (rng.Next(40) == 0) h = new EntityHandle(u.Capacity + 3, 1);
                sel[s] = h;
            }
            int ability = rng.Next(2) == 0 ? rally : zone;
            bool queued = rng.Next(4) == 0;
            int now = a.World.TickNumber;

            bool has = AbilityCaster.Lead(sel, u.Alive, u.Generation, u.TypeId, u.Position, Data.Units, ability, out Vector2 lead);
            int leadSlot = OracleLead(a, sel, ability);
            Assert.Equal(leadSlot >= 0, has);
            if (!has) { none++; Assert.Equal(default, lead); }
            else Assert.Equal(u.Position[leadSlot], lead);
            foreach (EntityHandle h in sel) if ((uint)h.Index >= (uint)u.Capacity || !u.IsAlive(h)) stale++;
            if (has)
            {
                int picked = AbilityCaster.PickCaster(sel, u.Alive, u.Generation, u.TypeId, u.Position, u.CastAbility, u.AbilityReadyTick, u.QueueCount,
                    u.QueueKind, u.QueueTypeId, ReadOnlySpan<EntityHandle>.Empty, Data.Units, ability, now, lead, queued, out int index);
                int k = Data.Units[u.TypeId[leadSlot]].Abilities.IndexOf(ability);
                bool leadFreeReady = u.AbilityReadyTick[leadSlot * DataLimits.MaxUnitAbilities + k] <= now && u.CastAbility[leadSlot] != k;
                if (leadFreeReady && !QueuedCast(u, leadSlot, k))
                {
                    // docs/03 "Implementation (M4-V6d)": the lead casts when it is ready and free.
                    Assert.Equal(leadSlot, picked);
                    leadCast++;
                }
                if (picked >= 0)
                {
                    Assert.True(u.Alive[picked] && Data.Units[u.TypeId[picked]].Abilities[index] == ability);
                    Command c = Command.UseAbility(0, new EntityHandle(picked, u.Generation[picked]), index, u.Position[picked], queued);
                    a.Enqueue(c);
                    b.Enqueue(c);
                    sent++;
                }
            }
            a.Tick();
            b.Tick();
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        Assert.True(sent > 50 && leadCast > 20, $"only {sent} casts ({leadCast} by the lead): the fuzz proves little");
        _out.WriteLine($"seed {seed}: 1200 ticks, {sent} selfAura casts sent ({leadCast} by a ready, free lead), {none} selections with no caster, {stale} stale or foreign handles; hash equal every tick");
    }

    // The documented lead: the first live selected unit whose type lists the ability, ready or not.
    private static int OracleLead(Simulation sim, EntityHandle[] sel, int ability)
    {
        UnitStore u = sim.World.Units;
        foreach (EntityHandle h in sel)
        {
            if ((uint)h.Index >= (uint)u.Capacity || !u.IsAlive(h)) continue;
            if (sim.World.Data.Units[u.TypeId[h.Index]].Abilities.IndexOf(ability) >= 0) return h.Index;
        }
        return -1;
    }

    private static bool QueuedCast(UnitStore u, int slot, int k)
    {
        for (int q = 0; q < u.QueueCount[slot]; q++)
        {
            int at = slot * Rts.Sim.Orders.OrderConstants.QueueCapacity + q;
            if (u.QueueKind[at] == CommandKind.UseAbility && u.QueueTypeId[at] == k) return true;
        }
        return false;
    }
}
