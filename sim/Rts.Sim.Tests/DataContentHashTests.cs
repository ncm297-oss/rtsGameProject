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
        return null;
    }

    private static GameData With(GameData d, DamageTable? table = null, RulesDef? rules = null, FactionDef? faction = null, UnitDef? unit = null,
        ResourceDef? resource = null, int resourceSlot = 0) => new()
    {
        DamageTable = table ?? d.DamageTable,
        Rules = rules ?? d.Rules,
        Factions = faction == null ? d.Factions : d.Factions.SetItem(faction.Id, faction),
        Units = unit == null ? d.Units : d.Units.SetItem(unit.Id, unit),
        Resources = resource == null ? d.Resources : d.Resources.SetItem(resourceSlot, resource),
    };

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
        Assert.Contains("AttackDef.Projectile", checkedFields); // nullable: null and "x" must differ
        foreach (string f in new[] { "Id", "Key", "DisplayName", "Description", "Resource", "FootprintWidth", "FootprintHeight" })
            Assert.Contains($"ResourceDef.{f}", checkedFields);
        Assert.True(checkedFields.Count >= 57, $"only {checkedFields.Count} fields checked");
    }
}
