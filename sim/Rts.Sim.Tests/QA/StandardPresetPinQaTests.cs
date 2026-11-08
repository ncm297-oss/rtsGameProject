using Rts.Sim.Commands;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M6-S6 (2026-11-05-1206): the <c>standard</c> starting resources play bit for bit as the build before presets. Every
/// save scene (3,000 ticks) and the six AI save rows plus the 2v2 (12,000 ticks) run at the default preset; the digest
/// (FNV-1a over the 64-bit words) of the state hash after every tick is pinned to what the same scenes gave on 5368cfe,
/// the commit before M6-S6 (measured there with this file, which compiles on both). A digest that moves means a match at
/// <c>standard</c> plays differently from the build before presets.
/// </summary>
public class StandardPresetPinQaTests
{
    private readonly ITestOutputHelper _out;

    public StandardPresetPinQaTests(ITestOutputHelper output) => _out = output;

    internal static ulong Digest(SaveScene scene, int ticks, out int distinct)
    {
        Simulation sim = scene.Build();
        var commands = new List<Command>();
        var seen = new HashSet<ulong>();
        ulong digest = 14695981039346656037UL;
        for (int t = 0; t < ticks; t++)
        {
            commands.Clear();
            scene.Script(sim, commands);
            foreach (Command c in commands) sim.Enqueue(c);
            sim.Tick();
            ulong h = sim.StateHash();
            seen.Add(h);
            digest = (digest ^ h) * 1099511628211UL;
        }
        distinct = seen.Count;
        return digest;
    }

    public static IEnumerable<object[]> SaveSceneRows() => new[]
    {
        new object[] { "cross_map_seed1", 0x45F973087C0BB1D1UL },
        new object[] { "menu_seed1", 0xB400016A35D7EAE4UL },
        new object[] { "menu_seed2", 0x2D3366B692C0C11FUL },
        new object[] { "menu_seed3", 0x752D3D910C14CAD0UL },
        new object[] { "menu_seed4", 0xF0BD10D5898E482EUL },
        new object[] { "menu_seed5", 0x8C33CBEB0F2A0DBEUL },
        new object[] { "four_seats_256", 0x2FAB4CEE371B9051UL },
        new object[] { "surround_brawl", 0xEAC1911851154EACUL },
        new object[] { "casts_and_sandstorm", 0xEB8381CBBA4DFAC2UL },
        new object[] { "stealth_beside_detector", 0x1C2D174FA705C278UL },
        new object[] { "construction_and_queue", 0x5808EE95CC66BA62UL },
        new object[] { "felled_tree_and_razed_building", 0x4D9635AD3817048CUL },
        new object[] { "defeated_seat", 0x7D61B9F483FC4098UL },
    };

    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(SaveSceneRows))]
    public void EverySaveScene_AtStandard_PlaysAs5368cfe(string name, ulong pinned)
    {
        SaveScene scene = SaveScenes.Named(name);
        ulong digest = Digest(scene, 3000, out int distinct);
        _out.WriteLine(FormattableString.Invariant($"PIN {name}: 0x{digest:X16} ({distinct} distinct)"));
        Assert.Equal(pinned, digest);
    }

    public static IEnumerable<object[]> AiRows() => new[]
    {
        new object[] { 1, 2, 0, false, 0xEF1691714BF6A76DUL },
        new object[] { 2, 2, 1, false, 0x08DAC14990BB29C5UL }, // M5-4c-2 fix round 2 (BUG-1123: the pulled-back rejoin): was 0xA796F8D89925CDD1 (fix round 1, BUG-1121 focus rule; before it 0x070CC85BB2A3B390 (M5-4c slice 2; before it 0x244EB8E19CE2992F)); seat 1 is Hard (gather bonus, focus fire, pull-back)
        new object[] { 3, 3, 2, false, 0xFCD8F85C768FCED4UL }, // M5-4c-2 fix round 1: was 0xFB6DD578FA7A165E (M5-4c slice 2; 0x0FDF27B5B49AE41A; M5-4d slice 2: 0x112526C6B3861467); seat 0 is Hard
        new object[] { 4, 4, 1, false, 0xFB6A7E60CAB35D32UL }, // M5-4c-2 fix round 1: was 0xE204EB9DE6D0FBF9 (M5-4c slice 2; 0xC01AABF0FFE171A9); seat 1 is Hard
        new object[] { 5, 2, 2, false, 0x12C3558D5A1F3C6CUL }, // M5-4c-2 fix round 2: was 0x81B9C98852D87A97 (fix round 1; before it 0xAD97F301BBCD12EE (M5-4c slice 2; 0x017E948DC0620D8B)); seat 0 is Hard
        new object[] { 3, 3, 0, true, 0x3257C07089F858CAUL }, // M5-4c slice 2: was 0x44BFA38FE0466396 (M5-4d slice 2: 0xCFA79548E71F2987); seat 2 is Hard
        new object[] { 1, 4, -1, false, 0x0D5D208D3FE10FD7UL }, // the 2v2 (human + three AIs)
    };

    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(AiRows))]
    public void EveryAiScene_AtStandard_12000Ticks_PlaysAs5368cfe(int seed, int players, int difficulty, bool cramped, ulong pinned)
    {
        SaveScene scene = difficulty < 0 ? AiSaveScenes.TwoVersusTwo((ulong)seed)
            : AiSaveScenes.AiMatch((ulong)seed, players, (Data.AiDifficulty)difficulty, cramped);
        ulong digest = Digest(scene, AiSaveScenes.ProbeWindow, out int distinct);
        _out.WriteLine(FormattableString.Invariant($"PIN {scene.Name}: 0x{digest:X16} ({distinct} distinct)"));
        Assert.True(distinct > AiSaveScenes.ProbeWindow / 2, "the scene stood still");
        Assert.Equal(pinned, digest);
    }
}
