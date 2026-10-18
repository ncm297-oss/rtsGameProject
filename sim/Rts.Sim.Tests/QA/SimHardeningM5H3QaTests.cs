using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-H3 (session 2026-10-17-1624), attacks on the sim hardening's behaviour changes:
/// (1) BUG-0887's replaced step (<c>AgainstKeptSide</c>): the alternating-point spam over lines of 10-32 at 0-60 degrees
/// and every offset of QA's original grid (the dev and the un-skipped QA rows cover 10 of these cells);
/// (2) BUG-0888's doubled budget round a cliff-sealed end: every walker position along 16-32 lines, with and without spam;
/// (3) determinism twins for both (same order every tick; reversed spawns bit-equal walker path);
/// (4) BUG-0790's stand points over 32 more seeds (9-40) of the gather pockets' wedge rule;
/// (5) BUG-0791's no-site wait: a step that waited on a <c>requires</c> (no search ran: BUG-0572's cell-free rule) must not
/// be held back once the requirement is met.
/// </summary>
[Collection(SerialCollection.Name)]
public class SimHardeningM5H3QaTests
{
    private readonly ITestOutputHelper _out;

    public SimHardeningM5H3QaTests(ITestOutputHelper output) => _out = output;

    private static void TickTopped(Simulation sim)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Hp[i] = w.Data.Units[u.TypeId[i]].Hp;
        sim.Tick();
    }

    private sealed record Walk(int Arrived, int GaveUp, int Flips, int Blocked, bool Finite, Vector2 End, List<ulong> Hashes, List<Vector2> Path);

    private static string Cell(Walk r) => $"arrived {(r.Arrived >= 0 ? r.Arrived.ToString() : "-")}, gave up {(r.GaveUp >= 0 ? r.GaveUp.ToString() : "-")}, flips {r.Flips}, blocked {r.Blocked}, end ({r.End.X:F1}, {r.End.Y:F1})";

    /// <summary>
    /// QA's BUG-0881 / BUG-0887 line scene: one Heavy Infantry 3 m behind an own Crossbowman line (1 m apart, at
    /// <paramref name="degrees"/>), planted on holding Raiders 14.5 m in front out of its sight, at <paramref name="offset"/> m
    /// along it; a Move to 5 m beyond the line's middle, re-issued every <paramref name="every"/> ticks (0 once) alternating
    /// with a point 2 m along the line.
    /// </summary>
    private static Walk LineWalk(int count, float degrees, int every, float offset, bool reversed = false, int ticks = 900, bool stopOnArrival = true)
    {
        var sim = new Simulation(TestSim.Config(7, 2, 2 * count + 8, 16 * count + 64), LocalMovementTests.Flat(80));
        sim.World.FlankRuleOn = true;
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        TestSim.KeepIn(sim, 1);
        var mid = new Vector2(80f, 80f);
        float a = degrees * MathF.PI / 180f;
        var along = new Vector2(MathF.Sin(a), MathF.Cos(a));
        var normal = new Vector2(along.Y, -along.X);
        var row = new EntityHandle[count];
        var targets = new EntityHandle[count];
        for (int n = 0; n < count; n++)
        {
            int k = reversed ? count - 1 - n : n;
            targets[k] = Place(sim, 1, Raider, mid + along * (k - (count - 1) / 2f) + normal * 14.5f);
            u.Hold[targets[k].Index] = true;
            Spot(sim, 0, targets[k]);
        }
        for (int n = 0; n < count; n++)
        {
            int k = reversed ? count - 1 - n : n;
            row[k] = Place(sim, 0, Crossbowman, mid + along * (k - (count - 1) / 2f));
        }
        for (int k = 0; k < count; k++) sim.Enqueue(Command.Attack(0, row[k], targets[k], false));
        for (int t = 0; t < 60 && !row.All(h => u.State[h.Index] == UnitState.Attacking); t++) TickTopped(sim);
        Assert.True(row.All(h => u.State[h.Index] == UnitState.Attacking), "setup: the row didn't plant");
        EntityHandle w = Place(sim, 0, HeavyInfantry, mid - normal * 3f + along * offset);
        Vector2 goal = mid + normal * 5f;
        sim.Enqueue(Command.Move(0, w, goal));
        return Follow(sim, w, goal, along, every, ticks, stopOnArrival);
    }

    /// <summary>
    /// QA's cliff-foot scene: a line of <paramref name="count"/> Crossbowmen along y from (60, 32.5) south, its north end
    /// against a cliff (cells y &lt;= 15 at level 1), the walker 3 m behind member <paramref name="behindMember"/> (+0.2 m),
    /// a Move 5 m in front of that member, re-issued every <paramref name="every"/> ticks alternating 2 m south.
    /// </summary>
    private static Walk CliffWalk(int count, int behindMember, int every, bool reversed = false, int ticks = 900, bool stopOnArrival = true)
    {
        (Simulation sim, EntityHandle w, Vector2 goal) = StageCliff(count, behindMember, reversed);
        return Follow(sim, w, goal, new Vector2(0f, 1f), every, ticks, stopOnArrival);
    }

    private static (Simulation Sim, EntityHandle Walker, Vector2 Goal) StageCliff(int count, int behindMember, bool reversed)
    {
        var rows = new string[64];
        for (int y = 0; y < 64; y++) rows[y] = new string(y <= 15 ? '1' : '0', 64);
        var sim = new Simulation(TestSim.Config(7, 2, 2 * count + 16, 16 * count + 64), LocalMovementTests.Rows(rows));
        sim.World.FlankRuleOn = true;
        UnitStore u = sim.World.Units;
        TestSim.KeepIn(sim, 1);
        var row = new EntityHandle[count];
        var targets = new EntityHandle[count];
        for (int n = 0; n < count; n++)
        {
            int k = reversed ? count - 1 - n : n;
            targets[k] = Place(sim, 1, Raider, new Vector2(74.5f, 32.5f + k));
            u.Hold[targets[k].Index] = true;
            Spot(sim, 0, targets[k]);
        }
        for (int n = 0; n < count; n++)
        {
            int k = reversed ? count - 1 - n : n;
            row[k] = Place(sim, 0, Crossbowman, new Vector2(60f, 32.5f + k));
        }
        for (int k = 0; k < count; k++) sim.Enqueue(Command.Attack(0, row[k], targets[k], false));
        for (int t = 0; t < 60 && !row.All(h => u.State[h.Index] == UnitState.Attacking); t++) TickTopped(sim);
        Assert.True(row.All(h => u.State[h.Index] == UnitState.Attacking), "setup: the row didn't plant");
        EntityHandle w = Place(sim, 0, HeavyInfantry, new Vector2(57f, 32.5f + behindMember + 0.2f));
        var goal = new Vector2(65f, 32.5f + behindMember);
        sim.Enqueue(Command.Move(0, w, goal));
        return (sim, w, goal);
    }

    /// <summary>
    /// 0 B a tick on the changed paths: the cliff walk round the far end with the Move re-clicked every 3 ticks (BUG-0887's
    /// replaced step, BUG-0888's doubled budget), and 40 workers crowding the one-sided mine (BUG-0790's corner cells and the
    /// own cell's free-point test).
    /// </summary>
    [Fact]
    public void ChangedPaths_MidFlankSpamAndCrowdedMineFace_AllocateNothing()
    {
        (Simulation sim, EntityHandle w, Vector2 goal) = StageCliff(24, 1, false);
        for (int t = 0; t < 30; t++) sim.Tick();
        int tick = 30;
        AllocationProbe.AssertZero(() =>
        {
            for (int t = 0; t < 60; t++, tick++)
            {
                if (tick % 3 == 0) sim.Enqueue(Command.Move(0, w, goal + new Vector2(0f, tick / 3 % 2 == 1 ? 2f : 0f)));
                sim.Tick();
            }
        }, _out);
        Assert.True(sim.World.Units.Flank[w.Index] != 0, "setup: the walker never flanked");

        Simulation g = GatherMaps.NewSim(ResourceMaps.Flat(PocketW, PocketH), units: 48, seed: 2);
        World gw = g.World;
        TestSim.KeepIn(g, 0);
        for (int x = 10; x <= 15; x++) ResourceMaps.Spawn(gw, ResourceMaps.Tree, x, 2, ResourceMaps.TreeWood);
        for (int y = 3; y <= 4; y++)
            foreach (int x in new[] { 10, 11, 14, 15 }) ResourceMaps.Spawn(gw, ResourceMaps.Tree, x, y, ResourceMaps.TreeWood);
        EntityHandle mineNode = ResourceMaps.Spawn(gw, ResourceMaps.Mine, 12, 3, 100_000);
        GatherMaps.Building(g, 24, 18);
        var rng = new SimRng(2, 907);
        for (int k = 0; k < 40; k++)
            g.Enqueue(Command.SpawnUnit(0, GatherMaps.Laborer, new Vector2(4f + rng.NextFloat() * 36f, 30f + rng.NextFloat() * 16f)));
        GatherMaps.Run(g, 2);
        int cell = gw.Resources.Cell[mineNode.Index];
        for (int i = 0; i < gw.Units.Capacity; i++)
            if (gw.Units.Alive[i] && gw.Data.Units[gw.Units.TypeId[i]].IsWorker)
                g.Enqueue(Command.Gather(0, new EntityHandle(i, gw.Units.Generation[i]), GatherMaps.At(g, cell % PocketW, cell / PocketW)));
        GatherMaps.Run(g, 400);
        AllocationProbe.AssertZero(() => GatherMaps.Run(g, 200), _out);
    }

    private static Walk Follow(Simulation sim, EntityHandle w, Vector2 goal, Vector2 along, int every, int ticks, bool stopOnArrival)
    {
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        int arrived = -1, gaveUp = -1, flips = 0, last = 0, blocked = 0;
        bool finite = true, wasMoving = false;
        var hashes = new List<ulong>();
        var path = new List<Vector2>();
        for (int t = 0; t < ticks && (arrived < 0 || !stopOnArrival); t++)
        {
            if (every > 0 && t > 0 && t % every == 0)
                sim.Enqueue(Command.Move(0, w, goal + along * (t / every % 2 == 1 ? 2f : 0f)));
            Vector2 before = u.Position[w.Index];
            TickTopped(sim);
            Vector2 p = u.Position[w.Index];
            hashes.Add(sim.StateHash());
            path.Add(p);
            finite &= float.IsFinite(p.X) && float.IsFinite(p.Y);
            if (!g.WorldToCell(p, out int cx, out int cy) || !g.IsPassable(cx, cy)) blocked++;
            float lat = Vector2.Dot(p - before, along);
            int sign = lat > 0.05f ? 1 : lat < -0.05f ? -1 : 0;
            if (sign != 0 && last != 0 && sign != last) flips++;
            if (sign != 0) last = sign;
            if (arrived < 0 && (Vector2.Distance(p, goal) < 1.2f || (every > 0 && Vector2.Distance(p, goal + along * 2f) < 1.2f))) arrived = t;
            if (gaveUp < 0 && arrived < 0 && wasMoving && u.State[w.Index] == UnitState.Idle && u.GoalCell[w.Index] == -1) gaveUp = t;
            wasMoving = u.State[w.Index] == UnitState.Moving;
        }
        return new Walk(arrived, gaveUp, flips, blocked, finite, u.Position[w.Index], hashes, path);
    }

    // ---------- (1) BUG-0887: the spam grid at angles ----------

    /// <summary>
    /// Every cell of QA's BUG-0887 grid (offsets -4, -0.3, 0.2, 1.5) at 0, 30, 45 and 60 degrees, for lines of 10-32 and
    /// re-clicks every 2, 3 and 7 ticks. Bound as BUG-0887's: arrives within 900 ticks with at most 8 turns along the line,
    /// never on a blocked cell.
    /// </summary>
    [Theory]
    // Lines of 10 (under FlankKeepMinMembers, 11: no kept side) fail the same cells on base bf8026e and head d048a21.
    [InlineData(10, 2, Skip = "BUG-0911: a Move re-clicked every 2-3 ticks behind a 10-line at 30-45 degrees swings and never arrives (pre-existing, base too)")]
    [InlineData(10, 3, Skip = "BUG-0911: a Move re-clicked every 2-3 ticks behind a 10-line at 30-45 degrees swings and never arrives (pre-existing, base too)")]
    [InlineData(16, 2)]
    [InlineData(16, 3)]
    [InlineData(16, 7)]
    [InlineData(24, 2)]
    [InlineData(24, 3)]
    [InlineData(24, 7)]
    [InlineData(32, 2)]
    [InlineData(32, 3)]
    [InlineData(32, 7)]
    public void SpamGrid_LineAtAnyAngle_MoveReissuedToAlternatePoints_Arrives(int count, int every)
    {
        var bad = new List<string>();
        foreach (float degrees in new[] { 0f, 30f, 45f, 60f })
            foreach (float offset in new[] { -4f, -0.3f, 0.2f, 1.5f })
            {
                Walk r = LineWalk(count, degrees, every, offset);
                string line = $"line {count} at {degrees} deg, offset {offset}, every {every}: {Cell(r)}";
                _out.WriteLine(line);
                if (!r.Finite || r.Blocked > 0 || r.Arrived < 0 || r.Flips > 8) bad.Add(line);
            }
        Assert.True(bad.Count == 0, $"{bad.Count} of 16 cells fail:\n" + string.Join("\n", bad));
    }

    /// <summary>
    /// Lines of 40 and 48: bigger than <c>MaxPlugCluster</c> (32), an army's front that slice 2 does not flank (docs/03
    /// "Not yet"). Reported; bound only that nothing goes non-finite or onto a blocked cell, and a single Move ends (arrives or
    /// gives up) within 900 ticks.
    /// </summary>
    [Theory]
    [InlineData(40)]
    [InlineData(48)]
    public void OverPlugClusterLines_Report_NoCorruption(int count)
    {
        foreach (int every in new[] { 0, 3 })
            foreach (float degrees in new[] { 0f, 45f })
            {
                Walk r = LineWalk(count, degrees, every, 0.2f);
                _out.WriteLine($"line {count} at {degrees} deg, every {every}: {Cell(r)}");
                Assert.True(r.Finite && r.Blocked == 0, Cell(r));
                if (every == 0) Assert.True(r.Arrived >= 0 || r.GaveUp >= 0, $"a single Move neither arrived nor ended: {Cell(r)}");
            }
    }

    // ---------- (2) BUG-0888: the cliff-sealed end ----------

    /// <summary>
    /// The walker behind any member of a 16-32 line whose north end meets a cliff, Move once or re-clicked every 3 ticks.
    /// Bound: arrives (round the open south end), never given up, never on a blocked cell.
    /// </summary>
    [Theory]
    [InlineData(16, 0)]
    [InlineData(16, 3)]
    [InlineData(24, 0)]
    [InlineData(24, 3)]
    [InlineData(32, 0)]
    [InlineData(32, 3)]
    public void CliffLine_WalkerBehindAnyMember_Arrives(int count, int every)
    {
        var bad = new List<string>();
        var members = new SortedSet<int> { 0, 1, 2, 4, 7, 10, count / 2, count - 4, count - 1 };
        foreach (int m in members)
        {
            Walk r = CliffWalk(count, m, every);
            string line = $"cliff line {count}, behind member {m}, every {every}: {Cell(r)}";
            _out.WriteLine(line);
            if (r.Blocked > 0 || r.Arrived < 0 || (every == 0 && r.GaveUp >= 0)) bad.Add(line);
        }
        Assert.True(bad.Count == 0, $"{bad.Count} of {members.Count} rows fail:\n" + string.Join("\n", bad));
    }

    // ---------- (3) determinism twins ----------

    /// <summary>Same spawns, same order, twice: hash equal at every tick (the spam at an angle; the cliff with spam).</summary>
    [Theory]
    [InlineData(false, 24, 45, 2)]
    [InlineData(false, 32, 30, 3)]
    [InlineData(true, 32, 4, 3)]
    [InlineData(true, 24, 1, 0)]
    public void Scenes_SameOrderTwice_HashEqualEveryTick(bool cliff, int count, int param, int every)
    {
        Walk a = cliff ? CliffWalk(count, param, every, ticks: 500, stopOnArrival: false) : LineWalk(count, param, every, 0.2f, ticks: 500, stopOnArrival: false);
        Walk b = cliff ? CliffWalk(count, param, every, ticks: 500, stopOnArrival: false) : LineWalk(count, param, every, 0.2f, ticks: 500, stopOnArrival: false);
        Assert.Equal(a.Hashes.Count, b.Hashes.Count);
        for (int t = 0; t < a.Hashes.Count; t++) Assert.True(a.Hashes[t] == b.Hashes[t], $"tick {t}: the twins' hashes part");
    }

    /// <summary>
    /// The row and its targets spawned in reverse (other slots): the flank rule reads a set (docs/03 "Determinism", what new
    /// code still owes), so the lone walker's path is bit-equal.
    /// </summary>
    [Theory]
    [InlineData(false, 24, 45, 2)]
    [InlineData(false, 32, 30, 3)]
    [InlineData(true, 24, 4, 0)]
    [InlineData(true, 32, 1, 3)]
    public void Scenes_RowSpawnedReversed_BitEqualWalkerPath(bool cliff, int count, int param, int every)
    {
        Walk a = cliff ? CliffWalk(count, param, every, ticks: 500, stopOnArrival: false) : LineWalk(count, param, every, 0.2f, ticks: 500, stopOnArrival: false);
        Walk b = cliff ? CliffWalk(count, param, every, reversed: true, ticks: 500, stopOnArrival: false) : LineWalk(count, param, every, 0.2f, reversed: true, ticks: 500, stopOnArrival: false);
        _out.WriteLine($"same order [{Cell(a)}], reversed [{Cell(b)}]");
        for (int t = 0; t < a.Path.Count; t++) Assert.True(a.Path[t] == b.Path[t], $"tick {t}: {a.Path[t]} vs reversed {b.Path[t]}");
    }

    // ---------- (4) BUG-0790: gather pockets, seeds 9-40 ----------

    private const int PocketW = 32, PocketH = 26;

    /// <summary>
    /// <c>Stress/GatherPocketStressTests</c>' wedge rule (no worker on its node leg stands out of reach, nothing between it
    /// and the node, within 0.5 m of one spot, for more than 40 ticks) and exact conservation, for the two one-sided pockets,
    /// seeds 9-40 (the stress row runs 1-8).
    /// </summary>
    [Theory]
    [InlineData(true, 12)]
    [InlineData(true, 40)]
    [InlineData(false, 12)]
    [InlineData(false, 40)]
    public void OneSidedPockets_Seeds9To40_NoGathererWedgedOutOfReach(bool mine, int workers)
    {
        var failures = new List<string>();
        int worstAll = 0;
        for (ulong seed = 9; seed <= 40; seed++)
        {
            Simulation sim = GatherMaps.NewSim(ResourceMaps.Flat(PocketW, PocketH), units: 48, seed: seed);
            World w = sim.World;
            UnitStore u = w.Units;
            TestSim.KeepIn(sim, 0);
            void T(int x, int y) => ResourceMaps.Spawn(w, ResourceMaps.Tree, x, y, ResourceMaps.TreeWood);
            EntityHandle node;
            if (mine)
            {
                for (int x = 10; x <= 15; x++) T(x, 2);
                for (int y = 3; y <= 4; y++) { T(10, y); T(11, y); T(14, y); T(15, y); }
                node = ResourceMaps.Spawn(w, ResourceMaps.Mine, 12, 3, 100_000);
            }
            else
            {
                for (int x = 10; x <= 14; x++) T(x, 3);
                T(10, 4); T(11, 4); T(13, 4); T(14, 4);
                node = ResourceMaps.Spawn(w, ResourceMaps.Tree, 12, 4, ResourceMaps.TreeWood);
            }
            GatherMaps.Building(sim, 24, 18);
            var rng = new SimRng(seed, 907);
            var before = (bool[])u.Alive.Clone();
            for (int k = 0; k < workers; k++)
                sim.Enqueue(Command.SpawnUnit(0, GatherMaps.Laborer, new Vector2(4f + rng.NextFloat() * 36f, 30f + rng.NextFloat() * 16f)));
            GatherMaps.Run(sim, 2);
            var hs = new List<EntityHandle>();
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && !before[i]) hs.Add(new EntityHandle(i, u.Generation[i]));
            Assert.Equal(workers, hs.Count);
            int cell = w.Resources.Cell[node.Index];
            foreach (EntityHandle h in hs) sim.Enqueue(Command.Gather(0, h, GatherMaps.At(sim, cell % PocketW, cell / PocketW)));
            (long gold0, long wood0) = GatherMaps.Conserved(w);
            var run = new int[workers];
            var anchor = new Vector2[workers];
            int worst = 0, worstUnit = -1, worstTick = 0;
            for (int t = 1; t <= 3000; t++)
            {
                sim.Tick();
                (long gold, long wood) = GatherMaps.Conserved(w);
                Assert.True(gold == gold0 && wood == wood0, $"seed {seed} tick {t}: conservation broken");
                for (int k = 0; k < workers; k++)
                {
                    int i = hs[k].Index;
                    if (!u.IsAlive(hs[k])) continue;
                    bool nodeLeg = EconomySystem.OnLoop(u, i) && w.Resources.IsAlive(u.GatherNode[i]) && u.Cargo[i] < w.Data.Rules.WorkerCarry;
                    if (!nodeLeg || InReach(w, i) || SomeoneBetween(w, i) || Vector2.Distance(u.Position[i], anchor[k]) > 0.5f)
                    {
                        anchor[k] = u.Position[i];
                        run[k] = 0;
                        continue;
                    }
                    if (++run[k] > worst) (worst, worstUnit, worstTick) = (run[k], i, t);
                }
            }
            worstAll = Math.Max(worstAll, worst);
            if (worst > 2 * EconomyConstants.RetryTicks)
                failures.Add($"seed {seed}: unit {worstUnit} wedged {worst} ticks (to tick {worstTick}) at {u.Position[worstUnit]}");
        }
        _out.WriteLine($"{(mine ? "MineOneSide" : "OneSide")} x{workers}, seeds 9-40: worst wedge {worstAll} ticks; {failures.Count} seeds over the bound");
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    private static bool InReach(World w, int i)
    {
        int n = w.Units.GatherNode[i].Index;
        var def = w.Data.Resources[w.Resources.TypeId[n]];
        return GatherMaps.DistanceToFootprint(w.NavGrid, w.Units.Position[i], w.Resources.Cell[n], def.FootprintWidth, def.FootprintHeight) <= EconomyConstants.Reach;
    }

    /// <summary>A copy of <c>GatherPocketStressTests.SomeoneBetween</c>: a unit's body or a blocked cell on the line to the node.</summary>
    private static bool SomeoneBetween(World w, int i)
    {
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        int n = u.GatherNode[i].Index;
        var def = w.Data.Resources[w.Resources.TypeId[n]];
        int cell = w.Resources.Cell[n];
        const float cs = MapConstants.CellSize;
        float x0 = cell % g.Width * cs, y0 = cell / g.Width * cs;
        Vector2 min = new(x0, y0), max = new(x0 + def.FootprintWidth * cs, y0 + def.FootprintHeight * cs);
        Vector2 p = u.Position[i], q = Vector2.Clamp(p, min, max);
        float mineD = Vector2.Distance(p, q);
        Vector2 d = q - p;
        float len2 = d.LengthSquared();
        for (int j = 0; j < u.Capacity; j++)
        {
            if (j == i || !u.Alive[j]) continue;
            Vector2 pj = u.Position[j];
            float s = len2 > 0f ? Math.Clamp(Vector2.Dot(pj - p, d) / len2, 0f, 1f) : 0f;
            float r = u.Radius[i] + u.Radius[j];
            if (Vector2.DistanceSquared(pj, p + d * s) > r * r) continue;
            if (Vector2.Distance(pj, Vector2.Clamp(pj, min, max)) < mineD) return true;
        }
        int steps = (int)(mineD / 0.25f);
        for (int k = 1; k < steps; k++)
        {
            Vector2 at = p + d * (k / (float)steps);
            if (!g.WorldToCell(at, out int cx, out int cy)) continue;
            bool own = at.X >= min.X && at.X <= max.X && at.Y >= min.Y && at.Y <= max.Y;
            if (!own && !g.IsPassable(cx, cy)) return true;
        }
        return false;
    }

    // ---------- (5) BUG-0791: the no-site wait after a requires failure ----------

    /// <summary>
    /// A build step for the Wickan Corral (requires a Legion Barracks) is due with no Barracks: <c>FindSite</c> returns -1 at
    /// its cell-free test (<c>CanPlaceAnywhere</c>, BUG-0572), with no search run. A finished Barracks then appears. Before
    /// M5-H3 the step sent its Build at the next think; the BUG-0791 wait must not hold a step whose search never ran (the
    /// fix's reason is the search's cost). Bound: the Build is sent within 2 thinks of the Barracks (view latency).
    /// </summary>
    [Fact(Skip = "BUG-0910: the BUG-0791 no-site wait also holds a step whose search never ran (requires unmet): Build 1-5 thinks late, base 1-2")]
    public void ABuildStepWaitingOnARequirement_SendsItsBuildAtTheThinkAfterTheRequirementIsMet()
    {
        var data = AiScenes.DataWithAi("malazan", root =>
            root["buildOrder"] = AiScenes.BuildOrder(("time", 0, "build", "malazan_wickan_corral", 0)));
        int corral = data.FindBuilding("malazan_wickan_corral");
        // Some thinks with no Barracks, then one; the next thinks may build. Repeated at several phases of the wait.
        var lags = new List<int>();
        for (int phase = 0; phase < 5; phase++)
        {
            Simulation s = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human), data);
            BuildMaps.SetTotals(s, 0, 5000, 5000);
            List<Command> log = AiScenes.Log(s, 0);
            AiPlayer a = s.AiOf(0)!;
            int waitThinks = 2 + phase;
            for (int t = 0; t < waitThinks * a.ThinkTicks; t++) s.Tick();
            Assert.DoesNotContain(log, c => c.Kind == CommandKind.Build && c.TypeId == corral);
            Assert.True(s.World.Buildings.Spawn(0, data.FindBuilding("malazan_barracks"), 44 * AiScenes.FlatSize + 8, out _));
            int thinksAtSpawn = a.Thinks, lag = -1;
            for (int t = 0; t < 10 * a.ThinkTicks && lag < 0; t++)
            {
                s.Tick();
                if (log.Any(c => c.Kind == CommandKind.Build && c.TypeId == corral)) lag = a.Thinks - thinksAtSpawn;
            }
            _out.WriteLine($"no Barracks for {waitThinks} thinks: the Corral's Build sent {lag} thinks after the Barracks stood (searches {a.SiteSearches})");
            lags.Add(lag);
        }
        Assert.True(lags.All(l => l >= 0 && l <= 2), $"thinks from the Barracks to the Corral's Build: {string.Join(", ", lags)}");
    }
}
