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
    public void WorldStoreCapacity_ComesFromConfig()
    {
        var world = new World(new SimConfig(Seed: 1, PlayerCount: 2, UnitCapacity: 37, CommandCapacity: 8));
        Assert.Equal(37, world.Units.Capacity);
    }
}
