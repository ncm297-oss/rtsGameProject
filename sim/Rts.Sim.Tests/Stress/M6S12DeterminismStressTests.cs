using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-S12 criterion 9: each new AI rule in a match, twins and a save / load. Raraku seed 1, Normal v Normal: seat 0's rally
/// point leaves the one-cell lane between its own buildings (BUG-1461, tick 3,020) and the economy moves gatherers between
/// resources (BUG-1531's rebalance, measured from the drop-off); the blind re-scout scene (BUG-1460, <see cref="BlindRescoutTests"/>'s
/// staging, the scout sent at tick 1). For each rule: two runs hash alike on every tick, and a save taken on the tick the rule
/// first acts (its command still pending) loads byte-identical, then follows the run hash for hash for 2,000 ticks.
/// </summary>
[Trait("Category", "Stress")]
public class M6S12DeterminismStressTests(ITestOutputHelper output)
{
    private const int Follow = 2000, Limit = 30_000;

    /// <summary>
    /// Plays twins of <paramref name="make"/>; <paramref name="acted"/> is asked after every tick of the first and returns true
    /// on the tick the rule first acted; the save is taken there.
    /// </summary>
    private int TwinsAndSave(string name, Func<Simulation> make, GameData data, Func<Simulation, bool> acted)
    {
        Simulation a = make(), b = make();
        int at = -1;
        Simulation? loaded = null;
        while (a.TickNumber < Limit && !a.MatchOver && (at < 0 || a.TickNumber < at + Follow))
        {
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"{name}: twins differ after tick {a.TickNumber}");
            if (loaded != null)
            {
                loaded.Tick();
                Assert.True(a.StateHash() == loaded.StateHash(), $"{name}: the loaded save diverged at tick {a.TickNumber}");
            }
            else if (acted(a))
            {
                at = a.TickNumber;
                byte[] save = SaveTestRun.Save(a);
                loaded = SaveTestRun.Load(data, save);
                Assert.Equal(save, SaveTestRun.Save(loaded));
                SaveTestRun.AssertSameState(a, loaded, $"{name}: at the save");
            }
        }
        output.WriteLine($"{name}: the rule acted at tick {at}; followed to {a.TickNumber}");
        Assert.True(at >= 0, $"{name}: the rule never acted: the row proves nothing");
        Assert.NotNull(loaded);
        return at;
    }

    [Fact]
    public void RarakuSeed1_TheRallyShift_TwinsAndSave()
    {
        TwinsAndSave("rally shift", () => MapFileTests.MapMatch("raraku", 1, false, out _), TestSim.Data, sim =>
        {
            AiPlayer ai = sim.AiOf(0)!;
            if (ai.Board.RallyVersion == 0 || !ai.TryHallPoint(out Vector2 hall)) return false;
            // The point is off the nominal one (the hall + ai.rallyDistance along the line to it) only by the passage rule.
            Vector2 d = ai.Board.Rally - hall;
            return MathF.Abs(d.Length() - ai.Rules.RallyDistance) > 0.5f && sim.TickNumber % ai.ThinkTicks == 1;
        });
    }

    [Fact]
    public void RarakuSeed1_ARebalance_TwinsAndSave()
    {
        int sentAt = -1, made = 0;
        TwinsAndSave("rebalance", () =>
        {
            Simulation sim = MapFileTests.MapMatch("raraku", 1, false, out _);
            if (made++ > 0) return sim; // the twin: no hook
            for (int p = 0; p < 2; p++)
            {
                AiPlayer ai = sim.AiOf(p)!;
                World w = sim.World;
                ai.SentForTests = c =>
                {
                    // A rebalance: a Gather to a worker at work (Gathering), not an idle one.
                    if (sentAt < 0 && c.Kind == CommandKind.Gather && w.Units.State[c.Unit.Index] == UnitState.Gathering) sentAt = c.Tick;
                };
            }
            return sim;
        }, TestSim.Data, sim => sentAt >= 0 && sim.PendingCommandCount > 0);
    }

    [Fact]
    public void ABlindSeat_TheReScout_TwinsAndSave()
    {
        TwinsAndSave("re-scout", BlindRescoutTests.BlindScene, BlindRescoutTests.Quiet, sim => sim.AiOf(0)!.Board.ScoutPhase == 1 && sim.PendingCommandCount > 0);
    }
}
