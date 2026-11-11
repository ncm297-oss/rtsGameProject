using Rts.Sim.Commands;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-4c-2 (2026-11-07-0926), criterion 6 "nothing but Hard moved": Normal v Normal and Easy v Normal whole matches on
/// Raraku seed 1, Pale Hills seeds 1 and 3 and Vathar Crossing seed 1, 36,000 ticks (or to the end). Each row folds the
/// state hash after every tick and every command either AI seat had accepted (kind, player, apply tick, sequence, type,
/// position bits, unit, target, flags) into one FNV-1a digest, pinned to what the same file printed on 6f6414b, the commit
/// before the Hard knobs (the file compiles on both). A digest that moves means Easy or Normal play differently.
/// M5-9a (2026-11-08): every row has a Normal seat, whose counter shift moves its digest (Raraku seed 1 Normal v Normal is
/// the one row the shift leaves unchanged); re-pinned with the old digest in each row's comment. With Normal's
/// <c>counterStrength</c> at 0 every row still gives its old digest (<c>CounterCompositionTests.TheNonHardPins_WithNormalStrengthZero_PlayAsBeforeThisTask</c>):
/// Easy itself plays as before. M5-9b (2026-11-09): the far expansion runs at every difficulty, so the four Pale Hills rows
/// move (both seats run out of gold there); the 8 pre-M5-9a digests are reproduced with Normal's strength, the starved wave
/// and the far expansion all at 0 (the same <c>CounterCompositionTests</c> row).
/// With <c>RTS_QA_DUMP</c> set to a folder the row writes its per-tick hashes and command lines there, to diff two trees.
/// </summary>
[Trait("Category", "Stress")]
public class NonHardMatchPinQaTests
{
    private readonly ITestOutputHelper _out;

    public NonHardMatchPinQaTests(ITestOutputHelper output) => _out = output;

    private const int Ticks = 36_000;

    [Theory]
    [InlineData("raraku", 1UL, AiDifficulty.Normal, "5A6F64C1AF757499 28231 1503")] // M5-H6 (the house rule, the hunt's free starts, the leg fixes): was E0612240C3E713D2 28083 1476
    [InlineData("raraku", 1UL, AiDifficulty.Easy, "CFE55368B0CBD15D 24498 1445")] // M5-9a: was 0D877B9258F20265 24498 1448
    [InlineData("pale_hills", 1UL, AiDifficulty.Normal, "7A4B426913372A53 36000 3109")] // M5-9a: was 69C4358E67DF7FC3 34219 3144; round 1 D714943B782A74E0 36000 2599 (fix round 1, BUG-1154: seat 0 now starved too) 70635F3A0D539227 36000 2598; M5-9b: both seats expand to a free start (ends 48,858)
    [InlineData("pale_hills", 1UL, AiDifficulty.Easy, "7BEB9DD74B7C779F 36000 2850")] // M5-9a: was 62F16C733AF6D9C8 36000 2601, then 9B5F2C36AE77DD3A 36000 2689; M5-9b: the far expansion (every difficulty)
    [InlineData("pale_hills", 3UL, AiDifficulty.Normal, "9271EBE2FC074DC3 33484 3354")] // M5-H6: was 1D84903554D236D3 29514 2366; M5-9a: was C9CB142CF3C97C88 29562 3103, then 49143BF840792D0F 31891 2447; M5-9b: the far expansion
    [InlineData("pale_hills", 3UL, AiDifficulty.Easy, "0819960BDEBD1EF9 36000 2811")] // M5-H6: was 08CE3315062BC646 36000 2811; M5-9a: was 4FF13AE84024D0BB 33524 3075; round 1 63DE1922B755D390 36000 2708, then 8FBFA3820C590F5A 36000 2606; M5-9b: the far expansion
    [InlineData("vathar_crossing", 1UL, AiDifficulty.Normal, "2D4804D993CBC56A 23322 1543")] // M5-H6: was 66A1290256133869 23043 1386; M5-9a: was 4FA98DA390936D9B 22794 1395
    [InlineData("vathar_crossing", 1UL, AiDifficulty.Easy, "5DD4CB05A846BBF1 27344 1790")] // M5-9a: was 325B1E0137F84D8D 36000 1798
    public void AMatchWithoutHard_PlaysAsBeforeTheHardKnobs(string map, ulong seed, AiDifficulty first, string expected)
    {
        string got = Run(map, seed, first, null, out bool over);
        _out.WriteLine($"{map} seed {seed} {first} v Normal: {got}; match over {over}");
        Assert.Equal(expected, got);
    }

    /// <summary>
    /// One row's "digest ticks commands" on <paramref name="data"/> (the shipped data when null). M7-3: split out of the row
    /// so <c>Data/FactionOrderTests</c> plays the same rows on a copy with a third faction appended to the order.
    /// </summary>
    internal static string Run(string map, ulong seed, AiDifficulty first, GameData? data, out bool matchOver)
    {
        Simulation sim = MapFileTests.MapMatch(map, seed, false, out _, first, AiDifficulty.Normal, data);
        var log = new List<Command>();
        for (int p = 0; p < 2; p++) sim.AiOf(p)!.SentForTests = c => log.Add(c);
        string? dump = Environment.GetEnvironmentVariable("RTS_QA_DUMP");
        StreamWriter? w = dump == null ? null : new StreamWriter(Path.Combine(dump, $"{map}-{seed}-{first}.txt"));
        ulong digest = 14695981039346656037UL;
        int logged = 0;
        while (sim.TickNumber < Ticks && !sim.MatchOver)
        {
            sim.Tick();
            ulong h = sim.StateHash();
            digest = Fold(digest, h);
            w?.WriteLine($"T {sim.TickNumber} {h:X16}");
            for (; logged < log.Count; logged++)
            {
                Command c = log[logged];
                ulong[] words =
                {
                    (ulong)c.Kind, (ulong)(uint)c.Player, (ulong)(uint)c.Tick, (ulong)(uint)c.Sequence, (ulong)(uint)c.TypeId,
                    (ulong)(uint)BitConverter.SingleToInt32Bits(c.Position.X), (ulong)(uint)BitConverter.SingleToInt32Bits(c.Position.Y),
                    (ulong)(uint)c.Unit.Index, (ulong)(uint)c.Unit.Generation, (ulong)(uint)c.Target.Index, (ulong)(uint)c.Target.Generation,
                    c.TargetIsBuilding ? 1UL : 0UL, (ulong)(uint)c.Flags,
                };
                foreach (ulong word in words) digest = Fold(digest, word);
                w?.WriteLine($"C {c.Tick} p{c.Player} {c.Kind} u{c.Unit.Index}.{c.Unit.Generation} t{c.Target.Index}.{c.Target.Generation} b{c.TargetIsBuilding} ({c.Position.X:R},{c.Position.Y:R}) ty{c.TypeId} f{c.Flags}");
            }
        }
        w?.Dispose();
        matchOver = sim.MatchOver;
        return $"{digest:X16} {sim.TickNumber} {log.Count}";
    }

    private static ulong Fold(ulong digest, ulong word)
    {
        for (int b = 0; b < 8; b++)
        {
            digest ^= (word >> (8 * b)) & 0xFF;
            digest *= 1099511628211UL;
        }
        return digest;
    }
}
