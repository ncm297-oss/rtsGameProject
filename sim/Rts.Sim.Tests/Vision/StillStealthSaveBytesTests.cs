using System.Numerics;
using System.Security.Cryptography;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-2 criterion 10 (third row): a save made on data without <c>stillStealth</c> is byte for byte what <c>main</c> wrote
/// before M8-2. The digests were taken on the base (10acb53) with this file and no M8-2 sim change, so they pin the bytes
/// main writes: the shipped data (no stealth bonus at all) and the M8-1 forest copy (a <c>FRST</c> part, no field unit).
/// </summary>
public class StillStealthSaveBytesTests
{
    /// <summary>A 300-tick brawl of 12 a side on a flat map, a Laborer standing aside, saved after the last tick.</summary>
    private static byte[] BrawlSave(GameData data, bool tree)
    {
        var sim = new Simulation(new SimConfig(5, 2, 96, 800) { Data = data }, LocalMovementTests.Flat(48));
        if (tree) ForestScenes.Tree(sim, 10, 10);
        CombatScenes.Place(sim, 0, data.FindUnit("malazan_laborer"), CombatScenes.At(sim, 11, 10));
        CombatScenes.FlatBrawl(sim, 12, new Vector2(24f, 24f), 12f);
        for (int t = 0; t < 300; t++) sim.Tick();
        return SaveTestRun.Save(sim);
    }

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [Fact]
    public void ASaveOnTheShippedData_IsTheBytesMainWrites()
    {
        byte[] bytes = BrawlSave(TestSim.Data, tree: false);
        Assert.Equal(-1, bytes.AsSpan().IndexOf("STIL"u8));
        Assert.Equal("094A8AE06379ACEC8FDE745767CD50B9649D80BD1A4915484A6BF5B16646B9BA 465698", Digest(bytes) + $" {bytes.Length}");
    }

    [Fact]
    public void ASaveOnTheForestCopy_IsTheBytesMainWrites()
    {
        byte[] bytes = BrawlSave(ForestScenes.Data3, tree: true);
        Assert.Equal(-1, bytes.AsSpan().IndexOf("STIL"u8));
        Assert.Equal("788743CA6E1EE1E5587D3B2997AAA11599E320C527202B65993B010F165C6CE4 459215", Digest(bytes) + $" {bytes.Length}");
    }
}
