using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>Tab subgroups: distinct types in ascending id, wrap, reset on a new selection, survival across deaths, edges.</summary>
public class SubgroupsTests
{
    private static (UnitStore Store, EntityHandle[] Units) Units(params int[] types)
    {
        var store = new UnitStore(Math.Max(1, types.Length));
        var units = new EntityHandle[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            units[i] = store.Alloc();
            store.TypeId[units[i].Index] = types[i];
        }
        return (store, units);
    }

    [Fact]
    public void Types_AreDistinctAndAscending_TabWraps()
    {
        (UnitStore store, EntityHandle[] units) = Units(4, 1, 4, 2, 1);
        var sub = new Subgroups(5);
        sub.Update(units, store.TypeId, reset: true);
        Assert.Equal(new[] { 1, 2, 4 }, sub.Types.ToArray());
        Assert.Equal(1, sub.ActiveType);
        sub.Next();
        Assert.Equal((1, 2), (sub.Index, sub.ActiveType));
        sub.Next();
        Assert.Equal((2, 4), (sub.Index, sub.ActiveType));
        sub.Next();
        Assert.Equal((0, 1), (sub.Index, sub.ActiveType));
    }

    [Fact]
    public void EmptySelection_HasNoSubgroups_AndTabDoesNothing()
    {
        var sub = new Subgroups(3);
        sub.Update(ReadOnlySpan<EntityHandle>.Empty, ReadOnlySpan<int>.Empty, reset: true);
        sub.Next();
        Assert.Equal((0, 0, -1), (sub.Count, sub.Index, sub.ActiveType));
    }

    [Fact]
    public void Reset_GoesBackToTheFirst_EvenForTheSameTypes()
    {
        (UnitStore store, EntityHandle[] units) = Units(0, 1, 2);
        var sub = new Subgroups(3);
        sub.Update(units, store.TypeId, reset: true);
        sub.Next();
        sub.Next();
        sub.Update(units, store.TypeId, reset: true);
        Assert.Equal(0, sub.Index);
    }

    [Fact]
    public void Refresh_KeepsTheActiveType_WhileItIsSelected_ElseFallsBackToTheFirst()
    {
        (UnitStore store, EntityHandle[] units) = Units(0, 1, 2);
        var sub = new Subgroups(3);
        sub.Update(units, store.TypeId, reset: true);
        sub.Next();
        sub.Next(); // type 2
        sub.Update(units.AsSpan(1), store.TypeId, reset: false); // type 0 died: type 2 is now index 1
        Assert.Equal((1, 2), (sub.Index, sub.ActiveType));
        sub.Update(units.AsSpan(0, 2), store.TypeId, reset: false); // type 2 gone
        Assert.Equal((0, 0), (sub.Index, sub.ActiveType));
    }

    [Fact]
    public void CapacityEdges_EveryTypeOnce_AndOutOfRangeTypesIgnored()
    {
        (UnitStore store, EntityHandle[] units) = Units(0, 1, 2, 3, 99, -1);
        var sub = new Subgroups(4);
        sub.Update(units, store.TypeId, reset: true);
        Assert.Equal(new[] { 0, 1, 2, 3 }, sub.Types.ToArray());
        sub.Update(new[] { new EntityHandle(50, 1) }, store.TypeId, reset: true); // slot past the store
        Assert.Equal(0, sub.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Subgroups(0));
    }
}
