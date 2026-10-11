using System.Diagnostics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-1 criterion 5: what reading a <see cref="PlayerView"/> costs. Eight players share 2,500 units (25 of them stealthed) and
/// 64 buildings on a generated 128 map with forests and mines. The gated row: each player's units in its own band of the map
/// (a match's territories, armies touching at the borders), every view's unit cursor walked start to end each tick in under
/// 0.3 ms for all eight. Reported beside it: the whole read (buildings, ghosts, resource nodes, queues, every cell), and the
/// worst case, every player's units interleaved over the whole map so each view answers nearly all 2,500. Reading allocates
/// nothing; a view holds no per-player buffer, so building one allocates the object alone.
/// </summary>
[Collection(SerialCollection.Name)]
public class PlayerViewPerfTests
{
    private const int Players = 8, Units = 2500, BuildingsEach = 8;
    private const double BudgetMs = 0.3;
    private readonly ITestOutputHelper _out;

    public PlayerViewPerfTests(ITestOutputHelper output) => _out = output;

    /// <param name="bands">True: player p's units fill the p-th of eight bands of open ground (row order); false: interleaved over the whole map.</param>
    private static Simulation Scene(bool bands)
    {
        GameData d = StealthTests.Data;
        var map = MapGenParams.Default with { Forests = 6, GoldMines = 4 };
        var sim = new Simulation(new SimConfig(3, Players, Units, 64) { Data = d, Map = map, Combat = false });
        NavGrid g = sim.World.NavGrid;
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        int stride = Math.Max(1, open.Count / Units);
        for (int k = 0; k < Units; k++)
        {
            int c = open[k * stride % open.Count];
            int owner = bands ? k / (Units / Players) : k % Players;
            FactionDef f = d.Factions[owner % d.Factions.Length];
            EntityHandle h = CombatScenes.Place(sim, owner, f.Units[k / Players % f.Units.Length], g.CellCenter(c % g.Width, c / g.Width));
            if (k % 100 == 0) StealthTests.Stealth(sim, h);
        }
        // Buildings on open 2 x 2 ground across the map, eight a player.
        int placed = 0;
        for (int k = 0; k < open.Count && placed < Players * BuildingsEach; k += 97)
        {
            int owner = placed % Players;
            int type = OfSlot(d, owner % d.Factions.Length, BuildingSlot.House);
            if (sim.World.Buildings.Spawn(owner, type, open[k], out _, false)) placed++;
        }
        Assert.Equal(Players * BuildingsEach, placed);
        for (int t = 0; t < 8; t++) sim.Tick(); // two fog updates: every player sees round its units
        return sim;
    }

    private static int OfSlot(GameData d, int faction, BuildingSlot slot)
    {
        foreach (BuildingDef b in d.Buildings)
            if (b.Faction == faction && b.Slot == slot) return b.Id;
        throw new InvalidOperationException($"no {slot}");
    }

    private static PlayerView[] Views(World w)
    {
        var views = new PlayerView[Players];
        for (int p = 0; p < Players; p++) views[p] = new PlayerView(w, p);
        return views;
    }

    [Fact]
    public void ReadingEveryPlayersView_AllocatesNothing_AndAViewHoldsNoBuffer()
    {
        Simulation sim = Scene(bands: false);
        World w = sim.World;
        Assert.Equal(Units, w.Units.Count);
        Assert.True(w.Units.Statuses.StealthedUnits > 0);
        PlayerView[] views = Views(w);
        long sum = 0;
        Action read = () =>
        {
            foreach (PlayerView v in views) sum += PlayerViewReader.ReadAll(v, cells: true);
        };
        AllocationProbe.AssertZero(read, _out);
        Action build = () => views[0] = new PlayerView(w, 0);
        build();
        long bytes = AllocationProbe.Measure(build);
        _out.WriteLine($"reading 8 views: 0 B; building one: {bytes} B; checksum {sum}");
        // The object alone (header, eight fields): no per-player array.
        Assert.True(bytes <= 96, $"building a PlayerView allocated {bytes} B: more than the object");
    }

    /// <summary>Every unit cursor of the eight views, start to end: the answers an AI's scan of the map takes.</summary>
    private static long EnumerateUnits(PlayerView[] views)
    {
        long n = 0;
        foreach (PlayerView v in views)
        {
            int s = -1;
            while (v.NextUnit(ref s, out SeenUnit u)) n += u.Hp;
        }
        return n;
    }

    /// <summary>
    /// The brief's row: eight players' views enumerate their units over 2,500 units in under 0.3 ms a tick (average over 60
    /// ticks), players in their own bands. The whole read and the interleaved worst case are measured and reported.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void EightPlayersViews_Over2500Units_EnumerateUnder03MsATick()
    {
        (double avg, double worst, long answers, double whole, double cells, string scene) = Measure(Scene(bands: true));
        (double mixed, _, long mixedAnswers, double mixedWhole, _, _) = Measure(Scene(bands: false));
        _out.WriteLine($"{scene}; players in bands: units enumerated avg {avg:F3} ms a tick, worst {worst:F3} ms ({answers} answers), the whole read {whole:F3} ms, " +
                       $"with every cell {cells:F3} ms. Interleaved (reported): units {mixed:F3} ms ({mixedAnswers} answers), the whole read {mixedWhole:F3} ms");
        Assert.True(avg < BudgetMs, $"avg {avg:F3} ms over the {BudgetMs} ms budget");
    }

    private static (double Avg, double Worst, long Answers, double WholeMs, double CellsMs, string Scene) Measure(Simulation sim)
    {
        World w = sim.World;
        PlayerView[] views = Views(w);
        const int ticks = 60;
        var times = new double[ticks];
        long sum = EnumerateUnits(views), answers = 0; // warm (JIT)
        double wholeMs = 0, cellsMs = 0;
        for (int p = 0; p < Players; p++) sum += PlayerViewReader.ReadAll(views[p], cells: true);
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            long start = Stopwatch.GetTimestamp();
            sum += EnumerateUnits(views);
            times[t] = Ms(start);
            start = Stopwatch.GetTimestamp();
            for (int p = 0; p < Players; p++) sum += PlayerViewReader.ReadAll(views[p], cells: false);
            wholeMs += Ms(start);
            start = Stopwatch.GetTimestamp();
            for (int p = 0; p < Players; p++) sum += PlayerViewReader.ReadAll(views[p], cells: true);
            cellsMs += Ms(start);
        }
        foreach (PlayerView v in views)
        {
            int s = -1;
            while (v.NextUnit(ref s, out _)) answers++;
        }
        Assert.NotEqual(0, sum);
        string scene = $"8 views over {w.Units.Count} units ({w.Units.Statuses.StealthedUnits} stealthed), {w.Buildings.Count} buildings, {w.Resources.Count} resource nodes";
        return (times.Average(), times.Max(), answers, wholeMs / ticks, cellsMs / ticks, scene);
    }

    private static double Ms(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
}
