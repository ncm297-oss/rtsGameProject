using System.Numerics;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-2): ScreenPicker ties and floods, SelectionSet against a reference model under fuzzing, StartLayout on many maps.</summary>
public class SelectionQaTests
{
    private readonly ITestOutputHelper _out;

    public SelectionQaTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void TwoThousandUnitsInOnePixel_ClickPicksTheLowestCandidateSlot_BoxPicksAllInAscendingOrder()
    {
        const int n = 2000;
        var centers = new Vector2[n];
        var radii = new float[n];
        var cand = new bool[n];
        var picked = new int[n];
        for (int i = 0; i < n; i++)
        {
            centers[i] = new Vector2(500.25f, 300.75f);
            radii[i] = 0.5f;
            cand[i] = i % 7 != 0 && i != 1; // slots 0 and 1 are not candidates (enemy / behind the camera)
        }
        Assert.Equal(2, ScreenPicker.PickClick(centers, radii, cand, new Vector2(500, 301)));
        Assert.Equal(2, ScreenPicker.PickClick(centers, radii, cand, new Vector2(500.25f, 300.75f)));
        int count = ScreenPicker.PickBox(centers, cand, new Vector2(500.25f, 300.75f), new Vector2(500.25f, 300.75f), picked);
        int expected = 0;
        for (int i = 0; i < n; i++) if (cand[i]) expected++;
        Assert.Equal(expected, count);
        for (int k = 1; k < count; k++) Assert.True(picked[k] > picked[k - 1], "box output not ascending");
        Assert.All(picked.AsSpan(0, count).ToArray(), s => Assert.True(cand[s]));
    }

    [Fact]
    public void Click_NearestCentreWins_NotTheBiggestRadius_AndEqualDistanceGoesToTheLowerSlot()
    {
        var centers = new[] { new Vector2(110, 100), new Vector2(103, 100), new Vector2(97, 100), new Vector2(100, 100) };
        var radii = new[] { 50f, 4f, 4f, 0f };
        var cand = new[] { true, true, true, false };
        // (100,100): slot 3 sits exactly there but is no candidate; slots 1 and 2 tie at 3 px -> 1.
        Assert.Equal(1, ScreenPicker.PickClick(centers, radii, cand, new Vector2(100, 100)));
        // 20 px from slot 1/2, 30 px from slot 0 whose 50 px radius covers it: slot 0 is the only hit.
        Assert.Equal(0, ScreenPicker.PickClick(centers, radii, cand, new Vector2(80, 100)));
        // Just outside the 12 px minimum of everything except slot 0's 50 px.
        Assert.Equal(-1, ScreenPicker.PickClick(centers, radii, cand, new Vector2(170, 100)));
    }

    [Fact]
    public void OffScreenAndNonFiniteCentres_AreNeverPickedEvenByAHugeBox()
    {
        var centers = new[] { new Vector2(float.NaN, 5), new Vector2(float.PositiveInfinity, 5), new Vector2(-1e30f, -1e30f), new Vector2(5, 5) };
        var radii = new[] { 1e30f, 1e30f, 1e30f, 1f };
        var cand = new[] { true, true, true, true };
        var picked = new int[4];
        int n = ScreenPicker.PickBox(centers, cand, new Vector2(-10, -10), new Vector2(2000, 2000), picked);
        Assert.Equal(1, n);
        Assert.Equal(3, picked[0]);
        Assert.Equal(3, ScreenPicker.PickClick(centers, radii, cand, new Vector2(5, 5)));
        // A NaN click picks nothing.
        Assert.Equal(-1, ScreenPicker.PickClick(centers, radii, cand, new Vector2(float.NaN, 5)));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void SelectionSet_MatchesAReferenceModel_UnderRandomAddRemoveToggleClearFreeRespawnPrune(ulong seed)
    {
        const int cap = 64;
        var store = new UnitStore(cap);
        var set = new SelectionSet(cap);
        var model = new List<EntityHandle>(); // selection order
        var rng = new SimRng(seed, RngStream.Combat);
        var live = new List<EntityHandle>();
        var everIssued = new List<EntityHandle>();
        for (int step = 0; step < 20000; step++)
        {
            int op = rng.NextInt(0, 8);
            switch (op)
            {
                case 0 when store.Count < cap:
                    EntityHandle h = store.Alloc();
                    live.Add(h);
                    everIssued.Add(h);
                    break;
                case 1 when live.Count > 0:
                    int k = rng.NextInt(0, live.Count);
                    store.Free(live[k]);
                    live.RemoveAt(k);
                    break;
                case 2 or 3 when everIssued.Count > 0:
                {
                    EntityHandle x = everIssued[rng.NextInt(0, everIssued.Count)]; // may be stale
                    bool got = set.Add(x);
                    int at = model.FindIndex(e => e.Index == x.Index);
                    bool want;
                    if (at >= 0) { want = model[at] != x; model[at] = x; }
                    else { want = true; model.Add(x); }
                    Assert.Equal(want, got);
                    break;
                }
                case 4 when everIssued.Count > 0:
                {
                    EntityHandle x = everIssued[rng.NextInt(0, everIssued.Count)];
                    bool got = set.Remove(x);
                    Assert.Equal(model.Remove(x), got);
                    break;
                }
                case 5 when everIssued.Count > 0:
                {
                    EntityHandle x = everIssued[rng.NextInt(0, everIssued.Count)];
                    set.Toggle(x);
                    if (!model.Remove(x))
                    {
                        int at = model.FindIndex(e => e.Index == x.Index);
                        if (at >= 0) model[at] = x; else model.Add(x);
                    }
                    break;
                }
                case 6:
                    if (rng.NextInt(0, 20) == 0) { set.Clear(); model.Clear(); }
                    break;
                case 7:
                {
                    int dropped = set.Prune(store.Alive, store.Generation);
                    int before = model.Count;
                    model.RemoveAll(e => !store.IsAlive(e));
                    Assert.Equal(before - model.Count, dropped);
                    break;
                }
            }
            Assert.Equal(model.Count, set.Count);
            Assert.Equal(model.ToArray(), set.Items.ToArray());
            foreach (EntityHandle e in everIssued) Assert.Equal(model.Contains(e), set.Contains(e));
        }
        // Out-of-range handles never touch the set.
        Assert.False(set.Add(new EntityHandle(cap, 0)));
        Assert.False(set.Add(new EntityHandle(-1, 0)));
        Assert.False(set.Remove(new EntityHandle(int.MaxValue, 0)));
        Assert.False(set.Contains(new EntityHandle(-5, 0)));
    }

    [Theory]
    [InlineData(1UL, 0.4f)]
    [InlineData(2UL, 1.0f)]
    [InlineData(3UL, 1.01f)]
    [InlineData(4UL, 2.5f)]
    [InlineData(5UL, 0f)]
    [InlineData(6UL, 0.6f)]
    public void StartLayout_OnGeneratedMaps_PassableUniqueOnItsSide_NoOverlap_AtTheMaxCount(ulong seed, float radius)
    {
        var rng = new SimRng(seed, RngStream.MapGen);
        Heightmap map = MapGenerator.Generate(MapGenParams.Default, ref rng);
        var grid = new NavGrid(map);
        float midX = grid.Width / 2 * MapConstants.CellSize;
        foreach (bool west in new[] { true, false })
        {
            Vector2[] a = StartLayout.Block(grid, 1000, west, radius);
            Vector2[] again = StartLayout.Block(grid, 1000, west, radius);
            Assert.Equal(a, again);
            var cells = new HashSet<(int, int)>();
            foreach (Vector2 p in a)
            {
                Assert.True(grid.WorldToCell(p, out int x, out int y), $"{p} off the map");
                Assert.True(grid.IsPassable(x, y), $"{p} on a blocked cell");
                Assert.True(cells.Add((x, y)), $"{p} duplicated");
                Assert.True(west ? p.X < midX : p.X > midX, $"{p} on the wrong side of the centre line (west {west})");
            }
            float minGap = float.MaxValue;
            for (int i = 0; i < a.Length; i++)
                for (int j = i + 1; j < a.Length; j++)
                    minGap = MathF.Min(minGap, Vector2.Distance(a[i], a[j]));
            _out.WriteLine($"seed {seed} r {radius} {(west ? "west" : "east")}: {a.Length} positions, min centre gap {minGap:F3} m (needs {2 * radius:F3})");
            Assert.True(a.Length == 1000 || radius >= 2.5f, $"only {a.Length} of 1000 fit");
            Assert.True(minGap >= 2 * radius - 1e-3f, $"bodies overlap: gap {minGap} < {2 * radius}");
        }
    }

    [Fact]
    public void StartLayout_ZeroNegativeAndNaNInputs_DoNotThrow()
    {
        var rng = new SimRng(1, RngStream.MapGen);
        var grid = new NavGrid(MapGenerator.Generate(MapGenParams.Default, ref rng));
        Assert.Empty(StartLayout.Block(grid, 0, true, 1f));
        Assert.Empty(StartLayout.Block(grid, -5, false, 1f));
        Assert.Equal(10, StartLayout.Block(grid, 10, true, float.NaN).Length);
        Assert.Equal(10, StartLayout.Block(grid, 10, true, -3f).Length);
        Assert.True(StartLayout.Block(grid, 100000, true, 0.5f).Length < 100000);
    }
}
