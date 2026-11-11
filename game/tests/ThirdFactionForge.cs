using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Game.Tests;

/// <summary>
/// M7-V3a: a copy of the shipped data with a third faction appended, for the view scenes that must prove a third faction
/// before the real one lands (D28). The worktree's <c>game/data</c> is never touched: the copy lives under the caller's
/// <c>user://</c> folder. The recipe is the brief's (BUG-1191's with other ids): Malazan's files copied to
/// <c>factions/&lt;id&gt;/</c>, <c>malazan_</c> to <c>&lt;id&gt;_</c>, <c>moranth_supply</c> to <c>&lt;id&gt;_upgrade</c>,
/// <c>"telas_fire"</c> to <c>"&lt;id&gt;_fire"</c>, <c>"cusser"</c> to <c>"&lt;id&gt;_cusser"</c>, the faction's id and
/// <c>displayName</c> set, and <c>&lt;id&gt;</c> appended to <c>common/factions.json</c>'s order. Every unit, building, tech and
/// ability <c>displayName</c> of the copy gets <see cref="NamePrefix"/> and every <c>description</c> <see cref="TextPrefix"/>, so
/// a screen that shows Malazan's words for the third faction is caught.
/// </summary>
public static class ThirdFactionForge
{
    /// <summary>The third faction's data id in the scenes.</summary>
    public const string Id = "third";

    /// <summary>The prefix on every forged unit, building, tech and ability name.</summary>
    public const string NamePrefix = "Tri ";

    /// <summary>The prefix on every forged description.</summary>
    public const string TextPrefix = "Third host: ";

    /// <summary>
    /// Copies <paramref name="dataDir"/> to <paramref name="root"/>/data (deleting <paramref name="root"/> first), adds the
    /// third faction named <paramref name="displayName"/> (with <c>regen</c> when <paramref name="regenHpPerSecond"/> is above
    /// 0), and loads it; the copy's folder in <paramref name="copy"/>. Throws when the copy does not load.
    /// </summary>
    public static GameData Forge(string dataDir, string root, string displayName, out string copy, double regenHpPerSecond = 0, double regenDelay = 0)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        copy = Path.Combine(root, "data");
        Directory.CreateDirectory(copy);
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)));

        string from = Path.Combine(copy, "factions", "malazan"), to = Path.Combine(copy, "factions", Id);
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
        {
            string text = File.ReadAllText(file)
                .Replace("malazan_", Id + "_")
                .Replace("moranth_supply", Id + "_upgrade")
                .Replace("\"telas_fire\"", $"\"{Id}_fire\"")
                .Replace("\"cusser\"", $"\"{Id}_cusser\"");
            File.WriteAllText(Path.Combine(to, Path.GetFileName(file)), text);
        }
        Edit(Path.Combine(to, "faction.json"), f =>
        {
            f["id"] = Id;
            f["displayName"] = displayName;
            if (regenHpPerSecond > 0) f["regen"] = new JsonObject { ["hpPerSecond"] = regenHpPerSecond, ["delay"] = regenDelay };
        });
        foreach ((string file, string list) in new[] { ("units.json", "units"), ("buildings.json", "buildings"), ("techs.json", "techs"), ("abilities.json", "abilities") })
        {
            string path = Path.Combine(to, file);
            if (!File.Exists(path)) continue;
            Edit(path, r =>
            {
                if (r[list] is not JsonArray rows) return;
                foreach (JsonNode? row in rows)
                {
                    if (row?["displayName"] is JsonNode n) row["displayName"] = NamePrefix + (string?)n;
                    if (row?["description"] is JsonNode d) row["description"] = TextPrefix + (string?)d;
                }
            });
        }
        string order = Path.Combine(copy, "common", "factions.json");
        Edit(order, r => r["order"]!.AsArray().Add(Id));

        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load: " + string.Join("; ", loaded.Errors.Take(5)));
        return loaded.Data!;
    }

    private static void Edit(string path, Action<JsonNode> edit)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException($"{path} is empty");
        edit(root);
        File.WriteAllText(path, root.ToJsonString());
    }
}
