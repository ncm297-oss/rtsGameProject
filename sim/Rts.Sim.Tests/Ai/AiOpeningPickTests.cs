using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-8a (docs/03 "AI architecture", "Openings"): the shipped files hold one opening, <c>standard</c>, equal to the pre-M5-8a
/// top-level order (criterion 1); the seat picks an opening at construction, Easy the first, Normal and Hard a uniform draw
/// from the AI's own stream, the same for the same seed (criterion 3); and the AI plays the opening it picked: its build
/// order, its compositions' units and its attack thresholds (criterion 4).
/// </summary>
public class AiOpeningPickTests
{
    private readonly ITestOutputHelper _out;

    public AiOpeningPickTests(ITestOutputHelper output) => _out = output;

    // ---------- criterion 1: the re-wrap moved no number ----------

    /// <summary>A step as the pre-M5-8a files wrote it: "time 390 research age_ii x1" (seconds and pop, not ticks and half-pop).</summary>
    private static string StepText(AiStep s)
    {
        string at = s.Trigger switch
        {
            AiStepTrigger.Time => "time " + (s.At / 20.0).ToString(CultureInfo.InvariantCulture),
            AiStepTrigger.Workers => "workers " + s.At.ToString(CultureInfo.InvariantCulture),
            _ => "pop " + (s.At / 2.0).ToString(CultureInfo.InvariantCulture),
        };
        string what = DataLimits.AiStepActionIds[(int)s.Action];
        string id = s.Action == AiStepAction.Expand ? "-" : s.TargetKey;
        return $"{at} {what} {id} x{s.Count}";
    }

    private static string CompositionText(GameData d, AiComposition c) =>
        $"age {c.Age}: " + string.Join(", ", c.Units.Select((u, k) => $"{d.Units[u].Key} {c.Weights[k].ToString(CultureInfo.InvariantCulture)}"));

    // The pre-M5-8a top-level buildOrder / compositions / attack of each shipped ai.json (base b30462a), written out by hand
    // from the files so the re-wrap is checked against the numbers it started from. D21 (the data track's openings) adds
    // openings after this one; the "standard" opening keeps these numbers unless the data track retunes it on purpose.
    private static readonly string[] MalazanSteps =
    {
        "time 0 train malazan_laborer x15", "pop 6 build malazan_billet x1", "pop 8 build malazan_depot x1",
        "pop 10 build malazan_barracks x1", "pop 12 build malazan_billet x1", "pop 14 build malazan_crossbow_range x1",
        "pop 16 build malazan_armory x1", "pop 16 research melee_weapons_1 x1", "pop 18 build malazan_billet x1",
        "pop 25 build malazan_billet x1", "pop 32 build malazan_billet x1", "pop 39 build malazan_billet x1",
        "pop 46 build malazan_billet x1", "pop 53 build malazan_billet x1", "pop 60 build malazan_billet x1",
        "pop 67 build malazan_billet x1", "pop 74 build malazan_billet x1", "pop 81 build malazan_billet x1",
        "time 390 research age_ii x1", "time 390 train malazan_laborer x10", "time 450 build malazan_cadre_tower x1",
        "time 480 build malazan_engineers_yard x1", "time 480 expand - x1", "time 510 build malazan_wickan_corral x1",
        "time 540 research moranth_supply x1", "time 570 build malazan_watchtower x1",
    };

    private static readonly string[] MalazanCompositions =
    {
        "age 1: malazan_crossbowman 30, malazan_heavy_infantry 35",
        "age 2: malazan_cadre_mage 10, malazan_catapult 5, malazan_crossbowman 30, malazan_heavy_infantry 35, malazan_sapper 10, malazan_wickan_lancer 10",
    };

    private static readonly string[] WhirlwindSteps =
    {
        "time 0 train whirlwind_camp_follower x13", "pop 6 build whirlwind_tent x1", "pop 8 build whirlwind_raider_camp x1",
        "pop 9 build whirlwind_supply_cache x1", "pop 11 build whirlwind_tent x1", "pop 12 build whirlwind_archer_camp x1",
        "pop 14 build whirlwind_horse_lines x1", "pop 16 build whirlwind_tent x1", "pop 20 build whirlwind_smithy x1",
        "pop 20 research melee_weapons_1 x1", "pop 23 build whirlwind_tent x1", "pop 30 build whirlwind_tent x1",
        "pop 37 build whirlwind_tent x1", "pop 44 build whirlwind_tent x1", "pop 51 build whirlwind_tent x1",
        "pop 58 build whirlwind_tent x1", "pop 65 build whirlwind_tent x1", "pop 72 build whirlwind_tent x1",
        "pop 79 build whirlwind_tent x1", "time 450 research age_ii x1", "time 450 train whirlwind_camp_follower x8",
        "time 480 expand - x1", "time 510 build whirlwind_shrine x1", "time 540 build whirlwind_raider_camp x1",
        "time 570 build whirlwind_ram_yard x1", "time 600 research dryjhnas_prophecy x1",
    };

    private static readonly string[] WhirlwindCompositions =
    {
        "age 1: whirlwind_desert_archer 20, whirlwind_horse_raider 15, whirlwind_raider 30",
        "age 2: whirlwind_battering_ram 5, whirlwind_desert_archer 20, whirlwind_horse_raider 15, whirlwind_priest 10, whirlwind_raider 30, whirlwind_zealot 20",
    };

    [Theory]
    [InlineData("malazan", 40, 1544)]
    [InlineData("whirlwind", 28, 861)]
    public void BothShippedFiles_HoldOneOpening_Standard_EqualToThePreM58aOrder(string faction, int attackHalfPop, int attackCost)
    {
        GameData d = TestSim.Data;
        AiDef ai = d.Ai[d.FindFaction(faction)];
        AiOpening o = Assert.Single(ai.Openings);
        Assert.Equal(("standard", "Standard", "The faction's standard build"), (o.Id, o.DisplayName, o.Description));
        Assert.Equal(faction == "malazan" ? MalazanSteps : WhirlwindSteps, o.BuildOrder.Select(StepText));
        Assert.Equal(faction == "malazan" ? MalazanCompositions : WhirlwindCompositions, o.Compositions.Select(c => CompositionText(d, c)));
        Assert.Equal((attackHalfPop, attackCost), (o.AttackMinHalfPop, o.AttackMinCost));
        // Every resolved id is the one its key names (the loader resolved them as before).
        foreach (AiStep s in o.BuildOrder)
        {
            if (s.Action == AiStepAction.Expand) Assert.Equal(-1, s.Target);
            else if (s.Action == AiStepAction.Build) Assert.Equal(d.FindBuilding(s.TargetKey), s.Target);
            else if (s.Action == AiStepAction.Train) Assert.Equal(d.FindUnit(s.TargetKey), s.Target);
            else Assert.Equal(d.FindTech(s.TargetKey), s.Target);
        }
    }

    // ---------- criterion 3: the pick ----------

    /// <summary>The shipped data with malazan's ai.json holding <paramref name="count"/> openings: the shipped one, then copies named <c>opening_k</c>, each edited by <paramref name="edit"/>.</summary>
    private static GameData WithOpenings(int count, Action<int, JsonNode>? edit = null) => AiScenes.DataWithAi("malazan", root =>
    {
        JsonArray openings = root["openings"]!.AsArray();
        JsonNode first = openings[0]!;
        for (int k = 1; k < count; k++)
        {
            JsonNode copy = first.DeepClone();
            copy["id"] = $"opening_{k}";
            copy["displayName"] = $"Opening {k}";
            openings.Add(copy);
        }
        for (int k = 0; k < count && edit != null; k++) edit(k, openings[k]!);
    });

    private static readonly Lazy<GameData> s_three = new(() => WithOpenings(3));

    /// <summary>A cheap seat build: player 0 an AI of malazan at <paramref name="difficulty"/>, player 1 human, on a small flat map; nothing ticks.</summary>
    private static Simulation Seat(GameData data, ulong seed, AiDifficulty difficulty)
    {
        var seats = ImmutableArray.Create(new PlayerSeat(0, SeatController.Ai, difficulty), new PlayerSeat(1, SeatController.Human, difficulty));
        var config = new SimConfig(seed, 2, 16, 16) { Data = data, Seats = seats };
        return new Simulation(config, LocalMovementTests.Flat(16));
    }

    [Fact]
    public void Easy_AlwaysPlaysTheFirstOpening()
    {
        for (ulong seed = 1; seed <= 50; seed++)
            Assert.Equal(0, Seat(s_three.Value, seed, AiDifficulty.Easy).AiOpening(0));
    }

    [Theory]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void NormalAndHard_PickEveryOpening_OverSeeds1To200(AiDifficulty difficulty)
    {
        var hits = new int[3];
        for (ulong seed = 1; seed <= 200; seed++)
        {
            int pick = Seat(s_three.Value, seed, difficulty).AiOpening(0);
            Assert.InRange(pick, 0, 2);
            hits[pick]++;
            // The rule, spelled out: the first draw of the seat's own stream, uniform over the openings.
            var rng = new SimRng(seed, (ulong)RngStream.Ai(0));
            Assert.Equal(rng.NextInt(0, 3), pick);
        }
        _out.WriteLine($"{difficulty}: picks per opening over seeds 1-200: {string.Join(", ", hits)}");
        Assert.All(hits, h => Assert.True(h > 0, $"an opening never picked: {string.Join(", ", hits)}"));
    }

    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void TheSameSeedAndDifficulty_PickTheSameOpening_InTwoSims(AiDifficulty difficulty)
    {
        for (ulong seed = 1; seed <= 20; seed++)
            Assert.Equal(Seat(s_three.Value, seed, difficulty).AiOpening(0), Seat(s_three.Value, seed, difficulty).AiOpening(0));
    }

    [Fact]
    public void AHumanSeat_OrAnUnknownPlayer_HasNoOpening()
    {
        Simulation sim = Seat(s_three.Value, 1, AiDifficulty.Normal);
        Assert.Equal(-1, sim.AiOpening(1));
        Assert.Equal(-1, sim.AiOpening(2));
        Assert.Equal(-1, sim.AiOpening(-1));
    }

    /// <summary>The pick moves no hashed stream: the world's state hash is the same whichever opening the seed picks, and as with no AI.</summary>
    [Fact]
    public void ThePick_MovesNoHashedState()
    {
        ulong withAi = Seat(s_three.Value, 7, AiDifficulty.Hard).StateHash();
        var seats = ImmutableArray.Create(new PlayerSeat(0, SeatController.Human, AiDifficulty.Hard), new PlayerSeat(1, SeatController.Human, AiDifficulty.Hard));
        var human = new Simulation(new SimConfig(7, 2, 16, 16) { Data = s_three.Value, Seats = seats }, LocalMovementTests.Flat(16));
        Assert.Equal(human.StateHash(), withAi);
    }

    /// <summary>
    /// A replay recorded with a Normal AI on the 3-opening data plays back (no AI) to equal checkpoints, and a match rebuilt
    /// from the replay's seed and seats picks the same opening as the recorded one.
    /// </summary>
    [Fact]
    public void AReplayWithANormalAi_PlaysBack_AndTheSameSetupPicksTheSameOpening()
    {
        GameData data = s_three.Value;
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        // A seed whose pick is not the first opening, so the draw is exercised.
        ulong seed = 1;
        while (Seat(data, seed, AiDifficulty.Normal).AiOpening(0) == 0) seed++;
        Simulation sim = AiScenes.Generated(seats, seed, record: true, out ReplayRecorder? rec, data: data);
        int pick = sim.AiOpening(0);
        Assert.NotEqual(0, pick);
        for (int t = 0; t < 600; t++) sim.Tick();
        Assert.True(sim.AiOf(0)!.CommandsSent > 0);
        Replay r = rec!.ToReplay();
        Assert.Equal(seed, r.Seed);
        ReplayResult played = ReplayPlayer.Run(r, data);
        Assert.True(played.Ok, played.ToString());
        Simulation again = AiScenes.Generated(r.Seats.Select(s => new PlayerSeat(data.FindFaction(s.Faction), s.Controller, s.Difficulty)).ToArray(),
            r.Seed, record: false, out _, data: data);
        Assert.Equal(pick, again.AiOpening(0));
    }

    // ---------- criterion 4: the picked opening is played ----------

    /// <summary>Two openings whose first (and only) step differs: A trains a worker at once, B builds a billet at once.</summary>
    private static readonly Lazy<GameData> s_twoOrders = new(() => WithOpenings(2, (k, o) =>
        o["buildOrder"] = k == 0
            ? AiScenes.BuildOrder(("time", 0, "train", "malazan_laborer", 1))
            : AiScenes.BuildOrder(("time", 0, "build", "malazan_billet", 0))));

    /// <summary>The flat-scene seed (from 1 up) whose Normal seat 0 picks opening <paramref name="index"/>.</summary>
    private static ulong SeedPicking(GameData data, int index)
    {
        for (ulong seed = 1; seed < 1000; seed++)
            if (Seat(data, seed, AiDifficulty.Normal).AiOpening(0) == index) return seed;
        throw new InvalidOperationException($"no seed picks opening {index}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TheExecutor_SendsThePickedOpeningsStep(int index)
    {
        GameData data = s_twoOrders.Value;
        ulong seed = SeedPicking(data, index);
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data, seed: seed);
        Assert.Equal(index, sim.AiOpening(0));
        AiPlayer ai = sim.AiOf(0)!;
        Assert.Same(data.Ai[0].Openings[index], ai.Opening);
        Assert.Single(ai.Board.StepDone); // the Blackboard is sized by the picked opening's order
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        List<Command> log = AiScenes.Log(sim, 0);
        for (int t = 0; t < 10 * ai.ThinkTicks; t++) sim.Tick();
        Command first = log.First(c => c.Kind is CommandKind.Train or CommandKind.Build);
        if (index == 0) Assert.Equal((CommandKind.Train, data.FindUnit("malazan_laborer")), (first.Kind, first.TypeId));
        else Assert.Equal((CommandKind.Build, data.FindBuilding("malazan_billet")), (first.Kind, first.TypeId));
        Assert.True(ai.Board.StepDone[0]);
    }

    /// <summary>Two openings with different attack thresholds: A waits for 100 pop, B goes at 1 pop and no cost.</summary>
    private static readonly Lazy<GameData> s_twoAttacks = new(() => WithOpenings(2, (k, o) =>
    {
        o["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
        o["attack"] = new JsonObject { ["minArmyPop"] = k == 0 ? 100 : 1, ["minArmyCost"] = 0 };
    }));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TheMilitaryManager_UsesThePickedOpeningsThresholds(int index)
    {
        GameData data = s_twoAttacks.Value;
        ulong seed = SeedPicking(data, index);
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data, seed: seed);
        Assert.Equal(index, sim.AiOpening(0));
        World w = sim.World;
        // A billet of player 0 in sight of player 1's hall (MilitaryManagerTests' scene), and four Heavy Infantry.
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out _));
        int heavy = data.FindUnit("malazan_heavy_infantry");
        for (int k = 0; k < 4; k++) CombatScenes.Place(sim, 0, heavy, w.NavGrid.CellCenter(18 + k, 28));
        AiPlayer ai = sim.AiOf(0)!;
        for (int t = 0; t < 3 * ai.ThinkTicks + 1; t++) sim.Tick();
        float scale = ai.Knobs.WaveSizeMultiplier;
        if (index == 0)
            Assert.Equal(0, ai.Board.WavesStarted);
        else
        {
            Assert.Equal(1, ai.Board.WavesStarted);
            Assert.Equal(data.Ai[0].Openings[1].AttackMinHalfPop * scale, ai.Board.WaveMinHalfPop);
            Assert.Equal(2, data.Ai[0].Openings[1].AttackMinHalfPop);
        }
    }

    /// <summary>The production manager trains the picked opening's composition: A only Heavy Infantry, B only Crossbowmen.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TheProductionManager_TrainsThePickedOpeningsMix(int index)
    {
        GameData data = WithOpenings(2, (k, o) =>
        {
            o["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
            string unit = k == 0 ? "malazan_heavy_infantry" : "malazan_crossbowman";
            o["compositions"] = JsonNode.Parse($$"""[{ "age": 1, "units": { "{{unit}}": 1 } }, { "age": 2, "units": { "{{unit}}": 1 } }]""");
        });
        ulong seed = SeedPicking(data, index);
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data, seed: seed);
        Assert.Equal(index, sim.AiOpening(0));
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_barracks"), 44 * AiScenes.FlatSize + 8, out _));
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_crossbow_range"), 44 * AiScenes.FlatSize + 16, out _));
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        List<Command> log = AiScenes.Log(sim, 0);
        AiPlayer ai = sim.AiOf(0)!;
        for (int t = 0; t < 4 * ai.ThinkTicks; t++) sim.Tick();
        List<int> trained = log.Where(c => c.Kind == CommandKind.Train).Select(c => c.TypeId).Distinct().ToList();
        _out.WriteLine($"opening {index}: trained {string.Join(", ", trained.Select(u => data.Units[u].Key))}");
        Assert.Equal(new[] { data.FindUnit(index == 0 ? "malazan_heavy_infantry" : "malazan_crossbowman") }, trained);
    }
}
