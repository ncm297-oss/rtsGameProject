using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA for D19 (session 2026-10-15-2325): the owner's numbers applied (line units x1.5 vs Mounted, Easy casts, building
/// damage set (C)). The Structure cells re-derived by hand from docs/02's formula in exact decimal arithmetic straight
/// from the JSON (not through the loader's floats); the shipped Easy seat really casts for both factions; the kill-time
/// report's "before D19" copy leaves the shipped files untouched.
/// </summary>
public class D19QaTests
{
    private readonly ITestOutputHelper _out;

    public D19QaTests(ITestOutputHelper output) => _out = output;

    private static JsonNode Json(string rel) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, rel.Replace('/', Path.DirectorySeparatorChar))))!;

    private static string[] Factions() =>
        Directory.GetDirectories(Path.Combine(TestDataDir.Shipped, "factions")).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray()!;

    /// <summary>docs/02 "Damage formula" in decimal: <c>max(1, floor(attack x type x bonus + 0.5) - armor)</c>, armor skipped for an ignoresArmor type.</summary>
    private static int Hand(int attack, decimal type, decimal bonus, int armor, bool ignoresArmor)
    {
        int rounded = (int)Math.Floor(attack * type * bonus + 0.5m);
        return Math.Max(1, ignoresArmor ? rounded : rounded - armor);
    }

    private static (decimal Mult, bool Ignores) StructureCell(string damageType)
    {
        foreach (JsonNode? t in Json("common/damage_table.json")["damageTypes"]!.AsArray())
            if ((string)t!["id"]! == damageType)
                return ((decimal)t["multipliers"]!["structure"]!, t["ignoresArmor"] is JsonNode n && (bool)n);
        throw new InvalidOperationException(damageType);
    }

    private static int BuildingArmor(string id)
    {
        foreach (string f in Factions())
            foreach (JsonNode? b in Json($"factions/{f}/buildings.json")["buildings"]!.AsArray())
                if ((string)b!["id"]! == id) return (int)b["armor"]!;
        throw new InvalidOperationException(id);
    }

    private static int Engine(string unit, string building, int attackBonus = 0)
    {
        GameData d = TestSim.Data;
        DamageTable t = d.DamageTable;
        return DamageCalc.Compute(t, d.Units[d.FindUnit(unit)].Attack, attackBonus, t.ArmorClassKeys.IndexOf("structure"), d.Buildings[d.FindBuilding(building)].Armor);
    }

    /// <summary>
    /// The three cells QA derived by hand (docs/02's formula, the shipped files): Heavy Infantry on a Town Hall
    /// 10 x 0.7 = 7, less armor 2 = 5 (was 10 x 0.4 = 4, less 5, floored to 1); Crossbowman on an infantry hall
    /// 9 x 0.35 = 3.15 -> 3, less armor 2 = 1 (still on the floor, docs/02's "by design"); Raider on a House
    /// 11 x 0.7 = 7.7 -> 8, less armor 1 = 7 (was 1). Plus the Cadre Mage (magic ignores armor) 9 x 0.6 = 5.4 -> 5,
    /// the Sapper's Sharpers 20 x 3.0 - 2 = 58 on a hall.
    /// </summary>
    [Theory]
    [InlineData("malazan_heavy_infantry", "whirlwind_holy_camp", 5)]
    [InlineData("malazan_crossbowman", "whirlwind_raider_camp", 1)]
    [InlineData("whirlwind_raider", "malazan_billet", 7)]
    [InlineData("malazan_cadre_mage", "whirlwind_raider_camp", 5)]
    [InlineData("malazan_sapper", "whirlwind_raider_camp", 58)]
    public void HandDerivedStructureCells_MatchTheEngine(string unit, string building, int expected) =>
        Assert.Equal(expected, Engine(unit, building));

    /// <summary>
    /// Every shipped unit's attack against every shipped building, attack bonus 0-3 (the Forge's range and more), by
    /// docs/02's formula in exact decimals from the JSON, equals <see cref="DamageCalc"/> on the loaded floats: no
    /// x.5 tie lands on the wrong side of the float rounding with D19's new multipliers (0.7, 0.35, 0.6) or armor.
    /// </summary>
    [Fact]
    public void EveryUnitOnEveryBuilding_DecimalFormula_EqualsTheEngine()
    {
        int cells = 0, floored = 0;
        foreach (string f in Factions())
            foreach (JsonNode? u in Json($"factions/{f}/units.json")["units"]!.AsArray())
            {
                JsonNode atk = u!["attack"]!;
                if (atk["value"] is null) continue;
                string id = (string)u["id"]!, type = (string)atk["type"]!;
                decimal bonus = atk["bonusVs"]?["structure"] is JsonNode bn ? (decimal)bn : 1m;
                (decimal mult, bool ignores) = StructureCell(type);
                foreach (string g in Factions())
                    foreach (JsonNode? b in Json($"factions/{g}/buildings.json")["buildings"]!.AsArray())
                    {
                        string bid = (string)b!["id"]!;
                        int armor = BuildingArmor(bid);
                        for (int bonusAtk = 0; bonusAtk <= 3; bonusAtk++)
                        {
                            int hand = Hand((int)atk["value"]! + bonusAtk, mult, bonus, armor, ignores);
                            Assert.True(hand == Engine(id, bid, bonusAtk), $"{id} (+{bonusAtk}) on {bid}: hand {hand}, engine {Engine(id, bid, bonusAtk)}");
                            cells++;
                            if (bonusAtk == 0 && hand == 1) floored++;
                        }
                    }
            }
        _out.WriteLine($"{cells} unit x building x attack-bonus cells agree; {floored} base cells on the 1-damage floor");
        Assert.True(cells > 0);
    }

    /// <summary>The x1.5 vs Mounted by hand: Heavy Infantry on a Horse Raider (armor 0) 10 x 1.0 x 1.5 = 15; Raider on a Wickan Lancer (armor 1) 11 x 1.5 = 16.5 -> 17 - 1 = 16.</summary>
    [Fact]
    public void LineOnMounted_HandDerived()
    {
        GameData d = TestSim.Data;
        DamageTable t = d.DamageTable;
        int mounted = t.ArmorClassKeys.IndexOf("mounted");
        UnitDef hi = d.Units[d.FindUnit("malazan_heavy_infantry")], raider = d.Units[d.FindUnit("whirlwind_raider")];
        UnitDef horse = d.Units[d.FindUnit("whirlwind_horse_raider")], lancer = d.Units[d.FindUnit("malazan_wickan_lancer")];
        Assert.Equal(15, DamageCalc.Compute(t, hi.Attack, 0, mounted, horse.Armor));
        Assert.Equal(16, DamageCalc.Compute(t, raider.Attack, 0, mounted, lancer.Armor));
    }

    /// <summary>
    /// The shipped Easy seat casts (D19 item 2), for both factions, on the shipped difficulty block: only the build
    /// order, the attack threshold and the scout / defense radius are quieted (as <c>AbilityManagerTests</c>' scene does),
    /// never <c>useAbilities</c>. Malazan: a Cadre Mage at 3 Raiders 9 m off; Whirlwind: a Priest at 4 Heavy Infantry 10 m off.
    /// </summary>
    [Theory]
    [InlineData(0, "malazan_cadre_mage", "whirlwind_raider", 3, 9f)]
    [InlineData(1, "whirlwind_priest", "malazan_heavy_infantry", 4, 10f)]
    public void ShippedEasySeat_Casts(int faction, string caster, string enemy, int n, float away)
    {
        GameData data = AiScenes.DataWith(dir =>
        {
            foreach (string f in new[] { "malazan", "whirlwind" })
                dir.EditJson($"factions/{f}/ai.json", root =>
                {
                    root["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", f == "malazan" ? "malazan_laborer" : "whirlwind_camp_follower", 1));
                    root["attack"] = new JsonObject { ["minArmyPop"] = 100, ["minArmyCost"] = 0 };
                });
            dir.EditJson("common/rules.json", r =>
            {
                r["ai"]!["scoutAfter"] = 3600;
                r["ai"]!["defendRadius"] = 8;
            });
        });
        for (int a = 0; a < data.Ai.Length; a++) Assert.True(data.Ai[a].Difficulty(AiDifficulty.Easy).UseAbilities);
        PlayerSeat[] seats = { new(faction, SeatController.Ai, AiDifficulty.Easy), new(1 - faction, SeatController.Human, AiDifficulty.Easy) };
        Simulation sim = AiScenes.Flat(seats, data, workers: 0);
        BuildMaps.SetTotals(sim, 0, 0, 0);
        AiPlayer ai = sim.AiOf(0)!;
        do sim.Tick(); while (ai.Board.RallyVersion == 0 && sim.TickNumber < 400);
        Assert.True(ai.Board.RallyVersion > 0);
        CombatScenes.Place(sim, 0, data.FindUnit(caster), ai.Board.Rally);
        for (int k = 0; k < n; k++)
        {
            EntityHandle h = CombatScenes.Place(sim, 1, data.FindUnit(enemy), ai.Board.Rally + new Vector2(away, k - (n - 1) / 2f));
            CombatScenes.Spot(sim, 0, h);
        }
        int start = sim.TickNumber;
        while (ai.Board.CastsSent == 0 && sim.TickNumber < start + 3 * ai.ThinkTicks) sim.Tick();
        _out.WriteLine($"{caster} (Easy, think every {ai.ThinkTicks} ticks): first cast sent by tick {sim.TickNumber} ({sim.TickNumber - start} after placing)");
        Assert.True(ai.Board.CastsSent > 0, $"the shipped Easy {caster} sent no cast in {3 * ai.ThinkTicks} ticks");
    }

    /// <summary>The kill-time report's "before D19" copy (D18's values put back) leaves every shipped file byte-identical and its temp folder gone.</summary>
    [Fact]
    public void BeforeD19Copy_LeavesShippedFilesByteIdentical()
    {
        static Dictionary<string, string> Digests()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string f in Directory.GetFiles(TestDataDir.Shipped, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                d[Path.GetRelativePath(TestDataDir.Shipped, f)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)));
            return d;
        }
        Dictionary<string, string> before = Digests();
        ulong hash = TestSim.Data.ContentHash();
        GameData old = BuildingKillTimeReportTests.BeforeD19();
        Assert.NotEqual(hash, old.ContentHash());
        Assert.Equal(before, Digests());
        Assert.Equal(hash, DataLoader.LoadAll(TestDataDir.Shipped).Data!.ContentHash());
        // The before copy is D18 exactly: Heavy Infantry on a Town Hall back on the floor (10 x 0.4 - 5 -> 1).
        DamageTable t = old.DamageTable;
        Assert.Equal(1, DamageCalc.Compute(t, old.Units[old.FindUnit("malazan_heavy_infantry")].Attack, 0, t.ArmorClassKeys.IndexOf("structure"),
            old.Buildings[old.FindBuilding("whirlwind_holy_camp")].Armor));
    }
}
