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
    public void DoubleTapSeconds_IsDerivedFromDoubleTapMs()
    {
        // BUG-0070: two literals for one rule could drift apart.
        Assert.Equal(ControlGroups.DoubleTapMs, ControlGroups.DoubleTapSeconds * 1000);
        Assert.Equal(0.3, ControlGroups.DoubleTapSeconds);
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

    [Theory]
    [InlineData(0UL)]
    [InlineData(800UL)] // 1.1 - 0.8 is 0.30000000000000004 in seconds (BUG-0067)
    [InlineData(123_456_789UL)]
    public void Tap_GapsCompareInWholeMilliseconds_AtAnyClockValue(ulong t0)
    {
        var groups = new ControlGroups(1);
        Assert.False(groups.Tap(0, t0 / 1000.0));
        Assert.True(groups.Tap(0, (t0 + 300) / 1000.0), "exactly 300 ms is a double-tap");
        Assert.False(groups.Tap(0, (t0 + 1000) / 1000.0));
        Assert.False(groups.Tap(0, (t0 + 1301) / 1000.0), "301 ms is not");
        Assert.False(groups.Tap(0, double.NaN));
        Assert.False(groups.Tap(0, double.NaN)); // NaN never pairs
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

    // ---- M6-V7: a group holds units or one building ----

    [Fact]
    public void AssignBuilding_RecallSelectsIt_AndHoldsNoUnits()
    {
        (Simulation sim, int house, _, _) = BuildingPickerTests.Base(1);
        BuildingStore b = sim.World.Buildings;
        UnitStore u = sim.World.Units;
        var groups = new ControlGroups(u.Capacity);
        groups.AssignBuilding(0, b.HandleOf(house));
        Assert.Equal(b.HandleOf(house), groups.Building(0));
        Assert.Equal(house, groups.RecallBuilding(0, b.Alive, b.Generation));
        Assert.Equal(0, groups.Items(0).Length);
        // The unit recall finds nothing, so the selection it would replace is left alone (the controller selects the building).
        var selection = new SelectionSet(u.Capacity);
        Assert.Equal(0, groups.Recall(0, selection, u.Alive, u.Generation));
        Assert.Equal(-1, groups.RecallBuilding(1, b.Alive, b.Generation)); // groups are independent
        Assert.Equal(0, groups.Building(1).Generation);
    }

    [Fact]
    public void UnitsReplaceABuilding_AndABuildingReplacesUnits()
    {
        (Simulation sim, int house, int site, _) = BuildingPickerTests.Base(1);
        BuildingStore b = sim.World.Buildings;
        var units = new UnitStore(8);
        EntityHandle a = units.Alloc(), c = units.Alloc();
        var groups = new ControlGroups(units.Capacity);

        // Ctrl + digit with units over a building group: units only.
        groups.AssignBuilding(0, b.HandleOf(house));
        groups.Assign(0, new[] { a });
        Assert.Equal(0, groups.Building(0).Generation);
        Assert.Equal(-1, groups.RecallBuilding(0, b.Alive, b.Generation));
        Assert.Equal(new[] { a }, groups.Items(0).ToArray());

        // Ctrl (or Shift) + digit with a building over a unit group: the building only.
        groups.AssignBuilding(0, b.HandleOf(site));
        Assert.Equal(0, groups.Items(0).Length);
        Assert.Equal(site, groups.RecallBuilding(0, b.Alive, b.Generation));

        // Shift + digit with units over a building group: the units come in, the building goes; with no units nothing changes.
        groups.Add(0, ReadOnlySpan<EntityHandle>.Empty);
        Assert.Equal(site, groups.RecallBuilding(0, b.Alive, b.Generation));
        groups.Add(0, new[] { a, c });
        Assert.Equal(0, groups.Building(0).Generation);
        Assert.Equal(new[] { a, c }, groups.Items(0).ToArray());
    }

    [Fact]
    public void ARazedOrRecycledBuilding_IsPrunedAndNeverRecalled()
    {
        (Simulation sim, int house, int site, int enemy) = BuildingPickerTests.Base(1);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        var groups = new ControlGroups(w.Units.Capacity);
        EntityHandle houseHandle = b.HandleOf(house);
        groups.AssignBuilding(0, houseHandle);
        groups.AssignBuilding(1, b.HandleOf(site));
        groups.AssignBuilding(2, houseHandle);
        int houseType = b.TypeId[house], houseCell = b.Cell[house];

        Assert.True(b.Free(houseHandle)); // razed
        // Recalled before a frame's prune: dropped, never selected.
        Assert.Equal(-1, groups.RecallBuilding(0, b.Alive, b.Generation));
        Assert.Equal(0, groups.Building(0).Generation);
        // The slot reused by another building (a new generation): still never recalled.
        Assert.True(b.Spawn(1, houseType, houseCell, out EntityHandle reborn));
        Assert.Equal(house, reborn.Index);
        Assert.Equal(1, groups.PruneBuildings(b.Alive, b.Generation)); // group 2's
        Assert.Equal(-1, groups.RecallBuilding(2, b.Alive, b.Generation));
        Assert.Equal(site, groups.RecallBuilding(1, b.Alive, b.Generation)); // the site's group is untouched
        Assert.Equal(0, groups.PruneBuildings(b.Alive, b.Generation));
        Assert.True(enemy >= 0);
    }

    [Fact]
    public void DoubleTapCentre_IsTheBuildingsFootprintCentre()
    {
        (Simulation sim, int house, _, _) = BuildingPickerTests.Base(1);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        var groups = new ControlGroups(w.Units.Capacity);
        groups.AssignBuilding(0, b.HandleOf(house));
        Assert.True(groups.TryBuildingCentre(0, b, w.Data.Buildings, w.NavGrid, out Vector2 centre));
        Assert.Equal(StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[b.TypeId[house]], b.Cell[house]), centre);

        // A unit group or an empty one has no building centre (the controller then takes the units' mean, TryMean).
        groups.Assign(1, new[] { new EntityHandle(0, 1) });
        Assert.False(groups.TryBuildingCentre(1, b, w.Data.Buildings, w.NavGrid, out _));
        Assert.False(groups.TryBuildingCentre(2, b, w.Data.Buildings, w.NavGrid, out _));
        // Razed: false, and the group lets it go.
        Assert.True(b.Free(b.HandleOf(house)));
        Assert.False(groups.TryBuildingCentre(0, b, w.Data.Buildings, w.NavGrid, out _));
        Assert.Equal(0, groups.Building(0).Generation);
    }
}
