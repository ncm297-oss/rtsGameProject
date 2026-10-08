using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using System.Reflection;

namespace Rts.Sim.Tests;

public class StateHashTests
{
    private const int Ticks = 1000;

    private static SimConfig Config(ulong seed) =>
        TestSim.Config(Seed: seed, PlayerCount: 3, UnitCapacity: 128, CommandCapacity: 64);

    // A fixed script: spawns from all players on a few ticks, plus RNG draws driven by the sim's
    // own streams, so the hash covers units, RNG states, and pending commands.
    private static Simulation Run(ulong seed, bool extraSpawn = false)
    {
        var sim = new Simulation(Config(seed));
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 50 == 0)
            {
                for (int p = 0; p < 3; p++)
                {
                    ref SimRng rng = ref sim.World.Rng(RngStream.Ai(p));
                    var pos = new Vector2(rng.NextFloat() * 100f, rng.NextFloat() * 100f);
                    sim.Enqueue(Command.SpawnUnit(p, typeId: rng.NextInt(0, 4), pos));
                }
            }
            if (extraSpawn && t == 500)
                sim.Enqueue(Command.SpawnUnit(0, typeId: 0, Vector2.Zero));
            sim.Tick();
        }
        return sim;
    }

    [Fact]
    public void SameSeedAndCommands_GiveEqualHash()
    {
        Simulation a = Run(1234);
        Simulation b = Run(1234);
        Assert.Equal(Ticks, a.TickNumber);
        Assert.True(a.World.Units.Count > 0);
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void DifferentSeed_GivesDifferentHash()
    {
        Assert.NotEqual(Run(1234).StateHash(), Run(1235).StateHash());
    }

    [Fact]
    public void OneExtraSpawn_GivesDifferentHash()
    {
        Assert.NotEqual(Run(1234).StateHash(), Run(1234, extraSpawn: true).StateHash());
    }

    [Fact]
    public void Hash_CoversTickPendingCommandsAndRng()
    {
        var a = new Simulation(Config(5));
        var b = new Simulation(Config(5));
        Assert.Equal(a.StateHash(), b.StateHash());

        b.Enqueue(Command.Noop(0));
        Assert.NotEqual(a.StateHash(), b.StateHash());

        var c = new Simulation(Config(5));
        c.World.Rng(RngStream.Combat).NextUInt();
        Assert.NotEqual(a.StateHash(), c.StateHash());

        var d = new Simulation(Config(5));
        d.Tick();
        Assert.NotEqual(a.StateHash(), d.StateHash());
    }

    [Fact]
    public void Hash_IsStableValueForFreshWorld()
    {
        // Guards against accidental use of per-process randomized hashing.
        ulong h1 = new Simulation(Config(77)).StateHash();
        ulong h2 = new Simulation(Config(77)).StateHash();
        Assert.Equal(h1, h2);
        Assert.NotEqual(0UL, h1);
    }

    // ---------- M1-4b: movement ----------

    /// <summary>100 units walking: one group Move at tick 0, half of them re-targeted at tick 200. Hash after every tick.</summary>
    private static ulong[] RunMoves(ulong seed)
    {
        Simulation sim = MoveScenario.Spawn(seed, units: 100, maxCost: 60f, out int goalCell);
        var g = sim.World.NavGrid;
        Vector2 goal = MoveScenario.Center(g, goalCell);
        var hashes = new ulong[600];
        for (int t = 0; t < hashes.Length; t++)
        {
            if (t == 0) MoveScenario.MoveAll(sim, goal + new Vector2(0.5f, 0.5f));
            if (t == 200)
            {
                for (int i = 0; i < 100; i += 2)
                    sim.Enqueue(Command.Move(sim.World.Units.Owner[i], MoveScenario.Handle(sim, i), goal + new Vector2(-12f, 8f)));
            }
            sim.Tick();
            hashes[t] = sim.StateHash();
        }
        return hashes;
    }

    [Fact]
    public void Moves_SameSeed_IdenticalHashEveryTick_DifferentSeedDiffers()
    {
        ulong[] a = RunMoves(21), b = RunMoves(21);
        Assert.Equal(a, b);
        Assert.Equal(600, a.Distinct().Count()); // the tick number alone changes the hash, but this guards a frozen sim too
        ulong[] c = RunMoves(22);
        Assert.NotEqual(a[^1], c[^1]);
    }

    [Fact]
    public void Hash_CoversStuckTicks_AndSpawnResetsIt()
    {
        // M1-4d-1: the give-up counter decides when a unit stops, so it is sim state.
        Simulation sim = MoveScenario.Spawn(5, units: 2, maxCost: 10f, out _);
        UnitStore u = sim.World.Units;
        ulong h0 = sim.StateHash();
        u.StuckTicks[1] = 1;
        Assert.NotEqual(h0, sim.StateHash());
        u.StuckTicks[1] = 0;
        Assert.Equal(h0, sim.StateHash());
        float best = u.BestRemaining[1];
        u.BestRemaining[1] = 12.5f;
        Assert.NotEqual(h0, sim.StateHash());
        u.BestRemaining[1] = best;
        Assert.Equal(h0, sim.StateHash());

        // A freed slot's give-up state does not leak into the next unit spawned there.
        u.StuckTicks[1] = 7;
        u.BestRemaining[1] = 3f;
        u.Free(MoveScenario.Handle(sim, 1));
        Assert.True(u.TryAlloc(out EntityHandle again));
        Assert.Equal(1, again.Index);
        Assert.Equal(0, u.StuckTicks[1]);
        Assert.Equal(float.PositiveInfinity, u.BestRemaining[1]);
    }

    [Fact]
    public void Hash_CoversStateGoalGoalCellAndCommandUnit()
    {
        Simulation sim = MoveScenario.Spawn(5, units: 2, maxCost: 10f, out int goalCell);
        UnitStore u = sim.World.Units;
        ulong h0 = sim.StateHash();

        u.State[1] = UnitState.Moving;
        Assert.NotEqual(h0, sim.StateHash());
        u.State[1] = UnitState.Idle;
        Assert.Equal(h0, sim.StateHash());

        u.Goal[1] = new Vector2(0f, float.Epsilon);
        Assert.NotEqual(h0, sim.StateHash());
        u.Goal[1] = default;
        Assert.Equal(h0, sim.StateHash());

        u.GoalCell[1] = goalCell;
        Assert.NotEqual(h0, sim.StateHash());
        u.GoalCell[1] = -1;
        Assert.Equal(h0, sim.StateHash());

        // Speed and Radius derive from TypeId, so they are deliberately not hashed.
        u.Speed[1] += 1f;
        u.Radius[1] += 1f;
        Assert.Equal(h0, sim.StateHash());

        // Two pending Moves that differ only in the unit's handle (index or generation) hash differently.
        static Simulation Pending(EntityHandle h)
        {
            Simulation s = MoveScenario.Spawn(5, units: 2, maxCost: 10f, out _);
            s.Enqueue(Command.Move(0, h, Vector2.One));
            return s;
        }
        ulong p0 = Pending(new EntityHandle(0, 1)).StateHash();
        Assert.NotEqual(p0, Pending(new EntityHandle(1, 1)).StateHash());
        Assert.NotEqual(p0, Pending(new EntityHandle(0, 2)).StateHash());
        Assert.Equal(p0, Pending(new EntityHandle(0, 1)).StateHash());
    }

    // ---------- M1-4c: the flow-field cache and order ticks are sim state (BUG-0021) ----------

    [Fact]
    public void Hash_CoversOrderTick()
    {
        Simulation sim = MoveScenario.Spawn(5, units: 2, maxCost: 10f, out _);
        ulong h0 = sim.StateHash();
        sim.World.Units.OrderTick[1] = 7;
        Assert.NotEqual(h0, sim.StateHash());
        sim.World.Units.OrderTick[1] = 0;
        Assert.Equal(h0, sim.StateHash());
    }

    [Fact]
    public void Hash_CoversFlowFieldCache_AGetChangesIt_TheSameGetsMakeItEqualAgain()
    {
        var a = new Simulation(Config(9));
        var b = new Simulation(Config(9));
        var g = a.World.NavGrid;
        List<int> open = FlowFieldOracle.PassableCells(g);
        int c1 = open[open.Count / 3], c2 = open[2 * open.Count / 3];
        Assert.Equal(a.StateHash(), b.StateHash());

        a.World.FlowFields.Get(c1);
        Assert.NotEqual(a.StateHash(), b.StateHash()); // before any tick
        b.World.FlowFields.Get(c1);
        Assert.Equal(a.StateHash(), b.StateHash());

        a.World.FlowFields.Get(c2);
        b.World.FlowFields.Get(c2);
        Assert.Equal(a.StateHash(), b.StateHash());

        // A hit builds nothing; it only moves the LRU stamp, and that alone changes the hash.
        int builds = a.World.FlowFields.BuildCount;
        a.World.FlowFields.Get(c1);
        Assert.Equal(builds, a.World.FlowFields.BuildCount);
        Assert.NotEqual(a.StateHash(), b.StateHash());
        b.World.FlowFields.Get(c1);
        Assert.Equal(a.StateHash(), b.StateHash());
        a.Tick();
        b.Tick();
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    /// <summary>M3-2b: two grids with the same cells and <c>Version</c> but a different <c>BlockVersion</c> hash differently.</summary>
    [Fact]
    public void Hash_CoversTheGridBlockVersionAlone()
    {
        var a = new Simulation(Config(9));
        var b = new Simulation(Config(9));
        NavGrid g = a.World.NavGrid;
        int cell = FlowFieldOracle.PassableCells(g).First(c => g.CanTakeResource(c % g.Width, c / g.Width));
        g.SetResource(cell % g.Width, cell / g.Width, 1, 1); // a closing change ...
        g.ClearResource(cell % g.Width, cell / g.Width, 1, 1); // ... undone by an opening one: Version 2, BlockVersion 1
        b.World.NavGrid.BumpVersionForTests();
        b.World.NavGrid.BumpVersionForTests(); // Version 2, BlockVersion 2, same cells
        Assert.Equal(g.Version, b.World.NavGrid.Version);
        Assert.Equal(g.PassableCount, b.World.NavGrid.PassableCount);
        Assert.NotEqual(g.BlockVersion, b.World.NavGrid.BlockVersion);
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    /// <summary>M3-2b: a cached field's <c>BlockVersion</c> tag is hashed (it decides whether units may follow the field).</summary>
    [Fact]
    public void Hash_CoversAFieldsBlockVersionTag()
    {
        var a = new Simulation(Config(9));
        var b = new Simulation(Config(9));
        int c = FlowFieldOracle.PassableCells(a.World.NavGrid)[100];
        FlowField fa = a.World.FlowFields.Get(c);
        b.World.FlowFields.Get(c);
        Assert.Equal(a.StateHash(), b.StateHash());
        typeof(FlowField).GetField("<BlockVersion>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fa, fa.BlockVersion + 1);
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    /// <summary>M3-2b: the movement pass's last seen <c>BlockVersion</c> (on <c>World</c>) is hashed: it decides when progress marks reset.</summary>
    [Fact]
    public void Hash_CoversTheSeenBlockVersion()
    {
        var a = new Simulation(Config(9));
        var b = new Simulation(Config(9));
        Assert.Equal(a.World.NavGrid.BlockVersion, a.World.SeenBlockVersion);
        a.World.SeenBlockVersion--;
        Assert.NotEqual(a.StateHash(), b.StateHash());
        // The next movement pass catches it up, and the hashes meet again.
        a.Tick();
        b.Tick();
        Assert.Equal(a.World.NavGrid.BlockVersion, a.World.SeenBlockVersion);
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Hash_SameCachedFieldsAndClock_DifferentLruOrder_Differs()
    {
        var a = new Simulation(Config(9));
        var b = new Simulation(Config(9));
        List<int> open = FlowFieldOracle.PassableCells(a.World.NavGrid);
        int c1 = open[100], c2 = open[200];
        // Same slots, same clock (4 uses), same count; only which field was used last differs.
        a.World.FlowFields.Get(c1); a.World.FlowFields.Get(c2); a.World.FlowFields.Get(c1); a.World.FlowFields.Get(c2);
        b.World.FlowFields.Get(c1); b.World.FlowFields.Get(c2); b.World.FlowFields.Get(c2); b.World.FlowFields.Get(c1);
        Assert.Equal(a.World.FlowFields.BuildCount, b.World.FlowFields.BuildCount);
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    /// <summary>M1-7: a pending command's flags are hashed: the same order queued and not differs while pending; once applied the state differs too. An unknown bit never gets that far (BUG-0054: Enqueue refuses it).</summary>
    [Fact]
    public void Hash_CoversPendingFlags()
    {
        Simulation Make()
        {
            var sim = new Simulation(Config(5));
            sim.Enqueue(Command.SpawnUnit(0, typeId: 0, new Vector2(20f, 20f)));
            sim.Tick();
            sim.Tick();
            return sim;
        }
        Simulation a = Make(), b = Make(), c = Make();
        Assert.Equal(a.StateHash(), b.StateHash());
        var unit = new EntityHandle(0, a.World.Units.Generation[0]);
        a.Enqueue(Command.Stop(0, unit));
        b.Enqueue(Command.Stop(0, unit, queued: true));
        ulong before = c.StateHash();
        Assert.Throws<ArgumentException>(() => c.Enqueue(Command.Stop(0, unit) with { Flags = 1 << 20 }));
        Assert.NotEqual(a.StateHash(), b.StateHash());
        Assert.Equal(before, c.StateHash());
    }

    /// <summary>M1-7: Hold, the queue count, and every queue entry of a live unit (also past the count) are hashed; a unit with no orders hashes as before the queue existed (the golden's checkpoints did not move).</summary>
    [Fact]
    public void Hash_CoversHoldAndEveryQueueEntry()
    {
        var sim = new Simulation(Config(6));
        sim.Enqueue(Command.SpawnUnit(0, typeId: 0, new Vector2(20f, 20f)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        ulong h0 = sim.StateHash();
        var seen = new HashSet<ulong> { h0 };
        u.Hold[0] = true;
        Assert.True(seen.Add(sim.StateHash()));
        u.Hold[0] = false;
        u.QueueCount[0] = 3;
        Assert.True(seen.Add(sim.StateHash()));
        u.QueueCount[0] = 0;
        for (int k = 0; k < Orders.OrderConstants.QueueCapacity; k++)
        {
            u.QueueKind[k] = CommandKind.Move;
            Assert.True(seen.Add(sim.StateHash()), $"entry {k} kind");
            u.QueuePosition[k] = new Vector2(3f, 0f);
            Assert.True(seen.Add(sim.StateHash()), $"entry {k} position");
            u.QueueKind[k] = CommandKind.Noop;
            Assert.True(seen.Add(sim.StateHash()), $"entry {k} position alone");
            u.QueuePosition[k] = Vector2.Zero;
            Assert.Equal(h0, sim.StateHash());
        }
        // Same entries in different slots differ.
        u.QueueKind[0] = CommandKind.Stop;
        ulong first = sim.StateHash();
        u.QueueKind[0] = CommandKind.Noop;
        u.QueueKind[1] = CommandKind.Stop;
        Assert.NotEqual(first, sim.StateHash());
    }

    // ---------- M3-1: resource nodes and the nav grid version ----------

    private static Simulation Trees(int capacity = ResourceStore.DefaultCapacity, params (int X, int Y)[] cells)
    {
        Simulation sim = ResourceMaps.NewSim(ResourceMaps.Flat(16, 16), resourceCapacity: capacity);
        foreach ((int x, int y) in cells) ResourceMaps.Spawn(sim.World, ResourceMaps.Tree, x, y, ResourceMaps.TreeWood);
        return sim;
    }

    [Fact]
    public void Hash_CoversOneNodesRemainingAmount()
    {
        Simulation a = Trees(cells: new[] { (3, 3), (5, 5) }), b = Trees(cells: new[] { (3, 3), (5, 5) });
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(1, a.World.Resources.Take(a.World.Resources.HandleOf(1), 1));
        Assert.Equal(a.World.NavGrid.Version, b.World.NavGrid.Version);
        Assert.NotEqual(a.StateHash(), b.StateHash());
        Assert.Equal(1, b.World.Resources.Take(b.World.Resources.HandleOf(1), 1));
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Hash_CoversAliveVsFreed_WithTheGridVersionEqual()
    {
        Simulation alive = Trees(cells: new[] { (3, 3) }), freed = Trees(cells: new[] { (3, 3) });
        Assert.Equal(ResourceMaps.TreeWood, freed.World.Resources.Take(freed.World.Resources.HandleOf(0), int.MaxValue));
        alive.World.NavGrid.BumpVersionForTests();
        Assert.Equal(alive.World.NavGrid.Version, freed.World.NavGrid.Version);
        Assert.NotEqual(alive.StateHash(), freed.StateHash());
    }

    [Fact]
    public void Hash_CoversTheNavGridVersionAlone()
    {
        Simulation a = Trees(cells: new[] { (3, 3) }), b = Trees(cells: new[] { (3, 3) });
        Assert.Equal(a.StateHash(), b.StateHash());
        a.World.NavGrid.BumpVersionForTests();
        Assert.NotEqual(a.StateHash(), b.StateHash());
        // Generated maps too: the version is hashed with no nodes at all.
        var c = new Simulation(Config(9));
        ulong h = c.StateHash();
        c.World.NavGrid.BumpVersionForTests();
        Assert.NotEqual(h, c.StateHash());
    }

    [Fact]
    public void Hash_CoversNodeCellTypeFreeListOrderAndCapacity()
    {
        ulong baseHash = Trees(cells: new[] { (3, 3) }).StateHash();
        Assert.NotEqual(baseHash, Trees(cells: new[] { (3, 4) }).StateHash());
        Simulation mine = ResourceMaps.NewSim(ResourceMaps.Flat(16, 16));
        ResourceMaps.Spawn(mine.World, ResourceMaps.Mine, 3, 3, ResourceMaps.TreeWood); // same anchor and amount, other type
        Assert.NotEqual(baseHash, mine.StateHash());
        Assert.NotEqual(baseHash, Trees(capacity: ResourceStore.DefaultCapacity - 1, cells: new[] { (3, 3) }).StateHash());

        // Same nodes, generations and version; only the free list's order differs.
        Simulation a = Trees(cells: new[] { (3, 3), (5, 5), (7, 7) }), b = Trees(cells: new[] { (3, 3), (5, 5), (7, 7) });
        a.World.Resources.Take(a.World.Resources.HandleOf(0), int.MaxValue);
        a.World.Resources.Take(a.World.Resources.HandleOf(1), int.MaxValue);
        b.World.Resources.Take(b.World.Resources.HandleOf(1), int.MaxValue);
        b.World.Resources.Take(b.World.Resources.HandleOf(0), int.MaxValue);
        Assert.Equal(a.World.NavGrid.Version, b.World.NavGrid.Version);
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Hash_SkipsNeverUsedSlots_ButAnEmptyStoreStillDiffersFromAUsedOne()
    {
        // High-water mark: a spawned-then-freed slot differs from a never-used one (generation 2 vs 1).
        Simulation used = Trees(cells: new[] { (3, 3) }), fresh = Trees();
        used.World.Resources.Take(used.World.Resources.HandleOf(0), int.MaxValue);
        fresh.World.NavGrid.BumpVersionForTests();
        fresh.World.NavGrid.BumpVersionForTests();
        Assert.Equal(0, used.World.Resources.Count);
        Assert.NotEqual(used.StateHash(), fresh.StateHash());
    }

    // ---------- M3-2: gather loops, cargo, buildings, player totals ----------

    /// <summary>A one-player sim with a Keep and one worker (slot 0), hashed equal to its twin.</summary>
    private static Simulation Economy()
    {
        Simulation sim = GatherMaps.NewSim(ResourceMaps.Flat(16, 16));
        GatherMaps.Building(sim, 2, 2);
        GatherMaps.Unit(sim, GatherMaps.At(sim, 9, 9));
        return sim;
    }

    public static IEnumerable<object[]> EconomyFields() => new[]
    {
        new object[] { "GatherNode.Index", (Action<UnitStore>)(u => u.GatherNode[0] = new EntityHandle(3, 0)) },
        new object[] { "GatherNode.Generation", (Action<UnitStore>)(u => u.GatherNode[0] = new EntityHandle(0, 2)) },
        new object[] { "GatherSite", (Action<UnitStore>)(u => u.GatherSite[0] = new Vector2(0f, 1f)) },
        new object[] { "GatherProgress", (Action<UnitStore>)(u => u.GatherProgress[0] = 0.5f) },
        new object[] { "Cargo", (Action<UnitStore>)(u => u.Cargo[0] = 1) },
        new object[] { "CargoKind", (Action<UnitStore>)(u => u.CargoKind[0] = Data.ResourceKind.Wood) },
    };

    [Theory]
    [MemberData(nameof(EconomyFields))]
    public void Hash_CoversEveryGatherField(string field, Action<UnitStore> change)
    {
        Simulation a = Economy(), b = Economy();
        Assert.Equal(a.StateHash(), b.StateHash());
        change(a.World.Units);
        Assert.True(a.StateHash() != b.StateHash(), field);
    }

    [Fact]
    public void Hash_CoversPlayerTotals()
    {
        Simulation a = Economy(), b = Economy(), c = Economy();
        a.World.AddToTotal(0, Data.ResourceKind.Gold, 1);
        b.World.AddToTotal(0, Data.ResourceKind.Wood, 1);
        Assert.NotEqual(c.StateHash(), a.StateHash());
        Assert.NotEqual(c.StateHash(), b.StateHash());
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Hash_CoversTheBuildingStore_OwnerCellTypeGenerationAndCapacity()
    {
        Simulation baseSim = Economy();
        ulong h = baseSim.StateHash();
        Simulation other = GatherMaps.NewSim(ResourceMaps.Flat(16, 16));
        GatherMaps.Building(other, 3, 2); // another cell
        GatherMaps.Unit(other, GatherMaps.At(other, 9, 9));
        Assert.NotEqual(h, other.StateHash());

        // Freed and re-placed on the same cell: generation 2; the version is evened out with the test seam.
        Simulation again = Economy();
        Assert.True(again.World.Buildings.Free(again.World.Buildings.HandleOf(0)));
        Assert.True(again.World.Buildings.Spawn(0, GatherMaps.Keep, 2 * 16 + 2, out _));
        baseSim.World.NavGrid.BumpVersionForTests();
        baseSim.World.NavGrid.BumpVersionForTests();
        Assert.Equal(baseSim.World.NavGrid.Version, again.World.NavGrid.Version);
        Assert.NotEqual(baseSim.StateHash(), again.StateHash());

        var small = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 32, CommandCapacity: 144) with { BuildingCapacity = 255 }, ResourceMaps.Flat(16, 16));
        var big = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 32, CommandCapacity: 144), ResourceMaps.Flat(16, 16));
        Assert.NotEqual(small.StateHash(), big.StateHash());
    }

    // ---------- M3-3: construction sites, builders, repair ----------

    /// <summary>A one-player sim with a House site (slot 0) and its builder (slot 0), hashed equal to its twin.</summary>
    private static Simulation Site()
    {
        Simulation sim = GatherMaps.NewSim(ResourceMaps.Flat(16, 16));
        EntityHandle w = GatherMaps.Unit(sim, GatherMaps.At(sim, 2, 2));
        sim.Enqueue(Command.Build(0, w, BuildMaps.House, GatherMaps.At(sim, 8, 8)));
        sim.Tick();
        sim.Tick();
        return sim;
    }

    public static IEnumerable<object[]> SiteFields() => new[]
    {
        new object[] { "Work", (Action<World>)(w => w.Buildings.SetWork(0, 1)) }, // hp stays 1
        new object[] { "Hp", (Action<World>)(w => w.Buildings.SetHp(0, 2)) },
        new object[] { "UnderConstruction", (Action<World>)(w => ((bool[])typeof(BuildingStore).GetField("_underConstruction", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w.Buildings)!)[0] = false) },
        new object[] { "RepairProgress", (Action<World>)(w => w.Buildings.RepairProgress(0) = 1) },
        new object[] { "RepairGold", (Action<World>)(w => w.Buildings.RepairGold(0) = 1) },
        new object[] { "RepairWood", (Action<World>)(w => w.Buildings.RepairWood(0) = 1) },
        new object[] { "BuildTarget.Index", (Action<World>)(w => w.Units.BuildTarget[0] = new EntityHandle(1, w.Units.BuildTarget[0].Generation)) },
        new object[] { "BuildTarget.Generation", (Action<World>)(w => w.Units.BuildTarget[0] = new EntityHandle(0, 9)) },
        new object[] { "QueueTypeId", (Action<World>)(w =>
        {
            w.Units.QueueKind[0] = CommandKind.Build;
            w.Units.QueueTypeId[0] = 1;
        }) },
    };

    [Theory]
    [MemberData(nameof(SiteFields))]
    public void Hash_CoversEveryConstructionField(string field, Action<World> change)
    {
        Simulation a = Site(), b = Site();
        Assert.Equal(a.StateHash(), b.StateHash());
        if (field == "QueueTypeId") b.World.Units.QueueKind[0] = CommandKind.Build; // only the type id differs
        if (field == "QueueTypeId") b.World.Units.QueueCount[0] = a.World.Units.QueueCount[0] = 1;
        change(a.World);
        Assert.True(a.StateHash() != b.StateHash(), field);
    }

    // ---------- M3-4: production queues, progress (the population reservation), rally points ----------

    /// <summary>A one-player sim with a Keep (slot 0) training two workers (the head started) with a rally point, hashed equal to its twin.</summary>
    private static Simulation Production()
    {
        Simulation sim = GatherMaps.NewSim(ResourceMaps.Flat(16, 16));
        GatherMaps.Building(sim, 2, 2);
        sim.Enqueue(Command.Train(0, GatherMaps.At(sim, 3, 3), GatherMaps.Laborer));
        sim.Enqueue(Command.Train(0, GatherMaps.At(sim, 3, 3), GatherMaps.Laborer));
        sim.Enqueue(Command.SetRally(0, BuildMaps.Cell(sim, 2, 2), GatherMaps.At(sim, 10, 10)));
        GatherMaps.Run(sim, 5);
        Assert.Equal((2, 4, true), (sim.World.Buildings.QueueCount[0], sim.World.Buildings.Progress[0], sim.World.Buildings.HasRally[0]));
        return sim;
    }

    public static IEnumerable<object[]> ProductionFields() => new[]
    {
        new object[] { "QueueCount", (Action<World>)(w => w.Buildings.QueueCountOf(0) = 3) },
        new object[] { "QueueTypeId[0]", (Action<World>)(w => w.Buildings.QueueEntry(0, 0) = 1) },
        new object[] { "QueueTypeId[1]", (Action<World>)(w => w.Buildings.QueueEntry(0, 1) = 1) },
        new object[] { "QueueTypeId[2] (past the count)", (Action<World>)(w => w.Buildings.QueueEntry(0, 2) = 1) },
        new object[] { "QueueTypeId[3]", (Action<World>)(w => w.Buildings.QueueEntry(0, 3) = 1) },
        new object[] { "QueueTypeId[4]", (Action<World>)(w => w.Buildings.QueueEntry(0, 4) = 1) },
        new object[] { "Progress (and so the head's reservation)", (Action<World>)(w => w.Buildings.ProgressOf(0) = 5) },
        new object[] { "Progress to 0 (no reservation)", (Action<World>)(w => w.Buildings.ProgressOf(0) = 0) },
        new object[] { "HasRally", (Action<World>)(w => w.Buildings.SetRally(0, false, w.Buildings.RallyPosition[0])) },
        new object[] { "RallyPosition.X", (Action<World>)(w => w.Buildings.SetRally(0, true, w.Buildings.RallyPosition[0] + new Vector2(0.25f, 0f))) },
        new object[] { "RallyPosition.Y", (Action<World>)(w => w.Buildings.SetRally(0, true, w.Buildings.RallyPosition[0] + new Vector2(0f, 0.25f))) },
    };

    [Theory]
    [MemberData(nameof(ProductionFields))]
    public void Hash_CoversEveryProductionField(string field, Action<World> change)
    {
        Simulation a = Production(), b = Production();
        Assert.Equal(a.StateHash(), b.StateHash());
        change(a.World);
        Assert.True(a.StateHash() != b.StateHash(), field);
    }

    /// <summary>
    /// Reflection audit: every per-slot array of <see cref="BuildingStore"/> is hashed. Each element of live slot 0 (all
    /// five queue entries) is changed in turn; the free list is covered by the high-water mark and its hashed tail.
    /// </summary>
    [Fact]
    public void EveryBuildingStoreArray_IsHashed()
    {
        Simulation sim = Production();
        BuildingStore b = sim.World.Buildings;
        ulong h0 = sim.StateHash();
        var unhashed = new List<string>();
        int audited = 0;
        foreach (FieldInfo f in typeof(BuildingStore).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (!f.FieldType.IsArray || f.Name == "_freeList") continue;
            var arr = (Array)f.GetValue(b)!;
            int per = arr.Length == b.Capacity ? 1 : arr.Length == b.Capacity * Rts.Sim.Economy.EconomyConstants.ProductionQueueCapacity ? Rts.Sim.Economy.EconomyConstants.ProductionQueueCapacity : 0;
            Assert.True(per > 0, $"{f.Name}: length {arr.Length} is neither per slot nor per queue entry; extend the audit");
            for (int e = 0; e < per; e++)
            {
                object? old = arr.GetValue(e);
                object changed = old switch
                {
                    int x => x + 1,
                    long x => x + 1,
                    bool x => !x,
                    Vector2 x => x + new Vector2(0.25f, 0f),
                    _ => throw new InvalidOperationException($"{f.Name}: element type {f.FieldType} not covered by the audit"),
                };
                arr.SetValue(changed, e);
                if (sim.StateHash() == h0) unhashed.Add(per == 1 ? f.Name : $"{f.Name}[{e}]");
                arr.SetValue(old, e);
                Assert.Equal(h0, sim.StateHash());
            }
            audited++;
        }
        Assert.True(audited >= 16, $"only {audited} arrays audited");
        Assert.True(unhashed.Count == 0, "not in StateHash: " + string.Join(", ", unhashed));
    }

    // ---------- M3-5: researched techs, research items in the queue ----------

    /// <summary>A two-player sim with a Keep (slot 0) researching Age II (started, progress 4) with a Laborer queued behind it, hashed equal to its twin.</summary>
    private static Simulation Research()
    {
        Simulation sim = GatherMaps.NewSim(ResourceMaps.Flat(16, 16), players: 2);
        GatherMaps.Building(sim, 2, 2);
        // M3-6: Age II needs two of the four hall slots finished.
        GatherMaps.Building(sim, 8, 2, type: ProductionMaps.Barracks);
        GatherMaps.Building(sim, 12, 2, type: ResearchMaps.Armory);
        BuildMaps.SetTotals(sim, 0, 1000, 1000);
        sim.Enqueue(Command.Research(0, GatherMaps.At(sim, 3, 3), ResearchMaps.AgeII));
        sim.Enqueue(Command.Train(0, GatherMaps.At(sim, 3, 3), GatherMaps.Laborer));
        GatherMaps.Run(sim, 5);
        Assert.Equal((2, 4, true, false), (sim.World.Buildings.QueueCount[0], sim.World.Buildings.Progress[0],
            sim.World.Buildings.QueueIsTechAt(0, 0), sim.World.Buildings.QueueIsTechAt(0, 1)));
        return sim;
    }

    public static IEnumerable<object[]> ResearchFields() => new[]
    {
        new object[] { "QueueIsTech[0] (the head)", (Action<World>)(w => w.Buildings.QueueIsTechEntry(0, 0) = false) },
        new object[] { "QueueIsTech[1]", (Action<World>)(w => w.Buildings.QueueIsTechEntry(0, 1) = true) },
        new object[] { "QueueIsTech[2] (past the count)", (Action<World>)(w => w.Buildings.QueueIsTechEntry(0, 2) = true) },
        new object[] { "QueueIsTech[4]", (Action<World>)(w => w.Buildings.QueueIsTechEntry(0, 4) = true) },
        new object[] { "the head's tech id", (Action<World>)(w => w.Buildings.QueueEntry(0, 0) = ResearchMaps.Melee1) },
        new object[] { "research progress", (Action<World>)(w => w.Buildings.ProgressOf(0) = 900) },
        new object[] { "research progress to 0", (Action<World>)(w => w.Buildings.ProgressOf(0) = 0) },
    };

    [Theory]
    [MemberData(nameof(ResearchFields))]
    public void Hash_CoversEveryResearchQueueField(string field, Action<World> change)
    {
        Simulation a = Research(), b = Research();
        Assert.Equal(a.StateHash(), b.StateHash());
        change(a.World);
        Assert.True(a.StateHash() != b.StateHash(), field);
    }

    /// <summary>Criterion 7: every tech flag of every player is hashed, each one differently, and clearing it again restores the hash.</summary>
    [Fact]
    public void Hash_CoversEveryTechFlagOfEveryPlayer()
    {
        Simulation baseSim = Research();
        ulong h0 = baseSim.StateHash();
        var seen = new HashSet<ulong> { h0 };
        int techs = TestSim.Data.Techs.Length;
        Assert.Equal(9, techs);
        for (int p = 0; p < 2; p++)
        {
            for (int t = 0; t < techs; t++)
            {
                Simulation a = Research();
                a.World.Techs.Set(p, t, true);
                Assert.True(a.World.HasTech(p, t));
                Assert.True(seen.Add(a.StateHash()), $"player {p} tech {TestSim.Data.Techs[t].Key}: not in the hash, or the same as another flag");
                a.World.Techs.Set(p, t, false);
                Assert.Equal(h0, a.StateHash());
            }
        }
        // Two flags differ from either alone.
        Simulation both = Research();
        both.World.Techs.Set(0, 0, true);
        both.World.Techs.Set(0, 1, true);
        Assert.True(seen.Add(both.StateHash()));
    }

    /// <summary>
    /// The tech words are added only for a player with a tech: the high half of its gold word says so, and a world with
    /// none hashes exactly as before M3-5 (ReplayGoldenTests: the checkpoints did not move).
    /// </summary>
    [Fact]
    public void Hash_PlayerWithATech_FlagsItInTheHighHalfOfItsGoldWord()
    {
        Simulation a = Research(), b = Research();
        b.World.Techs.Set(1, 0, true);
        Assert.NotEqual(a.StateHash(), b.StateHash());
        Assert.False(a.World.Techs.Any(0) || a.World.Techs.Any(1));
        Assert.True(b.World.Techs.Any(1) && !b.World.Techs.Any(0));
        Assert.Equal(1, b.World.Techs.WordsPerPlayer);
    }

    // ---------- M3-6: the finished-building counts the requirement gates read ----------

    /// <summary>
    /// The ledger's per-player finished counts (per building type and slot) are derived from the building store, which is
    /// hashed, so they are not hashed themselves: two worlds differing only in them hash the same. RequirementFuzzTests
    /// recounts them against the store every tick.
    /// </summary>
    [Fact]
    public void FinishedBuildingCounts_AreDerived_AndNotHashed()
    {
        Simulation a = Research(), b = Research();
        int barracks = ProductionMaps.Barracks;
        Assert.Equal(1, a.World.Ledger.FinishedOfType(0, barracks));
        b.World.Ledger.AddFinished(0, barracks, Data.BuildingSlot.InfantryHall, 5);
        Assert.Equal(6, b.World.Ledger.FinishedOfType(0, barracks));
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    /// <summary>
    /// M3-H2: the plateau ids are derived from terrain (read-only after load, so nothing to change), and the per-tick
    /// spawn memos and the never-seal answer memo are scratch: changing them doesn't move the hash.
    /// </summary>
    [Fact]
    public void PlateauIds_AndTheSpawnAndSealMemos_AreDerived_AndNotHashed()
    {
        Simulation a = Research(), b = Research();
        World w = b.World;
        Assert.True(w.Plateaus.Count > 0);
        w.SpawnPlateauFull[0] = true;
        w.SpawnCacheCleared[0] = true;
        w.CanPlace(0, BuildMaps.House, 5 * w.NavGrid.Width + 5, out _); // may leave a seal answer kept
        w.Seal.ForgetForTests();
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    // ---------- M4-1: combat state ----------

    /// <summary>The combat fields of UnitStore (M4-1); each one, mutated on a live unit, must flip the hash.</summary>
    private static readonly string[] CombatFields = { "Hp", "Target", "TargetIsBuilding", "CooldownTicks", "WindupTicks", "LastAttacker", "AnchorPosition", "Mode", "ChaseBest", "ChaseStall", "Ignored", "IgnoredIsBuilding", "GiveUps" };

    [Fact]
    public void Hash_CoversEveryCombatField_OnALiveUnit_AndRestoringItRestoresTheHash()
    {
        Simulation sim = CombatScenes.Flat(units: 8);
        EntityHandle h = CombatScenes.Place(sim, 0, CombatScenes.HeavyInfantry, CombatScenes.At(sim, 10, 10));
        UnitStore u = sim.World.Units;
        ulong h0 = sim.StateHash();
        var unhashed = new List<string>();
        foreach (string name in CombatFields)
        {
            FieldInfo? f = typeof(UnitStore).GetField(name);
            Assert.True(f != null, $"UnitStore.{name} is missing");
            var arr = (Array)f!.GetValue(u)!;
            Assert.Equal(u.Capacity, arr.Length);
            object? old = arr.GetValue(h.Index);
            object changed = old switch
            {
                int x => x - 1,
                float x => x + 0.25f,
                bool x => !x,
                Vector2 x => x + new Vector2(0.25f, 0f),
                EntityHandle x => new EntityHandle(x.Index + 1, x.Generation + 1),
                Combat.CombatMode x => x == Combat.CombatMode.None ? Combat.CombatMode.Retaliate : Combat.CombatMode.None,
                _ => throw new InvalidOperationException($"{name}: element type {f.FieldType} not covered"),
            };
            arr.SetValue(changed, h.Index);
            if (sim.StateHash() == h0) unhashed.Add(name);
            arr.SetValue(old, h.Index);
            Assert.Equal(h0, sim.StateHash());
        }
        Assert.True(unhashed.Count == 0, "not in StateHash: " + string.Join(", ", unhashed));

        // A target's generation alone, and a target being a building rather than a unit, also count.
        u.Target[h.Index] = new EntityHandle(3, 1);
        ulong unitTarget = sim.StateHash();
        u.Target[h.Index] = new EntityHandle(3, 2);
        Assert.NotEqual(unitTarget, sim.StateHash());
        u.Target[h.Index] = new EntityHandle(3, 1);
        u.TargetIsBuilding[h.Index] = true;
        Assert.NotEqual(unitTarget, sim.StateHash());
        u.Target[h.Index] = default;
        u.TargetIsBuilding[h.Index] = false;
        Assert.Equal(h0, sim.StateHash());
    }

    [Fact]
    public void Hash_CoversKillsAndLosses_PerPlayer()
    {
        Simulation sim = CombatScenes.Flat(units: 8);
        ulong h0 = sim.StateHash();
        var ledger = sim.World.Ledger;
        ledger.Kills[0]++;
        ulong k0 = sim.StateHash();
        Assert.NotEqual(h0, k0);
        ledger.Kills[0]--;
        ledger.Kills[1]++;
        Assert.NotEqual(h0, sim.StateHash());
        Assert.NotEqual(k0, sim.StateHash());
        ledger.Kills[1]--;
        ledger.Losses[0]++;
        Assert.NotEqual(h0, sim.StateHash());
        ledger.Losses[0]--;
        Assert.Equal(h0, sim.StateHash());
    }

    [Fact]
    public void Hash_ADeathBufferIsOutput_NotState()
    {
        Simulation sim = CombatScenes.Flat(units: 8);
        ulong h0 = sim.StateHash();
        sim.World.RecordDeath(new Combat.DeathEvent(new EntityHandle(1, 1), false, 0, 0, 1, Vector2.One));
        sim.World.Ledger.Kills[1]--;
        sim.World.Ledger.Losses[0]--;
        Assert.Equal(1, sim.World.Deaths.Length);
        Assert.Equal(h0, sim.StateHash());
    }

    [Fact]
    public void Hash_AnIdleUnitThatNeverFought_HashesTheSameWithCombatFieldsAtSpawnValues()
    {
        // Same unit placed on two sims; on one, the combat fields are written back to their spawn values by hand:
        // the flag bit stays clear, so nothing combat goes into the hash (the golden replay's guarantee).
        Simulation a = CombatScenes.Flat(units: 8), b = CombatScenes.Flat(units: 8);
        EntityHandle ha = CombatScenes.Place(a, 0, CombatScenes.HeavyInfantry, CombatScenes.At(a, 10, 10));
        EntityHandle hb = CombatScenes.Place(b, 0, CombatScenes.HeavyInfantry, CombatScenes.At(b, 10, 10));
        UnitStore u = b.World.Units;
        u.Hp[hb.Index] = TestSim.Data.Units[CombatScenes.HeavyInfantry].Hp;
        u.Target[hb.Index] = default;
        u.Mode[hb.Index] = Combat.CombatMode.None;
        u.AnchorPosition[hb.Index] = Vector2.Zero;
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(ha, hb);
    }
}
