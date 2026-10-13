using System.Diagnostics;
using System.Numerics;
using System.Text;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M5-4a): the generator's start locations, the placer's start mines and forest, and <see cref="StartBase.Plan"/> on them,
/// attacked independently of the developer's <c>MapGenTests</c> / <c>StartBaseTests</c>.
/// </summary>
[Collection(SerialCollection.Name)]
public class StartLocationQaTests
{
    private readonly ITestOutputHelper _out;

    public StartLocationQaTests(ITestOutputHelper output) => _out = output;

    private static MapGenParams MatchMap(int size) => MapGenParams.Default with { Width = size, Height = size, Forests = 12, GoldMines = 8 };

    public static IEnumerable<object[]> SeedsBySize()
    {
        foreach (int size in new[] { 128, 192, 256 })
            for (ulong seed = 1; seed <= 12; seed++)
                yield return new object[] { size, seed };
    }

    // ------------------------------------------------------------------ the 36 seed x size pairs

    /// <summary>
    /// Every pair: exactly two start locations, an exact point mirror, inside the map by the margin; the whole site disc open
    /// ground on the terrain (no cliff or ramp on or beside it); the planned hall footprint and all 5 workers inside the clear
    /// disc and actually spawned; both start mines, a start-forest tree and the enemy hall reachable on foot from the hall.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeedsBySize))]
    public void StartLocations_MirroredOpenFlat_HallAndWorkersInTheDisc_ResourcesAndEnemyReachableOnFoot(int size, ulong seed)
    {
        var sim = new Simulation(TestSim.Config(seed, 2, 64, 1024) with { Map = MatchMap(size) });
        World w = sim.World;
        MapGenParams p = w.Config.Map;
        ReadOnlySpan<StartLocation> starts = w.StartLocations;
        Assert.Equal(2, starts.Length);
        int x0 = starts[0].Cell % size, y0 = starts[0].Cell / size;
        Assert.Equal((size - 1 - y0) * size + (size - 1 - x0), starts[1].Cell); // exact point mirror
        Assert.True(x0 < size / 2, $"player 0 at x {x0} is not in the west half");

        // The site disc on the bare terrain: open ground (passable, no ramp, no cliff / ramp / border among the 8 neighbours).
        var terrain = new NavGrid(w.Heightmap);
        int r = p.StartClearRadius, rs = p.StartSiteRadius;
        foreach (StartLocation st in starts)
        {
            int cx = st.Cell % size, cy = st.Cell / size;
            Assert.InRange(cx, p.StartEdgeMargin + r, size - 1 - p.StartEdgeMargin - r);
            Assert.InRange(cy, p.StartEdgeMargin + r, size - 1 - p.StartEdgeMargin - r);
            Assert.Equal(terrain.LevelAt(cx, cy), st.Level);
            for (int y = cy - rs; y <= cy + rs; y++)
                for (int x = cx - rs; x <= cx + rs; x++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > rs * rs) continue;
                    Assert.True(terrain.IsPassable(x, y) && terrain.LevelAt(x, y) == st.Level && !w.Heightmap.IsRamp(x, y),
                        $"{size} s{seed}: site cell ({x},{y}) of {st} is blocked, off level or a ramp");
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            Assert.True((terrain.FlagsAt(x + dx, y + dy) & (NavFlags.Cliff | NavFlags.Ramp)) == 0,
                                $"{size} s{seed}: site cell ({x},{y}) of {st} is beside a cliff or ramp");
                }
        }

        // The plan: hall footprint and workers in the clear disc, everything spawned.
        float maxR = StartBaseTests.MaxRadius(w.Data);
        Vector2[][] blocks = { Array.Empty<Vector2>(), Array.Empty<Vector2>() };
        StartBasePlan plan = ViewReads.Plan(w, blocks, 5, maxR);
        for (int pl = 0; pl < 2; pl++)
        {
            int cx = starts[pl].Cell % size, cy = starts[pl].Cell / size, a = plan.HallAnchor[pl];
            Assert.True(a >= 0, $"{size} s{seed}: player {pl} has no hall");
            BuildingDef def = w.Data.Buildings[plan.HallType[pl]];
            for (int y = a / size; y < a / size + def.FootprintHeight; y++)
                for (int x = a % size; x < a % size + def.FootprintWidth; x++)
                    Assert.True((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r, $"{size} s{seed}: hall cell ({x},{y}) outside the clear disc");
            Assert.Equal(5, plan.Workers[pl].Length);
            foreach (Vector2 v in plan.Workers[pl])
            {
                Assert.True(w.NavGrid.WorldToCell(v, out int wx, out int wy));
                Assert.True((wx - cx) * (wx - cx) + (wy - cy) * (wy - cy) <= r * r, $"{size} s{seed}: worker cell ({wx},{wy}) outside the clear disc");
            }
        }
        StartBaseTests.Apply(sim, blocks, plan);
        Assert.Equal(2, w.Buildings.Count);
        Assert.Equal(10, w.Units.Count);

        // On foot from the hall: both start mines, a start-forest tree, and the enemy hall.
        var halls = new int[2];
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k]) halls[w.Buildings.Owner[k]] = k;
        for (int pl = 0; pl < 2; pl++)
        {
            bool[] reach = FloodFromFootprint(w.NavGrid, w.Buildings.Cell[halls[pl]], w.Data.Buildings[w.Buildings.TypeId[halls[pl]]].FootprintWidth,
                w.Data.Buildings[w.Buildings.TypeId[halls[pl]]].FootprintHeight);
            int otherHall = halls[1 - pl];
            BuildingDef od = w.Data.Buildings[w.Buildings.TypeId[otherHall]];
            Assert.True(Touches(w.NavGrid, reach, w.Buildings.Cell[otherHall], od.FootprintWidth, od.FootprintHeight), $"{size} s{seed}: player {pl} can't walk to the enemy hall");
            int mines = 0, trees = 0;
            int cx = starts[pl].Cell % size, cy = starts[pl].Cell / size;
            for (int k = 0; k < w.Resources.Capacity; k++)
            {
                if (!w.Resources.Alive[k]) continue;
                ResourceDef def = w.Data.Resources[w.Resources.TypeId[k]];
                int c = w.Resources.Cell[k], ax = c % size, ay = c / size;
                float dx = (ax + def.FootprintWidth * 0.5f - cx - 0.5f) * MapConstants.CellSize, dy = (ay + def.FootprintHeight * 0.5f - cy - 0.5f) * MapConstants.CellSize;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (w.NavGrid.LevelAt(ax, ay) != starts[pl].Level) continue;
                if (def.Resource == ResourceKind.Gold && d <= p.StartMineDistance)
                {
                    Assert.True(Touches(w.NavGrid, reach, c, def.FootprintWidth, def.FootprintHeight), $"{size} s{seed}: player {pl}'s start mine at ({ax},{ay}) is not reachable on foot");
                    mines++;
                }
                else if (def.Resource == ResourceKind.Wood && d <= p.StartForestDistance + 1.5f && Touches(w.NavGrid, reach, c, 1, 1))
                {
                    trees++;
                }
            }
            Assert.True(mines >= 2, $"{size} s{seed}: player {pl} reaches {mines} start mines");
            Assert.True(trees >= 1, $"{size} s{seed}: player {pl} reaches no tree within {p.StartForestDistance} m");
        }
        int sx = starts[1].Cell % size - x0, sy = starts[1].Cell / size - y0;
        _out.WriteLine($"{size} s{seed}: P0 ({x0},{y0}) L{starts[0].Level}, P1 L{starts[1].Level}, apart {MathF.Sqrt(sx * sx + sy * sy) * MapConstants.CellSize:F0} m");
    }

    /// <summary>Cells reachable on foot (4-connected over passable cells, which matches the diagonal no-corner-cut rule) from the cells 4-adjacent to a footprint.</summary>
    internal static bool[] FloodFromFootprint(NavGrid g, int anchor, int fw, int fh)
    {
        int w = g.Width;
        var seen = new bool[w * g.Height];
        var q = new Queue<int>();
        int ax = anchor % w, ay = anchor / w;
        for (int y = ay - 1; y <= ay + fh; y++)
            for (int x = ax - 1; x <= ax + fw; x++)
                if (g.IsPassable(x, y) && !seen[y * w + x]) { seen[y * w + x] = true; q.Enqueue(y * w + x); }
        while (q.Count > 0)
        {
            int c = q.Dequeue(), x = c % w, y = c / w;
            foreach ((int nx, int ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (!g.IsPassable(nx, ny) || seen[ny * w + nx]) continue;
                seen[ny * w + nx] = true;
                q.Enqueue(ny * w + nx);
            }
        }
        return seen;
    }

    /// <summary>True if a reached cell is 4-adjacent to the footprint at <paramref name="anchor"/>.</summary>
    internal static bool Touches(NavGrid g, bool[] reach, int anchor, int fw, int fh)
    {
        int w = g.Width, ax = anchor % w, ay = anchor / w;
        for (int y = ay; y < ay + fh; y++)
            for (int x = ax; x < ax + fw; x++)
                foreach ((int nx, int ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (g.InBounds(nx, ny) && reach[ny * w + nx]) return true;
        return false;
    }

    // ------------------------------------------------------------------ fairness survey (measured, reported)

    /// <summary>
    /// Measured over seeds 1-100 per size: how often the two bases stand on different levels (one on high ground, docs/02
    /// "High ground"), and the closest two bases come. Reported in the QA record; asserts only the generator's own rule
    /// (site discs apart).
    /// </summary>
    [Fact]
    public void Survey_StartLevelsAndSeparation_Seeds1To100()
    {
        foreach (int size in new[] { 128, 192, 256 })
        {
            int diffLevel = 0, flat = 0;
            float minApart = float.MaxValue, sum = 0;
            ulong minSeed = 0;
            var p = MapGenParams.Default with { Width = size, Height = size };
            for (ulong seed = 1; seed <= 100; seed++)
            {
                var rng = new SimRng(seed, RngStream.MapGen);
                Heightmap hm = MapGenerator.Generate(p, ref rng);
                ReadOnlySpan<StartLocation> s = hm.StartLocations;
                Assert.Equal(2, s.Length);
                if (s[0].Level != s[1].Level) diffLevel++;
                bool allZero = true;
                foreach (byte b in hm.Levels) if (b != 0) { allZero = false; break; }
                if (allZero) flat++;
                int dx = s[1].Cell % size - s[0].Cell % size, dy = s[1].Cell / size - s[0].Cell / size;
                float apart = MathF.Sqrt(dx * dx + dy * dy) * MapConstants.CellSize;
                Assert.True(apart > 2 * p.StartSiteRadius * MapConstants.CellSize);
                sum += apart;
                if (apart < minApart) { minApart = apart; minSeed = seed; }
            }
            _out.WriteLine($"{size}: bases on different levels {diffLevel}/100, flat fallback {flat}/100, apart min {minApart:F0} m (seed {minSeed}) mean {sum / 100:F0} m");
        }
    }

    /// <summary>
    /// 3-4 locations (M6-V5 will use them): on square 128 / 192 / 256 maps, seeds 1-30, every map gets exactly n quarter
    /// turns of player 0's, the site discs apart; measured how often that costs the terrain (flat fallback).
    /// </summary>
    [Fact]
    public void ThreeAndFourLocations_AlwaysQuarterTurns_FlatFallbackMeasured()
    {
        foreach (int n in new[] { 3, 4 })
            foreach (int size in new[] { 128, 192, 256 })
            {
                var p = MapGenParams.Default with { Width = size, Height = size, StartLocations = n };
                int flat = 0;
                for (ulong seed = 1; seed <= 30; seed++)
                {
                    var rng = new SimRng(seed, RngStream.MapGen);
                    Heightmap hm = MapGenerator.Generate(p, ref rng);
                    ReadOnlySpan<StartLocation> s = hm.StartLocations;
                    Assert.Equal(n, s.Length);
                    int x0 = s[0].Cell % size, y0 = s[0].Cell / size;
                    Assert.Equal((size - 1 - y0) * size + (size - 1 - x0), s[1].Cell);
                    Assert.Equal(x0 * size + (size - 1 - y0), s[2].Cell);
                    if (n == 4) Assert.Equal((size - 1 - x0) * size + y0, s[3].Cell);
                    bool allZero = true;
                    foreach (byte b in hm.Levels) if (b != 0) { allZero = false; break; }
                    if (allZero) flat++;
                }
                _out.WriteLine($"{n} locations on {size}: flat fallback {flat}/30");
            }
    }

    /// <summary>
    /// A hand-built start location by the west edge (legal: the Heightmap only checks the cell and level). The hall's
    /// centred anchor would start left of x 0; it must fall back to the nearest spot by that location, not wrap onto the
    /// previous row's east end. BUG-0622 item 1.
    /// </summary>
    [Fact(Skip = "BUG-0622 item 1: a hand-built start location by the west edge wraps the hall's anchor to the east edge")]
    public void HandBuiltStartByTheWestEdge_HallStaysByIt()
    {
        Heightmap flat = ResourceMaps.Flat(40, 40);
        var hm = new Heightmap(40, 40, flat.Levels, flat.Elevations, new[] { new StartLocation(20 * 40 + 1, 0) });
        Simulation sim = ResourceMaps.NewSim(hm);
        StartBasePlan plan = ViewReads.Plan(sim.World, new[] { Array.Empty<Vector2>() }, 5, 0.5f);
        _out.WriteLine($"hall anchor ({plan.HallAnchor[0] % 40},{plan.HallAnchor[0] / 40})");
        Assert.True(plan.HallAnchor[0] >= 0 && plan.HallAnchor[0] % 40 < 20, $"hall at x {plan.HallAnchor[0] % 40}, start at x 1");
    }

    // ------------------------------------------------------------------ Validate and the loud failure

    public static IEnumerable<object[]> MoreBadStartParams()
    {
        MapGenParams d = MapGenParams.Default;
        yield return new object[] { d with { StartEdgeMargin = -1 }, nameof(MapGenParams.StartEdgeMargin) };
        yield return new object[] { d with { StartClearRadius = int.MinValue }, nameof(MapGenParams.StartClearRadius) };
        yield return new object[] { d with { StartLocations = int.MaxValue }, nameof(MapGenParams.StartLocations) };
        yield return new object[] { d with { StartMineDistance = float.PositiveInfinity }, nameof(MapGenParams.StartMineDistance) };
        yield return new object[] { d with { StartMineDistance = float.NegativeInfinity }, nameof(MapGenParams.StartMineDistance) };
        yield return new object[] { d with { StartMineDistance = -0f }, nameof(MapGenParams.StartMineDistance) };
        yield return new object[] { d with { StartForestDistance = float.PositiveInfinity }, nameof(MapGenParams.StartForestDistance) };
        yield return new object[] { d with { StartForestDistance = -0f }, nameof(MapGenParams.StartForestDistance) };
        yield return new object[] { d with { StartLocations = 3, Width = 128, Height = 192 }, nameof(MapGenParams.StartLocations) };
        // The brief's tiny map: Width 32 with a large clear radius, and with the default one.
        yield return new object[] { d with { Width = 32, Height = 32, EdgeMargin = 2, Level1MaxSize = 20, StartClearRadius = 20, StartMineDistance = 41f, StartForestDistance = 42f }, nameof(MapGenParams.StartClearRadius) };
        yield return new object[] { d with { Width = 32, Height = 32, EdgeMargin = 2, Level1MaxSize = 20 }, nameof(MapGenParams.StartClearRadius) };
        yield return new object[] { d with { Width = 32, Height = 1024, EdgeMargin = 2, Level1MaxSize = 20 }, nameof(MapGenParams.StartClearRadius) };
    }

    [Theory]
    [MemberData(nameof(MoreBadStartParams))]
    public void Validate_RefusesEachStartParamOutOfRange_ByName_AndGenerateRefusesItToo(MapGenParams p, string name)
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(p.Validate);
        Assert.Equal(name, e.ParamName);
        var rng = new SimRng(1, RngStream.MapGen);
        Assert.Throws<ArgumentOutOfRangeException>(() => MapGenerator.Generate(p, ref rng));
    }

    /// <summary>
    /// docs/03 "Implementation (M5-4a)": "Validate makes the pair fit a flat map ... so the flat fallback always has them".
    /// Every parameter set Validate accepts must generate (never the InvalidOperationException meant to be unreachable).
    /// Swept: square and narrow maps 16-80 cells, StartEdgeMargin 1-3, StartClearRadius 2-8, mine distance at its minimum and
    /// maximum. BUG-0620: with a small StartEdgeMargin, Validate checks the clear disc against the map but the search needs
    /// the larger site disc two cells off the border, so e.g. 40 x 40 with margin 1 passes Validate and throws.
    /// </summary>
    [Fact(Skip = "BUG-0620: Validate accepts start params the flat fallback can't hold; Generate throws InvalidOperationException")]
    public void EveryValidatedStartParamSet_Generates()
    {
        var failures = new List<string>();
        int validated = 0;
        foreach (int width in new[] { 16, 24, 32, 40, 48, 56, 64, 80 })
            foreach (int height in new[] { 16, 24, 32, 40, 64 })
                foreach (int margin in new[] { 1, 2, 3 })
                    foreach (int clear in new[] { 2, 4, 8 })
                        foreach (float mine in new[] { clear * 2f + 0.5f, clear * 2f + 16f })
                        {
                            int minDim = Math.Min(width, height);
                            var p = MapGenParams.Default with
                            {
                                Width = width, Height = height, EdgeMargin = 1, Level1MinSize = 3, Level1MaxSize = minDim - 4,
                                Level2MinSize = 3, Level2MaxSize = 3, RampLength = 3, RampWidth = 1,
                                StartEdgeMargin = margin, StartClearRadius = clear, StartMineDistance = mine, StartForestDistance = clear * 2f + 1f,
                            };
                            try { p.Validate(); }
                            catch (ArgumentOutOfRangeException) { continue; }
                            validated++;
                            var rng = new SimRng(3, RngStream.MapGen);
                            try { MapGenerator.Generate(p, ref rng); }
                            catch (Exception ex) { failures.Add($"{width}x{height} margin {margin} clear {clear} mine {mine}: {ex.GetType().Name}"); }
                        }
        _out.WriteLine($"{validated} validated sets, {failures.Count} threw");
        foreach (string f in failures.Take(20)) _out.WriteLine(f);
        Assert.Empty(failures);
    }

    /// <summary>BUG-0620's minimal row: a 40 x 40 map with StartEdgeMargin 1 passes Validate (pinned), then Generate throws.</summary>
    [Fact]
    public void Bug0620_Minimal_ValidatesThenThrows_Pinned()
    {
        var p = MapGenParams.Default with { Width = 40, Height = 40, EdgeMargin = 1, Level1MaxSize = 30, StartEdgeMargin = 1 };
        p.Validate();
        var rng = new SimRng(1, RngStream.MapGen);
        // Pins today's behaviour so a fix shows up here: either Validate refuses it by name, or a pair comes out.
        var e = Record.Exception(() => MapGenerator.Generate(p, ref rng));
        _out.WriteLine(e?.Message ?? "generated");
        Assert.IsType<InvalidOperationException>(e);
    }

    /// <summary>
    /// BUG-0620 through a replay file: a format 6 header whose map params pass <see cref="MapGenParams.Validate"/> (so
    /// <c>TryRead</c> says None) but leave no room for the pair makes <see cref="ReplayPlayer.Run(Replay, GameData)"/> throw
    /// instead of refusing the replay with an error. Before M5-4a every validated header generated a map.
    /// </summary>
    [Fact(Skip = "BUG-0620: a validated replay header whose start locations can't fit throws InvalidOperationException out of ReplayPlayer.Run")]
    public void Bug0620_ReplayHeaderThatValidatesButCannotFit_IsRefusedNotThrown()
    {
        (Replay? bad, ReplayError read) = Bug0620Replay();
        Assert.Equal(ReplayError.None, read);
        ReplayResult res = ReplayPlayer.Run(bad!, TestSim.Data);
        Assert.False(res.Ok);
    }

    /// <summary>Pins BUG-0620's replay path as it is today, so a fix shows up: the header reads clean and playback throws.</summary>
    [Fact]
    public void Bug0620_ReplayPath_Pinned()
    {
        (Replay? bad, ReplayError read) = Bug0620Replay();
        Assert.Equal(ReplayError.None, read);
        Assert.Throws<InvalidOperationException>(() => ReplayPlayer.Run(bad!, TestSim.Data));
    }

    private static (Replay? Replay, ReplayError Read) Bug0620Replay()
    {
        var map = MapGenParams.Default with { Width = 40, Height = 40, EdgeMargin = 1, Level1MaxSize = 30, StartEdgeMargin = 1, StartLocations = 0 };
        var sim = new Simulation(TestSim.Config(1, 2, 16, 64) with { Map = map });
        var rec = new ReplayRecorder(sim, checkpointInterval: 10);
        for (int t = 0; t < 20; t++) sim.Tick();
        string text = Encoding.ASCII.GetString(ReplayFormat.Write(rec.ToReplay()));
        Assert.Contains("map.start-locations 0\n", text);
        string bad = Resign(text.Replace("map.start-locations 0\n", "map.start-locations 2\n"));
        ReplayError e = ReplayFormat.TryRead(Encoding.ASCII.GetBytes(bad), out Replay? r);
        return (r, e);
    }

    // ------------------------------------------------------------------ determinism and hash

    [Fact]
    public void SameSeedTwice_SameStartsResourcesAndHash_DifferentSeeds_DifferentStartsOrResources()
    {
        var hashes = new HashSet<ulong>();
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var a = new Simulation(TestSim.Config(seed, 2, 64, 64) with { Map = MatchMap(128) });
            var b = new Simulation(TestSim.Config(seed, 2, 64, 64) with { Map = MatchMap(128) });
            Assert.True(a.World.StartLocations.SequenceEqual(b.World.StartLocations));
            Assert.True(a.World.Resources.Cell.SequenceEqual(b.World.Resources.Cell));
            for (int t = 0; t < 20; t++) { a.Tick(); b.Tick(); }
            Assert.Equal(a.StateHash(), b.StateHash());
            Assert.True(hashes.Add(a.StateHash()), $"seed {seed} hashes like an earlier seed");
        }
    }

    /// <summary>Start locations are not hashed: on a seed whose layout fits (the terrain is the same with and without) and with no resources, the hash is the same with StartLocations 2 and 0.</summary>
    [Fact]
    public void StartLocations_AreNotInTheStateHash()
    {
        var with = new Simulation(TestSim.Config(1, 2, 64, 64) with { Map = MapGenParams.Default });
        var without = new Simulation(TestSim.Config(1, 2, 64, 64) with { Map = MapGenParams.Default with { StartLocations = 0 } });
        Assert.Equal(2, with.World.StartLocations.Length);
        Assert.True(without.World.StartLocations.IsEmpty);
        Assert.Equal(with.World.Heightmap.ContentHash(), without.World.Heightmap.ContentHash());
        Assert.Equal(with.StateHash(), without.StateHash());
    }

    /// <summary>StartBase.Plan is a read: planning on every seed changes no state hash.</summary>
    [Fact]
    public void Plan_OnStartLocations_ChangesNoStateHash()
    {
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var sim = new Simulation(TestSim.Config(seed, 2, 64, 64) with { Map = MatchMap(128) });
            sim.Tick();
            ulong before = sim.StateHash();
            for (int k = 0; k < 3; k++) ViewReads.Plan(sim.World, new[] { Array.Empty<Vector2>(), Array.Empty<Vector2>() }, 5, 1f);
            Assert.Equal(before, sim.StateHash());
        }
    }

    // ------------------------------------------------------------------ StartBase.Plan twin: byte-identical to the diff base

    /// <summary>
    /// The plans for hand-built grids (flat, a mined one, a two-player plateau) and for generated maps with
    /// <c>StartLocations</c> 0 (seeds 1-30, 128 / 192, armies 0 / 100) hash exactly as QA measured on the commit before M5-4a
    /// (e3feb17, the same 166 rows with the generator's default map then). Any change to the no-start-location path shows here.
    /// </summary>
    [Fact]
    public void Plan_WithoutStartLocations_IsByteIdenticalToBeforeM5_4a()
    {
        List<string> rows = PlanRows();
        Assert.Equal(166, rows.Count);
        ulong h = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(string.Join("\n", rows)))
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        _out.WriteLine($"plan rows hash {h:X16}");
        Assert.Equal(BaselinePlanHash, h);
    }

    /// <summary>FNV-1a 64 of the 166 rows joined by '\n', measured by QA on e3feb17 (before M5-4a) and on 70479d6 (identical).</summary>
    private const ulong BaselinePlanHash = 0x81E850E21A37A75CUL;

    private static string Dump(string name, StartBasePlan p)
    {
        var sb = new StringBuilder(name);
        for (int i = 0; i < p.HallAnchor.Length; i++)
        {
            sb.Append($" | p{i} t{p.HallType[i]} a{p.HallAnchor[i]} w{p.WorkerType[i]}");
            foreach (Vector2 v in p.Workers[i]) sb.Append($" {BitConverter.SingleToInt32Bits(v.X):X8},{BitConverter.SingleToInt32Bits(v.Y):X8}");
        }
        return sb.ToString();
    }

    private static Heightmap Plateau(int w, int h, int x0, int y0, int x1, int y1)
    {
        var lv = new byte[w * h];
        var el = new float[w * h];
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) { lv[y * w + x] = 1; el[y * w + x] = 4f; }
        return new Heightmap(w, h, lv, el);
    }

    private static List<string> PlanRows()
    {
        var lines = new List<string>();
        float maxR = TestSim.Data.Units.Max(u => u.Radius);
        foreach ((int w, int h) in new[] { (40, 40), (20, 20), (64, 48), (7, 7), (12, 30) })
        {
            Simulation s = ResourceMaps.NewSim(ResourceMaps.Flat(w, h));
            foreach ((int bx, int by) in new[] { (2, 2), (w / 4, h / 2), (w / 2, h / 2), (w - 3, h - 3) })
            {
                Vector2[][] blocks = { new[] { s.World.NavGrid.CellCenter(bx, by) } };
                lines.Add(Dump($"flat{w}x{h} b{bx},{by}", ViewReads.Plan(s.World, blocks, 5, maxR)));
                lines.Add(Dump($"flat{w}x{h} empty", ViewReads.Plan(s.World, new[] { Array.Empty<Vector2>() }, 5, maxR)));
            }
        }
        {
            Simulation s = ResourceMaps.NewSim(ResourceMaps.Flat(48, 48), units: 32);
            ResourceMaps.Spawn(s.World, ResourceMaps.Mine, 20, 10, 2500);
            for (int x = 5; x < 12; x++) ResourceMaps.Spawn(s.World, ResourceMaps.Tree, x, 30, 100);
            foreach (int workers in new[] { 0, 1, 5, 12 })
                lines.Add(Dump($"res48 w{workers}", ViewReads.Plan(s.World, new[] { new[] { s.World.NavGrid.CellCenter(14, 14) } }, workers, maxR)));
        }
        {
            var sim = new Simulation(TestSim.Config(5, 2, 64, 64), Plateau(60, 40, 4, 4, 22, 36));
            Vector2[][] blocks = { new[] { sim.World.NavGrid.CellCenter(10, 20) }, new[] { sim.World.NavGrid.CellCenter(50, 20) } };
            lines.Add(Dump("plateau2p", ViewReads.Plan(sim.World, blocks, 5, maxR)));
            lines.Add(Dump("plateau2p empty", ViewReads.Plan(sim.World, new[] { Array.Empty<Vector2>(), Array.Empty<Vector2>() }, 5, maxR)));
        }
        for (ulong seed = 1; seed <= 30; seed++)
        {
            foreach (int size in new[] { 128, 192 })
            {
                var map = MapGenParams.Default with { Width = size, Height = size, Forests = 12, GoldMines = 8, StartLocations = 0 };
                var sim = new Simulation(TestSim.Config(seed, 2, 2010, 4096) with { Map = map });
                World w = sim.World;
                foreach (int army in new[] { 0, 100 })
                {
                    var blocks = new Vector2[2][];
                    for (int p = 0; p < 2; p++)
                    {
                        FactionDef f = w.Data.Factions[w.FactionOf(p)];
                        float r = 0f;
                        foreach (int t in f.Units) r = Math.Max(r, w.Data.Units[t].Radius);
                        blocks[p] = army == 0 ? Array.Empty<Vector2>() : StartLayout.Block(w.NavGrid, army, west: p == 0, r);
                    }
                    lines.Add(Dump($"gen{size} s{seed} a{army} h{w.Heightmap.ContentHash():X16}", ViewReads.Plan(w, blocks, 5, maxR)));
                }
            }
        }
        return lines;
    }

    // ------------------------------------------------------------------ replay header

    /// <summary>Format 6 round-trips non-default start params byte for byte and plays back; a missing or out-of-range start line is a clean error, never an exception.</summary>
    [Fact]
    public void ReplayFormat6_NonDefaultStartParams_RoundTripAndPlay_BadLinesRefusedCleanly()
    {
        var map = MatchMap(192) with { StartClearRadius = 6, StartEdgeMargin = 12, StartMineDistance = 15.5f, StartForestDistance = 30f };
        var sim = new Simulation(TestSim.Config(4, 2, 64, 256) with { Map = map });
        var rec = new ReplayRecorder(sim, checkpointInterval: 50);
        for (int t = 0; t < 200; t++) sim.Tick();
        Replay r = rec.ToReplay();
        Assert.Equal(6, r.FormatVersion);
        byte[] bytes = ReplayFormat.Write(r);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? back));
        Assert.Equal(map, back!.Map);
        Assert.Equal(bytes, ReplayFormat.Write(back));
        Assert.True(ReplayPlayer.Run(back, TestSim.Data).Ok);

        string text = Encoding.ASCII.GetString(bytes);
        foreach ((string from, string to) in new[]
        {
            ("map.start-locations 2\n", ""),
            ("map.start-locations 2\n", "map.start-locations 1\n"),
            ("map.start-locations 2\n", "map.start-locations x\n"),
            ("map.start-clear-radius 6\n", "map.start-clear-radius 99\n"),
            ("map.start-forest-distance 41F00000\n", "map.start-forest-distance 7FC00000\n"),
        })
        {
            Assert.Contains(from, text);
            string bad = Resign(text.Replace(from, to));
            ReplayError e = ReplayFormat.TryRead(Encoding.ASCII.GetBytes(bad), out _);
            _out.WriteLine($"{from.Trim()} -> '{to.Trim()}': {e}");
            Assert.NotEqual(ReplayError.None, e);
        }
    }

    /// <summary>Recomputes the checksum line so a test edit reaches the header checks.</summary>
    private static string Resign(string text)
    {
        int at = text.LastIndexOf("checksum ", StringComparison.Ordinal);
        string body = text[..at];
        ulong h = 14695981039346656037UL;
        foreach (byte b in Encoding.ASCII.GetBytes(body)) { h ^= b; h *= 1099511628211UL; }
        return body + $"checksum {h:X16}\n";
    }

    // ------------------------------------------------------------------ odd resource counts (fairness)

    /// <summary>
    /// Measured: start resources per base when the counts don't cover every base (the launcher's --mines / --forests accept
    /// 0-64). docs/03 says the round robin "shorts every base alike".
    /// </summary>
    [Theory]
    [InlineData(1, 12)]
    [InlineData(3, 12)]
    [InlineData(8, 1)]
    public void ShortCounts_StartResourcesPerBase_Measured(int mines, int forests)
    {
        var sim = new Simulation(TestSim.Config(2, 2, 64, 64) with { Map = MapGenParams.Default with { GoldMines = mines, Forests = forests } });
        World w = sim.World;
        var near = new int[2];
        for (int pl = 0; pl < 2; pl++)
            near[pl] = MapGenTests.StartMines(w, w.StartLocations[pl]);
        int f0 = MapGenTests.LargestNearForest(w, w.StartLocations[0]), f1 = MapGenTests.LargestNearForest(w, w.StartLocations[1]);
        _out.WriteLine($"mines {mines} forests {forests}: start mines {near[0]} / {near[1]}, near forest {f0} / {f1} trees");
    }

    // ------------------------------------------------------------------ generation time

    /// <summary>Generation with start locations on 256 and 512 maps against the existing 50 ms default-generation gate (Debug), worst of 12 seeds.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Generate256And512_WithStartLocations_WorstOf12Seeds()
    {
        foreach (int size in new[] { 256, 512 })
        {
            var p = MapGenParams.Default with { Width = size, Height = size };
            double worst = 0;
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var rng = new SimRng(seed, RngStream.MapGen);
                var sw = Stopwatch.StartNew();
                MapGenerator.Generate(p, ref rng);
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            }
            _out.WriteLine($"{size}: worst {worst:F1} ms");
            if (size == 256) Assert.True(worst < 50, $"256 took {worst:F1} ms");
        }
    }

    /// <summary>The start search's worst case: 1,024 map, 4 locations, the largest clear radius, plateaus everywhere so no centre fits and every attempt scans every centre, then the flat fallback.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void WorstCaseStartSearch_1024_FourLocations_Radius32_Measured()
    {
        var p = MapGenParams.Default with
        {
            Width = 1024, Height = 1024, StartLocations = 4, StartClearRadius = 32, StartMineDistance = 66f, StartForestDistance = 70f,
            StartEdgeMargin = 1, Level1Plateaus = 32, Level1MinSize = 100, Level1MaxSize = 400, Level2Plateaus = 32,
        };
        p.Validate();
        var rng = new SimRng(1, RngStream.MapGen);
        var sw = Stopwatch.StartNew();
        Heightmap hm = MapGenerator.Generate(p, ref rng);
        double ms = sw.Elapsed.TotalMilliseconds;
        _out.WriteLine($"1024 worst start search: {ms:F0} ms, {hm.StartLocations.Length} locations");
        Assert.Equal(4, hm.StartLocations.Length);
        // docs/03: the worst valid generation takes about 1 s in Debug; the start search must not blow it up by an order.
        Assert.True(ms < 10_000, $"{ms:F0} ms");
    }
}
