using Rts.Sim.Commands;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M7-3b (2026-11-10-2116): every <see cref="StandardPresetPinQaTests"/> scene (the save scenes, 3,000 ticks; the AI rows
/// and the 2v2, 12,000 ticks), digested through <see cref="OutcomeFingerprint"/> (id-free: type, tech and ability keys, not
/// ids) after every tick. Pinned to what this file measured on de591f0, the commit before M7-3b's renumbering: the
/// state-hash digests moved with the ids (re-pinned), these must not (a moved one is a change in play, not a re-pin).
/// Being id-free, these pins also survive later renumberings (a third faction, an order change).
/// </summary>
[Trait("Category", "Stress")]
public class OutcomePinM73bQaTests
{
    private readonly ITestOutputHelper _out;

    public OutcomePinM73bQaTests(ITestOutputHelper output) => _out = output;

    private static ulong Digest(SaveScene scene, int ticks)
    {
        Simulation sim = scene.Build();
        var commands = new List<Command>();
        ulong digest = 14695981039346656037UL;
        for (int t = 0; t < ticks; t++)
        {
            commands.Clear();
            scene.Script(sim, commands);
            foreach (Command c in commands) sim.Enqueue(c);
            sim.Tick();
            digest = (digest ^ OutcomeFingerprint.Of(sim)) * 1099511628211UL;
        }
        return digest;
    }

    // Measured on de591f0 with this file (QA 2026-11-10-2116).
    public static IEnumerable<object[]> SaveSceneRows() => new[]
    {
        new object[] { "cross_map_seed1", 0x6ED4F61FF3AA7469UL },
        new object[] { "menu_seed1", 0xE10CDD07E76DB498UL },
        new object[] { "menu_seed2", 0x90742BD955B688D7UL },
        new object[] { "menu_seed3", 0xDDA471AA2766E3F4UL },
        new object[] { "menu_seed4", 0xB5F21EC4E49855B7UL },
        new object[] { "menu_seed5", 0xCB176AF9382DA01EUL },
        new object[] { "four_seats_256", 0xC4FB043C767B5038UL },
        new object[] { "surround_brawl", 0xA25004256FCD0478UL },
        new object[] { "casts_and_sandstorm", 0x05250A1D7668834DUL },
        new object[] { "stealth_beside_detector", 0xE8962E4E6CAED648UL },
        new object[] { "construction_and_queue", 0x1729AB826BDBBF5AUL },
        new object[] { "felled_tree_and_razed_building", 0x1100C13755AA7815UL },
        new object[] { "defeated_seat", 0x708FB147C227CA97UL },
    };

    [Theory]
    [MemberData(nameof(SaveSceneRows))]
    public void EverySaveScene_PlaysTheSameOutcomeAsDe591f0(string name, ulong pinned)
    {
        ulong digest = Digest(SaveScenes.Named(name), 3000);
        _out.WriteLine(FormattableString.Invariant($"OFP {name}: 0x{digest:X16}"));
        Assert.Equal(pinned, digest);
    }

    // Measured on de591f0 with this file (QA 2026-11-10-2116). Rows as StandardPresetPinQaTests.AiRows.
    public static IEnumerable<object[]> AiRows() => new[]
    {
        new object[] { 1, 2, 0, false, 0x3546ABECA82EA2A3UL },
        new object[] { 2, 2, 1, false, 0x072B988DD9AF8223UL },
        new object[] { 3, 3, 2, false, 0x13A00F83DC05E77EUL },
        new object[] { 4, 4, 1, false, 0x99F91DE86CC05AAFUL },
        new object[] { 5, 2, 2, false, 0xE8521B011327420AUL },
        new object[] { 3, 3, 0, true, 0xC9899EC24D14CC32UL },
        new object[] { 1, 4, -1, false, 0x8E6B162FAC2C8B07UL },
    };

    [Theory]
    [MemberData(nameof(AiRows))]
    public void EveryAiScene_12000Ticks_PlaysTheSameOutcomeAsDe591f0(int seed, int players, int difficulty, bool cramped, ulong pinned)
    {
        SaveScene scene = difficulty < 0 ? AiSaveScenes.TwoVersusTwo((ulong)seed)
            : AiSaveScenes.AiMatch((ulong)seed, players, (Data.AiDifficulty)difficulty, cramped);
        ulong digest = Digest(scene, AiSaveScenes.ProbeWindow);
        _out.WriteLine(FormattableString.Invariant($"OFP {scene.Name}: 0x{digest:X16}"));
        Assert.Equal(pinned, digest);
    }
}
