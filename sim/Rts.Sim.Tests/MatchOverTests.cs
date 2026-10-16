using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4b criterion 1-2, M5-4e criterion 1-2: the defeat rule (docs/02 "Game flow" 4). A player that has built and has no
/// live unit and no finished unit-producing building is defeated on that tick, once, with an event; a player that never
/// built never is; <see cref="Simulation.MatchOver"/> turns true when at
/// most one player that ever built stands; a defeated player's commands are dropped where they apply (recorded all the same,
/// so a replay drops the same ones); its AI stops. Scenes build their own buildings (content-independent).
/// </summary>
public class MatchOverTests
{
    private static int Type(Simulation sim, int player, BuildingSlot slot) => StartBase.BuildingOfSlot(sim.World.Data, sim.World.FactionOf(player), slot);

    /// <summary>A building of <paramref name="slot"/> for <paramref name="player"/> anchored at (x, y), straight into the store (a site with <paramref name="site"/>).</summary>
    private static EntityHandle Building(Simulation sim, int player, int x, int y, bool site = false, BuildingSlot slot = BuildingSlot.House)
    {
        Assert.True(sim.World.Buildings.Spawn(player, Type(sim, player, slot), y * sim.World.NavGrid.Width + x, out EntityHandle h, site));
        return h;
    }

    private static Vector2 Centre(Simulation sim, EntityHandle b)
    {
        BuildingStore s = sim.World.Buildings;
        BuildingDef d = sim.World.Data.Buildings[s.TypeId[b.Index]];
        int w = sim.World.NavGrid.Width, cell = s.Cell[b.Index];
        return new Vector2((cell % w + d.FootprintWidth * 0.5f) * MapConstants.CellSize, (cell / w + d.FootprintHeight * 0.5f) * MapConstants.CellSize);
    }

    /// <summary>Runs the two ticks that apply commands queued now (stamped for the next tick number); returns the number of the tick they applied in.</summary>
    private static int Applied(Simulation sim)
    {
        sim.Tick();
        sim.Tick();
        return sim.TickNumber - 1;
    }

    /// <summary>A unit of <paramref name="slot"/> for <paramref name="player"/> at cell (x, y), straight into the store.</summary>
    private static EntityHandle UnitOf(Simulation sim, int player, int x, int y, UnitSlot slot = UnitSlot.Worker) =>
        CombatScenes.Place(sim, player, StartBase.UnitOfSlot(sim.World.Data, sim.World.FactionOf(player), slot), CombatScenes.At(sim, x, y));

    /// <summary>
    /// M5-4e (a): player 0's only hall is razed while its worker lives: not defeated (a unit keeps a side in). The worker
    /// is killed: defeated on that very tick, one event, the match over; sticky and announced once.
    /// </summary>
    [Fact]
    public void TheLastHallRazed_AWorkerAlive_KeepsTheSideIn_TheWorkersDeathEndsIt()
    {
        Simulation sim = CombatScenes.Flat(48, 64, 2);
        World w = sim.World;
        EntityHandle hall = Building(sim, 0, 10, 10, slot: BuildingSlot.TownHall);
        Building(sim, 1, 34, 34, slot: BuildingSlot.TownHall);
        EntityHandle worker = UnitOf(sim, 0, 6, 30);
        Assert.Equal(1, w.ProductionBuildings(0));
        Assert.Equal(1, w.LiveUnits(0));
        w.Buildings.SetHp(hall.Index, 1);
        EntityHandle raider = CombatScenes.Place(sim, 1, CombatScenes.Raider, CombatScenes.At(sim, 9, 10));
        sim.Enqueue(Command.Attack(1, raider, hall, isBuilding: true));
        for (int t = 0; t < 400 && w.Buildings.IsAlive(hall); t++) sim.Tick();
        Assert.False(w.Buildings.IsAlive(hall), "the hall stood");
        for (int t = 0; t < 20; t++) sim.Tick();
        Assert.Equal(0, w.ProductionBuildings(0));
        Assert.False(w.IsDefeated(0));
        Assert.False(sim.MatchOver);
        CombatScenes.Spot(sim, 1, worker);
        sim.Enqueue(Command.Attack(1, raider, worker, isBuilding: false));
        while (w.Units.IsAlive(worker) && sim.TickNumber < 3000)
        {
            Assert.False(w.IsDefeated(0));
            Assert.Equal(0, w.DefeatEvents.Length);
            sim.Tick();
        }
        Assert.False(w.Units.IsAlive(worker), "the worker lived");
        Assert.Equal(new[] { new DefeatEvent(0, sim.TickNumber - 1) }, w.DefeatEvents.ToArray());
        Assert.True(w.IsDefeated(0));
        Assert.False(w.IsDefeated(1));
        Assert.True(sim.MatchOver);
        sim.Tick();
        Assert.True(w.IsDefeated(0));
        Assert.Equal(0, w.DefeatEvents.Length);
        Assert.True(sim.MatchOver);
    }

    /// <summary>
    /// M5-4e (b): units and only a building that trains nothing (each such slot of the faction in turn: House, Camp, Forge,
    /// tower): not defeated. The units die: defeated, and (M5-H2, BUG-0703) the building collapses on that tick.
    /// </summary>
    [Fact]
    public void UnitsAndOnlyABuildingThatTrainsNothing_TheUnitsDie_Defeated_TheBuildingGoneOnTheDefeatTick()
    {
        int checkedSlots = 0;
        foreach (BuildingSlot slot in Enum.GetValues<BuildingSlot>())
        {
            Simulation sim = CombatScenes.Flat(48, 64, 2);
            World w = sim.World;
            int type = Type(sim, 0, slot);
            if (type < 0 || !w.Data.UnitsTrainedAt(type).IsEmpty) continue;
            checkedSlots++;
            EntityHandle own = Building(sim, 0, 10, 10, slot: slot);
            Building(sim, 1, 34, 34, slot: BuildingSlot.TownHall);
            EntityHandle a = UnitOf(sim, 0, 6, 30), b = UnitOf(sim, 0, 8, 30, UnitSlot.Line);
            for (int t = 0; t < 10; t++) sim.Tick();
            Assert.False(w.IsDefeated(0), $"{slot}");
            w.Units.Free(a);
            sim.Tick();
            Assert.False(w.IsDefeated(0), $"{slot}: one unit left");
            w.Units.Free(b);
            sim.Tick();
            Assert.True(w.IsDefeated(0), $"{slot}");
            Assert.Equal(new[] { new DefeatEvent(0, sim.TickNumber - 1) }, w.DefeatEvents.ToArray());
            Assert.False(w.Buildings.IsAlive(own), $"{slot}: the building stands after the defeat");
            Assert.True(sim.MatchOver);
        }
        Assert.True(checkedSlots >= 2, "precondition: the faction has a house and at least one more slot that trains nothing");
    }

    /// <summary>
    /// M5-H2 (BUG-0703) scene: player 0 holds a House, a Forge researching, a Watchtower and a hall site, and one worker (its
    /// last unit); player 1 a hall and a soldier parked in the tower's range.
    /// </summary>
    private static (Simulation Sim, EntityHandle Worker, EntityHandle Soldier, EntityHandle[] Buildings, int Tech) CollapseScene()
    {
        Simulation sim = CombatScenes.Flat(48, 64, 2);
        World w = sim.World;
        TestSim.Explored(sim);
        EntityHandle worker = UnitOf(sim, 0, 4, 40);
        EntityHandle house = Building(sim, 0, 6, 20), forge = Building(sim, 0, 6, 6, slot: BuildingSlot.Forge);
        EntityHandle tower = Building(sim, 0, 20, 20, slot: BuildingSlot.WatchTower);
        EntityHandle site = Building(sim, 0, 16, 6, site: true, slot: BuildingSlot.TownHall);
        Building(sim, 1, 34, 34, slot: BuildingSlot.TownHall);
        BuildMaps.SetTotals(sim, 0, 50_000, 50_000);
        int tech = -1;
        foreach (int t in w.Data.TechsResearchableAt(w.Buildings.TypeId[forge.Index]))
            if (tech < 0 && w.CanResearch(0, forge.Index, t, out _)) tech = t;
        Assert.True(tech >= 0, "precondition: a tech researchable at the Forge");
        sim.Enqueue(Command.Research(0, Centre(sim, forge), tech));
        EntityHandle soldier = UnitOf(sim, 1, 24, 21, UnitSlot.Line);
        return (sim, worker, soldier, new[] { house, forge, tower, site }, tech);
    }

    /// <summary>
    /// M5-H2 (BUG-0703, docs/02 "Game flow" 4): the House gone on the defeat tick, and with it every other building of the
    /// side that is out (a Forge researching, a tower aiming at a soldier, a site): one <see cref="DeathEvent.Collapsed"/>
    /// event each, no killer, neither a kill nor a loss counted, the cells open, the research gone (never researched). The
    /// tower shoots no more: player 1's soldier parked in its range lives on and player 0's kills stay put.
    /// </summary>
    [Fact]
    public void TheHouseGoneOnTheDefeatTick_EveryBuildingCollapses_NeitherAKillNorALoss()
    {
        (Simulation sim, EntityHandle worker, EntityHandle soldier, EntityHandle[] buildings, int tech) = CollapseScene();
        World w = sim.World;
        for (int t = 0; t < 400 && w.Buildings.TowerTarget[buildings[2].Index] != soldier; t++) sim.Tick();
        Assert.True(w.Buildings.TowerTarget[buildings[2].Index] == soldier, "precondition: the tower aims at the soldier");
        Assert.Equal(1, w.Buildings.QueueCount[buildings[1].Index]);
        int[] kills = w.Kills.ToArray(), losses = w.Losses.ToArray();
        var cells = new int[buildings.Length];
        for (int k = 0; k < buildings.Length; k++) cells[k] = w.Buildings.Cell[buildings[k].Index];
        w.Units.Free(worker);
        sim.Tick();
        Assert.Equal(new[] { new DefeatEvent(0, sim.TickNumber - 1) }, w.DefeatEvents.ToArray());
        DeathEvent[] collapsed = w.Deaths.ToArray().Where(d => d.Collapsed).ToArray();
        Assert.Equal(buildings.Length, collapsed.Length);
        foreach (DeathEvent d in collapsed)
        {
            Assert.True(d.IsBuilding && d.VictimOwner == 0 && d.KillerOwner == -1 && !d.Expired, $"{d}");
            Assert.Contains(d.Victim, buildings);
        }
        for (int k = 0; k < buildings.Length; k++)
        {
            Assert.False(w.Buildings.IsAlive(buildings[k]), $"building {k} stands after the defeat");
            Assert.True(w.NavGrid.IsPassable(cells[k] % w.NavGrid.Width, cells[k] / w.NavGrid.Width), $"building {k}'s anchor cell stayed shut");
        }
        Assert.Equal(0, w.Ledger.LiveBuildings[0]);
        Assert.Equal(kills, w.Kills.ToArray());
        Assert.Equal(losses, w.Losses.ToArray());
        for (int t = 0; t < 600; t++) sim.Tick();
        Assert.True(w.Units.IsAlive(soldier), "the soldier died after the tower collapsed");
        Assert.False(w.HasTech(0, tech));
        Assert.Equal(kills[0], w.Kills[0]);
    }

    /// <summary>M5-H2 (BUG-0703): two runs of the collapse scene hash equal after every tick, through the defeat tick and 200 ticks after it.</summary>
    [Fact]
    public void ACollapse_HashTwinsEqual()
    {
        var a = CollapseScene();
        var b = CollapseScene();
        int defeatedAt = -1;
        for (int t = 0; t < 600 && (defeatedAt < 0 || t < defeatedAt + 200); t++)
        {
            if (t == 120)
            {
                a.Sim.World.Units.Free(a.Worker);
                b.Sim.World.Units.Free(b.Worker);
            }
            a.Sim.Tick();
            b.Sim.Tick();
            Assert.True(a.Sim.StateHash() == b.Sim.StateHash(), $"the twins differ after tick {a.Sim.TickNumber - 1}");
            if (defeatedAt < 0 && a.Sim.World.IsDefeated(0)) defeatedAt = t;
        }
        Assert.Equal(120, defeatedAt);
        Assert.Equal(0, a.Sim.World.Ledger.LiveBuildings[0]);
    }

    /// <summary>
    /// M5-H2 (BUG-0703): four AI seats on the flat scene, combat on, the early-wave data; seat 3's workers gone and a House
    /// beside its hall, and at tick 600 its hall razed (a store seam), so it falls mid-match and the House collapses. After
    /// every tick: no building of a defeated player stands (so none acts after
    /// its tick), and each player's kills and losses moved by exactly the tick's counted deaths (a collapse moves neither).
    /// </summary>
    [Fact]
    public void FourAiSeats_APlayerCollapsesMidMatch_NothingOfItsActsAfter_TheStatsUnmoved()
    {
        var seats = new PlayerSeat[]
        {
            new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal),
            new(0, SeatController.Ai, AiDifficulty.Hard), new(1, SeatController.Ai, AiDifficulty.Easy),
        };
        Simulation sim = AiScenes.Flat(seats, AiScenes.EarlyWaves(2), units: 600, commands: 1024, seed: 5, combat: true);
        World w = sim.World;
        for (int p = 0; p < 3; p++) BuildMaps.SetTotals(sim, p, 3000, 3000);
        BuildMaps.SetTotals(sim, 3, 0, 0);
        EntityHandle hall3 = default;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 3) hall3 = w.Buildings.HandleOf(k);
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 3) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        // A House beside seat 3's hall: still standing when the hall falls, so it collapses.
        (int h3x, int h3y) = AiScenes.HallAt(3);
        int house3 = StartBase.BuildingOfSlot(w.Data, w.FactionOf(3), BuildingSlot.House);
        bool placed = false;
        for (int d = 6; d < 14 && !placed; d++)
            foreach ((int dx, int dy) in new[] { (d, 0), (-d, 0), (0, d), (0, -d) })
                if (!placed) placed = w.Buildings.Spawn(3, house3, (h3y + dy) * w.NavGrid.Width + h3x + dx, out _);
        Assert.True(placed, "precondition: a House for seat 3");
        int line = StartBase.UnitOfSlot(w.Data, w.FactionOf(2), UnitSlot.Line);
        (int hx, int hy) = AiScenes.HallAt(2);
        for (int k = 0; k < 8; k++) CombatScenes.Place(sim, 2, line, w.NavGrid.CellCenter(hx + k % 4, hy + 6 + k / 4));
        int[] kills = w.Kills.ToArray(), losses = w.Losses.ToArray();
        int collapses = 0, defeatTick = -1;
        for (int t = 0; t < 2000; t++)
        {
            if (t == 600) w.Buildings.Damage(hall3, int.MaxValue);
            sim.Tick();
            foreach (DeathEvent d in w.Deaths)
            {
                if (d.Collapsed) collapses++;
                if (d.Expired || d.Collapsed) continue;
                if (d.KillerOwner >= 0) kills[d.KillerOwner]++;
                losses[d.VictimOwner]++;
            }
            Assert.Equal(kills, w.Kills.ToArray());
            Assert.Equal(losses, w.Losses.ToArray());
            foreach (DefeatEvent e in w.DefeatEvents) if (e.Player == 3) defeatTick = e.Tick;
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k]) Assert.False(w.IsDefeated(w.Buildings.Owner[k]), $"tick {sim.TickNumber - 1}: defeated player {w.Buildings.Owner[k]}'s building {k} stands");
        }
        Assert.Equal(600, defeatTick);
        Assert.True(collapses >= 1, "precondition: seat 3 had a building left to collapse");
    }

    /// <summary>M5-4e (c): no unit and an unfinished hall site: defeated (a site never counts); the site collapses on that tick (M5-H2).</summary>
    [Fact]
    public void NoUnitAndAnUnfinishedHallSite_IsDefeated()
    {
        Simulation sim = CombatScenes.Flat(48, 64, 2);
        World w = sim.World;
        EntityHandle site = Building(sim, 0, 10, 10, site: true, slot: BuildingSlot.TownHall);
        Building(sim, 1, 34, 34, slot: BuildingSlot.TownHall);
        Assert.Equal(0, w.ProductionBuildings(0));
        sim.Tick();
        Assert.True(w.IsDefeated(0));
        Assert.Equal(new[] { new DefeatEvent(0, sim.TickNumber - 1) }, w.DefeatEvents.ToArray());
        Assert.False(w.Buildings.IsAlive(site));
        Assert.True(sim.MatchOver);
    }

    /// <summary>M5-4e: the same hall site, finished, keeps the side in with no unit; razed, the side is out on that tick.</summary>
    [Fact]
    public void AHallSiteFinished_KeepsTheSideIn_WithNoUnit_RazedItIsOut()
    {
        Simulation sim = CombatScenes.Flat(48, 64, 2);
        World w = sim.World;
        EntityHandle site = Building(sim, 0, 10, 10, site: true, slot: BuildingSlot.TownHall);
        Building(sim, 1, 34, 34, slot: BuildingSlot.TownHall);
        w.Buildings.SetWork(site.Index, w.Buildings.WorkNeeded(w.Buildings.TypeId[site.Index]));
        Assert.Equal(1, w.ProductionBuildings(0));
        for (int t = 0; t < 10; t++) sim.Tick();
        Assert.False(w.IsDefeated(0));
        w.Buildings.Damage(site, int.MaxValue);
        sim.Tick();
        Assert.True(w.IsDefeated(0));
        Assert.Equal(new[] { new DefeatEvent(0, sim.TickNumber - 1) }, w.DefeatEvents.ToArray());
    }

    /// <summary>M5-4e (d): one of two halls razed, no unit at all: not defeated; the second razed: defeated.</summary>
    [Fact]
    public void ARazedHall_WithASecondHallStanding_NotDefeated()
    {
        Simulation sim = CombatScenes.Flat(48, 64, 2);
        World w = sim.World;
        EntityHandle h1 = Building(sim, 0, 6, 6, slot: BuildingSlot.TownHall), h2 = Building(sim, 0, 20, 6, slot: BuildingSlot.TownHall);
        Building(sim, 1, 34, 34, slot: BuildingSlot.TownHall);
        Assert.Equal(2, w.ProductionBuildings(0));
        w.Buildings.Damage(h1, int.MaxValue);
        for (int t = 0; t < 10; t++) sim.Tick();
        Assert.Equal(1, w.ProductionBuildings(0));
        Assert.False(w.IsDefeated(0));
        Assert.False(sim.MatchOver);
        w.Buildings.Damage(h2, int.MaxValue);
        sim.Tick();
        Assert.True(w.IsDefeated(0));
        Assert.True(sim.MatchOver);
    }

    /// <summary>M5-4e (e): a player that never built, with units that all die, is never defeated and doesn't end the match.</summary>
    [Fact]
    public void APlayerThatNeverBuilt_ItsUnitsAllDie_IsNeverDefeated_NorEndsTheMatch()
    {
        Simulation sim = CombatScenes.Flat(48, 64, 2);
        World w = sim.World;
        EntityHandle own = Building(sim, 0, 10, 10, slot: BuildingSlot.TownHall);
        EntityHandle raider = CombatScenes.Place(sim, 1, CombatScenes.Raider, CombatScenes.At(sim, 36, 36));
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            Assert.False(w.IsDefeated(1));
            Assert.False(sim.MatchOver);
        }
        w.Units.Free(raider);
        for (int t = 0; t < 20; t++) sim.Tick();
        Assert.Equal(0, w.LiveUnits(1));
        Assert.False(w.IsDefeated(1));
        Assert.False(w.EverBuilt(1));
        Assert.False(w.IsDefeated(0));
        Assert.True(w.Buildings.Free(own));
        sim.Tick();
        Assert.True(w.IsDefeated(0));
        Assert.False(w.IsDefeated(1));
        Assert.False(sim.MatchOver);
    }

    /// <summary>Three players: one defeat leaves the match on, the second ends it; two on one tick are two events in player order.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThreePlayers_TheMatchEndsAtTheSecondDefeat(bool sameTick)
    {
        Simulation sim = CombatScenes.Flat(48, 64, 3);
        World w = sim.World;
        EntityHandle h0 = Building(sim, 0, 8, 8, slot: BuildingSlot.TownHall), h1 = Building(sim, 1, 30, 8, slot: BuildingSlot.TownHall);
        Building(sim, 2, 20, 30, slot: BuildingSlot.TownHall);
        sim.Tick();
        Assert.False(sim.MatchOver);
        // Player 1's hall goes first: the events still come in player order.
        w.Buildings.Free(h1);
        if (sameTick) w.Buildings.Free(h0);
        sim.Tick();
        int at = sim.TickNumber - 1;
        if (sameTick)
        {
            Assert.Equal(new[] { new DefeatEvent(0, at), new DefeatEvent(1, at) }, w.DefeatEvents.ToArray());
            Assert.True(sim.MatchOver);
            return;
        }
        Assert.Equal(new[] { new DefeatEvent(1, at) }, w.DefeatEvents.ToArray());
        Assert.False(sim.MatchOver);
        w.Buildings.Free(h0);
        sim.Tick();
        Assert.Equal(new[] { new DefeatEvent(0, sim.TickNumber - 1) }, w.DefeatEvents.ToArray());
        Assert.True(sim.MatchOver);
        Assert.False(w.IsDefeated(2));
    }

    /// <summary>M5-4e (f): two players' last units go on one tick (their halls already gone): both events in player order, the match over with nobody standing.</summary>
    [Fact]
    public void TwoPlayers_BothOnOneTick_MatchOver()
    {
        Simulation sim = CombatScenes.Flat(48, 64, 2);
        World w = sim.World;
        EntityHandle h0 = Building(sim, 0, 8, 8, slot: BuildingSlot.TownHall), h1 = Building(sim, 1, 30, 8, slot: BuildingSlot.TownHall);
        EntityHandle u0 = UnitOf(sim, 0, 8, 30), u1 = UnitOf(sim, 1, 30, 30);
        w.Buildings.Free(h1);
        w.Buildings.Free(h0);
        for (int t = 0; t < 5; t++) sim.Tick();
        Assert.False(w.IsDefeated(0) || w.IsDefeated(1));
        w.Units.Free(u1);
        w.Units.Free(u0);
        sim.Tick();
        int at = sim.TickNumber - 1;
        Assert.Equal(new[] { new DefeatEvent(0, at), new DefeatEvent(1, at) }, w.DefeatEvents.ToArray());
        Assert.True(sim.MatchOver);
    }

    /// <summary>
    /// M5-4e (g): a summoned unit alone (its hall gone) keeps its side in until its lifetime ends (phase 5); it expires:
    /// defeated on that tick.
    /// </summary>
    [Fact]
    public void ASummonedUnitAlone_KeepsTheSideIn_UntilItExpires()
    {
        Simulation sim = CombatScenes.Flat(48, 64, 2);
        World w = sim.World;
        EntityHandle hall = Building(sim, 0, 8, 8, slot: BuildingSlot.TownHall);
        Building(sim, 1, 30, 8, slot: BuildingSlot.TownHall);
        int line = StartBase.UnitOfSlot(w.Data, w.FactionOf(0), UnitSlot.Line);
        int ends = sim.TickNumber + 60;
        Assert.True(w.Units.TrySpawn(0, line, w.Data.Units[line], CombatScenes.At(sim, 8, 30), ends, out EntityHandle summon));
        Assert.True(w.Units.IsSummoned(summon.Index));
        w.Buildings.Free(hall);
        while (w.Units.IsAlive(summon) && sim.TickNumber < ends + 5)
        {
            Assert.False(w.IsDefeated(0), $"defeated before tick {sim.TickNumber} with the summon alive");
            sim.Tick();
        }
        Assert.False(w.Units.IsAlive(summon));
        Assert.Equal(new[] { new DefeatEvent(0, ends) }, w.DefeatEvents.ToArray());
        Assert.Equal(ends, sim.TickNumber - 1);
        Assert.True(sim.MatchOver);
    }

    /// <summary>
    /// Criterion 1 (d): after player 0's defeat its Move, Build and Train (and a SpawnUnit) are dropped where they apply, while
    /// player 1's same commands apply. The hash equals a twin's that sent Noops in their place (a command never sent would
    /// differ by the player's hashed sequence counter alone). <see cref="World.CanPlace"/> agrees (<see cref="PlacementError.Defeated"/>).
    /// M5-4e: re-staged: the defeat takes the worker killed (a live unit keeps a side in, a site doesn't), so the
    /// orders name a dead worker; the SpawnUnit and Train are the ones only the defeat stops.
    /// </summary>
    [Fact]
    public void ADefeatedPlayersCommands_AreDropped_AnotherPlayersApply()
    {
        Simulation Run(bool send, out EntityHandle worker0, out EntityHandle worker1)
        {
            var sim = TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(1, 2, 64, 544), LocalMovementTests.Flat(48)));
            World w = sim.World;
            EntityHandle site0 = Building(sim, 0, 8, 8, site: true);
            EntityHandle hall1 = Building(sim, 1, 30, 8, slot: BuildingSlot.TownHall);
            int type0 = StartBase.UnitOfSlot(w.Data, w.FactionOf(0), UnitSlot.Worker), type1 = StartBase.UnitOfSlot(w.Data, w.FactionOf(1), UnitSlot.Worker);
            worker0 = CombatScenes.Place(sim, 0, type0, CombatScenes.At(sim, 8, 16));
            worker1 = CombatScenes.Place(sim, 1, type1, CombatScenes.At(sim, 30, 16));
            // Its worker killed, the site alone is out on the next tick (a site doesn't keep a side in) and collapses (M5-H2).
            w.Units.Free(worker0);
            Applied(sim);
            Assert.False(w.Buildings.IsAlive(site0));
            Assert.True(w.IsDefeated(0));
            // The placement query agrees with the door: the defeated player's ghost is refused (last, after every other rule).
            Assert.False(w.CanPlace(0, Type(sim, 0, BuildingSlot.House), 24 * 48 + 6, out PlacementError why0));
            Assert.Equal(PlacementError.Defeated, why0);
            Assert.False(w.CanPlace(0, Type(sim, 0, BuildingSlot.House), -1, out why0));
            Assert.Equal(PlacementError.OffMap, why0);
            Assert.True(w.CanPlace(1, Type(sim, 1, BuildingSlot.House), 24 * 48 + 36, out _));
            Vector2 hall = Centre(sim, hall1);
            for (int p = 0; p < 2; p++)
            {
                EntityHandle me = p == 0 ? worker0 : worker1;
                Command[] orders =
                {
                    Command.Move(p, me, CombatScenes.At(sim, 20, 30)),
                    Command.Build(p, me, Type(sim, p, BuildingSlot.House), CombatScenes.At(sim, p == 0 ? 6 : 36, 24), queued: true),
                    Command.Train(p, p == 0 ? CombatScenes.At(sim, 8, 8) : hall, p == 0 ? type0 : type1),
                    Command.SpawnUnit(p, p == 0 ? type0 : type1, CombatScenes.At(sim, 20, 20)),
                };
                foreach (Command c in orders) sim.Enqueue(p == 1 || send ? c : Command.Noop(0));
            }
            return sim;
        }

        Simulation a = Run(true, out EntityHandle a0, out EntityHandle a1), b = Run(false, out _, out _);
        // Not compared yet: the pending commands differ (they are hashed until they apply).
        for (int t = 0; t < 600; t++)
        {
            a.Tick();
            b.Tick();
            if (a.PendingCommandCount == 0) Assert.True(a.StateHash() == b.StateHash(), $"the runs differ after tick {a.TickNumber}");
        }
        World w = a.World;
        // Player 1's applied: a site of its own, a trained or training worker, the spawn.
        int sites = 0, units1 = 0, units0 = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 1 && w.Buildings.TypeId[k] == Type(a, 1, BuildingSlot.House)) sites++;
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            if (!w.Units.Alive[i]) continue;
            if (w.Units.Owner[i] == 0) units0++;
            else units1++;
        }
        Assert.Equal(1, sites);
        Assert.True(units1 >= 3, $"player 1 has {units1} units: its Train or spawn didn't apply");
        // Player 0's dropped: no building (its site collapsed, the Build didn't land), no unit (the SpawnUnit didn't land).
        Assert.Equal(0, w.Ledger.LiveBuildings[0]);
        Assert.Equal(0, units0);
        Assert.Equal(0, w.LiveUnits(0));
        Assert.Equal(0, a.PendingCommandCount);
        Assert.False(w.Units.IsAlive(a0));
        Assert.True(w.Units.Alive[a1.Index]);
    }

    /// <summary>Criterion 1 (e): the AI of a defeated seat sends nothing for 2,000 ticks after its defeat (M5-4e: its hall and its workers taken away).</summary>
    [Fact]
    public void ADefeatedSeatsAi_SendsNothing()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human));
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        for (int t = 0; t < 200; t++) sim.Tick();
        Assert.True(ai.CommandsSent > 0);
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0) Assert.True(w.Buildings.Free(w.Buildings.HandleOf(k)));
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        sim.Tick(); // its think at this tick's start came before the defeat
        Assert.True(w.IsDefeated(0));
        int sent = ai.CommandsSent, thinks = ai.Thinks;
        List<Command> log = AiScenes.Log(sim, 0);
        for (int t = 0; t < 2000; t++) sim.Tick();
        Assert.Empty(log);
        Assert.Equal(sent, ai.CommandsSent);
        Assert.Equal(thinks, ai.Thinks);
        Assert.Equal(0, w.LiveUnits(0));
    }

    /// <summary>
    /// Criterion 2: a recorded match on the generated map (two human seats) where player 0's infantry raze player 1's hall
    /// and then (M5-4e, shift-queued Attacks) its workers: player 1's commands after the defeat are logged (Enqueue accepts
    /// them) and dropped, and the recording plays back to equal checkpoints well past <see cref="Simulation.MatchOver"/>.
    /// </summary>
    [Fact]
    public void ARecordedMatchThatEndsInADefeat_PlaysBackPastMatchOver()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Human, SeatController.Human);
        Simulation sim = AiScenes.Generated(seats, 2, record: true, out ReplayRecorder? rec);
        World w = sim.World;
        sim.Tick();
        sim.Tick(); // the base's commands apply
        EntityHandle hall = default;
        var workers1 = new List<EntityHandle>();
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 1) hall = w.Buildings.HandleOf(k);
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) workers1.Add(new EntityHandle(i, w.Units.Generation[i]));
        int heavy = CombatScenes.HeavyInfantry;
        for (int k = 0; k < 16; k++) sim.Enqueue(Command.SpawnUnit(0, heavy, w.Units.Position[workers1[k % workers1.Count].Index] + new Vector2(0.4f * (k / 5), 0.5f)));
        Applied(sim);
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Units.TypeId[i] == heavy)
            {
                var h = new EntityHandle(i, w.Units.Generation[i]);
                sim.Enqueue(Command.Attack(0, h, hall, isBuilding: true));
                foreach (EntityHandle worker in workers1) sim.Enqueue(Command.Attack(0, h, worker, isBuilding: false, queued: true));
            }
        int defeatedAt = -1;
        // The loser's workers mill about one fixed point by their hall (pathing slice 1): a point that followed the first
        // worker let them drift 30 m out of every Heavy Infantry's sight before the hall fell, and whether a queued Attack on
        // them still found them then hung on where one worker happened to be pushed.
        Vector2 home = w.Units.Position[workers1[0].Index] + new Vector2(3f, 0f);
        while (sim.TickNumber < 6000 && (defeatedAt < 0 || sim.TickNumber < defeatedAt + 300))
        {
            if (sim.TickNumber % 25 == 0)
                foreach (EntityHandle h in workers1) sim.Enqueue(Command.Move(1, h, home));
            sim.Tick();
            if (defeatedAt < 0 && w.IsDefeated(1)) defeatedAt = sim.TickNumber;
        }
        Assert.True(defeatedAt > 0, $"player 1 stood: hall {w.Buildings.IsAlive(hall)}, {w.LiveUnits(1)} units");
        Assert.True(sim.MatchOver);
        Replay r = rec!.ToReplay();
        int after = r.Commands.Count(c => c.Player == 1 && c.Tick > defeatedAt);
        Assert.True(after > 0, "no command of the defeated player was logged");
        ReplayResult played = ReplayPlayer.Run(r, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(sim.TickNumber, played.TicksRun);
    }
}
