using System.Reflection;
using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-3 criterion 1: <c>units.json</c> <c>attack.fromStealthBonus</c> and the fourth passive trigger <c>nearbyOwn</c> load
/// (and JSON null reads as absent); the content hash takes each field only when it is set. The refusals, one error each,
/// are <see cref="DataValidationTests"/>' rows (<c>BrokenFromStealthBonus_*</c>, <c>BrokenNearbyOwnPassive_*</c>).
/// </summary>
public class HitBonusLoaderTests
{
    private static DataLoadResult LoadWith(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        edit(dir);
        return DataLoader.LoadAll(dir.Path);
    }

    private static GameData Ok(DataLoadResult r)
    {
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    [Fact]
    public void FromStealthBonus_Loads_AndNullOrAbsentReadsAsNone()
    {
        GameData d = Ok(LoadWith(dir => dir.SetUnitField("malazan", "malazan_crossbowman", "attack.fromStealthBonus", "0.5")));
        Assert.Equal(0.5f, d.Units[d.FindUnit("malazan_crossbowman")].Attack.FromStealthBonus);
        Assert.Equal(0f, d.Units[d.FindUnit("malazan_heavy_infantry")].Attack.FromStealthBonus);
        Assert.True(d.AnyHitBonus());
        GameData atMax = Ok(LoadWith(dir => dir.SetUnitField("malazan", "malazan_crossbowman", "attack.fromStealthBonus", "4")));
        Assert.Equal((float)DataLimits.MaxFromStealthBonus, atMax.Units[atMax.FindUnit("malazan_crossbowman")].Attack.FromStealthBonus);
        GameData nulled = Ok(LoadWith(dir => dir.SetUnitField("malazan", "malazan_crossbowman", "attack.fromStealthBonus", "null")));
        Assert.Equal(0f, nulled.Units[nulled.FindUnit("malazan_crossbowman")].Attack.FromStealthBonus);
        Assert.Equal(TestSim.Data.ContentHash(), nulled.ContentHash()); // null is absent: the hash does not move
        // A building's attack with null is absent too (only a value is refused).
        GameData tower = Ok(LoadWith(dir => dir.EditJson("factions/malazan/buildings.json", r =>
        {
            foreach (JsonNode? b in r["buildings"]!.AsArray())
                if (b!["attack"] is JsonObject a) a["fromStealthBonus"] = null;
        })));
        Assert.Equal(TestSim.Data.ContentHash(), tower.ContentHash());
    }

    /// <summary>The Hound's Pack Hunter as docs/03 "For the data track (M8-3)" gives it, on the Raider (counting itself).</summary>
    private static JsonObject Pack() => new()
    {
        ["trigger"] = "nearbyOwn", ["units"] = new JsonArray("whirlwind_raider", "whirlwind_zealot"), ["radius"] = 8, ["damageBonus"] = 0.1, ["maxStacks"] = 3,
    };

    [Fact]
    public void NearbyOwn_Loads_AsTheFourthTrigger_WithItsFourFields()
    {
        GameData d = Ok(LoadWith(dir => dir.SetUnitField("whirlwind", "whirlwind_raider", "passives", new JsonArray(Pack()).ToJsonString())));
        UnitDef raider = d.Units[d.FindUnit("whirlwind_raider")];
        Assert.Equal((-1, -1, -1, 0), (raider.BelowHpPassive, raider.DeathPassive, raider.HitPassive, raider.NearbyPassive));
        UnitPassive p = raider.Passives[0];
        Assert.Equal((PassiveTrigger.NearbyOwn, 8f, 0.1f, 3), (p.Trigger, p.Radius, p.DamageBonus, p.MaxStacks));
        Assert.Equal(new[] { d.FindUnit("whirlwind_raider"), d.FindUnit("whirlwind_zealot") }, p.Units.ToArray());
        Assert.Equal((-1, 0f, 0, 0f), (p.Status, p.Magnitude, p.DurationTicks, p.HpFraction));
        Assert.Equal(PassiveTrigger.NearbyOwn, (PassiveTrigger)DataLimits.PassiveTriggerIds.IndexOf("nearbyOwn"));
        Assert.True(d.AnyHitBonus());

        // Beside the Zealot's two: all four triggers on one unit, file order, one per trigger.
        GameData z = Ok(LoadWith(dir => dir.EditJson("factions/whirlwind/units.json", r =>
        {
            foreach (JsonNode? u in r["units"]!.AsArray())
                if ((string?)u!["id"] == "whirlwind_zealot")
                {
                    JsonArray list = u["passives"]!.AsArray();
                    list.Add(new JsonObject { ["trigger"] = "onHit", ["status"] = "slowed", ["magnitude"] = 0.2, ["duration"] = 2 });
                    list.Add(Pack());
                }
        })));
        UnitDef zealot = z.Units[z.FindUnit("whirlwind_zealot")];
        Assert.Equal((0, 1, 2, 3), (zealot.BelowHpPassive, zealot.DeathPassive, zealot.HitPassive, zealot.NearbyPassive));
        Assert.Equal(DataLimits.MaxUnitPassives, zealot.Passives.Length);

        // The bounds themselves load.
        Ok(LoadWith(dir => dir.SetUnitField("whirlwind", "whirlwind_raider", "passives",
            $"[{{\"trigger\": \"nearbyOwn\", \"units\": [\"whirlwind_raider\"], \"radius\": {DataLimits.MaxPassiveRadius}, \"damageBonus\": {DataLimits.MaxPassiveDamageBonus}, \"maxStacks\": {DataLimits.MaxPassiveStacks}}}]")));
    }

    [Fact]
    public void ShippedData_CarriesNeitherField_SoNoMatchChanges()
    {
        // M8-3 adds the schema only; no shipped row uses it (the Shadow roster lands with the data track).
        GameData d = TestSim.TwoFactionData;
        Assert.False(d.AnyHitBonus());
        Assert.All(d.Units, u => Assert.True(u.NearbyPassive < 0 && u.Attack.FromStealthBonus == 0f, u.Key));
    }

    /// <summary>The hash rule: each field moves the hash only when set; an explicit null does not.</summary>
    [Fact]
    public void EachField_InTheFiles_ChangesTheHash()
    {
        ulong shipped = TestSim.Data.ContentHash();
        ulong half = Ok(LoadWith(dir => dir.SetUnitField("malazan", "malazan_crossbowman", "attack.fromStealthBonus", "0.5"))).ContentHash();
        ulong more = Ok(LoadWith(dir => dir.SetUnitField("malazan", "malazan_crossbowman", "attack.fromStealthBonus", "0.75"))).ContentHash();
        ulong pack = Ok(LoadWith(dir => dir.SetUnitField("whirlwind", "whirlwind_raider", "passives", new JsonArray(Pack()).ToJsonString()))).ContentHash();
        Assert.Equal(4, new HashSet<ulong> { shipped, half, more, pack }.Count);
    }

    /// <summary>
    /// The reflection rows for the pack's two fields (hashed only when set, so they are varied on a <c>nearbyOwn</c> entry,
    /// as M4-4b-3b's spawn fields are on a spawn effect) and for the unit's index.
    /// </summary>
    [Fact]
    public void EveryPackField_ChangesTheHash()
    {
        GameData d = Ok(LoadWith(dir => dir.SetUnitField("whirlwind", "whirlwind_raider", "passives", new JsonArray(Pack()).ToJsonString())));
        ulong baseline = d.ContentHash();
        UnitDef raider = d.Units[d.FindUnit("whirlwind_raider")];
        UnitPassive p = raider.Passives[0];
        var variants = new (string Field, UnitPassive Changed)[]
        {
            ("DamageBonus", p with { DamageBonus = p.DamageBonus + 0.1f }),
            ("MaxStacks", p with { MaxStacks = p.MaxStacks + 1 }),
            ("Radius", p with { Radius = p.Radius + 0.5f }),
            ("Units", p with { Units = p.Units.Add(raider.Id) }),
            ("Trigger", p with { Trigger = PassiveTrigger.OnHit }),
        };
        foreach ((string field, UnitPassive changed) in variants)
        {
            UnitDef copy = Clone(raider);
            typeof(UnitDef).GetProperty(nameof(UnitDef.Passives))!.SetValue(copy, raider.Passives.SetItem(0, changed));
            Assert.True(WithUnit(d, copy).ContentHash() != baseline, $"UnitPassive.{field} is not in GameData.ContentHash");
        }
        UnitDef moved = Clone(raider);
        typeof(UnitDef).GetProperty(nameof(UnitDef.NearbyPassive))!.SetValue(moved, -1);
        Assert.NotEqual(baseline, WithUnit(d, moved).ContentHash());
        // The two fields at 0 on an entry of another trigger hash as the entry did before M8-3 (the shipped data's hash holds).
        Assert.Equal(TestSim.Data.ContentHash(), WithUnit(TestSim.Data, TestSim.Data.Units[TestSim.Data.FindUnit("whirlwind_zealot")]).ContentHash());
    }

    private static T Clone<T>(T obj) where T : class =>
        (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(obj, null)!;

    private static GameData WithUnit(GameData d, UnitDef unit) => new()
    {
        AiRules = d.AiRules, Maps = d.Maps, Ai = d.Ai, Statuses = d.Statuses, Abilities = d.Abilities, DamageTable = d.DamageTable, Rules = d.Rules,
        Factions = d.Factions, Units = d.Units.SetItem(unit.Id, unit), Resources = d.Resources, Projectiles = d.Projectiles, Buildings = d.Buildings, Techs = d.Techs,
    };
}
