using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-6 (BUG-1280): <see cref="BloodOilScenes.Edit"/> is idempotent, so the M7-4 suites keep loading once the data track ships
/// docs/03 "For the data track (M7-4)" rows: on a copy that already carries them it leaves exactly one Frenzy row and one
/// <c>statusDuration</c> effect (+4 s) on the Anointing, and applied twice it gives the data applied once.
/// </summary>
public class BloodOilScenesTests
{
    /// <summary>
    /// docs/03 "For the data track (M7-4)" exactly as written: the four files the data track will change. Idempotent
    /// (BUG-1310): a <c>teblor_frenzied</c> row or Anointing <c>statusDuration</c> effect the files already carry is taken out
    /// before the docs/03 one is appended, so on data that ships the rows the copy still has exactly one of each.
    /// </summary>
    internal static void DataTrackRows(TestDataDir dir)
    {
        dir.EditJson("common/statuses.json", r =>
        {
            JsonArray list = r["statuses"]!.AsArray();
            BloodOilScenes.RemoveWhere(list, n => (string?)n!["id"] == BloodOilScenes.Status);
            list.Add(JsonNode.Parse("""
                { "id": "teblor_frenzied", "displayName": "Blood-oil Frenzy", "description": "...", "kind": "frenzied", "damageBonus": 0.2, "onEnd": { "status": "slowed", "magnitude": 0.2, "duration": 4 } }
                """));
        });
        dir.EditJson("factions/teblor/abilities.json", r =>
        {
            foreach (JsonNode? a in r["abilities"]!.AsArray())
                if ((string)a!["id"]! == "blood_oil_frenzy")
                    a["effects"] = new JsonArray(JsonNode.Parse("""{ "kind": "applyStatus", "status": "teblor_frenzied", "magnitude": 0.4, "duration": 10 }"""));
        });
        dir.EditJson("factions/teblor/techs.json", r =>
        {
            foreach (JsonNode? t in r["techs"]!.AsArray())
                if ((string)t!["id"]! == "teblor_blood_oil_anointing")
                {
                    JsonArray effects = t["effects"]!.AsArray();
                    BloodOilScenes.RemoveWhere(effects, BloodOilScenes.IsStatusDuration);
                    effects.Add(JsonNode.Parse("""{ "stat": "statusDuration", "amount": 4, "statuses": ["teblor_frenzied"] }"""));
                }
        });
        dir.EditJson("factions/teblor/buildings.json", r =>
        {
            foreach (JsonNode? b in r["buildings"]!.AsArray()) b!["scale"] = 1.25;
        });
    }

    private static GameData Load(TestDataDir dir)
    {
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        return r.Data!;
    }

    [Fact]
    public void Edit_OnACopyCarryingTheDataTracksRows_LeavesOneFrenzyRow_AndOneFourSecondStatusDuration()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        DataTrackRows(dir);
        Assert.True(DataLoader.LoadAll(dir.Path).Ok); // the rows themselves load (the premise)
        BloodOilScenes.Edit(dir);
        GameData d = Load(dir);
        Assert.Single(d.Statuses, s => s.Key == BloodOilScenes.Status);
        int frenzy = d.FindStatus(BloodOilScenes.Status);
        Assert.Equal(BloodOilScenes.RowIndex, frenzy); // last, where every M7-4 pin expects it
        TechDef anointing = d.Techs[d.FindTech("teblor_blood_oil_anointing")];
        TechEffect e = Assert.Single(anointing.Effects, x => x.Stat == TechStat.StatusDuration);
        Assert.Equal((80f, frenzy), (e.Amount, Assert.Single(e.Statuses))); // 4 s, not 8
        Assert.Equal(BloodOilScenes.EffectIndex, anointing.Effects.IndexOf(e));
        // The same copy as the M7-4 suites' (the shipped data edited once) in everything the scenes read.
        GameData once = BloodOilScenes.Data;
        Assert.Equal(once.Statuses[BloodOilScenes.Frenzy].DamageBonus, d.Statuses[frenzy].DamageBonus);
        Assert.Equal(once.Statuses[BloodOilScenes.Frenzy].OnEndTicks, d.Statuses[frenzy].OnEndTicks);
        Assert.Equal(BloodOilScenes.FrenzyTicks, d.Abilities[d.FindAbility("blood_oil_frenzy")].Effects[0].DurationTicks);
    }

    [Fact]
    public void Edit_AppliedTwice_HashesAsAppliedOnce()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        BloodOilScenes.Edit(dir);
        BloodOilScenes.Edit(dir);
        GameData twice = Load(dir);
        Assert.Equal(BloodOilScenes.Data.ContentHash(), twice.ContentHash());
        Assert.Single(twice.Statuses, s => s.Key == BloodOilScenes.Status);
    }
}
