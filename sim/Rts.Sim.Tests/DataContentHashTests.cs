using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>M1-6: <see cref="GameData.ContentHash"/> is stable and covers every field of every def.</summary>
public class DataContentHashTests
{
    [Fact]
    public void SameFiles_SameHash_AcrossLoads()
    {
        DataLoadResult again = DataLoader.LoadAll(TestDataDir.Shipped);
        Assert.True(again.Ok);
        Assert.NotSame(TestSim.Data, again.Data);
        Assert.Equal(TestSim.Data.ContentHash(), again.Data!.ContentHash());
    }

    [Fact]
    public void OneStatChangedInAFile_ChangesTheHash()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("whirlwind", FirstKey("whirlwind"), "attack.range", "7.25");
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("\n", load.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), load.Data!.ContentHash());
    }

    private static string FirstKey(string faction) =>
        TestSim.Data.Units.First(u => TestSim.Data.Factions[u.Faction].Key == faction).Key;

    private static T Clone<T>(T obj) where T : class =>
        (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(obj, null)!;

    private static IEnumerable<PropertyInfo> Settable(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.SetMethod != null);

    /// <summary>A different value of the same type, or null for a type this test doesn't know (which fails it).</summary>
    private static object? Changed(object? value, Type t)
    {
        if (t == typeof(int)) return (int)value! + 1;
        if (t == typeof(uint)) return (uint)value! + 1;
        if (t == typeof(float)) return (float)value! + 0.5f;
        if (t == typeof(bool)) return !(bool)value!;
        if (t == typeof(string)) return (string?)value + "x";
        if (t.IsEnum) return Enum.ToObject(t, Convert.ToInt32(value) + 1);
        if (t == typeof(ImmutableArray<int>)) return ((ImmutableArray<int>)value!).Add(1);
        if (t == typeof(ImmutableArray<float>))
        {
            var a = (ImmutableArray<float>)value!;
            return a.Length > 0 ? a.SetItem(0, a[0] + 0.5f) : a.Add(1f);
        }
        if (t == typeof(ImmutableArray<byte>)) // M5-9a: ai.counters' masks
        {
            var a = (ImmutableArray<byte>)value!;
            return a.Length > 0 ? a.SetItem(0, (byte)(a[0] ^ 1)) : a.Add(1);
        }
        if (t == typeof(ImmutableArray<bool>))
        {
            var a = (ImmutableArray<bool>)value!;
            return a.Length > 0 ? a.SetItem(0, !a[0]) : a.Add(true);
        }
        if (t == typeof(ImmutableArray<string>))
        {
            var a = (ImmutableArray<string>)value!;
            return a.Length > 0 ? a.SetItem(0, a[0] + "x") : a.Add("x");
        }
        if (t == typeof(ImmutableArray<TechEffect>))
        {
            var a = (ImmutableArray<TechEffect>)value!;
            return a.Length > 0 ? a.SetItem(0, a[0] with { Amount = a[0].Amount + 1f })
                : a.Add(new TechEffect { Stat = TechStat.Armor, Amount = 1f, AttackType = -1, Tags = ImmutableArray<int>.Empty, Units = ImmutableArray<int>.Empty, Siege = -1 });
        }
        if (t == typeof(ImmutableArray<AbilityEffect>))
        {
            var a = (ImmutableArray<AbilityEffect>)value!;
            return a.Length > 0 ? a.SetItem(0, a[0] with { DurationTicks = a[0].DurationTicks + 1 })
                : a.Add(new AbilityEffect { Kind = AbilityEffectKind.Damage, DamageType = 0, Amount = 1, Status = -1 });
        }
        // M5-3: ai.json's lists (each element's own fields: EveryAiStructField_ChangesTheHash).
        if (t == typeof(ImmutableArray<AiDifficultyDef>)) return ((ImmutableArray<AiDifficultyDef>)value!).SetItem(0, ((ImmutableArray<AiDifficultyDef>)value!)[0] with { ThinkTicks = 99 });
        if (t == typeof(ImmutableArray<AiStep>)) return ((ImmutableArray<AiStep>)value!).Add(((ImmutableArray<AiStep>)value!)[0]);
        if (t == typeof(ImmutableArray<AiComposition>)) return ((ImmutableArray<AiComposition>)value!).RemoveAt(0);
        // M6-S6: one more starting-resources preset (a copy of the first; each field: EveryStartingResourcesField_AndTheirOrder_ChangeTheHash).
        if (t == typeof(ImmutableArray<StartingResourcesDef>)) return ((ImmutableArray<StartingResourcesDef>)value!).Add(((ImmutableArray<StartingResourcesDef>)value!)[0]);
        // M5-8a: one more opening (a copy of the first; each opening field: EveryAiStructField_ChangesTheHash).
        if (t == typeof(ImmutableArray<AiOpening>)) return ((ImmutableArray<AiOpening>)value!).Add(((ImmutableArray<AiOpening>)value!)[0]);
        if (t == typeof(ImmutableArray<UnitPassive>)) // M4-4b-3a (each field: EveryPassiveField_AndTheirCount_ChangeTheHash)
        {
            var a = (ImmutableArray<UnitPassive>)value!;
            return a.Length > 0 ? a.SetItem(0, a[0] with { Magnitude = a[0].Magnitude + 0.5f })
                : a.Add(new UnitPassive { Trigger = PassiveTrigger.WhenBelowHp, HpFraction = 0.5f, Status = 0, Magnitude = 0.5f, Units = ImmutableArray<int>.Empty });
        }
        return null;
    }

    private static GameData With(GameData d, DamageTable? table = null, RulesDef? rules = null, FactionDef? faction = null, UnitDef? unit = null,
        ResourceDef? resource = null, int resourceSlot = 0, BuildingDef? building = null, int buildingSlot = 0, TechDef? tech = null, int techSlot = 0,
        ProjectileDef? projectile = null, int projectileSlot = 0, StatusDef? status = null, int statusSlot = 0, AbilityDef? ability = null, int abilitySlot = 0,
        AiRulesDef? aiRules = null, AiDef? ai = null, int aiSlot = 0) => new()
    {
        AiRules = aiRules ?? d.AiRules, // M5-3
        Maps = d.Maps, // M6-S7
        Ai = ai == null ? d.Ai : d.Ai.SetItem(aiSlot, ai),
        Statuses = status == null ? d.Statuses : d.Statuses.SetItem(statusSlot, status),
        Abilities = ability == null ? d.Abilities : d.Abilities.SetItem(abilitySlot, ability),
        DamageTable = table ?? d.DamageTable,
        Rules = rules ?? d.Rules,
        Factions = faction == null ? d.Factions : d.Factions.SetItem(faction.Id, faction),
        Units = unit == null ? d.Units : d.Units.SetItem(unit.Id, unit),
        Resources = resource == null ? d.Resources : d.Resources.SetItem(resourceSlot, resource),
        Projectiles = projectile == null ? d.Projectiles : d.Projectiles.SetItem(projectileSlot, projectile),
        Buildings = building == null ? d.Buildings : d.Buildings.SetItem(buildingSlot, building),
        Techs = tech == null ? d.Techs : d.Techs.SetItem(techSlot, tech),
    };

    /// <summary>M3-5: every <see cref="TechEffect"/> field, the effect count, and the tech list length change the hash.</summary>
    [Fact]
    public void TechListLength_EffectCount_AndEveryEffectField_ChangeTheHash()
    {
        GameData d = TestSim.Data;
        ulong baseline = d.ContentHash();
        Assert.NotEqual(baseline, new GameData { DamageTable = d.DamageTable, Rules = d.Rules, Factions = d.Factions, Units = d.Units, Resources = d.Resources, Buildings = d.Buildings }.ContentHash());
        TechDef tech = d.Techs[d.FindTech("moranth_supply")];
        TechEffect e = tech.Effects[0];
        var variants = new (string Field, TechEffect Changed)[]
        {
            ("Stat", e with { Stat = TechStat.Hp }),
            ("Amount", e with { Amount = e.Amount + 0.5f }),
            ("AttackType", e with { AttackType = e.AttackType + 1 }),
            ("Tags", e with { Tags = e.Tags.Add(0) }),
            ("Units", e with { Units = e.Units.Add(0) }),
            ("Siege", e with { Siege = e.Siege + 1 }),
        };
        foreach ((string field, TechEffect changed) in variants)
        {
            TechDef copy = Clone(tech);
            typeof(TechDef).GetProperty(nameof(TechDef.Effects))!.SetValue(copy, tech.Effects.SetItem(0, changed));
            Assert.True(With(d, tech: copy, techSlot: tech.Id).ContentHash() != baseline, $"TechEffect.{field} is not in GameData.ContentHash");
        }
        Assert.Equal(6, typeof(TechEffect).GetProperties().Length); // a new field must be added above and to ContentHash
        TechDef fewer = Clone(tech);
        typeof(TechDef).GetProperty(nameof(TechDef.Effects))!.SetValue(fewer, tech.Effects.RemoveAt(1));
        Assert.NotEqual(baseline, With(d, tech: fewer, techSlot: tech.Id).ContentHash());
    }

    /// <summary>M4-4b-2: every <see cref="ZoneStatus"/> field of Sandstorm's zone, and the zone status count, change the hash.</summary>
    [Fact]
    public void EveryZoneStatusField_AndTheirCount_ChangeTheHash()
    {
        GameData d = TestSim.Data;
        ulong baseline = d.ContentHash();
        AbilityDef ability = d.Abilities[d.FindAbility("sandstorm")];
        AbilityEffect e = ability.Effects[ability.ZoneEffect];
        ZoneStatus z = e.ZoneStatuses[1];
        var variants = new (string Field, ImmutableArray<ZoneStatus> Changed)[]
        {
            ("Status", e.ZoneStatuses.SetItem(1, z with { Status = z.Status + 1 })),
            ("Magnitude", e.ZoneStatuses.SetItem(1, z with { Magnitude = z.Magnitude + 0.1f })),
            ("DurationTicks", e.ZoneStatuses.SetItem(1, z with { DurationTicks = z.DurationTicks + 1 })),
            ("count", e.ZoneStatuses.RemoveAt(1)),
        };
        foreach ((string field, ImmutableArray<ZoneStatus> changed) in variants)
        {
            AbilityDef copy = Clone(ability);
            typeof(AbilityDef).GetProperty(nameof(AbilityDef.Effects))!.SetValue(copy, ability.Effects.SetItem(ability.ZoneEffect, e with { ZoneStatuses = changed }));
            Assert.True(With(d, ability: copy, abilitySlot: ability.Id).ContentHash() != baseline, $"ZoneStatus {field} is not in GameData.ContentHash");
        }
        Assert.Equal(3, typeof(ZoneStatus).GetProperties().Length); // a new field must be added above and to ContentHash
    }

    /// <summary>
    /// M4-4b-3a: every <see cref="UnitPassive"/> field of the Zealot's two passives, and their count, change the hash; so does
    /// a passive edited in <c>whirlwind/units.json</c>.
    /// </summary>
    [Fact]
    public void EveryPassiveField_AndTheirCount_ChangeTheHash()
    {
        GameData d = TestSim.Data;
        ulong baseline = d.ContentHash();
        UnitDef zealot = d.Units[d.FindUnit("whirlwind_zealot")];
        Assert.Equal(2, zealot.Passives.Length);
        int fields = 0;
        foreach (int k in new[] { zealot.BelowHpPassive, zealot.DeathPassive })
        {
            UnitPassive p = zealot.Passives[k];
            var variants = new (string Field, UnitPassive Changed)[]
            {
                ("Trigger", p with { Trigger = p.Trigger == PassiveTrigger.OnDeath ? PassiveTrigger.WhenBelowHp : PassiveTrigger.OnDeath }),
                ("HpFraction", p with { HpFraction = p.HpFraction + 0.1f }),
                ("Status", p with { Status = p.Status + 1 }),
                ("Magnitude", p with { Magnitude = p.Magnitude + 0.1f }),
                ("DurationTicks", p with { DurationTicks = p.DurationTicks + 1 }),
                ("Radius", p with { Radius = p.Radius + 0.5f }),
                ("Units", p with { Units = p.Units.Add(zealot.Id) }),
            };
            foreach ((string field, UnitPassive changed) in variants)
            {
                UnitDef copy = Clone(zealot);
                typeof(UnitDef).GetProperty(nameof(UnitDef.Passives))!.SetValue(copy, zealot.Passives.SetItem(k, changed));
                Assert.True(With(d, unit: copy).ContentHash() != baseline, $"UnitPassive.{field} (passive {k}) is not in GameData.ContentHash");
                fields++;
            }
        }
        Assert.Equal(14, fields);
        Assert.Equal(7, typeof(UnitPassive).GetProperties().Length); // a new field must be added above and to ContentHash
        UnitDef fewer = Clone(zealot);
        typeof(UnitDef).GetProperty(nameof(UnitDef.Passives))!.SetValue(fewer, zealot.Passives.RemoveAt(1));
        Assert.NotEqual(baseline, With(d, unit: fewer).ContentHash());
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("whirlwind", "whirlwind_zealot", "passives", "[{ \"trigger\": \"whenBelowHp\", \"hpFraction\": 0.4, \"status\": \"frenzied\", \"magnitude\": 0.5 }]");
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("\n", load.Errors));
        Assert.NotEqual(baseline, load.Data!.ContentHash());
    }

    /// <summary>M4-4a: every <see cref="AbilityEffect"/> field, the effect count, and both list lengths change the hash; so do edits to both files.</summary>
    [Fact]
    public void AbilityListLength_EffectCount_AndEveryEffectField_ChangeTheHash()
    {
        GameData d = TestSim.Data;
        ulong baseline = d.ContentHash();
        AbilityDef ability = d.Abilities[d.FindAbility("telas_fire")];
        AbilityEffect e = ability.Effects[0];
        var variants = new (string Field, AbilityEffect Changed)[]
        {
            ("Kind", e with { Kind = AbilityEffectKind.Damage }),
            ("DamageType", e with { DamageType = e.DamageType + 1 }),
            ("Amount", e with { Amount = e.Amount + 1 }),
            ("Status", e with { Status = e.Status + 1 }),
            ("Magnitude", e with { Magnitude = e.Magnitude + 0.5f }),
            ("DurationTicks", e with { DurationTicks = e.DurationTicks + 1 }),
            ("Buildings", e with { Buildings = !e.Buildings }),
            ("FriendlyFire", e with { FriendlyFire = e.FriendlyFire + 0.5f }),
            ("BlocksVision", e with { BlocksVision = !e.BlocksVision }), // M4-4b-2
            ("ZoneStatuses", e with { ZoneStatuses = ImmutableArray.Create(new ZoneStatus { Status = 0, Magnitude = 0f, DurationTicks = 20 }) }),
        };
        foreach ((string field, AbilityEffect changed) in variants)
        {
            AbilityDef copy = Clone(ability);
            typeof(AbilityDef).GetProperty(nameof(AbilityDef.Effects))!.SetValue(copy, ability.Effects.SetItem(0, changed));
            Assert.True(With(d, ability: copy, abilitySlot: ability.Id).ContentHash() != baseline, $"AbilityEffect.{field} is not in GameData.ContentHash");
        }
        // A new field must be added above and to ContentHash. M4-4b-3b's three spawn fields (Unit, Count, LifetimeTicks) are
        // hashed only on a spawn effect, so they are varied on one: EverySpawnField_AndTheSummonKind_ChangeTheHash.
        Assert.Equal(13, typeof(AbilityEffect).GetProperties().Length);
        AbilityDef more = Clone(ability);
        typeof(AbilityDef).GetProperty(nameof(AbilityDef.Effects))!.SetValue(more, ability.Effects.Add(e));
        Assert.NotEqual(baseline, With(d, ability: more, abilitySlot: ability.Id).ContentHash());
        GameData fewer = With(d);
        typeof(GameData).GetProperty(nameof(GameData.Abilities))!.SetValue(fewer, ImmutableArray<AbilityDef>.Empty);
        Assert.NotEqual(baseline, fewer.ContentHash());
        GameData noStatuses = With(d);
        typeof(GameData).GetProperty(nameof(GameData.Statuses))!.SetValue(noStatuses, d.Statuses.RemoveAt(1));
        Assert.NotEqual(baseline, noStatuses.ContentHash());
    }

    /// <summary>
    /// M4-4b-3b criterion 5: the summon kind and every field of a spawn effect move the hash, for an in-memory ability (no
    /// shipped ability has either, so the spawn fields are hashed only on that kind and the shipped data's hash is untouched;
    /// the shipped hash itself is pinned by the golden replay alone, never here: rule 8). The kind's "no target" ripples
    /// nowhere else: the summon's Range and Radius are the hashed fields they were.
    /// </summary>
    [Fact]
    public void EverySpawnField_AndTheSummonKind_ChangeTheHash()
    {
        GameData d = TestSim.Data;
        ulong baseline = d.ContentHash();
        AbilityDef ability = d.Abilities[d.FindAbility("telas_fire")];
        AbilityDef summon = Clone(ability);
        typeof(AbilityDef).GetProperty(nameof(AbilityDef.Kind))!.SetValue(summon, AbilityKind.Summon);
        ulong kindOnly = With(d, ability: summon, abilitySlot: ability.Id).ContentHash();
        Assert.NotEqual(baseline, kindOnly);
        var spawn = new AbilityEffect
        {
            Kind = AbilityEffectKind.Spawn, DamageType = -1, Status = -1, ZoneStatuses = ImmutableArray<ZoneStatus>.Empty,
            Unit = d.FindUnit("malazan_heavy_infantry"), Count = 3, LifetimeTicks = 400,
        };
        AbilityDef withSpawn = Clone(summon);
        typeof(AbilityDef).GetProperty(nameof(AbilityDef.Effects))!.SetValue(withSpawn, ImmutableArray.Create(spawn));
        ulong spawned = With(d, ability: withSpawn, abilitySlot: ability.Id).ContentHash();
        Assert.NotEqual(kindOnly, spawned);
        var variants = new (string Field, AbilityEffect Changed)[]
        {
            ("Unit", spawn with { Unit = spawn.Unit + 1 }),
            ("Count", spawn with { Count = spawn.Count + 1 }),
            ("LifetimeTicks", spawn with { LifetimeTicks = spawn.LifetimeTicks + 1 }),
        };
        foreach ((string field, AbilityEffect changed) in variants)
        {
            AbilityDef copy = Clone(withSpawn);
            typeof(AbilityDef).GetProperty(nameof(AbilityDef.Effects))!.SetValue(copy, ImmutableArray.Create(changed));
            Assert.True(With(d, ability: copy, abilitySlot: ability.Id).ContentHash() != spawned, $"AbilityEffect.{field} of a spawn is not in GameData.ContentHash");
        }
        // The same three fields on a non-spawn effect are not hashed (they are always -1 / 0 there), which is what keeps the
        // shipped hash: a test-only check that the rule is the one documented.
        AbilityEffect status = ability.Effects[0];
        AbilityDef odd = Clone(ability);
        typeof(AbilityDef).GetProperty(nameof(AbilityDef.Effects))!.SetValue(odd, ImmutableArray.Create(status with { Count = 5 }));
        Assert.Equal(baseline, With(d, ability: odd, abilitySlot: ability.Id).ContentHash());
    }

    /// <summary>M4-4a: an edit to <c>common/statuses.json</c> or to <c>malazan/abilities.json</c> changes the hash.</summary>
    [Theory]
    [InlineData("common/statuses.json")]
    [InlineData("factions/malazan/abilities.json")]
    public void AStatusesOrAbilitiesFileEdit_ChangesTheHash(string file)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(file, root =>
        {
            if (file.StartsWith("common")) root["statuses"]![0]!["description"] = "Edited.";
            else root["abilities"]![0]!["cooldown"] = 24;
        });
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("\n", load.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), load.Data!.ContentHash());
    }

    [Fact]
    public void BuildingListLengthAndAFileEdit_ChangeTheHash()
    {
        GameData d = TestSim.Data;
        Assert.NotEqual(d.ContentHash(), new GameData { DamageTable = d.DamageTable, Rules = d.Rules, Factions = d.Factions, Units = d.Units, Resources = d.Resources }.ContentHash());
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/whirlwind/buildings.json", root => root["buildings"]![0]!["armor"] = 6);
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("\n", load.Errors));
        Assert.NotEqual(d.ContentHash(), load.Data!.ContentHash());
    }

    [Fact]
    public void ProjectileListLength_ChangesTheHash()
    {
        GameData d = TestSim.Data;
        GameData fewer = With(d);
        typeof(GameData).GetProperty(nameof(GameData.Projectiles))!.SetValue(fewer, d.Projectiles.RemoveAt(d.Projectiles.Length - 1));
        Assert.Equal(d.ContentHash(), With(d).ContentHash());
        Assert.NotEqual(d.ContentHash(), fewer.ContentHash());
    }

    [Fact]
    public void ResourceListLengthAndFootprintEdit_ChangeTheHash()
    {
        GameData d = TestSim.Data;
        Assert.NotEqual(d.ContentHash(), new GameData { DamageTable = d.DamageTable, Rules = d.Rules, Factions = d.Factions, Units = d.Units }.ContentHash());
        // M6-S7: without the maps folder on both sides: a 3-wide mine reaches into the shipped map's start clear ground and
        // the map is refused, which this row is not about.
        using TestDataDir plain = TestDataDir.CopyOfShipped(), dir = TestDataDir.CopyOfShipped();
        Directory.Delete(plain.FullPath("maps"), recursive: true);
        Directory.Delete(dir.FullPath("maps"), recursive: true);
        dir.EditJson("common/resources.json", root => root["resources"]![0]!["footprint"]!["width"] = 3);
        DataLoadResult baseline = DataLoader.LoadAll(plain.Path), load = DataLoader.LoadAll(dir.Path);
        Assert.True(baseline.Ok && load.Ok, string.Join("\n", load.Errors));
        Assert.NotEqual(baseline.Data!.ContentHash(), load.Data!.ContentHash());
    }

    [Fact]
    public void ChangingAnyFieldOfAnyDef_ChangesTheHash()
    {
        GameData d = TestSim.Data;
        ulong baseline = d.ContentHash();
        Assert.Equal(baseline, With(d).ContentHash());
        var checkedFields = new List<string>();

        void Check<T>(T def, Func<T, GameData> rebuild) where T : class
        {
            foreach (PropertyInfo p in Settable(typeof(T)))
            {
                if (p.PropertyType == typeof(AttackDef)) continue; // its fields are checked one by one below
                object? changed = Changed(p.GetValue(def), p.PropertyType);
                Assert.True(changed != null, $"{typeof(T).Name}.{p.Name}: unknown field type {p.PropertyType}; teach this test and ContentHash about it");
                T copy = Clone(def);
                p.SetValue(copy, changed);
                Assert.True(rebuild(copy).ContentHash() != baseline, $"{typeof(T).Name}.{p.Name} is not in GameData.ContentHash");
                checkedFields.Add($"{typeof(T).Name}.{p.Name}");
            }
        }

        Check(d.DamageTable, t => With(d, table: t));
        Check(d.Rules, r => With(d, rules: r));
        Check(d.Factions[0], f => With(d, faction: f));
        UnitDef unit = d.Units[0];
        Check(unit, u => With(d, unit: u));
        Check(unit.Attack, a =>
        {
            UnitDef u = Clone(unit);
            typeof(UnitDef).GetProperty(nameof(UnitDef.Attack))!.SetValue(u, a);
            return With(d, unit: u);
        });
        // M3-1: every ResourceDef field, on each shipped resource type.
        foreach (ResourceDef r in d.Resources)
            Check(r, x => With(d, resource: x, resourceSlot: r.Id)); // by slot: the Id row changes Id
        // M3-2: every BuildingDef field, on each shipped building type.
        foreach (BuildingDef b in d.Buildings)
            Check(b, x => With(d, building: x, buildingSlot: b.Id));
        foreach (string f in new[] { "Id", "Key", "Faction", "Slot", "DisplayName", "Description", "FootprintWidth", "FootprintHeight",
            "Hp", "Armor", "CostGold", "CostWood", "BuildTicks", "HalfPopProvided", "DropOff", "Requires", "RequiresTechs", "RequiresBuildings", "Sight",
            "Detector" }) // M4-3b (the attack: TowerAttack_EveryField_AndItsPresence_ChangeTheHash)
            Assert.Contains($"BuildingDef.{f}", checkedFields);
        // M3-5: every TechDef field, on each shipped tech (the effects' own fields: TechListLength_EffectCount_AndEveryEffectField_ChangeTheHash).
        foreach (TechDef t in d.Techs)
            Check(t, x => With(d, tech: x, techSlot: t.Id));
        foreach (string f in new[] { "Id", "Key", "Faction", "DisplayName", "Description", "ResearchedAtSlot", "CostGold", "CostWood",
            "ResearchTicks", "Requires", "Effects", "RequiresTechs", "RequiresBuildings", "RequiresAnyOf", "RequiresAnyOfCount", "RequiresAnyOfSlots" })
            Assert.Contains($"TechDef.{f}", checkedFields);
        foreach (string f in new[] { "Requires", "RequiresTechs", "RequiresBuildings", "TrainedAtTypeId" }) // M3-6: the resolved requires
            Assert.Contains($"UnitDef.{f}", checkedFields);
        Assert.Contains("AttackDef.Projectile", checkedFields); // nullable: null and "x" must differ
        Assert.Contains("RulesDef.BuildingSight", checkedFields); // M4-3a
        Assert.Contains("RulesDef.StartingResources", checkedFields); // M6-S6
        foreach (string f in new[] { "Id", "Key", "DisplayName", "Description", "Resource", "FootprintWidth", "FootprintHeight" })
            Assert.Contains($"ResourceDef.{f}", checkedFields);
        // M4-2b: every ProjectileDef field, on each shipped projectile type, and the attack's resolved id.
        foreach (ProjectileDef p in d.Projectiles)
            Check(p, x => With(d, projectile: x, projectileSlot: p.Id));
        foreach (string f in new[] { "Id", "Key", "Kind", "SpeedPerTick", "HitTolerance", "LeadSpeedPerTick" })
            Assert.Contains($"ProjectileDef.{f}", checkedFields);
        Assert.Contains("AttackDef.ProjectileTypeId", checkedFields);
        // M4-4a: every StatusDef and AbilityDef field, on each shipped entry (the effects' own fields:
        // AbilityListLength_EffectCount_AndEveryEffectField_ChangeTheHash), and a unit's ability list.
        foreach (StatusDef st in d.Statuses)
            Check(st, x => With(d, status: x, statusSlot: st.Id));
        foreach (string f in new[] { "Id", "Key", "DisplayName", "Description", "Kind", "DamageType" })
            Assert.Contains($"StatusDef.{f}", checkedFields);
        foreach (AbilityDef ab in d.Abilities)
            Check(ab, x => With(d, ability: x, abilitySlot: ab.Id));
        foreach (string f in new[] { "Id", "Key", "Faction", "DisplayName", "Description", "Kind", "Range", "Radius", "CastTicks", "CooldownTicks",
            "DurationTicks", "Affects", "Effects", "HitsBuildings" })
            Assert.Contains($"AbilityDef.{f}", checkedFields);
        Assert.Contains("UnitDef.Abilities", checkedFields);
        foreach (string f in new[] { "Passives", "BelowHpPassive", "DeathPassive" }) // M4-4b-3a
            Assert.Contains($"UnitDef.{f}", checkedFields);
        // M5-3: rules.json ai and every faction's ai.json (the list elements' own fields: EveryAiStructField_ChangesTheHash).
        Check(d.AiRules, r => With(d, aiRules: r));
        foreach (string f in new[] { "GoldShare", "HouseHalfPopMargin", "PlacementSearchCells", "SpacingCells", "ExpandReachCells", "ScoutAfterTicks",
            "ScoutRadius", "ScoutReach", "ScoutGrid", "RallyDistance", "RallyReach", "WaveCooldownTicks", "DefendRadius", "DefendForTicks", "WorkerFleeRadius", "CastMinTargets",
            "DefendRatio", "DefendMinValue", "RecallHallHp", "ReserveShift", "ReserveArmyShare", "GatherReach",
            "ScoutReplacements", "SpendDownQueue", "HuntMemoryTicks", "FocusReach", "CounterMemoryTicks", "CounterMasks",
            "StarvedWaveAfterTicks", "StarvedMinArmy", // M5-9a round 2: the starved wave's knobs
            "FarExpandReach" }) // M5-9b: the far expansion's knob
            Assert.Contains($"AiRulesDef.{f}", checkedFields);
        for (int slot = 0; slot < d.Ai.Length; slot++)
            Check(d.Ai[slot], x => With(d, ai: x, aiSlot: slot));
        foreach (string f in new[] { "Faction", "Difficulties", "Openings", "RetreatHpFraction", "RetreatLossFraction" })
            Assert.Contains($"AiDef.{f}", checkedFields);
        // M5-8a: every AiOpening field, on each faction's first opening.
        for (int slot = 0; slot < d.Ai.Length; slot++)
        {
            AiDef ai = d.Ai[slot];
            Check(ai.Openings[0], o => With(d, ai: WithOpenings(ai, ai.Openings.SetItem(0, o)), aiSlot: slot));
        }
        foreach (string f in new[] { "Id", "DisplayName", "Description", "BuildOrder", "Compositions", "AttackMinHalfPop", "AttackMinCost" })
            Assert.Contains($"AiOpening.{f}", checkedFields);
        Assert.True(checkedFields.Count >= 60, $"only {checkedFields.Count} fields checked");
    }

    /// <summary>A copy of <paramref name="value"/> with property <paramref name="p"/> changed (a record struct, through a box).</summary>
    private static T WithField<T>(T value, PropertyInfo p) where T : struct
    {
        object? changed = Changed(p.GetValue(value), p.PropertyType);
        Assert.True(changed != null, $"{typeof(T).Name}.{p.Name}: unknown field type {p.PropertyType}");
        object boxed = value;
        p.SetValue(boxed, changed);
        return (T)boxed;
    }

    /// <summary>M5-3: every field of an <see cref="AiDifficultyDef"/>, an <see cref="AiStep"/> and an <see cref="AiComposition"/> moves the hash, on each faction.</summary>
    /// <summary>M6-S6: every field of a starting-resources preset, the presets' count and their order move the hash.</summary>
    [Fact]
    public void EveryStartingResourcesField_AndTheirOrder_ChangeTheHash()
    {
        GameData d = TestSim.Data;
        ulong baseline = d.ContentHash();
        ImmutableArray<StartingResourcesDef> presets = d.Rules.StartingResources;
        Assert.True(presets.Length >= 2);
        GameData WithPresets(ImmutableArray<StartingResourcesDef> list)
        {
            RulesDef r = Clone(d.Rules);
            typeof(RulesDef).GetProperty(nameof(RulesDef.StartingResources))!.SetValue(r, list);
            return With(d, rules: r);
        }
        var fields = new List<string>();
        foreach (PropertyInfo p in Settable(typeof(StartingResourcesDef)))
        {
            for (int i = 0; i < presets.Length; i++)
            {
                StartingResourcesDef copy = Clone(presets[i]);
                p.SetValue(copy, Changed(p.GetValue(copy), p.PropertyType));
                Assert.True(WithPresets(presets.SetItem(i, copy)).ContentHash() != baseline, $"StartingResourcesDef.{p.Name} (preset {i}) is not in GameData.ContentHash");
            }
            fields.Add(p.Name);
        }
        Assert.Equal(new[] { "Key", "DisplayName", "Description", "Gold", "Wood", "AiSpendAbove" }, fields); // BUG-1050: AiSpendAbove
        Assert.NotEqual(baseline, WithPresets(presets.Reverse().ToImmutableArray()).ContentHash());
        Assert.NotEqual(baseline, WithPresets(presets.RemoveAt(presets.Length - 1)).ContentHash());
    }

    /// <summary>
    /// M6-S7: every public field of a map (on each shipped map, read by id (M6-S8: <c>Maps[0]</c> is Pale Hills in file order):
    /// its id, names, players, size, every level, ramp field, start, node field and the forest count), the maps' count and their
    /// order move the hash; data without maps hashes without a maps term at all (so a data set with no <c>maps/</c> folder keeps
    /// its old hash).
    /// </summary>
    [Theory]
    [InlineData("raraku")]
    [InlineData("pale_hills")]
    [InlineData("vathar_crossing")]
    public void EveryMapField_TheCount_AndTheOrder_ChangeTheHash(string id)
    {
        GameData d = TestSim.Data;
        ulong baseline = d.ContentHash();
        Assert.NotEmpty(d.Maps);
        GameData WithMaps(System.Collections.Immutable.ImmutableArray<MapDef> maps) => new()
        {
            AiRules = d.AiRules, Ai = d.Ai, Statuses = d.Statuses, Abilities = d.Abilities, DamageTable = d.DamageTable, Rules = d.Rules,
            Factions = d.Factions, Units = d.Units, Resources = d.Resources, Projectiles = d.Projectiles, Buildings = d.Buildings, Techs = d.Techs,
            Maps = maps,
        };
        Assert.Equal(baseline, WithMaps(d.Maps).ContentHash());
        int index = d.FindMap(id);
        Assert.True(index >= 0, $"no {id} in GameData.Maps");
        MapDef m = d.Maps[index];
        var fields = new List<string>();
        foreach (PropertyInfo p in Settable(typeof(MapDef)))
        {
            object? value = p.GetValue(m);
            object? changed = p.PropertyType == typeof(int) ? (int)value! + 1
                : p.PropertyType == typeof(string) ? (string)value! + "x"
                : p.PropertyType == typeof(ImmutableArray<byte>) ? ((ImmutableArray<byte>)value!).SetItem(0, (byte)(((ImmutableArray<byte>)value!)[0] + 1))
                : p.PropertyType == typeof(ImmutableArray<int>) ? ((ImmutableArray<int>)value!).SetItem(0, ((ImmutableArray<int>)value!)[0] + 1)
                : p.PropertyType == typeof(ImmutableArray<MapRampDef>) ? null
                : p.PropertyType == typeof(ImmutableArray<MapNodeDef>) ? null
                : throw new InvalidOperationException($"MapDef.{p.Name}: teach this test and ContentHash about {p.PropertyType}");
            if (changed == null) continue; // the struct lists: field by field below
            MapDef copy = Clone(m);
            p.SetValue(copy, changed);
            Assert.True(WithMaps(d.Maps.SetItem(index, copy)).ContentHash() != baseline, $"MapDef.{p.Name} is not in GameData.ContentHash");
            fields.Add(p.Name);
        }
        void Expect(string what, Func<MapDef, MapDef> edit)
        {
            Assert.True(WithMaps(d.Maps.SetItem(index, edit(Clone(m)))).ContentHash() != baseline, $"{what} is not in GameData.ContentHash");
            fields.Add(what);
        }
        MapDef With(MapDef x, string property, object list)
        {
            typeof(MapDef).GetProperty(property)!.SetValue(x, list);
            return x;
        }
        MapRampDef r = m.Ramps[0];
        Expect("Ramps.X", x => With(x, nameof(MapDef.Ramps), m.Ramps.SetItem(0, r with { X = r.X + 1 })));
        Expect("Ramps.Y", x => With(x, nameof(MapDef.Ramps), m.Ramps.SetItem(0, r with { Y = r.Y + 1 })));
        Expect("Ramps.Width", x => With(x, nameof(MapDef.Ramps), m.Ramps.SetItem(0, r with { Width = r.Width + 1 })));
        Expect("Ramps.Height", x => With(x, nameof(MapDef.Ramps), m.Ramps.SetItem(0, r with { Height = r.Height + 1 })));
        Expect("Ramps.Up", x => With(x, nameof(MapDef.Ramps), m.Ramps.SetItem(0, r with { Up = r.Up == MapDirection.North ? MapDirection.South : MapDirection.North })));
        Expect("Ramps.Count", x => With(x, nameof(MapDef.Ramps), m.Ramps.RemoveAt(0)));
        MapNodeDef n = m.Nodes[0];
        Expect("Nodes.TypeId", x => With(x, nameof(MapDef.Nodes), m.Nodes.SetItem(0, n with { TypeId = n.TypeId + 1 })));
        Expect("Nodes.Cell", x => With(x, nameof(MapDef.Nodes), m.Nodes.SetItem(0, n with { Cell = n.Cell + 1 })));
        Expect("Nodes.Amount", x => With(x, nameof(MapDef.Nodes), m.Nodes.SetItem(0, n with { Amount = n.Amount + 1 })));
        Expect("Nodes.Count", x => With(x, nameof(MapDef.Nodes), m.Nodes.RemoveAt(m.Nodes.Length - 1)));
        Expect("Starts.Count", x => With(x, nameof(MapDef.Starts), m.Starts.RemoveAt(1)));
        Assert.Equal(new[] { "Id", "Key", "DisplayName", "Description", "Players", "Size", "Levels", "Starts", "ForestCount" }, fields.Take(9));
        // The count and the order (a second map, the first renamed).
        MapDef second = Clone(m);
        typeof(MapDef).GetProperty(nameof(MapDef.Key))!.SetValue(second, "second");
        ulong two = WithMaps(d.Maps.Add(second)).ContentHash();
        Assert.NotEqual(baseline, two);
        Assert.NotEqual(two, WithMaps(System.Collections.Immutable.ImmutableArray.Create(second).AddRange(d.Maps)).ContentHash());
        // No maps: no maps term (the hash a data set without the folder had before M6-S7).
        Assert.Equal(WithMaps(System.Collections.Immutable.ImmutableArray<MapDef>.Empty).ContentHash(), WithMaps(default).ContentHash());
        Assert.NotEqual(baseline, WithMaps(System.Collections.Immutable.ImmutableArray<MapDef>.Empty).ContentHash());
    }

    /// <summary>M6-S7: an edit to the shipped map file moves the hash: its text, a terrain cell, a ramp, a start, a mine's gold, a tree.</summary>
    [Theory]
    [InlineData("description", "\"Other text.\"")]
    [InlineData("displayName", "\"Raraku Odhan\"")]
    [InlineData("mines[2].gold", "1999")]
    [InlineData("starts[0].y", "65")]
    [InlineData("forests[2].rows[0]", "\"TTT.\"")]
    public void AMapFileEdit_ChangesTheHash(string field, string json)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("maps/raraku.json", root =>
        {
            string[] parts = field.Split('.');
            JsonNode parent = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string part = parts[i];
                int open = part.IndexOf('[');
                parent = open < 0 ? parent[part]! : parent[part[..open]]![int.Parse(part[(open + 1)..^1])]!;
            }
            string last = parts[^1];
            int at = last.IndexOf('[');
            if (at < 0) parent[last] = JsonNode.Parse(json);
            else parent[last[..at]]![int.Parse(last[(at + 1)..^1])] = JsonNode.Parse(json);
        });
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("; ", load.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), load.Data!.ContentHash());
    }

    /// <summary>M6-S8: an edit to each new shipped map file moves the hash: its text, its name, a mine's gold, a tree taken from a forest row.</summary>
    [Theory]
    [InlineData("pale_hills", "description", "\"Other text.\"")]
    [InlineData("pale_hills", "displayName", "\"Pale Woods\"")]
    [InlineData("pale_hills", "mines[5].gold", "2499")]
    [InlineData("vathar_crossing", "description", "\"Other text.\"")]
    [InlineData("vathar_crossing", "displayName", "\"Vathar Fords\"")]
    [InlineData("vathar_crossing", "mines[9].gold", "1999")]
    public void ANewMapFileEdit_ChangesTheHash(string id, string field, string json)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson($"maps/{id}.json", root =>
        {
            int at = field.IndexOf('[');
            if (at < 0) root[field] = JsonNode.Parse(json);
            else root[field[..at]]![int.Parse(field[(at + 1)..field.IndexOf(']')])]![field[(field.IndexOf('.') + 1)..]] = JsonNode.Parse(json);
        });
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("; ", load.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), load.Data!.ContentHash());
    }

    /// <summary>M6-S8: a tree taken out of a new map's forest (the first tree of its first forest's first row with one) moves the hash.</summary>
    [Theory]
    [InlineData("pale_hills")]
    [InlineData("vathar_crossing")]
    public void ANewMapForestEdit_ChangesTheHash(string id)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson($"maps/{id}.json", root =>
        {
            JsonArray rows = root["forests"]![0]!["rows"]!.AsArray();
            int r = 0;
            while (!rows[r]!.GetValue<string>().Contains('T')) r++;
            string row = rows[r]!.GetValue<string>();
            int k = row.IndexOf('T');
            rows[r] = row[..k] + "." + row[(k + 1)..];
        });
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("; ", load.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), load.Data!.ContentHash());
    }

    /// <summary>M6-S7: a terrain cell edited in the file (a level-0 cell far from every start and node raised to 1) moves the hash.</summary>
    [Fact]
    public void AMapTerrainCellEdit_ChangesTheHash()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("maps/raraku.json", root =>
        {
            JsonArray rows = root["terrain"]!.AsArray();
            string row = rows[3]!.GetValue<string>();
            rows[3] = row[..3] + "1" + row[4..];
        });
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("; ", load.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), load.Data!.ContentHash());
    }

    /// <summary>M6-S6: an edit to a <c>rules.json</c> <c>startingResources</c> preset in the file moves the hash.</summary>
    [Theory]
    [InlineData("gold", "201")]
    [InlineData("wood", "199")]
    [InlineData("displayName", "\"Usual\"")]
    [InlineData("description", "\"Other text.\"")]
    [InlineData("aiSpendAbove", "1")] // BUG-1050: standard leaves it out (0); setting it moves the hash
    public void AStartingResourcesEditInTheFile_ChangesTheHash(string field, string json)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["startingResources"]![0]![field] = JsonNode.Parse(json));
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("; ", load.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), load.Data!.ContentHash());
    }

    [Fact]
    public void EveryAiStructField_ChangesTheHash()
    {
        GameData d = TestSim.Data;
        ulong baseline = d.ContentHash();
        int fields = 0;
        for (int slot = 0; slot < d.Ai.Length; slot++)
        {
            AiDef ai = d.Ai[slot];
            void Expect(string what, string property, object list)
            {
                AiDef copy = Clone(ai);
                typeof(AiDef).GetProperty(property)!.SetValue(copy, list);
                Assert.True(With(d, ai: copy, aiSlot: slot).ContentHash() != baseline, $"faction {slot}: {what} is not in GameData.ContentHash");
                fields++;
            }
            foreach (PropertyInfo p in typeof(AiDifficultyDef).GetProperties())
                Expect($"AiDifficultyDef.{p.Name}", nameof(AiDef.Difficulties), ai.Difficulties.SetItem(1, WithField(ai.Difficulties[1], p)));
            // M5-8a: the steps and compositions live in the openings.
            AiOpening first = ai.Openings[0];
            void ExpectInOpening(string what, string property, object list)
            {
                AiOpening o = Clone(first);
                typeof(AiOpening).GetProperty(property)!.SetValue(o, list);
                Expect(what, nameof(AiDef.Openings), ai.Openings.SetItem(0, o));
            }
            foreach (PropertyInfo p in typeof(AiStep).GetProperties())
                ExpectInOpening($"AiStep.{p.Name}", nameof(AiOpening.BuildOrder), first.BuildOrder.SetItem(0, WithField(first.BuildOrder[0], p)));
            foreach (PropertyInfo p in typeof(AiComposition).GetProperties())
                ExpectInOpening($"AiComposition.{p.Name}", nameof(AiOpening.Compositions), first.Compositions.SetItem(0, WithField(first.Compositions[0], p)));
        }
        // A new field must be added to ContentHash (these counts fail first).
        Assert.Equal((9, 6, 3), (typeof(AiDifficultyDef).GetProperties().Length, typeof(AiStep).GetProperties().Length, typeof(AiComposition).GetProperties().Length));
        Assert.Equal(18 * d.Ai.Length, fields); // M5-9a: counterStrength
        GameData noAi = With(d);
        typeof(GameData).GetProperty(nameof(GameData.Ai))!.SetValue(noAi, ImmutableArray<AiDef>.Empty);
        Assert.NotEqual(baseline, noAi.ContentHash());
    }

    /// <summary>A copy of <paramref name="ai"/> with <paramref name="openings"/>.</summary>
    private static AiDef WithOpenings(AiDef ai, ImmutableArray<AiOpening> openings)
    {
        AiDef copy = Clone(ai);
        typeof(AiDef).GetProperty(nameof(AiDef.Openings))!.SetValue(copy, openings);
        return copy;
    }

    /// <summary>A copy of <paramref name="o"/> with id <paramref name="id"/>.</summary>
    private static AiOpening Renamed(AiOpening o, string id)
    {
        AiOpening copy = Clone(o);
        typeof(AiOpening).GetProperty(nameof(AiOpening.Id))!.SetValue(copy, id);
        return copy;
    }

    /// <summary>
    /// M5-8a: the openings' order is content (it is the pick's index): two openings swapped move the hash, and so do an
    /// opening's id, display name and description, and the opening count (one dropped).
    /// </summary>
    [Fact]
    public void OpeningOrder_Id_Names_AndCount_ChangeTheHash()
    {
        GameData d = TestSim.Data;
        AiDef ai = d.Ai[0];
        AiOpening a = ai.Openings[0], b = Renamed(a, "second");
        ulong ab = With(d, ai: WithOpenings(ai, ImmutableArray.Create(a, b))).ContentHash();
        ulong ba = With(d, ai: WithOpenings(ai, ImmutableArray.Create(b, a))).ContentHash();
        Assert.NotEqual(ab, ba);
        Assert.NotEqual(ab, d.ContentHash()); // two openings vs one
        Assert.NotEqual(ab, With(d, ai: WithOpenings(ai, ImmutableArray.Create(a, Renamed(a, "third")))).ContentHash());
        Assert.Equal(ab, With(d, ai: WithOpenings(ai, ImmutableArray.Create(a, Renamed(a, "second")))).ContentHash());
    }

    /// <summary>M5-8a: an edit to an opening's id, displayName or description in the file moves the hash; so does adding one.</summary>
    [Theory]
    [InlineData("id", "\"standard_two\"")]
    [InlineData("displayName", "\"Standard II\"")]
    [InlineData("description", "\"Another line\"")]
    [InlineData("add", "")]
    public void AnOpeningFieldEditInTheFile_ChangesTheHash(string field, string json)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            JsonArray openings = root["openings"]!.AsArray();
            if (field == "add")
            {
                JsonNode copy = openings[0]!.DeepClone();
                copy["id"] = "copy";
                openings.Add(copy);
            }
            else openings[0]![field] = JsonNode.Parse(json);
        });
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("\n", load.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), load.Data!.ContentHash());
    }

    /// <summary>M5-3: an edit to any field of <c>rules.json</c> <c>ai</c> moves the hash.</summary>
    [Theory]
    [InlineData("goldShare", 0.5)]
    [InlineData("houseWhenPopWithin", 3)]
    [InlineData("placementSearchRadius", 24)]
    [InlineData("buildingSpacing", 4)]
    [InlineData("expandHallReach", 30)]
    [InlineData("scoutAfter", 20)]
    [InlineData("scoutRadius", 30)]
    [InlineData("scoutReach", 8)]
    [InlineData("scoutGrid", 40)]
    [InlineData("rallyDistance", 16)]
    [InlineData("rallyReach", 8)]
    [InlineData("waveCooldown", 45)]
    [InlineData("defendRadius", 40)] // M5-4b
    [InlineData("defendFor", 25)]
    [InlineData("workerFleeRadius", 12)]
    [InlineData("castMinTargets", 3)] // M5-4c
    [InlineData("defendRatio", 2)] // M5-4f
    [InlineData("defendMinValue", 200)]
    [InlineData("recallHallHp", 0.4)]
    [InlineData("reserveShift", 0.5)] // M5-4g
    [InlineData("reserveArmyShare", 0.5)] // M5-4g, BUG-1021
    [InlineData("gatherReach", 48)] // M6-S5, BUG-1045
    [InlineData("scoutReplacements", 3)] // M6-SH1, BUG-1052
    [InlineData("spendDownQueue", 3)] // M6-SH1, BUG-1051
    [InlineData("huntMemory", 90)] // M5-4d
    [InlineData("focusReach", 12)] // M5-4c slice 2
    [InlineData("counterMemory", 120)] // M5-9a
    [InlineData("starvedWaveAfter", 90)] // M5-9a round 2
    [InlineData("starvedWaveAfter", 0)]
    [InlineData("starvedMinArmy", 3)]
    [InlineData("farExpandReach", 256)] // M5-9b
    [InlineData("farExpandReach", 0)]
    public void AnAiRulesFieldEdit_ChangesTheHash(string field, double value)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["ai"]![field] = value);
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("\n", load.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), load.Data!.ContentHash());
    }

    /// <summary>M5-9a: an edit to <c>ai.counters</c> (a slot added to a list, or one list's order alone not) moves the hash only when the table changes.</summary>
    [Fact]
    public void AnAiCountersEdit_ChangesTheHash_ButNotAListsOrder()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped(), reordered = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["ai"]!["counters"]!["unique"] = new System.Text.Json.Nodes.JsonArray("line"));
        reordered.EditJson("common/rules.json", r => r["ai"]!["counters"]!["caster"] = new System.Text.Json.Nodes.JsonArray("ranged", "shock"));
        DataLoadResult load = DataLoader.LoadAll(dir.Path), same = DataLoader.LoadAll(reordered.Path);
        Assert.True(load.Ok && same.Ok, string.Join(" | ", load.Errors.Concat(same.Errors)));
        Assert.NotEqual(TestSim.Data.ContentHash(), load.Data!.ContentHash());
        Assert.Equal(TestSim.Data.ContentHash(), same.Data!.ContentHash()); // a set of slots: the order in a list is not content
    }

    /// <summary>M5-3: an edit to any field of a faction's <c>ai.json</c> moves the hash (a dotted path; the value as JSON).</summary>
    [Theory]
    [InlineData("difficulties.normal.thinkInterval", "2")]
    [InlineData("difficulties.normal.buildOrderSlack", "7")]
    [InlineData("difficulties.normal.waveSizeMultiplier", "1.5")]
    [InlineData("difficulties.normal.gatherBonus", "0.1")]
    [InlineData("difficulties.normal.focusFire", "true")]
    [InlineData("difficulties.normal.pullBackWounded", "true")]
    [InlineData("difficulties.normal.useAbilities", "false")]
    [InlineData("difficulties.normal.expandAfter", "500")]
    [InlineData("difficulties.normal.counterStrength", "0.25")] // M5-9a
    [InlineData("openings.0.buildOrder.0.at", "{ \"time\": 1 }")]
    [InlineData("openings.0.buildOrder.0.do", "\"build\"")]
    [InlineData("openings.0.buildOrder.0.count", "3")]
    [InlineData("openings.0.compositions.0.age", "2")]
    [InlineData("openings.0.compositions.0.units", "{ \"malazan_heavy_infantry\": 1 }")]
    [InlineData("openings.0.attack.minArmyPop", "11")]
    [InlineData("openings.0.attack.minArmyCost", "801")]
    [InlineData("retreat.hpFraction", "0.4")]
    [InlineData("retreat.lossFraction", "0.6")]
    public void AnAiJsonFieldEdit_ChangesTheHash(string path, string json)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            string[] parts = path.Split('.');
            JsonNode node = root;
            for (int i = 0; i < parts.Length - 1; i++) node = int.TryParse(parts[i], out int k) ? node[k]! : node[parts[i]]!;
            if (path == "openings.0.buildOrder.0.do") node["id"] = "malazan_billet"; // a build step names a building
            if (path == "openings.0.compositions.0.age") // two ages swapped: still one per age
            {
                root["openings"]![0]!["compositions"]![1]!["age"] = 1;
            }
            node[parts[^1]] = JsonNode.Parse(json);
        });
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("\n", load.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), load.Data!.ContentHash());
    }

    /// <summary>
    /// M4-3b: a building's attack is hashed: every <see cref="AttackDef"/> field of a tower's, changed in turn, and the attack
    /// taken away (null) or given to a building without one, each change the hash.
    /// </summary>
    [Fact]
    public void TowerAttack_EveryField_AndItsPresence_ChangeTheHash()
    {
        GameData d = TestSim.Data;
        ulong baseline = d.ContentHash();
        BuildingDef tower = d.Buildings[d.FindBuilding("malazan_watchtower")];
        AttackDef attack = tower.Attack!;
        int fields = 0;
        foreach (PropertyInfo p in Settable(typeof(AttackDef)))
        {
            object? changed = Changed(p.GetValue(attack), p.PropertyType);
            Assert.True(changed != null, $"AttackDef.{p.Name}: unknown field type {p.PropertyType}");
            AttackDef a = Clone(attack);
            p.SetValue(a, changed);
            BuildingDef b = Clone(tower);
            typeof(BuildingDef).GetProperty(nameof(BuildingDef.Attack))!.SetValue(b, a);
            Assert.True(With(d, building: b, buildingSlot: tower.Id).ContentHash() != baseline, $"a building's AttackDef.{p.Name} is not in GameData.ContentHash");
            fields++;
        }
        Assert.True(fields >= 12, $"only {fields} attack fields checked");
        BuildingDef none = Clone(tower);
        typeof(BuildingDef).GetProperty(nameof(BuildingDef.Attack))!.SetValue(none, null);
        Assert.NotEqual(baseline, With(d, building: none, buildingSlot: tower.Id).ContentHash());
        BuildingDef keep = d.Buildings[d.FindBuilding("malazan_garrison_keep")];
        BuildingDef armed = Clone(keep);
        typeof(BuildingDef).GetProperty(nameof(BuildingDef.Attack))!.SetValue(armed, attack);
        Assert.NotEqual(baseline, With(d, building: armed, buildingSlot: keep.Id).ContentHash());
    }
}
