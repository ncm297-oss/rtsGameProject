using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-1 criteria 4-6 and 8: regeneration (<see cref="RegenSystem"/>) on test copies of the shipped data whose Malazan
/// <c>faction.json</c> gains a <c>regen</c> (no shipped faction has one). Hits come through <see cref="CombatSystem.HitUnit"/>
/// between ticks, so the hit's tick (<see cref="UnitStore.LastDamagedTick"/>) is the next tick to run, t; "through tick k"
/// means after tick k has run (<c>sim.TickNumber == k + 1</c>).
/// </summary>
[Collection(SerialCollection.Name)]
public class RegenTests
{
    /// <summary>Malazan regenerates 1 HP/s after 6 s; Moranth Supply also gives Heavy Infantry +20 hp (for the tech ceiling row).</summary>
    internal static readonly Lazy<GameData> s_regen = new(() => RegenData(1, 6, hpTech: true));

    /// <summary>Malazan regenerates 1.5 HP/s after 6 s.</summary>
    internal static readonly Lazy<GameData> s_regen15 = new(() => RegenData(1.5, 6, hpTech: false));

    internal static GameData RegenData(double hpPerSecond, double delay, bool hpTech) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/faction.json", r => r["regen"] = new JsonObject { ["hpPerSecond"] = hpPerSecond, ["delay"] = delay });
        if (hpTech)
            dir.EditJson("factions/malazan/techs.json", r => r["techs"]![0]!["effects"]!.AsArray().Add(new JsonObject
            {
                ["stat"] = "hp", ["amount"] = 20, ["appliesTo"] = new JsonObject { ["units"] = new JsonArray("malazan_heavy_infantry") },
            }));
    });

    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    private static int Regenerating(GameData d) => d.FindStatus("regenerating");

    /// <summary>A two-player flat sim (player 0 Malazan, player 1 Whirlwind) on <paramref name="data"/>.</summary>
    internal static Simulation Scene(GameData data, int units = 64) =>
        new(new SimConfig(1, 2, units, 8 * units + 32) { Data = data }, LocalMovementTests.Flat(48));

    /// <summary>A Malazan Heavy Infantry (130 hp) of player 0, alone in the middle of the map.</summary>
    private static EntityHandle Hi(Simulation sim, int x = 20, int y = 20) => CombatScenes.Place(sim, 0, Heavy, CombatScenes.At(sim, x, y));

    /// <summary>A hit of <paramref name="damage"/> from player 1 between ticks: its tick is the next one to run.</summary>
    internal static void Hit(Simulation sim, EntityHandle unit, int damage) =>
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, unit, false, damage));

    /// <summary>Runs ticks until tick <paramref name="k"/> has run.</summary>
    private static void Through(Simulation sim, int k)
    {
        while (sim.TickNumber <= k) sim.Tick();
    }

    [Fact]
    public void AHeavyInfantryHitTo100_WaitsTheDelay_ThenGains1HpEvery20Ticks_To130Exactly_AndStops()
    {
        Simulation sim = Scene(s_regen.Value);
        UnitStore u = sim.World.Units;
        EntityHandle h = Hi(sim);
        sim.Tick();
        int t = sim.TickNumber;
        Hit(sim, h, 30);
        Assert.Equal((100, t), (u.Hp[h.Index], u.LastDamagedTick[h.Index]));
        Assert.Equal(20, sim.World.Data.Factions[0].RegenPerTick);
        Assert.Equal(120, sim.World.Data.Factions[0].RegenDelayTicks);
        for (int k = t; k <= t + 760; k++)
        {
            Through(sim, k);
            // From tick t + 120 on, 20 units a tick at 400 a hit point: one HP every 20 ticks.
            int expected = k < t + 120 ? 100 : Math.Min(130, 100 + (k - t - 119) / 20);
            Assert.True(expected == u.Hp[h.Index], $"tick {k}: hp {u.Hp[h.Index]}, want {expected}");
            Assert.True(u.Hp[h.Index] <= sim.World.MaxHp(h.Index));
        }
        Assert.Equal(100, HpAfter(t + 119));
        Assert.Equal(101, HpAfter(t + 139));
        Assert.Equal(130, HpAfter(t + 719));
        Assert.Equal(129, HpAfter(t + 718));
        Assert.Equal(0, u.RegenAccum[h.Index]);
        return;

        int HpAfter(int k) => k < t + 120 ? 100 : Math.Min(130, 100 + (k - t - 119) / 20);
    }

    [Fact]
    public void ASecondHit_InsideTheDelay_RestartsIt()
    {
        Simulation sim = Scene(s_regen.Value);
        UnitStore u = sim.World.Units;
        EntityHandle h = Hi(sim);
        sim.Tick();
        int t = sim.TickNumber;
        Hit(sim, h, 30);
        Through(sim, t + 99);
        Assert.Equal(100, u.Hp[h.Index]);
        Hit(sim, h, 10); // its tick: t + 100
        Assert.Equal(t + 100, u.LastDamagedTick[h.Index]);
        Through(sim, t + 219);
        Assert.Equal(90, u.Hp[h.Index]);
        Through(sim, t + 238);
        Assert.Equal(90, u.Hp[h.Index]);
        Through(sim, t + 239);
        Assert.Equal(91, u.Hp[h.Index]);
    }

    [Fact]
    public void AUnitAtItsMaximum_NeverChanges_AndHashesAsWithoutRegen()
    {
        Simulation sim = Scene(s_regen.Value), plain = Scene(TestSim.Data);
        EntityHandle h = Hi(sim);
        Hi(plain);
        for (int k = 0; k < 400; k++)
        {
            sim.Tick();
            plain.Tick();
            Assert.Equal(130, sim.World.Units.Hp[h.Index]);
            Assert.Equal((0, -1), (sim.World.Units.RegenAccum[h.Index], sim.World.Units.LastDamagedTick[h.Index]));
            Assert.Equal(plain.StateHash(), sim.StateHash());
        }
    }

    [Fact]
    public void OneAndAHalfHpPerSecond_Heals3HpEvery2Seconds_WithNoDriftOverAMinute()
    {
        Simulation sim = Scene(s_regen15.Value);
        UnitStore u = sim.World.Units;
        EntityHandle h = Hi(sim);
        sim.Tick();
        int t = sim.TickNumber;
        Hit(sim, h, 100); // 30 / 130
        Assert.Equal(30, sim.World.Data.Factions[0].RegenPerTick);
        int start = t + 120;
        Through(sim, start - 1);
        Assert.Equal(30, u.Hp[h.Index]);
        for (int n = 1; n <= 1200; n++) // a minute
        {
            Through(sim, start + n - 1);
            Assert.Equal(30 + 30 * n / 400, u.Hp[h.Index]); // the half carried by the accumulator
            if (n % 40 == 0) Assert.Equal((30 + 3 * (n / 40), 0), (u.Hp[h.Index], u.RegenAccum[h.Index]));
        }
        Assert.Equal(120, u.Hp[h.Index]);
    }

    [Fact]
    public void AnHpTech_ResearchedMidRegen_RaisesTheCeiling_AndTheUnitHealsToIt()
    {
        GameData d = s_regen.Value;
        Simulation sim = Scene(d);
        UnitStore u = sim.World.Units;
        EntityHandle h = Hi(sim);
        sim.Tick();
        int t = sim.TickNumber;
        Hit(sim, h, 50); // 80 / 130
        Through(sim, t + 300);
        Assert.Equal(89, u.Hp[h.Index]); // (300 - 119) / 20 = 9
        int tech = d.FindTech("moranth_supply");
        ProductionSystem.Researched(sim.World, 0, tech); // as phase 3 would: max and current move together
        Assert.Equal((150, 109), (sim.World.MaxHp(h.Index), u.Hp[h.Index]));
        int peak = 0;
        for (int k = t + 301; k <= t + 1200; k++)
        {
            Through(sim, k);
            peak = Math.Max(peak, u.Hp[h.Index]);
            Assert.True(u.Hp[h.Index] <= 150);
        }
        Assert.Equal((150, 150, 0), (peak, u.Hp[h.Index], u.RegenAccum[h.Index]));
    }

    /// <summary>
    /// Criterion 5: a regenerating status (magnitude 2 HP/s, 5 s = 100 ticks, applied through <see cref="StatusSystem.Apply"/>)
    /// heals through the faction's delay at 40 units a tick, one HP every 10 ticks. Its entry acts in the ticks it is still on
    /// the unit after phase 5's countdown, as a slow does: 99 of its 100 (the countdown ends it before regeneration reads it on
    /// its last tick). Then nothing until the faction's delay is over, the fraction it left carried.
    /// </summary>
    [Fact]
    public void ARegeneratingStatus_HealsThroughTheDelay_AndStopsWhenItExpires()
    {
        GameData d = s_regen.Value;
        Simulation sim = Scene(d);
        UnitStore u = sim.World.Units;
        EntityHandle h = Hi(sim);
        sim.Tick();
        int t = sim.TickNumber;
        Hit(sim, h, 30);
        StatusSystem.Apply(sim.World, h.Index, Regenerating(d), 2f, 100, 0);
        Assert.Equal(1, u.Statuses.RegeneratingUnits);
        Through(sim, t + 9);
        Assert.Equal(101, u.Hp[h.Index]);
        Through(sim, t + 49);
        Assert.Equal(105, u.Hp[h.Index]);
        Through(sim, t + 98);
        Assert.Equal((109, 360), (u.Hp[h.Index], u.RegenAccum[h.Index]));
        Through(sim, t + 99);
        Assert.Equal(0, u.Statuses.Count[h.Index]);
        Assert.Equal(0, u.Statuses.RegeneratingUnits);
        Through(sim, t + 119);
        Assert.Equal((109, 360), (u.Hp[h.Index], u.RegenAccum[h.Index])); // expired, the faction still waiting
        Through(sim, t + 120);
        Assert.Equal((109, 380), (u.Hp[h.Index], u.RegenAccum[h.Index]));
        Through(sim, t + 121);
        Assert.Equal((110, 0), (u.Hp[h.Index], u.RegenAccum[h.Index]));
    }

    [Fact]
    public void ARegeneratingStatus_StacksWithTheFactionRate_WhileItLasts()
    {
        GameData d = s_regen.Value;
        Simulation sim = Scene(d);
        UnitStore u = sim.World.Units;
        EntityHandle h = Hi(sim);
        sim.Tick();
        int t = sim.TickNumber;
        Hit(sim, h, 30);
        Through(sim, t + 139);
        Assert.Equal((101, 0), (u.Hp[h.Index], u.RegenAccum[h.Index]));
        StatusSystem.Apply(sim.World, h.Index, Regenerating(d), 2f, 100, 0);
        Through(sim, t + 159);
        Assert.Equal(104, u.Hp[h.Index]); // 20 ticks at 20 + 40 units: 3 HP a second
        Through(sim, t + 238);
        Assert.Equal((115, 340), (u.Hp[h.Index], u.RegenAccum[h.Index])); // 99 ticks x 60
        Through(sim, t + 240);
        Assert.Equal((115, 380), (u.Hp[h.Index], u.RegenAccum[h.Index])); // the status gone: 20 a tick again
        Through(sim, t + 241);
        Assert.Equal(116, u.Hp[h.Index]);
    }

    /// <summary>A Whirlwind unit (its faction has no regen) heals only while it carries the status; with neither, the pass is skipped.</summary>
    [Fact]
    public void AUnitOfAFactionWithoutRegen_HealsOnlyFromTheStatus()
    {
        GameData d = s_regen.Value;
        Simulation sim = Scene(d);
        UnitStore u = sim.World.Units;
        int raider = d.FindUnit("whirlwind_raider");
        EntityHandle h = CombatScenes.Place(sim, 1, raider, CombatScenes.At(sim, 30, 30));
        int max = sim.World.MaxHp(h.Index);
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 0, h, false, 20));
        Assert.Equal(-1, u.LastDamagedTick[h.Index]); // recorded only under a faction that regenerates
        for (int k = 0; k < 300; k++) sim.Tick();
        Assert.Equal(max - 20, u.Hp[h.Index]);
        StatusSystem.Apply(sim.World, h.Index, Regenerating(d), 2f, 21, 1);
        for (int k = 0; k < 40; k++) sim.Tick();
        Assert.Equal(max - 18, u.Hp[h.Index]); // 20 ticks x 40 units = 2 HP
        Assert.Equal(0, u.RegenAccum[h.Index]);
    }

    /// <summary>Shipped data: no faction regenerates and no unit carries the status, so the pass returns at once.</summary>
    [Fact]
    public void ShippedData_NoFactionRegenerates()
    {
        Assert.All(TestSim.Data.Factions, f => Assert.Equal((0, 0), (f.RegenPerTick, f.RegenDelayTicks)));
        Simulation sim = Scene(TestSim.Data);
        Assert.False(sim.World.AnyFactionRegen);
        Assert.Equal(0, sim.World.Units.Statuses.RegeneratingUnits);
    }

    /// <summary>
    /// Criterion 6: a scripted 3,000-tick 30 v 30 brawl on the regen copy (Malazan survivors regenerate after the fight; a
    /// Whirlwind unit gets a regenerating status at tick 200) runs twice to the same hash every tick, and it did heal.
    /// </summary>
    [Fact]
    public void TwoRunsOfAScriptedMatch_WithRegen_HashTheSameEveryTick()
    {
        ulong[] first = RegenMatch(out int healed1), second = RegenMatch(out int healed2);
        Assert.Equal(first, second);
        Assert.Equal(healed1, healed2);
        Assert.True(healed1 > 0, "no unit healed");
    }

    private static ulong[] RegenMatch(out int healed)
    {
        GameData d = s_regen.Value;
        var sim = new Simulation(new SimConfig(7, 2, 128, 1056) { Data = d }, LocalMovementTests.Flat(64));
        CombatScenes.FlatBrawl(sim, 30, new Vector2(64f, 64f), gap: 6f, ranks: 10);
        var hashes = new ulong[3000];
        healed = 0;
        UnitStore u = sim.World.Units;
        var before = new int[u.Capacity];
        for (int k = 0; k < hashes.Length; k++)
        {
            if (sim.TickNumber == 200)
                for (int i = 0; i < u.Capacity; i++)
                    if (u.Alive[i] && u.Owner[i] == 1)
                    {
                        StatusSystem.Apply(sim.World, i, Regenerating(d), 3f, 400, 1);
                        break;
                    }
            Array.Copy(u.Hp, before, u.Capacity);
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.Hp[i] > before[i]) healed++;
            hashes[k] = sim.StateHash();
        }
        return hashes;
    }

    /// <summary>Criterion 8: ticks with faction regeneration and regenerating statuses healing allocate nothing.</summary>
    [Fact]
    public void RegenTicks_WithTheFactionRateAndStatuses_AllocateNothing()
    {
        GameData d = s_regen15.Value;
        Simulation sim = Scene(d, units: 128);
        UnitStore u = sim.World.Units;
        var list = new List<EntityHandle>();
        for (int k = 0; k < 40; k++) list.Add(Hi(sim, 6 + k % 20 * 2, 10 + k / 20 * 4));
        sim.Tick();
        foreach (EntityHandle h in list) Hit(sim, h, 60);
        for (int k = 0; k < 20; k += 2) StatusSystem.Apply(sim.World, list[k].Index, Regenerating(d), 2f, 2000, 0);
        for (int k = 0; k < 130; k++) sim.Tick(); // past the delay, so both parts heal
        int hp = 0;
        foreach (EntityHandle h in list) hp += u.Hp[h.Index];
        Action ticks = () =>
        {
            for (int k = 0; k < 40; k++)
            {
                sim.Tick();
                sim.StateHash();
            }
        };
        AllocationProbe.AssertZero(ticks);
        int after = 0;
        foreach (EntityHandle h in list) after += u.Hp[h.Index];
        Assert.True(after > hp, "nothing healed inside the measured ticks");
    }
}
