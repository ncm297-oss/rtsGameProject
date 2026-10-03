using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Rts.Sim.Tests.QA;

/// <summary>Guards the non-negotiable architecture rules in CLAUDE.md (sim/presentation split, determinism, strict build).</summary>
public class ArchitectureTests
{
    // Patterns that must never appear in sim source. Comment lines are skipped so docs can name them.
    private static readonly (string Pattern, string Why)[] ForbiddenInSim =
    {
        (@"\busing\s+Godot\b", "Rts.Sim must not reference Godot"),
        (@"\bGodot\.", "Rts.Sim must not reference Godot"),
        (@"\bSystem\.Random\b|\bnew\s+Random\s*\(", "randomness must come from the sim's seeded RNG"),
        (@"\bDateTime\b|\bDateTimeOffset\b|\bStopwatch\b|\bEnvironment\.TickCount", "no wall clock in the sim"),
        (@"\bGuid\.NewGuid\b", "non-deterministic ids"),
        (@"\bHashCode\.", "HashCode is randomized per process"),
        (@"\bParallel\.|\bVector<", "no parallel loops or Vector<T> in the sim"),
    };

    [Fact]
    public void SimAssembly_ReferencesNoGodotAssembly()
    {
        AssemblyName[] refs = typeof(SimInfo).Assembly.GetReferencedAssemblies();
        foreach (AssemblyName r in refs)
        {
            Assert.False(
                (r.Name ?? "").StartsWith("Godot", StringComparison.OrdinalIgnoreCase),
                $"Rts.Sim references {r.FullName}");
        }
    }

    [Fact]
    public void SimProject_HasStrictBuildSettings()
    {
        XDocument proj = XDocument.Load(Path.Combine(RepoRoot(), "sim", "Rts.Sim", "Rts.Sim.csproj"));
        string Prop(string name) => proj.Descendants(name).LastOrDefault()?.Value.Trim() ?? "";

        Assert.Equal("net8.0", Prop("TargetFramework"));
        Assert.Equal("enable", Prop("Nullable"));
        Assert.Equal("true", Prop("TreatWarningsAsErrors"), ignoreCase: true);
        Assert.Equal("12", Prop("LangVersion"));
        Assert.Empty(proj.Descendants("PackageReference"));
        Assert.Empty(proj.Descendants("ProjectReference"));
        Assert.Empty(proj.Descendants("Reference"));
    }

    [Fact]
    public void SimSource_UsesNoForbiddenApis()
    {
        string simDir = Path.Combine(RepoRoot(), "sim", "Rts.Sim");
        var violations = new List<string>();
        foreach (string file in Directory.EnumerateFiles(simDir, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(simDir, file);
            if (rel.StartsWith("bin", StringComparison.Ordinal) || rel.StartsWith("obj", StringComparison.Ordinal))
                continue;
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("*", StringComparison.Ordinal))
                    continue;
                foreach ((string pattern, string why) in ForbiddenInSim)
                {
                    if (Regex.IsMatch(line, pattern))
                        violations.Add($"{rel}:{i + 1}: {why}: {line}");
                }
            }
        }
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RtsGame.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
