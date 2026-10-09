using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4a criterion 6: 500 units, 50 Burning and 50 Slowed live, a Telas Fire cast every tick (20 a second): the status and
/// ability phases cost at most 0.1 ms a tick, and ticks with all of it allocate nothing.
/// </summary>
[Collection(SerialCollection.Name)]
public class AbilityPerfTests
{
    private const int Mages = 50;
    private readonly ITestOutputHelper _out;

    public AbilityPerfTests(ITestOutputHelper output) => _out = output;

    private sealed class Scene
    {
        public required Simulation Sim { get; init; }
        public required EntityHandle[] Mages { get; init; }
        public required EntityHandle[] Targets { get; init; }
    }

    private static Scene Build()
    {
        var sim = TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 512, CommandCapacity: 1024), LocalMovementTests.Flat(128)));
        UnitStore u = sim.World.Units;
        var mages = new EntityHandle[Mages];
        var targets = new EntityHandle[500 - Mages];
        for (int k = 0; k < Mages; k++)
            mages[k] = CombatScenes.Place(sim, 0, AbilityScenes.Mage, new Vector2(20f + 4f * (k % 25), 40f + 40f * (k / 25)));
        for (int k = 0; k < targets.Length; k++)
        {
            targets[k] = CombatScenes.Place(sim, 1, CombatScenes.Crossbowman, new Vector2(20f + 2f * (k % 50), 50f + 2.5f * (k / 50)));
            u.Hp[targets[k].Index] = 1_000_000; // nobody dies: the load stays the same
        }
        return new Scene { Sim = sim, Mages = mages, Targets = targets };
    }

    /// <summary>Keeps 50 targets Burning and 50 other targets Slowed.</summary>
    private static void Refresh(Scene s)
    {
        for (int k = 0; k < 50; k++)
        {
            StatusSystem.Apply(s.Sim.World, s.Targets[k].Index, AbilityScenes.Burning, 10f, 80, 0);
            StatusSystem.Apply(s.Sim.World, s.Targets[100 + k].Index, AbilityScenes.Slowed, 0.3f, 80, 0);
        }
    }

    /// <summary>One cast a tick: mage <paramref name="t"/> mod 50, its cooldown cleared, at a point in range among the targets.</summary>
    private static void Cast(Scene s, int t)
    {
        EntityHandle m = s.Mages[t % Mages];
        UnitStore u = s.Sim.World.Units;
        u.AbilityReadyTick[m.Index * DataLimits.MaxUnitAbilities] = 0;
        s.Sim.Enqueue(Command.UseAbility(0, m, 0, u.Position[m.Index] + new Vector2(0f, 10f)));
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void FiveHundredUnits_50Burning_50Slowed_20CastsASecond_PhasesUnderPoint1Ms_AndAllocateNothing()
    {
        Scene s = Build();
        Simulation sim = s.Sim;
        World w = sim.World;
        for (int t = 0; t < 60; t++) // warm-up: casts under way, statuses live
        {
            if (t % 20 == 0) Refresh(s);
            Cast(s, t);
            sim.Tick();
        }
        long freq = Stopwatch.Frequency;
        const int Measured = 200;
        var ms = new double[Measured];
        int resolves = 0, burning = 0, slowed = 0;
        for (int t = 0; t < Measured; t++)
        {
            if (t % 20 == 0) Refresh(s);
            Cast(s, t);
            sim.Tick();
            foreach (AbilityEvent e in w.AbilityEvents) if (e.Resolved) resolves++;
            // The two phases once more on this state, timed alone (the tick around them would drown them in movement).
            int seen = w.AbilityEvents.Length;
            long start = Stopwatch.GetTimestamp();
            StatusSystem.Run(w);
            AbilitySystem.Run(w);
            ms[t] = (Stopwatch.GetTimestamp() - start) * 1000.0 / freq;
            foreach (AbilityEvent e in w.AbilityEvents[seen..]) if (e.Resolved) resolves++; // the extra run resolves some too (it counts down as well)
        }
        StatusStore st = w.Units.Statuses;
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            if (st.IndexOf(i, AbilityScenes.Burning) >= 0) burning++;
            if (st.IndexOf(i, AbilityScenes.Slowed) >= 0) slowed++;
        }
        Assert.True(burning >= 50 && slowed >= 50, $"burning {burning}, slowed {slowed}");
        Assert.True(resolves >= Measured / 2, $"only {resolves} resolves");
        AllocationProbe.AssertZero(() =>
        {
            for (int t = 0; t < 20; t++) sim.Tick();
        }, _out, setup: () =>
        {
            Refresh(s);
            for (int k = 0; k < 20; k++) Cast(s, k);
        });
        double avg = ms.Average();
        _out.WriteLine($"status + ability phases: avg {avg:F4} ms, worst {ms.Max():F4} ms a tick; {resolves} resolves, {burning} burning, {slowed} slowed");
        Assert.True(avg <= 0.1, $"avg {avg:F4} ms over the 0.1 ms budget");
    }
}
