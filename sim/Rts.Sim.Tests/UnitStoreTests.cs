using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

public class UnitStoreTests
{
    [Fact]
    public void Alloc_ReturnsSequentialLiveHandles()
    {
        var store = new UnitStore(4);
        EntityHandle a = store.Alloc();
        EntityHandle b = store.Alloc();

        Assert.Equal(0, a.Index);
        Assert.Equal(1, b.Index);
        Assert.True(store.IsAlive(a));
        Assert.True(store.IsAlive(b));
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void DefaultHandle_IsNeverAlive()
    {
        var store = new UnitStore(4);
        store.Alloc();
        Assert.False(store.IsAlive(default));
    }

    [Fact]
    public void Free_BumpsGenerationAndKillsHandle()
    {
        var store = new UnitStore(4);
        EntityHandle a = store.Alloc();
        int gen = store.Generation[a.Index];

        store.Free(a);

        Assert.False(store.IsAlive(a));
        Assert.False(store.Alive[a.Index]);
        Assert.Equal(gen + 1, store.Generation[a.Index]);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void ReusedSlot_StaleHandleStaysDead()
    {
        var store = new UnitStore(4);
        EntityHandle stale = store.Alloc();
        store.Free(stale);

        EntityHandle fresh = store.Alloc();

        Assert.Equal(stale.Index, fresh.Index);
        Assert.NotEqual(stale.Generation, fresh.Generation);
        Assert.True(store.IsAlive(fresh));
        Assert.False(store.IsAlive(stale));
    }

    [Fact]
    public void Alloc_ResetsReusedSlotFields()
    {
        var store = new UnitStore(1);
        EntityHandle a = store.Alloc();
        store.Owner[a.Index] = 3;
        store.TypeId[a.Index] = 7;
        store.Position[a.Index] = new System.Numerics.Vector2(5f, 6f);
        store.Free(a);

        EntityHandle b = store.Alloc();

        Assert.Equal(0, store.Owner[b.Index]);
        Assert.Equal(0, store.TypeId[b.Index]);
        Assert.Equal(default, store.Position[b.Index]);
    }

    [Fact]
    public void Free_StaleHandleThrows()
    {
        var store = new UnitStore(2);
        EntityHandle a = store.Alloc();
        store.Free(a);
        Assert.Throws<ArgumentException>(() => store.Free(a));
    }

    [Fact]
    public void AllocAtCapacity_FailsExplicitly()
    {
        var store = new UnitStore(3);
        store.Alloc();
        store.Alloc();
        store.Alloc();

        Assert.False(store.TryAlloc(out _));
        Assert.Throws<InvalidOperationException>(() => store.Alloc());
        Assert.Equal(3, store.Capacity);
        Assert.Equal(3, store.Position.Length);
    }

    [Fact]
    public void ReusedSlot_StartsWithMovementFieldsReset()
    {
        var store = new UnitStore(1);
        EntityHandle a = store.Alloc();
        store.Speed[0] = 0.2f;
        store.Radius[0] = 0.5f;
        store.State[0] = UnitState.Moving;
        store.Goal[0] = new System.Numerics.Vector2(3f, 4f);
        store.GoalCell[0] = 77;
        store.Free(a);
        store.Alloc();
        Assert.Equal(0f, store.Speed[0]);
        Assert.Equal(0f, store.Radius[0]);
        Assert.Equal(UnitState.Idle, store.State[0]);
        Assert.Equal(default, store.Goal[0]);
        Assert.Equal(-1, store.GoalCell[0]);
    }

    [Fact]
    public void WorldStoreCapacity_ComesFromConfig()
    {
        var world = new World(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 37, CommandCapacity: 8));
        Assert.Equal(37, world.Units.Capacity);
    }

    [Fact]
    public void ReusedSlot_StartsWithGatherFieldsAndPrevFacingReset()
    {
        var store = new UnitStore(1);
        EntityHandle a = store.Alloc();
        store.GatherNode[0] = new EntityHandle(4, 2);
        store.GatherSite[0] = new System.Numerics.Vector2(3f, 4f);
        store.GatherProgress[0] = 0.5f;
        store.Cargo[0] = 7;
        store.CargoKind[0] = Rts.Sim.Data.ResourceKind.Wood;
        store.PrevFacing[0] = 1.5f;
        store.Free(a);
        Assert.Equal(default, store.GatherNode[0]); // Free resets too, so a dead slot hashes nothing stale
        Assert.Equal(0, store.Cargo[0]);
        store.Cargo[0] = 3;
        store.Alloc();
        Assert.Equal(default, store.GatherNode[0]);
        Assert.Equal(default, store.GatherSite[0]);
        Assert.Equal(0f, store.GatherProgress[0]);
        Assert.Equal(0, store.Cargo[0]);
        Assert.Equal(Rts.Sim.Data.ResourceKind.Gold, store.CargoKind[0]);
        Assert.Equal(0f, store.PrevFacing[0]);
    }

    [Fact]
    public void PrevFacing_IsTheFacingAtTheStartOfTheTick()
    {
        Simulation sim = GatherMaps.NewSim(ResourceMaps.Flat(16, 16));
        EntityHandle h = GatherMaps.Unit(sim, GatherMaps.At(sim, 4, 4), type: GatherMaps.Infantry);
        sim.Enqueue(Rts.Sim.Commands.Command.Move(0, h, GatherMaps.At(sim, 4, 12)));
        UnitStore u = sim.World.Units;
        bool turned = false;
        for (int t = 0; t < 40; t++)
        {
            float before = u.Facing[h.Index];
            sim.Tick();
            Assert.Equal(before, u.PrevFacing[h.Index]);
            turned |= u.Facing[h.Index] != before;
        }
        Assert.True(turned);
    }
}
