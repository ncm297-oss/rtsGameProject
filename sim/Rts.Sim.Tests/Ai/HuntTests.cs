using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4d criterion 3 (and 5's save row): the hunt (<see cref="MilitaryManager"/>) on the flat scene, combat off, judged by the
/// AI's command log. Player 0 (AI, Malazan, Normal) knows no enemy building: player 1's base (human, Whirlwind) is out of its
/// sight, and the map is only explored, which shows no building. A wave is marked as sent before (the hunt's precondition)
/// by setting <see cref="Blackboard.WavesStarted"/>; the army is Heavy Infantry placed by the test.
/// </summary>
public class HuntTests
{
    private readonly ITestOutputHelper _out;

    public HuntTests(ITestOutputHelper output) => _out = output;

    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    private static int Raider => TestSim.Data.FindUnit("whirlwind_raider");

    /// <summary>A quiet Malazan order (one worker at 3,600 s) and a wave threshold no test army reaches.</summary>
    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
                o["attack"] = new JsonObject { ["minArmyPop"] = 200, ["minArmyCost"] = 0 };
            }
        })));

    private static Simulation Scene() =>
        AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_data.Value);

    private static List<EntityHandle> Army(Simulation sim, int n, int x, int y)
    {
        var list = new List<EntityHandle>();
        for (int k = 0; k < n; k++) list.Add(CombatScenes.Place(sim, 0, Heavy, sim.World.NavGrid.CellCenter(x + k % 6, y + k / 6)));
        return list;
    }

    /// <summary>Ticks to the next think of player 0 and through it (its commands are logged by then).</summary>
    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    /// <summary>
    /// The sweep's points, written out again from the map size and <c>ai.scoutGrid</c>: the grid's cell centres in row order,
    /// then its interior corners in row order, then (BUG-1107) the border ring in row order (the top edge's two corners and its
    /// cell centres, each cell row's west and east ends, the bottom edge likewise), half a map cell inside the border.
    /// </summary>
    private static Vector2[] GridPoints(Simulation sim)
    {
        float size = AiScenes.FlatSize * MapConstants.CellSize, m = MapConstants.CellSize * 0.5f, half;
        int lines = (int)MathF.Ceiling(size / sim.AiOf(0)!.Rules.ScoutGrid), inner = lines - 1;
        half = size / (2 * lines);
        var points = new List<Vector2>();
        for (int k = 0; k < lines * lines; k++)
            points.Add(new Vector2((k % lines + 0.5f) * size / lines, (k / lines + 0.5f) * size / lines));
        for (int j = 0; j < inner * inner; j++)
            points.Add(new Vector2((j % inner + 1) * size / lines, (j / inner + 1) * size / lines));
        Vector2 Ring(int hx, int hy) => Vector2.Clamp(new Vector2(hx * half, hy * half), new Vector2(m), new Vector2(size - m));
        foreach (int edge in new[] { 0, 2 * lines })
        {
            if (edge > 0)
                for (int hy = 1; hy < 2 * lines; hy += 2)
                {
                    points.Add(Ring(0, hy));
                    points.Add(Ring(2 * lines, hy));
                }
            points.Add(Ring(0, edge));
            for (int hx = 1; hx < 2 * lines; hx += 2) points.Add(Ring(hx, edge));
            points.Add(Ring(2 * lines, edge));
        }
        return points.ToArray();
    }

    /// <summary>
    /// BUG-1107: the sweep's points are the list <see cref="GridPoints"/> writes out (count and order), and every cell of the
    /// map, the border band and the corners included, lies within half a scout cell of one (plus a map cell of slack for the
    /// clamp and the cell centre), and each corner cell within a map cell of one. Before the ring a corner cell was half a
    /// cell's diagonal (30 m on this map) from any point.
    /// </summary>
    [Fact]
    public void TheSweepPoints_CoverTheBorderBandAndTheCorners()
    {
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        Vector2[] expected = GridPoints(sim);
        int cols = ScoutManager.GridLines(AiScenes.FlatSize, ai.Rules.ScoutGrid);
        Assert.Equal(expected.Length, MilitaryManager.HuntPoints(cols, cols));
        Assert.Equal(expected.Length, ai.Board.HuntVisited.Length);
        for (int k = 0; k < expected.Length; k++)
            Assert.True(Vector2.Distance(expected[k], MilitaryManager.HuntPoint(ai, cols, cols, k)) < 1e-3f, $"point {k}");
        float cell = AiScenes.FlatSize * MapConstants.CellSize / cols;
        for (int y = 0; y < AiScenes.FlatSize; y++)
            for (int x = 0; x < AiScenes.FlatSize; x++)
            {
                Vector2 at = sim.World.NavGrid.CellCenter(x, y);
                float best = float.MaxValue;
                for (int k = 0; k < expected.Length; k++) best = Math.Min(best, Vector2.Distance(at, expected[k]));
                bool corner = (x == 0 || x == AiScenes.FlatSize - 1) && (y == 0 || y == AiScenes.FlatSize - 1);
                float limit = corner ? MapConstants.CellSize : cell * 0.5f + MapConstants.CellSize;
                Assert.True(best <= limit, $"cell ({x}, {y}) is {best:F1} m from the nearest sweep point (limit {limit:F1})");
            }
    }

    /// <summary>
    /// BUG-1107 fix round 2: on an odd pass the interior points are the cell edges' midpoints (the even pass's worst spots,
    /// half a cell from four points: QA's interior hide, 25 m from the nearest point, never seen on the generated map) in
    /// <see cref="MilitaryManager.HuntPoint"/>'s order, the one spare index the map's centre; the ring is the same; pass 2
    /// is pass 0 again. Together the two passes' interior points are every point of the half-cell lattice. A sweep with
    /// every point visited clears its marks and counts the pass (<see cref="MilitaryManager.NextSweep"/>).
    /// </summary>
    [Fact]
    public void AnOddPass_SweepsTheCellEdgesMidpoints()
    {
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        Vector2[] even = GridPoints(sim);
        int cols = ScoutManager.GridLines(AiScenes.FlatSize, ai.Rules.ScoutGrid), first = MilitaryManager.RingStart(cols, cols);
        float size = AiScenes.FlatSize * MapConstants.CellSize, cell = size / cols;
        var expected = new List<Vector2>();
        for (int y = 0; y < cols; y++)
            for (int x = 0; x < cols - 1; x++) expected.Add(new Vector2((x + 1) * cell, (y + 0.5f) * cell));
        for (int y = 0; y < cols - 1; y++)
            for (int x = 0; x < cols; x++) expected.Add(new Vector2((x + 0.5f) * cell, (y + 1) * cell));
        expected.Add(new Vector2(size / 2, size / 2));
        Assert.Equal(first, expected.Count);
        ai.Board.HuntPass = 1;
        for (int k = 0; k < even.Length; k++)
        {
            Vector2 want = k < first ? expected[k] : even[k];
            Assert.True(Vector2.Distance(want, MilitaryManager.HuntPoint(ai, cols, cols, k)) < 1e-3f, $"odd pass, point {k}");
        }
        for (int i = 1; i < 2 * cols; i++)
            for (int j = 1; j < 2 * cols; j++)
            {
                var lattice = new Vector2(i * cell / 2, j * cell / 2);
                bool found = expected.Any(p => Vector2.Distance(p, lattice) < 1e-3f) || even.Take(first).Any(p => Vector2.Distance(p, lattice) < 1e-3f);
                Assert.True(found, $"half-cell lattice point {lattice} is on neither pass");
            }
        ai.Board.HuntPass = 2;
        for (int k = 0; k < even.Length; k++)
            Assert.True(Vector2.Distance(even[k], MilitaryManager.HuntPoint(ai, cols, cols, k)) < 1e-3f, $"pass 2, point {k}");

        ai.Board.HuntPass = 0;
        Array.Fill(ai.Board.HuntVisited, true);
        int next = MilitaryManager.NextSweep(ai, new Vector2(10, 10), -1);
        Assert.Equal(1, ai.Board.HuntPass);
        Assert.DoesNotContain(true, ai.Board.HuntVisited); // cleared: the pick is marked only once reached
        Assert.InRange(next, 0, even.Length - 1);
    }

    /// <summary>
    /// The sweep's rule written out again: the unvisited point nearest <paramref name="from"/>; points within a map cell of
    /// the least such distance tie, and the one nearer <paramref name="origin"/> wins (list order after). QA round 1: the
    /// tie is against the true least distance, not a running best (which let a chain of near-ties drift farther away).
    /// </summary>
    private static int Next(Vector2[] points, bool[] visited, Vector2 from, Vector2 origin, int skip = -1)
    {
        float least = float.MaxValue;
        for (int k = 0; k < points.Length; k++)
            if (!visited[k] && k != skip) least = Math.Min(least, Vector2.Distance(from, points[k]));
        int best = -1;
        float bestOrigin = float.MaxValue;
        for (int k = 0; k < points.Length; k++)
        {
            if (visited[k] || k == skip || Vector2.Distance(from, points[k]) >= least + MapConstants.CellSize) continue;
            float o = Vector2.Distance(origin, points[k]);
            if (o < bestOrigin)
            {
                bestOrigin = o;
                best = k;
            }
        }
        return best;
    }

    /// <summary>The rule QA round 1 found non-transitive (a running best that a near-tie could move up to 2 m farther each time).</summary>
    private static int OldNext(Vector2[] points, bool[] visited, Vector2 from, Vector2 origin)
    {
        int best = -1;
        float bestDistance = float.MaxValue, bestOrigin = float.MaxValue;
        for (int k = 0; k < points.Length; k++)
        {
            if (visited[k]) continue;
            float d = Vector2.Distance(from, points[k]), o = Vector2.Distance(origin, points[k]);
            if (d > bestDistance - MapConstants.CellSize && (d >= bestDistance + MapConstants.CellSize || o >= bestOrigin)) continue;
            bestDistance = d;
            bestOrigin = o;
            best = k;
        }
        return best;
    }

    /// <summary>
    /// The ring's clockwise order written out again from where the points stand: each ring point's position along the
    /// border (north edge west to east, east edge north to south, south edge east to west, west edge south to north); the
    /// next is the one just past it, the last wrapping to the first.
    /// </summary>
    private static int RingNextOf(Vector2[] points, int ringFirst, int k)
    {
        float size = AiScenes.FlatSize * MapConstants.CellSize, m = MapConstants.CellSize * 0.5f, s = size;
        float Along(Vector2 p) =>
            p.Y <= m + 0.01f ? p.X : p.X >= size - m - 0.01f ? s + p.Y : p.Y >= size - m - 0.01f ? 3 * s - p.X : 4 * s - p.Y;
        int best = -1;
        float bestGap = float.MaxValue;
        for (int j = ringFirst; j < points.Length; j++)
        {
            if (j == k) continue;
            float gap = Along(points[j]) - Along(points[k]);
            if (gap <= 0) gap += 4 * s;
            if (gap < bestGap)
            {
                bestGap = gap;
                best = j;
            }
        }
        return best;
    }

    /// <summary>
    /// QA round 1's tie note: from every 4 m spot of the map, with every point unvisited and the origin at each map corner,
    /// the AI's pick is the true-least rule's (<see cref="Next"/>); and the old running-best rule picks differently somewhere
    /// on this grid (so the comparison bites).
    /// </summary>
    [Fact]
    public void TheSweepTie_IsAgainstTheTrueLeastDistance()
    {
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        Vector2[] grid = GridPoints(sim);
        float size = AiScenes.FlatSize * MapConstants.CellSize;
        int differs = 0, checks = 0;
        foreach (Vector2 origin in new[] { new Vector2(10, 10), new Vector2(size - 10, 10), new Vector2(10, size - 10), new Vector2(size - 10, size - 10) })
        {
            ai.Board.HuntOrigin = origin;
            for (float y = 1; y < size; y += 4)
                for (float x = 1; x < size; x += 4)
                {
                    var none = new bool[grid.Length];
                    var from = new Vector2(x, y);
                    int want = Next(grid, none, from, origin);
                    Assert.True(want == MilitaryManager.NearestUnvisited(ai, ai.Board.HuntVisited, from, -1), $"from {from}, origin {origin}");
                    if (OldNext(grid, none, from, origin) != want) differs++;
                    checks++;
                }
        }
        _out.WriteLine($"{checks} picks checked; the old rule differs on {differs}");
        Assert.True(differs > 0, "the old rule never differs here: the test does not bite");
    }

    /// <summary>
    /// BUG-1107 fix round 2: <see cref="MilitaryManager.RingNext"/> is the clockwise order <see cref="RingNextOf"/> derives
    /// from the points' positions, and following it from any ring point visits every ring point once before coming back.
    /// </summary>
    [Fact]
    public void TheRingOrder_IsClockwiseRoundTheBorder()
    {
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        Vector2[] grid = GridPoints(sim);
        int cols = ScoutManager.GridLines(AiScenes.FlatSize, ai.Rules.ScoutGrid), first = MilitaryManager.RingStart(cols, cols);
        for (int k = first; k < grid.Length; k++)
            Assert.True(RingNextOf(grid, first, k) == MilitaryManager.RingNext(cols, cols, k), $"ring point {k} {grid[k]}");
        var seen = new HashSet<int>();
        int at = first;
        do Assert.True(seen.Add(at = MilitaryManager.RingNext(cols, cols, at)));
        while (at != first);
        Assert.Equal(grid.Length - first, seen.Count);
        // A non-square grid too (the numbering has a row of side points per cell row).
        for (int c = 1; c <= 4; c++)
            for (int r = 1; r <= 4; r++)
            {
                int f = MilitaryManager.RingStart(c, r), n = MilitaryManager.HuntPoints(c, r) - f, steps = 0;
                var once = new HashSet<int>();
                at = f;
                do { Assert.True(once.Add(at = MilitaryManager.RingNext(c, r, at)), $"{c} x {r}: {at} twice"); steps++; }
                while (at != f && steps <= n);
                Assert.Equal(n, steps);
                Assert.All(once, i => Assert.InRange(i, f, f + n - 1));
            }
    }

    private static Vector2 Centroid(Simulation sim, List<EntityHandle> army)
    {
        Vector2 sum = Vector2.Zero;
        foreach (EntityHandle h in army) sum += sim.World.Units.Position[h.Index];
        return sum / army.Count;
    }

    /// <summary>
    /// 3(a): an enemy unit seen once, then gone from sight, is remembered (its position as the view gave it, and the think);
    /// with no enemy building known and a wave sent before, the next think sends every army unit AttackMove to that point.
    /// Before the first wave nothing hunts, however fresh the sighting.
    /// </summary>
    [Fact]
    public void AFreshSighting_WithNoEnemyBuildingKnown_SendsTheArmyToIt()
    {
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        Assert.Equal(0, ai.Board.KnownEnemyBuildings);
        List<EntityHandle> army = Army(sim, 4, 30, 30);
        // In the army's sight, more than ai.defendRadius from player 0's hall (no defense).
        EntityHandle enemy = CombatScenes.Place(sim, 1, Raider, sim.World.NavGrid.CellCenter(37, 30));
        Vector2 seenAt = sim.World.Units.Position[enemy.Index];
        NextThink(sim);
        NextThink(sim);
        Assert.Equal(seenAt, ai.Board.EnemyUnitPoint);
        int seenTick = ai.Board.EnemyUnitTick;
        Assert.True(seenTick > 0);
        Assert.False(ai.Board.WaveActive, "no hunt before a wave was ever sent");
        Assert.Equal(0, ai.Board.HuntsStarted);

        sim.World.Units.Free(enemy);
        NextThink(sim);
        Assert.Equal(seenTick, ai.Board.EnemyUnitTick); // not seen again: kept
        ai.Board.WavesStarted = 1;
        ai.Board.WaveCooldownUntil = 0;
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        Assert.True(ai.Board.WaveActive && ai.Board.Hunting);
        Assert.Equal(1, ai.Board.HuntsStarted);
        Assert.Equal(-1, ai.Board.HuntSweep);
        List<Command> sent = log.Where(c => army.Contains(c.Unit)).ToList();
        Assert.Equal(army.OrderBy(h => h.Index), sent.Select(c => c.Unit));
        Assert.All(sent, c => Assert.Equal(CommandKind.AttackMove, c.Kind));
        Assert.All(sent, c => Assert.Equal(seenAt, c.Position));
    }

    /// <summary>
    /// 3(b): with no sighting the hunt sweeps the scout's grid geometry (cell centres, interior corners, the border ring): the
    /// point nearest the army first, then the unvisited point nearest the last (<see cref="Next"/>; from a ring point, the
    /// perimeter leg, <see cref="RingNextOf"/>), each visited once a member comes within <c>ai.rallyReach</c>; every new
    /// target is one AttackMove per member. An enemy unit coming into sight re-targets the hunt to it at the next think.
    /// </summary>
    [Fact]
    public void WithNoSighting_TheSweepVisitsTheGridInOrder_AndASightingRetargets()
    {
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> army = Army(sim, 4, 18, 28);
        ai.Board.WavesStarted = 1;
        Vector2[] grid = GridPoints(sim);
        var visited = new bool[grid.Length];
        Vector2 origin = Centroid(sim, army);
        var expected = new List<int> { Next(grid, visited, origin, origin) };
        int lines = ScoutManager.GridLines(AiScenes.FlatSize, ai.Rules.ScoutGrid), first = MilitaryManager.RingStart(lines, lines);
        int legNext = -1, legLeft = 0;
        int After(int reached)
        {
            if (legNext >= 0)
            {
                if (reached != legNext) return legNext;
                if (--legLeft > 0) return legNext = RingNextOf(grid, first, reached);
                legNext = -1;
                for (int k = first; k < grid.Length; k++) visited[k] = true;
            }
            else if (reached >= first)
            {
                legLeft = grid.Length - first;
                return legNext = RingNextOf(grid, first, reached);
            }
            return Next(grid, visited, grid[reached], origin, reached);
        }
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        Assert.True(ai.Board.Hunting);
        Assert.Equal(-1, ai.Board.EnemyUnitTick);
        var seen = new List<int>();
        float reach = ai.Rules.RallyReach;
        while (seen.Count < 3 && sim.TickNumber < 20_000)
        {
            int target = ai.Board.HuntSweep;
            Assert.True(target >= 0, $"tick {sim.TickNumber}: not sweeping");
            if (seen.Count == 0 || seen[^1] != target)
            {
                seen.Add(target);
                Assert.True(expected[seen.Count - 1] == target, $"sweep step {seen.Count}: point {target}, want {expected[seen.Count - 1]} (order so far {string.Join(", ", seen)})");
                Assert.Equal(grid[target], ai.Board.WaveTargetPoint);
                // This think's orders: one AttackMove per member, to the point.
                List<Command> orders = log.Where(c => c.Tick == sim.TickNumber && army.Contains(c.Unit)).ToList();
                Assert.Equal(army.OrderBy(h => h.Index), orders.Select(c => c.Unit));
                Assert.All(orders, c => Assert.True(c.Kind == CommandKind.AttackMove && c.Position == grid[target]));
                visited[target] = true;
            }
            NextThink(sim);
            // BUG-1107 fix: a point other than the target whose cell the seat's fog shows visible this think is visited too.
            var view = new PlayerView(sim.World, 0);
            for (int k = 0; k < grid.Length; k++)
            {
                if (visited[k] || k == target) continue;
                int cell = (int)(grid[k].Y / MapConstants.CellSize) * AiScenes.FlatSize + (int)(grid[k].X / MapConstants.CellSize);
                if (view.IsVisible(cell)) visited[k] = true;
            }
            if (ai.Board.HuntSweep != target) expected.Add(After(target));
            Assert.True(ai.Board.HuntSweep == target || visited.SequenceEqual(ai.Board.HuntVisited.Select((v, k) => v || k == target)),
                $"tick {sim.TickNumber}: the AI's marks differ from the visible points plus the targets");
            if (ai.Board.HuntSweep != target)
                Assert.Contains(army, h => Vector2.Distance(sim.World.Units.Position[h.Index], grid[target]) <= reach + 0.5f);
        }
        Assert.Equal(3, seen.Count);
        _out.WriteLine($"sweep order {string.Join(", ", seen)} by tick {sim.TickNumber}");

        // A sighting mid-sweep: an enemy unit beside the army, far from player 0's hall.
        Vector2 by = Centroid(sim, army) + new Vector2(0f, 8f);
        EntityHandle enemy = CombatScenes.Place(sim, 1, Raider, by);
        Vector2 at = sim.World.Units.Position[enemy.Index];
        NextThink(sim);
        if (ai.Board.EnemyUnitTick < 0) NextThink(sim); // a fog update between
        Assert.Equal(at, ai.Board.EnemyUnitPoint);
        Assert.Equal(-1, ai.Board.HuntSweep);
        Assert.Equal(at, ai.Board.WaveTargetPoint);
        Assert.Contains(log, c => c.Kind == CommandKind.AttackMove && c.Position == at && army.Contains(c.Unit));
    }

    /// <summary>Ticks once, then removes player 1's buildings and units (QA's edge scenes do the same).</summary>
    private static void RemovePlayer1(Simulation sim)
    {
        World w = sim.World;
        sim.Tick();
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 1) Assert.True(w.Buildings.Free(new EntityHandle(i, w.Buildings.Generation[i])));
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
    }

    /// <summary>
    /// BUG-1107 fix round 2: an army that reaches a border-ring point walks the whole ring clockwise, every ring point in
    /// turn (visited by sight or not) and back to the one it began at, then goes on nearest-first with every ring point
    /// marked. On the way a member passes within half a unit's sight of each edge spot level with a grid line (the spots
    /// half a scout cell from both neighbouring ring points that nearest-first never walked past).
    /// </summary>
    [Fact]
    public void TheRingLeg_WalksEveryEdgeInOrder_BackToItsStart()
    {
        Simulation sim = Scene();
        RemovePlayer1(sim); // the leg is not cut short by a building coming into view
        CombatScenes.Place(sim, 1, TestSim.Data.FindUnit("whirlwind_camp_follower"), sim.World.NavGrid.CellCenter(44, 32)); // keeps player 1 in: out of sight of the edges and far from player 0's hall (no defense)
        for (int i = 0; i < sim.World.Units.Capacity; i++) // player 0's workers: no scout to spot it and pull the army off the ring
            if (sim.World.Units.Alive[i] && sim.World.Units.Owner[i] == 0) sim.World.Units.Free(new EntityHandle(i, sim.World.Units.Generation[i]));
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> army = Army(sim, 4, 1, 30); // by the west edge: a ring point is the nearest sweep point
        ai.Board.WavesStarted = 1;
        Vector2[] grid = GridPoints(sim);
        int cols = ScoutManager.GridLines(AiScenes.FlatSize, ai.Rules.ScoutGrid), first = MilitaryManager.RingStart(cols, cols);
        float size = AiScenes.FlatSize * MapConstants.CellSize, cell = size / cols, m = MapConstants.CellSize * 0.5f;
        var gaps = new List<Vector2>();
        for (int g = 1; g < cols; g++)
            gaps.AddRange(new[] { new Vector2(g * cell, m), new Vector2(size - m, g * cell), new Vector2(g * cell, size - m), new Vector2(m, g * cell) });
        var nearest = Enumerable.Repeat(float.MaxValue, gaps.Count).ToArray();
        var targets = new List<int>();
        bool legSeen = false;
        while (sim.TickNumber < 30_000)
        {
            sim.Tick();
            int t = ai.Board.HuntSweep;
            if (t >= 0 && (targets.Count == 0 || targets[^1] != t)) targets.Add(t);
            foreach (EntityHandle h in army)
                for (int k = 0; k < gaps.Count; k++) nearest[k] = Math.Min(nearest[k], Vector2.Distance(sim.World.Units.Position[h.Index], gaps[k]));
            if (ai.Board.HuntRing >= 0) legSeen = true;
            else if (legSeen) break;
        }
        _out.WriteLine($"targets {string.Join(", ", targets)} by tick {sim.TickNumber}; hunting {ai.Board.Hunting}, sighting {ai.Board.EnemyUnitTick}");
        Assert.True(legSeen && ai.Board.HuntRing < 0, "the leg never began, or never ended");
        int start = targets[0], n = grid.Length - first;
        Assert.True(start >= first, $"the first target {start} {grid[start]} is not a ring point");
        Assert.True(targets.Count >= n + 2, $"only {targets.Count} targets");
        int want = start;
        for (int i = 1; i <= n; i++)
        {
            want = RingNextOf(grid, first, want);
            Assert.True(targets[i] == want, $"leg step {i}: point {targets[i]}, want {want}");
        }
        Assert.Equal(start, want);
        Assert.True(targets[n + 1] < first, $"after the leg the target {targets[n + 1]} is a ring point again");
        for (int k = first; k < grid.Length; k++) Assert.True(ai.Board.HuntVisited[k], $"ring point {k} not marked after the leg");
        float sight = s_data.Value.Units[Heavy].Sight;
        for (int k = 0; k < gaps.Count; k++)
            Assert.True(nearest[k] <= sight / 2, $"edge spot {gaps[k]}: the nearest member came {nearest[k]:F1} m (sight {sight})");
    }

    /// <summary>
    /// BUG-1107 fix round 2 (regression): a last enemy worker on the map's edge level with a grid line, half a scout cell from
    /// both neighbouring ring points, is seen by a hunt of four Heavy Infantry within 20,000 ticks (combat off: being seen is
    /// what counts). With the perimeter leg switched off the east-edge row (63, 21) is never seen (nearest-first turned
    /// inward at each ring point beside it, 28 of 29 points visited); the other rows happen to lie on its route.
    /// </summary>
    [Theory]
    [InlineData(21, 0)]  // north edge, the first grid line (42.7 m on the 128 m map, 3 x 3 grid)
    [InlineData(0, 42)]  // west edge, the second (85.3 m)
    [InlineData(63, 21)] // east edge, the first
    [InlineData(42, 63)] // south edge, the second
    public void ALastUnitOnTheEdge_LevelWithAGridLine_IsSeen(int x, int y)
    {
        Simulation sim = Scene();
        World w = sim.World;
        int worker = TestSim.Data.FindUnit("whirlwind_camp_follower");
        RemovePlayer1(sim);
        EntityHandle enemy = CombatScenes.Place(sim, 1, worker, w.NavGrid.CellCenter(x, y));
        Army(sim, 4, 18, 34);
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WavesStarted = 1;
        int seenAt = -1;
        while (sim.TickNumber < 20_000 && seenAt < 0)
        {
            sim.Tick();
            if (ai.Board.EnemyUnitTick >= 0) seenAt = sim.TickNumber;
        }
        _out.WriteLine($"unit at {w.Units.Position[enemy.Index]}: seen at tick {seenAt}; visited {ai.Board.HuntVisited.Count(v => v)} / {ai.Board.HuntVisited.Length}");
        Assert.True(ai.Board.HuntsStarted > 0, "no hunt started");
        Assert.True(seenAt >= 0, $"a last unit at cell ({x}, {y}) was never seen in 20,000 ticks");
    }

    /// <summary>
    /// 3(c): an enemy building coming into sight ends the hunt: the next think the wave rule targets it, every member sent
    /// AttackMove to its footprint centre (a wave again, not a hunt).
    /// </summary>
    [Fact]
    public void AKnownEnemyBuilding_EndsTheHunt_AndTheWaveTargetsIt()
    {
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> army = Army(sim, 4, 30, 30);
        ai.Board.WavesStarted = 1;
        NextThink(sim);
        Assert.True(ai.Board.Hunting);
        int tent = s_data.Value.FindBuilding("whirlwind_tent");
        Assert.True(sim.World.Buildings.Spawn(1, tent, 26 * AiScenes.FlatSize + 32, out EntityHandle building));
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        if (ai.Board.KnownEnemyBuildings == 0) NextThink(sim); // a fog update between
        Assert.True(ai.Board.WaveActive);
        Assert.False(ai.Board.Hunting);
        Assert.Equal(-1, ai.Board.HuntSweep);
        Assert.Equal(building, ai.Board.WaveTarget);
        Vector2 centre = ai.BuildingCentre(tent, 26 * AiScenes.FlatSize + 32);
        Assert.Equal(centre, ai.Board.WaveTargetPoint);
        List<Command> sent = log.Where(c => c.Kind == CommandKind.AttackMove && c.Position == centre).ToList();
        Assert.Equal(army.OrderBy(h => h.Index), sent.Select(c => c.Unit).Distinct().OrderBy(h => h.Index));
    }

    /// <summary>The hunt ends like a wave: losing the loss fraction of its start sends the members home, and none starts again before the cooldown.</summary>
    [Fact]
    public void AHunt_EndsLikeAWave_OnTheRetreat()
    {
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> army = Army(sim, 4, 30, 30);
        ai.Board.WavesStarted = 1;
        NextThink(sim);
        Assert.True(ai.Board.Hunting);
        foreach (EntityHandle h in army.Take(3)) sim.World.Units.Free(h);
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        Assert.False(ai.Board.WaveActive);
        Assert.False(ai.Board.Hunting);
        Assert.Contains(log, c => c.Kind == CommandKind.Move && c.Unit == army[3] && c.Position == ai.Board.Rally);
        int until = ai.Board.WaveCooldownUntil;
        Assert.True(until > sim.TickNumber);
        while (sim.TickNumber < until - ai.ThinkTicks)
        {
            NextThink(sim);
            Assert.False(ai.Board.WaveActive);
        }
    }

    /// <summary>
    /// The fog oracle for the memory (criterion 4): at every think of every seat that remembers a sighting from that think,
    /// the remembered point is exactly the position of a live unit of an enemy of the seat that the fog grants it
    /// (<see cref="Vision.FogStore.CanSeeUnit"/>), never a hidden one. A remembered sighting from an earlier think is never
    /// moved. Two AI seats with early waves and raiders in each other's base, so units are seen from the first think on.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    public void TheRememberedPoint_IsAlwaysAUnitTheFogGrantedThatThink(ulong seed)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, seed, record: false, out _, data: s_early.Value, raiders: 2);
        int checks = 0, kept = 0;
        string? bad = null;
        var last = new (Vector2 Point, int Tick)[2];
        for (int p = 0; p < 2; p++)
        {
            int player = p;
            last[p] = (default, -1);
            sim.AiOf(p)!.ThoughtForTests = ai => bad ??= OracleCheck(sim.World, ai, player, ref last[player], ref checks, ref kept);
        }
        for (int t = 0; t < 4000 && bad == null; t++) sim.Tick();
        Assert.True(bad == null, bad);
        _out.WriteLine($"seed {seed}: {checks} sightings checked against the fog, {kept} kept unchanged between thinks");
        Assert.True(checks > 0, "nothing was ever seen");
    }

    private static string? OracleCheck(World w, AiPlayer ai, int p, ref (Vector2 Point, int Tick) last, ref int checks, ref int kept)
    {
        Blackboard b = ai.Board;
        if (b.EnemyUnitTick == ai.Now)
        {
            checks++;
            for (int s = 0; s < w.Units.Capacity; s++)
                if (w.Units.Alive[s] && w.IsEnemy(p, w.Units.Owner[s]) && w.Units.Position[s] == b.EnemyUnitPoint && w.Fog.CanSeeUnit(p, s))
                {
                    last = (b.EnemyUnitPoint, b.EnemyUnitTick);
                    return null;
                }
            return $"tick {ai.Now}: player {p} remembers {b.EnemyUnitPoint}, where no enemy unit it sees stands";
        }
        if (b.EnemyUnitTick >= 0)
        {
            if (b.EnemyUnitTick != last.Tick || b.EnemyUnitPoint != last.Point) return $"tick {ai.Now}: player {p}'s memory moved without a sighting";
            kept++;
        }
        return null;
    }

    private static readonly Lazy<GameData> s_early = new(() => AiScenes.EarlyWaves());

    /// <summary>
    /// Criterion 5 (replays): a recording made before M5-4d of an AI match with no hunt (M6-S7's pre-map fixture, recorded on
    /// ab54ddb, re-recorded in M5-4c slice 2 and D27 for Hard's changed play: two AI seats, 1,600 ticks) still plays back to every checkpoint: playback runs no AI and the format is unchanged.
    /// </summary>
    [Fact]
    public void APreChangeAiRecording_WithNoHunt_PlaysBackAsBefore()
    {
        // The fixture is kept normalized (data hash zeroed, checksum line dropped): sealed again, then given the shipped hash.
        string body = File.ReadAllText(PreMapFixtures.ReplayPath("replay_ai_ffa_standard")).Replace("\r\n", "\n");
        Assert.Equal(Replays.ReplayError.None, Replays.ReplayFormat.TryRead(Replays.ReplayFormat.Seal(body), out Replays.Replay? recorded));
        Replays.ReplayResult played = Replays.ReplayPlayer.Run(ReplayTestRun.WithShippedDataHash(recorded!), TestSim.Data);
        Assert.True(played.Ok, $"{played.Error} at tick {played.Tick}");
    }

    /// <summary>
    /// Criterion 5 (scene row): a save mid-hunt loads into a sim that ticks with the same hash as the original for 1,000
    /// ticks, the AI seats (hunt memory, sweep marks) field for field equal at the end, and save -> load -> save byte-identical.
    /// The save is format 6 (an AI seat on a hand-built map).
    /// </summary>
    [Fact]
    public void ASaveMidHunt_FollowsTheRun_For1000Ticks()
    {
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        Army(sim, 4, 18, 28);
        ai.Board.WavesStarted = 1;
        NextThink(sim);
        for (int k = 0; k < 15; k++) NextThink(sim);
        Assert.True(ai.Board.Hunting);
        Assert.Contains(true, ai.Board.HuntVisited); // the sweep has moved on
        byte[] bytes = SaveTestRun.Save(sim);
        Assert.Equal(Rts.Sim.Save.SaveFormat.HuntMaplessVersion, BitConverter.ToInt32(bytes, Rts.Sim.Save.SaveFormat.MagicLength));
        Simulation load = SaveTestRun.Load(s_data.Value, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        SaveTestRun.AssertSameState(sim, load, "mid-hunt");
        for (int t = 0; t < 1000; t++)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
        SaveTestRun.AssertSameState(sim, load, "mid-hunt + 1,000");
    }
}
