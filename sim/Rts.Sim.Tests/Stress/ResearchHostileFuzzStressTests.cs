using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.QA;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResearchMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M3-5 (2026-10-07-1131): hostile research floods on two players over 3,000 ticks and 8 seeds. Each player has two
/// Town Halls and two Forges (so one tech can be aimed at two buildings of its slot in one tick), a production hall, a
/// mine and a grove are on the map, and workers gather. Commands: Research (valid, the same tech at every own building
/// in one tick, spammed, unknown ids, the other faction's upgrade, at the enemy's buildings), Train mixed into the same
/// queues, CancelTrain (indices -1 to 6, the head most often, spammed), Build (Forges and Halls too), Cancel, Move.
/// Between ticks a building is destroyed now and then (a researching one when there is one) and trained units are
/// removed. After every tick: twins hash-identical; gold and wood conserved to the coin (totals + cargo + nodes against
/// buildings, units, queued items and researched techs); population equals its recount; a tech is never queued twice
/// by a player nor queued once researched, never another faction's; sites hold no queue; the age follows Age II; and
/// after every completion <c>TechBonus</c> equals the raw-JSON oracle for both players, 14 units x 5 stats.
/// </summary>
public class ResearchHostileFuzzStressTests
{
    private const int Ticks = 3000;
    private const int Start = 30_000;
    private readonly ITestOutputHelper _out;

    public ResearchHostileFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static Simulation Setup()
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 2, UnitCapacity: 120, CommandCapacity: 1024), Flat(72, 56));
        World w = sim.World;
        Spawn(w, Mine, 34, 26, 4000);
        for (int x = 28; x < 42; x += 2) Spawn(w, Tree, x, 50, TreeWood);
        Building(sim, 4, 4);
        Building(sim, 4, 12);
        Building(sim, 12, 4, type: Armory);
        Building(sim, 12, 12, type: Armory);
        Building(sim, 20, 4, type: ProductionMaps.Barracks);
        Building(sim, 62, 44, player: 1, type: HolyCamp);
        Building(sim, 62, 36, player: 1, type: HolyCamp);
        Building(sim, 54, 44, player: 1, type: Smithy);
        Building(sim, 54, 36, player: 1, type: Smithy);
        Building(sim, 46, 44, player: 1, type: RaiderCamp);
        for (int n = 0; n < 2; n++)
        {
            Building(sim, 26 + 3 * n, 4, type: House);
            Building(sim, 40 + 3 * n, 52, player: 1, type: WhirlwindHouse);
        }
        for (int n = 0; n < 4; n++)
        {
            sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 4 + n, 20)));
            sim.Enqueue(Command.SpawnUnit(1, CampFollower, At(sim, 60 + n, 30)));
        }
        Run(sim, 2);
        // Half the workers gather, so totals move under the research.
        for (int i = 0, n = 0; i < w.Units.Capacity; i++)
        {
            if (!w.Units.Alive[i] || n++ % 2 != 0) continue;
            sim.Enqueue(Command.Gather(w.Units.Owner[i], new EntityHandle(i, w.Units.Generation[i]), n % 4 == 1 ? At(sim, 35, 27) : At(sim, 30, 50)));
        }
        Run(sim, 2);
        SetTotals(sim, 0, Start, Start);
        SetTotals(sim, 1, Start, Start);
        return sim;
    }

    private static void RandomCommands(ref SimRng rng, Simulation sim, List<int> slots)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        BuildingStore bs = w.Buildings;
        for (int p = 0; p < 2; p++)
        {
            if (rng.NextInt(0, 2) != 0) continue;
            int count = rng.NextInt(1, 4);
            for (int c = 0; c < count; c++)
            {
                slots.Clear();
                for (int k = 0; k < bs.Capacity; k++)
                    if (bs.Alive[k] && (bs.Owner[k] == p || rng.NextInt(0, 8) == 0)) slots.Add(k);
                int slot = slots.Count > 0 ? slots[rng.NextInt(0, slots.Count)] : -1;
                Vector2 at = slot >= 0 ? In(sim, slot) + new Vector2(rng.NextFloat() * 4f, rng.NextFloat() * 4f) : At(sim, rng.NextInt(0, g.Width), rng.NextInt(0, g.Height));
                Vector2 anywhere = At(sim, rng.NextInt(1, g.Width - 1), rng.NextInt(1, g.Height - 1));
                switch (rng.NextInt(0, 16))
                {
                    case 0: case 1: case 2: case 3: case 4:
                    {
                        var techs = slot >= 0 ? w.Data.TechsResearchableAt(bs.TypeId[slot]) : default;
                        int tech = slot >= 0 && techs.Length > 0 && rng.NextInt(0, 6) != 0
                            ? techs[rng.NextInt(0, techs.Length)]
                            : rng.NextInt(-2, w.Data.Techs.Length + 2);
                        if (rng.NextInt(0, 4) == 0)
                        {
                            // The same tech at every building the player owns (and one of the enemy's), in one tick.
                            for (int k = 0; k < bs.Capacity; k++)
                                if (bs.Alive[k] && (bs.Owner[k] == p || rng.NextInt(0, 6) == 0)) sim.Enqueue(Command.Research(p, In(sim, k), tech));
                        }
                        else
                        {
                            int spam = rng.NextInt(0, 4) == 0 ? 4 : 1;
                            for (int s = 0; s < spam; s++) sim.Enqueue(Command.Research(p, at, tech));
                        }
                        break;
                    }
                    case 5: case 6: case 7:
                    {
                        var trains = slot >= 0 ? w.Data.UnitsTrainedAt(bs.TypeId[slot]) : default;
                        int unit = slot >= 0 && trains.Length > 0 && rng.NextInt(0, 5) != 0
                            ? trains[rng.NextInt(0, trains.Length)]
                            : rng.NextInt(-1, w.Data.Units.Length + 1);
                        sim.Enqueue(Command.Train(p, at, unit));
                        break;
                    }
                    case 8: case 9:
                    {
                        // Rare enough that 600-1,200 tick research still finishes; the head half the time.
                        if (rng.NextInt(0, 8) != 0) break;
                        int index = rng.NextInt(0, 2) == 0 ? rng.NextInt(-1, 7) : 0;
                        int spam = rng.NextInt(0, 5) == 0 ? 3 : 1;
                        for (int s = 0; s < spam; s++) sim.Enqueue(Command.CancelTrain(p, at, index));
                        break;
                    }
                    case 10:
                    {
                        int worker = RandomUnit(ref rng, w, p, workersOnly: true);
                        if (worker < 0) break;
                        int[] types = p == 0 ? new[] { House, Armory, Keep, ProductionMaps.Barracks } : new[] { WhirlwindHouse, Smithy, HolyCamp, RaiderCamp };
                        sim.Enqueue(Command.Build(p, new EntityHandle(worker, w.Units.Generation[worker]), types[rng.NextInt(0, types.Length)], anywhere));
                        break;
                    }
                    case 11:
                        sim.Enqueue(Command.Cancel(p, slot >= 0 ? at : anywhere));
                        break;
                    default:
                    {
                        int u = RandomUnit(ref rng, w, p, workersOnly: false);
                        if (u >= 0 && w.Units.Cargo[u] == 0) sim.Enqueue(Command.Move(p, new EntityHandle(u, w.Units.Generation[u]), anywhere));
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
    [InlineData(201UL)]
    [InlineData(202UL)]
    [InlineData(203UL)]
    [InlineData(204UL)]
    [InlineData(205UL)]
    [InlineData(206UL)]
    [InlineData(207UL)]
    [InlineData(208UL)]
    public void HostileResearchFlood_TwinsIdentical_MoneyConservedToTheCoin_TechBonusMatchesTheOracle(ulong seed)
    {
        var oracle = new TechBonusOracle(TestDataDir.Shipped);
        Simulation a = Setup(), b = Setup();
        World w = a.World;
        UnitStore u = w.Units;
        BuildingStore bs = w.Buildings;
        var initial = new HashSet<int>(LiveUnits(w));
        (long gold0, long wood0) = Conserved(w);
        var rng = new SimRng(seed, 5151);
        var slots = new List<int>();
        var snap = new SlotSnap[bs.Capacity];
        var buildingGold = new long[2];
        var buildingWood = new long[2];
        long deadGold = 0, deadWood = 0;
        int destroyed = 0, techItemsLost = 0, removed = 0, completions = 0, maxQueued = 0, oracleChecks = 0;
        int lastResearched = 0;
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 500 == 499)
            {
                // A finished building; every other time a researching one when there is one.
                slots.Clear();
                for (int k = 0; k < bs.Capacity; k++)
                    if (bs.Alive[k] && bs.QueueIsTechAt(k, 0) && destroyed % 2 == 0) slots.Add(k);
                if (slots.Count == 0)
                    for (int k = 0; k < bs.Capacity; k++)
                        if (bs.Alive[k] && !bs.UnderConstruction[k]) slots.Add(k);
                if (slots.Count > 0)
                {
                    int k = slots[rng.NextInt(0, slots.Count)];
                    for (int q = 0; q < bs.QueueCount[k]; q++) techItemsLost += bs.QueueIsTechAt(k, q) ? 1 : 0;
                    a.World.Buildings.Damage(a.World.Buildings.HandleOf(k), 1_000_000);
                    b.World.Buildings.Damage(b.World.Buildings.HandleOf(k), 1_000_000);
                    destroyed++;
                }
            }
            if (t % 23 == 22)
            {
                int i = RandomUnit(ref rng, w, rng.NextInt(0, 2), workersOnly: false);
                if (i >= 0 && !initial.Contains(i) && u.Cargo[i] == 0)
                {
                    deadGold += w.Data.Units[u.TypeId[i]].CostGold;
                    deadWood += w.Data.Units[u.TypeId[i]].CostWood;
                    a.World.Units.Free(new EntityHandle(i, a.World.Units.Generation[i]));
                    b.World.Units.Free(new EntityHandle(i, b.World.Units.Generation[i]));
                    removed++;
                }
            }
            for (int k = 0; k < snap.Length; k++)
                snap[k] = new SlotSnap
                {
                    Alive = bs.Alive[k], Generation = bs.Generation[k], Owner = bs.Owner[k],
                    Type = bs.TypeId[k], Work = bs.Work[k], Site = bs.UnderConstruction[k],
                };
            SimRng rb = rng;
            RandomCommands(ref rng, a, slots);
            RandomCommands(ref rb, b, slots);
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");

            for (int k = 0; k < snap.Length; k++)
            {
                bool same = bs.Alive[k] && snap[k].Alive && bs.Generation[k] == snap[k].Generation;
                if (snap[k].Alive && snap[k].Site && !same)
                {
                    BuildingDef d = w.Data.Buildings[snap[k].Type];
                    long needed = bs.WorkNeeded(snap[k].Type), left = needed - snap[k].Work;
                    buildingGold[snap[k].Owner] -= d.CostGold * left / needed;
                    buildingWood[snap[k].Owner] -= d.CostWood * left / needed;
                }
                if (bs.Alive[k] && !same)
                {
                    Assert.True(bs.UnderConstruction[k], "only Build places buildings in the fuzz");
                    BuildingDef d = w.Data.Buildings[bs.TypeId[k]];
                    buildingGold[bs.Owner[k]] += d.CostGold;
                    buildingWood[bs.Owner[k]] += d.CostWood;
                }
            }

            long prodGold = deadGold, prodWood = deadWood;
            int researched = 0, queued = 0;
            for (int p = 0; p < 2; p++)
            {
                Assert.True(w.Gold[p] >= 0 && w.Wood[p] >= 0, $"seed {seed} tick {t}: negative totals");
                (long qg, long qw) = QueuedCost(w, p);
                (long rg, long rw) = ResearchedCost(w, p);
                prodGold += qg + rg;
                prodWood += qw + rw;
                Assert.True(RecountHalfPop(w, p) == w.HalfPop[p], $"seed {seed} tick {t} player {p}: HalfPop {w.HalfPop[p]} recount {RecountHalfPop(w, p)}");
                for (int tech = 0; tech < w.Data.Techs.Length; tech++)
                {
                    int copies = 0;
                    for (int k = 0; k < bs.Capacity; k++)
                    {
                        if (!bs.Alive[k] || bs.Owner[k] != p) continue;
                        for (int q = 0; q < bs.QueueCount[k]; q++)
                            if (bs.QueueIsTechAt(k, q) && bs.QueueTypeAt(k, q) == tech)
                            {
                                copies++;
                                Assert.True(System.Collections.Immutable.ImmutableArray.BinarySearch(w.Data.TechsResearchableAt(bs.TypeId[k]), tech) >= 0,
                                    $"seed {seed} tick {t}: {w.Data.Techs[tech].Key} queued at a building that doesn't research it");
                            }
                    }
                    Assert.True(copies <= 1, $"seed {seed} tick {t}: player {p} has {w.Data.Techs[tech].Key} queued {copies} times");
                    Assert.False(copies == 1 && w.HasTech(p, tech), $"seed {seed} tick {t}: {w.Data.Techs[tech].Key} queued and researched");
                    int faction = w.Data.Techs[tech].Faction;
                    Assert.False((copies > 0 || w.HasTech(p, tech)) && faction >= 0 && faction != w.FactionOf(p), $"seed {seed} tick {t}: another faction's upgrade");
                    queued += copies;
                    researched += w.HasTech(p, tech) ? 1 : 0;
                }
                Assert.Equal(w.HasTech(p, AgeII) ? 2 : 1, w.Age(p));
            }
            for (int k = 0; k < bs.Capacity; k++)
                if (bs.Alive[k] && bs.UnderConstruction[k]) Assert.Equal(0, bs.QueueCount[k]);
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y));
                if (initial.Contains(i)) continue;
                prodGold += w.Data.Units[u.TypeId[i]].CostGold;
                prodWood += w.Data.Units[u.TypeId[i]].CostWood;
            }
            (long gold, long wood) = Conserved(w);
            long spentGold = gold0 - gold - (buildingGold[0] + buildingGold[1]);
            long spentWood = wood0 - wood - (buildingWood[0] + buildingWood[1]);
            Assert.True(spentGold == prodGold, $"seed {seed} tick {t}: gold spent {spentGold}, units + queued items + techs cost {prodGold}");
            Assert.True(spentWood == prodWood, $"seed {seed} tick {t}: wood spent {spentWood}, units + queued items + techs cost {prodWood}");
            maxQueued = Math.Max(maxQueued, queued);
            if (researched != lastResearched)
            {
                Assert.True(researched > lastResearched, "a researched tech was lost");
                completions += researched - lastResearched;
                lastResearched = researched;
                oracle.AssertMatches(w, $"seed {seed} tick {t}");
                oracleChecks++;
            }
        }
        _out.WriteLine($"seed {seed}: {completions} techs researched ({oracleChecks} oracle checks), at most {maxQueued} queued at once, {techItemsLost} tech items lost with {destroyed} buildings destroyed, {removed} units removed, age {w.Age(0)} / {w.Age(1)}; final hash {a.StateHash():X16}");
        Assert.True(completions >= 4, $"only {completions} techs researched");
        Assert.True(techItemsLost >= 1, "no research item was lost with a building");
        Assert.True(maxQueued >= 3, $"at most {maxQueued} techs queued at once");
    }
}
