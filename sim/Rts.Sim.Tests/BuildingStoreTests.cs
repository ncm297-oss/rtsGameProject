using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-2 criterion 2: the building store, the dev SpawnBuilding command and buildings as walls.</summary>
public class BuildingStoreTests
{
    private static Simulation Sim(Heightmap? map = null, int buildings = BuildingStore.DefaultCapacity) =>
        new(TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 64) with { BuildingCapacity = buildings }, map ?? Flat(24, 24));

    [Fact]
    public void Slots_StaleHandles_Generation_AndCapacityRefusal()
    {
        Simulation sim = Sim(buildings: 2);
        BuildingStore b = sim.World.Buildings;
        Assert.Equal(2, b.Capacity);
        Assert.False(b.IsAlive(default));
        EntityHandle first = Building(sim, 2, 2);
        EntityHandle second = Building(sim, 10, 2, player: 1);
        Assert.Equal((0, 1), (first.Index, second.Index));
        Assert.Equal(1, b.Owner[second.Index]);
        Assert.Equal(TestSim.Data.Buildings[Keep].Hp, b.Hp[first.Index]);

        ulong full = sim.StateHash();
        int version = sim.World.NavGrid.Version;
        Assert.False(b.Spawn(0, Keep, 18 * 24 + 18, out EntityHandle refused)); // store full: refused, no throw
        Assert.Equal(default, refused);
        Assert.Equal(version, sim.World.NavGrid.Version);
        Assert.Equal(full, sim.StateHash());

        Assert.True(b.Free(first));
        Assert.False(b.IsAlive(first));
        Assert.False(b.Free(first)); // stale
        Assert.Equal(version + 1, sim.World.NavGrid.Version);
        Assert.True(b.Spawn(0, Keep, 18 * 24 + 18, out EntityHandle reused));
        Assert.Equal(first.Index, reused.Index); // LIFO
        Assert.Equal(first.Generation + 1, reused.Generation);
        Assert.False(b.IsAlive(first));
        Assert.True(b.IsAlive(reused));
    }

    [Fact]
    public void Success_BlocksExactlyTheFootprintWithBuildingAndBlocked_AndBumpsTheVersionOnce()
    {
        Simulation sim = Sim();
        NavGrid g = sim.World.NavGrid;
        int version = g.Version, passable = g.PassableCount;
        Building(sim, 5, 6);
        Assert.Equal(version + 1, g.Version);
        Assert.Equal(passable - 16, g.PassableCount);
        for (int y = 0; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                bool inside = x >= 5 && x < 9 && y >= 6 && y < 10;
                Assert.Equal(inside, (g.FlagsAt(x, y) & NavFlags.Building) != 0);
                if (inside)
                {
                    Assert.Equal(NavFlags.Building | NavFlags.Blocked, g.FlagsAt(x, y));
                    Assert.Equal(MapConstants.CostBlocked, g.CostAt(x, y));
                }
            }
        }
    }

    // 24 x 24: a level-1 plateau at x 14-19, y 4-9 (rim cells are cliffs) with a ramp at (16, 10); a tree at
    // (5, 18), a Keep at (6, 4-7 x 4-7 cells), a unit in cell (11, 19).
    private static Simulation RefusalMap()
    {
        var rows = new string[24];
        for (int r = 0; r < 24; r++)
        {
            var row = new char[24];
            for (int c = 0; c < 24; c++) row[c] = c >= 14 && c <= 19 && r >= 4 && r <= 9 ? '1' : '0';
            if (r == 10) row[16] = 'r';
            rows[r] = new string(row);
        }
        Simulation sim = Sim(FromRows(rows));
        Spawn(sim.World, Tree, 5, 18, TreeWood);
        Building(sim, 4, 4);
        Unit(sim, At(sim, 11, 19) + new Vector2(0.3f, 0.2f));
        return sim;
    }

    public static IEnumerable<object[]> Refusals() => new[]
    {
        new object[] { "unknown type", false, 2, 14 },
        new object[] { "border", true, 0, 14 },
        new object[] { "past the far edge", true, 21, 14 },
        new object[] { "cliff", true, 12, 4 },
        new object[] { "ramp", true, 15, 10 },
        new object[] { "two levels", true, 13, 5 },
        new object[] { "resource node", true, 4, 17 },
        new object[] { "another building", true, 6, 5 },
        new object[] { "a unit inside", true, 10, 18 },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void SpawnBuilding_Refused_LeavesGridAndHashUnchanged(string why, bool knownType, int x, int y)
    {
        Simulation sim = RefusalMap(), twin = RefusalMap();
        NavGrid g = sim.World.NavGrid;
        Assert.Equal(sim.StateHash(), twin.StateHash());
        int version = g.Version, count = sim.World.Buildings.Count;
        sim.Enqueue(Command.SpawnBuilding(0, knownType ? Keep : TestSim.Data.Buildings.Length, At(sim, x, y)));
        twin.Enqueue(Command.Noop(0));
        Run(sim, 2);
        Run(twin, 2);
        Assert.True(count == sim.World.Buildings.Count, why);
        Assert.Equal(version, g.Version);
        Assert.Equal(twin.StateHash(), sim.StateHash()); // both commands drained; nothing else differs
        for (int cy = 0; cy < g.Height; cy++)
            for (int cx = 0; cx < g.Width; cx++)
                Assert.Equal(twin.World.NavGrid.FlagsAt(cx, cy), g.FlagsAt(cx, cy));
        Assert.True(sim.World.Buildings.Fits(Keep, 5 * 24 + 15), "the plateau's inside fits");
    }

    [Fact]
    public void ARefusedSpawn_HashesLikeANoop()
    {
        Simulation a = Sim(), b = Sim();
        Building(a, 5, 5);
        Building(b, 5, 5);
        a.Enqueue(Command.SpawnBuilding(0, Keep, At(a, 6, 6))); // overlaps the first
        b.Enqueue(Command.SpawnBuilding(0, Keep, At(b, 6, 6)));
        Assert.Equal(a.StateHash(), b.StateHash());
        a.Tick();
        b.Tick();
        a.Tick();
        b.Tick();
        Assert.Equal(1, a.World.Buildings.Count);
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void QueuedFlagOnSpawnBuilding_IsMalformed()
    {
        Simulation sim = Sim();
        Command c = Command.SpawnBuilding(0, Keep, At(sim, 5, 5));
        Assert.True(c.IsWellFormed());
        c.Flags = Command.QueuedFlag;
        Assert.False(c.IsWellFormed());
        Assert.Throws<ArgumentException>(() => sim.Enqueue(c));
    }

    [Fact]
    public void AMoveThroughTheFootprint_RoutesRoundIt()
    {
        Simulation sim = Sim(Flat(24, 16));
        Building(sim, 10, 4); // cells 10-13 x 4-7
        EntityHandle u = Unit(sim, At(sim, 4, 6), type: Infantry);
        sim.Enqueue(Command.Move(0, u, At(sim, 19, 6)));
        NavGrid g = sim.World.NavGrid;
        UnitStore units = sim.World.Units;
        bool crossed = false;
        for (int t = 0; t < 600; t++)
        {
            sim.Tick();
            Assert.True(g.WorldToCell(units.Position[u.Index], out int x, out int y) && g.IsPassable(x, y), $"unit on a blocked cell at tick {t}");
            if (x >= 10 && x <= 13) crossed = true;
        }
        Assert.True(crossed);
        Assert.True(Vector2.Distance(units.Position[u.Index], At(sim, 19, 6)) < 1.5f, $"ended at {units.Position[u.Index]}");
    }
}
