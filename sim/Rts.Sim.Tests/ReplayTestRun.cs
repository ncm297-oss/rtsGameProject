using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;

namespace Rts.Sim.Tests;

/// <summary>Recorded runs and comparisons shared by the replay tests.</summary>
public static class ReplayTestRun
{
    /// <summary>Path of the checked-in golden replay.</summary>
    public static string GoldenPath =>
        Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Replays", "cross_map_seed1.replay");

    /// <summary>The golden's run: <see cref="CrossMapScenario"/> seed 1, 200 units, ordered across the map, exactly 1,500 ticks, checkpoints every 100.</summary>
    public static Replay RecordGolden()
    {
        ReplayRecorder? recorder = null;
        CrossMapScenario s = CrossMapScenario.Create(1, ScenarioTests.Army, ScenarioTests.StartRadius,
            onCreated: sim => recorder = new ReplayRecorder(sim, checkpointInterval: 100));
        s.OrderAll();
        while (s.Sim.TickNumber < 1500) s.Sim.Tick();
        return recorder!.ToReplay();
    }

    /// <summary>
    /// A 2-player run of <paramref name="ticks"/> ticks with a recorder attached from tick 0: 40 spawns
    /// near the center, a Noop, group moves every 50 ticks to random nearby cells, one Move with a NaN
    /// target (dropped by the sim, still logged) and one to a stale handle.
    /// </summary>
    public static (Simulation Sim, ReplayRecorder Recorder) RecordSmall(ulong seed, int ticks, int checkpointInterval = 100)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 128));
        var recorder = new ReplayRecorder(sim, checkpointInterval);
        NavGrid g = sim.World.NavGrid;
        int center = MoveScenario.CentralCell(g);
        FlowField field = FlowField.Build(g, center);
        var near = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (field.CostAt(c) <= 12f) near.Add(c);
        var rng = new SimRng(seed, 61);
        for (int i = 0; i < 40; i++)
        {
            Vector2 corner = MoveScenario.Center(g, near[rng.NextInt(0, near.Count)]) - new Vector2(MapConstants.CellSize / 2);
            sim.Enqueue(Command.SpawnUnit(i % 2, i % TestSim.UnitTypeCount, corner + new Vector2(0.05f + rng.NextFloat() * 1.9f, 0.05f + rng.NextFloat() * 1.9f)));
        }
        sim.Enqueue(Command.Noop(1));
        UnitStore u = sim.World.Units;
        for (int t = 0; t < ticks; t++)
        {
            if (t % 50 == 2)
            {
                Vector2 target = MoveScenario.Center(g, near[rng.NextInt(0, near.Count)]);
                MoveScenario.MoveAll(sim, target);
            }
            if (t == 120) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), new Vector2(float.NaN, 3f)));
            if (t == 130) sim.Enqueue(Command.Move(1, new EntityHandle(1, u.Generation[1] + 7), MoveScenario.Center(g, center)));
            sim.Tick();
        }
        return (sim, recorder);
    }

    /// <summary>
    /// M1-7: a 2-player run of <paramref name="ticks"/> ticks recorded from tick 0: 48 spawns near the
    /// center, then <see cref="OrderMix.Issue"/> every 3 ticks (every unit-order kind, queued and not).
    /// </summary>
    public static (Simulation Sim, ReplayRecorder Recorder) RecordOrders(ulong seed, int ticks, int checkpointInterval = 100)
    {
        const int units = 48, perTick = 6;
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: units, CommandCapacity: units + perTick + 8));
        var recorder = new ReplayRecorder(sim, checkpointInterval);
        List<int> cells = OrderMix.Cells(sim.World.NavGrid, 14f);
        var rng = new SimRng(seed, 43);
        OrderMix.SpawnInto(sim, units, cells, ref rng);
        for (int t = 0; t < ticks; t++)
        {
            if (t >= 2 && t % 3 == 0) OrderMix.Issue(sim, ref rng, cells, perTick);
            sim.Tick();
        }
        return (sim, recorder);
    }

    /// <summary>Asserts two replays are equal field for field (floats by bit pattern).</summary>
    public static void AssertEqual(Replay expected, Replay actual)
    {
        Assert.Equal(expected.FormatVersion, actual.FormatVersion);
        Assert.Equal(expected.Combat, actual.Combat);
        Assert.Equal(expected.SimVersion, actual.SimVersion);
        Assert.Equal(expected.DataHash, actual.DataHash);
        Assert.Equal(expected.Map, actual.Map);
        Assert.Equal(BitConverter.SingleToInt32Bits(expected.Map.MinPassableFraction), BitConverter.SingleToInt32Bits(actual.Map.MinPassableFraction));
        Assert.Equal(expected.Seed, actual.Seed);
        Assert.Equal(expected.PlayerCount, actual.PlayerCount);
        Assert.Equal(expected.UnitCapacity, actual.UnitCapacity);
        Assert.Equal(expected.CommandCapacity, actual.CommandCapacity);
        Assert.Equal(expected.ResourceCapacity, actual.ResourceCapacity);
        Assert.Equal(BitConverter.SingleToInt32Bits(expected.Map.MineSpacing), BitConverter.SingleToInt32Bits(actual.Map.MineSpacing));
        Assert.Equal(expected.CheckpointInterval, actual.CheckpointInterval);
        Assert.Equal(expected.TickCount, actual.TickCount);
        Assert.Equal(expected.Commands.Length, actual.Commands.Length);
        for (int i = 0; i < expected.Commands.Length; i++)
        {
            Command a = expected.Commands[i], b = actual.Commands[i];
            string at = $"command {i}";
            Assert.True(a.Kind == b.Kind, at);
            Assert.True(a.Player == b.Player, at);
            Assert.True(a.Tick == b.Tick, at);
            Assert.True(a.Sequence == b.Sequence, at);
            Assert.True(a.TypeId == b.TypeId, at);
            Assert.True(BitConverter.SingleToInt32Bits(a.Position.X) == BitConverter.SingleToInt32Bits(b.Position.X), at);
            Assert.True(BitConverter.SingleToInt32Bits(a.Position.Y) == BitConverter.SingleToInt32Bits(b.Position.Y), at);
            Assert.True(a.Unit == b.Unit, at);
            Assert.True(a.Flags == b.Flags, at);
            Assert.True(a.Target == b.Target, at);
            Assert.True(a.TargetIsBuilding == b.TargetIsBuilding, at);
        }
        Assert.Equal(expected.Checkpoints.ToArray(), actual.Checkpoints.ToArray());
    }

    /// <summary>A copy of <paramref name="r"/> with some fields replaced.</summary>
    public static Replay With(Replay r, int? formatVersion = null, ulong? dataHash = null,
        int? players = null, IEnumerable<Command>? commands = null, IEnumerable<ReplayCheckpoint>? checkpoints = null, string? simVersion = null,
        bool? combat = null) => new()
    {
        FormatVersion = formatVersion ?? r.FormatVersion,
        Combat = combat ?? r.Combat,
        SimVersion = simVersion ?? r.SimVersion,
        DataHash = dataHash ?? r.DataHash,
        Map = r.Map,
        Seed = r.Seed,
        PlayerCount = players ?? r.PlayerCount,
        UnitCapacity = r.UnitCapacity,
        CommandCapacity = r.CommandCapacity,
        ResourceCapacity = r.ResourceCapacity,
        CheckpointInterval = r.CheckpointInterval,
        TickCount = r.TickCount,
        Commands = commands == null ? r.Commands : commands.ToImmutableArray(),
        Checkpoints = checkpoints == null ? r.Checkpoints : checkpoints.ToImmutableArray(),
    };
}
