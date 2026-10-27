using System.Text;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA for M5-H4 (session 2026-10-26-2108). BUG-0990's load bounds against real saves: a save the sim itself wrote, of a
/// config <see cref="SimConfig.Validate"/> accepts, must load (the brief: "never refuses a real save of an empty world at
/// large capacities").
/// </summary>
public class SimHardeningM5H4QaTests
{
    private readonly ITestOutputHelper _out;

    public SimHardeningM5H4QaTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => TestSim.Data;

    /// <summary>
    /// BUG-1040: <see cref="SimConfig.Validate"/> used to accept any command capacity of at least 1, <c>TrySave</c> wrote
    /// it, but the load refused one over <c>SaveFormat.MaxCommandCapacity</c> (65,536) as Malformed: the sim's own save of
    /// an empty match did not load. Restated with the fix (the config is rejected up front; docs/03 "Save format"): the
    /// bound is the config's, so such a sim can't be made (refused by the existing path, <see cref="SimConfig.Validate"/>'s
    /// <see cref="ArgumentOutOfRangeException"/>, which the save header maps to InvalidHeader), and at the bound the sim's
    /// own save loads.
    /// </summary>
    [Theory]
    [InlineData(65_537)]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public void ARealSave_WithACommandCapacityAboveTheLoadBound_Loads(int commands)
    {
        var config = new SimConfig(5, 2, 64, commands) { Data = Data, StatsSampleCapacity = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation(config, LocalMovementTests.Flat(16)));

        var atBound = new Simulation(config with { CommandCapacity = SaveFormat.MaxCommandCapacity }, LocalMovementTests.Flat(16));
        atBound.Tick();
        Assert.True(atBound.TrySave(new MemoryStream(), out SaveError saved), "the save itself is refused");
        Assert.Equal(SaveError.None, saved);
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(Data, SaveTestRun.Save(atBound)));
    }

    /// <summary>
    /// The BUG-0990 pre-check's new terms against real saves: an empty world with one capacity at the format maximum
    /// (1,000,000; the units 200,000 to keep the row's memory sane) saves and loads, and the load ticks.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("projectile")]
    [InlineData("zone")]
    [InlineData("building")]
    [InlineData("resource")]
    [InlineData("unit")]
    [InlineData("all")]
    public void ARealEmptyWorldSave_WithOneCapacityAtTheFormatMaximum_Loads(string which)
    {
        int max = SaveFormat.MaxCapacity;
        bool all = which == "all";
        var config = new SimConfig(5, 2, which == "unit" || all ? 200_000 : 64, SaveFormat.MaxCommandCapacity)
        {
            Data = Data,
            StatsSampleCapacity = 0,
            ProjectileCapacity = which == "projectile" || all ? max : 0,
            ZoneCapacity = which == "zone" || all ? max : Abilities.ZoneStore.DefaultCapacity,
            BuildingCapacity = which == "building" || all ? max : Entities.BuildingStore.DefaultCapacity,
            ResourceCapacity = which == "resource" || all ? max : Entities.ResourceStore.DefaultCapacity,
        };
        var sim = new Simulation(config, LocalMovementTests.Flat(16));
        sim.Tick();
        byte[] bytes = SaveTestRun.Save(sim);
        _out.WriteLine($"{which}: {bytes.Length:N0} bytes, pre-check bound {SaveHeader.MinBodyBytes(config, 16 * 16):N0}");
        Simulation loaded = SaveTestRun.Load(Data, bytes);
        loaded.Tick();
        sim.Tick();
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }

    /// <summary>
    /// BUG-0960 item 2's new load checks (a live unit's goal cell on the map, each player's visible box inside it) against
    /// real saves at the map's edges: two players' units in the four corners ordered to points far off the map (the
    /// order clamps them) and back across it, fog boxes touching every edge; the save of every tick to 200 loads and
    /// hashes equal.
    /// </summary>
    [Fact]
    public void RealSaves_WithGoalsAndFogBoxesAtTheMapEdges_AllLoad()
    {
        var config = new SimConfig(7, 2, 16, 64) { Data = Data, StatsSampleCapacity = 0 };
        var sim = new Simulation(config, LocalMovementTests.Flat(16));
        float edge = 16 * Map.MapConstants.CellSize;
        var corners = new[] { new System.Numerics.Vector2(0.6f, 0.6f), new(edge - 0.6f, 0.6f), new(0.6f, edge - 0.6f), new(edge - 0.6f, edge - 0.6f) };
        for (int k = 0; k < 8; k++) sim.Enqueue(Commands.Command.SpawnUnit(k % 2, CombatScenes.HeavyInfantry, corners[k % 4]));
        sim.Tick();
        var far = new[] { new System.Numerics.Vector2(-500f, -500f), new(5000f, -500f), new(-500f, 5000f), new(5000f, 5000f) };
        int loads = 0;
        for (int t = 0; t < 200; t++)
        {
            if (t % 40 == 0)
                for (int k = 0; k < 8; k++)
                    if (sim.World.Units.Alive[k]) sim.Enqueue(Commands.Command.Move(k % 2, MoveScenario.Handle(sim, k), far[(k + t / 40) % 4]));
            sim.Tick();
            Simulation loaded = SaveTestRun.Load(Data, SaveTestRun.Save(sim));
            Assert.Equal(sim.StateHash(), loaded.StateHash());
            loads++;
        }
        Assert.Equal(200, loads);
    }

    /// <summary>
    /// The pre-check bound stays below what each section really writes, slot for slot: the save of an empty world grows by
    /// more per projectile and zone slot than <c>MinBodyBytes</c> charges (48 and 24).
    /// </summary>
    [Theory]
    [InlineData("projectile", 48)]
    [InlineData("zone", 24)]
    [InlineData("unit", 32)]
    [InlineData("building", 16)]
    [InlineData("resource", 16)]
    public void EachPreCheckTerm_IsBelowWhatTheSectionWrites_PerSlot(string which, int charged)
    {
        int Size(int n)
        {
            var c = new SimConfig(5, 2, which == "unit" ? n : 64, 64) { Data = Data, StatsSampleCapacity = 0 };
            c = which switch
            {
                "projectile" => c with { ProjectileCapacity = n },
                "zone" => c with { ZoneCapacity = n },
                "building" => c with { BuildingCapacity = n },
                "resource" => c with { ResourceCapacity = n },
                _ => c,
            };
            return SaveTestRun.Save(new Simulation(c, LocalMovementTests.Flat(16))).Length;
        }
        int a = Size(1000), b = Size(2000);
        double perSlot = (b - a) / 1000.0;
        _out.WriteLine($"{which}: {perSlot:F1} bytes a slot written, {charged} charged");
        Assert.True(perSlot > charged, $"{which}: writes {perSlot:F1} a slot, the pre-check charges {charged}");
    }

    /// <summary>
    /// BUG-1040 round 1: at the bound the queue holds 65,536 pending commands; a save of that full queue (written between
    /// ticks, every slot used) loads with the same pending commands and the same sequence counters, one more enqueue is
    /// refused on both, and both tick to the same hash. Proves the bound is reachable by a real save, not only by an empty one.
    /// </summary>
    [Fact]
    public void ARealSave_OfAFullQueueAtTheBound_LoadsAndTicksAlike()
    {
        var config = new SimConfig(9, 2, 64, SimConfig.MaxCommandCapacity) { Data = Data, StatsSampleCapacity = 0 };
        var sim = new Simulation(config, LocalMovementTests.Flat(16));
        sim.Tick();
        for (int i = 0; i < SimConfig.MaxCommandCapacity; i++) sim.Enqueue(Command.Noop(i & 1));
        Assert.Equal(SimConfig.MaxCommandCapacity, sim.PendingCommandCount);

        Simulation loaded = SaveTestRun.Load(Data, SaveTestRun.Save(sim));
        SaveTestRun.AssertSameState(sim, loaded, "full queue at the bound");
        Assert.ThrowsAny<InvalidOperationException>(() => loaded.Enqueue(Command.Noop(0)));
        Assert.Equal(SimConfig.MaxCommandCapacity, loaded.PendingCommandCount);
        // Stamped TickNumber + 1, so they apply in the tick after next (docs/03 "Commands"); the hash is compared after each.
        for (int t = 0; t < 2; t++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.Equal(sim.StateHash(), loaded.StateHash());
        }
        Assert.Equal(0, sim.PendingCommandCount);
        Assert.Equal(0, loaded.PendingCommandCount);
    }

    /// <summary>
    /// BUG-1040 round 1: the replay half. A match recorded with the command capacity at the bound writes a replay that reads
    /// and plays back to equal checkpoints; the same file with the header one over it is refused InvalidHeader by
    /// <see cref="ReplayPlayer.Run"/> (no throw building the sim), and so is 1,000,000, which read before the fix.
    /// </summary>
    [Fact]
    public void AReplayRecordedAtTheCommandBound_PlaysBack_AndOneOverIsRefusedWithoutThrowing()
    {
        var config = new SimConfig(11, 2, 64, SimConfig.MaxCommandCapacity) { Data = Data, StatsSampleCapacity = 0, Map = MapGenParams.Default with { Width = 64, Height = 64 } };
        var sim = new Simulation(config);
        var rec = new ReplayRecorder(sim, checkpointInterval: 20, tickCapacity: 100);
        sim.Enqueue(Command.SpawnUnit(0, CombatScenes.Laborer, new System.Numerics.Vector2(20f, 20f)));
        for (int t = 0; t < 100; t++) sim.Tick();
        Replay r = rec.ToReplay();
        Assert.Equal(SimConfig.MaxCommandCapacity, r.CommandCapacity);
        byte[] bytes = ReplayFormat.Write(r);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? back));
        ReplayResult played = ReplayPlayer.Run(back!, Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(100, played.TicksRun);

        string text = Encoding.ASCII.GetString(bytes);
        string body = text[..text.LastIndexOf("checksum ", StringComparison.Ordinal)];
        foreach (int over in new[] { SimConfig.MaxCommandCapacity + 1, 1_000_000 })
        {
            string forged = body.Replace($"\ncommand-capacity {SimConfig.MaxCommandCapacity}\n", $"\ncommand-capacity {over}\n");
            Assert.NotEqual(body, forged);
            Assert.Equal(ReplayError.InvalidHeader, ReplayFormat.TryRead(ReplayFormat.Seal(forged), out Replay? refused));
            Assert.Null(refused);
            // A replay object that skipped the reader (built in code) is refused by the player itself, before any sim.
            ReplayResult direct = ReplayPlayer.Run(new Replay
            {
                FormatVersion = back!.FormatVersion, SimVersion = back.SimVersion, Seed = back.Seed, PlayerCount = back.PlayerCount, Seats = back.Seats,
                UnitCapacity = back.UnitCapacity, CommandCapacity = over, ResourceCapacity = back.ResourceCapacity,
                Map = back.Map, Combat = back.Combat, DataHash = back.DataHash, TickCount = back.TickCount,
                CheckpointInterval = back.CheckpointInterval, Commands = back.Commands, Checkpoints = back.Checkpoints,
            }, Data);
            Assert.Equal(ReplayError.InvalidHeader, direct.Error);
            Assert.Equal(0, direct.TicksRun);
        }
    }

    /// <summary>
    /// BUG-1040 round 1: every capacity a sim can be built with saves and loads, swept from 1 to the bound (powers of two,
    /// their neighbours, and the game's 4,096), with a unit walking and a command pending so the queue is not empty.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4_095)]
    [InlineData(4_096)]
    [InlineData(32_768)]
    [InlineData(65_535)]
    [InlineData(65_536)]
    public void EveryBuildableCommandCapacity_SavesAndLoads(int commands)
    {
        var config = new SimConfig(13, 1, 8, commands) { Data = Data, StatsSampleCapacity = 0 };
        var sim = new Simulation(config, LocalMovementTests.Flat(16));
        sim.Enqueue(Command.SpawnUnit(0, CombatScenes.Laborer, new System.Numerics.Vector2(4f, 4f)));
        sim.Tick();
        sim.Tick();
        if (commands > 1) sim.Enqueue(Command.Noop(0));
        Simulation loaded = SaveTestRun.Load(Data, SaveTestRun.Save(sim));
        Assert.Equal(commands, loaded.World.Config.CommandCapacity);
        SaveTestRun.AssertSameState(sim, loaded, $"capacity {commands}");
    }
}
