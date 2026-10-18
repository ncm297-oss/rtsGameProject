using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-P2 (pathing slice 2, "flank through your own line"): adversarial rows for the flank rule (MovementSystem's
/// FlankTurn / FlankBlockOf) and its combat keep-alive (CombatSystem.QueuedBehindOwnLine). A generic staged scene: a row
/// of own Crossbowmen planted (Attacking), each on its own holding enemy Raider 10 m in front of it (hp topped up every
/// tick, so nothing dies unless a row kills it on purpose), and walkers of the same owner behind the row. Rows print the
/// rule off (World.FlankRuleOn false: before) and on, and state their bound.
/// </summary>
[Collection(SerialCollection.Name)]
public class FlankQaTests
{
    private readonly ITestOutputHelper _out;

    public FlankQaTests(ITestOutputHelper output) => _out = output;

    private sealed class Staged
    {
        public required Simulation Sim;
        public required EntityHandle[] Row;
        public required EntityHandle[] Targets;
        public required List<EntityHandle> Walkers;
        public Vector2 Normal; // from the row toward its targets
        public Vector2 Mid;    // the row's middle
    }

    private static Simulation NewSim(Heightmap map, int capacity = 160, ulong seed = 7) =>
        new(TestSim.Config(seed, 2, capacity, 8 * capacity + 32), map);

    /// <summary>
    /// Plants a row of Crossbowmen of player 0 at <paramref name="row"/> (spawned reversed if asked), each attacking its own
    /// holding Raider of player 1 at its position + <paramref name="normal"/> x 10 m. Ticks until every row unit is Attacking.
    /// </summary>
    private static Staged Plant(Simulation sim, Vector2[] row, Vector2 normal, bool reversed = false, float reach = 10f, int shooter = -1)
    {
        UnitStore u = sim.World.Units;
        TestSim.KeepIn(sim, 1);
        var s = new Staged { Sim = sim, Row = new EntityHandle[row.Length], Targets = new EntityHandle[row.Length], Walkers = new(), Normal = normal };
        Vector2 sum = Vector2.Zero;
        foreach (Vector2 p in row) sum += p;
        s.Mid = sum / row.Length;
        for (int n = 0; n < row.Length; n++)
        {
            int k = reversed ? row.Length - 1 - n : n;
            s.Targets[k] = Place(sim, 1, Raider, row[k] + normal * reach);
            u.Hold[s.Targets[k].Index] = true;
            Spot(sim, 0, s.Targets[k]);
        }
        for (int n = 0; n < row.Length; n++)
        {
            int k = reversed ? row.Length - 1 - n : n;
            s.Row[k] = Place(sim, 0, shooter < 0 ? Crossbowman : shooter, row[k]);
        }
        for (int k = 0; k < row.Length; k++) sim.Enqueue(Command.Attack(0, s.Row[k], s.Targets[k], false));
        for (int t = 0; t < 60 && !s.Row.All(h => u.State[h.Index] == UnitState.Attacking); t++) Tick(s);
        Assert.True(s.Row.All(h => u.State[h.Index] == UnitState.Attacking), "setup: the row didn't plant");
        return s;
    }

    private static void Tick(Staged s)
    {
        World w = s.Sim.World;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Hp[i] = w.Data.Units[u.TypeId[i]].Hp;
        s.Sim.Tick();
    }

    private static Vector2[] StraightRow(Vector2 mid, Vector2 along, int count, float spacing)
    {
        var p = new Vector2[count];
        for (int k = 0; k < count; k++) p[k] = mid + along * ((k - (count - 1) / 2f) * spacing);
        return p;
    }

    /// <summary>What one walker did over a run.</summary>
    private sealed record Trace(Vector2[] Path, int ArrivedTick, int GaveUpTick, int SideFlips, int BlockedTicks, bool Finite);

    /// <summary>
    /// Runs <paramref name="ticks"/> ticks and traces walker <paramref name="w"/>: when it stood at its Move goal (Idle on
    /// the goal cell), when its walk was dropped (Idle, goal -1, not arrived) or its chase given up (GiveUps rose), how often
    /// its sideways walking (along the row, beyond 0.05 m a tick) changed sign, and ticks spent on a blocked cell.
    /// </summary>
    private static Trace Run(Staged s, EntityHandle w, int ticks, Vector2? goal, Action<int>? each = null)
    {
        World world = s.Sim.World;
        UnitStore u = world.Units;
        NavGrid g = world.NavGrid;
        int goalCell = goal is Vector2 gp && g.WorldToCell(gp, out int gx, out int gy) ? gy * g.Width + gx : -2;
        var along = new Vector2(-s.Normal.Y, s.Normal.X);
        var path = new Vector2[ticks];
        int arrived = -1, gaveUp = -1, flips = 0, lastSign = 0, blocked = 0;
        bool finite = true;
        int giveUps0 = u.GiveUps[w.Index];
        bool wasMoving = false;
        for (int t = 0; t < ticks; t++)
        {
            each?.Invoke(t);
            Vector2 before = u.Position[w.Index];
            Tick(s);
            Vector2 p = u.Position[w.Index];
            path[t] = p;
            finite &= float.IsFinite(p.X) && float.IsFinite(p.Y);
            if (!g.WorldToCell(p, out int cx, out int cy) || !g.IsPassable(cx, cy)) blocked++;
            float lat = Vector2.Dot(p - before, along);
            int sign = lat > 0.05f ? 1 : lat < -0.05f ? -1 : 0;
            if (sign != 0)
            {
                if (lastSign != 0 && sign != lastSign) flips++;
                lastSign = sign;
            }
            // "Arrived" here: its centre got 1 m past the row's line (toward the targets) or it stood at its goal. An arrived
            // walker's own scan then takes the enemy targets in sight, so standing there is not waited for.
            if (arrived < 0 && (Vector2.Dot(p - s.Mid, s.Normal) > 1f
                || (goal != null && u.State[w.Index] == UnitState.Idle && u.GoalCell[w.Index] == goalCell))) arrived = t;
            if (gaveUp < 0 && arrived < 0 && goal != null && wasMoving && u.State[w.Index] == UnitState.Idle && u.GoalCell[w.Index] == -1) gaveUp = t;
            if (gaveUp < 0 && goal == null && u.GiveUps[w.Index] > giveUps0) gaveUp = t;
            wasMoving = u.State[w.Index] == UnitState.Moving;
        }
        return new Trace(path, arrived, gaveUp, flips, blocked, finite);
    }

    private string Line(string name, Trace off, Trace on)
    {
        string Cell(Trace r) => $"arrived {(r.ArrivedTick >= 0 ? r.ArrivedTick.ToString() : "-")}, gave up {(r.GaveUpTick >= 0 ? r.GaveUpTick.ToString() : "-")}, side flips {r.SideFlips}, blocked {r.BlockedTicks}, end {r.Path[^1]}";
        string line = $"{name}: off [{Cell(off)}] -> on [{Cell(on)}]";
        _out.WriteLine(line);
        return line;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // (b) Line length: the cluster bound. A row of N planted Crossbowmen 1 m apart, one Heavy Infantry 3 m behind its
    // middle (offset 0.2 m), Move to 5 m in front of the row (between the row and its targets).
    // ------------------------------------------------------------------------------------------------------------------

    private (Trace Off, Trace On) LongRow(int count, float spacing, bool flank, float offset = 0.2f, bool reversed = false, int ticks = 600, float reach = 10f, float beyond = 5f)
    {
        Trace Once(bool f)
        {
            Simulation sim = NewSim(LocalMovementTests.Flat(80), capacity: 2 * count + 8);
            sim.World.FlankRuleOn = f;
            var mid = new Vector2(70f, 80f);
            Staged s = Plant(sim, StraightRow(mid, Vector2.UnitY, count, spacing), Vector2.UnitX, reversed, reach);
            EntityHandle w = Place(sim, 0, HeavyInfantry, mid + new Vector2(-3f, offset));
            Vector2 goal = mid + new Vector2(beyond, 0f);
            sim.Enqueue(Command.Move(0, w, goal));
            return Run(s, w, ticks, goal);
        }
        return (Once(false), flank ? Once(true) : Once(false));
    }

    /// <summary>
    /// Rows of 10 to 32 (inside the block bound, 1 m apart): with the rule on the walker walks round and arrives, never
    /// stands on a blocked cell, and doesn't swing between the two ends (at most 2 side flips: the walk round, and the turn
    /// back to the goal past the end). Printed: rows the rule leaves (33 and 48: an army's front) and the off counts.
    /// </summary>
    [Theory]
    [InlineData(10, 5f)]
    [InlineData(16, 5f)]
    [InlineData(20, 5f)]
    [InlineData(24, 5f)]
    [InlineData(32, 5f)]
    [InlineData(16, 8f)]
    [InlineData(32, 8f)]
    [InlineData(16, 13f)]
    [InlineData(32, 13f)]
    public void LongOwnRow_UpToTheBlockBound_WalkerFindsAnEnd(int count, float beyond)
    {
        // The targets 14.5 m in front of the row: out of the walker's 14 m sight until it is round, so the count is about
        // the Move alone (no scan of its own takes a target and walks it round as a chase).
        (Trace off, Trace on) = LongRow(count, 1f, flank: true, reach: 14.5f, beyond: beyond);
        Line($"row of {count}, 1 m apart, Move to {beyond} m beyond (targets out of sight)", off, on);
        Assert.True(on.Finite && on.BlockedTicks == 0);
        Assert.True(on.SideFlips <= 8, $"swung {on.SideFlips} times between the ends");
        Assert.True(on.ArrivedTick >= 0, $"row of {count}: never arrived (gave up at {on.GaveUpTick}), end {on.Path[^1]}");
    }

    /// <summary>
    /// Past the block bound (33, 48): the movement half must not fire: the bit-equal path to the rule off until the walk is
    /// given up (after that the walker's own scan takes an enemy in sight and the chase rules differ: see
    /// <see cref="ChaseBehindAnOwnRowPastTheBlockBound_IsNotKeptForever"/>).
    /// </summary>
    [Theory]
    [InlineData(33)]
    [InlineData(48)]
    public void LongOwnRow_PastTheBlockBound_IsLeftAlone(int count)
    {
        (Trace off, Trace on) = LongRow(count, 1f, flank: true);
        Line($"row of {count}, 1 m apart, Move through", off, on);
        Assert.True(off.GaveUpTick > 0, "setup: the walk behind the front was not given up");
        for (int t = 0; t <= off.GaveUpTick; t++) Assert.True(off.Path[t] == on.Path[t], $"tick {t}: {off.Path[t]} vs {on.Path[t]}");
    }

    /// <summary>
    /// (f) The keep-alive where the flank can't help: a row of <paramref name="count"/> Crossbowmen 1 m apart (past the
    /// 32-member block bound, so the flank rule doesn't walk round it), each on its own holding Raider 10 m in front; one
    /// Heavy Infantry 3 m behind the row's middle given <paramref name="kind"/> on the Raider opposite it (Retaliate: Idle, its
    /// own scan takes one in sight). 3,000 ticks. Bound: over the last 500 ticks it neither holds a target behind the front
    /// nor walks (the BUG-0143 / BUG-0137 rule: a chase that gets no closer ends), as with the rule off. Ordered Attack is
    /// left out: M4-H3 keeps a reachable ordered target by design.
    /// </summary>
    [Theory]
    [InlineData(40, ChaseKind.AttackMove)]
    [InlineData(40, ChaseKind.Retaliate)]
    [InlineData(33, ChaseKind.Retaliate)]
    public void ChaseBehindAnOwnRowPastTheBlockBound_IsNotKeptForever(int count, ChaseKind kind)
    {
        (int Held, int Moving, int Flips, int GiveUps) Once(bool f)
        {
            Simulation sim = NewSim(LocalMovementTests.Flat(80), capacity: 2 * count + 8);
            sim.World.FlankRuleOn = f;
            UnitStore u = sim.World.Units;
            var mid = new Vector2(70f, 80f);
            Staged s = Plant(sim, StraightRow(mid, Vector2.UnitY, count, 1f), Vector2.UnitX);
            EntityHandle w = Place(sim, 0, HeavyInfantry, mid + new Vector2(-3f, 0.2f));
            EntityHandle opposite = s.Targets[count / 2];
            if (kind == ChaseKind.AttackMove) sim.Enqueue(Command.AttackMove(0, w, u.Position[opposite.Index]));
            Trace tr = Run(s, w, 2500, null);
            int held = 0, moving = 0;
            for (int t = 0; t < 500; t++)
            {
                Tick(s);
                if (u.Target[w.Index].Generation != 0) held++;
                if (u.State[w.Index] == UnitState.Moving) moving++;
            }
            int flips = 0, last = 0;
            for (int t = tr.Path.Length - 500; t < tr.Path.Length; t++)
            {
                float d = tr.Path[t].Y - tr.Path[t - 1].Y;
                int sg = d > 0.01f ? 1 : d < -0.01f ? -1 : 0;
                if (sg != 0 && last != 0 && sg != last) flips++;
                if (sg != 0) last = sg;
            }
            return (held, moving, flips, u.GiveUps[w.Index]);
        }
        var off = Once(false);
        var on = Once(true);
        _out.WriteLine($"row of {count}, {kind}: last 500 ticks off [held a target {off.Held}, Moving {off.Moving}, y flips (ticks 2,000-2,500) {off.Flips}, give-ups {off.GiveUps}] "
            + $"-> on [held {on.Held}, Moving {on.Moving}, flips {on.Flips}, give-ups {on.GiveUps}]");
        Assert.True(on.Held == 0 && on.Moving == 0, $"kept chasing behind a {count}-wide front: held a target {on.Held} of the last 500 ticks, Moving {on.Moving}, {on.Flips} y flips in 500 ticks (off: held {off.Held}, Moving {off.Moving})");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // (a) Spawn-order twins: one walker, the row spawned in either order: the bit-equal path, in every new scene.
    // ------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(10, 1f, 0.2f)]
    [InlineData(10, 1f, -0.3f)]
    [InlineData(20, 1f, 0.2f)]
    [InlineData(32, 1f, 0.2f)]
    [InlineData(12, 1.6f, 0.2f)] // gaps of exactly one Heavy Infantry's diameter (0.8 m)
    [InlineData(12, 1.2f, 0.1f)]
    public void OneWalker_RowSpawnedReversed_BitEqualPath(int count, float spacing, float offset)
    {
        Trace a = LongRow(count, spacing, flank: true, offset: offset).On;
        Trace b = LongRow(count, spacing, flank: true, offset: offset, reversed: true).On;
        _out.WriteLine($"row {count} x {spacing} m, offset {offset}: arrived {a.ArrivedTick}, gave up {a.GaveUpTick}, end {a.Path[^1]}");
        for (int t = 0; t < a.Path.Length; t++) Assert.True(a.Path[t] == b.Path[t], $"tick {t}: {a.Path[t]} vs {b.Path[t]}");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // (b) Gaps of exactly one body width, and a row at an angle.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 12 Crossbowmen with centre spacing 1.6 m (edge gaps 0.8 m: exactly the walker's diameter, so "not linked": every
    /// member its own block), 1.62 m and 1.58 m. Bound: the walker arrives (through a gap or round), no blocked tick.
    /// </summary>
    [Theory]
    [InlineData(1.58f)]
    [InlineData(1.6f)]
    [InlineData(1.62f)]
    public void RowWithGapsOfOneBodyWidth_WalkerArrives(float spacing)
    {
        (Trace off, Trace on) = LongRow(12, spacing, flank: true, offset: 0.1f);
        Line($"row of 12, {spacing} m apart (edge gap {spacing - 0.8f:F2} m)", off, on);
        Assert.True(on.Finite && on.BlockedTicks == 0);
        Assert.True(on.ArrivedTick >= 0, $"never arrived (gave up {on.GaveUpTick}), end {on.Path[^1]}");
        Assert.True(off.ArrivedTick < 0 || on.ArrivedTick >= 0);
    }

    /// <summary>A row of 10 at 30, 45 and 60 degrees to the walker's straight line: it arrives, at most 2 side flips.</summary>
    [Theory]
    [InlineData(30f)]
    [InlineData(45f)]
    [InlineData(60f)]
    public void RowAtAnAngle_WalkerArrives(float degrees)
    {
        Trace Once(bool f)
        {
            Simulation sim = NewSim(LocalMovementTests.Flat(80), capacity: 32);
            sim.World.FlankRuleOn = f;
            var mid = new Vector2(70f, 80f);
            float a = degrees * MathF.PI / 180f;
            var along = new Vector2(MathF.Sin(a), MathF.Cos(a)); // 0 deg: along y (square to the walk along x)
            var normal = new Vector2(along.Y, -along.X);
            Staged s = Plant(sim, StraightRow(mid, along, 10, 1f), normal);
            EntityHandle w = Place(sim, 0, HeavyInfantry, mid + new Vector2(-3f, 0.2f));
            Vector2 goal = mid + new Vector2(5f, 0f);
            sim.Enqueue(Command.Move(0, w, goal));
            return Run(s, w, 600, goal);
        }
        Trace off = Once(false), on = Once(true);
        Line($"row of 10 at {degrees} deg", off, on);
        Assert.True(on.Finite && on.BlockedTicks == 0);
        Assert.True(on.ArrivedTick >= 0, $"never arrived (gave up {on.GaveUpTick}), end {on.Path[^1]}");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // (b) A row whose one end meets a cliff: the walker must take the open end, never a blocked cell.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 48 x 48 map, cells y &lt;= 15 level 1 (the cliff edge row y 15 blocked, ground from y 16 = 32 m). A row of 10 along
    /// y from the cliff foot (first centre 32.5 m) at x 50; a Heavy Infantry 3 m behind the row near its cliff end (the
    /// nearer end is the sealed one), Move to 5 m in front. Bound: it arrives by the open end, no blocked tick.
    /// </summary>
    [Theory]
    [InlineData(1)] // the walker behind the second member
    [InlineData(3)]
    [InlineData(5)]
    public void RowAgainstACliff_WalkerTakesTheOpenEnd(int behindMember)
    {
        var rows = new string[48];
        for (int y = 0; y < 48; y++) rows[y] = new string(y <= 15 ? '1' : '0', 48);
        Trace Once(bool f)
        {
            Simulation sim = NewSim(LocalMovementTests.Rows(rows), capacity: 32);
            sim.World.FlankRuleOn = f;
            NavGrid g = sim.World.NavGrid;
            Assert.False(g.IsPassable(20, 15));
            Assert.True(g.IsPassable(20, 16));
            var row = new Vector2[10];
            for (int k = 0; k < 10; k++) row[k] = new Vector2(50f, 32.5f + k);
            Staged s = Plant(sim, row, Vector2.UnitX);
            EntityHandle w = Place(sim, 0, HeavyInfantry, new Vector2(47f, 32.5f + behindMember));
            Vector2 goal = new(55f, 32.5f + behindMember);
            sim.Enqueue(Command.Move(0, w, goal));
            return Run(s, w, 600, goal);
        }
        Trace off = Once(false), on = Once(true);
        Line($"row from the cliff foot, walker behind member {behindMember}", off, on);
        Assert.True(on.Finite && on.BlockedTicks == 0);
        Assert.True(on.ArrivedTick >= 0, $"never arrived (gave up {on.GaveUpTick}), end {on.Path[^1]}");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // (d) A 1-cell corridor plugged by an own planted fighter: wait (or give up as before), never walk into a wall.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A 1-cell corridor along cell row 10 (y 20-22 m) through a cliff band x 10-30, an own Catapult planted in its middle
    /// (attacking a holding Raider at the corridor's far mouth). A Heavy Infantry walks in from the west to the east side.
    /// Bound: no blocked tick, positions finite, and with the rule on it is no farther from the plug at the end than with it
    /// off plus 1 m (no flank backwards out of the corridor).
    /// </summary>
    [Fact]
    public void CorridorPluggedByOwnPlantedFighter_WaitsNotFlanksIntoAWall()
    {
        var rows = new string[24];
        for (int y = 0; y < 24; y++)
        {
            char[] r = new string('0', 48).ToCharArray();
            if (y != 10) for (int x = 10; x <= 30; x++) r[x] = '1';
            rows[y] = new string(r);
        }
        (Trace T, float D) Once(bool f)
        {
            Simulation sim = NewSim(LocalMovementTests.Rows(rows), capacity: 16);
            sim.World.FlankRuleOn = f;
            NavGrid g = sim.World.NavGrid;
            Assert.True(g.IsPassable(20, 10) && !g.IsPassable(20, 9) && !g.IsPassable(20, 11));
            Vector2 plug = g.CellCenter(20, 10);
            // A Catapult (radius 0.9): with a Heavy Infantry (0.4) it can't pass in the 2 m corridor. Its target 10 m east.
            Staged s = Plant(sim, new[] { plug }, Vector2.UnitX, shooter: TestSim.Data.FindUnit("malazan_catapult"));
            EntityHandle w = Place(sim, 0, HeavyInfantry, g.CellCenter(8, 10));
            Vector2 goal = g.CellCenter(36, 10);
            sim.Enqueue(Command.Move(0, w, goal));
            Trace tr = Run(s, w, 400, goal);
            return (tr, Vector2.Distance(tr.Path[^1], plug));
        }
        (Trace off, float dOff) = Once(false);
        (Trace on, float dOn) = Once(true);
        Line("1-cell corridor, own planted plug", off, on);
        _out.WriteLine($"distance to the plug at the end: off {dOff:F2} m, on {dOn:F2} m");
        Assert.True(on.Finite && on.BlockedTicks == 0);
        Assert.True(dOn <= dOff + 1f, $"flanked away: {dOn:F2} m from the plug, {dOff:F2} with the rule off");
    }

    /// <summary>
    /// (d) A mixed line: 10 posts 1 m apart, own planted Crossbowmen on the even ones and holding enemy Raiders on the odd
    /// ones (the line has no gap a Heavy Infantry fits through). 5 Heavy Infantry behind it, Move 5 m beyond. Bound: with the
    /// rule on no more of them get past the line than with it off, nobody stands on a blocked cell (the own units are single
    /// blocks with enemies between: walking "round" one must not mean through the enemy beside it).
    /// </summary>
    [Fact]
    public void MixedOwnAndEnemyLine_StillHolds()
    {
        int Passed(bool f, out int blocked)
        {
            Simulation sim = NewSim(LocalMovementTests.Flat(80), capacity: 48);
            sim.World.FlankRuleOn = f;
            UnitStore u = sim.World.Units;
            var mid = new Vector2(70f, 80f);
            Vector2[] posts = StraightRow(mid, Vector2.UnitY, 10, 1f);
            var own = new Vector2[5];
            for (int k = 0; k < 5; k++) own[k] = posts[2 * k];
            Staged s = Plant(sim, own, Vector2.UnitX, reach: 12f);
            for (int k = 0; k < 5; k++)
            {
                EntityHandle e = Place(sim, 1, Raider, posts[2 * k + 1]);
                u.Hold[e.Index] = true;
            }
            var walkers = new EntityHandle[5];
            for (int k = 0; k < 5; k++)
            {
                walkers[k] = Place(sim, 0, HeavyInfantry, mid + new Vector2(-3f, k - 2f));
                sim.Enqueue(Command.Move(0, walkers[k], mid + new Vector2(5f, k - 2f)));
            }
            NavGrid g = sim.World.NavGrid;
            blocked = 0;
            var past = new bool[5];
            var prev = new Vector2[5];
            for (int k = 0; k < 5; k++) prev[k] = u.Position[walkers[k].Index];
            for (int t = 0; t < 400; t++)
            {
                Tick(s);
                for (int k = 0; k < 5; k++)
                {
                    Vector2 p = u.Position[walkers[k].Index];
                    if (!g.WorldToCell(p, out int cx, out int cy) || !g.IsPassable(cx, cy)) blocked++;
                    // Through, not round: its centre crosses the line's x inside the line's span.
                    if (prev[k].X <= mid.X && p.X > mid.X && p.Y > posts[0].Y - 0.5f && p.Y < posts[9].Y + 0.5f) past[k] = true;
                    prev[k] = p;
                }
            }
            return past.Count(b => b);
        }
        int off = Passed(false, out int bOff), on = Passed(true, out int bOn);
        _out.WriteLine($"mixed own / enemy line: walkers through it (within the line's span) off {off}, on {on}; blocked ticks off {bOff}, on {bOn}");
        Assert.Equal(0, bOn);
        Assert.True(on <= off, $"the flank rule took {on} walkers through a mixed line, {off} with it off");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // (c) Oscillation: the row advances, or dies, mid-flank.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The 10-row; 30 ticks into the walker's flank the row is changed: <paramref name="what"/> = "stop" (every member
    /// Stopped: Idle, shoveable), "kill" (the half on the walker's side killed), "advance" (every member attack-moved 3 m
    /// toward its target: no longer planted while it walks). Bound: it arrives, at most 3 side flips, no blocked tick.
    /// </summary>
    [Theory]
    [InlineData("stop")]
    [InlineData("kill")]
    [InlineData("advance")]
    public void RowChangesMidFlank_WalkerArrivesWithoutSwinging(string what)
    {
        Trace Once(bool f)
        {
            Simulation sim = NewSim(LocalMovementTests.Flat(80), capacity: 32);
            sim.World.FlankRuleOn = f;
            UnitStore u = sim.World.Units;
            var mid = new Vector2(70f, 80f);
            Staged s = Plant(sim, StraightRow(mid, Vector2.UnitY, 10, 1f), Vector2.UnitX);
            EntityHandle w = Place(sim, 0, HeavyInfantry, mid + new Vector2(-3f, 0.2f));
            Vector2 goal = mid + new Vector2(5f, 0f);
            sim.Enqueue(Command.Move(0, w, goal));
            bool killed = false;
            return Run(s, w, 600, goal, t =>
            {
                if (t != 30) return;
                for (int k = 0; k < s.Row.Length; k++)
                {
                    EntityHandle h = s.Row[k];
                    if (what == "stop") sim.Enqueue(Command.Stop(0, h));
                    else if (what == "advance") sim.Enqueue(Command.AttackMove(0, h, u.Position[h.Index] + new Vector2(3f, 0f)));
                    else if (what == "kill" && (u.Position[h.Index].Y - mid.Y) * (u.Position[w.Index].Y - mid.Y) > 0f)
                    {
                        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, h, false, 100_000));
                        killed = true;
                    }
                }
                if (what == "kill") Assert.True(killed, "setup: nothing killed");
            });
        }
        Trace off = Once(false), on = Once(true);
        Line($"row {what} mid-flank", off, on);
        Assert.True(on.Finite && on.BlockedTicks == 0);
        Assert.True(on.SideFlips <= 8, $"swung {on.SideFlips} times");
        Assert.True(on.ArrivedTick >= 0, $"never arrived (gave up {on.GaveUpTick}), end {on.Path[^1]}");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // (f) Chase keep-alive: a target the chaser can't reach on foot (up a sealed cliff) while its own line shoots it from
    // the cliff foot. The give-up must still fire.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>40 x 40 ground with a level-1 plateau at x 20-35, y 5-34 and no ramp (cliff ring at x 20 / 35, y 5 / 34).</summary>
    private static string[] PlateauMap()
    {
        var rows = new string[40];
        for (int y = 0; y < 40; y++)
        {
            var c = new char[40];
            for (int x = 0; x < 40; x++) c[x] = x >= 20 && x <= 35 && y >= 5 && y <= 34 ? '1' : '0';
            rows[y] = new string(c);
        }
        return rows;
    }

    public enum ChaseKind { Ordered, AttackMove, Retaliate }

    /// <summary>
    /// The plateau map; a holding enemy Raider on the plateau at cell (22, 20) (x 45 m), spotted for player 0. A row of
    /// <paramref name="rowCount"/> Crossbowmen 1 m apart along y at x 38.6 m (cell 19, the cliff foot: the ground nearest
    /// the target) planted on it. <paramref name="chasers"/> Heavy Infantry 3 m behind the row, given the chase
    /// (<paramref name="kind"/>). Returns, per chaser, the tick its chase was given up (-1: never in <paramref name="ticks"/>)
    /// and how many ticks it still held the target at the end of the run.
    /// </summary>
    private (int[] GaveUp, int[] HeldLast500, int[] MovingLast500) CliffChase(ChaseKind kind, int rowCount, int chasers, bool flank, int ticks)
    {
        Simulation sim = NewSim(LocalMovementTests.Rows(PlateauMap()), capacity: rowCount + chasers + 8);
        sim.World.FlankRuleOn = flank;
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        TestSim.KeepIn(sim, 1);
        // A plateau with no ramp is sealed: the navgrid blocks it whole (as CombatFriendExceptionQaTests' scene); units
        // placed there stand on it. The cliff foot (cell 19) is the nearest open ground.
        Assert.True(g.IsPassable(19, 20) && !g.IsPassable(20, 20) && !g.IsPassable(22, 20), "setup: the map");
        const int top = 21;
        EntityHandle target = Place(sim, 1, Raider, g.CellCenter(top + 1, 20));
        u.Hold[target.Index] = true;
        Spot(sim, 0, target);
        var row = new EntityHandle[rowCount];
        float y0 = 41f - (rowCount - 1) / 2f;
        for (int k = 0; k < rowCount; k++)
        {
            row[k] = Place(sim, 0, Crossbowman, new Vector2(38.6f, y0 + k));
            sim.Enqueue(Command.Attack(0, row[k], target, false));
        }
        var s = new Staged { Sim = sim, Row = row, Targets = new[] { target }, Walkers = new(), Normal = Vector2.UnitX };
        for (int t = 0; t < 60 && !row.All(h => u.State[h.Index] == UnitState.Attacking); t++) Tick(s);
        Assert.True(row.All(h => u.State[h.Index] == UnitState.Attacking), "setup: the row didn't plant");
        var c = new EntityHandle[chasers];
        for (int k = 0; k < chasers; k++)
        {
            c[k] = Place(sim, 0, HeavyInfantry, new Vector2(35.6f - k / 5, 41f + (k % 5 - 2f)));
            if (kind == ChaseKind.Ordered) sim.Enqueue(Command.Attack(0, c[k], target, false));
            else if (kind == ChaseKind.AttackMove) sim.Enqueue(Command.AttackMove(0, c[k], g.CellCenter(top + 1, 20)));
        }
        var gaveUp = Enumerable.Repeat(-1, chasers).ToArray();
        var held = new int[chasers];
        var moving = new int[chasers];
        for (int t = 0; t < ticks; t++)
        {
            Tick(s);
            for (int k = 0; k < chasers; k++)
            {
                int i = c[k].Index;
                if (gaveUp[k] < 0 && u.GiveUps[i] > 0) gaveUp[k] = t;
                if (t >= ticks - 500)
                {
                    if (u.Target[i] == target) held[k]++;
                    if (u.State[i] == UnitState.Moving) moving[k]++;
                }
            }
        }
        return (gaveUp, held, moving);
    }

    /// <summary>
    /// The forced case for the "its walk reaches its goal" half of the keep-alive: the chase goal is the cliff foot, which
    /// the walk reaches (a finite field cost), but no standing point there is in a melee unit's reach of the target. The
    /// chase must still end: every chaser gives up within 3,000 ticks, and none still holds the target over the last 500
    /// ticks of a 4,000-tick run (an ordered Attack too: M4-H3's "nobody paces a cliff").
    /// </summary>
    [Theory]
    [InlineData(ChaseKind.Ordered, 10, 1)]
    [InlineData(ChaseKind.Ordered, 10, 5)]
    [InlineData(ChaseKind.AttackMove, 10, 5)]
    [InlineData(ChaseKind.Retaliate, 10, 5)]
    [InlineData(ChaseKind.AttackMove, 20, 5)]
    [InlineData(ChaseKind.Ordered, 20, 5)]
    public void ChaseOfATargetUpASealedCliff_OwnLineShootingItFromTheFoot_StillGivesUp(ChaseKind kind, int rowCount, int chasers)
    {
        (int[] gOff, int[] hOff, int[] mOff) = CliffChase(kind, rowCount, chasers, flank: false, ticks: 4000);
        (int[] gOn, int[] hOn, int[] mOn) = CliffChase(kind, rowCount, chasers, flank: true, ticks: 4000);
        _out.WriteLine($"{kind}, row {rowCount}, {chasers} chasers: gave up at off [{string.Join(", ", gOff)}] on [{string.Join(", ", gOn)}]; "
            + $"held the target over the last 500 ticks off [{string.Join(", ", hOff)}] on [{string.Join(", ", hOn)}]; Moving off [{string.Join(", ", mOff)}] on [{string.Join(", ", mOn)}]");
        if (kind == ChaseKind.Retaliate && gOn.All(x => x < 0) && hOn.All(x => x == 0)) return; // nobody took it: nothing to give up
        for (int k = 0; k < chasers; k++)
        {
            Assert.True(hOn[k] == 0, $"chaser {k} still held the unreachable target over the last 500 ticks ({hOn[k]} ticks, Moving {mOn[k]}); off: {hOff[k]}");
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Criterion 6 as written: criterion 1's scene (10 Crossbowmen on a Town Hall, 10 Heavy Infantry behind, Attack) in both
    // spawn orders. Printed: the first tick the two orders' positions part, rule off and on (the dev reports both part:
    // BUG-0046's slot-order clips). Bound: the rule on parts no earlier than the rule off does.
    // M5-H3 (BUG-0885 / BUG-0046, decision): spawn order is not part of the sim's input contract. Slot order is a documented
    // known limit (docs/03 "Determinism", "the slot-order class": surround claims taken in slot order, wall clips, shoves),
    // not a determinism break, so the reversed rows keep the "no earlier than the rule off" bound, and the same-order row
    // (false, false) asserts what is promised: the same spawns in the same order never part, rule off or on.
    // ------------------------------------------------------------------------------------------------------------------

    private static int FirstParting(bool flank, bool reverseRow, bool reverseMelee, out Vector2[] a)
    {
        Vector2[][] runs = new Vector2[2][];
        for (int r = 0; r < 2; r++)
        {
            bool rev = r == 1;
            var sim = new Simulation(TestSim.Config(7, 2, 40, 256), LocalMovementTests.Flat(64));
            World w = sim.World;
            UnitStore u = w.Units;
            w.FlankRuleOn = flank;
            TestSim.KeepIn(sim, 1);
            int hallType = -1;
            for (int b = 0; b < w.Data.Buildings.Length; b++)
                if (w.Data.Buildings[b].Faction == 1 - w.Data.Units[HeavyInfantry].Faction && w.Data.Buildings[b].Slot == Data.BuildingSlot.TownHall) hallType = b;
            Assert.True(w.Buildings.Spawn(1, hallType, 30 * 64 + 40, out EntityHandle hall));
            const float rowX = 80f - 0.4f - 14.9f;
            var row = new EntityHandle[10];
            for (int n = 0; n < 10; n++)
            {
                int k = rev && reverseRow ? 9 - n : n;
                row[k] = Place(sim, 0, Crossbowman, new Vector2(rowX, 64f + (k - 4.5f)));
                sim.Enqueue(Command.Attack(0, row[k], hall, true));
            }
            for (int t = 0; t < 40; t++)
            {
                for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Hp[i] = w.Data.Units[u.TypeId[i]].Hp;
                sim.Tick();
            }
            var mel = new EntityHandle[10];
            for (int n = 0; n < 10; n++)
            {
                int k = rev && reverseMelee ? 9 - n : n;
                mel[k] = Place(sim, 0, HeavyInfantry, new Vector2(rowX - 3f - k / 5, 64f + (k % 5 - 2f)));
            }
            for (int n = 0; n < 10; n++)
            {
                int k = rev && reverseMelee ? 9 - n : n;
                sim.Enqueue(Command.Attack(0, mel[k], hall, true));
            }
            var trace = new Vector2[200 * 10];
            for (int t = 0; t < 200; t++)
            {
                for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Hp[i] = w.Data.Units[u.TypeId[i]].Hp;
                if (w.Buildings.IsAlive(hall)) w.Buildings.SetHp(hall.Index, w.Data.Buildings[hallType].Hp);
                sim.Tick();
                for (int k = 0; k < 10; k++) trace[t * 10 + k] = u.Position[mel[k].Index];
            }
            runs[r] = trace;
        }
        a = runs[0];
        for (int t = 0; t < 200; t++)
            for (int k = 0; k < 10; k++)
                if (runs[0][t * 10 + k] != runs[1][t * 10 + k]) return t;
        return -1;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Criterion1Scene_BothSpawnOrders_FirstParting(bool reverseRow, bool reverseMelee)
    {
        int off = FirstParting(false, reverseRow, reverseMelee, out _);
        int on = FirstParting(true, reverseRow, reverseMelee, out _);
        _out.WriteLine($"criterion 1 scene, row reversed {reverseRow}, melee reversed {reverseMelee}: positions part at tick off {off}, on {on} (-1: never in 200)");
        if (!reverseRow && !reverseMelee)
        {
            Assert.True(off < 0 && on < 0, $"the same spawn order parted: off {off}, on {on}");
            return;
        }
        Assert.True(on < 0 || (off >= 0 && on >= off), $"the rule parts the spawn orders earlier: on {on}, off {off}");
    }

    /// <summary>
    /// Criterion 7 at scale: the 500 v 500 mixed brawl (line and shock units walking round their own crossbow / archer
    /// ranks: the rule's busiest scene), ticks 100-140, allocates nothing; also a 32-member block (the bound) mid-flank.
    /// </summary>
    [Fact]
    public void MixedBrawl500v500_And32Block_MidFlank_AllocateNothing()
    {
        Simulation sim = Flat(size: 128, units: 1000);
        MixedBrawl(sim, 500, new Vector2(128f, 128f), 8f);
        for (int t = 0; t < 100; t++) sim.Tick();
        AllocationProbe.AssertZero(() =>
        {
            for (int t = 0; t < 40; t++) sim.Tick();
        }, _out);
        Simulation row = NewSim(LocalMovementTests.Flat(80), capacity: 80);
        var mid = new Vector2(70f, 80f);
        Staged s = Plant(row, StraightRow(mid, Vector2.UnitY, 32, 1f), Vector2.UnitX, reach: 14.5f);
        for (int k = 0; k < 6; k++)
        {
            EntityHandle w = Place(row, 0, HeavyInfantry, mid + new Vector2(-3f, k - 2.5f));
            row.Enqueue(Command.Move(0, w, mid + new Vector2(5f, 0f)));
        }
        for (int t = 0; t < 20; t++) Tick(s);
        AllocationProbe.AssertZero(() =>
        {
            for (int t = 0; t < 40; t++) Tick(s);
        }, _out);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // (a) Determinism: a seeded fuzz of planted rows, walkers and orders, run twice, hashes equal every 50 ticks;
    // invariants every tick.
    // ------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    public void FlankFuzz_TwinRunsHashEqual_InvariantsHold(ulong seed)
    {
        ulong[] a = FlankFuzz(seed), b = FlankFuzz(seed);
        Assert.Equal(a, b);
        if (seed > 1) Assert.NotEqual(FlankFuzz(seed - 1)[^1], a[^1]);
    }

    /// <summary>
    /// 2 players on a 64 map: per player 3 rows of 6-14 Crossbowmen (random angle) each planted on its own holding enemy
    /// row; 30 melee walkers per player; every 40 ticks 10 random orders (Move, Attack a random enemy, attack-move, Stop,
    /// the same order spammed, an order to a dead handle). 1,200 ticks, no hp top-up. Returns hashes every 50 ticks.
    /// </summary>
    private static ulong[] FlankFuzz(ulong seed)
    {
        const int size = 64;
        var sim = new Simulation(TestSim.Config(seed, 2, 400, 4000), LocalMovementTests.Flat(size));
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        var rng = new Determinism.SimRng(seed, 911);
        var all = new List<EntityHandle>();
        for (int p = 0; p < 2; p++)
        {
            for (int r = 0; r < 3; r++)
            {
                int n = 6 + rng.NextInt(0, 9);
                float a = rng.NextFloat() * MathF.PI;
                var along = new Vector2(MathF.Cos(a), MathF.Sin(a));
                var normal = new Vector2(along.Y, -along.X);
                var mid = new Vector2(20f + rng.NextFloat() * 88f, 20f + rng.NextFloat() * 88f);
                Vector2[] pts = StraightRow(mid, along, n, 1f);
                for (int k = 0; k < n; k++)
                {
                    Vector2 rp = Vector2.Clamp(pts[k], new Vector2(4f), new Vector2(size * 2 - 4));
                    Vector2 tp = Vector2.Clamp(pts[k] + normal * 10f, new Vector2(4f), new Vector2(size * 2 - 4));
                    if (!u.TrySpawn(1 - p, Raider, w.Data.Units[Raider], tp, out EntityHandle t)) continue;
                    u.Hold[t.Index] = true;
                    if (!u.TrySpawn(p, Crossbowman, w.Data.Units[Crossbowman], rp, out EntityHandle c)) continue;
                    sim.Enqueue(Command.Attack(p, c, t, false));
                    all.Add(t);
                    all.Add(c);
                }
            }
            for (int k = 0; k < 30; k++)
            {
                var at = new Vector2(8f + rng.NextFloat() * 112f, 8f + rng.NextFloat() * 112f);
                if (u.TrySpawn(p, k % 2 == 0 ? HeavyInfantry : Raider, w.Data.Units[k % 2 == 0 ? HeavyInfantry : Raider], at, out EntityHandle m)) all.Add(m);
            }
        }
        var hashes = new List<ulong>();
        EntityHandle last = default;
        for (int t = 0; t < 1200; t++)
        {
            if (t % 40 == 0)
            {
                for (int q = 0; q < 10; q++)
                {
                    EntityHandle h = all[rng.NextInt(0, all.Count)];
                    int owner = u.IsAlive(h) ? u.Owner[h.Index] : rng.NextInt(0, 2);
                    var pt = new Vector2(4f + rng.NextFloat() * 120f, 4f + rng.NextFloat() * 120f);
                    switch (rng.NextInt(0, 6))
                    {
                        case 0: sim.Enqueue(Command.Move(owner, h, pt)); break;
                        case 1:
                            EntityHandle e = all[rng.NextInt(0, all.Count)];
                            sim.Enqueue(Command.Attack(owner, h, e, false));
                            break;
                        case 2: sim.Enqueue(Command.AttackMove(owner, h, pt)); break;
                        case 3: sim.Enqueue(Command.Stop(owner, h)); break;
                        case 4:
                            for (int z = 0; z < 5; z++) sim.Enqueue(Command.Move(owner, h, pt));
                            break;
                        case 5: if (last.Generation != 0) sim.Enqueue(Command.Move(owner, last, pt)); break;
                    }
                    if (!u.IsAlive(h)) last = h;
                }
            }
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 p = u.Position[i];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"seed {seed} tick {t}: unit {i} at {p}");
                Assert.True(g.WorldToCell(p, out int cx, out int cy), $"seed {seed} tick {t}: unit {i} off the map at {p}");
                if (u.State[i] != UnitState.Moving) Assert.True(g.IsPassable(cx, cy), $"seed {seed} tick {t}: unit {i} settled on a blocked cell {cx},{cy}");
                Assert.InRange(u.Hp[i], 0, w.Data.Units[u.TypeId[i]].Hp);
                if (u.Target[i].Generation != 0 && !u.TargetIsBuilding[i])
                    Assert.True(u.IsAlive(u.Target[i]) || u.State[i] != UnitState.Attacking, $"seed {seed} tick {t}: unit {i} attacking a dead target");
            }
            if (t % 50 == 49) hashes.Add(sim.StateHash());
        }
        return hashes.ToArray();
    }
}
