using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M5-4b, session 2026-10-13-0916): the defeat rule (docs/02 "Game flow" 4, docs/03 "Implementation (M5-4b)") attacked
/// at the command door, on the exact tick, and with three and four seats. Scenes build their own buildings.
/// </summary>
public class DefeatRuleQaTests
{
    private readonly ITestOutputHelper _out;

    public DefeatRuleQaTests(ITestOutputHelper output) => _out = output;

    private static int BuildingType(World w, int player, BuildingSlot slot) => StartBase.BuildingOfSlot(w.Data, w.FactionOf(player), slot);

    private static int UnitType(World w, int player, UnitSlot slot) => StartBase.UnitOfSlot(w.Data, w.FactionOf(player), slot);

    private static Vector2 At(Simulation sim, int x, int y) => sim.World.NavGrid.CellCenter(x, y);

    private static EntityHandle Spawn(Simulation sim, int player, int type, int x, int y, bool site = false)
    {
        Assert.True(sim.World.Buildings.Spawn(player, type, y * sim.World.NavGrid.Width + x, out EntityHandle h, site));
        return h;
    }

    private static Vector2 Centre(Simulation sim, EntityHandle b)
    {
        BuildingStore s = sim.World.Buildings;
        BuildingDef d = sim.World.Data.Buildings[s.TypeId[b.Index]];
        int w = sim.World.NavGrid.Width, cell = s.Cell[b.Index];
        return new Vector2((cell % w + d.FootprintWidth * 0.5f) * MapConstants.CellSize, (cell / w + d.FootprintHeight * 0.5f) * MapConstants.CellSize);
    }

    /// <summary>Runs the two ticks that apply commands queued now.</summary>
    private static void Applied(Simulation sim)
    {
        sim.Tick();
        sim.Tick();
    }

    /// <summary>
    /// The door, kind by kind: after player 0's defeat one command of **every** <see cref="CommandKind"/> (a row per kind; a
    /// new kind without a row fails here) is sent for it, each with live targets of its own (its walking worker, its
    /// marching soldier) or the enemy's buildings. The run hashes equal, after every tick, to a twin that sent a Noop in
    /// each one's place; the defeated player places, spawns and trains nothing, and its soldier finishes the walk it got
    /// before the defeat (Stop / Hold / AttackMove / Attack dropped). Player 1's identical commands apply.
    /// </summary>
    [Fact]
    public void EveryCommandKind_OfADefeatedPlayer_IsDropped_HashEqualToANoopTwin()
    {
        (Simulation Sim, EntityHandle Worker, EntityHandle Soldier, EntityHandle Hall1, EntityHandle House1, EntityHandle Worker1) Make()
        {
            var sim = TestSim.Explored(new Simulation(TestSim.Config(5, 2, 64, 256), LocalMovementTests.Flat(48)));
            World w = sim.World;
            EntityHandle site = Spawn(sim, 0, BuildingType(w, 0, BuildingSlot.House), 8, 8, site: true);
            EntityHandle hall1 = Spawn(sim, 1, BuildingType(w, 1, BuildingSlot.TownHall), 34, 6);
            EntityHandle house1 = Spawn(sim, 1, BuildingType(w, 1, BuildingSlot.House), 40, 20);
            Assert.True(w.Resources.Spawn(w.Data.FindResource("tree"), 30 * 48 + 20, w.Data.Rules.TreeWood, out _));
            BuildMaps.SetTotals(sim, 0, 5000, 5000);
            BuildMaps.SetTotals(sim, 1, 5000, 5000);
            EntityHandle worker = CombatScenes.Place(sim, 0, UnitType(w, 0, UnitSlot.Worker), At(sim, 8, 16));
            EntityHandle soldier = CombatScenes.Place(sim, 0, UnitType(w, 0, UnitSlot.Line), At(sim, 4, 44));
            EntityHandle worker1 = CombatScenes.Place(sim, 1, UnitType(w, 1, UnitSlot.Worker), At(sim, 44, 44));
            // Applied with the cancel, before the defeat: long walks the later orders would change.
            sim.Enqueue(Command.Move(0, worker, At(sim, 8, 44)));
            sim.Enqueue(Command.Move(0, soldier, At(sim, 44, 40)));
            sim.Enqueue(Command.Cancel(0, Centre(sim, site)));
            Applied(sim);
            Assert.True(w.IsDefeated(0));
            return (sim, worker, soldier, hall1, house1, worker1);
        }

        var a = Make();
        var b = Make();
        Assert.Equal(a.Sim.StateHash(), b.Sim.StateHash());
        World wa = a.Sim.World;
        int laborer = UnitType(wa, 0, UnitSlot.Worker), house = BuildingType(wa, 0, BuildingSlot.House);
        Vector2 hall = Centre(a.Sim, a.Hall1), house1 = Centre(a.Sim, a.House1), tree = At(a.Sim, 20, 30);
        int hallCell = wa.Buildings.Cell[a.Hall1.Index];
        var rows = new Dictionary<CommandKind, Command>
        {
            [CommandKind.Noop] = Command.Noop(0),
            [CommandKind.SpawnUnit] = Command.SpawnUnit(0, laborer, At(a.Sim, 20, 20)),
            [CommandKind.Move] = Command.Move(0, a.Worker, At(a.Sim, 20, 10)),
            [CommandKind.Stop] = Command.Stop(0, a.Soldier),
            [CommandKind.HoldPosition] = Command.HoldPosition(0, a.Soldier),
            [CommandKind.AttackMove] = Command.AttackMove(0, a.Soldier, At(a.Sim, 4, 4)),
            [CommandKind.SpawnBuilding] = Command.SpawnBuilding(0, house, At(a.Sim, 20, 40)),
            [CommandKind.Gather] = Command.Gather(0, a.Worker, tree),
            [CommandKind.Build] = Command.Build(0, a.Worker, house, At(a.Sim, 14, 30)),
            [CommandKind.Cancel] = Command.Cancel(0, house1),
            [CommandKind.Repair] = Command.Repair(0, a.Worker, hall),
            [CommandKind.Train] = Command.Train(0, hall, laborer),
            [CommandKind.CancelTrain] = Command.CancelTrain(0, hall, 0),
            [CommandKind.SetRally] = Command.SetRally(0, hallCell, At(a.Sim, 30, 30)),
            [CommandKind.ClearRally] = Command.ClearRally(0, hall),
            [CommandKind.Research] = Command.Research(0, hall, 0),
            [CommandKind.Attack] = Command.Attack(0, a.Soldier, a.Worker1, isBuilding: false),
            [CommandKind.UseAbility] = Command.UseAbility(0, a.Soldier, 0, At(a.Sim, 30, 40)),
        };
        foreach (CommandKind k in Enum.GetValues<CommandKind>())
            Assert.True(rows.ContainsKey(k), $"no door row for {k}: add one (rule 1)");
        // Player 1 trains at its own hall meanwhile: its commands apply around the dropped ones.
        int trained = UnitType(wa, 1, UnitSlot.Worker);
        foreach (Command c in rows.Values)
        {
            a.Sim.Enqueue(c);
            b.Sim.Enqueue(Command.Noop(0));
            Command mine = Command.Train(1, hall, trained);
            a.Sim.Enqueue(mine);
            b.Sim.Enqueue(mine);
            for (int t = 0; t < 6; t++)
            {
                a.Sim.Tick();
                b.Sim.Tick();
                if (a.Sim.PendingCommandCount == 0)
                    Assert.True(a.Sim.StateHash() == b.Sim.StateHash(), $"{c.Kind}: the runs differ after tick {a.Sim.TickNumber}");
            }
        }
        for (int t = 0; t < 800; t++)
        {
            a.Sim.Tick();
            b.Sim.Tick();
        }
        Assert.Equal(a.Sim.StateHash(), b.Sim.StateHash());
        Assert.Equal(0, wa.Ledger.LiveBuildings[0]);
        Assert.True(wa.IsDefeated(0));
        Assert.False(wa.Units.Hold[a.Soldier.Index]);
        Assert.True(Vector2.Distance(wa.Units.Position[a.Soldier.Index], At(a.Sim, 44, 40)) < 3f, $"the soldier stands at {wa.Units.Position[a.Soldier.Index]}");
        int own0 = 0;
        for (int i = 0; i < wa.Units.Capacity; i++)
            if (wa.Units.Alive[i] && wa.Units.Owner[i] == 0) own0++;
        Assert.Equal(2, own0);
        Assert.True(wa.Buildings.QueueCount[a.Hall1.Index] > 0 || wa.Units.Count > 3, "player 1's Trains never applied");
    }

    /// <summary>
    /// Orders applied before the defeat run to their end: a worker's Move, shift-queued Moves and an AttackMove behind it all
    /// play out after the defeat (the queue pops in phase 7), while a new Move sent after the defeat is dropped.
    /// </summary>
    [Fact]
    public void ShiftQueuedOrders_AppliedBeforeTheDefeat_PlayOut_ANewOneIsDropped()
    {
        var sim = TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(2, 2, 32, 64), LocalMovementTests.Flat(48)));
        World w = sim.World;
        EntityHandle site = Spawn(sim, 0, BuildingType(w, 0, BuildingSlot.House), 8, 8, site: true);
        Spawn(sim, 1, BuildingType(w, 1, BuildingSlot.TownHall), 34, 34);
        EntityHandle u = CombatScenes.Place(sim, 0, UnitType(w, 0, UnitSlot.Line), At(sim, 4, 20));
        sim.Enqueue(Command.Move(0, u, At(sim, 20, 20)));
        sim.Enqueue(Command.Move(0, u, At(sim, 20, 40), queued: true));
        sim.Enqueue(Command.AttackMove(0, u, At(sim, 6, 40), queued: true));
        sim.Enqueue(Command.Cancel(0, Centre(sim, site)));
        Applied(sim);
        Assert.True(w.IsDefeated(0));
        bool passedSecond = false;
        for (int t = 0; t < 1200; t++)
        {
            sim.Tick();
            if (Vector2.Distance(w.Units.Position[u.Index], At(sim, 20, 40)) < 2f) passedSecond = true;
        }
        Assert.True(passedSecond, "the second (shift-queued) leg never ran");
        Assert.True(Vector2.Distance(w.Units.Position[u.Index], At(sim, 6, 40)) < 2f, $"the queued AttackMove didn't finish: at {w.Units.Position[u.Index]}");
        sim.Enqueue(Command.Move(0, u, At(sim, 40, 10)));
        for (int t = 0; t < 200; t++) sim.Tick();
        Assert.True(Vector2.Distance(w.Units.Position[u.Index], At(sim, 6, 40)) < 2f, "a Move sent after the defeat applied");
    }

    /// <summary>
    /// The rule is evaluated once a tick, after phase 11: a player who cancels its last site and places a new one in the same
    /// tick (either order) is never defeated; the same cancel alone defeats it.
    /// </summary>
    [Theory]
    [InlineData("cancel then build", false)]
    [InlineData("build then cancel", false)]
    [InlineData("cancel only", true)]
    public void ACancelAndARebuild_InOneTick_DoNotDefeat(string how, bool defeated)
    {
        var sim = TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(2, 2, 32, 64), LocalMovementTests.Flat(48)));
        World w = sim.World;
        int house = BuildingType(w, 0, BuildingSlot.House);
        EntityHandle site = Spawn(sim, 0, house, 8, 8, site: true);
        Spawn(sim, 1, BuildingType(w, 1, BuildingSlot.TownHall), 34, 34);
        BuildMaps.SetTotals(sim, 0, 1000, 1000);
        EntityHandle worker = CombatScenes.Place(sim, 0, UnitType(w, 0, UnitSlot.Worker), At(sim, 16, 16));
        Command cancel = Command.Cancel(0, Centre(sim, site)), build = Command.Build(0, worker, house, At(sim, 20, 20));
        if (how == "build then cancel") sim.Enqueue(build);
        sim.Enqueue(cancel);
        if (how == "cancel then build") sim.Enqueue(build);
        Applied(sim);
        Assert.False(w.Buildings.IsAlive(site));
        Assert.Equal(defeated, w.IsDefeated(0));
        Assert.Equal(defeated ? 0 : 1, w.Ledger.LiveBuildings[0]);
        Assert.Equal(defeated ? 1 : 0, w.DefeatEvents.Length);
        Assert.Equal(defeated, sim.MatchOver);
    }

    /// <summary>
    /// The exact tick: the player's only site, one builder on it, is cancelled by a Cancel that applies (phase 1) on the very
    /// tick its last work would land (phase 4): the cancel wins and the player is defeated on that tick. One tick later the
    /// site has finished, the Cancel is dropped (a finished building isn't a site) and nobody is defeated.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void ACancelOnTheTickTheSiteWouldFinish(int late, bool defeated)
    {
        var sim = TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(2, 2, 32, 64), LocalMovementTests.Flat(48)));
        World w = sim.World;
        int house = BuildingType(w, 0, BuildingSlot.House);
        Spawn(sim, 1, BuildingType(w, 1, BuildingSlot.TownHall), 34, 34);
        BuildMaps.SetTotals(sim, 0, 1000, 1000);
        EntityHandle worker = CombatScenes.Place(sim, 0, UnitType(w, 0, UnitSlot.Worker), At(sim, 12, 12));
        sim.Enqueue(Command.Build(0, worker, house, At(sim, 10, 10)));
        Applied(sim);
        int s = w.Buildings.SlotAt(10, 10);
        Assert.True(s >= 0 && w.Buildings.UnderConstruction[s]);
        EntityHandle site = w.Buildings.HandleOf(s);
        int needed = w.Buildings.WorkNeeded(house);
        // Measure the work a tick once the builder is at it, then stop two ticks short (a command queued now applies in the
        // second tick from here).
        int step = 0;
        for (int t = 0; t < 2000 && step == 0; t++)
        {
            int before = w.Buildings.Work[s];
            sim.Tick();
            step = w.Buildings.Work[s] - before;
        }
        Assert.True(step > 0, "the builder never worked");
        while (w.Buildings.Work[s] + 2 * step < needed) sim.Tick();
        Assert.True(w.Buildings.Work[s] + step < needed, "the site finishes on the next tick: no room for the exact-tick case");
        for (int k = 0; k < late; k++) sim.Tick();
        sim.Enqueue(Command.Cancel(0, Centre(sim, site)));
        Applied(sim);
        Assert.Equal(defeated, !w.Buildings.IsAlive(site));
        Assert.Equal(defeated, w.IsDefeated(0));
        if (!defeated) Assert.False(w.Buildings.UnderConstruction[s]);
    }

    /// <summary>
    /// Three and four seats on a generated map with that many start locations, all human, recorded: each player places a
    /// house site with its worker; player 1's is razed by player 0's spawned soldiers, then (four seats) players 2 and 3
    /// cancel theirs on one tick, or (three seats) player 2 cancels. The events come in player order on their tick,
    /// <see cref="Simulation.MatchOver"/> turns true only at the last defeat, the losers keep sending orders (logged, dropped),
    /// and the recording plays back to equal checkpoints well past the end.
    /// </summary>
    [Theory]
    [InlineData(3, 1UL)]
    [InlineData(4, 1UL)]
    [InlineData(4, 7UL)]
    public void ThreeAndFourSeats_MixedDefeats_MatchOverAtTheLast_AndPlaybackEqual(int seats, ulong seed)
    {
        var seatList = new PlayerSeat[seats];
        for (int p = 0; p < seats; p++) seatList[p] = new PlayerSeat(p % 2, SeatController.Human, AiDifficulty.Normal);
        var map = MapGenParams.Default with { StartLocations = seats };
        var config = new SimConfig(seed, seats, 256, 1024) { Data = TestSim.Data, Map = map, Seats = seatList.ToImmutableArray() };
        var sim = new Simulation(config);
        var rec = new ReplayRecorder(sim, checkpointInterval: 25, commandCapacity: 16_384);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        float maxRadius = 0f;
        foreach (UnitDef d in w.Data.Units) maxRadius = Math.Max(maxRadius, d.Radius);
        var blocks = new Vector2[seats][];
        var factions = new int[seats];
        for (int p = 0; p < seats; p++)
        {
            blocks[p] = Array.Empty<Vector2>();
            factions[p] = w.FactionOf(p);
        }
        StartBasePlan plan = StartBase.Plan(g, w.Data, w.Buildings, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, factions, blocks, 1, maxRadius);
        for (int p = 0; p < seats; p++)
        {
            Assert.True(plan.HallAnchor[p] >= 0, $"no start for player {p}");
            sim.Enqueue(Command.SpawnUnit(p, plan.WorkerType[p], plan.Workers[p][0]));
        }
        Applied(sim);
        var workers = new EntityHandle[seats];
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i]) workers[w.Units.Owner[i]] = new EntityHandle(i, w.Units.Generation[i]);
        for (int p = 0; p < seats; p++)
        {
            int anchor = plan.HallAnchor[p];
            sim.Enqueue(Command.Build(p, workers[p], BuildingType(w, p, BuildingSlot.House), g.CellCenter(anchor % g.Width, anchor / g.Width)));
        }
        Applied(sim);
        var sites = new EntityHandle[seats];
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k]) sites[w.Buildings.Owner[k]] = w.Buildings.HandleOf(k);
        for (int p = 0; p < seats; p++) Assert.True(w.Buildings.IsAlive(sites[p]), $"player {p} placed no site");
        // Player 0's soldiers beside player 1's site, ordered onto it.
        int heavy = CombatScenes.HeavyInfantry;
        Vector2 at1 = Centre(sim, sites[1]);
        for (int k = 0; k < 4; k++) sim.Enqueue(Command.SpawnUnit(0, heavy, at1 + new Vector2(-4f + k * 1.2f, 4f)));
        Applied(sim);
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Units.TypeId[i] == heavy)
                sim.Enqueue(Command.Attack(0, new EntityHandle(i, w.Units.Generation[i]), sites[1], isBuilding: true));
        var events = new List<DefeatEvent>();
        int over = -1, cancelAt = -1, lastDefeat = -1;
        int[] losers = seats == 4 ? new[] { 2, 3 } : new[] { 2 };
        while (sim.TickNumber < 3000 && (over < 0 || sim.TickNumber < over + 400))
        {
            // The others keep ordering their workers about the whole time, and once defeated try to place again.
            if (sim.TickNumber % 20 == 0)
                for (int p = 1; p < seats; p++)
                {
                    sim.Enqueue(Command.Move(p, workers[p], w.Units.Position[workers[p].Index] + new Vector2(sim.TickNumber % 40 == 0 ? 2f : -2f, 0f)));
                    if (!w.IsDefeated(p)) continue;
                    sim.Enqueue(Command.SpawnBuilding(p, BuildingType(w, p, BuildingSlot.House), w.Units.Position[workers[p].Index] + new Vector2(6f, 6f)));
                    sim.Enqueue(Command.Build(p, workers[p], BuildingType(w, p, BuildingSlot.House), w.Units.Position[workers[p].Index] + new Vector2(-6f, 6f)));
                }
            if (cancelAt < 0 && w.IsDefeated(1))
            {
                // Then the rest cancel theirs on one tick.
                foreach (int p in losers) sim.Enqueue(Command.Cancel(p, Centre(sim, sites[p])));
                cancelAt = sim.TickNumber;
            }
            sim.Tick();
            foreach (DefeatEvent e in w.DefeatEvents)
            {
                events.Add(e);
                lastDefeat = e.Tick;
            }
            bool overNow = sim.MatchOver;
            if (overNow && over < 0) over = sim.TickNumber - 1;
            Assert.True(overNow == (over >= 0), "MatchOver went back to false");
        }
        _out.WriteLine($"{seats} seats, seed {seed}: events {string.Join(", ", events)}; over at {over}");
        Assert.True(over >= 0, "the match never ended");
        Assert.Equal(1 + losers.Length, events.Count);
        Assert.Equal(1, events[0].Player);
        Assert.Equal(losers, events.Skip(1).Select(e => e.Player).ToArray());
        Assert.True(events.Skip(1).All(e => e.Tick == events[1].Tick), "the same-tick cancels fell on different ticks");
        Assert.True(events[1].Tick > events[0].Tick);
        Assert.Equal(lastDefeat, over);
        Assert.False(w.IsDefeated(0));
        // The defeated players' SpawnBuildings and Builds never landed.
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k]) Assert.Equal(0, w.Buildings.Owner[k]);
        Replay r = rec.ToReplay();
        Assert.True(r.Commands.Count(c => c.Player != 0 && c.Tick > over) > 20, "the losers' later commands weren't logged");
        ReplayResult played = ReplayPlayer.Run(r, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(sim.TickNumber, played.TicksRun);
    }

    /// <summary>
    /// <see cref="Simulation.MatchOver"/>'s summary says "true once the match has a result". Three players: 0 and 1 have
    /// built, 2 has only a worker. Player 1 cancels its only site: one of two builders stands, the match is over. Then
    /// player 2 places its first site: the match is on again. A result that un-happens (BUG-0672).
    /// </summary>
    [Fact]
    public void MatchOver_OnceTrue_StaysTrue_WhenAPlayerThatNeverBuiltBuildsLater()
    {
        var sim = TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(2, 3, 32, 64), LocalMovementTests.Flat(48)));
        World w = sim.World;
        Spawn(sim, 0, BuildingType(w, 0, BuildingSlot.TownHall), 4, 4);
        EntityHandle site1 = Spawn(sim, 1, BuildingType(w, 1, BuildingSlot.House), 30, 6, site: true);
        BuildMaps.SetTotals(sim, 2, 1000, 1000);
        EntityHandle worker2 = CombatScenes.Place(sim, 2, UnitType(w, 2, UnitSlot.Worker), At(sim, 20, 36));
        sim.Enqueue(Command.Cancel(1, Centre(sim, site1)));
        Applied(sim);
        Assert.True(w.IsDefeated(1));
        Assert.True(sim.MatchOver);
        sim.Enqueue(Command.Build(2, worker2, BuildingType(w, 2, BuildingSlot.House), At(sim, 22, 40)));
        Applied(sim);
        Assert.True(w.EverBuilt(2));
        Assert.True(sim.MatchOver, "MatchOver went back to false after a result: player 2 built its first building");
    }
}
