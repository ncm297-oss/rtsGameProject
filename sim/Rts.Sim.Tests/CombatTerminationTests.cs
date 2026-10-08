using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Tests.QA;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// BUG-0137 / BUG-0143 (M4-1 fix round 2): the pre-M4 two-owner movement scenes on combat. Their own rows run combat
/// off because their limits are walking bounds and a fight fought to the end runs past them; here the same scenes fight,
/// and every one must come to rest (no chase or give-up livelock): nobody Moving for <see cref="StillTicks"/> ticks in a
/// row, starting within three times the walking limit.
/// </summary>
public class CombatTerminationTests
{
    /// <summary>Ticks in a row with nobody Moving that count as at rest (a fighter stands Idle a tick between chase legs).</summary>
    private const int StillTicks = 200;

    private readonly ITestOutputHelper _out;

    public CombatTerminationTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// <c>CrossMapStressTests.TwoHundred_TwoOwners_Seeds1To50_Terminate_Report</c>'s scenes (200 units, owners
    /// alternating, a level-1 goal across the map) on combat.
    /// </summary>
    [Theory]
    [InlineData(1UL, 10UL)]
    [InlineData(11UL, 20UL)]
    [InlineData(21UL, 30UL)]
    [InlineData(31UL, 40UL)]
    [InlineData(41UL, 50UL)]
    public void TwoOwnerCrossMap_OnCombat_ComesToRest(ulong first, ulong last)
    {
        var failures = new List<string>();
        double worst = 0;
        for (ulong seed = first; seed <= last; seed++)
        {
            CrossMapQaScenario? s = CrossMapQaScenario.Create(seed, 200, 12f, players: 2, goalMinLevel: 1, CrossMapStart.WestEdge);
            if (s == null) continue;
            MoveScenario.MoveAll(s.Sim, s.Goal);
            int rest = RestTick(s.Sim, 3 * s.LimitTicks);
            World w = s.Sim.World;
            if (rest < 0)
            {
                failures.Add($"seed {seed}: not at rest within {3 * s.LimitTicks} ticks (walking limit {s.LimitTicks})");
                continue;
            }
            worst = Math.Max(worst, (double)rest / s.LimitTicks);
            _out.WriteLine($"seed {seed}: at rest from tick {rest} ({(double)rest / s.LimitTicks:F2}x the walking limit), kills {w.Kills[0]} / {w.Kills[1]}");
        }
        _out.WriteLine($"worst {worst:F2}x the walking limit");
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    /// <summary>
    /// <c>LocalMovementStressTests.Crowd_ToFourPoints_BothPlayersAtEveryPoint_Report</c>'s scene (500 units, both players
    /// sent to each of four points 6 m apart, walking limit 3,000 ticks) on combat, seeds 1-3.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void Crowd500ToFourPoints_BothPlayersAtEveryPoint_OnCombat_ComesToRest(ulong seed)
    {
        const int walkingLimit = 3000;
        Simulation sim = MoveScenario.Spawn(seed, 500, 40f, out int goalCell, players: 2);
        World w = sim.World;
        Vector2[] four = CrowdRows.FourPoints(MoveScenario.Center(w.NavGrid, goalCell));
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), four[CrowdRows.PointOf(u, i, false)]));
        int rest = RestTick(sim, 3 * walkingLimit);
        _out.WriteLine($"seed {seed}: at rest from tick {rest} ({(double)rest / walkingLimit:F2}x the walking limit), kills {w.Kills[0]} / {w.Kills[1]}");
        Assert.True(rest >= 0, $"seed {seed}: not at rest within {3 * walkingLimit} ticks");
    }

    /// <summary>
    /// Ticks <paramref name="sim"/> until nobody has been Moving for <see cref="StillTicks"/> ticks in a row; the tick
    /// that run began, or -1 if it didn't begin by <paramref name="limit"/>.
    /// </summary>
    private static int RestTick(Simulation sim, int limit)
    {
        UnitStore u = sim.World.Units;
        int still = 0;
        for (int t = 1; t <= limit + StillTicks; t++)
        {
            sim.Tick();
            bool moving = false;
            for (int i = 0; i < u.Capacity && !moving; i++) moving = u.Alive[i] && u.State[i] == UnitState.Moving;
            still = moving ? 0 : still + 1;
            if (still == StillTicks) return t - StillTicks + 1 > limit ? -1 : t - StillTicks + 1;
        }
        return -1;
    }
}
