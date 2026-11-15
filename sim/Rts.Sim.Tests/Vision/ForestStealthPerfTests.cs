using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-1 criterion 6: what the forest stealth costs. A 500-unit brawl with 250 forest-stealth units standing among trees
/// allocates nothing a tick (and is timed against the brawl rows' 4 ms); a match whose seats have no forest stealth does no
/// per-unit work for the rule (one early return).
/// </summary>
[Collection(SerialCollection.Name)]
public class ForestStealthPerfTests
{
    private const double BudgetMs = 4.0; // the brawl rows' budget (CombatPerfTests, StealthPerfTests)
    private readonly ITestOutputHelper _out;

    public ForestStealthPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// 250 Malazan Heavy Infantry on the odd cells of a 20 x 50 cell block whose even cells are trees (every unit has four
    /// diagonal trees), against 240 Raiders and 10 Priests (10 m detectors, in the back rank) in a block 8 m east; every unit
    /// attack-moves into the other block.
    /// </summary>
    internal static Simulation ForestBrawl()
    {
        const int size = 128;
        GameData d = ForestScenes.Data3;
        var sim = new Simulation(new SimConfig(1, 2, 520, 4096) { Data = d }, LocalMovementTests.Flat(size));
        int heavy = d.FindUnit("malazan_heavy_infantry"), raider = d.FindUnit("whirlwind_raider"), priest = d.FindUnit("whirlwind_priest");
        const int x0 = 30, y0 = 40, columns = 10, rows = 25;
        for (int x = x0; x <= x0 + 2 * columns; x += 2)
            for (int y = y0; y <= y0 + 2 * rows; y += 2)
                ForestScenes.Tree(sim, x, y);
        for (int k = 0; k < columns * rows; k++)
            CombatScenes.Place(sim, 0, heavy, sim.World.NavGrid.CellCenter(x0 + 1 + 2 * (k % columns), y0 + 1 + 2 * (k / columns)));
        float east = (x0 + 2 * columns + 1) * 2f + 8f;
        for (int k = 0; k < 250; k++)
        {
            int col = k % 10, row = k / 10;
            int type = col == 9 && row % 2 == 0 && row < 20 ? priest : raider;
            CombatScenes.Place(sim, 1, type, new Vector2(east + col * 1f, y0 * 2f + 2f + row * 4f));
        }
        UnitStore u = sim.World.Units;
        Vector2 west = sim.World.NavGrid.CellCenter(x0 + columns, y0 + rows), eastMid = new(east + 5f, y0 * 2f + 50f);
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), u.Owner[i] == 0 ? eastMid : west));
        return sim;
    }

    [Fact]
    public void TheForestBrawl_Has500Units_250OfTheFactionAmongTrees_AllHeldAtTheStart()
    {
        Simulation sim = ForestBrawl();
        Assert.Equal(500, sim.World.Units.Count);
        sim.Tick();
        int held = 0;
        for (int i = 0; i < sim.World.Units.Capacity; i++)
            if (sim.World.Units.Alive[i] && sim.World.Units.Forest!.Held[i] != 0) held++;
        Assert.Equal(250, held);
    }

    /// <summary>The brawl's ticks, with units held, revealed by their hits, let go by hp loss and dying, allocate nothing.</summary>
    [Fact]
    public void ForestBrawlTicks_AllocateNothing()
    {
        Simulation sim = ForestBrawl();
        World w = sim.World;
        for (int t = 0; t < 40; t++) sim.Tick();
        int held = 0, letGo = 0;
        var heldBefore = new byte[w.Units.Capacity];
        Action ticks = () =>
        {
            for (int t = 0; t < 100; t++)
            {
                Array.Copy(w.Units.Forest!.Held, heldBefore, heldBefore.Length);
                sim.Tick();
                sim.StateHash();
                for (int j = 0; j < w.Units.Capacity; j++)
                {
                    if (w.Units.Forest!.Held[j] != 0) held++;
                    else if (heldBefore[j] != 0) letGo++;
                }
            }
        };
        AllocationProbe.AssertZero(ticks, _out);
        _out.WriteLine($"{held} held and {letGo} let-go unit-ticks, {w.Units.Statuses.StealthedUnits} carriers left, {w.Units.Count} units");
        Assert.True(held > 0 && letGo > 0, $"{held} held, {letGo} let go: not a forest row");
    }

    /// <summary>The forest brawl: ticks 5-205 average under the brawl rows' 4 ms; ticks 205-605 reported.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void ForestBrawl500_First200Ticks_AverageUnder4Ms()
    {
        Simulation sim = ForestBrawl();
        World w = sim.World;
        for (int t = 0; t < 5; t++) sim.Tick();
        var first = new double[200];
        var later = new double[400];
        long freq = Stopwatch.Frequency;
        for (int t = 0; t < first.Length + later.Length; t++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / freq;
            if (t < first.Length) first[t] = ms; else later[t - first.Length] = ms;
        }
        double avg = first.Average();
        _out.WriteLine($"500-unit brawl with 250 forest-stealth units among trees, ticks 5-205: avg {avg:F3} ms, worst {first.Max():F3} ms; ticks 205-605: avg {later.Average():F3} ms, "
            + $"worst {later.Max():F3} ms; {w.Units.Statuses.StealthedUnits} carriers left, {w.Kills[0] + w.Kills[1]} killed");
        Assert.True(avg < BudgetMs, $"avg {avg:F3} ms over the {BudgetMs} ms budget");
    }

    /// <summary>Data without the block, 2,500 units: the forest pass is one early return (10,000 passes well under a millisecond), nothing allocated, nothing changed.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void NoForestStealthSeat_ThePassIsOneEarlyReturn()
    {
        var sim = new Simulation(TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 2500, CommandCapacity: 64), LocalMovementTests.Flat(128));
        for (int k = 0; k < 2500; k++)
            CombatScenes.Place(sim, k % 2, k % 2 == 0 ? CombatScenes.Raider : CombatScenes.HeavyInfantry, CombatScenes.At(sim, 4 + k % 50 * 2, 4 + k / 50 * 2));
        sim.Tick();
        World w = sim.World;
        Assert.False(w.AnyForestStealth);
        ulong h = sim.StateHash();
        Action pass = () => { for (int k = 0; k < 10_000; k++) ForestStealthSystem.Run(w); };
        AllocationProbe.AssertZero(pass, _out);
        var sw = Stopwatch.StartNew();
        pass();
        double ms = sw.Elapsed.TotalMilliseconds;
        _out.WriteLine($"10,000 forest passes over 2,500 slots with no forest seat: {ms:F3} ms ({ms * 100:F3} ns a pass)");
        Assert.Equal(h, sim.StateHash());
        Assert.True(ms < 1.0, $"{ms:F3} ms for 10,000 passes: the pass visits slots without a forest seat");
    }
}
