using System.Collections.Immutable;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;

namespace Rts.Sim.Tests.Draw;

/// <summary>
/// M6-S15 A criterion 1 (BUG-1893; docs/02 "Game flow" 4, docs/01 2026-11-27-1857 (2)): the draw rule's drop-off clause. A
/// seat with a live worker and no finished drop-off counts the carried and map resources when the bank pays one of its
/// faction's drop-off buildings and the bank less that price, plus the carried and map resources, pays a unit. The first four
/// rows, the forged ten-wood row and the save row failed on the base d26338d3 (the seat was called spent); the four Guard_ rows held on the base too. Every
/// price comes from the data.
/// </summary>
public class DropOffClauseTests
{
    /// <summary>
    /// Seat 0 of <paramref name="faction"/>: an Infantry Hall (a producer, no drop-off), a worker, a gold mine of 2,000 on the
    /// map, no drop-off; seat 1 (Malazan) spent: an Infantry Hall and a soldier, bank 0.
    /// </summary>
    internal static Simulation Scene(int faction, GameData? data, out EntityHandle worker, bool node = true)
    {
        data ??= DrawScenes.Short;
        var seats = ImmutableArray.Create(
            new PlayerSeat(faction, SeatController.Human, AiDifficulty.Normal),
            new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal));
        var sim = new Simulation(new SimConfig(1, 2, 128, 1088) { Data = data, Seats = seats }, LocalMovementTests.Flat(64));
        DrawScenes.Building(sim, 0, 8, 8);
        DrawScenes.Building(sim, 1, 44, 44);
        DrawScenes.Unit(sim, 1, 44, 50);
        if (node) DrawScenes.GoldNode(sim, 34, 30, 2000);
        worker = DrawScenes.Unit(sim, 0, 20, 14, UnitSlot.Worker);
        DrawScenes.Broke(sim);
        Assert.Equal(0, sim.World.Ledger.FinishedDropOffs[0]);
        return sim;
    }

    /// <summary>The cheapest drop-off building of <paramref name="faction"/> by gold, then wood (data order on ties).</summary>
    internal static BuildingDef SmallDropOff(GameData d, int faction)
    {
        BuildingDef? best = null;
        foreach (BuildingDef b in d.Buildings)
            if (b.DropOff && b.Faction == faction && (best == null || b.CostGold < best.CostGold || b.CostGold == best.CostGold && b.CostWood < best.CostWood)) best = b;
        return best!;
    }

    /// <summary>The BUG-1893 scene on shipped data: a Malazan seat with a Laborer, an Infantry Hall, no drop-off, a gold mine, bank 35 gold / 100 wood.</summary>
    [Fact]
    public void Malazan_ALaborerNoDropOffAndABankThatPaysTheDepot_IsNotSpent()
    {
        Simulation sim = Scene(0, null, out _);
        World w = sim.World;
        BuildingDef depot = SmallDropOff(w.Data, 0);
        Assert.Equal(35, DrawScenes.Cheapest(sim, 0) - 1); // the brief's bank: one gold short of the Crossbowman
        w.Ledger.Gold[0] = 35;
        w.Ledger.Wood[0] = 100;
        Assert.True(w.Wood[0] >= depot.CostWood && w.Gold[0] >= depot.CostGold, $"precondition: the bank pays {depot.Key}");
        Assert.False(DrawReachScan.Sh3Can(w, 0), "M6-SH3's rule called it spent");
        Assert.True(w.CanStillTrain(0), $"the bank buys {depot.Key} and the mine's gold then pays a Laborer: {DrawReachScan.Line(w, 0)}");
        Assert.False(w.MatchSpent);
        sim.Tick();
        Assert.Equal(0, w.DrawClock);
    }

    /// <summary>The same for a Whirlwind and a Teblor seat: one gold short of its cheapest unit, the small drop-off's price in wood.</summary>
    [Theory]
    [InlineData("whirlwind")]
    [InlineData("teblor")]
    public void OtherFactions_ABankThatPaysTheSmallDropOff_IsNotSpent(string factionKey)
    {
        int faction = DrawScenes.Short.FindFaction(factionKey);
        Simulation sim = Scene(faction, null, out _);
        World w = sim.World;
        BuildingDef small = SmallDropOff(w.Data, faction);
        w.Ledger.Gold[0] = DrawScenes.Cheapest(sim, 0) - 1 + small.CostGold;
        w.Ledger.Wood[0] = small.CostWood;
        Assert.False(DrawReachScan.Sh3Can(w, 0), $"M6-SH3's rule called it spent: {DrawReachScan.Line(w, 0)}");
        Assert.True(w.CanStillTrain(0), $"{small.Key} bought, the mine's gold pays its worker: {DrawReachScan.Line(w, 0)}");
        Assert.False(w.MatchSpent);
    }

    private static readonly Lazy<GameData> s_hallOnly = new(() => DrawScenes.DataWith(DrawScenes.ShortSeconds, dir =>
    {
        // The Depot is no drop-off: the Malazan's only drop-off type is the hall (275 / 275).
        dir.EditJson("factions/malazan/buildings.json", root =>
        {
            foreach (System.Text.Json.Nodes.JsonNode? b in root["buildings"]!.AsArray())
                if ((string?)b!["id"] == "malazan_depot") b["dropOff"] = false;
        });
        // Every Malazan unit above the hall's gold, so a bank of the hall's price pays no unit by itself.
        foreach (string u in new[] { "malazan_laborer", "malazan_heavy_infantry", "malazan_crossbowman", "malazan_wickan_lancer", "malazan_cadre_mage", "malazan_catapult", "malazan_sapper" })
            dir.SetUnitField("malazan", u, "cost.gold", "300");
    }));

    /// <summary>Forged: the seat's only drop-off type is hall-priced and the bank pays it (and no unit by itself).</summary>
    [Fact]
    public void Forged_TheOnlyDropOffIsHallPriced_AndTheBankPaysIt_IsNotSpent()
    {
        GameData d = s_hallOnly.Value;
        Simulation sim = Scene(0, d, out _);
        World w = sim.World;
        BuildingDef hall = SmallDropOff(d, 0);
        Assert.Equal(BuildingSlot.TownHall, hall.Slot);
        w.Ledger.Gold[0] = hall.CostGold;
        w.Ledger.Wood[0] = hall.CostWood;
        Assert.False(DrawReachScan.Sh3Can(w, 0));
        Assert.True(w.CanStillTrain(0), DrawReachScan.Line(w, 0));
        w.Ledger.Wood[0] = hall.CostWood - 1;
        Assert.False(w.CanStillTrain(0), "one wood short of the hall: no drop-off the bank pays");
    }

    /// <summary>Guard (green on the base): the BUG-1893 scene with 74 wood, one short of the Depot, is spent.</summary>
    [Fact]
    public void Guard_OneWoodShortOfTheDepot_IsSpent()
    {
        Simulation sim = Scene(0, null, out _);
        World w = sim.World;
        BuildingDef depot = SmallDropOff(w.Data, 0);
        w.Ledger.Gold[0] = 35;
        w.Ledger.Wood[0] = depot.CostWood - 1;
        Assert.Equal(74, w.Wood[0]);
        Assert.False(w.CanStillTrain(0));
        Assert.True(w.MatchSpent);
    }

    /// <summary>Guard (green on the base): the worker dead, the seat is spent from that tick (an Infantry Hall and 35 gold pay for nothing).</summary>
    [Fact]
    public void Guard_TheWorkerDead_IsSpentFromThatTick()
    {
        Simulation sim = Scene(0, null, out EntityHandle worker);
        World w = sim.World;
        w.Ledger.Gold[0] = 35;
        w.Ledger.Wood[0] = 100;
        w.Units.Free(worker);
        Assert.False(w.CanStillTrain(0));
        Assert.True(w.MatchSpent, "spent from the tick the worker died");
        sim.Tick();
        Assert.Equal(0, w.DrawClock); // the death itself sets the clock back on its tick (M6-S13)
        sim.Tick();
        Assert.Equal(1, w.DrawClock);
    }

    private static readonly Lazy<GameData> s_everyUnitWood = new(() => DrawScenes.DataWith(DrawScenes.ShortSeconds, dir =>
        dir.SetUnitField("malazan", "malazan_laborer", "cost.wood", "10")));

    /// <summary>
    /// Guard (green on the base): exactly the Depot's price in the bank, no wood on the map and no unit free of wood (forged: the
    /// Laborer costs 10 wood): the Depot would take every wood, so no unit is paid; spent.
    /// </summary>
    [Fact]
    public void Guard_ExactlyTheDepotsPrice_NoWoodLeftAndNoUnitFreeOfWood_IsSpent()
    {
        GameData d = s_everyUnitWood.Value;
        Simulation sim = Scene(0, d, out _);
        World w = sim.World;
        BuildingDef depot = SmallDropOff(d, 0);
        foreach (int t in d.Factions[0].Units) Assert.True(d.Units[t].CostWood > 0, $"precondition: {d.Units[t].Key} costs wood");
        Assert.Equal(0, w.Resources.WoodLeft);
        w.Ledger.Gold[0] = 35 + depot.CostGold;
        w.Ledger.Wood[0] = depot.CostWood;
        Assert.False(w.CanStillTrain(0), DrawReachScan.Line(w, 0));
        Assert.True(w.MatchSpent);
    }

    /// <summary>The forged scene of the guard above with 10 wood over the Depot's price: the Laborer's wood is left, the mine pays its gold.</summary>
    [Fact]
    public void Forged_TenWoodOverTheDepot_PaysTheLaborer_IsNotSpent()
    {
        GameData d = s_everyUnitWood.Value;
        Simulation sim = Scene(0, d, out _);
        World w = sim.World;
        BuildingDef depot = SmallDropOff(d, 0);
        w.Ledger.Gold[0] = 35 + depot.CostGold;
        w.Ledger.Wood[0] = depot.CostWood + 10;
        Assert.True(w.CanStillTrain(0), DrawReachScan.Line(w, 0));
    }

    /// <summary>Guard (green on the base): a paid Depot site and an empty bank is spent until the tick the Depot is finished.</summary>
    [Fact]
    public void Guard_APaidDepotSite_WithAnEmptyBank_IsSpentUntilTheTickItIsFinished()
    {
        Simulation sim = Scene(0, null, out _);
        World w = sim.World;
        EntityHandle site = DrawScenes.Building(sim, 0, 20, 8, BuildingSlot.Camp, site: true);
        Assert.True(w.Data.Buildings[w.Buildings.TypeId[site.Index]].DropOff);
        Assert.False(w.CanStillTrain(0), "a drop-off site is no drop-off, and an empty bank pays no new one");
        for (int t = 1; t <= 30; t++)
        {
            sim.Tick();
            Assert.Equal(t, w.DrawClock);
        }
        w.Buildings.SetWork(site.Index, w.Buildings.WorkNeeded(w.Buildings.TypeId[site.Index]));
        Assert.Equal(1, w.Ledger.FinishedDropOffs[0]);
        Assert.True(w.CanStillTrain(0));
        sim.Tick();
        Assert.Equal(0, w.DrawClock);
    }

    /// <summary>The BUG-1893 scene (shipped costs, the short wait) with its bank set: 35 gold and <paramref name="wood"/>.</summary>
    internal static Simulation ClauseScene(int wood, out EntityHandle worker, bool node = true)
    {
        Simulation sim = Scene(0, DrawScenes.Short, out worker, node);
        sim.World.Ledger.Gold[0] = 35;
        sim.World.Ledger.Wood[0] = wood;
        return sim;
    }

    /// <summary>A save of a state the clause decides (not spent only through it) loads to the same hash and answer and plays alike; then the Laborer builds the Depot in both.</summary>
    [Fact]
    public void ASave_InAStateTheClauseDecides_LoadsToTheSameHashAndAnswer()
    {
        Simulation sim = ClauseScene(100, out EntityHandle worker);
        for (int t = 0; t < 40; t++) sim.Tick();
        Assert.True(sim.World.CanStillTrain(0) && !DrawReachScan.Sh3Can(sim.World, 0));
        Simulation loaded = SaveTestRun.Load(DrawScenes.Short, SaveTestRun.Save(sim));
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        for (int p = 0; p < 2; p++) Assert.Equal(sim.World.CanStillTrain(p), loaded.World.CanStillTrain(p));
        int depot = SmallDropOff(sim.World.Data, 0).Id;
        foreach (Simulation s in new[] { sim, loaded }) s.Enqueue(Command.Build(0, worker, depot, s.World.NavGrid.CellCenter(22, 10)));
        for (int t = 0; t < 1000; t++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.Equal(sim.StateHash(), loaded.StateHash());
            Assert.Equal(sim.World.CanStillTrain(0), loaded.World.CanStillTrain(0));
            Assert.Equal(sim.World.DrawClock, loaded.World.DrawClock);
        }
    }

    /// <summary>The determinism twin: the clause scene with the Depot built, twice, the same fold of hashes and draw clocks.</summary>
    [Fact]
    public void TheClauseScene_RunTwice_FoldsAlike()
    {
        string Play()
        {
            Simulation sim = ClauseScene(100, out EntityHandle worker);
            sim.Enqueue(Command.Build(0, worker, SmallDropOff(sim.World.Data, 0).Id, sim.World.NavGrid.CellCenter(22, 10)));
            var fold = new StateHasher();
            for (int t = 0; t < 1500; t++)
            {
                sim.Tick();
                fold.Add(sim.StateHash());
                fold.Add(sim.World.DrawClock);
            }
            return $"{sim.StateHash():X16} {fold.Value:X16}";
        }
        Assert.Equal(Play(), Play());
    }
}
