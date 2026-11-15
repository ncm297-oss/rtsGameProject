using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.QA;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.GatherOverlapScenes;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M6-S9 (session 2026-11-14-2156, full tier): the gather overlap (<c>rules.json</c> <c>gatherOverlap</c>) attacked with the
/// knob on, on test copies of the shipped data, each scene also run at 0 as the control. Every tick (<see cref="GatherOverlapQaWatch"/>):
/// finite positions, no unit on blocked ground, no gather pair past its scaled pack limit, no unit stepping past its speed by
/// more than float noise, BUG-0146's node-leg wedge, and the longest a worker went without a trip.
/// </summary>
[Collection(SerialCollection.Name)]
[Trait("Category", "Stress")]
public class GatherOverlapQaStressTests
{
    private readonly ITestOutputHelper _out;

    public GatherOverlapQaStressTests(ITestOutputHelper output) => _out = output;

    /// <summary>BUG-0146's bound (GatherPocketStressTests): two retry periods.</summary>
    private const int WedgeBound = 2 * Economy.EconomyConstants.RetryTicks;

    /// <summary>The fuzz's trip window (criterion 7): 60 s.</summary>
    private const int TripWindow = 60 * SimConstants.TicksPerSecond;

    /// <summary>A step past speed by more than this is a jump (a shove and a walk in one tick stay within speed by construction).</summary>
    private const float StepNoise = 1e-3f;

    private static void AssertHard(GatherOverlapQaWatch watch, string name)
    {
        Assert.True(watch.Failure == null, $"{name}: {watch.Failure}");
        Assert.True(watch.MaxStepExcess <= StepNoise, $"{name}: a step past speed by {watch.MaxStepExcess:F4} m ({watch.MaxStepAt})");
    }

    // ------------------------------------------------------------------ a mine wrapped by 24 and 40 workers

    [Theory]
    [InlineData(0.0, Workers.Laborer, 24)]
    [InlineData(0.5, Workers.Laborer, 24)]
    [InlineData(0.75, Workers.Laborer, 24)]
    [InlineData(0.0, Workers.Laborer, 40)]
    [InlineData(0.5, Workers.Laborer, 40)]
    [InlineData(0.75, Workers.Laborer, 40)]
    [InlineData(0.0, Workers.Gatherer, 24)]
    [InlineData(0.5, Workers.Gatherer, 24)]
    [InlineData(0.75, Workers.Gatherer, 24)]
    [InlineData(0.0, Workers.Gatherer, 40)]
    [InlineData(0.5, Workers.Gatherer, 40)]
    [InlineData(0.75, Workers.Gatherer, 40)]
    [InlineData(0.5, Workers.Mixed, 40)]
    [InlineData(0.75, Workers.Mixed, 40)]
    public void WrappedMine_InvariantsHold(double overlap, Workers kind, int count)
    {
        Simulation sim = NewSim(DataAt(overlap), count + 4);
        Setup(sim, Node.Mine, kind, count);
        var watch = new GatherOverlapQaWatch(sim);
        watch.Run(4000);
        int banked = sim.World.Gold[0];
        string name = $"{kind} x{count} at {overlap}";
        _out.WriteLine($"{name}: banked {banked}; {watch.Line}");
        AssertHard(watch, name);
        Assert.True(watch.LongestWedge <= WedgeBound, $"{name}: node-leg wedge {watch.LongestWedge} ticks ({watch.WedgeAt})");
    }

    // ------------------------------------------------------------------ GatherPocketStressTests' maps with the knob on

    public enum Shape { OneSide, Staircase, DepotCorridor, TreeRing, MineOneSide }

    private const int PocketW = 32, PocketH = 26;

    /// <summary>GatherPocketStressTests.BuildPocket, copied (private there).</summary>
    private static EntityHandle BuildPocket(Simulation sim, Shape shape)
    {
        World w = sim.World;
        EntityHandle node = default;
        void T(int x, int y) => Spawn(w, Tree, x, y, TreeWood);
        switch (shape)
        {
            case Shape.OneSide:
                for (int x = 10; x <= 14; x++) T(x, 3);
                T(10, 4); T(11, 4); T(13, 4); T(14, 4);
                node = Spawn(w, Tree, 12, 4, TreeWood);
                break;
            case Shape.Staircase:
            {
                var path = new HashSet<int>();
                (int, int)[] cells = { (12, 5), (13, 5), (13, 6), (14, 6), (14, 7), (15, 7), (15, 8), (16, 8), (16, 9) };
                foreach ((int x, int y) in cells) path.Add(y * PocketW + x);
                for (int y = 3; y <= 9; y++)
                    for (int x = 9; x <= 19; x++)
                    {
                        if (x == 12 && y == 4) continue;
                        if (!path.Contains(y * PocketW + x)) T(x, y);
                    }
                node = Spawn(w, Tree, 12, 4, TreeWood);
                break;
            }
            case Shape.DepotCorridor:
                for (int x = 10; x <= 15; x++) T(x, 3);
                T(11, 4); T(13, 4); T(14, 4);
                for (int y = 5; y <= 10; y++) T(11, y);
                for (int y = 7; y <= 10; y++) T(14, y);
                node = Spawn(w, Tree, 12, 4, TreeWood);
                Building(sim, 14, 5, type: TestSim.Data.FindBuilding("malazan_depot"));
                break;
            case Shape.TreeRing:
                for (int x = 7; x <= 19; x++)
                {
                    T(x, 2);
                    if (x != 13) T(x, 12);
                }
                for (int y = 3; y <= 11; y++)
                {
                    T(7, y);
                    T(19, y);
                }
                node = Spawn(w, Tree, 13, 7, TreeWood);
                break;
            case Shape.MineOneSide:
                for (int x = 10; x <= 15; x++) T(x, 2);
                for (int y = 3; y <= 4; y++)
                {
                    T(10, y); T(11, y); T(14, y); T(15, y);
                }
                node = Spawn(w, Mine, 12, 3, 100_000);
                break;
        }
        return node;
    }

    [Theory]
    [InlineData(Shape.OneSide, 12)]
    [InlineData(Shape.OneSide, 40)]
    [InlineData(Shape.Staircase, 12)]
    [InlineData(Shape.Staircase, 40)]
    [InlineData(Shape.DepotCorridor, 12)]
    [InlineData(Shape.DepotCorridor, 40)]
    [InlineData(Shape.TreeRing, 12)]
    [InlineData(Shape.TreeRing, 40)]
    [InlineData(Shape.MineOneSide, 12)]
    [InlineData(Shape.MineOneSide, 40)]
    public void Pocket_KnobOn_NoWedgeOutOfReach_InvariantsHold(Shape shape, int workers)
    {
        var failures = new List<string>();
        foreach (double overlap in new[] { 0.0, 0.5, 0.75 })
        {
            int worst = 0;
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var sim = new Simulation(new SimConfig(seed, 1, 48, 4 * 48 + 16) { Data = DataAt(overlap) }, Flat(PocketW, PocketH));
                World w = sim.World;
                UnitStore u = w.Units;
                TestSim.KeepIn(sim, 0);
                EntityHandle node = BuildPocket(sim, shape);
                Building(sim, 24, 18);
                var rng = new SimRng(seed, 907);
                var before = (bool[])u.Alive.Clone();
                for (int k = 0; k < workers; k++)
                    sim.Enqueue(Command.SpawnUnit(0, Laborer, new Vector2(4f + rng.NextFloat() * 36f, 30f + rng.NextFloat() * 16f)));
                Run(sim, 2);
                int cell = w.Resources.Cell[node.Index];
                for (int i = 0; i < u.Capacity; i++)
                    if (u.Alive[i] && !before[i]) sim.Enqueue(Command.Gather(0, new EntityHandle(i, u.Generation[i]), At(sim, cell % PocketW, cell / PocketW)));
                (long g0, long w0) = Conserved(w);
                var watch = new GatherOverlapQaWatch(sim);
                for (int t = 0; t < 3000; t++)
                {
                    watch.Tick();
                    (long g, long wd) = Conserved(w);
                    if (g != g0 || wd != w0) failures.Add($"{overlap} seed {seed}: conservation broken at tick {sim.TickNumber}");
                }
                worst = Math.Max(worst, watch.LongestWedge);
                string name = $"{shape} x{workers} at {overlap} seed {seed}";
                if (watch.Failure != null) failures.Add($"{name}: {watch.Failure}");
                if (watch.MaxStepExcess > StepNoise) failures.Add($"{name}: step past speed by {watch.MaxStepExcess:F4} ({watch.MaxStepAt})");
                if (watch.LongestWedge > WedgeBound) failures.Add($"{name}: wedge {watch.LongestWedge} ({watch.WedgeAt})");
            }
            _out.WriteLine($"{shape} x{workers} at {overlap}: longest node-leg wedge over seeds 1-8: {worst} ticks");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(10)));
    }

    // ------------------------------------------------------------------ a drop-off against a cliff

    /// <summary>
    /// 40 x 32 map: a level-1 plateau on columns 0-9 and rows 0-9 (its edge cells are cliffs, blocked), a Keep at 10-13 x 10-13
    /// with its west and north faces on the cliff, a mine at 10-11 x 20-21 against the west cliff: every trip runs along the
    /// cliff to a hall that can be reached from two faces only.
    /// </summary>
    private static Simulation CliffScene(GameData data, Workers kind, int count)
    {
        var rows = new string[32];
        for (int y = 0; y < 32; y++)
        {
            var c = new char[40];
            for (int x = 0; x < 40; x++) c[x] = x <= 9 || y <= 9 ? '1' : '0';
            rows[y] = new string(c);
        }
        var sim = new Simulation(new SimConfig(3, 1, count + 4, 4 * count + 32) { Data = data }, LocalMovementTests.Rows(rows));
        Spawn(sim.World, Mine, 10, 20, MineGold);
        Building(sim, 10, 10);
        UnitStore u = sim.World.Units;
        var before = (bool[])u.Alive.Clone();
        int n = 0;
        for (int y = 16; y <= 29 && n < count; y++)
            for (int x = 18; x <= 36 && n < count; x += 2, n++)
                sim.Enqueue(Command.SpawnUnit(0, TypeOf(kind, n), At(sim, x, y)));
        Run(sim, 2);
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !before[i]) sim.Enqueue(Command.Gather(0, new EntityHandle(i, u.Generation[i]), At(sim, 10, 20)));
        sim.Tick();
        return sim;
    }

    [Theory]
    [InlineData(0.0, Workers.Laborer)]
    [InlineData(0.5, Workers.Laborer)]
    [InlineData(0.75, Workers.Laborer)]
    [InlineData(0.0, Workers.Gatherer)]
    [InlineData(0.5, Workers.Gatherer)]
    [InlineData(0.75, Workers.Gatherer)]
    public void DropOffAgainstACliff_InvariantsHold_EveryoneKeepsDelivering(double overlap, Workers kind)
    {
        Simulation sim = CliffScene(DataAt(overlap), kind, 20);
        var watch = new GatherOverlapQaWatch(sim);
        watch.Run(6000);
        string name = $"cliff {kind} at {overlap}";
        _out.WriteLine($"{name}: banked {sim.World.Gold[0]}; {watch.Line}");
        AssertHard(watch, name);
        Assert.True(watch.LongestWedge <= WedgeBound, $"{name}: node-leg wedge {watch.LongestWedge} ({watch.WedgeAt})");
        // Off is the control: 20 Gatherers queueing at a three-sided mine go 95 s without a trip with today's rules.
        if (overlap > 0) Assert.True(watch.LongestNoTrip <= TripWindow, $"{name}: {watch.LongestNoTrip} ticks without a trip ({watch.NoTripAt})");
    }

    // ------------------------------------------------------------------ node exhaustion: 24 overlapped workers re-target at once

    [Theory]
    [InlineData(0.0, Workers.Laborer)]
    [InlineData(0.5, Workers.Laborer)]
    [InlineData(0.75, Workers.Laborer)]
    [InlineData(0.5, Workers.Gatherer)]
    [InlineData(0.75, Workers.Gatherer)]
    public void NodeExhaustion_24Workers_RetargetAtOnce_NoJump_KeepDelivering(double overlap, Workers kind)
    {
        Simulation sim = NewSim(DataAt(overlap), 32);
        World w = sim.World;
        // A mine that runs out about 40 s in, and a second one 7 m south of it (inside the 20 m node search).
        int first = Spawn(w, Mine, MineX, MineY, 900).Index;
        EntityHandle second = Spawn(w, Mine, MineX, MineY + 7, MineGold);
        Building(sim, KeepX, KeepY);
        UnitStore us = w.Units;
        var before = (bool[])us.Alive.Clone();
        var spawnAt = new List<Vector2>();
        for (int y = 10; y <= 25 && spawnAt.Count < 24; y++)
            for (int x = 2; x <= 8 && spawnAt.Count < 24; x++)
                spawnAt.Add(At(sim, x, y));
        for (int k = 0; k < 24; k++) sim.Enqueue(Command.SpawnUnit(0, TypeOf(kind, k), spawnAt[k]));
        Run(sim, 2);
        var list = new List<EntityHandle>();
        for (int i = 0; i < us.Capacity; i++) if (us.Alive[i] && !before[i]) list.Add(new EntityHandle(i, us.Generation[i]));
        EntityHandle[] ws = list.ToArray();
        Assert.Equal(24, ws.Length);
        foreach (EntityHandle h in ws) sim.Enqueue(Command.Gather(0, h, At(sim, MineX, MineY)));
        sim.Tick();
        var watch = new GatherOverlapQaWatch(sim);
        int diedAt = -1, overlappedAtDeath = 0;
        for (int t = 0; t < 6000; t++)
        {
            watch.Tick();
            if (diedAt < 0 && !w.Resources.Alive[first])
            {
                diedAt = sim.TickNumber;
                UnitStore u = w.Units;
                for (int a = 0; a < ws.Length; a++)
                    for (int b = a + 1; b < ws.Length; b++)
                        if (Vector2.Distance(u.Position[ws[a].Index], u.Position[ws[b].Index]) < u.Radius[ws[a].Index] + u.Radius[ws[b].Index]) overlappedAtDeath++;
            }
        }
        Assert.True(diedAt > 0, "the first mine never ran out");
        int onSecond = 0;
        foreach (EntityHandle h in ws) if (w.Units.GatherNode[h.Index] == second) onSecond++;
        string name = $"exhaustion {kind} at {overlap}";
        _out.WriteLine($"{name}: mine out at tick {diedAt} with {overlappedAtDeath} overlapped pairs; on the second mine at the end {onSecond}/24; banked {w.Gold[0]}; {watch.Line}");
        AssertHard(watch, name);
        Assert.True(watch.LongestWedge <= WedgeBound, $"{name}: node-leg wedge {watch.LongestWedge} ({watch.WedgeAt})");
        Assert.True(watch.LongestNoTrip <= TripWindow, $"{name}: {watch.LongestNoTrip} ticks without a trip ({watch.NoTripAt})");
        Assert.Equal(24, onSecond);
    }

    // ------------------------------------------------------------------ a worker killed while overlapped

    [Theory]
    [InlineData(0.5, Workers.Laborer)]
    [InlineData(0.75, Workers.Laborer)]
    [InlineData(0.75, Workers.Gatherer)]
    public void AWorkerKilledWhileOverlapped_TheRestCarryOn(double overlap, Workers kind)
    {
        Simulation sim = NewSim(DataAt(overlap), 20);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle[] ws = Setup(sim, Node.Mine, kind, 12);
        var watch = new GatherOverlapQaWatch(sim);
        int kills = 0;
        for (int t = 0; t < 6000; t++)
        {
            watch.Tick();
            if (kills >= 4 || t % 500 != 499) continue;
            // Kill the first live worker that overlaps another (closer than 0.8 of the radii sum).
            for (int a = 0; a < ws.Length; a++)
            {
                if (!u.IsAlive(ws[a])) continue;
                bool hit = false;
                for (int b = 0; b < ws.Length && !hit; b++)
                {
                    if (a == b || !u.IsAlive(ws[b])) continue;
                    int i = ws[a].Index, j = ws[b].Index;
                    if (Vector2.Distance(u.Position[i], u.Position[j]) < 0.8f * (u.Radius[i] + u.Radius[j])) hit = true;
                }
                if (!hit) continue;
                CombatSystem.HitUnit(w, new PendingHit(default, 1, ws[a], false, u.Hp[ws[a].Index] + 1));
                kills++;
                break;
            }
        }
        string name = $"kill while overlapped {kind} at {overlap}";
        _out.WriteLine($"{name}: {kills} killed; banked {w.Gold[0]}; {watch.Line}");
        Assert.True(kills > 0, "no overlapped worker to kill");
        AssertHard(watch, name);
        Assert.True(watch.LongestNoTrip <= TripWindow, $"{name}: {watch.LongestNoTrip} ticks without a trip ({watch.NoTripAt})");
    }

    // ------------------------------------------------------------------ the hall destroyed mid-delivery

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    public void HallDestroyedMidDelivery_WorkersCarryToTheOtherHall(double overlap)
    {
        Simulation sim = NewSim(DataAt(overlap), 24);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle[] ws = Setup(sim, Node.Mine, Workers.Mixed, 16);
        EntityHandle far = Building(sim, 30, 28);
        var watch = new GatherOverlapQaWatch(sim);
        watch.Run(1200);
        int carrying = 0;
        foreach (EntityHandle h in ws) if (u.Cargo[h.Index] > 0 && u.State[h.Index] == UnitState.Moving) carrying++;
        BuildingStore b = w.Buildings;
        int hall = -1;
        for (int k = 0; k < b.Capacity; k++) if (b.Alive[k] && b.Cell[k] == KeepY * w.NavGrid.Width + KeepX) hall = k;
        Assert.True(hall >= 0);
        CombatSystem.HitBuilding(w, new PendingHit(default, 1, new EntityHandle(hall, b.Generation[hall]), true, b.Hp[hall] + 1));
        int goldAt = w.Gold[0];
        for (int t = 0; t < 4800; t++)
        {
            watch.Tick();
        }
        string name = $"hall destroyed at {overlap}";
        _out.WriteLine($"{name}: {carrying} walking with a load when it fell; banked after {w.Gold[0] - goldAt}; {watch.Line}");
        Assert.True(b.IsAlive(far));
        AssertHard(watch, name);
        Assert.True(w.Gold[0] - goldAt > 0, "nobody delivered to the other hall");
    }

    // ------------------------------------------------------------------ enemy workers sharing a mine

    [Theory]
    [InlineData(Workers.Laborer)]
    [InlineData(Workers.Gatherer)]
    public void EnemyWorkersSharingAMine_KeepTheFullSumBetweenThem(Workers kind)
    {
        var lines = new List<string>();
        float offEnemy = 0f, onEnemy = 0f, offOther = 0f;
        foreach (double overlap in new[] { 0.0, 0.5, 0.75 })
        {
            float worstEnemy = float.PositiveInfinity, worstOther = float.PositiveInfinity;
            for (ulong seed = 1; seed <= 4; seed++)
            {
                var sim = new Simulation(new SimConfig(seed, 2, 32, 160) { Data = DataAt(overlap), Combat = false }, Flat(48, 40));
                Setup(sim, Node.Mine, kind, 12, player: 0);
                Building(sim, 26, 16, player: 1);
                UnitStore u = sim.World.Units;
                var before = (bool[])u.Alive.Clone();
                for (int k = 0; k < 12; k++) sim.Enqueue(Command.SpawnUnit(1, TypeOf(kind, k), At(sim, 38 + k % 4, 12 + k / 4 * 2 + (int)seed)));
                Run(sim, 2);
                for (int i = 0; i < u.Capacity; i++)
                    if (u.Alive[i] && !before[i]) sim.Enqueue(Command.Gather(1, new EntityHandle(i, u.Generation[i]), At(sim, MineX, MineY)));
                var watch = new GatherOverlapQaWatch(sim);
                watch.Run(4000);
                AssertHard(watch, $"enemies {kind} at {overlap} seed {seed}");
                worstEnemy = MathF.Min(worstEnemy, watch.ClosestEnemyShare);
                worstOther = MathF.Min(worstOther, watch.ClosestOtherShare);
                lines.Add($"{overlap} seed {seed}: p0 {sim.World.Gold[0]}, p1 {sim.World.Gold[1]}; {watch.Line}");
            }
            if (overlap == 0) { offEnemy = worstEnemy; offOther = worstOther; }
            else onEnemy = MathF.Min(onEnemy == 0 ? float.PositiveInfinity : onEnemy, worstEnemy);
            lines.Add($"== {overlap}: closest enemy pair {worstEnemy:F3} of the sum, closest non-gather pair {worstOther:F3}");
        }
        foreach (string l in lines) _out.WriteLine(l);
        // Enemies never get the scaled distance: they may come no closer with the knob on than the worst seen off,
        // less a margin for a different crowd (0.1 of the sum).
        Assert.True(onEnemy >= offEnemy - 0.1f, $"enemy workers came to {onEnemy:F3} of their radii sum with the knob on, {offEnemy:F3} off");
    }

    // ------------------------------------------------------------------ an army ordered through the mining line

    /// <summary>16 Heavy Infantry ordered across a mining lane (16 mixed workers) at 0, 0.5, 0.75: the knob must not slow them or leave them farther from the goal.</summary>
    [Fact]
    public void AnArmyOrderedThroughTheMiningLine_GetsThroughAsWellAsOff()
    {
        var done = new Dictionary<double, (int Ticks, float Mean, float Closest)>();
        foreach (double overlap in new[] { 0.0, 0.5, 0.75 })
        {
            Simulation sim = NewSim(DataAt(overlap), 48);
            World w = sim.World;
            UnitStore u = w.Units;
            Setup(sim, Node.Mine, Workers.Mixed, 16);
            Run(sim, 600);
            var army = new List<EntityHandle>();
            for (int k = 0; k < 16; k++) army.Add(Unit(sim, At(sim, 14 + k % 4, 3 + k / 4), type: Infantry));
            Vector2 goal = At(sim, 16, 34);
            foreach (EntityHandle h in army) sim.Enqueue(Command.Move(0, h, goal));
            var watch = new GatherOverlapQaWatch(sim);
            int arrivedAt = -1;
            for (int t = 0; t < 2400 && arrivedAt < 0; t++)
            {
                watch.Tick();
                bool all = true;
                foreach (EntityHandle h in army) if (u.State[h.Index] == UnitState.Moving) all = false;
                if (all && t > 10) arrivedAt = t; // (a command applies the tick after the one it was queued in)
            }
            float mean = 0f;
            foreach (EntityHandle h in army) mean += Vector2.Distance(u.Position[h.Index], goal) / army.Count;
            string name = $"army through the line at {overlap}";
            _out.WriteLine($"{name}: all stopped after {arrivedAt} ticks, mean {mean:F2} m from the goal; {watch.Line}");
            AssertHard(watch, name);
            Assert.True(arrivedAt >= 0, $"{name}: the army never finished moving");
            done[overlap] = (arrivedAt, mean, watch.ClosestOtherShare);
        }
        foreach (double v in new[] { 0.5, 0.75 })
        {
            Assert.True(done[v].Ticks <= done[0.0].Ticks * 5 / 4, $"at {v} the army took {done[v].Ticks} ticks, {done[0.0].Ticks} off");
            Assert.True(done[v].Mean <= done[0.0].Mean + 0.5f, $"at {v} the army ended {done[v].Mean:F2} m from the goal, {done[0.0].Mean:F2} off");
        }
    }

    // ------------------------------------------------------------------ slot order: 20 shuffled spawn orders

    [Fact]
    public void ShuffledSpawnOrder_20Seeds_BankSpread_KnobOnNoWorseThanOff()
    {
        var spread = new Dictionary<double, (int Min, int Max)>();
        foreach (double overlap in new[] { 0.0, 0.5 })
        {
            int min = int.MaxValue, max = int.MinValue;
            var banks = new List<int>();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                Simulation sim = NewSim(DataAt(overlap), 16);
                World w = sim.World;
                Spawn(w, Mine, MineX, MineY, MineGold);
                Building(sim, KeepX, KeepY);
                var points = new List<Vector2>();
                for (int y = 10; y <= 25 && points.Count < 12; y++)
                    for (int x = 2; x <= 8 && points.Count < 12; x++)
                        points.Add(At(sim, x, y));
                int[] order = Enumerable.Range(0, 12).ToArray();
                var rng = new SimRng(seed, 31);
                for (int k = order.Length - 1; k > 0; k--)
                {
                    int r = rng.NextInt(0, k + 1);
                    (order[k], order[r]) = (order[r], order[k]);
                }
                foreach (int k in order) sim.Enqueue(Command.SpawnUnit(0, TypeOf(Workers.Mixed, k), points[k]));
                Run(sim, 2);
                UnitStore u = w.Units;
                for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && w.Data.Units[u.TypeId[i]].IsWorker) sim.Enqueue(Command.Gather(0, new EntityHandle(i, u.Generation[i]), At(sim, MineX, MineY)));
                var watch = new GatherOverlapQaWatch(sim);
                watch.Run(6000);
                AssertHard(watch, $"shuffle {seed} at {overlap}");
                banks.Add(w.Gold[0]);
                min = Math.Min(min, w.Gold[0]);
                max = Math.Max(max, w.Gold[0]);
            }
            spread[overlap] = (min, max);
            _out.WriteLine($"{overlap}: 12 mixed workers, 5 min, 20 spawn orders: banked {string.Join(", ", banks)} (min {min}, max {max})");
        }
        // Not a bit-equality claim (the economy phase walks in slot order even off): the knob must not make the outcome
        // swing more with spawn order than off does (plus one trip of each kind).
        int offSpread = spread[0.0].Max - spread[0.0].Min, onSpread = spread[0.5].Max - spread[0.5].Min;
        Assert.True(onSpread <= offSpread + 25, $"spawn-order spread {onSpread} at 0.5 against {offSpread} at 0");
    }

    // ------------------------------------------------------------------ criterion 7 on every seed the developer's rows leave out

    /// <summary>
    /// The developer's economy fuzz (GatherOverlapFuzzTests.Run) on seeds 2-16 at <paramref name="overlap"/> and at 0: a worker
    /// that goes 60 s without a trip with the knob on while the same seed stays inside the window off is a wedge the knob
    /// forms (criterion 7). Seeds whose Keep has no node within the fuzz's 30 m walk are skipped (printed).
    /// </summary>
    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    public void EconomyFuzz_Seeds2To16_NoWedgeOnlyTheKnobForms(double overlap)
    {
        const int window = 60 * SimConstants.TicksPerSecond;
        var formed = new List<string>();
        for (ulong seed = 2; seed <= 16; seed++)
        {
            GatherOverlapFuzzTests.Result off, on;
            try
            {
                off = GatherOverlapFuzzTests.Run(0, seed, tripWindow: false);
                on = GatherOverlapFuzzTests.Run(overlap, seed, tripWindow: false);
            }
            catch (Exception e) when (e.Message.Contains("no node within") || e.Message.Contains("no Keep"))
            {
                // The fuzz's own scene can't be built on this seed (no node within its walk, or a Keep refused).
                _out.WriteLine($"seed {seed}: skipped ({e.Message.Split('\n')[0]})");
                continue;
            }
            _out.WriteLine($"seed {seed}: longest without a trip {off.LongestGap / 20.0:F1} s at 0, {on.LongestGap / 20.0:F1} s at {overlap}; income {off.Income0}/{off.Income1} -> {on.Income0}/{on.Income1}");
            if (on.LongestGap > window && off.LongestGap <= window) formed.Add($"seed {seed}: {on.LongestGap / 20.0:F1} s at {overlap} ({off.LongestGap / 20.0:F1} s at 0)");
        }
        Assert.True(formed.Count == 0, "a worker past 60 s without a trip only with the knob on: " + string.Join("; ", formed));
    }

    /// <summary>
    /// BUG-1430: the two fuzz scenes where a worker goes past 60 s without a trip only with the knob on (seed 7 at the cap,
    /// 0.75: 81.7 s against 52.7 s off; seed 3 at 0.25: 74.6 s against 58.5 s off). Criterion 7 asserted on them.
    /// </summary>
    [Theory]
    [InlineData(0.75, 7UL)]
    [InlineData(0.25, 3UL)]
    public void Bug1430_EconomyFuzz_KnobOnlyWedge_EveryWorkerKeepsDelivering(double overlap, ulong seed)
    {
        GatherOverlapFuzzTests.Result r = GatherOverlapFuzzTests.Run(overlap, seed, tripWindow: true);
        _out.WriteLine(r.Line);
    }

    // ------------------------------------------------------------------ the floor's discontinuity at a tiny value

    [Theory]
    [InlineData(Workers.Laborer)]
    [InlineData(Workers.Gatherer)]
    public void ATinyValue_Report_AgainstOff(Workers kind)
    {
        foreach (double overlap in new[] { 0.0, 0.001, 0.05, 0.25 })
        {
            Figures f = Row(overlap, Node.Mine, kind, 24, 6000);
            _out.WriteLine($"{kind} x24 mine at {overlap}: {f.Cells}");
        }
    }
}

/// <summary>
/// QA M6-S9: why Pale Hills seed 1 (Normal v Normal) no longer ends inside 72,000 ticks at 0.5 (the developer's table (b)):
/// every 6,000 ticks, per seat, the bank, workers (on a loop, and on a loop without a delivery for 60 s while their node
/// lives), other units and buildings; at 0 and at 0.5. A probe, no bound.
/// </summary>
[Collection(SerialCollection.Name)]
[Trait("Category", "Stress")]
public class GatherOverlapPaleHillsProbeQaStressTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void PaleHills_Seed1_Probe(double overlap)
    {
        GameData data = GatherOverlapScenes.DataAt(overlap);
        var seats = new[] { new PlayerSeat(0, SeatController.Ai, AiDifficulty.Normal, 0), new PlayerSeat(1, SeatController.Ai, AiDifficulty.Normal, 0) };
        int size = data.Maps[data.FindMap("pale_hills")].Size;
        Simulation sim = AiScenes.Generated(seats, 1, false, out _, data: data, configure: c => c with { MapId = "pale_hills", Map = c.Map with { Width = size, Height = size } });
        World w = sim.World;
        UnitStore u = w.Units;
        var last = new int[u.Capacity];
        var cargo = new int[u.Capacity];
        var node = new EntityHandle[u.Capacity];
        int worstGap = 0;
        string worstAt = "";
        CriterionMatch.Result r = CriterionMatch.Play(sim, 72_000, checks: false, onTick: s =>
        {
            int now = s.TickNumber;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.GatherNode[i] != node[i] || !w.Resources.IsAlive(u.GatherNode[i])) last[i] = now;
                if (cargo[i] > 0 && u.Cargo[i] == 0) last[i] = now;
                cargo[i] = u.Alive[i] ? u.Cargo[i] : 0;
                node[i] = u.Alive[i] ? u.GatherNode[i] : default;
                if (now - last[i] > worstGap)
                {
                    worstGap = now - last[i];
                    worstAt = $"unit {i} p{u.Owner[i]} {u.State[i]} at {u.Position[i]} cargo {u.Cargo[i]} stuck {u.StuckTicks[i]} tick {now}";
                }
            }
            if (now % 6000 != 0) return;
            string line = $"{overlap} tick {now}:";
            for (int p = 0; p < 2; p++)
            {
                int workers = 0, loop = 0, stale = 0, others = 0, buildings = 0;
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i] || u.Owner[i] != p) continue;
                    if (w.Data.Units[u.TypeId[i]].IsWorker)
                    {
                        workers++;
                        if (u.GatherNode[i].Generation != 0) loop++;
                        if (u.GatherNode[i].Generation != 0 && now - last[i] > 1200) stale++;
                    }
                    else others++;
                }
                for (int k = 0; k < w.Buildings.Capacity; k++) if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == p) buildings++;
                line += $" p{p}: gold {w.Gold[p]} wood {w.Wood[p]} gathered {w.GatheredGold[p] + w.GatheredWood[p]}, workers {workers} (loop {loop}, 60 s no trip {stale}), army {others}, buildings {buildings};";
            }
            output.WriteLine(line);
        });
        output.WriteLine($"{overlap}: ended {r.Over} at {r.EndTick}, winner {r.Winner}; longest a worker on its loop went without a trip: {worstGap} ticks ({worstAt})");
    }
}
