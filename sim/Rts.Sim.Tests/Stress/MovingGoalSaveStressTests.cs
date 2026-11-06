using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-SH1 (session 2026-11-05-2148): BUG-0960's last throw is now refused at load: a live Moving unit with goal cell
/// -1 is <see cref="SaveError.Malformed"/>. That is only safe if the sim itself never leaves a Moving unit without a goal,
/// or a real save would be refused (lost). AI v AI matches with fights (raiders from tick 0) and without, 2 and 4 seats,
/// to 12,000 ticks: after every tick no live Moving unit has goal -1, and a save every 500 ticks loads (None).
/// </summary>
public class MovingGoalSaveStressTests
{
    private readonly ITestOutputHelper _out;

    public MovingGoalSaveStressTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(1UL, 2, 0)]
    [InlineData(2UL, 2, 0)]
    [InlineData(3UL, 2, 3)]
    [InlineData(4UL, 2, 6)]
    [InlineData(5UL, 4, 0)]
    [InlineData(6UL, 4, 3)]
    public void NoMovingUnitIsEverGoalless_AndEverySaveLoads(ulong seed, int players, int raiders)
    {
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Ai, AiDifficulty.Normal);
        Simulation sim = AiScenes.Generated(seats, seed, record: false, out _, raiders: raiders);
        UnitStore u = sim.World.Units;
        int saves = 0, movingMax = 0;
        while (sim.TickNumber < 12_000)
        {
            sim.Tick();
            int moving = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.State[i] != UnitState.Moving) continue;
                moving++;
                Assert.True(u.GoalCell[i] >= 0, $"tick {sim.TickNumber}: unit {i} (owner {u.Owner[i]}) Moving with goal {u.GoalCell[i]}");
            }
            movingMax = Math.Max(movingMax, moving);
            if (sim.TickNumber % 500 != 0) continue;
            SaveError e = SaveTestRun.TryLoad(TestSim.Data, SaveTestRun.Save(sim));
            Assert.True(e == SaveError.None, $"tick {sim.TickNumber}: own save refused {e}");
            saves++;
        }
        _out.WriteLine($"seed {seed}, {players} seats, raiders {raiders}: {saves} saves loaded; at most {movingMax} units Moving; losses {string.Join("/", sim.World.UnitLosses.ToArray())}");
    }
}
