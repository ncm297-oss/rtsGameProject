using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

public class ZzScratchReachTests
{
    private readonly ITestOutputHelper _out;
    public ZzScratchReachTests(ITestOutputHelper o) => _out = o;

    private void Measure(string name, Simulation sim)
    {
        while (sim.TickNumber < 200) sim.Tick();
        World w = sim.World;
        for (int p = 0; p < 4 && (p < 2 || name.StartsWith("256")); p++)
        {
            AiPlayer ai = sim.AiOf(p)!;
            Vector2 hall = ai.HallPoint;
            float forest = float.MaxValue, m1 = float.MaxValue, m2 = float.MaxValue;
            for (int s = 0; s < w.Resources.Capacity; s++)
            {
                if (!w.Resources.Alive[s]) continue;
                int t = w.Resources.TypeId[s];
                float d = Vector2.Distance(ai.NodeCentre(t, w.Resources.Cell[s]), hall);
                if (w.Data.Resources[t].Resource == ResourceKind.Wood) forest = Math.Min(forest, d);
                else if (d < m1) { m2 = m1; m1 = d; }
                else if (d < m2) m2 = d;
            }
            _out.WriteLine($"{name} p{p}: hall {hall} has {ai.HasHall} forest {forest:F1} mine1 {m1:F1} mine2 {m2:F1}");
        }
    }

    [Fact]
    public void Reach()
    {
        for (ulong s = 1; s <= 10; s++)
            Measure($"menu {s}", AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), s));
        for (ulong s = 1; s <= 10; s++)
            Measure($"256x4 {s}", AiTeamTests.MirrorAllies(s));
    }
}
