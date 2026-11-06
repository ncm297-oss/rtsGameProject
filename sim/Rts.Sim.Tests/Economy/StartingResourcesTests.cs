using System.Collections.Immutable;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.Tests.Save;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S6: the starting-resources presets (<c>rules.json</c> <c>startingResources</c>, docs/02 "Game flow" 2), chosen per
/// match by <see cref="SimConfig.StartingResources"/>: every seat's gold and wood at tick 0 come from the preset, the
/// default is <c>standard</c> (today's start, bit for bit), an unknown id is refused, and a <c>high</c> match is deterministic.
/// </summary>
public class StartingResourcesTests
{
    private static StartingResourcesDef Preset(string id) => TestSim.Data.Rules.StartingResources[TestSim.Data.Rules.FindStartingResources(id)];

    [Fact]
    public void TheDefault_IsStandard()
    {
        SimConfig config = TestSim.Config(1, 2, 16, 16);
        Assert.Equal("standard", config.StartingResources);
        Assert.Same(Preset("standard"), config.StartingPreset);
    }

    /// <summary>Every seat of a match starts with its preset's gold and wood, whatever the seat count; nothing else of the ledger moves.</summary>
    [Theory]
    [InlineData("standard", 1)]
    [InlineData("standard", 4)]
    [InlineData("high", 1)]
    [InlineData("high", 2)]
    [InlineData("high", 4)]
    public void EverySeat_StartsWithThePresetsGoldAndWood(string id, int players)
    {
        var sim = new Simulation(TestSim.Config(3, players, 16, 16) with { StartingResources = id });
        StartingResourcesDef preset = Preset(id);
        for (int p = 0; p < players; p++)
        {
            Assert.Equal(preset.Gold, sim.World.Gold[p]);
            Assert.Equal(preset.Wood, sim.World.Wood[p]);
            Assert.Equal(0, sim.World.HalfPop[p]);
        }
    }

    /// <summary>The shipped numbers (the Producer's defaults, docs/02 "Game flow" 2): standard 200 / 200, high 1000 / 1000.</summary>
    [Fact]
    public void TheShippedPresets_AreStandard200_AndHigh1000()
    {
        Assert.Equal((200, 200), (Preset("standard").Gold, Preset("standard").Wood));
        Assert.Equal((1000, 1000), (Preset("high").Gold, Preset("high").Wood));
    }

    /// <summary>An id the data lacks (or none) is refused by <see cref="SimConfig.Validate"/>, so no sim is built on it.</summary>
    [Theory]
    [InlineData("low")]
    [InlineData("Standard")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownPreset_IsRefusedByValidate(string? id)
    {
        SimConfig config = TestSim.Config(1, 2, 16, 16) with { StartingResources = id! };
        ArgumentException e = Assert.ThrowsAny<ArgumentException>(config.Validate);
        Assert.Equal(nameof(SimConfig.StartingResources), e.ParamName);
        Assert.ThrowsAny<ArgumentException>(() => new Simulation(config));
    }

    /// <summary>
    /// A human-only <c>high</c> match on the menu's generated map differs from its <c>standard</c> twin only by the ledger at
    /// tick 0: the hashes differ, and with the high banks written back to the standard numbers they are equal (map, resources,
    /// the start bases, the fog, every RNG stream). Ticked on without commands that spend, the two stay equal but for the banks.
    /// </summary>
    [Fact]
    public void AHumanOnlyHighScene_DiffersFromStandard_OnlyByTheLedger()
    {
        Simulation standard = Scene("standard"), high = Scene("high");
        standard.Tick();
        high.Tick(); // the spawns apply
        Assert.NotEqual(standard.StateHash(), high.StateHash());
        for (int p = 0; p < 2; p++)
        {
            Assert.Equal((1000, 1000), (high.World.Gold[p], high.World.Wood[p]));
            Assert.Equal((200, 200), (standard.World.Gold[p], standard.World.Wood[p]));
        }
        WriteBanks(high, standard);
        Assert.Equal(standard.StateHash(), high.StateHash());
        for (int t = 0; t < 200; t++)
        {
            standard.Tick();
            high.Tick();
            Assert.Equal(standard.StateHash(), high.StateHash());
        }
    }

    /// <summary>A <c>high</c> match with two AI seats, built twice from one seed: the same state hash every 50 ticks for 3,000 ticks, and the same AI command counts.</summary>
    [Fact]
    public void AHighAiMatch_IsDeterministic()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation a = AiScenes.Generated(seats, 4, record: false, out _, configure: c => c with { StartingResources = "high" });
        Simulation b = AiScenes.Generated(seats, 4, record: false, out _, configure: c => c with { StartingResources = "high" });
        Simulation standard = AiScenes.Generated(seats, 4, record: false, out _);
        bool parted = false;
        for (int t = 0; t < 3000; t++)
        {
            a.Tick();
            b.Tick();
            standard.Tick();
            if (a.TickNumber % 50 == 0) Assert.Equal(a.StateHash(), b.StateHash());
            parted |= a.StateHash() != standard.StateHash();
        }
        for (int p = 0; p < 2; p++) Assert.Equal(a.AiOf(p)!.CommandsSent, b.AiOf(p)!.CommandsSent);
        Assert.True(parted);
    }

    /// <summary>A <c>high</c> AI match recorded for 1,500 ticks plays back to every checkpoint (format 8 through the text).</summary>
    [Fact]
    public void AHighAiReplay_PlaysToEveryCheckpoint()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, 2, record: true, out ReplayRecorder? rec, configure: c => c with { StartingResources = "high" });
        for (int t = 0; t < 1500; t++) sim.Tick();
        Replay replay = rec!.ToReplay();
        Assert.Equal(8, replay.FormatVersion);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        Assert.Equal("high", back!.StartingResources);
        ReplayResult played = ReplayPlayer.Run(back, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(15, back.Checkpoints.Length);
    }

    /// <summary>A <c>high</c> AI match saved at tick 500 loads with its preset and follows the unsaved run for 500 ticks.</summary>
    [Fact]
    public void AHighAiMatch_SavedAt500_FollowsTheUnsavedRun()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, 3, record: false, out _, configure: c => c with { StartingResources = "high" });
        for (int t = 0; t < 500; t++) sim.Tick();
        Simulation loaded = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(sim));
        Assert.Equal("high", loaded.World.Config.StartingResources);
        for (int t = 0; t < 500; t++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.Equal(sim.StateHash(), loaded.StateHash());
        }
    }

    /// <summary>The menu's generated map (seed 1, 12 forests, 8 mines) with two human seats and their start bases, at <paramref name="preset"/>.</summary>
    private static Simulation Scene(string preset)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Human, SeatController.Human);
        return AiScenes.Generated(seats, 1, record: false, out _, forests: 12, configure: c => c with { StartingResources = preset });
    }

    private static void WriteBanks(Simulation into, Simulation from)
    {
        for (int p = 0; p < from.World.Gold.Length; p++)
        {
            into.World.Ledger.Gold[p] = from.World.Gold[p];
            into.World.Ledger.Wood[p] = from.World.Wood[p];
        }
    }
}
