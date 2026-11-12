using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Rts.Sim.Data;

namespace Rts.Game.Tests;

/// <summary>
/// QA M7-V3b (session 2026-11-11-2256): <see cref="ThirdFactionForge"/> against shipped data with more than two factions,
/// on the branch itself (BUG-1231). A temp copy of <c>res://data</c> gets 0, 1, 3 and 5 extra factions (the rows that stay
/// within <see cref="DataLimits.MaxFactions"/>) appended to the order (Malazan copies, the forge's own replacements, ids <c>qa_extra_&lt;n&gt;</c>), standing in for the Teblor and the
/// factions after them; that copy is handed to the forge as its shipped data. Each time: the forged data is exactly
/// Malazan, Whirlwind, <c>third</c> in that order with <c>third</c> at index 2; no unit, building or tech of an extra
/// faction is left and no extra folder is left on disk; the forge's source copy is untouched (its order and folders as
/// written). The wide copy itself loads (so a red row is the forge, not the stand-in data). Headless, no window.
/// </summary>
public partial class QaM7V3bTest : Node
{
    private readonly List<string> _failures = new();

    public override void _Ready()
    {
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m7-v3b-test-{OS.GetProcessId()}"));
        try
        {
            // Rows past DataLimits.MaxFactions are skipped (the stand-in would not load), so more shipped factions keep it green.
            int shippedNow = Order(ProjectSettings.GlobalizePath("res://data")).Length;
            int[] rows = new[] { 0, 1, 3, 5 }.Where(e => shippedNow + e <= DataLimits.MaxFactions).ToArray();
            Check(rows.Length >= 2, $"{shippedNow} factions ship: no room for an extra one under {DataLimits.MaxFactions}");
            foreach (int extra in rows) Row(root, extra);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"QA M7-V3B TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M7-V3B TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private void Row(string root, int extra)
    {
        string wide = Path.Combine(root, $"wide{extra}", "data");
        CopyTree(ProjectSettings.GlobalizePath("res://data"), wide);
        var extras = new List<string>();
        for (int k = 0; k < extra; k++)
        {
            string id = $"qa_extra_{k}";
            AddMalazanCopy(wide, id);
            extras.Add(id);
        }
        string[] wideOrder = Order(wide);
        DataLoadResult wideLoad = DataLoader.LoadAll(wide);
        if (!Check(wideLoad.Ok, $"extra {extra}: the stand-in shipped data does not load: {string.Join("; ", wideLoad.Errors.Take(3))}")) return;
        int shipped = wideLoad.Data!.Factions.Length;

        GameData forged = ThirdFactionForge.Forge(wide, Path.Combine(root, $"forged{extra}"), "QA Forged Third", out string copy);
        string keys = string.Join(", ", forged.Factions.Select(f => f.Key));
        Check(keys == $"malazan, whirlwind, {ThirdFactionForge.Id}" && forged.FindFaction(ThirdFactionForge.Id) == 2,
            $"{shipped} shipped: forged factions [{keys}] (want malazan, whirlwind, {ThirdFactionForge.Id})");
        Check(string.Join(",", Order(copy)) == $"malazan,whirlwind,{ThirdFactionForge.Id}", $"{shipped} shipped: the copy's order [{string.Join(", ", Order(copy))}]");
        foreach (string id in extras)
        {
            Check(!Directory.Exists(Path.Combine(copy, "factions", id)), $"{shipped} shipped: {id}'s folder left in the copy");
            Check(!forged.Units.Any(u => u.Key.StartsWith(id + "_", StringComparison.Ordinal))
                && !forged.Buildings.Any(b => b.Key.StartsWith(id + "_", StringComparison.Ordinal)),
                $"{shipped} shipped: a unit or building of {id} is left in the forged data");
            Check(Directory.Exists(Path.Combine(wide, "factions", id)), $"{shipped} shipped: the forge deleted {id} from its source");
        }
        Check(Order(wide).SequenceEqual(wideOrder), $"{shipped} shipped: the forge changed its source's order to [{string.Join(", ", Order(wide))}]");
        // The forged third faction is the forge's Malazan copy, not a stand-in: its hall (the Garrison Keep's copy) carries the forge's name prefix.
        int hall = forged.FindBuilding(ThirdFactionForge.Id + "_garrison_keep");
        Check(hall >= 0 && forged.Buildings[hall].DisplayName.StartsWith(ThirdFactionForge.NamePrefix, StringComparison.Ordinal),
            $"{shipped} shipped: the third faction's hall is named '{(hall < 0 ? "" : forged.Buildings[hall].DisplayName)}'");
        GD.Print($"  note: {shipped} shipped ({string.Join(", ", wideOrder)}) -> forged [{keys}]");
    }

    private static string[] Order(string dataDir) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(dataDir, "common", "factions.json")))!["order"]!.AsArray().Select(n => (string)n!).ToArray();

    private static void CopyTree(string from, string to)
    {
        if (Directory.Exists(to)) Directory.Delete(to, true);
        Directory.CreateDirectory(to);
        foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }

    // The forge's own recipe (and tools/qa/third-faction-copy.ps1's), with a QA id, appended to the order.
    private static void AddMalazanCopy(string dataDir, string id)
    {
        string from = Path.Combine(dataDir, "factions", "malazan"), to = Path.Combine(dataDir, "factions", id);
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
        {
            string text = File.ReadAllText(file)
                .Replace("malazan_", id + "_")
                .Replace("moranth_supply", id + "_upgrade")
                .Replace("\"telas_fire\"", $"\"{id}_fire\"")
                .Replace("\"cusser\"", $"\"{id}_cusser\"");
            File.WriteAllText(Path.Combine(to, Path.GetFileName(file)), text);
        }
        string faction = Path.Combine(to, "faction.json");
        JsonNode f = JsonNode.Parse(File.ReadAllText(faction))!;
        f["id"] = id;
        f["displayName"] = "QA " + id;
        File.WriteAllText(faction, f.ToJsonString());
        string orderFile = Path.Combine(dataDir, "common", "factions.json");
        JsonNode r = JsonNode.Parse(File.ReadAllText(orderFile))!;
        r["order"]!.AsArray().Add(id);
        File.WriteAllText(orderFile, r.ToJsonString());
    }

    private bool Check(bool ok, string what)
    {
        if (!ok) _failures.Add(what);
        return ok;
    }
}
