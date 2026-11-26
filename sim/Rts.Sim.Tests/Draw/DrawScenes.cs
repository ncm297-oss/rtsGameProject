using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.Draw;

/// <summary>
/// M6-S13 test helpers: data copies with the draw rule's wait set (or the block taken out), and staged spent matches on a
/// flat map: every seat a finished unit-producing building in its own corner, no gold, no wood, no worker, and soldiers beside
/// it, so no seat can ever train again (docs/03 "Implementation (M6-S13)").
/// </summary>
internal static class DrawScenes
{
    /// <summary>The wait the staged scenes use, in seconds (5 s = 100 ticks).</summary>
    internal const double ShortSeconds = 5;

    /// <summary><see cref="ShortSeconds"/> in ticks.</summary>
    internal const int ShortTicks = 100;

    private static readonly Lazy<GameData> s_short = new(() => DataWith(ShortSeconds));
    private static readonly Lazy<GameData> s_off = new(() => DataWith(null));
    private static readonly Lazy<GameData> s_outOfReach = new(() => DataWith(DataLimits.MaxDrawSeconds));

    /// <summary>The shipped data with <c>draw.afterSeconds</c> <see cref="ShortSeconds"/> (cached).</summary>
    internal static GameData Short => s_short.Value;

    /// <summary>The shipped data with the <c>draw</c> block taken out: no draw rule (cached).</summary>
    internal static GameData Off => s_off.Value;

    /// <summary>The shipped data with the wait at the cap (a game day): a draw no test reaches (cached).</summary>
    internal static GameData OutOfReach => s_outOfReach.Value;

    /// <summary>
    /// Takes the <c>draw</c> block out of a data copy: for a test that rebuilds the data of a tree from before M6-S13 (the
    /// golden header of D32 / D33 / D34) from the shipped files, as M6-S10's rows took <c>gatherOverlap</c> back out.
    /// </summary>
    internal static void TakeOutDraw(TestDataDir dir) => dir.EditJson("common/rules.json", r => r.Remove("draw"));

    /// <summary>The shipped data without the draw block and with <paramref name="opening"/> as the Teblor's first opening: a pre-M6-S13 Teblor fixture's data.</summary>
    internal static GameData TeblorOpeningBeforeDraw(System.Text.Json.Nodes.JsonObject opening) => DataWith(null, dir =>
        dir.EditJson("factions/teblor/ai.json", root => root["openings"]![0] = System.Text.Json.Nodes.JsonNode.Parse(opening.ToJsonString())));

    /// <summary>The shipped data with <c>draw.afterSeconds</c> = <paramref name="seconds"/> (null: the block removed), edited further by <paramref name="also"/>.</summary>
    internal static GameData DataWith(double? seconds, Action<TestDataDir>? also = null) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("common/rules.json", r =>
        {
            if (seconds == null) r.Remove("draw");
            else r["draw"] = new System.Text.Json.Nodes.JsonObject { ["afterSeconds"] = seconds.Value };
        });
        also?.Invoke(dir);
    });

    /// <summary>
    /// A flat <paramref name="size"/>-cell sim on <paramref name="data"/> with <paramref name="players"/> human seats (faction p mod
    /// 2: Malazan, Whirlwind), teams from <paramref name="teams"/> (null: free-for-all). Nothing placed.
    /// </summary>
    internal static Simulation Empty(GameData data, int players, int size = 64, int[]? teams = null, int units = 128, ulong seed = 1)
    {
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Human, AiDifficulty.Normal, teams?[p] ?? 0);
        var config = new SimConfig(seed, players, units, 8 * units + 64) { Data = data, Seats = seats.ToImmutableArray() };
        return new Simulation(config, LocalMovementTests.Flat(size));
    }

    /// <summary>The corner cell seat <paramref name="p"/>'s base stands at (bit 0: high x, bit 1: high y), 8 cells in.</summary>
    internal static (int X, int Y) Corner(Simulation sim, int p)
    {
        int n = sim.World.NavGrid.Width;
        return ((p & 1) != 0 ? n - 14 : 8, (p & 2) != 0 ? n - 14 : 8);
    }

    /// <summary>A finished building of <paramref name="slot"/> for <paramref name="player"/> anchored at (x, y), straight into the store (a site with <paramref name="site"/>).</summary>
    internal static EntityHandle Building(Simulation sim, int player, int x, int y, BuildingSlot slot = BuildingSlot.InfantryHall, bool site = false)
    {
        int type = StartBase.BuildingOfSlot(sim.World.Data, sim.World.FactionOf(player), slot);
        Assert.True(sim.World.Buildings.Spawn(player, type, y * sim.World.NavGrid.Width + x, out EntityHandle h, site));
        return h;
    }

    /// <summary>A unit of <paramref name="slot"/> for <paramref name="player"/> at cell (x, y), straight into the store.</summary>
    internal static EntityHandle Unit(Simulation sim, int player, int x, int y, UnitSlot slot = UnitSlot.Line) =>
        CombatScenes.Place(sim, player, StartBase.UnitOfSlot(sim.World.Data, sim.World.FactionOf(player), slot), CombatScenes.At(sim, x, y));

    /// <summary>Every seat's gold and wood to 0.</summary>
    internal static void Broke(Simulation sim)
    {
        for (int p = 0; p < sim.World.Config.PlayerCount; p++)
        {
            sim.World.Ledger.Gold[p] = 0;
            sim.World.Ledger.Wood[p] = 0;
        }
    }

    /// <summary>
    /// A staged spent match: <see cref="Empty"/>, each seat an Infantry Hall in its corner and <paramref name="soldiers"/> line
    /// units beside it (holding, so nothing fights unless ordered), every bank 0. No gold node, no worker, no queue.
    /// </summary>
    internal static Simulation Spent(GameData data, int players = 2, int soldiers = 2, int size = 64, int[]? teams = null, ulong seed = 1)
    {
        Simulation sim = Empty(data, players, size, teams, seed: seed);
        for (int p = 0; p < players; p++)
        {
            (int x, int y) = Corner(sim, p);
            Building(sim, p, x, y);
            for (int k = 0; k < soldiers; k++)
            {
                EntityHandle u = Unit(sim, p, x + k % 3, y + 5 + k / 3);
                sim.World.Units.Hold[u.Index] = true;
            }
        }
        Broke(sim);
        return sim;
    }

    /// <summary>The id of the starting-resources preset with no gold and no wood that <see cref="ShortBroke"/> adds.</summary>
    internal const string BrokePreset = "zz_test_broke";

    private static readonly Lazy<GameData> s_shortBroke = new(() => DataWith(ShortSeconds, AddBrokePreset));

    /// <summary><see cref="Short"/> plus a starting-resources preset with nothing (<see cref="BrokePreset"/>), so a match built by commands alone (a replay) starts spent.</summary>
    internal static GameData ShortBroke => s_shortBroke.Value;

    /// <summary>Adds the <see cref="BrokePreset"/> preset to a data copy (rule 58: added once, replaced if a copy has it).</summary>
    internal static void AddBrokePreset(TestDataDir dir) => dir.EditJson("common/rules.json", r =>
    {
        var list = r["startingResources"]!.AsArray();
        for (int i = list.Count - 1; i >= 0; i--)
            if ((string?)list[i]!["id"] == BrokePreset) list.RemoveAt(i);
        list.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = BrokePreset, ["displayName"] = "Test", ["description"] = "Test only.", ["gold"] = 0, ["wood"] = 0 });
    });

    /// <summary>
    /// A spent match built by commands alone on seed <paramref name="seed"/>'s generated map (so a recorder attached first, with
    /// <paramref name="record"/>, carries all of it): every seat a Town Hall on its start and <paramref name="soldiers"/> line units
    /// on its worker spots, the <see cref="BrokePreset"/> preset, no worker. The map's mines hold gold nobody can mine.
    /// </summary>
    internal static Simulation SpentByCommands(GameData data, ulong seed, int players, int soldiers, bool record, out Replays.ReplayRecorder? recorder)
    {
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Human, AiDifficulty.Normal);
        var config = new SimConfig(seed, players, 256, 1024)
        {
            Data = data, Map = Map.MapGenParams.Default with { Forests = 8, GoldMines = 6 }, Seats = seats.ToImmutableArray(), StartingResources = BrokePreset,
        };
        var sim = new Simulation(config);
        recorder = record ? new Replays.ReplayRecorder(sim, checkpointInterval: 25, commandCapacity: 4096) : null;
        World w = sim.World;
        Map.NavGrid g = w.NavGrid;
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
            Assert.True(anchor >= 0, $"seed {seed}: no hall spot for player {p}");
            sim.Enqueue(Commands.Command.SpawnBuilding(p, plan.HallType[p], g.CellCenter(anchor % g.Width, anchor / g.Width)));
            int line = StartBase.UnitOfSlot(w.Data, factions[p], UnitSlot.Line);
            for (int k = 0; k < soldiers && k < plan.Workers[p].Length; k++) sim.Enqueue(Commands.Command.SpawnUnit(p, line, plan.Workers[p][k]));
        }
        return sim;
    }

    private static readonly Lazy<GameData> s_starved = new(() => DataWith(ShortSeconds, StarveTheMap));

    /// <summary>
    /// <see cref="Short"/> where nothing can ever be trained by a seat built as a match builds it (a Town Hall and five workers):
    /// the <c>standard</c> preset banks no gold and every generated mine holds 1 gold. For AI matches that draw.
    /// </summary>
    internal static GameData Starved => s_starved.Value;

    /// <summary>The <see cref="Starved"/> edit on a data copy.</summary>
    internal static void StarveTheMap(TestDataDir dir) => dir.EditJson("common/rules.json", r =>
    {
        foreach (System.Text.Json.Nodes.JsonNode? p in r["startingResources"]!.AsArray())
            if ((string?)p!["id"] == StartingResourcesDef.StandardId) p["gold"] = 0;
        r["startMines"]!["gold"] = 1;
        r["expansionMines"]!["gold"] = 1;
    });

    /// <summary>The first gold resource type of the data.</summary>
    internal static int GoldMineType(GameData data)
    {
        for (int t = 0; t < data.Resources.Length; t++)
            if (data.Resources[t].Resource == ResourceKind.Gold) return t;
        throw new InvalidOperationException("no gold resource");
    }

    /// <summary>A gold node holding <paramref name="gold"/> anchored at cell (x, y).</summary>
    internal static EntityHandle GoldNode(Simulation sim, int x, int y, int gold)
    {
        Assert.True(sim.World.Resources.Spawn(GoldMineType(sim.World.Data), y * sim.World.NavGrid.Width + x, gold, out EntityHandle h));
        return h;
    }

    /// <summary>The gold cost of the cheapest unit of <paramref name="player"/>'s faction (read from the data, not a literal).</summary>
    internal static int Cheapest(Simulation sim, int player)
    {
        World w = sim.World;
        int best = int.MaxValue;
        foreach (int t in w.Data.Factions[w.FactionOf(player)].Units) best = Math.Min(best, w.Data.Units[t].CostGold);
        return best;
    }

    /// <summary>Runs ticks until the match is over or <paramref name="max"/> ticks; returns the ticks run.</summary>
    internal static int RunToEnd(Simulation sim, int max) => CombatScenes.RunUntil(sim, () => sim.MatchOver, max);

    /// <summary>The position of building <paramref name="b"/>'s footprint centre (for Train / CancelTrain commands).</summary>
    internal static Vector2 Centre(Simulation sim, EntityHandle b)
    {
        BuildingStore s = sim.World.Buildings;
        BuildingDef d = sim.World.Data.Buildings[s.TypeId[b.Index]];
        int w = sim.World.NavGrid.Width, cell = s.Cell[b.Index];
        return new Vector2((cell % w + d.FootprintWidth * 0.5f) * Map.MapConstants.CellSize, (cell / w + d.FootprintHeight * 0.5f) * Map.MapConstants.CellSize);
    }
}
