using Rts.Sim.Data;
using Rts.Sim.Commands;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S5 criterion 5: shared vision changes nothing in a free-for-all. QA's five A/B scenes of M6-S4 (the menu's human v AI
/// on seeds 1 and 3, four scripted seats on a 256 map, four AI seats on seed 4, the surround brawl), every seat on team 0,
/// run 3,000 ticks; the digest of the 3,000 per-tick state hashes is pinned to what the same scenes gave at e5c4265, the
/// commit before shared vision. A digest that moves means a free-for-all plays differently.
/// </summary>
public class SharedVisionFfaPinTests
{
    private const int Ticks = 3000;

    private readonly ITestOutputHelper _out;

    public SharedVisionFfaPinTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Scenes() => new[]
    {
        new object[] { "menu_human_vs_ai_seed1", 0x37CF8428520DC8E5UL },
        new object[] { "menu_human_vs_ai_seed3", 0xD4CA1E11BBB6585AUL },
        new object[] { "four_seats_256", 0x2FAB4CEE371B9051UL },
        new object[] { "ai_seed4_4seats_Normal", 0x35907C9787F39EEAUL },
        new object[] { "surround_brawl", 0xEAC1911851154EACUL },
    };

    private static SaveScene Scene(string name) => name switch
    {
        "menu_human_vs_ai_seed1" => AiSaveScenes.HumanVsAi(1),
        "menu_human_vs_ai_seed3" => AiSaveScenes.HumanVsAi(3),
        "four_seats_256" => SaveScenes.FourSeats256(),
        "ai_seed4_4seats_Normal" => AiSaveScenes.AiMatch(4, 4, AiDifficulty.Normal),
        "surround_brawl" => SaveScenes.SurroundBrawl(),
        _ => throw new ArgumentException(name),
    };

    /// <summary>The digest (FNV-1a over the 64-bit words) of the scene's state hash after each of its first <see cref="Ticks"/> ticks.</summary>
    internal static ulong Digest(string name, out int distinct)
    {
        SaveScene scene = Scene(name);
        Simulation sim = scene.Build();
        for (int p = 0; p < sim.World.Config.PlayerCount; p++) Assert.Equal(0, sim.World.TeamOf(p));
        var commands = new List<Command>();
        var seen = new HashSet<ulong>();
        ulong digest = 14695981039346656037UL;
        for (int t = 0; t < Ticks; t++)
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

    [Theory]
    [MemberData(nameof(Scenes))]
    public void AFreeForAll_PlaysAsAtE5c4265_EveryTickHash(string name, ulong pinned)
    {
        ulong digest = Digest(name, out int distinct);
        _out.WriteLine(FormattableString.Invariant($"{name}: digest 0x{digest:X16}, {distinct} distinct tick hashes of {Ticks}"));
        Assert.True(distinct > Ticks * 9 / 10, "the scene stood still");
        Assert.Equal(pinned, digest);
    }
}
