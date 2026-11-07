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

    /// <summary>
    /// The base build's state hashes at ticks 500, 1,000 ... 6,000 for each row of <see cref="Rows"/>, in order. M5-4c slice 2:
    /// re-printed on that slice's tree (the line each row writes), since every row has a Hard seat (the difficulties turn per
    /// seat) and Hard now gathers faster, focuses fire and pulls back the wounded; the rows still prove a shift of 0 with no
    /// reservable step sends no reserve (<c>reserved</c> 0).
    /// </summary>
    private static readonly Dictionary<(int, int, bool), ulong[]> s_base = new()
    {
        [(2, 2, true)] = new ulong[]
        {
            0xBE3B530A28191C48, 0x10585E07BC597613, 0x2949F935A2594956, 0x99DB931F2548B72C,
            0xEA095D860E94B34C, 0xB5A9555E5899A066, 0x6263D41ED23D50E7, 0x4B8F04F47F121146,
            0xA4B92439F3770390, 0xE0532168DE033983, 0xC45D43387F462485, 0x313A21B99151BC3A,
        },
        [(4, 4, true)] = new ulong[]
        {
            0xF670CF2993E9036E, 0x524B13E5CDF7BA75, 0x689C17F492F0D752, 0x532A94678967835D,
            0x7C8C60851A3F3B2C, 0xEC78522DA1EA4236, 0x0354667396BF76E0, 0x28941A0F85F71993,
            0xC1BFFCE07E849657, 0xF1A3ECAA15B97428, 0xCD83B5274B9AE32A, 0x09DB2E176D0038C8,
        },
        [(3, 3, false)] = new ulong[]
        {
            0x55586CA407525F44, 0xCD6A2C9C6B223EC8, 0x63D5A1F15EA3EDAB, 0xF77AADAE0E8ED54E,
            0x62AD7BDD5751634C, 0x59124CF270A054DA, 0x1C2D49EBEA235757, 0xC12695EC095397FF,
            0x411F5C494FB67035, 0x5A040E4BA74DB9F0, 0x968955449FE785D6, 0x19DA0DEB6DA27111,
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
