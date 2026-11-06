using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S7: writes <see cref="PreMapFixtures"/>' replay files and prints the save digests, only with
/// <see cref="PreMapFixtures.WriteFlag"/> set. It was run once on the diff base (ab54ddb); a later run would overwrite the
/// proof that a generated match still writes the old bytes, so it fails whenever the flag is set.
/// </summary>
public class PreMapFixtureWriter
{
    private readonly ITestOutputHelper _out;

    public PreMapFixtureWriter(ITestOutputHelper output) => _out = output;

    [Fact]
    public void WritesTheFixtures_OnlyWithTheFlag()
    {
        if (Environment.GetEnvironmentVariable(PreMapFixtures.WriteFlag) != "1") return;
        Directory.CreateDirectory(PreMapFixtures.Folder);
        foreach ((string name, _) in PreMapFixtures.ReplayNames)
        {
            Replay replay = PreMapFixtures.ReplayScene(name);
            string text = PreMapFixtures.Normalize(PreMapFixtures.Text(ReplayFormat.Write(replay)));
            File.WriteAllText(PreMapFixtures.ReplayPath(name), text);
            _out.WriteLine($"{name}: format {replay.FormatVersion}, {text.Length} bytes");
        }
        foreach ((string name, _, _) in PreMapFixtures.SaveDigests)
        {
            byte[] bytes = PreMapFixtures.Normalize(SaveTestRun.Save(PreMapFixtures.SaveScene(name)));
            _out.WriteLine($"(\"{name}\", {bytes.Length}, 0x{PreMapFixtures.Digest(bytes):X16}UL),");
        }
        Assert.Fail("fixtures written; paste the save digests into PreMapFixtures.SaveDigests and rerun without the flag");
    }
}
