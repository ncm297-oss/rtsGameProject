using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D15 inspection): the file side of row (f). <c>AiContentQaTests.F</c> edits each page row alone; this edits each
/// <c>ai.json</c> step's trigger value alone (the file still loads) and asserts the page pin
/// (<see cref="AiContentTests.Problems"/>, what <c>A_EachPage_IsItsAiFile_BothWays</c> runs) names that faction and step.
/// It reads only the trigger, never a count, so M5-3's count-vs-pop change can't move it.
/// </summary>
public class AiContentFileSideQaTests
{
    public static TheoryData<string, int> FileSteps()
    {
        var rows = new TheoryData<string, int>();
        foreach (string faction in new[] { "malazan", "whirlwind" })
            for (int n = 1; n <= TestSim.Data.Ai[TestSim.Data.FindFaction(faction)].BuildOrder.Length; n++) rows.Add(faction, n);
        return rows;
    }

    [Theory]
    [MemberData(nameof(FileSteps))]
    public void EachAiJsonStep_TriggerEditedAlone_FailsThePagePinNamingTheStep(string faction, int n)
    {
        GameData d = AiScenes.DataWithAi(faction, root =>
        {
            JsonObject at = root["buildOrder"]![n - 1]!["at"]!.AsObject();
            if (at["pop"] != null) at["pop"] = (double)at["pop"]! + 0.5;
            else if (at["time"] != null) at["time"] = (double)at["time"]! + 1;
            else at["workers"] = (int)at["workers"]! + 1;
        });
        List<string> problems = AiContentTests.Problems(d, faction, PageTables.FactionLines(faction));
        Assert.True(problems.Any(p => p.StartsWith($"{faction} step {n}: page '", StringComparison.Ordinal)),
            $"{faction} ai.json step {n} edited: {string.Join("; ", problems)}");
    }
}
