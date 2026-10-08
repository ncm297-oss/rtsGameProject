using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-2b criterion 1: <c>common/projectiles.json</c> (sim-owned schema): the five shipped ids, every <c>attack.projectile</c>
/// resolved, and the loader's errors (missing id, bad kind, non-positive speed, unknown field, a hit tolerance on a lob,
/// a lob without splash), each at its field.
/// </summary>
public class ProjectileLoaderTests
{
    private static DataLoadResult LoadWith(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        edit(dir);
        return DataLoader.LoadAll(dir.Path);
    }

    private static void EditProjectile(TestDataDir dir, string id, Action<JsonObject> edit) =>
        dir.EditJson("common/projectiles.json", root =>
        {
            foreach (JsonNode? p in root["projectiles"]!.AsArray())
                if ((string?)p!["id"] == id) edit(p.AsObject());
        });

    private static void AssertErrorAt(DataLoadResult r, string file, string path)
    {
        Assert.False(r.Ok);
        Assert.Null(r.Data);
        Assert.True(r.Errors.Any(e => e.File == file && e.Path == path), $"no error at {file} {path}; got:\n" + string.Join("\n", r.Errors));
    }

    [Fact]
    public void Shipped_HasExactlyTheFiveIds_WithTheDocsSpeedsAndTolerance()
    {
        GameData d = TestSim.Data;
        Assert.Equal(new[] { "arrow", "bolt", "catapult_stone", "magic_bolt", "sharper" }, d.Projectiles.Select(p => p.Key).ToArray());
        foreach (ProjectileDef p in d.Projectiles)
        {
            Assert.Equal(d.FindProjectile(p.Key), p.Id);
            bool lob = p.Key is "catapult_stone" or "sharper";
            Assert.Equal(lob ? ProjectileKind.Lob : ProjectileKind.Aimed, p.Kind);
            // docs/02 "Projectiles": aimed 25 m/s, lobs 12 m/s; per tick at 20 Hz.
            Assert.Equal((lob ? 12f : 25f) / 20f, p.SpeedPerTick, 6);
            Assert.Equal(lob ? 0f : 0.3f, p.HitTolerance, 6);
        }
    }

    [Fact]
    public void Shipped_EveryAttackProjectile_Resolves_AndOnlyTheRamAndMeleeHaveNone()
    {
        GameData d = TestSim.Data;
        foreach (UnitDef u in d.Units)
        {
            if (u.Attack.Projectile == null)
            {
                Assert.Equal(-1, u.Attack.ProjectileTypeId);
                continue;
            }
            Assert.True(u.Attack.ProjectileTypeId >= 0, u.Key);
            Assert.Equal(u.Attack.Projectile, d.Projectiles[u.Attack.ProjectileTypeId].Key);
        }
        // The ram is a melee siege unit (Producer decision f): no projectile.
        Assert.Null(d.Units[d.FindUnit("whirlwind_battering_ram")].Attack.Projectile);
        Assert.Equal("bolt", d.Units[d.FindUnit("malazan_crossbowman")].Attack.Projectile);
    }

    [Fact]
    public void AnUnknownProjectileId_IsAnErrorAtTheUnitsAttackProjectile()
    {
        DataLoadResult r = LoadWith(dir => dir.SetUnitField("malazan", "malazan_crossbowman", "attack.projectile", "\"quarrel\""));
        AssertErrorAt(r, "factions/malazan/units.json", "units[2].attack.projectile");
        Assert.Contains(r.Errors, e => e.Message.Contains("unknown projectile 'quarrel'"));
    }

    [Fact]
    public void AProjectileIdRemovedFromTheFile_IsAnErrorAtEveryUnitNamingIt()
    {
        DataLoadResult r = LoadWith(dir => dir.EditJson("common/projectiles.json", root =>
        {
            JsonArray list = root["projectiles"]!.AsArray();
            for (int k = list.Count - 1; k >= 0; k--)
                if ((string?)list[k]!["id"] == "magic_bolt") list.RemoveAt(k);
        }));
        Assert.False(r.Ok);
        // Both casters fire it.
        Assert.Equal(2, r.Errors.Count(e => e.Path.EndsWith("attack.projectile") && e.Message.Contains("'magic_bolt'")));
    }

    [Theory]
    [InlineData("\"Aimed\"")]
    [InlineData("\"arc\"")]
    [InlineData("\"\"")]
    public void ABadKind_IsAnErrorAtTheKind(string raw)
    {
        DataLoadResult r = LoadWith(dir => EditProjectile(dir, "bolt", p => p["kind"] = JsonNode.Parse(raw)));
        AssertErrorAt(r, "common/projectiles.json", "projectiles[1].kind");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-25")]
    [InlineData(null)]
    public void ANonPositiveOrMissingSpeed_IsAnErrorAtTheSpeed(string? raw)
    {
        DataLoadResult r = LoadWith(dir => EditProjectile(dir, "arrow", p =>
        {
            if (raw == null) p.Remove("speed");
            else p["speed"] = JsonNode.Parse(raw);
        }));
        AssertErrorAt(r, "common/projectiles.json", "projectiles[0].speed");
    }

    [Fact]
    public void AnUnknownField_IsAnError()
    {
        DataLoadResult r = LoadWith(dir => EditProjectile(dir, "arrow", p => p["sped"] = 25));
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == "common/projectiles.json" && e.Message.Contains("sped"));
    }

    [Fact]
    public void AHitToleranceOnALob_IsAnErrorAtTheField()
    {
        DataLoadResult r = LoadWith(dir => EditProjectile(dir, "catapult_stone", p => p["hitTolerance"] = 0.3));
        AssertErrorAt(r, "common/projectiles.json", "projectiles[2].hitTolerance");
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("2.5")]
    public void AnOutOfRangeHitTolerance_IsAnErrorAtTheField(string raw)
    {
        DataLoadResult r = LoadWith(dir => EditProjectile(dir, "bolt", p => p["hitTolerance"] = JsonNode.Parse(raw)));
        AssertErrorAt(r, "common/projectiles.json", "projectiles[1].hitTolerance");
    }

    [Fact]
    public void AnAimedProjectileWithoutHitTolerance_GetsTheDocsDefault()
    {
        DataLoadResult r = LoadWith(dir => EditProjectile(dir, "bolt", p => p.Remove("hitTolerance")));
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal((float)DataLimits.DefaultHitTolerance, r.Data!.Projectiles[r.Data.FindProjectile("bolt")].HitTolerance);
        Assert.Equal(0.3, DataLimits.DefaultHitTolerance);
    }

    [Fact]
    public void ADuplicateId_IsAnErrorAtItsSecondDefinition()
    {
        DataLoadResult r = LoadWith(dir => EditProjectile(dir, "bolt", p => p["id"] = "arrow"));
        AssertErrorAt(r, "common/projectiles.json", "projectiles[1].id");
    }

    [Fact]
    public void TheFileMissing_IsOneError_NotOneMorePerUnit()
    {
        DataLoadResult r = LoadWith(dir => File.Delete(dir.FullPath("common/projectiles.json")));
        Assert.False(r.Ok);
        Assert.Single(r.Errors);
        Assert.Equal("common/projectiles.json", r.Errors[0].File);
    }

    [Fact]
    public void ALobFiredByAnAttackWithoutSplash_IsAnErrorAtTheSplash()
    {
        // A lob always explodes, and the explosion is the attack's splash: with none it would do nothing.
        DataLoadResult r = LoadWith(dir => dir.SetUnitField("malazan", "malazan_catapult", "attack.splash", null));
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == "factions/malazan/units.json" && e.Path.EndsWith("attack.splash") && e.Message.Contains("lob"));
    }

    [Fact]
    public void TheFile_IsInTheContentHash()
    {
        DataLoadResult faster = LoadWith(dir => EditProjectile(dir, "bolt", p => p["speed"] = 26));
        Assert.True(faster.Ok, string.Join("\n", faster.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), faster.Data!.ContentHash());
        DataLoadResult wider = LoadWith(dir => EditProjectile(dir, "arrow", p => p["hitTolerance"] = 0.4));
        Assert.NotEqual(TestSim.Data.ContentHash(), wider.Data!.ContentHash());
        DataLoadResult kind = LoadWith(dir =>
        {
            EditProjectile(dir, "magic_bolt", p => { p["kind"] = "lob"; p.Remove("hitTolerance"); });
        });
        Assert.True(kind.Ok, string.Join("\n", kind.Errors)); // both casters have splash, so a lob is allowed
        Assert.NotEqual(TestSim.Data.ContentHash(), kind.Data!.ContentHash());
    }
}
