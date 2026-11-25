using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// Content (D11): every unit passive in <c>units.json</c> (<see cref="UnitDef.Passives"/>) matches the "Passive **Name**:"
/// sentence of its unit's bullet in the faction page's "Unit notes", field by field, both ways: a page passive with no data
/// passive fails unless it is a named allowance (<see cref="PendingPassives"/>), and a data passive with no page sentence
/// fails. The data has no passive names, so a page sentence is matched to a data passive by its trigger wording:
/// "below &lt;n&gt;% HP" is <c>whenBelowHp</c>, "when a &lt;Unit&gt; dies" is <c>onDeath</c>, and (D-R36, ahead of the war-dog's
/// Hamstring) "each bite" / "each hit" is <c>onHit</c>, whose sentence states the status, its magnitude and "for &lt;n&gt; s",
/// and (D35, M8-3) "+&lt;n&gt;% damage for each other &lt;Unit&gt; within &lt;n&gt; m (max +&lt;n&gt;%)" is <c>nearbyOwn</c>.
/// D-H10 (BUG-1691): a stand-still sentence ("becomes Stealthed after &lt;n&gt; s without moving") is not a data passive
/// but the unit's <c>stillStealth</c> field, compared to its status and <c>delay</c>, both ways.
/// </summary>
public class PassiveContentTests
{
    /// <summary>The data's factions, in data order (D-R36): a new faction's passives are pinned the session they land.</summary>
    private static string[] Factions => ContentFactions.Keys;

    /// <summary>
    /// Page passives with no data passive yet (Malazan's Heavy Infantry and Wickan Lancer, whose triggers the sim doesn't
    /// have). Once one lands it is compared like any other, but its differences are reported, not failed, until it is
    /// removed here.
    /// </summary>
    public static readonly string[] PendingPassives = { "Shield Wall", "Charge" };

    private readonly ITestOutputHelper _out;

    public PassiveContentTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => TestSim.Data;

    /// <summary>One "Passive **Name**: text" sentence of a unit's "Unit notes" bullet.</summary>
    public sealed record PagePassive(string Unit, string Name, string Text);

    private const RegexOptions Opts = RegexOptions.CultureInvariant;

    private const string Num = @"(?<n>\d+(?:\.\d+)?)";

    /// <summary>"**Zealot (unique, Age II):**": the unit's display name, any parenthesis dropped.</summary>
    private static readonly Regex BulletUnit = new(@"^\*\*(?<unit>[^*:(]+?)\s*(?:\([^)]*\))?:\*\*", Opts);

    /// <summary>"Passive **Name**: text", the text running to the next "Passive **" or the bullet's end.</summary>
    private static readonly Regex PassiveSentence = new(@"Passive \*\*(?<name>[^*]+)\*\*:\s*(?<text>.*?)(?=\s+Passive \*\*|$)", Opts);

    private static readonly Regex BelowHp = new(@"^below " + Num + @" ?% HP\b", Opts);
    private static readonly Regex OnDeath = new(@"^when an? (?<unit>.+?) dies\b", Opts);
    private static readonly Regex OnHit = new(@"^each (?:bite|hit)\b", Opts);
    private static readonly Regex Gains = new(@"\b(?:gains?|leaves (?:it|the target)) (?<status>[A-Z][A-Za-z]*)", Opts);
    private static readonly Regex Within = new(@"\bwithin " + Num + @" ?m\b", Opts);
    private static readonly Regex For = new(@"\bfor " + Num + @" ?s\b", Opts);
    private static readonly Regex Takers = new(@"\bdies, (?<list>.+?) within\b", Opts);
    private static readonly Regex AttackSpeed = new(@"\+" + Num + @" ?% attack speed\b", Opts);

    /// <summary>
    /// D35 (M8-3's <c>nearbyOwn</c>, shadow.md's Pack Hunter): "+10% damage for each other Hound of Shadow within 8 m": the
    /// per-mate bonus, the counted types (one or a list, "or" / "and" / commas) and the radius.
    /// </summary>
    private static readonly Regex Nearby = new(@"^\+?" + Num + @" ?% damage for each other (?<list>.+?) within (?<r>\d+(?:\.\d+)?) ?m\b", Opts);

    /// <summary>
    /// D-H10 (BUG-1691, ruling docs/01 2026-11-24 ACCEPT (3)(a)): "becomes Stealthed after 3 s without moving": a stand-still
    /// stealth, which is the unit's <c>stillStealth</c> field (M8-2: <c>status</c> by display name, <c>delay</c> in seconds),
    /// not a data passive. The page keeps the "Passive **Name**:" shape for it (shadow.md's Shadowed).
    /// </summary>
    private static readonly Regex StillSentence = new(@"^(?:becomes |is |turns )?(?<status>[A-Z][A-Za-z]*) after " + Num + @" ?s without moving\b", Opts);

    /// <summary>
    /// Whether a page sentence's wording is one the pin reads: a trigger the sim has, or (BUG-1691) a stand-still stealth.
    /// <see cref="PagePinReadinessTests"/> uses it so a sentence the pin cannot place is named before its data lands.
    /// </summary>
    public static bool ReadsAsDataField(string text) => TriggerOf(text) != null || StillSentence.IsMatch(text);

    /// <summary>The pack's cap: "(max +30%)", "up to +30%", "at most 30%".</summary>
    private static readonly Regex NearbyCap = new(@"\b(?:max(?:imum)?|up to|at most)\s*\+?" + Num + @" ?%", Opts);

    /// <summary>D35: what a <c>nearbyOwn</c> sentence states: per-mate and cap in percent (null when unstated), radius in metres, counted names.</summary>
    public sealed record NearbyReading(double PerMate, double? Cap, double Radius, string[] Units);

    /// <summary>
    /// D35: <paramref name="text"/> (a "Passive **Name**:" sentence's words) read as a <c>nearbyOwn</c> passive, or null when
    /// its wording is not that shape. The counted names are singular ("Hounds of Shadow" stays as written: name them singly).
    /// </summary>
    public static NearbyReading? ReadNearby(string text)
    {
        Match m = Nearby.Match(text);
        if (!m.Success) return null;
        Match cap = NearbyCap.Match(text, m.Index + m.Length);
        string[] units = Regex.Split(m.Groups["list"].Value, @",\s*|\s+(?:and|or)\s+", Opts).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        return new NearbyReading(Number(m), cap.Success ? Number(cap) : null,
            double.Parse(m.Groups["r"].Value, CultureInfo.InvariantCulture), units);
    }

    [Theory]
    [MemberData(nameof(ContentFactions.Theory), MemberType = typeof(ContentFactions))]
    public void EveryDataPassive_MatchesItsPageSentence_AndEveryPageSentenceIsLoadedOrPending(string faction)
    {
        AbilityContentTests.Result r = Compare(Data, faction, PageTables.FactionLines(faction));
        foreach (string line in r.Reports) _out.WriteLine(line);
        Assert.True(r.Problems.Count == 0, $"docs/factions/{faction}.md \"Unit notes\" passives differ from units.json:\n" + string.Join("\n", r.Problems));
    }

    [Fact]
    public void TheZealotPassives_AreLoaded_AndStrictlyPinned()
    {
        // Guards the pin itself: with no loaded passive, or with these pending, every check above would pass vacuously.
        UnitDef zealot = Data.Units[Data.FindUnit("whirlwind_zealot")];
        Assert.Equal(2, zealot.Passives.Length);
        List<PagePassive> page = PagePassives(PageTables.FactionLines("whirlwind"), "whirlwind.md");
        Assert.Contains(page, p => p.Unit == "Zealot" && p.Name == "Frenzy of the Apocalypse");
        Assert.Contains(page, p => p.Unit == "Zealot" && p.Name == "Martyrdom");
        Assert.DoesNotContain("Frenzy of the Apocalypse", PendingPassives);
        Assert.DoesNotContain("Martyrdom", PendingPassives);
    }

    [Fact]
    public void PendingPassives_AreSentencesOnSomePage()
    {
        var names = Factions.SelectMany(f => PagePassives(PageTables.FactionLines(f), f + ".md")).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (string name in PendingPassives) Assert.True(names.Contains(name), $"pending passive '{name}' is on no faction page; drop it from the allowance");
    }

    /// <summary>D11 criterion 1, the page side: one whirlwind.md Zealot passive number or word changed fails naming the passive and the field.</summary>
    [Theory]
    [InlineData("Frenzy of the Apocalypse", "hpFraction", "below 50% HP", "below 40% HP")]
    [InlineData("Frenzy of the Apocalypse", "trigger", "below 50% HP", "below half HP")]
    [InlineData("Frenzy of the Apocalypse", "magnitude", "gains Frenzied (+50% attack speed)", "gains Frenzied (+30% attack speed)")]
    [InlineData("Frenzy of the Apocalypse", "magnitude", "gains Frenzied (+50% attack speed)", "gains Frenzied")]
    [InlineData("Frenzy of the Apocalypse", "status", "gains Frenzied", "gains Burning")]
    [InlineData("Frenzy of the Apocalypse", "duration", "(+50% attack speed). Passive", "(+50% attack speed) for 5 s. Passive")]
    [InlineData("Martyrdom", "unit", "when a Zealot dies", "when a Raider dies")]
    [InlineData("Martyrdom", "units", "Zealots within", "Raiders within")]
    [InlineData("Martyrdom", "units", "Zealots within", "Zealots and Raiders within")]
    [InlineData("Martyrdom", "radius", "within 6 m", "within 8 m")]
    [InlineData("Martyrdom", "radius", "within 6 m", "nearby")]
    [InlineData("Martyrdom", "status", "gain Frenzied", "gain Slowed")]
    [InlineData("Martyrdom", "magnitude", "gain Frenzied (+50% attack speed)", "gain Frenzied (+25% attack speed)")]
    [InlineData("Martyrdom", "magnitude", "gain Frenzied (+50% attack speed)", "gain Frenzied")]
    [InlineData("Martyrdom", "duration", "for 5 s", "for 4 s")]
    [InlineData("Martyrdom", "duration", "for 5 s", "briefly")]
    public void AMutatedPageNumber_FailsNamingThePassiveAndField(string passive, string field, string text, string mutant)
    {
        string[] lines = Mutated("whirlwind", text, mutant);

        AbilityContentTests.Result r = Compare(Data, "whirlwind", lines);

        string want = $"'{passive}' {field}:";
        Assert.True(r.Problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem naming {want}\n" + string.Join("\n", r.Problems));
    }

    /// <summary>D11 criterion 1, the data side: one Zealot passive field changed in memory fails naming the passive and the field.</summary>
    [Theory]
    [InlineData("Frenzy of the Apocalypse", "hpFraction", "below-0.4")]
    [InlineData("Frenzy of the Apocalypse", "magnitude", "below-magnitude-0.3")]
    [InlineData("Frenzy of the Apocalypse", "status", "below-slowed")]
    [InlineData("Martyrdom", "magnitude", "death-magnitude-0.25")]
    [InlineData("Martyrdom", "duration", "death-duration-4")]
    [InlineData("Martyrdom", "radius", "death-radius-8")]
    [InlineData("Martyrdom", "units", "death-units-plus-raider")]
    [InlineData("Martyrdom", "units", "death-units-raider")]
    [InlineData("Martyrdom", "status", "death-slowed")]
    [InlineData("Frenzy of the Apocalypse", "trigger", "drop-below")]
    [InlineData("Martyrdom", "trigger", "drop-death")]
    public void AMutatedDataNumber_FailsNamingThePassiveAndField(string passive, string field, string mutant)
    {
        int slowed = Data.Statuses.Single(s => s.Key == "slowed").Id;
        int raider = Data.FindUnit("whirlwind_raider");
        int ticks = SimConstants.TicksPerSecond;
        UnitDef zealot = Data.Units[Data.FindUnit("whirlwind_zealot")];
        ImmutableArray<UnitPassive> passives = zealot.Passives
            .Where(p => !(mutant == "drop-below" && p.Trigger == PassiveTrigger.WhenBelowHp) && !(mutant == "drop-death" && p.Trigger == PassiveTrigger.OnDeath))
            .Select(p => (p.Trigger, mutant) switch
            {
                (PassiveTrigger.WhenBelowHp, "below-0.4") => p with { HpFraction = 0.4f },
                (PassiveTrigger.WhenBelowHp, "below-magnitude-0.3") => p with { Magnitude = 0.3f },
                (PassiveTrigger.WhenBelowHp, "below-slowed") => p with { Status = slowed },
                (PassiveTrigger.OnDeath, "death-magnitude-0.25") => p with { Magnitude = 0.25f },
                (PassiveTrigger.OnDeath, "death-duration-4") => p with { DurationTicks = 4 * ticks },
                (PassiveTrigger.OnDeath, "death-radius-8") => p with { Radius = 8 },
                (PassiveTrigger.OnDeath, "death-units-plus-raider") => p with { Units = p.Units.Add(raider) },
                (PassiveTrigger.OnDeath, "death-units-raider") => p with { Units = ImmutableArray.Create(raider) },
                (PassiveTrigger.OnDeath, "death-slowed") => p with { Status = slowed },
                _ => p,
            }).ToImmutableArray();

        AbilityContentTests.Result r = Compare(Data, "whirlwind", PageTables.FactionLines("whirlwind"),
            passivesOf: u => u.Id == zealot.Id ? passives : u.Passives);

        string want = $"'{passive}' {field}:";
        Assert.True(r.Problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem naming {want}\n" + string.Join("\n", r.Problems));
    }

    /// <summary>A data passive with no page sentence fails naming the unit and the trigger.</summary>
    [Fact]
    public void ADataPassiveMissingFromThePage_FailsNamingUnitAndTrigger()
    {
        string[] lines = Mutated("whirlwind", " Passive **Martyrdom**: when a Zealot dies,", "");

        AbilityContentTests.Result r = Compare(Data, "whirlwind", lines);

        Assert.Contains(r.Problems, p => p.Contains("'Zealot' passive onDeath:", StringComparison.Ordinal));
    }

    /// <summary>
    /// BUG-0470: a second sentence with an already-stated trigger for the same unit fails naming both passives and quoting
    /// both sentences, whatever its numbers (units.json holds one passive per trigger, so data -> page reads only the first).
    /// </summary>
    [Theory]
    [InlineData("Last Rites", "Martyrdom", "when a Zealot dies, Zealots within 6 m gain Frenzied (+50% attack speed) for 5 s.")]
    [InlineData("Last Rites", "Martyrdom", "when a Zealot dies, Zealots within 12 m gain Frenzied (+90% attack speed) for 9 s.")]
    [InlineData("Last Stand", "Frenzy of the Apocalypse", "below 25% HP gains Frenzied (+80% attack speed).")]
    public void ASecondSameTriggerSentence_FailsNamingBoth(string second, string first, string text)
    {
        string[] lines = Mutated("whirlwind", "for 5 s.", $"for 5 s. Passive **{second}**: {text}");

        AbilityContentTests.Result r = Compare(Data, "whirlwind", lines);

        Assert.Contains(r.Problems, p => p.Contains($"'Zealot' passive '{second}' trigger: a second", StringComparison.Ordinal)
            && p.Contains($"after '{first}'", StringComparison.Ordinal) && p.Contains(text.TrimEnd('.'), StringComparison.Ordinal));
        // Pending on both sides it is reported, not failed (no shipped pair today; guards the allowance's shape).
        AbilityContentTests.Result pending = Compare(Data, "whirlwind", lines, pendingPassives: new[] { first, second });
        Assert.DoesNotContain(pending.Problems, p => p.Contains("a second", StringComparison.Ordinal));
    }

    /// <summary>The allowance works both ways: an unknown page passive fails, a pending one is only reported.</summary>
    [Fact]
    public void AnUnknownPagePassive_Fails_ButAPendingOneDoesNot()
    {
        string[] lines = Mutated("whirlwind", "for 5 s.", "for 5 s. Passive **Holy Rage**: +2 armor while near a Priest.");
        AbilityContentTests.Result failing = Compare(Data, "whirlwind", lines);
        AbilityContentTests.Result pending = Compare(Data, "whirlwind", lines, pendingPassives: new[] { "Holy Rage" });

        Assert.Contains(failing.Problems, p => p.Contains("'Holy Rage'", StringComparison.Ordinal));
        Assert.True(pending.Problems.Count == 0, string.Join("\n", pending.Problems));
        Assert.Contains(pending.Reports, l => l.Contains("'Holy Rage'", StringComparison.Ordinal) && l.Contains("pending", StringComparison.Ordinal));
    }

    /// <summary>Equivalent wordings still pass: the sentence's numbers are read by their words, not their position.</summary>
    [Theory]
    [InlineData("below 50% HP gains Frenzied (+50% attack speed)", "below 50 % HP gains Frenzied, +50 % attack speed")]
    [InlineData("Zealots within 6 m gain Frenzied (+50% attack speed) for 5 s", "Zealots within 6 m gain Frenzied for 5 s (+50% attack speed)")]
    public void AnEquivalentWording_Passes(string text, string mutant)
    {
        AbilityContentTests.Result r = Compare(Data, "whirlwind", Mutated("whirlwind", text, mutant));

        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
    }

    // ---- D-R36: the onHit shape (the war-dog's Hamstring, teblor.md), pinned before any shipped unit has one ----

    /// <summary>teblor.md's Hamstring sentence, as <see cref="PagePassives"/> reads it from the War-dog's note.</summary>
    private static PagePassive TeblorHamstring() =>
        Assert.Single(PagePassives(PageTables.FactionLines("teblor"), "teblor.md"), p => p.Name == "Hamstring");

    /// <summary>
    /// malazan.md with teblor.md's Hamstring sentence (<paramref name="text"/>, default its own words) added to the Sapper's
    /// note, and the Sapper given the data's Hamstring in memory (onHit, slowed, <paramref name="magnitude"/>,
    /// <paramref name="seconds"/>): the war-dog row's shape on a shipped unit, since no Teblor unit is loaded yet.
    /// </summary>
    private static AbilityContentTests.Result CompareHamstring(string? text = null, float magnitude = 0.2f, double seconds = 2, bool withPassive = true)
    {
        string sentence = text ?? TeblorHamstring().Text;
        string[] lines = Mutated("malazan", "Fragile; wants an escort.", $"Fragile; wants an escort. Passive **Hamstring**: {sentence}");
        UnitDef sapper = Data.Units[Data.FindUnit("malazan_sapper")];
        var hamstring = new UnitPassive
        {
            Trigger = PassiveTrigger.OnHit,
            Status = Data.Statuses.Single(x => x.Key == "slowed").Id,
            Magnitude = magnitude,
            DurationTicks = (int)Math.Round(seconds * SimConstants.TicksPerSecond),
            Units = ImmutableArray<int>.Empty,
        };
        ImmutableArray<UnitPassive> passives = withPassive ? sapper.Passives.Add(hamstring) : sapper.Passives;
        return Compare(Data, "malazan", lines, passivesOf: u => u.Id == sapper.Id ? passives : u.Passives);
    }

    [Fact]
    public void TeblorsHamstringSentence_IsTheWarDogsOnHitPassive()
    {
        PagePassive p = TeblorHamstring();
        Assert.Equal("War-dog", p.Unit);
        AbilityContentTests.Result r = CompareHamstring();
        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
    }

    /// <summary>The onHit pin reads each field: a page word or a data value changed alone fails naming the field.</summary>
    [Theory]
    [InlineData("Slowed 20%", "Slowed 30%", 0.2f, 2.0, "'Hamstring' magnitude:")]
    [InlineData("Slowed 20%", "Slowed", 0.2f, 2.0, "'Hamstring' magnitude:")]
    [InlineData("for 2 s", "for 3 s", 0.2f, 2.0, "'Hamstring' duration:")]
    [InlineData("for 2 s", "briefly", 0.2f, 2.0, "'Hamstring' duration:")]
    [InlineData("leaves it Slowed", "leaves it Burning", 0.2f, 2.0, "'Hamstring' status:")]
    [InlineData("", "", 0.25f, 2.0, "'Hamstring' magnitude:")]
    [InlineData("", "", 0.2f, 3.0, "'Hamstring' duration:")]
    [InlineData("each bite", "a bite", 0.2f, 2.0, "passive onHit: no \"Passive **Name**:\" sentence")]
    public void AMutatedHamstring_FailsNamingTheField(string text, string mutant, float magnitude, double seconds, string want)
    {
        string sentence = TeblorHamstring().Text;
        if (text.Length > 0)
        {
            Assert.Contains(text, sentence, StringComparison.Ordinal);
            sentence = sentence.Replace(text, mutant, StringComparison.Ordinal);
        }
        AbilityContentTests.Result r = CompareHamstring(sentence, magnitude, seconds);
        Assert.True(r.Problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem naming {want}\n" + string.Join("\n", r.Problems));
    }

    /// <summary>The page side alone: a Hamstring sentence with no data passive fails as a promise the data doesn't keep.</summary>
    [Fact]
    public void AHamstringSentence_WithoutTheDataPassive_Fails()
    {
        AbilityContentTests.Result r = CompareHamstring(withPassive: false);
        Assert.Contains(r.Problems, p => p.Contains("'Sapper' passive 'Hamstring' trigger: 'Sapper' has no onHit passive", StringComparison.Ordinal));
    }

    /// <summary>shadow.md's Shadowed sentence, as <see cref="PagePassives"/> reads it from the Shadow Archer's note.</summary>
    private static PagePassive ShadowShadowed() =>
        Assert.Single(PagePassives(PageTables.FactionLines("shadow"), "shadow.md"), p => p.Name == "Shadowed");

    /// <summary>
    /// teblor.md with <paramref name="sentence"/> (default shadow.md's Shadowed words) as "Passive **Shadowed**:" on the
    /// Javelin Thrower's note, compared on <paramref name="data"/> (default <see cref="StillScenes.Still"/>: the Thrower carries
    /// <c>stillStealth</c> stealthed, 3 s, G1's row).
    /// </summary>
    private static AbilityContentTests.Result CompareShadowed(string? sentence = null, GameData? data = null, bool onPage = true)
    {
        string[] lines = PageTables.FactionLines("teblor");
        int thrower = Array.FindIndex(lines, l => l.StartsWith("- **Javelin Thrower:**", StringComparison.Ordinal));
        Assert.True(thrower >= 0, "teblor.md has no '- **Javelin Thrower:**' bullet");
        if (onPage) lines[thrower] += $" Passive **Shadowed**: {sentence ?? ShadowShadowed().Text}";
        return Compare(data ?? StillScenes.Still, "teblor", lines);
    }

    /// <summary>BUG-1691: the Shadowed sentence is the stand-still stealth, the unit's <c>stillStealth</c>, and lands green on G1's row.</summary>
    [Fact]
    public void ShadowsShadowedSentence_IsTheStillStealthField()
    {
        PagePassive p = ShadowShadowed();
        Assert.Equal("Shadow Archer", p.Unit);
        Assert.True(ReadsAsDataField(p.Text));
        UnitDef thrower = StillScenes.Still.Units[StillScenes.Still.FindUnit("teblor_javelin_thrower")];
        Assert.True(thrower.HasStillStealth);
        AbilityContentTests.Result r = CompareShadowed();
        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
    }

    /// <summary>BUG-1691: each stand-still field is read: a wrong delay or status fails naming it; so does either side alone.</summary>
    [Theory]
    [InlineData("after 3 s", "after 4 s", "'Shadowed' stillStealth.delay: page 'becomes Stealthed after 4 s without moving', data 3 s")]
    [InlineData("after 3 s", "after 2.5 s", "'Shadowed' stillStealth.delay:")]
    [InlineData("becomes Stealthed", "becomes Slowed", "'Shadowed' stillStealth.status: page 'Slowed', data 'Stealthed'")]
    [InlineData("without moving", "while still", "'Shadowed' trigger: its wording names no trigger")]
    public void AMutatedShadowedSentence_FailsNamingTheField(string text, string mutant, string want)
    {
        string sentence = ShadowShadowed().Text;
        Assert.Contains(text, sentence, StringComparison.Ordinal);
        AbilityContentTests.Result r = CompareShadowed(sentence.Replace(text, mutant, StringComparison.Ordinal));
        Assert.True(r.Problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem naming {want}\n" + string.Join("\n", r.Problems));
    }

    [Fact]
    public void AShadowedSentence_OnAUnitWithoutStillStealth_Fails()
    {
        AbilityContentTests.Result r = CompareShadowed(data: Data);
        Assert.Contains(r.Problems, p => p.Contains("'Javelin Thrower' passive 'Shadowed' stillStealth: page 'becomes Stealthed after 3 s without moving', 'Javelin Thrower' has no stillStealth", StringComparison.Ordinal));
    }

    [Fact]
    public void AUnitWithStillStealth_AndNoSentence_Fails()
    {
        AbilityContentTests.Result r = CompareShadowed(onPage: false);
        Assert.Contains(r.Problems, p => p.Contains("'Javelin Thrower' stillStealth: no \"Passive **Name**:\" sentence states the stand-still stealth ('Stealthed after 3 s without moving')", StringComparison.Ordinal));
    }

    /// <summary>The faction page's lines with <paramref name="text"/> (which must be on exactly one line) replaced.</summary>
    private static string[] Mutated(string faction, string text, string mutant)
    {
        string[] lines = PageTables.FactionLines(faction);
        int[] hits = Enumerable.Range(0, lines.Length).Where(i => lines[i].Contains(text, StringComparison.Ordinal)).ToArray();
        Assert.True(hits.Length == 1, $"{faction}.md has '{text}' on {hits.Length} lines, expected 1");
        lines[hits[0]] = lines[hits[0]].Replace(text, mutant, StringComparison.Ordinal);
        return lines;
    }

    /// <summary>
    /// The "Passive **Name**: text" sentences of each bullet under "### Unit notes" in <paramref name="lines"/>, with the
    /// bullet's unit (continuation lines joined; the text keeps its words, the bold markers inside it dropped).
    /// </summary>
    public static List<PagePassive> PagePassives(string[] lines, string where)
    {
        int i = PageTables.Heading(lines, "### Unit notes", where);
        var bullets = new List<string>();
        for (i++; i < lines.Length && !lines[i].StartsWith('#'); i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal)) bullets.Add(line[2..]);
            else if (line.Length > 0 && bullets.Count > 0 && lines[i].StartsWith(' ')) bullets[^1] += " " + line;
        }
        var result = new List<PagePassive>();
        foreach (string bullet in bullets)
        {
            Match unit = BulletUnit.Match(bullet);
            if (!unit.Success) continue;
            foreach (Match m in PassiveSentence.Matches(bullet))
                result.Add(new PagePassive(unit.Groups["unit"].Value.Trim(), m.Groups["name"].Value.Trim(), m.Groups["text"].Value.Replace("**", "").Trim()));
        }
        return result;
    }

    /// <summary>The trigger a page sentence's wording names, or null when it is neither shape.</summary>
    private static PassiveTrigger? TriggerOf(string text) =>
        BelowHp.IsMatch(text) ? PassiveTrigger.WhenBelowHp : OnDeath.IsMatch(text) ? PassiveTrigger.OnDeath
        : OnHit.IsMatch(text) ? PassiveTrigger.OnHit : Nearby.IsMatch(text) ? PassiveTrigger.NearbyOwn : null;

    private static string TriggerId(PassiveTrigger t) => DataLimits.PassiveTriggerIds[(int)t];

    /// <summary>
    /// Compares <paramref name="faction"/>'s "Unit notes" passive sentences in <paramref name="lines"/> to its units'
    /// passives, both ways. <paramref name="passivesOf"/> stands in for <see cref="UnitDef.Passives"/> (in-memory mutants),
    /// <paramref name="pendingPassives"/> for <see cref="PendingPassives"/>. Problems read
    /// <c>&lt;faction&gt;.md "Unit notes" '&lt;Unit&gt;' passive '&lt;Name&gt;' &lt;field&gt;: ...</c>, the field a
    /// <c>passives[]</c> JSON field (or <c>trigger</c> / <c>unit</c>, the dying unit of an <c>onDeath</c>).
    /// </summary>
    public static AbilityContentTests.Result Compare(GameData data, string faction, string[] lines,
        Func<UnitDef, ImmutableArray<UnitPassive>>? passivesOf = null, IReadOnlyCollection<string>? pendingPassives = null)
    {
        Func<UnitDef, ImmutableArray<UnitPassive>> of = passivesOf ?? (u => u.Passives);
        IReadOnlyCollection<string> pending = pendingPassives ?? PendingPassives;
        var r = new AbilityContentTests.Result(new List<string>(), new List<string>());
        string where = $"{faction}.md \"Unit notes\"";
        List<PagePassive> page = PagePassives(lines, faction + ".md");
        int factionId = data.FindFaction(faction);
        UnitDef[] units = data.Units.Where(u => u.Faction == factionId).ToArray();

        // Data -> page: every loaded passive has its sentence, and the sentence states each of its fields.
        foreach (UnitDef u in units)
            foreach (UnitPassive p in of(u))
            {
                PagePassive? pp = page.FirstOrDefault(x => x.Unit == u.DisplayName && TriggerOf(x.Text) == p.Trigger);
                if (pp == null)
                {
                    r.Problems.Add($"{where} '{u.DisplayName}' passive {TriggerId(p.Trigger)}: no \"Passive **Name**:\" sentence states the data passive");
                    continue;
                }
                bool isPending = pending.Contains(pp.Name);
                List<string> sink = isPending ? r.Reports : r.Problems;
                if (isPending) r.Reports.Add($"{where} '{pp.Name}' has landed in units.json; drop it from PendingPassives to pin it");
                foreach ((string field, string problem) in FieldProblems(data, u, p, pp.Text))
                    sink.Add($"{where} '{u.DisplayName}' passive '{pp.Name}' {field}: {problem}");
            }
        // BUG-1691: a unit's stillStealth is stated by a stand-still sentence on its bullet.
        foreach (UnitDef u in units)
            if (u.HasStillStealth && !page.Any(x => x.Unit == u.DisplayName && StillSentence.IsMatch(x.Text)))
                r.Problems.Add($"{where} '{u.DisplayName}' stillStealth: no \"Passive **Name**:\" sentence states the stand-still stealth "
                    + $"('{data.Statuses[u.StillStealthStatus].DisplayName} after {Text(u.StillStealthDelayTicks / (double)SimConstants.TicksPerSecond)} s without moving')");

        // Page -> data: every sentence is a loaded passive of its unit, or pending; and at most one sentence per (unit,
        // trigger), because units.json holds at most one passive per trigger (BUG-0470): data -> page above reads only the
        // first, so a second one's numbers would otherwise never be compared.
        for (int i = 0; i < page.Count; i++)
        {
            PagePassive pp = page[i];
            UnitDef? u = units.FirstOrDefault(x => x.DisplayName == pp.Unit);
            Match still = StillSentence.Match(pp.Text);
            if (still.Success)
            {
                // BUG-1691: the stand-still stealth is the unit's stillStealth field, compared to its status and delay.
                List<string> stillSink = pending.Contains(pp.Name) ? r.Reports : r.Problems;
                string head = $"{where} '{pp.Unit}' passive '{pp.Name}' stillStealth";
                if (u == null) stillSink.Add($"{head}: no {faction} unit is named '{pp.Unit}'");
                else if (!u.HasStillStealth) stillSink.Add($"{head}: page '{still.Value}', '{pp.Unit}' has no stillStealth in units.json");
                else
                {
                    string status = data.Statuses[u.StillStealthStatus].DisplayName;
                    double delay = u.StillStealthDelayTicks / (double)SimConstants.TicksPerSecond;
                    if (still.Groups["status"].Value != status) stillSink.Add($"{head}.status: page '{still.Groups["status"].Value}', data '{status}'");
                    if (!Same(Number(still), delay)) stillSink.Add($"{head}.delay: page '{still.Value}', data {Text(delay)} s");
                }
                continue;
            }
            PassiveTrigger? t = TriggerOf(pp.Text);
            PagePassive? first = t == null ? null : page.Take(i).FirstOrDefault(x => x.Unit == pp.Unit && TriggerOf(x.Text) == t);
            if (first != null)
            {
                string twice = $"{where} '{pp.Unit}' passive '{pp.Name}' trigger: a second {TriggerId(t!.Value)} sentence for '{pp.Unit}' after '{first.Name}' "
                    + $"(first '{first.Text}', second '{pp.Text}'); units.json holds one passive per trigger, so both can't be true";
                if (pending.Contains(pp.Name) && pending.Contains(first.Name)) r.Reports.Add(twice + " (pending: reported, not failed)");
                else r.Problems.Add(twice);
                continue;
            }
            bool loaded = u != null && t is PassiveTrigger trigger && of(u).Any(p => p.Trigger == trigger);
            if (loaded) continue;
            string what = u == null ? $"no {faction} unit is named '{pp.Unit}'"
                : t == null ? "its wording names no trigger the sim has (\"below <n>% HP\", \"when a <Unit> dies\", \"each bite\" / \"each hit\", \"+<n>% damage for each other <Unit> within <n> m\")"
                : $"'{pp.Unit}' has no {TriggerId(t.Value)} passive in units.json";
            if (pending.Contains(pp.Name)) r.Reports.Add($"{where} '{pp.Unit}' passive '{pp.Name}' trigger: {what} (pending: reported, not failed)");
            else r.Problems.Add($"{where} '{pp.Unit}' passive '{pp.Name}' trigger: {what}");
        }
        return r;
    }

    /// <summary>Each field of <paramref name="p"/> against the words of its page sentence <paramref name="text"/>.</summary>
    private static IEnumerable<(string Field, string Problem)> FieldProblems(GameData data, UnitDef unit, UnitPassive p, string text)
    {
        if (p.Trigger == PassiveTrigger.NearbyOwn)
        {
            foreach (var x in NearbyProblems(data, p, text)) yield return x;
            yield break;
        }
        StatusDef status = data.Statuses[p.Status];

        Match gains = Gains.Match(text);
        if (!gains.Success) yield return ("status", $"page '{text}' names no status it gains, data '{status.DisplayName}'");
        else if (gains.Groups["status"].Value != status.DisplayName) yield return ("status", $"page '{gains.Groups["status"].Value}', data '{status.DisplayName}'");

        foreach ((string field, string problem) in MagnitudeProblems(status, p.Magnitude, text)) yield return (field, problem);

        Match below = BelowHp.Match(text);
        if (p.Trigger == PassiveTrigger.WhenBelowHp && (!below.Success || !Same(Number(below) / 100.0, p.HpFraction)))
            yield return ("hpFraction", below.Success ? $"page '{below.Value}', data {Pct(p.HpFraction)}%" : $"page '{text}' states no 'below <n>% HP', data {Pct(p.HpFraction)}%");

        // Duration and radius: an onDeath passive states both, an onHit one the duration only; a whenBelowHp one holds while
        // below the line and states neither.
        bool death = p.Trigger == PassiveTrigger.OnDeath;
        bool timed = death || p.Trigger == PassiveTrigger.OnHit;
        Match dur = For.Match(text);
        double seconds = p.DurationTicks / (double)SimConstants.TicksPerSecond;
        if (timed ? !dur.Success || !Same(Number(dur), seconds) : dur.Success)
            yield return ("duration", dur.Success ? $"page '{dur.Value}', data {(timed ? Text(seconds) + " s" : "none (held while below the line)")}" : $"page '{text}' states no 'for <n> s', data {Text(seconds)} s");
        Match within = Within.Match(text);
        if (death ? !within.Success || !Same(Number(within), p.Radius) : within.Success)
            yield return ("radius", within.Success ? $"page '{within.Value}', data {(death ? Text(p.Radius) + " m" : "none")}" : $"page '{text}' states no 'within <n> m', data {Text(p.Radius)} m");

        if (!death) yield break;

        Match dies = OnDeath.Match(text);
        if (dies.Groups["unit"].Value != unit.DisplayName) yield return ("unit", $"page 'when a {dies.Groups["unit"].Value} dies', data the passive is {unit.DisplayName}'s");

        string[] want = p.Units.Select(id => data.Units[id].DisplayName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Match takers = Takers.Match(text);
        string[] got = takers.Success
            ? Regex.Split(takers.Groups["list"].Value, @",\s*|\s+and\s+", Opts).Select(Singular).OrderBy(x => x, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        if (!got.SequenceEqual(want))
            yield return ("units", $"page '{(takers.Success ? takers.Groups["list"].Value : text)}', data {string.Join(", ", want)}");
    }

    /// <summary>
    /// D35: a <c>nearbyOwn</c> passive against its sentence: <c>damageBonus</c> the per-mate percent, <c>maxStacks</c> the
    /// cap (<c>maxStacks</c> x <c>damageBonus</c>, "(max +30%)"), <c>radius</c> the "within N m", <c>units</c> the counted
    /// types by display name. The passive has no status, magnitude or duration to state.
    /// </summary>
    private static IEnumerable<(string Field, string Problem)> NearbyProblems(GameData data, UnitPassive p, string text)
    {
        NearbyReading? r = ReadNearby(text);
        double perMate = Math.Round(p.DamageBonus * 100.0, 4), cap = Math.Round(p.MaxStacks * p.DamageBonus * 100.0, 4);
        if (r == null)
        {
            yield return ("trigger", $"page '{text}' is not '+<n>% damage for each other <Unit> within <n> m', data nearbyOwn");
            yield break;
        }
        if (!Same(r.PerMate, perMate)) yield return ("damageBonus", $"page +{Text(r.PerMate)}% for each other, data +{Text(perMate)}%");
        if (r.Cap is not double c) yield return ("maxStacks", $"page '{text}' states no cap ('max +<n>%'), data {p.MaxStacks} x +{Text(perMate)}% = +{Text(cap)}%");
        else if (!Same(c, cap)) yield return ("maxStacks", $"page cap +{Text(c)}%, data {p.MaxStacks} x +{Text(perMate)}% = +{Text(cap)}%");
        if (!Same(r.Radius, p.Radius)) yield return ("radius", $"page within {Text(r.Radius)} m, data {Text(p.Radius)} m");
        string[] want = p.Units.Select(id => data.Units[id].DisplayName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        string[] got = r.Units.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!got.SequenceEqual(want)) yield return ("units", $"page '{string.Join(", ", r.Units)}', data {string.Join(", ", want)}");
    }

    /// <summary>
    /// The magnitude claim a status kind's sentence makes: Frenzied "+&lt;n&gt;% attack speed", Slowed (D-R36, the onHit
    /// Hamstring) "Slowed &lt;n&gt;%" (or "Slowed by &lt;n&gt;%"); other kinds fail until pinned.
    /// </summary>
    private static IEnumerable<(string Field, string Problem)> MagnitudeProblems(StatusDef status, float magnitude, string text)
    {
        if (status.Kind == StatusKind.Slow)
        {
            Match slow = Regex.Match(text, @"\b" + Regex.Escape(status.DisplayName) + @" (?:by )?" + Num + @" ?%", Opts);
            if (!slow.Success) yield return ("magnitude", $"page '{text}' states no '{status.DisplayName} <n>%', data {Pct(magnitude)}%");
            else if (!Same(Number(slow) / 100.0, magnitude)) yield return ("magnitude", $"page '{slow.Value}', data {status.DisplayName} {Pct(magnitude)}%");
            yield break;
        }
        if (status.Kind != StatusKind.Frenzied)
        {
            yield return ("magnitude", $"no page wording is pinned for a {DataLimits.StatusKindIds[(int)status.Kind]} passive's magnitude (data {Text(magnitude)}); extend PassiveContentTests");
            yield break;
        }
        Match m = AttackSpeed.Match(text);
        if (!m.Success) yield return ("magnitude", $"page '{text}' states no '+<n>% attack speed', data +{Pct(magnitude)}%");
        else if (!Same(Number(m) / 100.0, magnitude)) yield return ("magnitude", $"page '{m.Value}', data +{Pct(magnitude)}% attack speed");
    }

    /// <summary>"Zealots" -> "Zealot": the page lists the taking types in the plural.</summary>
    private static string Singular(string name)
    {
        string n = name.Trim();
        return n.EndsWith('s') ? n[..^1] : n;
    }

    private static double Number(Match m) => double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);

    private static bool Same(double a, double b) => Math.Abs(a - b) < 1e-4;

    private static string Pct(float fraction) => Text(Math.Round(fraction * 100.0, 4));

    private static string Text(double v) => FactionPage.Text(Math.Round(v, 4));
}
