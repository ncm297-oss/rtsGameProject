using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests.Draw;

/// <summary>
/// M6-S13 criteria 2-4 (docs/02 "Game flow" 4, docs/03 "Implementation (M6-S13)"): "can still train" clause by clause on staged
/// worlds, "spent" needing a live match, and the clock and the draw (wait <see cref="DrawScenes.ShortTicks"/> ticks).
/// </summary>
public class DrawRuleTests
{
    // ---------- criterion 2: can still train, clause by clause ----------

    /// <summary>A two-seat world on the short-wait data, both seats an Infantry Hall and broke; seat 0's clauses are then set one at a time.</summary>
    private static Simulation Base(bool hall = true)
    {
        Simulation sim = DrawScenes.Empty(DrawScenes.Short, 2);
        if (hall) DrawScenes.Building(sim, 0, 8, 8);
        DrawScenes.Building(sim, 1, 44, 44);
        DrawScenes.Unit(sim, 1, 44, 50);
        DrawScenes.Broke(sim);
        return sim;
    }

    [Fact]
    public void Broke_WithAHall_CannotTrain_AndTheMatchIsSpent()
    {
        Simulation sim = Base();
        Assert.False(sim.World.CanStillTrain(0));
        Assert.False(sim.World.CanStillTrain(1));
        Assert.True(sim.World.MatchSpent);
    }

    [Fact]
    public void AUnitInAQueue_CanTrain_AResearchItemDoesNot()
    {
        Simulation sim = Base();
        World w = sim.World;
        int hall = FirstBuilding(w, 0);
        int keep = DrawScenes.Building(sim, 0, 20, 8, BuildingSlot.TownHall).Index;
        int tech = AnyTechAt(w, w.Buildings.TypeId[keep]);
        Assert.True(tech >= 0);
        w.Buildings.EnqueueTech(keep, tech);
        Assert.False(w.CanStillTrain(0), "a research item is not a unit in a queue");
        Assert.True(w.MatchSpent);
        w.Buildings.RemoveQueued(keep, 0);
        DrawScenes.Broke(sim); // the refund
        w.Buildings.Enqueue(hall, w.Data.UnitsTrainedAt(w.Buildings.TypeId[hall])[0]);
        Assert.True(w.CanStillTrain(0));
        Assert.False(w.MatchSpent);
        w.Buildings.RemoveQueued(hall, 0);
        DrawScenes.Broke(sim);
        Assert.False(w.CanStillTrain(0));
    }

    /// <summary>M6-SH3: the bank at the cheapest unit's gold and wood can train, one gold below cannot (M6-S13 read the gold alone; wood is now counted: <see cref="DrawReachTests"/>).</summary>
    [Fact]
    public void TheBank_AtTheCheapestUnitsCost_CanTrain_OneBelowCannot()
    {
        Simulation sim = Base();
        int cheapest = DrawScenes.Cheapest(sim, 0);
        Assert.True(cheapest > 0);
        sim.World.Ledger.Gold[0] = cheapest;
        sim.World.Ledger.Wood[0] = DrawReachTests.CheapestByGold(sim.World, 0).CostWood;
        Assert.True(sim.World.CanStillTrain(0));
        sim.World.Ledger.Gold[0] = cheapest - 1;
        Assert.False(sim.World.CanStillTrain(0));
        // Wood alone buys nothing.
        sim.World.Ledger.Wood[0] = 100_000;
        Assert.False(sim.World.CanStillTrain(0));
    }

    [Fact]
    public void GoldCarriedByAnOwnUnit_ClosesTheGap()
    {
        Simulation sim = Base();
        World w = sim.World;
        int cheapest = DrawScenes.Cheapest(sim, 0);
        w.Ledger.Gold[0] = cheapest - 3;
        w.Ledger.Wood[0] = DrawReachTests.CheapestByGold(w, 0).CostWood;
        DrawScenes.Building(sim, 0, 30, 8, BuildingSlot.TownHall); // M6-SH3: a load counts only with a finished drop-off
        EntityHandle worker = DrawScenes.Unit(sim, 0, 20, 20, UnitSlot.Worker);
        Assert.False(w.CanStillTrain(0), "a worker with no gold anywhere");
        w.Units.CargoKind[worker.Index] = ResourceKind.Gold;
        w.Units.Cargo[worker.Index] = 2;
        w.RecountDraw(); // a staged cargo: the store's own paths keep the count
        Assert.False(w.CanStillTrain(0));
        w.Units.Cargo[worker.Index] = 3;
        w.RecountDraw();
        Assert.True(w.CanStillTrain(0));
        // A load of wood is not gold.
        w.Units.CargoKind[worker.Index] = ResourceKind.Wood;
        w.RecountDraw();
        Assert.False(w.CanStillTrain(0));
    }

    [Fact]
    public void GoldLeftInANode_CountsWithALiveWorker_AndNotWithout()
    {
        Simulation sim = Base();
        World w = sim.World;
        int cheapest = DrawScenes.Cheapest(sim, 0);
        w.Ledger.Wood[0] = DrawReachTests.CheapestByGold(w, 0).CostWood;
        DrawScenes.Building(sim, 0, 30, 8, BuildingSlot.TownHall); // M6-SH3: the map counts only with a finished drop-off
        DrawScenes.GoldNode(sim, 30, 30, cheapest);
        Assert.Equal(cheapest, w.Resources.GoldLeft);
        Assert.False(w.CanStillTrain(0), "the hall alone cannot mine");
        EntityHandle worker = DrawScenes.Unit(sim, 0, 20, 20, UnitSlot.Worker);
        Assert.Equal(1, w.Ledger.LiveWorkers[0]);
        Assert.True(w.CanStillTrain(0));
        w.Units.Free(worker);
        Assert.Equal(0, w.Ledger.LiveWorkers[0]);
        Assert.False(w.CanStillTrain(0));
    }

    [Fact]
    public void ALiveWorker_IsAProducer_WithoutAHall()
    {
        Simulation sim = Base(hall: false);
        World w = sim.World;
        DrawScenes.Building(sim, 0, 8, 8, BuildingSlot.House);
        w.Ledger.Gold[0] = 10_000;
        Assert.False(w.CanStillTrain(0), "gold, a house, no worker");
        DrawScenes.Unit(sim, 0, 20, 20, UnitSlot.Worker);
        Assert.True(w.CanStillTrain(0));
    }

    [Fact]
    public void TheGold_ButNeitherABuildingThatTrainsNorAWorker_CannotTrain()
    {
        Simulation sim = Base(hall: false);
        World w = sim.World;
        DrawScenes.Building(sim, 0, 8, 8, BuildingSlot.House);
        DrawScenes.Unit(sim, 0, 20, 20);
        w.Ledger.Gold[0] = 10_000;
        Assert.False(w.CanStillTrain(0));
    }

    [Fact]
    public void ASite_IsNotABuilding()
    {
        Simulation sim = Base(hall: false);
        World w = sim.World;
        DrawScenes.Building(sim, 0, 8, 8, site: true);
        DrawScenes.Unit(sim, 0, 20, 20);
        w.Ledger.Gold[0] = 10_000;
        Assert.Equal(0, w.ProductionBuildings(0));
        Assert.False(w.CanStillTrain(0));
    }

    [Fact]
    public void ADefeatedSeat_IsIgnored()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short, players: 3);
        World w = sim.World;
        Assert.True(w.MatchSpent);
        w.Ledger.Gold[2] = 10_000;
        Assert.False(w.MatchSpent, "seat 2 can train");
        w.Ledger.Defeated[2] = true; // forged: the flag alone, its hall and gold kept
        Assert.True(w.MatchSpent, "a defeated seat is not looked at");
    }

    /// <summary>A forged faction with a unit that costs nothing (rule 58: the unit is found by id and edited) is never spent, even with nothing to produce from. M6-SH3: nothing = no gold and no wood (a unit of 0 gold that needs wood is judged by its wood: <see cref="DrawReachTests"/>).</summary>
    [Fact]
    public void AForgedFactionWithAZeroCostUnit_IsNeverSpent()
    {
        GameData data = DrawScenes.DataWith(DrawScenes.ShortSeconds, dir => dir.EditJson("factions/malazan/units.json", root =>
        {
            foreach (JsonNode? u in root["units"]!.AsArray())
                if ((string)u!["id"]! == "malazan_crossbowman")
                {
                    u["cost"]!["gold"] = 0;
                    u["cost"]!["wood"] = 0;
                }
        }));
        Simulation sim = DrawScenes.Spent(data);
        World w = sim.World;
        Assert.True(w.CanStillTrain(0));
        Assert.False(w.MatchSpent);
        DrawScenes.RunToEnd(sim, 3 * DrawScenes.ShortTicks);
        Assert.False(w.MatchDrawn);
        Assert.Equal(0, w.DrawClock);
    }

    // ---------- criterion 3: spent needs a live match ----------

    [Fact]
    public void FewerThanTwoStandingSidesThatBuilt_IsNeverSpent()
    {
        Simulation sim = DrawScenes.Empty(DrawScenes.Short, 2);
        DrawScenes.Building(sim, 0, 8, 8);
        DrawScenes.Unit(sim, 1, 40, 40); // seat 1 never built
        DrawScenes.Broke(sim);
        Assert.False(sim.World.MatchSpent);
        DrawScenes.RunToEnd(sim, 3 * DrawScenes.ShortTicks);
        Assert.False(sim.World.MatchDrawn);
        Assert.Equal(0, sim.World.DrawClock);
    }

    [Fact]
    public void ASandbox_NoSeatEverBuilt_IsNeverSpent()
    {
        Simulation sim = DrawScenes.Empty(DrawScenes.Short, 2);
        DrawScenes.Unit(sim, 0, 8, 8);
        DrawScenes.Unit(sim, 1, 40, 40);
        DrawScenes.Broke(sim);
        Assert.False(sim.World.MatchSpent);
        for (int t = 0; t < 3 * DrawScenes.ShortTicks; t++) sim.Tick();
        Assert.False(sim.World.MatchOver);
        Assert.Equal(0, sim.World.DrawClock);
    }

    [Fact]
    public void AfterAWin_IsNeverSpent()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short);
        World w = sim.World;
        // Seat 1 out: its hall and soldiers gone.
        FreeAllOf(w, 1);
        sim.Tick();
        Assert.True(w.IsDefeated(1));
        Assert.True(sim.MatchOver);
        Assert.False(w.MatchSpent);
        for (int t = 0; t < 3 * DrawScenes.ShortTicks; t++) sim.Tick();
        Assert.False(w.MatchDrawn);
        Assert.Equal(0, w.DrawClock);
        Assert.Equal(-1, w.DrawTicksLeft);
    }

    [Fact]
    public void TwoAlliedSeatsAlone_AreOneSide_NeverSpent()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short, teams: new[] { 1, 1 });
        Assert.False(sim.World.MatchSpent);
        for (int t = 0; t < 3 * DrawScenes.ShortTicks; t++) sim.Tick();
        Assert.False(sim.World.MatchDrawn);
        // Two teams of two (2v2), all four broke: two sides, spent.
        Simulation two = DrawScenes.Spent(DrawScenes.Short, players: 4, teams: new[] { 1, 2, 1, 2 });
        Assert.True(two.World.MatchSpent);
    }

    // ---------- criterion 4: the clock and the draw ----------

    [Fact]
    public void TheDraw_FallsOnTheFirstTickTheClockReachesTheWait()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short);
        World w = sim.World;
        int n = DrawScenes.ShortTicks;
        Assert.Equal(n, w.DrawWaitTicks);
        int buildings = w.Buildings.Count, units = w.Units.Count;
        for (int t = 1; t < n; t++)
        {
            sim.Tick();
            Assert.Equal(t, w.DrawClock);
            Assert.Equal(n - t, w.DrawTicksLeft);
            Assert.False(sim.MatchOver);
        }
        Assert.Equal(n - 1, w.DrawClock); // N - 1: not yet
        Assert.False(w.MatchDrawn);
        sim.Tick();
        Assert.Equal(n, w.DrawClock); // N: drawn on this tick
        Assert.True(w.MatchDrawn);
        Assert.True(sim.MatchOver);
        Assert.Equal(-1, w.DrawTicksLeft);
        Assert.False(w.IsDefeated(0));
        Assert.False(w.IsDefeated(1));
        Assert.Equal(0, w.DefeatEvents.Length);
        Assert.Equal(0, w.Deaths.Length);
        Assert.Equal(buildings, w.Buildings.Count);
        Assert.Equal(units, w.Units.Count);
        int drawnAt = sim.TickNumber;
        for (int t = 0; t < 1000; t++)
        {
            sim.Tick();
            Assert.True(w.MatchDrawn);
            Assert.True(sim.MatchOver);
            Assert.Equal(0, w.DefeatEvents.Length);
        }
        Assert.Equal(drawnAt + 1000, sim.TickNumber);
        Assert.False(w.IsDefeated(0) || w.IsDefeated(1));
        Assert.Equal(buildings, w.Buildings.Count);
    }

    [Fact]
    public void ANonSummonedUnitsDeath_AtClockNMinus1_RestartsIt()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short);
        World w = sim.World;
        int n = DrawScenes.ShortTicks;
        for (int t = 1; t < n; t++) sim.Tick();
        Assert.Equal(n - 1, w.DrawClock);
        w.Units.Free(FirstUnit(w, 0)); // dies before the next tick's check
        sim.Tick();
        Assert.Equal(0, w.DrawClock);
        Assert.False(w.MatchDrawn);
        sim.Tick();
        Assert.Equal(1, w.DrawClock);
        for (int t = 2; t <= n; t++) sim.Tick();
        Assert.True(w.MatchDrawn);
    }

    [Fact]
    public void AKillInTheTick_RestartsIt()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short);
        World w = sim.World;
        for (int t = 0; t < 50; t++) sim.Tick();
        Assert.Equal(50, w.DrawClock);
        // One of seat 1's soldiers at 1 hp beside a seat-0 soldier ordered to attack it.
        EntityHandle victim = FirstUnit(w, 1), killer = FirstUnit(w, 0);
        w.Units.Position[victim.Index] = w.Units.Position[killer.Index] + new System.Numerics.Vector2(0.9f, 0f);
        w.Units.Hp[victim.Index] = 1;
        CombatScenes.Spot(sim, 0, victim);
        w.Units.Hold[killer.Index] = false;
        sim.Enqueue(Command.Attack(0, killer, victim, isBuilding: false));
        int last = w.DrawClock;
        bool reset = false;
        for (int t = 0; t < 200 && w.Units.IsAlive(victim); t++)
        {
            sim.Tick();
            if (w.Units.IsAlive(victim)) last = w.DrawClock;
            else reset = w.DrawClock == 0;
        }
        Assert.False(w.Units.IsAlive(victim), "the soldier lived");
        Assert.True(reset, $"the clock read {w.DrawClock} on the death's tick (was {last})");
    }

    [Fact]
    public void ASummonsExpiryOrDeath_DoesNotRestartIt()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short);
        World w = sim.World;
        int type = Rts.Sim.ViewApi.StartBase.UnitOfSlot(w.Data, w.FactionOf(0), UnitSlot.Line);
        Assert.True(w.Units.TrySpawn(0, type, w.Data.Units[type], CombatScenes.At(sim, 30, 30), 30, out EntityHandle expiring));
        Assert.True(w.Units.TrySpawn(0, type, w.Data.Units[type], CombatScenes.At(sim, 32, 30), int.MaxValue, out EntityHandle killed));
        for (int t = 1; t <= 40; t++)
        {
            sim.Tick();
            Assert.Equal(t, w.DrawClock);
        }
        Assert.False(w.Units.IsAlive(expiring), "the summon did not expire");
        w.Units.Free(killed);
        sim.Tick();
        Assert.Equal(41, w.DrawClock);
    }

    [Fact]
    public void ASeatsDefeat_RestartsIt_ThreeSides()
    {
        Simulation sim = DrawScenes.Empty(DrawScenes.Short, 3);
        World w = sim.World;
        for (int p = 0; p < 2; p++)
        {
            (int x, int y) = DrawScenes.Corner(sim, p);
            DrawScenes.Building(sim, p, x, y);
            DrawScenes.Unit(sim, p, x, y + 5);
        }
        (int x2, int y2) = DrawScenes.Corner(sim, 2);
        EntityHandle hall2 = DrawScenes.Building(sim, 2, x2, y2); // seat 2: a hall and no unit
        DrawScenes.Broke(sim);
        for (int t = 1; t <= 30; t++) sim.Tick();
        Assert.Equal(30, w.DrawClock);
        Assert.True(w.Buildings.Free(hall2)); // no unit dies: seat 2 is out on the next tick
        sim.Tick();
        Assert.True(w.IsDefeated(2));
        Assert.Single(w.DefeatEvents.ToArray());
        Assert.Equal(0, w.DrawClock);
        Assert.False(sim.MatchOver);
        sim.Tick();
        Assert.Equal(1, w.DrawClock);
    }

    [Fact]
    public void ACancelThatRefundsEnoughGold_MakesItNotSpent_AndRestartsIt()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short);
        World w = sim.World;
        EntityHandle hall = DrawScenes.Building(sim, 0, 20, 8, BuildingSlot.TownHall);
        int tech = AnyTechAt(w, w.Buildings.TypeId[hall.Index]);
        Assert.True(tech >= 0, "the Town Hall researches nothing");
        Assert.True(w.Data.Techs[tech].CostGold >= DrawScenes.Cheapest(sim, 0), "the tech's refund does not reach the cheapest unit");
        w.Buildings.EnqueueTech(hall.Index, tech); // paid before the bank ran dry (the queue holds it)
        for (int t = 1; t <= 30; t++) sim.Tick();
        Assert.Equal(30, w.DrawClock);
        sim.Enqueue(Command.CancelTrain(0, DrawScenes.Centre(sim, hall), 0)); // applies in the tick after the next
        sim.Tick();
        Assert.Equal(31, w.DrawClock);
        sim.Tick();
        Assert.True(w.Gold[0] >= DrawScenes.Cheapest(sim, 0));
        Assert.Equal(0, w.DrawClock);
        Assert.False(w.MatchSpent);
        for (int t = 0; t < 3 * DrawScenes.ShortTicks; t++) sim.Tick();
        Assert.False(w.MatchDrawn);
    }

    [Fact]
    public void AWinOnTheTickTheWaitEnds_IsAWin()
    {
        // Seat 1 holds only its hall (no unit), so its fall on the last tick of the wait is a defeat without a death.
        Simulation sim = DrawScenes.Empty(DrawScenes.Short, 2);
        World w = sim.World;
        DrawScenes.Building(sim, 0, 8, 8);
        DrawScenes.Unit(sim, 0, 8, 13);
        EntityHandle hall1 = DrawScenes.Building(sim, 1, 44, 44);
        DrawScenes.Broke(sim);
        int n = DrawScenes.ShortTicks;
        for (int t = 1; t < n; t++) sim.Tick();
        Assert.Equal(n - 1, w.DrawClock);
        Assert.True(w.Buildings.Free(hall1));
        sim.Tick();
        Assert.True(w.IsDefeated(1));
        Assert.True(sim.MatchOver);
        Assert.False(w.MatchDrawn);
        Assert.Equal(0, w.DrawClock);
    }

    [Fact]
    public void ASeatThatBuildsAfterTheDraw_DoesNotTakeItBack()
    {
        // Seat 2 has a unit and never built: it is no side of the result, and builds only after the draw.
        Simulation s2 = DrawScenes.Empty(DrawScenes.Short, 3);
        for (int p = 0; p < 2; p++)
        {
            (int x, int y) = DrawScenes.Corner(s2, p);
            DrawScenes.Building(s2, p, x, y);
        }
        DrawScenes.Unit(s2, 2, 30, 30); // seat 2: a unit, never built, broke
        DrawScenes.Broke(s2);
        DrawScenes.RunToEnd(s2, 3 * DrawScenes.ShortTicks);
        Assert.True(s2.World.MatchDrawn);
        (int x2, int y2) = DrawScenes.Corner(s2, 2);
        DrawScenes.Building(s2, 2, x2, y2);
        s2.World.Ledger.Gold[2] = 10_000;
        for (int t = 0; t < 50; t++) s2.Tick();
        Assert.True(s2.World.MatchDrawn);
        Assert.True(s2.MatchOver);
    }

    // ---------- helpers ----------

    private static int FirstBuilding(World w, int p)
    {
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == p) return i;
        return -1;
    }

    private static EntityHandle FirstUnit(World w, int p)
    {
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == p) return new EntityHandle(i, w.Units.Generation[i]);
        throw new InvalidOperationException($"no unit of {p}");
    }

    private static void FreeAllOf(World w, int p)
    {
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == p) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == p) w.Buildings.Free(new EntityHandle(i, w.Buildings.Generation[i]));
    }

    /// <summary>The tech researched at building type <paramref name="type"/>'s slot costing the most gold, or -1.</summary>
    private static int AnyTechAt(World w, int type)
    {
        BuildingSlot slot = w.Data.Buildings[type].Slot;
        int best = -1;
        for (int t = 0; t < w.Data.Techs.Length; t++)
            if (w.Data.Techs[t].ResearchedAtSlot == slot && (w.Data.Techs[t].Faction < 0 || w.Data.Techs[t].Faction == w.Data.Buildings[type].Faction) && (best < 0 || w.Data.Techs[t].CostGold > w.Data.Techs[best].CostGold)) best = t;
        return best;
    }
}
