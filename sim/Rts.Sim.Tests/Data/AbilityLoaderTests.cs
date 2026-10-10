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
    private const string Whirlwind = "factions/whirlwind/abilities.json";

    /// <summary>M4-4b-2: loads the shipped data with Sandstorm (the Whirlwind file's entry 0) edited.</summary>
    private static DataLoadResult LoadWithSandstorm(Action<JsonObject> editSandstorm)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(Whirlwind, root => editSandstorm(root["abilities"]![0]!.AsObject()));
        return DataLoader.LoadAll(dir.Path);
    }

    /// <summary>Sandstorm's zone effect (its effect 0) in an edit.</summary>
    private static JsonObject Zone(JsonObject sandstorm) => sandstorm["effects"]![0]!.AsObject();

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
        Assert.Equal(3, d.Abilities.Length); // telas_fire, cusser (M4-4b-1), sandstorm (M4-4b-2)
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
        int sapper = d.FindUnit("malazan_sapper"), priest = d.FindUnit("whirlwind_priest");
        foreach (UnitDef u in d.Units)
            if (u.Id != mage.Id && u.Id != sapper && u.Id != priest) Assert.True(u.Abilities.IsEmpty, $"{u.Key} has abilities");
    }

    [Fact]
    public void AFactionWithoutAbilitiesJson_LoadsEmpty()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Delete(dir.FullPath(Abilities));
        File.Delete(dir.FullPath(Whirlwind)); // M4-4b-2: both factions ship one now
        dir.EditJson("factions/whirlwind/units.json", root => root["units"]![UnitIndex("whirlwind", "whirlwind_priest")]!.AsObject().Remove("abilities"));
        dir.EditJson(MalazanUnits, root => root["units"]![UnitIndex("malazan", "malazan_cadre_mage")]!.AsObject().Remove("abilities"));
        dir.EditJson(MalazanUnits, root => root["units"]![UnitIndex("malazan", "malazan_sapper")]!.AsObject().Remove("abilities"));
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
        // M4-4b-3b: summon and spawn are supported; targetUnit, teleport and autocast stay deferred.
        AssertErrorAt(LoadWith(a => a["kind"] = "targetUnit"), Abilities, "abilities[0].kind", "not supported yet");
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["kind"] = "teleport"), Abilities, "abilities[0].effects[0].kind", "not supported yet");
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["kind"] = "heal"), Abilities, "abilities[0].effects[0].kind", "unknown");
        AssertErrorAt(LoadWith(a => a["autocast"] = true), Abilities, "abilities[0].autocast", "not supported yet");
        AssertErrorAt(LoadWith(a => a["affects"] = "enemy_buildings"), Abilities, "abilities[0].affects", "unknown");
    }

    /// <summary>
    /// M4-4b-3a criterion 1: a <c>selfAura</c> ability loads (no longer "not supported yet") with no range; a <c>range</c> on it,
    /// 0 included, is an error at its path.
    /// </summary>
    [Fact]
    public void SelfAura_Loads_WithNoRange_AndARangeOnItIsAnError()
    {
        DataLoadResult ok = LoadWith(a => { a["kind"] = "selfAura"; a.Remove("range"); });
        Assert.True(ok.Ok, string.Join("\n", ok.Errors));
        AbilityDef aura = ok.Data!.Abilities[ok.Data.FindAbility("telas_fire")];
        Assert.Equal((AbilityKind.SelfAura, 0f, 3f), (aura.Kind, aura.Range, aura.Radius));
        Assert.DoesNotContain("selfAura", DataLimits.PlannedAbilityKindIds);
        Assert.Equal(1, DataLimits.AbilityKindIds.IndexOf("selfAura"));
        Assert.Equal(1, (int)AbilityKind.SelfAura);
        foreach (double range in new[] { 16.0, 0.0 })
            AssertErrorAt(LoadWith(a => { a["kind"] = "selfAura"; a["range"] = range; }), Abilities, "abilities[0].range", "selfAura ability has no range");
        // A target-ground ability still needs one.
        AssertErrorAt(LoadWith(a => a.Remove("range")), Abilities, "abilities[0].range", "missing");
        // Every existing effect loads in it: a zone and a damage effect too.
        DataLoadResult zone = LoadWithSandstorm(a => { a["kind"] = "selfAura"; a.Remove("range"); });
        Assert.True(zone.Ok, string.Join("\n", zone.Errors));
        DataLoadResult damage = LoadWith(a =>
        {
            a["kind"] = "selfAura";
            a.Remove("range");
            a["effects"] = new JsonArray(new JsonObject { ["kind"] = "damage", ["type"] = "magic", ["amount"] = 10 });
        });
        Assert.True(damage.Ok, string.Join("\n", damage.Errors));
    }

    /// <summary>Telas Fire made a summon of <paramref name="count"/> Heavy Infantry for <paramref name="lifetime"/> s, with no range and no radius.</summary>
    private static void Summon(JsonObject a, object? count = null, object? lifetime = null, string unit = "malazan_heavy_infantry")
    {
        a["kind"] = "summon";
        a.Remove("range");
        a.Remove("radius");
        a["effects"] = new JsonArray(Spawn(unit, count ?? 3, lifetime ?? 20));
    }

    private static JsonObject Spawn(string unit, object count, object lifetime)
    {
        var o = new JsonObject { ["kind"] = "spawn", ["unit"] = unit };
        if (count is not "absent") o["count"] = JsonValue.Create(count);
        if (lifetime is not "absent") o["lifetime"] = JsonValue.Create(lifetime);
        return o;
    }

    /// <summary>
    /// M4-4b-3b criterion 3: a <c>summon</c> ability with a <c>spawn</c> effect loads (neither is "not supported yet" now): no
    /// range, the radius optional (0 when absent), the unit, count and lifetime converted.
    /// </summary>
    [Fact]
    public void Summon_WithASpawnEffect_Loads()
    {
        DataLoadResult ok = LoadWith(a => Summon(a));
        Assert.True(ok.Ok, string.Join("\n", ok.Errors));
        GameData d = ok.Data!;
        AbilityDef s = d.Abilities[d.FindAbility("telas_fire")];
        Assert.Equal((AbilityKind.Summon, 0f, 0f), (s.Kind, s.Range, s.Radius));
        AbilityEffect e = Assert.Single(s.Effects);
        Assert.Equal((AbilityEffectKind.Spawn, d.FindUnit("malazan_heavy_infantry"), 3, 400), (e.Kind, e.Unit, e.Count, e.LifetimeTicks));
        Assert.Equal((-1, -1), (e.Status, e.DamageType));
        Assert.DoesNotContain("summon", DataLimits.PlannedAbilityKindIds);
        Assert.DoesNotContain("spawn", DataLimits.PlannedAbilityEffectKindIds);
        Assert.Equal(2, DataLimits.AbilityKindIds.IndexOf("summon"));
        Assert.Equal(3, DataLimits.AbilityEffectKindIds.IndexOf("spawn"));
        Assert.Equal((2, 3), ((int)AbilityKind.Summon, (int)AbilityEffectKind.Spawn));
        // Every other effect keeps Unit -1 and no count or lifetime.
        foreach (AbilityDef a in TestSim.Data.Abilities)
            foreach (AbilityEffect x in a.Effects) Assert.Equal((-1, 0, 0), (x.Unit, x.Count, x.LifetimeTicks));
        // A radius is allowed (the summon's other effects land within it), and the count's ends load.
        Assert.True(LoadWith(a => { Summon(a); a["radius"] = 2; }).Ok);
        Assert.True(LoadWith(a => Summon(a, count: 1, lifetime: 0.05)).Ok);
        Assert.True(LoadWith(a => Summon(a, count: DataLimits.MaxSummonCount)).Ok);
        // Two spawn effects, and a spawn beside an applyStatus (which then needs the radius).
        Assert.True(LoadWith(a => { Summon(a); a["effects"]!.AsArray().Add(Spawn("malazan_laborer", 1, 5)); }).Ok);
        DataLoadResult mixed = LoadWith(a =>
        {
            Summon(a);
            a["radius"] = 4;
            a["effects"]!.AsArray().Add(new JsonObject { ["kind"] = "applyStatus", ["status"] = "slowed", ["magnitude"] = 0.3, ["duration"] = 1 });
        });
        Assert.True(mixed.Ok, string.Join("\n", mixed.Errors));
    }

    /// <summary>M4-4b-3b criterion 3: a summon takes no range (0 included); it needs a spawn effect; its other effects need a radius.</summary>
    [Fact]
    public void Summon_RangeIsAnError_ASpawnIsRequired_AndOtherEffectsNeedARadius()
    {
        foreach (double range in new[] { 16.0, 0.0 })
            AssertErrorAt(LoadWith(a => { Summon(a); a["range"] = range; }), Abilities, "abilities[0].range", "summon ability has no range");
        AssertErrorAt(LoadWith(a => { a["kind"] = "summon"; a.Remove("range"); }), Abilities, "abilities[0].effects", "needs at least one 'spawn' effect");
        AssertErrorAt(LoadWith(a =>
        {
            Summon(a);
            a["effects"]!.AsArray().Add(new JsonObject { ["kind"] = "applyStatus", ["status"] = "slowed", ["magnitude"] = 0.3, ["duration"] = 1 });
        }), Abilities, "abilities[0].radius", "missing");
    }

    /// <summary>M4-4b-3b criterion 3: a spawn effect on a targetGround or a selfAura ability is an error at its kind.</summary>
    [Fact]
    public void ASpawnEffect_OnlyOnASummon()
    {
        AssertErrorAt(LoadWith(a => a["effects"] = new JsonArray(Spawn("malazan_heavy_infantry", 3, 20))),
            Abilities, "abilities[0].effects[0].kind", "belongs on a summon ability");
        AssertErrorAt(LoadWith(a => { a["kind"] = "selfAura"; a.Remove("range"); a["effects"] = new JsonArray(Spawn("malazan_heavy_infantry", 3, 20)); }),
            Abilities, "abilities[0].effects[0].kind", "belongs on a summon ability");
    }

    /// <summary>M4-4b-3b criterion 3: each spawn field's refusals, each at its path.</summary>
    [Fact]
    public void SpawnFields_AreChecked_EachAtItsPath()
    {
        const string P = "abilities[0].effects[0]";
        AssertErrorAt(LoadWith(a => Summon(a, unit: "whirlwind_raider")), Abilities, P + ".unit", "belongs to faction 'whirlwind'");
        AssertErrorAt(LoadWith(a => Summon(a, unit: "malazan_wraith")), Abilities, P + ".unit", "unknown unit");
        AssertErrorAt(LoadWith(a => { Summon(a); a["effects"]![0]!.AsObject().Remove("unit"); }), Abilities, P + ".unit", "missing");
        foreach (object count in new object[] { 0, 9, -1 })
            AssertErrorAt(LoadWith(a => Summon(a, count: count)), Abilities, P + ".count", "outside 1-8");
        AssertErrorAt(LoadWith(a => Summon(a, count: 2.5)), Abilities, P + ".count", "not a whole number");
        AssertErrorAt(LoadWith(a => Summon(a, count: "absent")), Abilities, P + ".count", "missing");
        AssertErrorAt(LoadWith(a => Summon(a, lifetime: 0)), Abilities, P + ".lifetime", "must be positive");
        AssertErrorAt(LoadWith(a => Summon(a, lifetime: "absent")), Abilities, P + ".lifetime", "missing");
        AssertErrorAt(LoadWith(a => Summon(a, lifetime: 0.01)), Abilities, P + ".lifetime", "rounds to 0 ticks");
        // The other effects' fields are errors on a spawn, and the spawn fields on the other effects.
        foreach ((string field, JsonNode value) in new (string, JsonNode)[] { ("amount", 3), ("type", "magic"), ("status", "slowed"), ("magnitude", 0.3), ("duration", 1), ("buildings", true), ("friendlyFire", 0.5), ("blocksVision", true) })
            AssertErrorAt(LoadWith(a => { Summon(a); a["effects"]![0]![field] = value.DeepClone(); }), Abilities, $"{P}.{field}", "no such field");
        foreach ((string field, JsonNode value) in new (string, JsonNode)[] { ("unit", "malazan_heavy_infantry"), ("count", 3), ("lifetime", 20) })
            AssertErrorAt(LoadWith(a => a["effects"]![0]![field] = value.DeepClone()), Abilities, $"{P}.{field}", "no such field");
    }

    /// <summary>M4-4a criterion 1: an <c>applyStatus</c> of a frenzied status needs a magnitude above 0 (any fraction: the attack-speed increase).</summary>
    [Fact]
    public void AFrenziedEffect_NeedsAMagnitudeAbove0()
    {
        foreach (double m in new[] { 0.5, 0.2, 2.0 })
        {
            DataLoadResult ok = LoadWith(a => { a["effects"]![0]!["status"] = "frenzied"; a["effects"]![0]!["magnitude"] = m; });
            Assert.True(ok.Ok, string.Join("\n", ok.Errors));
            Assert.Equal((float)m, ok.Data!.Abilities[ok.Data.FindAbility("telas_fire")].Effects[0].Magnitude);
        }
        AssertErrorAt(LoadWith(a => { a["effects"]![0]!["status"] = "frenzied"; a["effects"]![0]!["magnitude"] = 0; }),
            Abilities, "abilities[0].effects[0].magnitude", "must be positive");
        AssertErrorAt(LoadWith(a => { a["effects"]![0]!["status"] = "frenzied"; a["effects"]![0]!.AsObject().Remove("magnitude"); }),
            Abilities, "abilities[0].effects[0].magnitude", "missing");
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

    /// <summary>M4-4b-1: a damage-over-time duration is a whole number of seconds, at least 1; a slow's isn't held to it.</summary>
    [Theory]
    [InlineData(2.5, false)]
    [InlineData(0.5, false)]
    [InlineData(0.0, false)]
    [InlineData(1.0, true)]
    [InlineData(4.0, true)]
    public void ADamageOverTimeDuration_MustBeWholeSecondsAtLeastOne(double seconds, bool ok)
    {
        DataLoadResult r = LoadWith(a => a["effects"]![0]!["duration"] = seconds);
        if (ok)
        {
            Assert.True(r.Ok, string.Join("\n", r.Errors));
            Assert.Equal((int)(seconds * 20), r.Data!.Abilities[r.Data.FindAbility("telas_fire")].Effects[0].DurationTicks);
        }
        else AssertErrorAt(r, Abilities, "abilities[0].effects[0].duration");
        if (seconds is 2.5 or 0.5) AssertErrorAt(r, Abilities, "abilities[0].effects[0].duration", "whole number of seconds");
    }

    [Fact]
    public void ASlowsDuration_MayBeAFractionOfASecond()
    {
        DataLoadResult r = LoadWith(a => { a["effects"]![0]!["status"] = "slowed"; a["effects"]![0]!["magnitude"] = 0.3; a["effects"]![0]!["duration"] = 2.5; });
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(50, r.Data!.Abilities[r.Data.FindAbility("telas_fire")].Effects[0].DurationTicks);
    }

    /// <summary>M4-4b-1: the Cusser as docs/factions/malazan.md "Abilities" gives it, on the Sapper.</summary>
    [Fact]
    public void Shipped_Cusser_MatchesTheFactionPage_AndTheSapperHasIt()
    {
        GameData d = TestSim.Data;
        AbilityDef a = d.Abilities[d.FindAbility("cusser")];
        Assert.Equal((AbilityKind.TargetGround, 6f, 3.5f, 20, 900), (a.Kind, a.Range, a.Radius, a.CastTicks, a.CooldownTicks));
        Assert.Equal(AbilityAffects.EnemyUnits, a.Affects);
        Assert.True(a.HitsBuildings);
        AbilityEffect e = Assert.Single(a.Effects);
        Assert.Equal((AbilityEffectKind.Damage, d.DamageTable.DamageTypeKeys.IndexOf("siege"), 120, true, 0.5f),
            (e.Kind, e.DamageType, e.Amount, e.Buildings, e.FriendlyFire));
        Assert.Equal(new[] { a.Id }, d.Units[d.FindUnit("malazan_sapper")].Abilities.ToArray());
        Assert.False(d.Abilities[d.FindAbility("telas_fire")].HitsBuildings);
    }

    /// <summary>M4-4b-1: <c>buildings</c> and <c>friendlyFire</c> belong to a damage effect; friendly fire is 0-1, and only on an <c>enemy_units</c> ability.</summary>
    [Fact]
    public void BuildingsAndFriendlyFire_AreCheckedPerKindAndRange()
    {
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["buildings"] = true), Abilities, "abilities[0].effects[0].buildings", "no such field");
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["friendlyFire"] = 0.5), Abilities, "abilities[0].effects[0].friendlyFire", "no such field");
        static JsonArray Damage(double ff) => new(new JsonObject { ["kind"] = "damage", ["type"] = "siege", ["amount"] = 120, ["friendlyFire"] = ff });
        AssertErrorAt(LoadWith(a => a["effects"] = Damage(1.5)), Abilities, "abilities[0].effects[0].friendlyFire", "outside 0-1");
        AssertErrorAt(LoadWith(a => a["effects"] = Damage(-0.1)), Abilities, "abilities[0].effects[0].friendlyFire", "outside 0-1");
        foreach (double ok in new[] { 0.0, 1.0 })
        {
            DataLoadResult r = LoadWith(a => a["effects"] = Damage(ok));
            Assert.True(r.Ok, string.Join("\n", r.Errors));
        }
        AssertErrorAt(LoadWith(a => { a["effects"] = Damage(0.5); a["affects"] = "all_units"; }), Abilities, "abilities[0].effects[0].friendlyFire", "already takes");
        AssertErrorAt(LoadWith(a =>
        {
            a["affects"] = "own_units";
            a["effects"] = new JsonArray(new JsonObject { ["kind"] = "damage", ["type"] = "siege", ["amount"] = 1, ["buildings"] = true });
        }), Abilities, "abilities[0].effects[0].buildings", "only own units");
    }

    /// <summary>M4-4b-2: Sandstorm as docs/factions/whirlwind.md "Abilities" gives it (18 m, 6 m, 1.2 s, 45 s, 12 s), on the Priest.</summary>
    [Fact]
    public void Shipped_Sandstorm_MatchesTheFactionPage_AndThePriestHasIt()
    {
        GameData d = TestSim.Data;
        AbilityDef a = d.Abilities[d.FindAbility("sandstorm")];
        Assert.Equal(d.FindFaction("whirlwind"), a.Faction);
        Assert.Equal((AbilityKind.TargetGround, 18f, 6f, 24, 900, 240), (a.Kind, a.Range, a.Radius, a.CastTicks, a.CooldownTicks, a.DurationTicks));
        Assert.Equal(AbilityAffects.EnemyUnits, a.Affects); // Producer decision (a): "non-Whirlwind" read as enemy
        Assert.Equal("Sandstorm", a.DisplayName);
        Assert.False(a.HitsBuildings);
        Assert.Equal(0, a.ZoneEffect);
        AbilityEffect e = Assert.Single(a.Effects);
        Assert.Equal(AbilityEffectKind.CreateZone, e.Kind);
        Assert.True(e.BlocksVision);
        Assert.Equal(new[]
        {
            new ZoneStatus { Status = d.FindStatus("blinded"), Magnitude = 0f, DurationTicks = 20 },
            new ZoneStatus { Status = d.FindStatus("slowed"), Magnitude = 0.3f, DurationTicks = 20 },
        }, e.ZoneStatuses.ToArray());
        Assert.Equal(new[] { a.Id }, d.Units[d.FindUnit("whirlwind_priest")].Abilities.ToArray());
        Assert.Equal(-1, d.Abilities[d.FindAbility("telas_fire")].ZoneEffect);
        Assert.True(d.Abilities[d.FindAbility("telas_fire")].Effects[0].ZoneStatuses.IsEmpty);
    }

    /// <summary>M4-4b-2 criterion 1: a zone ability needs a duration (the zone's lifetime) of at least a tick.</summary>
    [Fact]
    public void AZoneAbility_WithoutADuration_IsAnErrorAtTheDuration()
    {
        AssertErrorAt(LoadWithSandstorm(a => a.Remove("duration")), Whirlwind, "abilities[0].duration", "leaves a zone");
        AssertErrorAt(LoadWithSandstorm(a => a["duration"] = 0), Whirlwind, "abilities[0].duration");
        AssertErrorAt(LoadWithSandstorm(a => a["duration"] = 0.01), Whirlwind, "abilities[0].duration", "rounds to 0 ticks");
        DataLoadResult r = LoadWithSandstorm(a => a["duration"] = 0.05);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(1, r.Data!.Abilities[r.Data.FindAbility("sandstorm")].DurationTicks);
        // An ability without a zone still may leave its duration out, or give 0.
        Assert.True(LoadWith(a => a["duration"] = 0).Ok);
    }

    /// <summary>M4-4b-2 criterion 1: <c>blocksVision</c> and <c>statuses</c> belong to a <c>createZone</c> effect only.</summary>
    [Fact]
    public void BlocksVision_AndStatuses_OnANonZoneEffect_AreErrors()
    {
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["blocksVision"] = true), Abilities, "abilities[0].effects[0].blocksVision", "no such field");
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["blocksVision"] = false), Abilities, "abilities[0].effects[0].blocksVision", "no such field");
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["statuses"] = new JsonArray()), Abilities, "abilities[0].effects[0].statuses", "no such field");
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(Abilities, root => root["abilities"]![1]!["effects"]![0]!["blocksVision"] = true); // the Cusser's damage effect
        AssertErrorAt(DataLoader.LoadAll(dir.Path), Abilities, "abilities[1].effects[0].blocksVision", "no such field");
    }

    /// <summary>M4-4b-2 criterion 1: a zone needs at least one status; its own fields are <c>blocksVision</c> and <c>statuses</c>.</summary>
    [Fact]
    public void AZone_WithNoStatuses_OrAnotherKindsField_IsAnError()
    {
        AssertErrorAt(LoadWithSandstorm(a => Zone(a)["statuses"] = new JsonArray()), Whirlwind, "abilities[0].effects[0].statuses", "at least one status");
        AssertErrorAt(LoadWithSandstorm(a => Zone(a).Remove("statuses")), Whirlwind, "abilities[0].effects[0].statuses", "missing");
        AssertErrorAt(LoadWithSandstorm(a => Zone(a)["status"] = "slowed"), Whirlwind, "abilities[0].effects[0].status", "no such field");
        AssertErrorAt(LoadWithSandstorm(a => Zone(a)["duration"] = 3), Whirlwind, "abilities[0].effects[0].duration", "no such field");
        AssertErrorAt(LoadWithSandstorm(a => Zone(a)["amount"] = 3), Whirlwind, "abilities[0].effects[0].amount", "no such field");
        Assert.False(LoadWithSandstorm(a => Zone(a)["radius"] = 3).Ok); // an unknown field
        // blocksVision is optional (default false).
        DataLoadResult r = LoadWithSandstorm(a => Zone(a).Remove("blocksVision"));
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.False(r.Data!.Abilities[r.Data.FindAbility("sandstorm")].Effects[0].BlocksVision);
        // At most one zone per ability.
        AssertErrorAt(LoadWithSandstorm(a => a["effects"]!.AsArray().Add(Zone(a).DeepClone())), Whirlwind, "abilities[0].effects[1].kind", "at most one");
    }

    /// <summary>M4-4b-2 criterion 1: each zone status follows the <c>applyStatus</c> rules, the whole-seconds rule for damage over time included.</summary>
    [Fact]
    public void ZoneStatuses_FollowTheApplyStatusRules_AtTheirPaths()
    {
        static JsonObject Entry(JsonObject a, int k) => Zone(a)["statuses"]![k]!.AsObject();
        AssertErrorAt(LoadWithSandstorm(a => Entry(a, 1)["status"] = "frozen"), Whirlwind, "abilities[0].effects[0].statuses[1].status", "unknown status");
        AssertErrorAt(LoadWithSandstorm(a => Entry(a, 1)["magnitude"] = 1), Whirlwind, "abilities[0].effects[0].statuses[1].magnitude", "below 1");
        AssertErrorAt(LoadWithSandstorm(a => Entry(a, 1).Remove("magnitude")), Whirlwind, "abilities[0].effects[0].statuses[1].magnitude", "missing");
        AssertErrorAt(LoadWithSandstorm(a => Entry(a, 1).Remove("duration")), Whirlwind, "abilities[0].effects[0].statuses[1].duration", "missing");
        AssertErrorAt(LoadWithSandstorm(a => Entry(a, 0)["magnitude"] = 1), Whirlwind, "abilities[0].effects[0].statuses[0].magnitude", "takes no magnitude");
        Assert.False(LoadWithSandstorm(a => Entry(a, 0)["kind"] = "slow").Ok); // an unknown field
        // Damage over time in a zone: whole seconds, at least 1 (M4-4b-1's rule).
        static void Burning(JsonObject a, double seconds) =>
            Zone(a)["statuses"]!.AsArray().Add(new JsonObject { ["status"] = "burning", ["magnitude"] = 5, ["duration"] = seconds });
        AssertErrorAt(LoadWithSandstorm(a => Burning(a, 2.5)), Whirlwind, "abilities[0].effects[0].statuses[2].duration", "whole number of seconds");
        AssertErrorAt(LoadWithSandstorm(a => Burning(a, 0.5)), Whirlwind, "abilities[0].effects[0].statuses[2].duration", "whole number of seconds");
        DataLoadResult r = LoadWithSandstorm(a => Burning(a, 2));
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(new ZoneStatus { Status = r.Data!.FindStatus("burning"), Magnitude = 5f, DurationTicks = 40 },
            r.Data.Abilities[r.Data.FindAbility("sandstorm")].Effects[0].ZoneStatuses[2]);
    }

    /// <summary>M4-4b-2: an <c>applyStatus</c> of a blind status takes no magnitude (the status holds its sight and reach).</summary>
    [Fact]
    public void ABlindApplyStatus_TakesNoMagnitude()
    {
        AssertErrorAt(LoadWith(a => a["effects"]![0]!["status"] = "blinded"), Abilities, "abilities[0].effects[0].magnitude", "takes no magnitude");
        DataLoadResult r = LoadWith(a => { a["effects"]![0]!["status"] = "blinded"; a["effects"]![0]!.AsObject().Remove("magnitude"); });
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(0f, r.Data!.Abilities[r.Data.FindAbility("telas_fire")].Effects[0].Magnitude);
    }
}
