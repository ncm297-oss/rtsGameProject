using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M4-4a, session 2026-10-09-0724): the status and ability phases at 2x and 5x the criterion's load (1,000 / 2,500
/// units, 10 % Burning and 10 % Slowed, 2 / 5 casts a tick). Cost must grow no worse than linearly: at most 0.1 ms x the
/// factor a tick (the criterion's budget scaled), and the full ticks allocate nothing.
/// </summary>
[Collection(SerialCollection.Name)]
public class AbilityScalePerfTests
{
    private readonly ITestOutputHelper _out;

    public AbilityScalePerfTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(2)]
    [InlineData(5)]
    public void StatusAndAbilityPhases_ScaleLinearly_AndAllocateNothing(int factor)
    {
        int units = 500 * factor, mages = 50 * factor, tenth = 50 * factor;
        var sim = TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(Seed: 5, PlayerCount: 2, UnitCapacity: units + 16, CommandCapacity: 4096), LocalMovementTests.Flat(256)));
        World w = sim.World;
        UnitStore u = w.Units;
        var m = new EntityHandle[mages];
        var targets = new EntityHandle[units - mages];
        for (int k = 0; k < mages; k++)
            m[k] = CombatScenes.Place(sim, 0, AbilityScenes.Mage, new Vector2(10f + 4f * (k % 100), 20f + 40f * (k / 100)));
        for (int k = 0; k < targets.Length; k++)
        {
            targets[k] = CombatScenes.Place(sim, 1, CombatScenes.Crossbowman, new Vector2(10f + 2f * (k % 200), 30f + 2.5f * (k / 200)));
            u.Hp[targets[k].Index] = 1_000_000;
        }
        void Refresh()
        {
            for (int k = 0; k < tenth; k++)
            {
                StatusSystem.Apply(w, targets[k].Index, AbilityScenes.Burning, 10f, 80, 0);
                StatusSystem.Apply(w, targets[tenth + k].Index, AbilityScenes.Slowed, 0.3f, 80, 0);
            }
        }
        int next = 0;
        void Cast()
        {
            for (int c = 0; c < factor; c++)
            {
                EntityHandle h = m[next++ % mages];
                u.AbilityReadyTick[h.Index * DataLimits.MaxUnitAbilities] = 0;
                sim.Enqueue(Command.UseAbility(0, h, 0, u.Position[h.Index] + new Vector2(0f, 10f)));
            }
        }
        for (int t = 0; t < 60; t++)
        {
            if (t % 20 == 0) Refresh();
            Cast();
            sim.Tick();
        }
        const int Measured = 200;
        var ms = new double[Measured];
        for (int t = 0; t < Measured; t++)
        {
            if (t % 20 == 0) Refresh();
            Cast();
            sim.Tick();
            long start = Stopwatch.GetTimestamp();
            StatusSystem.Run(w);
            AbilitySystem.Run(w);
            ms[t] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }
        AllocationProbe.AssertZero(() =>
        {
            for (int t = 0; t < 20; t++) sim.Tick();
        }, _out, setup: () =>
        {
            Refresh();
            for (int k = 0; k < 10; k++) Cast();
        });
        double avg = ms.Average();
        _out.WriteLine($"x{factor}: {units} units, status + ability phases avg {avg:F4} ms, worst {ms.Max():F4} ms; casters {u.CasterCount}, with statuses {u.Statuses.UnitsWithStatuses}");
        Assert.True(u.Statuses.UnitsWithStatuses >= 2 * tenth, $"only {u.Statuses.UnitsWithStatuses} units with statuses");
        Assert.True(avg <= 0.1 * factor, $"x{factor}: avg {avg:F4} ms over {0.1 * factor:F1} ms");
    }
}
