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
    /// reservable step sends no reserve (<c>reserved</c> 0). M5-4c-2 fix round 1 (BUG-1121's focus rule: a fighter focuses
    /// only about its attack range): the (2, 2) and (4, 4) rows re-printed again on that tree (from tick 2,500 and 3,500).
    /// M5-4c-2 fix round 2 (BUG-1123: a pulled-back unit goes out with the next wave, <c>MilitaryManager.FightsOn</c>): the same
    /// two rows' hashes at 5,500 and 6,000 re-printed (a Hard seat's second wave takes its wounded along).
    /// D27 (BUG-1163): Hard's <c>gatherBonus</c> 0.25 -> 0.4 (every row has a Hard seat), so all three rows re-printed from tick
    /// 500 on that tree; the old hashes are kept as <c>// was</c> lines; <c>reserved</c> is still 0.
    /// </summary>
    private static readonly Dictionary<(int, int, bool), ulong[]> s_base = new()
    {
        [(2, 2, true)] = new ulong[]
        {
            // was 0xBE3B530A28191C48, 0x10585E07BC597613, 0x2949F935A2594956, 0x99DB931F2548B72C,
            // was 0x7DC40E5AAC61ACEA, 0xCCB9E07E292835F2, 0x8AAD56D9E1ECA2F6, 0xBC71FD3D6A280DE8,
            // was 0x669D77D61898240E, 0x9302F3DA6809A2B9, 0xCE9E0ED1E83D2104, 0x4A59B309B2CDE613,
            0x26FF18F72843DBB0, 0x91C1B41B94B7CF83, 0x067BA445EEC1AE2B, 0x7F32D2D4F6C1B13E,
            0x0D54AF364F08EC0D, 0xFA501008F9E6F1FA, 0x6AE7125592986C6A, 0x4A821646625AE028,
            0x676CE72190696DAE, 0x3F25696010C25D33, 0x6C758346E5DB7EA7, 0x0B4982D5A5D0DF42,
        },
        [(4, 4, true)] = new ulong[]
        {
            // was 0xF670CF2993E9036E, 0x524B13E5CDF7BA75, 0x689C17F492F0D752, 0x532A94678967835D,
            // was 0x7C8C60851A3F3B2C, 0xEC78522DA1EA4236, 0x32FC1AD96B32D299, 0x8D0F3AD2E3744008,
            // was 0x6DB5753F06E39B62, 0x873706930D3B0028, 0x1EC56C878FA3FC71, 0xF2F4F3C068C2B868,
            0x27B73080A03A0DA4, 0x525F29C718C0B9C8, 0x70E36371865AEB9F, 0x14CACFF4403B721C,
            0x72A31BA1A8B254EA, 0x0FA73CF3EFD916BF, 0xB845ADFAEB027B65, 0xB632985CBEEFC768,
            0x1BD70BE458C47B23, 0xCEC91F1DACB1A1DC, 0x0657F56FD2353AB1, 0x7C6679776EF70C16,
        },
        [(3, 3, false)] = new ulong[]
        {
            // was 0x55586CA407525F44, 0xCD6A2C9C6B223EC8, 0x63D5A1F15EA3EDAB, 0xF77AADAE0E8ED54E,
            // was 0x62AD7BDD5751634C, 0x59124CF270A054DA, 0x1C2D49EBEA235757, 0xC12695EC095397FF,
            // was 0x411F5C494FB67035, 0x5A040E4BA74DB9F0, 0x968955449FE785D6, 0x19DA0DEB6DA27111,
            0x366C1B5C62D89694, 0x0D304FA7EFF3E825, 0xB4AA625C753A7A0B, 0x4381FD1E210973D8,
            0xC436B18FD8D22A92, 0xF031BCD45A30B412, 0x473A875BEA4E6DB6, 0x31BC1E7BDA486777,
            0x5D98C758C348A206, 0x7E76BE8C2FDB2E08, 0x7E1CA18C3E6DBDC8, 0x3D70038E422D7954,
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
