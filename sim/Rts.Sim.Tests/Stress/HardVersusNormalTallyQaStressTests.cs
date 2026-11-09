using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M5-9a criterion 7 (QA focus (f)): M5-4c slice 2's 42 Hard-v-Normal matches (docs/03 "Implementation (M5-4c slice 2)",
/// fix round 2's harness): Raraku, Pale Hills and Vathar Crossing; seeds 1-3 and 5-8; Hard in seat 0 or seat 1; to 72,000
/// ticks on the shipped data, the winner by <see cref="World.IsDefeated"/>. One row per map and Hard seat (7 seeds each), so
/// the tally can be run in parallel processes by filter. Each row prints a <c>TALLY</c> line per match and its sum; the
/// report adds them. Optional knob-0 twin (<c>RTS_QA_TALLY_KNOB0</c> set): <c>ai.starvedWaveAfter</c> 0.
/// </summary>
[Trait("Category", "Stress")]
public class HardVersusNormalTallyQaStressTests
{
    private readonly ITestOutputHelper _out;

    public HardVersusNormalTallyQaStressTests(ITestOutputHelper output) => _out = output;

    private static readonly ulong[] Seeds = { 1, 2, 3, 5, 6, 7, 8 };

    private static readonly Lazy<GameData> KnobOff = new(() => AiScenes.DataWith(dir =>
        dir.EditJson("common/rules.json", r => r["ai"]!["starvedWaveAfter"] = 0)));

    [Theory]
    [InlineData("raraku", 0)]
    [InlineData("raraku", 1)]
    [InlineData("pale_hills", 0)]
    [InlineData("pale_hills", 1)]
    [InlineData("vathar_crossing", 0)]
    [InlineData("vathar_crossing", 1)]
    public void HardVersusNormal_SevenSeeds_Tallied(string map, int hardSeat)
    {
        bool off = Environment.GetEnvironmentVariable("RTS_QA_TALLY_KNOB0") != null;
        GameData data = off ? KnobOff.Value : TestSim.Data;
        int hard = 0, normal = 0, none = 0;
        foreach (ulong seed in Seeds)
        {
            AiDifficulty a = hardSeat == 0 ? AiDifficulty.Hard : AiDifficulty.Normal, b = hardSeat == 0 ? AiDifficulty.Normal : AiDifficulty.Hard;
            int size = data.Maps[data.FindMap(map)].Size;
            PlayerSeat[] seats = { new(0, SeatController.Ai, a), new(1, SeatController.Ai, b) };
            Simulation sim = map == "raraku"
                ? AiScenes.Generated(seats, seed, false, out _, data: data, configure: c => c with { MapId = map })
                : AiScenes.Generated(seats, seed, false, out _, data: data, configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
            while (sim.TickNumber < 72_000 && !sim.MatchOver) sim.Tick();
            string result;
            if (!sim.MatchOver) { none++; result = "none"; }
            else if (sim.World.IsDefeated(1 - hardSeat)) { hard++; result = "hard"; }
            else { normal++; result = "normal"; }
            _out.WriteLine($"TALLY {(off ? "knob0" : "shipped")} {map} seed {seed} hard seat {hardSeat}: {result} at {sim.TickNumber}");
        }
        _out.WriteLine($"TALLY-SUM {(off ? "knob0" : "shipped")} {map} hard seat {hardSeat}: {hard} / {normal} / {none}");
    }
}
