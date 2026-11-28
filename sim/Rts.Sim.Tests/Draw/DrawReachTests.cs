using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests.Draw;

/// <summary>
/// M6-SH3 B criterion 2 (BUG-1854 / BUG-1860 / BUG-1731; docs/02 "Game flow" 4, docs/01 2026-11-27 second row): the exact
/// "can still train", clause by clause, on staged two-seat worlds (seat 1 an Infantry Hall, a soldier and nothing else, so it
/// can never train and seat 0 decides). Every cost comes from the data. Each row but the last two failed on M6-S13's rule (the
/// base 0159c425): wood not counted, the map's and the carried gold counted without a drop-off, carried gold counted without a
/// live worker, a paid item waiting at the cap counted, no unit limit. The last two are guards the old rule also kept.
/// </summary>
public class DrawReachTests
{
    /// <summary>Seat 0: an Infantry Hall (a producer that is no drop-off) and nothing else; seat 1 spent; both banks 0.</summary>
    private static Simulation Base(GameData? data = null)
    {
        Simulation sim = DrawScenes.Empty(data ?? DrawScenes.Short, 2);
        DrawScenes.Building(sim, 0, 8, 8);
        DrawScenes.Building(sim, 1, 44, 44);
        DrawScenes.Unit(sim, 1, 44, 50);
        DrawScenes.Broke(sim);
        return sim;
    }

    /// <summary>Seat <paramref name="p"/>'s faction unit with the least gold cost (data order on ties).</summary>
    internal static UnitDef CheapestByGold(World w, int p)
    {
        UnitDef? best = null;
        foreach (int t in w.Data.Factions[w.FactionOf(p)].Units)
            if (best == null || w.Data.Units[t].CostGold < best.CostGold) best = w.Data.Units[t];
        return best!;
    }

    /// <summary>Seat <paramref name="p"/>'s faction unit with the least gold cost among those costing no wood.</summary>
    private static UnitDef CheapestGoldOnly(World w, int p)
    {
        UnitDef? best = null;
        foreach (int t in w.Data.Factions[w.FactionOf(p)].Units)
            if (w.Data.Units[t].CostWood == 0 && (best == null || w.Data.Units[t].CostGold < best.CostGold)) best = w.Data.Units[t];
        Assert.NotNull(best);
        return best!;
    }

    /// <summary>Forges a load of <paramref name="kind"/> on unit <paramref name="u"/> and recounts (a staged cargo: the store's own paths keep the counts).</summary>
    private static void Load(World w, EntityHandle u, ResourceKind kind, int amount)
    {
        w.Units.CargoKind[u.Index] = kind;
        w.Units.Cargo[u.Index] = amount;
        w.RecountDraw();
    }

    [Fact]
    public void Wood_IsCounted_GoldForTheCheapestUnitButNoWood_IsSpent()
    {
        Simulation sim = Base();
        World w = sim.World;
        UnitDef cheap = CheapestByGold(w, 0);
        Assert.True(cheap.CostWood > 0, "precondition: the faction's cheapest unit by gold needs wood");
        // No unit at that gold costs no wood (else the bank would pay for it).
        foreach (int t in w.Data.Factions[w.FactionOf(0)].Units)
            Assert.False(w.Data.Units[t].CostGold <= cheap.CostGold && w.Data.Units[t].CostWood == 0, $"precondition: {w.Data.Units[t].Key}");
        w.Ledger.Gold[0] = cheap.CostGold;
        Assert.False(w.CanStillTrain(0), "gold for the cheapest unit, no wood and no way to get wood");
        Assert.True(w.MatchSpent);
        w.Ledger.Wood[0] = cheap.CostWood - 1;
        Assert.False(w.CanStillTrain(0), "one wood short");
        w.Ledger.Wood[0] = cheap.CostWood;
        Assert.True(w.CanStillTrain(0), "gold and wood for it");
        Assert.False(w.MatchSpent);
        w.Ledger.Gold[0] = cheap.CostGold - 1;
        Assert.False(w.CanStillTrain(0), "one gold short");
    }

    [Fact]
    public void TheMapsAndTheCarriedResources_AreIgnored_WithoutAFinishedDropOff()
    {
        Simulation sim = Base();
        World w = sim.World;
        UnitDef unit = CheapestGoldOnly(w, 0);
        DrawScenes.GoldNode(sim, 30, 30, 10 * unit.CostGold);
        EntityHandle worker = DrawScenes.Unit(sim, 0, 20, 20, UnitSlot.Worker);
        Assert.Equal(0, w.Ledger.FinishedDropOffs[0]);
        Assert.False(w.CanStillTrain(0), "a worker and gold on the map, nowhere to deliver it");
        Load(w, worker, ResourceKind.Gold, unit.CostGold);
        Assert.False(w.CanStillTrain(0), "a full purse carried, nowhere to deliver it");
        Assert.True(w.MatchSpent);
        // A finished drop-off of its own: the map's gold and the load count.
        EntityHandle hall = DrawScenes.Building(sim, 0, 20, 8, BuildingSlot.TownHall);
        Assert.True(w.Data.Buildings[w.Buildings.TypeId[hall.Index]].DropOff);
        Assert.Equal(1, w.Ledger.FinishedDropOffs[0]);
        Assert.True(w.CanStillTrain(0));
        Assert.True(w.Buildings.Free(hall));
        Assert.Equal(0, w.Ledger.FinishedDropOffs[0]);
        Assert.False(w.CanStillTrain(0), "the drop-off razed");
    }

    [Fact]
    public void TheCarriedAndTheMapsResources_AreIgnored_WithoutALiveWorker()
    {
        Simulation sim = Base();
        World w = sim.World;
        UnitDef unit = CheapestGoldOnly(w, 0);
        DrawScenes.Building(sim, 0, 20, 8, BuildingSlot.TownHall); // a finished drop-off (and a producer)
        DrawScenes.GoldNode(sim, 30, 30, 10 * unit.CostGold);
        EntityHandle soldier = DrawScenes.Unit(sim, 0, 20, 20);
        Load(w, soldier, ResourceKind.Gold, unit.CostGold); // forged: only workers carry in play
        Assert.Equal(0, w.Ledger.LiveWorkers[0]);
        Assert.Equal(unit.CostGold, w.Ledger.CarriedGold[0]);
        Assert.False(w.CanStillTrain(0), "gold carried and on the map, no worker");
        EntityHandle worker = DrawScenes.Unit(sim, 0, 22, 20, UnitSlot.Worker);
        Assert.True(w.CanStillTrain(0));
        w.Units.Free(worker);
        Assert.False(w.CanStillTrain(0));
    }

    [Fact]
    public void ASite_IsNoDropOff_AndTheClockStops_OnTheTickItIsFinished()
    {
        Simulation sim = Base();
        World w = sim.World;
        UnitDef unit = CheapestGoldOnly(w, 0);
        DrawScenes.GoldNode(sim, 30, 30, 10 * unit.CostGold);
        DrawScenes.Unit(sim, 0, 20, 20, UnitSlot.Worker);
        EntityHandle site = DrawScenes.Building(sim, 0, 20, 8, BuildingSlot.TownHall, site: true);
        Assert.False(w.CanStillTrain(0), "a drop-off site is no drop-off");
        for (int t = 1; t <= 30; t++)
        {
            sim.Tick();
            Assert.Equal(t, w.DrawClock);
        }
        w.Buildings.SetWork(site.Index, w.Buildings.WorkNeeded(w.Buildings.TypeId[site.Index])); // finished between ticks
        Assert.False(w.Buildings.UnderConstruction[site.Index]);
        Assert.Equal(1, w.Ledger.FinishedDropOffs[0]);
        Assert.True(w.CanStillTrain(0));
        sim.Tick();
        Assert.Equal(0, w.DrawClock);
        Assert.False(w.MatchSpent);
    }

    [Fact]
    public void AQueuedPaidItem_AtTheCap_DoesNotHoldTheClock_WithRoomItDoes_InTrainingItDoes()
    {
        Simulation sim = Base();
        World w = sim.World;
        int hall = FirstBuilding(w, 0);
        int type = w.Data.UnitsTrainedAt(w.Buildings.TypeId[hall])[0];
        int item = w.Data.Units[type].ItemHalfPop;
        // Fill the population to within less than one item of the cap.
        while (w.HalfPopCap[0] - w.HalfPop[0] >= item) DrawScenes.Unit(sim, 0, 2 + w.HalfPop[0] % 20, 30);
        w.Buildings.Enqueue(hall, type); // paid before the bank ran dry
        Assert.Equal(1, w.Ledger.QueuedUnits[0]);
        Assert.Equal(1, w.Ledger.WaitingUnits(0, type));
        Assert.False(w.CanStillTrain(0), "a paid item waiting at the cap, the bank empty");
        Assert.True(w.MatchSpent);
        for (int t = 1; t <= 20; t++) sim.Tick();
        Assert.Equal(20, w.DrawClock);
        Assert.Equal(0, w.Ledger.TrainingUnits[0]);
        // A House finished: room for the item now; it holds the clock and starts on the next tick.
        EntityHandle house = DrawScenes.Building(sim, 0, 30, 8, BuildingSlot.House);
        Assert.True(w.HalfPopCap[0] - w.HalfPop[0] >= item, "precondition: the house makes room");
        Assert.True(w.CanStillTrain(0), "a waiting item with room");
        sim.Tick();
        Assert.Equal(0, w.DrawClock);
        Assert.Equal(1, w.Ledger.TrainingUnits[0]);
        Assert.Equal(0, w.Ledger.WaitingUnits(0, type));
        // The house gone again: the item in training keeps its reservation and holds the clock.
        Assert.True(w.Buildings.Free(house));
        Assert.True(w.CanStillTrain(0), "an item in training");
        sim.Tick();
        Assert.Equal(0, w.DrawClock);
        // Cancelled: nothing queued; the refund is in the bank and pays for the same unit again.
        w.Buildings.RemoveQueued(hall, 0);
        Assert.Equal(0, w.Ledger.TrainingUnits[0]);
        Assert.Equal(0, w.Ledger.QueuedUnits[0]);
        Assert.True(w.CanStillTrain(0));
    }

    private static readonly Lazy<GameData> s_heavyCapped = new(() => DrawScenes.DataWith(DrawScenes.ShortSeconds, dir =>
    {
        MaxAliveLoaderTests.Strip(dir);
        dir.SetUnitField("malazan", MaxAliveLoaderTests.Heavy, "maxAlive", "7");
        // So a bank of exactly the Heavy's cost pays for no other Malazan unit (the Laborer costs less gold and no wood).
        dir.SetUnitField("malazan", "malazan_laborer", "cost.gold", "60");
    }));

    [Fact]
    public void AUnitTypeAtItsMaxAlive_IsSkipped()
    {
        GameData d = s_heavyCapped.Value;
        Simulation sim = Base(d);
        World w = sim.World;
        int heavy = UnitLimitTests.HeavyOf(d);
        UnitDef def = d.Units[heavy];
        w.Ledger.Gold[0] = def.CostGold;
        w.Ledger.Wood[0] = def.CostWood;
        foreach (int t in d.Factions[w.FactionOf(0)].Units)
            if (t != heavy) Assert.False(d.Units[t].CostGold <= def.CostGold && d.Units[t].CostWood <= def.CostWood, $"precondition: the bank also pays for {d.Units[t].Key}");
        UnitLimitTests.PlaceLive(sim, 0, heavy, def.MaxAlive - 1);
        Assert.True(w.CanStillTrain(0), "one below the limit");
        UnitLimitTests.PlaceLive(sim, 0, heavy, 1);
        Assert.Equal(def.MaxAlive, w.LimitedCount(0, heavy));
        Assert.False(w.CanStillTrain(0), "the only unit it can pay for is at its limit");
        Assert.True(w.MatchSpent);
    }

    [Fact]
    public void ARichSeatAtItsPopulationCap_IsNotSpent()
    {
        Simulation sim = Base();
        World w = sim.World;
        while (w.HalfPop[0] < w.HalfPopCap[0] || w.HalfPopCap[0] == 0 && w.HalfPop[0] == 0) DrawScenes.Unit(sim, 0, 2 + w.HalfPop[0] % 20, 30);
        w.Ledger.Gold[0] = 10_000;
        w.Ledger.Wood[0] = 10_000;
        Assert.True(w.HalfPop[0] >= w.HalfPopCap[0], "precondition: at the cap");
        Assert.True(w.CanStillTrain(0), "room is not asked in (b): it can build houses or lose units");
        Assert.False(w.MatchSpent);
    }

    private static readonly Lazy<GameData> s_free = new(() => DrawScenes.DataWith(DrawScenes.ShortSeconds, dir =>
    {
        dir.SetUnitField("malazan", "malazan_crossbowman", "cost.gold", "0");
        dir.SetUnitField("malazan", "malazan_crossbowman", "cost.wood", "0");
    }));

    private static readonly Lazy<GameData> s_noGoldSomeWood = new(() => DrawScenes.DataWith(DrawScenes.ShortSeconds, dir =>
        dir.SetUnitField("malazan", "malazan_crossbowman", "cost.gold", "0")));

    /// <summary>A unit that costs nothing: the seat is never spent, with nothing to produce from; one that costs no gold but wood is judged by its wood.</summary>
    [Fact]
    public void AZeroCostUnit_IsNeverSpent_AZeroGoldUnitIsJudgedByItsWood()
    {
        Simulation sim = DrawScenes.Spent(s_free.Value);
        Assert.True(sim.World.CanStillTrain(0));
        Assert.False(sim.World.MatchSpent);
        DrawScenes.RunToEnd(sim, 3 * DrawScenes.ShortTicks);
        Assert.False(sim.World.MatchDrawn);
        Simulation wood = DrawScenes.Spent(s_noGoldSomeWood.Value);
        World w = wood.World;
        Assert.False(w.CanStillTrain(0), "0 gold but wood, and no wood");
        w.Ledger.Wood[0] = w.Data.Units[w.Data.FindUnit("malazan_crossbowman")].CostWood;
        Assert.True(w.CanStillTrain(0));
    }

    private static int FirstBuilding(World w, int p)
    {
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == p) return i;
        return -1;
    }
}
