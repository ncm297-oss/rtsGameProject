using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Rts.Cli;

/// <summary>
/// M6-S8: the <c>mirror-map</c> verb's work (docs/03 "Implementation (M6-S8)"): a hand-made map source describing one half
/// (<c>point</c>: the north half, mirrored through the centre, x → size − 1 − x, y → size − 1 − y) or one quarter
/// (<c>quad</c>: the north-west quarter, mirrored across both axes) becomes the whole map file, so the layout is
/// symmetric by construction. A dev tool: nothing here ships or runs in the sim.
/// </summary>
/// <remarks>
/// The source is a map file (the <c>game/data/maps/</c> fields) whose <c>terrain</c> holds only the source rows (point:
/// size / 2 rows of size cells; quad: size / 2 rows of size / 2 cells) and whose ramps, starts, mines and forests lie inside
/// that part (point: <c>players</c> / 2 starts; quad: one start, <c>players</c> 4). The output lists each source item then
/// its mirrors: point the item and its centre mirror; quad the item, its centre mirror, its east-west mirror and its
/// north-south mirror, so starts 0 and 1 are always each other's centre mirror (a 2-player match is fair, and the AI scout's
/// first far point, the mirror of its own hall, is the enemy's start). A ramp's <c>up</c> flips with its mirror, a mine's and a forest's anchor move by their footprint, a forest's rows reverse.
/// </remarks>
public static class MirrorMap
{
    private enum Flip { None, Centre, EastWest, NorthSouth }

    private static readonly string[] s_fields = { "id", "displayName", "description", "players", "size", "terrain", "ramps", "starts", "mines", "forests" };

    private static readonly JavaScriptEncoder s_encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>
    /// The whole map file's text for <paramref name="sourceJson"/>, or null with <paramref name="error"/> set (one line) when
    /// the source is malformed or an item leaves its part. <paramref name="mineWidth"/> / <paramref name="mineHeight"/> are the
    /// gold mine's footprint (<c>resources.json</c>), which moves a mirrored mine's anchor.
    /// </summary>
    public static string? Mirror(string sourceJson, bool quad, int mineWidth, int mineHeight, out string error)
    {
        error = "";
        try
        {
            using JsonDocument doc = JsonDocument.Parse(sourceJson);
            return Build(doc.RootElement, quad, mineWidth, mineHeight);
        }
        catch (JsonException e)
        {
            error = $"malformed JSON: {e.Message}";
        }
        catch (FormatException e)
        {
            error = e.Message;
        }
        return null;
    }

    private static string Build(JsonElement root, bool quad, int mineWidth, int mineHeight)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("the source is not a JSON object");
        foreach (JsonProperty p in root.EnumerateObject())
            if (Array.IndexOf(s_fields, p.Name) < 0) throw new FormatException($"unknown field '{p.Name}'");
        string id = Text(root, "id"), name = Text(root, "displayName"), description = Text(root, "description");
        int players = Int(root, "players"), size = Int(root, "size");
        if (size < 8 || size % 2 != 0) throw new FormatException($"size {size} is not an even side of at least 8 cells");
        int half = size / 2;
        if (quad && players != 4) throw new FormatException($"quad symmetry makes 4 starts; players is {players}");
        if (!quad && (players < 2 || players % 2 != 0)) throw new FormatException($"point symmetry makes an even number of starts; players is {players}");
        Flip[] group = quad ? new[] { Flip.None, Flip.Centre, Flip.EastWest, Flip.NorthSouth } : new[] { Flip.None, Flip.Centre };
        int partWidth = quad ? half : size;

        // The terrain: each cell takes its mirror's level in the source part.
        JsonElement terrain = ListOf(root, "terrain");
        if (terrain.GetArrayLength() != half) throw new FormatException($"terrain has {terrain.GetArrayLength()} rows, the source part has {half}");
        var rows = new string[half];
        int r = 0;
        foreach (JsonElement row in terrain.EnumerateArray())
        {
            string s = row.ValueKind == JsonValueKind.String ? row.GetString()! : throw new FormatException($"terrain[{r}] is not a string");
            if (s.Length != partWidth) throw new FormatException($"terrain[{r}] has {s.Length} cells, the source part is {partWidth} wide");
            rows[r++] = s;
        }
        var full = new string[size];
        var line = new char[size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (quad) line[x] = rows[Math.Min(y, size - 1 - y)][Math.Min(x, size - 1 - x)];
                else line[x] = y < half ? rows[y][x] : rows[size - 1 - y][size - 1 - x];
            }
            full[y] = new string(line);
        }

        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"id\": ").Append(Quote(id)).Append(",\n");
        sb.Append("  \"displayName\": ").Append(Quote(name)).Append(",\n");
        sb.Append("  \"description\": ").Append(Quote(description)).Append(",\n");
        sb.Append("  \"players\": ").Append(Num(players)).Append(",\n");
        sb.Append("  \"size\": ").Append(Num(size)).Append(",\n");
        sb.Append("  \"terrain\": [\n");
        for (int y = 0; y < size; y++) sb.Append("    ").Append(Quote(full[y])).Append(y + 1 < size ? ",\n" : "\n");
        sb.Append("  ],\n");

        var items = new List<string>();
        if (root.TryGetProperty("ramps", out JsonElement ramps))
        {
            int i = 0;
            foreach (JsonElement ramp in Items(ramps, "ramps"))
            {
                string p = $"ramps[{i++}]";
                int x = Int(ramp, "x", p), y = Int(ramp, "y", p), w = Int(ramp, "width", p), h = Int(ramp, "height", p);
                string up = Text(ramp, "up", p);
                Inside(p, x, y, w, h, quad, half);
                foreach (Flip f in group)
                {
                    (int mx, int my) = Move(f, x, y, w, h, size);
                    items.Add($"{{ \"x\": {Num(mx)}, \"y\": {Num(my)}, \"width\": {Num(w)}, \"height\": {Num(h)}, \"up\": {Quote(FlipUp(f, up, p))} }}");
                }
            }
        }
        List("ramps", items, sb);

        JsonElement starts = ListOf(root, "starts");
        int wanted = quad ? 1 : players / 2;
        if (starts.GetArrayLength() != wanted) throw new FormatException($"starts has {starts.GetArrayLength()} entries, the source part needs {wanted}");
        items.Clear();
        int si = 0;
        foreach (JsonElement start in starts.EnumerateArray())
        {
            string p = $"starts[{si++}]";
            int x = Int(start, "x", p), y = Int(start, "y", p);
            Inside(p, x, y, 1, 1, quad, half);
            foreach (Flip f in group)
            {
                (int mx, int my) = Move(f, x, y, 1, 1, size);
                items.Add($"{{ \"x\": {Num(mx)}, \"y\": {Num(my)} }}");
            }
        }
        List("starts", items, sb);

        items.Clear();
        int mi = 0;
        foreach (JsonElement mine in Items(ListOf(root, "mines"), "mines"))
        {
            string p = $"mines[{mi++}]";
            int x = Int(mine, "x", p), y = Int(mine, "y", p), gold = Int(mine, "gold", p);
            Inside(p, x, y, mineWidth, mineHeight, quad, half);
            foreach (Flip f in group)
            {
                (int mx, int my) = Move(f, x, y, mineWidth, mineHeight, size);
                items.Add($"{{ \"x\": {Num(mx)}, \"y\": {Num(my)}, \"gold\": {Num(gold)} }}");
            }
        }
        List("mines", items, sb);

        sb.Append("  \"forests\": [\n");
        int fi = 0, written = 0, forestCount = ListOf(root, "forests").GetArrayLength() * group.Length;
        foreach (JsonElement forest in Items(ListOf(root, "forests"), "forests"))
        {
            string p = $"forests[{fi++}]";
            int x = Int(forest, "x", p), y = Int(forest, "y", p);
            JsonElement rowsOf = ListOf(forest, "rows", p);
            var src = new List<string>();
            int width = 0;
            foreach (JsonElement row in rowsOf.EnumerateArray())
            {
                string s = row.ValueKind == JsonValueKind.String ? row.GetString()! : throw new FormatException($"{p}.rows[{src.Count}] is not a string");
                src.Add(s);
                width = Math.Max(width, s.Length);
            }
            if (src.Count == 0 || width == 0) throw new FormatException($"{p} has no rows");
            Inside(p, x, y, width, src.Count, quad, half);
            foreach (Flip f in group)
            {
                (int mx, int my) = Move(f, x, y, width, src.Count, size);
                bool reverseRows = f is Flip.Centre or Flip.EastWest, reverseOrder = f is Flip.Centre or Flip.NorthSouth;
                sb.Append("    {\n");
                sb.Append("      \"x\": ").Append(Num(mx)).Append(",\n");
                sb.Append("      \"y\": ").Append(Num(my)).Append(",\n");
                sb.Append("      \"rows\": [\n");
                for (int k = 0; k < src.Count; k++)
                {
                    string row = src[reverseOrder ? src.Count - 1 - k : k];
                    // Padded to the forest's width only when the row moves (a mirrored row starts at the far edge).
                    if (reverseRows)
                    {
                        char[] c = row.PadRight(width, '.').ToCharArray();
                        System.Array.Reverse(c);
                        row = new string(c);
                    }
                    sb.Append("        ").Append(Quote(row)).Append(k + 1 < src.Count ? ",\n" : "\n");
                }
                sb.Append("      ]\n");
                sb.Append("    }").Append(++written < forestCount ? ",\n" : "\n");
            }
        }
        sb.Append("  ]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>The anchor of a <paramref name="w"/> x <paramref name="h"/> rectangle at (<paramref name="x"/>, <paramref name="y"/>) after flip <paramref name="f"/> on a map of side <paramref name="size"/>.</summary>
    private static (int X, int Y) Move(Flip f, int x, int y, int w, int h, int size) => f switch
    {
        Flip.Centre => (size - x - w, size - y - h),
        Flip.EastWest => (size - x - w, y),
        Flip.NorthSouth => (x, size - y - h),
        _ => (x, y),
    };

    private static string FlipUp(Flip f, string up, string p)
    {
        bool ew = f is Flip.Centre or Flip.EastWest, ns = f is Flip.Centre or Flip.NorthSouth;
        return up switch
        {
            "north" => ns ? "south" : "north",
            "south" => ns ? "north" : "south",
            "east" => ew ? "west" : "east",
            "west" => ew ? "east" : "west",
            _ => throw new FormatException($"{p}.up: unknown side '{up}' (north, south, east or west)"),
        };
    }

    /// <summary>Throws unless the rectangle lies inside the source part (quad: the north-west quarter; point: the north half).</summary>
    private static void Inside(string p, int x, int y, int w, int h, bool quad, int half)
    {
        int right = quad ? half : 2 * half;
        if (x < 0 || y < 0 || w < 1 || h < 1 || x + w > right || y + h > half)
            throw new FormatException($"{p}: ({x}, {y}) {w} x {h} leaves the source part ({right} x {half} cells from the north-west corner)");
    }

    private static void List(string field, List<string> items, StringBuilder sb)
    {
        if (items.Count == 0)
        {
            sb.Append("  \"").Append(field).Append("\": [],\n");
            return;
        }
        sb.Append("  \"").Append(field).Append("\": [\n");
        for (int i = 0; i < items.Count; i++) sb.Append("    ").Append(items[i]).Append(i + 1 < items.Count ? ",\n" : "\n");
        sb.Append("  ],\n");
    }

    private static JsonElement.ArrayEnumerator Items(JsonElement array, string p) =>
        array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : throw new FormatException($"{p} is not a list");

    private static JsonElement ListOf(JsonElement o, string name, string p = "")
    {
        if (!o.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Array)
            throw new FormatException($"{Where(p, name)} is missing or not a list");
        return v;
    }

    private static int Int(JsonElement o, string name, string p = "")
    {
        if (o.ValueKind != JsonValueKind.Object) throw new FormatException($"{(p.Length == 0 ? "the source" : p)} is not an object");
        if (!o.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int i))
            throw new FormatException($"{Where(p, name)} is missing or not a whole number");
        return i;
    }

    private static string Text(JsonElement o, string name, string p = "")
    {
        if (o.ValueKind != JsonValueKind.Object) throw new FormatException($"{(p.Length == 0 ? "the source" : p)} is not an object");
        if (!o.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.String)
            throw new FormatException($"{Where(p, name)} is missing or not a string");
        return v.GetString()!;
    }

    private static string Where(string p, string name) => p.Length == 0 ? name : $"{p}.{name}";

    private static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Quote(string s) => "\"" + JsonEncodedText.Encode(s, s_encoder).ToString() + "\"";
}
