using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-SH1 round 1 (session 2026-11-05-2148): the mark of a scout sent again (<see cref="ScoutManager.SentAgain"/>, the
/// grid point nearest the hall laid at the hall's centre) and the release of such a scout to gather
/// (<see cref="ScoutManager.FoundEnoughToGather"/>, BUG-1056). Attacks: a hall standing exactly on a scout grid centre
/// (the developer's stated false-negative edge) with every scout killed at the door; where the released scout itself
/// gathers (the door rule measures the forest nearest the hall, the economy sends a worker to the node nearest it);
/// a save taken while a scout sent again is out.
/// </summary>
[Collection(SerialCollection.Name)]
public class ScoutSentAgainQaTests
{
    private readonly ITestOutputHelper _out;

    public ScoutSentAgainQaTests(ITestOutputHelper output) => _out = output;

    private const int Size = 64;

    private static PlayerSeat[] Seats() => new PlayerSeat[] { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };

    /// <summary>
    /// A flat 64 x 64 cell (128 m) map, not explored, combat on: player 0's Town Hall anchored at (<paramref name="hx"/>,
    /// <paramref name="hy"/>), player 1's at (52, 52); each with two mines, 12 trees and five workers beside it (as
    /// <c>AiScenes.Flat</c> lays them). With the shipped <c>ai.scoutGrid</c> 56 m the grid is 3 x 3 with centres at 21.33,
    /// 64 and 106.67 m, so a 4 x 4 hall anchored at (30, 30) has its centre (64, 64) exactly on the middle grid centre.
    /// </summary>
    private static Simulation Scene(int hx, int hy)
    {
        GameData data = TestSim.Data;
        PlayerSeat[] seats = Seats();
        var config = new SimConfig(7, seats.Length, 256, 512) { Data = data, Seats = seats.ToImmutableArray(), Combat = true };
        var sim = new Simulation(config, LocalMovementTests.Flat(Size));
        World w = sim.World;
        int mine = data.FindResource("gold_mine"), tree = data.FindResource("tree");
        (int X, int Y)[] halls = { (hx, hy), (52, 52) };
        for (int p = 0; p < seats.Length; p++)
        {
            (int x, int y) = halls[p];
            int hall = StartBase.BuildingOfSlot(data, seats[p].Faction, BuildingSlot.TownHall);
            Assert.True(w.Buildings.Spawn(p, hall, y * Size + x, out _));
            int toward = p == 0 ? 1 : -1;
            for (int m = 0; m < 2; m++)
                Assert.True(w.Resources.Spawn(mine, (y - 6) * Size + x + toward * (2 + 3 * m), data.Rules.StartMineGold, out _));
            for (int t = 0; t < 12; t++)
            {
                int ty = p == 0 ? y + 9 + t / 4 : y - 9 + t / 4 - 3;
                Assert.True(w.Resources.Spawn(tree, ty * Size + x + t % 4, data.Rules.TreeWood, out _));
            }
            int worker = StartBase.UnitOfSlot(data, seats[p].Faction, UnitSlot.Worker);
            for (int k = 0; k < 5; k++)
                CombatScenes.Place(sim, p, worker, w.NavGrid.CellCenter(x + k % 4, y + 5 + k / 4));
        }
        return sim;
    }

    /// <summary>
    /// BUG-1052's cap with the hall on a grid centre: every scout of player 0 killed on its first tick out (before the head
    /// ring is seen) by tick 6,000. The cap ends the sweep for a scout sent again only (round 1), and SentAgain can't see
    /// the mark when the hall's centre is a grid centre. Control: the hall one cell west (centre 62 m, off the grid).
    /// The brief's rule: at most ai.scoutReplacements + 1 (3) scouts lost. Measured on a0b75b7: control 3 killed (2 marked);
    /// on the grid centre 9 killed, 0 marked, still sending at 6,000 (BUG-1057).
    /// </summary>
    [Fact]
    public void EveryScoutKilledAtTheDoor_TheCapHolds_WhenTheHallStandsOffTheGrid() => KillEveryScout(29, 30);

    /// <summary>See <see cref="EveryScoutKilledAtTheDoor_TheCapHolds_WhenTheHallStandsOffTheGrid"/>: the hall's centre (64, 64) is the scout grid's middle centre.</summary>
    [Fact(Skip = "BUG-1057 (open, S3): SentAgain can't see its mark when the hall's centre is a grid centre, so the cap never applies (9 scouts killed by 6,000)")]
    public void EveryScoutKilledAtTheDoor_TheCapHolds_WhenTheHallStandsOnAGridCentre() => KillEveryScout(30, 30);

    private void KillEveryScout(int hx, int hy)
    {
        Simulation sim = Scene(hx, hy);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        Vector2 hall = ReachRuleQaTests.HallCentre(w, 0);
        int killed = 0, sentAgainSeen = 0;
        while (sim.TickNumber < 6000)
        {
            sim.Tick();
            Blackboard b = ai.Board;
            if (b.ScoutPhase != 1) continue;
            var h = new EntityHandle(b.ScoutSlot, b.ScoutGeneration);
            if (!w.Units.IsAlive(h)) continue;
            if (ScoutManager.SentAgain(ai)) sentAgainSeen++;
            CombatSystem.HitUnit(w, new PendingHit(default, 1, h, false, w.Units.Hp[h.Index]));
            killed++;
        }
        _out.WriteLine($"hall anchor ({hx}, {hy}), centre {hall}: scouts killed {killed} (marked sent again {sentAgainSeen}); own losses {w.UnitLosses[0]}; phase {ai.Board.ScoutPhase}; lifted {ai.ReachLifted()}");
        Assert.True(killed >= 1, "setup: no scout was ever sent");
        Assert.True(killed <= ai.Rules.ScoutReplacements + 1, $"{killed} scouts fed to the door (cap {ai.Rules.ScoutReplacements})");
    }

    /// <summary>Distance (m) from <paramref name="p"/> to the nearest live building of <paramref name="owner"/>.</summary>
    private static float NearestBuilding(World w, int owner, Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i < w.Buildings.Capacity; i++)
        {
            if (!w.Buildings.Alive[i] || w.Buildings.Owner[i] != owner) continue;
            BuildingDef d = w.Data.Buildings[w.Buildings.TypeId[i]];
            int c = w.Buildings.Cell[i];
            var centre = new Vector2((c % w.NavGrid.Width + d.FootprintWidth * 0.5f) * MapConstants.CellSize,
                (c / w.NavGrid.Width + d.FootprintHeight * 0.5f) * MapConstants.CellSize);
            best = Math.Min(best, Vector2.Distance(centre, p));
        }
        return best;
    }

    private static Vector2 NodeCentre(World w, EntityHandle n)
    {
        ResourceDef d = w.Data.Resources[w.Resources.TypeId[n.Index]];
        int c = w.Resources.Cell[n.Index];
        return new Vector2((c % w.NavGrid.Width + d.FootprintWidth * 0.5f) * MapConstants.CellSize,
            (c / w.NavGrid.Width + d.FootprintHeight * 0.5f) * MapConstants.CellSize);
    }

    public static TheoryData<ulong, int> ReleaseRows()
    {
        var d = new TheoryData<ulong, int>();
        foreach (int k in new[] { 0, 200, 2000 })
            for (ulong s = 1; s <= 10; s++) d.Add(s, k);
        return d;
    }

    /// <summary>
    /// BUG-1056's release, where the released scout itself goes: seeds 1-10, trees within 90 m taken, one scout killed 0 /
    /// 200 / 2,000 ticks out. When a scout sent again is released alive, the first node it is set to gather (any kind) must
    /// not stand within ai.workerFleeRadius of an enemy building: the door rule's stated reason ("a scout gathering there
    /// ... would work inside the enemy base"). The door rule reads the forest nearest the hall; the economy sends a worker
    /// to the node nearest the worker. Measured on a0b75b7: 25 of 30 rows release; 8 of them first gather 7.1-15.8 m from an
    /// enemy building (6 at a gold mine, 2 at a forest), and 4 of those 8 scouts die by tick 6,000 (BUG-1058).
    /// </summary>
    [Theory(Skip = "BUG-1058 (open, S3): the released scout gathers at the node nearest itself, often the enemy's own mine or forest, inside ai.workerFleeRadius of an enemy building")]
    [MemberData(nameof(ReleaseRows))]
    public void AReleasedScout_FirstGathersClearOfTheEnemysBuildings(ulong seed, int killAt)
    {
        Simulation sim = AiScenes.Generated(Seats(), seed, record: false, out _);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        while (sim.TickNumber < 5) sim.Tick();
        Vector2 hall = ReachRuleQaTests.HallCentre(w, 0);
        ReachRuleQaTests.Clear(w, hall, 90f);
        int slot = -1, gen = 0, sentAt = -1, releasedAt = -1, gatherAt = -1, diedAt = -1;
        bool killed = false, wasAgain = false;
        float nodeToEnemy = -1f, nodeToHall = -1f;
        string kind = "";
        while (sim.TickNumber < 6000)
        {
            Blackboard b = ai.Board;
            wasAgain = b.ScoutPhase == 1 && ScoutManager.SentAgain(ai);
            int s0 = b.ScoutSlot, g0 = b.ScoutGeneration;
            sim.Tick();
            if (b.ScoutPhase == 1 && (b.ScoutSlot != slot || b.ScoutGeneration != gen))
            {
                (slot, gen, sentAt) = (b.ScoutSlot, b.ScoutGeneration, sim.TickNumber);
            }
            if (!killed && b.ScoutPhase == 1 && sim.TickNumber - sentAt == killAt)
            {
                var h = new EntityHandle(slot, gen);
                if (w.Units.IsAlive(h))
                {
                    CombatSystem.HitUnit(w, new PendingHit(default, 1, h, false, w.Units.Hp[slot]));
                    killed = true;
                }
            }
            if (releasedAt < 0 && wasAgain && b.ScoutPhase == 2 && w.Units.IsAlive(new EntityHandle(s0, g0)))
            {
                releasedAt = sim.TickNumber;
                (slot, gen) = (s0, g0);
            }
            if (releasedAt >= 0 && gatherAt < 0)
            {
                var h = new EntityHandle(slot, gen);
                if (!w.Units.IsAlive(h)) { if (diedAt < 0) diedAt = sim.TickNumber; }
                else
                {
                    EntityHandle n = w.Units.GatherNode[slot];
                    if (n.Generation != 0 && w.Resources.IsAlive(n))
                    {
                        gatherAt = sim.TickNumber;
                        Vector2 c = NodeCentre(w, n);
                        nodeToEnemy = NearestBuilding(w, 1, c);
                        nodeToHall = Vector2.Distance(c, hall);
                        kind = w.Data.Resources[w.Resources.TypeId[n.Index]].Resource.ToString();
                    }
                }
            }
            if (releasedAt >= 0 && gatherAt >= 0 && diedAt < 0 && !w.Units.IsAlive(new EntityHandle(slot, gen))) diedAt = sim.TickNumber;
        }
        _out.WriteLine($"ROW seed {seed} kill {killAt}: released {releasedAt}; first gather {gatherAt} ({kind}) {nodeToHall:F1} m from home, {nodeToEnemy:F1} m from an enemy building; scout died {diedAt}; own losses {w.UnitLosses[0]}; wood {w.GatheredWood[0]}");
        if (releasedAt < 0 || gatherAt < 0) return; // no release: nothing to judge
        Assert.True(nodeToEnemy > ai.Rules.WorkerFleeRadius,
            $"the released scout gathers {kind} {nodeToEnemy:F1} m from an enemy building (door {ai.Rules.WorkerFleeRadius} m)");
    }

    /// <summary>
    /// Mutation guard (QA's mutant "the release without <see cref="ScoutManager.KnowsEveryKind"/>" survived every other row):
    /// the developer's BUG-1056 scene (seed 3, trees within 90 m taken, the first scout killed 200 ticks out), but every
    /// gold node the seat knows is marked dropped each tick while the scout sent again is out, so the seat never knows a
    /// node of every kind. The scout must keep sweeping: if it leaves the sweep alive, every waypoint was visited.
    /// Unguarded it is released at the 186 m forest with a seat that knows no gold.
    /// </summary>
    [Fact]
    public void AScoutSentAgain_KnowingNoGold_IsNotReleased()
    {
        Simulation sim = AiScenes.Generated(Seats(), 3, record: false, out _);
        while (sim.TickNumber < 5) sim.Tick();
        ReachRuleQaTests.Clear(sim.World, ReachRuleQaTests.HallCentre(sim.World, 0), 90f);
        AiPlayer ai = sim.AiOf(0)!;
        while (ai.Board.ScoutPhase != 1 && sim.TickNumber < 3000) sim.Tick();
        for (int t = 0; t < 200; t++) sim.Tick();
        var first = new EntityHandle(ai.Board.ScoutSlot, ai.Board.ScoutGeneration);
        Assert.True(sim.World.Units.IsAlive(first) && ai.Board.ScoutPhase == 1, "setup: the first scout is not out");
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, first, false, sim.World.Units.Hp[first.Index]));
        for (int t = 0; t < 400 && ai.Board.ScoutPhase == 1; t++) sim.Tick();
        while (ai.Board.ScoutPhase != 1 && sim.TickNumber < 4000) sim.Tick();
        var scout = new EntityHandle(ai.Board.ScoutSlot, ai.Board.ScoutGeneration);
        Assert.True(ai.Board.ScoutPhase == 1 && ScoutManager.SentAgain(ai), "setup: no scout sent again is out");
        int marked = 0;
        bool sawEnemyAndForest = false;
        while (ai.Board.ScoutPhase == 1 && sim.TickNumber < 6000)
        {
            for (int s = -1; ai.View.NextResource(ref s, out SeenResource node);)
            {
                if (sim.World.Data.Resources[node.TypeId].Resource == ResourceKind.Wood || ai.Board.NodeDropped[s] == node.Handle.Generation) continue;
                if (ai.Board.NodeDropped[s] == 0) ai.Board.NodeMarks++;
                ai.Board.NodeDropped[s] = node.Handle.Generation;
                marked++;
            }
            sawEnemyAndForest |= ai.Board.KnownEnemyBuildings > 0 && ai.ReachLifted();
            sim.Tick();
        }
        bool alive = sim.World.Units.IsAlive(scout), allVisited = Array.TrueForAll(ai.Board.ScoutVisited, v => v);
        _out.WriteLine($"gold nodes marked dropped {marked}; enemy building known with the reach lifted {sawEnemyAndForest}; phase {ai.Board.ScoutPhase} at {sim.TickNumber}; scout alive {alive}; every point visited {allVisited}");
        Assert.True(sawEnemyAndForest, "setup: the seat never knew an enemy building with the reach lifted");
        Assert.False(ai.Board.ScoutPhase == 2 && alive && !allVisited, $"released at tick {sim.TickNumber} knowing no gold");
    }

    /// <summary>
    /// A save taken while a scout sent again is out (seed 3, trees within 90 m taken, the first scout killed 200 ticks out:
    /// the developer's BUG-1056 row; and seed 2 killed 30 ticks out): the loaded seat reads <see cref="ScoutManager.SentAgain"/>
    /// the same, equals the saved one, plays on hash-equal every tick to 6,000, and the scout is released on the same tick.
    /// </summary>
    [Theory]
    [InlineData(3UL, 200)]
    [InlineData(2UL, 30)]
    public void ASaveWhileAScoutSentAgainIsOut_LoadsMarked_AndPlaysOnHashEqual(ulong seed, int killAt)
    {
        Simulation a = AiScenes.Generated(Seats(), seed, record: false, out _);
        while (a.TickNumber < 5) a.Tick();
        ReachRuleQaTests.Clear(a.World, ReachRuleQaTests.HallCentre(a.World, 0), 90f);
        AiPlayer ai = a.AiOf(0)!;
        while (ai.Board.ScoutPhase != 1 && a.TickNumber < 3000) a.Tick();
        for (int t = 0; t < killAt; t++) a.Tick();
        var first = new EntityHandle(ai.Board.ScoutSlot, ai.Board.ScoutGeneration);
        Assert.True(a.World.Units.IsAlive(first) && ai.Board.ScoutPhase == 1, "setup: the first scout is not out");
        CombatSystem.HitUnit(a.World, new PendingHit(default, 1, first, false, a.World.Units.Hp[first.Index]));
        for (int t = 0; t < 400 && ai.Board.ScoutPhase == 1; t++) a.Tick();
        while (ai.Board.ScoutPhase != 1 && a.TickNumber < 4000) a.Tick();
        for (int t = 0; t < 50; t++) a.Tick();
        Assert.True(ai.Board.ScoutPhase == 1 && ScoutManager.SentAgain(ai), "setup: no scout sent again is out");
        int savedAt = a.TickNumber;
        Simulation loaded = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(a));
        AiPlayer al = loaded.AiOf(0)!;
        SaveTestRun.AssertSameAi(ai, al, $"on load at {savedAt}");
        Assert.True(ScoutManager.SentAgain(al), "the loaded scout is not marked sent again");
        int relA = -1, relL = -1;
        while (a.TickNumber < 6000)
        {
            a.Tick();
            loaded.Tick();
            Assert.True(a.StateHash() == loaded.StateHash(), $"diverged at tick {a.TickNumber} (saved at {savedAt})");
            if (relA < 0 && ai.Board.ScoutPhase == 2) relA = a.TickNumber;
            if (relL < 0 && al.Board.ScoutPhase == 2) relL = loaded.TickNumber;
        }
        _out.WriteLine($"seed {seed}: saved at {savedAt}; sweep ended {relA} / {relL}; wood {a.World.GatheredWood[0]} / {loaded.World.GatheredWood[0]}");
        Assert.Equal(relA, relL);
        SaveTestRun.AssertSameAi(ai, al, "at 6,000");
    }
}
