using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// M5-9a (D14 c): the minimum data the sim's task ships for the counter-composition knob, both factions: <c>counterStrength</c>
/// Easy 0 (docs/05 M5 criterion 9: "0 on Easy"), Normal 0.5, Hard 1.0 (the recommended values), and <c>rules.json</c>
/// <c>ai.counterMemory</c> 180 s. D26 (the data track) tunes them and moves this pin with them.
/// </summary>
public class CounterStrengthContentTests
{
    [Theory]
    [InlineData("malazan", AiDifficulty.Easy, 0f)]
    [InlineData("malazan", AiDifficulty.Normal, 0.5f)]
    [InlineData("malazan", AiDifficulty.Hard, 1f)]
    [InlineData("whirlwind", AiDifficulty.Easy, 0f)]
    [InlineData("whirlwind", AiDifficulty.Normal, 0.5f)]
    [InlineData("whirlwind", AiDifficulty.Hard, 1f)]
    public void CounterStrength_IsTheShippedValue(string faction, AiDifficulty difficulty, float strength)
    {
        GameData d = TestSim.Data;
        Assert.Equal(strength, d.Ai[d.FindFaction(faction)].Difficulty(difficulty).CounterStrength);
    }

    /// <summary>docs/05's "0 on Easy" is a rule, not a tuning: every faction's Easy is 0, so Easy matches stay as they were.</summary>
    [Fact]
    public void EveryFactionsEasy_ShiftsNothing()
    {
        foreach (AiDef ai in TestSim.Data.Ai) Assert.Equal(0f, ai.Difficulty(AiDifficulty.Easy).CounterStrength);
    }

    [Fact]
    public void CounterMemory_Is180Seconds()
    {
        Assert.Equal(180 * SimConstants.TicksPerSecond, TestSim.Data.AiRules.CounterMemoryTicks);
    }
}
