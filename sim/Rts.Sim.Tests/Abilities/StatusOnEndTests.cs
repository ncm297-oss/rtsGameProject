using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-4 criterion 3: a status's <c>onEnd</c> (Blood-oil Frenzy's Slowed 20 % for 4 s, on the <see cref="BloodOilScenes"/>
/// copy). The named status is on the unit the tick the entry runs out and lasts exactly its duration; it does not appear
/// on death, on a refresh or on an early removal; it lands with the store full; a re-cast during the aftermath gives the
/// Frenzy back while the Slow runs out its own time and is applied again when the second Frenzy ends; with two frenzied-kind
/// rows on a unit, speed and damage each take the largest and only the row with <c>onEnd</c> slows.
/// </summary>
public class StatusOnEndTests
{
    private static (Simulation Sim, EntityHandle Unit) Scene(GameData? data = null)
    {
        Simulation sim = data == null ? BloodOilScenes.Flat()
            : new Simulation(new SimConfig(1, 2, 128, 1056) { Data = data }, LocalMovementTests.Flat(48));
        EntityHandle h = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 20, 20));
        return (sim, h);
    }

    private static void Run(Simulation sim, int ticks)
    {
        for (int t = 0; t < ticks; t++) sim.Tick();
    }

    [Fact]
    public void TheSlow_IsOnTheTickTheFrenzyEnds_AndLastsExactlyItsDuration()
    {
        (Simulation sim, EntityHandle h) = Scene();
        World w = sim.World;
        UnitStore u = w.Units;
        int frenzy = BloodOilScenes.Frenzy, slowed = BloodOilScenes.Slowed;
        Assert.Equal(80, BloodOilScenes.AftermathTicks); // 4 s
        float speed = w.Data.Units[u.TypeId[h.Index]].SpeedPerTick;
        StatusSystem.Apply(w, h.Index, frenzy, 0.4f, 30, 1); // source player 1: the aftermath keeps it
        Run(sim, 29);
        Assert.Equal(1, BloodOilScenes.TicksOf(w, h, frenzy));
        Assert.Equal(-1, BloodOilScenes.TicksOf(w, h, slowed));
        Assert.Equal(speed, u.Speed[h.Index]);
        sim.Tick(); // the Frenzy's last countdown: it ends and the Slow starts, uncounted in that pass
        Assert.Equal(-1, BloodOilScenes.TicksOf(w, h, frenzy));
        Assert.Equal(80, BloodOilScenes.TicksOf(w, h, slowed));
        int at = u.Statuses.IndexOf(h.Index, slowed);
        Assert.Equal((0.2f, 1), (u.Statuses.Magnitude[at], u.Statuses.SourcePlayer[at]));
        Assert.Equal(speed * (1f - 0.2f), u.Speed[h.Index]);
        Assert.Equal(1, u.Statuses.Count[h.Index]);
        Run(sim, 79);
        Assert.Equal(1, BloodOilScenes.TicksOf(w, h, slowed)); // on for 80 ticks' ends: 80 .. 1
        sim.Tick();
        Assert.Equal(-1, BloodOilScenes.TicksOf(w, h, slowed));
        Assert.Equal(0, u.Statuses.Count[h.Index]); // the Slow has no onEnd: nothing follows it
        Assert.Equal(speed, u.Speed[h.Index]);
    }

    [Fact]
    public void NoSlow_WhenTheUnitDies_InThePassTheFrenzyEnds()
    {
        (Simulation sim, EntityHandle h) = Scene();
        World w = sim.World;
        UnitStore u = w.Units;
        // The Frenzy ends in the 20th phase 5; Burning's first pulse (1 s) kills in the same pass, after it.
        StatusSystem.Apply(w, h.Index, BloodOilScenes.Frenzy, 0.4f, 20, 0);
        StatusSystem.Apply(w, h.Index, w.Data.FindStatus("burning"), 100_000f, 20, 1);
        Run(sim, 19);
        Assert.True(u.IsAlive(h));
        sim.Tick();
        Assert.False(u.IsAlive(h));
        Assert.Equal(0, u.Statuses.Count[h.Index]);
        for (int i = 0; i < u.Capacity; i++)
            Assert.True(!u.Alive[i] || u.Statuses.IndexOf(i, BloodOilScenes.Slowed) < 0, $"unit {i} slowed");
    }

    [Fact]
    public void NoSlow_OnARefresh_OnlyWhenTheRefreshedEntryEnds()
    {
        (Simulation sim, EntityHandle h) = Scene();
        World w = sim.World;
        StatusSystem.Apply(w, h.Index, BloodOilScenes.Frenzy, 0.4f, 30, 0);
        Run(sim, 20);
        StatusSystem.Apply(w, h.Index, BloodOilScenes.Frenzy, 0.4f, 30, 0); // refreshed to 30 at tick 20: ends at 50
        Run(sim, 29);
        Assert.Equal(1, BloodOilScenes.TicksOf(w, h, BloodOilScenes.Frenzy));
        Assert.Equal(-1, BloodOilScenes.TicksOf(w, h, BloodOilScenes.Slowed)); // the first application's end (tick 30) passed quietly
        sim.Tick();
        Assert.Equal(80, BloodOilScenes.TicksOf(w, h, BloodOilScenes.Slowed));
    }

    [Fact]
    public void NoSlow_OnAnEarlyRemoval()
    {
        (Simulation sim, EntityHandle h) = Scene();
        World w = sim.World;
        StatusSystem.Apply(w, h.Index, BloodOilScenes.Frenzy, 0.4f, 30, 0);
        Run(sim, 10);
        StatusSystem.RemoveEntry(w, h.Index, w.Units.Statuses.IndexOf(h.Index, BloodOilScenes.Frenzy));
        Run(sim, 100);
        Assert.Equal(0, w.Units.Statuses.Count[h.Index]);
    }

    private static readonly Lazy<GameData> s_fillers = new(() => AiScenes.DataWith(dir =>
    {
        BloodOilScenes.Edit(dir);
        dir.EditJson("common/statuses.json", r =>
        {
            foreach (string id in new[] { "zz_fill_a", "zz_fill_b" })
                r["statuses"]!.AsArray().Add(new JsonObject { ["id"] = id, ["displayName"] = "Filler", ["description"] = "Test only.", ["kind"] = "frenzied" });
        });
    }));

    [Fact]
    public void TheSlow_LandsWithTheStoreFull()
    {
        GameData d = s_fillers.Value;
        (Simulation sim, EntityHandle h) = Scene(d);
        World w = sim.World;
        StatusSystem.Apply(w, h.Index, d.FindStatus(BloodOilScenes.Status), 0.4f, 10, 0);
        // Seven others, none a slow: the store is full and the Frenzy is its first entry.
        StatusSystem.Apply(w, h.Index, d.FindStatus("burning"), 1f, 400, 1);
        StatusSystem.Apply(w, h.Index, d.FindStatus("blinded"), 0f, 400, 1);
        StatusSystem.Apply(w, h.Index, d.FindStatus("frenzied"), 0.5f, 400, 0);
        StatusSystem.Apply(w, h.Index, d.FindStatus("regenerating"), 1f, 400, 0);
        StatusSystem.Apply(w, h.Index, d.FindStatus("revealed"), 0f, 400, 0);
        StatusSystem.Apply(w, h.Index, d.FindStatus("zz_fill_a"), 0.1f, 400, 0);
        StatusSystem.Apply(w, h.Index, d.FindStatus("zz_fill_b"), 0.1f, 400, 0);
        Assert.Equal(StatusStore.PerUnit, w.Units.Statuses.Count[h.Index]);
        Run(sim, 10);
        Assert.Equal(StatusStore.PerUnit, w.Units.Statuses.Count[h.Index]);
        Assert.Equal(-1, BloodOilScenes.TicksOf(w, h, d.FindStatus(BloodOilScenes.Status)));
        int at = w.Units.Statuses.IndexOf(h.Index, d.FindStatus("slowed"));
        Assert.Equal(h.Index * StatusStore.PerUnit + StatusStore.PerUnit - 1, at); // appended last
        Assert.Equal(80, w.Units.Statuses.TicksRemaining[at]);
    }

    [Fact]
    public void AReCast_DuringTheAftermath_GivesTheFrenzyBack_TheSlowRunsOut_ThenComesAgain()
    {
        (Simulation sim, EntityHandle h) = Scene();
        World w = sim.World;
        UnitStore u = w.Units;
        int frenzy = BloodOilScenes.Frenzy, slowed = BloodOilScenes.Slowed;
        float speed = w.Data.Units[u.TypeId[h.Index]].SpeedPerTick;
        StatusSystem.Apply(w, h.Index, frenzy, 0.4f, 20, 0);
        Run(sim, 20);
        Assert.Equal(80, BloodOilScenes.TicksOf(w, h, slowed));
        Run(sim, 10);
        StatusSystem.Apply(w, h.Index, frenzy, 0.4f, 200, 0); // the re-cast, 10 ticks into the aftermath
        Assert.Equal((200, 70), (BloodOilScenes.TicksOf(w, h, frenzy), BloodOilScenes.TicksOf(w, h, slowed)));
        Assert.Equal(1.2f, StatusSystem.DamageFactor(w, h.Index));
        Assert.Equal(speed * 0.8f, u.Speed[h.Index]); // Frenzied and Slowed at once
        Run(sim, 70);
        Assert.Equal(-1, BloodOilScenes.TicksOf(w, h, slowed)); // its own time
        Assert.Equal(130, BloodOilScenes.TicksOf(w, h, frenzy));
        Assert.Equal(speed, u.Speed[h.Index]);
        Run(sim, 130);
        Assert.Equal(-1, BloodOilScenes.TicksOf(w, h, frenzy));
        Assert.Equal(80, BloodOilScenes.TicksOf(w, h, slowed)); // the second end slows again
        Assert.Equal(speed * 0.8f, u.Speed[h.Index]);
    }

    /// <summary>A re-cast that ends while the first Slow still runs: the stacking rule refreshes it to the full 80 (longer wins).</summary>
    [Fact]
    public void ASecondEnd_DuringTheSlow_RefreshesItByTheStackingRule()
    {
        (Simulation sim, EntityHandle h) = Scene();
        World w = sim.World;
        StatusSystem.Apply(w, h.Index, BloodOilScenes.Frenzy, 0.4f, 20, 0);
        Run(sim, 20);
        StatusSystem.Apply(w, h.Index, BloodOilScenes.Frenzy, 0.4f, 30, 1);
        Run(sim, 30);
        Assert.Equal(80, BloodOilScenes.TicksOf(w, h, BloodOilScenes.Slowed)); // 50 left, refreshed to 80
        Assert.Equal(1, w.Units.Statuses.SourcePlayer[w.Units.Statuses.IndexOf(h.Index, BloodOilScenes.Slowed)]); // equal strength: the latest source
        Assert.Equal(1, w.Units.Statuses.Count[h.Index]);
    }

    [Fact]
    public void TwoFrenziedRows_SpeedAndDamageTakeTheLargest_OnlyTheOnEndRowSlows()
    {
        (Simulation sim, EntityHandle h) = Scene();
        World w = sim.World;
        int cooldown = w.Data.Units[w.Units.TypeId[h.Index]].Attack.CooldownTicks; // 36 (1.8 s)
        StatusSystem.Apply(w, h.Index, BloodOilScenes.PlainFrenzied, 0.5f, 30, 0);
        StatusSystem.Apply(w, h.Index, BloodOilScenes.Frenzy, 0.4f, 60, 0);
        Assert.Equal(2, w.Units.Statuses.Count[h.Index]);
        Assert.Equal(24, StatusSystem.AttackCooldown(w, h.Index, cooldown)); // 36 / 1.5: the Zealot's +50 % is the larger
        Assert.Equal(1.2f, StatusSystem.DamageFactor(w, h.Index)); // only the Frenzy row has a bonus
        Run(sim, 30);
        Assert.Equal(-1, BloodOilScenes.TicksOf(w, h, BloodOilScenes.PlainFrenzied));
        Assert.Equal(-1, BloodOilScenes.TicksOf(w, h, BloodOilScenes.Slowed)); // the plain row has no onEnd
        Assert.Equal(26, StatusSystem.AttackCooldown(w, h.Index, cooldown)); // 36 / 1.4 = 25.7
        Run(sim, 30);
        Assert.Equal(80, BloodOilScenes.TicksOf(w, h, BloodOilScenes.Slowed));
        Assert.Equal(cooldown, StatusSystem.AttackCooldown(w, h.Index, cooldown));
        Assert.Equal(1f, StatusSystem.DamageFactor(w, h.Index));
    }
}
