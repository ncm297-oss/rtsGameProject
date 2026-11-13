using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-4 criterion 4: the <c>statusDuration</c> tech stat (Request 49; Blood-oil Anointing's +4 s on the Frenzy, on the
/// <see cref="BloodOilScenes"/> copy). With the tech an application of a named status whose source is the tech's owner
/// lasts the amount longer; without it, not; another player's applications are unchanged; an entry already running is
/// unchanged by the research finishing; after a save and load the next application still gets it; never under one tick.
/// </summary>
public class StatusDurationTechTests
{
    /// <summary>
    /// Each player's Shaman with a Warrior beside it (within the 8 m aura), player <paramref name="p"/>'s at row 10 + 20 p.
    /// </summary>
    private static (Simulation Sim, EntityHandle[] Shamans, EntityHandle[] Warriors) Scene(GameData? data = null)
    {
        Simulation sim = data == null ? BloodOilScenes.Flat()
            : new Simulation(new SimConfig(1, 2, 128, 1056) { Data = data }, LocalMovementTests.Flat(48));
        var shamans = new EntityHandle[2];
        var warriors = new EntityHandle[2];
        for (int p = 0; p < 2; p++)
        {
            shamans[p] = BloodOilScenes.Place(sim, p, "teblor_shaman", At(sim, 10, 10 + 20 * p));
            warriors[p] = BloodOilScenes.Place(sim, p, "teblor_warrior", At(sim, 12, 10 + 20 * p));
        }
        return (sim, shamans, warriors);
    }

    /// <summary>Casts player <paramref name="p"/>'s Frenzy and runs to the tick it resolves; returns the Warrior's Frenzy ticks then (none counted down yet).</summary>
    private static int CastAndResolve(Simulation sim, int p, EntityHandle shaman, EntityHandle warrior)
    {
        int frenzy = sim.World.Data.FindStatus(BloodOilScenes.Status);
        BloodOilScenes.Cast(sim, p, shaman);
        Assert.True(RunUntil(sim, () => BloodOilScenes.TicksOf(sim.World, warrior, frenzy) > 0, 60) < 60, "the cast never resolved");
        return BloodOilScenes.TicksOf(sim.World, warrior, frenzy);
    }

    [Fact]
    public void WithTheTech_TheCastFrenzyLasts4sLonger_WithoutIt_And_ForTheOtherPlayer_Not()
    {
        Assert.Equal(200, BloodOilScenes.FrenzyTicks);
        (Simulation sim, EntityHandle[] s, EntityHandle[] wr) = Scene();
        World w = sim.World;
        Assert.Equal(200, CastAndResolve(sim, 0, s[0], wr[0])); // no tech yet
        Assert.Equal(200, BloodOilScenes.TicksOf(w, s[0], BloodOilScenes.Frenzy)); // own_units takes the caster too
        w.Techs.Set(0, BloodOilScenes.Anointing, true);
        Assert.Equal(80, w.Techs.StatusDurationTicks(0, BloodOilScenes.Frenzy));
        Assert.Equal(0, w.Techs.StatusDurationTicks(1, BloodOilScenes.Frenzy));
        Assert.Equal(0, w.Techs.StatusDurationTicks(0, BloodOilScenes.Slowed)); // not named
        Assert.Equal(0f, w.TechBonus(0, w.Units.TypeId[wr[0].Index], TechStat.StatusDuration)); // no per-unit bonus
        Assert.Equal(200, CastAndResolve(sim, 1, s[1], wr[1])); // player 1 has no Anointing
        // Player 0's Frenzy ends (its aftermath is Slowed, not named: 80), then a cast with the tech.
        Assert.True(RunUntil(sim, () => BloodOilScenes.TicksOf(w, wr[0], BloodOilScenes.Frenzy) < 0, 300) < 300);
        Assert.Equal(80, BloodOilScenes.TicksOf(w, wr[0], BloodOilScenes.Slowed));
        RunUntil(sim, () => w.Units.AbilityReadyTick[s[0].Index * DataLimits.MaxUnitAbilities] <= w.TickNumber, 1000);
        Assert.Equal(280, CastAndResolve(sim, 0, s[0], wr[0]));
    }

    [Fact]
    public void ARunningEntry_IsUnchangedByTheResearchFinishing_TheNextApplicationGetsIt()
    {
        (Simulation sim, _, EntityHandle[] wr) = Scene();
        World w = sim.World;
        StatusSystem.Apply(w, wr[0].Index, BloodOilScenes.Frenzy, 0.4f, 200, 0);
        sim.Tick();
        w.Techs.Set(0, BloodOilScenes.Anointing, true); // research completing (ProductionSystem.Researched sets the flag)
        Assert.Equal(199, BloodOilScenes.TicksOf(w, wr[0], BloodOilScenes.Frenzy));
        sim.Tick();
        Assert.Equal(198, BloodOilScenes.TicksOf(w, wr[0], BloodOilScenes.Frenzy));
        StatusSystem.Apply(w, wr[0].Index, BloodOilScenes.Frenzy, 0.4f, 200, 0); // a refresh, decided now: 280 > 198
        Assert.Equal(280, BloodOilScenes.TicksOf(w, wr[0], BloodOilScenes.Frenzy));
        // Player 1's application: its source has no tech.
        StatusSystem.Apply(w, wr[1].Index, BloodOilScenes.Frenzy, 0.4f, 200, 1);
        Assert.Equal(200, BloodOilScenes.TicksOf(w, wr[1], BloodOilScenes.Frenzy));
    }

    [Fact]
    public void AfterASaveAndLoad_MidMatch_TheNextApplicationStillGetsIt()
    {
        (Simulation sim, EntityHandle[] s, EntityHandle[] wr) = Scene();
        World w = sim.World;
        w.Techs.Set(0, BloodOilScenes.Anointing, true);
        Assert.Equal(280, CastAndResolve(sim, 0, s[0], wr[0]));
        for (int t = 0; t < 50; t++) sim.Tick();
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation loaded = SaveTestRun.Load(BloodOilScenes.Data, bytes);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        Assert.Equal(bytes, SaveTestRun.Save(loaded));
        Assert.Equal(80, loaded.World.Techs.StatusDurationTicks(0, BloodOilScenes.Frenzy)); // rebuilt from the flags
        Assert.Equal(0, loaded.World.Techs.StatusDurationTicks(1, BloodOilScenes.Frenzy));
        StatusSystem.Apply(sim.World, wr[1].Index, BloodOilScenes.Frenzy, 0.4f, 200, 0);
        StatusSystem.Apply(loaded.World, wr[1].Index, BloodOilScenes.Frenzy, 0.4f, 200, 0);
        Assert.Equal(280, BloodOilScenes.TicksOf(loaded.World, wr[1], BloodOilScenes.Frenzy));
        for (int t = 0; t < 400; t++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.True(sim.StateHash() == loaded.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
    }

    private static GameData With(Action<JsonObject> editEffect) => AiScenes.DataWith(dir =>
    {
        BloodOilScenes.Edit(dir);
        dir.EditJson("factions/teblor/techs.json", r => editEffect(r["techs"]![0]!["effects"]![BloodOilScenes.EffectIndex]!.AsObject())); // BUG-1280: not a literal 2
    });

    private static readonly Lazy<GameData> s_shortening = new(() => With(e => e["amount"] = -20));

    [Fact]
    public void AShorteningTech_NeverLeavesLessThanOneTick()
    {
        GameData d = s_shortening.Value;
        (Simulation sim, _, EntityHandle[] wr) = Scene(d);
        World w = sim.World;
        int frenzy = d.FindStatus(BloodOilScenes.Status);
        w.Techs.Set(0, d.FindTech("teblor_blood_oil_anointing"), true);
        Assert.Equal(-400, w.Techs.StatusDurationTicks(0, frenzy));
        StatusSystem.Apply(w, wr[0].Index, frenzy, 0.4f, 200, 0);
        Assert.Equal(1, BloodOilScenes.TicksOf(w, wr[0], frenzy));
        sim.Tick();
        Assert.Equal(-1, BloodOilScenes.TicksOf(w, wr[0], frenzy));
        Assert.Equal(80, BloodOilScenes.TicksOf(w, wr[0], d.FindStatus("slowed"))); // it still ended normally
    }

    private static readonly Lazy<GameData> s_slowAndZealot = new(() =>
        With(e => e["statuses"] = new JsonArray(BloodOilScenes.Status, "slowed", "frenzied")));

    /// <summary>The onEnd application has a duration: lengthened when its source has the tech. A whenBelowHp hold is not.</summary>
    [Fact]
    public void TheOnEndApplication_IsLengthened_AWhenBelowHpHold_IsNot()
    {
        GameData d = s_slowAndZealot.Value;
        (Simulation sim, _, EntityHandle[] wr) = Scene(d);
        World w = sim.World;
        w.Techs.Set(0, d.FindTech("teblor_blood_oil_anointing"), true);
        int frenzy = d.FindStatus(BloodOilScenes.Status), slowed = d.FindStatus("slowed"), frenzied = d.FindStatus("frenzied");
        StatusSystem.Apply(w, wr[0].Index, frenzy, 0.4f, 10, 0);
        Assert.Equal(90, BloodOilScenes.TicksOf(w, wr[0], frenzy));
        for (int t = 0; t < 90; t++) sim.Tick();
        Assert.Equal(160, BloodOilScenes.TicksOf(w, wr[0], slowed)); // 4 s + 4 s: the source (player 0) has the tech

        // A Zealot of player 0 below half: its passive holds Frenzied (named by the tech) for the 2-tick hold, not 82.
        EntityHandle z = BloodOilScenes.Place(sim, 0, "whirlwind_zealot", At(sim, 30, 30));
        w.Units.Hp[z.Index] = 10;
        sim.Tick();
        Assert.Equal(1, BloodOilScenes.TicksOf(w, z, frenzied));
        w.Units.Hp[z.Index] = w.MaxHp(z.Index);
        sim.Tick();
        Assert.Equal(-1, BloodOilScenes.TicksOf(w, z, frenzied));
    }
}
