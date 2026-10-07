using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResearchMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M3-6 (2026-10-07-1415): the requirement gates at the tick boundaries (a requirement met in phase 3 / 4 of tick D
/// opens a command applying in phase 1 of D + 1, never D; destroyed between ticks closes it; phase 1's own sequence order
/// decides inside a tick), the mirror match (an enemy of the same faction), hostile gate arguments, and the loader's
/// requires / requiresAnyOf / duplicate-key rules under hostile data.
/// </summary>
[Collection(SerialCollection.Name)]
public class RequirementQaTests
{
    private const string Common = "common/techs.json";
    private const string MalazanTechs = "factions/malazan/techs.json";
    private const string MalazanBuildings = "factions/malazan/buildings.json";

    private static GameData Fixture => RequirementGatingTests.Fixture; // Heavy Infantry needs a Crossbow Range; Cadre Tower needs Age II
    private static int Range => TestSim.Data.FindBuilding("malazan_crossbow_range");
    private static int CadreTower => TestSim.Data.FindBuilding("malazan_cadre_tower");

    private static Simulation NewSim(GameData? data = null, int players = 2) => RequirementGatingTests.NewSim(data, players);

    // ---------- the phase boundary, pinned on both sides ----------

    /// <summary>
    /// Runs one tick at a time, enqueuing <paramref name="enqueue"/> before each; returns (the tick in which
    /// <paramref name="met"/> first held after the tick ran, the tick in which <paramref name="queued"/> first held).
    /// </summary>
    private static (int Met, int Queued) Race(Simulation sim, int maxTicks, Action enqueue, Func<bool> met, Func<bool> queued)
    {
        int metAt = -1, queuedAt = -1;
        for (int n = 0; n < maxTicks && queuedAt < 0; n++)
        {
            enqueue();
            int tick = sim.TickNumber;
            sim.Tick();
            if (metAt < 0 && met()) metAt = tick;
            if (queuedAt < 0 && queued()) queuedAt = tick;
        }
        Assert.True(metAt >= 0, "the requirement was never met");
        Assert.True(queuedAt >= 0, "the command was never accepted");
        return (metAt, queuedAt);
    }

    [Fact]
    public void ALevelTwoResearch_ApplyingInTheTickAgeIICompletes_IsRefused_AndTheNextTicksIsAccepted()
    {
        Simulation sim = NewSim(players: 1);
        World w = sim.World;
        int keep = Building(sim, 4, 4).Index;
        Building(sim, 12, 4, type: Barracks);
        int armory = Building(sim, 18, 4, type: Armory).Index;
        Give(sim, 0, 100_000, 100_000);
        w.Techs.Set(0, Melee1, true);
        Apply(sim, Command.Research(0, In(sim, keep), AgeII));
        Assert.Equal(1, w.Buildings.QueueCount[keep]);
        int goldBefore = -1;
        (int met, int queued) = Race(sim, 1400,
            () =>
            {
                if (w.Buildings.QueueCount[armory] == 0) goldBefore = w.Gold[0];
                sim.Enqueue(Command.Research(0, In(sim, armory), Melee2));
            },
            () => w.HasTech(0, AgeII),
            () => w.Buildings.QueueCount[armory] > 0);
        // Age II completes in phase 3 of tick D; the Research applying in phase 1 of D still saw no Age II.
        Assert.Equal(met + 1, queued);
        Assert.Equal(goldBefore - w.Data.Techs[Melee2].CostGold, w.Gold[0]); // every refused one was free
    }

    [Fact]
    public void ATrain_ApplyingInTheTickTheRequiredSiteCompletes_IsRefused_AndTheNextTicksIsAccepted()
    {
        Simulation sim = NewSim(Fixture, players: 1);
        World w = sim.World;
        int bar = Building(sim, 4, 4, type: Barracks).Index;
        Building(sim, 4, 20); // Keep, for population
        Give(sim, 0, 100_000, 100_000);
        EntityHandle worker = Unit(sim, At(sim, 14, 10));
        Apply(sim, Command.Build(0, worker, Range, At(sim, 16, 10)));
        Assert.Equal(0, w.Ledger.FinishedOfType(0, Range));
        (int met, int queued) = Race(sim, 5000,
            () => sim.Enqueue(Command.Train(0, In(sim, bar), Infantry)),
            () => w.Ledger.FinishedOfType(0, Range) > 0,
            () => w.Buildings.QueueCount[bar] > 0);
        // The site completes in phase 4 (construction) of tick D; phase 1 of D ran first.
        Assert.Equal(met + 1, queued);
    }

    [Fact]
    public void AgeII_ApplyingInTheTickTheSecondHallCompletes_IsRefused_AndTheNextTicksIsAccepted()
    {
        Simulation sim = NewSim(players: 1);
        World w = sim.World;
        int keep = Building(sim, 4, 4).Index;
        Building(sim, 4, 20, type: Barracks);
        Give(sim, 0, 100_000, 100_000);
        EntityHandle worker = Unit(sim, At(sim, 14, 10));
        Apply(sim, Command.Build(0, worker, Armory, At(sim, 16, 10)));
        (int met, int queued) = Race(sim, 5000,
            () => sim.Enqueue(Command.Research(0, In(sim, keep), AgeII)),
            () => w.Ledger.FinishedInSlot(0, (int)BuildingSlot.Forge) > 0,
            () => w.Buildings.QueueCount[keep] > 0);
        Assert.Equal(met + 1, queued);
    }

    [Fact]
    public void TheOnlyRequiredBuilding_DestroyedBeforeTheApplyingTick_RefusesTheTrain_AndAfterItTheItemSurvives()
    {
        Simulation sim = NewSim(Fixture, players: 1);
        World w = sim.World;
        int bar = Building(sim, 4, 4, type: Barracks).Index;
        Building(sim, 4, 20);
        Give(sim, 0, 100_000, 100_000);

        // Destroyed between the stamping tick and the applying tick: refused, nothing paid.
        EntityHandle range = Building(sim, 20, 4, type: Range);
        sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        sim.Tick();
        w.Buildings.Damage(range, int.MaxValue);
        (int gold, int wood) = (w.Gold[0], w.Wood[0]);
        sim.Tick();
        Assert.Equal((0, gold, wood), (w.Buildings.QueueCount[bar], w.Gold[0], w.Wood[0]));

        // Destroyed right after the applying tick: the item was accepted and trains (queue-time rule).
        range = Building(sim, 20, 4, type: Range);
        Apply(sim, Command.Train(0, In(sim, bar), Infantry));
        Assert.Equal(1, w.Buildings.QueueCount[bar]);
        w.Buildings.Damage(range, int.MaxValue);
        int units = w.Units.Count;
        Run(sim, w.Buildings.TrainTicks(Infantry) + 2);
        Assert.Equal(units + 1, w.Units.Count);
    }

    [Fact]
    public void InsidePhaseOne_SequenceOrderDecides_ASpawnBeforeTheTrainOpensIt_AfterItDoesNot()
    {
        Simulation sim = NewSim(Fixture, players: 1);
        World w = sim.World;
        int bar = Building(sim, 4, 4, type: Barracks).Index;
        Building(sim, 4, 20);
        Give(sim, 0, 100_000, 100_000);
        // Train first, then the (dev) spawn of the Range, same tick: the Train saw no Range.
        sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        sim.Enqueue(Command.SpawnBuilding(0, Range, At(sim, 20, 4)));
        Run(sim, 2);
        Assert.Equal(1, w.Ledger.FinishedOfType(0, Range));
        Assert.Equal(0, w.Buildings.QueueCount[bar]);
        // Spawn first, then the Train: open.
        sim.Enqueue(Command.SpawnBuilding(0, Range, At(sim, 28, 4)));
        sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        Run(sim, 2);
        Assert.Equal(1, w.Buildings.QueueCount[bar]);
    }

    // ---------- the mirror match: an enemy of the same faction ----------

    [Fact]
    public void AnEnemyOfTheSameFaction_WithEveryRequirementMet_OpensNothingForThePlayer()
    {
        // Players 0 and 2 are both Malazan (faction = player % factions).
        Simulation sim = NewSim(Fixture, players: 3);
        World w = sim.World;
        Assert.Equal(w.FactionOf(0), w.FactionOf(2));
        int keep = Building(sim, 4, 4).Index;
        int bar = Building(sim, 12, 4, type: Barracks).Index;
        Building(sim, 4, 20, player: 2, type: Barracks);
        Building(sim, 12, 20, player: 2, type: Range);
        Building(sim, 20, 20, player: 2, type: Armory);
        w.Techs.Set(2, AgeII, true);
        Give(sim, 0, 100_000, 100_000);
        Assert.False(w.CanTrain(0, bar, Infantry, out TrainError te));
        Assert.Equal(TrainError.LockedByRequirement, te);
        Assert.False(w.CanResearch(0, keep, AgeII, out ResearchError re));
        Assert.Equal(ResearchError.Requires, re);
        Assert.False(w.CanPlace(0, CadreTower, Cell(sim, 30, 10), out PlacementError pe));
        Assert.Equal(PlacementError.Requires, pe);
        // And the enemy is open for all three on its own side.
        Assert.True(w.Ledger.FinishedInSlot(2, (int)BuildingSlot.RangedHall) == 1 && w.Ledger.FinishedOfType(0, Range) == 0);
    }

    [Fact]
    public void AFactionTechsAnyOf_NamingItsOwnBuilding_GatesOnThatBuildingsSlot()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanTechs, root => root["techs"]![0]!["requiresAnyOf"] = JsonNode.Parse("{\"count\": 1, \"of\": [\"malazan_crossbow_range\"]}"));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Simulation sim = NewSim(r.Data, players: 1);
        World w = sim.World;
        int armory = Building(sim, 4, 4, type: Armory).Index;
        Give(sim, 0, 100_000, 100_000);
        w.Techs.Set(0, AgeII, true);
        Assert.False(w.CanResearch(0, armory, Moranth, out ResearchError why));
        Assert.Equal(ResearchError.Requires, why);
        Building(sim, 12, 4, type: Range);
        Assert.True(w.CanResearch(0, armory, Moranth, out why), why.ToString());
    }

    // ---------- hostile gate arguments: an answer, never a throw ----------

    [Fact]
    public void TheGates_AnswerHostileArguments_WithoutThrowing()
    {
        Simulation sim = NewSim(Fixture, players: 2);
        World w = sim.World;
        int keep = Building(sim, 4, 4).Index;
        int[] players = { -1, 0, 1, 2, int.MaxValue, int.MinValue };
        int[] slots = { -1, keep, w.Buildings.Capacity, int.MaxValue };
        int[] types = { -1, 0, w.Data.Units.Length, w.Data.Buildings.Length, w.Data.Techs.Length, int.MaxValue, int.MinValue };
        int[] cells = { -1, 0, Cell(sim, 20, 10), int.MaxValue };
        foreach (int p in players)
        {
            foreach (int s in slots)
                foreach (int t in types)
                {
                    if (w.CanTrain(p, s, t, out _)) Assert.True(p == 0 && s == keep, $"CanTrain({p},{s},{t})");
                    if (w.CanResearch(p, s, t, out _)) Assert.True(p == 0 && s == keep, $"CanResearch({p},{s},{t})");
                }
            foreach (int t in types)
                foreach (int c in cells)
                    w.CanPlace(p, t, c, out _);
            if (p is not (0 or 1)) Assert.Equal(0, w.Ledger.FinishedOfType(p, Keep));
            Assert.Equal(0, w.Ledger.FinishedInSlot(p, int.MaxValue));
        }
    }

    // ---------- loader: cycles of length 1-4 through techs and buildings ----------

    private static DataLoadResult Load(TestDataDir dir) => DataLoader.LoadAll(dir.Path);

    private static void SetRequires(TestDataDir dir, string id, string raw)
    {
        foreach (string rel in new[] { Common, MalazanTechs, "factions/whirlwind/techs.json", MalazanBuildings, "factions/whirlwind/buildings.json", "factions/malazan/units.json", "factions/whirlwind/units.json" })
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

    [Fact]
    public void AFourCycle_TechBuildingTechBuilding_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, "armor_1", "[\"malazan_cadre_tower\"]");
        SetRequires(dir, "malazan_cadre_tower", "[\"ranged_weapons_1\"]");
        SetRequires(dir, "ranged_weapons_1", "[\"malazan_watchtower\"]");
        SetRequires(dir, "malazan_watchtower", "[\"armor_1\"]");
        DataLoadResult r = Load(dir);
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors);
        Assert.Contains("requires cycle", e.Message);
        Assert.Matches(@"\.requires\[0\]$", e.Path);
    }

    [Fact]
    public void AThreeBuildingCycle_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, "malazan_barracks", "[\"malazan_crossbow_range\"]");
        SetRequires(dir, "malazan_crossbow_range", "[\"malazan_wickan_corral\"]");
        SetRequires(dir, "malazan_wickan_corral", "[\"malazan_barracks\"]");
        DataLoadResult r = Load(dir);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(MalazanBuildings, e.File);
        Assert.Contains("requires cycle", e.Message);
    }

    [Fact]
    public void TwoDisjointCycles_AreTwoErrors_AndAUnitHangingOffOneAddsNone()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, "melee_weapons_1", "[\"melee_weapons_2\"]");        // 2-cycle with melee_weapons_2
        SetRequires(dir, "malazan_billet", "[\"malazan_billet\"]");           // self
        SetRequires(dir, "malazan_heavy_infantry", "[\"malazan_billet\"]");   // a unit needing a node of a cycle
        DataLoadResult r = Load(dir);
        Assert.Null(r.Data);
        Assert.Equal(2, r.Errors.Count);
        Assert.Contains(r.Errors, e => e.File == Common && e.Message.Contains("requires cycle"));
        Assert.Contains(r.Errors, e => e.File == MalazanBuildings && e.Message.Contains("requires itself"));
    }

    [Fact]
    public void TwoCyclesSharingANode_AreReported_AtRequiresEntries_WithoutACrash()
    {
        // armor_1 <-> malazan_cadre_tower and armor_1 <-> malazan_watchtower (a figure eight through armor_1).
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, "armor_1", "[\"malazan_cadre_tower\", \"malazan_watchtower\"]");
        SetRequires(dir, "malazan_cadre_tower", "[\"armor_1\"]");
        SetRequires(dir, "malazan_watchtower", "[\"armor_1\"]");
        DataLoadResult r = Load(dir);
        Assert.Null(r.Data);
        Assert.InRange(r.Errors.Count, 1, 2);
        Assert.All(r.Errors, e => Assert.Contains(".requires[", e.Path));
        Assert.All(r.Errors, e => Assert.Contains("cycle", e.Message));
    }

    [Fact]
    public void ACycleAcrossFactions_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, "whirlwind_shrine", "[\"malazan_cadre_tower\"]");
        SetRequires(dir, "malazan_cadre_tower", "[\"whirlwind_shrine\"]");
        DataLoadResult r = Load(dir);
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.Message.Contains("requires cycle"));
    }

    // ---------- loader: requiresAnyOf.count of the wrong JSON type ----------

    [Theory]
    [InlineData("\"2\"")]
    [InlineData("2.5")]
    [InlineData("2.0")]
    [InlineData("1e10")]
    [InlineData("-2")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[2]")]
    public void ACountOfTheWrongTypeOrRange_IsOneErrorInTheCommonTechs(string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string text = File.ReadAllText(dir.FullPath(Common));
        Assert.Contains("\"count\": 2,", text);
        File.WriteAllText(dir.FullPath(Common), text.Replace("\"count\": 2,", $"\"count\": {raw},"));
        DataLoadResult r = Load(dir);
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(Common, e.File);
        Assert.False(string.IsNullOrWhiteSpace(e.Message));
    }

    [Theory]
    [InlineData("{\"count\": 1, \"of\": \"forge\"}")]
    [InlineData("{\"count\": 1, \"of\": [null]}")]
    [InlineData("{\"count\": 1, \"of\": [\"\"]}")]
    [InlineData("{\"count\": 1, \"of\": [7]}")]
    [InlineData("[\"forge\"]")]
    [InlineData("\"forge\"")]
    public void AnAnyOfOfTheWrongShape_IsOneError(string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(Common, root => root["techs"]![0]!["requiresAnyOf"] = JsonNode.Parse(raw));
        DataLoadResult r = Load(dir);
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(Common, e.File);
    }

    [Fact]
    public void AnAnyOfNamingTheOtherFactionsBuilding_InTheFactionsOwnFile_IsOneError_EvenWithTheSameSlot()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        // whirlwind_smithy is the Whirlwind forge: same slot as the Malazan Armory, still not Malazan's.
        dir.EditJson(MalazanTechs, root => root["techs"]![0]!["requiresAnyOf"] = JsonNode.Parse("{\"count\": 1, \"of\": [\"whirlwind_smithy\"]}"));
        DataLoadResult r = Load(dir);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal((MalazanTechs, "techs[0].requiresAnyOf.of[0]"), (e.File, e.Path));
    }

    // ---------- loader: duplicate keys at every depth of every file kind ----------

    /// <summary>Inserts <paramref name="insert"/> right after occurrence <paramref name="occurrence"/> (0-based) of <paramref name="anchor"/>.</summary>
    private static void InsertAfter(TestDataDir dir, string rel, string anchor, int occurrence, string insert)
    {
        string text = File.ReadAllText(dir.FullPath(rel));
        int at = -1;
        for (int n = 0; n <= occurrence; n++)
        {
            at = text.IndexOf(anchor, at + 1, StringComparison.Ordinal);
            Assert.True(at >= 0, $"anchor '{anchor}' #{n} not in {rel}");
        }
        at += anchor.Length;
        File.WriteAllText(dir.FullPath(rel), text[..at] + insert + text[at..]);
    }

    [Theory]
    // root level: the empty copy comes first, so last-wins would hide it completely
    [InlineData("common/damage_table.json", "{", 0, "\"damageTypes\": [], ", "damageTypes")]
    [InlineData("common/rules.json", "{", 0, "\"popCap\": 5, ", "popCap")]
    [InlineData("common/resources.json", "{", 0, "\"resources\": [], ", "resources")]
    [InlineData("factions/malazan/faction.json", "{", 0, "\"id\": \"whirlwind\", ", "id")]
    [InlineData("factions/malazan/units.json", "{", 0, "\"units\": [], ", "units")]
    [InlineData("factions/malazan/buildings.json", "{", 0, "\"buildings\": [], ", "buildings")]
    [InlineData("common/techs.json", "{", 0, "\"techs\": [], ", "techs")]
    // depth 2-4, and in a later array element
    [InlineData("common/damage_table.json", "\"multipliers\": { ", 1, "\"light\": 9.0, ", @"damageTypes\[1\]\.multipliers\.light")]
    [InlineData("common/rules.json", "\"gatherRate\": { ", 0, "\"gold\": 9.0, ", @"gatherRate\.gold")]
    [InlineData("common/resources.json", "\"footprint\": { ", 1, "\"width\": 3, ", @"resources\[1\]\.footprint\.width")]
    [InlineData("factions/malazan/faction.json", "\"gold\": { ", 0, "\"displayName\": \"Lead\", ", @"resources\.gold\.displayName")]
    [InlineData("factions/malazan/buildings.json", "\"cost\": { ", 3, "\"gold\": 1, ", @"buildings\[3\]\.cost\.gold")]
    [InlineData("common/techs.json", "\"requiresAnyOf\": { ", 0, "\"count\": 4, ", @"techs\[0\]\.requiresAnyOf\.count")]
    [InlineData("factions/malazan/techs.json", "\"appliesTo\": { ", 1, "\"units\": [\"malazan_sapper\"], ", @"techs\[0\]\.effects\[1\]\.appliesTo\.units")]
    // an escaped spelling of the same key is the same key
    [InlineData("factions/malazan/buildings.json", "\"hp\": 2400,", 0, " \"h\\u0070\": 1,", @"buildings\[0\]\.hp")]
    public void ADuplicateKey_AtAnyDepth_IsOneErrorAtItsPath(string rel, string anchor, int occurrence, string insert, string pathPattern)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        InsertAfter(dir, rel, anchor, occurrence, insert);
        DataLoadResult r = Load(dir);
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(rel, e.File);
        Assert.Matches("^" + pathPattern + "$", e.Path);
        Assert.Contains("duplicate key", e.Message);
    }

    [Fact]
    public void ADuplicateKey_InAFileWithAByteOrderMark_IsStillReported_AndTheBomAloneLoads()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string rel = "common/rules.json";
        byte[] bom = { 0xEF, 0xBB, 0xBF };
        byte[] body = File.ReadAllBytes(dir.FullPath(rel));
        File.WriteAllBytes(dir.FullPath(rel), bom.Concat(body).ToArray());
        Assert.True(Load(dir).Ok, string.Join("\n", Load(dir).Errors));
        InsertAfter(dir, rel, "\"gatherRate\": { ", 0, "\"wood\": 0.6, ");
        DataError e = Assert.Single(Load(dir).Errors);
        Assert.Equal("gatherRate.wood", e.Path);
    }

    [Fact]
    public void ThreeCopiesOfOneKey_AreTwoErrors_AndTwoDuplicatesInOneFileAreTwo()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string rel = "common/rules.json";
        InsertAfter(dir, rel, "{", 0, "\"popCap\": 1, \"popCap\": 2, ");
        InsertAfter(dir, rel, "\"repair\": { ", 0, "\"rateFactor\": 0.5, ");
        DataLoadResult r = Load(dir);
        Assert.Equal(3, r.Errors.Count);
        Assert.Equal(2, r.Errors.Count(e => e.Path == "popCap"));
        Assert.Single(r.Errors, e => e.Path == "repair.rateFactor");
    }

    // ---------- content that loads but can never be met (BUG-0100) ----------

    [Fact]
    public void BUG0100_Today_ARequirementOnAnotherFactionsBuilding_Loads()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, "malazan_heavy_infantry", "[\"whirlwind_raider_camp\"]");
        DataLoadResult r = Load(dir);
        Assert.True(r.Ok, "BUG-0100 fixed? then flip this pin to the skipped test below: " + string.Join("; ", r.Errors));
        // ...and a Malazan player can never train it (the dev spawn aside, no Malazan worker can place a Raider Camp).
        Simulation sim = NewSim(r.Data, players: 1);
        int bar = Building(sim, 4, 4, type: Barracks).Index;
        Give(sim, 0, 100_000, 100_000);
        Assert.False(sim.World.CanTrain(0, bar, Infantry, out TrainError why));
        Assert.Equal(TrainError.LockedByRequirement, why);
        Assert.False(sim.World.CanPlace(0, RaiderCamp, Cell(sim, 20, 10), out PlacementError pe));
        Assert.Equal(PlacementError.WrongFaction, pe);
    }

    [Fact]
    public void BUG0100_Today_ACommonTechRequiringOneFactionsBuilding_Loads()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, "armor_1", "[\"malazan_barracks\"]"); // Whirlwind can never research Armor 1 (nor Armor 2)
        DataLoadResult r = Load(dir);
        Assert.True(r.Ok, "BUG-0100 fixed? then flip this pin: " + string.Join("; ", r.Errors));
    }

    [Fact]
    public void BUG0100_Today_AnAnyOfThatOnlyItsOwnTechCanOpen_Loads()
    {
        // Age II needs any two of the four halls, but three of the Malazan halls need Age II: only the Armory is
        // reachable, so Malazan can never reach Age II (nor anything behind it).
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        foreach (string id in new[] { "malazan_barracks", "malazan_crossbow_range", "malazan_wickan_corral" })
            SetRequires(dir, id, "[\"age_ii\"]");
        DataLoadResult r = Load(dir);
        Assert.True(r.Ok, "BUG-0100 fixed? then flip this pin: " + string.Join("; ", r.Errors));
    }

    [Theory(Skip = "BUG-0100: requirements that can never be met (another faction's building, an any-of only its own tech opens) load clean")]
    [InlineData("malazan_heavy_infantry", "[\"whirlwind_raider_camp\"]")]
    [InlineData("armor_1", "[\"malazan_barracks\"]")]
    public void ARequirementThatCanNeverBeMet_IsOneError(string id, string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetRequires(dir, id, raw);
        DataLoadResult r = Load(dir);
        Assert.Null(r.Data);
        Assert.Single(r.Errors);
    }
}
