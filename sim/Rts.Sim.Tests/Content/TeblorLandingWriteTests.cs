using System.Text.Json.Nodes;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D30 criterion 1, the whole suite: writes <see cref="TeblorLanding"/>'s four data files, teblor.md and docs/02 into the
/// repo the tests run from, so every content test can be run on the landing (in a scratch copy of the repo, never a
/// worktree). Does nothing unless <c>D30_WRITE_LANDING</c> is <c>1</c>; Stress, so never in the default loop.
/// </summary>
[Trait("Category", "Stress")]
public class TeblorLandingWriteTests
{
    [Fact]
    public void WriteTheLandingIntoThisRepo_WhenAsked()
    {
        if (Environment.GetEnvironmentVariable("D30_WRITE_LANDING") != "1") return;
        string root = TestDataDir.RepoRoot();
        Assert.False(root.Contains(Path.Combine(".claude", "worktrees"), StringComparison.OrdinalIgnoreCase), $"refusing to write the landing into a worktree: {root}");
        foreach ((string rel, string text) in TeblorLanding.FileTexts())
            File.WriteAllText(Path.Combine(TestDataDir.Shipped, rel), JsonNode.Parse(text)!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllLines(Path.Combine(root, "docs", "factions", "teblor.md"), TeblorLanding.PageLines());
        File.WriteAllLines(Path.Combine(root, "docs", "02-game-design.md"), TeblorLanding.Doc02Lines());
    }
}
