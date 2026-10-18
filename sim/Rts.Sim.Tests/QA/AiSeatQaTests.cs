using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M5-2, session 2026-10-11-0634): attacks on the AI seat. The AI reads no hidden state (a twin world that differs only in
/// what player 0 can't see answers every view query and every <see cref="PlayerView.CanPlace"/> alike; the oracle with AI seats
/// thinking); its commands go through the same door and drop the same way as a human's; impossible build orders never stall
/// or spam; the economy never re-sends a worker forever to a node the player remembers but is gone (BUG-0570); format-5 headers
/// at the player bound.
/// </summary>
public class AiSeatQaTests
{
    private readonly ITestOutputHelper _out;

    public AiSeatQaTests(ITestOutputHelper output) => _out = output;

    private static int Unit(string key) => TestSim.Data.FindUnit(key);

    private static int Building(string key) => TestSim.Data.FindBuilding(key);

    /// <summary>An ai.json for malazan whose only step can't fire for an hour, so a scene's commands are the economy's.</summary>
    private static readonly Lazy<GameData> s_quietOrder = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1))));

    // ---------- BUG-0570: the economy and a remembered node that is gone ----------

    /// <summary>
    /// BUG-0570. Player 0 (AI, Hard) has seen every node (the explore seam). Its own two mines are mined out in its sight (its
    /// memory drops them); player 1's two mines are mined out out of its sight (its memory keeps them, <c>Remaining</c> -1,
    /// BUG-0540's rule). The gold share sends idle workers to gold: the nearest known gold is a remembered mine that is gone,
    /// the Gather finds no node where it lands and is dropped, the worker stays Idle, and the next think sends it again. A
    /// worker must not be sent to the same place over and over (40 thinks here): it should go to wood (trees are known and in
    /// sight), or at least walk there and learn the mine is gone.
    /// </summary>
    [Fact]
    public void IdleWorkers_AreNotSentForever_ToARememberedMineThatIsGone()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quietOrder.Value);
        World w = sim.World;
        ResourceStore r = w.Resources;
        int mine = w.Data.FindResource("gold_mine");
        var farMines = new List<int>();
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k] || r.TypeId[k] != mine) continue;
            if (r.Cell[k] % AiScenes.FlatSize >= AiScenes.FlatSize / 2) farMines.Add(k);
            r.Take(new EntityHandle(k, r.Generation[k]), int.MaxValue);
        }
        Assert.Equal(2, farMines.Count);
        sim.Tick(); // tick 0: a fog update, then the first think
        var view = new PlayerView(w, 0);
        foreach (int k in farMines)
        {
            Assert.True(view.TryGetResource(k, out SeenResource remembered), "precondition: the far mine is remembered");
            Assert.Equal(-1, remembered.Remaining);
        }
        // Every gold mine the player still knows is gone (the near ones may be dropped or remembered, by its sight).
        int known = 0, s = -1;
        while (view.NextResource(ref s, out SeenResource n))
            if (n.TypeId == mine)
            {
                known++;
                Assert.False(r.Alive[s], $"precondition: mine slot {s} is gone");
            }
        Assert.True(known >= 2);

        List<Command> log = AiScenes.Log(sim, 0);
        AiPlayer ai = sim.AiOf(0)!;
        int think = ai.ThinkTicks;
        for (int t = 0; t < 40 * think; t++) sim.Tick();
        var perWorker = log.Where(c => c.Kind == CommandKind.Gather).GroupBy(c => c.Unit).Select(g => g.Count()).ToList();
        int worst = perWorker.Count == 0 ? 0 : perWorker.Max();
        int idle = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Units.State[i] == UnitState.Idle) idle++;
        _out.WriteLine($"{log.Count(c => c.Kind == CommandKind.Gather)} Gathers over {ai.Thinks} thinks; most to one worker {worst}; idle workers at the end {idle}; wood {w.Wood[0]}");
        Assert.True(worst <= 3, $"a worker was sent {worst} Gathers in 40 thinks (dropped each time: the mine is gone)");
        Assert.Equal(0, idle);
    }

    // ---------- no hidden state: a twin that differs only where player 0 can't see ----------

    /// <summary>
    /// Two flat scenes, the same but for what player 0 can't see: in B, player 1 has a barracks and a site built in the
    /// unseen middle-south, a raider standing there, and a tree there felled. After a fog update player 0's view answers
    /// alike in both: every resource and building answer, and <see cref="PlayerView.CanPlace"/> (pass / fail and reason) for
    /// every anchor of every type of its faction. What the AI may base a command on leaks nothing.
    /// </summary>
    [Fact]
    public void TwinWorlds_DifferingOnlyOutOfSight_AnswerPlayer0Alike_CanPlaceIncluded()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Human, SeatController.Human);
        Simulation a = AiScenes.Flat(seats);
        Simulation b = AiScenes.Flat(seats);
        BuildMaps.SetTotals(a, 0, 5000, 5000);
        BuildMaps.SetTotals(b, 0, 5000, 5000);
        a.Tick();
        b.Tick();
        // The hidden changes, south-middle (player 0's hall is at (8, 30), its sight far from x 30-44, y 48-60).
        World wb = b.World;
        Assert.True(wb.Buildings.Spawn(1, Building("whirlwind_raider_camp"), 52 * AiScenes.FlatSize + 36, out _));
        Assert.True(wb.Buildings.Spawn(1, FirstOfFaction(1), 56 * AiScenes.FlatSize + 42, out _, site: true));
        CombatScenes.Place(b, 1, Unit("whirlwind_raider"), wb.NavGrid.CellCenter(33, 50));
        int tree = wb.Data.FindResource("tree"), felled = -1;
        for (int k = 0; k < wb.Resources.Capacity && felled < 0; k++)
            if (wb.Resources.Alive[k] && wb.Resources.TypeId[k] == tree && wb.Resources.Cell[k] % AiScenes.FlatSize > 36) felled = k;
        Assert.True(felled >= 0);
        wb.Resources.Take(new EntityHandle(felled, wb.Resources.Generation[felled]), int.MaxValue);
        for (int t = 0; t < 4; t++)
        {
            a.Tick();
            b.Tick();
        }
        var va = new PlayerView(a.World, 0);
        var vb = new PlayerView(b.World, 0);
        Assert.False(vb.IsVisible(52 * AiScenes.FlatSize + 36), "precondition: the hidden barracks is out of player 0's sight");
        // Resources: the felled tree is still answered in B (remembered), with the same record.
        for (int k = 0; k < a.World.Resources.Capacity; k++)
        {
            bool ka = va.TryGetResource(k, out SeenResource ra), kb = vb.TryGetResource(k, out SeenResource rb);
            Assert.True(ka == kb && ra == rb, $"resource slot {k}: {ka} {ra} v {kb} {rb}");
        }
        // Buildings: what player 0 sees of slot order and records.
        var ba = new List<SeenBuilding>();
        var bb = new List<SeenBuilding>();
        int s = -1;
        while (va.NextBuilding(ref s, out SeenBuilding x)) ba.Add(x);
        s = -1;
        while (vb.NextBuilding(ref s, out SeenBuilding x)) bb.Add(x);
        Assert.Equal(ba, bb);
        // Placement, every anchor, every type of the player's faction (and one of the other's).
        int differ = 0, checkedCount = 0;
        string first = "";
        foreach (BuildingDef d in a.World.Data.Buildings)
        {
            if (d.Faction != a.World.FactionOf(0) && d.Id != FirstOfFaction(1)) continue;
            for (int c = 0; c < AiScenes.FlatSize * AiScenes.FlatSize; c++)
            {
                bool pa = va.CanPlace(d.Id, c, out PlacementError ea), pb = vb.CanPlace(d.Id, c, out PlacementError eb);
                checkedCount++;
                if (pa == pb && ea == eb) continue;
                differ++;
                if (first.Length == 0) first = $"{d.Key} at ({c % AiScenes.FlatSize}, {c / AiScenes.FlatSize}): {ea} v {eb}";
            }
        }
        _out.WriteLine($"{checkedCount} CanPlace queries, {differ} differ{(first.Length > 0 ? ", first " + first : "")}");
        Assert.True(differ == 0, $"CanPlace reveals hidden state: {differ} answers differ, first {first}");
    }

    private static int FirstOfFaction(int faction)
    {
        foreach (BuildingDef d in TestSim.Data.Buildings)
            if (d.Faction == faction && d.Slot == BuildingSlot.House) return d.Id;
        throw new InvalidOperationException("no house");
    }

    // ---------- the same door ----------

    /// <summary>
    /// A Train at an enemy's building and a Build on ground player 0 never explored, sent through the AI's door
    /// (<see cref="ICommandSink.TryEnqueue"/>) in one twin and through <see cref="Simulation.Enqueue"/> in the other: both
    /// accepted at the door, both dropped when they apply (no queue item at the enemy, no site), the twins' hashes equal every tick.
    /// </summary>
    [Fact]
    public void AiDoorAndHumanDoor_DropAnEnemyTrainAndAnUnexploredBuild_Alike()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Human, SeatController.Human);
        Simulation human = AiScenes.Generated(seats, 11, record: false, out _);
        Simulation door = AiScenes.Generated(seats, 11, record: false, out _);
        for (int t = 0; t < 4; t++)
        {
            human.Tick();
            door.Tick();
        }
        World w = human.World;
        NavGrid g = w.NavGrid;
        int enemyHall = -1;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 1) enemyHall = k;
        Assert.True(enemyHall >= 0);
        BuildingDef hd = w.Data.Buildings[w.Buildings.TypeId[enemyHall]];
        int hc = w.Buildings.Cell[enemyHall];
        Vector2 hallAt = new((hc % g.Width + hd.FootprintWidth * 0.5f) * MapConstants.CellSize, (hc / g.Width + hd.FootprintHeight * 0.5f) * MapConstants.CellSize);
        int worker = -1;
        for (int i = 0; i < w.Units.Capacity && worker < 0; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0) worker = i;
        int unexplored = -1;
        int barracks = Building("malazan_barracks");
        BuildingDef bd = w.Data.Buildings[barracks];
        for (int c = 0; c < g.Width * g.Height && unexplored < 0; c++)
        {
            int x = c % g.Width, y = c / g.Width;
            if (x + bd.FootprintWidth >= g.Width || y + bd.FootprintHeight >= g.Height || w.Fog.IsExplored(0, c)) continue;
            bool open = true;
            for (int dy = 0; dy < bd.FootprintHeight && open; dy++)
                for (int dx = 0; dx < bd.FootprintWidth && open; dx++)
                    open = g.IsPassable(x + dx, y + dy) && !w.Fog.IsExplored(0, (y + dy) * g.Width + x + dx);
            if (open) unexplored = c;
        }
        Assert.True(unexplored >= 0);
        BuildMaps.SetTotals(human, 0, 5000, 5000);
        BuildMaps.SetTotals(door, 0, 5000, 5000);
        var handle = new EntityHandle(worker, w.Units.Generation[worker]);
        Command train = Command.Train(0, hallAt, Unit("malazan_laborer"));
        Command build = Command.Build(0, handle, barracks, g.CellCenter(unexplored % g.Width, unexplored / g.Width));
        human.Enqueue(train);
        human.Enqueue(build);
        ICommandSink sink = door;
        Assert.True(sink.TryEnqueue(train));
        Assert.True(sink.TryEnqueue(build));
        int buildingsBefore = w.Buildings.Count;
        for (int t = 0; t < 40; t++)
        {
            human.Tick();
            door.Tick();
            Assert.True(human.StateHash() == door.StateHash(), $"the doors differ after tick {human.TickNumber}");
        }
        Assert.Equal(0, w.Buildings.QueueCount[enemyHall]);
        Assert.Equal(buildingsBefore, w.Buildings.Count);
        Assert.Equal(5000, w.Gold[0]);
    }

    /// <summary>The AI's door refuses quietly what <see cref="Simulation.Enqueue"/> refuses loudly: a full queue, an unknown player, a malformed command; nothing queued, no exception.</summary>
    [Fact]
    public void AiDoor_RefusesFullUnknownAndMalformed_WithoutThrowing()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Human, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, commands: 4);
        ICommandSink sink = sim;
        Assert.False(sink.TryEnqueue(Command.Train(2, Vector2.One, 0)));
        Assert.False(sink.TryEnqueue(Command.Train(-1, Vector2.One, 0)));
        Assert.False(sink.TryEnqueue(new Command { Kind = (CommandKind)99, Player = 0 }));
        Assert.Equal(0, sim.PendingCommandCount);
        for (int k = 0; k < 4; k++) Assert.True(sink.TryEnqueue(Command.Train(0, Vector2.One, 0)));
        Assert.False(sink.TryEnqueue(Command.Train(0, Vector2.One, 0)));
        Assert.Equal(4, sim.PendingCommandCount);
        sim.Tick();
    }

    // ---------- impossible build orders ----------

    /// <summary>
    /// Every step impossible at once: a train of a unit whose building the player never has (the corral's lancer), a research
    /// of a tech whose building it never has, a build of a type whose requirement is never met, an expand (a free mine is known:
    /// player 1's, by the explore seam) and a house with no wood to pay for either. Over 100 thinks (Hard) none of those
    /// steps sends a single command, nothing throws, and every think ran.
    /// </summary>
    [Fact]
    public void ImpossibleSteps_SendNothing_AndNeverStall()
    {
        GameData data = AiScenes.DataWithAi("malazan", root =>
        {
            AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(
                ("time", 0, "train", "malazan_wickan_lancer", 5),
                ("time", 0, "research", "armor_1", 0),
                ("time", 0, "build", RequiringBuilding(), 0),
                ("time", 0, "expand", null, 0),
                ("time", 0, "build", "malazan_billet", 0));
            foreach (string d in new[] { "easy", "normal", "hard" }) root["difficulties"]![d]!["expandAfter"] = 0;
        });
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, data);
        BuildMaps.SetTotals(sim, 0, 100_000, 0); // no wood: every building costs some
        List<Command> log = AiScenes.Log(sim, 0);
        AiPlayer ai = sim.AiOf(0)!;
        int think = ai.ThinkTicks;
        for (int t = 0; t < 100 * think; t++) sim.Tick();
        Assert.Equal(100, ai.Thinks);
        var bad = log.Where(c => c.Kind is CommandKind.Train or CommandKind.Research or CommandKind.Build).ToList();
        foreach (Command c in bad.Take(5)) _out.WriteLine($"{c.Kind} {c.TypeId} at tick {c.Tick} {c.Position}");
        Assert.Empty(bad);
    }

    /// <summary>A malazan building with a requirement (a tech or another building), for the impossible-steps scene.</summary>
    private static string RequiringBuilding()
    {
        foreach (BuildingDef d in TestSim.Data.Buildings)
            if (d.Faction == TestSim.Data.FindFaction("malazan") && (d.RequiresTechs.Length > 0 || d.RequiresBuildings.Length > 0)) return d.Key;
        throw new InvalidOperationException("no malazan building with a requirement");
    }

    // ---------- format-5 headers at the player bound ----------

    /// <summary>A recorded match of 16 players (the bound), seats mixed human and AI of every difficulty, writes 16 seat lines, reads back equal and byte for byte; 17 is refused before any seat line is read.</summary>
    [Fact]
    public void SixteenSeats_RoundTrip_SeventeenRefused()
    {
        Assert.Equal(16, Replay.MaxPlayers);
        var seats = new PlayerSeat[16];
        for (int p = 0; p < 16; p++) seats[p] = new PlayerSeat(p % 2, p % 3 == 0 ? SeatController.Ai : SeatController.Human, (AiDifficulty)(p % 3));
        var config = new SimConfig(9, 16, 64, 256) { Data = TestSim.Data, Seats = seats.ToImmutableArray() };
        var sim = new Simulation(config);
        var rec = new ReplayRecorder(sim, checkpointInterval: 10);
        for (int t = 0; t < 40; t++) sim.Tick();
        Replay r = rec.ToReplay();
        Assert.Equal(16, r.Seats.Length);
        byte[] bytes = ReplayFormat.Write(r);
        string text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Equal(16, text.Split('\n').Count(l => l.StartsWith("seat ", StringComparison.Ordinal)));
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? back));
        Assert.Equal(bytes, ReplayFormat.Write(back!));
        Assert.True(ReplayPlayer.Run(back!, TestSim.Data).Ok);
        // 17 players with 17 seat lines: refused at the count.
        string seventeen = text.Replace("players 16\n", "players 17\n").Replace("seat 15 ", "seat 16 malazan human normal\nseat 15 ");
        Assert.NotEqual(ReplayError.None, ReplayFormat.TryRead(ReplayQaReseal(seventeen), out _));
    }

    /// <summary>Re-seals edited replay text with a fresh checksum line, so only the edit is judged.</summary>
    private static byte[] ReplayQaReseal(string text) => ReplayFormat.Seal(text[..text.LastIndexOf("checksum ", StringComparison.Ordinal)]);

    /// <summary>A seat naming faction "99" (not a snake_case id) is malformed; an AI seat whose difficulty is missing is malformed.</summary>
    [Theory]
    [InlineData("seat 1 whirlwind ai ", "seat 1 99 ai ")]
    [InlineData("seat 1 whirlwind ai normal\n", "seat 1 whirlwind ai\n")]
    [InlineData("seat 1 whirlwind ai normal\n", "seat 1 whirlwind ai \n")]
    public void HostileSeatLines_AreRefused(string find, string replace)
    {
        PlayerSeat[] seats = { new(0, SeatController.Human, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        var sim = new Simulation(new SimConfig(2, 2, 32, 64) { Data = TestSim.Data, Seats = seats.ToImmutableArray() });
        var rec = new ReplayRecorder(sim, checkpointInterval: 10);
        for (int t = 0; t < 20; t++) sim.Tick();
        string text = System.Text.Encoding.ASCII.GetString(ReplayFormat.Write(rec.ToReplay()));
        Assert.Contains(find, text);
        Assert.Equal(ReplayError.Malformed, ReplayFormat.TryRead(ReplayQaReseal(text.Replace(find, replace)), out _));
    }
}
