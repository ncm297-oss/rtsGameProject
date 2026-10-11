using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M5-2, session 2026-10-11-0634): AI seats over long runs. (1) The view the AI thinks with stays equal to the brute-force
/// oracle while both seats think and fight (3 seeds x 2,000 ticks, every tick, both players). (2) Two AI seats for 6,000 ticks
/// (five minutes): the world invariants after every tick, at most a bounded number of commands per think, a recording that plays
/// back without the AI to every checkpoint, and different seeds giving different hashes.
/// </summary>
public class AiSeatStressTests
{
    private readonly ITestOutputHelper _out;

    public AiSeatStressTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData(2UL)]
    [InlineData(5UL)]
    [InlineData(9UL)]
    public void TheAisOwnView_EqualsTheOracle_EveryTick_WhileTwoAiSeatsThink(ulong seed)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Hard), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Generated(seats, seed, record: false, out _);
        World w = sim.World;
        var tally = new PlayerViewOracle.Tally();
        for (int t = 0; t < 2000; t++)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++) PlayerViewOracle.Check(w, sim.AiOf(p)!.View, p, tally, $"seed {seed} tick {t} player {p}");
            Assert.True(tally.First.Length == 0, tally.First + "\n" + tally);
        }
        _out.WriteLine($"seed {seed}: {tally}; AI commands {sim.AiOf(0)!.CommandsSent} / {sim.AiOf(1)!.CommandsSent}");
        Assert.Equal(0, tally.Leaks);
        Assert.Equal(0, tally.Misses);
        Assert.Equal(0, tally.FieldMismatches);
        Assert.True(sim.AiOf(0)!.CommandsSent > 0 && sim.AiOf(1)!.CommandsSent > 0);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(4UL)]
    [InlineData(8UL)]
    public void TwoAiSeats_SixThousandTicks_InvariantsBoundedCommands_AndPlayback(ulong seed)
    {
        const int ticks = 6000;
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Hard), new(1, SeatController.Ai, AiDifficulty.Hard) };
        Simulation sim = AiScenes.Generated(seats, seed, record: true, out ReplayRecorder? rec);
        Simulation other = AiScenes.Generated(seats, seed + 100, record: false, out _);
        World w = sim.World;
        UnitStore u = w.Units;
        int think = sim.AiOf(0)!.ThinkTicks;
        int worstPerThink = 0, thinks = 0, sameAsOther = 0;
        for (int t = 0; t < ticks; t++)
        {
            int logged = rec!.CommandCount;
            sim.Tick();
            other.Tick();
            if (sim.StateHash() == other.StateHash()) sameAsOther++;
            int sent = rec.CommandCount - logged;
            if ((sim.TickNumber - 1) % think == 0)
            {
                thinks++;
                worstPerThink = Math.Max(worstPerThink, sent);
            }
            for (int p = 0; p < 2; p++)
            {
                Assert.True(w.Gold[p] >= 0 && w.Wood[p] >= 0, $"seed {seed} tick {t}: player {p} gold {w.Gold[p]} wood {w.Wood[p]}");
            }
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y), $"seed {seed} tick {t}: unit {i} at {u.Position[i]}");
                Assert.True(u.Hp[i] > 0 && u.Hp[i] <= w.MaxHpOf(u.Owner[i], u.TypeId[i]), $"seed {seed} tick {t}: unit {i} hp {u.Hp[i]}");
            }
        }
        int workers0 = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && w.Data.Units[u.TypeId[i]].Slot == UnitSlot.Worker) workers0++;
        Replay r = rec!.ToReplay();
        int gathers = r.Commands.Count(c => c.Kind == CommandKind.Gather);
        _out.WriteLine($"seed {seed}: {thinks} think ticks, worst {worstPerThink} commands in one; {r.Commands.Length} logged ({gathers} Gathers); units {u.Count}, buildings {w.Buildings.Count}, player 0 workers {workers0}, gold {w.Gold[0]} / {w.Gold[1]}, wood {w.Wood[0]} / {w.Wood[1]}, pop {w.HalfPop[0]}/{w.HalfPopCap[0]}");
        // One per due step, per idle worker, one house, one production Train: far under the store sizes.
        Assert.True(worstPerThink <= 2 * (u.Count + 16), $"worst think sent {worstPerThink}");
        Assert.True(sameAsOther < 10, $"seed {seed} and {seed + 100} had equal hashes at {sameAsOther} ticks");
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? back));
        ReplayResult res = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(res.Ok, $"seed {seed}: {res.Error} at tick {res.Tick}");
        Assert.Equal(ticks, res.TicksRun);
    }
}
