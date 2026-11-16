using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-SH2 item 0 (BUG-1520, the owner's playtest bug; docs/02 "Economy": only a finished building is a drop-off). Attacks
/// past the developer's rows: every drop-off building of every faction as a site (the developer's rows use four of six), the
/// site cancelled or destroyed while a loaded worker waits or walks to it, the site finished through real construction by a
/// loaded builder, 30 loaded workers waiting for one site, and a seeded fuzz of sites placed / finished / damaged / destroyed
/// under a gather crowd, with resource conservation, "no load banked away from a finished drop-off" and a save-load twin
/// checked every tick.
/// </summary>
public class DropOffSiteQaTests
{
    private readonly ITestOutputHelper _out;

    public DropOffSiteQaTests(ITestOutputHelper output) => _out = output;

    private const int SiteX = 12, SiteY = 12;

    /// <summary>Every shipped building with <c>dropOff</c> true, gold and wood.</summary>
    public static TheoryData<string, bool> DropOffBuildings()
    {
        var rows = new TheoryData<string, bool>();
        foreach (BuildingDef b in TestSim.Data.Buildings)
        {
            if (!b.DropOff) continue;
            rows.Add(b.Key, false);
            rows.Add(b.Key, true);
        }
        return rows;
    }

    private static int WorkerOf(int faction)
    {
        foreach (UnitDef u in TestSim.Data.Units)
            if (u.IsWorker && u.Faction == faction) return u.Id;
        throw new InvalidOperationException($"no worker for faction {faction}");
    }

    private sealed record Scene(Simulation Sim, EntityHandle Worker, EntityHandle Node, EntityHandle Site, bool Wood)
    {
        public World W => Sim.World;
        public int I => Worker.Index;
        public int Carry => W.Data.CarryOf(W.Units.TypeId[I]);
        public long Gathered => Wood ? W.GatheredWood[0] : W.GatheredGold[0];
        public long Bank => Wood ? W.Wood[0] : W.Gold[0];
    }

    /// <summary>A loaded worker of the site's faction 0.5 m left of an own site of <paramref name="key"/> (or at <paramref name="workerAt"/>); player 1's laborer far off.</summary>
    private static Scene Make(string key, bool wood, Vector2? workerAt = null)
    {
        Simulation sim = NewSim(Flat(64, 32), units: 16, players: 2);
        World w = sim.World;
        int type = TestSim.Data.FindBuilding(key);
        EntityHandle node = wood ? Spawn(w, Tree, 44, 20, TreeWood) : Spawn(w, Mine, 44, 4, 2500);
        CombatScenes.Place(sim, 1, Laborer, At(sim, 61, 29));
        EntityHandle worker = CombatScenes.Place(sim, 0, WorkerOf(TestSim.Data.Buildings[type].Faction), workerAt ?? new Vector2(SiteX * 2f - 0.5f, SiteY * 2f + 1f));
        Assert.True(w.Buildings.Spawn(0, type, SiteY * w.NavGrid.Width + SiteX, out EntityHandle site, site: true));
        Assert.True(w.Buildings.UnderConstruction[site.Index]);
        w.Units.CargoKind[worker.Index] = wood ? ResourceKind.Wood : ResourceKind.Gold;
        w.Units.Cargo[worker.Index] = w.Data.CarryOf(w.Units.TypeId[worker.Index]);
        return new Scene(sim, worker, node, site, wood);
    }

    private static Vector2 NodeAt(World w, EntityHandle node)
    {
        int cell = w.Resources.Cell[node.Index];
        return w.NavGrid.CellCenter(cell % w.NavGrid.Width, cell / w.NavGrid.Width);
    }

    private static void StartLoop(Scene s)
    {
        s.Sim.Enqueue(Command.Gather(0, s.Worker, NodeAt(s.W, s.Node)));
        while (s.Sim.PendingCommandCount > 0) s.Sim.Tick();
        Assert.True(EconomySystem.OnLoop(s.W.Units, s.I));
    }

    private static void Finish(World w, EntityHandle site)
    {
        w.Buildings.SetWork(site.Index, w.Buildings.WorkNeeded(w.Buildings.TypeId[site.Index]));
        Assert.False(w.Buildings.UnderConstruction[site.Index]);
    }

    private static bool InReachOf(World w, Vector2 p, int slot)
    {
        BuildingDef def = w.Data.Buildings[w.Buildings.TypeId[slot]];
        return EconomySystem.InReach(w.NavGrid, p, w.Buildings.Cell[slot], def.FootprintWidth, def.FootprintHeight);
    }

    /// <summary>
    /// Every drop-off building of every faction, gold and wood: in reach of the site for 400 ticks the worker banks nothing, its
    /// bank does not move and it keeps its load on the loop; finished, the load lands within 200 ticks, the bank up by exactly one load.
    /// </summary>
    [Theory]
    [MemberData(nameof(DropOffBuildings))]
    public void EveryDropOffBuilding_AsASite_TakesNoLoad_ThenExactlyOneOnceFinished(string key, bool wood)
    {
        Scene s = Make(key, wood);
        Assert.True(InReachOf(s.W, s.W.Units.Position[s.I], s.Site.Index));
        long gathered = s.Gathered, bank = s.Bank;
        StartLoop(s);
        Run(s.Sim, 400);
        Assert.Equal(gathered, s.Gathered);
        Assert.Equal(bank, s.Bank);
        Assert.Equal(s.Carry, s.W.Units.Cargo[s.I]);
        Assert.True(EconomySystem.OnLoop(s.W.Units, s.I), $"{key}: left its loop while the site stood");
        Finish(s.W, s.Site);
        int at = s.Sim.TickNumber;
        while (s.Gathered == gathered && s.Sim.TickNumber < at + 200) s.Sim.Tick();
        Assert.Equal(gathered + s.Carry, s.Gathered);
        Assert.Equal(bank + s.Carry, s.Bank);
    }

    /// <summary>The site cancelled (the command) while the loaded worker waits by it with no finished drop-off: the loop ends, Idle, the load kept, nothing banked.</summary>
    [Fact]
    public void TheSiteCancelledWhileALoadWaits_TheLoopEnds_TheLoadKept()
    {
        Scene s = Make("malazan_depot", wood: true);
        StartLoop(s);
        Run(s.Sim, 100);
        long gathered = s.Gathered;
        s.Sim.Enqueue(Command.Cancel(0, At(s.Sim, SiteX, SiteY)));
        Run(s.Sim, 2 * EconomyConstants.RetryTicks + 4);
        Assert.False(s.W.Buildings.IsAlive(s.Site), "the cancel was refused");
        Assert.False(EconomySystem.OnLoop(s.W.Units, s.I));
        Assert.Equal(UnitState.Idle, s.W.Units.State[s.I]);
        Assert.Equal(s.Carry, s.W.Units.Cargo[s.I]);
        Assert.Equal(gathered, s.Gathered);
    }

    /// <summary>
    /// The site destroyed while a loaded worker walks to it (the pre-rule walk, put back by hand) with a finished hall far off:
    /// the worker delivers at the hall, once.
    /// </summary>
    [Theory]
    [InlineData("whirlwind_supply_cache", false)]
    [InlineData("teblor_woodcutters_lodge", true)]
    public void TheSiteDestroyedMidWalk_WithAHallFarOff_DeliversAtTheHall(string key, bool wood)
    {
        Scene s = Make(key, wood, workerAt: new Vector2(SiteX * 2f - 10f, SiteY * 2f + 1f));
        int hallType = TestSim.Data.Buildings.First(b => b.DropOff && b.Faction == TestSim.Data.Buildings[TestSim.Data.FindBuilding(key)].Faction && b.FootprintWidth == 4).Id;
        EntityHandle hall = Building(s.Sim, 2, 24, type: hallType);
        StartLoop(s);
        BuildingDef def = s.W.Data.Buildings[s.W.Buildings.TypeId[s.Site.Index]];
        Assert.True(EconomySystem.WalkToFootprint(s.W, s.I, s.W.Buildings.Cell[s.Site.Index], def.FootprintWidth, def.FootprintHeight));
        Assert.True(s.W.Buildings.Free(s.Site));
        long gathered = s.Gathered;
        for (int t = 0; t < 3000 && s.Gathered == gathered; t++) s.Sim.Tick();
        Assert.Equal(gathered + s.Carry, s.Gathered);
        Assert.True(InReachOf(s.W, s.W.Units.Position[s.I], hall.Index), "delivered away from the hall");
    }

    /// <summary>
    /// A loaded worker on its loop is ordered to build a drop-off itself (the Build command, real construction): it banks
    /// nothing while building, the site finishes, and the load it still carries lands once it is sent back to gather. The
    /// bank moves only by the site's cost and the one load.
    /// </summary>
    [Fact]
    public void ALoadedWorker_BuildsTheDropOffItself_BanksNothingUntilItIsFinished()
    {
        Simulation sim = BuildMaps.NewSim(Flat(64, 32), units: 16, players: 2);
        World w = sim.World;
        EntityHandle tree = Spawn(w, Tree, 30, 16, TreeWood);
        CombatScenes.Place(sim, 1, Laborer, At(sim, 61, 29));
        EntityHandle worker = CombatScenes.Place(sim, 0, Laborer, At(sim, 27, 16));
        int depot = TestSim.Data.FindBuilding("malazan_depot");
        BuildMaps.Give(sim, 0, 1000, 1000);
        w.Units.CargoKind[worker.Index] = ResourceKind.Wood;
        int carry = w.Data.CarryOf(Laborer);
        w.Units.Cargo[worker.Index] = carry;
        sim.Enqueue(Command.Build(0, worker, depot, At(sim, 24, 15)));
        Run(sim, 2);
        int slot = BuildMaps.SiteAt(sim, 24, 15);
        Assert.True(slot >= 0, "the Build was refused");
        long gathered = w.GatheredWood[0], bank = w.Wood[0];
        for (int t = 0; t < 4000 && w.Buildings.UnderConstruction[slot]; t++)
        {
            sim.Tick();
            Assert.Equal(gathered, w.GatheredWood[0]);
            Assert.Equal(carry, w.Units.Cargo[worker.Index]);
        }
        Assert.False(w.Buildings.UnderConstruction[slot], "never finished");
        Assert.Equal(bank, w.Wood[0]);
        sim.Enqueue(Command.Gather(0, worker, NodeAt(w, tree)));
        for (int t = 0; t < 400 && w.GatheredWood[0] == gathered; t++) sim.Tick();
        Assert.Equal(gathered + carry, w.GatheredWood[0]);
    }

    /// <summary>
    /// 30 loaded workers on one wood loop with only a depot site standing: while it stands nobody banks and nobody wanders
    /// (none moves more than a walker's radius); finished, all 30 loads land, none wedged. The numbers are printed.
    /// </summary>
    [Fact]
    public void ThirtyLoadedWorkers_WaitForOneSite_ThenAllDeliver()
    {
        Simulation sim = NewSim(Flat(64, 48), units: 40, players: 2);
        World w = sim.World;
        EntityHandle tree = Spawn(w, Tree, 34, 24, TreeWood * 4);
        for (int y = 18; y <= 30; y++) if (y != 24) Spawn(w, Tree, 34, y, TreeWood);
        CombatScenes.Place(sim, 1, Laborer, At(sim, 61, 45));
        int depot = TestSim.Data.FindBuilding("malazan_depot");
        Assert.True(w.Buildings.Spawn(0, depot, 23 * w.NavGrid.Width + 28, out EntityHandle site, site: true));
        var workers = new List<EntityHandle>();
        int carry = w.Data.CarryOf(Laborer);
        for (int k = 0; k < 30; k++)
        {
            EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 20 + k % 6, 16 + k / 6 * 3));
            w.Units.CargoKind[h.Index] = ResourceKind.Wood;
            w.Units.Cargo[h.Index] = carry;
            workers.Add(h);
            sim.Enqueue(Command.Gather(0, h, NodeAt(w, tree)));
        }
        Run(sim, 2);
        var start = workers.Select(h => w.Units.Position[h.Index]).ToArray();
        long gathered = w.GatheredWood[0];
        float drift = 0f;
        for (int t = 0; t < 600; t++)
        {
            sim.Tick();
            for (int k = 0; k < workers.Count; k++) drift = MathF.Max(drift, Vector2.Distance(start[k], w.Units.Position[workers[k].Index]));
        }
        Assert.Equal(gathered, w.GatheredWood[0]);
        Assert.All(workers, h => Assert.Equal(carry, w.Units.Cargo[h.Index]));
        Finish(w, site);
        int finished = sim.TickNumber, allIn = -1;
        var delivered = new bool[workers.Count];
        for (int t = 0; t < 3000 && allIn < 0; t++)
        {
            sim.Tick();
            // A full worker cannot take more, so a load below the carry means it banked.
            for (int k = 0; k < workers.Count; k++) delivered[k] |= w.Units.Cargo[workers[k].Index] < carry;
            if (delivered.All(d => d)) allIn = sim.TickNumber - finished;
        }
        _out.WriteLine($"30 loaded workers: max drift while waiting {drift:F2} m; all delivered {allIn} ticks after the site finished; wood banked {w.GatheredWood[0] - gathered}");
        Assert.True(drift <= w.Units.Radius[workers[0].Index], $"a waiting worker wandered {drift:F2} m");
        Assert.True(allIn >= 0, $"{delivered.Count(d => !d)} of 30 never delivered in 3,000 ticks after the site finished");
        Assert.True(w.GatheredWood[0] - gathered >= 30L * carry);
    }

    /// <summary>
    /// Seeded fuzz: 20 workers (Laborers and Teblor Gatherers, each on one kind for good, so no load is dropped) on mines and
    /// trees while drop-off sites of all three factions are placed, finished, damaged and destroyed at random, gathers re-sent
    /// and spammed, moves given. Every tick: gold and wood conserved (banks + cargo + nodes); a load leaves a worker only in
    /// reach of an own finished drop-off; cargo within carry; positions finite. A twin is saved and loaded every 700 ticks and
    /// hashes alike every tick.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void SitesFuzz_ConservationAndFinishedOnlyDeposits_EveryTick(int seed)
    {
        var rng = new Random(seed);
        Simulation a = FuzzScene();
        Simulation b = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(a)); // the twin starts from a's bytes
        World w = a.World;
        (long gold, long wood) = Conserved(w);
        int cap = w.Units.Capacity;
        var prevCargo = new int[cap];
        var prevKind = new ResourceKind[cap];
        var prevPos = new Vector2[cap];
        int[] dropOffs = TestSim.Data.Buildings.Where(d => d.DropOff).Select(d => d.Id).ToArray();
        int deposits = 0, sitesPlaced = 0, sitesFinished = 0, destroyed = 0, waitsBySite = 0;
        for (int t = 0; t < 6000; t++)
        {
            if (t % 23 == 0) Act(rng, a, b, dropOffs, ref sitesPlaced, ref sitesFinished, ref destroyed);
            for (int i = 0; i < cap; i++) { prevCargo[i] = w.Units.Alive[i] ? w.Units.Cargo[i] : 0; prevKind[i] = w.Units.CargoKind[i]; prevPos[i] = w.Units.Position[i]; }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: the save-load twin diverged at tick {a.TickNumber}");
            Assert.Equal((gold, wood), Conserved(w));
            for (int i = 0; i < cap; i++)
            {
                if (!w.Units.Alive[i]) continue;
                Vector2 p = w.Units.Position[i];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y));
                Assert.InRange(w.Units.Cargo[i], 0, w.Data.CarryOf(w.Units.TypeId[i]));
                if (prevCargo[i] > 0 && w.Units.Cargo[i] == 0 && prevKind[i] == w.Units.CargoKind[i])
                {
                    deposits++;
                    bool finished = false;
                    for (int k = 0; k < w.Buildings.Capacity && !finished; k++)
                        finished = w.Buildings.Alive[k] && w.Buildings.Owner[k] == w.Units.Owner[i] && !w.Buildings.UnderConstruction[k]
                            && w.Data.Buildings[w.Buildings.TypeId[k]].DropOff && (InReachOf(w, p, k) || InReachOf(w, prevPos[i], k)); // the deposit (phase 4) comes before the tick's movement
                    Assert.True(finished, $"seed {seed} tick {a.TickNumber}: unit {i} banked a load at {p} (from {prevPos[i]}) with no finished drop-off in reach");
                }
                if (EconomySystem.OnLoop(w.Units, i) && w.Units.State[i] == UnitState.Returning && w.Units.Cargo[i] > 0) waitsBySite++;
            }
            if (t % 700 == 699) // the twin goes through bytes again
            {
                byte[] bytes = SaveTestRun.Save(b);
                b = SaveTestRun.Load(TestSim.Data, bytes);
                Assert.Equal(bytes, SaveTestRun.Save(b));
            }
        }
        _out.WriteLine($"seed {seed}: {deposits} deposits, {sitesPlaced} sites placed, {sitesFinished} finished, {destroyed} buildings destroyed, {waitsBySite} worker-ticks standing loaded");
        Assert.True(deposits > 20, "the fuzz delivered almost nothing: it tests nothing");
    }

    private static Simulation FuzzScene()
    {
        Simulation sim = NewSim(Flat(64, 64), units: 32, players: 2, seed: 7);
        World w = sim.World;
        var rng = new Random(1234);
        for (int k = 0; k < 3; k++) Spawn(w, Mine, 8 + k * 20, 50, 3000);
        for (int k = 0; k < 24; k++) Spawn(w, Tree, 50 + k % 3 * 3, 6 + k / 3 * 3, TreeWood);
        CombatScenes.Place(sim, 1, Laborer, At(sim, 61, 61));
        int gatherer = GatherOverlapScenes.GathererType;
        for (int k = 0; k < 20; k++)
            CombatScenes.Place(sim, 0, k % 4 == 3 ? gatherer : Laborer, At(sim, 10 + rng.Next(40), 10 + rng.Next(30)));
        return sim;
    }

    /// <summary>One random act, applied alike to both twins (they hold the same state, so the same slots).</summary>
    private static void Act(Random rng, Simulation a, Simulation b, int[] dropOffs, ref int placed, ref int finished, ref int destroyed)
    {
        World w = a.World;
        int roll = rng.Next(10);
        if (roll <= 1)
        {
            int type = dropOffs[rng.Next(dropOffs.Length)];
            BuildingDef def = w.Data.Buildings[type];
            int x = 4 + rng.Next(56), y = 4 + rng.Next(56), cell = y * w.NavGrid.Width + x;
            if (!w.Buildings.Fits(type, cell) || !w.Seal.KeepsConnected(x, y, def.FootprintWidth, def.FootprintHeight)) return;
            for (int i = 0; i < w.Units.Capacity; i++)
            {
                if (!w.Units.Alive[i]) continue;
                Vector2 p = w.Units.Position[i];
                if (p.X >= x * 2f - 1f && p.X < (x + def.FootprintWidth) * 2f + 1f && p.Y >= y * 2f - 1f && p.Y < (y + def.FootprintHeight) * 2f + 1f) return;
            }
            Assert.True(a.World.Buildings.Spawn(0, type, cell, out _, site: true));
            Assert.True(b.World.Buildings.Spawn(0, type, cell, out _, site: true));
            placed++;
            return;
        }
        int slot = PickBuilding(rng, w);
        if (roll == 2 && slot >= 0 && w.Buildings.UnderConstruction[slot])
        {
            foreach (Simulation s in new[] { a, b }) s.World.Buildings.SetWork(slot, s.World.Buildings.WorkNeeded(s.World.Buildings.TypeId[slot]));
            finished++;
            return;
        }
        if (roll == 3 && slot >= 0)
        {
            foreach (Simulation s in new[] { a, b }) Assert.True(s.World.Buildings.Free(s.World.Buildings.HandleOf(slot)));
            destroyed++;
            return;
        }
        if (roll == 4 && slot >= 0)
        {
            foreach (Simulation s in new[] { a, b }) s.World.Buildings.Damage(s.World.Buildings.HandleOf(slot), 50);
            return;
        }
        int unit = PickWorker(rng, w);
        if (unit < 0) return;
        var h = new EntityHandle(unit, w.Units.Generation[unit]);
        bool goldWorker = w.Units.TypeId[unit] == GatherOverlapScenes.GathererType || unit % 2 == 0;
        int node = PickNode(rng, w, goldWorker ? ResourceKind.Gold : ResourceKind.Wood);
        if (roll <= 7 && node >= 0)
        {
            int cell = w.Resources.Cell[node];
            Vector2 at = w.NavGrid.CellCenter(cell % w.NavGrid.Width, cell / w.NavGrid.Width);
            int times = roll == 7 ? 3 : 1;
            for (int k = 0; k < times; k++)
            {
                a.Enqueue(Command.Gather(0, h, at));
                b.Enqueue(Command.Gather(0, h, at));
            }
            return;
        }
        Vector2 to = new(4f + rng.Next(110), 4f + rng.Next(110));
        a.Enqueue(Command.Move(0, h, to));
        b.Enqueue(Command.Move(0, h, to));
    }

    private static int PickBuilding(Random rng, World w)
    {
        int n = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++) if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0) n++;
        if (n == 0) return -1;
        int pick = rng.Next(n);
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && pick-- == 0) return k;
        return -1;
    }

    private static int PickWorker(Random rng, World w)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++) if (w.Units.Alive[i] && w.Units.Owner[i] == 0) n++;
        if (n == 0) return -1;
        int pick = rng.Next(n);
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && pick-- == 0) return i;
        return -1;
    }

    private static int PickNode(Random rng, World w, ResourceKind kind)
    {
        int n = 0;
        ResourceStore r = w.Resources;
        for (int k = 0; k < r.Capacity; k++) if (r.Alive[k] && w.Data.Resources[r.TypeId[k]].Resource == kind) n++;
        if (n == 0) return -1;
        int pick = rng.Next(n);
        for (int k = 0; k < r.Capacity; k++)
            if (r.Alive[k] && w.Data.Resources[r.TypeId[k]].Resource == kind && pick-- == 0) return k;
        return -1;
    }
}
