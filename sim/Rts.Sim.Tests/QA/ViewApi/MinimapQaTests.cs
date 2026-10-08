using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-4): minimap read-only proof (hash twin with minimap-issued orders), transform precision at pixel edges on square and non-square maps, orders to the exact far edge, dots across death and respawn as the other faction, and refresh cost on a 256 map.</summary>
public class MinimapQaTests
{
    private const float Cs = MapConstants.CellSize;
    private static readonly uint[] Colors = { 0x3366CC, 0xE08020 };
    private readonly ITestOutputHelper _out;

    public MinimapQaTests(ITestOutputHelper output) => _out = output;

    private static SimConfig Config(ulong seed, int w, int h) =>
        TestSim.ConfigNoCombat(seed, PlayerCount: 2, UnitCapacity: 2000, CommandCapacity: 4096) with { Map = MapGenParams.Default with { Width = w, Height = h } };

    private static List<Command> StartArmies(Simulation sim, int perPlayer)
    {
        var cmds = new List<Command>();
        GameData data = sim.World.Data;
        for (int p = 0; p < 2; p++)
        {
            FactionDef f = data.Factions[p];
            float maxR = 0f;
            foreach (int t in f.Units) maxR = Math.Max(maxR, data.Units[t].Radius);
            Vector2[] spots = StartLayout.Block(sim.World.NavGrid, perPlayer, p == 0, maxR);
            for (int k = 0; k < spots.Length; k++) cmds.Add(Command.SpawnUnit(p, f.Units[k % f.Units.Length], spots[k]));
        }
        return cmds;
    }

    // Pixels a player is likely to hit on the minimap's edges: the map rect's exact corners and borders, a hair inside and a hair outside.
    private static Vector2 EdgePixel(MinimapTransform fit, SimRng rng)
    {
        (Vector2 pos, Vector2 size) = fit.MapRect;
        float[] xs = { pos.X, pos.X + 0.001f, pos.X - 0.01f, pos.X + size.X, pos.X + size.X - 0.001f, pos.X + size.X + 0.01f, pos.X + size.X - 0.5f };
        float[] ys = { pos.Y, pos.Y + 0.001f, pos.Y - 0.01f, pos.Y + size.Y, pos.Y + size.Y - 0.001f, pos.Y + size.Y + 0.01f, pos.Y + size.Y - 0.5f };
        float x = rng.NextInt(0, 2) == 0 ? xs[rng.NextInt(0, xs.Length)] : pos.X + rng.NextFloat() * size.X;
        float y = rng.NextInt(0, 2) == 0 ? ys[rng.NextInt(0, ys.Length)] : pos.Y + rng.NextFloat() * size.Y;
        return new Vector2(x, y);
    }

    [Theory]
    [InlineData(5UL, 128, 128, 1000)]
    [InlineData(9UL, 192, 96, 400)]
    [InlineData(13UL, 96, 160, 300)]
    public void MinimapDrivenSim_HashesEqualToABareTwin_EveryTick(ulong seed, int w, int h, int perPlayer)
    {
        var a = new Simulation(Config(seed, w, h));
        var b = new Simulation(Config(seed, w, h));
        World world = a.World;
        UnitStore u = world.Units;
        var raster = new MinimapRaster(world.Heightmap, world.NavGrid, Colors, u.Capacity);
        var fit = new MinimapTransform(new Vector2(220, 220), new Vector2(w, h) * Cs);
        var selection = new SelectionSet(u.Capacity);
        var rng = new SimRng(seed ^ 0x5EED, RngStream.Combat); // the test's own stream
        foreach (Command c in StartArmies(a, perPlayer)) { a.Enqueue(c); b.Enqueue(c); }

        int orders = 0, rejectedPixels = 0, dropped = 0, edgeOrders = 0, lastRefresh = -4;
        float maxX = w * Cs, maxY = h * Cs;
        for (int frame = 0; a.TickNumber < 1200; frame++)
        {
            ulong before = a.StateHash();
            if (a.TickNumber - lastRefresh >= Minimap4)
            {
                int drawn = raster.DrawDots(u.Alive, u.Position, u.Owner);
                Assert.Equal(u.Count, drawn);
                lastRefresh = a.TickNumber;
            }
            _ = fit.ToPixel(fit.ClampToMap(MinimapTransform.RayToGround(new Vector3(maxX / 2, 40, maxY / 2 + 28), new Vector3(0.4f, -1f, -0.7f), 0f)));
            selection.Prune(u.Alive, u.Generation);

            if (frame % 3 == 0)
            {
                // Reselect a random handful (up to 1,000) of own units every so often, like box selections.
                if (frame % 21 == 0)
                {
                    selection.Clear();
                    int want = rng.NextInt(1, 1001);
                    int start = rng.NextInt(0, u.Capacity);
                    for (int k = 0; k < u.Capacity && selection.Count < want; k++)
                    {
                        int s = (start + k) % u.Capacity;
                        if (u.Alive[s] && u.Owner[s] == 0) selection.Add(new EntityHandle(s, u.Generation[s]));
                    }
                }
                Vector2 px = EdgePixel(fit, rng);
                bool onMap = fit.TryToMap(px, out Vector2 target);
                Assert.Equal(before, a.StateHash()); // raster, transform and selection reads changed nothing
                if (!onMap) { rejectedPixels++; }
                else
                {
                    Assert.True(target.X >= 0 && target.Y >= 0 && target.X <= maxX && target.Y <= maxY, $"{px} -> {target}");
                    if (target.X == maxX || target.Y == maxY || target.X == 0 || target.Y == 0) edgeOrders++;
                    // SelectionController.OrderMoveTo's rule: never half an order.
                    if (a.PendingCommandCount + selection.Count > a.World.Config.CommandCapacity) dropped++;
                    else
                    {
                        foreach (EntityHandle hnd in selection.Items)
                        {
                            Command m = Command.Move(0, hnd, target);
                            a.Enqueue(m);
                            b.Enqueue(m);
                        }
                        if (selection.Count > 0) orders++;
                    }
                }
            }
            else Assert.Equal(before, a.StateHash());

            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"minimap-driven sim diverged from its bare twin at tick {a.TickNumber}");
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 p = u.Position[i];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && p.X >= 0 && p.Y >= 0 && p.X < maxX && p.Y < maxY,
                    $"tick {a.TickNumber}: slot {i} at {p} outside the {maxX} x {maxY} m map");
            }
        }
        _out.WriteLine($"seed {seed} {w}x{h}, {perPlayer}/player: {a.TickNumber} ticks, {orders} minimap orders ({edgeOrders} to an exact map edge), {rejectedPixels} off-map pixels ignored, {dropped} dropped for overflow, hash {a.StateHash():X16}");
        Assert.True(orders >= 100, $"only {orders} minimap orders");
        Assert.True(edgeOrders > 0 && rejectedPixels > 0);
        Assert.Equal(2 * perPlayer, u.Count);
    }

    private const int Minimap4 = 4; // Minimap.RefreshTicks (game assembly, not referenced here)

    [Theory]
    [InlineData(128, 128)]
    [InlineData(192, 96)]
    [InlineData(48, 160)]
    public void OrdersToTheExactMapCorners_KeepUnitsOnTheMap_AndSettleOnPassableCells(int w, int h)
    {
        var sim = new Simulation(Config(31, w, h));
        UnitStore u = sim.World.Units;
        NavGrid grid = sim.World.NavGrid;
        foreach (Command c in StartArmies(sim, 160)) sim.Enqueue(c);
        sim.Tick();
        sim.Tick(); // commands apply on the tick after they are enqueued
        Assert.Equal(320, u.Count);
        var fit = new MinimapTransform(new Vector2(220, 220), new Vector2(w, h) * Cs);
        (Vector2 pos, Vector2 size) = fit.MapRect;
        Vector2[] corners = { pos, pos + new Vector2(size.X, 0), pos + size, pos + new Vector2(0, size.Y) };
        int k = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Assert.True(fit.TryToMap(corners[k % 4], out Vector2 target), $"corner pixel {corners[k % 4]} rejected");
            sim.Enqueue(Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), target));
            k++;
        }
        float maxX = w * Cs, maxY = h * Cs;
        for (int t = 0; t < 1500; t++)
        {
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 p = u.Position[i];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && p.X >= 0 && p.Y >= 0 && p.X < maxX && p.Y < maxY, $"tick {sim.TickNumber}: slot {i} at {p}");
            }
        }
        int idleOnBlocked = 0, idle = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.State[i] != UnitState.Idle) continue;
            idle++;
            Assert.True(grid.WorldToCell(u.Position[i], out int cx, out int cy));
            if (!grid.IsPassable(cx, cy)) idleOnBlocked++;
        }
        _out.WriteLine($"{w}x{h}: {k} units ordered to the 4 exact corners; after 1500 ticks {idle} idle, {idleOnBlocked} idle on a blocked cell");
        Assert.Equal(0, idleOnBlocked);
    }

    public static IEnumerable<object[]> Fits()
    {
        foreach ((float cw, float ch) in new[] { (220f, 220f), (160f, 48f), (48f, 160f), (221f, 37f), (1f, 1f) })
            foreach ((int mw, int mh) in new[] { (128, 128), (160, 48), (48, 160), (256, 256), (4, 4), (1024, 16) })
                yield return new object[] { cw, ch, mw, mh };
    }

    [Theory]
    [MemberData(nameof(Fits))]
    public void PixelEdges_InsideAccepted_OutsideRejected_AndResultsStayOnTheMap(float cw, float ch, int mw, int mh)
    {
        var mapM = new Vector2(mw, mh) * Cs;
        var fit = new MinimapTransform(new Vector2(cw, ch), mapM);
        (Vector2 pos, Vector2 size) = fit.MapRect;
        // The map fills one axis exactly and fits inside the other, centred.
        Assert.True(MathF.Abs(size.X - cw) < 1e-3f || MathF.Abs(size.Y - ch) < 1e-3f, $"map rect {size} fills neither axis of {cw}x{ch}");
        Assert.True(pos.X >= -1e-3f && pos.Y >= -1e-3f && pos.X + size.X <= cw + 1e-3f && pos.Y + size.Y <= ch + 1e-3f, $"map rect {pos} {size} leaves the {cw}x{ch} control");
        Assert.True(MathF.Abs(pos.X * 2 + size.X - cw) < 1e-3f && MathF.Abs(pos.Y * 2 + size.Y - ch) < 1e-3f, "map rect not centred");

        // Every integer control pixel's centre, plus the exact rect borders and a hair either side.
        int bad = 0;
        for (int py = -1; py <= (int)ch; py++)
        {
            for (int pxi = -1; pxi <= (int)cw; pxi++)
            {
                var p = new Vector2(pxi + 0.5f, py + 0.5f);
                bool inside = p.X >= pos.X && p.Y >= pos.Y && p.X <= pos.X + size.X && p.Y <= pos.Y + size.Y;
                bool ok = fit.TryToMap(p, out Vector2 m);
                if (ok)
                {
                    if (!(m.X >= 0 && m.Y >= 0 && m.X <= mapM.X && m.Y <= mapM.Y)) bad++;
                    if (Vector2.Distance(fit.ToPixel(m), p) > 1e-3f) bad++;
                }
                // Within 1e-4 px of the border either answer is fine (float rounding).
                bool nearBorder = MathF.Abs(p.X - pos.X) < 1e-4f || MathF.Abs(p.X - pos.X - size.X) < 1e-4f
                    || MathF.Abs(p.Y - pos.Y) < 1e-4f || MathF.Abs(p.Y - pos.Y - size.Y) < 1e-4f;
                if (ok != inside && !nearBorder) bad++;
            }
        }
        Assert.Equal(0, bad);
        // Corners a hair (1e-4 px) inside; the exact far border can round either way in float.
        Vector2 hair = new(1e-4f * MathF.Max(1f, size.X / 100f), 1e-4f * MathF.Max(1f, size.Y / 100f));
        foreach (Vector2 edge in new[] { pos + hair, pos + size - hair, new Vector2(pos.X + size.X - hair.X, pos.Y + hair.Y), new Vector2(pos.X + hair.X, pos.Y + size.Y - hair.Y) })
        {
            Assert.True(fit.TryToMap(edge, out Vector2 m), $"corner {edge} (a hair inside) rejected");
            Assert.True(m.X >= 0 && m.Y >= 0 && m.X <= mapM.X && m.Y <= mapM.Y, $"corner {edge} -> {m}");
        }
        float eps = MathF.Max(1e-3f, size.X * 1e-5f);
        Assert.False(fit.TryToMap(pos - new Vector2(eps, 0), out _), "a hair left of the map accepted");
        Assert.False(fit.TryToMap(pos - new Vector2(0, eps), out _), "a hair above the map accepted");
        Assert.False(fit.TryToMap(pos + size + new Vector2(eps, 0), out _), "a hair right of the map accepted");
        Assert.False(fit.TryToMap(pos + size + new Vector2(0, eps), out _), "a hair below the map accepted");
    }

    [Fact]
    public void ADotFollowsItsSlot_ThroughDeathAndRespawnAsTheOtherFaction()
    {
        var sim = new Simulation(TestSim.Config(3, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 64));
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid grid = w.NavGrid;
        var raster = new MinimapRaster(w.Heightmap, grid, Colors, u.Capacity);
        Vector2 p = grid.CellCenter(40, 40), q = grid.CellCenter(80, 70);
        Assert.True(grid.IsPassable(40, 40) && grid.IsPassable(80, 70), "pick other cells for this seed");
        sim.Enqueue(Command.SpawnUnit(0, w.Data.Factions[0].Units[0], p));
        sim.Enqueue(Command.SpawnUnit(1, w.Data.Factions[1].Units[0], p)); // an enemy on the same cell, later slot
        sim.Tick();
        sim.Tick();
        Assert.Equal(2, u.Count);
        u.Position[0] = u.Position[1] = p; // pin both onto one cell (separation may have nudged them)
        raster.DrawDots(u.Alive, u.Position, u.Owner);
        Assert.Equal(Colors[1], Rgb(raster, p)); // later slot wins

        // The later slot dies: the earlier unit's colour must show again, not a hole.
        u.Free(new EntityHandle(1, u.Generation[1]));
        Assert.Equal(1, raster.DrawDots(u.Alive, u.Position, u.Owner));
        Assert.Equal(Colors[0], Rgb(raster, p));

        // Slot 0 dies: no dots at all.
        u.Free(new EntityHandle(0, u.Generation[0]));
        Assert.Equal(0, raster.DrawDots(u.Alive, u.Position, u.Owner));
        Assert.Equal(0, raster.Dots[Index(raster, p) + 3]);

        // Slot 0 comes back (LIFO free list) as player 1 elsewhere: dot at q in player 1's colour, nothing at p.
        sim.Enqueue(Command.SpawnUnit(1, w.Data.Factions[1].Units[0], q));
        sim.Tick();
        sim.Tick();
        u.Position[0] = q;
        Assert.True(u.Alive[0] && u.Owner[0] == 1, "slot 0 was not the one reused");
        Assert.Equal(1, raster.DrawDots(u.Alive, u.Position, u.Owner));
        Assert.Equal(0, raster.Dots[Index(raster, p) + 3]);
        Assert.Equal(Colors[1], Rgb(raster, q));
        int opaque = 0;
        for (int i = 3; i < raster.Dots.Length; i += 4) if (raster.Dots[i] != 0) opaque++;
        Assert.Equal(MinimapRaster.DotCells, opaque); // one dot: its cell and its rim (BUG-0064), nothing left at p
    }

    [Fact]
    public void DotsOutsideTheRasterOrWithBadOwners_DoNotThrowOrBleed()
    {
        Heightmap map = Rts.Sim.Tests.ViewApi.TerrainHeightTests.GeneratedMap(2);
        var raster = new MinimapRaster(map, new NavGrid(map), Colors, 4);
        bool[] alive = { true, true, true, true, true, true };
        Vector2[] pos = { new(-1e30f, 5), new(float.NaN, 1), new(1e30f, 1e30f), new(map.Width * Cs, map.Height * Cs), new(3, 3), new(5, 5) };
        int[] owner = { 0, 0, 1, -1, 2, 0 };
        // Six slots but room for four: the extra two are never drawn, nothing throws.
        int n = raster.DrawDots(alive, pos, owner);
        Assert.Equal(2, n); // slot 0 clamped to column 0, slot 2 to the far corner; NaN, owner -1 skipped
        Assert.Equal(255, raster.Dots[(2 * map.Width + 0) * 4 + 3]); // slot 0 at (0, 2)
        Assert.Equal(255, raster.Dots[((map.Height - 1) * map.Width + map.Width - 1) * 4 + 3]);
    }

    /// <summary>Wall-clock tests; they run alone in <see cref="SerialCollection"/>.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Fact]
        [Trait("Category", "Perf")]
        public void DotRefresh_2000Units_On256Map_UnderABudget()
        {
            var rng0 = new SimRng(4, RngStream.MapGen);
            Heightmap map = MapGenerator.Generate(MapGenParams.Default with { Width = 256, Height = 256 }, ref rng0);
            var swBake = Stopwatch.StartNew();
            var raster = new MinimapRaster(map, new NavGrid(map), Colors, 2000);
            swBake.Stop();
            var alive = new bool[2000];
            var pos = new Vector2[2000];
            var owner = new int[2000];
            var rng = new SimRng(8, RngStream.Combat);
            for (int i = 0; i < 2000; i++)
            {
                alive[i] = true;
                pos[i] = new Vector2(rng.NextFloat() * 512f, rng.NextFloat() * 512f);
                owner[i] = i & 1;
            }
            for (int i = 0; i < 20; i++) raster.DrawDots(alive, pos, owner);
            double worst = 0, total = 0;
            const int runs = 300;
            var sw = new Stopwatch();
            for (int r = 0; r < runs; r++)
            {
                for (int i = 0; i < 2000; i += 97) pos[i] = new Vector2(rng.NextFloat() * 512f, rng.NextFloat() * 512f);
                sw.Restart();
                raster.DrawDots(alive, pos, owner);
                sw.Stop();
                total += sw.Elapsed.TotalMilliseconds;
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            }
            _out.WriteLine($"256x256 bake {swBake.Elapsed.TotalMilliseconds:F2} ms; DrawDots 2,000 units avg {total / runs:F4} ms, worst {worst:F4} ms");
            Assert.True(total / runs <= 0.25, $"DrawDots avg {total / runs:F4} ms, over half the 0.5 ms refresh budget");
        }
    }

    private static int Index(MinimapRaster r, Vector2 p)
    {
        Assert.True(r.TryPixelOf(p, out int x, out int y));
        return (y * r.Width + x) * 4;
    }

    private static uint Rgb(MinimapRaster r, Vector2 p)
    {
        int i = Index(r, p);
        Assert.Equal(255, r.Dots[i + 3]);
        return (uint)(r.Dots[i] << 16 | r.Dots[i + 1] << 8 | r.Dots[i + 2]);
    }
}
