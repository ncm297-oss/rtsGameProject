using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M3-H1, session 2026-10-06-2326): the BUG-0093 pocket rule under 1,000 random steps on 6 maps (flat groves,
/// generated maps with plateaus, ramps, forests and mines, a hand-made two-level map with a cliff and a ramp, a
/// small map whose clutter hugs the border). Each step mixes Build (two players' workers), SpawnBuilding, Cancel,
/// Damage (partial or fatal), felling any node (interior ones too, through the seam), Gather and Move orders, so
/// Cancels, destruction, fells in phase 4 and push-outs land in the same tick. After every tick an independent
/// oracle (written from docs/03's statement of the rule, not from <c>NavGrid.ClearFootprint</c>) checks the grid,
/// and a twin fed the same stream hashes identically.
/// </summary>
[Collection(SerialCollection.Name)]
public class PocketRuleFuzzStressTests
{
    private readonly ITestOutputHelper _out;

    public PocketRuleFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static int Barracks => TestSim.Data.FindBuilding("malazan_barracks");

    /// <summary>Independent pocket-rule oracle; null when the grid is right, else what's wrong.</summary>
    /// <remarks>
    /// "Free" = passable on the bare grid (same heightmap, nothing placed) and covered by no building or node. The rule
    /// says: every passable cell reaches every other (4-connected), every free cell is passable or a pocket, a pocket cell
    /// is blocked and never beside a passable cell (else it should have reopened), pocket cells carry no building or node
    /// flag, costs follow passability and <see cref="NavGrid.PassableCount"/> counts the passable cells.
    /// </remarks>
    internal static string? Check(NavGrid g, NavGrid bare)
    {
        int w = g.Width, h = g.Height, n = w * h, passable = 0, start = -1;
        for (int i = 0; i < n; i++)
        {
            int x = i % w, y = i / w;
            NavFlags f = g.FlagsAt(x, y);
            bool open = (f & NavFlags.Blocked) == 0;
            if (open)
            {
                passable++;
                if (start < 0) start = i;
                if (!bare.IsPassable(x, y)) return $"({x}, {y}) passable but blocked on the bare grid";
                if (g.CostAt(x, y) != MapConstants.CostPassable) return $"({x}, {y}) passable with cost {g.CostAt(x, y)}";
                if ((f & (NavFlags.Pocket | NavFlags.Building | NavFlags.Resource)) != 0) return $"({x}, {y}) passable with {f}";
            }
            else if (g.CostAt(x, y) != MapConstants.CostBlocked) return $"({x}, {y}) blocked with cost {g.CostAt(x, y)}";
            if ((f & NavFlags.Pocket) != 0)
            {
                if ((f & (NavFlags.Building | NavFlags.Resource)) != 0) return $"({x}, {y}) pocket under a building or node: {f}";
                if (!bare.IsPassable(x, y)) return $"({x}, {y}) pocket on ground the bare grid blocks";
                if (g.IsPassable(x - 1, y) || g.IsPassable(x + 1, y) || g.IsPassable(x, y - 1) || g.IsPassable(x, y + 1))
                    return $"({x}, {y}) pocket beside a passable cell (should have reopened)";
            }
            bool free = bare.IsPassable(x, y) && (f & (NavFlags.Building | NavFlags.Resource)) == 0;
            if (free && !open && (f & NavFlags.Pocket) == 0) return $"({x}, {y}) free but blocked without Pocket: {f}";
        }
        if (passable != g.PassableCount) return $"PassableCount {g.PassableCount}, counted {passable}";
        if (start < 0) return null;
        var seen = new bool[n];
        var q = new int[n];
        int head = 0, tail = 0;
        q[tail++] = start;
        seen[start] = true;
        while (head < tail)
        {
            int c = q[head++];
            int cx = c % w, cy = c / w;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = cy + (d == 2 ? -1 : d == 3 ? 1 : 0);
                if (!g.IsPassable(nx, ny)) continue;
                int j = ny * w + nx;
                if (seen[j]) continue;
                seen[j] = true;
                q[tail++] = j;
            }
        }
        return tail == passable ? null : $"flood from ({start % w}, {start / w}) reaches {tail} of {passable} passable cells";
    }

    private sealed class Run
    {
        public required Simulation Sim;
        public required EntityHandle[][] Workers;
    }

    private static Simulation NewMapSim(int map, ulong seed)
    {
        SimConfig Cfg() => TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512) with { BuildingCapacity = 128, ResourceCapacity = 4096 };
        switch (map)
        {
            case 0: // flat 48 x 40, a solid grove with ragged edges
            {
                var sim = new Simulation(Cfg(), Flat(48, 40));
                var rng = new SimRng(seed, 7);
                for (int y = 12; y < 28; y++)
                    for (int x = 14; x < 34; x++)
                        if (rng.NextInt(0, 5) != 0) Spawn(sim.World, Tree, x, y, TreeWood);
                return sim;
            }
            case 1:
                return new Simulation(Cfg() with { Map = MapGenParams.Default with { Width = 64, Height = 64, Forests = 14, GoldMines = 4, MineSpacing = 8f } });
            case 2:
                return new Simulation(Cfg() with { Map = MapGenParams.Default with { Forests = 24, GoldMines = 6 } });
            case 3: // two levels: a level-1 plateau east with a 3-wide ramp, trees hugging the cliff foot and the ramp
            {
                var rows = new string[30];
                for (int y = 0; y < 30; y++)
                    rows[y] = new string('0', 20) + (y is >= 13 and <= 15 ? "r" : "0") + new string('1', 19);
                var sim = new Simulation(Cfg(), FromRows(rows));
                NavGrid g = sim.World.NavGrid;
                var rng = new SimRng(seed, 9);
                for (int y = 2; y < 28; y++)
                    for (int x = 14; x < 34; x++)
                        if (rng.NextInt(0, 3) == 0 && g.CanTakeResource(x, y)) sim.World.Resources.Spawn(Tree, y * g.Width + x, TreeWood, out _);
                return sim;
            }
            case 4: // small: clutter on the border ring's inner side
            {
                var sim = new Simulation(Cfg(), Flat(24, 20));
                NavGrid g = sim.World.NavGrid;
                var rng = new SimRng(seed, 11);
                for (int y = 1; y < 19; y++)
                    for (int x = 1; x < 23; x++)
                        if ((x <= 3 || y <= 3 || x >= 20 || y >= 16) && rng.NextInt(0, 2) == 0 && g.CanTakeResource(x, y))
                            sim.World.Resources.Spawn(Tree, y * g.Width + x, TreeWood, out _);
                return sim;
            }
            default:
                return new Simulation(Cfg() with { Map = MapGenParams.Default with { Width = 96, Height = 80, Forests = 30, GoldMines = 8, ForestMinTrees = 20, ForestMaxTrees = 60 } });
        }
    }

    private static Run NewRun(int map, ulong seed)
    {
        Simulation sim = NewMapSim(map, seed);
        NavGrid g = sim.World.NavGrid;
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        var rng = new SimRng(seed, 13);
        for (int k = 0; k < 24; k++)
        {
            int c = open[rng.NextInt(0, open.Count)];
            sim.Enqueue(Command.SpawnUnit(k % 2, Laborer, g.CellCenter(c % g.Width, c / g.Width)));
        }
        sim.Tick();
        sim.Tick();
        for (int p = 0; p < 2; p++) Give(sim, p, 1_000_000, 1_000_000);
        var workers = new EntityHandle[2][];
        UnitStore u = sim.World.Units;
        for (int p = 0; p < 2; p++)
        {
            var list = new List<EntityHandle>();
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.Owner[i] == p) list.Add(new EntityHandle(i, u.Generation[i]));
            workers[p] = list.ToArray();
        }
        return new Run { Sim = sim, Workers = workers };
    }

    /// <summary>A cell near the clutter: a live node's or building's cell, or anywhere, give or take 3.</summary>
    private static Vector2 HotSpot(World w, ref SimRng rng)
    {
        NavGrid g = w.NavGrid;
        int cell = -1;
        int pick = rng.NextInt(0, 10);
        if (pick < 5) cell = PickCell(w.Resources.Capacity, i => w.Resources.Alive[i], i => w.Resources.Cell[i], ref rng);
        else if (pick < 9) cell = PickCell(w.Buildings.Capacity, i => w.Buildings.Alive[i], i => w.Buildings.Cell[i], ref rng);
        if (cell < 0) cell = rng.NextInt(0, g.Width * g.Height);
        int x = Math.Clamp(cell % g.Width + rng.NextInt(-3, 4), 0, g.Width - 1);
        int y = Math.Clamp(cell / g.Width + rng.NextInt(-3, 4), 0, g.Height - 1);
        return g.CellCenter(x, y);
    }

    private static int PickCell(int capacity, Func<int, bool> alive, Func<int, int> cell, ref SimRng rng)
    {
        int count = 0;
        for (int i = 0; i < capacity; i++) if (alive(i)) count++;
        if (count == 0) return -1;
        int k = rng.NextInt(0, count);
        for (int i = 0; i < capacity; i++)
            if (alive(i) && k-- == 0) return cell(i);
        return -1;
    }

    private static int PickSlot(int capacity, Func<int, bool> ok, ref SimRng rng)
    {
        int count = 0;
        for (int i = 0; i < capacity; i++) if (ok(i)) count++;
        if (count == 0) return -1;
        int k = rng.NextInt(0, count);
        for (int i = 0; i < capacity; i++)
            if (ok(i) && k-- == 0) return i;
        return -1;
    }

    private static void Act(Run run, ref SimRng rng)
    {
        Simulation sim = run.Sim;
        World w = sim.World;
        BuildingStore b = w.Buildings;
        ResourceStore r = w.Resources;
        NavGrid g = w.NavGrid;
        int player = rng.NextInt(0, 2);
        EntityHandle worker = run.Workers[player][rng.NextInt(0, run.Workers[player].Length)];
        int roll = rng.NextInt(0, 100);
        if (roll < 30)
        {
            int t = rng.NextInt(0, 6);
            int type = t < 4 ? House : t == 4 ? Keep : Barracks;
            sim.Enqueue(Command.Build(player, worker, type, HotSpot(w, ref rng)));
        }
        else if (roll < 42)
            sim.Enqueue(Command.SpawnBuilding(player, House, HotSpot(w, ref rng)));
        else if (roll < 54)
        {
            int k = PickSlot(b.Capacity, i => b.Alive[i] && b.UnderConstruction[i], ref rng);
            if (k >= 0) sim.Enqueue(Command.Cancel(b.Owner[k], g.CellCenter(b.Cell[k] % g.Width, b.Cell[k] / g.Width)));
        }
        else if (roll < 68)
        {
            int k = PickSlot(b.Capacity, i => b.Alive[i], ref rng);
            if (k >= 0) b.Damage(b.HandleOf(k), rng.NextInt(0, 3) == 0 ? 50 : 1_000_000);
        }
        else if (roll < 84)
        {
            int k = PickSlot(r.Capacity, i => r.Alive[i], ref rng);
            if (k >= 0) r.Take(r.HandleOf(k), rng.NextInt(0, 4) == 0 ? 1 : r.Remaining[k]);
        }
        else if (roll < 94)
        {
            int k = PickSlot(r.Capacity, i => r.Alive[i], ref rng);
            if (k >= 0) sim.Enqueue(Command.Gather(player, worker, g.CellCenter(r.Cell[k] % g.Width, r.Cell[k] / g.Width)));
        }
        else
            sim.Enqueue(Command.Move(player, worker, HotSpot(w, ref rng)));
    }

    private static int CountFlag(NavGrid g, NavFlags flag)
    {
        int n = 0;
        for (int c = 0; c < g.Width * g.Height; c++)
            if ((g.FlagsAt(c % g.Width, c / g.Width) & flag) != 0) n++;
        return n;
    }

    /// <summary>1,000 steps (1-3 actions, then a tick) per map; the oracle after every tick; twins equal every tick.</summary>
    [Theory]
    [Trait("Category", "Soak")]
    [InlineData(0, 101UL)]
    [InlineData(1, 102UL)]
    [InlineData(2, 103UL)]
    [InlineData(3, 104UL)]
    [InlineData(4, 105UL)]
    [InlineData(5, 106UL)]
    public void ThousandRandomSteps_IndependentOracleEveryTick_TwinsEqual(int map, ulong seed)
    {
        Run a = NewRun(map, seed), b = NewRun(map, seed);
        NavGrid g = a.Sim.World.NavGrid;
        var bare = new NavGrid(a.Sim.World.Heightmap);
        Assert.Null(Check(g, bare));
        var rngA = new SimRng(seed, 93);
        var rngB = new SimRng(seed, 93);
        int pocketsMade = 0, pocketsReopened = 0, maxPocket = 0, unitsOnPocket = 0, spuriousBumps = 0, places = 0, frees = 0;
        int prevPocket = 0;
        for (int step = 0; step < 1000; step++)
        {
            int actions = 1 + rngA.NextInt(0, 3);
            rngB.NextInt(0, 3);
            int v0 = g.Version, bv0 = g.BlockVersion, pc0 = g.PassableCount, bc0 = a.Sim.World.Buildings.Count, rc0 = a.Sim.World.Resources.Count;
            for (int k = 0; k < actions; k++)
            {
                Act(a, ref rngA);
                Act(b, ref rngB);
            }
            a.Sim.Tick();
            b.Sim.Tick();
            Assert.True(a.Sim.StateHash() == b.Sim.StateHash(), $"map {map} step {step}: twins differ");
            string? bad = Check(g, bare);
            Assert.True(bad == null, $"map {map} step {step}: {bad}");
            int pocket = CountFlag(g, NavFlags.Pocket);
            if (pocket > prevPocket) pocketsMade++;
            if (pocket < prevPocket) pocketsReopened++;
            prevPocket = pocket;
            maxPocket = Math.Max(maxPocket, pocket);
            // Nothing a unit can use changed (same passable count, no closing): Version must not move.
            if (g.PassableCount == pc0 && g.BlockVersion == bv0 && g.Version != v0) spuriousBumps++;
            if (a.Sim.World.Buildings.Count > bc0) places++;
            if (a.Sim.World.Buildings.Count < bc0 || a.Sim.World.Resources.Count < rc0) frees++;
            UnitStore u = a.Sim.World.Units;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y), $"map {map} step {step}: unit {i} non-finite");
                if (g.WorldToCell(u.Position[i], out int cx, out int cy) && (g.FlagsAt(cx, cy) & NavFlags.Pocket) != 0) unitsOnPocket++;
            }
        }
        _out.WriteLine($"map {map}: {places} steps placed, {frees} freed; pockets made on {pocketsMade} steps, reopened on {pocketsReopened}, largest {maxPocket} cells, final {prevPocket}; spurious version bumps {spuriousBumps}; unit-ticks on a pocket cell {unitsOnPocket}");
        Assert.Equal(0, unitsOnPocket);
        Assert.Equal(0, spuriousBumps);
    }
}
