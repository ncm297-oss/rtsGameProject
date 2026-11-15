using Rts.Sim.Data;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D32 (session 2026-11-14-1246): every claim in the Teblor <c>standard</c> opening's description (BUG-1390's fix) is
/// backed by the shipped opening's data, so the words and the steps can't drift apart silently: the two buildings named
/// "first" are built before Age II is researched, "four Warriors raid at 8 army pop" is the wave threshold over a
/// Warrior-only Age I mix, each unit kind named as joining is in the Age II mix and has a building step for its trainer,
/// Shamans also have their own train step, and "a second Longhouse" has an <c>expand</c> step. The kinds the shipped text
/// leaves out (the Breakers, Destrier Riders and War-dogs, fielded in no match by 15:00 on D32's row) stay out of it.
/// Measured play (how often each claim holds) is <see cref="D32OpeningQaStressTests"/>.
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
    public void TheDescription_IsD32sSentence()
    {
        Assert.Equal("War Lodge and Javelin Ground first, then Age II; four Warriors raid at 8 army pop, then Javelin Throwers and Shamans join them and a second Longhouse goes up.", Desc);
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

    [Theory]
    [InlineData("Breaker")]
    [InlineData("Destrier")]
    [InlineData("War-dog")]
    public void TheKindsNotFieldedBy15_AreNotPromised(string word) => Assert.DoesNotContain(word, Desc);
}
