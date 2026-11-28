using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Tests.QA;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Draw;

/// <summary>
/// M6-SH3 B criteria 3 and 4: the exact "can still train" only narrows M6-S13's rule (every state the old rule called spent
/// is still spent: "new can train" implies "old can train", seat by seat, over fuzzed and AI states, both read by scanning
/// the stores, <see cref="DrawReachScan"/>), the sim's counter-based answer equals the scan on every state, and a save made
/// mid-clock on a state that uses the new counts (wood carried, a drop-off site, an item waiting at the cap) loads to the same
/// hash, clock and counts and draws on the same tick; the same scene run twice draws alike (the twin).
/// M6-S15 (BUG-1893, criterion 2): the drop-off clause only widens M6-SH3's rule ("M6-SH3 can" implies "now can"), every
/// state where the two differ is one where the clause's own condition holds (scanned, <see cref="DrawReachScan.DropOffClause"/>),
/// and the sim's answer is still the scan's. The row "new can implies M6-S13 can" (M6-SH3's "M6-S13-spent implies spent")
/// still holds with the clause, so it is kept as it was: the clause counts the carried and map gold only with a live worker,
/// which M6-S13's rule always counted, and only after taking a drop-off price (at least 0) off the bank.
/// </summary>
public class DrawReachPropertyTests
{
    private readonly ITestOutputHelper _out;

    public DrawReachPropertyTests(ITestOutputHelper output) => _out = output;

    /// <summary>The seat-states (over every Check) where M6-SH3's rule said "cannot" and the drop-off clause said "can" (M6-S15).</summary>
    private int _widened;

    /// <summary>
    /// Checks every undefeated seat: new implies M6-S13's old; M6-SH3's implies new, and new without M6-SH3's implies the
    /// clause (M6-S15); the sim's answer is the scan's. Counts the states where the old rule said "can" and the new one does not.
    /// </summary>
    private int Check(Simulation sim, string where)
    {
        World w = sim.World;
        int narrowed = 0;
        for (int p = 0; p < w.Config.PlayerCount; p++)
        {
            if (w.IsDefeated(p)) continue;
            bool now = DrawReachScan.NewCan(w, p), old = DrawReachScan.OldCan(w, p), sh3 = DrawReachScan.Sh3Can(w, p);
            Assert.True(!now || old, $"{where} p{p}: the new rule can train where M6-S13's could not: {DrawReachScan.Line(w, p)}");
            Assert.True(!sh3 || now, $"{where} p{p}: M6-SH3's rule can train where the new one cannot: {DrawReachScan.Line(w, p)}");
            if (now && !sh3)
            {
                Assert.True(DrawReachScan.DropOffClause(w, p), $"{where} p{p}: the new rule differs from M6-SH3's without the clause: {DrawReachScan.Line(w, p)}");
                _widened++;
            }
            Assert.True(w.CanStillTrain(p) == now, $"{where} p{p}: World.CanStillTrain {w.CanStillTrain(p)}, the scan {now}: {DrawReachScan.Line(w, p)}");
            if (old && !now) narrowed++;
        }
        return narrowed;
    }

    public static TheoryData<int> Batches()
    {
        var rows = new TheoryData<int>();
        for (int b = 0; b < 6; b++) rows.Add(b);
        return rows;
    }

    /// <summary>
    /// M6-S15: batches 4 and 5 give about half the seats the price of their faction's cheapest drop-off in wood on top of the
    /// fuzz's bank (a separate stream, so the fuzz's own draws are unchanged), so the drop-off clause is reached; batches 0-3
    /// are M6-SH3's states unchanged.
    /// </summary>
    private static void AddDropOffWood(Simulation sim, ulong seed)
    {
        World w = sim.World;
        var rng = new SimRng(seed, 1893);
        for (int p = 0; p < w.Config.PlayerCount; p++)
        {
            if (rng.NextInt(0, 2) != 0) continue;
            int best = int.MaxValue;
            foreach (BuildingDef b in w.Data.Buildings)
                if (b.DropOff && b.Faction == w.FactionOf(p)) best = Math.Min(best, b.CostWood);
            w.Ledger.Wood[p] += best;
        }
    }

    /// <summary>200 seeds of QA's draw fuzz (2-4 seats, halls, soldiers, workers, sites, nodes, banks near the cheapest unit; trains, cancels, gathers, research, kills), every tick for 600 ticks.</summary>
    [Theory]
    [MemberData(nameof(Batches))]
    public void OldRuleSpent_ImpliesNewRuleSpent_OverTheFuzzedStates(int batch)
    {
        int states = 0, narrowed = 0, oldSpent = 0, newSpent = 0;
        for (int k = 0; k < 50; k++)
        {
            ulong seed = (ulong)(batch * 50 + k + 1);
            Simulation sim = DrawRuleQaTests.Scene(seed, DrawScenes.Short, out SimRng rng);
            if (batch >= 4) AddDropOffWood(sim, seed);
            for (int t = 0; t < 600 && !sim.MatchOver; t++)
            {
                if (rng.NextInt(0, 3) == 0) DrawRuleQaTests.Disturb(sim, ref rng);
                sim.Tick();
                narrowed += Check(sim, $"seed {seed} tick {sim.TickNumber}");
                states++;
                bool anyOld = false, anyNew = false;
                for (int p = 0; p < sim.World.Config.PlayerCount; p++)
                {
                    if (sim.World.IsDefeated(p)) continue;
                    anyOld |= DrawReachScan.OldCan(sim.World, p);
                    anyNew |= DrawReachScan.NewCan(sim.World, p);
                }
                if (!anyOld) oldSpent++;
                if (!anyNew) newSpent++;
            }
        }
        _out.WriteLine($"batch {batch}: {states} states; no seat can train: old rule {oldSpent}, new rule {newSpent}; seat-states the new rule narrowed {narrowed}; seat-states the drop-off clause widened (M6-S15) {_widened}");
        Assert.True(newSpent >= oldSpent);
        Assert.True(narrowed > 0, "the fuzz never reached a state the new rule reads differently");
        if (batch >= 4) Assert.True(_widened > 0, "the fuzz never reached the drop-off clause");
    }

    /// <summary>The same over two AI matches (two Normal seats; the shipped data and the starved map), every 25th tick to 12,000.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OldRuleSpent_ImpliesNewRuleSpent_ThroughAnAiMatch(bool starved)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Generated(seats, 3, false, out _, data: starved ? DrawScenes.Starved : null);
        int checks = 0, narrowed = 0;
        while (sim.TickNumber < 12_000 && !sim.MatchOver)
        {
            sim.Tick();
            if (sim.TickNumber % 25 != 0) continue;
            narrowed += Check(sim, $"tick {sim.TickNumber}");
            string? fail = DrawRuleQaTests.CountersAgree(sim.World);
            Assert.True(fail == null, $"tick {sim.TickNumber}: {fail}");
            checks++;
        }
        _out.WriteLine($"{(starved ? "starved" : "shipped")}: {checks} checks to {sim.TickNumber}, over {sim.MatchOver} drawn {sim.World.MatchDrawn}; narrowed {narrowed}; widened by the drop-off clause {_widened}");
    }

    /// <summary>
    /// A spent two-seat world that uses every new count: seat 0 an Infantry Hall, a worker carrying wood, a drop-off site, gold on
    /// the map and a paid item waiting at the cap; seat 1 spent.
    /// </summary>
    internal static Simulation NewCountsScene()
    {
        Simulation sim = DrawScenes.Empty(DrawScenes.Short, 2);
        World w = sim.World;
        EntityHandle hall = DrawScenes.Building(sim, 0, 8, 8);
        DrawScenes.Building(sim, 1, 44, 44);
        DrawScenes.Unit(sim, 1, 44, 50);
        DrawScenes.GoldNode(sim, 30, 30, 500);
        DrawScenes.Building(sim, 0, 20, 8, BuildingSlot.TownHall, site: true);
        EntityHandle worker = DrawScenes.Unit(sim, 0, 20, 20, UnitSlot.Worker);
        w.Units.CargoKind[worker.Index] = ResourceKind.Wood;
        w.Units.Cargo[worker.Index] = 7;
        int type = w.Data.UnitsTrainedAt(w.Buildings.TypeId[hall.Index])[0];
        while (w.HalfPopCap[0] - w.HalfPop[0] >= w.Data.Units[type].ItemHalfPop) DrawScenes.Unit(sim, 0, 2 + w.HalfPop[0] % 20, 30);
        w.Buildings.Enqueue(hall.Index, type);
        DrawScenes.Broke(sim);
        w.RecountDraw();
        Assert.True(w.MatchSpent, "the scene is spent");
        return sim;
    }

    [Fact]
    public void ASaveMidClock_OnTheNewCounts_LoadsToTheSameHashClockAndCounts_AndDrawsOnTheSameTick()
    {
        Simulation sim = NewCountsScene();
        for (int t = 0; t < 40; t++) sim.Tick();
        Assert.Equal(40, sim.World.DrawClock);
        Simulation loaded = SaveTestRun.Load(DrawScenes.Short, SaveTestRun.Save(sim));
        World a = sim.World, b = loaded.World;
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        Assert.Equal(a.DrawClock, b.DrawClock);
        Assert.Null(DrawRuleQaTests.CountersAgree(b));
        for (int p = 0; p < 2; p++)
        {
            Assert.Equal(a.Ledger.CarriedWood[p], b.Ledger.CarriedWood[p]);
            Assert.Equal(a.Ledger.FinishedDropOffs[p], b.Ledger.FinishedDropOffs[p]);
            Assert.Equal(a.Ledger.TrainingUnits[p], b.Ledger.TrainingUnits[p]);
            Assert.Equal(a.CanStillTrain(p), b.CanStillTrain(p));
        }
        Assert.Equal(a.Resources.WoodLeft, b.Resources.WoodLeft);
        while (!sim.MatchOver)
        {
            sim.Tick();
            loaded.Tick();
            Assert.Equal(sim.StateHash(), loaded.StateHash());
        }
        Assert.True(a.MatchDrawn && b.MatchDrawn);
        Assert.Equal(sim.TickNumber, loaded.TickNumber);
        _out.WriteLine($"drawn at {sim.TickNumber} in both");
    }

    [Fact]
    public void TheNewCountsScene_RunTwice_DrawsOnTheSameTickWithTheSameFold()
    {
        string Play()
        {
            Simulation sim = NewCountsScene();
            var fold = new StateHasher();
            while (!sim.MatchOver && sim.TickNumber < 10 * DrawScenes.ShortTicks)
            {
                sim.Tick();
                fold.Add(sim.StateHash());
            }
            Assert.True(sim.World.MatchDrawn);
            return $"{sim.TickNumber} {sim.StateHash():X16} {fold.Value:X16}";
        }
        string first = Play(), second = Play();
        _out.WriteLine(first);
        Assert.Equal(first, second);
    }
}
