using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-3b criterion 5: what towers cost a tick (Debug). 20 Watchtowers among 500 enemy Raiders, against a twin where the 20
/// buildings are Billets (same footprint, no attack): the difference is the towers' scans, shots, flights and hits.
/// </summary>
[Collection(SerialCollection.Name)]
public class TowerPerfTests
{
    private const double BudgetMs = 0.2;
    private const int Towers = 20;
    private const int Enemies = 500;
    private readonly ITestOutputHelper _out;

    public TowerPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// A flat 128 map: 20 buildings of player 0 (Watchtowers, or Billets) on a 5 x 4 grid 12 m apart, and 500 holding Raiders
    /// of player 1 on a 2 m grid over the same ground (none in a footprint), so every tower has targets in range the whole
    /// time; 5 ticks run.
    /// </summary>
    public static Simulation Scene(bool towers)
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: Enemies, CommandCapacity: 2 * Enemies + 32) with { ProjectileCapacity = 64 },
            LocalMovementTests.Flat(128));
        int type = towers ? TowerTests.Watchtower : BuildMaps.House;
        var footprints = new List<(Vector2 Min, Vector2 Max)>();
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 4; j++)
            {
                int x = 26 + 6 * i, y = 26 + 6 * j;
                TowerTests.PlaceBuilding(sim, 0, type, x, y);
                footprints.Add((new Vector2(x * 2f, y * 2f), new Vector2(x * 2f + 4f, y * 2f + 4f)));
            }
        int placed = 0;
        for (float y = 41f; y < 120f && placed < Enemies; y += 2f)
            for (float x = 41f; x < 120f && placed < Enemies; x += 2f)
            {
                var p = new Vector2(x, y);
                if (footprints.Any(f => p.X > f.Min.X - 1f && p.X < f.Max.X + 1f && p.Y > f.Min.Y - 1f && p.Y < f.Max.Y + 1f)) continue;
                EntityHandle h = CombatScenes.Place(sim, 1, CombatScenes.Raider, p);
                sim.Enqueue(Command.HoldPosition(1, h));
                placed++;
            }
        Assert.Equal(Enemies, placed);
        for (int t = 0; t < 5; t++) sim.Tick();
        return sim;
    }

    /// <summary>The summed hit points of <paramref name="player"/>'s live units.</summary>
    private static long HpOf(World w, int player)
    {
        long hp = 0;
        for (int i = 0; i < w.Units.Capacity; i++) if (w.Units.Alive[i] && w.Units.Owner[i] == player) hp += w.Units.Hp[i];
        return hp;
    }

    private static double AverageTickMs(Simulation sim, int ticks)
    {
        long freq = Stopwatch.Frequency, total = 0;
        for (int t = 0; t < ticks; t++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            total += Stopwatch.GetTimestamp() - start;
        }
        return total * 1000.0 / freq / ticks;
    }

    /// <summary>
    /// The towers' cost: three alternating pairs of 300-tick runs (towers, then Billets), the smallest difference of the
    /// pair averages, at most 0.2 ms a tick; the towers fired throughout.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void TwentyTowersAmong500Enemies_CostAtMost02MsATick()
    {
        var diffs = new List<double>();
        string line = "";
        for (int run = 0; run < 3; run++)
        {
            Simulation withTowers = Scene(towers: true);
            Simulation plain = Scene(towers: false);
            long hp0 = HpOf(withTowers.World, 1);
            double a = AverageTickMs(withTowers, 300);
            double b = AverageTickMs(plain, 300);
            long dealt = hp0 - HpOf(withTowers.World, 1);
            diffs.Add(a - b);
            line += $"run {run}: towers {a:F3} ms, billets {b:F3} ms, difference {a - b:F3} ms ({dealt} hp dealt, {withTowers.World.Kills[0]} kills); ";
            // 20 towers, a shot each every 40 ticks (7 or 8 started in 300 ticks, 6 landed in the window measured: 600 hp), 5 hp
            // a hit on a Raider: every tower fires throughout.
            Assert.True(dealt >= 20 * 5 * 5, $"the towers dealt only {dealt} hp");
            Assert.Equal(HpOf(plain.World, 1), (long)Enemies * plain.World.Data.Units[CombatScenes.Raider].Hp);
        }
        _out.WriteLine(line);
        double best = diffs.Min();
        Assert.True(best <= BudgetMs, $"towers cost {best:F3} ms a tick, over the {BudgetMs} ms budget: {line}");
    }

    /// <summary>The tower scan, wind-up, shot and hit allocate nothing a tick (the scene above, towers firing).</summary>
    [Fact]
    public void TowerTicks_AllocateNothing()
    {
        Simulation sim = Scene(towers: true);
        for (int t = 0; t < 40; t++) sim.Tick(); // past the first scans and shots
        AllocationProbe.AssertZero(() => sim.Tick());
        AllocationProbe.AssertZero(() =>
        {
            for (int t = 0; t < 45; t++) sim.Tick(); // a whole cooldown: every tower scans, winds up and fires
        });
    }
}
