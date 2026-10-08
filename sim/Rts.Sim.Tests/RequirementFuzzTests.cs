using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResearchMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M3-6 criteria 6-7: random Train / Research / Build / Cancel / CancelTrain commands on two players for 3,000 ticks with
/// requirements that come and go (halls built, finished and destroyed; Age II researched), on the gating fixture
/// (<see cref="RequirementGatingTests.Fixture"/>): twin sims stay hash-identical every tick, spending balances exactly
/// (a locked command never moves money), and the ledger's derived finished counts equal a recount of the store.
/// </summary>
[Collection(SerialCollection.Name)]
public class RequirementFuzzTests
{
    private const int Ticks = 3000;
    private const int Start = 20_000;
    private readonly ITestOutputHelper _out;

    public RequirementFuzzTests(ITestOutputHelper output) => _out = output;

    private static GameData D => RequirementGatingTests.Fixture;

    private static int B(string key) => D.FindBuilding(key);

    private static readonly string[][] BuildTypes =
    {
        new[] { "malazan_billet", "malazan_barracks", "malazan_crossbow_range", "malazan_wickan_corral", "malazan_armory", "malazan_cadre_tower", "malazan_engineers_yard" },
        new[] { "whirlwind_tent", "whirlwind_raider_camp", "whirlwind_archer_camp", "whirlwind_horse_lines", "whirlwind_smithy", "whirlwind_shrine", "whirlwind_ram_yard" },
    };

    private static Simulation Setup()
    {
        Simulation sim = new(new SimConfig(5, 2, 200, 512) { Data = D, Combat = false }, ResourceMaps.Flat(64, 48));
        Building(sim, 4, 4);
        Building(sim, 12, 4, type: B("malazan_barracks"));
        Building(sim, 18, 4, type: B("malazan_armory"));
        Building(sim, 24, 4, type: B("malazan_engineers_yard"));
        Building(sim, 40, 30, player: 1, type: HolyCamp);
        Building(sim, 48, 30, player: 1, type: RaiderCamp);
        Building(sim, 54, 30, player: 1, type: B("whirlwind_smithy"));
        for (int n = 0; n < 5; n++)
        {
            sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 4 + n, 10)));
            sim.Enqueue(Command.SpawnUnit(1, CampFollower, At(sim, 40 + n, 36)));
        }
        Run(sim, 2);
        SetTotals(sim, 0, Start, Start);
        SetTotals(sim, 1, Start, Start);
        return sim;
    }

    private sealed class Counts
    {
        public int LockedTrain, LockedResearch, LockedPlace;
    }

    /// <summary>One tick's random commands for both players; the gate answers are read from <paramref name="sim"/> before they apply.</summary>
    private static void RandomCommands(ref SimRng rng, Simulation sim, List<int> buildingSlots, Counts? counts)
    {
        World w = sim.World;
        for (int p = 0; p < 2; p++)
        {
            if (rng.NextInt(0, 2) != 0) continue;
            buildingSlots.Clear();
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k] && (w.Buildings.Owner[k] == p || rng.NextInt(0, 10) == 0)) buildingSlots.Add(k);
            int slot = buildingSlots.Count > 0 ? buildingSlots[rng.NextInt(0, buildingSlots.Count)] : -1;
            Vector2 at = slot >= 0 ? In(sim, slot) : At(sim, rng.NextInt(1, 63), rng.NextInt(1, 47));
            int x = rng.NextInt(1, 60), y = rng.NextInt(1, 44);
            switch (rng.NextInt(0, 10))
            {
                case 0: case 1: case 2:
                    var trains = slot >= 0 ? w.Data.UnitsTrainedAt(w.Buildings.TypeId[slot]) : default;
                    int unit = slot >= 0 && trains.Length > 0 && rng.NextInt(0, 5) != 0 ? trains[rng.NextInt(0, trains.Length)] : rng.NextInt(-1, w.Data.Units.Length + 1);
                    if (counts != null && slot >= 0 && !w.CanTrain(p, slot, unit, out TrainError te) && te == TrainError.LockedByRequirement) counts.LockedTrain++;
                    sim.Enqueue(Command.Train(p, at, unit));
                    break;
                case 3: case 4: case 5:
                    var techs = slot >= 0 ? w.Data.TechsResearchableAt(w.Buildings.TypeId[slot]) : default;
                    int tech = slot >= 0 && techs.Length > 0 && rng.NextInt(0, 5) != 0 ? techs[rng.NextInt(0, techs.Length)] : rng.NextInt(-1, w.Data.Techs.Length + 1);
                    if (counts != null && slot >= 0 && !w.CanResearch(p, slot, tech, out ResearchError re) && re == ResearchError.Requires) counts.LockedResearch++;
                    sim.Enqueue(Command.Research(p, at, tech));
                    break;
                case 6: case 7:
                    int worker = RandomWorker(ref rng, w, p);
                    if (worker < 0) break;
                    int type = B(BuildTypes[p][rng.NextInt(0, BuildTypes[p].Length)]);
                    if (counts != null && !w.CanPlace(p, type, Cell(sim, x, y), out PlacementError pe) && pe == PlacementError.Requires) counts.LockedPlace++;
                    sim.Enqueue(Command.Build(p, new EntityHandle(worker, w.Units.Generation[worker]), type, At(sim, x, y)));
                    break;
                case 8:
                    if (rng.NextInt(0, 4) == 0) sim.Enqueue(Command.CancelTrain(p, at, rng.NextInt(-1, 6)));
                    break;
                case 9:
                    if (rng.NextInt(0, 3) == 0) sim.Enqueue(Command.Cancel(p, at));
                    break;
            }
        }
    }

    private static int RandomWorker(ref SimRng rng, World w, int player)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == player && w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker) n++;
        if (n == 0) return -1;
        int pick = rng.NextInt(0, n);
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == player && w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker && pick-- == 0) return i;
        return -1;
    }

    /// <summary>The ledger's finished counts (per type and slot) against a scan of the store.</summary>
    private static void AssertFinishedCounts(World w, int tick)
    {
        int slots = DataLimits.BuildingSlotIds.Length;
        for (int p = 0; p < 2; p++)
        {
            var ofType = new int[w.Data.Buildings.Length];
            var inSlot = new int[slots];
            for (int k = 0; k < w.Buildings.Capacity; k++)
            {
                if (!w.Buildings.Alive[k] || w.Buildings.Owner[k] != p || w.Buildings.UnderConstruction[k]) continue;
                ofType[w.Buildings.TypeId[k]]++;
                inSlot[(int)w.Data.Buildings[w.Buildings.TypeId[k]].Slot]++;
            }
            for (int t = 0; t < ofType.Length; t++)
                Assert.True(ofType[t] == w.Ledger.FinishedOfType(p, t), $"tick {tick} player {p} type {w.Data.Buildings[t].Key}: {w.Ledger.FinishedOfType(p, t)} counted, {ofType[t]} finished");
            for (int s = 0; s < slots; s++)
                Assert.True(inSlot[s] == w.Ledger.FinishedInSlot(p, s), $"tick {tick} player {p} slot {s}");
        }
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
    public void Twins_StayIdentical_SpendingBalances_AndFinishedCountsMatchARecount_EveryTick(ulong seed)
    {
        Simulation a = Setup(), b = Setup();
        World w = a.World;
        var initial = new HashSet<int>(LiveUnits(w));
        var rng = new SimRng(seed, 93);
        var slots = new List<int>();
        var counts = new Counts();
        var snap = new SlotSnap[w.Buildings.Capacity];
        var buildingGold = new long[2];
        var buildingWood = new long[2];
        var deadGold = new long[2];
        var deadWood = new long[2];
        int destroyed = 0, flips = 0, built = 0, completed = 0;
        int range = B("malazan_crossbow_range");
        bool open = false;
        for (int t = 0; t < Ticks; t++)
        {
            // Test seams on both twins: a finished building destroyed now and then (requirements go), a trained unit removed.
            if (t % 150 == 149)
            {
                // Every other time every finished Crossbow Range (the Heavy Infantry's requirement) when there is one, so the
                // requirement goes too. (M4-2a: all of them, not one. With BUG-0146's builder walks seed 1 keeps 3 to 6 finished
                // ranges at every seam, so destroying one never closed the requirement and the coverage check below starved.)
                slots.Clear();
                for (int s = 0; s < w.Buildings.Capacity; s++)
                    if (w.Buildings.Alive[s] && !w.Buildings.UnderConstruction[s] && w.Buildings.TypeId[s] == range && t % 300 == 149) slots.Add(s);
                bool ranges = slots.Count > 0;
                if (!ranges)
                    for (int s = 0; s < w.Buildings.Capacity; s++)
                        if (w.Buildings.Alive[s] && !w.Buildings.UnderConstruction[s]) slots.Add(s);
                int k = slots.Count > 0 ? slots[rng.NextInt(0, slots.Count)] : -1;
                foreach (int s in ranges ? slots : k >= 0 ? new List<int> { k } : new List<int>())
                {
                    a.World.Buildings.Damage(a.World.Buildings.HandleOf(s), 1_000_000);
                    b.World.Buildings.Damage(b.World.Buildings.HandleOf(s), 1_000_000);
                    destroyed++;
                }
            }
            // A site finished now and then (builders get pulled off often), so building requirements open as well as close.
            if (t % 23 == 22)
            {
                slots.Clear();
                for (int s = 0; s < w.Buildings.Capacity; s++)
                    if (w.Buildings.Alive[s] && w.Buildings.UnderConstruction[s]) slots.Add(s);
                int k = slots.Count > 0 ? slots[rng.NextInt(0, slots.Count)] : -1;
                if (k >= 0)
                {
                    a.World.Buildings.SetWork(k, a.World.Buildings.WorkNeeded(a.World.Buildings.TypeId[k]));
                    b.World.Buildings.SetWork(k, b.World.Buildings.WorkNeeded(b.World.Buildings.TypeId[k]));
                    completed++;
                }
            }
            if (t % 41 == 40)
            {
                int i = -1, n = 0;
                for (int s = 0; s < w.Units.Capacity; s++)
                    if (w.Units.Alive[s] && !initial.Contains(s) && rng.NextInt(0, ++n) == 0) i = s;
                if (i >= 0)
                {
                    UnitDef d = w.Data.Units[w.Units.TypeId[i]];
                    deadGold[w.Units.Owner[i]] += d.CostGold;
                    deadWood[w.Units.Owner[i]] += d.CostWood;
                    a.World.Units.Free(new EntityHandle(i, a.World.Units.Generation[i]));
                    b.World.Units.Free(new EntityHandle(i, b.World.Units.Generation[i]));
                }
            }
            for (int k = 0; k < snap.Length; k++)
                snap[k] = new SlotSnap
                {
                    Alive = w.Buildings.Alive[k], Generation = w.Buildings.Generation[k], Owner = w.Buildings.Owner[k],
                    Type = w.Buildings.TypeId[k], Work = w.Buildings.Work[k], Site = w.Buildings.UnderConstruction[k],
                };
            SimRng rb = rng;
            RandomCommands(ref rng, a, slots, counts);
            RandomCommands(ref rb, b, slots, null);
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            AssertFinishedCounts(w, t);

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
                    // A gated type is placed only once its requirement is met (it was met when the Build applied).
                    Assert.True(d.RequiresTechs.All(r => w.HasTech(w.Buildings.Owner[k], r)) || d.RequiresTechs.IsEmpty, $"{d.Key} placed while locked");
                    buildingGold[w.Buildings.Owner[k]] += d.CostGold;
                    buildingWood[w.Buildings.Owner[k]] += d.CostWood;
                    built++;
                }
            }

            for (int p = 0; p < 2; p++)
            {
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
            }
            // The Heavy Infantry's requirement (a finished Crossbow Range) coming and going.
            bool nowOpen = w.Ledger.FinishedOfType(0, range) > 0;
            if (nowOpen != open) flips++;
            open = nowOpen;
        }
        _out.WriteLine($"seed {seed}: locked Train {counts.LockedTrain}, Research {counts.LockedResearch}, Build {counts.LockedPlace}; " +
            $"{built} sites placed, {completed} completed by the seam, {destroyed} buildings destroyed, age {w.Age(0)} / {w.Age(1)}, infantry requirement flipped {flips} times");
        Assert.True(counts.LockedTrain > 0 && counts.LockedResearch > 0 && counts.LockedPlace > 0, "a gate was never refused");
        Assert.True(built >= 5, $"only {built} sites placed");
        Assert.True(flips >= 2, $"the infantry requirement flipped only {flips} times");
    }
}
