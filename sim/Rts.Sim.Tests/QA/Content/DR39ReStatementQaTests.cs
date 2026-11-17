using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D-R39 (2026-11-15): attacks on the two re-statements of <see cref="AiOpeningPairsReportTests"/>' row, on real scenes
/// rather than on the verdict function alone. (1) "beaten: hall lost" must excuse a seat only when no finished town hall
/// of its stands: razing one hall while a second finished hall stands still leaves a DEAD END. (2) "defeated before its
/// first wave was due" must not excuse the named side once it is kept alive: on (0, 2) seed 3, with the Malazan army
/// removed every second and the <c>holy_war</c> army too (so it is alive, keeps its producers and never waves), the row's
/// verdict at the tick it really fell (12:29) fails.
/// </summary>
public class DR39ReStatementQaTests
{
    [Fact]
    public void RazingOneHall_WhileASecondFinishedHallStands_IsStillADeadEnd()
    {
        ulong seed = AiOpeningSeeds.SmallestPair(2, 0);
        Simulation sim = AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seed);
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
        ai.Board.StepDueTick[step] = sim.TickNumber - 20;
        w.Ledger.Gold[1] = 5000;
        w.Ledger.Wood[1] = 5000;

        var b = w.Buildings;
        int hallType = -1, firstHall = -1;
        for (int s = 0; s < b.Capacity; s++)
            if (b.Alive[s] && b.Owner[s] == 1 && w.Data.Buildings[b.TypeId[s]].Slot == BuildingSlot.TownHall) { hallType = b.TypeId[s]; firstHall = s; }
        Assert.True(firstHall >= 0);
        // A second finished hall on the first free cell scanning from the map's middle row.
        int width = w.NavGrid.Width, height = w.NavGrid.Height;
        bool placed = false;
        for (int y = height / 2; y < height - 8 && !placed; y += 3)
            for (int x = 8; x < width - 8 && !placed; x += 3)
                placed = b.Spawn(1, hallType, y * width + x, out _, false);
        Assert.True(placed, "no free cell for a second hall");

        b.Damage(b.HandleOf(firstHall), int.MaxValue);
        Assert.False(b.Alive[firstHall]);
        Assert.False(w.IsDefeated(1));
        string? why = AiOpeningPairsReportTests.WhyWaiting(sim, ai, ai.Opening.BuildOrder[step], step, -1);
        Assert.NotEqual(AiOpeningPairsReportTests.HallLost, why);
        Assert.Null(why);
        var deadEnds = new List<string>();
        AiOpeningPairsReportTests.Waiting(sim, ai, -1, deadEnds, out _);
        Assert.Contains(deadEnds, x => x.Contains($"step {step + 1} (", StringComparison.Ordinal) && x.Contains("DEAD END", StringComparison.Ordinal));
    }

    [Fact]
    public void TheNamedSide_KeptAliveWithItsProducers_AndNoWave_FailsTheRowVerdict()
    {
        const int Bound = 14_980; // 12:29, the tick the named side really fell on
        ulong seed = AiOpeningSeeds.SmallestPair(0, 2);
        Simulation sim = AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seed);
        Assert.Equal((0, 2), (sim.AiOpening(0), sim.AiOpening(1)));
        AiPlayer ai = sim.AiOf(1)!;
        World w = sim.World;
        int due = -1, defeated = -1;
        while (sim.TickNumber < Bound)
        {
            sim.Tick();
            if (sim.TickNumber % SimConstants.TicksPerSecond == 0) RemoveArmies(w);
            if (defeated < 0 && w.IsDefeated(1)) defeated = sim.TickNumber;
            if (due < 0 && defeated < 0 && sim.TickNumber % ai.ThinkTicks == 0 && AiOpeningPairsReportTests.WaveDue(sim, ai)) due = sim.TickNumber;
        }
        Assert.Equal(-1, defeated);
        Assert.Equal(0, ai.Board.WavesStarted);
        bool producer = false;
        var b = w.Buildings;
        for (int s = 0; s < b.Capacity && !producer; s++)
        {
            if (!b.Alive[s] || b.Owner[s] != 1 || b.UnderConstruction[s]) continue;
            foreach (UnitDef u in w.Data.Units)
                if (u.TrainedAtTypeId == b.TypeId[s] && u.Slot != UnitSlot.Worker) producer = true;
        }
        Assert.True(producer, "the holy_war seat has no finished army producer");
        // M6-SH2 re-staging: the side is no longer in DefeatedBeforeDue (on the M6-SH2 tree it survives and waves, so the
        // report row asked for it to be taken out); the verdict for a named side kept alive with no wave is still the subject,
        // so the row names it itself, as the next row does. The cross-check with the report's list stays, the other way round
        // (BUG-1532): the list must not name the side, so if the entry comes back this row is read again. Was
        // Assert.True(Array.IndexOf(DefeatedBeforeDue, (0, 2, seed, 1)) >= 0).
        Assert.True(Array.IndexOf(AiOpeningPairsReportTests.DefeatedBeforeDue, (0, 2, seed, 1)) < 0,
            "DefeatedBeforeDue names the side again: the row's own naming below is out of date");
        const bool named = true;
        string? text = AiOpeningPairsReportTests.NoWave("whirlwind", ai.Opening.Id, seed, ai.Board.WavesStarted, defeated, due, named, out bool fails);
        Assert.True(fails, text);
        Assert.NotNull(text);
    }

    [Fact]
    public void TheNamedSide_DefeatedAfterItsWaveWasDue_FailsTheRowVerdict()
    {
        // The measured fall is 12:29 with the wave never due; any due tick before the fall must fail, at every think.
        for (int due = 0; due < 14_980; due += 997)
        {
            AiOpeningPairsReportTests.NoWave("whirlwind", "holy_war", 3, 0, 14_980, due, named: true, out bool fails);
            Assert.True(fails, $"due at tick {due}");
        }
    }

    /// <summary>Frees every live non-worker unit of both seats (a test-only cut, so neither army can wave or kill).</summary>
    private static void RemoveArmies(World w)
    {
        UnitStore u = w.Units;
        for (int s = 0; s < u.Capacity; s++)
            if (u.Alive[s] && w.Data.Units[u.TypeId[s]].Slot != UnitSlot.Worker)
                u.Free(new EntityHandle(s, u.Generation[s]));
    }
}
