using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Stress;

namespace Rts.Sim.Tests;

/// <summary>
/// BUG-1370 (S4): <c>Stress/FrenzyCastReportTests</c>' "landed" column read the ready tick at slot 0 of the Shaman's ability
/// list, so an ability the data track lists before the Frenzy would be counted as Frenzy casts. A forged Shaman with a
/// second ability (<c>zz_test_shout</c>, listed first) casts each once; only the Frenzy's cast is counted.
/// </summary>
public class FrenzyReportSlotTests
{
    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
    {
        BloodOilScenes.Edit(dir);
        dir.EditJson("factions/teblor/abilities.json", root => root["abilities"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "zz_test_shout", ["displayName"] = "Test", ["description"] = "Test only.", ["kind"] = "selfAura", ["radius"] = 0.5,
            ["castTime"] = 0, ["cooldown"] = 1, ["affects"] = "own_units", ["autocast"] = false,
            ["effects"] = new JsonArray(new JsonObject { ["kind"] = "applyStatus", ["status"] = "frenzied", ["magnitude"] = 0.5, ["duration"] = 1 }),
        }));
        dir.SetUnitField("teblor", "teblor_shaman", "abilities", $"[\"zz_test_shout\", \"{BloodOilScenes.FrenzyAbility}\"]");
    }));

    [Fact]
    public void AShamanWithAnAbilityListedBeforeTheFrenzy_CountsOnlyTheFrenzysCasts()
    {
        GameData d = s_data.Value;
        int shaman = d.FindUnit("teblor_shaman");
        Assert.Equal(1, FrenzyCastReportTests.FrenzySlot(d, shaman));
        var sim = new Simulation(new SimConfig(1, 2, 16, 64) { Data = d }, LocalMovementTests.Flat(32));
        EntityHandle s = CombatScenes.Place(sim, 0, shaman, CombatScenes.At(sim, 10, 10));
        var landed = new FrenzyCastReportTests.LandedCounter(sim.World.Units.Capacity, shaman, FrenzyCastReportTests.FrenzySlot(d, shaman));
        sim.Tick();
        Assert.Equal(1, landed.Step(sim.World, 0));

        // The shout (slot 0) lands: not a Frenzy cast.
        sim.Enqueue(Command.UseAbility(0, s, 0, sim.World.Units.Position[s.Index]));
        AbilityScenes.TickOf(sim, s, resolved: true);
        landed.Step(sim.World, 0);
        Assert.Equal(0, landed.Landed);

        // The Frenzy (slot 1) lands: one.
        sim.Enqueue(Command.UseAbility(0, s, 1, sim.World.Units.Position[s.Index]));
        AbilityScenes.TickOf(sim, s, resolved: true);
        landed.Step(sim.World, 0);
        Assert.Equal(1, landed.Landed);
    }
}
