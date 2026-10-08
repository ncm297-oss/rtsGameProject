using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-2b (session 2026-10-08-0913): malformed <c>common/projectiles.json</c> beyond the developer's rows. Every case
/// must be rejected with an error naming the file, never crash, never silently default; and the speed bounds a float
/// tick step can hold.
/// </summary>
public class ProjectileLoaderQaTests
{
    private const string File = "common/projectiles.json";
    private readonly ITestOutputHelper _out;

    public ProjectileLoaderQaTests(ITestOutputHelper output) => _out = output;

    private static DataLoadResult LoadWith(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        edit(dir);
        return DataLoader.LoadAll(dir.Path);
    }

    private static void WriteRaw(TestDataDir dir, string text) => System.IO.File.WriteAllText(dir.FullPath(File), text);

    private static void EditArrow(TestDataDir dir, Action<JsonObject> edit) =>
        dir.EditJson(File, root =>
        {
            foreach (JsonNode? p in root["projectiles"]!.AsArray())
                if ((string?)p!["id"] == "arrow") edit(p.AsObject());
        });

    public static IEnumerable<object[]> Malformed() => new[]
    {
        new object[] { "not json", "{ \"projectiles\": [ " },
        new object[] { "empty object", "{ }" },
        new object[] { "projectiles null", "{ \"projectiles\": null }" },
        new object[] { "projectiles an object", "{ \"projectiles\": { \"id\": \"arrow\" } }" },
        new object[] { "a null entry", "{ \"projectiles\": [ null, { \"id\": \"bolt\", \"kind\": \"aimed\", \"speed\": 25 } ] }" },
        new object[] { "speed a string", "{ \"projectiles\": [ { \"id\": \"arrow\", \"kind\": \"aimed\", \"speed\": \"25\" } ] }" },
        new object[] { "kind a number", "{ \"projectiles\": [ { \"id\": \"arrow\", \"kind\": 0, \"speed\": 25 } ] }" },
        new object[] { "id missing", "{ \"projectiles\": [ { \"kind\": \"aimed\", \"speed\": 25 } ] }" },
        new object[] { "id not snake_case", "{ \"projectiles\": [ { \"id\": \"Arrow\", \"kind\": \"aimed\", \"speed\": 25 } ] }" },
        new object[] { "unknown top-level field", "{ \"projectiles\": [ ], \"extra\": 1 }" },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedFile_IsRejected_WithAnErrorInTheFile(string why, string text)
    {
        DataLoadResult r = LoadWith(dir => WriteRaw(dir, text));
        _out.WriteLine($"{why}: {r.Errors.Count} errors; first: {(r.Errors.Count > 0 ? r.Errors[0].ToString() : "-")}");
        Assert.False(r.Ok, why);
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.File == File);
    }

    [Theory]
    [InlineData("\"0.3\"")]
    [InlineData("true")]
    [InlineData("null")]
    public void AHitToleranceOfTheWrongType_OrNull_IsAnErrorOrTheDefault(string raw)
    {
        DataLoadResult r = LoadWith(dir => EditArrow(dir, p => p["hitTolerance"] = JsonNode.Parse(raw)));
        _out.WriteLine($"hitTolerance {raw}: ok {r.Ok}; {string.Join(" | ", r.Errors)}");
        if (raw == "null")
        {
            // JSON null reads as "absent": the documented default. Pinned so a change is seen.
            Assert.True(r.Ok, string.Join("\n", r.Errors));
            Assert.Equal(0.3f, r.Data!.Projectiles[r.Data.FindProjectile("arrow")].HitTolerance, 5);
        }
        else
        {
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.File == File);
        }
    }

    /// <summary>
    /// A speed the loader accepts must give a usable per-tick step: positive and finite in float. A tiny double (1e-50)
    /// or a huge one (1e40) passes "positive" but becomes 0 or infinity per tick: a projectile that never moves for an
    /// hour, or a velocity of infinity / NaN in the hashed store.
    /// </summary>
    [Theory]
    [InlineData("1e-50")]
    [InlineData("1e40")]
    public void ASpeedThatIsNoFloatStep_IsRejected(string raw)
    {
        DataLoadResult r = LoadWith(dir => EditArrow(dir, p => p["speed"] = JsonNode.Parse(raw)));
        string got = r.Ok ? $"accepted: {r.Data!.Projectiles[r.Data.FindProjectile("arrow")].SpeedPerTick} m a tick" : string.Join(" | ", r.Errors);
        _out.WriteLine($"speed {raw}: {got}");
        Assert.False(r.Ok, $"speed {raw} {got}");
    }

    [Fact]
    public void AnEmptyList_IsRejected_AtEveryUnitNamingAProjectile()
    {
        DataLoadResult r = LoadWith(dir => WriteRaw(dir, "{ \"projectiles\": [ ] }"));
        Assert.False(r.Ok);
        Assert.Equal(6, r.Errors.Count(e => e.Path.EndsWith(".attack.projectile", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnUnusedProjectileId_Loads()
    {
        // Not required by the brief: a sixth, unused id is data for a future unit. Pinned so a change is seen.
        DataLoadResult r = LoadWith(dir => dir.EditJson(File, root =>
            root["projectiles"]!.AsArray().Add(JsonNode.Parse("{ \"id\": \"zz_spare\", \"kind\": \"aimed\", \"speed\": 30 }"))));
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(6, r.Data!.Projectiles.Length);
    }

    [Fact]
    public void AProjectileOnAnAttackWithoutValue_StillResolves_AndTheUnitCannotFight()
    {
        // The Laborer has a melee attack; give it a bolt: it resolves (no rule against it), and nothing crashes.
        DataLoadResult r = LoadWith(dir => dir.SetUnitField("malazan", "malazan_laborer", "attack.projectile", "\"bolt\""));
        _out.WriteLine($"laborer with a bolt: ok {r.Ok}; {string.Join(" | ", r.Errors)}");
        Assert.True(r.Ok || r.Errors.All(e => e.File != File));
    }

    // ---------- round 1 (BUG-0182 item 1, BUG-0183) ----------

    /// <summary>The BUG-0182 floor (<see cref="DataLimits.MinProjectileSpeed"/> = 1 m/s): just under is an error at the field, the floor itself loads.</summary>
    [Theory]
    [InlineData("0.999", false)]
    [InlineData("1", true)]
    [InlineData("1.0001", true)]
    public void TheSpeedFloor_IsInclusive(string raw, bool ok)
    {
        DataLoadResult r = LoadWith(dir => EditArrow(dir, p => p["speed"] = JsonNode.Parse(raw)));
        _out.WriteLine($"speed {raw}: ok {r.Ok}; {string.Join(" | ", r.Errors)}");
        Assert.Equal(ok, r.Ok);
        if (!ok) Assert.Contains(r.Errors, e => e.File == File && e.Path == "projectiles[0].speed");
    }

    /// <summary>
    /// <c>leadSpeed</c> of the wrong JSON type, or one a float tick step cannot hold, must be an error at the field, as a
    /// wrong-type <c>hitTolerance</c> and a 1e40 <c>speed</c> are (never a silent default and never infinity).
    /// </summary>
    [Theory]
    [InlineData("\"5\"")]
    [InlineData("true")]
    [InlineData("[5]")]
    [InlineData("{}")]
    [InlineData("1e40")]
    public void ABadLeadSpeed_IsAnErrorAtTheField(string raw)
    {
        DataLoadResult r = LoadWith(dir => EditArrow(dir, p => p["leadSpeed"] = JsonNode.Parse(raw)));
        string got = r.Ok ? $"accepted: {r.Data!.Projectiles[r.Data.FindProjectile("arrow")].LeadSpeedPerTick} m a tick" : string.Join(" | ", r.Errors);
        _out.WriteLine($"leadSpeed {raw}: {got}");
        Assert.False(r.Ok, $"leadSpeed {raw} {got}");
        Assert.Contains(r.Errors, e => e.File == File && e.Path.Contains("projectiles[0].leadSpeed", StringComparison.Ordinal));
    }

    /// <summary>A <c>leadSpeed</c> of null is the same as none (the default 0, never leads), like a null <c>hitTolerance</c> is its default; pinned either way.</summary>
    [Fact]
    public void ANullLeadSpeed_IsTheDefaultOrAnError()
    {
        DataLoadResult r = LoadWith(dir => EditArrow(dir, p => p["leadSpeed"] = null));
        _out.WriteLine($"leadSpeed null: ok {r.Ok}; {string.Join(" | ", r.Errors)}");
        if (r.Ok) Assert.Equal(0f, r.Data!.Projectiles[r.Data.FindProjectile("arrow")].LeadSpeedPerTick);
        else Assert.Contains(r.Errors, e => e.File == File);
    }

    /// <summary>The lead speed is in the data hash: a replay recorded with one lead speed must not play against another.</summary>
    [Fact]
    public void LeadSpeed_ChangesTheContentHash()
    {
        DataLoadResult a = LoadWith(dir => EditArrow(dir, p => p["leadSpeed"] = 5));
        DataLoadResult b = LoadWith(dir => EditArrow(dir, p => p["leadSpeed"] = 5.5));
        Assert.True(a.Ok && b.Ok);
        Assert.NotEqual(a.Data!.ContentHash(), b.Data!.ContentHash());
    }
}
