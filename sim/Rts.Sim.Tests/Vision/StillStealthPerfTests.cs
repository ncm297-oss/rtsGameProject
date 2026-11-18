using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-2 criteria 9, 11, 12 and 13: what the stand-still stealth costs and how it behaves in a crowd. A 500-unit brawl of
/// field units allocates nothing a tick; data without the field does no per-unit work for the rule; and two reports
/// (measured, not pass / fail): the tick cost of 500 moving units with and without the field in the data, and how long 12
/// field units sent to one point take to hide after the group arrives.
/// </summary>
[Collection(SerialCollection.Name)]
public class StillStealthPerfTests
{
    private readonly ITestOutputHelper _out;

    public StillStealthPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// 250 Malazan Crossbowmen and 250 Whirlwind Desert Archers, every one of a type with the field (or, on the shipped data,
    /// the same types without it), in two 10 x 25 blocks 40 m apart on a 128-cell map; every 40 ticks a fifth of each side is
    /// sent to a random point near its block or attack-moves at the other (orders from a seeded stream).
    /// </summary>
    internal static Simulation Crowd(GameData d, bool combat)
    {
        var sim = new Simulation(new SimConfig(1, 2, 520, 4096) { Data = d, Combat = combat }, LocalMovementTests.Flat(128));
        int bow = d.FindUnit("malazan_crossbowman"), archer = d.FindUnit("whirlwind_desert_archer");
        for (int k = 0; k < 250; k++)
        {
            CombatScenes.Place(sim, 0, bow, new Vector2(60f + k % 10 * 2f, 70f + k / 10 * 2f));
            CombatScenes.Place(sim, 1, archer, new Vector2(140f + k % 10 * 2f, 70f + k / 10 * 2f));
        }
        return sim;
    }

    internal static void Orders(Simulation sim, ref SimRng rng)
    {
        if (sim.TickNumber % 40 != 3) return;
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || rng.NextInt(0, 5) != 0) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            int p = u.Owner[i];
            Vector2 home = p == 0 ? new Vector2(70f, 95f) : new Vector2(150f, 95f), away = p == 0 ? new Vector2(150f, 95f) : new Vector2(70f, 95f);
            if (rng.NextInt(0, 4) == 0) sim.Enqueue(Command.AttackMove(p, h, away + new Vector2(rng.NextInt(-10, 11), rng.NextInt(-20, 21))));
            else sim.Enqueue(Command.Move(p, h, home + new Vector2(rng.NextInt(-12, 13), rng.NextInt(-25, 26))));
        }
    }

    [Fact]
    public void TheCrowd_Has500FieldUnits()
    {
        Simulation sim = Crowd(StillScenes.Still, combat: true);
        Assert.Equal(500, sim.World.Units.Count);
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) Assert.True(StillScenes.Still.Units[u.TypeId[i]].HasStillStealth);
    }

    /// <summary>Criterion 11: the crowd's ticks, with 500 field units held, moving, let go, revealed, hurt and dying, allocate nothing.</summary>
    [Fact]
    public void FiveHundredFieldUnits_Ticks_AllocateNothing()
    {
        Simulation sim = Crowd(StillScenes.Still, combat: true);
        World w = sim.World;
        var rng = new SimRng(1, 77);
        for (int t = 0; t < 120; t++)
        {
            Orders(sim, ref rng);
            sim.Tick();
        }
        int held = 0, letGo = 0;
        var heldBefore = new byte[w.Units.Capacity];
        var rngRun = rng;
        Action ticks = () =>
        {
            for (int t = 0; t < 120; t++)
            {
                Orders(sim, ref rngRun);
                Array.Copy(w.Units.Still!.Held, heldBefore, heldBefore.Length);
                sim.Tick();
                sim.StateHash();
                for (int j = 0; j < w.Units.Capacity; j++)
                {
                    if (w.Units.Still!.Held[j] != 0) held++;
                    else if (heldBefore[j] != 0) letGo++;
                }
            }
        };
        // Orders enqueue commands into the sim's preallocated queue (no allocation either).
        AllocationProbe.AssertZero(ticks, _out);
        _out.WriteLine($"{held} held and {letGo} let-go unit-ticks, {w.Units.Statuses.StealthedUnits} carriers, {w.Units.Count} units");
        Assert.True(held > 0 && letGo > 0, $"{held} held, {letGo} let go: not a stand-still row");
    }

    /// <summary>Criterion 9: data without the field, 2,500 units: the pass is one early return (10,000 passes well under a millisecond), nothing allocated or changed.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void NoFieldInTheData_ThePassIsOneEarlyReturn()
    {
        var sim = new Simulation(TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 2500, CommandCapacity: 64), LocalMovementTests.Flat(128));
        for (int k = 0; k < 2500; k++)
            CombatScenes.Place(sim, k % 2, k % 2 == 0 ? CombatScenes.Raider : CombatScenes.Crossbowman, CombatScenes.At(sim, 4 + k % 50 * 2, 4 + k / 50 * 2));
        sim.Tick();
        World w = sim.World;
        Assert.Null(w.Units.Still);
        ulong h = sim.StateHash();
        Action pass = () => { for (int k = 0; k < 10_000; k++) StillStealthSystem.Run(w); };
        AllocationProbe.AssertZero(pass, _out);
        var sw = Stopwatch.StartNew();
        pass();
        double ms = sw.Elapsed.TotalMilliseconds;
        _out.WriteLine($"10,000 stand-still passes over 2,500 slots on data without the field: {ms:F3} ms ({ms * 100:F3} ns a pass)");
        Assert.Equal(h, sim.StateHash());
        Assert.True(ms < 1.0, $"{ms:F3} ms for 10,000 passes: the pass visits slots on data without the field");
    }

    /// <summary>
    /// Criterion 12 (a report, not pass / fail; run it alone, rule 42): 500 moving units (combat off, so all 500 keep walking
    /// and stopping), with the field in the data (every unit a field type) and without (the shipped data, same types), three
    /// alternating runs of ticks 20-620 each. Prints each run's average and worst tick and the field runs' held unit-ticks.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Report_TickCostOf500MovingUnits_WithAndWithoutTheField()
    {
        var with = new List<double>();
        var without = new List<double>();
        for (int run = 0; run < 3; run++)
            foreach (bool field in new[] { true, false })
            {
                Simulation sim = Crowd(field ? StillScenes.Still : TestSim.Data, combat: false);
                var rng = new SimRng(1, 78);
                for (int t = 0; t < 20; t++)
                {
                    Orders(sim, ref rng);
                    sim.Tick();
                }
                long freq = Stopwatch.Frequency, total = 0, worst = 0;
                int held = 0;
                for (int t = 0; t < 600; t++)
                {
                    Orders(sim, ref rng);
                    long start = Stopwatch.GetTimestamp();
                    sim.Tick();
                    long took = Stopwatch.GetTimestamp() - start;
                    total += took;
                    worst = Math.Max(worst, took);
                    if (field)
                        for (int j = 0; j < sim.World.Units.Capacity; j++)
                            if (sim.World.Units.Still!.Held[j] != 0) held++;
                }
                double avg = total * 1000.0 / freq / 600, worstMs = worst * 1000.0 / freq;
                (field ? with : without).Add(avg);
                _out.WriteLine($"run {run} {(field ? "with the field   " : "without the field")}: avg {avg:F3} ms, worst {worstMs:F3} ms{(field ? $", {held} held unit-ticks" : "")}");
            }
        _out.WriteLine($"mean of runs: with {with.Average():F3} ms, without {without.Average():F3} ms ({(with.Average() / without.Average() - 1) * 100:+0.0;-0.0} %)");
        // The rule's own pass, apart from the detection pass its held units feed (M4-5's StealthSystem.Run): 1,000 passes
        // over the 500 field units at rest-and-held as the last field run left them.
        Simulation last = Crowd(StillScenes.Still, combat: false);
        for (int t = 0; t < 80; t++) last.Tick();
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < 1000; k++) StillStealthSystem.Run(last.World);
        double passUs = sw.Elapsed.TotalMilliseconds; // ms for 1,000 = us a pass
        sw.Restart();
        for (int k = 0; k < 1000; k++) StealthSystem.Run(last.World);
        double detectUs = sw.Elapsed.TotalMilliseconds;
        _out.WriteLine($"the stand-still pass alone over 500 held field units: {passUs:F1} us a pass; the M4-5 detection pass over the same {last.World.Units.Statuses.StealthedUnits} carriers: {detectUs:F1} us a pass");
    }

    /// <summary>
    /// Criterion 13 (a report, not pass / fail): 12 field units (Crossbowmen, from a 4 x 3 block) ordered to one point 30 m
    /// away. The group's arrival is the first tick, after they set off, none of them is Moving; for each unit, the ticks from then until it is
    /// hidden, the tick its position last changed, and how many ticks after the arrival its position still changed (the
    /// separation nudges). Flags any unit still visible 10 s (200 ticks) after the arrival.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Report_TwelveFieldUnitsSentToOnePoint_TicksFromArrivalUntilHidden()
    {
        Simulation sim = StillScenes.Scene(StillScenes.Still, combat: false, size: 64);
        World w = sim.World;
        UnitStore u = w.Units;
        var hs = new EntityHandle[12];
        for (int k = 0; k < hs.Length; k++) hs[k] = CombatScenes.Place(sim, 0, StillScenes.Still.FindUnit("malazan_crossbowman"), new Vector2(30f + k % 4 * 1.5f, 40f + k / 4 * 1.5f));
        sim.Tick();
        foreach (EntityHandle h in hs) sim.Enqueue(Command.Move(0, h, new Vector2(60f, 42f)));
        int arrival = -1;
        bool started = false;
        var hiddenAt = Enumerable.Repeat(-1, hs.Length).ToArray();
        var lastChange = new int[hs.Length];
        var changesAfter = new int[hs.Length];
        var last = hs.Select(h => u.Position[h.Index]).ToArray();
        for (int t = 0; t < 3000 && (arrival < 0 || hiddenAt.Contains(-1)); t++)
        {
            sim.Tick();
            int now = sim.TickNumber - 1;
            bool anyMoving = hs.Any(h => u.State[h.Index] == UnitState.Moving);
            if (anyMoving) started = true;
            if (arrival < 0 && started && !anyMoving) arrival = now;
            for (int k = 0; k < hs.Length; k++)
            {
                int i = hs[k].Index;
                if (u.Position[i] != last[k])
                {
                    last[k] = u.Position[i];
                    lastChange[k] = now;
                    if (arrival >= 0) changesAfter[k]++;
                }
                if (hiddenAt[k] < 0 && w.IsStealthed(i)) hiddenAt[k] = now;
            }
        }
        Assert.True(arrival >= 0, "the group never arrived");
        _out.WriteLine($"group arrival (no unit Moving): tick {arrival}");
        int late = 0, worst = 0;
        for (int k = 0; k < hs.Length; k++)
        {
            int after = hiddenAt[k] < 0 ? -1 : hiddenAt[k] - arrival;
            worst = Math.Max(worst, after);
            if (after < 0 || after > 200) late++;
            _out.WriteLine($"unit {k,2}: hidden {(hiddenAt[k] < 0 ? "never" : $"tick {hiddenAt[k]}")}, {after} ticks after the arrival ({after / 20.0:F2} s); position last changed tick {lastChange[k]}, changed in {changesAfter[k]} ticks after the arrival");
        }
        _out.WriteLine($"worst {worst} ticks ({worst / 20.0:F2} s); units visible past 10 s after the arrival: {late}");
    }
}
