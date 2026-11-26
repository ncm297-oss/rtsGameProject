using System.Text.Json.Nodes;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D-H10: the M7-8 reserve switches as the shipped AI files write them (docs/03 "For the data track (M7-8)"). No expand
/// step carries <c>noReserve</c> at any value (BUG-1663, ruling docs/01 2026-11-24 ACCEPT (1)(e): on an expand step it means
/// "never expand"; not shipped without 30-minute rows), and no switch is written <c>false</c> (leave it out: <c>false</c>
/// changes nothing and only hides which steps are switched). Read from the raw JSON of every faction folder, so a faction
/// whose files land is checked the session it lands.
/// </summary>
public class ReserveSwitchContentTests
{
    private static readonly string[] Switches = { "throughReserve", "noReserve" };

    /// <summary>Every problem of the AI file <paramref name="root"/> (a faction's <c>ai.json</c>), each naming the opening and the step.</summary>
    internal static List<string> Problems(string faction, JsonNode root)
    {
        var problems = new List<string>();
        foreach (JsonNode? o in root["openings"]?.AsArray() ?? new JsonArray())
        {
            // D38: every list of the opening, buildOrder and each buildOrders entry (named "(easy)" etc.).
            var lists = new List<(string Name, JsonArray Order)> { ("", o!["buildOrder"]!.AsArray()) };
            if (o["buildOrders"] is JsonObject entries)
                foreach (var (key, list) in entries) lists.Add(($" ({key})", list!.AsArray()));
            foreach (var (name, order) in lists)
            for (int i = 0; i < order.Count; i++)
            {
                JsonObject step = order[i]!.AsObject();
                string what = $"{faction} opening '{(string?)o["id"]}'{name} step {i + 1} ({(string?)step["do"]} {(string?)step["id"]})";
                if ((string?)step["do"] == "expand" && step.ContainsKey("noReserve"))
                    problems.Add($"{what}: noReserve on an expand step means never expand (BUG-1663)");
                foreach (string s in Switches)
                    if (step[s] is JsonValue v && v.TryGetValue(out bool b) && !b)
                        problems.Add($"{what}: {s} written false; leave the switch out");
            }
        }
        return problems;
    }

    private static string[] FactionFolders() =>
        Directory.GetDirectories(Path.Combine(TestDataDir.Shipped, "factions")).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void NoShippedExpandStep_CarriesNoReserve_AndNoSwitchIsWrittenFalse()
    {
        string[] folders = FactionFolders();
        Assert.Contains("teblor", folders);
        var problems = new List<string>();
        int files = 0;
        foreach (string f in folders)
        {
            string path = Path.Combine(TestDataDir.Shipped, "factions", f, "ai.json");
            if (!File.Exists(path)) continue;
            files++;
            problems.AddRange(Problems(f, JsonNode.Parse(File.ReadAllText(path))!));
        }
        Assert.Equal(ContentFactions.Keys.Length, files);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>The check is not vacuous: the Teblor file with a switch on its expand step (true or false), or a switch written false, fails naming the step.</summary>
    [Theory]
    [InlineData("expand", null, "noReserve", true, "noReserve on an expand step means never expand")]
    [InlineData("expand", null, "noReserve", false, "noReserve on an expand step means never expand")]
    [InlineData("train", "teblor_shaman", "throughReserve", false, "throughReserve written false")]
    [InlineData("research", "armor_1", "noReserve", false, "noReserve written false")]
    public void ASwitchedExpandOrAFalseSwitch_FailsNamingTheStep(string what, string? id, string field, bool value, string want)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", "teblor", "ai.json")))!;
        Assert.Empty(Problems("teblor", root));
        JsonArray order = root["openings"]![0]!["buildOrder"]!.AsArray();
        int i = Enumerable.Range(0, order.Count).First(k => (string?)order[k]!["do"] == what && (string?)order[k]!["id"] == id);
        order[i]![field] = value;
        List<string> problems = Problems("teblor", root);
        string step = $"teblor opening 'standard' step {i + 1} ({what} {id})";
        Assert.Contains($"{step}: {want}" + (want.EndsWith("false", StringComparison.Ordinal) ? "; leave the switch out" : " (BUG-1663)"), problems);
    }

    /// <summary>D38: the check reads every list: a switch on Easy's or Hard's expand step fails naming the list.</summary>
    [Theory]
    [InlineData("easy")]
    [InlineData("hard")]
    public void ASwitchedExpandInAnEntry_FailsNamingTheList(string key)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", "teblor", "ai.json")))!;
        JsonArray order = root["openings"]![0]!["buildOrders"]![key]!.AsArray();
        int i = Enumerable.Range(0, order.Count).First(k => (string?)order[k]!["do"] == "expand");
        order[i]!["noReserve"] = true;
        Assert.Contains($"teblor opening 'standard' ({key}) step {i + 1} (expand ): noReserve on an expand step means never expand (BUG-1663)", Problems("teblor", root));
    }
}
