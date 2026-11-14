using System.Globalization;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D31: the two numbers the landing adds outside the Frenzy rows, held to the docs both ways.
/// <list type="bullet">
/// <item><b>A worker's own load</b> (<c>units.json</c> <c>carry</c>, M7-7): each faction page's "Unit notes" bullet of its
/// worker says "carries N" exactly when the worker has a <c>carry</c> of N (a bullet that also says "not in the data" is a
/// promise the data must not keep yet), and docs/02's "Worker carry capacity" row names every worker with one, "the
/// &lt;Faction id&gt; &lt;worker&gt; carries N", and no other.</item>
/// <item><b>An opening's own reserve share</b> (<c>ai.json</c> <c>attack.reserveArmyShare</c>, M7-6): docs/02's reserve
/// sentence names every opening with one, "the &lt;faction&gt; `&lt;opening&gt;` opening saves at N of its wave", and no
/// other (the page side is <see cref="AiContentTests"/>' "Reserve:" bullet).</item>
/// </list>
/// </summary>
public class CarryAndReserveShareContentTests
{
    private const string Doc02 = "docs/02-game-design.md";

    /// <summary>A faction as docs/02 names it in these sentences: its id capitalized ("Teblor", "Malazan").</summary>
    private static string Cap(string key) => char.ToUpperInvariant(key[0]) + key[1..];

    private static string Num(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The "### Unit notes" bullet of <paramref name="name"/> ("- **Gatherer:** ..."), continuation lines joined; null when absent.</summary>
    private static string? NoteBullet(string[] lines, string name)
    {
        int h = Array.IndexOf(lines, "### Unit notes");
        if (h < 0) return null;
        string head = $"- **{name}:**";
        for (int i = h + 1; i < lines.Length && !lines[i].StartsWith('#'); i++)
        {
            if (!lines[i].StartsWith(head, StringComparison.Ordinal)) continue;
            string text = lines[i];
            for (int k = i + 1; k < lines.Length && lines[k].StartsWith("  ", StringComparison.Ordinal); k++) text += " " + lines[k].Trim();
            return text;
        }
        return null;
    }

    private static readonly Regex Carries = new(@"\bcarries (\d+)\b", RegexOptions.CultureInvariant);

    /// <summary>Every way <paramref name="faction"/>'s page (<paramref name="lines"/>) and its workers' <c>carry</c> in <paramref name="d"/> disagree.</summary>
    internal static List<string> PageCarryProblems(GameData d, string faction, string[] lines)
    {
        var problems = new List<string>();
        int f = d.FindFaction(faction);
        foreach (UnitDef w in d.Units.Where(u => u.Faction == f && u.Slot == UnitSlot.Worker))
        {
            string who = $"{faction}.md Unit notes {w.DisplayName}";
            string? bullet = NoteBullet(lines, w.DisplayName);
            Match m = bullet == null ? Match.Empty : Carries.Match(bullet);
            bool promise = bullet != null && bullet.Contains("not in the data", StringComparison.Ordinal);
            if (w.Carry > 0)
            {
                if (!m.Success) problems.Add($"{who}: data carry {w.Carry}, which the page does not state ('carries N')");
                else if (m.Groups[1].Value != w.Carry.ToString(CultureInfo.InvariantCulture)) problems.Add($"{who}: page 'carries {m.Groups[1].Value}' vs data carry {w.Carry}");
                if (promise) problems.Add($"{who}: the page notes the carry as 'not in the data', but the data has carry {w.Carry}; drop the note");
            }
            else if (m.Success && !promise)
                problems.Add($"{who}: page 'carries {m.Groups[1].Value}' vs data: no carry (rules.json workerCarry {d.Rules.WorkerCarry})");
        }
        return problems;
    }

    /// <summary>"the Teblor Gatherer carries 15" claims in docs/02's carry row, against every unit with a <c>carry</c>.</summary>
    internal static List<string> Doc02CarryProblems(GameData d, string[] doc02)
    {
        string? row = doc02.FirstOrDefault(l => l.StartsWith("| Worker carry capacity |", StringComparison.Ordinal));
        if (row == null) return new List<string> { "docs/02 has no '| Worker carry capacity |' row" };
        var page = Regex.Matches(row, @"\bthe ([A-Z][\w-]*) ([A-Z][\w-]*(?: [A-Z][\w-]*)*) carries (\d+)\b")
            .Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value}").ToList();
        var data = d.Units.Where(u => u.Carry > 0).Select(u => $"{Cap(d.Factions[u.Faction].Key)} {u.DisplayName} {u.Carry}").ToList();
        var problems = new List<string>();
        foreach (string p in page.Except(data)) problems.Add($"docs/02 carry row: page 'the {p}' is no worker's carry in the data [{string.Join(", ", data)}]");
        foreach (string x in data.Except(page)) problems.Add($"docs/02 carry row: data '{x}', which the row does not state ('the <faction> <worker> carries N')");
        if (data.Count > 0 && row.Contains("none ships one yet", StringComparison.Ordinal)) problems.Add("docs/02 carry row: says 'none ships one yet', but the data has a worker carry");
        return problems;
    }

    /// <summary>"the Teblor `standard` opening saves at 0.25 of its wave" claims in docs/02's reserve sentence, against every opening's own share.</summary>
    internal static List<string> Doc02ShareProblems(GameData d, string[] doc02)
    {
        string text = string.Join(" ", doc02.Select(l => l.Trim()));
        var page = Regex.Matches(text, @"\bthe ([A-Z][\w-]*) `(\w+)` opening saves at (\d+(?:\.\d+)?) of its wave\b")
            .Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value} {Num(double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))}").ToList();
        var data = new List<string>();
        for (int f = 0; f < d.Factions.Length; f++)
            foreach (AiOpening o in d.Ai[f].Openings)
                if (o.ReserveArmyShare is float s) data.Add($"{Cap(d.Factions[f].Key)} {o.Id} {Num(s)}");
        var problems = new List<string>();
        foreach (string p in page.Except(data)) problems.Add($"docs/02 reserve share: page '{p}' is no opening's own share in the data [{string.Join(", ", data)}]");
        foreach (string x in data.Except(page)) problems.Add($"docs/02 reserve share: data '{x}', which docs/02 does not state ('the <faction> `<opening>` opening saves at N of its wave')");
        if (data.Count > 0 && text.Contains("no shipped opening does yet", StringComparison.Ordinal)) problems.Add("docs/02 reserve share: says 'no shipped opening does yet', but an opening has its own share");
        return problems;
    }

    private static void AssertNone(List<string> problems) => Assert.True(problems.Count == 0, string.Join("\n", problems));

    private static void AssertNamed(List<string> problems, string want) =>
        Assert.True(problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem containing '{want}':\n" + string.Join("\n", problems));

    private static GameData Load(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        edit(dir);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    [Theory]
    [MemberData(nameof(ContentFactions.Theory), MemberType = typeof(ContentFactions))]
    public void EachPagesWorkerNote_StatesItsCarry_BothWays(string faction) =>
        AssertNone(PageCarryProblems(TestSim.Data, faction, PageTables.FactionLines(faction)));

    [Fact]
    public void Docs02sCarryRow_And_ReserveSentence_MatchTheData_BothWays()
    {
        string[] doc02 = PageTables.Lines(Doc02);
        AssertNone(Doc02CarryProblems(TestSim.Data, doc02));
        AssertNone(Doc02ShareProblems(TestSim.Data, doc02));
    }

    /// <summary>Not vacuous: the shipped data has exactly the landing's carry (the Gatherer, 15) and share (the Teblor standard, 0.25), and nothing else.</summary>
    [Fact]
    public void TheShippedData_CarriesTheLandingsCarryAndShare_AndNoOther()
    {
        GameData d = TestSim.Data;
        Assert.Equal(new[] { "teblor_gatherer 15" }, d.Units.Where(u => u.Carry > 0).Select(u => $"{u.Key} {u.Carry}"));
        Assert.Equal(15, d.CarryOf(d.FindUnit("teblor_gatherer")));
        Assert.Equal(10, d.Rules.WorkerCarry);
        int teblor = d.FindFaction("teblor");
        Assert.Equal(0.25f, d.Ai[teblor].Openings[0].ReserveArmyShare);
        for (int f = 0; f < d.Factions.Length; f++)
            for (int k = 0; k < d.Ai[f].Openings.Length; k++)
                if (f != teblor || k != 0) Assert.Null(d.Ai[f].Openings[k].ReserveArmyShare);
    }

    /// <summary>The page side: the Gatherer's note changed, dropped, or noted "not in the data" each fail naming the Gatherer.</summary>
    [Theory]
    [InlineData("carries 15 instead of 10", "carries 16 instead of 10", "teblor.md Unit notes Gatherer: page 'carries 16' vs data carry 15")]
    [InlineData("carries 15 instead of 10", "is a worker", "teblor.md Unit notes Gatherer: data carry 15, which the page does not state")]
    [InlineData("(`units.json` `carry` 15,", "(not in the data: `units.json` `carry` 15,", "teblor.md Unit notes Gatherer: the page notes the carry as 'not in the data'")]
    public void TheGatherersNote_EditedByHand_FailsByName(string find, string replace, string want)
    {
        string[] lines = PageTables.FactionLines("teblor");
        int i = Array.FindIndex(lines, l => l.Contains(find, StringComparison.Ordinal));
        Assert.True(i >= 0, $"teblor.md has no line containing '{find}'");
        lines[i] = lines[i].Replace(find, replace, StringComparison.Ordinal);
        AssertNamed(PageCarryProblems(TestSim.Data, "teblor", lines), want);
    }

    /// <summary>
    /// The file side: the Gatherer's carry moved, removed, and a carry put on a second worker (the Malazan Laborer) each fail
    /// by name, on the page and on docs/02; a carry on a soldier is the loader's own error.
    /// </summary>
    [Fact]
    public void ACarryMovedRemovedOrOnASecondUnit_FailsByName()
    {
        string[] doc02 = PageTables.Lines(Doc02);
        GameData sixteen = Load(dir => dir.SetUnitField("teblor", "teblor_gatherer", "carry", "16"));
        AssertNamed(PageCarryProblems(sixteen, "teblor", PageTables.FactionLines("teblor")), "teblor.md Unit notes Gatherer: page 'carries 15' vs data carry 16");
        AssertNamed(Doc02CarryProblems(sixteen, doc02), "docs/02 carry row: data 'Teblor Gatherer 16'");

        GameData none = Load(dir => dir.SetUnitField("teblor", "teblor_gatherer", "carry", null));
        AssertNamed(PageCarryProblems(none, "teblor", PageTables.FactionLines("teblor")), "teblor.md Unit notes Gatherer: page 'carries 15' vs data: no carry");
        AssertNamed(Doc02CarryProblems(none, doc02), "docs/02 carry row: page 'the Teblor Gatherer 15' is no worker's carry");

        GameData second = Load(dir => dir.SetUnitField("malazan", "malazan_laborer", "carry", "12"));
        AssertNamed(PageCarryProblems(second, "malazan", PageTables.FactionLines("malazan")), "malazan.md Unit notes Laborer: data carry 12, which the page does not state");
        AssertNamed(Doc02CarryProblems(second, doc02), "docs/02 carry row: data 'Malazan Laborer 12'");

        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("teblor", "teblor_warrior", "carry", "15");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.False(r.Ok); // a soldier's carry is refused (M7-7)
        Assert.Contains(r.Errors, e => e.ToString().Contains("teblor_warrior", StringComparison.Ordinal) && e.ToString().Contains("carry", StringComparison.Ordinal));
    }

    /// <summary>docs/02's two sentences edited by hand fail naming the claim.</summary>
    [Theory]
    [InlineData("the Teblor Gatherer carries 15", "the Teblor Gatherer carries 12", "docs/02 carry row: page 'the Teblor Gatherer 12'")]
    [InlineData("the Teblor Gatherer carries 15", "none ships one yet", "docs/02 carry row: data 'Teblor Gatherer 15', which the row does not state")]
    [InlineData("saves at 0.25 of its wave", "saves at 0.5 of its wave", "docs/02 reserve share: page 'Teblor standard 0.5'")]
    public void Docs02_EditedByHand_FailsByName(string find, string replace, string want)
    {
        string[] doc02 = PageTables.Lines(Doc02);
        int i = Array.FindIndex(doc02, l => l.Contains(find, StringComparison.Ordinal));
        Assert.True(i >= 0, $"docs/02 has no line containing '{find}'");
        doc02[i] = doc02[i].Replace(find, replace, StringComparison.Ordinal);
        List<string> problems = Doc02CarryProblems(TestSim.Data, doc02).Concat(Doc02ShareProblems(TestSim.Data, doc02)).ToList();
        AssertNamed(problems, want);
    }

    /// <summary>The file side of the share on docs/02: moved to 0.5, removed, and put on a second opening each fail by name.</summary>
    [Fact]
    public void AShareMovedRemovedOrOnASecondOpening_FailsByNameOnDocs02()
    {
        string[] doc02 = PageTables.Lines(Doc02);
        GameData half = Load(dir => dir.EditJson("factions/teblor/ai.json", root => AiScenes.Opening(root)["attack"]!["reserveArmyShare"] = 0.5));
        AssertNamed(Doc02ShareProblems(half, doc02), "docs/02 reserve share: data 'Teblor standard 0.5'");
        GameData gone = Load(dir => dir.EditJson("factions/teblor/ai.json", root => AiScenes.Opening(root)["attack"]!.AsObject().Remove("reserveArmyShare")));
        AssertNamed(Doc02ShareProblems(gone, doc02), "docs/02 reserve share: page 'Teblor standard 0.25' is no opening's own share");
        GameData second = Load(dir => dir.EditJson("factions/malazan/ai.json", root => AiScenes.Opening(root)["attack"]!["reserveArmyShare"] = 0.25));
        AssertNamed(Doc02ShareProblems(second, doc02), "docs/02 reserve share: data 'Malazan standard 0.25'");
    }
}
