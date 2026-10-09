using System.Numerics;
using System.Reflection;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-H1 (session 2026-10-08-2144): hostile variants of the BUG-0149 fix (a switch keeps the stall count only back to
/// the target held just before, or to the given-up one) and of the BUG-0133 fix (leftover offsets past pass 24).
/// </summary>
public class ChasePrevQaTests
{
    private readonly ITestOutputHelper _out;

    public ChasePrevQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>40 x 40, a sealed level-1 plateau at x 20-35, y 5-34 (the give-up rows' map); optional wall at x 15, y 16-24.</summary>
    private static Simulation PlateauSim(bool wall, int units = 12)
    {
        var rows = new string[40];
        for (int y = 0; y < 40; y++)
        {
            var c = new char[40];
            for (int x = 0; x < 40; x++) c[x] = x >= 20 && x <= 35 && y >= 5 && y <= 34 ? '1' : '0';
            if (wall && y >= 16 && y <= 24) c[15] = '1';
            rows[y] = new string(c);
        }
        return new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: units, CommandCapacity: 64), LocalMovementTests.Rows(rows));
    }

    private static EntityHandle Holder(Simulation sim, int type, int x, int y, bool spot)
    {
        EntityHandle h = Place(sim, 1, type, At(sim, x, y));
        sim.Enqueue(Command.HoldPosition(1, h));
        if (spot) Spot(sim, 0, h);
        return h;
    }

    /// <summary>
    /// The brief's hostile variant: two unreachable cliff-top targets in a row (A stalls, then a nearer B is taken and
    /// stalls too), then a reachable Raider behind a short wall comes into sight. It is a third, new target: a fresh chase,
    /// so the chaser walks round the wall and fights it.
    /// </summary>
    [Fact]
    public void TwoUnreachableInARow_ThenAReachableOneBehindAWall_IsFought()
    {
        Simulation sim = PlateauSim(wall: true);
        UnitStore u = sim.World.Units;
        EntityHandle c = Place(sim, 0, HeavyInfantry, At(sim, 18, 20));
        EntityHandle a = Holder(sim, Laborer, 22, 20, spot: true);
        RunUntil(sim, () => u.Target[c.Index] == a && u.ChaseStall[c.Index] >= 6, 400);
        Assert.True(u.Target[c.Index] == a && u.ChaseStall[c.Index] >= 6, $"setup A: target {u.Target[c.Index]}, stall {u.ChaseStall[c.Index]}");
        EntityHandle b = Holder(sim, Laborer, 21, 20, spot: true); // nearer, same tier
        RunUntil(sim, () => u.Target[c.Index] == b, 40);
        Assert.Equal(b, u.Target[c.Index]);
        Assert.Equal(a, u.ChasePrev[c.Index]);
        RunUntil(sim, () => u.Target[c.Index] != b || u.ChaseStall[c.Index] >= 6, 400);
        Assert.True(u.Target[c.Index] == b && u.ChaseStall[c.Index] >= 6, $"setup B: target {u.Target[c.Index]}, stall {u.ChaseStall[c.Index]}, give-ups {u.GiveUps[c.Index]}");
        EntityHandle r = Holder(sim, Raider, 13, 20, spot: false);
        bool fought = false;
        int end = RunUntil(sim, () =>
        {
            fought |= u.Target[c.Index] == r && u.State[c.Index] == UnitState.Attacking;
            return fought || (u.Ignored[c.Index] == r && u.Mode[c.Index] == CombatMode.None);
        }, 1200);
        _out.WriteLine($"fought {fought} after {end} ticks; give-ups {u.GiveUps[c.Index]}, ignored {u.Ignored[c.Index]} (raider {r})");
        Assert.True(fought, $"gave up a reachable third target: give-ups {u.GiveUps[c.Index]}, {u.State[c.Index]}");
    }

    /// <summary>
    /// BUG-0143's livelock, two and three targets: unreachable cliff-top Laborers, spotted, which the chaser's scan takes in
    /// turn (each 1, 2 or 3 scan intervals a different one targets the chaser, so it is the tier-0 pick: the seam the dev's
    /// <c>SwitchingBackToTheTargetHeldJustBefore_KeepsTheStallCount</c> uses). docs/03 says two targets taken in turn can't
    /// restart the count forever; the chase must end within a generous bound (3 x 3 x GiveUpScans scans + 400 ticks).
    /// It never does, on this branch and on its base 4c1f168: every switch re-takes <c>ChaseBest</c> from the new gap, and
    /// the chaser's walk along the cliff toward the new target counts as progress, so the stall count never passes 3. BUG-0241.
    /// </summary>
    [Theory(Skip = "BUG-0241: targets taken in turn reset the chase's progress mark on every switch; the chase never ends")]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    public void UnreachableTargetsTakenInTurn_TheChaseStillEnds(int targets, int scansPerTurn) => TakenInTurn(targets, scansPerTurn, expectEnd: true);

    /// <summary>Pin for BUG-0241 (today's behaviour, also on 4c1f168): the chase is still on after the bound. Flip to the skipped theory above when fixed.</summary>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 1)]
    [InlineData(3, 3)]
    public void Bug0241Pin_UnreachableTargetsTakenInTurn_NeverEnd(int targets, int scansPerTurn) => TakenInTurn(targets, scansPerTurn, expectEnd: false);

    private void TakenInTurn(int targets, int scansPerTurn, bool expectEnd)
    {
        Simulation sim = PlateauSim(wall: false);
        UnitStore u = sim.World.Units;
        EntityHandle c = Place(sim, 0, HeavyInfantry, At(sim, 18, 20));
        var all = new[] { Holder(sim, Laborer, 22, 17, true), Holder(sim, Laborer, 22, 20, true), Holder(sim, Laborer, 22, 23, true) };
        EntityHandle[] t3 = all[..targets];
        int limit = 3 * CombatConstants.GiveUpScans * CombatConstants.ScanInterval * 3 + 400;
        int switches = 0, maxStall = 0, endedAt = -1;
        EntityHandle last = default;
        for (int t = 0; t < limit; t++)
        {
            int turn = sim.TickNumber / (CombatConstants.ScanInterval * scansPerTurn) % targets;
            for (int k = 0; k < targets; k++) u.Target[t3[k].Index] = k == turn ? c : default;
            sim.Tick();
            if (u.Target[c.Index] != last && u.Target[c.Index] != default) switches++;
            last = u.Target[c.Index];
            maxStall = Math.Max(maxStall, u.ChaseStall[c.Index]);
            if (t > 40 && u.Target[c.Index] == default && u.Mode[c.Index] == CombatMode.None) { endedAt = t; break; }
        }
        _out.WriteLine($"{targets} targets, rotation every {scansPerTurn} scan(s): ended at {endedAt}, switches {switches}, max stall {maxStall}, give-ups {u.GiveUps[c.Index]}, pos {u.Position[c.Index]}");
        if (!expectEnd)
        {
            Assert.True(endedAt < 0, "BUG-0241 looks fixed: flip this pin to UnreachableTargetsTakenInTurn_TheChaseStillEnds");
            return;
        }
        Assert.True(endedAt >= 0, $"{targets} targets, rotation every {scansPerTurn} scan(s): still chasing after {limit} ticks ({switches} switches, stall never above {maxStall}, give-ups {u.GiveUps[c.Index]})");
    }

    /// <summary>BUG-0133: the leftover offsets of passes 1-20,000 are pairwise distinct, never the centre, and inside the cell (|x|, |y| below half a cell).</summary>
    [Fact]
    public void LeftoverOffsets_FirstTwentyThousandPasses_AreDistinct_AndInsideTheCell()
    {
        MethodInfo m = typeof(ConstructionSystem).GetMethod("LeftoverOffset", BindingFlags.NonPublic | BindingFlags.Static)!;
        var seen = new HashSet<Vector2>();
        float half = MapConstants.CellSize / 2f;
        for (int pass = 1; pass <= 20_000; pass++)
        {
            var o = (Vector2)m.Invoke(null, new object[] { pass })!;
            Assert.True(o != Vector2.Zero, $"pass {pass}: the centre");
            Assert.True(MathF.Abs(o.X) < half && MathF.Abs(o.Y) < half, $"pass {pass}: {o} outside the cell");
            Assert.True(seen.Add(o), $"pass {pass}: {o} repeats an earlier pass");
        }
    }
}
