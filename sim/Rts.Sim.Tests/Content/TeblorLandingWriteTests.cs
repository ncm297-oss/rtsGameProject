namespace Rts.Sim.Tests.Content;

/// <summary>
/// D30 criterion 1, the whole suite: writes <see cref="TeblorLanding"/>'s four data files, teblor.md and docs/02 into the
/// repo the tests run from, so every content test can be run on the landing (in a scratch copy of the repo, never a
/// worktree). Does nothing unless <c>D30_WRITE_LANDING</c> is <c>1</c>; Stress, so never in the default loop. Since D-H8
/// (BUG-1331 item 3) it writes through <see cref="TeblorLanding.EditText"/> and <see cref="TeblorLanding.DocText"/>, so
/// each file keeps its formatting and line endings and <c>git diff</c> shows only the landing's lines; written twice,
/// nothing more changes.
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
        // Everything computed before anything is written: the docs' edits read the page as it is.
        var writes = new List<(string Path, string Text)>();
        foreach (string rel in TeblorLanding.Files)
        {
            string path = Path.Combine(TestDataDir.Shipped, rel);
            writes.Add((path, TeblorLanding.EditText(rel, File.ReadAllText(path))));
        }
        foreach (string rel in TeblorLanding.Docs) writes.Add((Path.Combine(root, rel), TeblorLanding.DocText(rel)));
        foreach ((string path, string text) in writes) File.WriteAllText(path, text); // UTF-8 without a BOM, as the files are
    }
}
