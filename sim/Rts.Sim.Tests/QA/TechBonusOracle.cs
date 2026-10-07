using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M3-5 (2026-10-07-1131): an independent recount of <c>World.TechBonus</c> straight from the shipped JSON (not through
/// <see cref="TechDef"/> / <see cref="TechEffect"/>): for a set of researched tech keys, the sum of every effect whose
/// set filters all match the unit (docs/03 "Data format", <c>techs.json</c>): <c>attackType</c> = the unit's
/// <c>attack.type</c>, <c>tags</c> = any one of the unit's tags, <c>units</c> = its id, <c>siege</c> = its slot is
/// <c>siege</c>. Ability cooldown amounts are seconds in the file and ticks (x 20) in the sim.
/// </summary>
public sealed class TechBonusOracle
{
    private sealed record UnitRow(string Id, string Slot, string AttackType, string[] Tags);
    private sealed record EffectRow(string Stat, double Amount, string? AttackType, string[]? Tags, string[]? Units, bool? Siege);

    private readonly Dictionary<string, UnitRow> _units = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<EffectRow>> _techs = new(StringComparer.Ordinal);

    /// <summary>Reads every <c>units.json</c> and <c>techs.json</c> under <paramref name="dataDir"/>.</summary>
    public TechBonusOracle(string dataDir)
    {
        var techFiles = new List<string> { Path.Combine(dataDir, "common", "techs.json") };
        foreach (string folder in Directory.GetDirectories(Path.Combine(dataDir, "factions")))
        {
            techFiles.Add(Path.Combine(folder, "techs.json"));
            JsonNode units = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "units.json")))!;
            foreach (JsonNode? u in units["units"]!.AsArray())
            {
                string id = (string)u!["id"]!;
                _units[id] = new UnitRow(id, (string)u["slot"]!, (string)u["attack"]!["type"]!,
                    u["tags"]!.AsArray().Select(t => (string)t!).ToArray());
            }
        }
        foreach (string file in techFiles)
        {
            JsonNode root = JsonNode.Parse(File.ReadAllText(file))!;
            foreach (JsonNode? t in root["techs"]!.AsArray())
            {
                var effects = new List<EffectRow>();
                foreach (JsonNode? e in t!["effects"]!.AsArray())
                {
                    JsonNode a = e!["appliesTo"]!;
                    effects.Add(new EffectRow((string)e["stat"]!, (double)e["amount"]!, (string?)a["attackType"],
                        a["tags"]?.AsArray().Select(x => (string)x!).ToArray(),
                        a["units"]?.AsArray().Select(x => (string)x!).ToArray(),
                        (bool?)a["siege"]));
                }
                _techs[(string)t["id"]!] = effects;
            }
        }
    }

    /// <summary>Unit ids in the files.</summary>
    public IEnumerable<string> UnitIds => _units.Keys;

    /// <summary>Tech ids in the files.</summary>
    public IEnumerable<string> TechIds => _techs.Keys;

    /// <summary>The bonus to <paramref name="stat"/> (JSON spelling) of <paramref name="unitId"/> from <paramref name="researched"/>.</summary>
    public double Bonus(IEnumerable<string> researched, string unitId, string stat)
    {
        UnitRow u = _units[unitId];
        double sum = 0;
        foreach (string tech in researched)
        {
            foreach (EffectRow e in _techs[tech])
            {
                if (e.Stat != stat) continue;
                if (e.AttackType != null && e.AttackType != u.AttackType) continue;
                if (e.Siege != null && (u.Slot == "siege") != e.Siege.Value) continue;
                if (e.Units != null && e.Units.Length > 0 && !e.Units.Contains(u.Id)) continue;
                if (e.Tags != null && e.Tags.Length > 0 && !e.Tags.Any(u.Tags.Contains)) continue;
                sum += stat == "abilityCooldown" ? Math.Round(e.Amount * 20) : e.Amount;
            }
        }
        return sum;
    }

    /// <summary>Asserts <c>World.TechBonus</c> equals the recount for every player, every unit type and every stat.</summary>
    public void AssertMatches(World w, string context)
    {
        string[] stats = DataLimits.TechStatIds.ToArray();
        for (int p = 0; p < w.Gold.Length; p++)
        {
            var researched = new List<string>();
            for (int t = 0; t < w.Data.Techs.Length; t++)
                if (w.HasTech(p, t)) researched.Add(w.Data.Techs[t].Key);
            foreach (string unit in _units.Keys)
            {
                int type = w.Data.FindUnit(unit);
                for (int s = 0; s < stats.Length; s++)
                {
                    double expected = Bonus(researched, unit, stats[s]);
                    float actual = w.TechBonus(p, type, (TechStat)s);
                    if (actual != (float)expected)
                        Assert.Fail($"{context}: player {p} [{string.Join(",", researched)}] {unit} {stats[s]}: TechBonus {actual}, data says {expected}");
                }
            }
        }
    }
}
