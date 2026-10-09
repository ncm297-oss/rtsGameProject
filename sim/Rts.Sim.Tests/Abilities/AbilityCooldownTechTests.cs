using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4b-1 criterion 1: the <c>abilityCooldown</c> tech effect (Moranth Supply, Dryjhna's Prophecy) shortens the cooldown a
/// resolve starts, read at the resolve (a tech finishing mid-cooldown leaves the running one), never below 1 tick.
/// </summary>
[Collection(SerialCollection.Name)]
public class AbilityCooldownTechTests
{
    private static int Slot(EntityHandle h) => h.Index * DataLimits.MaxUnitAbilities;

    private static GameData Load(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        edit(dir);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    private static Simulation Sim(GameData data) =>
        TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 128) with { Data = data },
            LocalMovementTests.Flat(48)));

    /// <summary>Casts ability 0 of <paramref name="caster"/> at a point 4 m east of it; returns the resolve tick.</summary>
    private static int Cast(Simulation sim, int player, EntityHandle caster)
    {
        sim.Enqueue(Command.UseAbility(player, caster, 0, Off(sim.World.Units.Position[caster.Index], 4f)));
        return TickOf(sim, caster, resolved: true);
    }

    /// <summary>A Whirlwind ability for the Priest fixture: 45 s cooldown, a small Burning.</summary>
    private static void GivePriestAnAbility(TestDataDir dir)
    {
        File.WriteAllText(dir.FullPath("factions/whirlwind/abilities.json"), """
            { "abilities": [ { "id": "test_prophecy", "displayName": "Test", "description": "Test.", "kind": "targetGround",
              "range": 10, "radius": 2, "castTime": 0.5, "cooldown": 45, "affects": "enemy_units",
              "effects": [ { "kind": "applyStatus", "status": "burning", "magnitude": 1, "duration": 1 } ] } ] }
            """);
        dir.SetUnitField("whirlwind", "whirlwind_priest", "abilities", "[\"test_prophecy\"]");
    }

    /// <summary>The Sapper's Cusser: 45 s (900 ticks) from the resolve; after Moranth Supply 30 s (600).</summary>
    [Fact]
    public void MoranthSupply_SapperCusser_ReadyIn600TicksNot900()
    {
        GameData d = TestSim.Data;
        int sapper = d.FindUnit("malazan_sapper");
        Simulation sim = Sim(d);
        EntityHandle plain = Place(sim, 0, sapper, At(sim, 10, 10));
        int r0 = Cast(sim, 0, plain);
        Assert.Equal(r0 + 900, sim.World.Units.AbilityReadyTick[Slot(plain)]);

        sim = Sim(d);
        sim.World.Techs.Set(0, d.FindTech("moranth_supply"), true);
        EntityHandle supplied = Place(sim, 0, sapper, At(sim, 10, 10));
        int r1 = Cast(sim, 0, supplied);
        Assert.Equal(r1 + 600, sim.World.Units.AbilityReadyTick[Slot(supplied)]);
    }

    /// <summary>Moranth Supply finishing while a cooldown runs leaves that one at 900; the next cast gets 600.</summary>
    [Fact]
    public void ATechFinishingMidCooldown_LeavesTheRunningOne()
    {
        GameData d = TestSim.Data;
        Simulation sim = Sim(d);
        EntityHandle s = Place(sim, 0, d.FindUnit("malazan_sapper"), At(sim, 10, 10));
        int r0 = Cast(sim, 0, s);
        GatherMaps.Run(sim, 100);
        sim.World.Techs.Set(0, d.FindTech("moranth_supply"), true);
        Assert.Equal(r0 + 900, sim.World.Units.AbilityReadyTick[Slot(s)]);
        while (sim.TickNumber < r0 + 900) sim.Tick();
        int r1 = Cast(sim, 0, s);
        Assert.Equal(r1 + 600, sim.World.Units.AbilityReadyTick[Slot(s)]);
    }

    /// <summary>A -60 s Moranth Supply on a 45 s cooldown floors at 1 tick: usable again on the next tick.</summary>
    [Fact]
    public void ACooldownBonusBelowTheCooldown_FloorsAtOneTick()
    {
        GameData d = Load(dir =>
        {
            dir.EditJson("factions/malazan/techs.json", root =>
        {
            foreach (JsonNode? t in root["techs"]!.AsArray())
                if ((string)t!["id"]! == "moranth_supply")
                    foreach (JsonNode? e in t["effects"]!.AsArray())
                        if ((string)e!["stat"]! == "abilityCooldown") e["amount"] = -60;
        });
        });
        Simulation sim = Sim(d);
        sim.World.Techs.Set(0, d.FindTech("moranth_supply"), true);
        Assert.Equal(-1200f, sim.World.TechBonus(0, d.FindUnit("malazan_sapper"), TechStat.AbilityCooldown));
        EntityHandle s = Place(sim, 0, d.FindUnit("malazan_sapper"), At(sim, 10, 10));
        int r = Cast(sim, 0, s);
        Assert.Equal(r + 1, sim.World.Units.AbilityReadyTick[Slot(s)]);
    }

    /// <summary>A Priest fixture: 45 s, 30 s after Dryjhna's Prophecy; another player's tech changes nothing.</summary>
    [Fact]
    public void DryjhnasProphecy_PriestFixture_Is600Ticks_AndOnlyForItsPlayer()
    {
        GameData d = Load(GivePriestAnAbility);
        int priest = d.FindUnit("whirlwind_priest");
        Simulation sim = Sim(d);
        sim.World.Techs.Set(0, d.FindTech("dryjhnas_prophecy"), true); // player 0's: not the caster's owner
        EntityHandle p = Place(sim, 1, priest, At(sim, 10, 10));
        int r0 = Cast(sim, 1, p);
        Assert.Equal(r0 + 900, sim.World.Units.AbilityReadyTick[Slot(p)]);

        sim = Sim(d);
        sim.World.Techs.Set(1, d.FindTech("dryjhnas_prophecy"), true);
        p = Place(sim, 1, priest, At(sim, 10, 10));
        int r1 = Cast(sim, 1, p);
        Assert.Equal(r1 + 600, sim.World.Units.AbilityReadyTick[Slot(p)]);
    }
}
