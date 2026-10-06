using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>Control groups 1-9: assign, add, recall, pruning of dead and recycled units, the double-tap rule, the camera mean.</summary>
public class ControlGroupsTests
{
    [Fact]
    public void Assign_Replaces_Add_Appends_Recall_ReplacesTheSelection()
    {
        var store = new UnitStore(16);
        EntityHandle a = store.Alloc(), b = store.Alloc(), c = store.Alloc(), d = store.Alloc();
        var groups = new ControlGroups(store.Capacity);
        groups.Assign(0, new[] { a, b });
        groups.Assign(0, new[] { c });
        Assert.Equal(new[] { c }, groups.Items(0).ToArray());
        groups.Add(0, new[] { a, c });
        Assert.Equal(new[] { c, a }, groups.Items(0).ToArray());
        Assert.Equal(0, groups.Items(1).Length); // groups are independent

        var selection = new SelectionSet(store.Capacity);
        selection.Add(d);
        Assert.Equal(2, groups.Recall(0, selection, store.Alive, store.Generation));
        Assert.Equal(new[] { c, a }, selection.Items.ToArray());
    }

    [Fact]
    public void Recall_OfAnEmptyGroup_LeavesTheSelectionAlone()
    {
        var store = new UnitStore(4);
        EntityHandle a = store.Alloc();
        var groups = new ControlGroups(store.Capacity);
        var selection = new SelectionSet(store.Capacity);
        selection.Add(a);
        Assert.Equal(0, groups.Recall(8, selection, store.Alive, store.Generation));
        Assert.Equal(new[] { a }, selection.Items.ToArray());
    }

    [Fact]
    public void RecycledSlot_IsNeverRecalled_EvenBeforeAFramePrune()
    {
        var store = new UnitStore(4);
        EntityHandle a = store.Alloc(), b = store.Alloc();
        var groups = new ControlGroups(store.Capacity);
        groups.Assign(0, new[] { a, b });
        store.Free(a);
        EntityHandle reborn = store.Alloc(); // same slot, new generation (an enemy, say)
        Assert.Equal(a.Index, reborn.Index);
        var selection = new SelectionSet(store.Capacity);
        Assert.Equal(1, groups.Recall(0, selection, store.Alive, store.Generation));
        Assert.Equal(new[] { b }, selection.Items.ToArray());
        Assert.False(selection.Contains(reborn));
    }

    [Fact]
    public void Prune_DropsDeadAndRecycledFromEveryGroup()
    {
        var store = new UnitStore(8);
        EntityHandle a = store.Alloc(), b = store.Alloc(), c = store.Alloc();
        var groups = new ControlGroups(store.Capacity);
        for (int g = 0; g < ControlGroups.Count; g++) groups.Assign(g, new[] { a, b, c });
        store.Free(b);
        store.Free(c);
        store.Alloc(); // recycles c's slot (LIFO)
        Assert.Equal(2 * ControlGroups.Count, groups.Prune(store.Alive, store.Generation));
        for (int g = 0; g < ControlGroups.Count; g++) Assert.Equal(new[] { a }, groups.Items(g).ToArray());
        Assert.Equal(0, groups.Prune(store.Alive, store.Generation));
    }

    [Fact]
    public void FullCapacity_EveryGroupHoldsEverySlot()
    {
        var store = new UnitStore(1000);
        var all = new EntityHandle[store.Capacity];
        for (int i = 0; i < all.Length; i++) all[i] = store.Alloc();
        var groups = new ControlGroups(store.Capacity);
        for (int g = 0; g < ControlGroups.Count; g++)
        {
            groups.Assign(g, all);
            groups.Add(g, all);
            Assert.Equal(all.Length, groups.Items(g).Length);
        }
        var selection = new SelectionSet(store.Capacity);
        Assert.Equal(all.Length, groups.Recall(8, selection, store.Alive, store.Generation));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void GroupIndexOutOfRange_Throws(int group)
    {
        var groups = new ControlGroups(4);
        Assert.Throws<ArgumentOutOfRangeException>(() => groups.Assign(group, ReadOnlySpan<EntityHandle>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => groups.Tap(group, 0));
    }

    [Fact]
    public void Tap_SecondTapOfTheSameGroupWithinTheWindow_IsADoubleTap()
    {
        var groups = new ControlGroups(4);
        Assert.False(groups.Tap(0, 0.0));
        Assert.True(groups.Tap(0, ControlGroups.DoubleTapSeconds)); // the boundary counts
        Assert.False(groups.Tap(0, 10.31)); // consumed: a third tap starts a new pair
        Assert.False(groups.Tap(0, 10.31 + ControlGroups.DoubleTapSeconds + 0.001)); // too slow
        Assert.False(groups.Tap(1, 11.0));
        Assert.False(groups.Tap(2, 11.1)); // another group in between breaks the pair
        Assert.False(groups.Tap(1, 11.2));
        Assert.True(groups.Tap(1, 11.3));
        Assert.False(groups.Tap(3, 20.0));
        Assert.False(groups.Tap(3, 19.9)); // a clock running backwards never doubles
    }

    [Fact]
    public void TryMean_AveragesLiveUnitsOnly()
    {
        var store = new UnitStore(4);
        EntityHandle a = store.Alloc(), b = store.Alloc(), c = store.Alloc();
        store.Position[a.Index] = new Vector2(10, 20);
        store.Position[b.Index] = new Vector2(30, 40);
        store.Position[c.Index] = new Vector2(1000, 1000);
        var groups = new ControlGroups(store.Capacity);
        groups.Assign(0, new[] { a, b, c });
        store.Free(c);
        Assert.True(groups.TryMean(0, store.Position, store.Alive, store.Generation, out Vector2 mean));
        Assert.Equal(new Vector2(20, 30), mean);
        Assert.False(groups.TryMean(1, store.Position, store.Alive, store.Generation, out _));
    }
}
