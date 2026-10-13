using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D16 inspection): the file side of row J for every cell. <c>AiContentTests.J_AOneValueFileEdit_FailsByName</c> edits three
/// malazan fields; this edits each of the 8 fields of each difficulty of both factions' <c>ai.json</c> alone (the file still
/// loads) and asserts the page pin (<see cref="AiContentTests.DifficultyProblems"/>) reports exactly that faction, difficulty
/// and column, and nothing for the other faction.
/// </summary>
public class AiDifficultyFileSideQaTests
{
    private static readonly string[] Fields =
    {
        "thinkInterval", "buildOrderSlack", "waveSizeMultiplier", "gatherBonus", "focusFire", "pullBackWounded",
        "useAbilities", "expandAfter",
    };

    private static readonly string[] Difficulties = { "easy", "normal", "hard" };

    public static TheoryData<string, int, int> FileCells()
    {
        var rows = new TheoryData<string, int, int>();
        foreach (string faction in new[] { "malazan", "whirlwind" })
            for (int k = 0; k < Difficulties.Length; k++)
                for (int f = 0; f < Fields.Length; f++)
                    rows.Add(faction, k, f);
        return rows;
    }

    [Theory]
    [MemberData(nameof(FileCells))]
    public void EachDifficultyField_EditedAlone_FailsThePagePinNamingTheCell(string faction, int difficulty, int field)
    {
        string fieldName = Fields[field];
        GameData d = AiScenes.DataWithAi(faction, root =>
        {
            JsonNode block = root["difficulties"]![Difficulties[difficulty]]!;
            JsonNode v = block[fieldName]!;
            if (v.GetValueKind() == System.Text.Json.JsonValueKind.True || v.GetValueKind() == System.Text.Json.JsonValueKind.False)
                block[fieldName] = !v.GetValue<bool>();
            else if (fieldName == "gatherBonus")
                block[fieldName] = v.GetValue<double>() + 0.05;
            else
                block[fieldName] = v.GetValue<double>() + 0.5;
        });
        string column = AiContentTests.DifficultyColumns[field + 1];
        string row = AiContentTests.DifficultyRows[difficulty];
        List<string> problems = AiContentTests.DifficultyProblems(d, faction, PageTables.FactionLines(faction));
        Assert.True(problems.Count == 1 && problems[0].StartsWith($"{faction} {row} {column}: page '", StringComparison.Ordinal),
            $"{faction} {Difficulties[difficulty]}.{fieldName} edited: {string.Join("; ", problems)}");
        string other = faction == "malazan" ? "whirlwind" : "malazan";
        Assert.Empty(AiContentTests.DifficultyProblems(d, other, PageTables.FactionLines(other)));
    }
}
