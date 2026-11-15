using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Replays;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.GatherOverlapScenes;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S9 criteria 3-6 and 8: the gather overlap (<c>rules.json</c> <c>gatherOverlap</c>) on test data. On, two workers of one
/// player on a gather loop pass and stand closer than their radii sum but never past their scaled pack limit, the mine's bank
/// does not fall; every other pair keeps today's distance (where a scene has no gather pair the state hash is equal tick for
/// tick at 0 and 0.5); a worker leaving the loop separates under today's rules; on is deterministic, saves, replays and
/// allocates nothing.
/// </summary>
[Collection(SerialCollection.Name)] // the allocation row
public class GatherOverlapTests
{
    private readonly ITestOutputHelper _out;

    public GatherOverlapTests(ITestOutputHelper output) => _out = output;

    /// <summary>Float tolerance on a distance (m): a clip lands a float's rounding inside its limit.</summary>
    private const float Tolerance = 1e-4f;

    // ------------------------------------------------------------------ criterion 3 (a): head-on on the leg

    /// <summary>
    /// A Keep at cells 2-5 x 3-6 and a mine at 38-39 x 4-5 on a 44 x 12 flat map; worker A (full load) starts at cell (30, 4)
    /// and walks to the Keep, worker B at (12, 4) walks to the mine: both on the row y = 9 m, head-on. Ticks until they have
    /// passed (and 20 more); returns their smallest centre distance while both walked and the most stuck ticks either had.
    /// </summary>
    private static (float MinDistance, float Sum, int MaxStuck, bool Passed) HeadOn(double overlap, Workers kind)
    {
        Simulation sim = NewSim(DataAt(overlap), 8, width: 44, height: 12);
        World w = sim.World;
        EntityHandle mine = Spawn(w, Mine, 38, 4, w.Data.Rules.StartMineGold);
        Building(sim, 2, 3);
        UnitStore u = w.Units;
        EntityHandle a = Unit(sim, At(sim, 30, 4), type: TypeOf(kind, 1));
        EntityHandle b = Unit(sim, At(sim, 12, 4), type: TypeOf(kind, 0));
        u.Cargo[a.Index] = w.Data.CarryOf(u.TypeId[a.Index]);
        u.CargoKind[a.Index] = ResourceKind.Gold;
        sim.Enqueue(Command.Gather(0, a, At(sim, 38, 4)));
        sim.Enqueue(Command.Gather(0, b, At(sim, 38, 4)));
        sim.Tick();
        sim.Tick();
        Assert.Equal((UnitState.Moving, UnitState.Moving), (u.State[a.Index], u.State[b.Index]));
        Assert.True(MovementSystem.IsGatherPair(u, a.Index, b.Index));
        float min = float.PositiveInfinity;
        int maxStuck = 0, after = -1;
        for (int t = 0; t < 400 && after < 20; t++)
        {
            sim.Tick();
            bool walking = u.State[a.Index] == UnitState.Moving && u.State[b.Index] == UnitState.Moving;
            if (walking) min = MathF.Min(min, Vector2.Distance(u.Position[a.Index], u.Position[b.Index]));
            if (u.State[a.Index] == UnitState.Moving) maxStuck = Math.Max(maxStuck, u.StuckTicks[a.Index]);
            if (u.State[b.Index] == UnitState.Moving) maxStuck = Math.Max(maxStuck, u.StuckTicks[b.Index]);
            if (after >= 0) after++;
            else if (u.Position[a.Index].X < u.Position[b.Index].X) after = 0;
        }
        Assert.True(w.Resources.IsAlive(mine));
        return (min, u.Radius[a.Index] + u.Radius[b.Index], maxStuck, after >= 0);
    }

    [Theory]
    [InlineData(Workers.Laborer)]
    [InlineData(Workers.Gatherer)]
    [InlineData(Workers.Mixed)]
    public void Criterion3a_TwoGatherersMeetingHeadOn_PassCloserThanTheRadiiSum_NoStuckTick(Workers kind)
    {
        var off = HeadOn(0, kind);
        var on = HeadOn(0.5, kind);
        _out.WriteLine($"{kind}: radii sum {on.Sum:F2} m; off: closest {off.MinDistance:F3} m, most stuck {off.MaxStuck}; 0.5: closest {on.MinDistance:F3} m, most stuck {on.MaxStuck}");
        Assert.True(on.Passed && off.Passed, "the two never passed");
        Assert.True(on.MinDistance < on.Sum, $"at 0.5 they passed {on.MinDistance:F3} m apart, not inside the radii sum {on.Sum:F2}");
        Assert.True(on.MinDistance >= PackLimit(on.Sum, 0.5) - Tolerance, $"at 0.5 they came within {on.MinDistance:F3} m, past the scaled pack limit");
        Assert.Equal(0, on.MaxStuck);
    }

    // ------------------------------------------------------------------ criterion 3 (b, c): 12 on one mine

    /// <summary>
    /// 12 workers on one mine for 5 minutes, at 0 and 0.5 (and the gather pairs' floor at 0.25 and 0.75): at 0.5 the workers
    /// standing at the mine stand closer to their nearest neighbour than at 0, no two centres ever come closer than the scaled
    /// pack limit (less float tolerance), and the bank is not lower than at 0. For 0.4 m, 0.75 m and mixed radii.
    /// </summary>
    [Theory]
    [InlineData(Workers.Laborer)]
    [InlineData(Workers.Gatherer)]
    [InlineData(Workers.Mixed)]
    public void Criterion3b_TwelveOnOneMine_StandCloser_NeverPastTheScaledPackLimit_BankNotLower(Workers kind)
    {
        Figures off = Row(0, Node.Mine, kind, 12, 6000);
        _out.WriteLine($"{kind} 12 on one mine, 5 min | banked | trips | stuck ticks | give-ups | closest pair (share of sum) | mean stand gap");
        _out.WriteLine($"0: {off.Cells}");
        foreach (double v in new[] { 0.25, 0.5, 0.75 })
        {
            Figures on = Row(v, Node.Mine, kind, 12, 6000);
            _out.WriteLine($"{v}: {on.Cells}");
            float limitShare = MovementConstants.ShoveSpacing * (1f - (float)v);
            Assert.True(on.MinRatio >= limitShare - Tolerance, $"{kind} at {v}: a pair came to {on.MinRatio:F4} of its radii sum, past the scaled pack limit {limitShare:F3}");
            Assert.True(on.MinDistance > 0.05f, $"{kind} at {v}: two centres within {on.MinDistance:F3} m");
            if (v != 0.5) continue;
            Assert.True(on.MeanStandGap < off.MeanStandGap, $"{kind}: at 0.5 the mine's workers stand {on.MeanStandGap:F3} m from their nearest, not closer than at 0 ({off.MeanStandGap:F3})");
            Assert.True(on.Banked >= off.Banked, $"{kind}: banked {on.Banked} at 0.5, lower than {off.Banked} at 0 (a finding)");
        }
    }

    // ------------------------------------------------------------------ criterion 4: scope

    /// <summary>
    /// The predicate itself on a live scene at 0.5: two of one player's miners are a gather pair (their distance scaled); a
    /// soldier, an Idle worker, a builder and another player's miner beside them are not (the radii sum exactly).
    /// </summary>
    [Fact]
    public void Criterion4_ThePairDistance_IsScaledForAGatherPairOnly()
    {
        Simulation sim = TestSim.Explored(NewSim(DataAt(0.5), 16, players: 2));
        World w = sim.World;
        UnitStore u = w.Units;
        Spawn(w, Mine, MineX, MineY, w.Data.Rules.StartMineGold);
        Building(sim, KeepX, KeepY, 0);
        Building(sim, KeepX, KeepY + 8, 1);
        EntityHandle m1 = Unit(sim, At(sim, 17, 17)), m2 = Unit(sim, At(sim, 17, 18));
        EntityHandle enemy = Unit(sim, At(sim, 22, 17), player: 1);
        EntityHandle soldier = Unit(sim, At(sim, 16, 17), type: Infantry);
        EntityHandle idle = Unit(sim, At(sim, 16, 18));
        EntityHandle builder = Unit(sim, At(sim, 16, 19));
        BuildMaps.SetTotals(sim, 0, 1000, 1000);
        foreach (EntityHandle h in new[] { m1, m2 }) sim.Enqueue(Command.Gather(0, h, At(sim, MineX, MineY)));
        sim.Enqueue(Command.Gather(1, enemy, At(sim, MineX, MineY)));
        sim.Enqueue(Command.Build(0, builder, BuildMaps.House, At(sim, 15, 24)));
        Run(sim, 3);
        Assert.True(u.GatherNode[m1.Index].Generation != 0 && u.GatherNode[enemy.Index].Generation != 0);
        Assert.True(u.BuildTarget[builder.Index].Generation != 0 && u.GatherNode[builder.Index].Generation == 0);
        float Sum(EntityHandle a, EntityHandle b) => u.Radius[a.Index] + u.Radius[b.Index];
        float Pair(EntityHandle a, EntityHandle b) => MovementSystem.PairSum(w, u, a.Index, b.Index);
        Assert.Equal(Sum(m1, m2) * w.GatherPairScale, Pair(m1, m2));
        Assert.Equal(Pair(m1, m2), Pair(m2, m1));
        foreach (EntityHandle other in new[] { soldier, idle, builder, enemy })
        {
            Assert.Equal(Sum(m1, other), Pair(m1, other));
            Assert.Equal(Sum(m1, other), Pair(other, m1));
            Assert.False(MovementSystem.IsGatherPair(u, m1.Index, other.Index));
        }
        // Off, even the gather pair touches at the radii sum.
        Simulation offSim = NewSim(Off, 4);
        EntityHandle o1 = Unit(offSim, At(offSim, 4, 4)), o2 = Unit(offSim, At(offSim, 5, 4));
        Assert.Equal(offSim.World.Units.Radius[o1.Index] * 2f, MovementSystem.PairSum(offSim.World, offSim.World.Units, o1.Index, o2.Index));
    }

    /// <summary>Rows below player 0's Keep that player 1's Keep stands in the two-player scenes: both west of the mine.</summary>
    private const int EnemyKeepRows = 6;

    /// <summary>Pair-ticks within 3 m the two sides of the enemy scene must share in its 2,400 ticks (BUG-1433: it had 2).</summary>
    private const int MinEnemyPairTicks = 500;

    /// <summary>Runs <paramref name="scene"/> on a fresh sim at 0 and at 0.5 for <paramref name="ticks"/> ticks; asserts the hashes are equal every tick.</summary>
    private void HashEqualAtZeroAndHalf(string name, Func<GameData, Simulation> scene, int ticks)
    {
        Simulation off = scene(Off), on = scene(DataAt(0.5));
        Assert.Equal(off.StateHash(), on.StateHash());
        for (int t = 0; t < ticks; t++)
        {
            off.Tick();
            on.Tick();
            Assert.True(off.StateHash() == on.StateHash(), $"{name}: the hashes differ at tick {off.TickNumber}");
        }
        _out.WriteLine($"{name}: equal for {ticks} ticks");
    }

    [Fact]
    public void Criterion4_AnArmyMarchingThroughStandingSoldiers_IsHashEqualAtZeroAndHalf() =>
        HashEqualAtZeroAndHalf("march through standing soldiers", data =>
        {
            Simulation sim = NewSim(data, 64, width: 40, height: 30);
            for (int k = 0; k < 20; k++) Unit(sim, At(sim, 18 + k % 4, 10 + k / 4), type: Infantry);
            var marchers = new List<EntityHandle>();
            for (int k = 0; k < 24; k++) marchers.Add(Unit(sim, At(sim, 3 + k % 4, 8 + k / 4), type: Infantry));
            foreach (EntityHandle h in marchers) sim.Enqueue(Command.Move(0, h, At(sim, 35, 13)));
            return sim;
        }, 600);

    [Fact]
    public void Criterion4_AnIdleWorkerBesideAMiner_IsHashEqualAtZeroAndHalf() =>
        HashEqualAtZeroAndHalf("an Idle worker beside a miner", data =>
        {
            Simulation sim = NewSim(data, 8);
            EntityHandle[] miner = Setup(sim, Node.Mine, Workers.Laborer, 1);
            Unit(sim, At(sim, MineX - 1, MineY));
            Unit(sim, At(sim, MineX - 1, MineY + 1), type: GathererType);
            return sim;
        }, 2400);

    [Fact]
    public void Criterion4_ABuilderBesideAMiner_IsHashEqualAtZeroAndHalf() =>
        HashEqualAtZeroAndHalf("a builder beside a miner", data =>
        {
            Simulation sim = TestSim.Explored(NewSim(data, 8));
            Setup(sim, Node.Mine, Workers.Gatherer, 1);
            EntityHandle builder = Unit(sim, At(sim, MineX - 2, MineY + 3));
            BuildMaps.SetTotals(sim, 0, 1000, 1000);
            sim.Enqueue(Command.Build(0, builder, BuildMaps.House, At(sim, MineX - 2, MineY + 5)));
            return sim;
        }, 2400);

    [Fact]
    public void Criterion4_TwoPlayersWorkersOnOneMine_OneEach_IsHashEqualAtZeroAndHalf() =>
        HashEqualAtZeroAndHalf("two players' workers on one mine, one each", data =>
        {
            Simulation sim = NewSim(data, 8, players: 2);
            Setup(sim, Node.Mine, Workers.Laborer, 1);
            // BUG-1433: player 1's Keep beside player 0's, west of the mine, so both carry to the mine's west face (it stood
            // east of the mine, and the two never met).
            Building(sim, KeepX, KeepY + EnemyKeepRows, 1);
            EntityHandle other = Unit(sim, At(sim, KeepX + 5, KeepY + EnemyKeepRows + 2), player: 1);
            sim.Enqueue(Command.Gather(1, other, At(sim, MineX, MineY)));
            return sim;
        }, 2400);

    /// <summary>
    /// Scenes with gather pairs beside other units at 0.5: a soldier walking through a mining line, an Idle worker and a builder
    /// beside 6 miners, two players' 6 workers each on one mine. Every tick, every pair within 3 m that is not one player's two
    /// gatherers has the radii sum as its distance, and no such pair ever stands closer than at 0 does by more than the noise of
    /// a different crowd: the pair distance is unchanged for them, whatever the gather pairs beside them do.
    /// </summary>
    [Fact]
    public void Criterion4_ScenesWithGatherPairs_KeepTheRadiiSumForEveryOtherPair()
    {
        foreach ((string name, Func<GameData, (Simulation, EntityHandle)> scene) in new (string, Func<GameData, (Simulation, EntityHandle)>)[]
        {
            ("a soldier walking through a mining line", data =>
            {
                Simulation sim = NewSim(data, 16);
                Setup(sim, Node.Mine, Workers.Laborer, 8);
                Run(sim, 200);
                EntityHandle soldier = Unit(sim, At(sim, 16, 8), type: Infantry);
                return (sim, soldier);
            }),
            ("an Idle worker and a builder beside 6 miners", data =>
            {
                Simulation sim = TestSim.Explored(NewSim(data, 16));
                Setup(sim, Node.Mine, Workers.Mixed, 6);
                Unit(sim, At(sim, MineX - 1, MineY + 2)); // Idle beside the mine
                EntityHandle builder = Unit(sim, At(sim, MineX - 2, MineY + 3));
                BuildMaps.SetTotals(sim, 0, 1000, 1000);
                sim.Enqueue(Command.Build(0, builder, BuildMaps.House, At(sim, MineX - 2, MineY + 5)));
                return (sim, default);
            }),
            ("two players' 6 workers each on one mine", data =>
            {
                Simulation sim = NewSim(data, 24, players: 2);
                Setup(sim, Node.Mine, Workers.Laborer, 6);
                Building(sim, KeepX, KeepY + EnemyKeepRows, 1); // BUG-1433: beside player 0's Keep, so the two sides share the mine's west face
                for (int k = 0; k < 6; k++)
                {
                    EntityHandle h = Unit(sim, At(sim, KeepX + 5 + k % 2, KeepY + EnemyKeepRows + 1 + k / 2), player: 1);
                    sim.Enqueue(Command.Gather(1, h, At(sim, MineX, MineY)));
                }
                return (sim, default);
            }),
        })
        {
            float offClosest = ClosestOtherPair(scene(Off), 2400, scaled: false, out int offPairs);
            float onClosest = ClosestOtherPair(scene(DataAt(0.5)), 2400, scaled: true, out int onPairs);
            _out.WriteLine($"{name}: closest non-gather pair (share of its radii sum) {offClosest:F3} at 0, {onClosest:F3} at 0.5 ({offPairs} / {onPairs} pair-ticks within reach)");
            Assert.True(onPairs > 0, $"{name}: no non-gather pair ever came near");
            // BUG-1433: the enemy scene must bring the two sides together, not two pair-ticks in 2 minutes.
            if (name.StartsWith("two players")) Assert.True(onPairs >= MinEnemyPairTicks && offPairs >= MinEnemyPairTicks, $"{name}: the sides met for {offPairs} / {onPairs} pair-ticks");
        }
    }

    /// <summary>
    /// Ticks <paramref name="sim"/>; every tick asserts each live pair within 3 m that is no gather pair has the radii sum as its
    /// movement distance (<see cref="MovementSystem.PairSum"/>), and that each gather pair's is scaled when <paramref name="scaled"/>.
    /// Returns the smallest share of the radii sum any non-gather pair stood at.
    /// </summary>
    private static float ClosestOtherPair((Simulation Sim, EntityHandle Walker) scene, int ticks, bool scaled, out int pairTicks)
    {
        Simulation sim = scene.Sim;
        World w = sim.World;
        UnitStore u = w.Units;
        float closest = float.PositiveInfinity;
        pairTicks = 0;
        for (int t = 0; t < ticks; t++)
        {
            // The walker (when the scene has one) crosses the workers' lane between the Keep and the mine, back and forth.
            if (scene.Walker.Generation != 0 && t % 150 == 0)
                sim.Enqueue(Command.Move(0, scene.Walker, At(sim, 16, t / 150 % 2 == 0 ? 28 : 8)));
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                for (int j = i + 1; j < u.Capacity; j++)
                {
                    if (!u.Alive[j]) continue;
                    float sum = u.Radius[i] + u.Radius[j];
                    float d = Vector2.Distance(u.Position[i], u.Position[j]);
                    if (d > 3f) continue;
                    bool pair = u.GatherNode[i].Generation != 0 && u.GatherNode[j].Generation != 0 && u.Owner[i] == u.Owner[j];
                    float expected = pair && scaled ? sum * w.GatherPairScale : sum;
                    Assert.Equal(expected, MovementSystem.PairSum(w, u, i, j));
                    if (pair) continue;
                    pairTicks++;
                    closest = MathF.Min(closest, d / sum);
                }
            }
        }
        return closest;
    }

    // ------------------------------------------------------------------ criterion 5: leaving the loop

    /// <summary>
    /// Six workers on a mine at 0.5 until a pair stands closer than its radii sum (overlapped); then one of them gets a Move
    /// (<paramref name="stop"/> false) or a Stop. Every unit's step every tick stays within its speed (no teleport), and the
    /// pair is back at its radii sum or more within 2 s (Move) or, for a Stop, within the stopped worker's mate's next walk.
    /// </summary>
    [Theory]
    [InlineData(false, Workers.Laborer)]
    [InlineData(false, Workers.Gatherer)]
    [InlineData(true, Workers.Laborer)]
    [InlineData(true, Workers.Mixed)]
    public void Criterion5_AWorkerLeavingTheLoop_SeparatesUnderTodaysRules_NoTeleport(bool stop, Workers kind)
    {
        Simulation sim = NewSim(DataAt(0.5), 12);
        EntityHandle[] ws = Setup(sim, Node.Mine, kind, 6);
        UnitStore u = sim.World.Units;
        int a = -1, b = -1;
        for (int t = 0; t < 2400 && a < 0; t++)
        {
            sim.Tick();
            for (int k = 0; k < ws.Length && a < 0; k++)
                for (int m = k + 1; m < ws.Length && a < 0; m++)
                {
                    int i = ws[k].Index, j = ws[m].Index;
                    if (Vector2.Distance(u.Position[i], u.Position[j]) < 0.9f * (u.Radius[i] + u.Radius[j])) { a = i; b = j; }
                }
        }
        Assert.True(a >= 0, "no pair ever overlapped");
        float sum = u.Radius[a] + u.Radius[b];
        float startDistance = Vector2.Distance(u.Position[a], u.Position[b]);
        var handle = new EntityHandle(a, u.Generation[a]);
        sim.Enqueue(stop ? Command.Stop(0, handle) : Command.Move(0, handle, u.Position[a] + new Vector2(-12f, 0f)));
        var before = new Vector2[u.Capacity];
        int separatedAt = -1, limit = stop ? 1200 : 40;
        for (int t = 0; t < limit + 1 && separatedAt < 0; t++)
        {
            Array.Copy(u.Position, before, u.Capacity);
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && Vector2.Distance(before[i], u.Position[i]) > u.Speed[i] + Tolerance)
                    Assert.Fail($"unit {i} stepped {Vector2.Distance(before[i], u.Position[i]):F4} m in tick {sim.TickNumber}, past its speed {u.Speed[i]:F3}");
            if (t >= 1) Assert.False(MovementSystem.IsGatherPair(u, a, b), "the worker is still on the loop after its order");
            if (Vector2.Distance(u.Position[a], u.Position[b]) >= sum - Tolerance) separatedAt = t;
        }
        _out.WriteLine($"{(stop ? "Stop" : "Move")} {kind}: the pair stood {startDistance:F3} m apart (radii sum {sum:F2}); back at the radii sum after {separatedAt} ticks");
        Assert.True(separatedAt >= 0, $"the pair is still overlapped {limit} ticks after the {(stop ? "Stop" : "Move")}");
    }

    // ------------------------------------------------------------------ criterion 6: determinism with the knob on

    /// <summary>The 5-minute economy scene on a generated map (12 forests, 8 mines): two players, 10 Laborers each, on <paramref name="data"/>, combat off (as the M3-2 economy fuzz records).</summary>
    private static Simulation EconomyScene(GameData data, ulong seed, out ReplayRecorder recorder)
    {
        SimConfig config = TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 48, CommandCapacity: 256)
            with { Data = data, Map = MapGenParams.Default with { Forests = 12, GoldMines = 8, StartLocations = 0 }, Combat = false };
        var sim = new Simulation(config);
        recorder = new ReplayRecorder(sim, checkpointInterval: 100);
        EconomyScenario.Setup(sim, 10, player: 0, mineIndex: 0);
        EconomyScenario.Setup(sim, 10, player: 1, mineIndex: 1);
        return sim;
    }

    [Fact]
    public void Criterion6_FiveMinuteEconomy_TwinsEqualEveryTick_SaveMidSceneContinuesEqual_ReplayPlaysBack()
    {
        GameData data = DataAt(0.5);
        Simulation a = EconomyScene(data, 3, out ReplayRecorder rec), b = EconomyScene(data, 3, out _);
        Simulation? loaded = null;
        for (int t = 0; t < 6000; t++)
        {
            a.Tick();
            b.Tick();
            loaded?.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"twins differ after tick {a.TickNumber}");
            if (loaded != null) Assert.True(a.StateHash() == loaded.StateHash(), $"the save loaded at tick 3000 differs after tick {a.TickNumber}");
            if (t == 2999)
            {
                loaded = SaveTestRun.Load(data, SaveTestRun.Save(a));
                Assert.Equal(a.StateHash(), loaded.StateHash());
            }
        }
        int banked = a.World.Gold[0] + a.World.Wood[0] + a.World.Gold[1] + a.World.Wood[1];
        Replay rep = rec.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(rep), out Replay? back));
        ReplayResult res = ReplayPlayer.Run(back!, data, combat: false);
        Assert.True(res.Ok, $"replay: {res.Error} at tick {res.Tick}");
        // The recording carries the data's hash: it refuses data with the overlap off.
        Assert.False(ReplayPlayer.Run(back!, Off, combat: false).Ok);
        _out.WriteLine($"6,000 ticks equal; banked {banked}; replay {rep.Commands.Length} commands, {rep.Checkpoints.Length} checkpoints matched");
        Assert.True(banked > 800, $"banked {banked}");
    }

    /// <summary>
    /// The permutation pattern (LocalMovementTests' column through the crowd): a mine's two workers (a Laborer and a Gatherer)
    /// spawned in either order give the same states unit for unit (compared in spawn-list order, not slot order) for 2.5
    /// minutes, at 0 and at 0.5.
    /// </summary>
    /// <remarks>
    /// As there, not a general guarantee: the economy phase walks workers in slot order (each picks its stand point from the
    /// others' places at that moment) and a walker's clips against standing units go in slot order, so a mine's 4 workers
    /// first differ between spawn orders at tick 1,568 with the overlap off (and at 404 at 0.5). Same seed and commands
    /// still give the same hash (the twins rows).
    /// </remarks>
    [Theory]
    [InlineData(0.0, 2)]
    [InlineData(0.5, 2)]
    public void Criterion6_TheResult_DoesNotDependOnSpawnOrder(double overlap, int count)
    {
        ulong[] forward = ByIdentity(overlap, count, reversed: false), backward = ByIdentity(overlap, count, reversed: true);
        int firstDiff = -1;
        for (int t = 0; t < forward.Length && firstDiff < 0; t++) if (forward[t] != backward[t]) firstDiff = t;
        _out.WriteLine($"{count} workers at {overlap}: spawn orders first differ at tick {firstDiff} (-1: never) of {forward.Length}");
        Assert.Equal(-1, firstDiff);
    }

    /// <summary>Per tick, a hash of every worker's position, velocity, state, goal cell and cargo in spawn-list order.</summary>
    private static ulong[] ByIdentity(double overlap, int count, bool reversed)
    {
        Simulation sim = NewSim(DataAt(overlap), count + 4);
        EntityHandle[] ws = Setup(sim, Node.Mine, Workers.Mixed, count, reversed);
        UnitStore u = sim.World.Units;
        var hashes = new ulong[3000];
        for (int t = 0; t < hashes.Length; t++)
        {
            sim.Tick();
            ulong h = 14695981039346656037UL;
            void Mix(uint bits) { h = (h ^ bits) * 1099511628211UL; }
            foreach (EntityHandle e in ws)
            {
                int i = e.Index;
                Mix(BitConverter.SingleToUInt32Bits(u.Position[i].X));
                Mix(BitConverter.SingleToUInt32Bits(u.Position[i].Y));
                Mix(BitConverter.SingleToUInt32Bits(u.Velocity[i].X));
                Mix(BitConverter.SingleToUInt32Bits(u.Velocity[i].Y));
                Mix((uint)u.State[i]);
                Mix((uint)u.GoalCell[i]);
                Mix((uint)u.Cargo[i]);
            }
            hashes[t] = h;
        }
        return hashes;
    }

    // ------------------------------------------------------------------ criterion 8: allocation

    [Fact]
    public void Criterion8_ATickWithTheOverlapOn_50GatheringWorkersAnd200Marchers_AllocatesNothing()
    {
        Simulation sim = GatherOverlapPerfTests.Mixed(DataAt(0.5), 200, 50, seed: 9);
        Assert.True(sim.World.GatherOverlapOn);
        for (int t = 0; t < 300; t++) sim.Tick();
        Action ticks = () =>
        {
            for (int t = 0; t < 100; t++) sim.Tick();
        };
        AllocationProbe.AssertZero(ticks, _out);
    }
}
