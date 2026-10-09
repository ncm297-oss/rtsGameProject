using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

public class SimulationTests
{
    private static Simulation NewSim(int players = 2) =>
        new(TestSim.Config(Seed: TestSeeds.PreMix(42) /* pre-M1-6 map: open ground east of the center */, PlayerCount: players, UnitCapacity: 64, CommandCapacity: 64));

    [Fact]
    public void TickConstants_Are20Hz()
    {
        Assert.Equal(50, SimConstants.TickMs);
        Assert.Equal(20, SimConstants.TicksPerSecond);
    }

    [Fact]
    public void TickNumber_Counts0_1_2()
    {
        Simulation sim = NewSim();
        Assert.Equal(0, sim.TickNumber);
        sim.Tick();
        Assert.Equal(1, sim.TickNumber);
        sim.Tick();
        Assert.Equal(2, sim.TickNumber);
    }

    [Fact]
    public void Enqueue_StampsNextTick()
    {
        Simulation sim = NewSim();
        sim.Tick();
        sim.Tick(); // TickNumber == 2

        sim.Enqueue(Command.Noop(0));
        sim.Enqueue(Command.Noop(1));
        sim.Enqueue(Command.Noop(0));

        Assert.Equal(3, sim.PendingCommandCount);
        sim.Tick(); // tick 2: nothing due yet
        Assert.Equal(3, sim.PendingCommandCount);
        sim.Tick(); // tick 3: all applied
        Assert.Equal(0, sim.PendingCommandCount);
    }

    [Fact]
    public void CommandEnqueuedAtTickN_AppliesInTickNPlus1_NotN()
    {
        Simulation sim = NewSim();
        sim.Tick();
        int n = sim.TickNumber;

        sim.Enqueue(Command.SpawnUnit(0, typeId: 1, new Vector2(1f, 2f)));

        sim.Tick(); // runs tick n
        Assert.Equal(n + 1, sim.TickNumber);
        Assert.Equal(0, sim.World.Units.Count);

        sim.Tick(); // runs tick n + 1
        Assert.Equal(1, sim.World.Units.Count);
    }

    [Fact]
    public void InterleavedPlayers_Player0AppliesFirst_ThenBySequence()
    {
        Simulation sim = NewSim();
        sim.Enqueue(Command.SpawnUnit(1, typeId: 2, Vector2.Zero));
        sim.Enqueue(Command.SpawnUnit(0, typeId: 4, Vector2.Zero));
        sim.Enqueue(Command.SpawnUnit(1, typeId: 3, Vector2.Zero));
        sim.Enqueue(Command.SpawnUnit(0, typeId: 5, Vector2.Zero));

        sim.Tick();
        sim.Tick();

        var u = sim.World.Units;
        Assert.Equal(4, u.Count);
        Assert.Equal(new[] { 0, 0, 1, 1 }, u.Owner[..4]);
        Assert.Equal(new[] { 4, 5, 2, 3 }, u.TypeId[..4]);
    }

    [Fact]
    public void SpawnUnit_OrderDeterminesHandleIndices()
    {
        Simulation sim = NewSim();
        for (int i = 0; i < 5; i++)
            sim.Enqueue(Command.SpawnUnit(0, typeId: i, new Vector2(i, -i)));

        sim.Tick();
        sim.Tick();

        var u = sim.World.Units;
        for (int i = 0; i < 5; i++)
        {
            Assert.True(u.Alive[i]);
            Assert.Equal(i, u.TypeId[i]);
            Assert.Equal(new Vector2(i, -i), u.Position[i]);
            Assert.Equal(new Vector2(i, -i), u.PrevPosition[i]);
        }
    }

    [Fact]
    public void SpawnUnit_WhenStoreFull_IsDroppedNotThrown()
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 2, CommandCapacity: 8));
        for (int i = 0; i < 3; i++)
            sim.Enqueue(Command.SpawnUnit(0, typeId: i, Vector2.Zero));
        sim.Tick();
        sim.Tick();
        Assert.Equal(2, sim.World.Units.Count);
    }

    [Fact]
    public void Enqueue_UnknownPlayerOrFullQueue_Throws()
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 2, CommandCapacity: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Enqueue(Command.Noop(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Enqueue(Command.Noop(-1)));
        sim.Enqueue(Command.Noop(0));
        Assert.Throws<InvalidOperationException>(() => sim.Enqueue(Command.Noop(0)));
    }

    /// <summary>
    /// BUG-0054 / BUG-0056: an undefined kind, an unknown flag bit, or the queued flag on a kind that
    /// isn't a unit order is refused at the door like an unknown player: an exception, nothing queued,
    /// no sequence number used (the next command hashes as in a twin that never saw the bad one).
    /// </summary>
    [Theory]
    [InlineData(99, 0)]
    [InlineData(-1, 0)]
    [InlineData(18, 0)] // one past the last kind (UseAbility, M4-4a)
    [InlineData((int)CommandKind.SpawnBuilding, Command.QueuedFlag)]
    [InlineData((int)CommandKind.Cancel, Command.QueuedFlag)] // M3-3: not a unit order
    [InlineData((int)CommandKind.Train, Command.QueuedFlag)] // M3-4: the production kinds aren't unit orders either
    [InlineData((int)CommandKind.SetRally, Command.QueuedFlag)]
    [InlineData((int)CommandKind.Stop, 2)]
    [InlineData((int)CommandKind.Move, 1 << 20)]
    [InlineData((int)CommandKind.HoldPosition, -1)]
    [InlineData((int)CommandKind.SpawnUnit, Command.QueuedFlag)]
    [InlineData((int)CommandKind.Noop, Command.QueuedFlag)]
    public void Enqueue_MalformedKindOrFlags_ThrowsAndChangesNothing(int kind, int flags)
    {
        Simulation Make()
        {
            var s = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 2, CommandCapacity: 4));
            s.Enqueue(Command.SpawnUnit(0, typeId: 0, new Vector2(20f, 20f)));
            s.Tick();
            s.Tick();
            return s;
        }
        Simulation sim = Make(), twin = Make();
        var bad = new Command { Kind = (CommandKind)kind, Player = 0, Flags = flags, Unit = new EntityHandle(0, sim.World.Units.Generation[0]) };
        ulong before = sim.StateHash();
        Assert.Throws<ArgumentException>(() => sim.Enqueue(bad));
        Assert.Equal(0, sim.PendingCommandCount);
        Assert.Equal(before, sim.StateHash());
        sim.Enqueue(Command.Noop(0));
        twin.Enqueue(Command.Noop(0));
        Assert.Equal(twin.StateHash(), sim.StateHash()); // same sequence number: the bad one used none
    }

    // ---------- M1-4b: data-driven spawns, Move validation ----------

    [Fact]
    public void SpawnUnit_EveryShippedType_GetsSpeedAndRadiusFromJson()
    {
        GameData data = TestSim.Data;
        int seen = 0;
        foreach (string file in Directory.GetFiles(Path.Combine(TestDataDir.Shipped, "factions"), "units.json", SearchOption.AllDirectories))
        {
            foreach (JsonNode? node in JsonNode.Parse(File.ReadAllText(file))!["units"]!.AsArray())
            {
                string key = (string)node!["id"]!;
                int type = data.FindUnit(key);
                Assert.True(type >= 0, key);
                var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 2, CommandCapacity: 2));
                sim.Enqueue(Command.SpawnUnit(0, type, new Vector2(10f, 10f)));
                sim.Tick();
                sim.Tick();
                UnitStore u = sim.World.Units;
                Assert.Equal(1, u.Count);
                Assert.Equal(data.Units[type].SpeedPerTick, u.Speed[0]);
                Assert.Equal(data.Units[type].Radius, u.Radius[0]);
                // Straight from the JSON too: speed is m/s in data, m/tick in the sim.
                Assert.Equal((float)((double)node["speed"]! / SimConstants.TicksPerSecond), u.Speed[0]);
                Assert.Equal((float)(double)node["radius"]!, u.Radius[0]);
                Assert.Equal(UnitState.Idle, u.State[0]);
                Assert.Equal(-1, u.GoalCell[0]);
                seen++;
            }
        }
        Assert.Equal(data.Units.Length, seen);
    }

    [Fact]
    public void SpawnUnit_UnknownTypeId_IsDropped()
    {
        Simulation sim = NewSim();
        foreach (int bad in new[] { -1, TestSim.UnitTypeCount, int.MaxValue, int.MinValue })
            sim.Enqueue(Command.SpawnUnit(0, bad, Vector2.One));
        sim.Enqueue(Command.SpawnUnit(0, TestSim.UnitTypeCount - 1, Vector2.One));
        sim.Tick();
        sim.Tick();
        Assert.Equal(1, sim.World.Units.Count);
        Assert.Equal(TestSim.UnitTypeCount - 1, sim.World.Units.TypeId[0]);
    }

    /// <summary>Two identical sims with one unit per player at a passable cell; returns its position.</summary>
    private static (Simulation A, Simulation B, Vector2 Spot) TwinSims()
    {
        Simulation a = NewSim(), b = NewSim();
        NavGrid g = a.World.NavGrid;
        Vector2 spot = MoveScenario.Center(g, MoveScenario.CentralCell(g));
        foreach (Simulation s in new[] { a, b })
        {
            s.Enqueue(Command.SpawnUnit(0, 0, spot));
            s.Enqueue(Command.SpawnUnit(1, 0, spot));
            s.Tick();
            s.Tick();
        }
        Assert.Equal(a.StateHash(), b.StateHash());
        return (a, b, spot);
    }

    public static TheoryData<string> BadMoves => new() { "stale", "foreign", "nan", "infinite", "outside", "negative" };

    [Theory]
    [MemberData(nameof(BadMoves))]
    public void Move_Invalid_IsDropped_StateHashMatchesANoop(string kind)
    {
        (Simulation a, Simulation b, Vector2 spot) = TwinSims();
        EntityHandle mine = MoveScenario.Handle(a, 0);
        Vector2 target = spot + new Vector2(6f, 0f);
        float mapMeters = a.World.NavGrid.Width * MapConstants.CellSize;
        Command bad = kind switch
        {
            "stale" => Command.Move(0, mine with { Generation = mine.Generation + 1 }, target),
            "foreign" => Command.Move(0, MoveScenario.Handle(a, 1), target),
            "nan" => Command.Move(0, mine, new Vector2(float.NaN, 5f)),
            "infinite" => Command.Move(0, mine, new Vector2(5f, float.PositiveInfinity)),
            "outside" => Command.Move(0, mine, new Vector2(mapMeters + 1f, 5f)),
            _ => Command.Move(0, mine, new Vector2(-0.5f, 5f)),
        };
        a.Enqueue(bad);
        b.Enqueue(Command.Noop(0));
        foreach (Simulation s in new[] { a, b })
        {
            s.Tick();
            s.Tick(); // applies on the second tick
        }
        Assert.Equal(b.StateHash(), a.StateHash());
        Assert.Equal(UnitState.Idle, a.World.Units.State[0]);
        Assert.Equal(UnitState.Idle, a.World.Units.State[1]);
    }

    [Fact]
    public void Move_Valid_SetsMovingAndGoal_AndChangesHash()
    {
        (Simulation a, Simulation b, Vector2 spot) = TwinSims();
        Vector2 target = spot + new Vector2(6.5f, 0.25f);
        NavGrid g = a.World.NavGrid;
        Assert.True(g.WorldToCell(target, out int tx, out int ty));
        Assert.True(g.IsPassable(tx, ty), "test assumes open ground east of the map center");
        a.Enqueue(Command.Move(0, MoveScenario.Handle(a, 0), target));
        b.Enqueue(Command.Noop(0));
        foreach (Simulation s in new[] { a, b })
        {
            s.Tick();
            s.Tick(); // applies on the second tick
        }
        UnitStore u = a.World.Units;
        Assert.Equal(UnitState.Moving, u.State[0]);
        Assert.Equal(target, u.Goal[0]);
        Assert.Equal(ty * g.Width + tx, u.GoalCell[0]);
        Assert.NotEqual(spot, u.Position[0]); // movement ran in the same tick
        Assert.Equal(UnitState.Idle, u.State[1]);
        Assert.NotEqual(b.StateHash(), a.StateHash());
    }
}
