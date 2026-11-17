using Rts.Sim.Data;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D32 (session 2026-11-14-1246): every claim in the Teblor <c>standard</c> opening's description (BUG-1390's fix) is
/// backed by the shipped opening's data, so the words and the steps can't drift apart silently: the two buildings named
/// "first" are built before Age II is researched, "four Warriors raid at 8 army pop" is the wave threshold over a
/// Warrior-only Age I mix, each unit kind named as joining is in the Age II mix and has a building step for its trainer,
/// Shamans also have their own train step, and "a second Longhouse" has an <c>expand</c> step. D32's text left out the
/// Breakers, Destrier Riders and War-dogs (fielded in no match by 15:00 on D32's row). D33 (2026-11-17, BUG-1420) names them
/// again: each now has its own train step, and on the 64-match Normal row they are fielded in 12, 22 and 39 matches
/// (<c>Content/D33BalanceReportTests</c>), so the checks that kept them out became checks that each named kind has a
/// trainer step and a train step. Measured play (how often each claim holds) is <see cref="D32OpeningQaStressTests"/>.
/// </summary>
public class D32DescriptionQaTests
{
    private static readonly GameData D = TestSim.Data;

    private static AiOpening Standard()
    {
        AiOpening o = D.Ai[D.FindFaction("teblor")].Openings[0];
        Assert.Equal("standard", o.Id);
        return o;
    }

    private static int StepIndex(AiOpening o, AiStepAction action, string key)
    {
        for (int i = 0; i < o.BuildOrder.Length; i++)
            if (o.BuildOrder[i].Action == action && o.BuildOrder[i].TargetKey == key) return i;
        return -1;
    }

    private static string Desc => Standard().Description;

    [Fact]
    public void TheDescription_IsD33sSentence()
    {
        // D33 (2026-11-17): was "... then Javelin Throwers and Shamans join them and a second Longhouse goes up." (D32's, BUG-1390).
        Assert.Equal("War Lodge and Javelin Ground first, then Age II; four Warriors raid at 8 army pop, then Javelin Throwers, Shamans, Breakers, Destrier Riders and War-dogs join them and a second Longhouse goes up.", Desc);
    }

    [Fact]
    public void WarLodgeAndJavelinGroundFirst_ThenAgeII_IsTheStepOrder()
    {
        AiOpening o = Standard();
        int age = StepIndex(o, AiStepAction.Research, "age_ii");
        Assert.True(age >= 0);
        foreach (string b in new[] { "teblor_war_lodge", "teblor_javelin_ground" })
        {
            int i = StepIndex(o, AiStepAction.Build, b);
            Assert.True(i >= 0 && i < age, $"{b} is step {i + 1}, Age II step {age + 1}");
            Assert.Equal(AiStepTrigger.Pop, o.BuildOrder[i].Trigger); // a pop step: due long before Age II's 6:00
        }
        // No other non-house building is due before Age II.
        for (int i = 0; i < age; i++)
        {
            AiStep s = o.BuildOrder[i];
            if (s.Action != AiStepAction.Build) continue;
            Assert.Contains(s.TargetKey, new[] { "teblor_hut", "teblor_war_lodge", "teblor_javelin_ground", "teblor_woodcutters_lodge" });
        }
    }

    [Fact]
    public void FourWarriorsRaidAt8ArmyPop_IsTheWaveOverAWarriorOnlyAgeIMix()
    {
        AiOpening o = Standard();
        Assert.Contains("four Warriors raid at 8 army pop", Desc);
        Assert.Equal(16, o.AttackMinHalfPop); // 8 pop
        AiComposition ageOne = o.Compositions.Single(c => c.Age == 1);
        Assert.Equal(new[] { D.FindUnit("teblor_warrior") }, ageOne.Units.ToArray());
        Assert.Equal(4, o.AttackMinHalfPop / D.Units[D.FindUnit("teblor_warrior")].HalfPop);
    }

    [Theory]
    [InlineData("Javelin Throwers", "teblor_javelin_thrower", "teblor_javelin_ground")]
    [InlineData("Shamans", "teblor_shaman", "teblor_shamans_circle")]
    [InlineData("Breakers", "teblor_breaker", "teblor_breakers_hall")]
    [InlineData("Destrier Riders", "teblor_destrier_rider", "teblor_stables")]
    [InlineData("War-dogs", "teblor_war_dog", "teblor_stables")]
    public void EachKindNamedAsJoining_IsInTheAgeIIMix_AndItsTrainerIsBuilt(string words, string unit, string trainer)
    {
        AiOpening o = Standard();
        Assert.Contains(words, Desc);
        AiComposition ageTwo = o.Compositions.Single(c => c.Age == 2);
        int u = D.FindUnit(unit);
        int k = ageTwo.Units.IndexOf(u);
        Assert.True(k >= 0 && ageTwo.Weights[k] > 0, $"{unit} not in the Age II mix");
        Assert.True(StepIndex(o, AiStepAction.Build, trainer) >= 0, $"no build step for {trainer}");
    }

    [Fact]
    public void Shamans_HaveTheirOwnTrainStep_AfterTheCircleStep()
    {
        AiOpening o = Standard();
        int circle = StepIndex(o, AiStepAction.Build, "teblor_shamans_circle");
        int train = StepIndex(o, AiStepAction.Train, "teblor_shaman");
        Assert.True(circle >= 0 && train > circle, $"Circle step {circle + 1}, Shaman step {train + 1}");
        Assert.True(o.BuildOrder[train].Count >= 1);
        Assert.True(o.BuildOrder[train].At >= o.BuildOrder[circle].At, "the Shaman step is due before its Circle");
    }

    [Fact]
    public void ASecondLonghouseGoesUp_IsAnExpandStep_AfterAgeII()
    {
        AiOpening o = Standard();
        Assert.Contains("a second Longhouse goes up", Desc);
        int expand = -1;
        for (int i = 0; i < o.BuildOrder.Length; i++) if (o.BuildOrder[i].Action == AiStepAction.Expand) expand = i;
        Assert.True(expand > StepIndex(o, AiStepAction.Research, "age_ii"));
    }

    /// <summary>
    /// D33: the three kinds D32's text left out are named again, and each has its own train step (as the Shamans do), due no
    /// sooner than its hall's step. Was <c>TheKindsNotFieldedBy15_AreNotPromised</c> (D32: none of them in the text).
    /// </summary>
    [Theory]
    [InlineData("Breakers", "teblor_breaker", "teblor_breakers_hall")]
    [InlineData("Destrier Riders", "teblor_destrier_rider", "teblor_stables")]
    [InlineData("War-dogs", "teblor_war_dog", "teblor_stables")]
    public void TheKindsD33Names_HaveTheirOwnTrainStep(string words, string unit, string hall)
    {
        AiOpening o = Standard();
        Assert.Contains(words, Desc);
        int train = StepIndex(o, AiStepAction.Train, unit), build = StepIndex(o, AiStepAction.Build, hall);
        Assert.True(train >= 0 && build >= 0, $"{unit}: train step {train + 1}, {hall} step {build + 1}");
        Assert.True(o.BuildOrder[train].Count >= 1);
        Assert.True(o.BuildOrder[train].At >= o.BuildOrder[build].At, $"the {unit} step is due before its {hall}");
    }
}
