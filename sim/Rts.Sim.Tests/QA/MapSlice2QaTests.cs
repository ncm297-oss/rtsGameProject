using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Rts.Sim.Vision;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-S8 (session 2026-11-06-0326): Pale Hills and Vathar Crossing measured on a built world, not on the loader's word:
/// the start contract (site disc open and flat, clear disc free of nodes, mines / tree distances, apart), the flood over the
/// built nav grid (4-neighbour, nodes blocking); Vathar's fords as chokepoints (a ford sealed by buildings sends the walk
/// to another ford; the band can't see the bank); Pale Hills' corridors under a 20-unit wave; the <c>mirror-map</c> round
/// trip (one source cell / one ramp's side mutated); and BUG-1057 on a 256 map: every quarter-point hall, a rebuilt hall and
/// <c>TryHallPoint</c>'s first-building fallback, each with every scout killed at the door.
/// </summary>
[Collection(SerialCollection.Name)]
public class MapSlice2QaTests
{
    private const string PaleHills = "pale_hills", Vathar = "vathar_crossing";

    private readonly ITestOutputHelper _out;

    public MapSlice2QaTests(ITestOutputHelper output) => _out = output;

    private static MapDef Def(string id) => TestSim.Data.Maps[TestSim.Data.FindMap(id)];

    /// <summary>A sim on map <paramref name="id"/> with no AI seats (two players), room for <paramref name="units"/> units.</summary>
    private static Simulation Bare(string id, int units = 128)
    {
        int s = Def(id).Size;
        return new Simulation(new SimConfig(3, 2, units, 4 * units) { Data = TestSim.Data, MapId = id, Map = MapGenParams.Default with { Width = s, Height = s } });
    }

    // ---------- the start contract and the flood on the built world ----------

    /// <summary>
    /// Every start of the map on the built world: its site disc (10 cells, centre to centre) is open ground on the terrain
    /// (<c>ResourcePlacer.IsOpenGround</c> on the map's bare nav grid: passable, no ramp, no cliff; the start mines stand
    /// inside it) on the start's level; its clear disc (8 cells) touches no node footprint on the built world; two mines' centres within 18 m and a tree's within 24 m; every two starts half the side apart.
    /// </summary>
    [Theory]
    [InlineData(PaleHills)]
    [InlineData(Vathar)]
    [InlineData("raraku")]
    public void TheStartContract_HoldsOnTheBuiltWorld(string id)
    {
        Simulation sim = Bare(id);
        World w = sim.World;
        MapDef m = Def(id);
        int s = m.Size;
        NavGrid g = w.NavGrid;
        var bare = new NavGrid(m.Terrain); // the terrain alone: the site disc is judged before the nodes (mines stand inside it, 17-18 m out)
        var node = new int[s * s]; // 1 + resource slot covering the cell
        for (int k = 0; k < w.Resources.Capacity; k++)
        {
            if (!w.Resources.Alive[k]) continue;
            ResourceDef d = w.Data.Resources[w.Resources.TypeId[k]];
            int c = w.Resources.Cell[k];
            for (int y = 0; y < d.FootprintHeight; y++)
                for (int x = 0; x < d.FootprintWidth; x++)
                    node[c + y * s + x] = k + 1;
        }
        Assert.Equal(m.Starts.Length, w.StartLocations.Length);
        for (int i = 0; i < w.StartLocations.Length; i++)
        {
            int sc = w.StartLocations[i].Cell, sx = sc % s, sy = sc / s, level = g.LevelAt(sx, sy);
            Assert.Equal(m.Starts[i], sc);
            Assert.Equal(w.StartLocations[0].Level, w.StartLocations[i].Level);
            for (int dy = -10; dy <= 10; dy++)
                for (int dx = -10; dx <= 10; dx++)
                {
                    if (dx * dx + dy * dy > 100) continue;
                    int x = sx + dx, y = sy + dy;
                    Assert.True(ResourcePlacer.IsOpenGround(bare, x, y), $"{id} start {i}: site cell ({x}, {y}) not open ground (flags {bare.FlagsAt(x, y)})");
                    Assert.True(g.LevelAt(x, y) == level, $"{id} start {i}: site cell ({x}, {y}) on level {g.LevelAt(x, y)}, start on {level}");
                    if (dx * dx + dy * dy <= 64) Assert.True(node[y * s + x] == 0, $"{id} start {i}: a node on the clear disc at ({x}, {y})");
                }
            Vector2 centre = g.CellCenter(sx, sy);
            var mines = new List<float>();
            float tree = float.MaxValue;
            for (int k = 0; k < w.Resources.Capacity; k++)
            {
                if (!w.Resources.Alive[k]) continue;
                ResourceDef d = w.Data.Resources[w.Resources.TypeId[k]];
                int c = w.Resources.Cell[k];
                var nc = new Vector2((c % s + d.FootprintWidth * 0.5f) * MapConstants.CellSize, (c / s + d.FootprintHeight * 0.5f) * MapConstants.CellSize);
                float dist = Vector2.Distance(centre, nc);
                if (d.Resource == ResourceKind.Gold) mines.Add(dist);
                else tree = Math.Min(tree, dist);
            }
            mines.Sort();
            _out.WriteLine($"{id} start {i} ({sx}, {sy}) level {level}: mines {string.Join(" / ", mines.Take(3).Select(d => d.ToString("F1")))} m; tree {tree:F1} m");
            Assert.True(mines.Count >= 2 && mines[1] <= 18f, $"{id} start {i}: second mine {(mines.Count > 1 ? mines[1] : -1):F1} m");
            Assert.True(tree <= 24f, $"{id} start {i}: tree {tree:F1} m");
            for (int o = 0; o < i; o++)
            {
                int ox = w.StartLocations[o].Cell % s, oy = w.StartLocations[o].Cell / s;
                Assert.True(4 * ((sx - ox) * (sx - ox) + (sy - oy) * (sy - oy)) >= s * s, $"{id}: starts {o} and {i} closer than half the side");
            }
        }
    }

    /// <summary>
    /// The flood on the built world's nav grid (trees and mines blocking, 4-neighbour: no diagonal squeeze between two
    /// trees counted): every passable cell is reached from start 0, every start reaches every other, and every gold mine has
    /// a reached cell beside it. Also the share of passable cells free of nodes (criterion 5's open ground on Pale Hills).
    /// </summary>
    [Theory]
    [InlineData(PaleHills)]
    [InlineData(Vathar)]
    public void TheFlood_OnTheBuiltNavGrid_ReachesEveryPassableCell_FourNeighbour(string id)
    {
        Simulation sim = Bare(id);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int s = g.Width;
        bool[] seen = Flood(g, w.StartLocations[0].Cell);
        int passable = 0, reached = 0;
        var firstMissed = (-1, -1);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                if (!g.IsPassable(x, y)) continue;
                passable++;
                if (seen[y * s + x]) reached++;
                else if (firstMissed.Item1 < 0) firstMissed = (x, y);
            }
        _out.WriteLine($"{id}: passable {passable}, reached 4-neighbour {reached}; first missed {firstMissed}");
        Assert.True(passable == reached, $"{id}: {passable - reached} passable cells not reached 4-neighbour from start 0, first {firstMissed}");
        foreach (StartLocation st in w.StartLocations) Assert.True(seen[st.Cell]);
        for (int k = 0; k < w.Resources.Capacity; k++)
        {
            if (!w.Resources.Alive[k] || w.Data.Resources[w.Resources.TypeId[k]].Resource != ResourceKind.Gold) continue;
            ResourceDef d = w.Data.Resources[w.Resources.TypeId[k]];
            int c = w.Resources.Cell[k], cx = c % s, cy = c / s;
            bool touches = false;
            for (int y = cy - 1; y <= cy + d.FootprintHeight && !touches; y++)
                for (int x = cx - 1; x <= cx + d.FootprintWidth && !touches; x++)
                    if (g.InBounds(x, y) && seen[y * s + x]) touches = true;
            Assert.True(touches, $"{id}: the mine at ({cx}, {cy}) has no reached cell beside it");
        }
    }

    private static bool[] Flood(NavGrid g, int from)
    {
        int s = g.Width;
        var seen = new bool[s * g.Height];
        var queue = new Queue<int>();
        seen[from] = true;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % s, y = i / s;
            Visit(x - 1, y);
            Visit(x + 1, y);
            Visit(x, y - 1);
            Visit(x, y + 1);
        }
        return seen;

        void Visit(int x, int y)
        {
            if (!g.IsPassable(x, y) || seen[y * s + x]) return;
            seen[y * s + x] = true;
            queue.Enqueue(y * s + x);
        }
    }

    // ---------- Vathar's fords ----------

    /// <summary>The ford (0-3, west to east) whose ramps cover column <paramref name="x"/>; -1 for none.</summary>
    private static int FordOf(MapDef m, int x)
    {
        int[] xs = m.Ramps.Select(r => r.X).Distinct().OrderBy(v => v).ToArray();
        for (int f = 0; f < xs.Length; f++)
        {
            MapRampDef r = m.Ramps.First(q => q.X == xs[f]);
            if (x >= r.X && x < r.X + r.Width) return f;
        }
        return -1;
    }

    /// <summary>
    /// Walks one Heavy Infantry of player 0 from start 0 to start 1 on Vathar Crossing, with <paramref name="block"/> run
    /// first; returns the ford it stepped from the north bank (level 1) down into (-1 for none) and the tick it arrived (-1).
    /// </summary>
    private (int Ford, int Arrived) WalkAcross(Action<Simulation, int>? block, int limit = 9000)
    {
        MapDef m = Def(Vathar);
        int s = m.Size;
        Simulation sim = Bare(Vathar);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        EntityHandle unit = CombatScenes.Spawn(sim, (0, CombatScenes.HeavyInfantry, g.CellCenter(m.Starts[0] % s, m.Starts[0] / s)))[0];
        block?.Invoke(sim, -1);
        Vector2 goal = g.CellCenter(m.Starts[1] % s, m.Starts[1] / s);
        sim.Enqueue(Command.Move(0, unit, goal));
        int last = -1, ford = -1;
        for (int t = 0; t < limit; t++)
        {
            sim.Tick();
            Assert.True(g.WorldToCell(w.Units.Position[unit.Index], out int x, out int y));
            int cell = y * s + x;
            if (last >= 0 && cell != last && ford < 0 && g.LevelAt(last % s, last / s) == 1 && (g.FlagsAt(x, y) & NavFlags.Ramp) != 0)
                ford = FordOf(m, x);
            last = cell;
            if (Vector2.Distance(w.Units.Position[unit.Index], goal) < 6f) return (ford, sim.TickNumber);
        }
        return (ford, -1);
    }

    /// <summary>
    /// Fords as chokepoints: the unit from start 0 to start 1 goes down one ford; with that ford's north mouth sealed by a
    /// wall of buildings it goes down another and still arrives; with the two nearest sealed, a third. The walls are checked
    /// to have been placed (the nav grid blocked across the ramp's top).
    /// </summary>
    [Fact]
    public void ASealedFord_SendsTheWalkToAnotherFord()
    {
        MapDef m = Def(Vathar);
        (int open, int openAt) = WalkAcross(null);
        _out.WriteLine($"open: ford {open}, arrived at tick {openAt}");
        Assert.True(open >= 0 && openAt > 0, "the open walk took no ford or never arrived");
        (int second, int secondAt) = WalkAcross((sim, _) => Seal(sim, m, open));
        _out.WriteLine($"ford {open} sealed: ford {second}, arrived at tick {secondAt}");
        Assert.True(second >= 0 && second != open, $"with ford {open} sealed the walk took ford {second}");
        Assert.True(secondAt > 0, "with one ford sealed the unit never arrived");
        (int third, int thirdAt) = WalkAcross((sim, _) => { Seal(sim, m, open); Seal(sim, m, second); });
        _out.WriteLine($"fords {open} and {second} sealed: ford {third}, arrived at tick {thirdAt}");
        Assert.True(third >= 0 && third != open && third != second && thirdAt > 0, $"with two fords sealed: ford {third}, arrived {thirdAt}");
    }

    /// <summary>
    /// Seals ford <paramref name="ford"/>'s north ramp from the north bank: 2 x 2 buildings (the walker's own, player 0's: a
    /// player with buildings and no unit is defeated and its buildings go) over the bank rows above its top, wide of the ramp.
    /// </summary>
    private static void Seal(Simulation sim, MapDef m, int ford)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int s = m.Size;
        MapRampDef r = m.Ramps.Where(q => FordOf(m, q.X) == ford).OrderBy(q => q.Y).First(); // the north one
        int billet = TestSim.Data.FindBuilding("malazan_billet");
        int placed = 0;
        for (int y = r.Y - 4; y < r.Y; y += 2)
            for (int x = r.X - 4; x < r.X + r.Width + 4; x += 2)
                if (w.Buildings.Spawn(0, billet, y * s + x, out _)) placed++;
        // The ramp's top row is reachable from the north bank only through the row above it: every cell there now blocked.
        for (int x = r.X - 1; x <= r.X + r.Width; x++)
            Assert.False(g.IsPassable(x, r.Y - 1) && g.LevelAt(x, r.Y - 1) == 1 && (g.FlagsAt(x, r.Y - 1) & NavFlags.Building) == 0 && (g.FlagsAt(x, r.Y - 1) & NavFlags.Cliff) == 0,
                $"ford {ford}: bank cell ({x}, {r.Y - 1}) left open ({placed} billets placed)");
    }

    /// <summary>
    /// A wall of units on the ford: 24 Heavy Infantry of player 0 holding on the open walk's north ramp cells. Measured
    /// (printed): whether the walker still arrives and through which ford. Asserted: it arrives within the limit (a wall of
    /// own units is not a seal: units don't block the path, so it must push through or go round, never stall for good).
    /// </summary>
    [Fact]
    public void AWallOfOwnUnitsOnTheFord_TheWalkerStillArrives()
    {
        MapDef m = Def(Vathar);
        (int open, int openAt) = WalkAcross(null);
        (int ford, int at) = WalkAcross((sim, _) =>
        {
            NavGrid g = sim.World.NavGrid;
            MapRampDef r = m.Ramps.Where(q => FordOf(m, q.X) == open).OrderBy(q => q.Y).First();
            var wall = new List<(int, int, Vector2)>();
            for (int y = r.Y; y < r.Y + r.Height; y++)
                for (int x = r.X; x < r.X + r.Width; x++)
                    wall.Add((0, CombatScenes.HeavyInfantry, g.CellCenter(x, y)));
            foreach (EntityHandle h in CombatScenes.Spawn(sim, wall.ToArray())) sim.Enqueue(Command.HoldPosition(0, h));
        });
        _out.WriteLine($"open walk: ford {open} at {openAt}; with 24 holding units on ford {open}'s north ramp: ford {ford}, arrived {at}");
        Assert.True(at > 0, "the walker never arrived past a wall of own holding units");
    }

    /// <summary>
    /// High ground at the river: in every 10th column off the fords, a Laborer of player 0 on the band 1-2 cells from the
    /// north (and the south) bank's cliff does not see a Laborer of player 1 on the bank 8-10 m away (inside its 14 m
    /// sight); the bank one sees it. A ford's ramp is excluded (a ramp joins the levels).
    /// </summary>
    [Fact]
    public void TheBand_CannotSeeTheBank_TheBankSeesTheBand()
    {
        MapDef m = Def(Vathar);
        int s = m.Size, pairs = 0;
        Simulation sim = Bare(Vathar, 256);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        foreach (bool north in new[] { true, false })
            for (int x = 6; x < s - 6; x += 10)
            {
                if (FordOf(m, x) >= 0 || FordOf(m, x - 3) >= 0 || FordOf(m, x + 3) >= 0) continue;
                // The band's first passable level-0 cell from the bank, walking toward the river's middle.
                int step = north ? 1 : -1, y = north ? 100 : 155;
                while (g.LevelAt(x, y) != 0) y += step;
                int band = y;
                while (!g.IsPassable(x, band) && Math.Abs(band - y) < 4) band += step;
                band += step; // one more cell into the band
                int bank = y - step;
                while (!g.IsPassable(x, bank) || g.LevelAt(x, bank) != 1) bank -= step;
                bank -= step;
                if (!g.IsPassable(x, bank) || g.LevelAt(x, bank) != 1 || !g.IsPassable(x, band)) continue;
                float d = Vector2.Distance(g.CellCenter(x, band), g.CellCenter(x, bank));
                if (d <= 4.5f || d > 12f) continue;
                EntityHandle low = CombatScenes.Spotter(sim, 0, g.CellCenter(x, band));
                EntityHandle high = CombatScenes.Spotter(sim, 1, g.CellCenter(x, bank));
                w.Fog.Update();
                bool lowSees = w.Fog.CanSeeUnit(0, high.Index), highSees = w.Fog.CanSeeUnit(1, low.Index);
                Assert.False(lowSees, $"column {x} ({(north ? "north" : "south")} bank): the band unit at row {band} sees the bank unit at row {bank} ({d:F1} m)");
                Assert.True(highSees, $"column {x}: the bank unit at row {bank} doesn't see the band unit at row {band} ({d:F1} m)");
                w.Units.Free(low);
                w.Units.Free(high);
                pairs++;
            }
        _out.WriteLine($"{pairs} band / bank pairs checked");
        Assert.True(pairs >= 30, $"only {pairs} pairs checked");
    }

    // ---------- waves through the chokepoints ----------

    /// <summary>
    /// 20 Heavy Infantry of player 0 in a 5 x 4 block at <paramref name="from"/>'s start, all moved to start
    /// <paramref name="to"/>; returns the tick each came within 16 m of the goal (-1: never by <paramref name="limit"/>).
    /// </summary>
    private static int[] Wave(Simulation sim, int from, int to, int limit)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int s = g.Width;
        int fc = w.StartLocations[from].Cell, tc = w.StartLocations[to].Cell;
        var spawn = new List<(int, int, Vector2)>();
        for (int k = 0; k < 20; k++)
            spawn.Add((0, CombatScenes.HeavyInfantry, g.CellCenter(fc % s - 3 + (k % 5) * 1, fc / s - 2 + k / 5) + new Vector2(0.3f * (k % 2), 0f)));
        EntityHandle[] units = CombatScenes.Spawn(sim, spawn.ToArray());
        Vector2 goal = g.CellCenter(tc % s, tc / s);
        foreach (EntityHandle h in units) sim.Enqueue(Command.Move(0, h, goal));
        int[] arrived = Enumerable.Repeat(-1, units.Length).ToArray();
        int start = sim.TickNumber;
        while (sim.TickNumber - start < limit && arrived.Any(a => a < 0))
        {
            sim.Tick();
            for (int k = 0; k < units.Length; k++)
                if (arrived[k] < 0 && Vector2.Distance(w.Units.Position[units[k].Index], goal) <= 16f) arrived[k] = sim.TickNumber - start;
        }
        return arrived;
    }

    /// <summary>
    /// BUG-0889-style jams in Pale Hills' corridors and Vathar's fords: a 20-unit wave from start 0 to its neighbour's start
    /// (a corridor), to the diagonal start (start 1) and, on Vathar, across a ford. The same wave on a flat 256 map between
    /// the same cells is the baseline. Asserted: all 20 arrive within 16 m; the last no later than twice the flat run's last
    /// plus 30 s (a jam at a tree wall or a ramp would hold units for minutes). Printed: first / last arrivals.
    /// </summary>
    [Theory]
    [InlineData(PaleHills, 0, 2)]
    [InlineData(PaleHills, 0, 3)]
    [InlineData(PaleHills, 0, 1)]
    [InlineData(Vathar, 0, 2)]
    [InlineData(Vathar, 0, 1)]
    [InlineData(Vathar, 0, 3)]
    public void ATwentyUnitWave_ThroughTheMap_ArrivesWithoutAJam(string id, int from, int to)
    {
        const int limit = 12_000;
        int[] map = Wave(Bare(id), from, to, limit);
        // The baseline: a flat 256 map with the same start cells (as a map with no terrain and no nodes).
        MapDef m = Def(id);
        var flat = new Simulation(new SimConfig(3, 2, 128, 512) { Data = TestSim.Data }, LocalMovementTests.Flat(m.Size));
        int fc = m.Starts[from], tc = m.Starts[to], s = m.Size;
        EntityHandle probe = CombatScenes.Spawn(flat, (1, CombatScenes.HeavyInfantry, flat.World.NavGrid.CellCenter(1, 1)))[0];
        flat.World.Units.Free(probe);
        int[] baseline = WaveOnFlat(flat, fc, tc, limit);
        int mapLast = map.Max(), flatLast = baseline.Max();
        _out.WriteLine($"{id} start {from} -> {to}: arrived {map.Count(a => a >= 0)}/20, first {map.Where(a => a >= 0).DefaultIfEmpty(-1).Min()} last {mapLast} ticks; flat baseline first {baseline.Min()} last {flatLast}");
        Assert.True(map.All(a => a >= 0), $"{map.Count(a => a < 0)} of 20 never arrived within {limit} ticks");
        Assert.True(mapLast <= 2 * flatLast + 600, $"last arrival {mapLast} ticks against the flat run's {flatLast}");
    }

    private static int[] WaveOnFlat(Simulation sim, int fromCell, int toCell, int limit)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int s = g.Width;
        var spawn = new List<(int, int, Vector2)>();
        for (int k = 0; k < 20; k++)
            spawn.Add((0, CombatScenes.HeavyInfantry, g.CellCenter(fromCell % s - 3 + k % 5, fromCell / s - 2 + k / 5) + new Vector2(0.3f * (k % 2), 0f)));
        EntityHandle[] units = CombatScenes.Spawn(sim, spawn.ToArray());
        Vector2 goal = g.CellCenter(toCell % s, toCell / s);
        foreach (EntityHandle h in units) sim.Enqueue(Command.Move(0, h, goal));
        int[] arrived = Enumerable.Repeat(-1, units.Length).ToArray();
        int start = sim.TickNumber;
        while (sim.TickNumber - start < limit && arrived.Any(a => a < 0))
        {
            sim.Tick();
            for (int k = 0; k < units.Length; k++)
                if (arrived[k] < 0 && Vector2.Distance(w.Units.Position[units[k].Index], goal) <= 16f) arrived[k] = sim.TickNumber - start;
        }
        return arrived;
    }

    // ---------- mirror-map round trip ----------

    private static string Source(string id) => File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), "tools", "Rts.Cli", "maps", $"{id}.quad.json"));

    private static string[] TerrainOf(string mapJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(mapJson);
        return doc.RootElement.GetProperty("terrain").EnumerateArray().Select(r => r.GetString()!).ToArray();
    }

    /// <summary>
    /// One source cell changed (quad): the output differs from the shipped file in exactly that cell's four mirror cells,
    /// each now the new level; nothing else in the terrain moves. A cell on the quarter's inner edge (x = 127) lands on
    /// two adjacent columns (127 and 128), as the symmetry wants.
    /// </summary>
    [Theory]
    [InlineData(Vathar, 5, 7)]
    [InlineData(Vathar, 127, 30)]
    [InlineData(PaleHills, 20, 127)]
    public void MirrorMap_OneSourceCellMutated_ChangesExactlyItsFourMirrors(string id, int x, int y)
    {
        var src = System.Text.Json.Nodes.JsonNode.Parse(Source(id))!;
        var rows = src["terrain"]!.AsArray();
        string row = rows[y]!.GetValue<string>();
        char was = row[x], now = was == '2' ? '1' : (char)(was + 1);
        rows[y] = row[..x] + now + row[(x + 1)..];
        string? shipped = Rts.Cli.MirrorMap.Mirror(Source(id), true, 2, 2, out string e0);
        string? mutated = Rts.Cli.MirrorMap.Mirror(src.ToJsonString(), true, 2, 2, out string e1);
        Assert.True(shipped != null, e0);
        Assert.True(mutated != null, e1);
        Assert.Equal(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "maps", $"{id}.json")).ReplaceLineEndings("\n"), shipped!.ReplaceLineEndings("\n"));
        string[] a = TerrainOf(shipped), b = TerrainOf(mutated!);
        int s = a.Length;
        var expected = new HashSet<(int, int)> { (x, y), (s - 1 - x, y), (x, s - 1 - y), (s - 1 - x, s - 1 - y) };
        var diff = new HashSet<(int, int)>();
        for (int yy = 0; yy < s; yy++)
            for (int xx = 0; xx < s; xx++)
                if (a[yy][xx] != b[yy][xx])
                {
                    diff.Add((xx, yy));
                    Assert.Equal(now, b[yy][xx]);
                }
        Assert.Equal(expected.OrderBy(p => p).ToArray(), diff.OrderBy(p => p).ToArray());
    }

    /// <summary>
    /// A source ramp's side flips with each mirror (quad): north becomes south, north, south (centre, east-west,
    /// north-south); east becomes west, west, east. A changed side changes only that ramp's four entries.
    /// </summary>
    [Theory]
    [InlineData("north", "north", "south", "north", "south")]
    [InlineData("south", "south", "north", "south", "north")]
    [InlineData("east", "east", "west", "west", "east")]
    [InlineData("west", "west", "east", "east", "west")]
    public void MirrorMap_ARampsSide_FlipsWithEachMirror(string up, string asIs, string centre, string eastWest, string northSouth)
    {
        var src = System.Text.Json.Nodes.JsonNode.Parse(Source(Vathar))!;
        src["ramps"]![0]!["up"] = up;
        string? text = Rts.Cli.MirrorMap.Mirror(src.ToJsonString(), true, 2, 2, out string error);
        Assert.True(text != null, error);
        var outRamps = System.Text.Json.Nodes.JsonNode.Parse(text!)!["ramps"]!.AsArray();
        Assert.Equal(new[] { asIs, centre, eastWest, northSouth }, outRamps.Take(4).Select(r => r!["up"]!.GetValue<string>()).ToArray());
        var shippedRamps = System.Text.Json.Nodes.JsonNode.Parse(Rts.Cli.MirrorMap.Mirror(Source(Vathar), true, 2, 2, out _)!)!["ramps"]!.AsArray();
        for (int k = 4; k < outRamps.Count; k++) Assert.Equal(shippedRamps[k]!.ToJsonString(), outRamps[k]!.ToJsonString());
    }

    /// <summary>
    /// The tool through the CLI with the out path the same as the in path: the source is replaced by the whole map (the
    /// tool reads, then writes the one path it was given). Pinned as measured: running it twice then refuses (a 256-row
    /// terrain is not a 128-row quarter), so the damage is visible, not silent.
    /// </summary>
    [Fact]
    public void MirrorMap_OutPathEqualToInPath_OverwritesTheSource_AndASecondRunRefuses()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"rts-qa-mirror-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "v.quad.json");
        try
        {
            File.WriteAllText(path, Source(Vathar));
            var o = new StringWriter();
            var e = new StringWriter();
            int first = Rts.Cli.CliRunner.Run(new[] { "mirror-map", path, path, "--symmetry", "quad", "--data", TestDataDir.Shipped }, o, e);
            _out.WriteLine($"first run exit {first}: {o}{e}");
            Assert.Equal(0, first);
            Assert.Equal(256, TerrainOf(File.ReadAllText(path)).Length);
            var e2 = new StringWriter();
            int second = Rts.Cli.CliRunner.Run(new[] { "mirror-map", path, path, "--symmetry", "quad", "--data", TestDataDir.Shipped }, new StringWriter(), e2);
            _out.WriteLine($"second run exit {second}: {e2}");
            Assert.Equal(1, second);
            Assert.Contains("terrain has 256 rows", e2.ToString());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ---------- BUG-1057 on a 256 map ----------

    private static readonly PlayerSeat[] s_seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };

    /// <summary>
    /// A flat 256 x 256 map, not explored, combat on: player 0's building (its Town Hall, or with <paramref name="billetOnly"/>
    /// a Billet, 2 x 2, and no hall) with its centre on cell corner (<paramref name="cx"/>, <paramref name="cy"/>), player 1's
    /// hall at (120, 100); each with two mines, 12 trees and five workers beside it. With <c>ai.scoutGrid</c> 56 m a 512 m
    /// side is 10 grid cells of 51.2 m, so (64, 64), (192, 64), (64, 192) and (192, 192) cells are grid centres.
    /// </summary>
    private static Simulation Big(int cx, int cy, bool billetOnly = false)
    {
        const int size = 256;
        GameData data = TestSim.Data;
        var config = new SimConfig(7, s_seats.Length, 256, 512) { Data = data, Seats = s_seats.ToImmutableArray(), Combat = true };
        var sim = new Simulation(config, LocalMovementTests.Flat(size));
        World w = sim.World;
        int mine = data.FindResource("gold_mine"), tree = data.FindResource("tree");
        for (int p = 0; p < 2; p++)
        {
            int type = p == 0 && billetOnly ? data.FindBuilding("malazan_billet") : StartBase.BuildingOfSlot(data, s_seats[p].Faction, BuildingSlot.TownHall);
            BuildingDef d = data.Buildings[type];
            (int x, int y) = p == 0 ? (cx - d.FootprintWidth / 2, cy - d.FootprintHeight / 2) : (120, 100);
            Assert.True(w.Buildings.Spawn(p, type, y * size + x, out _));
            int toward = cx < 128 ? 1 : -1;
            for (int m = 0; m < 2; m++)
                Assert.True(w.Resources.Spawn(mine, (y - 6) * size + x + toward * (2 + 3 * m), data.Rules.StartMineGold, out _));
            for (int t = 0; t < 12; t++)
                Assert.True(w.Resources.Spawn(tree, (y + 9 + t / 4) * size + x + t % 4, data.Rules.TreeWood, out _));
            int worker = StartBase.UnitOfSlot(data, s_seats[p].Faction, UnitSlot.Worker);
            for (int k = 0; k < 5; k++)
                CombatScenes.Place(sim, p, worker, w.NavGrid.CellCenter(x + k % 4, y + 5 + k / 4));
        }
        return sim;
    }

    /// <summary>Kills player 0's scout if it is out and alive; whether one was killed.</summary>
    private static bool KillScout(Simulation sim)
    {
        Blackboard b = sim.AiOf(0)!.Board;
        if (b.ScoutPhase != 1) return false;
        var h = new EntityHandle(b.ScoutSlot, b.ScoutGeneration);
        if (!sim.World.Units.IsAlive(h)) return false;
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, h, false, sim.World.Units.Hp[h.Index]));
        return true;
    }

    /// <summary>
    /// BUG-1057 at each of a 256 map's four quarter points (and the control one cell off): every scout of player 0 killed at
    /// the door until tick 8,000 costs at most <c>ai.scoutReplacements</c> + 1 (3) scouts, and the sweep ends with the reach
    /// lifted. Also asserts the setup: the hall's centre is one of the scout grid's points.
    /// </summary>
    [Theory]
    [InlineData(64, 64)]
    [InlineData(192, 64)]
    [InlineData(64, 192)]
    [InlineData(192, 192)]
    [InlineData(65, 64)]
    public void EveryScoutKilledAtTheDoor_AQuarterPointHallOnA256Map_LosesAtMostThree(int cx, int cy)
    {
        Simulation sim = Big(cx, cy);
        AiPlayer ai = sim.AiOf(0)!;
        Vector2 hall = ReachRuleQaTests.HallCentre(sim.World, 0);
        Assert.Equal(new Vector2(cx * MapConstants.CellSize, cy * MapConstants.CellSize), hall);
        int killed = 0, marked = 0;
        bool onGrid = false;
        while (sim.TickNumber < 8000)
        {
            sim.Tick();
            if (ai.Board.ScoutPhase == 1 && !onGrid) onGrid = ai.Board.ScoutPoints.Skip(Blackboard.ScoutHead).Any(p => p == hall) || ai.Board.ScoutPoints.Any(p => Vector2.Distance(p, hall) < 0.6f);
            if (ai.Board.ScoutPhase == 1 && ScoutManager.SentAgain(ai) && sim.World.Units.IsAlive(new EntityHandle(ai.Board.ScoutSlot, ai.Board.ScoutGeneration))) marked++;
            if (KillScout(sim)) killed++;
        }
        _out.WriteLine($"hall centre {hall}: killed {killed}, marked-alive ticks {marked}, phase {ai.Board.ScoutPhase}, lifted {ai.ReachLifted()}, a grid point at the hall {onGrid}");
        if (cx % 64 == 0 && cy % 64 == 0) Assert.True(onGrid, "setup: the hall's centre is no scout grid point");
        Assert.True(killed >= 1, "setup: no scout ever went out");
        Assert.True(killed <= ai.Rules.ScoutReplacements + 1, $"{killed} scouts fed to the door");
        Assert.Equal(2, ai.Board.ScoutPhase);
    }

    /// <summary>
    /// BUG-1057 with the hall rebuilt: the quarter-point hall (64, 64) loses its first scout; the hall is destroyed and the
    /// same hall rebuilt on the same cell (a new slot, same cell); every scout killed at the door after. At most 3 lost.
    /// </summary>
    [Fact]
    public void EveryScoutKilledAtTheDoor_AfterTheQuarterPointHallIsRebuilt_LosesAtMostThree()
    {
        Simulation sim = Big(64, 64);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        int killed = 0;
        while (ai.Board.ScoutPhase != 1 && sim.TickNumber < 4000) sim.Tick();
        Assert.True(KillScout(sim));
        killed++;
        int hallType = StartBase.BuildingOfSlot(TestSim.Data, s_seats[0].Faction, BuildingSlot.TownHall);
        int slot = Enumerable.Range(0, w.Buildings.Capacity).First(i => w.Buildings.Alive[i] && w.Buildings.Owner[i] == 0 && w.Buildings.TypeId[i] == hallType);
        int cell = w.Buildings.Cell[slot];
        Assert.True(w.Buildings.Free(new EntityHandle(slot, w.Buildings.Generation[slot])));
        sim.Tick();
        Assert.True(w.Buildings.Spawn(0, hallType, cell, out EntityHandle rebuilt));
        Assert.Equal(cell, w.Buildings.Cell[rebuilt.Index]);
        bool sawMark = false;
        while (sim.TickNumber < 8000)
        {
            sim.Tick();
            if (ai.Board.ScoutPhase == 1 && ScoutManager.SentAgain(ai)) sawMark = true;
            if (KillScout(sim)) killed++;
        }
        _out.WriteLine($"rebuilt hall: killed {killed}; saw a marked scout {sawMark}; phase {ai.Board.ScoutPhase}; lifted {ai.ReachLifted()}");
        Assert.True(killed <= ai.Rules.ScoutReplacements + 1, $"{killed} scouts fed to the door after the rebuild");
        Assert.Equal(2, ai.Board.ScoutPhase);
    }

    /// <summary>
    /// BUG-1057 through <c>TryHallPoint</c>'s first-building fallback: player 0 has no hall, only a Billet (2 x 2) whose
    /// centre is the grid centre (64, 64) m x 2; every scout killed at the door until 8,000: at most 3 lost.
    /// </summary>
    [Fact]
    public void EveryScoutKilledAtTheDoor_NoHall_ABilletOnAGridCentre_LosesAtMostThree()
    {
        Simulation sim = Big(64, 64, billetOnly: true);
        AiPlayer ai = sim.AiOf(0)!;
        Vector2 centre = new(128f, 128f);
        int killed = 0;
        bool hallPoint = false;
        while (sim.TickNumber < 8000)
        {
            sim.Tick();
            if (!hallPoint && ai.TryHallPoint(out Vector2 p)) hallPoint = p == centre;
            if (KillScout(sim)) killed++;
        }
        _out.WriteLine($"billet only: TryHallPoint at the grid centre {hallPoint}; killed {killed}; phase {ai.Board.ScoutPhase}; lifted {ai.ReachLifted()}");
        Assert.True(hallPoint, "setup: TryHallPoint did not fall back to the billet's centre (128, 128)");
        Assert.True(killed >= 1, "setup: no scout went out from the billet");
        Assert.True(killed <= ai.Rules.ScoutReplacements + 1, $"{killed} scouts fed to the door");
    }

    /// <summary>SentAgain on the 256 map's quarter-point hall with a scout sent again out allocates nothing.</summary>
    [Fact]
    public void SentAgain_OnA256QuarterPointHall_AllocatesNothing()
    {
        Simulation sim = Big(192, 192);
        AiPlayer ai = sim.AiOf(0)!;
        while (ai.Board.ScoutPhase != 1 && sim.TickNumber < 4000) sim.Tick();
        Assert.True(KillScout(sim));
        for (int t = 0; t < 400 && ai.Board.ScoutPhase == 1; t++) sim.Tick();
        while (ai.Board.ScoutPhase != 1 && sim.TickNumber < 8000) sim.Tick();
        Assert.Equal(1, ai.Board.ScoutPhase);
        int sink = 0;
        Action probe = () => { if (ScoutManager.SentAgain(ai)) sink++; };
        probe();
        AllocationProbe.AssertZero(probe, _out);
        Assert.True(sink >= 1, "the replacement read unmarked");
    }
}
