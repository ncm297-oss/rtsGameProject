using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// BUG-1331 item 3: edits to a data file made in its text, so everything the edit doesn't touch keeps its bytes (the
/// indentation, the inline objects and arrays, the line endings). Values are found by JSON pointer ("/buildings/3/scale")
/// with <see cref="Utf8JsonReader"/>; a new property goes on a line of its own before the object's last one (one added
/// line, no comma moved); a new array element after the last one. New values are written in the files' style: an inline
/// <c>{ "a": 1, "b": [2] }</c>, or (<see cref="Row"/>) one property a line.
/// </summary>
internal static class JsonTextEdit
{
    /// <summary>One value: its text span [Start, End), its property name's start (-1 in an array), its children's pointers in order.</summary>
    private sealed record Node(int Start, int End, int NameStart, List<string> Children);

    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The file's line ending: CRLF when it has one, else LF.</summary>
    public static string NewLine(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static Dictionary<string, Node> Parse(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        // Byte offset -> char offset (the reader counts bytes; the text is chars).
        var charAt = new int[bytes.Length + 1];
        int b = 0, c = 0;
        foreach (Rune r in text.EnumerateRunes())
        {
            for (int k = 0; k < r.Utf8SequenceLength; k++) charAt[b + k] = c;
            b += r.Utf8SequenceLength;
            c += r.Utf16SequenceLength;
        }
        charAt[b] = c;

        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        var stack = new Stack<(string Path, bool Array, int Next, int Start, int NameStart)>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        string? name = null;
        int nameStart = -1;
        string Child()
        {
            if (stack.Count == 0) return "";
            var top = stack.Pop();
            string p = top.Array ? $"{top.Path}/{top.Next}" : $"{top.Path}/{name}";
            stack.Push(top with { Next = top.Next + 1 });
            nodes[top.Path].Children.Add(p);
            return p;
        }
        while (reader.Read())
        {
            int start = charAt[reader.TokenStartIndex];
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    name = reader.GetString();
                    nameStart = start;
                    break;
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                {
                    bool inArray = stack.Count > 0 && stack.Peek().Array;
                    string p = Child();
                    nodes[p] = new Node(start, -1, inArray || stack.Count == 0 ? -1 : nameStart, new List<string>());
                    stack.Push((p, reader.TokenType == JsonTokenType.StartArray, 0, start, inArray ? -1 : nameStart));
                    break;
                }
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                {
                    var top = stack.Pop();
                    nodes[top.Path] = nodes[top.Path] with { End = charAt[(int)reader.BytesConsumed] };
                    break;
                }
                default:
                {
                    bool inArray = stack.Peek().Array;
                    string p = Child();
                    nodes[p] = new Node(start, charAt[(int)reader.BytesConsumed], inArray ? -1 : nameStart, new List<string>());
                    break;
                }
            }
        }
        return nodes;
    }

    /// <summary>Whether <paramref name="pointer"/> names a value in <paramref name="text"/>.</summary>
    public static bool Has(string text, string pointer) => Parse(text).ContainsKey(pointer);

    /// <summary>The number of elements of the array (or properties of the object) at <paramref name="pointer"/>.</summary>
    public static int Count(string text, string pointer) => Parse(text)[pointer].Children.Count;

    /// <summary><paramref name="text"/> with the value at <paramref name="pointer"/> replaced by <paramref name="value"/> written inline.</summary>
    public static string Replace(string text, string pointer, JsonNode value) => ReplaceRaw(text, pointer, Inline(value));

    private static string ReplaceRaw(string text, string pointer, string raw)
    {
        Node n = Parse(text)[pointer];
        return text[..n.Start] + raw + text[n.End..];
    }

    /// <summary>The leading whitespace of the line <paramref name="at"/> is on, which must be all that precedes it there.</summary>
    private static (int LineStart, string Indent) IndentOf(string text, int at)
    {
        int ls = text.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        string indent = text[ls..at];
        if (indent.Trim().Length != 0) throw new InvalidOperationException($"the value at offset {at} does not start its line ('{indent}')");
        return (ls, indent);
    }

    /// <summary>
    /// <paramref name="text"/> with <c>"<paramref name="key"/>": <paramref name="value"/>,</c> added to the object at
    /// <paramref name="pointer"/> on a new line just before its last property (so no other line changes), or the value
    /// replaced when the object has the key.
    /// </summary>
    public static string SetProperty(string text, string pointer, string key, JsonNode value)
    {
        Dictionary<string, Node> nodes = Parse(text);
        if (nodes.ContainsKey($"{pointer}/{key}")) return Replace(text, $"{pointer}/{key}", value);
        Node last = nodes[nodes[pointer].Children[^1]];
        (int ls, string indent) = IndentOf(text, last.NameStart);
        return text[..ls] + indent + Inline(JsonValue.Create(key)!) + ": " + Inline(value) + "," + NewLine(text) + text[ls..];
    }

    /// <summary><paramref name="text"/> with <paramref name="element"/> added after the last element of the array at <paramref name="pointer"/>, at its indentation.</summary>
    public static string Append(string text, string pointer, JsonNode element, bool row = false)
    {
        Dictionary<string, Node> nodes = Parse(text);
        Node last = nodes[nodes[pointer].Children[^1]];
        (_, string indent) = IndentOf(text, last.Start);
        string nl = NewLine(text);
        string raw = row ? Row(element.AsObject(), indent, nl) : Inline(element);
        return text[..last.End] + "," + nl + indent + raw + text[last.End..];
    }

    /// <summary><paramref name="text"/> with the element at <paramref name="pointer"/> replaced by <paramref name="element"/> written as a <see cref="Row"/>.</summary>
    public static string ReplaceRow(string text, string pointer, JsonObject element)
    {
        Node n = Parse(text)[pointer];
        (_, string indent) = IndentOf(text, n.Start);
        return ReplaceRaw(text, pointer, Row(element, indent, NewLine(text)));
    }

    /// <summary>An object one property a line (its values inline), the closing brace at <paramref name="indent"/>: a data file's row.</summary>
    public static string Row(JsonObject o, string indent, string nl)
    {
        var sb = new StringBuilder("{").Append(nl);
        int i = 0;
        foreach ((string k, JsonNode? v) in o)
        {
            sb.Append(indent).Append("  ").Append(Inline(JsonValue.Create(k)!)).Append(": ").Append(Inline(v)).Append(++i < o.Count ? "," : "").Append(nl);
        }
        return sb.Append(indent).Append('}').ToString();
    }

    /// <summary>A value on one line in the files' style: <c>{ "a": 1, "b": [2, 3] }</c>, <c>[]</c>, strings unescaped beyond JSON's needs.</summary>
    public static string Inline(JsonNode? v) => v switch
    {
        null => "null",
        JsonObject o => o.Count == 0 ? "{}" : "{ " + string.Join(", ", o.Select(kv => Inline(JsonValue.Create(kv.Key)) + ": " + Inline(kv.Value))) + " }",
        JsonArray a => "[" + string.Join(", ", a.Select(Inline)) + "]",
        _ => v.ToJsonString(Relaxed),
    };

    /// <summary>Whether two JSON values are equal, an object's properties in any order, an array's in order.</summary>
    public static bool Same(JsonNode? a, JsonNode? b) => (a, b) switch
    {
        (null, null) => true,
        (JsonObject x, JsonObject y) => x.Count == y.Count && x.All(kv => y.ContainsKey(kv.Key) && Same(kv.Value, y[kv.Key])),
        (JsonArray x, JsonArray y) => x.Count == y.Count && x.Zip(y).All(p => Same(p.First, p.Second)),
        (JsonValue x, JsonValue y) => x.ToJsonString() == y.ToJsonString()
            || (x.TryGetValue(out double dx) && y.TryGetValue(out double dy) && dx == dy),
        _ => false,
    };
}
