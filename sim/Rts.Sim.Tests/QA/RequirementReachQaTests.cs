using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M3-H2, session 2026-10-07-1715): BUG-0100's load errors at depth. Requirement chains three and four links long
/// that end at another faction's building or tech, any-of fields whose members are cut off through faction techs and
/// other halls, a mirror for each faction, and the false-positive side (a deep chain that does reach).
/// </summary>
public class RequirementReachQaTests
{
    private const string Common = "common/techs.json";
    private const string MalazanBuildings = "factions/malazan/buildings.json";
    private const string MalazanUnits = "factions/malazan/units.json";

    private static readonly string[] Files =
    {
        Common, "factions/malazan/techs.json", "factions/whirlwind/techs.json", MalazanBuildings,
        "factions/whirlwind/buildings.json", MalazanUnits, "factions/whirlwind/units.json",
    };

    private readonly ITestOutputHelper _out;

    public RequirementReachQaTests(ITestOutputHelper output) => _out = output;

    private static void SetRequires(TestDataDir dir, string id, string raw)
    {
        foreach (string rel in Files)
        {
            bool hit = false;
            JsonObject root = JsonNode.Parse(File.ReadAllText(dir.FullPath(rel)))!.AsObject();
            foreach ((string _, JsonNode? list) in root)
                foreach (JsonNode? e in list!.AsArray())
                    if ((string)e!["id"]! == id)
                    {
                        e["requires"] = JsonNode.Parse(raw);
                        hit = true;
                    }
            if (!hit) continue;
            File.WriteAllText(dir.FullPath(rel), root.ToJsonString());
            return;
        }
        throw new InvalidOperationException($"no def '{id}'");
    }

    private DataError OneError(TestDataDir dir)
    {
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        foreach (DataError e in r.Errors) _out.WriteLine($"{e.File} {e.Path}: {e.Message}");
        Assert.Null(r.Data);
        return Assert.Single(r.Errors);
    }

    private void Loads(TestDataDir dir)
    {
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
    }

    /// <summary>Heavy Infantry -> Moranth Supply -> Cadre Tower -> the Whirlwind Shrine: one error, at the entry that leaves the faction.</summary>
    [Fact]
    public void AThreeLinkChainEndingAtAnotherFactionsBuilding_IsOneError_AtTheCrossingEntry()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, "malazan_heavy_infantry", "[\"moranth_supply\"]");
        SetRequires(dir, "moranth_supply", "[\"malazan_cadre_tower\"]");
        SetRequires(dir, "malazan_cadre_tower", "[\"whirlwind_shrine\"]");
        DataError e = OneError(dir);
        Assert.Equal(MalazanBuildings, e.File);
        Assert.EndsWith(".requires[0]", e.Path);
        Assert.Contains("whirlwind_shrine", e.Message);
    }

    /// <summary>A unit naming the other faction's tech can never be trained: one error at the entry.</summary>
    [Fact]
    public void AUnitRequiringAnotherFactionsTech_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, "malazan_heavy_infantry", "[\"age_ii\", \"dryjhnas_prophecy\"]");
        DataError e = OneError(dir);
        Assert.Equal(MalazanUnits, e.File);
        Assert.EndsWith(".requires[1]", e.Path);
    }

    /// <summary>
    /// Age II's any-of (2 of the four halls), with Malazan's cut off at depth: Barracks -> Moranth Supply -> Crossbow
    /// Range -> Age II, and the Corral -> Age II. Only the Armory is reachable: one error at the field, naming Malazan only.
    /// </summary>
    [Fact]
    public void AnAnyOfCutOffThroughAFactionTechThreeLinksDeep_IsOneErrorAtTheField()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, "malazan_barracks", "[\"moranth_supply\"]");
        SetRequires(dir, "moranth_supply", "[\"malazan_crossbow_range\"]");
        SetRequires(dir, "malazan_crossbow_range", "[\"age_ii\"]");
        SetRequires(dir, "malazan_wickan_corral", "[\"age_ii\"]");
        DataError e = OneError(dir);
        Assert.Equal(Common, e.File);
        Assert.EndsWith(".requiresAnyOf", e.Path);
        Assert.Contains("malazan", e.Message);
        Assert.DoesNotContain("whirlwind", e.Message);
    }

    /// <summary>
    /// The false-positive side, four links: Armory (free) -> Moranth Supply -> Barracks -> Corral, Crossbow Range behind
    /// Age II. Three halls reachable without Age II, so it loads.
    /// </summary>
    [Fact]
    public void AnAnyOfReachedThroughAFourLinkChain_Loads()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, "moranth_supply", "[\"malazan_armory\"]");
        SetRequires(dir, "malazan_barracks", "[\"moranth_supply\"]");
        SetRequires(dir, "malazan_wickan_corral", "[\"malazan_barracks\"]");
        SetRequires(dir, "malazan_crossbow_range", "[\"age_ii\"]");
        Loads(dir);
    }

    /// <summary>The Whirlwind mirror: Raider Camp and Archer Camp behind Age II, Horse Lines behind the Raider Camp: one error naming Whirlwind only.</summary>
    [Fact]
    public void AnAnyOfCutOffForWhirlwindOnly_NamesWhirlwindOnly()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, "whirlwind_raider_camp", "[\"age_ii\"]");
        SetRequires(dir, "whirlwind_archer_camp", "[\"age_ii\"]");
        DataError e = OneError(dir);
        Assert.Equal(Common, e.File);
        Assert.Contains("whirlwind", e.Message);
        Assert.DoesNotContain("malazan", e.Message);
    }

    /// <summary>Both factions cut off: still one error at the field, naming both.</summary>
    [Fact]
    public void AnAnyOfCutOffForBothFactions_IsOneErrorNamingBoth()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        foreach (string id in new[] { "whirlwind_raider_camp", "whirlwind_archer_camp", "malazan_barracks", "malazan_crossbow_range", "malazan_wickan_corral" })
            SetRequires(dir, id, "[\"age_ii\"]");
        DataError e = OneError(dir);
        Assert.Contains("whirlwind", e.Message);
        Assert.Contains("malazan", e.Message);
    }

    /// <summary>
    /// A building that requires a tech researched only at a building of its own slot: the Armory (forge) requiring
    /// Melee Weapons (researched at the forge). No Malazan player can ever finish an Armory, so nothing researched at the
    /// forge is reachable either. docs/03 and BUG-0100 ask that a requirement that can never be met be a load error; this
    /// one loads. BUG-0134.
    /// </summary>
    [Theory(Skip = "BUG-0134: a building requiring a tech researched only at its own slot loads clean")]
    [InlineData("malazan_armory", "[\"melee_weapons_1\"]")]
    [InlineData("malazan_armory", "[\"moranth_supply\"]")]
    public void ABuildingRequiringATechResearchedOnlyAtItself_IsAnError(string id, string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, id, raw);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        Assert.NotEmpty(r.Errors);
    }

    /// <summary>Pin for BUG-0134 (today's behaviour). Flip to the skipped theory above when fixed.</summary>
    [Theory]
    [InlineData("malazan_armory", "[\"melee_weapons_1\"]")]
    [InlineData("malazan_armory", "[\"moranth_supply\"]")]
    public void Bug0134Pin_ABuildingRequiringATechResearchedOnlyAtItself_LoadsClean(string id, string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, id, raw);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, "BUG-0134 looks fixed: flip this pin. " + string.Join("; ", r.Errors));
    }
}
