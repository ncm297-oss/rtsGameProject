using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S14 item D (BUG-1554 / 1556, docs/01 2026-11-17 (3); docs/03 "Implementation (M6-S14)"): the seat counts the blind
/// re-scouts it has lost (<see cref="Blackboard.BlindScoutsLost"/>), and at <c>ai.scoutReplacements</c> it sends no blind
/// re-scout again. The scene is QA's blind one (<see cref="M6S12AiRulesQaTests.BlindScene"/>: no node of either kind known,
/// five idle workers, past the scout's own cap, the sweep ended); "camped" kills each re-scout the tick it is out.
/// </summary>
public class BlindRescoutCountTests
{
    private readonly ITestOutputHelper _out;

    public BlindRescoutCountTests(ITestOutputHelper output) => _out = output;

    /// <summary>Kills player 0's scout if one is out and alive (an enemy camped at the door); true when it did.</summary>
    private static bool KillTheScout(Simulation sim)
    {
        World w = sim.World;
        Blackboard b = sim.AiOf(0)!.Board;
        if (b.ScoutPhase != 1 || b.ScoutSlot < 0) return false;
        var scout = new EntityHandle(b.ScoutSlot, b.ScoutGeneration);
        if (!w.Units.IsAlive(scout)) return false;
        CombatSystem.HitUnit(w, new PendingHit(default, 1, scout, false, w.Units.Hp[scout.Index]));
        return true;
    }

    /// <summary>
    /// The count: every blind re-scout killed as it sets out is counted once, the count stops at <c>ai.scoutReplacements</c>
    /// (2), and from then on no re-scout goes for the rest of 12,000 ticks though the seat stays blind with idle workers.
    /// </summary>
    [Fact]
    public void EachBlindRescoutLost_IsCounted_AndAtTheCapNoneGoesAgain()
    {
        Simulation sim = M6S12AiRulesQaTests.BlindScene(keepMines: false);
        AiPlayer ai = sim.AiOf(0)!;
        int cap = ai.Rules.ScoutReplacements, killed = 0, capAt = -1, sentAfterCap = 0;
        byte phase = ai.Board.ScoutPhase;
        Assert.True(cap > 0);
        while (sim.TickNumber < 12_000)
        {
            sim.Tick();
            if (phase == 2 && ai.Board.ScoutPhase == 1 && capAt >= 0) sentAfterCap++;
            phase = ai.Board.ScoutPhase;
            if (KillTheScout(sim)) killed++;
            if (capAt < 0 && ai.Board.BlindScoutsLost == cap) capAt = sim.TickNumber;
        }
        _out.WriteLine($"killed {killed}, counted {ai.Board.BlindScoutsLost} (cap {cap}, reached at {capAt}); sent after the cap {sentAfterCap}; blind to wood {ScoutManager.BlindTo(ai, Data.ResourceKind.Wood)}");
        Assert.Equal(cap, killed);
        Assert.Equal(cap, ai.Board.BlindScoutsLost);
        Assert.Equal(0, sentAfterCap);
        Assert.False(ai.Board.BlindScoutOut);
        Assert.True(ScoutManager.BlindTo(ai, Data.ResourceKind.Wood) || ScoutManager.BlindTo(ai, Data.ResourceKind.Gold), "the scene: the seat is still blind");
    }

    /// <summary>A re-scout that comes home alive (released) is not counted: the scene unharmed for 12,000 ticks counts none.</summary>
    [Fact]
    public void ABlindRescoutReleasedAlive_IsNotCounted()
    {
        Simulation sim = M6S12AiRulesQaTests.BlindScene(keepMines: false);
        AiPlayer ai = sim.AiOf(0)!;
        int sent = 0;
        byte phase = ai.Board.ScoutPhase;
        while (sim.TickNumber < 12_000)
        {
            sim.Tick();
            if (phase == 2 && ai.Board.ScoutPhase == 1) sent++;
            phase = ai.Board.ScoutPhase;
        }
        _out.WriteLine($"re-scouts sent {sent}, counted lost {ai.Board.BlindScoutsLost}");
        Assert.True(sent > 0);
        Assert.Equal(0, ai.Board.BlindScoutsLost);
    }

    /// <summary>
    /// Criteria 6 and 8 (the count survives a save; twins): the camped scene run twice gives the same state hash every tick and
    /// the same count; a save taken on the think after the first blind re-scout died (count 1, the next one free to go) and one
    /// taken on the think after the last allowed one died (count at the cap) each load equal field for field, save again to the
    /// same bytes, and follow the run hash for hash, count for count, to tick 9,000.
    /// </summary>
    [Fact]
    public void TheCount_Twins_AndSurvivesASave_ToTheEnd()
    {
        Simulation a = M6S12AiRulesQaTests.BlindScene(keepMines: false), b = M6S12AiRulesQaTests.BlindScene(keepMines: false);
        AiPlayer ai = a.AiOf(0)!;
        int cap = ai.Rules.ScoutReplacements;
        var loads = new List<(Simulation Sim, int At, int Count)>();
        int seen = 0;
        while (a.TickNumber < 9000)
        {
            a.Tick();
            b.Tick();
            foreach ((Simulation s, _, _) in loads) s.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"twins diverged at tick {a.TickNumber}");
            foreach ((Simulation s, int at, _) in loads)
                Assert.True(a.StateHash() == s.StateHash(), $"the save of tick {at} diverged at tick {a.TickNumber}");
            KillTheScout(a);
            KillTheScout(b);
            foreach ((Simulation s, _, _) in loads) KillTheScout(s);
            if (ai.Board.BlindScoutsLost != seen && (a.TickNumber - 1) % ai.ThinkTicks == 0)
            {
                // The think that counted the loss has run: save between ticks, as a player would.
                seen = ai.Board.BlindScoutsLost;
                byte[] bytes = SaveTestRun.Save(a);
                Simulation load = SaveTestRun.Load(a.World.Data, bytes);
                Assert.Equal(bytes, SaveTestRun.Save(load));
                SaveTestRun.AssertSameState(a, load, $"saved with {seen} lost");
                Assert.Equal(seen, load.AiOf(0)!.Board.BlindScoutsLost);
                loads.Add((load, a.TickNumber, seen));
            }
        }
        _out.WriteLine($"saves at {string.Join(", ", loads.Select(l => $"{l.At} (count {l.Count})"))}; end count {ai.Board.BlindScoutsLost}");
        Assert.Equal(cap, loads.Count);
        Assert.Equal(cap, ai.Board.BlindScoutsLost);
        Assert.Equal(cap, b.AiOf(0)!.Board.BlindScoutsLost);
        foreach ((Simulation s, int at, _) in loads)
        {
            Assert.Equal(cap, s.AiOf(0)!.Board.BlindScoutsLost);
            SaveTestRun.AssertSameState(a, s, $"the save of tick {at} at the end");
        }
    }
}
