using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S2: the round-trip scenario set S of criterion 2, each a fresh reference sim plus its between-ticks script. Setups go
/// through commands (dev spawns and the start bases, as <c>Match.cs</c> places them) or the store seams before tick 0; every
/// later change is a command, so a load fed the same commands must follow the reference tick for tick.
/// </summary>
internal static class SaveScenes
{
    /// <summary>The ticks criterion 2 saves at.</summary>
    internal static readonly int[] SaveTicks = { 1, 137, 1000 };

    /// <summary>
    /// The shipped data with 50,000 starting gold and wood (so a 5-deep queue holding a tech is affordable from the start)
    /// and 20 wood a tree (so trees fall within the run).
    /// </summary>
    internal static GameData RichData => s_rich.Value;

    private static readonly Lazy<GameData> s_rich = new(() => AiScenes.DataWith(dir => dir.EditJson("common/rules.json", root =>
    {
        root["startingGold"] = 50_000;
        root["startingWood"] = 50_000;
        root["treeWood"] = 20;
    })));

    /// <summary>All of S: the golden replay's scene, the menu match on seeds 1-5, and the rest.</summary>
    internal static IEnumerable<SaveScene> All()
    {
        yield return CrossMapGolden();
        for (ulong seed = 1; seed <= 5; seed++) yield return Menu(seed);
        yield return FourSeats256();
        yield return SurroundBrawl();
        yield return CastsAndSandstorm();
        yield return StealthBesideDetector();
        yield return ConstructionAndQueue();
        yield return FelledTreeAndRazedBuilding();
        yield return DefeatedSeat();
    }

    /// <summary>By name, for the theory rows.</summary>
    internal static SaveScene Named(string name) => All().First(s => s.Name == name);

    /// <summary>The scene names, for <c>[MemberData]</c>.</summary>
    public static IEnumerable<object[]> Names() => All().Select(s => new object[] { s.Name });

    // ---------- the golden replay ----------

    /// <summary><see cref="CrossMapScenario"/> seed 1, built from <c>cross_map_seed1.replay</c>'s header and driven by its command log.</summary>
    internal static SaveScene CrossMapGolden()
    {
        Assert.Equal(ReplayError.None, ReplayFormat.TryReadFile(ReplayTestRun.GoldenPath, out Replay? replay));
        Replay r = replay!;
        return new SaveScene("cross_map_seed1", () =>
        {
            var seats = r.Seats.Select(s => new PlayerSeat(TestSim.Data.FindFaction(s.Faction), SeatController.Human, s.Difficulty)).ToImmutableArray();
            var config = new SimConfig(r.Seed, r.PlayerCount, r.UnitCapacity, r.CommandCapacity)
            {
                Data = TestSim.Data, Map = r.Map, ResourceCapacity = r.ResourceCapacity, Combat = r.Combat, Seats = seats,
            };
            return new Simulation(config);
        }, (sim, output) =>
        {
            // Between ticks TickNumber is t: the log's commands stamped t + 1, in log order.
            foreach (Command c in r.Commands)
                if (c.Tick == sim.TickNumber + 1) output.Add(c);
        });
    }

    // ---------- generated maps ----------

    /// <summary>The menu's match (SimRunner's capacities, the skirmish map: 128, 12 forests, 8 mines, 2 start locations), two human seats.</summary>
    internal static SaveScene Menu(ulong seed) => Generated($"menu_seed{seed}", seed, 2, 128, forests: 12, mines: 8);

    /// <summary>A 4-seat human-only match on a 256 map.</summary>
    internal static SaveScene FourSeats256() => Generated("four_seats_256", 7, 4, 256, forests: 24, mines: 16);

    /// <summary>
    /// A generated map with each player's start base placed by commands from <see cref="StartBase.Plan"/> (hall and five
    /// workers, as Match.cs does), plus a small start army per player (four line units, two ranged, a caster) beside it, and
    /// <see cref="MatchScript"/> driving every seat.
    /// </summary>
    internal static SaveScene Generated(string name, ulong seed, int players, int size, int forests, int mines)
    {
        var script = new MatchScript(players);
        return new SaveScene(name, () =>
        {
            var seats = Enumerable.Range(0, players).Select(p => new PlayerSeat(p % 2, SeatController.Human, AiDifficulty.Normal)).ToImmutableArray();
            var map = MapGenParams.Default with { Width = size, Height = size, Forests = forests, GoldMines = mines, StartLocations = players };
            var sim = new Simulation(new SimConfig(seed, players, 2000, 4096) { Data = TestSim.Data, Map = map, Seats = seats });
            PlaceBases(sim, army: true);
            return sim;
        }, script.Run);
    }

    /// <summary>Queues each player's start base (and with <paramref name="army"/> a start army) as commands for tick 1.</summary>
    internal static void PlaceBases(Simulation sim, bool army)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int players = w.Config.PlayerCount;
        float maxRadius = 0f;
        foreach (UnitDef d in w.Data.Units) maxRadius = Math.Max(maxRadius, d.Radius);
        var blocks = new Vector2[players][];
        var factions = new int[players];
        for (int p = 0; p < players; p++)
        {
            blocks[p] = Array.Empty<Vector2>();
            factions[p] = w.FactionOf(p);
        }
        StartBasePlan plan = StartBase.Plan(g, w.Data, w.Buildings, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, factions, blocks, 5, maxRadius);
        for (int p = 0; p < players; p++)
        {
            int anchor = plan.HallAnchor[p];
            Assert.True(anchor >= 0, $"no hall spot for player {p}");
            sim.Enqueue(Command.SpawnBuilding(p, plan.HallType[p], g.CellCenter(anchor % g.Width, anchor / g.Width)));
            foreach (Vector2 spot in plan.Workers[p]) sim.Enqueue(Command.SpawnUnit(p, plan.WorkerType[p], spot));
            if (!army) continue;
            Vector2 near = plan.Workers[p][0];
            UnitSlot[] slots = { UnitSlot.Line, UnitSlot.Line, UnitSlot.Line, UnitSlot.Line, UnitSlot.Ranged, UnitSlot.Ranged, UnitSlot.Caster };
            for (int k = 0; k < slots.Length; k++)
                sim.Enqueue(Command.SpawnUnit(p, StartBase.UnitOfSlot(w.Data, factions[p], slots[k]), near + new Vector2(0.9f * (k % 4) - 1.3f, 2.5f + 0.9f * (k / 4))));
        }
    }

    // ---------- flat scenes ----------

    private const int FlatSize = 64;

    /// <summary>A sim of <paramref name="data"/> on a flat 64 map, every cell explored (so any spot may be built on).</summary>
    private static Simulation Flat(GameData data, int players, bool combat = true, int units = 256) =>
        TestSim.Explored(new Simulation(new SimConfig(11, players, units, 1024) { Data = data, Combat = combat }, LocalMovementTests.Flat(FlatSize)));

    private static Vector2 M(float x, float y) => new(x, y);

    private static int Unit(GameData data, string key) => data.FindUnit(key);

    private static int Building(GameData data, string key) => data.FindBuilding(key);

    /// <summary>The handles of <paramref name="p"/>'s live units of <paramref name="type"/> (any type for -1), slot order.</summary>
    internal static List<EntityHandle> UnitsOf(World w, int p, int type = -1)
    {
        var list = new List<EntityHandle>();
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == p && (type < 0 || u.TypeId[i] == type)) list.Add(new EntityHandle(i, u.Generation[i]));
        return list;
    }

    /// <summary>The handle of <paramref name="p"/>'s first live building (any type for -1), or default.</summary>
    internal static EntityHandle FirstBuilding(World w, int p, int type = -1)
    {
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == p && (type < 0 || b.TypeId[k] == type)) return b.HandleOf(k);
        return default;
    }

    /// <summary>
    /// A brawl with a surround ring: player 1's 16 Raiders attack player 0's building (a finished House, rebuilt by a dev
    /// spawn whenever it falls) from all sides, so the surround rule hands out spots; player 0's Heavy Infantry fight back.
    /// </summary>
    internal static SaveScene SurroundBrawl()
    {
        GameData data = TestSim.Data;
        int house = Building(data, "malazan_billet"), raider = Unit(data, "whirlwind_raider"), infantry = Unit(data, "malazan_heavy_infantry");
        Vector2 site = M(60f, 60f);
        return new SaveScene("surround_brawl", () =>
        {
            Simulation sim = Flat(data, 2);
            sim.Enqueue(Command.SpawnBuilding(0, house, site));
            sim.Enqueue(Command.SpawnBuilding(1, Building(data, "whirlwind_raider_camp"), M(20f, 20f)));
            for (int k = 0; k < 16; k++)
            {
                float a = k * 0.3926991f; // 16 points round the house, 9 m out
                sim.Enqueue(Command.SpawnUnit(1, raider, site + M(2f, 2f) + 9f * new Vector2(MathF.Cos(a), MathF.Sin(a))));
            }
            for (int k = 0; k < 4; k++) sim.Enqueue(Command.SpawnUnit(0, infantry, M(46f + 1.2f * k, 72f)));
            return sim;
        }, (sim, output) =>
        {
            World w = sim.World;
            int t = sim.TickNumber;
            EntityHandle target = FirstBuilding(w, 0, house);
            if (target == default && t % 50 == 0) output.Add(Command.SpawnBuilding(0, house, site));
            if (target != default && t % 30 == 1)
                foreach (EntityHandle h in UnitsOf(w, 1, raider)) output.Add(Command.Attack(1, h, target, isBuilding: true));
            if (t % 300 == 150 && UnitsOf(w, 1, raider).Count < 12)
                for (int k = 0; k < 4; k++) output.Add(Command.SpawnUnit(1, raider, M(36f + k, 30f)));
            if (t % 400 == 200 && UnitsOf(w, 0, infantry).Count < 3)
                for (int k = 0; k < 2; k++) output.Add(Command.SpawnUnit(0, infantry, M(46f + 1.2f * k, 72f)));
        });
    }

    /// <summary>
    /// Casts that resolve after the save and a live Sandstorm: player 1's Priests cast at fixed ticks (a cast stands 1.2 s, a
    /// zone lasts 12 s), so at every save tick a cast is under way and (from tick 137 on) a zone is live; player 0's
    /// Laborers stand in the storms (a worker never scans, so nobody stops a cast); a Cadre Mage throws Telas Fire.
    /// </summary>
    internal static SaveScene CastsAndSandstorm()
    {
        GameData data = TestSim.Data;
        int priest = Unit(data, "whirlwind_priest"), laborer = Unit(data, "malazan_laborer"), mage = Unit(data, "malazan_cadre_mage");
        int[] castAt = { 0, 100, 127, 600, 900, 990, 1400, 1800, 2400 };
        return new SaveScene("casts_and_sandstorm", () =>
        {
            Simulation sim = Flat(data, 2, units: 128);
            // Straight into the store before tick 0, so the first cast is sent at tick 0 and is under way at the tick-1 save.
            for (int k = 0; k < 10; k++) CombatScenes.Place(sim, 1, priest, M(70f + 1.5f * (k % 5), 50f + 2f * (k / 5)));
            for (int k = 0; k < 8; k++) CombatScenes.Place(sim, 0, laborer, M(58f + 1.2f * (k % 4), 50f + 1.5f * (k / 4)));
            CombatScenes.Place(sim, 0, mage, M(50f, 52f));
            return sim;
        }, (sim, output) =>
        {
            World w = sim.World;
            int t = sim.TickNumber;
            if (Array.IndexOf(castAt, t) >= 0)
            {
                // The first priest (slot order) whose Sandstorm is ready casts 10 m west of itself: always in range.
                foreach (EntityHandle h in UnitsOf(w, 1, priest))
                    if (w.Units.CastAbility[h.Index] < 0 && w.Units.AbilityReadyTick[h.Index * DataLimits.MaxUnitAbilities] <= t)
                    {
                        output.Add(Command.UseAbility(1, h, 0, w.Units.Position[h.Index] - M(10f, 0f)));
                        break;
                    }
            }
            if (t % 120 == 30)
                foreach (EntityHandle h in UnitsOf(w, 0, mage))
                    if (UnitsOf(w, 1, priest).FirstOrDefault() is { } target && target != default)
                        output.Add(Command.UseAbility(0, h, 0, w.Units.Position[target.Index]));
            if (t % 200 == 50 && UnitsOf(w, 0, laborer).Count < 6)
                for (int k = 0; k < 3; k++) output.Add(Command.SpawnUnit(0, laborer, M(58f + 1.2f * k, 54f)));
            if (t % 200 == 60 && UnitsOf(w, 1, priest).Count < 6)
                for (int k = 0; k < 3; k++) output.Add(Command.SpawnUnit(1, priest, M(72f + 1.5f * k, 56f)));
            if (t % 200 == 70 && UnitsOf(w, 0, mage).Count == 0) output.Add(Command.SpawnUnit(0, mage, M(50f, 52f)));
        });
    }

    /// <summary>
    /// A stealthed unit beside a detector (the stealth test data: Heavy Infantry self-cast Stealthed; combat off, so the
    /// detection mask is the subject): a Priest (a 10 m detector) walks past the hidden infantry and back, a Watchtower stands
    /// near them, and the infantry re-cloak before their 60 s run out.
    /// </summary>
    internal static SaveScene StealthBesideDetector()
    {
        GameData data = Rts.Sim.Tests.StealthTests.Data;
        int infantry = Unit(data, "malazan_heavy_infantry"), priest = Unit(data, "whirlwind_priest"), raider = Unit(data, "whirlwind_raider");
        return new SaveScene("stealth_beside_detector", () =>
        {
            Simulation sim = Flat(data, 2, combat: false, units: 64);
            for (int k = 0; k < 3; k++) sim.Enqueue(Command.SpawnUnit(0, infantry, M(40f + 1.2f * k, 48f)));
            sim.Enqueue(Command.SpawnUnit(1, priest, M(46f, 50f)));
            sim.Enqueue(Command.SpawnUnit(1, raider, M(41f, 46f))); // within stealth.proximity (3 m) of the first
            sim.Enqueue(Command.SpawnBuilding(0, Building(data, "malazan_watchtower"), M(30f, 30f)));
            sim.Enqueue(Command.SpawnBuilding(1, Building(data, "whirlwind_raider_camp"), M(100f, 100f)));
            return sim;
        }, (sim, output) =>
        {
            World w = sim.World;
            int t = sim.TickNumber;
            if (t % 1100 == 0)
                foreach (EntityHandle h in UnitsOf(w, 0, infantry)) output.Add(Command.UseAbility(0, h, 0, w.Units.Position[h.Index]));
            if (t % 120 == 5)
                foreach (EntityHandle h in UnitsOf(w, 1, priest))
                    output.Add(Command.Move(1, h, t % 240 == 5 ? M(80f, 50f) : M(44f, 49f)));
            if (t % 300 == 77)
                foreach (EntityHandle h in UnitsOf(w, 0, infantry)) output.Add(Command.Move(0, h, w.Units.Position[h.Index] + M(t % 600 == 77 ? 4f : -4f, 0f)));
        });
    }

    /// <summary>
    /// A building under construction and a 5-deep queue holding a tech (rich data): player 0's Garrison Keep queues four
    /// Laborers and Age II at once (its two hall kinds stand finished), workers keep a construction site going, the Armory
    /// queues techs and the Barracks trains; player 1 does the same from its own base.
    /// </summary>
    internal static SaveScene ConstructionAndQueue()
    {
        GameData data = RichData;
        var script = new MatchScript(2);
        return new SaveScene("construction_and_queue", () =>
        {
            Simulation sim = Flat(data, 2, units: 256);
            for (int p = 0; p < 2; p++)
            {
                int faction = sim.World.FactionOf(p);
                float x0 = p == 0 ? 20f : 90f;
                sim.Enqueue(Command.SpawnBuilding(p, StartBase.BuildingOfSlot(data, faction, BuildingSlot.TownHall), M(x0, 60f)));
                sim.Enqueue(Command.SpawnBuilding(p, StartBase.BuildingOfSlot(data, faction, BuildingSlot.InfantryHall), M(x0, 40f)));
                sim.Enqueue(Command.SpawnBuilding(p, StartBase.BuildingOfSlot(data, faction, BuildingSlot.RangedHall), M(x0 + 12f, 40f)));
                sim.Enqueue(Command.SpawnBuilding(p, StartBase.BuildingOfSlot(data, faction, BuildingSlot.Forge), M(x0 + 12f, 76f)));
                int worker = StartBase.UnitOfSlot(data, faction, UnitSlot.Worker);
                for (int k = 0; k < 5; k++) sim.Enqueue(Command.SpawnUnit(p, worker, M(x0 + 1.2f * k, 72f)));
            }
            return sim;
        }, (sim, output) =>
        {
            World w = sim.World;
            int t = sim.TickNumber;
            if (t == 2 || t % 500 == 130)
                for (int p = 0; p < 2; p++)
                {
                    // Fill the hall's queue to five: four workers, then a tech when one is researchable there.
                    EntityHandle hall = FirstBuilding(w, p, StartBase.BuildingOfSlot(w.Data, w.FactionOf(p), BuildingSlot.TownHall));
                    if (hall == default) continue;
                    BuildingDef def = w.Data.Buildings[w.Buildings.TypeId[hall.Index]];
                    Vector2 at = StartBase.FootprintCenter(w.NavGrid, def, w.Buildings.Cell[hall.Index]);
                    int worker = StartBase.UnitOfSlot(w.Data, w.FactionOf(p), UnitSlot.Worker);
                    for (int k = w.Buildings.QueueCount[hall.Index]; k < 4; k++) output.Add(Command.Train(p, at, worker));
                    foreach (int tech in w.Data.TechsResearchableAt(w.Buildings.TypeId[hall.Index]))
                        output.Add(Command.Research(p, at, tech));
                }
            script.Run(sim, output);
        });
    }

    /// <summary>
    /// A felled tree and a razed building (rich data: 20 wood a tree): player 0's workers cut a forest beside its hall, and
    /// player 1's Raiders raze player 0's House, which a dev spawn rebuilds whenever it is gone.
    /// </summary>
    internal static SaveScene FelledTreeAndRazedBuilding()
    {
        GameData data = RichData;
        int house = Building(data, "malazan_billet"), raider = Unit(data, "whirlwind_raider"), laborer = Unit(data, "malazan_laborer");
        Vector2 houseAt = M(90f, 90f);
        return new SaveScene("felled_tree_and_razed_building", () =>
        {
            Simulation sim = Flat(data, 2, units: 128);
            World w = sim.World;
            int tree = data.FindResource("tree");
            for (int k = 0; k < 24; k++)
                Assert.True(w.Resources.Spawn(tree, (20 + k / 6) * FlatSize + 12 + k % 6, data.Rules.TreeWood, out _));
            sim.Enqueue(Command.SpawnBuilding(0, StartBase.BuildingOfSlot(data, 0, BuildingSlot.TownHall), M(24f, 52f)));
            sim.Enqueue(Command.SpawnBuilding(0, house, houseAt));
            sim.Enqueue(Command.SpawnBuilding(1, Building(data, "whirlwind_raider_camp"), M(110f, 20f)));
            for (int k = 0; k < 6; k++) sim.Enqueue(Command.SpawnUnit(0, laborer, M(30f + 1.1f * k, 50f)));
            for (int k = 0; k < 8; k++) sim.Enqueue(Command.SpawnUnit(1, raider, M(104f + 1.1f * (k % 4), 100f + 1.1f * (k / 4))));
            return sim;
        }, (sim, output) =>
        {
            World w = sim.World;
            int t = sim.TickNumber;
            if (t % 20 == 3)
                foreach (EntityHandle h in UnitsOf(w, 0, laborer))
                    if (w.Units.State[h.Index] == UnitState.Idle && NearestTree(w) is { } at) output.Add(Command.Gather(0, h, at));
            EntityHandle target = FirstBuilding(w, 0, house);
            if (target == default && t % 60 == 0) output.Add(Command.SpawnBuilding(0, house, houseAt));
            if (target != default && t % 40 == 9)
                foreach (EntityHandle h in UnitsOf(w, 1, raider)) output.Add(Command.Attack(1, h, target, isBuilding: true));
        });
    }

    private static Vector2? NearestTree(World w)
    {
        ResourceStore r = w.Resources;
        for (int k = 0; k < r.Capacity; k++)
            if (r.Alive[k]) return w.NavGrid.CellCenter(r.Cell[k] % w.NavGrid.Width, r.Cell[k] / w.NavGrid.Width);
        return null;
    }

    /// <summary>
    /// A seat already defeated (M5-4e): player 2 builds only a House and has no unit, so it is out on tick 1 and the House
    /// collapses; players 0 and 1 play on (<see cref="MatchScript"/>), so the match isn't over.
    /// </summary>
    internal static SaveScene DefeatedSeat()
    {
        GameData data = TestSim.Data;
        var script = new MatchScript(3);
        return new SaveScene("defeated_seat", () =>
        {
            Simulation sim = Flat(data, 3, units: 256);
            for (int p = 0; p < 2; p++)
            {
                int faction = sim.World.FactionOf(p);
                float x0 = p == 0 ? 24f : 96f;
                sim.Enqueue(Command.SpawnBuilding(p, StartBase.BuildingOfSlot(data, faction, BuildingSlot.TownHall), M(x0, 60f)));
                for (int k = 0; k < 5; k++) sim.Enqueue(Command.SpawnUnit(p, StartBase.UnitOfSlot(data, faction, UnitSlot.Worker), M(x0 + 1.2f * k, 70f)));
                for (int k = 0; k < 4; k++) sim.Enqueue(Command.SpawnUnit(p, StartBase.UnitOfSlot(data, faction, UnitSlot.Line), M(x0 + 1.2f * k, 76f)));
            }
            sim.Enqueue(Command.SpawnBuilding(2, StartBase.BuildingOfSlot(data, sim.World.FactionOf(2), BuildingSlot.House), M(60f, 110f)));
            return sim;
        }, script.Run);
    }
}
