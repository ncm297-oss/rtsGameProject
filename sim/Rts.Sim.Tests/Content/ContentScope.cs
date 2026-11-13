using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D30: runs a content pin's own body on a data copy and in-memory docs instead of the shipped ones, so a landing (the
/// Teblor's M7-4 rows, <see cref="TeblorLanding"/>) is proven against the very checks that will hold it after it ships.
/// Inside <see cref="Use"/>, <see cref="Data"/> is the copy's data, <see cref="PageTables.Lines"/> returns the copy's
/// version of a doc it carries, and <see cref="ReadDataFile"/> the copy's version of a data file. The scope is an
/// <see cref="AsyncLocal{T}"/>, so tests running in parallel never see each other's copy; outside one, everything reads
/// the shipped files.
/// </summary>
internal sealed class ContentScope
{
    private static readonly AsyncLocal<ContentScope?> s_current = new();

    /// <summary>The copy's loaded data.</summary>
    public required GameData CopyData { get; init; }

    /// <summary>Docs the copy replaces, by repo-relative path with forward slashes (<c>docs/factions/teblor.md</c>).</summary>
    public IReadOnlyDictionary<string, string[]> Docs { get; init; } = new Dictionary<string, string[]>();

    /// <summary>Data files the copy replaces, by path relative to <c>game/data/</c> with forward slashes, as JSON text.</summary>
    public IReadOnlyDictionary<string, string> Files { get; init; } = new Dictionary<string, string>();

    /// <summary>The data the content pins read: the scope's copy, else the shipped data.</summary>
    public static GameData Data => s_current.Value?.CopyData ?? TestSim.Data;

    /// <summary>Makes <paramref name="scope"/> current until the result is disposed (the previous one comes back).</summary>
    public static IDisposable Use(ContentScope scope)
    {
        ContentScope? previous = s_current.Value;
        s_current.Value = scope;
        return new Restore(previous);
    }

    /// <summary>The scope's lines of <paramref name="relativePath"/> (a fresh array each call: pins edit them), or null.</summary>
    public static string[]? DocLines(string relativePath)
    {
        ContentScope? s = s_current.Value;
        return s != null && s.Docs.TryGetValue(Key(relativePath), out string[]? lines) ? (string[])lines.Clone() : null;
    }

    /// <summary>The text of data file <paramref name="relative"/> (<c>factions/teblor/techs.json</c>): the scope's, else the shipped file.</summary>
    public static string ReadDataFile(string relative)
    {
        ContentScope? s = s_current.Value;
        if (s != null && s.Files.TryGetValue(Key(relative), out string? text)) return text;
        return File.ReadAllText(Path.Combine(TestDataDir.Shipped, relative));
    }

    private static string Key(string path) => path.Replace('\\', '/');

    private sealed class Restore(ContentScope? previous) : IDisposable
    {
        public void Dispose() => s_current.Value = previous;
    }
}
