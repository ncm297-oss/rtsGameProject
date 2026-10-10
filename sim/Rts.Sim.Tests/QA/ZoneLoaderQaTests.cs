using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M4-4b-2, 2026-10-10-0215): the zone and blind data the developer's rows don't reach (a null zone status, a
/// non-bool <c>blocksVision</c>, a blind sight above the maximum), and data-built zones that play: a Burning zone status
/// pulses once a second while a unit stands inside (a refresh every tick never restarts the pulse clock, BUG-0301), and a
/// zone of <c>own_units</c> statuses spares enemies.
/// </summary>
[Collection(SerialCollection.Name)]
public class ZoneLoaderQaTests
{
    private const string Whirlwind = "factions/whirlwind/abilities.json";
    private const string Statuses = "common/statuses.json";

    private static DataLoadResult Load(string file, Action<JsonObject> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(file, edit);
        return DataLoader.LoadAll(dir.Path);
    }

    private static JsonObject Sandstorm(JsonObject root) => root["abilities"]![0]!.AsObject();
    private static JsonObject Zone(JsonObject root) => Sandstorm(root)["effects"]![0]!.AsObject();

    private static void AssertErrorAt(DataLoadResult r, string file, string path, string? text = null)
    {
        Assert.False(r.Ok);
        Assert.True(r.Errors.Any(e => e.File == file && e.Path == path && (text == null || e.Message.Contains(text))),
            $"no error at {file} {path}: " + string.Join("; ", r.Errors));
    }

    [Fact]
    public void ANullZoneStatus_IsAnErrorAtItsPath()
    {
        AssertErrorAt(Load(Whirlwind, r => Zone(r)["statuses"]!.AsArray().Add(null)), Whirlwind, "abilities[0].effects[0].statuses[2]");
    }

    [Fact]
    public void ANonBoolBlocksVision_IsRefused()
    {
        DataLoadResult r = Load(Whirlwind, root => Zone(root)["blocksVision"] = "yes");
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == Whirlwind);
    }

    [Fact]
    public void ABlindSightAboveTheMaximum_IsAnError()
    {
        DataLoadResult r = Load(Statuses, root =>
        {
            foreach (JsonNode? s in root["statuses"]!.AsArray())
                if ((string)s!["id"]! == "blinded") s["sight"] = DataLimits.MaxSight + 1;
        });
        AssertErrorAt(r, Statuses, "statuses[2].sight", "above the maximum");
    }

    [Fact]
    public void AZoneOnANonZoneAbilityWithoutDuration_TelasFireTurnedZone_IsAnErrorAtTheDuration()
    {
        DataLoadResult r = Load("factions/malazan/abilities.json", root =>
        {
            JsonObject telas = root["abilities"]![0]!.AsObject();
            telas.Remove("duration");
            telas["effects"]!.AsArray().Add(new JsonObject
            {
                ["kind"] = "createZone",
                ["statuses"] = new JsonArray(new JsonObject { ["status"] = "slowed", ["magnitude"] = 0.5, ["duration"] = 1 }),
            });
        });
        AssertErrorAt(r, "factions/malazan/abilities.json", "abilities[0].duration", "leaves a zone");
    }

    /// <summary>A Burning (5/s, 1 s) zone status on a unit standing inside for 60 ticks lands 3 pulses, 20 ticks apart: one a second, never restarted by the per-tick refresh.</summary>
    [Fact]
    public void ABurningZoneStatus_PulsesOnceASecond_WhileInside()
    {
        DataLoadResult r = Load(Whirlwind, root =>
            Zone(root)["statuses"]!.AsArray().Add(new JsonObject { ["status"] = "burning", ["magnitude"] = 5, ["duration"] = 1 }));
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        GameData data = r.Data!;
        var sim = TestSim.Explored(new Simulation(
            TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 64) with { Data = data }, LocalMovementTests.Flat(48)));
        UnitStore u = sim.World.Units;
        Vector2 centre = At(sim, 24, 24);
        EntityHandle victim = Place(sim, 0, data.FindUnit("malazan_heavy_infantry"), centre);
        sim.Tick();
        AbilityDef storm = data.Abilities[data.FindAbility("sandstorm")];
        ZoneSystem.Create(sim.World, 1, storm, centre);
        var pulses = new List<int>();
        int hp = u.Hp[victim.Index];
        for (int t = 0; t < 60; t++)
        {
            sim.Tick();
            if (u.Hp[victim.Index] < hp) pulses.Add(t);
            hp = u.Hp[victim.Index];
        }
        Assert.Equal(3, pulses.Count);
        Assert.Equal(20, pulses[1] - pulses[0]);
        Assert.Equal(20, pulses[2] - pulses[1]);
    }

    /// <summary>A zone of <c>own_units</c> (a data-made buff zone) slows its owner's unit inside and spares the enemy's.</summary>
    [Fact]
    public void AnOwnUnitsZone_TakesTheOwnersUnits_SparesEnemies()
    {
        DataLoadResult r = Load(Whirlwind, root => Sandstorm(root)["affects"] = "own_units");
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        GameData data = r.Data!;
        var sim = TestSim.Explored(new Simulation(
            TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 64) with { Data = data }, LocalMovementTests.Flat(48)));
        Vector2 centre = At(sim, 24, 24);
        EntityHandle own = Place(sim, 1, data.FindUnit("whirlwind_raider"), centre);
        EntityHandle enemy = Place(sim, 0, data.FindUnit("malazan_crossbowman"), Off(centre, 2f));
        sim.Tick();
        ZoneSystem.Create(sim.World, 1, data.Abilities[data.FindAbility("sandstorm")], centre);
        sim.Tick();
        Assert.Equal(data.FindStatus("blinded"), sim.World.Units.Statuses.BlindOf(own.Index));
        Assert.Equal(0, sim.World.Units.Statuses.Count[enemy.Index]);
    }
}
