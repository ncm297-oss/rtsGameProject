using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M6-V22: <see cref="OrderPath"/> is read for every selected unit every frame; it allocates nothing.</summary>
[Collection(SerialCollection.Name)]
public class OrderPathAllocationTests
{
    private readonly ITestOutputHelper _out;

    public OrderPathAllocationTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Fill_AndQueuesBehind_ForEveryUnit_AllocateZeroBytes()
    {
        Simulation sim = Flat(units: 64);
        var units = new EntityHandle[40];
        for (int k = 0; k < units.Length; k++) units[k] = Place(sim, 0, HeavyInfantry, At(sim, 4 + k % 10, 6 + k / 10));
        foreach (EntityHandle h in units)
        {
            sim.Enqueue(Command.Move(0, h, At(sim, 40, 40)));
            for (int q = 0; q < 8; q++) sim.Enqueue(Command.Move(0, h, At(sim, 10 + 3 * q, 30), queued: true));
        }
        sim.Tick();
        sim.Tick();
        var buffer = new OrderPoint[OrderPath.MaxPoints];
        OrderPath reader = OrderPathTests.Reader(sim.World);
        long sum = 0;
        Action block = () =>
        {
            foreach (EntityHandle h in units)
            {
                int n = reader.Fill(h, buffer);
                for (int k = 0; k < n; k++) sum += (long)buffer[k].Point.X;
                if (reader.QueuesBehind(h.Index)) sum++;
            }
        };
        block();
        Assert.True(sum > 0);
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"OrderPath for 40 units x 9 points: 0 bytes (runs {runs}), checksum {sum}");
    }
}
