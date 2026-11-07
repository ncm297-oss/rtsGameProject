using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.HardKnobsScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4c slice 2 criteria 5 and 7: the Hard knobs in whole matches. Two processes play Hard against Hard to the same
/// per-tick hashes; a save mid-wave with a focus in progress and a pulled-back member follows its run; and the focus a seat
/// picks is always a live enemy unit its own fog shows it that think.
/// </summary>
public class HardKnobsMatchTests
{
    private readonly ITestOutputHelper _out;

    public HardKnobsMatchTests(ITestOutputHelper output) => _out = output;

    private const string ChildVariable = "RTS_HARD_KNOBS_CHILD";
    private const string ChildLine = "HARD-KNOBS ";

    /// <summary>What the focus and pull-back rules did over a match, per seat; and the first fog fault, if any.</summary>
    private sealed class Watch
    {
        internal readonly int[] Focuses = new int[2], PullBacks = new int[2];
        internal string? Bad;
        internal int Checks;
    }

    /// <summary>
    /// Hooks both seats of <paramref name="sim"/>: every think with a focus is checked against the world (criterion 7: a live
    /// unit of an enemy of the seat that <see cref="Vision.FogStore.CanSeeUnit"/> grants it); every Move a seat sends to a
    /// wave member below its faction's <c>retreat.hpFraction</c> is a pull-back.
    /// </summary>
    private static Watch Hook(Simulation sim)
    {
        var watch = new Watch();
        World w = sim.World;
        for (int p = 0; p < 2; p++)
        {
            int seat = p;
            AiPlayer ai = sim.AiOf(p)!;
            ai.ThoughtForTests = a =>
            {
                EntityHandle f = a.FocusTarget;
                if (f.Generation == 0) return;
                watch.Focuses[seat]++;
                watch.Checks++;
                bool ok = w.Units.Alive[f.Index] && w.Units.Generation[f.Index] == f.Generation && w.IsEnemy(seat, w.Units.Owner[f.Index])
                    && w.Fog.CanSeeUnit(seat, f.Index);
                if (!ok) watch.Bad ??= $"tick {a.Now}: player {seat}'s focus {f.Index} is not a live enemy unit it sees";
            };
            ai.SentForTests = c =>
            {
                int i = c.Unit.Index;
                if (c.Kind == CommandKind.Move && ai.Board.WaveGeneration[i] == c.Unit.Generation && w.Units.Alive[i]
                    && w.Units.Hp[i] < ai.Def.RetreatHpFraction * w.MaxHp(i))
                    watch.PullBacks[seat]++;
            };
        }
        return watch;
    }

    /// <summary>
    /// Criterion 5: Hard against Hard on Raraku, seed 1, 20,000 ticks (or to the end): this process and a fresh child
    /// <c>dotnet test</c> of the row fold the same per-tick hashes, with the same focus picks and pull-backs. Both check
    /// criterion 7's fog rule at every think too.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void Raraku_HardVersusHard_TwoProcesses_SamePerTickHashes() =>
        TwoProcesses(MapFileTests.RarakuMatch(1, false, out _, AiDifficulty.Hard, AiDifficulty.Hard), "raraku", nameof(Raraku_HardVersusHard_TwoProcesses_SamePerTickHashes));

    /// <summary>Criterion 5: the same on Pale Hills, seed 1.</summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void PaleHills_HardVersusHard_TwoProcesses_SamePerTickHashes() =>
        TwoProcesses(MapFileTests.MapMatch(MapFileTests.PaleHills, 1, false, out _, AiDifficulty.Hard, AiDifficulty.Hard), "pale_hills",
            nameof(PaleHills_HardVersusHard_TwoProcesses_SamePerTickHashes));

    private void TwoProcesses(Simulation sim, string map, string row)
    {
        Watch watch = Hook(sim);
        var h = new StateHasher();
        while (sim.TickNumber < 20_000 && !sim.MatchOver)
        {
            sim.Tick();
            h.Add(sim.StateHash());
        }
        Assert.True(watch.Bad == null, watch.Bad);
        string mine = $"{h.Value:X16} {sim.StateHash():X16} {sim.TickNumber} focus {watch.Focuses[0]}/{watch.Focuses[1]} pulled {watch.PullBacks[0]}/{watch.PullBacks[1]}";
        if (Environment.GetEnvironmentVariable(ChildVariable) == "1")
        {
            _out.WriteLine(ChildLine + mine);
            return;
        }
        _out.WriteLine($"{map}, this process: {mine}; match over {sim.MatchOver}");
        Assert.True(watch.Focuses[0] + watch.Focuses[1] > 0, "no seat focused fire");
        Assert.True(watch.PullBacks[0] + watch.PullBacks[1] > 0, "no seat pulled a wounded unit back");
        Assert.Equal(mine, MapFileTests.RunChild(typeof(HardKnobsMatchTests), row, ChildVariable, ChildLine, seconds: 1200));
    }

    /// <summary>
    /// Criterion 7: two Hard seats on the generated map with early waves and raiders in each other's base (they fight from the
    /// first think): at every think a seat picks a focus, it is a live enemy unit its own fog grants it, and it focuses and
    /// pulls back within the run.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    public void TheFocus_IsAlwaysAnEnemyUnitTheSeatSees(ulong seed)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, seed, record: false, out _, data: s_early.Value, raiders: 3);
        Watch watch = Hook(sim);
        for (int t = 0; t < 6000 && watch.Bad == null; t++) sim.Tick();
        Assert.True(watch.Bad == null, watch.Bad);
        _out.WriteLine($"seed {seed}: {watch.Checks} focus picks checked against the fog; focus {watch.Focuses[0]}/{watch.Focuses[1]}, pull-backs {watch.PullBacks[0]}/{watch.PullBacks[1]}");
        Assert.True(watch.Focuses[0] > 0 && watch.Focuses[1] > 0, "a seat never focused");
    }

    private static readonly Lazy<GameData> s_early = new(() => AiScenes.EarlyWaves());

    /// <summary>
    /// Criterion 5: the focus scene on Hard (combat on), one member cut to 30 hp once the wave is out, before it meets the Raiders, saved at
    /// the end of the first think whose focus is held by a member while the wounded one is out of the wave: the save is
    /// format 6 (the slice adds no state: <c>AIST</c> and <c>HUNT</c> as before), loads, re-saves byte for byte, follows the
    /// run hash for hash for 1,000 ticks, and the seats are equal field for field at both ends. The formats are unchanged:
    /// saves 6 / 7, replays 9.
    /// </summary>
    [Fact]
    public void ASaveMidWave_WithAFocusAndAPulledBackMember_FollowsTheRunFor1000Ticks()
    {
        Assert.Equal((7, 6), (SaveFormat.CurrentVersion, SaveFormat.HuntMaplessVersion));
        Assert.Equal(9, Replay.CurrentFormatVersion);
        (Simulation sim, List<EntityHandle> members, _) = Focus(AiDifficulty.Hard);
        AiPlayer ai = sim.AiOf(0)!;
        NextThink(sim);
        Assert.True(ai.Board.WaveActive, "the wave did not start");
        sim.World.Units.Hp[members[3].Index] = 30; // after the start: a wounded unit is in no new wave
        EntityHandle focus = default;
        ai.ThoughtForTests = a => focus = a.FocusTarget;
        bool ready = false;
        for (int t = 0; t < 2400 && !ready; t++)
        {
            sim.Tick();
            bool pulled = sim.World.Units.IsAlive(members[3]) && ai.Board.WaveGeneration[members[3].Index] != members[3].Generation;
            bool held = focus.Generation != 0 && members.Any(m => sim.World.Units.IsAlive(m) && ai.View.TryGetAttackOrder(m.Index, out EntityHandle h) && h == focus);
            ready = pulled && held && ai.Board.WaveActive;
        }
        Assert.True(ready, "no tick with a focus held and the wounded member home");
        _out.WriteLine($"saved at tick {sim.TickNumber}, focus {focus.Index}");
        byte[] bytes = SaveTestRun.Save(sim);
        Assert.Equal(SaveFormat.HuntMaplessVersion, BitConverter.ToInt32(bytes, SaveFormat.MagicLength));
        Simulation load = SaveTestRun.Load(HardKnobsScenes.Data.Value, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        SaveTestRun.AssertSameState(sim, load, "mid-focus");
        int focuses = 0;
        ai.ThoughtForTests = a => { if (a.FocusTarget.Generation != 0) focuses++; };
        for (int t = 0; t < 1000; t++)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
        SaveTestRun.AssertSameState(sim, load, "mid-focus + 1,000");
        Assert.True(focuses > 0, "no focus after the save");
    }
}
