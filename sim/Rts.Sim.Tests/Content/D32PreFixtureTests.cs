using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D32's one data hash move: a save and a recording made on the diff base (93c2f3b, data hash <see cref="PreHash"/>, the
/// hash D31 shipped) are refused <c>DataMismatch</c> on the shipped data, with the player-facing text from <c>ui.json</c>.
/// The fixtures (<c>Content/Fixtures/pre_d32/</c>, the pattern of QA's <c>pre_d31</c>) were written by
/// <see cref="WriteThePreD32Fixtures_WhenAsked"/> before the opening changed, and are never rewritten: the writer refuses
/// on any data whose hash is not <see cref="PreHash"/>.
/// </summary>
public class D32PreFixtureTests(ITestOutputHelper output)
{
    /// <summary>The shipped data hash at D32's diff base (D31's move, the golden header before D32).</summary>
    internal const ulong PreHash = 0x27036D3A2F2BA1CBUL;

    private const string WriteVariable = "D32_WRITE_PRE_FIXTURES";

    private static string Fixture(string name) =>
        Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Content", "Fixtures", "pre_d32", name);

    private static string UiText(string section, string key)
    {
        JsonNode ui = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "common", "ui.json")))!;
        return (string)ui[section]![key]!;
    }

    /// <summary>The save: a Shaman's Blood-oil Frenzy running on a Warrior, on a 12-cell flat map (<c>pre_d31</c>'s pattern), 40 ticks after the cast.</summary>
    private static byte[] MakeSave()
    {
        GameData d = TestSim.Data;
        Simulation sim = new(new SimConfig(1, 2, 16, 64) { Data = d, ResourceCapacity = 8 }, LocalMovementTests.Flat(12));
        EntityHandle shaman = BloodOilScenes.Place(sim, 0, "teblor_shaman", CombatScenes.At(sim, 4, 6));
        BloodOilScenes.Place(sim, 0, "teblor_warrior", CombatScenes.At(sim, 6, 6));
        sim.World.Fog.Update();
        BloodOilScenes.Cast(sim, 0, shaman);
        while (sim.TickNumber < 40) sim.Tick();
        return SaveTestRun.Save(sim);
    }

    /// <summary>The recording: two human seats (Teblor, Malazan) on the generated map, seed 1; the Teblor train a Gatherer at tick 2; 400 ticks.</summary>
    private static Replay MakeRecording()
    {
        GameData d = TestSim.Data;
        PlayerSeat[] seats =
        {
            new(d.FindFaction("teblor"), SeatController.Human, AiDifficulty.Normal),
            new(d.FindFaction("malazan"), SeatController.Human, AiDifficulty.Normal),
        };
        Simulation sim = AiScenes.Generated(seats, 1, true, out ReplayRecorder? rec);
        int hall = -1, longhouse = d.FindBuilding("teblor_longhouse");
        var bs = sim.World.Buildings;
        while (hall < 0 && sim.TickNumber < 20)
        {
            sim.Tick();
            for (int s = 0; s < bs.Capacity && hall < 0; s++)
                if (bs.Alive[s] && bs.Owner[s] == 0 && bs.TypeId[s] == longhouse) hall = s;
        }
        Assert.True(hall >= 0, "no Teblor Longhouse by tick 20");
        BuildingDef def = d.Buildings[bs.TypeId[hall]];
        int anchor = bs.Cell[hall], width = sim.World.NavGrid.Width;
        var centre = new System.Numerics.Vector2((anchor % width + def.FootprintWidth / 2f) * Map.MapConstants.CellSize,
            (anchor / width + def.FootprintHeight / 2f) * Map.MapConstants.CellSize);
        sim.Enqueue(Command.Train(0, centre, d.FindUnit("teblor_gatherer")));
        while (sim.TickNumber < 400) sim.Tick();
        return rec!.ToReplay();
    }

    /// <summary>Writes the two fixtures when <c>D32_WRITE_PRE_FIXTURES=1</c>, on the diff base's data only; otherwise does nothing.</summary>
    [Fact]
    public void WriteThePreD32Fixtures_WhenAsked()
    {
        if (Environment.GetEnvironmentVariable(WriteVariable) != "1") return;
        Assert.True(TestSim.Data.ContentHash() == PreHash, $"the fixtures are written on the diff base's data ({PreHash:X16}), not {TestSim.Data.ContentHash():X16}");
        Directory.CreateDirectory(Path.GetDirectoryName(Fixture("x"))!);
        File.WriteAllText(Fixture("save_teblor_ai.b64"), Convert.ToBase64String(MakeSave()));
        ReplayFormat.WriteFile(MakeRecording(), Fixture("replay_teblor_train.replay"));
        output.WriteLine($"wrote {Fixture("save_teblor_ai.b64")} and {Fixture("replay_teblor_train.replay")}");
    }

    /// <summary>The save made before D32 carries the diff base's hash and is refused DataMismatch, no sim back; the player reads <c>save.data_mismatch</c>.</summary>
    [Fact]
    public void ASaveMadeBeforeD32_IsRefusedForItsDataHash_WithTheUiText()
    {
        byte[] bytes = Convert.FromBase64String(File.ReadAllText(Fixture("save_teblor_ai.b64")));
        ulong saved = BitConverter.ToUInt64(bytes, SaveBytes.HeaderOffsets(bytes).DataHash);
        Assert.Equal(PreHash, saved);
        Assert.NotEqual(saved, TestSim.Data.ContentHash());
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(TestSim.Data, bytes));
        string text = UiText("save", "data_mismatch");
        output.WriteLine($"save refused: {SaveError.DataMismatch} -> \"{text}\"");
        Assert.Contains("different game data", text, StringComparison.Ordinal);
    }

    /// <summary>The recording made before D32 reads, then is refused before tick 1 (DataMismatch); the player reads <c>load.refused</c>.</summary>
    [Fact]
    public void ARecordingMadeBeforeD32_IsRefusedBeforeTick1_WithTheUiText()
    {
        Assert.Equal(ReplayError.None, ReplayFormat.TryReadFile(Fixture("replay_teblor_train.replay"), out Replay? replay));
        Assert.Equal(PreHash, replay!.DataHash);
        ReplayResult result = ReplayPlayer.Run(replay, TestSim.Data);
        Assert.Equal(ReplayError.DataMismatch, result.Error);
        Assert.Equal(0, result.TicksRun);
        string text = UiText("load", "refused");
        output.WriteLine($"replay refused: {result.Error} -> \"{text}\"");
        Assert.Contains("different game data", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mutant: the same fixtures on a copy of the data whose hash is the diff base's (the shipped files with D32's two
    /// <c>ai.json</c> changes put back, <see cref="D32TeblorOpeningReportTests.D31"/>'s opening and description) load: the
    /// refusal is the hash move, not a broken file.
    /// </summary>
    [Fact]
    public void OnTheDiffBasesData_BothFixturesLoad()
    {
        // M6-S10 (sim track, handed this row for the session; rule 58): the diff base's data had no gatherOverlap, so the
        // copy takes the shipped 0.5 back out too (as BUG-1400's fix took the landing's rows out of its "off" copy).
        GameData before = AiScenes.DataWith(dir =>
        {
            dir.EditJson("factions/teblor/ai.json", root => root["openings"]![0] = D32TeblorOpeningReportTests.D31());
            dir.EditJson("common/rules.json", r => r.Remove("gatherOverlap"));
        });
        Assert.Equal(PreHash, before.ContentHash());
        byte[] bytes = Convert.FromBase64String(File.ReadAllText(Fixture("save_teblor_ai.b64")));
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(before, bytes));
        Assert.Equal(ReplayError.None, ReplayFormat.TryReadFile(Fixture("replay_teblor_train.replay"), out Replay? replay));
        ReplayResult result = ReplayPlayer.Run(replay!, before);
        Assert.True(result.Ok, $"{result.Error} at {result.Tick}");
    }
}
