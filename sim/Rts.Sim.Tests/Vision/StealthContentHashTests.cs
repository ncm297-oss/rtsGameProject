using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-5 criterion 4: the stealth data moves <see cref="GameData.ContentHash"/>: the two status kinds, the two rules fields
/// and a unit's <c>detector</c> (each field of each def is also covered by <c>DataContentHashTests.ChangingAnyFieldOfAnyDef_ChangesTheHash</c>,
/// which walks every settable property). In its own file so the data track's identical <c>DataContentHashTests.cs</c> hunk merges.
/// </summary>
public class StealthContentHashTests
{
    private static ulong HashWith(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        edit(dir);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        return r.Data!.ContentHash();
    }

    private static JsonObject Status(JsonObject root, string id) =>
        root["statuses"]!.AsArray().Select(n => n!.AsObject()).Single(s => (string)s["id"]! == id);

    [Fact]
    public void TheStealthKinds_TheRulesFields_AndADetector_EachChangeTheHash()
    {
        ulong shipped = TestSim.Data.ContentHash();
        Assert.Equal(shipped, HashWith(_ => { })); // a copy hashes as the original
        var hashes = new List<ulong>
        {
            shipped,
            // M4-H3 (BUG-0481): a stealthed status needs the revealed one, so the revealed kind changes only with it.
            HashWith(d => d.EditJson("common/statuses.json", r => Status(r, "stealthed")["kind"] = "frenzied")),
            HashWith(d => d.EditJson("common/statuses.json", r =>
            {
                Status(r, "stealthed")["kind"] = "frenzied";
                Status(r, "revealed")["kind"] = "frenzied";
            })),
            HashWith(d => d.EditJson("common/statuses.json", r => r["statuses"]!.AsArray().Remove(Status(r, "stealthed")))),
            HashWith(d => d.EditJson("common/statuses.json", r =>
            {
                r["statuses"]!.AsArray().Remove(Status(r, "stealthed"));
                r["statuses"]!.AsArray().Remove(Status(r, "revealed"));
            })),
            HashWith(d => d.EditJson("common/rules.json", r => r["stealth"]!["proximity"] = 3.5)),
            HashWith(d => d.EditJson("common/rules.json", r => r["stealth"]!["revealSeconds"] = 4)),
            // Not 10 m: the shipped casters' own value since D12.
            HashWith(d => d.SetUnitField("malazan", "malazan_cadre_mage", "detector", "12")),
            HashWith(d => d.SetUnitField("malazan", "malazan_cadre_mage", "detector", "13")),
        };
        Assert.Equal(hashes.Count, hashes.Distinct().Count());
    }
}
