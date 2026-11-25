using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M7-8, "off is identical", independently of the developer's three matches: four more shipped-data AI matches on other
/// maps, seeds, faction mixes and a High start (the spend-down rule), 30,000 ticks, the state hash every 500 ticks, printed
/// as a fold and a list. The file uses no M7-8 API, so the same file runs on the base tree; equal digests on both say the
/// switches (absent from the shipped data) and the dropped-order list change no match. A measurement: it prints.
/// </summary>
[Trait("Category", "Stress")]
public class M78ShippedDigestQaStressTests(ITestOutputHelper output)
{
    private const int Bound = 30_000, Every = 500;

    [Theory]
    [InlineData("raraku_teblor_v_malazan")]
    [InlineData("generated_ffa4")]
    [InlineData("pale_2v2_teblor")]
    [InlineData("vathar_teblor_high")]
    public void ShippedAiMatch_QaDigests(string which)
    {
        GameData d = TestSim.Data;
        int teblor = d.FindFaction("teblor"), malazan = d.FindFaction("malazan"), whirlwind = d.FindFaction("whirlwind");
        (string map, ulong seed, PlayerSeat[] seats, string start) = which switch
        {
            "raraku_teblor_v_malazan" => ("raraku", 5UL, new[]
            {
                new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Normal), new PlayerSeat(malazan, SeatController.Ai, AiDifficulty.Normal),
            }, StartingResourcesDef.StandardId),
            "generated_ffa4" => ("generated", 7UL, new[]
            {
                new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Hard), new PlayerSeat(whirlwind, SeatController.Ai, AiDifficulty.Normal),
                new PlayerSeat(malazan, SeatController.Ai, AiDifficulty.Easy), new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Normal),
            }, StartingResourcesDef.StandardId),
            "pale_2v2_teblor" => ("pale_hills", 4UL, new[]
            {
                new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Normal, 1), new PlayerSeat(whirlwind, SeatController.Ai, AiDifficulty.Normal, 2),
                new PlayerSeat(malazan, SeatController.Ai, AiDifficulty.Normal, 1), new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Hard, 2),
            }, StartingResourcesDef.StandardId),
            _ => ("vathar_crossing", 3UL, new[]
            {
                new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Hard), new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Easy),
            }, "high"),
        };
        int units = seats.Length > 2 ? 1024 : 400;
        Simulation sim = map == "generated"
            ? AiScenes.Generated(seats, seed, false, out _, units: units, configure: c => c with { StartingResources = start })
            : AiScenes.Generated(seats, seed, false, out _, units: units, configure: c => c with
            {
                MapId = map, StartingResources = start,
                Map = c.Map with { Width = d.Maps[d.FindMap(map)].Size, Height = d.Maps[d.FindMap(map)].Size },
            });
        var hashes = new List<string>();
        var fold = new StateHasher();
        while (sim.TickNumber < Bound)
        {
            sim.Tick();
            if (sim.TickNumber % Every != 0) continue;
            ulong h = sim.StateHash();
            fold.Add(h);
            hashes.Add($"{h:X16}");
        }
        output.WriteLine($"QADIGEST | {which} | {map} seed {seed} start {start} | data {d.ContentHash():X16} | over {sim.MatchOver} at {sim.TickNumber} | fold {fold.Value:X16}");
        output.WriteLine(string.Join(" ", hashes));
        Assert.Equal(Bound / Every, hashes.Count);
    }
}
