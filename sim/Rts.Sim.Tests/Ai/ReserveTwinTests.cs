using System.Collections.Immutable;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// BUG-1023 item 4: M5-4g criterion 2's twin, as the Producer restated it. With <c>ai.reserveShift</c> 0 and a scene with
/// no reservable step (every step affordable when due, or none due), the M5-4g tree is hash-equal to the base build
/// (f531dd9, the tree before M5-4g): the reserve's walk, its gate and the gather share change nothing when they name no
/// reserve. The scenes are generated matches of AI seats (as <c>AiSaveScenes.AiMatch</c>, bases and a small army placed by
/// commands), each <c>reserveArmyShare</c> 0 so the gate is open and the executor judges every step at every think.
/// The hashes were printed by this file built against f531dd9 (<c>QA_BASE</c> defined: the rules' reserve fields don't
/// exist there) and pinned here.
/// </summary>
public class ReserveTwinTests
{
    private const int Ticks = 6000, Every = 500;
    private readonly ITestOutputHelper _out;

    public ReserveTwinTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
    {
#if !QA_BASE
        dir.EditJson("common/rules.json", r =>
        {
            r["ai"]!["reserveShift"] = 0;
            r["ai"]!["reserveArmyShare"] = 0;
        });
#endif
    }));

    /// <summary>
    /// Row (seed, seats, difficulty of seat 0, rich): rich tops every bank up to 20,000 / 20,000 before every tick, so each
    /// step is affordable the think it is due; not rich leaves the economy alone and every opening's steps not due (their
    /// triggers past the run), so production, the houses, defense and waves run on their own.
    /// </summary>
    public static TheoryData<int, int, AiDifficulty, bool> Rows() => new()
    {
        { 2, 2, AiDifficulty.Normal, true },
        { 4, 4, AiDifficulty.Hard, true },
        { 3, 3, AiDifficulty.Easy, false },
    };

    /// <summary>The base build's state hashes at ticks 500, 1,000 ... 6,000 for each row of <see cref="Rows"/>, in order.</summary>
    private static readonly Dictionary<(int, int, bool), ulong[]> s_base = new()
    {
        [(2, 2, true)] = new ulong[]
        {
            0xDB9FE45AA23D1B1C, 0x345A18C0C70ADE1D, 0xD531FF8881401A07, 0xE13FF0FA6C188E22,
            0x4FA31AF5D1450E45, 0x00C76B8D7E78AFEE, 0x08E3905883E1EABD, 0x5C8699EBA8C67CE5,
            0xB4D155B4E0D61BAE, 0xAF441C986F9CC223, 0x064106DFB44627D7, 0xC1EC306446263CE7,
        },
        [(4, 4, true)] = new ulong[]
        {
            0xA9695A151B504CFF, 0xDFBAF2893E94337C, 0x930F127943870B5C, 0x2FFB87E6C189E083,
            0x11C745D6C5719B33, 0xE30CB99709F51D72, 0xF6ACD0B704E66725, 0xAF9F8F8D975DDE76,
            0x3187FE90ABAB5B61, 0xABD6FFC208A20560, 0x1542C3FFDF57DB93, 0x42E8FF6BE27539FD,
        },
        [(3, 3, false)] = new ulong[]
        {
            0x49F09C36EC8E153B, 0x14EAE0C0CB3E25B4, 0x46C0F7D20E102E4F, 0xED970FF19BB6F12E,
            0x258FA3B720BF3687, 0xDB634ABBE24863B7, 0x4EAE080632A05FE9, 0x2B79FA25E3E53C98,
            0x8D04BA611A891596, 0x6735FC988446A2DE, 0x968540B4C7A973E4, 0x85C36B08F1A3E2B2,
        },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void AShiftOfZero_WithNoReservableStep_IsHashEqualToTheBaseBuild(int seed, int players, AiDifficulty difficulty, bool rich)
    {
        GameData data = rich ? s_data.Value : s_notDue.Value;
        var seats = Enumerable.Range(0, players).Select(p => new PlayerSeat(p % 2, SeatController.Ai, (AiDifficulty)(((int)difficulty + p) % 3))).ToArray();
        var map = MapGenParams.Default with { Width = 128, Height = 128, Forests = 12, GoldMines = 8, StartLocations = players };
        var sim = new Simulation(new SimConfig((ulong)seed, players, 2000, 4096) { Data = data, Map = map, Seats = seats.ToImmutableArray() });
        SaveScenes.PlaceBases(sim, army: true);
        int reserved = 0;
#if !QA_BASE
        for (int p = 0; p < players; p++) sim.AiOf(p)!.ThoughtForTests = a => { if (a.ReserveStep >= 0) reserved++; };
#endif
        var hashes = new List<ulong>();
        for (int t = 0; t < Ticks; t++)
        {
            if (rich)
                for (int p = 0; p < players; p++) BuildMaps.SetTotals(sim, p, 20_000, 20_000);
            sim.Tick();
            if (sim.TickNumber % Every == 0) hashes.Add(sim.StateHash());
        }
        _out.WriteLine($"[({seed}, {players}, {(rich ? "true" : "false")})] = new ulong[] {{ {string.Join(", ", hashes.Select(h => $"0x{h:X16}"))} }},");
        Assert.Equal(0, reserved);
        Assert.Equal(s_base[(seed, players, rich)], hashes.ToArray());
    }

    /// <summary>Every opening's steps moved past the run (step i at 3,600 - i s: the loader's maximum, and no two steps alike): none is ever due.</summary>
    private static readonly Lazy<GameData> s_notDue = new(() => AiScenes.DataWith(dir =>
    {
        foreach (string f in new[] { "malazan", "whirlwind" })
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                foreach (System.Text.Json.Nodes.JsonNode? o in root["openings"]!.AsArray())
                {
                    System.Text.Json.Nodes.JsonArray steps = o!["buildOrder"]!.AsArray();
                    for (int i = 0; i < steps.Count; i++) steps[i]!["at"] = new System.Text.Json.Nodes.JsonObject { ["time"] = 3600 - i };
                }
            });
#if !QA_BASE
        dir.EditJson("common/rules.json", r =>
        {
            r["ai"]!["reserveShift"] = 0;
            r["ai"]!["reserveArmyShare"] = 0;
        });
#endif
    }));
}
