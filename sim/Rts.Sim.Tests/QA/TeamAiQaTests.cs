using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA for M6-S4: the AI's ability manager with an ally. A Cadre Mage never casts at an ally's group (the AI's "seen enemies"
/// list holds enemies only), and a Sapper doesn't throw its friendly-fire Cusser where an ally's unit stands among the enemies,
/// as it doesn't where its own does. Each has a twin where the same units are an enemy's (or the ally stands clear), and the
/// cast happens, so the scene can cast. Neither rule was caught by any test when reverted (QA mutation run, 2026-10-27-0158).
/// </summary>
public class TeamAiQaTests
{
    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
    {
        foreach (string f in new[] { "malazan", "whirlwind" })
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", f == "malazan" ? "malazan_laborer" : "whirlwind_camp_follower", 1));
                AiScenes.Opening(root)["attack"] = new JsonObject { ["minArmyPop"] = 100, ["minArmyCost"] = 0 };
            });
        dir.EditJson("common/rules.json", r =>
        {
            JsonNode ai = r["ai"]!;
            ai["scoutAfter"] = 3600;
            ai["defendRadius"] = 8;
            ai["castMinTargets"] = 2;
        });
    }));

    /// <summary>Player 0 an AI (Malazan, Normal); player 1 human, its ally (team 1); player 2 human, an enemy (team 0). No money; one think run.</summary>
    private static Simulation Scene()
    {
        PlayerSeat[] seats =
        {
            new(0, SeatController.Ai, AiDifficulty.Normal, 1), new(1, SeatController.Human, AiDifficulty.Normal, 1),
            new(1, SeatController.Human, AiDifficulty.Normal, 0),
        };
        Simulation sim = AiScenes.Flat(seats, s_data.Value, workers: 0);
        BuildMaps.SetTotals(sim, 0, 0, 0);
        NextThink(sim);
        return sim;
    }

    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    private static List<Command> Casts(Simulation sim)
    {
        var list = new List<Command>();
        sim.AiOf(0)!.SentForTests = c => { if (c.Kind == CommandKind.UseAbility) list.Add(c); };
        return list;
    }

    /// <summary><paramref name="n"/> Raiders of <paramref name="owner"/> 1 m apart round <paramref name="at"/>, spotted by player 0 and holding.</summary>
    private static void Group(Simulation sim, int owner, int n, Vector2 at)
    {
        for (int k = 0; k < n; k++)
        {
            EntityHandle h = CombatScenes.Place(sim, owner, sim.World.Data.FindUnit("whirlwind_raider"), at + new Vector2(0f, k - (n - 1) / 2f));
            CombatScenes.Spot(sim, 0, h);
            sim.World.Units.Hold[h.Index] = true;
        }
    }

    [Theory]
    [InlineData(1, false)] // the ally's group: no cast
    [InlineData(2, true)] // the same group, the enemy's: cast
    public void ACadreMage_NeverCastsAtAnAllysGroup(int owner, bool casts)
    {
        Simulation sim = Scene();
        Vector2 rally = sim.AiOf(0)!.Board.Rally;
        CombatScenes.Place(sim, 0, sim.World.Data.FindUnit("malazan_cadre_mage"), rally);
        Group(sim, owner, 3, rally + new Vector2(9f, 0f));
        List<Command> sent = Casts(sim);
        for (int k = 0; k < 3; k++) NextThink(sim);
        Assert.Equal(casts, sent.Count > 0);
    }

    [Theory]
    [InlineData(false, true)] // the ally's unit stands clear: the Cusser is thrown
    [InlineData(true, false)] // the ally's unit stands among the enemies: no Cusser (its friendly fire would hit it)
    public void ASapper_DoesNotThrowTheCusser_WhereAnAllysUnitStandsAmongTheEnemies(bool allyAmong, bool casts)
    {
        Simulation sim = Scene();
        Vector2 rally = sim.AiOf(0)!.Board.Rally;
        CombatScenes.Place(sim, 0, sim.World.Data.FindUnit("malazan_sapper"), rally);
        Vector2 at = rally + new Vector2(4f, 0f);
        Group(sim, 2, 3, at);
        EntityHandle ally = CombatScenes.Place(sim, 1, sim.World.Data.FindUnit("whirlwind_camp_follower"), allyAmong ? at + new Vector2(0.6f, 0f) : rally + new Vector2(-12f, 0f));
        CombatScenes.Spot(sim, 0, ally);
        sim.World.Units.Hold[ally.Index] = true;
        List<Command> sent = Casts(sim);
        for (int k = 0; k < 3; k++) NextThink(sim);
        Assert.Equal(casts, sent.Count > 0);
    }
}
