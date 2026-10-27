using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S3: the round-trip scenes with AI seats (criterion 1) and the probes that find their moments. Every scene is a
/// generated map with the bases placed by commands as <c>Match.cs</c> places them, on the shipped data (one row on
/// <see cref="CrampedData"/>); the AI seats send their own commands, so an AI-only scene's script sends nothing. The probes
/// read the seats' Blackboards between ticks (rule 37: the moments are found, never pinned to a tick of the shipped
/// <c>ai.json</c>).
/// </summary>
internal static class AiSaveScenes
{
    /// <summary>The fixed save ticks of an AI scene besides the first think after the opening one: 1, 137 and 1,000.</summary>
    internal static readonly int[] FixedTicks = { 1, 137, 1000 };

    /// <summary>How far a scene runs looking for its probed moments.</summary>
    internal const int ProbeWindow = 12_000;

    /// <summary>The probes' names, in <see cref="Probes"/>' order.</summary>
    internal static readonly string[] ProbeNames = { "mid-wave", "mid-cast", "no-site wait", "during a defense" };

    /// <summary>A wave in progress, a ground cast remembered, a build step waiting out a no-site search, a threat held: at any AI seat.</summary>
    internal static Func<Simulation, bool>[] Probes() => new Func<Simulation, bool>[]
    {
        sim => AnySeat(sim, ai => ai.Board.WaveActive),
        sim => AnySeat(sim, ai => ai.Board.RecentCastCount > 0),
        sim => AnySeat(sim, ai => ai.Board.StepNoSiteUntil.Any(until => until > sim.TickNumber)),
        sim => AnySeat(sim, ai => ai.Board.Defending),
    };

    private static bool AnySeat(Simulation sim, Func<AiPlayer, bool> test)
    {
        for (int p = 0; p < sim.World.Config.PlayerCount; p++)
            if (sim.AiOf(p) is { } ai && test(ai)) return true;
        return false;
    }

    /// <summary>The first tick after 1 at which some AI seat thinks (its think interval): the load's first tick is a think.</summary>
    internal static int FirstThinkAfterTheOpeningOne(Simulation sim)
    {
        int first = int.MaxValue;
        for (int p = 0; p < sim.World.Config.PlayerCount; p++)
            if (sim.AiOf(p) is { } ai) first = Math.Min(first, ai.ThinkTicks);
        return first;
    }

    /// <summary>
    /// The AI-v-AI rows (seed, seats, first seat's difficulty, cramped): seeds 1-5, 2 to 4 seats, every difficulty (mixed
    /// within a row too), both factions; and seed 3 again on <see cref="CrampedData"/>, where build steps wait out no-site
    /// searches (on the shipped data's 32-cell search radius a generated map never ran out of room in 12,000 ticks).
    /// </summary>
    public static IEnumerable<object[]> Rows()
    {
        yield return new object[] { 1, 2, AiDifficulty.Easy, false };
        yield return new object[] { 2, 2, AiDifficulty.Normal, false };
        yield return new object[] { 3, 3, AiDifficulty.Hard, false };
        yield return new object[] { 4, 4, AiDifficulty.Normal, false };
        yield return new object[] { 5, 2, AiDifficulty.Hard, false };
        yield return new object[] { 3, 3, AiDifficulty.Easy, true };
    }

    /// <summary>The probes a row must reach: a wave, a cast and a defense on the shipped data; a no-site wait, a cast and a defense cramped.</summary>
    internal static int[] ExpectedProbes(bool cramped) => cramped ? new[] { 1, 2, 3 } : new[] { 0, 1, 3 };

    /// <summary>The shipped data with the AI's site search cut to a 4-cell radius (<c>rules.json</c> <c>ai.placementSearchRadius</c>): a base runs out of room within minutes.</summary>
    internal static GameData CrampedData => s_cramped.Value;

    private static readonly Lazy<GameData> s_cramped = new(() =>
        AiScenes.DataWith(dir => dir.EditJson("common/rules.json", r => r["ai"]!["placementSearchRadius"] = 4)));

    /// <summary>
    /// <paramref name="players"/> AI seats on the menu's map (128, 12 forests, 8 mines, one start location a seat), player p
    /// faction p mod 2 at difficulty <paramref name="difficulty"/> + p (mod 3), so every row mixes difficulties; each base
    /// a hall, five workers and a small army (line, ranged and a caster), and two line units of each seat raiding the next
    /// seat's base from the start (so there is a fight to defend against).
    /// </summary>
    internal static SaveScene AiMatch(ulong seed, int players, AiDifficulty difficulty, bool cramped = false) =>
        Match($"ai_seed{seed}_{players}seats_{difficulty}{(cramped ? "_cramped" : "")}", seed, Enumerable.Range(0, players)
            .Select(p => new PlayerSeat(p % 2, SeatController.Ai, (AiDifficulty)(((int)difficulty + p) % 3))).ToArray(), raiders: 2, script: null, cramped ? CrampedData : null);

    /// <summary>The menu's match as the game starts it: a human (scripted by <see cref="MatchScript"/>) against an AI at Normal.</summary>
    internal static SaveScene HumanVsAi(ulong seed)
    {
        var script = new MatchScript(1);
        return Match($"menu_human_vs_ai_seed{seed}", seed,
            new[] { new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal), new PlayerSeat(1, SeatController.Ai, AiDifficulty.Normal) },
            raiders: 0, script: script.Run, data: null);
    }

    /// <summary>
    /// M6-S4: a 2v2 (teams 1, 2, 1, 2): a scripted human (player 0) and three Normal AI seats, two raiders of each seat in the
    /// next (an enemy's) base.
    /// </summary>
    internal static SaveScene TwoVersusTwo(ulong seed)
    {
        var script = new MatchScript(1);
        PlayerSeat[] seats = Enumerable.Range(0, 4)
            .Select(p => new PlayerSeat(p % 2, p == 0 ? SeatController.Human : SeatController.Ai, AiDifficulty.Normal, p % 2 == 0 ? 1 : 2)).ToArray();
        return Match($"team_2v2_human_and_ai_seed{seed}", seed, seats, raiders: 2, script: script.Run, data: null);
    }

    private static SaveScene Match(string name, ulong seed, PlayerSeat[] seats, int raiders, Action<Simulation, List<Command>>? script, GameData? data) =>
        new(name, () =>
        {
            var map = MapGenParams.Default with { Width = 128, Height = 128, Forests = 12, GoldMines = 8, StartLocations = seats.Length };
            var sim = new Simulation(new SimConfig(seed, seats.Length, 2000, 4096) { Data = data ?? TestSim.Data, Map = map, Seats = seats.ToImmutableArray() });
            SaveScenes.PlaceBases(sim, army: true);
            Raid(sim, raiders);
            return sim;
        }, script ?? ((_, _) => { }));

    /// <summary>Queues <paramref name="raiders"/> line units of each seat among the next seat's start workers (the plan's spots, as <c>AiScenes.Generated</c>).</summary>
    private static void Raid(Simulation sim, int raiders)
    {
        if (raiders == 0) return;
        World w = sim.World;
        int players = w.Config.PlayerCount;
        float maxRadius = 0f;
        foreach (UnitDef d in w.Data.Units) maxRadius = Math.Max(maxRadius, d.Radius);
        var blocks = new Vector2[players][];
        var factions = new int[players];
        for (int p = 0; p < players; p++)
        {
            blocks[p] = Array.Empty<Vector2>();
            factions[p] = w.FactionOf(p);
        }
        // Nothing has spawned yet (the bases are queued), so this is the plan PlaceBases used.
        StartBasePlan plan = StartBase.Plan(w.NavGrid, w.Data, w.Buildings, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, factions, blocks, 5, maxRadius);
        for (int p = 0; p < players; p++)
        {
            int line = StartBase.UnitOfSlot(w.Data, factions[p], UnitSlot.Line);
            Vector2[] spots = plan.Workers[(p + 1) % players];
            for (int k = 0; k < raiders; k++) sim.Enqueue(Command.SpawnUnit(p, line, spots[k % spots.Length] + new Vector2(0.4f, 0.3f)));
        }
    }
}
