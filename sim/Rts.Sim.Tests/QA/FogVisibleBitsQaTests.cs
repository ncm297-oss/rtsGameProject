using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-H1 (session 2026-10-08-2144), BUG-0215's fix: the fog's packed visible bits are hashed state and the byte map is
/// derived from them. On generated three-level maps with two fighting armies attack-moved at random (6 seeds): the packed
/// visible and explored bits agree with the byte map on every cell after every tick; a copy of only the hashed fog state
/// (the byte map rebuilt from the bits, the stamp boxes and versions re-derived two ways) taken between ticks, update tick
/// next or not, resumes to the same hash on every later tick; and every flipped visible bit moves the hash. Plus the
/// <c>AddToHash</c> cost on a 1024 map.
/// </summary>
[Collection(SerialCollection.Name)]
public class FogVisibleBitsQaTests
{
    private const int PerSide = 40;
    private readonly ITestOutputHelper _out;

    public FogVisibleBitsQaTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Army0 = { "malazan_heavy_infantry", "malazan_crossbowman", "malazan_wickan_lancer", "malazan_catapult" };
    private static readonly string[] Army1 = { "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_horse_raider", "whirlwind_zealot" };

    private static T Field<T>(FogStore fog, string name) =>
        (T)typeof(FogStore).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fog)!;

    private static List<int> Passable(World w)
    {
        var cells = new List<int>();
        NavGrid g = w.NavGrid;
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) cells.Add(c);
        return cells;
    }

    private static Simulation NewSim(ulong seed)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 2 * PerSide, CommandCapacity: 4 * PerSide + 16));
        List<int> cells = Passable(sim.World);
        NavGrid g = sim.World.NavGrid;
        var rng = new SimRng(seed, 31);
        for (int k = 0; k < PerSide; k++)
            for (int side = 0; side < 2; side++)
            {
                int c = cells[rng.NextInt(0, cells.Count)];
                int type = TestSim.Data.FindUnit((side == 0 ? Army0 : Army1)[rng.NextInt(0, 4)]);
                sim.Enqueue(Command.SpawnUnit(side, type, g.CellCenter(c % g.Width, c / g.Width)));
            }
        return sim;
    }

    private static void Orders(Simulation sim, List<int> cells, ref SimRng rng)
    {
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            int c = cells[rng.NextInt(0, cells.Count)];
            sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), g.CellCenter(c % g.Width, c / g.Width)));
        }
    }

    /// <summary>Packed bits == byte map on every cell, both players; the public reads agree; visible implies explored.</summary>
    private static void AssertBitsMatchBytes(World w, string context)
    {
        FogStore fog = w.Fog;
        ulong[][] visible = Field<ulong[][]>(fog, "_visible");
        ulong[][] explored = Field<ulong[][]>(fog, "_explored");
        for (int p = 0; p < 2; p++)
        {
            ReadOnlySpan<byte> bytes = fog.Visibility(p);
            for (int c = 0; c < bytes.Length; c++)
            {
                bool vb = (visible[p][c >> 6] >> (c & 63) & 1) != 0, eb = (explored[p][c >> 6] >> (c & 63) & 1) != 0;
                byte b = bytes[c];
                if (b > VisionConstants.Visible || vb != (b == VisionConstants.Visible) || eb != (b != VisionConstants.Unexplored) || (vb && !eb)
                    || fog.IsVisible(p, c) != vb || fog.IsExplored(p, c) != eb)
                    Assert.Fail($"{context}: player {p} cell {c}: byte {b}, visible bit {vb}, explored bit {eb}");
            }
            // Bits past the last cell stay clear (they are hashed).
            int n = bytes.Length;
            if (n % 64 != 0)
            {
                Assert.Equal(0UL, visible[p][^1] >> (n % 64));
                Assert.Equal(0UL, explored[p][^1] >> (n % 64));
            }
        }
    }

    /// <summary>
    /// What a save/load would do with only the hashed fog state: the byte map rebuilt from the packed bits (stamp marks
    /// dropped), every version zeroed, and each player's last-update box re-derived: the tight box of its visible bits
    /// (<paramref name="tight"/>) or the whole map.
    /// </summary>
    private static void RederiveFog(World w, bool tight)
    {
        FogStore fog = w.Fog;
        ulong[][] visible = Field<ulong[][]>(fog, "_visible");
        ulong[][] explored = Field<ulong[][]>(fog, "_explored");
        byte[][] bytes = Field<byte[][]>(fog, "_visibility");
        int[] version = Field<int[]>(fog, "_version");
        int[] box = Field<int[]>(fog, "_visibleBox");
        int width = fog.Width;
        for (int p = 0; p < bytes.Length; p++)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int c = 0; c < bytes[p].Length; c++)
            {
                bool vb = (visible[p][c >> 6] >> (c & 63) & 1) != 0, eb = (explored[p][c >> 6] >> (c & 63) & 1) != 0;
                bytes[p][c] = vb ? VisionConstants.Visible : eb ? VisionConstants.Explored : VisionConstants.Unexplored;
                if (!vb) continue;
                int x = c % width, y = c / width;
                x0 = Math.Min(x0, x);
                y0 = Math.Min(y0, y);
                x1 = Math.Max(x1, x);
                y1 = Math.Max(y1, y);
            }
            version[p] = 0;
            if (!tight)
            {
                (box[4 * p], box[4 * p + 1], box[4 * p + 2], box[4 * p + 3]) = (0, 0, width - 1, fog.Height - 1);
            }
            else if (x1 < 0)
            {
                (box[4 * p], box[4 * p + 1], box[4 * p + 2], box[4 * p + 3]) = (int.MaxValue, 0, 0, 0);
            }
            else
            {
                (box[4 * p], box[4 * p + 1], box[4 * p + 2], box[4 * p + 3]) = (x0, y0, x1, y1);
            }
        }
    }

    /// <summary>
    /// Six seeds, 900 ticks of two 40-unit armies attack-moved across a generated three-level map every 100 ticks: after
    /// every tick the packed bits match the byte map on every cell. Three twins run the same commands; between ticks, at
    /// six points (three with an update tick next, three without), twin b's fog is re-derived from its hashed bits with the
    /// tight box and twin c's with the whole-map box. All three hash equal before and after, and on every later tick.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(5UL)]
    [InlineData(8UL)]
    [InlineData(13UL)]
    public void VisibleBitsMatchTheByteMap_AndARederivedFogResumesTheSameFuture(ulong seed)
    {
        Simulation a = NewSim(seed), b = NewSim(seed), c = NewSim(seed);
        List<int> cells = Passable(a.World);
        SimRng ra = new(seed, 32), rb = new(seed, 32), rc = new(seed, 32);
        var pick = new SimRng(seed, 33);
        var cuts = new HashSet<int>();
        int updateNext = 0, nonUpdateNext = 0;
        while (updateNext + nonUpdateNext < 6)
        {
            int t = pick.NextInt(20, 880);
            bool upd = VisionSystem.IsUpdateTick(t);
            if (cuts.Contains(t) || (upd ? updateNext >= 3 : nonUpdateNext >= 3)) continue;
            cuts.Add(t);
            if (upd) updateNext++;
            else nonUpdateNext++;
        }
        int maxVisible = 0;
        for (int t = 0; t < 900; t++)
        {
            if (cuts.Contains(a.TickNumber))
            {
                ulong h = a.StateHash();
                RederiveFog(b.World, tight: true);
                RederiveFog(c.World, tight: false);
                Assert.True(h == b.StateHash() && h == c.StateHash(), $"seed {seed}: re-deriving the fog changed the hash at tick {a.TickNumber}");
            }
            if (t % 100 == 2)
            {
                Orders(a, cells, ref ra);
                Orders(b, cells, ref rb);
                Orders(c, cells, ref rc);
            }
            a.Tick();
            b.Tick();
            c.Tick();
            ulong ha = a.StateHash();
            Assert.True(ha == b.StateHash(), $"seed {seed}: the tight-box copy differs after tick {t}");
            Assert.True(ha == c.StateHash(), $"seed {seed}: the whole-map-box copy differs after tick {t}");
            AssertBitsMatchBytes(a.World, $"seed {seed} tick {t} (a)");
            AssertBitsMatchBytes(b.World, $"seed {seed} tick {t} (b)");
            int vis = 0;
            ReadOnlySpan<byte> v0 = a.World.Fog.Visibility(0);
            for (int k = 0; k < v0.Length; k++) if (v0[k] == VisionConstants.Visible) vis++;
            maxVisible = Math.Max(maxVisible, vis);
        }
        World w = a.World;
        _out.WriteLine($"seed {seed}: {w.Fog.Width}x{w.Fog.Height}, multi-level {w.Fog.MultiLevel}, kills {w.Kills[0]}/{w.Kills[1]}, cuts at {string.Join(",", cuts.OrderBy(x => x))}, most visible cells (p0) {maxVisible}");
        Assert.True(w.Fog.MultiLevel, "setup: not a multi-level map");
        Assert.True(w.Kills[0] + w.Kills[1] > 0, "setup: nobody died");
    }

    /// <summary>On a fought-over three-level state, 400 random visible bits (both players, any cell): each one flipped moves the hash, flipped back restores it; and the same for explored bits.</summary>
    [Fact]
    public void EveryRandomVisibleOrExploredBitFlip_MovesTheHash()
    {
        Simulation a = NewSim(4);
        List<int> cells = Passable(a.World);
        var r = new SimRng(4, 32);
        for (int t = 0; t < 302; t++)
        {
            if (t % 100 == 2) Orders(a, cells, ref r);
            a.Tick();
        }
        Assert.False(VisionSystem.IsUpdateTick(a.TickNumber));
        ulong h0 = a.StateHash();
        ulong[][] visible = Field<ulong[][]>(a.World.Fog, "_visible");
        ulong[][] explored = Field<ulong[][]>(a.World.Fog, "_explored");
        int n = a.World.Fog.Width * a.World.Fog.Height;
        var rng = new SimRng(4, 99);
        for (int k = 0; k < 400; k++)
        {
            ulong[][] set = k % 2 == 0 ? visible : explored;
            int p = rng.NextInt(0, 2), c = rng.NextInt(0, n);
            set[p][c >> 6] ^= 1UL << (c & 63);
            Assert.True(a.StateHash() != h0, $"flip {k}: player {p} cell {c} ({(k % 2 == 0 ? "visible" : "explored")}) left the hash");
            set[p][c >> 6] ^= 1UL << (c & 63);
            Assert.Equal(h0, a.StateHash());
        }
    }

    /// <summary>
    /// The cost of hashing the fog on the largest map (1024 x 1024; 2 and 8 players): every player's explored and visible
    /// bits, 16,384 words each. Reported; the bound is generous (a replay checks a hash every 100 ticks, the seed-21 row
    /// every tick on a smaller map).
    /// </summary>
    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(2)]
    [InlineData(8)]
    public void FogAddToHash_On1024Map_Cost(int players)
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: players, UnitCapacity: 64, CommandCapacity: 64)
            with { Map = MapGenParams.Default with { Width = 1024, Height = 1024 } });
        sim.Tick();
        for (int k = 0; k < 5; k++) sim.StateHash();
        var sw = Stopwatch.StartNew();
        const int reps = 50;
        for (int k = 0; k < reps; k++) sim.StateHash();
        double whole = sw.Elapsed.TotalMilliseconds / reps;
        long before = GC.GetAllocatedBytesForCurrentThread();
        sim.StateHash();
        long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        _out.WriteLine($"1024 x 1024, {players} players: StateHash {whole:F3} ms (fog bits {2 * players * 1024 * 1024 / 64:N0} words), {alloc} B allocated");
        Assert.Equal(0, alloc);
        Assert.True(whole < 5.0 * players / 2, $"StateHash on a 1024 map with {players} players took {whole:F3} ms");
    }
}
