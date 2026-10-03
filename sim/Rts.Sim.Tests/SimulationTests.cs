using System.Numerics;
using Rts.Sim.Commands;

namespace Rts.Sim.Tests;

public class SimulationTests
{
    private static Simulation NewSim(int players = 2) =>
        new(new SimConfig(Seed: 42, PlayerCount: players, UnitCapacity: 64, CommandCapacity: 64));

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
        sim.Enqueue(Command.SpawnUnit(1, typeId: 10, Vector2.Zero));
        sim.Enqueue(Command.SpawnUnit(0, typeId: 20, Vector2.Zero));
        sim.Enqueue(Command.SpawnUnit(1, typeId: 11, Vector2.Zero));
        sim.Enqueue(Command.SpawnUnit(0, typeId: 21, Vector2.Zero));

        sim.Tick();
        sim.Tick();

        var u = sim.World.Units;
        Assert.Equal(4, u.Count);
        Assert.Equal(new[] { 0, 0, 1, 1 }, u.Owner[..4]);
        Assert.Equal(new[] { 20, 21, 10, 11 }, u.TypeId[..4]);
    }

    [Fact]
    public void SpawnUnit_OrderDeterminesHandleIndices()
    {
        Simulation sim = NewSim();
        for (int i = 0; i < 5; i++)
            sim.Enqueue(Command.SpawnUnit(0, typeId: 100 + i, new Vector2(i, -i)));

        sim.Tick();
        sim.Tick();

        var u = sim.World.Units;
        for (int i = 0; i < 5; i++)
        {
            Assert.True(u.Alive[i]);
            Assert.Equal(100 + i, u.TypeId[i]);
            Assert.Equal(new Vector2(i, -i), u.Position[i]);
            Assert.Equal(new Vector2(i, -i), u.PrevPosition[i]);
        }
    }

    [Fact]
    public void SpawnUnit_WhenStoreFull_IsDroppedNotThrown()
    {
        var sim = new Simulation(new SimConfig(Seed: 1, PlayerCount: 1, UnitCapacity: 2, CommandCapacity: 8));
        for (int i = 0; i < 3; i++)
            sim.Enqueue(Command.SpawnUnit(0, typeId: i, Vector2.Zero));
        sim.Tick();
        sim.Tick();
        Assert.Equal(2, sim.World.Units.Count);
    }

    [Fact]
    public void Enqueue_UnknownPlayerOrFullQueue_Throws()
    {
        var sim = new Simulation(new SimConfig(Seed: 1, PlayerCount: 2, UnitCapacity: 2, CommandCapacity: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Enqueue(Command.Noop(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Enqueue(Command.Noop(-1)));
        sim.Enqueue(Command.Noop(0));
        Assert.Throws<InvalidOperationException>(() => sim.Enqueue(Command.Noop(0)));
    }
}
