using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M6-VH2, BUG-0622 item 1): <see cref="StartBase.Plan"/> after the west / north wrap fix, attacked independently of the
/// developer's <c>StartBaseTests.HandBuiltStartByAnEdge_HallStaysByIt</c>. Every cell of a 40 x 40 map's 5-cell border band
/// as a hand-built start location: no exception, the hall (when there is one) is a hall spot by the location (never across
/// the map), the plan is the same twice, and the plan is read-only: the state hash and the building / unit counts are
/// unchanged by planning (the view track's ViewApi rule).
/// </summary>
public class StartBaseEdgeQaTests
{
    private readonly ITestOutputHelper _out;

    public StartBaseEdgeQaTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void EveryBorderBandCell_AsAStartLocation_HallByIt_SameTwice_StateUntouched()
    {
        const int size = 40, band = 5;
        int planned = 0, none = 0, worst = 0;
        Heightmap flat = ResourceMaps.Flat(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (x >= band && y >= band && x < size - band && y < size - band) continue;
                var hm = new Heightmap(size, size, flat.Levels, flat.Elevations, new[] { new StartLocation(y * size + x, 0) });
                Simulation sim = ResourceMaps.NewSim(hm);
                World w = sim.World;
                ulong before = sim.StateHash();
                int buildings = w.Buildings.Count, units = w.Units.Count;
                StartBasePlan a = ViewReads.Plan(w, new[] { Array.Empty<Vector2>() }, 5, 0.5f);
                StartBasePlan b = ViewReads.Plan(w, new[] { Array.Empty<Vector2>() }, 5, 0.5f);
                Assert.True(before == sim.StateHash(), $"start ({x},{y}): planning moved the state hash");
                Assert.True(buildings == w.Buildings.Count && units == w.Units.Count, $"start ({x},{y}): planning changed the stores");
                Assert.True(a.HallAnchor[0] == b.HallAnchor[0] && a.Workers[0].Length == b.Workers[0].Length, $"start ({x},{y}): two plans differ");
                for (int k = 0; k < a.Workers[0].Length; k++) Assert.Equal(a.Workers[0][k], b.Workers[0][k]);
                int anchor = a.HallAnchor[0];
                if (anchor < 0) { none++; continue; }
                planned++;
                int ax = anchor % size, ay = anchor / size;
                BuildingDef hall = w.Data.Buildings[a.HallType[0]];
                Assert.True(ViewReads.IsHallSpot(w, a.HallType[0], anchor, 0, null), $"start ({x},{y}): ({ax},{ay}) is no hall spot");
                Assert.True(ax + hall.FootprintWidth <= size && ay + hall.FootprintHeight <= size, $"start ({x},{y}): footprint off the map at ({ax},{ay})");
                int dist = (int)MathF.Ceiling(MathF.Max(MathF.Abs(ax + hall.FootprintWidth / 2f - x), MathF.Abs(ay + hall.FootprintHeight / 2f - y)));
                worst = Math.Max(worst, dist);
                Assert.True(dist <= 6, $"start ({x},{y}): hall at ({ax},{ay}), {dist} cells from the location");
                // Workers stand on the map, near the hall.
                foreach (Vector2 p in a.Workers[0])
                    Assert.True(p.X > 0 && p.Y > 0 && p.X < size * MapConstants.CellSize && p.Y < size * MapConstants.CellSize, $"start ({x},{y}): worker at {p}");
            }
        _out.WriteLine($"border band of {size} x {size}: {planned} halls planned, {none} without a spot, farthest hall centre {worst} cells from its location");
        Assert.True(planned > 0);
    }

    /// <summary>A location already well inside the map keeps the centred anchor (the fix changes only the off-map case).</summary>
    [Fact]
    public void InteriorLocations_KeepTheCentredAnchor()
    {
        const int size = 40;
        Heightmap flat = ResourceMaps.Flat(size, size);
        for (int y = 8; y < size - 8; y += 3)
            for (int x = 8; x < size - 8; x += 3)
            {
                var hm = new Heightmap(size, size, flat.Levels, flat.Elevations, new[] { new StartLocation(y * size + x, 0) });
                Simulation sim = ResourceMaps.NewSim(hm);
                StartBasePlan plan = ViewReads.Plan(sim.World, new[] { Array.Empty<Vector2>() }, 5, 0.5f);
                BuildingDef hall = sim.World.Data.Buildings[plan.HallType[0]];
                int expected = (y - hall.FootprintHeight / 2) * size + x - hall.FootprintWidth / 2;
                Assert.True(expected == plan.HallAnchor[0], $"start ({x},{y}): anchor {plan.HallAnchor[0]}, want {expected}");
            }
    }
}
