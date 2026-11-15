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
/// M6-S10 (the Keep-corner cure and <c>gatherOverlap</c> 0.5): fourteen fingerprints moved, as a behaviour change must (workers
/// stand and walk differently from tick 1, so every gathering scene plays another game); re-pinned once with the old value in
/// each row's comment. This is not a renumbering: the six save scenes without gathering (cross_map_seed1, surround_brawl,
/// casts_and_sandstorm, stealth_beside_detector, construction_and_queue, defeated_seat) kept theirs.
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
        new object[] { "menu_seed1", 0xD1FDD1C1D18920E3UL }, // M6-S10 (the Keep-corner cure, gatherOverlap 0.5: a change in play): was 0xE10CDD07E76DB498
        new object[] { "menu_seed2", 0x3935E91681E15BB9UL }, // M6-S10 (the Keep-corner cure, gatherOverlap 0.5: a change in play): was 0x90742BD955B688D7
        new object[] { "menu_seed3", 0x2EAB57126F6E53CDUL }, // M6-S10 (the Keep-corner cure, gatherOverlap 0.5: a change in play): was 0xDDA471AA2766E3F4
        new object[] { "menu_seed4", 0x9AF4C6E009AC1012UL }, // M6-S10 (the Keep-corner cure, gatherOverlap 0.5: a change in play): was 0xB5F21EC4E49855B7
        new object[] { "menu_seed5", 0x4F2A2FAE270042B3UL }, // M6-S10 (the Keep-corner cure, gatherOverlap 0.5: a change in play): was 0xCB176AF9382DA01E
        new object[] { "four_seats_256", 0xE14C6FD913F5E6B6UL }, // M6-S10 (the Keep-corner cure, gatherOverlap 0.5: a change in play): was 0xC4FB043C767B5038
        new object[] { "surround_brawl", 0xA25004256FCD0478UL },
        new object[] { "casts_and_sandstorm", 0x05250A1D7668834DUL },
        new object[] { "stealth_beside_detector", 0xE8962E4E6CAED648UL },
        new object[] { "construction_and_queue", 0x1729AB826BDBBF5AUL },
        new object[] { "felled_tree_and_razed_building", 0xACB016DD68E0FCF3UL }, // M6-S10 (the Keep-corner cure, gatherOverlap 0.5: a change in play): was 0x1100C13755AA7815
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
        new object[] { 1, 2, 0, false, 0x6068BB609154B21CUL }, // M6-S10: was 0x3546ABECA82EA2A3
        new object[] { 2, 2, 1, false, 0xFC175F677CC212DEUL }, // M6-S10: was 0x072B988DD9AF8223
        new object[] { 3, 3, 2, false, 0xA08252EC916352CFUL }, // M6-S10: was 0x13A00F83DC05E77E
        new object[] { 4, 4, 1, false, 0x28538ED7267EF3C1UL }, // M6-S10: was 0x99F91DE86CC05AAF
        new object[] { 5, 2, 2, false, 0x4245344F2464DFBAUL }, // M6-S10: was 0xE8521B011327420A
        new object[] { 3, 3, 0, true, 0x59957393434AC0A8UL }, // M6-S10: was 0xC9899EC24D14CC32
        new object[] { 1, 4, -1, false, 0x6E3F35D35905EDFDUL }, // M6-S10: was 0x8E6B162FAC2C8B07
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
