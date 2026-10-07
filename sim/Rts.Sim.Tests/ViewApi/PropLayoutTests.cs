using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M2-3b: the resource props' instance lists match the store (count per type, footprint centre on the terrain), relist once per grid version and never otherwise.</summary>
public class PropLayoutTests
{
    private const float Cs = MapConstants.CellSize;

    /// <summary>The Match's map: default terrain with 12 forests and 8 mines.</summary>
    internal static Simulation MatchSim(ulong seed, int units = 64, int forests = 12, int mines = 8) =>
        new(TestSim.Config(seed, 2, units, 4096) with { Map = MapGenParams.Default with { Forests = forests, GoldMines = mines } });

    internal static PropLayout Layout(World w) => new(w.Data.Resources, w.Resources.Capacity);

    internal static bool Refresh(PropLayout l, World w) =>
        l.Refresh(w.Heightmap, w.NavGrid, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell);

    /// <summary>Oracle: every live node of every type, in slot order, at its footprint centre on its (flat, one-level) footprint's elevation.</summary>
    internal static void AssertMatchesStore(PropLayout l, World w)
    {
        ResourceStore r = w.Resources;
        int gw = w.NavGrid.Width;
        for (int t = 0; t < l.TypeCount; t++)
        {
            var want = new List<int>();
            for (int s = 0; s < r.Capacity; s++)
                if (r.Alive[s] && r.TypeId[s] == t) want.Add(s);
            Assert.Equal(want.ToArray(), l.SlotsOf(t).ToArray());
            Assert.Equal(want.Count, l.CountOf(t));
            ReadOnlySpan<float> b = l.TransformsOf(t);
            Assert.Equal(want.Count * PropLayout.Stride, b.Length);
            var def = w.Data.Resources[t];
            for (int k = 0; k < want.Count; k++)
            {
                int a = r.Cell[want[k]], x0 = a % gw, y0 = a / gw;
                float ex = (x0 + def.FootprintWidth / 2f) * Cs, ez = (y0 + def.FootprintHeight / 2f) * Cs;
                float ey = w.Heightmap.ElevationAt(x0, y0);
                int o = k * PropLayout.Stride;
                Assert.True(MathF.Abs(b[o + 3] - ex) <= 1e-3f && MathF.Abs(b[o + 7] - ey) <= 1e-3f && MathF.Abs(b[o + 11] - ez) <= 1e-3f,
                    $"type {t} slot {want[k]}: origin ({b[o + 3]}, {b[o + 7]}, {b[o + 11]}), want ({ex}, {ey}, {ez})");
                // A pure yaw: unit up axis, orthonormal ground axes.
                Assert.Equal(new Vector3(0f, 1f, 0f), new Vector3(b[o + 1], b[o + 5], b[o + 9]));
                Assert.Equal(1f, b[o] * b[o] + b[o + 8] * b[o + 8], 3);
                Assert.Equal(b[o], b[o + 10]);
                Assert.Equal(b[o + 2], -b[o + 8]);
                if (def.FootprintWidth * def.FootprintHeight > 1) Assert.True(b[o] is 0f or 1f or -1f, $"type {t}: basis {b[o]} not axis-aligned");
            }
        }
    }

    [Fact]
    public void HandMap_TreesAndAMineOnTwoLevels_SitAtFootprintCentresOnTheTerrain()
    {
        // A level-1 plateau in the middle of a flat 17 x 16 map, reached by a ramp at (4, 8): its lip is cliff, its inside open.
        var rows = new string[16];
        for (int y = 0; y < 16; y++) rows[y] = y == 8 ? "0000r111111100000" : y is >= 5 and <= 11 ? "00000111111100000" : "00000000000000000";
        Simulation sim = ResourceMaps.NewSim(ResourceMaps.FromRows(rows));
        World w = sim.World;
        ResourceMaps.Spawn(w, ResourceMaps.Tree, 2, 2, 50);
        ResourceMaps.Spawn(w, ResourceMaps.Tree, 3, 2, 50);
        ResourceMaps.Spawn(w, ResourceMaps.Tree, 8, 8, 50);              // on the plateau
        ResourceMaps.Spawn(w, ResourceMaps.Mine, 2, 12, 500);            // 2 x 2 on level 0
        EntityHandle highMine = ResourceMaps.Spawn(w, ResourceMaps.Mine, 9, 7, 500); // 2 x 2 on level 1
        PropLayout l = Layout(w);
        Assert.True(Refresh(l, w));
        AssertMatchesStore(l, w);
        Assert.Equal(3, l.CountOf(ResourceMaps.Tree));
        Assert.Equal(2, l.CountOf(ResourceMaps.Mine));

        // A 2 x 2 mine's centre is its anchor cell's far corner (a cell corner, not a cell centre), at plateau height.
        ReadOnlySpan<float> m = l.TransformsOf(ResourceMaps.Mine);
        int k = l.SlotsOf(ResourceMaps.Mine).IndexOf(highMine.Index);
        Assert.Equal(new Vector3(10 * Cs, MapConstants.LevelHeight, 8 * Cs),
            new Vector3(m[k * PropLayout.Stride + 3], m[k * PropLayout.Stride + 7], m[k * PropLayout.Stride + 11]));
        // A tree on the plateau stands at level 1 in its cell's centre.
        Assert.Equal(new Vector3(8.5f * Cs, MapConstants.LevelHeight, 8.5f * Cs),
            PropLayout.CentreOf(w.Heightmap, w.NavGrid.Width, 8 * w.NavGrid.Width + 8, 1, 1));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void GeneratedMaps_12Forests8Mines_CountsEqualTheStorePerType(ulong seed)
    {
        Simulation sim = MatchSim(seed);
        World w = sim.World;
        Assert.True(w.ResourcePlacement.Mines > 0 && w.ResourcePlacement.Trees > 0, $"seed {seed}: placed {w.ResourcePlacement}");
        PropLayout l = Layout(w);
        Refresh(l, w);
        AssertMatchesStore(l, w);
        Assert.Equal(w.ResourcePlacement.Trees, l.CountOf(ResourceMaps.Tree));
        Assert.Equal(w.ResourcePlacement.Mines, l.CountOf(ResourceMaps.Mine));
    }

    [Fact]
    public void TakeToZero_RemovesTheInstance_WithExactlyOneRelist()
    {
        Simulation sim = MatchSim(1);
        World w = sim.World;
        ResourceStore r = w.Resources;
        PropLayout l = Layout(w);
        Refresh(l, w);
        Assert.False(Refresh(l, w));
        Assert.Equal(1, l.Rebuilds);
        foreach (int type in new[] { ResourceMaps.Tree, ResourceMaps.Mine })
        {
            int before = l.CountOf(type);
            int slot = l.SlotsOf(type)[before / 2];
            // A partial take changes nothing visible and no version: no relist.
            Assert.Equal(1, r.Take(r.HandleOf(slot), 1));
            Assert.False(Refresh(l, w));
            int rebuilds = l.Rebuilds;
            r.Take(r.HandleOf(slot), int.MaxValue);
            Assert.True(Refresh(l, w));
            Assert.False(Refresh(l, w));
            Assert.Equal(rebuilds + 1, l.Rebuilds);
            Assert.Equal(before - 1, l.CountOf(type));
            Assert.DoesNotContain(slot, l.SlotsOf(type).ToArray());
            AssertMatchesStore(l, w);
        }
    }

    [Fact]
    public void ARelist_MarksChangedOnlyTheTypesWhoseListChanged()
    {
        // BUG-0086: one felled tree re-uploaded both the tree and the mine buffers.
        Simulation sim = MatchSim(1);
        World w = sim.World;
        ResourceStore r = w.Resources;
        PropLayout l = Layout(w);
        Refresh(l, w);
        Assert.True(l.Changed(ResourceMaps.Tree) && l.Changed(ResourceMaps.Mine), "the first fill changes every type");
        foreach ((int felled, int other) in new[] { (ResourceMaps.Tree, ResourceMaps.Mine), (ResourceMaps.Mine, ResourceMaps.Tree) })
        {
            int slot = l.SlotsOf(felled)[l.CountOf(felled) / 2];
            r.Take(r.HandleOf(slot), int.MaxValue);
            Assert.True(Refresh(l, w));
            Assert.True(l.Changed(felled), $"type {felled} lost a node but is not marked changed");
            Assert.False(l.Changed(other), $"type {other} is marked changed, but nothing of it changed");
        }
        // A version bump with nothing changed relists, and marks nothing.
        w.NavGrid.BumpVersionForTests();
        Assert.True(Refresh(l, w));
        Assert.False(l.Changed(ResourceMaps.Tree) || l.Changed(ResourceMaps.Mine));
        // Losing the last node of the list (the tail) is a count change.
        int last = l.SlotsOf(ResourceMaps.Tree)[l.CountOf(ResourceMaps.Tree) - 1];
        r.Take(r.HandleOf(last), int.MaxValue);
        Assert.True(Refresh(l, w));
        Assert.True(l.Changed(ResourceMaps.Tree) && !l.Changed(ResourceMaps.Mine));
    }

    [Fact]
    public void SixHundredTicksWithNoPassabilityChange_NeverRelist()
    {
        Simulation sim = MatchSim(2, units: 200);
        World w = sim.World;
        Vector2[] spots = StartLayout.Block(w.NavGrid, 100, west: true, 1f);
        for (int k = 0; k < spots.Length; k++) sim.Enqueue(Rts.Sim.Commands.Command.SpawnUnit(0, 0, spots[k]));
        PropLayout l = Layout(w);
        Refresh(l, w);
        int version = w.NavGrid.Version;
        for (int tick = 0; tick < 600; tick++)
        {
            if (tick == 5)
            {
                // March the army across the map so the ticks do real work.
                for (int i = 0; i < w.Units.Capacity; i++)
                    if (w.Units.Alive[i]) sim.Enqueue(Rts.Sim.Commands.Command.Move(0, new EntityHandle(i, w.Units.Generation[i]), new Vector2(200f, 128f)));
            }
            sim.Tick();
            Assert.False(Refresh(l, w));
        }
        Assert.Equal(version, w.NavGrid.Version);
        Assert.Equal(1, l.Rebuilds);
    }

    [Fact]
    public void Yaw_OneCellAnyAngle_SquareQuarterTurns_OblongHalfTurns_StableAndVaried()
    {
        var oneCell = new HashSet<float>();
        int same = 0;
        for (int c = 0; c < 4096; c++)
        {
            float a = PropLayout.YawOf(c, 1, 1), q = PropLayout.YawOf(c, 2, 2) / (MathF.PI / 2), h = PropLayout.YawOf(c, 3, 1) / MathF.PI;
            Assert.True(a >= 0f && a < 2f * MathF.PI && a == PropLayout.YawOf(c, 1, 1) && q == MathF.Round(q) && h is 0f or 1f, $"cell {c}: {a} {q} {h}");
            oneCell.Add(a);
            if (a == PropLayout.YawOf(c + 1, 1, 1)) same++;
        }
        Assert.True(oneCell.Count > 200, $"only {oneCell.Count} distinct tree yaws");
        // Neighbouring cells differ, so a forest doesn't look stamped.
        Assert.True(same < 80, $"{same} of 4096 neighbours share a yaw");
    }
}
