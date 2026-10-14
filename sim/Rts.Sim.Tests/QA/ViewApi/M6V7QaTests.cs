using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA (M6-V7): the edge band's last pixel on all four sides at three resolutions, the "when confined" rule, control-group
/// building semantics against a real raze (combat, not a store Free), and the read-only proof: a sim whose ViewApi helpers
/// (ControlGroups' building calls, CameraLimits.EdgeDirection, CursorConfine) are driven every tick hashes the same as a bare twin.
/// </summary>
[Collection(SerialCollection.Name)]
public class M6V7QaTests
{
    private readonly ITestOutputHelper _out;

    public M6V7QaTests(ITestOutputHelper output) => _out = output;

    // ---- the edge band: last pixel, first pixel outside the band, all four sides ----

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(1152, 648)]
    [InlineData(1366, 768)]
    public void EdgeBand_LastPixelOnEverySide_Pans_AndTheBandIsEightPixelsEachSide(int w, int h)
    {
        var size = new Vector2(w, h);
        float band = CameraLimits.EdgePanPixels;
        Assert.Equal(8f, band);
        float midX = w / 2f, midY = h / 2f;
        // The pixels a confined cursor can reach: 0 .. size - 1 (and Godot may report the border, size).
        Assert.Equal(new Vector2(-1, 0), CameraLimits.EdgeDirection(new(0, midY), size, band));
        Assert.Equal(new Vector2(1, 0), CameraLimits.EdgeDirection(new(w - 1, midY), size, band));
        Assert.Equal(new Vector2(0, -1), CameraLimits.EdgeDirection(new(midX, 0), size, band));
        Assert.Equal(new Vector2(0, 1), CameraLimits.EdgeDirection(new(midX, h - 1), size, band));
        // Sub-pixel cursor positions inside the last pixel (a scaled desktop) still pan.
        Assert.Equal(new Vector2(1, 0), CameraLimits.EdgeDirection(new(w - 0.25f, midY), size, band));
        Assert.Equal(new Vector2(0, 1), CameraLimits.EdgeDirection(new(midX, h - 0.25f), size, band));
        // Corners.
        Assert.Equal(new Vector2(-1, -1), CameraLimits.EdgeDirection(new(0, 0), size, band));
        Assert.Equal(new Vector2(1, -1), CameraLimits.EdgeDirection(new(w - 1, 0), size, band));
        Assert.Equal(new Vector2(-1, 1), CameraLimits.EdgeDirection(new(0, h - 1), size, band));
        Assert.Equal(new Vector2(1, 1), CameraLimits.EdgeDirection(new(w - 1, h - 1), size, band));
        // Integer pixel columns / rows in the band: 8 on each side (symmetric), none else.
        int left = 0, right = 0, top = 0, bottom = 0;
        for (int x = 0; x < w; x++)
        {
            Vector2 d = CameraLimits.EdgeDirection(new(x, midY), size, band);
            if (d.X < 0) left++;
            if (d.X > 0) right++;
        }
        for (int y = 0; y < h; y++)
        {
            Vector2 d = CameraLimits.EdgeDirection(new(midX, y), size, band);
            if (d.Y < 0) top++;
            if (d.Y > 0) bottom++;
        }
        Assert.Equal((8, 8, 8, 8), (left, right, top, bottom));
        // One pixel past the window on any side: the cursor is on another monitor, no pan.
        Assert.Equal(Vector2.Zero, CameraLimits.EdgeDirection(new(-1, midY), size, band));
        Assert.Equal(Vector2.Zero, CameraLimits.EdgeDirection(new(w + 1, midY), size, band));
        Assert.Equal(Vector2.Zero, CameraLimits.EdgeDirection(new(midX, -1), size, band));
        Assert.Equal(Vector2.Zero, CameraLimits.EdgeDirection(new(midX, h + 1), size, band));
    }

    [Theory]
    [InlineData(float.PositiveInfinity, 10f)]
    [InlineData(float.NegativeInfinity, 10f)]
    [InlineData(10f, float.PositiveInfinity)]
    public void EdgeBand_InfiniteCursor_NoPan(float x, float y)
    {
        Assert.Equal(Vector2.Zero, CameraLimits.EdgeDirection(new(x, y), new(1280, 720), CameraLimits.EdgePanPixels));
    }

    [Fact]
    public void EdgeBand_ZeroSizedViewport_DoesNotThrow_AndIsFinite()
    {
        // A minimised window can report a 0 x 0 viewport: no crash, a finite direction.
        Vector2 d = CameraLimits.EdgeDirection(Vector2.Zero, Vector2.Zero, CameraLimits.EdgePanPixels);
        Assert.True(float.IsFinite(d.X) && float.IsFinite(d.Y) && Math.Abs(d.X) <= 1 && Math.Abs(d.Y) <= 1);
    }

    // ---- control groups: units, then a building, then units; a raze by combat; A armed is the controller's (scene) ----

    [Fact]
    public void Group_UnitsThenBuildingThenUnits_EachReplacesTheOther_AndRecallSeesOnlyTheLatest()
    {
        (Simulation sim, int house, int site, _) = BuildingPickerTests.Base(3);
        World w = sim.World;
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        var own = new List<EntityHandle>();
        for (int i = 0; i < u.Capacity && own.Count < 3; i++) if (u.Alive[i] && u.Owner[i] == 0) own.Add(new EntityHandle(i, u.Generation[i]));
        Assert.Equal(3, own.Count);
        var groups = new ControlGroups(u.Capacity);
        var selection = new SelectionSet(u.Capacity);

        groups.Assign(4, own.ToArray());
        groups.AssignBuilding(4, b.HandleOf(house));
        Assert.Equal(0, groups.Items(4).Length);
        Assert.Equal(house, groups.RecallBuilding(4, b.Alive, b.Generation));
        Assert.False(groups.TryMean(4, u.Position, u.Alive, u.Generation, out _));
        groups.Assign(4, own.GetRange(0, 2).ToArray());
        Assert.Equal(-1, groups.RecallBuilding(4, b.Alive, b.Generation));
        Assert.False(groups.TryBuildingCentre(4, b, w.Data.Buildings, w.NavGrid, out _));
        Assert.Equal(2, groups.Recall(4, selection, u.Alive, u.Generation));
        // A building replaces a building (Ctrl + digit on the site).
        groups.AssignBuilding(4, b.HandleOf(house));
        groups.AssignBuilding(4, b.HandleOf(site));
        Assert.Equal(site, groups.RecallBuilding(4, b.Alive, b.Generation));
        // Shift + digit with an empty selection keeps the building; Assign with an empty span (the controller never sends one) empties it.
        groups.Add(4, ReadOnlySpan<EntityHandle>.Empty);
        Assert.Equal(site, groups.RecallBuilding(4, b.Alive, b.Generation));
        // Other groups untouched throughout.
        for (int g = 0; g < ControlGroups.Count; g++)
            if (g != 4) Assert.True(groups.Items(g).Length == 0 && groups.Building(g).Generation == 0);
        // Out-of-range group indices throw for the new calls too.
        Assert.Throws<ArgumentOutOfRangeException>(() => groups.AssignBuilding(9, b.HandleOf(house)));
        Assert.Throws<ArgumentOutOfRangeException>(() => groups.RecallBuilding(-1, b.Alive, b.Generation));
        Assert.Throws<ArgumentOutOfRangeException>(() => groups.Building(9));
    }

    [Fact]
    public void Group_HandleOutOfTheStore_IsNeverRecalled()
    {
        (Simulation sim, _, _, _) = BuildingPickerTests.Base(3);
        BuildingStore b = sim.World.Buildings;
        var groups = new ControlGroups(16);
        groups.AssignBuilding(0, new EntityHandle(b.Capacity + 5, 1));
        Assert.Equal(-1, groups.RecallBuilding(0, b.Alive, b.Generation));
        groups.AssignBuilding(1, new EntityHandle(-1, 1));
        Assert.Equal(1, groups.PruneBuildings(b.Alive, b.Generation));
        Assert.Equal(-1, groups.RecallBuilding(1, b.Alive, b.Generation));
    }

    // Twin A: a view that, every tick, holds a Billet in group 0 and a Barracks in group 1 and units in group 2, prunes,
    // recalls, centres, edge-pans and asks CursorConfine; twin B: the same commands only. Player 1's heavy infantry, spawned
    // beside the Billet, raze it (a real raze by combat, not a store Free).
    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    public void ViewApiDriven_HashesEqualABareTwin_EveryTick_AndARazedBuildingIsPruned(int attackers)
    {
        Simulation a = BuildMaps.NewSim(ResourceMaps.Flat(40, 30), units: 64, players: 2);
        Simulation bsim = BuildMaps.NewSim(ResourceMaps.Flat(40, 30), units: 64, players: 2);
        int billetType = BuildMaps.House, barracksType = ProductionMaps.Barracks;
        EntityHandle billet = GatherMaps.Building(a, 10, 10, 0, billetType), barracks = GatherMaps.Building(a, 25, 10, 0, barracksType);
        Assert.Equal(billet, GatherMaps.Building(bsim, 10, 10, 0, billetType));
        Assert.Equal(barracks, GatherMaps.Building(bsim, 25, 10, 0, barracksType));
        World w = a.World;
        BuildingStore b = w.Buildings;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        foreach (Simulation s in new[] { a, bsim })
        {
            s.Enqueue(Command.SpawnUnit(0, GatherMaps.Infantry, g.CellCenter(30, 20)));
            for (int k = 0; k < attackers; k++) s.Enqueue(Command.SpawnUnit(1, GatherMaps.Infantry, g.CellCenter(8 + k % 10, 15 + k / 10)));
            s.Tick();
            s.Tick();
        }
        Assert.Equal(a.StateHash(), bsim.StateHash());
        var groups = new ControlGroups(u.Capacity);
        groups.AssignBuilding(0, billet);
        groups.AssignBuilding(1, barracks);
        var own = new List<EntityHandle>();
        var theirs = new List<EntityHandle>();
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            (u.Owner[i] == 0 ? own : theirs).Add(new EntityHandle(i, u.Generation[i]));
        }
        Assert.Single(own);
        Assert.Equal(attackers, theirs.Count);
        groups.Assign(2, own.ToArray());
        foreach (EntityHandle h in theirs)
        {
            Command c = Command.Attack(1, h, billet, isBuilding: true);
            a.Enqueue(c);
            bsim.Enqueue(c);
        }

        int razedAt = -1;
        var mouse = new Vector2(1279, 719);
        for (int t = 0; t < 6000; t++)
        {
            ulong before = a.StateHash();
            // The view's frame: everything SelectionController and RtsCamera call.
            groups.Prune(u.Alive, u.Generation);
            groups.PruneBuildings(b.Alive, b.Generation);
            int held = groups.RecallBuilding(0, b.Alive, b.Generation);
            groups.RecallBuilding(1, b.Alive, b.Generation);
            groups.TryBuildingCentre(0, b, w.Data.Buildings, g, out _);
            groups.TryBuildingCentre(1, b, w.Data.Buildings, g, out _);
            groups.TryMean(2, u.Position, u.Alive, u.Generation, out _);
            groups.Tap(0, t * 0.05);
            _ = CameraLimits.EdgeDirection(mouse, new Vector2(1280, 720), CameraLimits.EdgePanPixels);
            _ = CursorConfine.Wanted(true, false, true, true);
            Assert.Equal(before, a.StateHash());
            if (held < 0 && razedAt < 0) razedAt = a.TickNumber;
            a.Tick();
            bsim.Tick();
            Assert.Equal(bsim.StateHash(), a.StateHash());
            if (razedAt >= 0 && t > razedAt + 20) break;
        }
        _out.WriteLine($"{attackers} attackers: Billet razed by tick {razedAt}");
        Assert.True(razedAt > 0, "the Billet never fell: the raze scenario did not run");
        Assert.False(b.Alive[billet.Index] && b.Generation[billet.Index] == billet.Generation);
        Assert.Equal(0, groups.Building(0).Generation);
        Assert.False(groups.TryBuildingCentre(0, b, w.Data.Buildings, g, out _));
        // The Barracks' group is untouched by the Billet's fall.
        Assert.Equal(barracks.Index, groups.RecallBuilding(1, b.Alive, b.Generation));
    }
}
