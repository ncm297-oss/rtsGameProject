using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D38 (BUG-1612): the three Teblor <c>standard</c> build orders D37 measured, shipped through M7-9's <c>buildOrders</c>
/// (docs/03 "For the data track (M7-9)"; docs/01, the second 2026-11-26 row, (3)): <c>buildOrder</c> is Normal's (M1RS,
/// <c>Fixtures/teblor_standard_d37_normal.json</c>), <c>buildOrders.easy</c> K9KC10S and <c>buildOrders.hard</c> QRD1 (the
/// Easy and Hard fixtures), each the fixture's <c>buildOrder</c> step for step, switches included. The fixtures are the
/// source; this class reads them, it never retypes a step.
/// </summary>
internal static class D38TeblorOrders
{
    /// <summary>The difficulties with an entry in <c>buildOrders</c>, in difficulty order (Normal plays <c>buildOrder</c>).</summary>
    internal static readonly string[] Entries = { "easy", "hard" };

    /// <summary>The three difficulties' keys, in <see cref="AiDifficulty"/> order.</summary>
    internal static readonly string[] Difficulties = { "easy", "normal", "hard" };

    /// <summary>The fixture whose <c>buildOrder</c> difficulty <paramref name="difficulty"/> plays.</summary>
    internal static JsonObject Fixture(string difficulty) => D37TeblorFixtureTests.Fixture(difficulty);

    /// <summary>The list a <paramref name="difficulty"/> seat plays in the opening <paramref name="opening"/> (raw JSON): its <c>buildOrders</c> entry, else <c>buildOrder</c>.</summary>
    internal static JsonArray ListOf(JsonObject opening, string difficulty) =>
        (opening["buildOrders"]?[difficulty] ?? opening["buildOrder"])!.AsArray();

    /// <summary>The <see cref="AiDifficulty"/> of a key ("easy" -> Easy).</summary>
    internal static AiDifficulty Difficulty(string key) => (AiDifficulty)Array.IndexOf(Difficulties, key);

    /// <summary>
    /// Every way the opening <paramref name="shipped"/> (raw JSON) is not D38's: <c>buildOrder</c> the Normal fixture's,
    /// <c>buildOrders</c> right after it with exactly the Easy and Hard fixtures' lists, and every other field each fixture's.
    /// Each problem names the list or field and the fixture.
    /// </summary>
    internal static List<string> Problems(JsonObject shipped)
    {
        var problems = new List<string>();
        if (!JsonNode.DeepEquals(shipped["buildOrder"], Fixture("normal")["buildOrder"])) problems.Add("buildOrder differs from the D37 normal fixture");
        JsonObject? lists = shipped["buildOrders"] as JsonObject;
        if (lists == null) problems.Add("no buildOrders field");
        else
        {
            string keys = string.Join(", ", lists.Select(kv => kv.Key));
            if (keys != string.Join(", ", Entries)) problems.Add($"buildOrders holds '{keys}', expected '{string.Join(", ", Entries)}'");
            foreach (string d in Entries)
                if (!JsonNode.DeepEquals(lists[d], Fixture(d)["buildOrder"])) problems.Add($"buildOrders.{d} differs from the D37 {d} fixture");
        }
        string[] names = shipped.Select(kv => kv.Key).ToArray();
        int at = Array.IndexOf(names, "buildOrders");
        if (at >= 0 && (at == 0 || names[at - 1] != "buildOrder")) problems.Add("buildOrders is not placed right after buildOrder");
        foreach (string d in Difficulties)
        {
            JsonObject f = Fixture(d);
            foreach (var (field, value) in f)
                if (field != "buildOrder" && !JsonNode.DeepEquals(shipped[field], value)) problems.Add($"{field} differs from the D37 {d} fixture's");
            if (shipped.Count != f.Count + 1) problems.Add($"the shipped opening has {shipped.Count} fields, the D37 {d} fixture {f.Count} + buildOrders");
        }
        return problems.Distinct().ToList();
    }
}
