using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4b-3a criterion 2: the Frenzied status (docs/02 "Status effects": attack speed up) shortens the attack cooldown to
/// <c>max(1, round(cooldown / (1 + magnitude)))</c> from the next swing, leaves the wind-up alone, counts only the largest
/// magnitude, ends with its duration, and is hashed state.
/// </summary>
public class FrenziedTests
{
    private static int Zealot => TestSim.Data.FindUnit("whirlwind_zealot");

    private static readonly Lazy<GameData> s_twoFrenzies = new(() =>
    {
        // A second status of the frenzied kind (test-only), so one unit can carry two Frenzied entries at once.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/statuses.json", root => root["statuses"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "zz_test_frenzy", ["displayName"] = "Test Frenzy", ["description"] = "Test only.", ["kind"] = "frenzied",
        }));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    });

    /// <summary>
    /// Player 0's Zealot (1.0 s = 20-tick cooldown, 0.3 s = 6-tick wind-up) ordered onto player 1's Battering Ram 1.5 m away,
    /// which attacks buildings only (so never hits back) and is given a million hit points: a swing every cooldown, forever.
    /// </summary>
    private static Simulation Duel(out EntityHandle zealot, GameData? data = null)
    {
        Simulation sim = data == null ? Flat() : new Simulation(new SimConfig(1, 2, 64, 544) { Data = data }, LocalMovementTests.Flat(48));
        GameData d = sim.World.Data;
        zealot = Place(sim, 0, d.FindUnit("whirlwind_zealot"), At(sim, 10, 10));
        EntityHandle dummy = Place(sim, 1, d.FindUnit("whirlwind_battering_ram"), At(sim, 10, 10, 1.5f));
        sim.World.Units.Hp[dummy.Index] = 1_000_000;
        sim.Enqueue(Command.Attack(0, zealot, dummy, false));
        return sim;
    }

    /// <summary>
    /// Runs <paramref name="ticks"/> ticks; returns the ticks a swing of <paramref name="unit"/> started in, with the cooldown
    /// and wind-up it set and whether it had any status at that tick's end.
    /// </summary>
    private static List<(int Tick, int Cooldown, int Windup, bool Statused)> Swings(Simulation sim, EntityHandle unit, int ticks)
    {
        UnitStore u = sim.World.Units;
        var swings = new List<(int, int, int, bool)>();
        for (int t = 0; t < ticks; t++)
        {
            int before = u.CooldownTicks[unit.Index];
            sim.Tick();
            if (u.CooldownTicks[unit.Index] > before)
                swings.Add((sim.TickNumber - 1, u.CooldownTicks[unit.Index], u.WindupTicks[unit.Index], u.Statuses.Count[unit.Index] > 0));
        }
        return swings;
    }

    private static int[] Gaps(List<(int Tick, int Cooldown, int Windup, bool Statused)> swings) =>
        swings.Zip(swings.Skip(1), (a, b) => b.Tick - a.Tick).ToArray();

    [Fact]
    public void Base_TheZealotSwingsEvery20Ticks_WithA6TickWindup()
    {
        AttackDef attack = TestSim.Data.Units[Zealot].Attack;
        Assert.Equal((20, 6), (attack.CooldownTicks, attack.WindupTicks));
        Simulation sim = Duel(out EntityHandle z);
        var swings = Swings(sim, z, 120);
        Assert.True(swings.Count >= 5, $"{swings.Count} swings");
        Assert.All(Gaps(swings), g => Assert.Equal(20, g));
        Assert.All(swings, s => Assert.Equal((20, 6), (s.Cooldown, s.Windup)));
    }

    [Fact]
    public void FrenziedHalf_SwingsEvery13Ticks_AndTheWindupIsUnchanged()
    {
        Simulation sim = Duel(out EntityHandle z);
        StatusSystem.Apply(sim.World, z.Index, sim.World.Data.FindStatus("frenzied"), 0.5f, 10_000, 0);
        var swings = Swings(sim, z, 120);
        Assert.True(swings.Count >= 8, $"{swings.Count} swings");
        Assert.All(Gaps(swings), g => Assert.Equal(13, g)); // round(20 / 1.5) = round(13.33)
        Assert.All(swings, s => Assert.Equal((13, 6), (s.Cooldown, s.Windup)));
    }

    [Fact]
    public void TwoFrenziedEntries_HalfAndAFifth_TheLargestCounts_13Ticks()
    {
        GameData d = s_twoFrenzies.Value;
        Simulation sim = Duel(out EntityHandle z, d);
        StatusSystem.Apply(sim.World, z.Index, d.FindStatus("zz_test_frenzy"), 0.2f, 10_000, 0);
        StatusSystem.Apply(sim.World, z.Index, d.FindStatus("frenzied"), 0.5f, 10_000, 0);
        Assert.Equal(2, sim.World.Units.Statuses.Count[z.Index]);
        var swings = Swings(sim, z, 100);
        Assert.True(swings.Count >= 6);
        Assert.All(swings, s => Assert.Equal(13, s.Cooldown)); // not 20 / 1.7 = 12: magnitudes don't add
        // The +20 % alone: round(20 / 1.2) = round(16.67) = 17.
        Simulation fifth = Duel(out EntityHandle f, d);
        StatusSystem.Apply(fifth.World, f.Index, d.FindStatus("zz_test_frenzy"), 0.2f, 10_000, 0);
        Assert.All(Swings(fifth, f, 100), s => Assert.Equal(17, s.Cooldown));
        // The same status twice keeps the stronger magnitude (the stacking rule).
        Simulation same = Duel(out EntityHandle g);
        int frenzied = same.World.Data.FindStatus("frenzied");
        StatusSystem.Apply(same.World, g.Index, frenzied, 0.5f, 10_000, 0);
        StatusSystem.Apply(same.World, g.Index, frenzied, 0.2f, 10_000, 0);
        Assert.All(Swings(same, g, 100), s => Assert.Equal(13, s.Cooldown));
    }

    [Fact]
    public void Expiry_TheNextSwingIsBackTo20()
    {
        Simulation sim = Duel(out EntityHandle z);
        StatusSystem.Apply(sim.World, z.Index, sim.World.Data.FindStatus("frenzied"), 0.5f, 50, 0);
        var swings = Swings(sim, z, 160);
        Assert.Contains(swings, s => s.Statused);
        Assert.Contains(swings, s => !s.Statused);
        foreach (var s in swings) Assert.Equal(s.Statused ? 13 : 20, s.Cooldown);
        Assert.Equal(0, sim.World.Units.Statuses.Count[z.Index]);
        // Each gap is the cooldown its swing set: 13s while Frenzied, then 20 from the first swing after it ended.
        int[] gaps = Gaps(swings);
        for (int k = 0; k < gaps.Length; k++) Assert.Equal(swings[k].Cooldown, gaps[k]);
        int first = swings.FindIndex(s => !s.Statused);
        Assert.True(first > 0 && swings.Skip(first).All(s => !s.Statused && s.Cooldown == 20));
    }

    [Theory]
    [InlineData(20, 0.5f, 13)]
    [InlineData(20, 1.0f, 10)]
    [InlineData(30, 0.5f, 20)]
    [InlineData(3, 0.2f, 3)]   // 2.5 rounds half away from zero
    [InlineData(1, 5f, 1)]     // at least a tick
    [InlineData(2, 100f, 1)]
    public void AttackCooldown_IsMax1RoundOfCooldownOver1PlusMagnitude(int cooldown, float magnitude, int expected)
    {
        Simulation sim = Duel(out EntityHandle z);
        Assert.Equal(cooldown, StatusSystem.AttackCooldown(sim.World, z.Index, cooldown)); // no status: unchanged
        StatusSystem.Apply(sim.World, z.Index, sim.World.Data.FindStatus("frenzied"), magnitude, 100, 0);
        Assert.Equal(expected, StatusSystem.AttackCooldown(sim.World, z.Index, cooldown));
        // A slow or a burn is no frenzy.
        Simulation other = Duel(out EntityHandle o);
        StatusSystem.Apply(other.World, o.Index, other.World.Data.FindStatus("slowed"), 0.3f, 100, 1);
        Assert.Equal(cooldown, StatusSystem.AttackCooldown(other.World, o.Index, cooldown));
    }

    /// <summary>A Frenzied entry is status-store state: its id, magnitude and ticks change the hash; two runs agree.</summary>
    [Fact]
    public void TheHash_CoversTheEntry_AndTwinsAgree()
    {
        Simulation a = Duel(out EntityHandle za), b = Duel(out EntityHandle zb), c = Duel(out _);
        int frenzied = a.World.Data.FindStatus("frenzied");
        StatusSystem.Apply(a.World, za.Index, frenzied, 0.5f, 50, 0);
        StatusSystem.Apply(b.World, zb.Index, frenzied, 0.5f, 50, 0);
        Assert.NotEqual(c.StateHash(), a.StateHash());
        Assert.Equal(a.StateHash(), b.StateHash());
        StatusStore s = b.World.Units.Statuses;
        int at = s.IndexOf(zb.Index, frenzied);
        s.Magnitude[at] = 0.6f;
        Assert.NotEqual(a.StateHash(), b.StateHash());
        s.Magnitude[at] = 0.5f;
        s.TicksRemaining[at]++;
        Assert.NotEqual(a.StateHash(), b.StateHash());
        s.TicksRemaining[at]--;
        Assert.Equal(a.StateHash(), b.StateHash());
        for (int t = 0; t < 120; t++)
        {
            a.Tick();
            b.Tick();
            Assert.Equal(a.StateHash(), b.StateHash());
        }
    }
}
