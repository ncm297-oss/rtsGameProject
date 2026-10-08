using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M3-H2, session 2026-10-07-1715): the BUG-0112 fixture helper, plateau ids on maps with many same-level islands,
/// push-out of hundreds of units onto a tiny plateau, and the never-seal answer memo (BUG-0096) against a fresh flood
/// after every kind of grid change, including mid-tick cancels and a tree felled by a real worker.
/// </summary>
[Collection(SerialCollection.Name)]
public class PlateauSealMemoQaTests
{
    private readonly ITestOutputHelper _out;

    public PlateauSealMemoQaTests(ITestOutputHelper output) => _out = output;

    // ------------------------------------------------------------------ BUG-0112 helper

    /// <summary>
    /// The BUG-0112 helper clears building <c>requires</c> and nothing else: same ids, every unit's and tech's
    /// requirements unchanged, every building's empty, and the shipped data does have some (or the helper proves nothing).
    /// </summary>
    [Fact]
    public void DataWithoutBuildingRequires_ClearsEveryBuildingsRequires_AndNothingElse()
    {
        GameData a = TestSim.Data, b = TestSim.DataWithoutBuildingRequires;
        Assert.Equal(a.Buildings.Length, b.Buildings.Length);
        Assert.Equal(a.Units.Length, b.Units.Length);
        Assert.Equal(a.Techs.Length, b.Techs.Length);
        int gated = 0;
        for (int k = 0; k < a.Buildings.Length; k++)
        {
            Assert.Equal(a.Buildings[k].Id, b.Buildings[k].Id);
            Assert.Equal(a.Buildings[k].Faction, b.Buildings[k].Faction);
            Assert.Empty(b.Buildings[k].RequiresTechs);
            Assert.Empty(b.Buildings[k].RequiresBuildings);
            if (a.Buildings[k].RequiresTechs.Length + a.Buildings[k].RequiresBuildings.Length > 0) gated++;
        }
        Assert.True(gated >= 2, $"only {gated} shipped buildings have requires: the helper would prove nothing");
        for (int k = 0; k < a.Units.Length; k++)
        {
            Assert.Equal(a.Units[k].Id, b.Units[k].Id);
            Assert.Equal(a.Units[k].RequiresTechs, b.Units[k].RequiresTechs);
            Assert.Equal(a.Units[k].RequiresBuildings, b.Units[k].RequiresBuildings);
        }
        for (int k = 0; k < a.Techs.Length; k++)
        {
            Assert.Equal(a.Techs[k].Id, b.Techs[k].Id);
            Assert.Equal(a.Techs[k].RequiresTechs, b.Techs[k].RequiresTechs);
            Assert.Equal(a.Techs[k].RequiresBuildings, b.Techs[k].RequiresBuildings);
            Assert.Equal(a.Techs[k].RequiresAnyOfCount, b.Techs[k].RequiresAnyOfCount);
        }
    }

    // ------------------------------------------------------------------ plateaus

    /// <summary>A size x size level-0 map with 5 x 5 level-1 islands at (ox + 30 i, oy + 30 j), each with a ramp on its west side.</summary>
    private static Heightmap Islands(int size, int perSide)
    {
        var rows = new char[size][];
        for (int y = 0; y < size; y++) rows[y] = Enumerable.Repeat('0', size).ToArray();
        for (int i = 0; i < perSide; i++)
        {
            for (int j = 0; j < perSide; j++)
            {
                int x0 = 20 + 30 * i, y0 = 20 + 30 * j;
                for (int y = y0; y <= y0 + 4; y++) for (int x = x0; x <= x0 + 4; x++) rows[y][x] = '1';
                rows[y0 + 2][x0 - 1] = 'r';
            }
        }
        return FromRows(rows.Select(r => new string(r)).ToArray());
    }

    /// <summary>
    /// 36 same-level islands (level 1) on a 256 map, each 10 cells (3 x 3 plus the ramp-top cell): each is its own plateau
    /// with a box inside the island, and a House on each of six of them pushes 30 own units off, every one onto its own
    /// island, as evenly as the pigeonhole allows. Before BUG-0097's fix the walk took every level-1 cell.
    /// </summary>
    [Fact]
    public void ThirtySixSameLevelIslands_256Map_EachIsAPlateau_PushOutStaysOnItsIsland()
    {
        const int per = 7; // 7 x 7 = 49 islands at 20..200
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 256, CommandCapacity: 512), Islands(256, per));
        World w = sim.World;
        NavGrid g = w.NavGrid;
        Assert.Equal(per * per + 1, w.Plateaus.Count);
        var ids = new HashSet<int>();
        for (int i = 0; i < per; i++)
        {
            for (int j = 0; j < per; j++)
            {
                int x0 = 20 + 30 * i, y0 = 20 + 30 * j, p = w.Plateaus.At(Cell(sim, x0 + 2, y0 + 2));
                Assert.True(p >= 0 && ids.Add(p), $"island ({i}, {j}) plateau {p}");
                Assert.True(w.Plateaus.Bounds(p, out int minX, out int minY, out int maxX, out int maxY));
                Assert.True(minX >= x0 && maxX <= x0 + 4 && minY >= y0 && maxY <= y0 + 4, $"island ({i}, {j}) box ({minX}, {minY})-({maxX}, {maxY})");
                Assert.Equal(w.Plateaus.At(Cell(sim, 5, 5)), w.Plateaus.At(Cell(sim, x0 - 1, y0 + 2))); // its ramp: the ground
            }
        }
        for (int k = 0; k < 31; k++) sim.Enqueue(Command.SpawnUnit(0, Laborer, g.CellCenter(4 + k, 4)));
        Run(sim, 2);
        SetTotals(sim, 0, 100_000, 100_000);
        UnitStore u = w.Units;
        const float cs = MapConstants.CellSize;
        foreach ((int i, int j) in new[] { (0, 0), (3, 3), (6, 6), (6, 0), (0, 6), (2, 5) })
        {
            int x0 = 20 + 30 * i + 2, y0 = 20 + 30 * j + 2, p = w.Plateaus.At(Cell(sim, x0, y0));
            for (int k = 0; k < 30; k++) u.Position[k] = u.PrevPosition[k] = new Vector2(x0 * cs + 0.1f + 0.3f * (k % 6), y0 * cs + 0.1f + 0.3f * (k / 6));
            Assert.True(ConstructionSystem.StartBuild(w, 30, House, At(sim, x0, y0), replaceQueue: true), $"House on island ({i}, {j})");
            var perCell = new Dictionary<int, int>();
            for (int k = 0; k < 30; k++)
            {
                Assert.True(g.WorldToCell(u.Position[k], out int x, out int y));
                Assert.True(g.IsPassable(x, y) && w.Plateaus.At(y * g.Width + x) == p, $"island ({i}, {j}): unit {k} at ({x}, {y}) left it");
                perCell[y * g.Width + x] = perCell.GetValueOrDefault(y * g.Width + x) + 1;
            }
            Assert.Equal(6, perCell.Count); // 10 cells less the House's 4
            Assert.True(perCell.Values.Max() - perCell.Values.Min() <= 1, $"island ({i}, {j}): {string.Join(",", perCell.Values)}");
        }
    }

    /// <summary>
    /// Generated 256 maps with many level-1 plateaus: the plateau partition equals an independent flood's (connected,
    /// passable, same-level, 4-adjacent), and some seed has 10 or more separate level-1 plateaus.
    /// </summary>
    [Fact]
    public void GeneratedMaps256_ManyLevel1Plateaus_PartitionMatchesAnIndependentFlood()
    {
        int best = 0, seedsWithTen = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 16) with
            {
                Map = MapGenParams.Default with { Width = 256, Height = 256, Level1Plateaus = 16, Level1MinSize = 12, Level1MaxSize = 30, Level2Plateaus = 6 },
            });
            World w = sim.World;
            NavGrid g = w.NavGrid;
            int n = g.Width * g.Height;
            var mine = new int[n];
            Array.Fill(mine, -1);
            var queue = new int[n];
            int labels = 0, level1 = 0;
            var theirsOf = new List<int>();
            var mineOf = new Dictionary<int, int>();
            for (int s = 0; s < n; s++)
            {
                if (mine[s] >= 0 || !g.IsPassable(s % g.Width, s / g.Width)) continue;
                int lv = g.LevelAt(s % g.Width, s / g.Width), head = 0, tail = 0;
                if (lv == 1) level1++;
                mine[s] = labels;
                queue[tail++] = s;
                while (head < tail)
                {
                    int c = queue[head++];
                    foreach (int d in new[] { c - 1, c + 1, c - g.Width, c + g.Width })
                    {
                        int dx = d % g.Width, dy = d / g.Width;
                        if (mine[d] >= 0 || !g.IsPassable(dx, dy) || g.LevelAt(dx, dy) != lv) continue;
                        mine[d] = labels;
                        queue[tail++] = d;
                    }
                }
                theirsOf.Add(w.Plateaus.At(s));
                labels++;
            }
            Assert.Equal(labels, w.Plateaus.Count);
            for (int c = 0; c < n; c++)
            {
                int theirs = w.Plateaus.At(c);
                if (mine[c] < 0) { Assert.Equal(-1, theirs); continue; }
                Assert.Equal(theirsOf[mine[c]], theirs);
                if (mineOf.TryGetValue(theirs, out int m)) Assert.Equal(m, mine[c]);
                else mineOf[theirs] = mine[c];
            }
            _out.WriteLine($"seed {seed}: {labels} plateaus, {level1} of level 1");
            best = Math.Max(best, level1);
            if (level1 >= 10) seedsWithTen++;
        }
        Assert.True(seedsWithTen > 0, $"no seed with 10+ level-1 plateaus (best {best})");
    }

    // ------------------------------------------------------------------ push-out: 400 on a 3 x 3 plateau

    /// <summary>A 64 x 64 map with one 5 x 5 level-1 block at (20-24, 20-24) and a ramp west at y 22: a 10-cell plateau (3 x 3 and the ramp-top cell).</summary>
    private static (Simulation Sim, int Worker) FourHundredInAHouseOnATinyPlateau(int count)
    {
        var rows = new string[64];
        for (int y = 0; y < 64; y++)
        {
            var row = new char[64];
            for (int x = 0; x < 64; x++) row[x] = x >= 20 && x <= 24 && y >= 20 && y <= 24 ? '1' : '0';
            if (y == 22) row[19] = 'r';
            rows[y] = new string(row);
        }
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: count + 8, CommandCapacity: count + 64), FromRows(rows));
        NavGrid g = sim.World.NavGrid;
        for (int k = 0; k <= count; k++) sim.Enqueue(Command.SpawnUnit(0, Laborer, g.CellCenter(2 + k % 60, 30 + k / 60)));
        Run(sim, 2);
        UnitStore u = sim.World.Units;
        const float cs = MapConstants.CellSize;
        // The House at (22, 22) covers (22-23, 22-23): every unit on its own point inside it.
        for (int k = 0; k < count; k++)
            u.Position[k] = u.PrevPosition[k] = new Vector2(22 * cs + 0.05f + (2 * cs - 0.1f) * (k % 20) / 20f, 22 * cs + 0.05f + (2 * cs - 0.1f) * (k / 20) / 20f);
        SetTotals(sim, 0, 1000, 1000);
        return (sim, count);
    }

    /// <summary>
    /// 400 own units in a House footprint on a 10-cell plateau (6 cells left): all set down on the plateau, as even as the
    /// pigeonhole allows, finite and on passable ground for 10 s afterwards, in well under a tick's budget.
    /// </summary>
    [Fact]
    public void FourHundredPushedOntoATinyPlateau_AllStayOnIt_Evenly_FiniteAfterwards()
    {
        (Simulation warm, int ww) = FourHundredInAHouseOnATinyPlateau(400);
        ConstructionSystem.StartBuild(warm.World, ww, House, At(warm, 22, 22), replaceQueue: true); // JIT
        (Simulation sim, int worker) = FourHundredInAHouseOnATinyPlateau(400);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        int p = w.Plateaus.At(Cell(sim, 22, 22));
        long t0 = Stopwatch.GetTimestamp();
        Assert.True(ConstructionSystem.StartBuild(w, worker, House, At(sim, 22, 22), replaceQueue: true));
        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        var perCell = new Dictionary<int, int>();
        for (int k = 0; k < 400; k++)
        {
            Assert.True(g.WorldToCell(u.Position[k], out int x, out int y));
            Assert.True(g.IsPassable(x, y) && w.Plateaus.At(y * g.Width + x) == p, $"unit {k} at ({x}, {y}) left the plateau");
            perCell[y * g.Width + x] = perCell.GetValueOrDefault(y * g.Width + x) + 1;
        }
        _out.WriteLine($"400 pushed onto {perCell.Count} cells: {string.Join(", ", perCell.Values)}; Build apply {ms:F3} ms");
        Assert.Equal(6, perCell.Count);
        Assert.True(perCell.Values.Max() - perCell.Values.Min() <= 1);
        Assert.True(ms < 5, $"{ms:F3} ms");
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            for (int k = 0; k < u.Capacity; k++)
            {
                if (!u.Alive[k]) continue;
                Assert.True(float.IsFinite(u.Position[k].X) && float.IsFinite(u.Position[k].Y), $"tick {t}: unit {k} at {u.Position[k]}");
                Assert.True(g.WorldToCell(u.Position[k], out int x, out int y) && g.IsPassable(x, y), $"tick {t}: unit {k} at {u.Position[k]} on blocked ground");
            }
        }
    }

    /// <summary>
    /// The dev's report: leftovers "share cells (never points)". With more than 24 leftovers per cell the per-pass offset
    /// (8 directions x 3 radii) repeats, so leftovers of pass k and k + 24 in one cell stand on one point. BUG-0133.
    /// </summary>
    [Fact(Skip = "BUG-0133: past 24 leftovers a cell, the leftover offsets repeat and units share a point")]
    public void FourHundredPushedOntoATinyPlateau_NoTwoOnOnePoint()
    {
        (Simulation sim, int worker) = FourHundredInAHouseOnATinyPlateau(400);
        Assert.True(ConstructionSystem.StartBuild(sim.World, worker, House, At(sim, 22, 22), replaceQueue: true));
        Assert.Equal(0, IdenticalPairs(sim.World.Units, 400));
    }

    /// <summary>Pin for BUG-0133 (today's behaviour): up to 24 per cell no point is shared; at 400 some are. Flip to the skipped test above when fixed.</summary>
    [Fact]
    public void Bug0133Pin_LeftoverOffsetsRepeatAfter24Passes()
    {
        (Simulation few, int fw) = FourHundredInAHouseOnATinyPlateau(6 * 25); // 6 centers + 144 leftovers: 24 passes
        Assert.True(ConstructionSystem.StartBuild(few.World, fw, House, At(few, 22, 22), replaceQueue: true));
        Assert.Equal(0, IdenticalPairs(few.World.Units, 150));
        (Simulation many, int mw) = FourHundredInAHouseOnATinyPlateau(400);
        Assert.True(ConstructionSystem.StartBuild(many.World, mw, House, At(many, 22, 22), replaceQueue: true));
        int same = IdenticalPairs(many.World.Units, 400);
        _out.WriteLine($"400 pushed: {same} identical-position pairs");
        Assert.True(same > 0, "BUG-0133 looks fixed: flip this pin to FourHundredPushedOntoATinyPlateau_NoTwoOnOnePoint");
    }

    private static int IdenticalPairs(UnitStore u, int count)
    {
        var seen = new Dictionary<Vector2, int>();
        int pairs = 0;
        for (int k = 0; k < count; k++)
        {
            int c = seen.GetValueOrDefault(u.Position[k]);
            pairs += c;
            seen[u.Position[k]] = c + 1;
        }
        return pairs;
    }

    /// <summary>The 400-unit push-out is deterministic: two runs, the same hash after the push and after 5 s.</summary>
    [Fact]
    public void FourHundredPushedOntoATinyPlateau_Deterministic()
    {
        (Simulation a, int wa) = FourHundredInAHouseOnATinyPlateau(400);
        (Simulation b, int wb) = FourHundredInAHouseOnATinyPlateau(400);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.True(ConstructionSystem.StartBuild(a.World, wa, House, At(a, 22, 22), replaceQueue: true));
        Assert.True(ConstructionSystem.StartBuild(b.World, wb, House, At(b, 22, 22), replaceQueue: true));
        Assert.Equal(a.StateHash(), b.StateHash());
        for (int t = 0; t < 100; t++)
        {
            a.Tick();
            b.Tick();
            Assert.Equal(a.StateHash(), b.StateHash());
        }
    }

    // ------------------------------------------------------------------ never-seal memo (BUG-0096)

    /// <summary>A 64 x 64 flat map with a tree wall at x = 32 from y = 3 to the bottom border: a House in the top gap seals the map.</summary>
    private static Simulation Wall(int units = 16)
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: units, CommandCapacity: 256) with { ResourceCapacity = 128, BuildingCapacity = 64 }, Flat(64, 64));
        for (int y = 3; y < 63; y++) Spawn(sim.World, Tree, 32, y, TreeWood);
        return sim;
    }

    /// <summary>
    /// Fuzz: random closing and opening changes on and round the wall (trees felled and regrown, House footprints set
    /// and cleared, footprints cleared inside an enclosure so they stay pocket cells, version-only bumps), and after
    /// each the memo's answer at a few hot footprints equals a fresh flood's. 1,500 steps a seed.
    /// </summary>
    [Theory]
    [InlineData(1UL)] [InlineData(2UL)] [InlineData(3UL)] [InlineData(4UL)] [InlineData(5UL)] [InlineData(6UL)]
    public void SealMemo_AfterAnyGridChange_EqualsAFreshFlood(ulong seed)
    {
        Simulation sim = Wall();
        World w = sim.World;
        NavGrid g = w.NavGrid;
        var rng = new SimRng(seed, 77);
        var trees = new HashSet<int>(Enumerable.Range(3, 60).Select(y => y * g.Width + 32));
        var houses = new List<int>();
        var felled = new List<int>();
        (int X, int Y)[] hot = { (32, 1), (31, 1), (33, 1), (31, 2), (30, 40), (33, 40), (20, 20) };
        int memoHits = 0, checks = 0, flips = 0;
        bool? last = null;
        for (int step = 0; step < 1500; step++)
        {
            int op = rng.NextInt(0, 8);
            if (op == 0)
            {
                // Fell a wall tree: an opening change.
                int y = rng.NextInt(3, 63), c = y * g.Width + 32;
                if (trees.Remove(c)) { g.ClearResource(32, y, 1, 1); felled.Add(c); }
            }
            else if (op == 6 || op == 7)
            {
                // Regrow a felled one (a closing change), so the wall keeps closing again.
                if (felled.Count > 0)
                {
                    int k = rng.NextInt(0, felled.Count), c = felled[k];
                    if (g.CanTakeResource(32, c / g.Width) && !HouseCovers(houses, g, c)) { g.SetResource(32, c / g.Width, 1, 1); trees.Add(c); felled.RemoveAt(k); }
                }
            }
            else if (op == 1)
            {
                // A House footprint somewhere near the wall or the gap.
                int x = rng.NextInt(26, 37), y = rng.NextInt(1, 61);
                if (AllPassable(g, x, y, 2, 2)) { g.SetBuilding(x, y, 2, 2); houses.Add(y * g.Width + x); }
            }
            else if (op == 2 && houses.Count > 0)
            {
                int k = rng.NextInt(0, houses.Count), c = houses[k];
                houses.RemoveAt(k);
                g.ClearBuilding(c % g.Width, c / g.Width, 2, 2);
            }
            else if (op == 3) g.BumpVersionForTests();
            // op 4, 5 (and a refused 0-3, 6, 7): nothing changes (the memo should answer)
            int pick = rng.NextInt(0, hot.Length);
            (int hx, int hy) = hot[pick];
            if (!AllPassable(g, hx, hy, 2, 2)) continue;
            long floods = w.Seal.Floods;
            bool memo = w.Seal.KeepsConnected(hx, hy, 2, 2);
            if (w.Seal.Floods == floods) memoHits++;
            w.Seal.ForgetForTests();
            bool fresh = w.Seal.KeepsConnected(hx, hy, 2, 2);
            Assert.True(memo == fresh, $"seed {seed} step {step} op {op}: memo {memo}, flood {fresh} at ({hx}, {hy})");
            checks++;
            if (pick == 0)
            {
                if (last.HasValue && last.Value != fresh) flips++;
                last = fresh;
            }
        }
        _out.WriteLine($"seed {seed}: {checks} checks, {memoHits} answered from the memo, gap verdict flipped {flips} times");
        Assert.True(memoHits >= 50, $"only {memoHits} memo answers: the fuzz doesn't exercise the memo");
        Assert.True(flips >= 5, $"gap verdict flipped only {flips} times");
    }

    private static bool AllPassable(NavGrid g, int x0, int y0, int fw, int fh)
    {
        for (int y = y0; y < y0 + fh; y++) for (int x = x0; x < x0 + fw; x++) if (!g.IsPassable(x, y)) return false;
        return true;
    }

    private static bool HouseCovers(List<int> houses, NavGrid g, int cell)
    {
        int x = cell % g.Width, y = cell / g.Width;
        foreach (int h in houses)
            if (x >= h % g.Width && x < h % g.Width + 2 && y >= h / g.Width && y < h / g.Width + 2) return true;
        return false;
    }

    /// <summary>
    /// One tick, three commands in order: a Build at the gap is refused (it seals; the answer is kept), the Cancel of a
    /// House site that closes the wall opens it, and a second Build at the gap in the same tick is placed: the memo
    /// followed the cancel's version bump.
    /// </summary>
    [Fact]
    public void SealMemo_ACancelEarlierInTheSameTick_OpensTheGap_TheLaterBuildIsPlaced()
    {
        // The tree wall with a two-cell gap at y 40-41, closed by a House site at (32, 40).
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 16, CommandCapacity: 256) with { ResourceCapacity = 128, BuildingCapacity = 64 }, Flat(64, 64));
        World w = sim.World;
        for (int y = 3; y < 63; y++) if (y != 40 && y != 41) Spawn(w, Tree, 32, y, TreeWood);
        Give(sim, 0, 100_000, 100_000);
        EntityHandle a = Unit(sim, At(sim, 10, 10)), b = Unit(sim, At(sim, 12, 10)), c = Unit(sim, At(sim, 30, 44));
        Run(sim, 1);
        Assert.True(w.CanPlace(0, House, Cell(sim, 32, 40), out PlacementError why0), why0.ToString());
        sim.Enqueue(Command.Build(0, c, House, At(sim, 32, 40)));
        Run(sim, 2); // commands apply in the second tick
        Assert.True(SiteAt(sim, 32, 40) >= 0, "the wall's House site");
        Assert.False(w.CanPlace(0, House, Cell(sim, 32, 1), out PlacementError why));
        Assert.Equal(PlacementError.SealsGround, why);
        sim.Enqueue(Command.Build(0, a, House, At(sim, 32, 1)));
        sim.Enqueue(Command.Cancel(0, At(sim, 32, 40)));
        sim.Enqueue(Command.Build(0, b, House, At(sim, 32, 1)));
        Run(sim, 2); // all three apply in one tick, in this order
        Assert.Equal(-1, SiteAt(sim, 32, 40));
        Assert.True(SiteAt(sim, 32, 1) >= 0, "the second Build at the gap wasn't placed after the cancel opened the wall");
    }

    /// <summary>
    /// A wall tree with 1 wood left, felled by a real worker: before, Builds at the gap are refused for SealsGround; the
    /// tick after the fell the same Build is placed (the depletion's version bump reached the memo).
    /// </summary>
    [Fact]
    public void SealMemo_ATreeFelledByAWorker_TheNextBuildAtTheGapIsPlaced()
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 16, CommandCapacity: 256) with { ResourceCapacity = 128, BuildingCapacity = 64 }, Flat(64, 64));
        World w = sim.World;
        for (int y = 3; y < 63; y++) Spawn(w, Tree, 32, y, y == 40 ? 1 : TreeWood);
        Give(sim, 0, 100_000, 100_000);
        EntityHandle cutter = Unit(sim, At(sim, 31, 40)), builder = Unit(sim, At(sim, 10, 10));
        Run(sim, 1);
        sim.Enqueue(Command.Gather(0, cutter, At(sim, 32, 40)));
        bool placed = false;
        int refusedBefore = 0;
        for (int t = 0; t < 400 && !placed; t++)
        {
            bool open = w.NavGrid.IsPassable(32, 40);
            if (!open)
            {
                Assert.False(w.CanPlace(0, House, Cell(sim, 32, 1), out PlacementError why));
                Assert.Equal(PlacementError.SealsGround, why);
                refusedBefore++;
            }
            sim.Enqueue(Command.Build(0, builder, House, At(sim, 32, 1)));
            Run(sim, 1);
            placed = SiteAt(sim, 32, 1) >= 0;
            if (placed) Assert.True(w.NavGrid.IsPassable(32, 40), $"tick {t}: placed while the wall was closed");
        }
        _out.WriteLine($"{refusedBefore} ticks refused before the fell");
        Assert.True(refusedBefore > 0);
        Assert.True(placed, "never placed after the tree fell");
    }
}
