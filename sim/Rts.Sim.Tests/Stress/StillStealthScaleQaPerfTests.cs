using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M8-2, session 2026-11-17-0816): 2,000 stand-still units (4x the 500-unit budget, every one a field type) on a 160-cell
/// flat map, 2.5 m apart, one enemy far off. Three stages: all at rest and hidden; then half of them walking 10 m back and
/// forth (a hold let go and taken again every few seconds); the same scene on the copy without the field for comparison.
/// Reports ms per tick for each, the stand-still pass alone, and the bytes allocated. Bounds: nothing allocates after
/// warm-up; the pass alone stays under 1 ms (Debug) at 2,000 units.
/// </summary>
[Trait("Category", "Perf")]
[Collection(SerialCollection.Name)]
public class StillStealthScaleQaPerfTests
{
    private readonly ITestOutputHelper _out;

    public StillStealthScaleQaPerfTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_plain = new(() => AiScenes.DataWith(StealthTests.AddTo));

    private const int Units = 2000;

    private static (Simulation Sim, EntityHandle[] Units) Scene(GameData data)
    {
        var sim = new Simulation(new SimConfig(1, 2, Units + 64, 8192) { Data = data }, LocalMovementTests.Flat(160));
        World w = sim.World;
        int bow = data.FindUnit("malazan_crossbowman");
        var list = new EntityHandle[Units];
        int k = 0;
        for (int y = 0; y < 50 && k < Units; y++)
            for (int x = 0; x < 40 && k < Units; x++)
                list[k++] = CombatScenes.Place(sim, 0, bow, new Vector2(30f + 2.5f * x, 30f + 2.5f * y));
        CombatScenes.Place(sim, 1, data.FindUnit("whirlwind_raider"), w.NavGrid.CellCenter(158, 158));
        return (sim, list);
    }

    private static double MsPerTick(Simulation sim, int ticks, EntityHandle[]? walkers)
    {
        var sw = new Stopwatch();
        for (int t = 0; t < ticks; t++)
        {
            Orders(sim, walkers);
            sw.Start();
            sim.Tick();
            sw.Stop();
        }
        return sw.Elapsed.TotalMilliseconds / ticks;
    }

    /// <summary>Every 100 ticks, the even-numbered half walks 10 m east or back west.</summary>
    private static void Orders(Simulation sim, EntityHandle[]? walkers)
    {
        if (walkers == null || sim.TickNumber % 100 != 0) return;
        float dx = sim.TickNumber / 100 % 2 == 0 ? 10f : -10f;
        UnitStore u = sim.World.Units;
        for (int k = 0; k < walkers.Length; k += 2)
            sim.Enqueue(Command.Move(0, walkers[k], u.Position[walkers[k].Index] + new Vector2(dx, 0f)));
    }

    [Fact]
    public void TwoThousandStandStillUnits_TickCost_ThePassAlone_AndNoAllocation()
    {
        (Simulation still, EntityHandle[] units) = Scene(StillScenes.Still);
        (Simulation plain, EntityHandle[] plainUnits) = Scene(s_plain.Value);
        Assert.NotNull(still.World.Units.Still);
        Assert.Null(plain.World.Units.Still);
        for (int t = 0; t < 80; t++)
        {
            still.Tick();
            plain.Tick();
        }
        int held = 0;
        foreach (EntityHandle h in units) if (still.World.Units.Still!.Held[h.Index] != 0) held++;
        Assert.Equal(Units, held);

        double restStill = MsPerTick(still, 200, null), restPlain = MsPerTick(plain, 200, null);

        // The pass alone over 2,000 held units (the store's state unchanged by a second pass in the same tick).
        var sw = Stopwatch.StartNew();
        for (int r = 0; r < 1000; r++) StillStealthSystem.Run(still.World);
        double pass = sw.Elapsed.TotalMilliseconds / 1000;

        // Half walking: warm up the walks, then measure and count the allocations.
        double walkStill0 = MsPerTick(still, 400, units), walkPlain0 = MsPerTick(plain, 400, plainUnits);
        long before = GC.GetAllocatedBytesForCurrentThread();
        double walkStill = MsPerTick(still, 400, units);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        double walkPlain = MsPerTick(plain, 400, plainUnits);
        long plainBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        // At rest (no orders): the field's own work alone.
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int t = 0; t < 200; t++) still.Tick();
        long restBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        int letGo = 0;
        foreach (EntityHandle h in units) if (still.World.Units.Still!.Held[h.Index] == 0) letGo++;
        _out.WriteLine($"2,000 units at rest, all hidden: {restStill:F2} ms / tick with the field, {restPlain:F2} without ({100 * (restStill / restPlain - 1):+0;-0}%)");
        _out.WriteLine($"half walking: {walkStill:F2} ms / tick with the field, {walkPlain:F2} without ({100 * (walkStill / walkPlain - 1):+0;-0}%); warm-up {walkStill0:F2} / {walkPlain0:F2}; {letGo} not held at the end");
        _out.WriteLine($"the stand-still pass alone: {pass * 1000:F1} us at 2,000 held units; bytes over 400 walking ticks: {bytes} with the field, {plainBytes} without; over 200 more ticks: {restBytes}");
        Assert.True(bytes <= plainBytes, $"the field allocates: {bytes} bytes with it, {plainBytes} without");
        Assert.True(pass < 1.0, $"the pass alone {pass:F3} ms");
    }
}
