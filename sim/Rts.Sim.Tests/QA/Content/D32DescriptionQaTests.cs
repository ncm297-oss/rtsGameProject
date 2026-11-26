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
/// D38 (BUG-1612): the opening carries a build order per difficulty (Normal's <c>buildOrder</c>, Easy's and Hard's in
/// <c>buildOrders</c>) under the one description, so every step claim is read on each difficulty's list
/// (<see cref="AiOpening.BuildOrderFor"/>), by position in the file; the Shaman step's claim is position only, not due time
/// (docs/01, the second 2026-11-26 row, (3): the tooltip says "may follow"; Easy's K9KC10S has its Shaman step due at 9:00
/// behind a Circle due at 10:00).
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

    /// <summary>D38: the list each difficulty plays, Easy, Normal, Hard.</summary>
    private static (AiDifficulty Diff, System.Collections.Immutable.ImmutableArray<AiStep> Steps)[] Lists()
    {
        AiOpening o = Standard();
        return Enum.GetValues<AiDifficulty>().Select(x => (x, o.BuildOrderFor(x))).ToArray();
    }

    private static int StepIndex(System.Collections.Immutable.ImmutableArray<AiStep> steps, AiStepAction action, string key)
    {
        for (int i = 0; i < steps.Length; i++)
            if (steps[i].Action == action && steps[i].TargetKey == key) return i;
        return -1;
    }

    private static string Desc => Standard().Description;

    [Fact]
    public void TheDescription_IsD34sSentence()
    {
        // D34 fix round 1 (2026-11-18, BUG-1641 / BUG-1613): was "... then Javelin Throwers, Shamans, Breakers, Destrier Riders and
        // War-dogs join them and a second Longhouse goes up." (D33's; the test was TheDescription_IsD33sSentence).
        // D33 (2026-11-17): was "... then Javelin Throwers and Shamans join them and a second Longhouse goes up." (D32's, BUG-1390).
        Assert.Equal("War Lodge and Javelin Ground first, then Age II; four Warriors raid at 8 army pop, then Javelin Throwers and War-dogs join them; Shamans, Breakers, Destrier Riders and a second Longhouse may follow.", Desc);
    }

    [Fact]
    public void WarLodgeAndJavelinGroundFirst_ThenAgeII_IsTheStepOrder()
    {
        foreach (var (x, steps) in Lists())
        {
            int age = StepIndex(steps, AiStepAction.Research, "age_ii");
            Assert.True(age >= 0, $"{x}: no Age II step");
            foreach (string b in new[] { "teblor_war_lodge", "teblor_javelin_ground" })
            {
                int i = StepIndex(steps, AiStepAction.Build, b);
                Assert.True(i >= 0 && i < age, $"{x}: {b} is step {i + 1}, Age II step {age + 1}");
                Assert.Equal(AiStepTrigger.Pop, steps[i].Trigger); // a pop step: due long before Age II's 6:00
            }
            // No other non-house building is due before Age II.
            for (int i = 0; i < age; i++)
            {
                AiStep s = steps[i];
                if (s.Action != AiStepAction.Build) continue;
                Assert.Contains(s.TargetKey, new[] { "teblor_hut", "teblor_war_lodge", "teblor_javelin_ground", "teblor_woodcutters_lodge" });
            }
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
        foreach (var (x, steps) in Lists())
            Assert.True(StepIndex(steps, AiStepAction.Build, trainer) >= 0, $"{x}: no build step for {trainer}");
    }

    [Fact]
    public void Shamans_HaveTheirOwnTrainStep_AfterTheCircleStep()
    {
        // D38: per list, by position in the file (docs/01, the second 2026-11-26 row, (3)); the due-time check (the Shaman
        // step due no sooner than its Circle) is gone by that ruling: Easy's Shaman step is due at 9:00, its Circle at 10:00,
        // and the step waits for the Circle.
        foreach (var (x, steps) in Lists())
        {
            int circle = StepIndex(steps, AiStepAction.Build, "teblor_shamans_circle");
            int train = StepIndex(steps, AiStepAction.Train, "teblor_shaman");
            Assert.True(circle >= 0 && train > circle, $"{x}: Circle step {circle + 1}, Shaman step {train + 1}");
            Assert.True(steps[train].Count >= 1, $"{x}: the Shaman step trains none");
        }
    }

    [Fact]
    public void ASecondLonghouseGoesUp_IsAnExpandStep_AfterAgeII()
    {
        Assert.Contains("a second Longhouse may follow", Desc); // D34 fix round 1: was "a second Longhouse goes up" (D33's text)
        foreach (var (x, steps) in Lists())
        {
            int expand = -1;
            for (int i = 0; i < steps.Length; i++) if (steps[i].Action == AiStepAction.Expand) expand = i;
            Assert.True(expand > StepIndex(steps, AiStepAction.Research, "age_ii"), $"{x}: no expand step after Age II");
        }
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
        Assert.Contains(words, Desc);
        foreach (var (x, steps) in Lists())
        {
            int train = StepIndex(steps, AiStepAction.Train, unit), build = StepIndex(steps, AiStepAction.Build, hall);
            Assert.True(train >= 0 && build >= 0, $"{x}: {unit}: train step {train + 1}, {hall} step {build + 1}");
            Assert.True(steps[train].Count >= 1, $"{x}: the {unit} step trains none");
            Assert.True(steps[train].At >= steps[build].At, $"{x}: the {unit} step is due before its {hall}");
        }
    }
}
