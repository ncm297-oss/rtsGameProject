using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D38 inspection, 2026-11-26-0717): the three shipped Teblor <c>standard</c> build orders. The developer's
/// <c>Content/D38ShippedOrdersTests</c> edits step 18 of each list; these rows go further. Every row of Easy's and Hard's
/// page tables, each cell edited alone, is named on its own list (<c>AiContentQaTests.F</c> covers only <c>buildOrder</c>'s
/// table). Every step of each <c>buildOrders</c> entry, its trigger edited in the file, is named on that list alone
/// (<c>AiContentFileSideQaTests</c> covers only <c>buildOrder</c>). A reserve switch toggled on any train step of any list,
/// on either side, is named. Easy plays opening 0 even beside a second opening that a Normal or Hard seat draws.
/// </summary>
public class D38QaTests
{
    private static GameData Data => TestSim.Data;
    private static readonly string[] Entries = { "easy", "hard" };

    private static AiOpening Standard()
    {
        AiOpening o = Data.Ai[Data.FindFaction("teblor")].Openings[0];
        Assert.Equal("standard", o.Id);
        return o;
    }

    private static AiDifficulty Diff(string key) => key == "easy" ? AiDifficulty.Easy : key == "hard" ? AiDifficulty.Hard : AiDifficulty.Normal;

    /// <summary>The line index of row <paramref name="n"/> of <paramref name="key"/>'s table (null: <c>buildOrder</c>'s, the first under the heading).</summary>
    private static int RowLine(string[] lines, string? key, int n)
    {
        int from = key == null
            ? Array.FindIndex(lines, l => l.StartsWith("### Opening 1: `standard`", StringComparison.Ordinal))
            : Array.FindIndex(lines, l => l.StartsWith("**", StringComparison.Ordinal) && l.Contains(AiContentTests.EntryLabel(key), StringComparison.Ordinal));
        Assert.True(from >= 0, $"teblor.md: no table for {key ?? "buildOrder"}");
        int row = Array.FindIndex(lines, from, l => l.StartsWith($"| {n} | ", StringComparison.Ordinal));
        Assert.True(row > from, $"teblor.md {key ?? "buildOrder"}: no row {n}");
        return row;
    }

    private static string Who(string? key) => key == null ? "teblor standard" : $"teblor standard ({key})";

    // ---------- the page side: every cell of Easy's and Hard's tables ----------

    public static TheoryData<string, int> EntryRows()
    {
        var rows = new TheoryData<string, int>();
        foreach (string key in Entries)
            for (int n = 1; n <= Standard().BuildOrderFor(Diff(key)).Length; n++) rows.Add(key, n);
        return rows;
    }

    /// <summary>Row n of an entry's table: its At, Do, Id and Count, each edited alone, are named on that list's step n and no other list.</summary>
    [Theory]
    [MemberData(nameof(EntryRows))]
    public void EachEntryTableRow_EachCellEditedAlone_IsNamedOnItsListOnly(string key, int n)
    {
        string[] lines = PageTables.FactionLines("teblor");
        Assert.Empty(AiContentTests.Problems(Data, "teblor", lines));
        int row = RowLine(lines, key, n);
        for (int cell = 2; cell <= 5; cell++)
        {
            string[] parts = lines[row].Split('|');
            string v = parts[cell].Trim();
            parts[cell] = " " + (cell switch
            {
                2 => Regex.Replace(v, @"\d+(\.\d+)?$", m => (double.Parse(m.Value, CultureInfo.InvariantCulture) + 0.5).ToString(CultureInfo.InvariantCulture)),
                3 or 4 => "x" + v,
                _ => Regex.Replace(v, @"^\d+", m => (int.Parse(m.Value, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture)),
            }) + " ";
            string[] edited = (string[])lines.Clone();
            edited[row] = string.Join("|", parts);
            List<string> problems = AiContentTests.Problems(Data, "teblor", edited);
            string want = $"{Who(key)} step {n}: page '";
            Assert.True(problems.Count > 0 && problems.All(p => p.StartsWith(want, StringComparison.Ordinal)),
                $"{key} row {n} cell {cell} '{v}' -> '{parts[cell].Trim()}': {string.Join("; ", problems)}");
        }
    }

    public static TheoryData<string, int> TrainSteps()
    {
        var rows = new TheoryData<string, int>();
        AiOpening o = Standard();
        foreach (string key in new[] { "normal", "easy", "hard" })
        {
            ImmutableArray<AiStep> steps = o.BuildOrderFor(Diff(key));
            for (int i = 0; i < steps.Length; i++)
                if (steps[i].Action == AiStepAction.Train) rows.Add(key, i + 1);
        }
        return rows;
    }

    /// <summary>The reserve switch on a train step of any list, added or taken out on the page alone, is named on that list's step.</summary>
    [Theory]
    [MemberData(nameof(TrainSteps))]
    public void EachTrainRow_SwitchToggledOnThePage_IsNamedOnItsList(string list, int n)
    {
        string? key = list == "normal" ? null : list;
        string[] lines = PageTables.FactionLines("teblor");
        int row = RowLine(lines, key, n);
        string[] parts = lines[row].Split('|');
        string count = parts[5].Trim();
        string words = " " + AiContentTests.ThroughReserveWords;
        parts[5] = " " + (count.Contains(AiContentTests.ThroughReserveWords, StringComparison.Ordinal) ? count.Replace(words, "", StringComparison.Ordinal) : count + words) + " ";
        string[] edited = (string[])lines.Clone();
        edited[row] = string.Join("|", parts);
        List<string> problems = AiContentTests.Problems(Data, "teblor", edited);
        string p = Assert.Single(problems);
        Assert.StartsWith($"{Who(key)} step {n}: page '", p);
    }

    /// <summary>The same switch toggled in the file alone is named on that list's step only.</summary>
    [Theory]
    [MemberData(nameof(TrainSteps))]
    public void EachTrainStep_SwitchToggledInTheFile_IsNamedOnItsListOnly(string list, int n)
    {
        string? key = list == "normal" ? null : list;
        GameData d = AiScenes.DataWithAi("teblor", root =>
        {
            JsonObject o = AiScenes.Opening(root).AsObject();
            JsonObject step = (key == null ? o["buildOrder"] : o["buildOrders"]![key])![n - 1]!.AsObject();
            if (step["throughReserve"] != null) step.Remove("throughReserve");
            else step["throughReserve"] = true;
        });
        List<string> problems = AiContentTests.Problems(d, "teblor", PageTables.FactionLines("teblor"));
        string p = Assert.Single(problems);
        Assert.StartsWith($"{Who(key)} step {n}: page '", p);
    }

    // ---------- the file side: every step of each buildOrders entry ----------

    /// <summary>Each step of Easy's and Hard's lists, its trigger edited alone in the file, is named on that list only (the other two lists unchanged).</summary>
    [Theory]
    [MemberData(nameof(EntryRows))]
    public void EachEntryStep_TriggerEditedInTheFile_IsNamedOnItsListOnly(string key, int n)
    {
        GameData d = AiScenes.DataWithAi("teblor", root =>
        {
            JsonObject at = AiScenes.Opening(root)["buildOrders"]![key]![n - 1]!["at"]!.AsObject();
            if (at["pop"] != null) at["pop"] = (double)at["pop"]! + 0.5;
            else if (at["time"] != null) at["time"] = (double)at["time"]! + 1;
            else at["workers"] = (int)at["workers"]! + 1;
        });
        List<string> problems = AiContentTests.Problems(d, "teblor", PageTables.FactionLines("teblor"));
        string p = Assert.Single(problems);
        Assert.StartsWith($"{Who(key)} step {n}: page '", p);
    }

    /// <summary>An entry's table moved to sit under the other entry's label (the two tables swapped) is caught.</summary>
    [Fact]
    public void TheTwoEntryTables_Swapped_AreCaught()
    {
        string[] lines = PageTables.FactionLines("teblor");
        int e = Array.FindIndex(lines, l => l.StartsWith("**", StringComparison.Ordinal) && l.Contains(AiContentTests.EntryLabel("easy"), StringComparison.Ordinal));
        int h = Array.FindIndex(lines, l => l.StartsWith("**", StringComparison.Ordinal) && l.Contains(AiContentTests.EntryLabel("hard"), StringComparison.Ordinal));
        Assert.True(e > 0 && h > e);
        string[] swapped = (string[])lines.Clone();
        (swapped[e], swapped[h]) = (lines[h], lines[e]); // the labels traded: each table now carries the other key
        Assert.Contains(AiContentTests.Problems(Data, "teblor", swapped), p => p.Contains("is not labelled `buildOrders.easy`", StringComparison.Ordinal));
    }

    // ---------- Easy plays opening 0 only ----------

    /// <summary>
    /// QA focus (5): Teblor ships one opening, so "Easy plays opening 0" is true by count alone. With a forged second opening
    /// (a quiet order, its own <c>buildOrders.easy</c> that must never be played) a Normal and a Hard seat draw both openings
    /// over 24 seeds, each playing its own list of the opening drawn; an Easy seat plays opening 0's <c>buildOrders.easy</c>
    /// on every seed.
    /// </summary>
    [Fact]
    public void EasyPlaysOpeningZeroOnly_EvenBesideASecondOpeningThatOthersDraw()
    {
        GameData d = AiScenes.DataWithAi("teblor", root =>
        {
            JsonArray openings = root["openings"]!.AsArray();
            JsonObject second = openings[0]!.DeepClone().AsObject();
            second["id"] = "qa_second";
            second["displayName"] = "QA second";
            second["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "teblor_gatherer", 1));
            second["buildOrders"] = new JsonObject { ["easy"] = AiScenes.BuildOrder(("time", 3500, "train", "teblor_gatherer", 2)) };
            openings.Add(second);
        });
        int teblor = d.FindFaction("teblor"), malazan = d.FindFaction("malazan");
        AiOpening first = d.Ai[teblor].Openings[0], other = d.Ai[teblor].Openings[1];
        Assert.True(first.BuildOrderFor(AiDifficulty.Easy).SequenceEqual(Standard().BuildOrderFor(AiDifficulty.Easy)));
        var drawn = new Dictionary<AiDifficulty, SortedSet<int>> { [AiDifficulty.Normal] = new(), [AiDifficulty.Hard] = new() };
        for (ulong seed = 1; seed <= 24; seed++)
            foreach (AiDifficulty x in Enum.GetValues<AiDifficulty>())
            {
                PlayerSeat[] seats = { new(teblor, SeatController.Ai, x), new(malazan, SeatController.Ai, AiDifficulty.Normal) };
                AiPlayer ai = AiScenes.Generated(seats, seed, false, out _, data: d).AiOf(0)!;
                AiOpening picked = d.Ai[teblor].Openings[ai.OpeningIndex];
                Assert.True(picked.BuildOrderFor(x).SequenceEqual(ai.Steps), $"seed {seed} {x}: Steps is not the drawn opening's {x} list");
                if (x == AiDifficulty.Easy)
                {
                    Assert.Equal(0, ai.OpeningIndex);
                    Assert.True(ai.Steps.SequenceEqual(first.BuildOrderFor(AiDifficulty.Easy)), $"seed {seed}: Easy does not play K9KC10S");
                    Assert.False(ai.Steps.SequenceEqual(other.BuildOrderFor(AiDifficulty.Easy)));
                }
                else drawn[x].Add(ai.OpeningIndex);
            }
        Assert.Equal(new[] { 0, 1 }, drawn[AiDifficulty.Normal]);
        Assert.Equal(new[] { 0, 1 }, drawn[AiDifficulty.Hard]);
    }

    // ---------- the hash ----------

    /// <summary>
    /// QA focus (6): no test file holds the shipped data hash as a literal (rules 49 / 73); the golden header and the
    /// <c>GatherWedgeQaTests</c> comment (BUG-0750's rule) are the only places it is written.
    /// </summary>
    [Fact]
    public void TheShippedHash_IsWrittenOnlyInTheGoldenAndItsComment()
    {
        string hash = Data.ContentHash().ToString("X16");
        Assert.Contains("data-hash " + hash, File.ReadAllText(ReplayTestRun.GoldenPath));
        string tests = Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests");
        var holders = Directory.GetFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(f => File.ReadAllLines(f).Any(l => l.Contains(hash, StringComparison.OrdinalIgnoreCase) && !l.TrimStart().StartsWith("//", StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(tests, f)).ToArray();
        Assert.True(holders.Length == 0, "the shipped hash as a literal in: " + string.Join(", ", holders));
    }
}
