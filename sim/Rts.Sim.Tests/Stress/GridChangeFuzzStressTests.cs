using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Tests.QA;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M3-2b, session 2026-10-06-1744): opening changes (trees felled), closing changes (Keeps dropped by
/// <c>SpawnBuilding</c>, trees grown by the test seam) and move orders interleaved by a seeded stream over
/// 3,000 ticks, then a settle window with no new orders. After every tick: twins hash equal, no unit on
/// blocked ground or non-finite, and after every grid change or field build every field the cache hands
/// out (usable) never points into, or diagonally past, a blocked cell. Units are also spawned onto
/// freshly felled cells and ordered to goals whose fields are cached (the stranded case), and the longest
/// "waiting for a field" run and the settle time are bounded.
/// </summary>
[Collection(SerialCollection.Name)]
public class GridChangeFuzzStressTests
{
    private readonly ITestOutputHelper _out;

    public GridChangeFuzzStressTests(ITestOutputHelper output) => _out = output;

    private sealed class Scene
    {
        public required Simulation Sim;
        public required List<Vector2> Open;
        public required Vector2[] Goals;
        public readonly List<int> Felled = new();
        public int StrandOrders;
    }

    private static Scene NewScene(ulong seed)
    {
        // Combat on: seed 3 found BUG-0150 (a retaliator ping-ponging between two targets at its sight edge forever).
        SimConfig config = TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 160, CommandCapacity: 512)
            with { Map = MapGenParams.Default with { Forests = 16, GoldMines = 2 } };
        var sim = new Simulation(config);
        NavGrid g = sim.World.NavGrid;
        var open = new List<Vector2>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(g.CellCenter(c % g.Width, c / g.Width));
        var rng = new SimRng(seed, 501);
        for (int k = 0; k < 80; k++)
            sim.Enqueue(Command.SpawnUnit(k % 2, k % TestSim.UnitTypeCount, open[rng.NextInt(0, open.Count)]));
        sim.Tick();
        sim.Tick();
        // A fixed pool of goal points (two of them anywhere, so maybe blocked or off the passable ground): at
        // most 24 live goals, under the 32-slot cache, so waits measure M3-2b and not the over-capacity LRU (BUG-0025).
        var goals = new Vector2[24];
        for (int k = 0; k < goals.Length; k++)
            goals[k] = k < 2
                ? new Vector2(1f + rng.NextFloat() * (g.Width - 1) * MapConstants.CellSize, 1f + rng.NextFloat() * (g.Height - 1) * MapConstants.CellSize)
                : open[rng.NextInt(0, open.Count)];
        return new Scene { Sim = sim, Open = open, Goals = goals };
    }

    /// <summary>Issues tick <paramref name="t"/>'s hostile events into one sim (both twins call it with their own RNG of the same seed).</summary>
    private static void Events(Scene s, ref SimRng rng, int t, bool ordersOn, ref int fells, ref int closes, ref int strandSpawns)
    {
        Simulation sim = s.Sim;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        // Opening: fell a random tree (often several per tick in bursts).
        int roll = rng.NextInt(0, 100);
        if (roll < 35)
        {
            List<int> trees = GridChangeOracle.LiveTrees(w);
            int n = roll < 5 ? 3 : 1;
            for (int k = 0; k < n && trees.Count > 0; k++)
            {
                int pick = rng.NextInt(0, trees.Count);
                int slot = trees[pick];
                trees.RemoveAt(pick);
                int cell = w.Resources.Cell[slot];
                w.Resources.Take(w.Resources.HandleOf(slot), w.Resources.Remaining[slot]);
                fells++;
                s.Felled.Add(cell);
                // The stranded case: a unit appears on the just-opened cell and is ordered to a goal some other unit already has (its field is likely cached, stale now).
                // (Only onto open ground: since M3-H1 an interior tree's cell stays blocked as a pocket, BUG-0093.)
                if (rng.NextInt(0, 4) == 0 && u.Count < u.Capacity - 4 && g.IsPassable(cell % g.Width, cell / g.Width))
                {
                    sim.Enqueue(Command.SpawnUnit(0, rng.NextInt(0, TestSim.UnitTypeCount), g.CellCenter(cell % g.Width, cell / g.Width) + new Vector2(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f)));
                    strandSpawns++;
                }
            }
        }
        // Closing: a Keep dropped at a random spot (command), or a tree grown on a free cell (seam).
        roll = rng.NextInt(0, 1000);
        if (roll < 25)
        {
            for (int tries = 0; tries < 50; tries++)
            {
                Vector2 p = s.Open[rng.NextInt(0, s.Open.Count)];
                g.WorldToCell(p, out int x, out int y);
                if (!w.Buildings.Fits(GatherMaps.Keep, y * g.Width + x)) continue;
                sim.Enqueue(Command.SpawnBuilding(rng.NextInt(0, 2), GatherMaps.Keep, p));
                closes++;
                break;
            }
        }
        else if (roll < 45)
        {
            for (int tries = 0; tries < 50; tries++)
            {
                Vector2 p = s.Open[rng.NextInt(0, s.Open.Count)];
                g.WorldToCell(p, out int x, out int y);
                if (!g.CanTakeResource(x, y) || GridChangeOracle.UnitIn(w, x, y)) continue;
                // Don't wall anybody in a pocket: only on a cell with all 8 neighbours open.
                bool clear = true;
                for (int dy = -1; dy <= 1 && clear; dy++)
                    for (int dx = -1; dx <= 1 && clear; dx++)
                        if (!g.IsPassable(x + dx, y + dy)) clear = false;
                if (!clear) continue;
                if (w.Resources.Spawn(ResourceMaps.Tree, y * g.Width + x, ResourceMaps.TreeWood, out _)) { closes++; break; }
            }
        }
        if (!ordersOn) return;
        // The stranded case, ordered: a unit standing on a felled cell (spawned or walked there) gets a pool goal,
        // whose field is likely cached and stale with no direction on that cell.
        if (rng.NextInt(0, 3) == 0)
        {
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || !g.WorldToCell(u.Position[i], out int ux, out int uy)) continue;
                int at = uy * g.Width + ux;
                if (!s.Felled.Contains(at)) continue;
                sim.Enqueue(Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), s.Goals[rng.NextInt(0, s.Goals.Length)]));
                s.StrandOrders++;
                s.Felled.Remove(at);
                break;
            }
        }
        // Orders: groups to shared goals (some to a blocked cell), single orders, spam of the same order.
        roll = rng.NextInt(0, 100);
        if (roll < 25)
        {
            Vector2 goal = s.Goals[rng.NextInt(0, s.Goals.Length)];
            int count = 1 + rng.NextInt(0, 10);
            for (int k = 0; k < count; k++)
            {
                int i = rng.NextInt(0, u.Capacity);
                if (!u.Alive[i]) continue;
                sim.Enqueue(Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), goal));
                if (rng.NextInt(0, 8) == 0) sim.Enqueue(Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), goal)); // spam
            }
        }
        else if (roll < 30)
        {
            // Send a few units to a goal another Moving unit already has (shares the cached, maybe stale, field).
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.State[i] != UnitState.Moving) continue;
                int gc = u.GoalCell[i];
                Vector2 goal = g.CellCenter(gc % g.Width, gc / g.Width);
                for (int k = 0; k < 3; k++)
                {
                    int j = rng.NextInt(0, u.Capacity);
                    if (u.Alive[j]) sim.Enqueue(Command.Move(u.Owner[j], new EntityHandle(j, u.Generation[j]), goal));
                }
                break;
            }
        }
    }

    /// <summary>Who is still Moving, and why: goal groups, cache state of their goal, the unit's cell in the field.</summary>
    internal static string Diagnose(World w)
    {
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        var sb = new System.Text.StringBuilder();
        var goals = new SortedDictionary<int, List<int>>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.State[i] == UnitState.Moving)
            {
                if (!goals.TryGetValue(u.GoalCell[i], out var l)) goals[u.GoalCell[i]] = l = new List<int>();
                l.Add(i);
            }
        sb.AppendLine($"tick {w.TickNumber}: {goals.Count} moving goal groups, cache {w.FlowFields.Count}/{w.FlowFields.Capacity}, builds {w.FlowFields.BuildCount}, grid v{g.Version}/b{g.BlockVersion}");
        int shown = 0;
        foreach (var (gc, list) in goals)
        {
            var f = w.FlowFields.PeekCached(gc);
            foreach (int i in list)
            {
                if (shown++ > 25) break;
                g.WorldToCell(u.Position[i], out int x, out int y);
                int c = y * g.Width + x;
                string fs = f == null ? "no usable field" : $"field v{f.Version}/b{f.BlockVersion} target {f.TargetCell} dir {f.DirectionAt(c)} cost {f.CostAt(c)}";
                sb.AppendLine($"  unit {i} goal {gc} ({gc % g.Width},{gc / g.Width}) at cell ({x},{y}) passable {g.IsPassable(x, y)} flags {g.FlagsAt(x, y)} vel {u.Velocity[i]} stuck {u.StuckTicks[i]} orderTick {u.OrderTick[i]} target {u.Target[i]}{(u.TargetIsBuilding[i] ? " (building)" : "")} mode {u.Mode[i]} {fs}");
            }
        }
        return sb.ToString();
    }

    [Theory]
    [Trait("Category", "Soak")]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void OpeningAndClosingChangesInterleavedWithOrders_3000Ticks_FieldsNeverPointIntoBlockedCells_NobodyStandsOnBlockedGround_TwinsMatch(ulong seed)
    {
        const int ticks = 3000, settle = 4000;
        Scene a = NewScene(seed), b = NewScene(seed);
        var rngA = new SimRng(seed, 777);
        var rngB = new SimRng(seed, 777);
        World w = a.Sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        var wait = new int[u.Capacity];
        var waitGen = new int[u.Capacity];
        int longestWait = 0, fells = 0, closes = 0, strands = 0, dummy0 = 0, dummy1 = 0, dummy2 = 0, checks = 0, maxBuilds = 0;
        int lastVersion = -1, lastBuilds = -1, settledAt = -1;
        int resets = 0, strandedWaits = 0;
        for (int t = 0; t < ticks + settle; t++)
        {
            bool ordersOn = t < ticks;
            bool eventsOn = t < ticks;
            if (eventsOn)
            {
                Events(a, ref rngA, t, ordersOn, ref fells, ref closes, ref strands);
                Events(b, ref rngB, t, ordersOn, ref dummy0, ref dummy1, ref dummy2);
            }
            int seenBlock = g.BlockVersion;
            int builds0 = w.FlowFields.BuildCount;
            a.Sim.Tick();
            b.Sim.Tick();
            if (seenBlock != w.SeenBlockVersion) resets++;
            maxBuilds = Math.Max(maxBuilds, w.FlowFields.BuildCount - builds0);
            Assert.True(a.Sim.StateHash() == b.Sim.StateHash(), $"seed {seed}: twins differ after tick {a.Sim.TickNumber}");
            int bad = MoveScenario.FirstUnitOnBlockedGround(w);
            Assert.True(bad < 0, $"seed {seed} tick {t}: unit {bad} on blocked ground at {(bad >= 0 ? u.Position[bad] : default)}");
            Assert.Equal(-1, GridChangeOracle.FirstNonFinite(w));
            if (g.Version != lastVersion || w.FlowFields.BuildCount != lastBuilds)
            {
                lastVersion = g.Version;
                lastBuilds = w.FlowFields.BuildCount;
                string? v = GridChangeOracle.UsableFieldViolation(w);
                Assert.True(v == null, $"seed {seed} tick {t}: {v}");
                checks++;
            }
            for (int i = 0; i < u.Capacity; i++)
            {
                if (waitGen[i] != u.Generation[i]) { waitGen[i] = u.Generation[i]; wait[i] = 0; }
                bool waiting = GridChangeOracle.WaitingForField(w, i);
                if (waiting && w.FlowFields.PeekCached(u.GoalCell[i]) != null) strandedWaits++;
                wait[i] = waiting ? wait[i] + 1 : 0;
                longestWait = Math.Max(longestWait, wait[i]);
            }
            if (t >= ticks && settledAt < 0)
            {
                bool anyMoving = false;
                for (int i = 0; i < u.Capacity && !anyMoving; i++) anyMoving = u.Alive[i] && u.State[i] == UnitState.Moving;
                if (!anyMoving)
                {
                    settledAt = t - ticks;
                    break;
                }
            }
        }
        if (settledAt < 0) _out.WriteLine(Diagnose(w));
        _out.WriteLine($"seed {seed}: {fells} fells, {closes} closing changes queued ({resets} progress resets), {strands} units spawned on felled cells ({a.StrandOrders} ordered from one), grid v{g.Version}/b{g.BlockVersion}, " +
            $"{checks} usable-field checks, longest field wait {longestWait} ticks ({strandedWaits} unit-ticks waiting on a stale field's undirected cell), max builds/tick {maxBuilds}, units {u.Count}, settled {settledAt} ticks after the last order");
        Assert.True(maxBuilds <= MovementConstants.MaxFieldBuildsPerTick, $"{maxBuilds} builds in a tick");
        Assert.True(settledAt >= 0, $"seed {seed}: units still Moving {settle} ticks after the last order");
        Assert.True(longestWait <= 60, $"seed {seed}: a unit waited {longestWait} ticks for its field");
    }
}
