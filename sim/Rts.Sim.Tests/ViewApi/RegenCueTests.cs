using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>
/// M7-V3a: <see cref="RegenCue.Healing"/>, the read-only "is this unit regenerating" read behind the selection panel's cue,
/// against what <see cref="RegenSystem"/> really does on the next tick (the regen copy of <see cref="RegenTests"/>: Malazan
/// 1 HP/s after 6 s). A call writes nothing: the twin rows hash equal every tick with and without it.
/// </summary>
[Collection(SerialCollection.Name)]
public class RegenCueTests
{
    private static GameData D => RegenTests.s_regen.Value;

    private static bool Cue(Simulation sim, int slot)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        return RegenCue.Healing(w.Data, u.Statuses, slot, w.FactionOf(u.Owner[slot]), u.Hp[slot], w.MaxHp(slot), u.LastDamagedTick[slot], w.TickNumber);
    }

    private static EntityHandle Heavy(Simulation sim, int x = 20, int y = 20) =>
        CombatScenes.Place(sim, 0, D.FindUnit("malazan_heavy_infantry"), CombatScenes.At(sim, x, y));

    /// <summary>The panel's five moments: hurt, no cue for the 6 s delay, cue after, gone on the next hit, gone at full health.</summary>
    [Fact]
    public void AHurtUnit_ShowsNoCueThroughTheDelay_ThenTheCue_GoneOnAHit_AndAtFullHealth()
    {
        Simulation sim = RegenTests.Scene(D);
        UnitStore u = sim.World.Units;
        EntityHandle h = Heavy(sim);
        sim.Tick();
        Assert.False(Cue(sim, h.Index)); // at full health, never damaged
        int t = sim.TickNumber;
        RegenTests.Hit(sim, h, 30); // its tick: t
        for (int k = t; k < t + 120; k++)
        {
            Assert.True(!Cue(sim, h.Index), $"cue at next tick {sim.TickNumber} (hit in tick {t})");
            sim.Tick();
        }
        Assert.Equal(t + 120, sim.TickNumber);
        Assert.True(Cue(sim, h.Index)); // tick t + 120 heals
        sim.Tick();
        Assert.Equal(20, u.RegenAccum[h.Index]);
        for (int k = 0; k < 100; k++) sim.Tick();
        Assert.True(Cue(sim, h.Index));
        RegenTests.Hit(sim, h, 5);
        Assert.False(Cue(sim, h.Index)); // the hit's tick is the next one: the delay restarts at once
        while (u.Hp[h.Index] < sim.World.MaxHp(h.Index)) sim.Tick();
        Assert.False(Cue(sim, h.Index)); // full
        sim.Tick();
        Assert.False(Cue(sim, h.Index));
    }

    /// <summary>A regenerating status shows the cue through the faction's delay and until its last tick's countdown ends it.</summary>
    [Fact]
    public void ARegeneratingStatus_ShowsTheCue_ThroughTheDelay_UntilItsLastTick()
    {
        Simulation sim = RegenTests.Scene(D);
        UnitStore u = sim.World.Units;
        EntityHandle h = Heavy(sim);
        sim.Tick();
        RegenTests.Hit(sim, h, 60);
        StatusSystem.Apply(sim.World, h.Index, D.FindStatus("regenerating"), 2f, 10, 0);
        for (int k = 0; k < 9; k++)
        {
            Assert.True(Cue(sim, h.Index), $"status tick {k}: no cue with {u.Statuses.TicksRemaining[h.Index * StatusStore.PerUnit]} left");
            sim.Tick();
        }
        Assert.Equal(1, u.Statuses.TicksRemaining[h.Index * StatusStore.PerUnit]);
        Assert.False(Cue(sim, h.Index)); // its last tick's countdown ends it before regeneration reads it
    }

    /// <summary>A unit of a faction without regen and without the status never shows it; a burning pulse due next tick hides the faction's.</summary>
    [Fact]
    public void NoRegen_NoCue_AndABurningPulseDue_HidesTheFactionRate()
    {
        Simulation sim = RegenTests.Scene(D);
        UnitStore u = sim.World.Units;
        EntityHandle raider = CombatScenes.Place(sim, 1, D.FindUnit("whirlwind_raider"), CombatScenes.At(sim, 30, 30));
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 0, raider, false, 20));
        for (int k = 0; k < 200; k++)
        {
            Assert.False(Cue(sim, raider.Index));
            sim.Tick();
        }
        EntityHandle h = Heavy(sim);
        sim.Tick();
        RegenTests.Hit(sim, h, 60);
        for (int k = 0; k < 130; k++) sim.Tick();
        Assert.True(Cue(sim, h.Index));
        StatusSystem.Apply(sim.World, h.Index, D.FindStatus("burning"), 1f, 200, 1);
        int pulses = 0, rows = 0;
        for (int k = 0; k < 120; k++)
        {
            bool due = u.Statuses.PulseTicks[h.Index * StatusStore.PerUnit] == 1;
            int before = u.Hp[h.Index] * CombatConstants.RegenFixedOne + u.RegenAccum[h.Index];
            bool cue = Cue(sim, h.Index);
            if (due) pulses++;
            Assert.Equal(!due && sim.TickNumber - u.LastDamagedTick[h.Index] >= 120, cue);
            sim.Tick();
            int after = u.Hp[h.Index] * CombatConstants.RegenFixedOne + u.RegenAccum[h.Index];
            if (!due) Assert.Equal(cue, after > before);
            rows++;
        }
        Assert.True(pulses > 0 && rows == 120);
    }

    /// <summary>
    /// A 3,000-tick 30 v 30 brawl on the regen copy (statuses on some Whirlwind units): every tick, for every live unit, the
    /// cue read before the tick agrees with what the tick did (a unit not hit in it gained exactly when the cue said so), and
    /// the state hash is the twin's (the same match with no call) on every tick.
    /// </summary>
    [Fact]
    public void ThroughABrawl_TheCueIsWhatTheNextTickHeals_AndCallingItMovesNoHash()
    {
        Simulation sim = Brawl(), twin = Brawl();
        UnitStore u = sim.World.Units;
        int n = u.Capacity, healedRows = 0, waitingRows = 0, statusRows = 0;
        var cue = new bool[n];
        var before = new int[n];
        var gen = new int[n];
        var alive = new bool[n];
        int regen = D.FindStatus("regenerating");
        for (int k = 0; k < 3000; k++)
        {
            if (sim.TickNumber % 300 == 200)
                for (int i = 0, given = 0; i < n && given < 3; i++)
                    if (u.Alive[i] && u.Owner[i] == 1 && u.Hp[i] < sim.World.MaxHp(i))
                    {
                        StatusSystem.Apply(sim.World, i, regen, 3f, 80, 1);
                        StatusSystem.Apply(twin.World, i, regen, 3f, 80, 1);
                        given++;
                    }
            for (int i = 0; i < n; i++)
            {
                alive[i] = u.Alive[i];
                if (!alive[i]) continue;
                gen[i] = u.Generation[i];
                cue[i] = Cue(sim, i);
                before[i] = u.Hp[i] * CombatConstants.RegenFixedOne + u.RegenAccum[i];
            }
            int ran = sim.TickNumber;
            sim.Tick();
            twin.Tick();
            Assert.True(sim.StateHash() == twin.StateHash(), $"tick {ran}: the cue's reads moved the hash");
            for (int i = 0; i < n; i++)
            {
                if (!alive[i] || !u.Alive[i] || u.Generation[i] != gen[i]) continue;
                int after = u.Hp[i] * CombatConstants.RegenFixedOne + u.RegenAccum[i];
                bool hit = u.LastDamagedTick[i] == ran || after < before[i];
                if (hit) continue;
                Assert.True(cue[i] == (after > before[i]), $"tick {ran} unit {i} (owner {u.Owner[i]}): cue {cue[i]}, gained {after - before[i]}");
                if (cue[i] && u.Statuses.HasRegen(i)) statusRows++;
                if (cue[i]) healedRows++;
                else if (u.Hp[i] < sim.World.MaxHp(i)) waitingRows++;
            }
        }
        Assert.True(healedRows > 100 && waitingRows > 100 && statusRows > 10, $"rows: healed {healedRows}, waiting {waitingRows}, status {statusRows}");
    }

    private static Simulation Brawl()
    {
        var sim = new Simulation(new SimConfig(7, 2, 128, 1056) { Data = D }, LocalMovementTests.Flat(64));
        CombatScenes.FlatBrawl(sim, 30, new Vector2(64f, 64f), gap: 6f, ranks: 10);
        return sim;
    }

    /// <summary>Reading the cue for every slot of a brawl allocates nothing.</summary>
    [Fact]
    public void ReadingEverySlot_AllocatesNothing()
    {
        Simulation sim = Brawl();
        for (int k = 0; k < 400; k++) sim.Tick();
        int n = sim.World.Units.Capacity, shown = 0;
        AllocationProbe.AssertZero(() =>
        {
            for (int r = 0; r < 50; r++)
                for (int i = 0; i < n; i++)
                    if (sim.World.Units.Alive[i] && Cue(sim, i)) shown++;
        });
        Assert.True(shown >= 0);
    }

    /// <summary>Out-of-range slots and factions read false, never throw.</summary>
    [Fact]
    public void OutOfRange_IsFalse()
    {
        var s = new StatusStore(2);
        Assert.False(RegenCue.Healing(D, s, -1, 0, 1, 10, -1, 5));
        Assert.False(RegenCue.Healing(D, s, 2, 0, 1, 10, -1, 5));
        Assert.False(RegenCue.Healing(D, s, 0, -1, 1, 10, -1, 5));
        Assert.False(RegenCue.Healing(D, s, 0, D.Factions.Length, 1, 10, -1, 5));
        Assert.True(RegenCue.Healing(D, s, 0, D.FindFaction("malazan"), 1, 10, -1, 5));
        Assert.False(RegenCue.Healing(D, s, 0, D.FindFaction("malazan"), 10, 10, -1, 5));
    }
}
