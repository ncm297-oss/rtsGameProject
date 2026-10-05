using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>The ViewApi helpers the view calls every frame or on every click allocate nothing.</summary>
[Collection(SerialCollection.Name)]
public class ViewApiAllocationTests
{
    private readonly ITestOutputHelper _out;

    public ViewApiAllocationTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void TerrainHeight_GroundPicker_ScreenPicker_SelectionPrune_AllocateZeroBytes()
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(3);
        const int n = 2000;
        var centers = new Vector2[n];
        var radii = new float[n];
        var ok = new bool[n];
        var picked = new int[n];
        var store = new UnitStore(n);
        var selection = new SelectionSet(n);
        for (int i = 0; i < n; i++)
        {
            centers[i] = new Vector2(i % 50 * 20, i / 50 * 15);
            radii[i] = 10f;
            ok[i] = i % 3 != 0;
            selection.Add(store.Alloc());
        }
        float sum = 0f;
        int count = 0;
        Action block = () =>
        {
            for (int i = 0; i < n; i++) sum += TerrainHeight.At(map, i * 0.127f, i * 0.113f);
            for (int i = 0; i < 50; i++)
            {
                if (GroundPicker.TryPick(map, new Vector3(i * 5f, 60f, 100f), new Vector3(0.1f, -1f, -0.7f), out Vector3 hit)) sum += hit.Y;
            }
            count += ScreenPicker.PickClick(centers, radii, ok, new Vector2(400, 300));
            count += ScreenPicker.PickBox(centers, ok, new Vector2(0, 0), new Vector2(1152, 648), picked);
            count += selection.Prune(store.Alive, store.Generation);
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"ViewApi per-frame helpers: 0 bytes (runs {runs}), checksum {sum + count}");
    }
}
