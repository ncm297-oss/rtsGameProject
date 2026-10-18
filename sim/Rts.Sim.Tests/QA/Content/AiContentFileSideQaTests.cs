using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D15 inspection): the file side of row (f). <c>AiContentQaTests.F</c> edits each page row alone; this edits each
/// <c>ai.json</c> step's trigger value alone (the file still loads) and asserts the page pin
/// (<see cref="AiContentTests.Problems"/>, what <c>A_EachPage_IsItsAiFile_BothWays</c> runs) names that faction, opening and
/// step. It reads only the trigger, never a count, so M5-3's count-vs-pop change can't move it. D21: every step of every
/// opening, not the first opening's only.
/// </summary>
public class AiContentFileSideQaTests
{
    public static TheoryData<string, int, int> FileSteps()
    {
        var rows = new TheoryData<string, int, int>();
        foreach (string faction in new[] { "malazan", "whirlwind" })
        {
            AiDef ai = TestSim.Data.Ai[TestSim.Data.FindFaction(faction)];
            for (int k = 0; k < ai.Openings.Length; k++)
                for (int n = 1; n <= ai.Openings[k].BuildOrder.Length; n++) rows.Add(faction, k, n);
        }
        return rows;
    }

    [Theory]
    [MemberData(nameof(FileSteps))]
    public void EachAiJsonStep_TriggerEditedAlone_FailsThePagePinNamingTheStep(string faction, int k, int n)
    {
        GameData d = AiScenes.DataWithAi(faction, root =>
        {
            JsonObject at = AiScenes.Opening(root, k)["buildOrder"]![n - 1]!["at"]!.AsObject();
            if (at["pop"] != null) at["pop"] = (double)at["pop"]! + 0.5;
            else if (at["time"] != null) at["time"] = (double)at["time"]! + 1;
            else at["workers"] = (int)at["workers"]! + 1;
        });
        string id = TestSim.Data.Ai[TestSim.Data.FindFaction(faction)].Openings[k].Id;
        List<string> problems = AiContentTests.Problems(d, faction, PageTables.FactionLines(faction));
        Assert.True(problems.Any(p => p.StartsWith($"{faction} {id} step {n}: page '", StringComparison.Ordinal)),
            $"{faction} ai.json {id} step {n} edited: {string.Join("; ", problems)}");
    }
}
