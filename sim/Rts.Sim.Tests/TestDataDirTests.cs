namespace Rts.Sim.Tests;

/// <summary>
/// BUG-0915 (<c>BuildingSightLoaderTests.ABadBuildingSightRule_IsAnErrorAtTheField("0")</c> red once in a full run, green
/// alone): the temp copy every negative data test loads must not fail a test that passed for a reason outside it. On
/// Windows <c>Directory.Delete</c> throws <see cref="UnauthorizedAccessException"/>, not <see cref="IOException"/>, for a
/// file it may not delete (read-only, or held by an indexer or virus scanner a moment after it was written); the dispose
/// caught only the second, so the <c>using</c> around a passing load failed the row.
/// </summary>
public class TestDataDirTests
{
    [Fact]
    public void Dispose_AFileItMayNotDelete_LeavesTheFolderBehind_WithoutThrowing()
    {
        TestDataDir dir = TestDataDir.CopyOfShipped();
        string file = dir.FullPath("common/rules.json");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            dir.Dispose();
        }
        finally
        {
            if (File.Exists(file)) File.SetAttributes(file, FileAttributes.Normal);
            if (Directory.Exists(dir.Path)) Directory.Delete(dir.Path, recursive: true);
        }
    }

    [Fact]
    public void Dispose_RemovesTheCopy()
    {
        TestDataDir dir = TestDataDir.CopyOfShipped();
        Assert.True(File.Exists(dir.FullPath("common/rules.json")));
        dir.Dispose();
        Assert.False(Directory.Exists(dir.Path));
    }
}
