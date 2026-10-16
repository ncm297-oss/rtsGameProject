using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-H2 (session 2026-10-16-0847): attacks on the hardening's smaller items beyond the collapse rule
/// (<see cref="CollapseRuleQaTests"/>): BUG-0702 item 2's no-stacking rule against the judging rule (docs/03 "Implementation
/// (M5-4c)": a cast that didn't land is sent again, at most <see cref="AbilityManager.MaxTries"/> times at the same point).
/// </summary>
public class SimHardeningM5H2QaTests
{
    private readonly ITestOutputHelper _out;

    public SimHardeningM5H2QaTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_data = new(() => AbilityManagerQaTests.Data());

    /// <summary>
    /// BUG-0760: one Priest beside a group, the test stopping it every tick it casts, so no storm ever lands. The judging rule
    /// (docs/03) sends a cast that didn't land again at the next think, up to <see cref="AbilityManager.MaxTries"/> times at
    /// the same point, as it does for Telas Fire (<c>AbilityManagerTests.ACastThatNeverLands_*</c>). The no-stacking rule's
    /// record of the Priest's own first send ("a storm runs here until sent + cast + duration") must not stand in for a storm
    /// that never came: it blocks the retries for the whole window.
    /// </summary>
    [Fact(Skip = "BUG-0760: the no-stacking record of the Priest's own failed send blocks the judging rule's retries (1 send, not 3); red on 47ca4a2, green on 8c39c08")]
    public void ASandstormThatNeverLands_IsSentAgain_UpToMaxTries()
    {
        Simulation sim = AbilityManagerQaTests.Scene(s_data.Value, faction: 1);
        AiPlayer ai = sim.AiOf(0)!;
        Vector2 rally = ai.Board.Rally;
        EntityHandle priest = CombatScenes.Place(sim, 0, sim.World.Data.FindUnit("whirlwind_priest"), rally);
        for (int k = 0; k < 4; k++)
        {
            EntityHandle e = CombatScenes.Place(sim, 1, sim.World.Data.FindUnit("malazan_heavy_infantry"), rally + new Vector2(10f, k - 1.5f));
            CombatScenes.Spot(sim, 0, e);
        }
        var casts = new List<Command>();
        ai.SentForTests = c => { if (c.Kind == CommandKind.UseAbility) casts.Add(c); };
        AbilityDef storm = AbilityManagerQaTests.Ability(sim.World.Data, "sandstorm");
        int until = sim.TickNumber + 12 * ai.ThinkTicks;
        int landed = 0;
        while (sim.TickNumber < until)
        {
            if (sim.World.Units.CastAbility[priest.Index] >= 0) sim.Enqueue(Command.Stop(0, priest));
            sim.Tick();
            foreach (Abilities.AbilityEvent e in sim.World.AbilityEvents) if (e.Resolved) landed++;
        }
        _out.WriteLine($"a storm that never lands: {casts.Count} sends in 12 thinks ({landed} landed); window sent + cast + duration = {storm.CastTicks + storm.DurationTicks} ticks, think {ai.ThinkTicks} ticks");
        Assert.Equal(0, landed);
        Assert.True(casts.Count == AbilityManager.MaxTries,
            $"{casts.Count} sends of a storm that never landed (docs/03: sent again up to {AbilityManager.MaxTries} times; the no-stacking record of its own failed send blocks the retries)");
    }
}
