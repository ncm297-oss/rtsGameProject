using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResearchMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M3-6 criteria 3-5: <c>World.CanTrain</c>, <c>CanPlace</c> and <c>CanResearch</c> gate on <c>requires</c> (and Age II's
/// any two of the four halls), at queue time only. Shipped data for Age II, the uniques and the level-2 upgrades; a
/// fixture copy (<see cref="Fixture"/>) for a unit requiring a building and a building requiring Age II.
/// </summary>
public class RequirementGatingTests
{
    private static readonly Lazy<GameData> s_fixture = new(LoadFixture);

    /// <summary>
    /// The shipped data with two edits: Heavy Infantry requires a finished Crossbow Range, and the Cadre Tower (caster
    /// hall) requires Age II. Every id is the same as in the shipped data (ids follow the string ids).
    /// </summary>
    public static GameData Fixture => s_fixture.Value;

    private static GameData LoadFixture()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "requires", "[\"malazan_crossbow_range\"]");
        dir.EditJson("factions/malazan/buildings.json", root =>
        {
            foreach (JsonNode? b in root["buildings"]!.AsArray())
                b!["requires"] = (string)b["id"]! == "malazan_cadre_tower" ? JsonNode.Parse("[\"age_ii\"]") : new JsonArray();
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    private static int Range => TestSim.Data.FindBuilding("malazan_crossbow_range");
    private static int CadreTower => TestSim.Data.FindBuilding("malazan_cadre_tower");
    private static int ArcherCamp => TestSim.Data.FindBuilding("whirlwind_archer_camp");

    /// <summary>A sim on a flat 48 x 32 map with <paramref name="data"/> (shipped by default).</summary>
    public static Simulation NewSim(GameData? data = null, int players = 2) =>
        new(new SimConfig(5, players, 64, 512) { Data = data ?? TestSim.Data }, ResourceMaps.Flat(48, 32));

    private static TrainError Train(World w, int player, int slot, int type)
    {
        w.CanTrain(player, slot, type, out TrainError why);
        return why;
    }

    private static ResearchError Research(World w, int player, int slot, int tech)
    {
        w.CanResearch(player, slot, tech, out ResearchError why);
        return why;
    }

    private static PlacementError Place(World w, int player, int type, int cell)
    {
        w.CanPlace(player, type, cell, out PlacementError why);
        return why;
    }

    /// <summary>A construction site through the store (no worker), so it stays unfinished.</summary>
    private static int Site(Simulation sim, int player, int type, int x, int y)
    {
        Assert.True(sim.World.Buildings.Spawn(player, type, Cell(sim, x, y), out EntityHandle h, site: true));
        return h.Index;
    }

    // ---------- criterion 3: CanTrain ----------

    [Fact]
    public void TheSapper_IsLockedUntilAgeIICompletes_AndATrainApplyingTheNextTickIsAccepted()
    {
        Simulation sim = NewSim(players: 1);
        World w = sim.World;
        int keep = Building(sim, 4, 4).Index;
        Building(sim, 12, 4, type: Barracks);
        Building(sim, 18, 4, type: Armory);
        int yard = Building(sim, 26, 4, type: EngineersYard).Index;
        Give(sim, 0, 100_000, 100_000);
        Assert.Equal(TrainError.LockedByRequirement, Train(w, 0, yard, Sapper));
        sim.Enqueue(Command.Research(0, In(sim, keep), AgeII));
        int done = -1, firstQueued = -1;
        for (int n = 0; n < 1300 && firstQueued < 0; n++)
        {
            // Stamped for the tick after the next: the one enqueued before tick D applies in phase 1 of tick D + 1.
            sim.Enqueue(Command.Train(0, In(sim, yard), Sapper));
            int tick = sim.TickNumber;
            sim.Tick();
            if (done < 0 && w.HasTech(0, AgeII)) done = tick;
            if (firstQueued < 0 && w.Buildings.QueueCount[yard] > 0) firstQueued = tick;
            Assert.Equal(done >= 0 ? TrainError.None : TrainError.LockedByRequirement, Train(w, 0, yard, Sapper));
        }
        Assert.True(done > 0, "Age II never completed");
        Assert.Equal(done + 1, firstQueued); // completed in phase 3 of tick D; the Train applying in phase 1 of D took the old answer
        Assert.Equal(2, w.Age(0));
    }

    [Fact]
    public void AUnitRequiringABuilding_NeedsAnOwnFinishedOne_NotASiteNorAnEnemys_AndASecondKeepsItOpen()
    {
        Simulation sim = NewSim(Fixture);
        World w = sim.World;
        int bar = Building(sim, 4, 4, type: Barracks).Index;
        Building(sim, 12, 20); // a Keep for population
        Give(sim, 0, 100_000, 100_000);
        Assert.Equal(TrainError.LockedByRequirement, Train(w, 0, bar, Infantry));

        int site = Site(sim, 0, Range, 12, 4);
        Assert.Equal(TrainError.LockedByRequirement, Train(w, 0, bar, Infantry)); // a site doesn't count
        Building(sim, 30, 4, player: 1, type: Range);                             // the dev spawn ignores faction
        Assert.Equal(TrainError.LockedByRequirement, Train(w, 0, bar, Infantry)); // an enemy's doesn't count

        w.Buildings.SetWork(site, w.Buildings.WorkNeeded(Range));                 // the site completes
        Assert.Equal(TrainError.None, Train(w, 0, bar, Infantry));
        int second = Building(sim, 20, 4, type: Range).Index;
        w.Buildings.Damage(w.Buildings.HandleOf(site), 1_000_000);
        Assert.Equal(TrainError.None, Train(w, 0, bar, Infantry));               // the second one keeps it open

        // Queued while open, then the last Range goes: the item survives (queue-time rule) and trains.
        Apply(sim, Command.Train(0, In(sim, bar), Infantry));
        Assert.Equal(1, w.Buildings.QueueCount[bar]);
        w.Buildings.Damage(w.Buildings.HandleOf(second), 1_000_000);
        Assert.Equal(TrainError.LockedByRequirement, Train(w, 0, bar, Infantry));
        int units = w.Units.Count;
        Run(sim, w.Buildings.TrainTicks(Infantry) + 2);
        Assert.Equal(units + 1, w.Units.Count);
        Assert.Equal(0, w.Buildings.QueueCount[bar]);

        // Locked again: a Train is dropped with the totals unchanged.
        (int gold, int wood) = (w.Gold[0], w.Wood[0]);
        Apply(sim, Command.Train(0, In(sim, bar), Infantry));
        Assert.Equal((0, gold, wood), (w.Buildings.QueueCount[bar], w.Gold[0], w.Wood[0]));
    }

    [Fact]
    public void LockedByRequirement_KeepsItsPlaceInTheReasonOrder()
    {
        // After NotTrainedHere, before QueueFull and CannotAfford.
        Simulation sim = NewSim(players: 1);
        World w = sim.World;
        int yard = Building(sim, 4, 4, type: EngineersYard).Index;
        int bar = Building(sim, 12, 4, type: Barracks).Index;
        SetTotals(sim, 0, 0, 0);
        Assert.Equal(TrainError.NotTrainedHere, Train(w, 0, bar, Sapper));
        Assert.Equal(TrainError.LockedByRequirement, Train(w, 0, yard, Sapper));
        w.Techs.Set(0, AgeII, true);
        Assert.Equal(TrainError.CannotAfford, Train(w, 0, yard, Sapper));
    }

    // ---------- criterion 4: CanPlace ----------

    [Fact]
    public void ABuildingRequiringAgeII_IsRequiresBefore_AndNoneAfter()
    {
        Simulation sim = NewSim(Fixture);
        World w = sim.World;
        Give(sim, 0, 10_000, 10_000);
        int cell = Cell(sim, 20, 10);
        Assert.Equal(PlacementError.Requires, Place(w, 0, CadreTower, cell));
        w.Techs.Set(0, AgeII, true);
        Assert.Equal(PlacementError.None, Place(w, 0, CadreTower, cell));
        w.Techs.Set(0, AgeII, false);
        Assert.Equal(PlacementError.Requires, Place(w, 0, CadreTower, cell));
        // An unrequired building is untouched by the rule.
        Assert.Equal(PlacementError.None, Place(w, 0, House, cell));
    }

    [Fact]
    public void Requires_OutranksOffMapBlockedAndCannotAfford_AndWrongFactionOutranksIt()
    {
        Simulation sim = NewSim(Fixture);
        World w = sim.World;
        int keep = Building(sim, 4, 4).Index;
        int onKeep = w.Buildings.Cell[keep];
        int offMap = Cell(sim, 47, 31); // the footprint runs off the map
        int free = Cell(sim, 20, 10);
        SetTotals(sim, 0, 0, 0);
        foreach (int cell in new[] { -1, offMap, onKeep, free })
            Assert.Equal(PlacementError.Requires, Place(w, 0, CadreTower, cell));
        Assert.Equal(PlacementError.WrongFaction, Place(w, 1, CadreTower, free));
        // With Age II the map and cost rules come back, in their order.
        w.Techs.Set(0, AgeII, true);
        Assert.Equal(PlacementError.OffMap, Place(w, 0, CadreTower, -1));
        Assert.Equal(PlacementError.OffMap, Place(w, 0, CadreTower, offMap));
        Assert.Equal(PlacementError.Blocked, Place(w, 0, CadreTower, onKeep));
        Assert.Equal(PlacementError.CannotAfford, Place(w, 0, CadreTower, free));
        // Another player's Age II doesn't open it.
        w.Techs.Set(0, AgeII, false);
        w.Techs.Set(1, AgeII, true);
        Assert.Equal(PlacementError.Requires, Place(w, 0, CadreTower, free));
    }

    [Fact]
    public void ABuildWhileLocked_IsDropped_TotalsUnchanged_AndAccepted_OnceUnlocked()
    {
        Simulation sim = NewSim(Fixture);
        World w = sim.World;
        EntityHandle worker = Unit(sim, At(sim, 18, 10));
        Give(sim, 0, 10_000, 10_000);
        (int gold, int wood) = (w.Gold[0], w.Wood[0]);
        Apply(sim, Command.Build(0, worker, CadreTower, At(sim, 20, 10)));
        Assert.Equal((0, gold, wood), (w.Buildings.Count, w.Gold[0], w.Wood[0]));
        Assert.Equal(default, w.Units.BuildTarget[worker.Index]);
        // Queued behind a Move it is checked when it starts: still locked, still dropped.
        sim.Enqueue(Command.Move(0, worker, At(sim, 16, 16)));
        sim.Enqueue(Command.Build(0, worker, CadreTower, At(sim, 20, 10), queued: true));
        Run(sim, 60);
        Assert.Equal((0, gold, wood), (w.Buildings.Count, w.Gold[0], w.Wood[0]));
        w.Techs.Set(0, AgeII, true);
        Apply(sim, Command.Build(0, worker, CadreTower, At(sim, 20, 10)));
        Assert.Equal(1, w.Buildings.Count);
        BuildingDef def = w.Data.Buildings[CadreTower];
        Assert.Equal((gold - def.CostGold, wood - def.CostWood), (w.Gold[0], w.Wood[0]));
    }

    // ---------- criterion 5: CanResearch ----------

    [Fact]
    public void AgeII_NeedsTwoDistinctHallSlots_Finished_Own()
    {
        Simulation sim = NewSim();
        World w = sim.World;
        int keep = Building(sim, 4, 4).Index;
        Give(sim, 0, 10_000, 10_000);
        Assert.Equal(ResearchError.Requires, Research(w, 0, keep, AgeII));            // none
        Building(sim, 12, 4, type: Barracks);
        Assert.Equal(ResearchError.Requires, Research(w, 0, keep, AgeII));            // one
        int ranged = Site(sim, 0, Range, 18, 4);
        int shock = Site(sim, 0, Corral, 24, 4);
        Assert.Equal(ResearchError.Requires, Research(w, 0, keep, AgeII));            // + two sites
        Building(sim, 30, 4, type: Barracks);
        Assert.Equal(ResearchError.Requires, Research(w, 0, keep, AgeII));            // two of one slot count once
        Building(sim, 4, 20, player: 1, type: ArcherCamp);
        Building(sim, 12, 20, player: 1, type: Smithy);
        Assert.Equal(ResearchError.Requires, Research(w, 0, keep, AgeII));            // an enemy's two don't count
        w.Buildings.SetWork(shock, w.Buildings.WorkNeeded(Corral));
        Assert.Equal(ResearchError.None, Research(w, 0, keep, AgeII));                // Barracks + Corral
        w.Buildings.Damage(w.Buildings.HandleOf(shock), 1_000_000);
        Assert.Equal(ResearchError.Requires, Research(w, 0, keep, AgeII));
        w.Buildings.SetWork(ranged, w.Buildings.WorkNeeded(Range));
        Assert.Equal(ResearchError.None, Research(w, 0, keep, AgeII));                // Barracks + Range
    }

    [Theory]
    [InlineData("malazan_barracks", "malazan_armory")]
    [InlineData("malazan_crossbow_range", "malazan_wickan_corral")]
    [InlineData("malazan_wickan_corral", "malazan_armory")]
    [InlineData("malazan_barracks", "malazan_crossbow_range")]
    public void AnyTwoDistinctOfTheFour_OpenAgeII(string a, string b)
    {
        Simulation sim = NewSim(players: 1);
        World w = sim.World;
        int keep = Building(sim, 4, 4).Index;
        Give(sim, 0, 10_000, 10_000);
        Building(sim, 12, 4, type: w.Data.FindBuilding(a));
        Assert.Equal(ResearchError.Requires, Research(w, 0, keep, AgeII));
        Building(sim, 20, 4, type: w.Data.FindBuilding(b));
        Assert.Equal(ResearchError.None, Research(w, 0, keep, AgeII));
    }

    [Fact]
    public void LevelTwo_NeedsLevelOneAndAgeII_AndTheFactionUpgradeNeedsAgeII()
    {
        Simulation sim = NewSim(players: 1);
        World w = sim.World;
        int armory = Building(sim, 4, 4, type: Armory).Index;
        Give(sim, 0, 10_000, 10_000);
        Assert.Equal(ResearchError.Requires, Research(w, 0, armory, Melee2));
        Assert.Equal(ResearchError.Requires, Research(w, 0, armory, Moranth));
        w.Techs.Set(0, Melee1, true);
        Assert.Equal(ResearchError.Requires, Research(w, 0, armory, Melee2));
        w.Techs.Set(0, Melee1, false);
        w.Techs.Set(0, AgeII, true);
        Assert.Equal(ResearchError.Requires, Research(w, 0, armory, Melee2));
        Assert.Equal(ResearchError.None, Research(w, 0, armory, Moranth));
        w.Techs.Set(0, Melee1, true);
        Assert.Equal(ResearchError.None, Research(w, 0, armory, Melee2));
        // Requires comes after NotResearchedHere and before AlreadyResearched.
        w.Techs.Set(0, AgeII, false);
        w.Techs.Set(0, Melee2, true);
        Assert.Equal(ResearchError.Requires, Research(w, 0, armory, Melee2));
        Assert.Equal(ResearchError.NotResearchedHere, Research(w, 0, armory, AgeII));
    }

    [Fact]
    public void AResearchWhileLocked_IsDropped_TotalsUnchanged()
    {
        Simulation sim = NewSim(players: 1);
        World w = sim.World;
        int keep = Building(sim, 4, 4).Index;
        int armory = Building(sim, 12, 4, type: Armory).Index;
        Give(sim, 0, 10_000, 10_000);
        (int gold, int wood) = (w.Gold[0], w.Wood[0]);
        sim.Enqueue(Command.Research(0, In(sim, keep), AgeII)); // one hall slot only
        sim.Enqueue(Command.Research(0, In(sim, armory), Melee2));
        sim.Enqueue(Command.Research(0, In(sim, armory), Moranth));
        Run(sim, 2);
        Assert.Equal((0, 0, gold, wood), (w.Buildings.QueueCount[keep], w.Buildings.QueueCount[armory], w.Gold[0], w.Wood[0]));
    }

    [Fact]
    public void AgeIIQueuedWithTwoHalls_CompletesAfterOneIsDestroyed()
    {
        Simulation sim = NewSim(players: 1);
        World w = sim.World;
        int keep = Building(sim, 4, 4).Index;
        int bar = Building(sim, 12, 4, type: Barracks).Index;
        Building(sim, 20, 4, type: Armory);
        Give(sim, 0, 10_000, 10_000);
        Apply(sim, Command.Research(0, In(sim, keep), AgeII));
        Assert.Equal(1, w.Buildings.QueueCount[keep]);
        w.Buildings.Damage(w.Buildings.HandleOf(bar), 1_000_000);
        Assert.Equal(ResearchError.Requires, Research(w, 0, keep, AgeII));
        Run(sim, w.Data.Techs[AgeII].ResearchTicks);
        Assert.True(w.HasTech(0, AgeII));
        Assert.Equal(2, w.Age(0));
    }

    // ---------- the derived counts ----------

    [Fact]
    public void FinishedCounts_FollowSpawnCompletionAndDestruction_PerPlayerTypeAndSlot()
    {
        Simulation sim = NewSim();
        World w = sim.World;
        PlayerLedger l = w.Ledger;
        int a = Building(sim, 4, 4, type: Barracks).Index;
        int site = Site(sim, 0, Barracks, 12, 4);
        Building(sim, 20, 4, player: 1, type: RaiderCamp);
        Assert.Equal((1, 1, 0), (l.FinishedOfType(0, Barracks), l.FinishedInSlot(0, (int)BuildingSlot.InfantryHall), l.FinishedOfType(1, Barracks)));
        Assert.Equal((1, 1), (l.FinishedOfType(1, RaiderCamp), l.FinishedInSlot(1, (int)BuildingSlot.InfantryHall)));
        w.Buildings.SetWork(site, w.Buildings.WorkNeeded(Barracks));
        Assert.Equal((2, 2), (l.FinishedOfType(0, Barracks), l.FinishedInSlot(0, (int)BuildingSlot.InfantryHall)));
        w.Buildings.Damage(w.Buildings.HandleOf(a), 1_000_000);
        Assert.Equal(1, l.FinishedOfType(0, Barracks));
        Site(sim, 0, Barracks, 4, 4);
        Assert.Equal(1, l.FinishedOfType(0, Barracks));
        Apply(sim, Command.Cancel(0, At(sim, 4, 4))); // a cancelled site never counted
        Assert.Equal(1, l.FinishedOfType(0, Barracks));
        Assert.Equal((0, 0, 0), (l.FinishedOfType(-1, Barracks), l.FinishedOfType(0, -1), l.FinishedInSlot(0, 99)));
    }
}
