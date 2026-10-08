using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResearchMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M3-5 criteria 7-8: random Train / Research / CancelTrain / Build / Cancel commands on two players for 3,000 ticks, with
/// buildings destroyed and trained units removed now and then: twin sims stay hash-identical every tick, every player's
/// spending balances exactly against units, queued items (units and techs) and researched techs, a tech is never both
/// researched and queued nor queued twice, and the population equals its recount.
/// </summary>
[Collection(SerialCollection.Name)]
public class ResearchFuzzTests
{
    private const int Ticks = 3000;
    private const int Start = 20_000;
    private readonly ITestOutputHelper _out;

    public ResearchFuzzTests(ITestOutputHelper output) => _out = output;

    /// <summary>Two players on a flat 64 x 48 map, each with a Town Hall, a production hall, a Forge and four workers.</summary>
    private static Simulation Setup()
    {
        Simulation sim = BuildMaps.NewSim(Flat(64, 48), units: 200, players: 2, combat: false);
        Building(sim, 4, 4);
        Building(sim, 12, 4, type: ProductionMaps.Barracks);
        Building(sim, 18, 4, type: ResearchMaps.Armory);
        Building(sim, 40, 30, player: 1, type: HolyCamp);
        Building(sim, 48, 30, player: 1, type: RaiderCamp);
        Building(sim, 54, 30, player: 1, type: ResearchMaps.Smithy);
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
                case 0: case 1: case 2:
                    var trains = slot >= 0 ? w.Data.UnitsTrainedAt(w.Buildings.TypeId[slot]) : default;
                    int unit = slot >= 0 && trains.Length > 0 && rng.NextInt(0, 4) != 0
                        ? trains[rng.NextInt(0, trains.Length)]
                        : rng.NextInt(-1, w.Data.Units.Length + 1);
                    sim.Enqueue(Command.Train(p, at, unit));
                    break;
                case 3: case 4: case 5: case 6:
                    // Mostly a tech the building researches (another player's building now and then), else any id.
                    var techs = slot >= 0 ? w.Data.TechsResearchableAt(w.Buildings.TypeId[slot]) : default;
                    int tech = slot >= 0 && techs.Length > 0 && rng.NextInt(0, 5) != 0
                        ? techs[rng.NextInt(0, techs.Length)]
                        : rng.NextInt(-1, w.Data.Techs.Length + 1);
                    sim.Enqueue(Command.Research(p, at, tech));
                    break;
                case 7: case 8:
                    // Rarer than queueing: a cancel every few hundred ticks still lets a 600-1,200 tick research finish.
                    if (rng.NextInt(0, 4) == 0) sim.Enqueue(Command.CancelTrain(p, at, rng.NextInt(-1, 6)));
                    break;
                case 9:
                    int worker = RandomUnit(ref rng, w, p, workersOnly: true);
                    if (worker < 0) break;
                    int[] types = p == 0 ? new[] { House, ProductionMaps.Barracks, Keep, ResearchMaps.Armory } : new[] { WhirlwindHouse, RaiderCamp, HolyCamp, ResearchMaps.Smithy };
                    sim.Enqueue(Command.Build(p, new EntityHandle(worker, w.Units.Generation[worker]), types[rng.NextInt(0, types.Length)], anywhere));
                    break;
                case 10:
                    sim.Enqueue(Command.Cancel(p, slot >= 0 ? at : anywhere));
                    break;
                case 11:
                    sim.Enqueue(Command.SetRally(p, slot >= 0 ? w.Buildings.Cell[slot] : 0, anywhere));
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
        public bool Alive, Site;
        public int Generation, Owner, Type, Work;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void Twins_StayIdentical_AndSpendingBalancesAgainstUnitsQueuedItemsAndResearchedTechs_EveryTick(ulong seed)
    {
        Simulation a = Setup(), b = Setup();
        World w = a.World;
        var initial = new HashSet<int>(LiveUnits(w));
        var rng = new SimRng(seed, 91);
        var slots = new List<int>();
        var snap = new SlotSnap[w.Buildings.Capacity];
        var buildingGold = new long[2];
        var buildingWood = new long[2];
        var deadGold = new long[2];
        var deadWood = new long[2];
        int destroyed = 0, removed = 0, maxQueuedTechs = 0, techItemsDestroyed = 0;
        for (int t = 0; t < Ticks; t++)
        {
            // Test seams between ticks, on both twins: a building destroyed now and then, a trained unit removed (M4 death).
            if (t % 600 == 599)
            {
                // A live building; every other time one researching when there is any, so destroyed research items get refunded.
                slots.Clear();
                for (int s = 0; s < w.Buildings.Capacity; s++)
                    if (w.Buildings.Alive[s] && w.Buildings.QueueIsTechAt(s, 0) && t % 1200 == 599) slots.Add(s);
                if (slots.Count == 0)
                    for (int s = 0; s < w.Buildings.Capacity; s++)
                        if (w.Buildings.Alive[s]) slots.Add(s);
                int k = slots.Count > 0 ? slots[rng.NextInt(0, slots.Count)] : -1;
                if (k >= 0)
                {
                    for (int q = 0; q < w.Buildings.QueueCount[k]; q++) techItemsDestroyed += w.Buildings.QueueIsTechAt(k, q) ? 1 : 0;
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
            a.Tick();
            b.Tick();
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

            int queuedTechs = 0;
            for (int p = 0; p < 2; p++)
            {
                // Spent on production = every queued item (unit or tech) + every unit trained (alive or removed) + every tech researched.
                (long qg, long qw) = QueuedCost(w, p);
                (long rg, long rw) = ResearchedCost(w, p);
                long gold = deadGold[p] + qg + rg, wood = deadWood[p] + qw + rw;
                foreach (int i in LiveUnits(w))
                {
                    if (initial.Contains(i) || w.Units.Owner[i] != p) continue;
                    gold += w.Data.Units[w.Units.TypeId[i]].CostGold;
                    wood += w.Data.Units[w.Units.TypeId[i]].CostWood;
                }
                Assert.True(Start - w.Gold[p] - buildingGold[p] == gold, $"seed {seed} tick {t} player {p}: gold spent {Start - w.Gold[p] - buildingGold[p]}, items, units and techs cost {gold}");
                Assert.True(Start - w.Wood[p] - buildingWood[p] == wood, $"seed {seed} tick {t} player {p}: wood");
                Assert.Equal(RecountHalfPop(w, p), w.HalfPop[p]);

                // A tech is queued at most once per player, never once researched, and only one of the player's faction or common.
                for (int tech = 0; tech < w.Data.Techs.Length; tech++)
                {
                    int copies = 0;
                    for (int k = 0; k < w.Buildings.Capacity; k++)
                    {
                        if (!w.Buildings.Alive[k] || w.Buildings.Owner[k] != p) continue;
                        for (int q = 0; q < w.Buildings.QueueCount[k]; q++)
                            if (w.Buildings.QueueIsTechAt(k, q) && w.Buildings.QueueTypeAt(k, q) == tech) copies++;
                    }
                    Assert.True(copies <= 1, $"tick {t}: player {p} has {w.Data.Techs[tech].Key} queued {copies} times");
                    Assert.False(copies == 1 && w.HasTech(p, tech), $"tick {t}: player {p} has {w.Data.Techs[tech].Key} researched and queued");
                    int faction = w.Data.Techs[tech].Faction;
                    Assert.False((copies == 1 || w.HasTech(p, tech)) && faction >= 0 && faction != w.FactionOf(p));
                    queuedTechs += copies;
                }
                Assert.Equal(w.HasTech(p, AgeII) ? 2 : 1, w.Age(p));
            }
            maxQueuedTechs = Math.Max(maxQueuedTechs, queuedTechs);
            foreach (int i in initial) Assert.True(w.Units.Alive[i]);
        }
        int researched = 0;
        for (int p = 0; p < 2; p++)
            for (int tech = 0; tech < w.Data.Techs.Length; tech++) researched += w.HasTech(p, tech) ? 1 : 0;
        _out.WriteLine($"seed {seed}: {researched} techs researched, at most {maxQueuedTechs} queued at once, {techItemsDestroyed} tech items lost with {destroyed} buildings destroyed, {removed} units removed, age {w.Age(0)} / {w.Age(1)}");
        Assert.True(researched >= 3, $"only {researched} techs researched");
        Assert.True(techItemsDestroyed >= 1, "no research item was lost with a building");
        Assert.True(maxQueuedTechs >= 2, $"at most {maxQueuedTechs} techs queued at once");
    }
}
