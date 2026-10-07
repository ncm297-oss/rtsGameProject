using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M3-4 criteria 8-9: random Train / CancelTrain / SetRally / ClearRally / Build / Cancel commands on two players for
/// 3,000 ticks, with buildings destroyed and trained units removed now and then: twin sims stay hash-identical every
/// tick, every player's spending balances to the unit, and the population equals its recount every tick.
/// </summary>
[Collection(SerialCollection.Name)]
public class ProductionFuzzTests
{
    private const int Ticks = 3000;
    private const int Start = 20_000;
    private readonly ITestOutputHelper _out;

    public ProductionFuzzTests(ITestOutputHelper output) => _out = output;

    /// <summary>Two players, each with a Town Hall, two production halls and four workers on a flat 64 x 48 map; with <paramref name="nodes"/> a mine and a grove to rally onto.</summary>
    private static Simulation Setup(bool nodes)
    {
        Simulation sim = BuildMaps.NewSim(Flat(64, 48), units: 200, players: 2);
        World w = sim.World;
        if (nodes)
        {
            Spawn(w, Mine, 30, 20, 2500);
            for (int x = 26; x < 36; x += 2) Spawn(w, Tree, x, 40, TreeWood);
        }
        Building(sim, 4, 4);
        Building(sim, 12, 4, type: ProductionMaps.Barracks);
        Building(sim, 18, 4, type: Corral);
        Building(sim, 40, 30, player: 1, type: HolyCamp);
        Building(sim, 48, 30, player: 1, type: RaiderCamp);
        Building(sim, 54, 30, player: 1, type: TestSim.Data.FindBuilding("whirlwind_horse_lines"));
        for (int n = 0; n < 4; n++)
        {
            sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 4 + n, 10)));
            sim.Enqueue(Command.SpawnUnit(1, CampFollower, At(sim, 40 + n, 36)));
        }
        Run(sim, 2);
        SetTotals(sim, 0, Start, Start);
        SetTotals(sim, 1, Start, Start);
        return sim;
    }

    /// <summary>One tick's random commands for both players (the same draws for both twins).</summary>
    private static void RandomCommands(ref SimRng rng, Simulation sim, List<int> buildingSlots)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        for (int p = 0; p < 2; p++)
        {
            if (rng.NextInt(0, 3) != 0) continue;
            buildingSlots.Clear();
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k] && (w.Buildings.Owner[k] == p || rng.NextInt(0, 10) == 0)) buildingSlots.Add(k);
            int slot = buildingSlots.Count > 0 ? buildingSlots[rng.NextInt(0, buildingSlots.Count)] : -1;
            Vector2 at = slot >= 0 ? In(sim, slot) + new Vector2(rng.NextFloat() * 4f, rng.NextFloat() * 4f) : At(sim, rng.NextInt(1, 63), rng.NextInt(1, 47));
            Vector2 anywhere = At(sim, rng.NextInt(1, 63), rng.NextInt(1, 47));
            switch (rng.NextInt(0, 12))
            {
                case 0: case 1: case 2: case 3: case 4:
                    // Mostly a type the building trains (a quarter of those locked or another player's), else any id.
                    var trains = slot >= 0 ? w.Data.UnitsTrainedAt(w.Buildings.TypeId[slot]) : default;
                    int unit = slot >= 0 && trains.Length > 0 && rng.NextInt(0, 4) != 0
                        ? trains[rng.NextInt(0, trains.Length)]
                        : rng.NextInt(-1, w.Data.Units.Length + 1);
                    sim.Enqueue(Command.Train(p, at, unit));
                    break;
                case 5:
                    sim.Enqueue(Command.CancelTrain(p, at, rng.NextInt(-1, 6)));
                    break;
                case 6: case 7:
                    Vector2 target = rng.NextInt(0, 3) == 0 ? At(sim, 31, 21) : anywhere;
                    sim.Enqueue(Command.SetRally(p, slot >= 0 ? w.Buildings.Cell[slot] : rng.NextInt(-1, g.Width * g.Height), target));
                    break;
                case 8:
                    sim.Enqueue(Command.ClearRally(p, at));
                    break;
                case 9:
                    int worker = RandomUnit(ref rng, w, p, workersOnly: true);
                    if (worker < 0) break;
                    int type = rng.NextInt(0, 2) == 0 ? (p == 0 ? House : WhirlwindHouse) : (p == 0 ? ProductionMaps.Barracks : RaiderCamp);
                    sim.Enqueue(Command.Build(p, new EntityHandle(worker, w.Units.Generation[worker]), type, anywhere));
                    break;
                case 10:
                    sim.Enqueue(Command.Cancel(p, slot >= 0 ? at : anywhere));
                    break;
            }
        }
    }

    private static int RandomUnit(ref SimRng rng, World w, int player, bool workersOnly)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == player && (!workersOnly || w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker)) n++;
        if (n == 0) return -1;
        int pick = rng.NextInt(0, n);
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == player && (!workersOnly || w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker) && pick-- == 0) return i;
        return -1;
    }

    private struct SlotSnap
    {
        public bool Alive;
        public int Generation, Owner, Type, Work;
        public bool Site;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void Twins_StayIdentical_SpendingBalances_AndPopulationMatchesItsRecount_EveryTick(ulong seed)
    {
        Simulation a = Setup(nodes: false), b = Setup(nodes: false);
        World w = a.World;
        var initial = new HashSet<int>(LiveUnits(w));
        var rng = new SimRng(seed, 77);
        var slots = new List<int>();
        var snap = new SlotSnap[w.Buildings.Capacity];
        var buildingGold = new long[2];
        var buildingWood = new long[2];
        var deadGold = new long[2];
        var deadWood = new long[2];
        int trained = 0, destroyed = 0, removed = 0;
        for (int t = 0; t < Ticks; t++)
        {
            // Test seams between ticks, on both twins: a building destroyed now and then, a trained unit removed (M4 death).
            if (t % 450 == 449)
            {
                int k = rng.NextInt(0, w.Buildings.Capacity);
                if (w.Buildings.Alive[k])
                {
                    a.World.Buildings.Damage(a.World.Buildings.HandleOf(k), 1_000_000);
                    b.World.Buildings.Damage(b.World.Buildings.HandleOf(k), 1_000_000);
                    destroyed++;
                }
            }
            if (t % 37 == 36)
            {
                int i = RandomUnit(ref rng, w, rng.NextInt(0, 2), workersOnly: false);
                if (i >= 0 && !initial.Contains(i))
                {
                    UnitDef d = w.Data.Units[w.Units.TypeId[i]];
                    deadGold[w.Units.Owner[i]] += d.CostGold;
                    deadWood[w.Units.Owner[i]] += d.CostWood;
                    a.World.Units.Free(new EntityHandle(i, a.World.Units.Generation[i]));
                    b.World.Units.Free(new EntityHandle(i, b.World.Units.Generation[i]));
                    removed++;
                }
            }
            for (int k = 0; k < snap.Length; k++)
                snap[k] = new SlotSnap
                {
                    Alive = w.Buildings.Alive[k], Generation = w.Buildings.Generation[k], Owner = w.Buildings.Owner[k],
                    Type = w.Buildings.TypeId[k], Work = w.Buildings.Work[k], Site = w.Buildings.UnderConstruction[k],
                };
            SimRng rb = rng;
            RandomCommands(ref rng, a, slots);
            RandomCommands(ref rb, b, slots);
            int before = w.Units.Count;
            a.Tick();
            b.Tick();
            trained += Math.Max(0, w.Units.Count - before);
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");

            // Building money: a new site paid its cost; a site gone (cancelled) got back the unbuilt fraction.
            for (int k = 0; k < snap.Length; k++)
            {
                bool same = w.Buildings.Alive[k] && snap[k].Alive && w.Buildings.Generation[k] == snap[k].Generation;
                if (snap[k].Alive && snap[k].Site && !same)
                {
                    BuildingDef d = w.Data.Buildings[snap[k].Type];
                    long needed = w.Buildings.WorkNeeded(snap[k].Type), left = needed - snap[k].Work;
                    buildingGold[snap[k].Owner] -= d.CostGold * left / needed;
                    buildingWood[snap[k].Owner] -= d.CostWood * left / needed;
                }
                if (w.Buildings.Alive[k] && !same)
                {
                    Assert.True(w.Buildings.UnderConstruction[k], "only Build places buildings in the fuzz");
                    BuildingDef d = w.Data.Buildings[w.Buildings.TypeId[k]];
                    buildingGold[w.Buildings.Owner[k]] += d.CostGold;
                    buildingWood[w.Buildings.Owner[k]] += d.CostWood;
                }
            }

            for (int p = 0; p < 2; p++)
            {
                // Spent on production = the cost of every item queued or training plus every unit trained (alive or removed).
                long gold = deadGold[p], wood = deadWood[p];
                for (int k = 0; k < w.Buildings.Capacity; k++)
                {
                    if (!w.Buildings.Alive[k] || w.Buildings.Owner[k] != p) continue;
                    for (int q = 0; q < w.Buildings.QueueCount[k]; q++)
                    {
                        gold += w.Data.Units[w.Buildings.QueueTypeAt(k, q)].CostGold;
                        wood += w.Data.Units[w.Buildings.QueueTypeAt(k, q)].CostWood;
                    }
                }
                foreach (int i in LiveUnits(w))
                {
                    if (initial.Contains(i) || w.Units.Owner[i] != p) continue;
                    gold += w.Data.Units[w.Units.TypeId[i]].CostGold;
                    wood += w.Data.Units[w.Units.TypeId[i]].CostWood;
                }
                Assert.True(Start - w.Gold[p] - buildingGold[p] == gold, $"seed {seed} tick {t} player {p}: gold spent {Start - w.Gold[p] - buildingGold[p]} on production, items and units cost {gold}");
                Assert.True(Start - w.Wood[p] - buildingWood[p] == wood, $"seed {seed} tick {t} player {p}: wood");
                Assert.Equal(RecountHalfPop(w, p), w.HalfPop[p]);
                Assert.Equal(RecountHalfPop(b.World, p), b.World.HalfPop[p]);
                Assert.True(w.HalfPopCap[p] <= w.Data.Rules.HalfPopCap);
            }
            // Initial units never die in this fuzz, so the slots of the set stay theirs.
            foreach (int i in initial) Assert.True(w.Units.Alive[i]);
        }
        _out.WriteLine($"seed {seed}: {trained} units trained, {removed} removed, {destroyed} buildings destroyed, {w.Buildings.Count} buildings, pop {w.HalfPop[0]}/{w.HalfPopCap[0]} and {w.HalfPop[1]}/{w.HalfPopCap[1]}");
        Assert.True(trained >= 20, $"only {trained} trained");
    }

    [Theory]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void WithRalliesOntoResourceNodes_TwinsStayIdentical_AndPopulationMatchesItsRecount(ulong seed)
    {
        Simulation a = Setup(nodes: true), b = Setup(nodes: true);
        var rng = new SimRng(seed, 78);
        var slots = new List<int>();
        int gathering = 0;
        for (int t = 0; t < Ticks; t++)
        {
            SimRng rb = rng;
            RandomCommands(ref rng, a, slots);
            RandomCommands(ref rb, b, slots);
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            for (int p = 0; p < 2; p++) Assert.Equal(RecountHalfPop(a.World, p), a.World.HalfPop[p]);
            for (int i = 0; i < a.World.Units.Capacity; i++)
                if (a.World.Units.Alive[i] && a.World.Units.GatherNode[i] != default) gathering++;
        }
        _out.WriteLine($"seed {seed}: {a.World.Units.Count} units; {gathering} unit-ticks on a gather loop");
        Assert.True(gathering > 0, "no trained worker ever rallied onto a node");
    }
    [Fact]
    public void AReplayWithProductionCommands_ReadsBack_AndReplaysEveryCheckpoint()
    {
        var sim = new Simulation(TestSim.Config(Seed: 21, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 64) with { Map = new MapGenParams { GoldMines = 4 } });
        var rec = new Replays.ReplayRecorder(sim, checkpointInterval: 50);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int[] halls = { Keep, HolyCamp };
        var anchors = new int[2];
        for (int p = 0; p < 2; p++)
        {
            int c = p == 0 ? 0 : g.Width * g.Height - 1;
            // Starting gold (200) can't pay for a hall, but the dev command doesn't charge: every other rule must pass.
            while (!w.CanPlace(p, halls[p], c, out Economy.PlacementError why) && why != Economy.PlacementError.CannotAfford) c += p == 0 ? 37 : -37;
            anchors[p] = c;
            sim.Enqueue(Command.SpawnBuilding(p, halls[p], g.CellCenter(c % g.Width, c / g.Width)));
        }
        Run(sim, 2);
        for (int p = 0; p < 2; p++)
        {
            Vector2 at = g.CellCenter(anchors[p] % g.Width + 1, anchors[p] / g.Width + 1);
            int worker = p == 0 ? Laborer : CampFollower;
            sim.Enqueue(Command.SetRally(p, anchors[p], at + new Vector2(12f, 9f)));
            for (int n = 0; n < 4; n++) sim.Enqueue(Command.Train(p, at, worker));
            sim.Enqueue(Command.CancelTrain(p, at, 3));
        }
        Run(sim, 500);
        sim.Enqueue(Command.ClearRally(0, g.CellCenter(anchors[0] % g.Width, anchors[0] / g.Width)));
        Run(sim, 500);
        Assert.True(w.Units.Count >= 6, $"{w.Units.Count} trained");
        Replays.Replay replay = rec.ToReplay();
        byte[] bytes = Replays.ReplayFormat.Write(replay);
        Assert.Equal(Replays.ReplayError.None, Replays.ReplayFormat.TryRead(bytes, out Replays.Replay? back));
        Assert.Equal(3, back!.FormatVersion);
        foreach (CommandKind k in new[] { CommandKind.Train, CommandKind.CancelTrain, CommandKind.SetRally, CommandKind.ClearRally })
            Assert.Contains(back.Commands, c => c.Kind == k);
        Replays.ReplayResult result = Replays.ReplayPlayer.Run(back, TestSim.Data);
        Assert.True(result.Ok, $"{result.Error} at tick {result.Tick}");
    }
}
