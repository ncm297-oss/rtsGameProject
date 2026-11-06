using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.QA;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-SH1 (BUG-1052 / 1053, docs/03 "Implementation (M6-S7)", the M6-SH1 note): a lost scout is sent again (replaced
/// before the head ring is seen; after it, only while the seat knows no node of some resource kind) only while the own
/// unit losses are at most <c>ai.scoutReplacements</c>; past that the sweep ends and the reach lifts. The count is the
/// saved match stats' own unit losses, so the blackboard keeps no new state. Round 1 (BUG-1055 / 1056): the first scout's
/// death is always followed (unless the knob is 0), and a scout sent again, marked in its saved waypoints, is released to
/// gather once it knows a forest clear of the enemy's buildings.
/// </summary>
[Collection(SerialCollection.Name)]
public class ScoutReplacementTests
{
    private readonly ITestOutputHelper _out;

    public ScoutReplacementTests(ITestOutputHelper output) => _out = output;

    private static readonly PlayerSeat[] s_seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };

    private static GameData Replacements(int n) => n == 2 ? TestSim.Data
        : AiScenes.DataWith(dir => dir.EditJson("common/rules.json", r => r["ai"]!["scoutReplacements"] = n));

    /// <summary>Player 0's scout (slot, generation) while one is out, else (-1, 0).</summary>
    private static (int Slot, int Gen) Scout(Simulation sim)
    {
        Blackboard b = sim.AiOf(0)!.Board;
        return b.ScoutPhase == 1 ? (b.ScoutSlot, b.ScoutGeneration) : (-1, 0);
    }

    /// <summary>Kills player 0's scout if one is out and alive; true when it did.</summary>
    private static bool KillScout(Simulation sim)
    {
        (int slot, int gen) = Scout(sim);
        if (slot < 0) return false;
        var h = new EntityHandle(slot, gen);
        if (!sim.World.Units.IsAlive(h)) return false;
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, h, false, sim.World.Units.Hp[slot]));
        return true;
    }

    /// <summary>Kills one live unit of <paramref name="player"/> that is not player 0's scout (a worker by preference); false when none.</summary>
    private static bool KillOther(Simulation sim, int player)
    {
        World w = sim.World;
        (int scout, _) = Scout(sim);
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            if (!w.Units.Alive[i] || w.Units.Owner[i] != player || player == 0 && i == scout) continue;
            CombatSystem.HitUnit(w, new PendingHit(default, 1 - player, new EntityHandle(i, w.Units.Generation[i]), false, w.Units.Hp[i]));
            return true;
        }
        return false;
    }

    /// <summary>Seed <paramref name="seed"/>, player 0's trees within 90 m of its hall taken (so the first scout's sweep can't end on a forest near home).</summary>
    private static Simulation Far(ulong seed, GameData? data = null)
    {
        Simulation sim = AiScenes.Generated(s_seats, seed, record: false, out _, data: data);
        while (sim.TickNumber < 5) sim.Tick();
        ReachRuleQaTests.Clear(sim.World, ReachRuleQaTests.HallCentre(sim.World, 0), 90f);
        return sim;
    }

    /// <summary>Ticks until player 0's scout is out (phase 1), at most to <paramref name="until"/>.</summary>
    private static void UntilScoutOut(Simulation sim, int until)
    {
        while (sim.AiOf(0)!.Board.ScoutPhase != 1 && sim.TickNumber < until) sim.Tick();
        Assert.Equal(1, sim.AiOf(0)!.Board.ScoutPhase);
    }

    /// <summary>Ticks until the AI has judged the scout's death (phase no longer 1), at most 400 ticks.</summary>
    private static byte AfterTheDeath(Simulation sim)
    {
        for (int t = 0; t < 400 && sim.AiOf(0)!.Board.ScoutPhase == 1; t++) sim.Tick();
        return sim.AiOf(0)!.Board.ScoutPhase;
    }

    /// <summary>
    /// <c>ai.scoutReplacements</c> 0, 1, 2, 4: every scout killed its first tick out, the seat loses exactly n + 1 scouts by
    /// tick 6,000 (the cap is the losses, every one of them a scout here), then the sweep ends with the reach lifted.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void EveryScoutKilled_TheSeatLosesReplacementsPlusOneScouts_ThenTheReachLifts(int replacements)
    {
        Simulation sim = AiScenes.Generated(s_seats, 1, record: false, out _, data: Replacements(replacements));
        AiPlayer ai = sim.AiOf(0)!;
        int killed = 0;
        while (sim.TickNumber < 6000)
        {
            sim.Tick();
            if (KillScout(sim)) killed++;
        }
        _out.WriteLine($"scoutReplacements {replacements}: killed {killed}; own unit losses {sim.World.UnitLosses[0]}; phase {ai.Board.ScoutPhase}; lifted {ai.ReachLifted()}");
        Assert.Equal(replacements + 1, killed);
        Assert.Equal(2, ai.Board.ScoutPhase);
        Assert.True(ai.ReachLifted());
    }

    /// <summary>
    /// Mutation guard (the losses read from the wrong seat): the enemy losing units before a scout sent again dies doesn't
    /// engage the cap, the own seat losing them does. Same start (seed 2, far forest): the first scout killed 30 ticks out,
    /// before the ring is seen, is replaced (M6-SH1 round 1, BUG-1055: the first death always is); then with 3 enemy units
    /// killed the replacement's death is followed again (phase 0); with 2 own workers killed (own losses 4 with both scouts,
    /// past the cap of 2) it is not (phase 2).
    /// </summary>
    [Theory]
    [InlineData(1, (byte)0)]
    [InlineData(0, (byte)2)]
    public void TheCap_CountsTheOwnSeatsLossesOnly(int lossesOf, byte phase)
    {
        Simulation sim = Far(2);
        UntilScoutOut(sim, 3000);
        for (int t = 0; t < 30; t++) sim.Tick();
        Assert.True(KillScout(sim));
        Assert.Equal(0, AfterTheDeath(sim));
        UntilScoutOut(sim, 4000);
        for (int t = 0; t < 30; t++) sim.Tick();
        Assert.True(ScoutManager.SentAgain(sim.AiOf(0)!), "setup: the replacement isn't marked sent again");
        for (int k = 0; k < (lossesOf == 0 ? 2 : 3); k++) Assert.True(KillOther(sim, lossesOf));
        Assert.True(KillScout(sim));
        Assert.Equal(phase, AfterTheDeath(sim));
        _out.WriteLine($"losses of player {lossesOf}: own {sim.World.UnitLosses[0]}, enemy {sim.World.UnitLosses[1]}; phase {phase}");
    }

    /// <summary>
    /// BUG-1055 (M6-SH1 round 1): a seat that lost 3 workers before its first scout went out (own losses past the cap of 2)
    /// still follows that scout's death with another (phase 0, then a new unit out), on seeds 1-3 of the far-forest start;
    /// with ai.scoutReplacements 0 it doesn't (phase 2). Before the fix the cap read the 3 earlier losses and ended the sweep.
    /// </summary>
    [Theory]
    [InlineData(1UL, 2, (byte)0)]
    [InlineData(2UL, 2, (byte)0)]
    [InlineData(3UL, 2, (byte)0)]
    [InlineData(1UL, 0, (byte)2)]
    public void ASeatPastTheCapBeforeItsFirstScout_StillFollowsThatScoutsDeath(ulong seed, int replacements, byte phase)
    {
        Simulation sim = Far(seed, Replacements(replacements));
        for (int k = 0; k < 3; k++) Assert.True(KillOther(sim, 0));
        UntilScoutOut(sim, 3000);
        Assert.False(ScoutManager.SentAgain(sim.AiOf(0)!), "the first scout is marked sent again");
        for (int t = 0; t < 30; t++) sim.Tick();
        Assert.True(KillScout(sim));
        Assert.Equal(phase, AfterTheDeath(sim));
        _out.WriteLine($"seed {seed}, scoutReplacements {replacements}: own losses {sim.World.UnitLosses[0]}; phase {phase}");
        if (phase == 2) return;
        UntilScoutOut(sim, 4000);
        Assert.True(ScoutManager.SentAgain(sim.AiOf(0)!), "the second scout isn't marked sent again");
    }

    /// <summary>
    /// The mark of a scout sent again (<see cref="ScoutManager.SentAgain"/>): the grid point nearest the hall is laid at the
    /// hall's centre and visited; every other point is laid as for the first scout, so its walk is the same. Seed 2's far
    /// start, the first scout killed 30 ticks out.
    /// </summary>
    [Fact]
    public void AScoutSentAgain_IsMarkedByItsHomeGridPoint_AndItsOtherPointsAreTheFirstScouts()
    {
        Simulation sim = Far(2);
        AiPlayer ai = sim.AiOf(0)!;
        UntilScoutOut(sim, 3000);
        Vector2[] firstPoints = (Vector2[])ai.Board.ScoutPoints.Clone();
        Assert.False(ScoutManager.SentAgain(ai));
        for (int t = 0; t < 30; t++) sim.Tick();
        Assert.True(KillScout(sim));
        AfterTheDeath(sim);
        UntilScoutOut(sim, 4000);
        Assert.True(ScoutManager.SentAgain(ai));
        Vector2 hall = ReachRuleQaTests.HallCentre(sim.World, 0);
        int moved = 0;
        for (int k = 0; k < firstPoints.Length; k++)
        {
            if (ai.Board.ScoutPoints[k] == firstPoints[k]) continue;
            moved++;
            Assert.True(k >= Blackboard.ScoutHead, $"head point {k} moved");
            Assert.Equal(hall, ai.Board.ScoutPoints[k]);
            Assert.True(ai.Board.ScoutVisited[k]);
        }
        Assert.Equal(1, moved);
    }

    /// <summary>
    /// BUG-1056 (M6-SH1 round 1): a scout sent again ends its sweep once the reach has lifted and the seat knows a node of
    /// every kind and an enemy building, when its nearest known forest is clear of the enemy's buildings: seed 3, its trees
    /// within 90 m taken, the scout killed 200 ticks out; the replacement is released alive (phase 2) on the think it learns
    /// of the 186 m forest, and the seat's first wood comes in by tick 6,000 (before: the sweep went on, no wood by 6,000).
    /// </summary>
    [Fact]
    public void AScoutSentAgain_KnowingAFarForestClearOfTheEnemy_IsReleasedToGather()
    {
        Simulation sim = Far(3);
        AiPlayer ai = sim.AiOf(0)!;
        UntilScoutOut(sim, 3000);
        for (int t = 0; t < 200; t++) sim.Tick();
        Assert.True(KillScout(sim));
        AfterTheDeath(sim);
        UntilScoutOut(sim, 4000);
        (int Slot, int Gen) scout = (ai.Board.ScoutSlot, ai.Board.ScoutGeneration);
        while (ai.Board.ScoutPhase == 1 && sim.TickNumber < 6000) sim.Tick();
        int releasedAt = sim.TickNumber;
        Assert.Equal(2, ai.Board.ScoutPhase);
        Assert.True(sim.World.Units.IsAlive(new EntityHandle(scout.Slot, scout.Gen)), "the scout died");
        Assert.True(ScoutManager.KnowsEveryKind(ai) && ai.Board.KnownEnemyBuildings > 0);
        Assert.False(ScoutManager.FoundEnough(ai), "setup: a forest near home was found");
        while (sim.World.GatheredWood[0] == 0 && sim.TickNumber < 6000) sim.Tick();
        _out.WriteLine($"released at {releasedAt}; first wood at {sim.TickNumber}");
        Assert.True(sim.World.GatheredWood[0] > 0, "no wood by tick 6,000");
    }

    /// <summary>
    /// The other side of the release (BUG-1056): a scout sent again whose nearest known forest stands at an enemy's door
    /// (within ai.workerFleeRadius of an enemy building: seed 5's far start, the forest 10.8 m from one) keeps sweeping
    /// although the seat knows a node of every kind and an enemy building. Released, it and the gatherers sent after it
    /// worked inside the enemy base (measured: 2 more workers lost by tick 6,000).
    /// </summary>
    [Fact]
    public void AScoutSentAgain_KnowingOnlyAForestAtAnEnemysDoor_KeepsSweeping()
    {
        Simulation sim = Far(5);
        AiPlayer ai = sim.AiOf(0)!;
        UntilScoutOut(sim, 3000);
        Assert.True(KillScout(sim));
        AfterTheDeath(sim);
        UntilScoutOut(sim, 4000);
        while (!(ai.ReachLifted() && ScoutManager.KnowsEveryKind(ai) && ai.Board.KnownEnemyBuildings > 0) && ai.Board.ScoutPhase == 1 && sim.TickNumber < 6000) sim.Tick();
        Assert.True(ScoutManager.KnowsEveryKind(ai) && ai.Board.KnownEnemyBuildings > 0, "setup: the seat never knew every kind and an enemy building");
        int knewAt = sim.TickNumber;
        for (int t = 0; t < 100; t++) sim.Tick();
        _out.WriteLine($"knew every kind and an enemy building at {knewAt}; phase at {sim.TickNumber}: {ai.Board.ScoutPhase}");
        Assert.Equal(1, ai.Board.ScoutPhase);
    }

    /// <summary>
    /// BUG-1053's other side: a seat that knows a node of every kind (the shipped start: trees and a mine in sight of the
    /// hall) never sends a scout again once one dies after the ring is seen, on seeds 1-3: one scout sent by tick 6,000.
    /// So a match whose scout lives on, or dies with its home known, plays as before (the pins don't move).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void ASeatKnowingANodeOfEveryKind_NeverReScouts(ulong seed)
    {
        Simulation sim = AiScenes.Generated(s_seats, seed, record: false, out _);
        AiPlayer ai = sim.AiOf(0)!;
        UntilScoutOut(sim, 3000);
        while (!ai.ReachLifted() && ai.Board.ScoutPhase == 1 && sim.TickNumber < 6000) sim.Tick();
        Assert.True(ai.ReachLifted() && ai.Board.ScoutPhase == 1, "setup: the sweep ended before the ring was seen");
        Assert.True(ScoutManager.KnowsEveryKind(ai), "setup: the seat knows no node of some kind");
        int killedAt = sim.TickNumber;
        Assert.True(KillScout(sim));
        Assert.Equal(2, AfterTheDeath(sim));
        int sent = 0;
        while (sim.TickNumber < 6000)
        {
            sim.Tick();
            if (ai.Board.ScoutPhase != 2) sent++;
        }
        _out.WriteLine($"seed {seed}: scout killed at tick {killedAt}; ticks not at phase 2 after the death {sent}; knows every kind {ScoutManager.KnowsEveryKind(ai)}");
        Assert.Equal(0, sent);
    }

    /// <summary>
    /// BUG-1053: the scout killed once its head ring is seen, on a start where the seat knows no forest (every tree within
    /// 90 m taken, seed 1): one more scout is sent (phase 0, then 1 with a new unit); with ai.scoutReplacements 0 none is.
    /// </summary>
    [Theory]
    [InlineData(2, true)]
    [InlineData(0, false)]
    public void TheScoutLostAfterTheRing_WithNoForestKnown_IsSentAgain_WithinTheCap(int replacements, bool again)
    {
        Simulation sim = Far(1, Replacements(replacements));
        AiPlayer ai = sim.AiOf(0)!;
        UntilScoutOut(sim, 3000);
        while (!ai.ReachLifted() && ai.Board.ScoutPhase == 1 && sim.TickNumber < 6000) sim.Tick();
        Assert.True(ai.ReachLifted() && ai.Board.ScoutPhase == 1, "setup: the scout's sweep ended before its ring was seen");
        Assert.False(ScoutManager.KnowsEveryKind(ai), "setup: the seat knows a forest already");
        (int Slot, int Gen) first = (ai.Board.ScoutSlot, ai.Board.ScoutGeneration);
        Assert.True(KillScout(sim));
        Assert.Equal(again ? (byte)0 : (byte)2, AfterTheDeath(sim));
        if (!again) return;
        for (int t = 0; t < 400 && ai.Board.ScoutPhase != 1; t++) sim.Tick();
        Assert.Equal(1, ai.Board.ScoutPhase);
        Assert.NotEqual(first, (ai.Board.ScoutSlot, ai.Board.ScoutGeneration));
        Assert.True(ai.ReachLifted(), "the reach fell back after the re-scout");
    }

    /// <summary>
    /// A save at a tick between two scout deaths (every scout killed its first tick out, seed 1) loads into a seat equal to
    /// the saved one, and the loaded sim follows the unsaved one hash for hash for 3,000 ticks, the cap engaging in both on
    /// the same tick: the count it reads (own unit losses) is saved state. After the first death (losses 1) and after the
    /// second (losses 2, the next death is past the cap).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ASaveBetweenTwoScoutDeaths_LoadsEqual_AndTheCapEngagesOnTheSameTick(int deathsBeforeSave)
    {
        Simulation a = AiScenes.Generated(s_seats, 1, record: false, out _), b = AiScenes.Generated(s_seats, 1, record: false, out _);
        int deaths = 0;
        while (deaths < deathsBeforeSave && a.TickNumber < 4000)
        {
            a.Tick();
            b.Tick();
            bool ka = KillScout(a), kb = KillScout(b);
            Assert.Equal(ka, kb);
            if (ka) deaths++;
        }
        Assert.Equal(deathsBeforeSave, deaths);
        for (int t = 0; t < 5; t++) { a.Tick(); b.Tick(); } // between the deaths: the replacement not sent yet, or just sent
        Simulation loaded = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(b));
        SaveTestRun.AssertSameAi(a.AiOf(0), loaded.AiOf(0), "on load");
        Assert.Equal(a.World.UnitLosses[0], loaded.World.UnitLosses[0]);
        int cappedA = -1, cappedL = -1;
        for (int t = 0; t < 3000; t++)
        {
            a.Tick();
            loaded.Tick();
            Assert.Equal(KillScout(a), KillScout(loaded));
            Assert.True(a.StateHash() == loaded.StateHash(), $"diverged at tick {a.TickNumber}");
            if (cappedA < 0 && a.AiOf(0)!.Board.ScoutPhase == 2) cappedA = a.TickNumber;
            if (cappedL < 0 && loaded.AiOf(0)!.Board.ScoutPhase == 2) cappedL = loaded.TickNumber;
        }
        SaveTestRun.AssertSameAi(a.AiOf(0), loaded.AiOf(0), "after 3,000 ticks");
        _out.WriteLine($"saved after {deathsBeforeSave} deaths at tick {b.TickNumber}; own losses {a.World.UnitLosses[0]}; capped at {cappedA} / {cappedL}");
        Assert.True(cappedA > 0, "the cap never engaged");
        Assert.Equal(cappedA, cappedL);
    }

    /// <summary>The death branch's new reads (the own losses, <see cref="ScoutManager.KnowsEveryKind"/>, round 1's <see cref="ScoutManager.SentAgain"/> and <see cref="ScoutManager.FoundEnoughToGather"/>) allocate nothing, on both seats of a running match.</summary>
    [Fact]
    public void KnowsEveryKind_AndTheOwnLosses_AllocateNothing()
    {
        Simulation sim = AiScenes.Generated(s_seats, 1, record: false, out _);
        while (sim.TickNumber < 700) sim.Tick();
        AiPlayer[] ais = { sim.AiOf(0)!, sim.AiOf(1)! };
        int sink = 0;
        Action probe = () =>
        {
            foreach (AiPlayer ai in ais)
            {
                sink += ai.View.OwnUnitLosses;
                if (ScoutManager.KnowsEveryKind(ai)) sink++;
                if (ScoutManager.SentAgain(ai)) sink++;
                if (ScoutManager.FoundEnoughToGather(ai)) sink++;
            }
        };
        probe();
        AllocationProbe.AssertZero(probe, _out);
        _out.WriteLine($"{sink}");
    }

    /// <summary>The accessor reads the own seat's count, which moves with an own unit's death only.</summary>
    [Fact]
    public void OwnUnitLosses_IsTheOwnSeatsCount()
    {
        Simulation sim = AiScenes.Generated(s_seats, 1, record: false, out _);
        while (sim.TickNumber < 5) sim.Tick();
        Assert.True(KillOther(sim, 1));
        Assert.Equal((0, 1), (sim.AiOf(0)!.View.OwnUnitLosses, sim.AiOf(1)!.View.OwnUnitLosses));
        Assert.True(KillOther(sim, 0));
        Assert.Equal((1, 1), (sim.AiOf(0)!.View.OwnUnitLosses, sim.AiOf(1)!.View.OwnUnitLosses));
    }
}
