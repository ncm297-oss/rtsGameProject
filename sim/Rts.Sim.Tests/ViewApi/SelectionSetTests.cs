using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>The view's selection: handles, Shift toggle/add, pruning of dead and recycled units.</summary>
public class SelectionSetTests
{
    [Fact]
    public void Add_KeepsOrder_IgnoresDuplicates()
    {
        var store = new UnitStore(8);
        EntityHandle a = store.Alloc(), b = store.Alloc(), c = store.Alloc();
        var s = new SelectionSet(store.Capacity);
        Assert.True(s.Add(c));
        Assert.True(s.Add(a));
        Assert.False(s.Add(c));
        Assert.True(s.Add(b));
        Assert.Equal(new[] { c, a, b }, s.Items.ToArray());
        Assert.True(s.Contains(a));
    }

    [Fact]
    public void Toggle_RemovesSelected_AddsUnselected_KeepsTheOthersOrder()
    {
        var store = new UnitStore(8);
        EntityHandle a = store.Alloc(), b = store.Alloc(), c = store.Alloc();
        var s = new SelectionSet(store.Capacity);
        s.Add(a);
        s.Add(b);
        s.Add(c);
        s.Toggle(a);
        Assert.Equal(new[] { b, c }, s.Items.ToArray());
        Assert.False(s.Contains(a));
        s.Toggle(a);
        Assert.Equal(new[] { b, c, a }, s.Items.ToArray());
        Assert.False(s.Remove(new EntityHandle(5, 1)));
    }

    [Fact]
    public void Clear_EmptiesIt_AndSlotsCanBeAddedAgain()
    {
        var store = new UnitStore(4);
        EntityHandle a = store.Alloc();
        var s = new SelectionSet(store.Capacity);
        s.Add(a);
        s.Clear();
        Assert.Equal(0, s.Count);
        Assert.False(s.Contains(a));
        Assert.True(s.Add(a));
    }

    [Fact]
    public void Prune_DropsDeadAndRecycledSlots_KeepsTheRestInOrder()
    {
        var store = new UnitStore(8);
        EntityHandle a = store.Alloc(), b = store.Alloc(), c = store.Alloc(), d = store.Alloc();
        var s = new SelectionSet(store.Capacity);
        s.Add(a);
        s.Add(b);
        s.Add(c);
        s.Add(d);
        store.Free(b);
        store.Free(d);
        EntityHandle d2 = store.Alloc(); // LIFO free list: d's slot again, new generation
        Assert.Equal(d.Index, d2.Index);

        Assert.Equal(2, s.Prune(store.Alive, store.Generation));
        Assert.Equal(new[] { a, c }, s.Items.ToArray());
        Assert.False(s.Contains(d2));
        Assert.Equal(0, s.Prune(store.Alive, store.Generation));
        Assert.True(s.Add(d2));
        Assert.True(s.Contains(d2));
    }

    [Fact]
    public void Add_AStaleHandleToTheSameSlot_IsReplacedByTheNewOne()
    {
        var store = new UnitStore(2);
        EntityHandle a = store.Alloc();
        var s = new SelectionSet(store.Capacity);
        s.Add(a);
        store.Free(a);
        EntityHandle a2 = store.Alloc();
        Assert.True(s.Add(a2));
        Assert.Equal(1, s.Count);
        Assert.True(s.Contains(a2));
        Assert.False(s.Contains(a));
    }

    [Fact]
    public void OutOfRangeHandles_AreRejected()
    {
        var s = new SelectionSet(4);
        Assert.False(s.Add(new EntityHandle(4, 1)));
        Assert.False(s.Add(new EntityHandle(-1, 1)));
        Assert.False(s.Contains(new EntityHandle(99, 1)));
        Assert.Equal(0, s.Count);
    }
}
