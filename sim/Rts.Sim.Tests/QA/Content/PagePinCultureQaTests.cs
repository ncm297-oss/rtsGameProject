using System.Globalization;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D3, BUG-0111): the page pins read and write numbers in the invariant culture, so they pass the same on a machine
/// whose culture writes decimals with a comma ("7,5") or groups thousands with a dot. Each pin runs under such cultures.
/// </summary>
public class PagePinCultureQaTests
{
    public static IEnumerable<object[]> Cultures() =>
        new[] { "de-DE", "fr-FR", "tr-TR", "ar-SA" }.Select(c => new object[] { c });

    private static void Under(string culture, Action pin)
    {
        CultureInfo saved = CultureInfo.CurrentCulture, savedUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            pin();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            CultureInfo.CurrentUICulture = savedUi;
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void UnitPagePins_PassUnderCulture(string culture) => Under(culture, () =>
    {
        var t = new UnitContentTests();
        t.C_EveryNumber_MatchesThePage();
        t.G_ThePagesUnitTables_MatchTheRosterAbove();
        // D5: the Battering Ram note's "attacks buildings only" against attack.targets (case-insensitive regex, tr-TR row).
        t.H_TheUnitNotes_SayAttacksXOnly_ExactlyWhenTheDataNarrowsTargets();
    });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void BuildingPagePins_PassUnderCulture(string culture) => Under(culture, () =>
    {
        var t = new BuildingContentTests();
        t.C_EveryNumber_MatchesDocs02();
        t.G_ThePagesBuildingTables_MatchTheRosterAndTemplate();
        t.G2_Docs02Requires_IsWhatThePagesSubstitute();
        t.I_BUG0090_ADescriptionThatSaysNeedsX_HasExactlyThoseRequires();
    });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void TechPagePins_PassUnderCulture(string culture) => Under(culture, () =>
    {
        var t = new TechContentTests();
        t.A_EachFaction_ShipsExactlyItsFactionUpgrade_AsTheDocsSay();
        t.B_ThePagesFactionUpgradeLine_MatchesTheData();
        t.C_ThePagesTechsTable_MatchesTheData();
        t.D_Descriptions_StateEveryEffectWithItsNumber_AndTheirRequirements();
        // D4: the shared techs' pins to docs/02 "Forge upgrades" / "Ages".
        t.F_TheForgeUpgradesTable_MatchesTheSharedTechs();
        t.G_TheAgesSection_MatchesAgeII_AndItsDescription();
    });
}
