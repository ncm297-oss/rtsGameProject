using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-2 test scenes for <see cref="AiPlayer"/>: a flat hand-made map with a start base per player (a finished Town Hall, five
/// workers, two gold mines and a forest beside it), explored, the seats as given; and a generated-map match whose bases
/// are placed through the command door (for replays). Nothing ticks while a scene is set up, so an AI's first think is tick 0's.
/// </summary>
internal static class AiScenes
{
    internal const int FlatSize = 64;

    /// <summary>The Town Hall anchor (x, y) of player <paramref name="p"/> on the flat scene (players 0-3: west, east, north, south).</summary>
    internal static (int X, int Y) HallAt(int p) => (p % 4) switch
    {
        0 => (8, 30),
        1 => (52, 30),
        2 => (30, 8),
        _ => (30, 52),
    };

    /// <summary>Seats: player p plays faction p mod 2, the given controllers and difficulty.</summary>
    internal static PlayerSeat[] Seats(AiDifficulty difficulty, params SeatController[] controllers) =>
        controllers.Select((c, p) => new PlayerSeat(p % 2, c, difficulty)).ToArray();

    /// <summary>The flat scene (see the type's summary) with <paramref name="data"/> (shipped by default).</summary>
    internal static Simulation Flat(PlayerSeat[] seats, GameData? data = null, int units = 256, int commands = 512, ulong seed = 3,
        int workers = 5, bool combat = false, bool expansionMine = false)
    {
        data ??= TestSim.Data;
        var config = new SimConfig(seed, seats.Length, units, commands) { Data = data, Seats = seats.ToImmutableArray(), Combat = combat };
        var sim = new Simulation(config, LocalMovementTests.Flat(FlatSize));
        World w = sim.World;
        int mine = data.FindResource("gold_mine"), tree = data.FindResource("tree");
        for (int p = 0; p < seats.Length; p++)
        {
            (int hx, int hy) = HallAt(p);
            int hall = StartBase.BuildingOfSlot(data, seats[p].Faction, BuildingSlot.TownHall);
            Assert.True(w.Buildings.Spawn(p, hall, hy * FlatSize + hx, out _));
            // Two mines above the hall, a forest below it, toward the map's middle.
            bool vertical = p >= 2;
            int toward = p % 2 == 0 ? 1 : -1;
            for (int m = 0; m < 2; m++)
            {
                int mx = vertical ? hx - 2 + 6 * m : hx + toward * 2 + 3 * m * toward, my = vertical ? hy + toward * 7 : hy - 6;
                Assert.True(w.Resources.Spawn(mine, my * FlatSize + mx, data.Rules.StartMineGold, out _));
            }
            for (int t = 0; t < 12; t++)
            {
                int tx = vertical ? hx + 8 + t % 4 : hx + t % 4, ty = vertical ? hy + t / 4 : hy + 9 + t / 4;
                Assert.True(w.Resources.Spawn(tree, ty * FlatSize + tx, data.Rules.TreeWood, out _));
            }
            int worker = StartBase.UnitOfSlot(data, seats[p].Faction, UnitSlot.Worker);
            for (int k = 0; k < workers; k++)
                CombatScenes.Place(sim, p, worker, w.NavGrid.CellCenter(hx + k % 4, hy + 5 + k / 4));
        }
        // An expansion: a mine in the middle, more than 20 cells (the shipped expandHallReach, 40 m) from every hall.
        if (expansionMine) Assert.True(w.Resources.Spawn(mine, 30 * FlatSize + 31, data.Rules.ExpansionMineGold, out _));
        return TestSim.Explored(sim);
    }

    /// <summary>The shipped data with player faction <paramref name="faction"/>'s ai.json edited by <paramref name="edit"/> (a temp copy, loaded once per call).</summary>
    internal static GameData DataWithAi(string faction, Action<JsonNode> edit) =>
        DataWith(dir => dir.EditJson($"factions/{faction}/ai.json", root => edit(root)));

    /// <summary>The shipped data edited by <paramref name="edit"/> in a temp copy (M5-3: ai.json, rules.json and units.json together).</summary>
    internal static GameData DataWith(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        edit(dir);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Data != null, string.Join("; ", r.Errors));
        return r.Data!;
    }

    /// <summary>
    /// M5-3: data whose AIs (both factions) wave early: <paramref name="minArmyPop"/> pop and no cost, so two AI seats fight
    /// within a short run.
    /// </summary>
    internal static GameData EarlyWaves(double minArmyPop = 2) => DataWith(dir =>
    {
        foreach (string f in new[] { "malazan", "whirlwind" })
            dir.EditJson($"factions/{f}/ai.json", root => root["attack"] = new JsonObject { ["minArmyPop"] = minArmyPop, ["minArmyCost"] = 0 });
    });

    /// <summary>A build order as ai.json writes it, from (trigger, value, do, id, count) rows (count 0: none written).</summary>
    internal static JsonArray BuildOrder(params (string Trigger, double At, string Do, string? Id, int Count)[] steps)
    {
        var a = new JsonArray();
        foreach ((string trigger, double at, string what, string? id, int count) in steps)
        {
            var step = new JsonObject { ["at"] = new JsonObject { [trigger] = at }, ["do"] = what };
            if (id != null) step["id"] = id;
            if (count > 0) step["count"] = count;
            a.Add(step);
        }
        return a;
    }

    /// <summary>A live log of the commands player <paramref name="player"/>'s AI has had accepted, stamped with the tick each applies in (the AI's test seam).</summary>
    internal static List<Command> Log(Simulation sim, int player)
    {
        var list = new List<Command>();
        sim.AiOf(player)!.SentForTests = c => list.Add(c);
        return list;
    }

    /// <summary>The commands a recorder logged for <paramref name="player"/>, in log order.</summary>
    internal static List<Command> CommandsOf(ReplayRecorder rec, int player)
    {
        var list = new List<Command>();
        for (int i = 0; i < rec.CommandCount; i++)
            if (rec.CommandAt(i).Player == player) list.Add(rec.CommandAt(i));
        return list;
    }

    /// <summary>
    /// A generated-map match recordable as a replay: <paramref name="seats"/>, the map with forests and mines, each player's
    /// Town Hall and five workers placed by commands (as <c>Match.cs</c> does) from <c>StartBase.Plan</c>. A recorder is
    /// attached first when <paramref name="record"/>. M5-3: <paramref name="forests"/> on the map; <paramref name="raiders"/>
    /// line units of each player spawned in the next player's base.
    /// </summary>
    internal static Simulation Generated(PlayerSeat[] seats, ulong seed, bool record, out ReplayRecorder? recorder, int units = 400, int commands = 1024,
        GameData? data = null, int forests = 8, int raiders = 0)
    {
        var map = MapGenParams.Default with { Forests = forests, GoldMines = 6 };
        var config = new SimConfig(seed, seats.Length, units, commands) { Data = data ?? TestSim.Data, Map = map, Seats = seats.ToImmutableArray() };
        var sim = new Simulation(config);
        recorder = record ? new ReplayRecorder(sim, checkpointInterval: 100, commandCapacity: 16_384) : null;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        float maxRadius = 0f;
        foreach (UnitDef d in w.Data.Units) maxRadius = Math.Max(maxRadius, d.Radius);
        // M5-4a: each base on its start location (two mines and a forest beside it), so no start block is needed.
        var blocks = new Vector2[seats.Length][];
        for (int p = 0; p < seats.Length; p++) blocks[p] = Array.Empty<Vector2>();
        var factions = new int[seats.Length];
        for (int p = 0; p < seats.Length; p++) factions[p] = w.FactionOf(p);
        StartBasePlan plan = StartBase.Plan(g, w.Data, w.Buildings, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, factions, blocks, 5, maxRadius);
        for (int p = 0; p < seats.Length; p++)
        {
            int anchor = plan.HallAnchor[p];
            Assert.True(anchor >= 0, $"seed {seed}: no hall spot for player {p}");
            sim.Enqueue(Command.SpawnBuilding(p, plan.HallType[p], g.CellCenter(anchor % g.Width, anchor / g.Width)));
            foreach (Vector2 spot in plan.Workers[p]) sim.Enqueue(Command.SpawnUnit(p, plan.WorkerType[p], spot));
        }
        // M5-3: raiders, each player's line units among the next player's workers, so the AIs see each other's base and fight
        // from the start (their waves, retreats and the fighting go through the recorded commands).
        for (int p = 0; p < seats.Length && raiders > 0; p++)
        {
            int line = StartBase.UnitOfSlot(w.Data, factions[p], UnitSlot.Line);
            Vector2[] spots = plan.Workers[(p + 1) % seats.Length];
            for (int k = 0; k < raiders; k++)
                sim.Enqueue(Command.SpawnUnit(p, line, spots[k % spots.Length] + new Vector2(0.4f * (k / spots.Length), 0.3f)));
        }
        return sim;
    }
}
