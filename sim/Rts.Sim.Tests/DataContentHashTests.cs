using System.Collections.Immutable;
using System.Reflection;
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
        return null;
    }

    private static GameData With(GameData d, DamageTable? table = null, RulesDef? rules = null, FactionDef? faction = null, UnitDef? unit = null,
        ResourceDef? resource = null, int resourceSlot = 0, BuildingDef? building = null, int buildingSlot = 0, TechDef? tech = null, int techSlot = 0,
        ProjectileDef? projectile = null, int projectileSlot = 0, StatusDef? status = null, int statusSlot = 0, AbilityDef? ability = null, int abilitySlot = 0) => new()
    {
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
        };
        foreach ((string field, AbilityEffect changed) in variants)
        {
            AbilityDef copy = Clone(ability);
            typeof(AbilityDef).GetProperty(nameof(AbilityDef.Effects))!.SetValue(copy, ability.Effects.SetItem(0, changed));
            Assert.True(With(d, ability: copy, abilitySlot: ability.Id).ContentHash() != baseline, $"AbilityEffect.{field} is not in GameData.ContentHash");
        }
        Assert.Equal(6, typeof(AbilityEffect).GetProperties().Length); // a new field must be added above and to ContentHash
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
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/resources.json", root => root["resources"]![0]!["footprint"]!["width"] = 3);
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("\n", load.Errors));
        Assert.NotEqual(d.ContentHash(), load.Data!.ContentHash());
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
            "DurationTicks", "Affects", "Effects" })
            Assert.Contains($"AbilityDef.{f}", checkedFields);
        Assert.Contains("UnitDef.Abilities", checkedFields);
        Assert.True(checkedFields.Count >= 57, $"only {checkedFields.Count} fields checked");
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
