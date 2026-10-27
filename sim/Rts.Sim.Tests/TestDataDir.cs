using System.Text.Json.Nodes;

namespace Rts.Sim.Tests;

/// <summary>Locates the shipped <c>game/data/</c> folder and makes editable temp copies of it for negative tests.</summary>
public sealed class TestDataDir : IDisposable
{
    private TestDataDir(string path) => Path = path;

    /// <summary>Root of the temp copy.</summary>
    public string Path { get; }

    /// <summary>Repo root, found by walking up from the test binaries to <c>RtsGame.sln</c>.</summary>
    public static string RepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "RtsGame.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>The real <c>game/data/</c> folder.</summary>
    public static string Shipped => System.IO.Path.Combine(RepoRoot(), "game", "data");

    /// <summary>Copies the shipped data into a fresh temp folder (deleted on dispose).</summary>
    public static TestDataDir CopyOfShipped()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rts-data-tests", Guid.NewGuid().ToString("N"));
        foreach (string file in Directory.GetFiles(Shipped, "*", SearchOption.AllDirectories))
        {
            string target = System.IO.Path.Combine(path, System.IO.Path.GetRelativePath(Shipped, file));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            RetryOnce(() => System.IO.File.Copy(file, target, overwrite: true));
        }
        return new TestDataDir(path);
    }

    /// <summary>Full path of a file given relative to the data root with forward slashes.</summary>
    public string FullPath(string rel) => System.IO.Path.Combine(Path, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>Parses a JSON file, lets <paramref name="edit"/> change it, and writes it back.</summary>
    public void EditJson(string rel, Action<JsonObject> edit)
    {
        JsonObject root = JsonNode.Parse(RetryOnce(() => System.IO.File.ReadAllText(FullPath(rel))))!.AsObject();
        edit(root);
        string text = root.ToJsonString();
        RetryOnce(() => System.IO.File.WriteAllText(FullPath(rel), text));
    }

    /// <summary>Sets (or with <paramref name="rawJson"/> null, removes) a dotted field of one unit.</summary>
    public void SetUnitField(string faction, string unitId, string dottedField, string? rawJson)
    {
        EditJson($"factions/{faction}/units.json", root =>
        {
            JsonObject unit = root["units"]!.AsArray().Select(n => n!.AsObject()).Single(u => (string)u["id"]! == unitId);
            string[] parts = dottedField.Split('.');
            JsonObject parent = unit;
            for (int i = 0; i < parts.Length - 1; i++)
                parent = parent[parts[i]]!.AsObject();
            if (rawJson == null) parent.Remove(parts[^1]);
            else parent[parts[^1]] = JsonNode.Parse(rawJson);
        });
    }

    /// <summary>
    /// Deletes the copy. BUG-0915: a file the OS won't let go of (held a moment by an indexer or virus scanner, or
    /// read-only) throws <see cref="UnauthorizedAccessException"/> on Windows, not only <see cref="IOException"/>; either
    /// is retried once and then left behind (a temp folder), never failing the test that used the copy.
    /// </summary>
    public void Dispose()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == 0) Thread.Sleep(50);
            }
        }
    }

    /// <summary>BUG-0915: a file step under disk contention (another process holding a just-written file) is tried once more after a short wait.</summary>
    private static void RetryOnce(Action io) => RetryOnce(() => { io(); return 0; });

    private static T RetryOnce<T>(Func<T> io)
    {
        try
        {
            return io();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Thread.Sleep(100);
            return io();
        }
    }
}
