using System.Numerics;
using System.Text.Json.Nodes;
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

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M3-6 (2026-10-07-1415): 8 seeds x 3,000 ticks of random Train / Research / Build / Cancel / CancelTrain / Repair on
/// three players (players 0 and 2 both Malazan: the mirror match), with test seams that destroy finished buildings and
/// sites, finish sites and damage buildings for repair, on a fixture where every gate kind has a building requirement
/// (a unit, a building, a faction tech with requires and one with requiresAnyOf naming own buildings). After every tick:
/// three twins hash-identical; the ledger's finished counts equal a recount; exact conservation of both resources
/// including repair payments (recomputed from hit points and the store's repair accumulators); totals never negative;
/// and an independent oracle (store scan, not the ledger) proves every item queued and every site placed this tick had
/// its requirement met at the start of the tick.
/// </summary>
[Collection(SerialCollection.Name)]
public class RequirementHostileFuzzStressTests
{
    private const int Ticks = 3000;
    private const int Start = 20_000;
    private const int Players = 3;
    private readonly ITestOutputHelper _out;

    public RequirementHostileFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_data = new(LoadData);

    private static GameData D => s_data.Value;

    private static GameData LoadData()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "requires", "[\"malazan_crossbow_range\"]");
        dir.SetUnitField("whirlwind", "whirlwind_raider", "requires", "[\"whirlwind_archer_camp\"]");
        dir.EditJson("factions/malazan/buildings.json", root =>
        {
            foreach (JsonNode? b in root["buildings"]!.AsArray())
                b!["requires"] = (string)b["id"]! switch
                {
                    "malazan_cadre_tower" => JsonNode.Parse("[\"age_ii\"]"),
                    "malazan_wickan_corral" => JsonNode.Parse("[\"malazan_barracks\"]"),
                    _ => new JsonArray(),
                };
        });
        dir.EditJson("factions/whirlwind/buildings.json", root =>
        {
            foreach (JsonNode? b in root["buildings"]!.AsArray())
                b!["requires"] = (string)b["id"]! == "whirlwind_horse_lines" ? JsonNode.Parse("[\"whirlwind_raider_camp\"]") : new JsonArray();
        });
        dir.EditJson("factions/malazan/techs.json", root => root["techs"]![0]!["requires"] = JsonNode.Parse("[\"age_ii\", \"malazan_crossbow_range\"]"));
        dir.EditJson("factions/whirlwind/techs.json", root =>
            root["techs"]![0]!["requiresAnyOf"] = JsonNode.Parse("{\"count\": 1, \"of\": [\"whirlwind_shrine\", \"siege_works\"]}"));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    private static int B(string key) => D.FindBuilding(key);

    private static readonly string[] MalazanTypes =
        { "malazan_billet", "malazan_barracks", "malazan_crossbow_range", "malazan_wickan_corral", "malazan_armory", "malazan_cadre_tower", "malazan_engineers_yard", "malazan_watchtower" };
    private static readonly string[] WhirlwindTypes =
        { "whirlwind_tent", "whirlwind_raider_camp", "whirlwind_archer_camp", "whirlwind_horse_lines", "whirlwind_smithy", "whirlwind_shrine", "whirlwind_ram_yard" };

    /// <summary>Building types some requirement in the fixture names (the seam's preferred kills).</summary>
    private static readonly int[] Required =
        { B("malazan_crossbow_range"), B("malazan_barracks"), B("whirlwind_archer_camp"), B("whirlwind_raider_camp"), B("whirlwind_shrine"), B("whirlwind_ram_yard") };

    private static Simulation Setup()
    {
        Simulation sim = new(new SimConfig(5, Players, 300, 1024) { Data = D }, ResourceMaps.Flat(72, 56));
        Building(sim, 4, 4);
        Building(sim, 12, 4, type: B("malazan_barracks"));
        Building(sim, 18, 4, type: B("malazan_armory"));
        Building(sim, 40, 40, player: 1, type: HolyCamp);
        Building(sim, 48, 40, player: 1, type: RaiderCamp);
        Building(sim, 56, 40, player: 1, type: B("whirlwind_smithy"));
        Building(sim, 4, 44, player: 2);
        Building(sim, 12, 44, player: 2, type: B("malazan_barracks"));
        Building(sim, 18, 44, player: 2, type: B("malazan_crossbow_range")); // the mirror enemy's Range must not open player 0's
        for (int n = 0; n < 5; n++)
        {
            sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 4 + n, 12)));
            sim.Enqueue(Command.SpawnUnit(1, CampFollower, At(sim, 40 + n, 48)));
            sim.Enqueue(Command.SpawnUnit(2, Laborer, At(sim, 4 + n, 52)));
        }
        Run(sim, 2);
        for (int p = 0; p < Players; p++) SetTotals(sim, p, Start, Start);
        return sim;
    }

    // ---------- the oracle: requirements recounted from the store, not the ledger ----------

    private static bool OwnsFinished(World w, int player, int type)
    {
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == player && !b.UnderConstruction[k] && b.TypeId[k] == type) return true;
        return false;
    }

    private static bool Met(World w, int player, IEnumerable<int> techs, IEnumerable<int> buildings) =>
        techs.All(t => w.HasTech(player, t)) && buildings.All(t => OwnsFinished(w, player, t));

    private static bool TechMet(World w, int player, TechDef t)
    {
        if (!Met(w, player, t.RequiresTechs, t.RequiresBuildings)) return false;
        if (t.RequiresAnyOfCount == 0) return true;
        var slots = new HashSet<int>();
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == player && !b.UnderConstruction[k]) slots.Add((int)w.Data.Buildings[b.TypeId[k]].Slot);
        return t.RequiresAnyOf.Count(id => slots.Contains(SlotOf(w, id))) >= t.RequiresAnyOfCount;
    }

    private static int SlotOf(World w, string id)
    {
        int s = DataLimits.BuildingSlotIds.IndexOf(id);
        return s >= 0 ? s : (int)w.Data.Buildings[w.Data.FindBuilding(id)].Slot;
    }

    // ---------- per-tick snapshot ----------

    private struct Snap
    {
        public bool Alive, Site;
        public int Generation, Owner, Type, Work, Hp;
        public long RepairGold, RepairWood;
        public Dictionary<(bool, int), int> Items;
    }

    private static Snap[] TakeSnap(World w)
    {
        BuildingStore b = w.Buildings;
        var s = new Snap[b.Capacity];
        for (int k = 0; k < b.Capacity; k++)
        {
            s[k] = new Snap
            {
                Alive = b.Alive[k], Site = b.UnderConstruction[k], Generation = b.Generation[k], Owner = b.Owner[k],
                Type = b.TypeId[k], Work = b.Work[k], Hp = b.Hp[k], RepairGold = b.RepairGold(k), RepairWood = b.RepairWood(k),
                Items = new Dictionary<(bool, int), int>(),
            };
            if (!b.Alive[k]) continue;
            for (int q = 0; q < b.QueueCount[k]; q++)
            {
                var key = (b.QueueIsTechAt(k, q), b.QueueTypeAt(k, q));
                s[k].Items[key] = s[k].Items.GetValueOrDefault(key) + 1;
            }
        }
        return s;
    }

    private static int RandomWorker(ref SimRng rng, World w, int player)
    {
        int n = 0, pick = -1;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == player && w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker && rng.NextInt(0, ++n) == 0) pick = i;
        return pick;
    }

    private static void RandomCommands(ref SimRng rng, Simulation sim, List<int> pool)
    {
        World w = sim.World;
        for (int p = 0; p < Players; p++)
        {
            if (rng.NextInt(0, 3) == 0) continue;
            pool.Clear();
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k] && (w.Buildings.Owner[k] == p || rng.NextInt(0, 8) == 0)) pool.Add(k);
            int slot = pool.Count > 0 ? pool[rng.NextInt(0, pool.Count)] : -1;
            Vector2 at = slot >= 0 ? In(sim, slot) : At(sim, rng.NextInt(0, 72), rng.NextInt(0, 56));
            switch (rng.NextInt(0, 12))
            {
                case 0: case 1: case 2:
                {
                    var trains = slot >= 0 ? w.Data.UnitsTrainedAt(w.Buildings.TypeId[slot]) : default;
                    int unit = slot >= 0 && trains.Length > 0 && rng.NextInt(0, 6) != 0 ? trains[rng.NextInt(0, trains.Length)] : rng.NextInt(-2, w.Data.Units.Length + 2);
                    sim.Enqueue(Command.Train(p, at, unit));
                    if (rng.NextInt(0, 4) == 0) sim.Enqueue(Command.Train(p, at, unit)); // spam
                    break;
                }
                case 3: case 4: case 5:
                {
                    var techs = slot >= 0 ? w.Data.TechsResearchableAt(w.Buildings.TypeId[slot]) : default;
                    int tech = slot >= 0 && techs.Length > 0 && rng.NextInt(0, 6) != 0 ? techs[rng.NextInt(0, techs.Length)] : rng.NextInt(-2, w.Data.Techs.Length + 2);
                    sim.Enqueue(Command.Research(p, at, tech));
                    break;
                }
                case 6: case 7:
                {
                    int worker = RandomWorker(ref rng, w, p);
                    if (worker < 0) break;
                    string[] types = w.FactionOf(p) == w.FactionOf(0) ? MalazanTypes : WhirlwindTypes;
                    int type = rng.NextInt(0, 10) == 0 ? rng.NextInt(-1, w.Data.Buildings.Length + 1) : B(types[rng.NextInt(0, types.Length)]);
                    sim.Enqueue(Command.Build(p, new EntityHandle(worker, w.Units.Generation[worker]), type,
                        At(sim, rng.NextInt(0, 70), rng.NextInt(0, 54)), rng.NextInt(0, 5) == 0));
                    break;
                }
                case 8:
                    sim.Enqueue(Command.CancelTrain(p, at, rng.NextInt(-1, 6)));
                    break;
                case 9:
                    sim.Enqueue(Command.Cancel(p, at));
                    break;
                case 10: case 11:
                {
                    int worker = RandomWorker(ref rng, w, p);
                    if (worker < 0) break;
                    sim.Enqueue(Command.Repair(p, new EntityHandle(worker, w.Units.Generation[worker]), at, rng.NextInt(0, 5) == 0));
                    break;
                }
            }
        }
    }

    [Theory]
    [InlineData(11UL)]
    [InlineData(12UL)]
    [InlineData(13UL)]
    [InlineData(14UL)]
    [InlineData(15UL)]
    [InlineData(16UL)]
    [InlineData(17UL)]
    [InlineData(18UL)]
    public void Twins_Conservation_DerivedCounts_AndTheQueueTimeOracle_HoldEveryTick(ulong seed)
    {
        Simulation[] sims = { Setup(), Setup(), Setup() };
        Simulation a = sims[0];
        World w = a.World;
        var initial = new HashSet<int>(LiveUnits(w));
        var rng = new SimRng(seed, 61);
        var seam = new SimRng(seed, 62);
        var pool = new List<int>();
        var buildingNet = new long[Players, 2];
        var repairPaid = new long[Players, 2];
        var dead = new long[Players, 2];
        var seamDestroyedSite = new HashSet<(int, int)>(); // (slot, generation)
        long one = EconomyConstants.RepairFixedOne;
        long costRate = (long)MathF.Round(D.Rules.RepairCostFactor * one);
        int destroyed = 0, sitesKilled = 0, completed = 0, damaged = 0, gatedQueued = 0, gatedPlaced = 0, repairTicks = 0;
        var flips = new int[3];
        var last = new bool[3];

        for (int t = 0; t < Ticks; t++)
        {
            // ---- seams, identical on every twin ----
            if (t % 97 == 96 || t % 151 == 150)
            {
                bool site = t % 151 == 150;
                pool.Clear();
                // Every other finished-building kill goes for a required type when one stands, so requirements go as well as come.
                if (!site && t / 97 % 2 == 0)
                    for (int k = 0; k < w.Buildings.Capacity; k++)
                        if (w.Buildings.Alive[k] && !w.Buildings.UnderConstruction[k] && Array.IndexOf(Required, w.Buildings.TypeId[k]) >= 0) pool.Add(k);
                if (pool.Count == 0)
                    for (int k = 0; k < w.Buildings.Capacity; k++)
                        if (w.Buildings.Alive[k] && w.Buildings.UnderConstruction[k] == site) pool.Add(k);
                if (pool.Count > 0)
                {
                    int k = pool[seam.NextInt(0, pool.Count)];
                    if (site) seamDestroyedSite.Add((k, w.Buildings.Generation[k]));
                    foreach (Simulation s in sims) s.World.Buildings.Damage(s.World.Buildings.HandleOf(k), int.MaxValue);
                    if (site) sitesKilled++; else destroyed++;
                }
            }
            // Every 300 ticks a "raid" levels every finished building of the unit requirements' types (Range for the
            // Heavy Infantry, Archer Camp for the Raider), so those requirements close completely, then reopen as rebuilt.
            if (t % 300 == 299)
                for (int k = 0; k < w.Buildings.Capacity; k++)
                    if (w.Buildings.Alive[k] && !w.Buildings.UnderConstruction[k] && (w.Buildings.TypeId[k] == Required[0] || w.Buildings.TypeId[k] == Required[2]))
                    {
                        foreach (Simulation s in sims) s.World.Buildings.Damage(s.World.Buildings.HandleOf(k), int.MaxValue);
                        destroyed++;
                    }
            if (t % 19 == 18)
            {
                pool.Clear();
                for (int k = 0; k < w.Buildings.Capacity; k++)
                    if (w.Buildings.Alive[k] && w.Buildings.UnderConstruction[k]) pool.Add(k);
                if (pool.Count > 0)
                {
                    int k = pool[seam.NextInt(0, pool.Count)];
                    foreach (Simulation s in sims) s.World.Buildings.SetWork(k, s.World.Buildings.WorkNeeded(s.World.Buildings.TypeId[k]));
                    completed++;
                }
            }
            if (t % 13 == 12)
            {
                pool.Clear();
                for (int k = 0; k < w.Buildings.Capacity; k++)
                    if (w.Buildings.Alive[k] && !w.Buildings.UnderConstruction[k] && w.Buildings.Hp[k] > 2) pool.Add(k);
                if (pool.Count > 0)
                {
                    int k = pool[seam.NextInt(0, pool.Count)];
                    int amount = w.Buildings.Hp[k] / 2;
                    foreach (Simulation s in sims) s.World.Buildings.Damage(s.World.Buildings.HandleOf(k), amount);
                    damaged++;
                }
            }
            if (t % 43 == 42)
            {
                int i = -1, n = 0;
                for (int s = 0; s < w.Units.Capacity; s++)
                    if (w.Units.Alive[s] && !initial.Contains(s) && seam.NextInt(0, ++n) == 0) i = s;
                if (i >= 0)
                {
                    UnitDef d = w.Data.Units[w.Units.TypeId[i]];
                    dead[w.Units.Owner[i], 0] += d.CostGold;
                    dead[w.Units.Owner[i], 1] += d.CostWood;
                    foreach (Simulation s in sims) s.World.Units.Free(new EntityHandle(i, s.World.Units.Generation[i]));
                }
            }

            // ---- the oracle's view of the start of the tick (phase 1 sees exactly this) ----
            Snap[] before = TakeSnap(w);
            var unitOpen = new bool[Players, w.Data.Units.Length];
            var techOpen = new bool[Players, w.Data.Techs.Length];
            var buildingOpen = new bool[Players, w.Data.Buildings.Length];
            for (int p = 0; p < Players; p++)
            {
                for (int u = 0; u < w.Data.Units.Length; u++) unitOpen[p, u] = Met(w, p, w.Data.Units[u].RequiresTechs, w.Data.Units[u].RequiresBuildings);
                for (int x = 0; x < w.Data.Techs.Length; x++) techOpen[p, x] = TechMet(w, p, w.Data.Techs[x]);
                for (int x = 0; x < w.Data.Buildings.Length; x++) buildingOpen[p, x] = Met(w, p, w.Data.Buildings[x].RequiresTechs, w.Data.Buildings[x].RequiresBuildings);
            }

            for (int s = 0; s < sims.Length; s++)
            {
                SimRng r = rng;
                RandomCommands(ref r, sims[s], pool);
                if (s == sims.Length - 1) rng = r;
            }
            foreach (Simulation s in sims) s.Tick();
            ulong h = a.StateHash();
            for (int s = 1; s < sims.Length; s++)
                Assert.True(h == sims[s].StateHash(), $"seed {seed}: twin {s} differs after tick {t}");

            // ---- derived counts against a recount ----
            for (int p = 0; p < Players; p++)
                for (int x = 0; x < w.Data.Buildings.Length; x++)
                {
                    int n = 0;
                    for (int k = 0; k < w.Buildings.Capacity; k++)
                        if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == p && !w.Buildings.UnderConstruction[k] && w.Buildings.TypeId[k] == x) n++;
                    Assert.True(n == w.Ledger.FinishedOfType(p, x), $"seed {seed} tick {t}: player {p} {w.Data.Buildings[x].Key} ledger {w.Ledger.FinishedOfType(p, x)} vs {n}");
                }
            for (int p = 0; p < Players; p++)
                for (int sl = 0; sl < DataLimits.BuildingSlotIds.Length; sl++)
                {
                    int n = 0;
                    for (int k = 0; k < w.Buildings.Capacity; k++)
                        if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == p && !w.Buildings.UnderConstruction[k] && (int)w.Data.Buildings[w.Buildings.TypeId[k]].Slot == sl) n++;
                    Assert.True(n == w.Ledger.FinishedInSlot(p, sl), $"seed {seed} tick {t}: player {p} slot {sl}");
                }

            // ---- per-slot changes: new queue items and sites against the oracle; building and repair money ----
            for (int k = 0; k < before.Length; k++)
            {
                Snap o = before[k];
                bool same = w.Buildings.Alive[k] && o.Alive && w.Buildings.Generation[k] == o.Generation;
                if (o.Alive && o.Site && !same && !seamDestroyedSite.Contains((k, o.Generation)))
                {
                    // Cancelled this tick (phase 1, before any work): the unbuilt fraction came back.
                    BuildingDef d = w.Data.Buildings[o.Type];
                    long needed = w.Buildings.WorkNeeded(o.Type), left = needed - o.Work;
                    buildingNet[o.Owner, 0] -= d.CostGold * left / needed;
                    buildingNet[o.Owner, 1] -= d.CostWood * left / needed;
                }
                if (w.Buildings.Alive[k] && !same)
                {
                    Assert.True(w.Buildings.UnderConstruction[k], "only Build places buildings in the fuzz");
                    int owner = w.Buildings.Owner[k], type = w.Buildings.TypeId[k];
                    BuildingDef d = w.Data.Buildings[type];
                    Assert.True(buildingOpen[owner, type], $"seed {seed} tick {t}: {d.Key} placed for player {owner} with its requirement unmet");
                    if (d.Requires.Length > 0) gatedPlaced++;
                    buildingNet[owner, 0] += d.CostGold;
                    buildingNet[owner, 1] += d.CostWood;
                }
                if (same && !o.Site)
                {
                    int restored = w.Buildings.Hp[k] - o.Hp;
                    Assert.True(restored >= 0, $"seed {seed} tick {t}: slot {k} lost hit points inside a tick");
                    if (restored > 0)
                    {
                        BuildingDef d = w.Data.Buildings[o.Type];
                        long perUnit = d.Hp * one;
                        repairPaid[o.Owner, 0] += (o.RepairGold + restored * d.CostGold * costRate) / perUnit;
                        repairPaid[o.Owner, 1] += (o.RepairWood + restored * d.CostWood * costRate) / perUnit;
                        repairTicks++;
                    }
                }
                if (same)
                {
                    int owner = w.Buildings.Owner[k];
                    var now = new Dictionary<(bool, int), int>();
                    for (int q = 0; q < w.Buildings.QueueCount[k]; q++)
                    {
                        var key = (w.Buildings.QueueIsTechAt(k, q), w.Buildings.QueueTypeAt(k, q));
                        now[key] = now.GetValueOrDefault(key) + 1;
                    }
                    foreach (((bool tech, int type), int n) in now)
                    {
                        if (n <= o.Items.GetValueOrDefault((tech, type))) continue;
                        bool open = tech ? techOpen[owner, type] : unitOpen[owner, type];
                        string key = tech ? w.Data.Techs[type].Key : w.Data.Units[type].Key;
                        Assert.True(open, $"seed {seed} tick {t}: {key} queued for player {owner} with its requirement unmet");
                        bool gated = tech ? w.Data.Techs[type].Requires.Length + w.Data.Techs[type].RequiresAnyOfCount > 0 : w.Data.Units[type].Requires.Length > 0;
                        if (gated) gatedQueued++;
                    }
                }
            }

            // ---- exact conservation, per player and resource ----
            for (int p = 0; p < Players; p++)
            {
                Assert.True(w.Gold[p] >= 0 && w.Wood[p] >= 0, $"seed {seed} tick {t}: player {p} negative totals");
                (long qg, long qw) = QueuedCost(w, p);
                (long rg, long rw) = ResearchedCost(w, p);
                long gold = dead[p, 0] + qg + rg, wood = dead[p, 1] + qw + rw;
                foreach (int i in LiveUnits(w))
                {
                    if (initial.Contains(i) || w.Units.Owner[i] != p) continue;
                    gold += w.Data.Units[w.Units.TypeId[i]].CostGold;
                    wood += w.Data.Units[w.Units.TypeId[i]].CostWood;
                }
                long spentGold = Start - w.Gold[p] - buildingNet[p, 0] - repairPaid[p, 0];
                long spentWood = Start - w.Wood[p] - buildingNet[p, 1] - repairPaid[p, 1];
                Assert.True(spentGold == gold, $"seed {seed} tick {t} player {p}: gold spent {spentGold}, items / units / techs {gold}");
                Assert.True(spentWood == wood, $"seed {seed} tick {t} player {p}: wood spent {spentWood}, items / units / techs {wood}");
                Assert.Equal(RecountHalfPop(w, p), w.HalfPop[p]);
            }

            for (int p = 0; p < Players; p++)
            {
                bool open = unitOpen[p, w.FactionOf(p) == w.FactionOf(0) ? Infantry : Raider];
                if (open != last[p]) flips[p]++;
                last[p] = open;
            }
        }
        _out.WriteLine($"seed {seed}: {gatedQueued} gated items queued, {gatedPlaced} gated sites placed, {destroyed} finished and {sitesKilled} sites destroyed, " +
            $"{completed} sites finished by the seam, {damaged} damaged, {repairTicks} repair ticks, ages {w.Age(0)}/{w.Age(1)}/{w.Age(2)}, unit-requirement flips {string.Join("/", flips)}");
        Assert.True(gatedQueued > 0, "no gated item was ever queued");
        Assert.True(repairTicks > 0, "nothing was ever repaired");
        Assert.True(flips[0] + flips[1] + flips[2] >= 6, $"the unit requirements came and went only {flips[0] + flips[1] + flips[2]} times");
    }
}
