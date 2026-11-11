using System;
using System.Collections.Generic;
using System.IO;

namespace Rts.Sim.Data;

public static partial class DataLoader
{
    /// <summary>
    /// M7-3: the faction folders in <c>common/factions.json</c>'s <c>order</c> (faction id = index). Every folder under
    /// <c>factions/</c> must be listed and every entry must have a folder; a repeat, a non-id entry and more than
    /// <see cref="DataLimits.MaxFactions"/> entries are errors too (one each, at <c>order[i]</c> or naming the folder).
    /// Why a list the data owns and not the folders' ordinal order: a new faction's folder (<c>teblor</c>) would sort
    /// between the shipped two and renumber every later faction's types, moving every state hash. Unlisted folders follow
    /// in ordinal order and an unreadable file falls back to the folders' ordinal order, so their files are still checked
    /// (the load fails on the error anyway).
    /// </summary>
    private static string[] FactionOrder(Checker c, string dataDir)
    {
        string[] folders = Array.Empty<string>();
        string factionsDir = Path.Combine(dataDir, "factions");
        if (Directory.Exists(factionsDir))
        {
            folders = Directory.GetDirectories(factionsDir);
            for (int i = 0; i < folders.Length; i++) folders[i] = Path.GetFileName(folders[i]);
            Array.Sort(folders, StringComparer.Ordinal); // load-time only; the fallback order and the unlisted folders' order
        }
        FactionOrderJson? json = c.Read("common/factions.json", DataJsonContext.Default.FactionOrderJson);
        List<string?>? order = json == null ? null : c.Obj(json.Order, "order");
        if (order == null) return folders;
        if (order.Count > DataLimits.MaxFactions)
            c.Error("order", $"{order.Count} factions is above the maximum {DataLimits.MaxFactions}");
        var result = new List<string>(); // load-time only
        for (int i = 0; i < order.Count; i++)
        {
            string id = c.Id(order[i], $"order[{i}]");
            if (id.Length == 0) continue;
            int first = result.IndexOf(id);
            if (first >= 0)
                c.Error($"order[{i}]", $"faction '{id}' is listed twice");
            else if (Array.IndexOf(folders, id) < 0)
                c.Error($"order[{i}]", $"faction '{id}' has no folder factions/{id}/");
            else
                result.Add(id);
        }
        foreach (string folder in folders)
        {
            if (result.Contains(folder)) continue;
            c.Error("order", $"folder factions/{folder}/ is not listed (a new faction appends its id to the order)");
            result.Add(folder);
        }
        return result.ToArray();
    }

    /// <summary>
    /// M7-3: the id table of a per-faction key set: faction by faction in faction order, each faction's keys in ordinal
    /// order. <paramref name="accepted"/> holds (faction, entry) pairs; <paramref name="keyOf"/> reads an entry's id.
    /// For the shipped two factions this is the global ordinal order the loader used before (every Malazan key sorts
    /// before every Whirlwind key), so no id moved.
    /// </summary>
    private static string[] FactionKeys(int factions, List<(int Faction, int Index)> accepted, Func<int, int, string> keyOf)
    {
        var keys = new string[accepted.Count];
        int n = 0;
        for (int f = 0; f < factions; f++)
        {
            int start = n;
            foreach ((int faction, int index) in accepted)
                if (faction == f) keys[n++] = keyOf(faction, index);
            Array.Sort(keys, start, n - start, StringComparer.Ordinal);
        }
        return keys;
    }

    /// <summary>
    /// The id of <paramref name="key"/> in a per-faction id table (<see cref="FactionKeys"/>), or -1. A walk, not a binary
    /// search: the table is ordinal only within each faction. Load time only.
    /// </summary>
    private static int KeyId(string[] keys, string key) => Array.IndexOf(keys, key);
}
