using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-SH2 item 0 (BUG-1520, the owner's playtest bug; docs/02 "Economy"): only a finished building is a drop-off. A loaded
/// worker beside its own drop-off site banks nothing and keeps its cargo; with a finished drop-off it walks there; with none
/// it waits and delivers once the site is finished; a worker already walking to a site re-picks; a queued order behind the
/// loop with only a site standing starts at once. Rows for gold and wood, a hall and a Camp-type building, the Laborer and the
/// Teblor Gatherer.
/// </summary>
[Collection(SerialCollection.Name)] // measures allocation (criterion 9)
public class DropOffSiteTests
{
    // 48 x 24 flat map, 2 players. The drop-off site is anchored at (12, 12); the loaded worker stands 0.5 m left of it, in
    // reach (EconomyConstants.Reach 1.25 m). A finished hall, when the row has one, is anchored at (2, 2), about 20 m away.
    // The node the loop is on: a tree at (30, 16) or a gold mine at (30, 4). Player 1's lone soldier stands far east.
    private const int SiteX = 12, SiteY = 12, HallX = 2, HallY = 2;

    private sealed class Scene
    {
        public required Simulation Sim;
        public required EntityHandle Worker, Node, Site;
        public EntityHandle Hall;
        public required bool Wood;
        public int I => Worker.Index;
        public World W => Sim.World;
        public UnitStore U => Sim.World.Units;
        public int Carry => Sim.World.Data.CarryOf(U.TypeId[I]);
        public int Gathered => Wood ? Sim.World.GatheredWood[0] : Sim.World.GatheredGold[0];
    }

    private static int WorkerType(bool gatherer) => gatherer ? GatherOverlapScenes.GathererType : Laborer;

    private static int HallType(bool gatherer) => TestSim.Data.FindBuilding(gatherer ? "teblor_longhouse" : "malazan_garrison_keep");

    private static int CampType(bool gatherer) => TestSim.Data.FindBuilding(gatherer ? "teblor_woodcutters_lodge" : "malazan_depot");

    /// <summary>A loaded worker (a full load of the row's kind) beside an own drop-off site, a finished hall far off when <paramref name="hall"/>.</summary>
    private static Scene Make(bool gatherer, bool wood, bool camp, bool hall)
    {
        Simulation sim = NewSim(Flat(48, 24), units: 16, players: 2);
        World w = sim.World;
        EntityHandle node = wood ? Spawn(w, Tree, 30, 16, TreeWood) : Spawn(w, Mine, 30, 4, 2500);
        EntityHandle enemy = CombatScenes.Place(sim, 1, Infantry, At(sim, 45, 21));
        EntityHandle worker = CombatScenes.Place(sim, 0, WorkerType(gatherer), new Vector2(SiteX * 2f - 0.5f, SiteY * 2f + 1f));
        EntityHandle hallHandle = hall ? Building(sim, HallX, HallY, type: HallType(gatherer)) : default;
        int siteType = camp ? CampType(gatherer) : HallType(gatherer);
        Assert.True(w.Buildings.Spawn(0, siteType, SiteY * w.NavGrid.Width + SiteX, out EntityHandle site, site: true));
        Assert.True(w.Buildings.UnderConstruction[site.Index]);
        UnitStore u = w.Units;
        u.CargoKind[worker.Index] = wood ? ResourceKind.Wood : ResourceKind.Gold;
        u.Cargo[worker.Index] = w.Data.CarryOf(u.TypeId[worker.Index]);
        _ = enemy;
        return new Scene { Sim = sim, Worker = worker, Node = node, Site = site, Hall = hallHandle, Wood = wood };
    }

    private static Vector2 NodeCell(Scene s)
    {
        int cell = s.W.Resources.Cell[s.Node.Index], width = s.W.NavGrid.Width;
        return s.W.NavGrid.CellCenter(cell % width, cell / width);
    }

    /// <summary>Puts the loaded worker on its loop (a Gather with a full load starts the return leg) and applies it.</summary>
    private static void StartLoop(Scene s)
    {
        s.Sim.Enqueue(Command.Gather(0, s.Worker, NodeCell(s)));
        while (s.Sim.PendingCommandCount > 0) s.Sim.Tick();
        Assert.True(EconomySystem.OnLoop(s.U, s.I));
    }

    private static bool InReachOf(Scene s, EntityHandle building)
    {
        BuildingDef def = s.W.Data.Buildings[s.W.Buildings.TypeId[building.Index]];
        return EconomySystem.InReach(s.W.NavGrid, s.U.Position[s.I], s.W.Buildings.Cell[building.Index], def.FootprintWidth, def.FootprintHeight);
    }

    private static void Finish(Scene s)
    {
        BuildingStore b = s.W.Buildings;
        b.SetWork(s.Site.Index, b.WorkNeeded(b.TypeId[s.Site.Index]));
        Assert.False(b.UnderConstruction[s.Site.Index]);
    }

    public static IEnumerable<object[]> Rows()
    {
        foreach (bool gatherer in new[] { false, true })
            foreach (bool wood in new[] { false, true })
                foreach (bool camp in new[] { false, true })
                    yield return new object[] { gatherer, wood, camp };
    }

    /// <summary>
    /// Criterion 13 rows 1 and 3: in reach of its own drop-off site with no finished drop-off, the worker banks nothing for
    /// 400 ticks, keeps its full load and stays on its loop; once the site is finished it delivers within 200 ticks.
    /// </summary>
    [Theory]
    [MemberData(nameof(Rows))]
    public void InReachOfAnOwnSite_BanksNothing_KeepsItsCargo_ThenDeliversOnceTheSiteIsFinished(bool gatherer, bool wood, bool camp)
    {
        Scene s = Make(gatherer, wood, camp, hall: false);
        Assert.True(InReachOf(s, s.Site));
        int gathered = s.Gathered;
        StartLoop(s);
        Run(s.Sim, 400);
        Assert.Equal(gathered, s.Gathered);
        Assert.Equal(s.Carry, s.U.Cargo[s.I]);
        Assert.True(EconomySystem.OnLoop(s.U, s.I), "left its loop while the site stood unfinished");
        Finish(s);
        int finished = s.Sim.TickNumber;
        while (s.Gathered == gathered && s.Sim.TickNumber < finished + 200) s.Sim.Tick();
        Assert.Equal(gathered + s.Carry, s.Gathered);
        Assert.Equal(0, s.U.Cargo[s.I]);
    }

    /// <summary>Criterion 13 row 2: beside its own site with a finished hall about 20 m off, the worker walks to the hall and delivers there, never at the site.</summary>
    [Theory]
    [MemberData(nameof(Rows))]
    public void BesideAnOwnSite_WithAFinishedHallFartherAway_WalksThereAndDelivers(bool gatherer, bool wood, bool camp)
    {
        Scene s = Make(gatherer, wood, camp, hall: true);
        int gathered = s.Gathered;
        StartLoop(s);
        for (int t = 0; t < 3000 && s.Gathered == gathered; t++) s.Sim.Tick();
        Assert.Equal(gathered + s.Carry, s.Gathered);
        Assert.True(InReachOf(s, s.Hall), $"delivered at {s.U.Position[s.I]}, not in reach of the finished hall");
        Assert.False(InReachOf(s, s.Site), "delivered in reach of the site");
    }

    /// <summary>
    /// Criterion 13 row 4: a worker already walking its load to a site when the rule lands (the base's return leg, put back with
    /// the loop's own walk, then saved and loaded) re-picks the finished hall on the next tick and delivers there.
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    public void AWorkerAlreadyWalkingToASite_FromASave_RePicksTheFinishedHall(bool gatherer, bool wood, bool camp)
    {
        Scene s = Make(gatherer, wood, camp, hall: true);
        // Step it back from the site first, so the base's walk to it is a real walk.
        s.U.Position[s.I] = new Vector2(SiteX * 2f - 8f, SiteY * 2f + 1f);
        StartLoop(s);
        BuildingDef def = s.W.Data.Buildings[s.W.Buildings.TypeId[s.Site.Index]];
        Assert.True(EconomySystem.WalkToFootprint(s.W, s.I, s.W.Buildings.Cell[s.Site.Index], def.FootprintWidth, def.FootprintHeight));
        Assert.Equal(UnitState.Moving, s.U.State[s.I]);
        Simulation loaded = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(s.Sim));
        var t = new Scene { Sim = loaded, Worker = s.Worker, Node = s.Node, Site = s.Site, Hall = s.Hall, Wood = wood };
        int gathered = t.Gathered;
        loaded.Tick();
        Assert.True(InReachGoal(t, t.Hall), $"goal {t.U.Goal[t.I]} is not by the finished hall one tick after loading");
        for (int k = 0; k < 3000 && t.Gathered == gathered; k++) loaded.Tick();
        Assert.Equal(gathered + t.Carry, t.Gathered);
        Assert.True(InReachOf(t, t.Hall), "delivered elsewhere than at the finished hall");
    }

    private static bool InReachGoal(Scene s, EntityHandle building)
    {
        BuildingDef def = s.W.Data.Buildings[s.W.Buildings.TypeId[building.Index]];
        return EconomySystem.InReach(s.W.NavGrid, s.U.Goal[s.I], s.W.Buildings.Cell[building.Index], def.FootprintWidth, def.FootprintHeight);
    }

    /// <summary>Criterion 13 row 5 (EndsForQueue): a loaded worker with an order queued behind its loop and only a site standing starts the order at once, cargo kept.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    public void AQueuedOrder_WithOnlyASiteStanding_StartsAtOnce_CargoKept(bool gatherer, bool wood, bool camp)
    {
        Scene s = Make(gatherer, wood, camp, hall: false);
        StartLoop(s);
        int gathered = s.Gathered;
        Vector2 target = At(s.Sim, 30, 20);
        s.Sim.Enqueue(Command.Move(0, s.Worker, target, queued: true));
        while (s.Sim.PendingCommandCount > 0) s.Sim.Tick();
        int applied = s.Sim.TickNumber;
        while (EconomySystem.OnLoop(s.U, s.I) && s.Sim.TickNumber < applied + 2) s.Sim.Tick();
        Assert.False(EconomySystem.OnLoop(s.U, s.I));
        Assert.Equal(UnitState.Moving, s.U.State[s.I]);
        Assert.True(Vector2.Distance(target, s.U.Goal[s.I]) < 0.01f, $"goal {s.U.Goal[s.I]}");
        Assert.Equal(s.Carry, s.U.Cargo[s.I]);
        Assert.Equal(gathered, s.Gathered);
    }

    /// <summary>
    /// Criterion 9: the rule's per-tick code allocates nothing: ticks with a site standing (the scan in <c>Run</c>), a loaded
    /// worker waiting beside it (<c>OwnsDropOffSite</c>, <c>Wait</c>), and a second worker walking its load to the site and
    /// re-picking (<c>WalksToSite</c>, the walk to the hall).
    /// </summary>
    [Fact]
    public void TheRulesTicks_AllocateNothing()
    {
        Scene s = Make(gatherer: false, wood: false, camp: true, hall: false);
        StartLoop(s);
        GatherMaps.Run(s.Sim, 30); // warm: the waiting worker's retries
        AllocationProbe.AssertZero(() => GatherMaps.Run(s.Sim, 40));
        Scene t = Make(gatherer: false, wood: false, camp: true, hall: true);
        t.U.Position[t.I] = new Vector2(SiteX * 2f - 8f, SiteY * 2f + 1f);
        StartLoop(t);
        BuildingDef def = t.W.Data.Buildings[t.W.Buildings.TypeId[t.Site.Index]];
        int cell = t.W.Buildings.Cell[t.Site.Index];
        AllocationProbe.AssertZero(() => GatherMaps.Run(t.Sim, 3),
            setup: () => EconomySystem.WalkToFootprint(t.W, t.I, cell, def.FootprintWidth, def.FootprintHeight));
    }

    /// <summary>
    /// Criterion 7: twins of the waiting scene hash alike every tick, and a save taken while the loaded worker waits beside the
    /// site loads, saves byte-identical and follows the run hash for hash through the site's finishing and the delivery.
    /// </summary>
    [Fact]
    public void AWorkerWaitingBesideASite_Twins_AndASaveMidWait_FollowTheRun()
    {
        Scene a = Make(gatherer: true, wood: true, camp: true, hall: false);
        Scene b = Make(gatherer: true, wood: true, camp: true, hall: false);
        StartLoop(a);
        StartLoop(b);
        GatherMaps.Run(a.Sim, 50);
        GatherMaps.Run(b.Sim, 50);
        Assert.Equal(a.Sim.StateHash(), b.Sim.StateHash());
        byte[] bytes = SaveTestRun.Save(a.Sim);
        Simulation c = SaveTestRun.Load(TestSim.Data, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(c));
        var loaded = new Scene { Sim = c, Worker = a.Worker, Node = a.Node, Site = a.Site, Wood = true };
        int gathered = a.Gathered;
        for (int t = 0; t < 400; t++)
        {
            if (t == 100)
            {
                Finish(a);
                Finish(b);
                Finish(loaded);
            }
            a.Sim.Tick();
            b.Sim.Tick();
            c.Tick();
            Assert.True(a.Sim.StateHash() == b.Sim.StateHash(), $"twins differ after tick {a.Sim.TickNumber}");
            Assert.True(a.Sim.StateHash() == c.StateHash(), $"the loaded save diverged at tick {a.Sim.TickNumber}");
        }
        Assert.Equal(gathered + a.Carry, a.Gathered);
    }

    /// <summary>The rule's other side: a finished drop-off that is damaged (or being repaired: the same building state) still takes a load.</summary>
    [Fact]
    public void ADamagedFinishedDropOff_StillTakesALoad()
    {
        Scene s = Make(gatherer: false, wood: false, camp: true, hall: false);
        Finish(s);
        s.W.Buildings.Damage(s.Site, 300);
        Assert.True(s.W.Buildings.IsAlive(s.Site));
        int gathered = s.Gathered;
        StartLoop(s);
        for (int t = 0; t < 5 && s.Gathered == gathered; t++) s.Sim.Tick();
        Assert.Equal(gathered + s.Carry, s.Gathered);
    }

    /// <summary>With no own drop-off at all (no site either), a loaded worker's loop ends as before: Idle, cargo kept.</summary>
    [Fact]
    public void NoDropOffAndNoSite_TheLoopEndsAsBefore_CargoKept()
    {
        Scene s = Make(gatherer: false, wood: true, camp: true, hall: false);
        s.W.Buildings.Free(s.Site);
        s.Sim.Enqueue(Command.Gather(0, s.Worker, NodeCell(s)));
        while (s.Sim.PendingCommandCount > 0) s.Sim.Tick();
        Run(s.Sim, 2);
        Assert.False(EconomySystem.OnLoop(s.U, s.I));
        Assert.Equal(UnitState.Idle, s.U.State[s.I]);
        Assert.Equal(s.Carry, s.U.Cargo[s.I]);
    }
}
