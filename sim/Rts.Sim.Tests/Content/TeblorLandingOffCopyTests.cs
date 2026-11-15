using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// BUG-1400 (default loop): the "without the rows" copy of <see cref="TeblorLandingOpeningReportTests"/> really lacks the
/// landing's rows now that the shipped files carry them (D31), and the "with" copy has them, so the Stress report compares
/// two different games again. Before the fix the "off" copy was the shipped data, which has <c>teblor_frenzied</c>.
/// </summary>
public class TeblorLandingOffCopyTests
{
    [Fact]
    public void TheOffCopy_IsWithoutTheRows_AndTheOnCopy_IsWith()
    {
        JsonObject opening = TeblorOpeningVariantsReportTests.D28();
        GameData off = TeblorLandingOpeningReportTests.Make(opening, rows: false, share: 1.0);
        GameData on = TeblorLandingOpeningReportTests.Make(opening, rows: true, share: 1.0);

        Assert.True(off.FindStatus(TeblorLanding.Status) < 0, "the off copy still has the landing's status (BUG-1400)");
        Assert.True(on.FindStatus(TeblorLanding.Status) >= 0, "the on copy lacks the landing's status");
        Assert.Equal(off.FindStatus("frenzied"), FrenzyStatus(off));
        Assert.Equal(on.FindStatus(TeblorLanding.Status), FrenzyStatus(on));
    }

    /// <summary>The status the Teblor's Blood-oil Frenzy applies in <paramref name="data"/>.</summary>
    private static int FrenzyStatus(GameData data)
    {
        AbilityDef frenzy = data.Abilities[data.FindAbility("blood_oil_frenzy")];
        Assert.Single(frenzy.Effects);
        return frenzy.Effects[0].Status;
    }
}
