using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D4): the shared techs' text is read by every faction. TechContentTests.H forbids building, slot, unit and
/// faction names case-sensitively; these rows also forbid every faction tech's name ("Moranth Supply") and catch a
/// lower-cased name ("caster hall"). The second row checks the "needs" reader against <c>requires</c> for every tech
/// in the data (shared and faction), so a tech added later is covered without a new pin.
/// </summary>
public class SharedTechTextQaTests
{
    private static GameData Data => TestSim.Data;

    private static readonly string[] SlotNames =
    {
        "Town Hall", "House", "Camp", "Infantry Hall", "Ranged Hall", "Shock Hall", "Forge", "Caster Hall",
        "Siege Works", "Watch Tower",
    };

    [Fact]
    public void SharedTechText_NamesNoFactionThing_InAnyCase()
    {
        string[] forbidden = Data.Buildings.Select(b => b.DisplayName)
            .Concat(Data.Units.Select(u => u.DisplayName))
            .Concat(Data.Factions.Select(f => f.DisplayName))
            .Concat(Data.Techs.Where(t => t.Faction >= 0).Select(t => t.DisplayName))
            .Concat(SlotNames)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        TechDef[] shared = Data.Techs.Where(t => t.Faction == -1).ToArray();
        Assert.Equal(7, shared.Length);
        foreach (TechDef t in shared)
            foreach (string text in new[] { t.DisplayName, t.Description })
                foreach (string name in forbidden)
                    Assert.False(text.Contains(name, StringComparison.OrdinalIgnoreCase),
                        $"{t.Key}: shared text names '{name}' (any case): '{text}'");
    }

    [Fact]
    public void EveryTech_DescriptionNeeds_MatchRequires_AndAnyOfMatchesItsCount()
    {
        string[] countWords = { "zero", "one", "two", "three", "four" };
        Assert.Equal(9, Data.Techs.Length);
        foreach (TechDef t in Data.Techs)
        {
            RequiresText.AssertMatches(Data, t.Key, t.Description, t.Requires);
            var anyOf = RequiresText.AnyOf(t.Description);
            if (t.RequiresAnyOfCount == 0)
                Assert.True(anyOf == null, $"{t.Key}: description has an any-of clause but the data has none: '{t.Description}'");
            else
            {
                Assert.True(anyOf != null, $"{t.Key}: data any-of {t.RequiresAnyOfCount} but the description has no 'Needs <n> kinds of building: ...'");
                Assert.True(anyOf!.Value.Count == countWords[t.RequiresAnyOfCount],
                    $"{t.Key} any-of count: data {countWords[t.RequiresAnyOfCount]} vs description {anyOf.Value.Count}");
                Assert.True(anyOf.Value.Kinds.Length == t.RequiresAnyOfSlots.Length,
                    $"{t.Key} any-of kinds: data {t.RequiresAnyOfSlots.Length} slots vs description [{string.Join(", ", anyOf.Value.Kinds)}]");
            }
        }
    }
}
