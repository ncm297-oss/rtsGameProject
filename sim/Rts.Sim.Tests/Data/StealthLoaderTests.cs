using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-5 criterion 5: the stealth schema. A unit's optional <c>detector</c> (m, above 0, at most <see cref="DataLimits.MaxSight"/>),
/// <c>rules.json</c> <c>stealth {proximity, revealSeconds}</c> (required, above 0), the <c>stealthed</c> / <c>revealed</c>
/// status kinds (no magnitude; one revealed status), each refused at its file and path. Also BUG-0452 items 1-2: a summon
/// whose effects are all spawns needs no <c>affects</c> and refuses a <c>radius</c>.
/// </summary>
public class StealthLoaderTests
{
    private const string Rules = "common/rules.json";
    private const string Statuses = "common/statuses.json";
    private const string MalazanUnits = "factions/malazan/units.json";
    private const string MalazanAbilities = "factions/malazan/abilities.json";

    private static DataLoadResult Load(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        edit(dir);
        return DataLoader.LoadAll(dir.Path);
    }

    private static void AssertErrorAt(DataLoadResult r, string file, string path, string? text = null)
    {
        Assert.False(r.Ok);
        Assert.True(r.Errors.Any(e => e.File == file && e.Path == path && (text == null || e.Message.Contains(text, StringComparison.Ordinal))),
            $"no error at {file} {path}: " + string.Join("; ", r.Errors));
    }

    /// <summary>The index of <paramref name="unit"/> in the shipped Malazan units file.</summary>
    private static int UnitIndex(string unit)
    {
        JsonArray list = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", "malazan", "units.json")))!["units"]!.AsArray();
        for (int k = 0; k < list.Count; k++)
            if ((string)list[k]!["id"]! == unit) return k;
        throw new InvalidOperationException(unit);
    }

    [Fact]
    public void Shipped_TheKindsAndRulesLoad_AndOnlyTheCastersDetect()
    {
        GameData d = TestSim.Data;
        StatusDef stealthed = d.Statuses[d.FindStatus("stealthed")];
        StatusDef revealed = d.Statuses[d.FindStatus("revealed")];
        Assert.Equal((StatusKind.Stealthed, "Stealthed"), (stealthed.Kind, stealthed.DisplayName));
        Assert.Equal((StatusKind.Revealed, "Revealed"), (revealed.Kind, revealed.DisplayName));
        Assert.DoesNotContain(stealthed.Description, char.IsDigit);
        Assert.DoesNotContain(revealed.Description, char.IsDigit);
        Assert.Equal((4, 5), ((int)StatusKind.Stealthed, (int)StatusKind.Revealed));
        Assert.Equal("stealthed", DataLimits.StatusKindIds[4]);
        Assert.Equal("revealed", DataLimits.StatusKindIds[5]);
        Assert.Equal(3f, d.Rules.StealthProximity);
        Assert.Equal(60, d.Rules.RevealTicks);
        // docs/02: every caster detects at 10 m (D12), no other unit detects; the towers have theirs.
        Assert.All(d.Units, u => Assert.Equal(u.Slot == UnitSlot.Caster ? 10f : 0f, u.Detector));
        Assert.Equal(16f, d.Buildings[d.FindBuilding("malazan_watchtower")].Detector);
    }

    [Theory]
    [InlineData(0.0, "must be positive")]
    [InlineData(-3.0, "must be positive")]
    [InlineData(64.5, "above the maximum")]
    public void ADetector_RefusesZeroNegativeAndAboveMaxSight(double value, string text)
    {
        int k = UnitIndex("malazan_cadre_mage");
        AssertErrorAt(Load(dir => dir.SetUnitField("malazan", "malazan_cadre_mage", "detector", value.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            MalazanUnits, $"units[{k}].detector", text);
    }

    [Fact]
    public void ADetector_At64m_Loads_AndChangesTheHash()
    {
        DataLoadResult r = Load(dir => dir.SetUnitField("malazan", "malazan_cadre_mage", "detector", "64"));
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(64f, r.Data!.Units[r.Data.FindUnit("malazan_cadre_mage")].Detector);
        Assert.NotEqual(TestSim.Data.ContentHash(), r.Data.ContentHash());
    }

    [Theory]
    [InlineData("proximity", 0.0)]
    [InlineData("proximity", -1.0)]
    [InlineData("proximity", 65.0)]
    [InlineData("revealSeconds", 0.0)]
    [InlineData("revealSeconds", -3.0)]
    [InlineData("revealSeconds", 0.01)] // rounds to 0 ticks
    public void TheStealthRules_RefuseZeroAndNegative(string field, double value)
    {
        AssertErrorAt(Load(dir => dir.EditJson(Rules, root => root["stealth"]![field] = value)), Rules, "stealth." + field);
    }

    [Theory]
    [InlineData("proximity")]
    [InlineData("revealSeconds")]
    public void TheStealthRules_AreRequired(string field)
    {
        AssertErrorAt(Load(dir => dir.EditJson(Rules, root => root["stealth"]!.AsObject().Remove(field))), Rules, "stealth." + field, "missing");
    }

    [Fact]
    public void TheStealthObject_IsRequired()
    {
        AssertErrorAt(Load(dir => dir.EditJson(Rules, root => root.Remove("stealth"))), Rules, "stealth", "missing");
    }

    [Fact]
    public void ASecondRevealedStatus_IsRefused_AtItsKind()
    {
        DataLoadResult r = Load(dir => dir.EditJson(Statuses, root => root["statuses"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "unmasked", ["displayName"] = "Unmasked", ["description"] = "Test.", ["kind"] = "revealed",
        })));
        AssertErrorAt(r, Statuses, "statuses[6].kind", "already the revealed status");
    }

    /// <summary>
    /// BUG-0481: a <c>stealthed</c> status needs exactly one <c>revealed</c> one (an attack or cast from stealth applies it);
    /// the shipped data minus <c>revealed</c> is refused with one error, at <c>statuses</c>, naming the missing kind. Data
    /// with neither kind still loads (nothing can be stealthed, so nothing needs revealing).
    /// </summary>
    [Fact]
    public void AStealthedStatusWithoutARevealedOne_IsRefused_AtStatuses_WithOneError()
    {
        DataLoadResult r = Load(dir => dir.EditJson(Statuses, root => Remove(root, "revealed")));
        AssertErrorAt(r, Statuses, "statuses", "no status has kind 'revealed'");
        Assert.Single(r.Errors);
        DataLoadResult neither = Load(dir => dir.EditJson(Statuses, root =>
        {
            Remove(root, "revealed");
            Remove(root, "stealthed");
        }));
        Assert.True(neither.Ok, string.Join("; ", neither.Errors));
    }

    private static void Remove(JsonNode root, string id)
    {
        JsonArray list = root["statuses"]!.AsArray();
        for (int i = list.Count - 1; i >= 0; i--)
            if ((string)list[i]!["id"]! == id) list.RemoveAt(i);
    }

    [Theory]
    [InlineData("stealthed")]
    [InlineData("revealed")]
    public void StealthKinds_TakeNoMagnitude_NorSightReachOrDamageType(string status)
    {
        DataLoadResult withMagnitude = Load(dir => dir.EditJson(MalazanAbilities, root =>
        {
            JsonObject telas = root["abilities"]!.AsArray().Select(n => n!.AsObject()).Single(a => (string)a["id"]! == "telas_fire");
            telas["effects"]!.AsArray().Add(new JsonObject { ["kind"] = "applyStatus", ["status"] = status, ["magnitude"] = 1, ["duration"] = 3 });
        }));
        Assert.False(withMagnitude.Ok);
        Assert.Contains(withMagnitude.Errors, e => e.File == MalazanAbilities && e.Path.EndsWith(".magnitude", StringComparison.Ordinal) && e.Message.Contains("no magnitude", StringComparison.Ordinal));
        DataLoadResult without = Load(dir => dir.EditJson(MalazanAbilities, root =>
        {
            JsonObject telas = root["abilities"]!.AsArray().Select(n => n!.AsObject()).Single(a => (string)a["id"]! == "telas_fire");
            telas["effects"]!.AsArray().Add(new JsonObject { ["kind"] = "applyStatus", ["status"] = status, ["duration"] = 3 });
        }));
        Assert.True(without.Ok, string.Join("; ", without.Errors));
        int index = status == "stealthed" ? 4 : 5; // the file's order
        foreach (string field in new[] { "sight", "reach", "damageType" })
        {
            DataLoadResult r = Load(dir => dir.EditJson(Statuses, root =>
                root["statuses"]!.AsArray().Select(n => n!.AsObject()).Single(s => (string)s["id"]! == status)[field] = field == "damageType" ? "magic" : 2));
            AssertErrorAt(r, Statuses, $"statuses[{index}].{field}");
        }
    }

    // ---------- BUG-0452 items 1-2 ----------

    private static JsonObject SpawnOnly(bool affects, bool radius)
    {
        var a = new JsonObject
        {
            ["id"] = "zz_test_spawn_only", ["displayName"] = "Test", ["description"] = "Test only.", ["kind"] = "summon",
            ["castTime"] = 0.5, ["cooldown"] = 10, ["autocast"] = false,
            ["effects"] = new JsonArray(new JsonObject { ["kind"] = "spawn", ["unit"] = "malazan_heavy_infantry", ["count"] = 2, ["lifetime"] = 5 }),
        };
        if (affects) a["affects"] = "own_units";
        if (radius) a["radius"] = 4;
        return a;
    }

    private static DataLoadResult LoadAbility(JsonObject ability) => Load(dir =>
    {
        dir.EditJson(MalazanAbilities, root => root["abilities"]!.AsArray().Add(ability));
        dir.SetUnitField("malazan", "malazan_cadre_mage", "abilities", "[\"telas_fire\", \"zz_test_spawn_only\"]");
    });

    [Fact]
    public void Bug0452_ASpawnOnlySummon_NeedsNoAffects_AndDefaultsToOwnUnits()
    {
        DataLoadResult r = LoadAbility(SpawnOnly(affects: false, radius: false));
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        AbilityDef a = r.Data!.Abilities[r.Data.FindAbility("zz_test_spawn_only")];
        Assert.Equal((AbilityKind.Summon, AbilityAffects.OwnUnits, 0f), (a.Kind, a.Affects, a.Radius));
    }

    [Fact]
    public void Bug0452_ASpawnOnlySummon_RefusesARadius()
    {
        int k = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", "malazan", "abilities.json")))!["abilities"]!.AsArray().Count;
        AssertErrorAt(LoadAbility(SpawnOnly(affects: true, radius: true)), MalazanAbilities, $"abilities[{k}].radius", "spawn effects only has no radius");
    }

    [Fact]
    public void Bug0452_ASummonWithAnotherEffect_StillNeedsAffectsAndRadius()
    {
        JsonObject a = SpawnOnly(affects: false, radius: false);
        a["effects"]!.AsArray().Add(new JsonObject { ["kind"] = "damage", ["type"] = "magic", ["amount"] = 5 });
        int k = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", "malazan", "abilities.json")))!["abilities"]!.AsArray().Count;
        DataLoadResult r = LoadAbility(a);
        AssertErrorAt(r, MalazanAbilities, $"abilities[{k}].affects", "missing");
        AssertErrorAt(r, MalazanAbilities, $"abilities[{k}].radius", "missing");
    }
}
