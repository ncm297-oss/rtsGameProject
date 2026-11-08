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
/// Easy itself plays as before.
/// With <c>RTS_QA_DUMP</c> set to a folder the row writes its per-tick hashes and command lines there, to diff two trees.
/// </summary>
[Trait("Category", "Stress")]
public class NonHardMatchPinQaTests
{
    private readonly ITestOutputHelper _out;

    public NonHardMatchPinQaTests(ITestOutputHelper output) => _out = output;

    private const int Ticks = 36_000;

    [Theory]
    [InlineData("raraku", 1UL, AiDifficulty.Normal, "E0612240C3E713D2 28083 1476")]
    [InlineData("raraku", 1UL, AiDifficulty.Easy, "CFE55368B0CBD15D 24498 1445")] // M5-9a: was 0D877B9258F20265 24498 1448
    [InlineData("pale_hills", 1UL, AiDifficulty.Normal, "D714943B782A74E0 36000 2599")] // M5-9a: was 69C4358E67DF7FC3 34219 3144
    [InlineData("pale_hills", 1UL, AiDifficulty.Easy, "9B5F2C36AE77DD3A 36000 2689")] // M5-9a: was 62F16C733AF6D9C8 36000 2601
    [InlineData("pale_hills", 3UL, AiDifficulty.Normal, "49143BF840792D0F 31891 2447")] // M5-9a: was C9CB142CF3C97C88 29562 3103
    [InlineData("pale_hills", 3UL, AiDifficulty.Easy, "63DE1922B755D390 36000 2708")] // M5-9a: was 4FF13AE84024D0BB 33524 3075
    [InlineData("vathar_crossing", 1UL, AiDifficulty.Normal, "66A1290256133869 23043 1386")] // M5-9a: was 4FA98DA390936D9B 22794 1395
    [InlineData("vathar_crossing", 1UL, AiDifficulty.Easy, "5DD4CB05A846BBF1 27344 1790")] // M5-9a: was 325B1E0137F84D8D 36000 1798
    public void AMatchWithoutHard_PlaysAsBeforeTheHardKnobs(string map, ulong seed, AiDifficulty first, string expected)
    {
        Simulation sim = MapFileTests.MapMatch(map, seed, false, out _, first, AiDifficulty.Normal);
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
        string got = $"{digest:X16} {sim.TickNumber} {log.Count}";
        _out.WriteLine($"{map} seed {seed} {first} v Normal: {got}; match over {sim.MatchOver}");
        Assert.Equal(expected, got);
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
