using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4a criterion 2: <c>factions/&lt;id&gt;/abilities.json</c> and each unit's <c>abilities</c> list: the shipped Telas Fire
/// (docs/factions/malazan.md "Abilities"), and the loader's refusals, each an error naming the file and the field.
/// </summary>
public class AbilityLoaderTests
{
    private const string Abilities = "factions/malazan/abilities.json";
    private const string MalazanUnits = "factions/malazan/units.json";

    private static DataLoadResult LoadWith(Action<JsonObject> editTelasFire)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(Abilities, root => editTelasFire(root["abilities"]![0]!.AsObject()));
        return DataLoader.LoadAll(dir.Path);
    }

    private static void AssertErrorAt(DataLoadResult r, string file, string path, string? text = null)
    {
        Assert.False(r.Ok);
        Assert.True(r.Errors.Any(e => e.File == file && e.Path == path && (text == null || e.Message.Contains(text))),
            $"no error at {file} {path}: " + string.Join("; ", r.Errors));
    }

    private static int UnitIndex(string faction, string id)
    {
        JsonArray list = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", faction, "units.json")))!["units"]!.AsArray();
        for (int i = 0; i < list.Count; i++)
            if ((string)list[i]!["id"]! == id) return i;
        throw new InvalidOperationException(id);
    }

    [Fact]
    public void Shipped_TelasFire_MatchesTheFactionPage_AndTheCadreMageHasIt()
    {
        GameData d = TestSim.Data;
        Assert.Single(d.Abilities);
        AbilityDef a = d.Abilities[d.FindAbility("telas_fire")];
        Assert.Equal(d.FindFaction("malazan"), a.Faction);
        Assert.Equal(AbilityKind.TargetGround, a.Kind);
        Assert.Equal(16f, a.Range);
        Assert.Equal(3f, a.Radius);
        Assert.Equal(16, a.CastTicks);      // 0.8 s
        Assert.Equal(500, a.CooldownTicks); // 25 s
        Assert.Equal(0, a.DurationTicks);
        Assert.Equal(AbilityAffects.EnemyUnits, a.Affects);
        Assert.Equal("Telas Fire", a.DisplayName);
        AbilityEffect e = Assert.Single(a.Effects);
        Assert.Equal(AbilityEffectKind.ApplyStatus, e.Kind);
        Assert.Equal(d.FindStatus("burning"), e.Status);
        Assert.Equal(10f, e.Magnitude);
        Assert.Equal(80, e.DurationTicks); // 4 s
        UnitDef mage = d.Units[d.FindUnit("malazan_cadre_mage")];
        Assert.Equal(new[] { a.Id }, mage.Abilities.ToArray());
        foreach (UnitDef u in d.Units)
            if (u.Id != mage.Id) Assert.True(u.Abilities.IsEmpty, $"{u.Key} has abilities");
    }

    [Fact]
    public void AFactionWithoutAbilitiesJson_LoadsEmpty()
    {
        Assert.False(File.Exists(Path.Combine(TestDataDir.Shipped, "factions", "whirlwind", "abilities.json")));
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Delete(dir.FullPath(Abilities));
        dir.EditJson(MalazanUnits, root => root["units"]![UnitIndex("malazan", "malazan_cadre_mage")]!.AsObject().Remove("abilities"));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Empty(r.Data!.Abilities);
    }

    [Fact]
    public void UnknownStatus_IsAnErrorAtTheEffect()
    {
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["status"] = "frozen"), Abilities, "abilities[0].effects[0].status", "unknown status 'frozen'");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void ZeroOrNegativeCooldown_IsAnError(double cooldown)
    {
        AssertErrorAt(LoadWith(a => a["cooldown"] = cooldown), Abilities, "abilities[0].cooldown");
    }

    [Fact]
    public void RadiusOverTheCap_AndRangeOverMaxSight_AreErrors()
    {
        AssertErrorAt(LoadWith(a => a["radius"] = DataLimits.MaxAbilityRadius + 0.5), Abilities, "abilities[0].radius", "above the maximum");
        Assert.True(LoadWith(a => a["radius"] = DataLimits.MaxAbilityRadius).Ok);
        AssertErrorAt(LoadWith(a => a["range"] = DataLimits.MaxSight + 1), Abilities, "abilities[0].range", "above the maximum");
        Assert.True(LoadWith(a => a["range"] = DataLimits.MaxSight).Ok);
        AssertErrorAt(LoadWith(a => a["radius"] = 0), Abilities, "abilities[0].radius");
    }

    [Fact]
    public void UnknownKind_IsAnError_AndAPlannedKindIsNotSupportedYet()
    {
        AssertErrorAt(LoadWith(a => a["kind"] = "fireball"), Abilities, "abilities[0].kind", "unknown");
        AssertErrorAt(LoadWith(a => a["kind"] = "summon"), Abilities, "abilities[0].kind", "not supported yet");
        AssertErrorAt(LoadWith(a => a["kind"] = "selfAura"), Abilities, "abilities[0].kind", "not supported yet");
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["kind"] = "createZone"), Abilities, "abilities[0].effects[0].kind", "not supported yet");
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["kind"] = "heal"), Abilities, "abilities[0].effects[0].kind", "unknown");
        AssertErrorAt(LoadWith(a => a["autocast"] = true), Abilities, "abilities[0].autocast", "not supported yet");
        AssertErrorAt(LoadWith(a => a["affects"] = "enemy_buildings"), Abilities, "abilities[0].affects", "unknown");
    }

    [Fact]
    public void EffectFields_AreCheckedPerKind()
    {
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["magnitude"] = 2.5), Abilities, "abilities[0].effects[0].magnitude", "not a whole number");
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["duration"] = 0), Abilities, "abilities[0].effects[0].duration");
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["amount"] = 3), Abilities, "abilities[0].effects[0].amount", "no such field");
        AssertErrorAt(LoadWith(a => a["effects"] = new JsonArray()), Abilities, "abilities[0].effects", "no effects");
        // A slow takes a fraction below 1.
        AssertErrorAt(LoadWith(a => { a["effects"]![0]!["status"] = "slowed"; a["effects"]![0]!["magnitude"] = 1; }),
            Abilities, "abilities[0].effects[0].magnitude", "below 1");
        Assert.True(LoadWith(a => { a["effects"]![0]!["status"] = "slowed"; a["effects"]![0]!["magnitude"] = 0.3; }).Ok);
        // A damage effect: a known type, a whole amount of at least 1, no status fields.
        DataLoadResult ok = LoadWith(a => a["effects"] = new JsonArray(new JsonObject { ["kind"] = "damage", ["type"] = "siege", ["amount"] = 120 }));
        Assert.True(ok.Ok, string.Join("\n", ok.Errors));
        AbilityEffect e = ok.Data!.Abilities[0].Effects[0];
        Assert.Equal((AbilityEffectKind.Damage, 120, -1), (e.Kind, e.Amount, e.Status));
        AssertErrorAt(LoadWith(a => a["effects"] = new JsonArray(new JsonObject { ["kind"] = "damage", ["type"] = "fire", ["amount"] = 1 })),
            Abilities, "abilities[0].effects[0].type", "unknown damage type");
        AssertErrorAt(LoadWith(a => a["effects"] = new JsonArray(new JsonObject { ["kind"] = "damage", ["type"] = "siege", ["amount"] = 0 })),
            Abilities, "abilities[0].effects[0].amount");
    }

    [Fact]
    public void AForeignFactionsAbility_OnAUnit_IsAnError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        int raider = UnitIndex("whirlwind", "whirlwind_raider");
        dir.EditJson("factions/whirlwind/units.json", root => root["units"]![raider]!["abilities"] = new JsonArray("telas_fire"));
        AssertErrorAt(DataLoader.LoadAll(dir.Path), "factions/whirlwind/units.json", $"units[{raider}].abilities[0]", "belongs to faction 'malazan'");
    }

    [Fact]
    public void AUnitsList_UnknownId_Repeat_AndMoreThanFour_AreErrors()
    {
        int mage = UnitIndex("malazan", "malazan_cadre_mage");
        DataLoadResult Load(JsonArray list)
        {
            using TestDataDir dir = TestDataDir.CopyOfShipped();
            dir.EditJson(MalazanUnits, root => root["units"]![mage]!["abilities"] = list);
            return DataLoader.LoadAll(dir.Path);
        }
        AssertErrorAt(Load(new JsonArray("telas_ice")), MalazanUnits, $"units[{mage}].abilities[0]", "unknown ability");
        AssertErrorAt(Load(new JsonArray("telas_fire", "telas_fire")), MalazanUnits, $"units[{mage}].abilities[1]", "listed twice");
        AssertErrorAt(Load(new JsonArray("telas_fire", "a", "b", "c", "d")), MalazanUnits, $"units[{mage}].abilities", "above the maximum 4");
    }

    [Fact]
    public void ADuplicateIdAcrossFactions_IsAnError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.WriteAllText(dir.FullPath("factions/whirlwind/abilities.json"), File.ReadAllText(dir.FullPath(Abilities)));
        AssertErrorAt(DataLoader.LoadAll(dir.Path), "factions/whirlwind/abilities.json", "abilities[0].id", "duplicate ability id");
    }
}
