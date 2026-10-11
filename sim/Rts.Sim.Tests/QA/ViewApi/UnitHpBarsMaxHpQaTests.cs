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
    // Un-skipped by M4-V7b (BUG-0450 fixed): re-pointed at the view's read, UnitHpBars.Collect(shown, hp, maxHp per slot,
    // slots) with each slot's World.MaxHp (what UnitViews.FillMaxHp copies each frame).
    [Fact]
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
        var maxHp = new int[u.Capacity];
        for (int i = 0; i < u.Capacity; i++) maxHp[i] = u.Alive[i] ? w.MaxHp(i) : 0;
        int n = UnitHpBars.Collect(u.Alive, u.Hp, maxHp, slots);
        Assert.Equal(1, n); // failed before the fix: 70 >= the type's 63, so no bar
        Assert.Equal(z.Index, slots[0]);
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
