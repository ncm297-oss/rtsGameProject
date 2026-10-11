using Rts.Sim.Data;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M4-V7b: which ability kinds the card casts with no target (<see cref="AbilityCaster.IsNoTarget"/>).</summary>
public class NoTargetKindTests
{
    [Theory]
    [InlineData(AbilityKind.TargetGround, false)]
    [InlineData(AbilityKind.SelfAura, true)]
    [InlineData(AbilityKind.Summon, true)]
    public void IsNoTarget_SelfAuraAndSummon(AbilityKind kind, bool noTarget) => Assert.Equal(noTarget, AbilityCaster.IsNoTarget(kind));

    [Fact]
    public void TheSimsTestSummon_IsNoTarget_WithNoRangeOrRadius()
    {
        AbilityDef s = SummonTests.Data.Abilities[SummonTests.Data.FindAbility("zz_test_summon")];
        Assert.True(AbilityCaster.IsNoTarget(s.Kind));
        Assert.Equal((0f, 0f), (s.Range, s.Radius));
    }
}
