using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Replays;

namespace Rts.Sim.Tests;

/// <summary>CLAUDE.md rule 5: per-tick code must not allocate.</summary>
[Collection(SerialCollection.Name)]
public class AllocationTests
{
    private static Simulation WarmSim()
    {
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: 512, CommandCapacity: 256));
        // Warm-up: JIT both the empty-tick and the spawn path before measuring.
        sim.Tick();
        sim.Enqueue(Command.SpawnUnit(1, typeId: 0, Vector2.One));
        sim.Enqueue(Command.Noop(0));
        sim.Tick();
        sim.Tick();
        return sim;
    }

    [Fact]
    public void Tick_WithNoCommands_AllocatesNothing()
    {
        Simulation sim = WarmSim();

        Action tick = sim.Tick;
        AllocationProbe.AssertZero(tick);
    }

    [Fact]
    public void Tick_With100SpawnCommands_AllocatesNothing()
    {
        Simulation sim = WarmSim();
        // Enqueue from both players, out of player order, so the sort has real work to do.
        Action queue = () =>
        {
            for (int i = 0; i < 100; i++)
                sim.Enqueue(Command.SpawnUnit(1 - (i % 2), typeId: i % TestSim.UnitTypeCount, new Vector2(i, i)));
        };

        // Two ticks: the first sorts the unsorted batch, the second applies it.
        Action twoTicks = () =>
        {
            sim.Tick();
            sim.Tick();
        };
        int runs = AllocationProbe.AssertZero(twoTicks, setup: queue);

        Assert.Equal(1 + runs * 100, sim.World.Units.Count);
    }

    [Fact]
    public void SpatialHash_RebuildAnd1000Queries_On500Units_AllocateNothing()
    {
        var sim = new Simulation(TestSim.Config(Seed: 4, PlayerCount: 2, UnitCapacity: 512, CommandCapacity: 512));
        for (int i = 0; i < 500; i++)
            sim.Enqueue(Command.SpawnUnit(i % 2, typeId: 0, new Vector2((i * 37) % 256, (i * 91) % 256)));
        sim.Tick();
        sim.Tick(); // the spawns apply on tick 1
        Spatial.SpatialHash hash = sim.World.Spatial;
        Assert.Equal(500, hash.Count);
        int[] buffer = new int[64]; // smaller than some results, so truncation runs too
        int sink = RunHashQueries(sim, hash, buffer, 50); // JIT warm-up

        Action block = () =>
        {
            sim.Tick(); // includes the rebuild
            hash.Rebuild(sim.World.Units);
            sink += RunHashQueries(sim, hash, buffer, 1000);
        };
        AllocationProbe.AssertZero(block);

        Assert.NotEqual(0, sink);
    }

    private static int RunHashQueries(Simulation sim, Spatial.SpatialHash hash, int[] buffer, int count)
    {
        int sink = 0;
        for (int i = 0; i < count; i++)
        {
            var c = new Vector2((i * 13) % 300 - 20, (i * 29) % 300 - 20);
            switch (i % 3)
            {
                case 0: sink += hash.QueryRadius(c, 2f + i % 40, buffer); break;
                case 1: sink += hash.QueryRect(c, c + new Vector2(-30f, 25f), buffer); break;
                default: if (hash.NearestEnemy(c, 30f, i % 2, out int slot)) sink += slot + 1; break;
            }
        }
        return sink + sim.TickNumber;
    }

    [Fact]
    public void Tick_With500MovingUnits_AndACacheMiss_AllocatesNothing()
    {
        Simulation sim = MoveScenario.Spawn(seed: 13, units: 500, maxCost: float.MaxValue, out int goalCell);
        var g = sim.World.NavGrid;
        Vector2 goal = MoveScenario.Center(g, goalCell);
        // Warm-up: a first group Move (JITs Apply, a cache miss and the movement loop) and some walking.
        MoveScenario.MoveAll(sim, goal);
        for (int i = 0; i < 10; i++) sim.Tick();
        int builds = sim.World.FlowFields.BuildCount;
        Vector2 next = goal + new Vector2(6f, 0f);
        Assert.True(g.WorldToCell(next, out int nx, out int ny));
        Assert.False(sim.World.FlowFields.Contains(ny * g.Width + nx));

        Action block = () =>
        {
            sim.Tick();                     // a plain moving tick
            MoveScenario.MoveAll(sim, next); // a new target cell (or its nearest passable cell): not cached
            sim.Tick();
            sim.Tick();                     // applies the Moves and builds the field inside the tick
        };
        AllocationProbe.AssertZero(block);

        // A re-run re-issues the same Moves: the field is cached by then, so still one build.
        Assert.Equal(builds + 1, sim.World.FlowFields.BuildCount);
        int moving = 0;
        for (int i = 0; i < 500; i++) if (sim.World.Units.State[i] == Entities.UnitState.Moving) moving++;
        Assert.True(moving > 400, $"{moving} moving");
    }
    /// <summary>M1-6 criterion 7: a recorder's checkpoint hash runs inside Tick, into a preallocated buffer.</summary>
    [Fact]
    public void Tick_WithReplayRecorder_500TicksAnd5Checkpoints_AllocatesNothing()
    {
        (Simulation sim, ReplayRecorder rec) = ReplayTestRun.RecordSmall(seed: 13, ticks: 200);
        Assert.Equal(200, sim.TickNumber);
        Assert.Equal(2, rec.CheckpointCount); // warm: the checkpoint path has run
        var g = sim.World.NavGrid;
        var far = new[] { MoveScenario.Center(g, MoveScenario.CentralCell(g)) + new Vector2(30f, 0f), MoveScenario.Center(g, MoveScenario.CentralCell(g)) };
        int run = 0;
        Action order = () => MoveScenario.MoveAll(sim, far[run++ % 2]); // unmeasured; Enqueue is off-tick
        Action ticks = () =>
        {
            for (int t = 0; t < 500; t++) sim.Tick();
        };
        int before = rec.CheckpointCount;
        int runs = AllocationProbe.AssertZero(ticks, setup: order);
        Assert.Equal(before + 5 * runs, rec.CheckpointCount);
        Assert.Equal(200 + 500 * runs, sim.TickNumber);
    }

    /// <summary>
    /// M1-7 criterion 7: 500 units cycling shift-queued orders (an unqueued Move, then queued Move,
    /// AttackMove, and for some a terminal Stop or HoldPosition) among four nearby points: applying
    /// them and phase 7 popping them allocates nothing.
    /// </summary>
    [Fact]
    public void Tick_With500UnitsCyclingQueuedOrders_AllocatesNothing()
    {
        Simulation sim = MoveScenario.Spawn(seed: 14, units: 500, maxCost: 14f, out int goalCell, capacity: 1000, players: 1); // capacity: room for 3-4 commands per unit
        var g = sim.World.NavGrid;
        Vector2 c = MoveScenario.Center(g, goalCell);
        Vector2[] points = { c + new Vector2(-6f, -6f), c + new Vector2(6f, -6f), c + new Vector2(6f, 6f), c + new Vector2(-6f, 6f) };
        Entities.UnitStore u = sim.World.Units;
        int queuedPerRun = 0;
        Action order = () =>
        {
            queuedPerRun = 0;
            for (int i = 0; i < 500; i++)
            {
                var h = MoveScenario.Handle(sim, i);
                sim.Enqueue(Command.Move(0, h, points[i % 4]));
                sim.Enqueue(Command.Move(0, h, points[(i + 1) % 4], queued: true));
                sim.Enqueue(Command.AttackMove(0, h, points[(i + 2) % 4], queued: true));
                queuedPerRun += 2;
                if (i % 5 == 0) { sim.Enqueue(Command.Stop(0, h, queued: true)); queuedPerRun++; }
                else if (i % 7 == 0) { sim.Enqueue(Command.HoldPosition(0, h, queued: true)); queuedPerRun++; }
            }
        };
        Action ticks = () =>
        {
            for (int t = 0; t < 200; t++) sim.Tick();
        };
        order();
        ticks(); // warm-up: JITs the order paths and pops
        AllocationProbe.AssertZero(ticks, setup: order);
        int left = 0, holding = 0;
        for (int i = 0; i < 500; i++) { left += u.QueueCount[i]; if (u.Hold[i]) holding++; }
        Assert.True(left <= queuedPerRun - 150, $"{left} of {queuedPerRun} queued orders still waiting: too few pops measured");
        Assert.True(holding > 0, "no queued HoldPosition was reached");
    }

    // ---------- M3-1: resource nodes ----------

    /// <summary>Spawns trees on every cell that takes one, in index order, until <paramref name="count"/> stand.</summary>
    private static void FillTrees(Simulation sim, int count, int amount)
    {
        Map.NavGrid g = sim.World.NavGrid;
        Entities.ResourceStore r = sim.World.Resources;
        for (int c = 0; c < g.Width * g.Height && r.Count < count; c++)
            r.Spawn(ResourceMaps.Tree, c, amount, out _);
        Assert.Equal(count, r.Count);
    }

    [Fact]
    public void TickAndStateHash_With4000LiveNodes_AllocateNothing()
    {
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 64));
        FillTrees(sim, 4000, ResourceMaps.TreeWood);
        ulong sink = 0;
        sim.Tick();
        sink ^= sim.StateHash(); // JIT warm-up
        Action block = () =>
        {
            sim.Tick();
            sink ^= sim.StateHash();
        };
        AllocationProbe.AssertZero(block);
        Assert.Equal(4000, sim.World.Resources.Count);
        _ = sink;
    }

    /// <summary>M3-2b: a tree felled before every measured tick, 32 goal groups walking stale-but-usable fields while the build pass refreshes 2 a tick.</summary>
    [Fact]
    public void Tick_WithATreeFelledEveryTick_AndRefreshesRunning_AllocatesNothing()
    {
        (Simulation sim, Entities.EntityHandle[] trees) = GridChangeMovementTests.ThirtyTwoGroups();
        int k = 0;
        Action fell = () => Assert.Equal(ResourceMaps.TreeWood, sim.World.Resources.Take(trees[k++], ResourceMaps.TreeWood));
        fell();
        sim.Tick(); // JIT warm-up: the refresh path and the step masks
        int builds = sim.World.FlowFields.BuildCount;
        int runs = AllocationProbe.AssertZero(sim.Tick, setup: fell);
        Assert.Equal(builds + 2 * runs, sim.World.FlowFields.BuildCount); // the cap's 2 refreshes ran every measured tick
        Assert.Equal(32, Enumerable.Range(0, 32).Count(i => sim.World.Units.State[i] == Entities.UnitState.Moving));
    }

    [Fact]
    public void HundredTakes_IncludingFifty_Frees_AllocateNothing()
    {
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 8));
        Entities.ResourceStore r = sim.World.Resources;
        var handles = new Entities.EntityHandle[50];
        Action setup = () =>
        {
            Assert.Equal(0, r.Count);
            FillTrees(sim, 50, 2);
            for (int i = 0; i < 50; i++) handles[i] = r.HandleOf(i);
        };
        int taken = 0;
        Action block = () =>
        {
            for (int k = 0; k < 100; k++) taken += r.Take(handles[k % 50], 1); // the second pass empties each tree
        };
        setup();
        block(); // JIT warm-up: Take, Free and the grid's ClearResource
        int versionBefore = sim.World.NavGrid.Version;
        int runs = AllocationProbe.AssertZero(block, setup: setup);
        Assert.Equal(0, r.Count);
        Assert.Equal(100 * (runs + 1), taken);
        Assert.Equal(versionBefore + runs * 100, sim.World.NavGrid.Version); // 50 spawns + 50 frees per run
    }

    /// <summary>M3-3 criterion 11: 100 ticks holding a Build (new site, joined), a Cancel, a Repair and a completion, plus CanPlace queries, allocate nothing.</summary>
    [Fact]
    public void Tick_WithABuildACancelARepairAndACompletion_AllocatesNothing()
    {
        Simulation sim = BuildMaps.NewSim(ResourceMaps.Flat(64, 40));
        Entities.EntityHandle keep = GatherMaps.Building(sim, 4, 4);
        Entities.EntityHandle[] repairers = BuildMaps.WorkersRound(sim, 4, 4, 4, 4, 2);
        Entities.EntityHandle[] builders = Enumerable.Range(0, 3).Select(i => GatherMaps.Unit(sim, GatherMaps.At(sim, 18 + 2 * i, 30))).ToArray();
        BuildMaps.Give(sim, 0, 1_000_000, 1_000_000);
        Entities.BuildingStore b = sim.World.Buildings;
        int run = 0, sink = 0;
        Action block = () =>
        {
            int x = 16 + 3 * run, y = 26; // sites a few cells from the builders, so they arrive and work
            sim.Enqueue(Command.Build(0, builders[0], BuildMaps.House, GatherMaps.At(sim, x, y)));
            sim.Enqueue(Command.Build(0, builders[1], BuildMaps.House, GatherMaps.At(sim, x, y)));
            sim.Enqueue(Command.Build(0, builders[2], BuildMaps.House, GatherMaps.At(sim, x, y + 5)));
            sim.Enqueue(Command.Repair(0, repairers[0], GatherMaps.At(sim, 5, 5)));
            sim.Enqueue(Command.Repair(0, repairers[1], GatherMaps.At(sim, 5, 5)));
            sim.Tick();
            sim.Tick();
            for (int c = 0; c < 64 * 40; c += 7)
                if (sim.World.CanPlace(0, GatherMaps.Keep, c, out _)) sink++;
            int k = BuildMaps.SiteAt(sim, x, y);
            b.SetWork(k, b.WorkNeeded(BuildMaps.House) - 40); // completes inside the block
            sim.Enqueue(Command.Cancel(0, GatherMaps.At(sim, x, y + 5)));
            for (int t = 0; t < 98; t++) sim.Tick();
            run++;
        };
        Action damage = () => b.Damage(keep, 300);
        damage();
        block(); // warm-up: JIT every path once
        Assert.False(b.UnderConstruction[BuildMaps.SiteAt(sim, 16, 26)]);
        AllocationProbe.AssertZero(block, setup: damage);
        Assert.True(sink > 0);
        Assert.True(b.Hp[keep.Index] > 2400 - 600, "the repairers worked");
        Assert.Equal(-1, BuildMaps.SiteAt(sim, 16 + 3 * (run - 1), 31)); // cancelled
        Assert.False(b.UnderConstruction[BuildMaps.SiteAt(sim, 16 + 3 * (run - 1), 26)]); // completed
    }
    /// <summary>
    /// M3-H1 criterion 10: the pocket decision (a building freed into a pocket, ring only and with the flood), refused
    /// Builds on the cheap-first path, and a placement that pushes own units out allocate nothing.
    /// </summary>
    [Fact]
    public void PocketDecisions_RefusedBuilds_AndAPushOut_AllocateNothing()
    {
        Simulation sim = null!;
        Entities.EntityHandle c = default, d = default, worker = default;
        Action setup = () =>
        {
            (sim, c, d, _, _) = PocketRuleTests.Enclosure();
            worker = GatherMaps.Unit(sim, GatherMaps.At(sim, 22, 4));
            for (int k = 0; k < 4; k++) GatherMaps.Unit(sim, GatherMaps.At(sim, 24, 14) + new System.Numerics.Vector2(0.3f * k, 0f));
            BuildMaps.SetTotals(sim, 0, 100, 100); // a Keep (275 / 275) can't be afforded, a House (0 / 50) can
        };
        Action block = () =>
        {
            Entities.BuildingStore b = sim.World.Buildings;
            b.Damage(c, 100_000); // into a pocket: the ring alone decides
            b.Damage(d, 100_000); // beside that pocket: the flood decides
            for (int k = 0; k < 10; k++) sim.Enqueue(Command.Build(0, worker, GatherMaps.Keep, GatherMaps.At(sim, 20, 2)));
            sim.Enqueue(Command.Build(0, worker, BuildMaps.House, GatherMaps.At(sim, 24, 14))); // on four own units: push-out
            sim.Tick();
            sim.Tick();
        };
        setup();
        block(); // JIT warm-up
        Assert.Equal(Map.NavFlags.Blocked | Map.NavFlags.Pocket, sim.World.NavGrid.FlagsAt(12, 8));
        Assert.True(BuildMaps.SiteAt(sim, 24, 14) >= 0);
        Assert.True(BuildMaps.SiteAt(sim, 20, 2) < 0);
        AllocationProbe.AssertZero(block, setup: setup);
    }
    /// <summary>
    /// M3-4 criterion 10: ticks applying Train, CancelTrain, SetRally and ClearRally, CanTrain queries, and production
    /// running heads to completion and spawning (one worker rallied onto a mine, one infantry with no rally point)
    /// allocate nothing.
    /// </summary>
    [Fact]
    public void Tick_WithTrainCancelTrainSetRallyClearRally_AndSpawns_AllocatesNothing()
    {
        Simulation sim = BuildMaps.NewSim(ResourceMaps.Flat(48, 32), units: 64);
        int keep = GatherMaps.Building(sim, 4, 4).Index;
        int bar = GatherMaps.Building(sim, 14, 4, type: ProductionMaps.Barracks).Index;
        for (int k = 0; k < 4; k++) GatherMaps.Building(sim, 4 + 4 * k, 20, type: BuildMaps.House);
        ResourceMaps.Spawn(sim.World, ResourceMaps.Mine, 30, 10, 100_000);
        BuildMaps.Give(sim, 0, 1_000_000, 1_000_000);
        System.Numerics.Vector2 atKeep = ProductionMaps.In(sim, keep), atBar = ProductionMaps.In(sim, bar), mine = GatherMaps.At(sim, 31, 11);
        int keepCell = sim.World.Buildings.Cell[keep], barCell = sim.World.Buildings.Cell[bar];
        int sink = 0;
        Action block = () =>
        {
            sim.Enqueue(Command.SetRally(0, keepCell, mine));
            sim.Enqueue(Command.SetRally(0, barCell, mine));
            sim.Enqueue(Command.ClearRally(0, atBar));
            sim.Enqueue(Command.Train(0, atKeep, GatherMaps.Laborer));
            sim.Enqueue(Command.Train(0, atKeep, GatherMaps.Laborer));
            sim.Enqueue(Command.Train(0, atKeep, GatherMaps.Laborer));
            sim.Enqueue(Command.Train(0, atBar, GatherMaps.Infantry));
            sim.Enqueue(Command.CancelTrain(0, atKeep, 2));
            for (int t = 0; t < 300; t++)
            {
                sim.Tick();
                if (sim.World.CanTrain(0, keep, GatherMaps.Laborer, out _)) sink++;
            }
        };
        block(); // warm-up: JIT every path once
        Assert.Equal(1, sim.World.Buildings.QueueCount[keep]); // one of the two left is trained, the next is training
        int units = sim.World.Units.Count;
        AllocationProbe.AssertZero(block);
        Assert.True(sim.World.Units.Count >= units + 2, "spawns happened in the measured block");
        Assert.True(sink > 0);
    }

    /// <summary>
    /// M3-5 criterion 9: ticks applying Research and a CancelTrain of a queued tech, a Train behind it, CanResearch and
    /// TechBonus queries, and research running to completion (setting the tech flag and the bonus sums) allocate nothing.
    /// </summary>
    [Fact]
    public void Tick_WithResearchCancelAndCompletion_AndTheTechQueries_AllocatesNothing()
    {
        Simulation sim = BuildMaps.NewSim(ResourceMaps.Flat(48, 32), units: 64);
        int keep = GatherMaps.Building(sim, 4, 4).Index;
        int armory = GatherMaps.Building(sim, 14, 4, type: ResearchMaps.Armory).Index;
        BuildMaps.Give(sim, 0, 1_000_000, 1_000_000);
        World w = sim.World;
        System.Numerics.Vector2 atKeep = ProductionMaps.In(sim, keep), atArmory = ProductionMaps.In(sim, armory);
        int armoryType = w.Buildings.TypeId[armory];
        float sink = 0f;
        int researched = 0;
        Action block = () =>
        {
            int pick = -1, other = -1;
            foreach (int t in w.Data.TechsResearchableAt(armoryType))
            {
                if (!w.CanResearch(0, armory, t, out _)) continue;
                if (pick < 0) pick = t;
                else if (other < 0) other = t;
            }
            sim.Enqueue(Command.Research(0, atArmory, pick));
            sim.Enqueue(Command.Research(0, atArmory, other));
            sim.Enqueue(Command.CancelTrain(0, atArmory, 1)); // the second tech, refunded
            sim.Enqueue(Command.Train(0, atKeep, GatherMaps.Laborer));
            for (int t = 0; t < 950; t++)
            {
                sim.Tick();
                if (w.CanResearch(0, armory, other, out _)) sink += 1f;
                sink += w.TechBonus(0, GatherMaps.Infantry, Data.TechStat.Attack) + w.TechBonus(0, GatherMaps.Infantry, Data.TechStat.Armor);
            }
            if (w.HasTech(0, pick)) researched++;
        };
        block(); // warm-up: JIT every path once
        Assert.Equal(1, researched);
        AllocationProbe.AssertZero(block);
        Assert.Equal(2, researched); // a tech completed in the measured block
        Assert.True(sink > 0f);
    }

    /// <summary>
    /// M3-6 criterion 8: ticks applying locked Train / Research / Build commands (each refused by its requirement gate),
    /// and the three gates queried both locked and open (a building requirement, Age II's any-of over the halls, a
    /// level-2 upgrade's two techs), allocate nothing.
    /// </summary>
    [Fact]
    public void LockedCommands_AndTheThreeRequirementGates_AllocateNothing()
    {
        Data.GameData d = RequirementGatingTests.Fixture;
        var sim = new Simulation(new SimConfig(5, 1, 64, 1024) { Data = d }, ResourceMaps.Flat(64, 48));
        World w = sim.World;
        int keep = GatherMaps.Building(sim, 4, 4).Index;
        int yard = GatherMaps.Building(sim, 12, 4, type: ProductionMaps.EngineersYard).Index;
        int armory = GatherMaps.Building(sim, 20, 4, type: ResearchMaps.Armory).Index;
        int bar = GatherMaps.Building(sim, 28, 4, type: ProductionMaps.Barracks).Index;
        GatherMaps.Building(sim, 36, 4, type: d.FindBuilding("malazan_crossbow_range"));
        Entities.EntityHandle worker = GatherMaps.Unit(sim, GatherMaps.At(sim, 30, 20));
        BuildMaps.Give(sim, 0, 1_000_000, 1_000_000);
        int tower = d.FindBuilding("malazan_cadre_tower");
        System.Numerics.Vector2 atKeep = ProductionMaps.In(sim, keep), atYard = ProductionMaps.In(sim, yard), atArmory = ProductionMaps.In(sim, armory);
        System.Numerics.Vector2 site = GatherMaps.At(sim, 40, 20);
        int cell = 20 * w.NavGrid.Width + 40;
        int open = 0;
        Action block = () =>
        {
            for (int t = 0; t < 50; t++)
            {
                sim.Enqueue(Command.Train(0, atYard, ProductionMaps.Sapper));
                sim.Enqueue(Command.Research(0, atArmory, ResearchMaps.Melee2));
                sim.Enqueue(Command.Build(0, worker, tower, site));
                sim.Tick();
                if (w.CanTrain(0, yard, ProductionMaps.Sapper, out _)) open++;
                if (w.CanTrain(0, bar, GatherMaps.Infantry, out _)) open++;          // building requirement met
                if (w.CanResearch(0, keep, ResearchMaps.AgeII, out _)) open++;       // any-of: Barracks + Range + Armory
                if (w.CanResearch(0, armory, ResearchMaps.Melee2, out _)) open++;
                if (w.CanPlace(0, tower, cell, out _)) open++;
            }
        };
        block(); // warm-up: JIT every path once
        AllocationProbe.AssertZero(block);
        Assert.Equal(0, w.Buildings.QueueCount[yard] + w.Buildings.QueueCount[armory]);
        Assert.True(open > 0, "no gate was ever open");
    }
    /// <summary>
    /// M3-H2: a push-out on a full plateau (the leftovers' passes), a House's seal answer asked again (the memo's hit)
    /// and after a grid change (its miss), and production waiting on a full plateau (the per-plateau memo and the box
    /// fill from the unit store) allocate nothing.
    /// </summary>
    [Fact]
    public void PushOutLeftovers_SealMemo_AndSpawnsWaitingOnAFullPlateau_AllocateNothing()
    {
        (Simulation sim, int worker) = PlateauTests.ThirtyOnASmallPlateau();
        World w = sim.World;
        System.Numerics.Vector2 at = GatherMaps.At(sim, 30, 30);
        (Simulation full, int keep, List<(int X, int Y)> free) = ProductionTests.PlateauKeep(48, 20);
        foreach ((int x, int y) in free) full.Enqueue(Command.SpawnUnit(1, ProductionMaps.Raider, GatherMaps.At(full, x, y)));
        GatherMaps.Run(full, 2);
        BuildMaps.SetTotals(full, 0, 1000, 1000);
        full.Enqueue(Command.Train(0, ProductionMaps.In(full, keep), GatherMaps.Laborer));
        GatherMaps.Run(full, full.World.Data.Units[GatherMaps.Laborer].TrainTicks + 4);
        Assert.Equal(1, full.World.Buildings.QueueCount[keep]); // complete, waiting on the full plateau
        int sink = 0, builds = 0;
        Action block = () =>
        {
            for (int round = 0; round < 5; round++)
            {
                PlateauTests.PackIntoTheHouse(sim);
                if (Economy.ConstructionSystem.StartBuild(w, worker, BuildMaps.House, at, replaceQueue: true)) builds++;
                // Beside the border, so the ring isn't all open and the flood runs (then its kept answer is read).
                if (w.CanPlace(0, BuildMaps.House, w.NavGrid.Width + 1, out _)) sink++;
                if (w.CanPlace(0, BuildMaps.House, w.NavGrid.Width + 1, out _)) sink++;
                sim.Enqueue(Command.Cancel(0, at));
                sim.Tick();
                sim.Tick();
                full.Tick();
            }
        };
        block();
        AllocationProbe.AssertZero(block);
        Assert.True(sink >= 0);
        Assert.Equal(10, builds); // every round placed the House on the 30 (so the leftovers' passes ran)
        Assert.Equal(1, full.World.Buildings.QueueCount[keep]);
    }
}
