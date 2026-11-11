using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M7-3b: tech and ability ids were renumbered once (BUG-1171: the common techs, then each faction's in faction order). A
/// save or a recording holds those ids as indexes, so one made before M7-3b would mean other techs and abilities now; both
/// are refused for their data hash (the shipped <see cref="GameData.ContentHash"/> moved with the id order), with no
/// save- or replay-format number change. The fixtures under <c>Replays/pre_m7_3b/</c> were written on the diff base
/// (de591f0) by <see cref="TechIdScenes"/>; they are never rewritten. A save made now round-trips byte for byte.
/// </summary>
public class PreM73bRefusalTests
{
    private readonly ITestOutputHelper _out;

    public PreM73bRefusalTests(ITestOutputHelper output) => _out = output;

    private static string Fixture(string name) => Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Replays", "pre_m7_3b", name);

    private static byte[] MainSave() => Convert.FromBase64String(File.ReadAllText(Fixture("save_tech_cooldown.b64")));

    /// <summary>The pre-M7-3b save (a researched <c>moranth_supply</c>, a Cusser cooldown running) is refused <see cref="SaveError.DataMismatch"/>, and no sim comes back.</summary>
    [Fact]
    public void ASaveMadeBeforeM73b_WithATechAndACooldown_IsRefusedDataMismatch()
    {
        byte[] bytes = MainSave();
        // The fixture is a whole, valid save of its own build: only the data hash refuses it (a flipped hash byte, re-checksummed, stays refused the same way).
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(TestSim.Data, bytes));
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bytes);
        ulong saved = BitConverter.ToUInt64(bytes, at.DataHash);
        _out.WriteLine($"saved data hash {saved:X16}, shipped {TestSim.Data.ContentHash():X16}");
        Assert.NotEqual(TestSim.Data.ContentHash(), saved);
    }

    /// <summary>
    /// Why the refusal matters: the same scene on this build researches <c>moranth_supply</c> under another id than the save's
    /// (6 before M7-3b), so the save's research flags, loaded as they stand, would name <c>ranged_weapons_2</c>.
    /// </summary>
    [Fact]
    public void TheSavedTechId_NamesAnotherTechNow()
    {
        GameData d = TestSim.Data;
        Assert.Equal(7, d.FindTech("moranth_supply"));
        Assert.Equal("ranged_weapons_2", d.Techs[6].Key);
    }

    /// <summary>The same scene saved on this build loads and saves again byte for byte, with the tech and the cooldown kept.</summary>
    [Fact]
    public void ASaveMadeNow_WithATechAndACooldown_RoundTripsByteIdentical()
    {
        Simulation sim = TechIdScenes.SaveScene();
        int ready = TechIdScenes.CusserReadyTick(sim);
        Assert.True(ready > sim.TickNumber, $"the cooldown has run out: ready {ready}, tick {sim.TickNumber}");
        Assert.True(sim.World.HasTech(0, TestSim.Data.FindTech("moranth_supply")));
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation loaded = SaveTestRun.Load(TestSim.Data, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(loaded));
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        Assert.True(loaded.World.HasTech(0, TestSim.Data.FindTech("moranth_supply")));
        Assert.Equal(ready, TechIdScenes.CusserReadyTick(loaded));
        // The pre-M7-3b save of the same scene differs: it is of other data.
        Assert.NotEqual(MainSave(), bytes);
    }

    /// <summary>The pre-M7-3b recording (a Research by id) reads, then is refused before tick 1 with <see cref="ReplayError.DataMismatch"/>, not a crash.</summary>
    [Fact]
    public void ARecordingMadeBeforeM73b_IsRefusedForItsDataHash()
    {
        Assert.Equal(ReplayError.None, ReplayFormat.TryReadFile(Fixture("replay_research.replay"), out Replay? replay));
        Command research = Assert.Single(replay!.Commands, c => c.Kind == CommandKind.Research);
        Assert.Equal(4, research.TypeId); // melee_weapons_1 before M7-3b
        Assert.Equal("melee_weapons_2", TestSim.Data.Techs[4].Key); // what 4 names now
        ReplayResult result = ReplayPlayer.Run(replay, TestSim.Data);
        Assert.Equal(ReplayError.DataMismatch, result.Error);
        Assert.Equal(0, result.TicksRun);
    }

    /// <summary>The same recording made now plays to its end (the refusal is the data hash, not the scene).</summary>
    [Fact]
    public void TheSameRecordingMadeNow_Plays()
    {
        Replay replay = TechIdScenes.RecordResearch();
        Assert.Equal(TestSim.Data.FindTech("melee_weapons_1"), Assert.Single(replay.Commands, c => c.Kind == CommandKind.Research).TypeId);
        ReplayResult result = ReplayPlayer.Run(replay, TestSim.Data);
        Assert.True(result.Ok, $"{result.Error} at tick {result.Tick}");
        Assert.Equal(200, result.TicksRun);
    }
}
