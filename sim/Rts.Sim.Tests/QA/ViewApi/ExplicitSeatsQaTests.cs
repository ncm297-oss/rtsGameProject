using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA (M6-V2, session 2026-10-12-1930): the view's <c>SimRunner.Start</c> now always passes explicit <see cref="SimConfig.Seats"/>.
/// Without <c>--faction</c> / <c>--ai</c> those are <c>SimRunner.BuildSeats</c>' defaults (player p plays <c>p mod factions</c>,
/// human, Normal; restated here, the game assembly is not referenced). A run with them must be the same run as one with no seats
/// at all: equal state hashes every tick and byte-equal replays.
/// </summary>
[Collection(SerialCollection.Name)]
public class ExplicitSeatsQaTests
{
    private static ImmutableArray<PlayerSeat> ViewDefaults(int players, int factions)
    {
        var b = ImmutableArray.CreateBuilder<PlayerSeat>(players);
        for (int p = 0; p < players; p++) b.Add(new PlayerSeat(p % factions, SeatController.Human, AiDifficulty.Normal));
        return b.MoveToImmutable();
    }

    private static Simulation Match(ulong seed, int players, bool explicitSeats, out ReplayRecorder recorder)
    {
        SimConfig config = TestSim.Config(seed, players, 2010, 4096) with { Map = MapGenParams.Default with { Forests = 12, GoldMines = 8 } };
        if (explicitSeats) config = config with { Seats = ViewDefaults(players, TestSim.Data.Factions.Length) };
        var sim = new Simulation(config);
        recorder = new ReplayRecorder(sim, 20);
        World w = sim.World;
        if (players == 2)
        {
            var blocks = new Vector2[2][];
            for (int p = 0; p < 2; p++)
                blocks[p] = StartLayout.Block(w.NavGrid, 30, west: p == 0, StartBaseTests.MaxRadius(w.Data));
            StartBasePlan plan = ViewReads.Plan(w, blocks, 5, StartBaseTests.MaxRadius(w.Data));
            for (int p = 0; p < 2; p++)
            {
                FactionDef f = w.Data.Factions[w.FactionOf(p)];
                for (int k = 0; k < blocks[p].Length; k++) sim.Enqueue(Command.SpawnUnit(p, f.Units[k % f.Units.Length], blocks[p][k]));
                if (plan.HallAnchor[p] < 0) continue;
                int a = plan.HallAnchor[p];
                sim.Enqueue(Command.SpawnBuilding(p, plan.HallType[p], w.NavGrid.CellCenter(a % w.NavGrid.Width, a / w.NavGrid.Width)));
                foreach (Vector2 v in plan.Workers[p]) sim.Enqueue(Command.SpawnUnit(p, plan.WorkerType[p], v));
            }
        }
        return sim;
    }

    [Theory]
    [InlineData(1UL, 2)]
    [InlineData(5UL, 2)]
    [InlineData(3UL, 3)]
    [InlineData(3UL, 1)]
    public void ExplicitDefaultSeats_AreTheSameRun_AsNoSeats(ulong seed, int players)
    {
        Simulation a = Match(seed, players, explicitSeats: false, out ReplayRecorder ra), b = Match(seed, players, explicitSeats: true, out ReplayRecorder rb);
        Assert.False(b.World.Config.HasAiSeat);
        for (int t = 0; t < 300; t++)
        {
            a.Tick();
            b.Tick();
            Assert.Equal(a.StateHash(), b.StateHash());
        }
        for (int p = 0; p < players; p++) Assert.Equal(a.World.FactionOf(p), b.World.FactionOf(p));
        Assert.Equal(ReplayFormat.Write(ra.ToReplay()), ReplayFormat.Write(rb.ToReplay()));
    }
}
