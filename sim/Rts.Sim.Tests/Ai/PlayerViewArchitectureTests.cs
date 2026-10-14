using System.Reflection;
using System.Text.RegularExpressions;
using Rts.Sim.Ai;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-1 criterion 2 (CLAUDE.md rule 3: the AI reads the world only through its own fog-filtered view): no file under
/// <c>sim/Rts.Sim/Ai/</c> but <c>PlayerView.cs</c> names the world, the simulation, a store or the fog (the ViewApi guard's
/// pattern: code lines, comments skipped), shown to catch a planted reference; and nothing <see cref="PlayerView"/> hands out
/// is a reference into the sim (every answer is a value).
/// </summary>
public class PlayerViewArchitectureTests
{
    private const string TheOneFile = "PlayerView.cs";

    private static readonly string[] Forbidden =
    {
        @"\bWorld\b", @"\bSimulation\b", @"\bUnitStore\b", @"\bBuildingStore\b", @"\bResourceStore\b", @"\bFogStore\b",
        @"\bStatusStore\b", @"\bZoneStore\b", @"\bProjectileStore\b",
    };

    /// <summary>Every code line (comments skipped) of the given files that names a forbidden type, except in <see cref="TheOneFile"/>.</summary>
    internal static List<string> Scan(IEnumerable<(string Name, string[] Lines)> files)
    {
        var hits = new List<string>();
        foreach ((string name, string[] lines) in files)
        {
            if (name == TheOneFile) continue;
            bool inBlock = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = Code(lines[i], ref inBlock).Trim();
                if (line.Length == 0) continue;
                foreach (string p in Forbidden)
                    if (Regex.IsMatch(line, p)) hits.Add($"{name}:{i + 1}: {p}: {line}");
            }
        }
        return hits;
    }

    /// <summary>
    /// The code of one line with its comments taken out: <c>//</c> to the end of the line, and <c>/* */</c> blocks, which may
    /// span lines (<paramref name="inBlock"/>). M5-H1 (BUG-0541): a code line that starts with <c>*</c> (an expression
    /// continued on a new line) is scanned; only text inside a block comment is skipped. String literals are not parsed.
    /// </summary>
    internal static string Code(string line, ref bool inBlock)
    {
        var code = new System.Text.StringBuilder();
        for (int k = 0; k < line.Length; k++)
        {
            if (inBlock)
            {
                if (line[k] == '*' && k + 1 < line.Length && line[k + 1] == '/') { inBlock = false; k++; }
                continue;
            }
            if (line[k] == '/' && k + 1 < line.Length && line[k + 1] == '/') break;
            if (line[k] == '/' && k + 1 < line.Length && line[k + 1] == '*') { inBlock = true; k++; continue; }
            code.Append(line[k]);
        }
        return code.ToString();
    }

    private static List<(string Name, string[] Lines)> AiSources()
    {
        string dir = Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim", "Ai");
        var files = new List<(string, string[])>();
        foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            files.Add((Path.GetRelativePath(dir, file), File.ReadAllLines(file)));
        return files;
    }

    [Fact]
    public void NoAiFileButPlayerView_NamesTheWorld_TheSimulation_AStoreOrTheFog()
    {
        List<(string Name, string[] Lines)> files = AiSources();
        Assert.Contains(files, f => f.Name == TheOneFile);
        Assert.True(files.Count > 1, "only PlayerView.cs: the scan would check nothing");
        List<string> hits = Scan(files);
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
        // PlayerView.cs itself does hold the world (the one door), so the pattern does match real code.
        Assert.Contains(File.ReadAllLines(Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim", "Ai", TheOneFile)), l => Regex.IsMatch(l, @"\bWorld\b"));
    }

    /// <summary>The mutation: a second file under Ai/ planting a world reference fails the scan, at its line; a comment naming it does not.</summary>
    [Fact]
    public void APlantedWorldReference_InASecondAiFile_FailsTheScan()
    {
        List<(string Name, string[] Lines)> files = AiSources();
        files.Add(("Managers/Planted.cs", new[]
        {
            "namespace Rts.Sim.Ai;",
            "// The AI never reads World directly.",
            "internal sealed class Planted",
            "{",
            "    private readonly Rts.Sim.World _world = null!;",
            "    internal int Peek(FogStore fog) => fog.Width;",
            "}",
        }));
        List<string> hits = Scan(files);
        Assert.Equal(2, hits.Count);
        Assert.StartsWith("Managers/Planted.cs:5:", hits[0]);
        Assert.StartsWith("Managers/Planted.cs:6:", hits[1]);
    }

    /// <summary>
    /// BUG-0541 item 2: an expression continued on a line that starts with <c>*</c> is code, and is scanned; a block
    /// comment naming the world (its <c>*</c> lines included) is not.
    /// </summary>
    [Fact]
    public void AContinuationLineStartingWithAStar_IsScanned_ABlockCommentIsNot()
    {
        List<(string Name, string[] Lines)> files = AiSources();
        files.Add(("Planted.cs", new[]
        {
            "namespace Rts.Sim.Ai;",
            "/* The AI never reads",
            " * Rts.Sim.World directly. */",
            "internal static class Planted",
            "{",
            "    internal static int Peek(object o) => 1",
            "        * ((Rts.Sim.World)o).Units.Count; /* World */",
            "}",
        }));
        List<string> hits = Scan(files);
        string hit = Assert.Single(hits);
        Assert.StartsWith("Planted.cs:7:", hit);
    }

    /// <summary>Every public property, return value and out parameter of PlayerView, and every field of what it returns, is a value type with no reference inside (the constructor's world is the only reference in).</summary>
    [Fact]
    public void PlayerView_HandsOutValuesOnly()
    {
        const BindingFlags pub = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        Type t = typeof(PlayerView);
        Assert.True(t.IsSealed);
        Assert.Empty(t.GetFields(pub));
        var outs = new List<(string Where, Type Type)>();
        foreach (PropertyInfo p in t.GetProperties(pub)) outs.Add(($"{p.Name}", p.PropertyType));
        foreach (MethodInfo m in t.GetMethods(pub))
        {
            if (m.IsSpecialName) continue; // property getters, listed above
            outs.Add(($"{m.Name} returns", m.ReturnType));
            foreach (ParameterInfo p in m.GetParameters())
                if (p.ParameterType.IsByRef) outs.Add(($"{m.Name}({p.Name})", p.ParameterType.GetElementType()!));
        }
        Assert.Contains(outs, o => o.Type == typeof(SeenUnit));
        Assert.Contains(outs, o => o.Where == "TryGetAbilityReady(readyTick)"); // M5-4c: the own cooldown and cast answers
        Assert.Contains(outs, o => o.Where == "TryGetCast(abilityIndex)");
        foreach ((string where, Type type) in outs)
            Assert.True(IsPlainValue(type, 0), $"PlayerView.{where} hands out {type.FullName}");
    }

    /// <summary>A primitive, an enum, or a struct whose instance fields are all plain values (so no reference rides inside).</summary>
    private static bool IsPlainValue(Type t, int depth)
    {
        if (t.IsPrimitive || t.IsEnum) return true;
        if (!t.IsValueType || depth > 4) return false;
        foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (!IsPlainValue(f.FieldType, depth + 1)) return false;
        return true;
    }
}
