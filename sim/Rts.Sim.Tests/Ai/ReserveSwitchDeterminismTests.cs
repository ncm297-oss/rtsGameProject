using Rts.Sim.Data;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-8 criterion 9: determinism with the switches on. On <see cref="TeblorSwitchOrders"/>' order B (throughReserve on three
/// train steps and noReserve on the expand step), the Normal Teblor mirror on Pale Hills, two seeds x 18,000 ticks: two
/// plain runs are hash-equal at every 500 ticks, and a third run saved and reloaded every 2,000 ticks (each save loaded
/// and played on from the loaded sim) reaches the same hashes. Save format 10: no new part, nothing new stored (the
/// switches are data, read every think).
/// </summary>
public class ReserveSwitchDeterminismTests
{
    private const int Bound = 18_000, Every = 500, SaveEvery = 2_000;

    private readonly ITestOutputHelper _out;

    public ReserveSwitchDeterminismTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_b = new(() => TeblorSwitchOrders.Data("B"));

    private static Simulation New(GameData d, ulong seed) => TeblorSwitchOrders.Match(d, "pale_hills", TeblorSwitchOrders.Mirror(d, AiDifficulty.Normal), seed);

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void SwitchedOrder_TwinsAndASaveChain_AreHashEqual(ulong seed)
    {
        Assert.Equal(11, SaveFormat.CurrentVersion); // M6-S14: format 11 (was 10)
        GameData d = s_b.Value;
        var steps = d.Ai[d.FindFaction("teblor")].Openings[0].BuildOrder;
        Assert.Contains(steps, s => s.ThroughReserve);
        Assert.Contains(steps, s => s.NoReserve);
        Simulation a = New(d, seed), b = New(d, seed), c = New(d, seed);
        int checks = 0, saves = 0, reserved = 0;
        a.AiOf(0)!.ThoughtForTests = ai => { if (ai.ReserveStep >= 0) reserved++; };
        while (a.TickNumber < Bound)
        {
            a.Tick();
            b.Tick();
            c.Tick();
            if (a.TickNumber % SaveEvery == 0)
            {
                c = SaveTestRun.Load(d, SaveTestRun.Save(c));
                saves++;
            }
            if (a.TickNumber % Every != 0) continue;
            ulong h = a.StateHash();
            Assert.True(h == b.StateHash(), $"seed {seed}: the twins differ at tick {a.TickNumber}");
            Assert.True(h == c.StateHash(), $"seed {seed}: the save chain differs at tick {a.TickNumber}");
            checks++;
        }
        _out.WriteLine($"seed {seed}: {checks} checks, {saves} saves, {reserved} thinks of seat 0 with a reserved step, end hash {a.StateHash():X16}");
        Assert.Equal(Bound / Every, checks);
        Assert.True(reserved > 0, "the reserve was never named: the switches had nothing to act on");
    }
}
