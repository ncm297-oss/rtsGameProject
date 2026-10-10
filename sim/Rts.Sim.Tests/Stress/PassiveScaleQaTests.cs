using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M4-4b-3a (session 2026-10-10-1015): the passives at scale. 2,500 units none of which has a passive: whole ticks
/// allocate nothing. 2,500 Zealots idle at full hp (the phase-5 pass visits every one, none below): the empty tick stays
/// inside the 500 us EmptyTick budget. 2,500 Zealots all below their line (every one re-Frenzied every tick): reported, and
/// held to the same 500 us.
/// </summary>
[Collection(SerialCollection.Name)]
public class PassiveScaleQaTests
{
    private readonly ITestOutputHelper _out;

    public PassiveScaleQaTests(ITestOutputHelper output) => _out = output;

    private static Simulation Spawned(int units, int type)
    {
        var sim = new Simulation(TestSim.Config(1, 2, UnitCapacity: units, CommandCapacity: units));
        for (int i = 0; i < units; i++)
            sim.Enqueue(Command.SpawnUnit(0, type, new Vector2(i, i))); // one owner: nobody fights, so only the passive differs
        sim.Tick();
        sim.Tick();
        Assert.Equal(units, sim.World.Units.Count);
        return sim;
    }

    [Fact]
    public void TwoThousandFiveHundredUnits_NoPassive_WholeTicksAllocateNothing()
    {
        Simulation sim = Spawned(2500, 0);
        Assert.Equal(0, sim.World.Units.BelowHpUnits);
        for (int i = 0; i < 20; i++) sim.Tick();
        Action ticks = () => { for (int t = 0; t < 50; t++) sim.Tick(); };
        AllocationProbe.AssertZero(ticks, _out);
    }

    [Fact]
    public void TwoThousandFiveHundredZealots_BelowTheLine_WholeTicksAllocateNothing()
    {
        Simulation sim = Spawned(2500, TestSim.Data.FindUnit("whirlwind_zealot"));
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Hp[i] = 10;
        for (int i = 0; i < 20; i++) sim.Tick();
        Action ticks = () => { for (int t = 0; t < 50; t++) sim.Tick(); };
        AllocationProbe.AssertZero(ticks, _out);
        Assert.True(u.Statuses.UnitsWithStatuses == 2500);
    }

    /// <summary>
    /// One owner's 2,500 Raiders (no passive), Zealots at full hp, or Zealots all below the line, idle on the default map:
    /// the Zealot rows against the Raider row are the passive pass's whole cost (the Raider and the Zealot are both melee
    /// infantry; nobody fights). Each held to the EmptyTick budget.
    /// </summary>
    [Theory]
    [Trait("Category", "Perf")]
    [InlineData("whirlwind_raider", false)]
    [InlineData("whirlwind_zealot", false)]
    [InlineData("whirlwind_zealot", true)]
    public void EmptyTick_2500OneOwner_RaidersVsZealots_IsCheap(string type, bool belowLine)
    {
        Simulation sim = Spawned(2500, TestSim.Data.FindUnit(type));
        UnitStore u = sim.World.Units;
        Assert.Equal(type == "whirlwind_zealot" ? 2500 : 0, u.BelowHpUnits);
        if (belowLine) for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Hp[i] = 10;
        for (int i = 0; i < 100; i++) sim.Tick();
        var sw = Stopwatch.StartNew();
        const int ticks = 2000;
        for (int i = 0; i < ticks; i++) sim.Tick();
        sw.Stop();
        double avgUs = sw.Elapsed.TotalMilliseconds * 1000 / ticks;
        _out.WriteLine($"2500 {type} ({(belowLine ? "all below the line, Frenzied every tick" : "full hp")}): avg tick {avgUs:F1} us");
        Assert.True(avgUs < 500, $"avg empty tick {avgUs:F1} us with 2,500 {type}");
    }

    /// <summary>The phase-5 passive pass alone over 2,500 full-hp Zealots (visits each, none below): reported per pass, under 100 us.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void PassivePass_2500FullHpZealots_Reported()
    {
        Simulation sim = Spawned(2500, TestSim.Data.FindUnit("whirlwind_zealot"));
        World w = sim.World;
        ulong h = sim.StateHash();
        for (int k = 0; k < 100; k++) Abilities.PassiveSystem.Run(w);
        var sw = Stopwatch.StartNew();
        const int passes = 2000;
        for (int k = 0; k < passes; k++) Abilities.PassiveSystem.Run(w);
        double us = sw.Elapsed.TotalMilliseconds * 1000 / passes;
        _out.WriteLine($"passive pass over 2,500 full-hp Zealots: {us:F1} us");
        Assert.Equal(h, sim.StateHash());
        Assert.True(us < 100, $"{us:F1} us a pass");
    }
}
