using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-3b criterion 1: a building's optional <c>attack</c> (the unit attack object) and <c>detector</c> (sim-owned schema):
/// the shipped towers' values (docs/02 "Buildings": "Attack 10 pierce / 2 s, range 18; sight 24; detector 16 m"), the
/// defaults, and the loader's refusals, each an error at its field naming the building.
/// </summary>
public class TowerAttackLoaderTests
{
    private const string MalazanBuildings = "factions/malazan/buildings.json";

    private static DataLoadResult LoadWith(Action<JsonObject> editTower)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanBuildings, root =>
            editTower(root["buildings"]!.AsArray().Select(n => n!.AsObject()).Single(b => (string)b["id"]! == "malazan_watchtower")));
        return DataLoader.LoadAll(dir.Path);
    }

    private static int TowerIndex()
    {
        JsonArray list = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", "malazan", "buildings.json")))!["buildings"]!.AsArray();
        for (int i = 0; i < list.Count; i++)
            if ((string)list[i]!["id"]! == "malazan_watchtower") return i;
        throw new InvalidOperationException("no malazan_watchtower");
    }

    /// <summary>Asserts the load failed with an error at <c>buildings[tower].field</c>, naming the tower unless <paramref name="named"/> is false (the generic number readers' errors).</summary>
    private static void AssertErrorAt(DataLoadResult r, string field, bool named = true)
    {
        Assert.False(r.Ok);
        string path = $"buildings[{TowerIndex()}].{field}";
        Assert.True(r.Errors.Any(e => e.File == MalazanBuildings && e.Path == path && (!named || e.Message.Contains("malazan_watchtower"))),
            $"no error at {path}: " + string.Join("; ", r.Errors));
    }

    [Fact]
    public void Shipped_BothTowersShoot10PierceEvery2s_Range18_Detector16_NoOtherBuildingShoots()
    {
        GameData d = TestSim.Data;
        int pierce = d.DamageTable.DamageTypeKeys.IndexOf("pierce");
        int towers = 0;
        foreach (BuildingDef b in d.Buildings)
        {
            if (b.Slot != BuildingSlot.WatchTower)
            {
                Assert.True(b.Attack == null, $"{b.Key} has an attack");
                Assert.Equal(0f, b.Detector);
                continue;
            }
            towers++;
            AttackDef a = b.Attack!;
            Assert.NotNull(a);
            Assert.Equal(10, a.Value);
            Assert.Equal(pierce, a.DamageType);
            Assert.Equal(40, a.CooldownTicks); // 2 s
            Assert.Equal(18f, a.Range);
            Assert.Equal(8, a.WindupTicks); // docs/02 "Stats": the ranged default 0.4 s
            Assert.Equal(0f, a.MinRange);
            Assert.Equal(0f, a.Splash);
            Assert.True(a.ProjectileTypeId >= 0);
            Assert.Equal(ProjectileKind.Aimed, d.Projectiles[a.ProjectileTypeId].Kind);
            Assert.Equal(AttackTargets.Units, a.Targets);
            Assert.Equal(16f, b.Detector);
            Assert.Equal(24f, b.Sight);
        }
        Assert.Equal(2, towers);
    }

    [Fact]
    public void ATowerWithoutAttackOrDetector_Loads_AndNeverShoots()
    {
        DataLoadResult r = LoadWith(t =>
        {
            t.Remove("attack");
            t.Remove("detector");
        });
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        BuildingDef tower = r.Data!.Buildings[r.Data.FindBuilding("malazan_watchtower")];
        Assert.Null(tower.Attack);
        Assert.Equal(0f, tower.Detector);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-18")]
    public void ARangeOfZeroOrLess_IsRefusedAtTheRange(string range)
    {
        DataLoadResult r = LoadWith(t => t["attack"]!["range"] = JsonNode.Parse(range));
        AssertErrorAt(r, "attack.range", named: range == "0"); // a negative number is the reader's "must not be negative"
        Assert.Single(r.Errors);
    }

    [Fact]
    public void AValueOfZero_IsRefusedAtTheValue() =>
        AssertErrorAt(LoadWith(t => t["attack"]!["value"] = 0), "attack.value");

    [Fact]
    public void ALobProjectile_IsRefusedAtTheProjectile()
    {
        DataLoadResult r = LoadWith(t =>
        {
            t["attack"]!["projectile"] = "catapult_stone";
            t["attack"]!["splash"] = 2; // a lob needs a splash anyway: only the building rule is left to break
        });
        AssertErrorAt(r, "attack.projectile");
        Assert.Single(r.Errors);
    }

    [Fact]
    public void NoProjectile_IsRefusedAtTheProjectile() =>
        AssertErrorAt(LoadWith(t => t["attack"]!.AsObject().Remove("projectile")), "attack.projectile");

    [Fact]
    public void TargetsBuildings_IsRefusedAtTheTargets()
    {
        DataLoadResult r = LoadWith(t => t["attack"]!["targets"] = "buildings");
        AssertErrorAt(r, "attack.targets");
        Assert.Single(r.Errors);
    }

    [Theory]
    [InlineData("all")]
    [InlineData(null)]
    public void TargetsAllOrAbsent_Load(string? targets)
    {
        DataLoadResult r = LoadWith(t =>
        {
            if (targets == null) t["attack"]!.AsObject().Remove("targets");
            else t["attack"]!["targets"] = targets;
        });
        Assert.True(r.Ok, string.Join("\n", r.Errors));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("64.5")]
    [InlineData("1e300")]
    public void ADetectorOfZeroOrLessOrPast64m_IsRefusedAtTheDetector(string raw)
    {
        DataLoadResult r = LoadWith(t => t["detector"] = JsonNode.Parse(raw));
        AssertErrorAt(r, "detector");
        DataError e = Assert.Single(r.Errors);
        Assert.Contains("detector", e.Message);
        Assert.DoesNotContain("Infinity", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADetectorOf64m_Loads()
    {
        DataLoadResult r = LoadWith(t => t["detector"] = 64);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(64f, r.Data!.Buildings[r.Data.FindBuilding("malazan_watchtower")].Detector);
    }

    [Fact]
    public void AnUnknownAttackField_IsAnErrorAtIt()
    {
        DataLoadResult r = LoadWith(t => t["attack"]!["rate"] = 2);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == MalazanBuildings && e.Path.Contains("rate"));
    }

    [Fact]
    public void AnAttackOnAnyBuilding_LoadsTheSameWay()
    {
        // The hook is the field, not the slot: a Town Hall with the tower's attack shoots too (data-driven, rule 6).
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanBuildings, root =>
            root["buildings"]!.AsArray()[0]!.AsObject()["attack"] = JsonNode.Parse(
                "{ \"value\": 5, \"type\": \"pierce\", \"cooldown\": 3, \"range\": 10, \"windup\": 0, \"projectile\": \"arrow\" }"));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        AttackDef a = r.Data!.Buildings[r.Data.FindBuilding("malazan_garrison_keep")].Attack!;
        Assert.Equal((5, 60, 10f, 0, AttackTargets.All), (a.Value, a.CooldownTicks, a.Range, a.WindupTicks, a.Targets));
    }
}
