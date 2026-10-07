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

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M3-4 (2026-10-07-0925): hostile command floods on two players over 3,000 ticks and 8 seeds, on a map with two
/// level-1 plateaus (a production hall on each), a mine and a grove: Train (own, enemy's, locked, unknown ids, spam of the
/// same one), CancelTrain (indices -1 to 6, spam), SetRally (own / enemy cells, random cells, targets on nodes, plateaus,
/// off the map), ClearRally, Build, Cancel, and dev SpawnUnit floods past the cap (locked types only, so they are never
/// mistaken for trained units), plus finished buildings destroyed and trained units removed between ticks, a unit store
/// small enough to fill. After every tick: twins hash-identical; gold and wood conserved to the unit (totals + cargo +
/// nodes, so gathering is neutral); population equals its recount and the cap equals its recount; nothing non-finite,
/// nothing off the map; units trained the same tick never share a cell.
/// </summary>
public class ProductionHostileFuzzStressTests
{
    private const int Ticks = 3000;
    private const int Start = 20_000;
    private readonly ITestOutputHelper _out;

    public ProductionHostileFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static Heightmap TwoPlateaus()
    {
        const int w = 72, h = 56;
        var rows = new string[h];
        for (int y = 0; y < h; y++)
        {
            var row = new char[w];
            for (int x = 0; x < w; x++)
            {
                bool a = x >= 8 && x <= 19 && y >= 30 && y <= 41;
                bool b = x >= 50 && x <= 61 && y >= 8 && y <= 19;
                row[x] = a || b ? '1' : '0';
            }
            if (y == 35) row[7] = 'r';
            if (y == 13) row[49] = 'r';
            rows[y] = new string(row);
        }
        return FromRows(rows);
    }

    private static Simulation Setup()
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 2, UnitCapacity: 120, CommandCapacity: 1024), TwoPlateaus());
        World w = sim.World;
        Spawn(w, Mine, 34, 26, 4000);
        for (int x = 28; x < 42; x += 2) Spawn(w, Tree, x, 50, TreeWood);
        Building(sim, 4, 4);
        Building(sim, 12, 4, type: Corral);
        Building(sim, 12, 33, type: ProductionMaps.Barracks); // on plateau A
        Building(sim, 60, 44, player: 1, type: HolyCamp);
        Building(sim, 52, 44, player: 1, type: TestSim.Data.FindBuilding("whirlwind_horse_lines"));
        Building(sim, 54, 12, player: 1, type: RaiderCamp); // on plateau B
        // Houses for room under the cap (Town Hall 20 + 3 x 16 half-pop).
        for (int n = 0; n < 3; n++)
        {
            Building(sim, 22 + 3 * n, 4, type: House);
            Building(sim, 40 + 3 * n, 52, player: 1, type: WhirlwindHouse);
        }
        for (int n = 0; n < 4; n++)
        {
            sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 4 + n, 10)));
            sim.Enqueue(Command.SpawnUnit(1, CampFollower, At(sim, 60 + n, 50)));
        }
        Run(sim, 2);
        SetTotals(sim, 0, Start, Start);
        SetTotals(sim, 1, Start, Start);
        return sim;
    }

    private static bool Locked(World w, int type) => w.Data.Units[type].Requires.Length > 0;

    private static void RandomCommands(ref SimRng rng, Simulation sim, List<int> slots, int tick)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        for (int p = 0; p < 2; p++)
        {
            if (rng.NextInt(0, 2) != 0) continue;
            int count = rng.NextInt(1, 4);
            for (int c = 0; c < count; c++)
            {
                slots.Clear();
                for (int k = 0; k < w.Buildings.Capacity; k++)
                    if (w.Buildings.Alive[k] && (w.Buildings.Owner[k] == p || rng.NextInt(0, 6) == 0)) slots.Add(k);
                int slot = slots.Count > 0 ? slots[rng.NextInt(0, slots.Count)] : -1;
                Vector2 at = slot >= 0 ? In(sim, slot) + new Vector2(rng.NextFloat() * 5f, rng.NextFloat() * 5f) : At(sim, rng.NextInt(0, g.Width), rng.NextInt(0, g.Height));
                Vector2 anywhere = rng.NextInt(0, 12) == 0
                    ? new Vector2(rng.NextFloat() * 400f - 100f, rng.NextFloat() * 400f - 100f) // often off the map
                    : At(sim, rng.NextInt(0, g.Width), rng.NextInt(0, g.Height));
                switch (rng.NextInt(0, 14))
                {
                    case 0: case 1: case 2: case 3:
                    {
                        var trains = slot >= 0 ? w.Data.UnitsTrainedAt(w.Buildings.TypeId[slot]) : default;
                        int unit = slot >= 0 && trains.Length > 0 && rng.NextInt(0, 5) != 0
                            ? trains[rng.NextInt(0, trains.Length)]
                            : rng.NextInt(-2, w.Data.Units.Length + 2);
                        int spam = rng.NextInt(0, 4) == 0 ? 7 : 1;
                        for (int s = 0; s < spam; s++) sim.Enqueue(Command.Train(p, at, unit));
                        break;
                    }
                    case 4: case 5:
                    {
                        int index = rng.NextInt(-1, 7);
                        int spam = rng.NextInt(0, 4) == 0 ? 6 : 1;
                        for (int s = 0; s < spam; s++) sim.Enqueue(Command.CancelTrain(p, at, index));
                        break;
                    }
                    case 6: case 7:
                    {
                        Vector2 target = rng.NextInt(0, 5) switch
                        {
                            0 => At(sim, 35, 27), // on the mine
                            1 => At(sim, 30, 50), // a tree
                            2 => At(sim, 14 + rng.NextInt(0, 4), 37), // plateau A
                            3 => At(sim, 56, 16), // plateau B
                            _ => anywhere,
                        };
                        int cell = slot >= 0 && rng.NextInt(0, 5) != 0 ? w.Buildings.Cell[slot] + rng.NextInt(0, 3) : rng.NextInt(-5, g.Width * g.Height + 5);
                        sim.Enqueue(Command.SetRally(p, cell, target));
                        break;
                    }
                    case 8:
                        sim.Enqueue(Command.ClearRally(p, at));
                        break;
                    case 9:
                    {
                        int worker = RandomUnit(ref rng, w, p, workersOnly: true);
                        if (worker < 0) break;
                        int type = rng.NextInt(0, 3) switch
                        {
                            0 => p == 0 ? House : WhirlwindHouse,
                            1 => p == 0 ? ProductionMaps.Barracks : RaiderCamp,
                            _ => rng.NextInt(0, w.Data.Buildings.Length),
                        };
                        sim.Enqueue(Command.Build(p, new EntityHandle(worker, w.Units.Generation[worker]), type, anywhere));
                        break;
                    }
                    case 10:
                        sim.Enqueue(Command.Cancel(p, slot >= 0 ? at : anywhere));
                        break;
                    case 11:
                    {
                        // Only in a window, so the cap and the store also stay open long enough for training.
                        if (tick < 1000 || tick >= 1600) break;
                        // Dev spawns past the cap, locked types only; a third of them on blocked or off-map points.
                        int type = rng.NextInt(0, 2) == 0 ? Sapper : Zealot;
                        int n = rng.NextInt(1, 6);
                        for (int s = 0; s < n; s++) sim.Enqueue(Command.SpawnUnit(p, type, rng.NextInt(0, 3) == 0 ? anywhere : At(sim, rng.NextInt(1, g.Width - 1), rng.NextInt(1, g.Height - 1))));
                        break;
                    }
                    default:
                    {
                        int u = RandomUnit(ref rng, w, p, workersOnly: false);
                        if (u >= 0) sim.Enqueue(Command.Move(p, new EntityHandle(u, w.Units.Generation[u]), anywhere));
                        break;
                    }
                }
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
        public bool Alive, Site;
        public int Generation, Owner, Type, Work;
    }

    [Theory]
    [InlineData(101UL)]
    [InlineData(102UL)]
    [InlineData(103UL)]
    [InlineData(104UL)]
    [InlineData(105UL)]
    [InlineData(106UL)]
    [InlineData(107UL)]
    [InlineData(108UL)]
    public void HostileFlood_TwinsIdentical_MoneyAndPopConserved_EveryTick(ulong seed)
    {
        Simulation a = Setup(), b = Setup();
        World w = a.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        var initial = new HashSet<int>(LiveUnits(w));
        (long gold0, long wood0) = Conserved(w);
        var rng = new SimRng(seed, 4242);
        var slots = new List<int>();
        var snap = new SlotSnap[w.Buildings.Capacity];
        var buildingGold = new long[2];
        var buildingWood = new long[2];
        var deadGold = new long[2];
        var deadWood = new long[2];
        var aliveBefore = new bool[u.Capacity];
        var genBefore = new int[u.Capacity];
        var newCells = new HashSet<int>();
        int trained = 0, devSpawned = 0, destroyed = 0, removed = 0, storeFullTicks = 0, maxOver = 0, rallyGathers = 0;
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 600 == 599)
            {
                int k = rng.NextInt(0, w.Buildings.Capacity);
                for (int n = 0; n < w.Buildings.Capacity && !(w.Buildings.Alive[k] && !w.Buildings.UnderConstruction[k]); n++) k = (k + 1) % w.Buildings.Capacity;
                if (w.Buildings.Alive[k] && !w.Buildings.UnderConstruction[k])
                {
                    a.World.Buildings.Damage(a.World.Buildings.HandleOf(k), 1_000_000);
                    b.World.Buildings.Damage(b.World.Buildings.HandleOf(k), 1_000_000);
                    destroyed++;
                }
            }
            if (t >= 1600 && t % 3 == 0)
            {
                // After the dev-spawn window the locked units go again (deaths), a few a tick.
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i] || !Locked(w, u.TypeId[i])) continue;
                    a.World.Units.Free(new EntityHandle(i, a.World.Units.Generation[i]));
                    b.World.Units.Free(new EntityHandle(i, b.World.Units.Generation[i]));
                    removed++;
                    break;
                }
            }
            if (t % 9 == 8)
            {
                int i = RandomUnit(ref rng, w, rng.NextInt(0, 2), workersOnly: false);
                if (i >= 0 && !initial.Contains(i) && u.Cargo[i] == 0)
                {
                    if (!Locked(w, u.TypeId[i]))
                    {
                        UnitDef d = w.Data.Units[u.TypeId[i]];
                        deadGold[u.Owner[i]] += d.CostGold;
                        deadWood[u.Owner[i]] += d.CostWood;
                    }
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
            Array.Copy(u.Alive, aliveBefore, u.Capacity);
            Array.Copy(u.Generation, genBefore, u.Capacity);
            SimRng rb = rng;
            RandomCommands(ref rng, a, slots, t);
            RandomCommands(ref rb, b, slots, t);
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            if (u.FreeCount == 0) storeFullTicks++;

            // New units this tick: trained ones (not locked types) never share a cell with each other.
            newCells.Clear();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || (aliveBefore[i] && genBefore[i] == u.Generation[i])) continue;
                if (Locked(w, u.TypeId[i])) { devSpawned++; continue; }
                trained++;
                if (EconomySystem_OnLoop(u, i)) rallyGathers++;
                Assert.True(g.WorldToCell(u.Position[i], out int x, out int y), $"seed {seed} tick {t}: trained unit {i} off the map");
                Assert.True(newCells.Add(y * g.Width + x), $"seed {seed} tick {t}: two trained units in ({x}, {y})");
            }

            // Site money, as in ProductionFuzzTests: a new site paid its cost, a site gone got back the unbuilt fraction.
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

            long prodGold = 0, prodWood = 0;
            for (int p = 0; p < 2; p++)
            {
                Assert.True(w.Gold[p] >= 0 && w.Wood[p] >= 0, $"seed {seed} tick {t}: negative totals");
                prodGold += deadGold[p];
                prodWood += deadWood[p];
                int provided = 0;
                for (int k = 0; k < w.Buildings.Capacity; k++)
                {
                    if (!w.Buildings.Alive[k] || w.Buildings.Owner[k] != p) continue;
                    if (!w.Buildings.UnderConstruction[k]) provided += w.Data.Buildings[w.Buildings.TypeId[k]].HalfPopProvided;
                    else Assert.Equal(0, w.Buildings.QueueCount[k]);
                    for (int q = 0; q < w.Buildings.QueueCount[k]; q++)
                    {
                        prodGold += w.Data.Units[w.Buildings.QueueTypeAt(k, q)].CostGold;
                        prodWood += w.Data.Units[w.Buildings.QueueTypeAt(k, q)].CostWood;
                    }
                }
                Assert.Equal(Math.Min(provided, w.Data.Rules.HalfPopCap), w.HalfPopCap[p]);
                Assert.True(RecountHalfPop(w, p) == w.HalfPop[p], $"seed {seed} tick {t} player {p}: HalfPop {w.HalfPop[p]} recount {RecountHalfPop(w, p)}");
                Assert.Equal(RecountHalfPop(b.World, p), b.World.HalfPop[p]);
                maxOver = Math.Max(maxOver, w.HalfPop[p] - w.HalfPopCap[p]);
            }
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 pos = u.Position[i];
                Assert.True(float.IsFinite(pos.X) && float.IsFinite(pos.Y), $"seed {seed} tick {t}: unit {i} at {pos}");
                if (initial.Contains(i) || Locked(w, u.TypeId[i])) continue;
                prodGold += w.Data.Units[u.TypeId[i]].CostGold;
                prodWood += w.Data.Units[u.TypeId[i]].CostWood;
            }
            (long gold, long wood) = Conserved(w);
            long spentGold = gold0 - gold - (buildingGold[0] + buildingGold[1]);
            long spentWood = wood0 - wood - (buildingWood[0] + buildingWood[1]);
            Assert.True(spentGold == prodGold, $"seed {seed} tick {t}: gold spent on production {spentGold}, items + trained units cost {prodGold}");
            Assert.True(spentWood == prodWood, $"seed {seed} tick {t}: wood spent on production {spentWood}, items + trained units cost {prodWood}");
        }
        _out.WriteLine($"seed {seed}: {trained} trained ({rallyGathers} straight onto a gather loop), {devSpawned} dev-spawned, {removed} removed, {destroyed} destroyed, store full on {storeFullTicks} ticks, max pop over cap {maxOver}; final hash {a.StateHash():X16}");
        Assert.True(trained >= 10, $"only {trained} trained"); // hostile: cancels and spam keep this low (19-24 seen)
        Assert.True(devSpawned > 0);
    }

    private static bool EconomySystem_OnLoop(UnitStore u, int i) => Rts.Sim.Economy.EconomySystem.OnLoop(u, i);

    [Fact]
    public void DifferentSeeds_GiveDifferentHashes()
    {
        var hashes = new HashSet<ulong>();
        for (ulong seed = 1; seed <= 3; seed++)
        {
            Simulation sim = Setup();
            var rng = new SimRng(seed, 4242);
            var slots = new List<int>();
            for (int t = 0; t < 600; t++)
            {
                RandomCommands(ref rng, sim, slots, t);
                sim.Tick();
            }
            hashes.Add(sim.StateHash());
        }
        Assert.Equal(3, hashes.Count);
    }
}
