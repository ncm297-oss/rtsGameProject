using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA M4-4b-3b (session 2026-10-10-1314), BUG-0450: since hp techs raise hit points (BUG-0421), a unit's maximum is
/// <c>World.MaxHp(slot)</c>, not its type's <c>hp</c>. A Zealot with Dryjhna's Prophecy at 70 / 83 is hurt and must show a
/// bar of 70 / 83; <c>UnitHpBars.Collect</c> (and <c>CombatViews.SyncBars</c>'s <c>Shows(hp, type hp)</c>) still compare
/// against 63, so it shows none. Re-point this row at whatever max-hp read the view's fix takes.
/// </summary>
public class UnitHpBarsMaxHpQaTests
{
    [Fact(Skip = "BUG-0450: UnitHpBars uses the type's hp as the maximum; a Prophecy Zealot at 70 / 83 shows no bar")]
    public void AProphecyZealotAt70Of83_ShowsABar()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        ProductionSystem.Researched(w, 0, w.Data.FindTech("dryjhnas_prophecy"));
        EntityHandle z = Place(sim, 0, w.Data.FindUnit("whirlwind_zealot"), At(sim, 10, 10));
        u.Hp[z.Index] = 70;
        Assert.Equal(83, w.MaxHp(z.Index));
        var slots = new int[8];
        int n = UnitHpBars.Collect(u.Alive, u.TypeId, u.Hp, w.Data.Units, slots);
        Assert.Equal(1, n); // fails today: 70 >= the type's 63, so no bar
    }

    /// <summary>What the bar reads today, pinned so the change is visible: the type's hp as the max, so 70 / 63 shows nothing.</summary>
    [Fact]
    public void Today_TheBarUsesTheTypeHp_SoAnUpgradedHurtZealotShowsNothing()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        ProductionSystem.Researched(w, 0, w.Data.FindTech("dryjhnas_prophecy"));
        EntityHandle z = Place(sim, 0, w.Data.FindUnit("whirlwind_zealot"), At(sim, 10, 10));
        u.Hp[z.Index] = 70;
        Assert.False(UnitHpBars.Shows(u.Hp[z.Index], w.Data.Units[u.TypeId[z.Index]].Hp, out _));
        Assert.True(UnitHpBars.Shows(u.Hp[z.Index], w.MaxHp(z.Index), out float fill));
        Assert.Equal(70f / 83f, fill, 5);
    }
}
