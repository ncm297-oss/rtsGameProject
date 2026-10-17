using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// Pathing slice 2, criterion 5: docs/03's crowd rows (<see cref="CrowdRows"/>, M1-4d-3) re-measured with the flank rule off
/// (<c>World.FlankRuleOn</c>, the before) and on, same scenarios, printed as the slice-2 table. The movement rows run combat
/// off, where no unit is ever <c>Attacking</c> or <c>Casting</c>: the rule can't fire, so their end states hash equal (asserted).
/// The rows with combat on (two owners fighting at the points or the random goals) are where it can: printed, and held to
/// docs/03 M1-4d-3's noise band (give-ups within 1 % of the units).
/// </summary>
[Trait("Category", "Soak")] // a few minutes in Debug; the quick loop filters it out
public class FlankCrowdRowsReport
{
    private readonly ITestOutputHelper _out;

    public FlankCrowdRowsReport(ITestOutputHelper output) => _out = output;

    private void Row(string name, CrowdRows.Result off, CrowdRows.Result on, bool mustHashEqual)
    {
        string Cell(CrowdRows.Result r) => r.Arrived >= 0 ? $"arrived {r.Arrived}, gave up {r.GaveUp}" : $"gave up {r.GaveUp}";
        _out.WriteLine($"| {name} | {Cell(off)} | {Cell(on)} | {(off.Hash == on.Hash ? "equal" : "differ")} |");
        if (mustHashEqual) Assert.True(off.Hash == on.Hash, $"{name}: the end states differ with no fighter on the map");
    }

    [Fact]
    public void CrowdRows_FlankOffAndOn_Table()
    {
        _out.WriteLine("| Row | Flank off (before) | Flank on (after) | End-state hash |");
        _out.WriteLine("| --- | --- | --- | --- |");
        for (ulong seed = 1; seed <= 3; seed++)
            Row($"500 to 4 points, one player a point, seed {seed}",
                CrowdRows.ToFourPoints(seed, 500, 3000, onePlayerPerPoint: true, combat: false, flank: false),
                CrowdRows.ToFourPoints(seed, 500, 3000, onePlayerPerPoint: true, combat: false, flank: true), true);
        Row("2,500 to 4 points, one player a point, seed 1",
            CrowdRows.ToFourPoints(1, 2500, 6000, onePlayerPerPoint: true, combat: false, flank: false),
            CrowdRows.ToFourPoints(1, 2500, 6000, onePlayerPerPoint: true, combat: false, flank: true), true);
        for (ulong seed = 1; seed <= 5; seed++)
            Row($"128 to 64 goals, seed {seed}",
                CrowdRows.MoreGoalsThanCacheSlots(seed, combat: false, flank: false),
                CrowdRows.MoreGoalsThanCacheSlots(seed, combat: false, flank: true), true);
        foreach (string owner in new[] { "same", "mixed" })
            Row($"200 walkers crossing a settled 300-blob, PreMix(73), {owner} owner", Crossing(owner, false), Crossing(owner, true), true);
        for (ulong seed = 1; seed <= 2; seed++)
        {
            CrowdRows.Result off = RandomGoals(seed, false), on = RandomGoals(seed, true);
            Row($"500 units to 500 random goals (two owners, combat on), seed {seed}", off, on, false);
            Assert.True(on.GaveUp <= off.GaveUp + 5, $"random goals seed {seed}: {on.GaveUp} gave up, {off.GaveUp} before (band 1 % of 500)");
        }
        for (ulong seed = 1; seed <= 2; seed++)
        {
            CrowdRows.Result off = CrowdRows.ToFourPoints(seed, 500, 8000, onePlayerPerPoint: true, combat: true, flank: false);
            CrowdRows.Result on = CrowdRows.ToFourPoints(seed, 500, 8000, onePlayerPerPoint: true, combat: true, flank: true);
            Row($"500 to 4 points, one player a point, combat on, seed {seed}", off, on, false);
            Assert.True(on.GaveUp <= off.GaveUp + 5, $"4 points on combat seed {seed}: {on.GaveUp} gave up, {off.GaveUp} before (band 1 % of 500)");
            Assert.Equal(0, on.StillMoving);
        }
    }

    /// <summary>QA's crossing scene (<c>ShoveQaTests.Crossing</c>): walkers arrived (<see cref="MoveScenario.Arrived"/>) or given up.</summary>
    private static CrowdRows.Result Crossing(string owner, bool flank)
    {
        Simulation sim = QA.ShoveQaTests.Crossing(TestSeeds.PreMix(73), 300, 200, owner, out _);
        sim.World.FlankRuleOn = flank;
        UnitStore u = sim.World.Units;
        int t = 0;
        bool moving;
        do
        {
            sim.Tick();
            t++;
            moving = false;
            for (int i = 0; i < u.Capacity && !moving; i++) moving = u.Alive[i] && u.State[i] == UnitState.Moving;
        } while ((moving || t < 2) && t < 4000);
        bool[] there = MoveScenario.Arrived(sim.World);
        int arrived = 0, gaveUp = 0;
        for (int i = 300; i < 500; i++)
        {
            if (there[i]) arrived++;
            else if (u.GoalCell[i] < 0) gaveUp++;
        }
        return new CrowdRows.Result(arrived, gaveUp, moving ? 1 : 0, t, 0, null, sim.StateHash());
    }

    /// <summary>The stress row's 500 units (two owners, combat on) to 500 random passable cells' centres, ticked until none moves.</summary>
    private static CrowdRows.Result RandomGoals(ulong seed, bool flank)
    {
        Simulation sim = MoveScenario.Spawn(seed, 500, 60f, out _);
        sim.World.FlankRuleOn = flank;
        NavGrid g = sim.World.NavGrid;
        var rng = new SimRng(seed, 501);
        UnitStore u = sim.World.Units;
        var passable = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++) if (g.IsPassable(c % g.Width, c / g.Width)) passable.Add(c);
        for (int i = 0; i < u.Capacity; i++)
            sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, passable[rng.NextInt(0, passable.Count)])));
        sim.Tick();
        int t = 0, moving;
        do
        {
            sim.Tick();
            t++;
            moving = 0;
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Moving) moving++;
        } while (moving > 0 && t < 30_000);
        int gaveUp = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.GoalCell[i] < 0) gaveUp++;
        return new CrowdRows.Result(-1, gaveUp, moving, t, 0, null, sim.StateHash()); // arrived isn't defined for 500 goals
    }
}
