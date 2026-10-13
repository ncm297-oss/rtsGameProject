using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4b criterion 1-2: the defeat rule (docs/02 "Game flow" 4). A player whose last building (a site too) goes is defeated on
/// that tick, once, with an event; a player that never built never is; <see cref="Simulation.MatchOver"/> turns true when at
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

    /// <summary>Criterion 1 (a): a finished building razed, a site cancelled, a site razed: false to true on that very tick, one event, the match over.</summary>
    [Theory]
    [InlineData("finished razed")]
    [InlineData("site cancelled")]
    [InlineData("site razed")]
    public void ThePlayersLastBuildingGoing_DefeatsItOnThatTick(string how)
    {
        Simulation sim = CombatScenes.Flat(48, 64, 2);
        World w = sim.World;
        EntityHandle own = Building(sim, 0, 10, 10, site: how != "finished razed");
        Building(sim, 1, 34, 34);
        if (how == "site cancelled") sim.Enqueue(Command.Cancel(0, Centre(sim, own)));
        else
        {
            if (how == "finished razed") w.Buildings.SetHp(own.Index, 1);
            EntityHandle raider = CombatScenes.Place(sim, 1, CombatScenes.Raider, CombatScenes.At(sim, 9, 10));
            sim.Enqueue(Command.Attack(1, raider, own, isBuilding: true));
        }
        int ticks = 0;
        while (w.Buildings.IsAlive(own) && ticks++ < 400)
        {
            Assert.False(w.IsDefeated(0));
            Assert.False(sim.MatchOver);
            Assert.Equal(0, w.DefeatEvents.Length);
            sim.Tick();
        }
        Assert.False(w.Buildings.IsAlive(own), "the building stood");
        int tick = sim.TickNumber - 1;
        Assert.True(w.IsDefeated(0));
        Assert.False(w.IsDefeated(1));
        Assert.Equal(new[] { new DefeatEvent(0, tick) }, w.DefeatEvents.ToArray());
        Assert.True(sim.MatchOver);
        // Sticky, and announced once.
        sim.Tick();
        Assert.True(w.IsDefeated(0));
        Assert.Equal(0, w.DefeatEvents.Length);
        Assert.True(sim.MatchOver);
    }

    /// <summary>Criterion 1 (b): units and no building from tick 0: never defeated, never the match's end (one player ever built).</summary>
    [Fact]
    public void APlayerThatNeverBuilt_IsNeverDefeated_NorEndsTheMatch()
    {
        Simulation sim = CombatScenes.Flat(48, 64, 2);
        World w = sim.World;
        EntityHandle own = Building(sim, 0, 10, 10);
        CombatScenes.Place(sim, 1, CombatScenes.Raider, CombatScenes.At(sim, 36, 36));
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            Assert.False(w.IsDefeated(1));
            Assert.False(sim.MatchOver);
        }
        Assert.True(w.Buildings.Free(own));
        sim.Tick();
        Assert.True(w.IsDefeated(0));
        Assert.False(w.IsDefeated(1));
        Assert.False(w.EverBuilt(1));
        Assert.False(sim.MatchOver);
    }

    /// <summary>Criterion 1 (c): three players: one defeat leaves the match on, the second ends it; two on one tick are two events in player order.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThreePlayers_TheMatchEndsAtTheSecondDefeat(bool sameTick)
    {
        Simulation sim = CombatScenes.Flat(48, 64, 3);
        World w = sim.World;
        EntityHandle s0 = Building(sim, 0, 8, 8, site: true), s1 = Building(sim, 1, 30, 8, site: true);
        Building(sim, 2, 20, 30);
        sim.Tick();
        Assert.False(sim.MatchOver);
        // Player 1's cancel is sent first: the events still come in player order.
        sim.Enqueue(Command.Cancel(1, Centre(sim, s1)));
        if (sameTick) sim.Enqueue(Command.Cancel(0, Centre(sim, s0)));
        int at = Applied(sim);
        if (sameTick)
        {
            Assert.Equal(new[] { new DefeatEvent(0, at), new DefeatEvent(1, at) }, w.DefeatEvents.ToArray());
            Assert.True(sim.MatchOver);
            return;
        }
        Assert.Equal(new[] { new DefeatEvent(1, at) }, w.DefeatEvents.ToArray());
        Assert.False(sim.MatchOver);
        sim.Enqueue(Command.Cancel(0, Centre(sim, s0)));
        at = Applied(sim);
        Assert.Equal(new[] { new DefeatEvent(0, at) }, w.DefeatEvents.ToArray());
        Assert.True(sim.MatchOver);
        Assert.False(w.IsDefeated(2));
    }

    /// <summary>Two players losing their last buildings on one tick: both defeated, the match over with nobody standing.</summary>
    [Fact]
    public void TwoPlayers_BothOnOneTick_MatchOver()
    {
        Simulation sim = CombatScenes.Flat(48, 64, 2);
        EntityHandle s0 = Building(sim, 0, 8, 8, site: true), s1 = Building(sim, 1, 30, 8, site: true);
        sim.Enqueue(Command.Cancel(0, Centre(sim, s0)));
        sim.Enqueue(Command.Cancel(1, Centre(sim, s1)));
        int at = Applied(sim);
        Assert.Equal(new[] { new DefeatEvent(0, at), new DefeatEvent(1, at) }, sim.World.DefeatEvents.ToArray());
        Assert.True(sim.MatchOver);
    }

    /// <summary>
    /// Criterion 1 (d): after player 0's defeat its Move, Build and Train (and a SpawnUnit) are dropped where they apply, while
    /// player 1's same commands apply. The hash equals a twin's that sent Noops in their place (a command never sent would
    /// differ by the player's hashed sequence counter alone). The Move it got before the defeat keeps running; the Build
    /// shift-queued behind it is refused when it pops, as <see cref="World.CanPlace"/> says (<see cref="PlacementError.Defeated"/>).
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
            sim.Enqueue(Command.Move(0, worker0, CombatScenes.At(sim, 8, 40))); // applies with the cancel, before the defeat
            // Shift-queued behind the walk, also applied before the defeat: refused when it pops (a defeated player places nothing).
            sim.Enqueue(Command.Build(0, worker0, Type(sim, 0, BuildingSlot.House), CombatScenes.At(sim, 12, 42), queued: true));
            sim.Enqueue(Command.Cancel(0, Centre(sim, site0)));
            Applied(sim);
            Assert.True(w.IsDefeated(0));
            Assert.Equal(UnitState.Moving, w.Units.State[worker0.Index]);
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
        // Player 0's dropped: no building, its one worker, which finished the walk it had before the defeat (not the later Move).
        Assert.Equal(0, w.Ledger.LiveBuildings[0]);
        Assert.Equal(1, units0);
        Assert.Equal(0, a.PendingCommandCount);
        Assert.True(Vector2.Distance(w.Units.Position[a0.Index], CombatScenes.At(a, 8, 40)) < 2f, $"player 0's worker stands at {w.Units.Position[a0.Index]}");
        Assert.True(w.Units.Alive[a1.Index]);
    }

    /// <summary>Criterion 1 (e): the AI of a defeated seat sends nothing for 2,000 ticks after its defeat; its units stay.</summary>
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
        sim.Tick(); // its think at this tick's start came before the defeat
        Assert.True(w.IsDefeated(0));
        int sent = ai.CommandsSent, thinks = ai.Thinks;
        List<Command> log = AiScenes.Log(sim, 0);
        for (int t = 0; t < 2000; t++) sim.Tick();
        Assert.Empty(log);
        Assert.Equal(sent, ai.CommandsSent);
        Assert.Equal(thinks, ai.Thinks);
        int units = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0) units++;
        Assert.Equal(5, units);
    }

    /// <summary>
    /// Criterion 2: a recorded match on the generated map (two human seats) where player 0's infantry raze player 1's hall:
    /// player 1's commands after the defeat are logged (Enqueue accepts them) and dropped, and the recording plays back to
    /// equal checkpoints well past <see cref="Simulation.MatchOver"/>.
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
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Units.TypeId[i] == heavy) sim.Enqueue(Command.Attack(0, new EntityHandle(i, w.Units.Generation[i]), hall, isBuilding: true));
        int defeatedAt = -1;
        while (sim.TickNumber < 6000 && (defeatedAt < 0 || sim.TickNumber < defeatedAt + 300))
        {
            if (sim.TickNumber % 25 == 0)
                foreach (EntityHandle h in workers1) sim.Enqueue(Command.Move(1, h, w.Units.Position[workers1[0].Index] + new Vector2(3f, 0f)));
            sim.Tick();
            if (defeatedAt < 0 && w.IsDefeated(1)) defeatedAt = sim.TickNumber;
        }
        Assert.True(defeatedAt > 0, "player 1's hall stood");
        Assert.True(sim.MatchOver);
        Replay r = rec!.ToReplay();
        int after = r.Commands.Count(c => c.Player == 1 && c.Tick > defeatedAt);
        Assert.True(after > 0, "no command of the defeated player was logged");
        ReplayResult played = ReplayPlayer.Run(r, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(sim.TickNumber, played.TicksRun);
    }
}
