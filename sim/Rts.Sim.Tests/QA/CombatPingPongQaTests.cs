using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-2a (session 2026-10-08-0313), BUG-0150: M4-2a switched four two-owner movement rows to combat off because, once
/// the Battering Ram stopped fighting units, a retaliator in their brawls ping-pongs between two targets forever (a
/// "can attack" target at its sight edge reached by a detour that leads away from a nearer worker: every switch resets
/// <c>ChaseBest</c> to the new target's gap, so each target in turn shows "progress" and the give-up never builds).
/// These rows keep the combat-on versions of two of them, so the combat-off switch can't hide the livelock for good.
/// </summary>
[Collection(SerialCollection.Name)]
public class CombatPingPongQaTests
{
    private readonly ITestOutputHelper _out;

    public CombatPingPongQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>GridChangeQaTests' four-groups row with combat on: 300 units to four points, one player per point, everybody stands within 3,000 ticks.</summary>
    [Fact(Skip = "BUG-0150: a retaliator ping-pongs between two targets forever (6 units still Moving after 3,000 ticks)")]
    public void FourGroups_CombatOn_EverybodyComesToRest()
    {
        var still = new List<string>();
        foreach (int period in new[] { 1, 2, 3 })
        {
            int tick = 0;
            CrowdRows.Result r = CrowdRows.ToFourPoints(1, 300, 3000, onePlayerPerPoint: true, everyTick: w =>
            {
                if (++tick < 300 && tick % period == 0) w.NavGrid.BumpVersionForTests();
                return null;
            });
            _out.WriteLine($"closing every {period}: {r}");
            if (r.StillMoving != 0) still.Add($"period {period}: {r.StillMoving} still moving");
        }
        Assert.Empty(still);
    }

    /// <summary>CrowdRowSweepStressTests' seeds 81-140 with combat on, the seed that never stops (110): every unit comes to rest.</summary>
    [Fact(Skip = "BUG-0150: a retaliator ping-pongs between two targets forever (seed 110: 5 units still Moving after 3,000 ticks)")]
    public void MoreGoalsThanCacheSlots_Seed110_CombatOn_AllTerminate()
    {
        var stuck = new List<string>();
        foreach (ulong seed in new ulong[] { 110 })
        {
            CrowdRows.Result r = CrowdRows.MoreGoalsThanCacheSlots(seed);
            if (r.StillMoving != 0) stuck.Add($"seed {seed}: {r.StillMoving} still moving after {r.Ticks} ticks");
        }
        _out.WriteLine($"{stuck.Count} maps never stop. {string.Join("; ", stuck)}");
        Assert.Empty(stuck);
    }
}
