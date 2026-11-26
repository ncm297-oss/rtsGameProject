using System.Text.RegularExpressions;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-4 criterion 1 (BUG-1771, rule 79): a test that replaces an opening's <c>buildOrder</c> must also take its
/// <c>buildOrders</c> out, or a seat whose difficulty has an entry plays the shipped entry, not the test's list. So every
/// write goes through <see cref="AiScenes.SetBuildOrder"/>: no <c>.cs</c> under <c>sim/Rts.Sim.Tests</c> outside the data
/// track's <c>Content/</c> and <c>QA/Content/</c> assigns the <c>buildOrder</c> key by indexer except that helper's one line, and no test
/// reads a seat's steps as <c>Opening.BuildOrder</c> (the seat plays <c>AiPlayer.Steps</c>).
/// </summary>
public class BuildOrderHelperSourceTests
{
    private static readonly Regex s_write = new(@"\[""buildOrder""\]\s*=(?!=)");

    private static IEnumerable<(string Rel, string Text)> SimTestSources()
    {
        string root = Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests");
        foreach (string f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (rel.StartsWith("Content/", StringComparison.Ordinal) || rel.StartsWith("QA/Content/", StringComparison.Ordinal)
                || rel.StartsWith("bin/", StringComparison.Ordinal) || rel.StartsWith("obj/", StringComparison.Ordinal)) continue;
            yield return (rel, File.ReadAllText(f));
        }
    }

    [Fact]
    public void OnlyTheHelper_WritesBuildOrder()
    {
        var hits = new List<string>();
        int files = 0;
        foreach ((string rel, string text) in SimTestSources())
        {
            files++;
            foreach (Match m in s_write.Matches(text))
                hits.Add($"{rel}:{text.Take(m.Index).Count(c => c == '\n') + 1}");
        }
        Assert.True(files > 300, $"only {files} files read");
        string hit = Assert.Single(hits);
        Assert.StartsWith("Ai/AiScenes.cs:", hit);
        Assert.Contains("o.Remove(\"buildOrders\");", File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Ai", "AiScenes.cs")));
    }

    [Fact]
    public void NoTest_ReadsASeatsStepsAsTheOpeningsBuildOrder()
    {
        var hits = new List<string>();
        foreach ((string rel, string text) in SimTestSources())
            if (Regex.IsMatch(text, @"\.Opening\.BuildOrder\b")) hits.Add(rel);
        Assert.Empty(hits);
    }

    /// <summary>The helper itself: the list written, <c>buildOrders</c> gone, the opening's other fields kept.</summary>
    [Fact]
    public void SetBuildOrder_WritesTheList_AndTakesBuildOrdersOut()
    {
        var opening = System.Text.Json.Nodes.JsonNode.Parse("""
            { "id": "standard", "buildOrder": [ { "at": { "time": 0 }, "do": "expand" } ],
              "buildOrders": { "hard": [ { "at": { "time": 1 }, "do": "expand" } ] }, "attack": { "minArmyPop": 8 } }
            """)!;
        AiScenes.SetBuildOrder(opening, AiScenes.BuildOrder(("time", 5, "research", "age_ii", 0)));
        Assert.Null(opening["buildOrders"]);
        Assert.False(opening.AsObject().ContainsKey("buildOrders"));
        Assert.Equal("age_ii", (string?)opening["buildOrder"]![0]!["id"]);
        Assert.Single(opening["buildOrder"]!.AsArray());
        Assert.Equal(8, (int)opening["attack"]!["minArmyPop"]!);
    }
}
