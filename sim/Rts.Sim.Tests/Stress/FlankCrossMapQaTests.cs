using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M5-P2 (e): the cross-map scenario (200 units of every type over the default map, docs/05 M1) on seeds 1-30, the flank
/// rule off (before) and on. One player: nobody fights, so the rule can't fire: the end states must hash equal and nobody
/// gives up. Two players with combat on (their units meet and fight on the way, so own units do plant): printed give-ups
/// and still-moving counts, before and after; per seed the fights are chaotic (seed 1: 15 -> 8, seed 2: 4 -> 13), so only
/// the sum over the 30 seeds is held, to before + 1 % of all 6,000 units.
/// </summary>
[Trait("Category", "Soak")]
public class FlankCrossMapQaTests
{
    private readonly ITestOutputHelper _out;

    public FlankCrossMapQaTests(ITestOutputHelper output) => _out = output;

    private static (int GaveUp, int Moving, ulong Hash, int Ticks) Run(ulong seed, int players, bool flank)
    {
        CrossMapScenario s = CrossMapScenario.Create(seed, ScenarioTests.Army, ScenarioTests.StartRadius, players: players);
        s.Sim.World.FlankRuleOn = flank;
        s.OrderAll();
        s.Sim.Tick();
        s.Sim.Tick();
        int ticks = 1;
        while (s.AnyMoving() && ticks < s.LimitTicks)
        {
            s.Sim.Tick();
            ticks++;
            Assert.True(MoveScenario.FirstUnitOnBlockedGround(s.Sim.World) < 0, $"seed {seed}: a unit on blocked ground at tick {ticks}");
        }
        UnitStore u = s.Sim.World.Units;
        int moving = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Moving) moving++;
        return (s.GaveUp(), moving, s.Sim.StateHash(), ticks);
    }

    [Fact]
    public void CrossMap_Seeds1To30_OnePlayer_HashEqual_TwoPlayersCombat_GiveUpsWithinBand()
    {
        int sumOff = 0, sumOn = 0;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var off1 = Run(seed, 1, false);
            var on1 = Run(seed, 1, true);
            var off2 = Run(seed, 2, false);
            var on2 = Run(seed, 2, true);
            sumOff += off2.GaveUp;
            sumOn += on2.GaveUp;
            _out.WriteLine($"seed {seed}: one player gave up {off1.GaveUp} -> {on1.GaveUp} (hash {(off1.Hash == on1.Hash ? "equal" : "DIFFER")}); "
                + $"two players, combat: gave up {off2.GaveUp} -> {on2.GaveUp}, still moving {off2.Moving} -> {on2.Moving}, ticks {off2.Ticks} -> {on2.Ticks}");
            Assert.Equal(off1.Hash, on1.Hash);
            Assert.Equal(0, on1.GaveUp);
        }
        _out.WriteLine($"two players, combat, seeds 1-30: gave up {sumOff} -> {sumOn}");
        Assert.True(sumOn <= sumOff + 60, $"summed give-ups {sumOn}, {sumOff} before");
    }
}
