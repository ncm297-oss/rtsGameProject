using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M5-4a): the AI on the generated scene with start locations, past the developer's seeds 1-3: the scout's known wood and
/// first wood (BUG-0592's target, 1,200 / 2,000) and AI vs AI first wood (3,000) on seeds 4-6.
/// </summary>
[Collection(SerialCollection.Name)]
public class StartLocationAiQaTests
{
    private readonly ITestOutputHelper _out;

    public StartLocationAiQaTests(ITestOutputHelper output) => _out = output;

    private static bool KnowsWood(AiPlayer ai)
    {
        int s = -1;
        while (ai.View.NextResource(ref s, out SeenResource n))
            if (ai.Data.Resources[n.TypeId].Resource == ResourceKind.Wood) return true;
        return false;
    }

    [Theory]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    public void GeneratedScene_Seeds4To6_AiKnowsWoodBy1200_FirstWoodBy2000(ulong seed)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Generated(seats, seed, record: false, out _);
        AiPlayer ai = sim.AiOf(0)!;
        int knewWood = -1, firstWood = -1, lastWood = sim.World.Wood[0];
        while (sim.TickNumber < 2400 && firstWood < 0)
        {
            sim.Tick();
            if (knewWood < 0 && KnowsWood(ai)) knewWood = sim.TickNumber;
            if (firstWood < 0 && sim.World.Wood[0] > lastWood) firstWood = sim.TickNumber;
            lastWood = sim.World.Wood[0];
        }
        _out.WriteLine($"seed {seed}: knew wood at {knewWood}, first wood in at {firstWood}");
        Assert.InRange(knewWood, 0, 1200);
        Assert.InRange(firstWood, 0, 2000);
    }

    [Theory]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    public void AiVersusAi_Seeds4To6_BothSeatsWoodBy3000(ulong seed)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, seed, record: false, out _, forests: 12);
        int[] firstWood = { -1, -1 }, last = { sim.World.Wood[0], sim.World.Wood[1] };
        while (sim.TickNumber < 3000 && (firstWood[0] < 0 || firstWood[1] < 0))
        {
            sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                if (firstWood[p] < 0 && sim.World.Wood[p] > last[p]) firstWood[p] = sim.TickNumber;
                last[p] = sim.World.Wood[p];
            }
        }
        _out.WriteLine($"seed {seed}: first wood {firstWood[0]} / {firstWood[1]}");
        Assert.True(firstWood[0] > 0 && firstWood[1] > 0, $"seed {seed}: first wood {firstWood[0]} / {firstWood[1]}");
    }
}
