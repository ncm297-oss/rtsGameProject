using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Draw;
using Rts.Sim.Tests.QA;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// QA, M6-S15 (session 2026-11-27-1857, QA focus 1, 2, 3, 6 and 8): the draw rule's drop-off clause from both sides on shipped
/// prices, an independent fuzz of the clause (QA's own scan, not <see cref="DrawReachScan.DropOffClause"/>), the computer
/// player's small drop-off (A2) on the three shipped maps' bases, with no node known and for two seats of one team, the shapes
/// of "spends what it has" (C) the brief does not name, and the allocation of an AI think on the new paths.
/// </summary>
[Collection(SerialCollection.Name)] // the 0-byte think rows measure allocation (SerialCollectionTests)
public class M6S15QaTests
{
    private readonly ITestOutputHelper _out;

    public M6S15QaTests(ITestOutputHelper output) => _out = output;

    private static int Tree(GameData d) => d.FindResource("tree");

    private static void Trees(Simulation sim, int x, int y, int wood)
    {
        // One tree per 10 wood (at most), ten to a row, so the map's wood is exact.
        int tree = Tree(sim.World.Data), k = 0;
        while (wood > 0)
        {
            int take = Math.Min(wood, 10);
            Assert.True(sim.World.Resources.Spawn(tree, (y + k / 10) * sim.World.NavGrid.Width + x + k % 10, take, out _));
            wood -= take;
            k++;
        }
    }

    // ---------------- QA focus 1: the clause from both sides ----------------

    /// <summary>The bank pays the Depot, but what is left pays no unit and the map holds no gold: spent, and the match draws.</summary>
    [Fact]
    public void Clause_TheDepotPaid_NothingLeftForAUnit_NoGoldOnTheMap_IsSpentAndDraws()
    {
        Simulation sim = DropOffClauseTests.ClauseScene(100, out _, node: false);
        World w = sim.World;
        Trees(sim, 30, 40, 500); // wood on the map, gold nowhere: no Malazan unit is free of gold
        Assert.Equal(0, w.Resources.GoldLeft);
        Assert.False(w.CanStillTrain(0), DrawReachScan.Line(w, 0));
        Assert.True(w.MatchSpent);
        int ran = DrawScenes.RunToEnd(sim, w.DrawWaitTicks + 50);
        Assert.True(w.MatchDrawn, $"not drawn after {ran} ticks; clock {w.DrawClock}");
    }

    /// <summary>
    /// The drop-off's price is paid with wood the unit also needs (Teblor, shipped prices): 69 gold / 75 wood pays no unit by
    /// itself (the Javelin Thrower is 70 / 50, the Gatherer 75 / 0); it buys the Woodcutters' Lodge and leaves no wood, so the
    /// Javelin then needs 50 wood from the map and the Gatherer 6 gold. A gold mine of 5 and 49 wood on the map is spent (it
    /// would not be if the Lodge's wood were not taken off the bank); 50 wood, or a mine of 6, is not.
    /// </summary>
    [Theory]
    [InlineData(5, 49, false)]
    [InlineData(5, 50, true)]
    [InlineData(6, 49, true)]
    public void Clause_Teblor_TheLodgeTakesTheWoodTheJavelinNeeds_TheMapDecides(int mapGold, int mapWood, bool can)
    {
        int teblor = DrawScenes.Short.FindFaction("teblor");
        Simulation sim = DropOffClauseTests.Scene(teblor, null, out _, node: false);
        World w = sim.World;
        BuildingDef lodge = DropOffClauseTests.SmallDropOff(w.Data, teblor);
        UnitDef javelin = w.Data.Units[w.Data.FindUnit("teblor_javelin_thrower")];
        Trees(sim, 30, 40, mapWood);
        DrawScenes.GoldNode(sim, 34, 20, mapGold);
        w.Ledger.Gold[0] = javelin.CostGold - 1; // 69
        w.Ledger.Wood[0] = lodge.CostWood;
        Assert.Equal(mapWood, w.Resources.WoodLeft);
        Assert.Equal(mapGold, w.Resources.GoldLeft);
        Assert.Equal(can, w.CanStillTrain(0));
        Assert.Equal(!can, w.MatchSpent);
    }

    /// <summary>Another seat's (an ally's) finished drop-off does not stand in for the seat's own: the clause still decides.</summary>
    [Fact]
    public void Clause_AnAllysDropOff_IsNotTheSeatsOwn()
    {
        var seats = System.Collections.Immutable.ImmutableArray.Create(
            new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 1),
            new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 2),
            new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 1));
        var sim = new Simulation(new SimConfig(1, 3, 128, 1088) { Data = DrawScenes.Short, Seats = seats }, LocalMovementTests.Flat(64));
        World w = sim.World;
        DrawScenes.Building(sim, 0, 8, 8);
        DrawScenes.Building(sim, 1, 44, 44);
        DrawScenes.Unit(sim, 1, 44, 50);
        DrawScenes.Building(sim, 2, 8, 30, BuildingSlot.TownHall); // the ally's Keep, a drop-off
        DrawScenes.GoldNode(sim, 34, 30, 2000);
        DrawScenes.Unit(sim, 0, 20, 14, UnitSlot.Worker);
        DrawScenes.Broke(sim);
        Assert.Equal(0, w.Ledger.FinishedDropOffs[0]);
        w.Ledger.Gold[0] = 35;
        w.Ledger.Wood[0] = 74;
        Assert.False(w.CanStillTrain(0), "the ally's Keep is no drop-off of seat 0: one wood short of the Depot");
        w.Ledger.Wood[0] = 75;
        Assert.True(w.CanStillTrain(0));
    }

    // ---------------- QA focus 2: QA's own scan of the clause over a fuzz biased to it ----------------

    /// <summary>QA's scan of the clause, straight from the stores (written apart from the developer's).</summary>
    private static bool QaClause(World w, int p)
    {
        GameData d = w.Data;
        int workers = 0, dropOffs = 0;
        long carriedGold = 0, carriedWood = 0, mapGold = 0, mapWood = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            if (!w.Units.Alive[i] || w.Units.Owner[i] != p) continue;
            if (d.Units[w.Units.TypeId[i]].IsWorker) workers++;
            if (w.Units.CargoKind[i] == ResourceKind.Gold) carriedGold += w.Units.Cargo[i];
            else carriedWood += w.Units.Cargo[i];
        }
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == p && !w.Buildings.UnderConstruction[k] && d.Buildings[w.Buildings.TypeId[k]].DropOff) dropOffs++;
        for (int n = 0; n < w.Resources.Capacity; n++)
        {
            if (!w.Resources.Alive[n]) continue;
            if (d.Resources[w.Resources.TypeId[n]].Resource == ResourceKind.Gold) mapGold += w.Resources.Remaining[n];
            else mapWood += w.Resources.Remaining[n];
        }
        if (workers == 0 || dropOffs > 0) return false;
        int f = w.FactionOf(p);
        foreach (BuildingDef b in d.Buildings)
        {
            if (!b.DropOff || b.Faction != f) continue;
            if (w.Gold[p] < b.CostGold || w.Wood[p] < b.CostWood) continue;
            long g = w.Gold[p] - b.CostGold + carriedGold + mapGold, wd = w.Wood[p] - b.CostWood + carriedWood + mapWood;
            foreach (int u in d.Factions[f].Units)
            {
                UnitDef def = d.Units[u];
                if (def.MaxAlive > 0 && w.LimitedCount(p, u) + def.SpawnCount > def.MaxAlive) continue;
                if (g >= def.CostGold && wd >= def.CostWood) return true;
            }
        }
        return false;
    }

    public static TheoryData<int> FuzzBatches()
    {
        var rows = new TheoryData<int>();
        for (int b = 0; b < 4; b++) rows.Add(b);
        return rows;
    }

    /// <summary>
    /// 60 seeds a batch (seeds 5,001+) of the draw fuzz's scenes, every seat given a worker about two times in three, its
    /// drop-offs (the Town Hall) taken about half the time, and a bank drawn round its small drop-off's and the hall's price;
    /// then the fuzz's disturbances (trains, cancels, gathers, kills) for 400 ticks. Every tick, every undefeated seat:
    /// <see cref="World.CanStillTrain"/> equals M6-SH3's scan OR QA's scan of the clause; and with the clause alone true the seat
    /// has a worker and no finished drop-off. Counts the seat-states the clause alone decided.
    /// </summary>
    [Theory]
    [MemberData(nameof(FuzzBatches))]
    public void Fuzz_CanStillTrain_IsSh3OrTheClause_QaScan(int batch)
    {
        int clauseOnly = 0, states = 0;
        for (int k = 0; k < 60; k++)
        {
            ulong seed = (ulong)(5_001 + batch * 60 + k);
            Simulation sim = DrawRuleQaTests.Scene(seed, DrawScenes.Short, out SimRng rng);
            World w = sim.World;
            var mine = new SimRng(seed, 1935);
            for (int p = 0; p < w.Config.PlayerCount; p++)
            {
                (int x, int y) = DrawScenes.Corner(sim, p);
                if (mine.NextInt(0, 3) != 0) DrawScenes.Unit(sim, p, x + 3, y + 9, UnitSlot.Worker);
                if (mine.NextInt(0, 2) == 0)
                    for (int b = 0; b < w.Buildings.Capacity; b++)
                        if (w.Buildings.Alive[b] && w.Buildings.Owner[b] == p && w.Data.Buildings[w.Buildings.TypeId[b]].DropOff)
                            w.Buildings.Free(w.Buildings.HandleOf(b));
                BuildingDef small = DropOffClauseTests.SmallDropOff(w.Data, w.FactionOf(p));
                int cheapest = DrawScenes.Cheapest(sim, p);
                int[] woods = { 0, small.CostWood - 1, small.CostWood, small.CostWood + 15, small.CostWood + 50, 274, 275, 400 };
                int[] golds = { 0, cheapest - 1, cheapest, cheapest + small.CostGold, 274, 275, 400 };
                w.Ledger.Wood[p] = woods[mine.NextInt(0, woods.Length)];
                w.Ledger.Gold[p] = golds[mine.NextInt(0, golds.Length)];
            }
            if (mine.NextInt(0, 2) == 0) DrawScenes.GoldNode(sim, 22, 30, 1 + mine.NextInt(0, 200));
            if (mine.NextInt(0, 2) == 0) Trees(sim, 20, 36, 10 * (1 + mine.NextInt(0, 8)));
            for (int t = 0; t < 400 && !sim.MatchOver; t++)
            {
                if (rng.NextInt(0, 3) == 0) DrawRuleQaTests.Disturb(sim, ref rng);
                sim.Tick();
                for (int p = 0; p < w.Config.PlayerCount; p++)
                {
                    if (w.IsDefeated(p)) continue;
                    states++;
                    bool sh3 = DrawReachScan.Sh3Can(w, p), clause = QaClause(w, p), now = w.CanStillTrain(p);
                    Assert.True(now == (sh3 || clause), $"seed {seed} tick {sim.TickNumber} p{p}: CanStillTrain {now}, M6-SH3 {sh3}, QA clause {clause}: {DrawReachScan.Line(w, p)}");
                    if (clause && !sh3) clauseOnly++;
                }
            }
        }
        _out.WriteLine($"batch {batch}: {states} seat-states, {clauseOnly} decided by the clause alone");
        Assert.True(clauseOnly > 0, "the fuzz never reached the clause");
    }

    // ---------------- QA focus 6: A2 on the shipped maps, no node known, two seats of one team ----------------

    private static BuildingDef Small(GameData d, int faction) => DropOffClauseTests.SmallDropOff(d, faction);

    /// <summary>Frees every building of <paramref name="p"/> that is a drop-off (the hall).</summary>
    private static void FreeDropOffs(World w, int p)
    {
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == p && w.Data.Buildings[w.Buildings.TypeId[k]].DropOff)
                Assert.True(w.Buildings.Free(w.Buildings.HandleOf(k)));
    }

    private static int FinishedOf(World w, int p, int type)
    {
        int n = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == p && w.Buildings.TypeId[k] == type && !w.Buildings.UnderConstruction[k]) n++;
        return n;
    }

    /// <summary>
    /// On each shipped map's base (seed 1, two Normal seats), seat <paramref name="seat"/> loses its hall at tick 200 with its
    /// workers alive and a bank of its small drop-off's price plus 25 wood: the Build is sent, the building stands (the worker
    /// reached the spot), and gold is delivered to it.
    /// </summary>
    [Theory]
    [InlineData("pale_hills", 0)]
    [InlineData("pale_hills", 1)]
    [InlineData("raraku", 0)]
    [InlineData("raraku", 1)]
    [InlineData("vathar_crossing", 0)]
    [InlineData("vathar_crossing", 1)]
    public void A2_OnEachShippedMapsBase_TheSmallDropOffIsBuiltWhereTheWorkerReaches_AndGoldIsDelivered(string map, int seat)
    {
        Simulation sim = CriterionMatch.Build(map, 1);
        World w = sim.World;
        while (sim.TickNumber < 200) sim.Tick();
        BuildingDef small = Small(w.Data, w.FactionOf(seat));
        FreeDropOffs(w, seat);
        BuildMaps.SetTotals(sim, seat, 0, small.CostWood + 25);
        List<Command> log = AiScenes.Log(sim, seat);
        int sent = -1, finished = -1, goldAtFinish = -1;
        while (sim.TickNumber < 200 + 4_000 && (finished < 0 || sim.TickNumber < finished + 1_500))
        {
            sim.Tick();
            if (sent < 0 && log.Any(c => c.Kind == CommandKind.Build && c.TypeId == small.Id)) sent = sim.TickNumber;
            if (sent >= 0 && finished < 0 && FinishedOf(w, seat, small.Id) > 0) { finished = sim.TickNumber; goldAtFinish = w.Gold[seat]; }
        }
        _out.WriteLine($"{map} seat {seat}: {small.Key} sent {sent}, finished {finished} (gold {goldAtFinish}), 1,500 ticks on gold {w.Gold[seat]}; builds {log.Count(c => c.Kind == CommandKind.Build && c.TypeId == small.Id)}");
        Assert.True(sent > 0, $"{map} seat {seat}: no Build of {small.Key}; sent {string.Join(", ", log.Where(c => c.Kind == CommandKind.Build).Select(c => $"{c.TypeId}@{c.Tick}"))}");
        Assert.True(finished > 0, $"{map} seat {seat}: {small.Key} never finished (sent at {sent})");
        Assert.True(w.Gold[seat] > goldAtFinish, $"{map} seat {seat}: no gold delivered after {small.Key} stood");
    }

    /// <summary>The seat with wood for the small drop-off and no node known (every resource gone): it is still built (round the rebuild centre), once.</summary>
    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    [InlineData("teblor")]
    public void A2_WoodForTheDropOff_NoNodeKnown_BuildsItOnce(string faction)
    {
        int f = TestSim.Data.FindFaction(faction);
        var seats = new[] { new PlayerSeat(f, SeatController.Ai, AiDifficulty.Normal), new PlayerSeat(f == 0 ? 1 : 0, SeatController.Human, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Flat(seats, explored: false);
        World w = sim.World;
        for (int n = 0; n < w.Resources.Capacity; n++)
            if (w.Resources.Alive[n]) w.Resources.Take(w.Resources.HandleOf(n), int.MaxValue);
        FreeDropOffs(w, 0);
        BuildingDef small = Small(w.Data, f);
        BuildMaps.SetTotals(sim, 0, 0, small.CostWood + 25);
        List<Command> log = AiScenes.Log(sim, 0);
        for (int t = 0; t < 4_000; t++) sim.Tick();
        int builds = log.Count(c => c.Kind == CommandKind.Build && c.TypeId == small.Id);
        _out.WriteLine($"{faction}: {builds} Build(s) of {small.Key}, finished {FinishedOf(w, 0, small.Id)}; bank {w.Gold[0]} / {w.Wood[0]}");
        Assert.True(builds >= 1, $"{faction}: no Build of {small.Key} with no node known");
        Assert.Equal(1, FinishedOf(w, 0, small.Id));
        Assert.Equal(1, builds);
    }

    /// <summary>Two seats of one team (Vathar Crossing 2v2 seed 1, seats 0 and 2) lose their halls on the same tick: each builds its own small drop-off.</summary>
    [Fact]
    public void A2_TwoSeatsOfOneTeam_EachBuildsItsOwn()
    {
        Simulation sim = CriterionMatch.Build("vathar_crossing", 1, 4, new[] { 1, 2, 1, 2 });
        World w = sim.World;
        while (sim.TickNumber < 200) sim.Tick();
        var logs = new Dictionary<int, List<Command>>();
        foreach (int p in new[] { 0, 2 })
        {
            FreeDropOffs(w, p);
            BuildMaps.SetTotals(sim, p, 0, Small(w.Data, w.FactionOf(p)).CostWood + 25);
            logs[p] = AiScenes.Log(sim, p);
        }
        for (int t = 0; t < 4_000; t++) sim.Tick();
        foreach (int p in new[] { 0, 2 })
        {
            BuildingDef small = Small(w.Data, w.FactionOf(p));
            _out.WriteLine($"seat {p}: {small.Key} builds {logs[p].Count(c => c.Kind == CommandKind.Build && c.TypeId == small.Id)}, finished {FinishedOf(w, p, small.Id)}");
            Assert.True(FinishedOf(w, p, small.Id) >= 1, $"seat {p}: no {small.Key} stands");
        }
    }

    // ---------------- QA focus 3: C's shapes the brief does not name ----------------

    private static readonly Lazy<GameData> s_mix = new(() => AiScenes.DataWithAi("malazan", root =>
    {
        foreach (JsonNode? o in root["openings"]!.AsArray())
        {
            AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 3600, "build", "malazan_billet", 0)));
            o!["compositions"] = new JsonArray(
                new JsonObject { ["age"] = 1, ["units"] = new JsonObject { ["malazan_heavy_infantry"] = 35, ["malazan_crossbowman"] = 30 } },
                new JsonObject { ["age"] = 2, ["units"] = new JsonObject { ["malazan_heavy_infantry"] = 35, ["malazan_crossbowman"] = 30 } });
        }
    }));

    /// <summary>
    /// A seat with no hall and no drop-off, 40 gold / 74 wood (under the Depot's 75: A2 cannot help), a gold mine it knows, a
    /// Barracks and a Crossbow Range; its pick is the Heavy Infantry (54 gold). Income can never close the gap (no drop-off and
    /// none it can pay for), but <see cref="AiPlayer.PickNeverPayable"/> counts the known mine as income, so production waits for
    /// the Heavy for good while the Crossbowman (36 / 45) is payable now and its Range is free; the draw rule says the seat can
    /// still train (the bank pays the Crossbowman), so it holds a match open. BUG-1935.
    /// </summary>
    [Fact(Skip = "BUG-1935 (open, S3): a seat with no drop-off that knows a node waits for its pick for good; C's 'never payable' counts a node it cannot deliver from")]
    public void C_NoDropOffAndNoneItCanPay_AKnownMine_AnotherMixUnitPayable_TrainsIt()
    {
        Simulation sim = SpendWhatItHasTests.Scene(s_mix.Value, 40, 74, out List<Command> log, keepGold: true, noHall: true);
        for (int t = 0; t < 6_000; t++) sim.Tick();
        Assert.True(sim.World.CanStillTrain(0));
        Assert.Contains(log, c => c.Kind == CommandKind.Train);
    }

    /// <summary>The measured shape of BUG-1935, kept green: the seat sends no Train for 6,000 ticks and the draw rule calls it able to train all along.</summary>
    [Fact]
    public void C_NoDropOffAndNoneItCanPay_AKnownMine_Measured()
    {
        Simulation sim = SpendWhatItHasTests.Scene(s_mix.Value, 40, 74, out List<Command> log, keepGold: true, noHall: true);
        AiPlayer ai = sim.AiOf(0)!;
        bool everNever = false, alwaysCan = true;
        ai.ThoughtForTests = a => everNever |= a.PickNeverPayable();
        for (int t = 0; t < 6_000; t++)
        {
            sim.Tick();
            alwaysCan &= sim.World.CanStillTrain(0);
        }
        World w = sim.World;
        _out.WriteLine($"6,000 ticks: Trains {log.Count(c => c.Kind == CommandKind.Train)}, Builds {log.Count(c => c.Kind == CommandKind.Build)}, Gathers {log.Count(c => c.Kind == CommandKind.Gather)}; "
            + $"bank {w.Gold[0]} / {w.Wood[0]}; can train all along {alwaysCan}; PickNeverPayable ever {everNever}; drop-offs {w.Ledger.FinishedDropOffs[0]}");
        Assert.Equal(40, w.Gold[0]);
        Assert.Equal(74, w.Wood[0]);
        Assert.True(alwaysCan);
        Assert.False(everNever);
        Assert.DoesNotContain(log, c => c.Kind == CommandKind.Train);
    }

    /// <summary>
    /// A Teblor seat with 100 gold and 40 wood (75-119 gold, under 50 wood), its Longhouse and War Lodge standing, every gold mine
    /// emptied (no gold known, no worker at gold): its pick (a Warrior, 120 gold) can never be paid for, no mix unit is payable
    /// (the Javelin Thrower needs 50 wood), so production trains the Gatherer (75 / 0) at the Longhouse.
    /// </summary>
    [Fact]
    public void C_Teblor_100Gold40Wood_NoGoldKnown_TrainsTheGatherer()
    {
        GameData d = s_teblorMix.Value;
        int teblor = d.FindFaction("teblor");
        var seats = new[] { new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Normal), new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Flat(seats, d, explored: false, workers: 7);
        World w = sim.World;
        for (int n = 0; n < w.Resources.Capacity; n++)
            if (w.Resources.Alive[n] && d.Resources[w.Resources.TypeId[n]].Resource == ResourceKind.Gold) w.Resources.Take(w.Resources.HandleOf(n), int.MaxValue);
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("teblor_war_lodge"), 40 * AiScenes.FlatSize + 4, out _));
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("teblor_javelin_ground"), 46 * AiScenes.FlatSize + 4, out _));
        // A Javelin Thrower alive, so the Warrior is the most under-weighted entry (the pick).
        w.Units.Hold[CombatScenes.Place(sim, 0, d.FindUnit("teblor_javelin_thrower"), w.NavGrid.CellCenter(16, 26)).Index] = true;
        BuildMaps.SetTotals(sim, 0, 100, 40);
        List<Command> log = AiScenes.Log(sim, 0);
        int gatherer = d.FindUnit("teblor_gatherer"), pick = -2;
        bool never = false;
        sim.AiOf(0)!.ThoughtForTests = a => { if (pick == -2) { pick = ProductionManager.Pick(a); never = a.PickNeverPayable(); } };
        for (int t = 0; t < 3 * sim.AiOf(0)!.ThinkTicks + 1; t++) sim.Tick();
        _out.WriteLine($"pick {(pick >= 0 ? d.Units[pick].Key : pick.ToString())}; Trains {string.Join(", ", log.Where(c => c.Kind == CommandKind.Train).Select(c => $"{d.Units[c.TypeId].Key}@{c.Tick}"))}");
        Assert.Equal(d.FindUnit("teblor_warrior"), pick);
        Assert.True(never, "the Warrior's gold can never be paid for here");
        Assert.Contains(log, c => c.Kind == CommandKind.Train && c.TypeId == gatherer);
    }

    /// <summary>Teblor: a Warrior / Javelin Thrower mix and a far step only, so nothing but production trains in the first thinks (seven workers: above the floor).</summary>
    private static readonly Lazy<GameData> s_teblorMix = new(() => AiScenes.DataWithAi("teblor", root =>
    {
        foreach (JsonNode? o in root["openings"]!.AsArray())
        {
            AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 3600, "build", "teblor_hut", 0)));
            o!["compositions"] = new JsonArray(
                new JsonObject { ["age"] = 1, ["units"] = new JsonObject { ["teblor_warrior"] = 40, ["teblor_javelin_thrower"] = 30 } },
                new JsonObject { ["age"] = 2, ["units"] = new JsonObject { ["teblor_warrior"] = 40, ["teblor_javelin_thrower"] = 30 } });
        }
    }));

    /// <summary>
    /// QA focus 4: a briefly short seat (a gold mine it may gather known; <see cref="SpendWhatItHasTests"/>' guard scene) for
    /// 12,000 ticks: C's rule never reads its pick as never payable, and no Train of another type is sent while it is short.
    /// </summary>
    [Fact]
    public void C_ABrieflyShortSeat_NeverReadsNeverPayable_In12000Ticks()
    {
        Simulation sim = SpendWhatItHasTests.Scene(s_mix.Value, 40, 100, out List<Command> log, keepGold: true);
        AiPlayer ai = sim.AiOf(0)!;
        int never = 0, thinks = 0;
        ai.ThoughtForTests = a => { thinks++; if (a.PickNeverPayable()) never++; };
        for (int t = 0; t < 12_000; t++) sim.Tick();
        _out.WriteLine($"{thinks} thinks, never-payable {never}; Trains {log.Count(c => c.Kind == CommandKind.Train)}; bank {sim.World.Gold[0]} / {sim.World.Wood[0]}");
        Assert.Equal(0, never);
    }

    // ---------------- QA focus 8: allocation of an AI think on the new paths ----------------

    /// <summary>
    /// 0 bytes over 2,000 ticks (after 400 warm-up ticks) of the AI on C's path every think (the pick never payable, a mix unit
    /// payable, then the worker) and on A2's path (no hall, the small drop-off's wood, a known node), no test hook attached.
    /// </summary>
    [Theory]
    [InlineData("spend")]
    [InlineData("smallDropOff")]
    [InlineData("hold")]
    public void AnAiThink_OnTheNewPaths_AllocatesNothing(string path)
    {
        Simulation sim;
        if (path == "smallDropOff")
        {
            var seats = new[] { new PlayerSeat(0, SeatController.Ai, AiDifficulty.Normal), new PlayerSeat(1, SeatController.Human, AiDifficulty.Normal) };
            sim = AiScenes.Flat(seats);
            FreeDropOffs(sim.World, 0);
            BuildMaps.SetTotals(sim, 0, 0, 100);
        }
        else if (path == "spend") sim = SpendWhatItHasTests.Scene(s_mix.Value, 5_000, 5_000, out _);
        else sim = SpendWhatItHasTests.Scene(s_mix.Value, 40, 74, out _, keepGold: true, noHall: true);
        int trains = 0;
        sim.AiOf(0)!.SentForTests = c => { if (c.Kind == CommandKind.Train) trains++; }; // allocated once, here; no capture grows
        for (int t = 0; t < 400; t++) sim.Tick();
        int warm = trains;
        // The pick is never payable on "spend" only while the Heavy's gold is short: drain the bank to 40 gold (the Crossbowman
        // still payable) every think, so C's path runs every think.
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int t = 0; t < 2_000; t++)
        {
            if (path == "spend") { sim.World.Ledger.Gold[0] = 40; sim.World.Ledger.Wood[0] = 5_000; }
            sim.Tick();
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        _out.WriteLine($"{path}: {bytes} bytes over 2,000 ticks; Trains sent {warm} in the warm-up, {trains - warm} measured");
        if (path == "spend") Assert.True(trains > warm, "C's path sent no Train while measured");
        Assert.Equal(0, bytes);
    }
}
