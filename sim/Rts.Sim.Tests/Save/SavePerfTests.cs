using System.Diagnostics;
using Rts.Sim.Commands;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S2 criterion 7: the menu's match (128 map, 2 seats) at tick 12,000 saves in at most 100 ms, loads in at most 100 ms,
/// and its file is at most 8 MB; the 4-seat 256 map's size and times are printed (no gate). The clock is the test's, never
/// the sim's (rule 2). Each time is the best of three after a warm-up, so the JIT and a one-off GC don't count.
/// </summary>
[Collection(SerialCollection.Name)]
public class SavePerfTests
{
    private readonly ITestOutputHelper _out;

    public SavePerfTests(ITestOutputHelper output) => _out = output;

    private const int MatchTicks = 12_000;

    private static Simulation Run(SaveScene scene, int ticks)
    {
        Simulation sim = scene.Build();
        var commands = new List<Command>();
        while (sim.TickNumber < ticks)
        {
            commands.Clear();
            scene.Script(sim, commands);
            foreach (Command c in commands) sim.Enqueue(c);
            sim.Tick();
        }
        return sim;
    }

    private (double SaveMs, double LoadMs, long Bytes) Measure(Simulation sim)
    {
        byte[] bytes = SaveTestRun.Save(sim);
        SaveTestRun.Load(sim.World.Data, bytes); // warm-up
        double save = double.MaxValue, load = double.MaxValue;
        for (int k = 0; k < 3; k++)
        {
            var sw = Stopwatch.StartNew();
            bytes = SaveTestRun.Save(sim);
            save = Math.Min(save, sw.Elapsed.TotalMilliseconds);
            sw.Restart();
            Simulation loaded = SaveTestRun.Load(sim.World.Data, bytes);
            load = Math.Min(load, sw.Elapsed.TotalMilliseconds);
            Assert.Equal(sim.StateHash(), loaded.StateHash());
        }
        return (save, load, bytes.Length);
    }

    [Trait("Category", "Perf")]
    [Fact]
    public void MenuMatch_AtTick12000_SavesAndLoadsUnder100Ms_InUnder8MB()
    {
        Simulation sim = Run(SaveScenes.Menu(1), MatchTicks);
        (double save, double load, long bytes) = Measure(sim);
        _out.WriteLine($"menu match at tick {sim.TickNumber}: {sim.World.Units.Count} units, {sim.World.FlowFields.Count} flow fields; " +
            $"save {save:F1} ms, load {load:F1} ms, {bytes:N0} bytes");
        Assert.True(save <= 100, $"save took {save:F1} ms");
        Assert.True(load <= 100, $"load took {load:F1} ms");
        Assert.True(bytes <= 8L * 1024 * 1024, $"{bytes:N0} bytes");
    }

    [Trait("Category", "Perf")]
    [Fact]
    public void FourSeats256_AtTick12000_SizeAndTimes_Printed()
    {
        Simulation sim = Run(SaveScenes.FourSeats256(), MatchTicks);
        (double save, double load, long bytes) = Measure(sim);
        _out.WriteLine($"4-seat 256 map at tick {sim.TickNumber}: {sim.World.Units.Count} units, {sim.World.FlowFields.Count} flow fields; " +
            $"save {save:F1} ms, load {load:F1} ms, {bytes:N0} bytes");
        Assert.True(bytes > 0);
    }
}
