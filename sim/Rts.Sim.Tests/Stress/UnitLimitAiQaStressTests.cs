using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M8-4 (Request 56 G4): the kept count of a unit type's <c>maxAlive</c> against a walk of the stores after every tick of
/// real AI matches with combat (deaths in the middle of a tick, packs of units dying together, waves), and the computer
/// player's behaviour with a capped type, on test data only. The copy: Malazan Heavy Infantry (the Line slot, which the
/// start army also spawns four of) limited to 6; every Malazan opening's compositions as written per test; both factions
/// wave early (4 pop, no cost) so the seats fight within the run. Rule 58: every shipped <c>maxAlive</c> is stripped first.
/// </summary>
[Trait("Category", "Stress")]
public class UnitLimitAiQaStressTests(ITestOutputHelper output)
{
    private const int Limit = 6;

    private static GameData Forge(JsonObject mix) => AiScenes.DataWith(dir =>
    {
        MaxAliveLoaderTests.Strip(dir);
        dir.SetUnitField("malazan", MaxAliveLoaderTests.Heavy, "maxAlive", Limit.ToString());
        foreach (string f in TestSim.ShippedFactions)
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                foreach (JsonNode? o in root["openings"]!.AsArray())
                {
                    o!["attack"] = new JsonObject { ["minArmyPop"] = 4, ["minArmyCost"] = 0 };
                    if (f != "malazan") continue;
                    o["compositions"] = new JsonArray(
                        new JsonObject { ["age"] = 1, ["units"] = mix.DeepClone() },
                        new JsonObject { ["age"] = 2, ["units"] = mix.DeepClone() });
                }
            });
    });

    private static readonly Lazy<GameData> s_mixed = new(() => Forge(new JsonObject { ["malazan_heavy_infantry"] = 60, ["malazan_crossbowman"] = 40 }));
    private static readonly Lazy<GameData> s_onlyCapped = new(() => Forge(new JsonObject { ["malazan_heavy_infantry"] = 100 }));

    private static Simulation Match(GameData d, int players, ulong seed, Func<int, AiDifficulty> difficulty)
    {
        var seats = Enumerable.Range(0, players).Select(p => new PlayerSeat(p % 2, SeatController.Ai, difficulty(p))).ToArray();
        var map = MapGenParams.Default with { Width = 96, Height = 96, Forests = 10, GoldMines = 2 * players, StartLocations = players };
        var sim = new Simulation(new SimConfig(seed, players, 1200, 4096) { Data = d, Map = map, Seats = seats.ToImmutableArray() });
        SaveScenes.PlaceBases(sim, army: true);
        return sim;
    }

    /// <summary>Every player's kept count of the capped type equals the walk; after the start army (tick 1) none passes the limit.</summary>
    private static void CheckCounts(Simulation sim, int heavy, string where)
    {
        World w = sim.World;
        for (int p = 0; p < w.Config.PlayerCount; p++)
        {
            Assert.True(w.UnitLimit(p, heavy, out int count, out int limit), where);
            int walk = UnitLimitTests.Recount(w, p, heavy);
            Assert.True(count == walk, $"{where}: player {p}'s kept count {count}, walk {walk} at tick {sim.TickNumber}");
            Assert.True(sim.TickNumber <= 1 || count <= limit, $"{where}: player {p}'s count {count} past {limit} at tick {sim.TickNumber}");
        }
    }

    /// <summary>
    /// 20 seeds, four AI seats (Malazan, Whirlwind, Malazan, Whirlwind; difficulties turning by seed), 6,000 ticks of fighting
    /// on a 96-cell generated map: after every tick the kept count equals the walk for every player and stays at or under the
    /// limit; no AI Train is refused for the limit; at tick 3,000 the match is saved and loaded, the count rebuilt equals
    /// the walk, and the loaded run equals the unsaved one, hash and count, every tick to 3,600.
    /// </summary>
    [Fact]
    public void TwentySeeds_FourAiSeatsFighting_KeptCountEqualsTheWalk_EveryTick_AndAfterALoad()
    {
        GameData d = s_mixed.Value;
        int heavy = d.FindUnit(MaxAliveLoaderTests.Heavy);
        var lines = new List<string>();
        for (int seed = 1; seed <= 20; seed++)
        {
            int s = seed;
            Simulation sim = Match(d, 4, (ulong)seed, p => (AiDifficulty)((s + p) % 3));
            int limitRefusals = 0, deaths = 0, trains = 0, atLimitTicks = 0;
            sim.RefusedForTests = (c, why, detail) => { if (why == CommandRefusal.TrainRefused && (TrainError)detail == TrainError.LimitReached) limitRefusals++; };
            for (int p = 0; p < 4; p += 2) sim.AiOf(p)!.SentForTests = c => { if (c.Kind == CommandKind.Train && c.TypeId == heavy) trains++; };
            int prev0 = 0;
            Simulation? loaded = null;
            while (sim.TickNumber < 6_000)
            {
                sim.Tick();
                CheckCounts(sim, heavy, $"seed {seed}");
                int now0 = UnitLimitTests.Recount(sim.World, 0, heavy);
                if (now0 < prev0) deaths += prev0 - now0;
                prev0 = now0;
                if (now0 == Limit) atLimitTicks++;
                if (sim.TickNumber == 3_000)
                {
                    loaded = SaveTestRun.Load(d, SaveTestRun.Save(sim));
                    CheckCounts(loaded, heavy, $"seed {seed} loaded");
                    Assert.Equal(sim.StateHash(), loaded.StateHash());
                }
                else if (loaded != null && sim.TickNumber <= 3_600)
                {
                    loaded.Tick();
                    CheckCounts(loaded, heavy, $"seed {seed} loaded");
                    Assert.True(sim.StateHash() == loaded.StateHash(), $"seed {seed}: the loaded run differs at tick {sim.TickNumber}");
                }
            }
            lines.Add($"seed {seed}: player 0 Heavy Trains sent {trains} (players 0 + 2), losses seen {deaths}, ticks at the limit {atLimitTicks}, refused for the limit {limitRefusals}");
            Assert.Equal(0, limitRefusals);
        }
        foreach (string l in lines) output.WriteLine(l);
    }

    /// <summary>
    /// Measure and report (docs/03 M8-4 "Known limits" (2)): a Malazan seat whose composition is only the capped type, against a
    /// Whirlwind seat, both waving early, 12,000 ticks, each difficulty. Asserted: no Train refused for the limit, the count true
    /// every tick, and the seat trains the type again after losses (a death frees a place). Reported: gold banked, Trains sent,
    /// losses, attack orders, and who is left.
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void OnlyTheCappedTypeInTheComposition_Report(AiDifficulty difficulty)
    {
        GameData d = s_onlyCapped.Value;
        int heavy = d.FindUnit(MaxAliveLoaderTests.Heavy);
        Simulation sim = Match(d, 2, 7, p => difficulty);
        int limitRefusals = 0, trains = 0, attacks = 0, losses = 0, trainsAfterLoss = 0, maxGold = 0;
        bool lost = false;
        sim.RefusedForTests = (c, why, detail) => { if (why == CommandRefusal.TrainRefused && (TrainError)detail == TrainError.LimitReached && c.Player == 0) limitRefusals++; };
        sim.AiOf(0)!.SentForTests = c =>
        {
            if (c.Kind == CommandKind.Train && c.TypeId == heavy)
            {
                trains++;
                if (lost) trainsAfterLoss++;
            }
            if (c.Kind is CommandKind.AttackMove or CommandKind.Attack) attacks++;
        };
        int prev = 0;
        var gold = new List<string>();
        while (sim.TickNumber < 12_000)
        {
            sim.Tick();
            CheckCounts(sim, heavy, difficulty.ToString());
            int live = UnitLimitTests.Recount(sim.World, 0, heavy);
            if (live < prev)
            {
                losses += prev - live;
                lost = true;
            }
            prev = live;
            maxGold = Math.Max(maxGold, sim.World.Gold[0]);
            if (sim.TickNumber % 2_000 == 0) gold.Add($"{sim.TickNumber}: {sim.World.Gold[0]} g / {sim.World.Wood[0]} w, heavy {live}, units {sim.World.LiveUnits(0)} vs {sim.World.LiveUnits(1)}");
        }
        output.WriteLine($"{difficulty}: Heavy Trains sent {trains} ({trainsAfterLoss} after the first loss), losses {losses}, attack orders {attacks}, max gold {maxGold}, refused for the limit {limitRefusals}");
        foreach (string g in gold) output.WriteLine("  " + g);
        Assert.Equal(0, limitRefusals);
        if (losses > 0) Assert.True(trainsAfterLoss > 0, $"{difficulty}: lost {losses} Heavy Infantry and never trained one again");
    }
}
