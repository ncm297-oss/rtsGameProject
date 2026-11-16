using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D-R39: the two re-statements of <see cref="AiOpeningPairsReportTests"/>' row, proved not to hide what the row is for.
/// (1) "beaten: hall lost" names only a side with no finished town hall; with the hall standing, a due step it cannot
/// place still reads "DEAD END" and fails the row. (2) "defeated before its first wave was due" is reported; a side alive at
/// the end without a wave, or one whose wave was due before it fell, still fails.
/// </summary>
public class AiOpeningPairsVerdictTests
{
    /// <summary>
    /// A forged dead end on the (2, 0) row's scene (seed 13, the Whirlwind <c>standard</c> seat), one minute in: its last
    /// house step forced due with the bank full, nothing else in its way, so no reason applies: "DEAD END", a failure. Then
    /// every town hall of the seat is razed (no tick between): the same step reads "beaten: hall lost" and is no failure.
    /// </summary>
    [Fact]
    public void ADueStep_HallStanding_IsADeadEnd_HallRazed_IsBeatenHallLost()
    {
        ulong seed = AiOpeningSeeds.SmallestPair(2, 0);
        Simulation sim = AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seed);
        Assert.Equal((2, 0), (sim.AiOpening(0), sim.AiOpening(1)));
        while (sim.TickNumber < 1200) sim.Tick();
        AiPlayer ai = sim.AiOf(1)!;
        World w = sim.World;
        int step = -1;
        for (int i = ai.Opening.BuildOrder.Length - 1; i >= 0 && step < 0; i--)
        {
            AiStep s = ai.Opening.BuildOrder[i];
            if (s.Action == AiStepAction.Build && s.Target == ai.HouseType && !ai.Board.StepDone[i] && ai.Board.StepDueTick[i] < 0) step = i;
        }
        Assert.True(step >= 0, "no house step left undone and not due at 1:00");
        Assert.True(w.HalfPopCap[1] < w.Data.Rules.HalfPopCap, "the seat is at the hard cap at 1:00");
        ai.Board.StepDueTick[step] = sim.TickNumber - 20;
        w.Ledger.Gold[1] = 5000;
        w.Ledger.Wood[1] = 5000;
        string label = $"step {step + 1} (";

        Assert.Null(AiOpeningPairsReportTests.WhyWaiting(sim, ai, ai.Opening.BuildOrder[step], step, -1));
        var deadEnds = new List<string>();
        List<string> waiting = AiOpeningPairsReportTests.Waiting(sim, ai, -1, deadEnds, out _);
        Assert.Contains(waiting, x => x.StartsWith(label, StringComparison.Ordinal) && x.Contains("DEAD END", StringComparison.Ordinal));
        Assert.Contains(deadEnds, x => x.Contains(label, StringComparison.Ordinal) && x.Contains("DEAD END", StringComparison.Ordinal));

        var b = w.Buildings;
        int razed = 0;
        for (int s = 0; s < b.Capacity; s++)
            if (b.Alive[s] && b.Owner[s] == 1 && w.Data.Buildings[b.TypeId[s]].Slot == BuildingSlot.TownHall)
            {
                b.Damage(b.HandleOf(s), int.MaxValue);
                razed++;
            }
        Assert.True(razed > 0, "the seat had no town hall to raze");
        Assert.False(w.IsDefeated(1));
        Assert.Equal(AiOpeningPairsReportTests.HallLost, AiOpeningPairsReportTests.WhyWaiting(sim, ai, ai.Opening.BuildOrder[step], step, -1));
        deadEnds.Clear();
        waiting = AiOpeningPairsReportTests.Waiting(sim, ai, -1, deadEnds, out _);
        Assert.Contains(waiting, x => x.StartsWith(label, StringComparison.Ordinal) && x.EndsWith(AiOpeningPairsReportTests.HallLost, StringComparison.Ordinal));
        Assert.DoesNotContain(deadEnds, x => x.Contains(label, StringComparison.Ordinal));
    }

    /// <summary>
    /// The no-wave verdict's cases. Only the named side (<see cref="AiOpeningPairsReportTests.DefeatedBeforeDue"/>) defeated
    /// before its wave was due is reported; any other side without a wave fails as before, defeated or not, and a named side
    /// that now waves fails so the list is pruned.
    /// </summary>
    [Theory]
    [InlineData(1, -1, -1, false, null)]          // a wave sent: no verdict
    [InlineData(1, -1, -1, true, true)]           // the named side now waves: take it out
    [InlineData(0, 14980, -1, true, false)]       // named, defeated at 12:29, never due: reported
    [InlineData(0, 14980, 14980, true, false)]    // named, due on the tick it fell (not before): reported
    [InlineData(0, 14980, -1, false, true)]       // the same outcome on a side not named: fails as before
    [InlineData(0, -1, -1, true, true)]           // named but alive at the end: fails
    [InlineData(0, -1, -1, false, true)]          // alive at the end, never due: fails as before
    [InlineData(0, -1, 7000, false, true)]        // alive, due and never sent: fails
    [InlineData(0, 14980, 7000, true, true)]      // named, due at 5:50, never sent, then defeated: fails
    public void NoWave_ReportsOnlyTheNamedSideDefeatedBeforeItsWaveWasDue(int waves, int defeated, int due, bool named, bool? fails)
    {
        string? text = AiOpeningPairsReportTests.NoWave("whirlwind", "holy_war", 3, waves, defeated, due, named, out bool failed);
        if (fails == null)
        {
            Assert.Null(text);
            Assert.False(failed);
            return;
        }
        Assert.NotNull(text);
        Assert.Equal(fails.Value, failed);
        if (waves > 0) Assert.Contains("take it out of DefeatedBeforeDue", text, StringComparison.Ordinal);
        else Assert.Contains("sent no wave", text, StringComparison.Ordinal);
        if (!fails.Value) Assert.Contains("before its first wave was due (reported, not failed)", text, StringComparison.Ordinal);
    }

    /// <summary>The named list holds the one measured case (one seed of nine, <c>Content/HolyWarFirstWaveReportTests</c>) and nothing else.</summary>
    [Fact]
    public void DefeatedBeforeDue_HoldsOnlyTheMeasuredCase()
    {
        // M6-SH2: the measured case is gone (Content/HolyWarFirstWaveReportTests: 0 of 9 runs since the sim's drop-off rule and
        // retrain; the row asked for the entry to be taken out), so the list holds nothing. Was { (0, 2, 3, 1) }.
        Assert.Empty(AiOpeningPairsReportTests.DefeatedBeforeDue);
        Assert.Equal(3UL, AiOpeningSeeds.SmallestPair(0, 2));
    }

    /// <summary>
    /// The real scene behind the (0, 2) row, cut at 5:00 (6,000 ticks): the Whirlwind <c>holy_war</c> seat is alive, has a
    /// finished building that trains its army, and has sent no wave, so the verdict at that bound fails exactly as the row's
    /// old rule did, even for the named side: the re-statement reports only a side already defeated.
    /// </summary>
    [Fact]
    public void AnAliveSeatWithItsProducers_AndNoWave_StillFails()
    {
        ulong seed = AiOpeningSeeds.SmallestPair(0, 2);
        Simulation sim = AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seed);
        Assert.Equal((0, 2), (sim.AiOpening(0), sim.AiOpening(1)));
        int due = -1;
        AiPlayer ai = sim.AiOf(1)!;
        while (sim.TickNumber < 6000)
        {
            sim.Tick();
            if (due < 0 && sim.TickNumber % ai.ThinkTicks == 0 && AiOpeningPairsReportTests.WaveDue(sim, ai)) due = sim.TickNumber;
        }
        World w = sim.World;
        Assert.False(w.IsDefeated(1));
        Assert.Equal(0, ai.Board.WavesStarted);
        bool producer = false;
        var b = w.Buildings;
        for (int s = 0; s < b.Capacity && !producer; s++)
        {
            if (!b.Alive[s] || b.Owner[s] != 1 || b.UnderConstruction[s]) continue;
            foreach (UnitDef u in w.Data.Units)
                if (u.TrainedAtTypeId == b.TypeId[s] && u.Slot != UnitSlot.Worker) producer = true;
        }
        Assert.True(producer, "the holy_war seat has no finished army producer at 5:00");
        string? text = AiOpeningPairsReportTests.NoWave("whirlwind", ai.Opening.Id, seed, ai.Board.WavesStarted, -1, due, named: true, out bool fails);
        Assert.True(fails, text);
        Assert.Equal($"whirlwind (holy_war) sent no wave in 20 minutes on seed {seed}", text);
    }
}
