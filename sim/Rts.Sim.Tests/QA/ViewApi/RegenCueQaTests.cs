using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA M7-V3a (session 2026-11-11-1317): attacks on <see cref="RegenCue.Healing"/>, the read behind the selection panel's
/// "Regenerating" cue, on the regen copy (Malazan 1 HP/s after 6 s, <see cref="RegenTests"/>). Every row compares the cue
/// read between ticks with what the next tick really did to the unit's fixed-point hit points (hp x RegenFixedOne + the
/// accumulator), so the cue can never claim healing that does not happen or miss healing that does.
/// (1) A unit hit every 118 ticks (5.9 s) never shows the cue and never heals; at 120 ticks the hit lands in the tick the
/// delay ends (still never); at 121 / 140 ticks the cue shows on exactly the 1 / 21 healing ticks a cycle. (2) A regenerating status and the faction rule at once: the cue through the delay by the
/// status alone, off when the status ends inside the delay, on again by the faction rule, and both rates summed when both
/// apply. (3) Seeded fuzz: random hits, regenerating and burning statuses on Malazan and Whirlwind units over many seeds,
/// cue v gain on every tick, and the state hash equal to an untouched twin's at the end of every seed.
/// </summary>
[Collection(SerialCollection.Name)]
public class RegenCueQaTests
{
    private static GameData D => RegenTests.s_regen.Value;

    private static bool Cue(Simulation sim, int slot)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        return RegenCue.Healing(w.Data, u.Statuses, slot, w.FactionOf(u.Owner[slot]), u.Hp[slot], w.MaxHp(slot), u.LastDamagedTick[slot], w.TickNumber);
    }

    private static long Fixed(Simulation sim, int slot) =>
        (long)sim.World.Units.Hp[slot] * CombatConstants.RegenFixedOne + sim.World.Units.RegenAccum[slot];

    private static EntityHandle Heavy(Simulation sim, int owner = 0, int x = 20, int y = 20) =>
        CombatScenes.Place(sim, owner, D.FindUnit(owner == 0 ? "malazan_heavy_infantry" : "whirlwind_raider"), CombatScenes.At(sim, x, y));

    [Theory]
    [InlineData(118, 0)] // 5.9 s: never
    [InlineData(119, 0)]
    [InlineData(120, 0)] // the hit lands in the very tick the delay would have ended
    [InlineData(121, 1)]
    [InlineData(140, 21)]
    public void AUnitHitEveryNTicks_ShowsTheCueOnlyOnTheTicksItHeals(int period, int cueTicksPerCycle)
    {
        Simulation sim = RegenTests.Scene(D);
        EntityHandle h = Heavy(sim);
        sim.Tick();
        int cues = 0, healed = 0, cycles = 0;
        for (int k = 0; k < 3000; k++)
        {
            int i = h.Index;
            if (k % period == 0)
            {
                // A light hit keeps it alive through 3,000 ticks (130 hp, 2 a hit, at most 26 hits).
                RegenTests.Hit(sim, h, 2);
                cycles++;
            }
            bool cue = Cue(sim, i);
            long before = Fixed(sim, i);
            bool hitThisTick = sim.World.Units.LastDamagedTick[i] == sim.TickNumber;
            sim.Tick();
            Assert.True(sim.World.Units.IsAlive(h), $"period {period}: the unit died at tick {sim.TickNumber}");
            long after = Fixed(sim, i);
            if (cue) cues++;
            if (after > before) healed++;
            if (!hitThisTick) Assert.True(cue == (after > before), $"period {period}, tick {sim.TickNumber - 1}: cue {cue}, gained {after - before}");
            else Assert.False(cue, $"period {period}: a cue on the hit's own tick {sim.TickNumber - 1}");
        }
        Assert.Equal(cues, healed);
        // The first cycle starts at k = 0; whole cycles only.
        int whole = 3000 / period;
        Assert.InRange(cues, cueTicksPerCycle * (whole - 1), cueTicksPerCycle * whole + cueTicksPerCycle);
        Assert.True(cycles > 20);
    }

    [Fact]
    public void AStatusAndTheFactionRuleAtOnce_TheCueFollowsTheSumOfBoth()
    {
        Simulation sim = RegenTests.Scene(D);
        UnitStore u = sim.World.Units;
        EntityHandle h = Heavy(sim);
        int regen = D.FindStatus("regenerating");
        sim.Tick();
        RegenTests.Hit(sim, h, 80);
        int hitTick = sim.TickNumber;
        // 40 ticks of a 2 HP/s status inside the 120-tick delay.
        StatusSystem.Apply(sim.World, h.Index, regen, 2f, 40, 1);
        int statusRate = DataLoader.RegenPerTick(2), factionRate = D.Factions[D.FindFaction("malazan")].RegenPerTick;
        int phase = 0; // 0 status only, 1 nothing, 2 faction only, 3 both
        var seen = new bool[4];
        for (int k = 0; k < 400; k++)
        {
            int next = sim.TickNumber;
            if (next == hitTick + 200) StatusSystem.Apply(sim.World, h.Index, regen, 2f, 60, 1);
            bool cue = Cue(sim, h.Index);
            long before = Fixed(sim, h.Index);
            bool status = u.Statuses.HasRegen(h.Index) && u.Statuses.TicksRemaining[h.Index * StatusStore.PerUnit] > 1;
            bool faction = next - u.LastDamagedTick[h.Index] >= 120;
            phase = status && faction ? 3 : faction ? 2 : status ? 0 : 1;
            sim.Tick();
            long gain = Fixed(sim, h.Index) - before;
            if (u.Hp[h.Index] >= sim.World.MaxHp(h.Index)) break;
            int want = (status ? statusRate : 0) + (faction ? factionRate : 0);
            Assert.True(gain == want, $"next tick {next}, phase {phase}: gained {gain}, want {want}");
            Assert.True(cue == (want > 0), $"next tick {next}, phase {phase}: cue {cue}, rate {want}");
            seen[phase] = true;
        }
        Assert.True(seen[0] && seen[1] && seen[2] && seen[3], $"phases seen: {string.Join(",", seen)}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void SeededFuzz_HitsAndStatuses_TheCueIsTheNextTicksGain_AndTheHashIsTheTwins(int seed)
    {
        Simulation sim = RegenTests.Scene(D), twin = RegenTests.Scene(D);
        int regen = D.FindStatus("regenerating"), burning = D.FindStatus("burning");
        var units = new EntityHandle[12];
        for (int k = 0; k < units.Length; k++)
        {
            int owner = k % 3 == 2 ? 1 : 0; // two in three regenerate by faction
            units[k] = Heavy(sim, owner, 8 + (k % 4) * 8, 8 + (k / 4) * 8);
            Heavy(twin, owner, 8 + (k % 4) * 8, 8 + (k / 4) * 8);
        }
        sim.Tick();
        twin.Tick();
        ulong rng = (ulong)seed * 0x9E3779B97F4A7C15UL + 1;
        int Next(int n)
        {
            rng ^= rng << 13; rng ^= rng >> 7; rng ^= rng << 17;
            return (int)(rng % (ulong)n);
        }
        UnitStore u = sim.World.Units;
        int checkedRows = 0, cueRows = 0;
        for (int t = 0; t < 1500; t++)
        {
            // Random events between ticks, applied to both twins alike.
            int roll = Next(100);
            EntityHandle target = units[Next(units.Length)];
            if (u.IsAlive(target))
            {
                if (roll < 6) { int dmg = 1 + Next(6); RegenTests.Hit(sim, target, dmg); RegenTests.Hit(twin, target, dmg); }
                else if (roll < 9) { int ticks = 2 + Next(80); float mag = 1 + Next(4); StatusSystem.Apply(sim.World, target.Index, regen, mag, ticks, 1); StatusSystem.Apply(twin.World, target.Index, regen, mag, ticks, 1); }
                else if (roll < 10) { int ticks = 20 + Next(60); StatusSystem.Apply(sim.World, target.Index, burning, 1f, ticks, 1); StatusSystem.Apply(twin.World, target.Index, burning, 1f, ticks, 1); }
            }
            var cue = new bool[units.Length];
            var before = new long[units.Length];
            var pulse = new bool[units.Length];
            for (int k = 0; k < units.Length; k++)
            {
                if (!u.IsAlive(units[k])) continue;
                int i = units[k].Index;
                cue[k] = Cue(sim, i);
                before[k] = Fixed(sim, i);
                int head = i * StatusStore.PerUnit;
                for (int e = 0; e < u.Statuses.Count[i]; e++)
                    if (D.Statuses[u.Statuses.StatusId[head + e]].Kind == StatusKind.DamageOverTime && u.Statuses.PulseTicks[head + e] == 1) pulse[k] = true;
            }
            int ran = sim.TickNumber;
            sim.Tick();
            twin.Tick();
            Assert.True(sim.StateHash() == twin.StateHash(), $"seed {seed} tick {ran}: the cue's reads moved the hash");
            for (int k = 0; k < units.Length; k++)
            {
                if (!u.IsAlive(units[k])) continue;
                int i = units[k].Index;
                // A hit or a pulse in this tick: its damage hides the gain; the cue must at least be off for a faction-only healer hit now.
                if (u.LastDamagedTick[i] == ran || pulse[k]) continue;
                long gain = Fixed(sim, i) - before[k];
                Assert.True(cue[k] == (gain > 0), $"seed {seed} tick {ran} unit {k} (owner {u.Owner[i]}): cue {cue[k]}, gained {gain}");
                checkedRows++;
                if (cue[k]) cueRows++;
            }
        }
        Assert.True(checkedRows > 5000 && cueRows > 500, $"rows {checkedRows}, cue rows {cueRows}");
    }
}
