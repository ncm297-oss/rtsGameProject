using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D35: M8-3's two hit bonuses (docs/03 "Implementation (M8-3)") in the content pins, before the Shadow roster carries
/// them. shadow.md's Schema gaps rows G2 / G3 are docs/03's "For the data track (M8-3)" rows; the description reading
/// (<see cref="UnitHitBonusText"/>) and the page pins (<see cref="HitBonusPagePin.FirstShotProblems"/>, the
/// <c>nearbyOwn</c> sentence of <see cref="PassiveContentTests"/>) are proven on a temp copy of the shipped data with
/// shadow.md's two rows applied to two Teblor units (the Javelin Thrower takes the Archer's, the War-dog the Hound's: the
/// D-R36 method), each row looked up and then added or edited, so the copy is right whether or not the shipped data
/// already carries the field (rule 58).
/// </summary>
public class HitBonusPinTests
{
    private const string Archer = "teblor_javelin_thrower";
    private const string Hound = "teblor_war_dog";

    /// <summary>shadow.md's ids and names to the two Teblor stand-ins (longest first).</summary>
    private static readonly (string Shadow, string Teblor)[] ToTeblor =
    {
        ("shadow_archer", Archer), ("shadow_hound", Hound),
        ("Shadow Archer", "Javelin Thrower"), ("Hounds of Shadow", "War-dogs"), ("Hound of Shadow", "War-dog"), ("Hounds", "War-dogs"), ("Hound", "War-dog"),
    };

    private static string AsTeblor(string s) => ToTeblor.Aggregate(s, (t, p) => t.Replace(p.Shadow, p.Teblor, StringComparison.Ordinal));

    private static string[] ShadowLines() => PageTables.FactionLines("shadow");

    private static List<HitBonusPagePin.PageRow> ShadowRows()
    {
        var problems = new List<string>();
        List<HitBonusPagePin.PageRow> rows = HitBonusPagePin.Rows(ShadowLines(), "shadow.md", problems);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        return rows;
    }

    // ---------- shadow.md against docs/03 (criterion 1) ----------

    /// <summary>docs/03 "For the data track (M8-3)": its ```json blocks, each one property (the "..." placeholder dropped), and its text.</summary>
    private static (List<JsonObject> Blocks, string Text) Docs03Rows()
    {
        string[] lines = PageTables.Lines("docs/03-technical-design.md");
        int from = Array.FindIndex(lines, l => l.StartsWith("**For the data track (M8-3).**", StringComparison.Ordinal));
        Assert.True(from >= 0, "docs/03 has no '**For the data track (M8-3).**' paragraph");
        int to = Array.FindIndex(lines, from + 1, l => l.StartsWith("**Questions for the Producer (M8-3).**", StringComparison.Ordinal));
        Assert.True(to > from);
        var blocks = new List<JsonObject>();
        for (int i = from; i < to; i++)
        {
            if (lines[i].Trim() != "```json") continue;
            int end = Array.FindIndex(lines, i + 1, l => l.Trim() == "```");
            JsonObject o = JsonNode.Parse("{" + string.Join("\n", lines[(i + 1)..end]) + "}")!.AsObject();
            foreach (JsonObject inner in o.Select(kv => kv.Value).OfType<JsonObject>()) inner.Remove("...");
            blocks.Add(o);
            i = end;
        }
        return (blocks, string.Join(" ", lines[from..to]));
    }

    [Fact]
    public void ShadowPagesRows_AreDocs03sRows_FieldForField()
    {
        (List<JsonObject> blocks, string text) = Docs03Rows();
        List<HitBonusPagePin.PageRow> rows = ShadowRows();
        Assert.Equal(new[] { "G2", "G3" }, rows.Select(r => r.Gap));
        Assert.Equal(new[] { "shadow_archer", "shadow_hound" }, rows.Select(r => r.Unit));
        Assert.Equal(rows.Count, blocks.Count);
        foreach (HitBonusPagePin.PageRow r in rows)
        {
            JsonObject doc = Assert.Single(blocks, b => b.ContainsKey(r.Key));
            Assert.True(JsonTextEdit.Same(doc[r.Key], r.Value), $"shadow.md {r.Gap} '{r.Key}': page {JsonTextEdit.Inline(r.Value)}, docs/03 {JsonTextEdit.Inline(doc[r.Key])}");
            Assert.Contains($"on `{r.Unit}`", text, StringComparison.Ordinal);
        }
    }

    /// <summary>The mechanics table points at the built fields, every key of the row named; the gap rows say built, not proposed.</summary>
    [Fact]
    public void ShadowPagesMechanicsRows_NameTheBuiltFields()
    {
        string[] lines = ShadowLines();
        List<List<string>> tables = PageTables.TablesInSection(lines, PageTables.HeadingStartingWith(lines, "## Schema gaps"));
        Assert.Equal(2, tables.Count);
        string[][] mechanics = tables[0].Skip(2).Select(l => PageTables.Cells(l)).ToArray();
        string[][] gaps = tables[1].Skip(2).Select(l => PageTables.Cells(l)).ToArray();
        foreach (HitBonusPagePin.PageRow r in ShadowRows())
        {
            string[] m = Assert.Single(mechanics, c => c[2].Contains($"gap {r.Gap}, built", StringComparison.Ordinal));
            if (r.Key == "attack") Assert.Contains("`attack.fromStealthBonus`", m[2], StringComparison.Ordinal);
            else
            {
                JsonObject p = HitBonusPagePin.NearbyOf(r)!;
                Assert.Contains("`passives`", m[2], StringComparison.Ordinal);
                foreach ((string key, JsonNode? value) in p)
                    Assert.Contains(key == "trigger" ? (string)value! : key, m[2], StringComparison.Ordinal);
            }
            string[] g = Assert.Single(gaps, c => c[0] == r.Gap);
            Assert.StartsWith("**Built (M8-3", g[2], StringComparison.Ordinal);
            Assert.DoesNotContain("nearbyAllies\"", g[3], StringComparison.Ordinal);
        }
    }

    // ---------- the page's rows against its own notes (criterion 3, through the readiness check) ----------

    [Fact]
    public void ShadowPagesRows_AgreeWithItsUnitNotes()
    {
        Assert.Empty(HitBonusPagePin.RowProblems("shadow.md", ShadowLines(), ahead: true));
        Assert.Empty(PagePinReadinessTests.Problems("shadow", ShadowLines()));
    }

    /// <summary>One number of a row or of its note changed alone is a readiness problem naming the gap, the unit and the field.</summary>
    [Theory]
    [InlineData("The first shot from stealth deals +50% damage", "The first shot from stealth deals +40% damage", "shadow.md Schema gaps G2 `shadow_archer` fromStealthBonus:")]
    [InlineData("\"fromStealthBonus\": 0.5", "\"fromStealthBonus\": 0.4", "shadow.md Schema gaps G2 `shadow_archer` fromStealthBonus:")]
    [InlineData("The first shot from stealth deals +50% damage", "The first shot from hiding hurts more", "shadow.md Schema gaps G2 `shadow_archer` fromStealthBonus:")]
    [InlineData("+10% damage for each other Hound of Shadow", "+20% damage for each other Hound of Shadow", "shadow.md Schema gaps G3 `shadow_hound` damageBonus:")]
    [InlineData("\"damageBonus\": 0.1", "\"damageBonus\": 0.2", "shadow.md Schema gaps G3 `shadow_hound` damageBonus:")]
    [InlineData("(max +30%)", "(max +40%)", "shadow.md Schema gaps G3 `shadow_hound` maxStacks:")]
    [InlineData("\"maxStacks\": 3", "\"maxStacks\": 4", "shadow.md Schema gaps G3 `shadow_hound` maxStacks:")]
    [InlineData("Hound of Shadow within 8 m", "Hound of Shadow within 6 m", "shadow.md Schema gaps G3 `shadow_hound` radius:")]
    [InlineData("\"radius\": 8", "\"radius\": 6", "shadow.md Schema gaps G3 `shadow_hound` radius:")]
    [InlineData("each other Hound of Shadow within", "each other Edur Warrior within", "shadow.md Schema gaps G3 `shadow_hound` units:")]
    [InlineData("\"units\": [\"shadow_hound\"]", "\"units\": [\"shadow_hound\", \"shadow_archer\"]", "shadow.md Schema gaps G3 `shadow_hound` units:")]
    [InlineData("`shadow_archer` `\"attack\"", "`shadow_archerx` `\"attack\"", "shadow.md Schema gaps G2 `shadow_archerx`: not an id of the Units table")]
    [InlineData("`\"attack\": { \"fromStealthBonus\": 0.5 }`", "`\"attack\": { \"fromStealthBonus\": 0.5`", "shadow.md Schema gaps G2: '\"attack\": { \"fromStealthBonus\": 0.5' is not one JSON property")]
    [InlineData("`\"attack\": { \"fromStealthBonus\": 0.5 }`", "`attack.fromStealthBonus` 0.5", "shadow.md Unit notes 'Shadow Archer': states a first shot from stealth, but no Schema gaps row")]
    [InlineData("`\"passives\": [{", "`\"passivez\": [{", "shadow.md Unit notes 'Hound of Shadow': a nearbyOwn passive sentence, but no Schema gaps row")]
    public void AnEditedShadowRowOrNote_IsAReadinessProblem_NamingGapUnitAndField(string find, string replace, string expected)
    {
        string[] lines = ShadowLines();
        int[] hits = Enumerable.Range(0, lines.Length).Where(i => lines[i].Contains(find, StringComparison.Ordinal)).ToArray();
        Assert.True(hits.Length == 1, $"shadow.md has '{find}' on {hits.Length} lines, expected 1");
        lines[hits[0]] = lines[hits[0]].Replace(find, replace, StringComparison.Ordinal);

        List<string> problems = PagePinReadinessTests.Problems("shadow", lines);

        Assert.True(problems.Any(p => p.StartsWith(expected, StringComparison.Ordinal)), $"expected '{expected}', got:\n{string.Join("\n", problems)}");
    }

    // ---------- the forged copy: shadow.md's rows on two Teblor units ----------

    /// <summary>
    /// A temp copy of the shipped data with <paramref name="rows"/> applied to the stand-ins (each unit looked up, then its
    /// <c>attack.fromStealthBonus</c> set and its <c>nearbyOwn</c> passive edited in place or added), or with
    /// <paramref name="strip"/> both fields taken off them.
    /// </summary>
    private static GameData Forge(IEnumerable<HitBonusPagePin.PageRow> rows, bool strip = false)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/teblor/units.json", root =>
        {
            JsonArray units = root["units"]!.AsArray();
            JsonObject Unit(string id) => units.Select(n => n!.AsObject()).Single(u => (string)u["id"]! == id);
            if (strip)
            {
                foreach (string id in new[] { Archer, Hound })
                {
                    JsonObject u = Unit(id);
                    u["attack"]!.AsObject().Remove("fromStealthBonus");
                    if (u["passives"] is JsonArray ps)
                        foreach (JsonNode? p in ps.Where(p => (string?)p!["trigger"] == "nearbyOwn").ToArray()) ps.Remove(p);
                }
                return;
            }
            foreach (HitBonusPagePin.PageRow r in rows)
            {
                JsonObject u = Unit(AsTeblor(r.Unit));
                if (HitBonusPagePin.StealthOf(r) is double fs) u["attack"]!.AsObject()["fromStealthBonus"] = fs;
                if (HitBonusPagePin.NearbyOf(r) is JsonObject near)
                {
                    var p = JsonNode.Parse(AsTeblor(near.ToJsonString()))!.AsObject();
                    if (u["passives"] is not JsonArray ps) u["passives"] = ps = new JsonArray();
                    int at = ps.Select((x, i) => (x, i)).Where(t => (string?)t.x!["trigger"] == "nearbyOwn").Select(t => t.i).DefaultIfEmpty(-1).First();
                    if (at >= 0) ps[at] = p;
                    else ps.Add(p);
                }
            }
        });
        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        return result.Data!;
    }

    private static readonly Lazy<GameData> Forged = new(() => Forge(ShadowRows()));
    private static readonly Lazy<GameData> Stripped = new(() => Forge(Array.Empty<HitBonusPagePin.PageRow>(), strip: true));

    private static UnitDef UnitOf(GameData d, string id) => d.Units[d.FindUnit(id)];

    [Fact]
    public void TheForgedCopy_CarriesShadowPagesRows_OnTheStandIns()
    {
        GameData d = Forged.Value;
        Assert.Equal(0.5f, UnitOf(d, Archer).Attack.FromStealthBonus);
        UnitDef dog = UnitOf(d, Hound);
        Assert.True(dog.NearbyPassive >= 0);
        UnitPassive p = dog.Passives[dog.NearbyPassive];
        Assert.Equal((0.1f, 3, 8f), (p.DamageBonus, p.MaxStacks, p.Radius));
        Assert.Equal(new[] { dog.Id }, p.Units.ToArray());
        Assert.Contains(dog.Passives, x => x.Trigger == PassiveTrigger.OnHit); // the Hamstring kept: edited in, not replaced
        Assert.Equal(0f, UnitOf(Stripped.Value, Archer).Attack.FromStealthBonus);
        Assert.Equal(-1, UnitOf(Stripped.Value, Hound).NearbyPassive);
    }

    // ---------- the description reading (criterion 2) ----------

    [Fact]
    public void EveryShippedUnitDescription_StatesItsHitBonuses()
    {
        List<string> problems = UnitHitBonusText.Problems(TestSim.Data.Units);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>The right sentences pass: "+50%" and "50% more damage" on the first-shot unit; per-mate, cap and reach on the pack unit.</summary>
    [Theory]
    [InlineData(Archer, "Its first shot from hiding deals +50% damage.")]
    [InlineData(Archer, "Vanishes once it has stood still for 3 seconds. Its first shot from hiding deals 50% more damage.")]
    [InlineData(Archer, "Hurls javelins from cover; the first shot from stealth deals +50% damage.")]
    [InlineData(Hound, "Each other War-dog within 8 m adds +10% damage, up to +30%.")]
    [InlineData(Hound, "A big dog: each other War-dog within 8 m adds 10% damage, up to 30%. No more than 7 can live at once.")]
    [InlineData(Hound, "Hunts in packs: +10% damage for each other War-dog within 8 m (max +30%).")]
    public void TheRightSentence_Passes(string unit, string description)
    {
        List<string> problems = UnitHitBonusText.Problems(UnitOf(Forged.Value, unit), description);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>shadow.md's own Owner review sentences for the Archer and the Hound pass on the stand-ins (the landing's words).</summary>
    [Theory]
    [InlineData("shadow_archer", Archer)]
    [InlineData("shadow_hound", Hound)]
    public void ShadowPagesOwnerReviewSentence_Passes(string shadowId, string unit)
    {
        string[] lines = ShadowLines();
        List<string[]> review = PageTables.FirstTableRows(lines, Array.IndexOf(lines, "## Owner review (D-R38, proposed)"), stripBackticks: true);
        string text = Assert.Single(review, r => r[0] == "units.json" && r[1] == shadowId)[3];
        List<string> problems = UnitHitBonusText.Problems(UnitOf(Forged.Value, unit), text);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Each wrong (or missing) number fails, naming the unit and the field.</summary>
    [Theory]
    [InlineData(Archer, "Its first shot from hiding deals +40% damage.", UnitHitBonusText.StealthField)]
    [InlineData(Archer, "Its first shot from hiding deals 5% more damage.", UnitHitBonusText.StealthField)]
    [InlineData(Archer, "Its first shot from hiding deals more damage.", UnitHitBonusText.StealthField)]
    // BUG-1690 (D-H10): was field null ("'50%' reads as no hit bonus"); a percentage with no first-shot or pack words is no
    // longer read, so the row fails as the bonus not stated in a first-shot sentence.
    [InlineData(Archer, "Hurls javelins, 50% harder.", UnitHitBonusText.StealthField)]
    [InlineData(Hound, "Each other War-dog within 8 m adds +20% damage, up to +30%.", UnitHitBonusText.PerMateField)]
    [InlineData(Hound, "Each other War-dog within 8 m adds +30% damage, up to +30%.", UnitHitBonusText.PerMateField)]
    [InlineData(Hound, "Each other War-dog within 8 m adds +10% damage, up to +40%.", UnitHitBonusText.CapField)]
    [InlineData(Hound, "Each other War-dog within 8 m adds +10% damage, up to +10%.", UnitHitBonusText.CapField)]
    [InlineData(Hound, "Each other War-dog within 8 m adds +10% damage.", UnitHitBonusText.CapField)]
    [InlineData(Hound, "Each other War-dog within 6 m adds +10% damage, up to +30%.", UnitHitBonusText.RadiusField)]
    [InlineData(Hound, "Each other War-dog nearby adds +10% damage, up to +30%.", UnitHitBonusText.RadiusField)]
    [InlineData(Hound, "Big shaggy dogs that hunt in packs.", UnitHitBonusText.PerMateField)]
    [InlineData("teblor_warrior", "Its first shot from hiding deals +50% damage.", UnitHitBonusText.StealthField)]
    [InlineData("teblor_warrior", "Each other Warrior adds +10% damage.", UnitHitBonusText.PerMateField)]
    [InlineData("teblor_warrior", "Each other Warrior within 6 m adds +10% damage.", UnitHitBonusText.PerMateField)]
    [InlineData("teblor_warrior", "Each other Warrior within 6 m adds +10% damage.", UnitHitBonusText.RadiusField)]
    [InlineData("teblor_warrior", "Stronger with each other Warrior near, up to +30%.", UnitHitBonusText.CapField)]
    [InlineData(Hound, "Each other War-dog within 8 m adds +10% damage, up to +30%. Each bite also adds 5%.", UnitHitBonusText.PerMateField)]
    public void AWrongNumber_FailsNamingTheUnitAndTheField(string unit, string description, string? field)
    {
        List<string> problems = UnitHitBonusText.Problems(UnitOf(Forged.Value, unit), description);

        string want = field == null ? $"{unit} description: " : $"{unit} description {field}: ";
        Assert.True(problems.Any(p => p.StartsWith(want, StringComparison.Ordinal)), $"no problem starting '{want}':\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// BUG-1690 (D-H10, ruling docs/01 2026-11-24 ACCEPT (3)(b)): a percentage or a "within N m" in a sentence with no
    /// first-shot or pack words, on a unit without the matching field, is not a hit bonus and is not read (the passive and
    /// status pins own it): a Hamstring's slow, a Frenzy's cap, an on-death reach. Two rows here were failing rows before
    /// the ruling ("Stronger in a crowd, up to +30%" as a cap, "Fights best within 8 m of a Shaman" as a reach).
    /// </summary>
    [Theory]
    [InlineData("teblor_warrior", "Stronger in a crowd, up to +30%.")]
    [InlineData("teblor_warrior", "Fights best within 8 m of a Shaman.")]
    [InlineData("teblor_warrior", "Below half health it gains Frenzied, up to +50% attack speed.")]
    [InlineData("teblor_warrior", "When a Warrior dies, Warriors within 6 m gain 20% attack speed for 5 s.")]
    [InlineData("teblor_breaker", "Each swing slows its target by 20% for 2 s.")]
    [InlineData(Archer, "Its first shot from hiding deals +50% damage. Each hit slows by 20%; Javelin Throwers within 6 m of a dying one flee.")]
    public void APercentageThatIsNoHitBonus_IsNotRead(string unit, string description)
    {
        List<string> problems = UnitHitBonusText.Problems(UnitOf(Forged.Value, unit), description);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ---------- the page pins, both ways, on the forged copy (criterion 3) ----------

    /// <summary>
    /// teblor.md with shadow.md's two sentences on the stand-ins' notes (the Archer's first-shot sentence on the Javelin
    /// Thrower, the Hound's Pack Hunter passive on the War-dog, names substituted), or with <paramref name="edit"/> applied
    /// to the result.
    /// </summary>
    private static string[] TeblorWithShadowSentences(Func<string, string>? edit = null)
    {
        List<(string Unit, string Text)> notes = HitBonusPagePin.Notes(ShadowLines());
        string archer = notes.Single(n => n.Unit == "Shadow Archer").Text;
        int at = archer.IndexOf("The first shot from stealth", StringComparison.Ordinal);
        string firstShot = archer[at..(archer.IndexOf('.', at) + 1)];
        PassiveContentTests.PagePassive pack = Assert.Single(PassiveContentTests.PagePassives(ShadowLines(), "shadow.md"), p => p.Name == "Pack Hunter");
        string packSentence = AsTeblor(pack.Text[..(pack.Text.IndexOf(". ", StringComparison.Ordinal) + 1)]);

        string[] lines = PageTables.FactionLines("teblor");
        int thrower = Array.FindIndex(lines, l => l.StartsWith("- **Javelin Thrower:**", StringComparison.Ordinal));
        int dog = Array.FindIndex(lines, l => l.StartsWith("- **War-dog (unique, Age II):**", StringComparison.Ordinal));
        Assert.True(thrower >= 0 && dog >= 0);
        lines[thrower] += " " + firstShot;
        lines[dog] += $" Passive **{pack.Name}**: {packSentence}";
        return edit == null ? lines : lines.Select(edit).ToArray();
    }

    private static List<string> PagePins(GameData d, string[] lines, Func<UnitDef, ImmutableArray<UnitPassive>>? passivesOf = null, Func<UnitDef, float>? stealthOf = null)
    {
        var problems = new List<string>(HitBonusPagePin.FirstShotProblems(d, "teblor", lines, stealthOf));
        problems.AddRange(PassiveContentTests.Compare(d, "teblor", lines, passivesOf).Problems);
        return problems;
    }

    [Fact]
    public void ThePagePins_PassBothWays_WithTheRowsAndTheSentences()
    {
        List<string> problems = PagePins(Forged.Value, TeblorWithShadowSentences());
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Data to page: the rows landed without the page's sentences fail, naming the unit and the field.</summary>
    [Fact]
    public void TheRowsWithoutTheSentences_Fail()
    {
        List<string> problems = PagePins(Forged.Value, PageTables.FactionLines("teblor"));
        Assert.Contains(problems, p => p.StartsWith("teblor.md \"Unit notes\" 'Javelin Thrower' attack.fromStealthBonus: data +50%", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("teblor.md \"Unit notes\" 'War-dog' passive nearbyOwn: no \"Passive **Name**:\" sentence", StringComparison.Ordinal));
    }

    /// <summary>Page to data: the sentences without the rows fail, naming the unit and the field.</summary>
    [Fact]
    public void TheSentencesWithoutTheRows_Fail()
    {
        List<string> problems = PagePins(Stripped.Value, TeblorWithShadowSentences());
        Assert.Contains(problems, p => p.StartsWith("teblor.md \"Unit notes\" 'Javelin Thrower' attack.fromStealthBonus: page +50%, data none", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("teblor.md \"Unit notes\" 'War-dog' passive 'Pack Hunter' trigger: 'War-dog' has no nearbyOwn passive", StringComparison.Ordinal));
    }

    /// <summary>One page number changed alone fails naming the field.</summary>
    [Theory]
    [InlineData("deals +50% damage", "deals +40% damage", "'Javelin Thrower' attack.fromStealthBonus: page +40%, data +50%")]
    [InlineData("+10% damage for each", "+20% damage for each", "'War-dog' passive 'Pack Hunter' damageBonus:")]
    [InlineData("(max +30%)", "(max +40%)", "'War-dog' passive 'Pack Hunter' maxStacks:")]
    [InlineData("(max +30%)", "", "'War-dog' passive 'Pack Hunter' maxStacks:")]
    [InlineData("War-dog within 8 m", "War-dog within 6 m", "'War-dog' passive 'Pack Hunter' radius:")]
    [InlineData("each other War-dog within", "each other Warrior within", "'War-dog' passive 'Pack Hunter' units:")]
    public void AMutatedPageNumber_FailsNamingTheField(string text, string mutant, string want)
    {
        string[] lines = TeblorWithShadowSentences(l => l.Replace(text, mutant, StringComparison.Ordinal));
        Assert.Contains(lines, l => mutant.Length == 0 || l.Contains(mutant, StringComparison.Ordinal));

        List<string> problems = PagePins(Forged.Value, lines);

        Assert.True(problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem naming {want}\n" + string.Join("\n", problems));
    }

    /// <summary>One data number changed alone (in memory, on the forged copy) fails naming the field.</summary>
    [Theory]
    [InlineData("fromStealthBonus-0.4", "'Javelin Thrower' attack.fromStealthBonus: page +50%, data +40%")]
    [InlineData("damageBonus-0.2", "'War-dog' passive 'Pack Hunter' damageBonus:")]
    [InlineData("maxStacks-2", "'War-dog' passive 'Pack Hunter' maxStacks:")]
    [InlineData("radius-6", "'War-dog' passive 'Pack Hunter' radius:")]
    [InlineData("units-plus-warrior", "'War-dog' passive 'Pack Hunter' units:")]
    public void AMutatedDataNumber_FailsNamingTheField(string mutant, string want)
    {
        GameData d = Forged.Value;
        int warrior = d.FindUnit("teblor_warrior");
        UnitDef thrower = UnitOf(d, Archer), dog = UnitOf(d, Hound);
        ImmutableArray<UnitPassive> passives = dog.Passives.Select(p => (p.Trigger, mutant) switch
        {
            (PassiveTrigger.NearbyOwn, "damageBonus-0.2") => p with { DamageBonus = 0.2f },
            (PassiveTrigger.NearbyOwn, "maxStacks-2") => p with { MaxStacks = 2 },
            (PassiveTrigger.NearbyOwn, "radius-6") => p with { Radius = 6 },
            (PassiveTrigger.NearbyOwn, "units-plus-warrior") => p with { Units = p.Units.Add(warrior) },
            _ => p,
        }).ToImmutableArray();

        List<string> problems = PagePins(d, TeblorWithShadowSentences(),
            passivesOf: u => u.Id == dog.Id ? passives : u.Passives,
            stealthOf: u => u.Id == thrower.Id && mutant == "fromStealthBonus-0.4" ? 0.4f : u.Attack.FromStealthBonus);

        Assert.True(problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem naming {want}\n" + string.Join("\n", problems));
    }

    /// <summary>The first-shot pin over every data faction's page (the pack is <see cref="PassiveContentTests"/>' theory).</summary>
    [Theory]
    [MemberData(nameof(ContentFactions.Theory), MemberType = typeof(ContentFactions))]
    public void EveryFirstShotBonus_MatchesItsUnitNote_BothWays(string faction)
    {
        List<string> problems = HitBonusPagePin.FirstShotProblems(TestSim.Data, faction, PageTables.FactionLines(faction));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
