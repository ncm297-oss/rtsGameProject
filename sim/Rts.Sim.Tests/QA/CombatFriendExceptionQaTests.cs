using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-1 re-check (fix round 2): adversarial rows for the BUG-0143 friend exception (<c>FriendFightsTarget</c>: a
/// stalled chase keeps its target while another unit of its owner stands Attacking within its reach of the target) and
/// the stall count kept across a target switch. Every row states its bound.
/// </summary>
public class CombatFriendExceptionQaTests
{
    private readonly ITestOutputHelper _out;

    public CombatFriendExceptionQaTests(ITestOutputHelper output) => _out = output;

    private static Simulation OnRows(int units, params string[] rows) =>
        new(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: units, CommandCapacity: 8 * units + 32), LocalMovementTests.Rows(rows));

    /// <summary>40 x 40 ground with a level-1 plateau at x 20-35, y 5-34 and no ramp: unreachable from the ground.</summary>
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

    /// <summary>
    /// The documented known limit, bounded: a chaser below a sealed plateau whose friend up there fights its target keeps
    /// pressing for that fight only. Once the fight ends (the target dies, or the friend dies and the chase stalls with no
    /// friend), the chaser is Idle with no combat mode within 200 ticks, and then stays put for 2,000 ticks.
    /// <paramref name="friendWins"/> picks which side of the plateau fight dies (by hit points).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChaserBelowACliff_FriendFightsItsTargetOnThePlateau_SettlesWhenThatFightEnds(bool friendWins)
    {
        Simulation sim = OnRows(8, PlateauMap());
        Vector2 home = At(sim, 17, 20);
        EntityHandle c = Place(sim, 0, HeavyInfantry, home);
        EntityHandle e = Place(sim, 1, Raider, At(sim, 22, 20));
        EntityHandle f = Place(sim, 0, HeavyInfantry, At(sim, 22, 20, dx: 0.9f));
        sim.Enqueue(Command.HoldPosition(1, e));
        sim.Enqueue(Command.HoldPosition(0, f)); // stays on the plateau, fights what is in reach
        UnitStore u = sim.World.Units;
        if (friendWins) u.Hp[e.Index] = 400; else u.Hp[f.Index] = 400;
        u.Hp[friendWins ? f.Index : e.Index] = 4000;
        int chased = 0, cMoving = 0, end = -1;
        for (int t = 0; t < 6000 && end < 0; t++)
        {
            sim.Tick();
            if (u.Target[c.Index] == e) chased++;
            if (u.State[c.Index] == UnitState.Moving) cMoving++;
            if (!u.IsAlive(e) || !u.IsAlive(f)) end = t;
        }
        Assert.True(end >= 0, "setup: the plateau fight did not end in 6,000 ticks");
        _out.WriteLine($"fight ended on tick {end} ({(u.IsAlive(f) ? "friend won" : "target won")}); chaser held the target {chased} ticks, Moving {cMoving}");
        Assert.True(chased > 200, $"setup: the chaser held the target for {chased} ticks only (the friend exception never applied)");
        int settled = RunUntil(sim, () => u.Target[c.Index] == default && u.Mode[c.Index] == CombatMode.None && u.State[c.Index] == UnitState.Idle, 400);
        _out.WriteLine($"settled {settled} ticks after the fight, {Vector2.Distance(u.Position[c.Index], home):F1} m from home");
        Assert.True(settled <= 200, $"not settled 200 ticks after the fight: {u.State[c.Index]} mode {u.Mode[c.Index]} target {u.Target[c.Index]}");
        int moving = 0, engaged = 0;
        for (int t = 0; t < 2000; t++)
        {
            sim.Tick();
            if (u.State[c.Index] == UnitState.Moving) moving++;
            if (u.Target[c.Index] != default) engaged++;
        }
        Assert.True(moving == 0 && engaged <= 40 * CombatConstants.MaxGiveUps, $"after settling: Moving {moving} ticks, with a target {engaged} ticks");
    }

    /// <summary>
    /// The kept stall count, open ground: a chaser stalled on a cliff-top Laborer for about 8 scans switches to a reachable
    /// Raider (higher priority) holding about 10 m away on open ground, straight line clear. It must reach and fight it, not
    /// give it up on the switch's first scan.
    /// </summary>
    [Fact]
    public void StalledChaser_SwitchesToAReachableEnemy_ReachesAndFightsIt()
    {
        Simulation sim = OnRows(8, PlateauMap());
        EntityHandle c = Place(sim, 0, HeavyInfantry, At(sim, 18, 20));
        EntityHandle low = Place(sim, 1, Laborer, At(sim, 21, 20));
        sim.Enqueue(Command.HoldPosition(1, low));
        Spot(sim, 0, low); // M4-3a: up the cliff, a level-0 unit can't see it: spotted
        UnitStore u = sim.World.Units;
        RunUntil(sim, () => u.Target[c.Index] == low && u.ChaseStall[c.Index] >= 8, 400);
        Assert.True(u.Target[c.Index] == low && u.ChaseStall[c.Index] >= 8, $"setup: stall {u.ChaseStall[c.Index]}");
        EntityHandle r = Place(sim, 1, Raider, At(sim, 13, 20)); // 10-11 m off: in sight (cells are 2 m)
        sim.Enqueue(Command.HoldPosition(1, r));
        int t = RunUntil(sim, () => u.Target[c.Index] == r, 20);
        _out.WriteLine($"after {t} ticks: target {u.Target[c.Index]} (raider {r}, laborer {low}), stall {u.ChaseStall[c.Index]}, state {u.State[c.Index]}, ignored {u.Ignored[c.Index]}, give-ups {u.GiveUps[c.Index]}, pos {u.Position[c.Index]}");
        Assert.Equal(r, u.Target[c.Index]);
        bool fought = false;
        int end = RunUntil(sim, () =>
        {
            fought |= u.Target[c.Index] == r && u.State[c.Index] == UnitState.Attacking;
            return fought || u.Ignored[c.Index] == r;
        }, 400);
        _out.WriteLine($"switched after {t} ticks; fought {fought} after {end} ticks; gave the raider up {u.Ignored[c.Index] == r}");
        Assert.True(fought, "the chaser gave up a reachable enemy it had just switched to");
    }

    /// <summary>
    /// BUG-0149: same, but the reachable Raider holds behind a short wall (the way round is about 10 m longer than the
    /// straight line, so the gap does not shrink at first). A fresh chase of it gets <see cref="CombatConstants.GiveUpScans"/>
    /// scans; with the stall count carried over from the cliff chase it gives the Raider up on the switch's next scan,
    /// twice, reaches <see cref="CombatConstants.MaxGiveUps"/> and stands Idle 10 m from a reachable enemy it can see.
    /// With the count reset on a switch (the round-1 rule) it walks round and fights it by tick 168.
    /// </summary>
    [Fact(Skip = "BUG-0149: a stall count carried across a target switch makes the chaser give up a reachable enemy behind a short detour")]
    public void StalledChaser_SwitchesToAnEnemyBehindAWall_WalksRoundAndFightsIt()
    {
        string[] rows = PlateauMap();
        // A wall at x = 15, y 16-24 between the chaser's side and the raider.
        for (int y = 16; y <= 24; y++)
        {
            char[] r0 = rows[y].ToCharArray();
            r0[15] = '1';
            rows[y] = new string(r0);
        }
        Simulation sim;
        try { sim = OnRows(8, rows); }
        catch (Exception ex) { _out.WriteLine($"map: {ex.Message}"); throw; }
        EntityHandle c = Place(sim, 0, HeavyInfantry, At(sim, 18, 20));
        EntityHandle low = Place(sim, 1, Laborer, At(sim, 21, 20));
        sim.Enqueue(Command.HoldPosition(1, low));
        Spot(sim, 0, low); // M4-3a: up the cliff, a level-0 unit can't see it: spotted, so the stalled cliff chase happens
        UnitStore u = sim.World.Units;
        RunUntil(sim, () => u.Target[c.Index] == low && u.ChaseStall[c.Index] >= 8, 400);
        Assert.True(u.Target[c.Index] == low && u.ChaseStall[c.Index] >= 8, $"setup: stall {u.ChaseStall[c.Index]}");
        EntityHandle r = Place(sim, 1, Raider, At(sim, 13, 20));
        sim.Enqueue(Command.HoldPosition(1, r));
        bool fought = false;
        int end = RunUntil(sim, () =>
        {
            fought |= u.Target[c.Index] == r && u.State[c.Index] == UnitState.Attacking;
            return fought || (u.Ignored[c.Index] == r && u.Mode[c.Index] == CombatMode.None && u.State[c.Index] == UnitState.Idle);
        }, 1200);
        _out.WriteLine($"behind a wall: fought {fought}; gave it up {u.Ignored[c.Index] == r} after {end} ticks; give-ups {u.GiveUps[c.Index]}");
        Assert.True(fought, $"gave up a reachable enemy behind a short wall: give-ups {u.GiveUps[c.Index]}, {u.State[c.Index]}");
    }
    /// <summary>
    /// The combat-on counterpart the fix round left out: <c>Crowd_ToFourPoints_BothPlayersAtEveryPoint_Report</c>'s 2,500-unit
    /// scene (walking limit 6,000) on combat comes to rest (nobody Moving for 200 ticks in a row) within 2x the walking limit.
    /// </summary>
    [Theory]
    [Trait("Category", "Soak")]
    [InlineData(1UL)]
    public void Crowd2500ToFourPoints_BothPlayersAtEveryPoint_OnCombat_ComesToRest(ulong seed)
    {
        const int walkingLimit = 6000, still = 200;
        Simulation sim = MoveScenario.Spawn(seed, 2500, 70f, out int goalCell, players: 2);
        World w = sim.World;
        Vector2[] four = CrowdRows.FourPoints(MoveScenario.Center(w.NavGrid, goalCell));
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), four[CrowdRows.PointOf(u, i, false)]));
        int run = 0, rest = -1;
        for (int t = 1; t <= 2 * walkingLimit + still && rest < 0; t++)
        {
            sim.Tick();
            bool moving = false;
            for (int i = 0; i < u.Capacity && !moving; i++) moving = u.Alive[i] && u.State[i] == UnitState.Moving;
            run = moving ? 0 : run + 1;
            if (run == still) rest = t - still + 1;
        }
        _out.WriteLine($"seed {seed}: at rest from tick {rest} ({(double)rest / walkingLimit:F2}x the walking limit), kills {w.Kills[0]} / {w.Kills[1]}");
        Assert.True(rest >= 0 && rest <= 2 * walkingLimit, $"seed {seed}: not at rest within {2 * walkingLimit} ticks");
    }
}
