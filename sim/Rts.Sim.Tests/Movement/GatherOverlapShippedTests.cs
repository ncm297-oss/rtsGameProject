using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherOverlapScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S10 criterion 7: <c>gatherOverlap</c> 0.5 is the shipped data. The shipped rules carry it (the World switches the rule on
/// from the data alone), the save and replay format numbers are unchanged, and a save and a recording made on the base's data
/// (the shipped files without the field: the hash before M6-S10) are refused on the shipped data with
/// <see cref="SaveError.DataMismatch"/> / <see cref="ReplayError.DataMismatch"/>, not crashed. Twins, a save at 3,000 continued
/// and a replay played back on the shipped data are <see cref="GatherOverlapTests"/>' criterion-6 row (its 0.5 data is now
/// the shipped data, hash for hash: <see cref="TheShippedData_IsTheHalfCopy"/>).
/// </summary>
public class GatherOverlapShippedTests
{
    private readonly ITestOutputHelper _out;

    public GatherOverlapShippedTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void TheShippedRules_Carry0Point5_AndTheWorldSwitchesTheRuleOn()
    {
        Assert.Equal(0.5f, TestSim.Data.Rules.GatherOverlap);
        var w = new World(new SimConfig(1, 1, 4, 16) { Data = TestSim.Data });
        Assert.True(w.GatherOverlapOn);
        Assert.Equal(0.5f, w.GatherPairScale);
    }

    /// <summary>The copy the M6-S9 tests call "0.5" (the shipped files with the field written) hashes as the shipped data, so their rows measure the game that ships.</summary>
    [Fact]
    public void TheShippedData_IsTheHalfCopy() => Assert.Equal(TestSim.Data.ContentHash(), DataAt(0.5).ContentHash());

    [Fact]
    public void TheFormatNumbers_AreUnchanged()
    {
        Assert.Equal(10, SaveFormat.CurrentVersion);
        Assert.Equal(9, Replay.CurrentFormatVersion);
        Assert.Equal(6, Replay.TeamlessFormatVersion);
    }

    /// <summary>A save and a recording of the same economy scene made on the base's data: both refused on the shipped data before anything runs.</summary>
    [Fact]
    public void ASaveAndARecording_MadeOnTheBasesData_AreRefused_NotCrashed()
    {
        GameData before = DataWith(null);
        Assert.NotEqual(before.ContentHash(), TestSim.Data.ContentHash());
        SimConfig config = TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: 48, CommandCapacity: 256)
            with { Data = before, Map = Map.MapGenParams.Default with { Forests = 12, GoldMines = 8, StartLocations = 0 }, Combat = false };
        var sim = new Simulation(config);
        var rec = new ReplayRecorder(sim, checkpointInterval: 100);
        EconomyScenario.Setup(sim, 10, player: 0, mineIndex: 0);
        for (int t = 0; t < 600; t++) sim.Tick();
        byte[] save = SaveTestRun.Save(sim);
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(before, save));
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(TestSim.Data, save));
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(rec.ToReplay()), out Replay? replay));
        Assert.True(ReplayPlayer.Run(replay!, before, combat: false).Ok);
        ReplayResult refused = ReplayPlayer.Run(replay!, TestSim.Data, combat: false);
        Assert.Equal(ReplayError.DataMismatch, refused.Error);
        Assert.Equal(0, refused.TicksRun);
        _out.WriteLine($"base data {before.ContentHash():X16}, shipped {TestSim.Data.ContentHash():X16}: save and recording refused");
    }
}
