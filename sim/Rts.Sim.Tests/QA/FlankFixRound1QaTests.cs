using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-P2 re-inspection after fix round 1 (session 2026-10-17-0554): attacks on the round's changes that compile on the
/// pre-fix commit too (no <c>UnitStore.Flank</c> here), so each row can be A/B'd against 3ec22ee.
/// (1) The keep-alive tightening (<c>StandsInReachAtGoal</c> on M4-H3's ordered keep and BUG-0731's spot keep, and
/// <c>Flanking</c> on the slice-2 friend exception) must not make chasers give up a target they can reach: a hall ringed by
/// friends, a hall walled on three sides by trees, a unit up a plateau with a ramp shot at from the cliff foot.
/// (2) BUG-0881 variants the dev didn't run: lines of 11-31 at angles, Moves re-issued every tick, a re-order to the far end
/// mid-flank, and a 24-line whose near end meets a cliff.
/// </summary>
[Collection(SerialCollection.Name)]
public class FlankFixRound1QaTests
{
    private readonly ITestOutputHelper _out;

    public FlankFixRound1QaTests(ITestOutputHelper output) => _out = output;

    public enum Kind { Ordered, AttackMove }

    private static int Hall(GameData data)
    {
        for (int b = 0; b < data.Buildings.Length; b++)
            if (data.Buildings[b].Faction == 1 && data.Buildings[b].Slot == BuildingSlot.TownHall) return b;
        throw new InvalidOperationException("no whirlwind hall");
    }

    /// <summary>Tops every unit's hp up and the given building's, after counting whether it took damage last tick.</summary>
    private static bool TickTopped(Simulation sim, EntityHandle building)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        bool hit = false;
        if (building.Generation != 0 && w.Buildings.IsAlive(building))
        {
            int max = w.Data.Buildings[w.Buildings.TypeId[building.Index]].Hp;
            hit = w.Buildings.Hp[building.Index] < max;
            w.Buildings.SetHp(building.Index, max);
        }
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Hp[i] = w.Data.Units[u.TypeId[i]].Hp;
        sim.Tick();
        return hit;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // (1a) A hall ringed by friends: more melee than the ring has spots, plus queued ranged units, ordered or attack-move.
    // The hall's hp topped up, so the fight never ends. Bound: no chaser ever gives the hall up (it is reachable: a spot
    // frees on a death or the ring's queue holds them), and every ranged unit strikes.
    // ------------------------------------------------------------------------------------------------------------------

    /// <param name="trees">0: open ground; 3: trees two cells deep on the hall's north, east and west (open south), the
    /// chasers starting north-east of it, so their walk leads round.</param>
    private (int GaveUp, int MeleeStruck, int RangedStruck, int HeldAtEnd, int Hits, string Detail) HallScene(Kind kind, int melee, int ranged, int trees, bool flank, int ticks)
    {
        var sim = new Simulation(TestSim.Config(7, 2, melee + ranged + 16, 8 * (melee + ranged) + 64), LocalMovementTests.Flat(64));
        sim.World.FlankRuleOn = flank;
        TestSim.Explored(sim);
        World w = sim.World;
        UnitStore u = w.Units;
        TestSim.KeepIn(sim, 1);
        Assert.True(w.Buildings.Spawn(1, Hall(w.Data), 30 * 64 + 40, out EntityHandle hall)); // footprint cells 40-43, 30-33
        if (trees == 3)
        {
            int tree = w.Data.FindResource("tree");
            for (int y = 28; y <= 33; y++)
                for (int x = 38; x <= 45; x++)
                {
                    if (x >= 40 && x <= 43 && y >= 30) continue;
                    if (y >= 30 && x >= 40 && x <= 43) continue;
                    Assert.True(w.Resources.Spawn(tree, y * 64 + x, w.Data.Rules.TreeWood, out _), $"tree ({x}, {y})");
                }
            Spotter(sim, 0, new Vector2(84f, 75f)); // the hall is out of the chasers' sight at the start: an Attack needs it seen
        }
        var all = new List<EntityHandle>();
        var isRanged = new List<bool>();
        for (int k = 0; k < melee + ranged; k++)
        {
            bool r = k >= melee;
            Vector2 at = new Vector2(96f + (k % 8) * 1.2f, 40f - (k / 8) * 1.2f);
            if (trees == 0) at = new Vector2(70f + (k % 8) * 1.2f, 50f - (k / 8) * 1.2f);
            EntityHandle h = Place(sim, 0, r ? Crossbowman : HeavyInfantry, at);
            all.Add(h);
            isRanged.Add(r);
            if (kind == Kind.Ordered) sim.Enqueue(Command.Attack(0, h, hall, isBuilding: true));
            else sim.Enqueue(Command.AttackMove(0, h, new Vector2(84f, 64f)));
        }
        var struck = new bool[all.Count];
        int hits = 0;
        for (int t = 0; t < ticks; t++)
        {
            if (TickTopped(sim, hall)) hits++;
            for (int k = 0; k < all.Count; k++)
            {
                int i = all[k].Index;
                if (u.State[i] == UnitState.Attacking && u.Target[i] == hall && u.TargetIsBuilding[i]) struck[k] = true;
            }
        }
        int gaveUp = 0, ms = 0, rs = 0, held = 0;
        var detail = new System.Text.StringBuilder();
        for (int k = 0; k < all.Count; k++)
        {
            int i = all[k].Index;
            if (u.GiveUps[i] > 0)
            {
                gaveUp++;
                detail.Append($" [unit {i} {(isRanged[k] ? "XB" : "HI")} gave up x{u.GiveUps[i]}, {u.State[i]} at {u.Position[i]}, struck {struck[k]}]");
            }
            if (struck[k]) { if (isRanged[k]) rs++; else ms++; }
            else
            {
                detail.Append($" [unit {i} {(isRanged[k] ? "XB" : "HI")} never struck: {u.State[i]} at {u.Position[i]}, target {u.Target[i].Index}/{u.TargetIsBuilding[i]}, mode {u.Mode[i]}, stall {u.ChaseStall[i]}; own within 2 m:");
                for (int j = 0; j < u.Capacity; j++)
                    if (j != i && u.Alive[j] && u.Owner[j] == 0 && Vector2.Distance(u.Position[j], u.Position[i]) < 2f) detail.Append($" {j}:{u.State[j]}");
                detail.Append(']');
            }
            if (u.Target[i] == hall) held++;
        }
        return (gaveUp, ms, rs, held, hits, detail.ToString());
    }

    [Theory]
    [InlineData(Kind.Ordered, 30, 0, 0)]
    [InlineData(Kind.Ordered, 30, 12, 0, Skip = "BUG-0889: one ordered Crossbowman queued behind its own Crossbowmen on the hall never strikes (pre-existing, 321d7d6 too)")]
    [InlineData(Kind.AttackMove, 30, 12, 0)]
    [InlineData(Kind.Ordered, 20, 8, 3, Skip = "BUG-0889: one ordered Crossbowman queued behind its own Crossbowmen on the hall never strikes (pre-existing, 321d7d6 too)")]
    [InlineData(Kind.AttackMove, 20, 8, 3)]
    public void HallRingedByFriends_ReachableChasersNeverGiveItUp(Kind kind, int melee, int ranged, int trees)
    {
        var on = HallScene(kind, melee, ranged, trees, flank: true, ticks: 2000);
        _out.WriteLine($"{kind}, {melee} HI + {ranged} XB, trees {trees}: gave up {on.GaveUp}, melee struck {on.MeleeStruck}/{melee}, ranged struck {on.RangedStruck}/{ranged}, "
            + $"holding the hall at the end {on.HeldAtEnd}/{melee + ranged}, ticks the hall was hit {on.Hits}{on.Detail}");
        Assert.True(on.Hits > 0, "setup: nobody struck the hall");
        Assert.True(on.RangedStruck == ranged, $"ranged struck {on.RangedStruck}/{ranged}");
        if (kind == Kind.Ordered)
            Assert.True(on.GaveUp == 0, $"{on.GaveUp} ordered chasers gave a reachable hall up:{on.Detail}");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // (1b) A unit up a plateau that HAS a ramp, an own Crossbowman row shooting it from the cliff foot (the BUG-0880
    // geometry, but reachable). The chasers' route leads away (round to the ramp). Bound: every chaser reaches it
    // (stands Attacking it) and none gives it up.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>40 x 40 cells, a level-1 plateau at x 20-35, y 5-34 with a 3-wide ramp on its south edge (y 35, x 26-28).</summary>
    private static Heightmap RampedPlateau()
    {
        const int w = 40, h = 40;
        float height = MapConstants.LevelHeight;
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                bool plateau = x >= 20 && x <= 35 && y >= 5 && y <= 34;
                bool ramp = y == 35 && x >= 26 && x <= 28;
                levels[i] = (byte)(plateau ? 1 : 0);
                elevations[i] = plateau ? height : ramp ? height / 2 : 0f;
            }
        return new Heightmap(w, h, levels, elevations);
    }

    private (int[] GaveUp, int[] Reached, int[] HeldAtEnd) RampChase(Kind kind, int rowCount, int chasers, bool flank, int ticks)
    {
        var sim = new Simulation(TestSim.Config(7, 2, rowCount + chasers + 8, 8 * (rowCount + chasers) + 32), RampedPlateau());
        sim.World.FlankRuleOn = flank;
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        TestSim.KeepIn(sim, 1);
        Assert.True(g.IsPassable(19, 20) && !g.IsPassable(20, 20) && g.IsPassable(22, 20) && g.IsPassable(27, 35), "setup: the map");
        EntityHandle target = Place(sim, 1, Raider, g.CellCenter(22, 20));
        u.Hold[target.Index] = true;
        Spot(sim, 0, target);
        var row = new EntityHandle[rowCount];
        float y0 = 41f - (rowCount - 1) / 2f;
        for (int k = 0; k < rowCount; k++)
        {
            row[k] = Place(sim, 0, Crossbowman, new Vector2(38.6f, y0 + k));
            sim.Enqueue(Command.Attack(0, row[k], target, false));
        }
        for (int t = 0; t < 60 && !row.All(h => u.State[h.Index] == UnitState.Attacking); t++) TickTopped(sim, default);
        Assert.True(row.All(h => u.State[h.Index] == UnitState.Attacking), "setup: the row didn't plant");
        var c = new EntityHandle[chasers];
        for (int k = 0; k < chasers; k++)
        {
            c[k] = Place(sim, 0, HeavyInfantry, new Vector2(35.6f - k / 5, 41f + (k % 5 - 2f)));
            if (kind == Kind.Ordered) sim.Enqueue(Command.Attack(0, c[k], target, false));
            else sim.Enqueue(Command.AttackMove(0, c[k], g.CellCenter(22, 20)));
        }
        var gaveUp = Enumerable.Repeat(-1, chasers).ToArray();
        var reached = Enumerable.Repeat(-1, chasers).ToArray();
        for (int t = 0; t < ticks; t++)
        {
            TickTopped(sim, default);
            for (int k = 0; k < chasers; k++)
            {
                int i = c[k].Index;
                if (gaveUp[k] < 0 && u.GiveUps[i] > 0) gaveUp[k] = t;
                if (reached[k] < 0 && u.State[i] == UnitState.Attacking && u.Target[i] == target) reached[k] = t;
            }
        }
        var heldAtEnd = c.Select(h => u.Target[h.Index] == target ? 1 : 0).ToArray();
        return (gaveUp, reached, heldAtEnd);
    }

    [Theory]
    [InlineData(Kind.Ordered, 10, 5)]
    [InlineData(Kind.AttackMove, 10, 5)]
    [InlineData(Kind.Ordered, 20, 5)]
    public void ChaseOfATargetUpARampedPlateau_OwnLineShootingFromTheFoot_IsWalkedToTheEnd(Kind kind, int rowCount, int chasers)
    {
        var on = RampChase(kind, rowCount, chasers, flank: true, ticks: 2500);
        _out.WriteLine($"{kind}, row {rowCount}, {chasers} chasers: gave up at [{string.Join(", ", on.GaveUp)}], reached at [{string.Join(", ", on.Reached)}], holding at the end [{string.Join(", ", on.HeldAtEnd)}]");
        if (kind == Kind.Ordered)
        {
            Assert.True(on.GaveUp.All(x => x < 0), "an ordered chaser gave up a target its walk reaches");
            Assert.True(on.Reached.All(x => x >= 0), "an ordered chaser never reached a reachable target");
        }
        else Assert.True(on.Reached.Count(x => x >= 0) >= 1, "no attack-mover reached the target");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // (2) BUG-0881 variants: one Heavy Infantry 3 m behind the middle of an own Crossbowman line, 1 m apart, at an angle;
    // targets 14.5 m in front (out of the walker's sight). Move to 5 m beyond the line's middle.
    // ------------------------------------------------------------------------------------------------------------------

    private sealed record Walk(int Arrived, int GaveUp, int Flips, int Blocked, bool Finite, Vector2 End);

    /// <param name="every">Re-issue the Move every this many ticks (0: once), alternating 2 m along the line.</param>
    /// <param name="switchAt">At this tick (if &gt; 0) re-order the Move to 5 m beyond the line's other quarter.</param>
    private Walk LineWalk(int count, float degrees, int every, int switchAt, bool flank, int ticks = 900, float offset = 0.2f, bool reversed = false, bool alternate = true, Action<int, Simulation, EntityHandle>? each = null)
    {
        var sim = new Simulation(TestSim.Config(7, 2, 2 * count + 8, 16 * count + 64), LocalMovementTests.Flat(80));
        sim.World.FlankRuleOn = flank;
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
            Vector2 p = mid + along * (k - (count - 1) / 2f);
            targets[k] = Place(sim, 1, Raider, p + normal * 14.5f);
            u.Hold[targets[k].Index] = true;
            Spot(sim, 0, targets[k]);
        }
        for (int n = 0; n < count; n++)
        {
            int k = reversed ? count - 1 - n : n;
            row[k] = Place(sim, 0, Crossbowman, mid + along * (k - (count - 1) / 2f));
        }
        for (int k = 0; k < count; k++) sim.Enqueue(Command.Attack(0, row[k], targets[k], false));
        for (int t = 0; t < 60 && !row.All(h => u.State[h.Index] == UnitState.Attacking); t++) TickTopped(sim, default);
        Assert.True(row.All(h => u.State[h.Index] == UnitState.Attacking), "setup: the row didn't plant");
        EntityHandle w = Place(sim, 0, HeavyInfantry, mid - normal * 3f + along * offset);
        Vector2 goal = mid + normal * 5f;
        Vector2 other = mid + normal * 5f - along * (count / 4f);
        sim.Enqueue(Command.Move(0, w, goal));
        int arrived = -1, gaveUp = -1, flips = 0, last = 0, blocked = 0;
        bool finite = true, wasMoving = false;
        Vector2 current = goal;
        for (int t = 0; t < ticks && arrived < 0; t++)
        {
            if (switchAt > 0 && t == switchAt)
            {
                current = other;
                sim.Enqueue(Command.Move(0, w, current));
            }
            else if (every > 0 && t > 0 && t % every == 0)
            {
                Vector2 alt = current + along * (alternate && t / every % 2 == 1 ? 2f : 0f);
                sim.Enqueue(Command.Move(0, w, alt));
            }
            Vector2 before = u.Position[w.Index];
            TickTopped(sim, default);
            Vector2 p = u.Position[w.Index];
            finite &= float.IsFinite(p.X) && float.IsFinite(p.Y);
            if (!g.WorldToCell(p, out int cx, out int cy) || !g.IsPassable(cx, cy)) blocked++;
            float lat = Vector2.Dot(p - before, along);
            int sign = lat > 0.05f ? 1 : lat < -0.05f ? -1 : 0;
            if (sign != 0 && last != 0 && sign != last) flips++;
            if (sign != 0) last = sign;
            if (Vector2.Distance(p, current) < 1.2f || Vector2.Distance(p, current + along * 2f) < 1.2f) arrived = t;
            if (gaveUp < 0 && arrived < 0 && wasMoving && u.State[w.Index] == UnitState.Idle && u.GoalCell[w.Index] == -1) gaveUp = t;
            wasMoving = u.State[w.Index] == UnitState.Moving;
            each?.Invoke(t, sim, w);
        }
        return new Walk(arrived, gaveUp, flips, blocked, finite, u.Position[w.Index]);
    }

    private static string Cell(Walk r) => $"arrived {(r.Arrived >= 0 ? r.Arrived.ToString() : "-")}, gave up {(r.GaveUp >= 0 ? r.GaveUp.ToString() : "-")}, flips {r.Flips}, blocked {r.Blocked}, end {r.End}";

    [Theory]
    [InlineData(10, 0f, 0)]
    [InlineData(11, 0f, 0)]
    [InlineData(12, 30f, 0)]
    [InlineData(15, 45f, 0)]
    [InlineData(20, 60f, 0)]
    [InlineData(24, 30f, 0)]
    [InlineData(31, 45f, 0)]
    [InlineData(32, 30f, 0)]
    [InlineData(24, 0f, 3)]
    [InlineData(16, 0f, 5)]
    [InlineData(24, 45f, 2, Skip = "BUG-0887: a Move re-issued to points 2 m apart behind a 24-32 line still swings and never arrives")]
    public void LineAtAnAngle_MoveMaybeReissued_WalkerArrives(int count, float degrees, int every)
    {
        Walk off = LineWalk(count, degrees, every, 0, flank: false);
        Walk on = LineWalk(count, degrees, every, 0, flank: true);
        _out.WriteLine($"line {count} at {degrees} deg, re-issued every {every}: off [{Cell(off)}] -> on [{Cell(on)}]");
        Assert.True(on.Finite && on.Blocked == 0, $"blocked {on.Blocked}");
        Assert.True(on.Arrived >= 0, $"never arrived: {Cell(on)}");
        Assert.True(on.Flips <= 8, $"swung: {Cell(on)}");
    }

    /// <summary>
    /// A Move re-issued every <paramref name="every"/> ticks to two points 2 m apart along the line (a player's click spam),
    /// the walker 3 m behind the line at <paramref name="offset"/> m along it. Bound: it arrives within 900 ticks, at most 8
    /// turns along the line (the dev's <c>LongOwnLine_MoveReissued_KeepsItsSideAndArrives</c> is the 24 / 0.2 m row).
    /// </summary>
    [Theory]
    [InlineData(24, 0.2f, 3)]
    [InlineData(16, -0.3f, 3)]
    [InlineData(24, -4f, 3)]
    [InlineData(16, 1.5f, 2)]
    [InlineData(32, -0.3f, 3, Skip = "BUG-0887: never arrives, 178 turns along the line in 900 ticks")]
    [InlineData(32, 0.2f, 3, Skip = "BUG-0887: never arrives, 252 turns")]
    [InlineData(24, 1.5f, 2, Skip = "BUG-0887: never arrives, 276 turns")]
    [InlineData(24, -0.3f, 2, Skip = "BUG-0887: never arrives, 275 turns")]
    [InlineData(32, 0.2f, 7, Skip = "BUG-0887: arrives at tick 661 after 59 turns")]
    public void LongLine_MoveReissuedToAlternatePoints_Arrives(int count, float offset, int every)
    {
        Walk on = LineWalk(count, 0f, every, 0, flank: true, offset: offset);
        _out.WriteLine($"line {count}, offset {offset}, re-issued every {every}: {Cell(on)}");
        Assert.True(on.Finite && on.Blocked == 0);
        Assert.True(on.Arrived >= 0, $"never arrived: {Cell(on)}");
        Assert.True(on.Flips <= 8, $"swung: {Cell(on)}");
    }

    [Theory]
    [InlineData(24, 30)]
    [InlineData(32, 40)]
    [InlineData(16, 20)]
    public void MidFlank_ReorderedToTheOtherQuarter_WalkerArrives(int count, int switchAt)
    {
        Walk off = LineWalk(count, 0f, 0, switchAt, flank: false);
        Walk on = LineWalk(count, 0f, 0, switchAt, flank: true);
        _out.WriteLine($"line {count}, re-ordered at tick {switchAt} to the other quarter: off [{Cell(off)}] -> on [{Cell(on)}]");
        Assert.True(on.Finite && on.Blocked == 0);
        Assert.True(on.Arrived >= 0, $"never arrived: {Cell(on)}");
    }

    [Theory]
    [InlineData(24, 0.2f)]
    [InlineData(32, -0.3f)]
    public void LongLine_SpawnedReversed_MoveSpammed_BitEqualWalk(int count, float offset)
    {
        Walk a = LineWalk(count, 0f, 3, 0, flank: true, offset: offset);
        Walk b = LineWalk(count, 0f, 3, 0, flank: true, offset: offset, reversed: true);
        _out.WriteLine($"line {count}, offset {offset}: [{Cell(a)}] vs reversed [{Cell(b)}]");
        Assert.Equal(a, b);
    }

    /// <summary>
    /// A 24-line along y from a cliff foot (cells y &lt;= 15 level 1, so the line's north end meets the cliff), the walker
    /// behind member <paramref name="behindMember"/> (the cliff end nearer). Bound: it arrives by the open end, never on a blocked cell.
    /// </summary>
    [Theory]
    [InlineData(1, Skip = "BUG-0888: walks round the open end, gives the Move up on the walk back at tick 229")]
    [InlineData(4, Skip = "BUG-0888: walks round the open end, gives the Move up on the walk back at tick 229")]
    public void LongLineAgainstACliff_WalkerTakesTheOpenEnd(int behindMember)
    {
        var rows = new string[64];
        for (int y = 0; y < 64; y++) rows[y] = new string(y <= 15 ? '1' : '0', 64);
        Walk Once(bool f)
        {
            var sim = new Simulation(TestSim.Config(7, 2, 64, 512), LocalMovementTests.Rows(rows));
            sim.World.FlankRuleOn = f;
            UnitStore u = sim.World.Units;
            NavGrid g = sim.World.NavGrid;
            TestSim.KeepIn(sim, 1);
            const int count = 24;
            var row = new EntityHandle[count];
            for (int k = 0; k < count; k++)
            {
                var p = new Vector2(60f, 32.5f + k);
                EntityHandle t = Place(sim, 1, Raider, p + new Vector2(14.5f, 0f));
                u.Hold[t.Index] = true;
                Spot(sim, 0, t);
                row[k] = Place(sim, 0, Crossbowman, p);
                sim.Enqueue(Command.Attack(0, row[k], t, false));
            }
            for (int t = 0; t < 60 && !row.All(h => u.State[h.Index] == UnitState.Attacking); t++) TickTopped(sim, default);
            Assert.True(row.All(h => u.State[h.Index] == UnitState.Attacking), "setup: the row didn't plant");
            EntityHandle w = Place(sim, 0, HeavyInfantry, new Vector2(57f, 32.5f + behindMember + 0.2f));
            Vector2 goal = new Vector2(65f, 32.5f + behindMember);
            sim.Enqueue(Command.Move(0, w, goal));
            int arrived = -1, gaveUp = -1, flips = 0, last = 0, blocked = 0;
            bool wasMoving = false;
            for (int t = 0; t < 900 && arrived < 0; t++)
            {
                float y0 = u.Position[w.Index].Y;
                TickTopped(sim, default);
                Vector2 p = u.Position[w.Index];
                if (!g.WorldToCell(p, out int cx, out int cy) || !g.IsPassable(cx, cy)) blocked++;
                float d = p.Y - y0;
                int sign = d > 0.05f ? 1 : d < -0.05f ? -1 : 0;
                if (sign != 0 && last != 0 && sign != last) flips++;
                if (sign != 0) last = sign;
                if (Vector2.Distance(p, goal) < 1.2f) arrived = t;
                if (gaveUp < 0 && arrived < 0 && wasMoving && u.State[w.Index] == UnitState.Idle && u.GoalCell[w.Index] == -1) gaveUp = t;
                wasMoving = u.State[w.Index] == UnitState.Moving;
            }
            return new Walk(arrived, gaveUp, flips, blocked, true, u.Position[w.Index]);
        }
        Walk off = Once(false), on = Once(true);
        _out.WriteLine($"24-line from a cliff foot, walker behind member {behindMember}: off [{Cell(off)}] -> on [{Cell(on)}]");
        Assert.True(on.Blocked == 0, $"blocked {on.Blocked}");
        Assert.True(on.Arrived >= 0, $"never arrived: {Cell(on)}");
        Assert.True(on.Flips <= 8, $"swung: {Cell(on)}");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // (1c) BUG-0882 variants: a hall ringed by trees, <paramref name="depth"/> cells deep, with an optional one-cell door
    // on its south face. Melee with no door must give the hall up (nobody can strike it); ranged units shoot over the trees
    // from the nearest open ground and must keep on; melee with a door must get in and strike, not give up.
    // ------------------------------------------------------------------------------------------------------------------

    private (int GaveUp, int Struck, int MovingAtEnd, int HeldAtEnd, int Hits) TreeRing(Kind kind, int unitType, int count, int depth, bool door, int ticks)
    {
        var sim = new Simulation(TestSim.Config(7, 2, count + 16, 8 * count + 64), LocalMovementTests.Flat(64));
        World w = sim.World;
        UnitStore u = w.Units;
        TestSim.KeepIn(sim, 1);
        Assert.True(w.Buildings.Spawn(1, Hall(w.Data), 30 * 64 + 40, out EntityHandle hall)); // footprint cells 40-43, 30-33
        int tree = w.Data.FindResource("tree");
        for (int y = 30 - depth; y <= 33 + depth; y++)
            for (int x = 40 - depth; x <= 43 + depth; x++)
            {
                if (x >= 40 && x <= 43 && y >= 30 && y <= 33) continue;
                if (door && x == 41 && y > 33) continue; // a one-cell lane to the south face
                Assert.True(w.Resources.Spawn(tree, y * 64 + x, w.Data.Rules.TreeWood, out _), $"tree ({x}, {y})");
            }
        // The hall seen from the start (an Attack on an unseen building is dropped: the dev's own BUG-0882 scene never took it).
        Spotter(sim, 0, new Vector2(92.5f, 74.5f));
        var army = new EntityHandle[count];
        for (int k = 0; k < count; k++)
        {
            army[k] = Place(sim, 0, unitType, new Vector2(78f + k % 5 * 1.2f, 84f + k / 5 * 1.2f));
            if (kind == Kind.Ordered) sim.Enqueue(Command.Attack(0, army[k], hall, isBuilding: true));
            else sim.Enqueue(Command.AttackMove(0, army[k], new Vector2(84f, 64f)));
        }
        var struck = new bool[count];
        int hits = 0;
        for (int t = 0; t < ticks; t++)
        {
            if (TickTopped(sim, hall)) hits++;
            for (int k = 0; k < count; k++)
                if (u.State[army[k].Index] == UnitState.Attacking && u.Target[army[k].Index] == hall) struck[k] = true;
        }
        int gave = army.Count(h => u.GiveUps[h.Index] > 0);
        int moving = army.Count(h => u.State[h.Index] == UnitState.Moving);
        int held = army.Count(h => u.Target[h.Index] == hall);
        return (gave, struck.Count(x => x), moving, held, hits);
    }

    [Theory]
    [InlineData(Kind.Ordered, "hi", 20, 2, false)]
    [InlineData(Kind.AttackMove, "hi", 20, 2, false)]
    [InlineData(Kind.Ordered, "hi", 10, 1, false)]
    [InlineData(Kind.Ordered, "xb", 8, 2, false, Skip = "BUG-0889: one ordered Crossbowman queued behind its own Crossbowmen on the hall never strikes (pre-existing)")]
    [InlineData(Kind.AttackMove, "xb", 8, 2, false)]
    [InlineData(Kind.Ordered, "hi", 10, 2, true)]
    [InlineData(Kind.Ordered, "hi", 10, 1, true)]
    public void HallRingedByTrees_MeleeGivesUp_RangedAndDoorKeepOn(Kind kind, string type, int count, int depth, bool door)
    {
        int unitType = type == "hi" ? HeavyInfantry : Crossbowman;
        var r = TreeRing(kind, unitType, count, depth, door, 2000);
        _out.WriteLine($"{kind} {count} {type}, ring {depth} deep, door {door}: gave up {r.GaveUp}, struck {r.Struck}, Moving at the end {r.MovingAtEnd}, holding the hall at the end {r.HeldAtEnd}, ticks hit {r.Hits}");
        if (type == "xb")
        {
            Assert.True(r.Struck == count, $"ranged: struck {r.Struck}/{count}");
            if (kind == Kind.Ordered) Assert.True(r.GaveUp == 0, $"ranged gave a hall in reach up: {r.GaveUp}");
        }
        else if (door)
        {
            Assert.True(r.Struck >= 1, "nobody got in by the door");
            if (kind == Kind.Ordered) Assert.True(r.MovingAtEnd + r.HeldAtEnd >= 1, "the ordered chasers dropped a hall they could reach by the door");
        }
        else
        {
            Assert.True(r.Hits == 0, "setup: a melee unit struck through the trees");
            Assert.True(r.HeldAtEnd == 0 && r.MovingAtEnd == 0, $"held {r.HeldAtEnd}, Moving {r.MovingAtEnd}: kept on a hall no blow can land on");
        }
    }
}
