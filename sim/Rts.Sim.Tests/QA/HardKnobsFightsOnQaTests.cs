using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.HardKnobsScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-4c-2 round 2 (2026-11-07-0926, BUG-1123's fix): the <see cref="MilitaryManager.FightsOn"/> bit. A flagged member
/// pulled by the defense drops the bit, is no member, and the next wave flags it again; a save mid-way through that second
/// wave follows the run. Over whole mixed-difficulty matches: the bit is never set on Easy or Normal, never left on a slot
/// that is not a wave's, and a flagged member is never pulled back home while its wave goes on.
/// </summary>
public class HardKnobsFightsOnQaTests
{
    private readonly ITestOutputHelper _out;

    public HardKnobsFightsOnQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Six Heavy Infantry wave (a wave needs four), the two nearest the hall wounded at the start (flagged). A Raider by the
    /// hall: with no home unit the defense pulls the two members nearest the hall (the start threshold keeps four), the two
    /// flagged ones. Pulled, each is out of the wave with the bit dropped (flag 0) and a defender. The threat gone and the
    /// wave ended (its other members removed), two fresh units make the next wave: the two wounded are members again,
    /// flagged again, the fresh not; cut further they are not sent home. A save then follows the run for 600 ticks.
    /// </summary>
    [Fact]
    public void AFlaggedMember_PulledByTheDefense_DropsTheBit_ThenTheNextWaveFlagsItAgain()
    {
        Simulation sim = Scene(AiDifficulty.Hard, combat: false);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> wounded = Army(sim, 2, 14, 26); // clear of the hall's footprint, nearer it than the rest
        foreach (EntityHandle h in wounded) w.Units.Hp[h.Index] = 30; // below 39 = 0.3 x 130
        List<EntityHandle> healthy = Army(sim, 4, 22, 28);
        NextThink(sim);
        Assert.Equal(1, ai.Board.WavesStarted);
        Assert.Equal(6 * TestSim.Data.Units[Heavy].HalfPop, ai.Board.WaveStartHalfPop);
        foreach (EntityHandle h in wounded) Assert.NotEqual(0, ai.Board.WaveFlag[h.Index] & MilitaryManager.FightsOn);
        foreach (EntityHandle h in healthy) Assert.Equal(0, ai.Board.WaveFlag[h.Index] & MilitaryManager.FightsOn);

        EntityHandle raider = CombatScenes.Place(sim, 1, CombatScenes.Raider, ai.HallPoint + new Vector2(0f, 20f));
        CombatScenes.Spot(sim, 0, raider);
        w.Units.Hold[raider.Index] = true;
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 4 && ai.Board.WavePulls == 0; k++) NextThink(sim);
        Assert.True(ai.Board.Defending, "no defense");
        Assert.True(ai.Board.WaveActive, "the wave ended on the pull");
        Assert.Equal(2, ai.Board.WavePulls);
        foreach (EntityHandle h in wounded)
        {
            Assert.Equal(0, ai.Board.WaveGeneration[h.Index]);
            Assert.Equal(0, ai.Board.WaveFlag[h.Index]); // the bit dropped with the pull
            Assert.Equal(h.Generation, ai.Board.DefenderGeneration[h.Index]);
        }
        foreach (EntityHandle h in healthy) Assert.Equal(h.Generation, ai.Board.WaveGeneration[h.Index]);
        Assert.Equal(4 * TestSim.Data.Units[Heavy].HalfPop, ai.Board.WaveStartHalfPop);

        // The threat gone, the wave's other members gone: the wave ends; two fresh units make the next one with the two wounded.
        w.Units.Free(raider);
        foreach (EntityHandle h in healthy) w.Units.Free(h);
        for (int k = 0; k < 4; k++) NextThink(sim);
        Assert.False(ai.Board.WaveActive, "the emptied wave goes on");
        List<EntityHandle> fresh = Army(sim, 2, 18, 34);
        for (int k = 0; k < 200 && ai.Board.WavesStarted < 2; k++) NextThink(sim);
        Assert.Equal(2, ai.Board.WavesStarted);
        Assert.Equal(4 * TestSim.Data.Units[Heavy].HalfPop, ai.Board.WaveStartHalfPop);
        foreach (EntityHandle h in wounded)
        {
            Assert.Equal(h.Generation, ai.Board.WaveGeneration[h.Index]);
            Assert.NotEqual(0, ai.Board.WaveFlag[h.Index] & MilitaryManager.FightsOn);
        }
        foreach (EntityHandle h in fresh)
        {
            Assert.Equal(h.Generation, ai.Board.WaveGeneration[h.Index]);
            Assert.Equal(0, ai.Board.WaveFlag[h.Index] & MilitaryManager.FightsOn);
        }
        log.Clear();
        foreach (EntityHandle h in wounded) w.Units.Hp[h.Index] = 5;
        for (int k = 0; k < 6; k++) NextThink(sim);
        Assert.True(ai.Board.WaveActive);
        Assert.DoesNotContain(log, c => wounded.Contains(c.Unit) && c.Kind == CommandKind.Move);
        foreach (EntityHandle h in wounded)
        {
            Assert.Equal(h.Generation, ai.Board.WaveGeneration[h.Index]);
            Assert.NotEqual(0, ai.Board.WaveFlag[h.Index] & MilitaryManager.FightsOn);
            Assert.NotEqual(UnitState.Idle, w.Units.State[h.Index]); // on its way with the wave
        }

        // A save with the bit set on the re-flagged pair follows the run.
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation load = SaveTestRun.Load(HardKnobsScenes.Data.Value, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        foreach (EntityHandle h in wounded) Assert.Equal(ai.Board.WaveFlag[h.Index], load.AiOf(0)!.Board.WaveFlag[h.Index]);
        for (int t = 0; t < 600; t++)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
        SaveTestRun.AssertSameState(sim, load, "re-flagged + 600");
        _out.WriteLine($"pulls {ai.Board.WavePulls}, waves {ai.Board.WavesStarted}, commands to the wounded since the cut: " +
            string.Join(", ", log.Where(c => wounded.Contains(c.Unit)).Select(c => $"{c.Tick}:{c.Kind}")));
    }

    /// <summary>
    /// The bit's invariants over whole matches, every tick, Easy / Normal / Hard / Hard on 4-seat Pale Hills (seeds 1 and 2,
    /// 20,000 ticks or the end): (a) an Easy or Normal seat never holds <see cref="MilitaryManager.FightsOn"/> on any slot;
    /// (b) a Hard seat's bit sits only on a slot that has a wave generation, beside an order owed of 0, ToAttack or Focused
    /// (never ToRally); (c) a member flagged at the start of a think is not sent a Move to the rally point while its wave
    /// goes on, unless that is <c>Leave</c> (its tries spent, or a units-only attacker at the target). Prints the flags set and
    /// the pull-backs, so a vacuous pass shows.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void MixedDifficulties_PaleHills_TheBitIsHardOnly_AndAFlaggedMemberIsNeverPulledBack(ulong seed)
    {
        AiDifficulty[] diffs = { AiDifficulty.Easy, AiDifficulty.Normal, AiDifficulty.Hard, AiDifficulty.Hard };
        PlayerSeat[] seats = Enumerable.Range(0, 4).Select(p => new PlayerSeat(p % 2, SeatController.Ai, diffs[p])).ToArray();
        Simulation sim = AiScenes.Generated(seats, seed, false, out _, units: 1000, commands: 2048,
            configure: c => c with { MapId = MapFileTests.PaleHills, Map = c.Map with { Width = 256, Height = 256 } });
        World w = sim.World;
        string? bad = null;
        int slots = w.Units.Capacity;
        var flagged = new bool[4][];
        var tries = new byte[4][];
        for (int p = 0; p < 4; p++) { flagged[p] = new bool[slots]; tries[p] = new byte[slots]; }
        var flagsSet = new int[4];
        var pulls = new int[4];
        var flaggedLeaves = new int[4];
        var seen = new HashSet<(int, int, int)>();
        for (int p = 0; p < 4; p++)
        {
            int seat = p;
            AiPlayer ai = sim.AiOf(p)!;
            ai.SentForTests = c =>
            {
                int i = c.Unit.Index;
                Blackboard b = ai.Board;
                if (c.Kind != CommandKind.Move || c.Position != b.Rally || !b.WaveActive || !w.Units.IsAlive(c.Unit)) return;
                if (!flagged[seat][i]) { if (w.Units.Hp[i] < ai.Def.RetreatHpFraction * w.MaxHp(i)) pulls[seat]++; return; }
                UnitDef def = w.Data.Units[w.Units.TypeId[i]];
                bool leave = tries[seat][i] >= MilitaryManager.MaxTries || def.Attack.Targets == AttackTargets.Units;
                if (leave) { flaggedLeaves[seat]++; return; }
                bad ??= $"tick {sim.TickNumber}: seat {seat} sent flagged member {i} home while its wave goes on (tries {tries[seat][i]})";
            };
        }
        while (sim.TickNumber < 20_000 && !sim.MatchOver && bad == null)
        {
            for (int p = 0; p < 4 && bad == null; p++)
            {
                Blackboard b = sim.AiOf(p)!.Board;
                for (int i = 0; i < slots; i++)
                {
                    byte f = b.WaveFlag[i];
                    bool bit = (f & MilitaryManager.FightsOn) != 0;
                    flagged[p][i] = bit && b.WaveActive && w.Units.Alive[i] && b.WaveGeneration[i] == w.Units.Generation[i];
                    tries[p][i] = b.WaveTries[i];
                    if (!bit) continue;
                    if (diffs[p] != AiDifficulty.Hard) { bad = $"tick {sim.TickNumber}: {diffs[p]} seat {p} holds FightsOn on slot {i}"; break; }
                    if (b.WaveGeneration[i] == 0) { bad = $"tick {sim.TickNumber}: seat {p} slot {i} FightsOn without a wave generation"; break; }
                    byte owed = MilitaryManager.Owed(b, i);
                    if (owed is not (0 or MilitaryManager.ToAttack or MilitaryManager.Focused)) { bad = $"tick {sim.TickNumber}: seat {p} slot {i} flag 0x{f:X2}"; break; }
                    if (seen.Add((p, i, b.WaveGeneration[i] * 1000 + b.WavesStarted))) flagsSet[p]++;
                }
            }
            sim.Tick();
        }
        _out.WriteLine($"seed {seed}: tick {sim.TickNumber}, over {sim.MatchOver}; FightsOn memberships per seat {string.Join("/", flagsSet)}; " +
            $"pull-backs {string.Join("/", pulls)}; flagged members' Leaves {string.Join("/", flaggedLeaves)}; waves " +
            string.Join("/", Enumerable.Range(0, 4).Select(p => sim.AiOf(p)!.Board.WavesStarted)));
        Assert.True(bad == null, bad);
        Assert.True(pulls[2] + pulls[3] > 0, "no Hard pull-back at all: the oracle saw nothing");
    }
}
