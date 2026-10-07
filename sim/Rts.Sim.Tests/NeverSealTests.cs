using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-3 criterion 9 (BUG-0078): no placement ever cuts passable ground in two.</summary>
[Collection(SerialCollection.Name)]
public class NeverSealTests
{
    private readonly ITestOutputHelper _out;

    public NeverSealTests(ITestOutputHelper output) => _out = output;

    /// <summary>Passable cells a 4-connected flood from the first one doesn't reach (0: all connected). Independent of the sim's check.</summary>
    private static int Unreached(NavGrid g)
    {
        int n = g.Width * g.Height, start = -1, open = 0;
        for (int i = 0; i < n; i++)
        {
            if (!g.IsPassable(i % g.Width, i / g.Width)) continue;
            open++;
            if (start < 0) start = i;
        }
        var seen = new bool[n];
        var queue = new Queue<int>();
        queue.Enqueue(start);
        seen[start] = true;
        int reached = 1;
        int[] dx = { -1, 1, 0, 0 }, dy = { 0, 0, -1, 1 };
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int x = c % g.Width + dx[d], y = c / g.Width + dy[d];
                if (!g.IsPassable(x, y) || seen[y * g.Width + x]) continue;
                seen[y * g.Width + x] = true;
                reached++;
                queue.Enqueue(y * g.Width + x);
            }
        }
        return open - reached;
    }

    [Theory]
    [InlineData(1UL)] [InlineData(2UL)] [InlineData(3UL)] [InlineData(4UL)]
    [InlineData(5UL)] [InlineData(6UL)] [InlineData(7UL)] [InlineData(8UL)]
    public void FiveHundredRandomLegalPlacements_NeverLeaveAPocket(ulong seed)
    {
        var config = TestSim.Config(Seed: seed, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 16)
            with { Map = MapGenParams.Default with { Forests = 12, GoldMines = 8 }, BuildingCapacity = 512 };
        var sim = new Simulation(config);
        NavGrid g = sim.World.NavGrid;
        Assert.Equal(0, Unreached(g));
        Give(sim, 0, 10_000_000, 10_000_000);
        EntityHandle w = Unit(sim, At(sim, g.Width / 2, g.Height / 2));
        int[] types = TestSim.Data.Buildings.Where(d => d.Faction == 0).Select(d => d.Id).ToArray();
        var rng = new SimRng(seed, 77);
        int placed = 0, sealing = 0;
        for (int k = 0; k < 500; k++)
        {
            int type = types[rng.NextInt(0, types.Length)], cell = -1;
            for (int tries = 0; tries < 400 && cell < 0; tries++)
            {
                int c = rng.NextInt(0, g.Width * g.Height);
                if (sim.World.CanPlace(0, type, c, out var why)) cell = c;
                else if (why == Economy.PlacementError.SealsGround) sealing++;
            }
            if (cell < 0) break; // the map is full for this type
            int before = sim.World.Buildings.Count;
            sim.Enqueue(Command.Build(0, w, type, g.CellCenter(cell % g.Width, cell / g.Width)));
            sim.Tick();
            sim.Tick(); // applies in the second tick
            Assert.Equal(before + 1, sim.World.Buildings.Count);
            Assert.True(Unreached(g) == 0, $"seed {seed}: placement {k} (type {type} at {cell}) left a pocket");
            placed++;
        }
        _out.WriteLine($"seed {seed}: {placed} placements, {sealing} sealing candidates refused, {g.PassableCount} passable cells left");
        Assert.True(placed >= 300 && sealing > 0, $"{placed} placed, {sealing} refused as sealing");
    }
}
