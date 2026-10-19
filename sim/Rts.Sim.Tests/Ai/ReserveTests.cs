using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4g slice 1 (BUG-0981, docs/03 "Implementation (M5-4g)"): the build order's reserved step. Each think the executor names
/// the first due, not done, unsent <c>build</c> / <c>research</c> / <c>expand</c> step the purse can't pay (and that nothing
/// but its cost holds back); the production manager and the <c>train</c> steps after it spend only above its cost; the
/// gatherers lean toward the kind it lacks by <c>ai.reserveShift</c>. Scenes: the flat AI scene, a Hard Malazan seat (0.5 s
/// thinks, no slack) against a human seat, every Malazan opening given the test's order (a Hard seat may pick any), the
/// purse set just before a think.
/// </summary>
public class ReserveTests
{
    private readonly ITestOutputHelper _out;

    public ReserveTests(ITestOutputHelper output) => _out = output;

    private static int Unit(GameData d, string key) => d.FindUnit(key);

    /// <summary>Shipped data with every Malazan opening's build order <paramref name="order"/>, its compositions <paramref name="unit"/> alone, and optionally rules.json ai edits.</summary>
    private static GameData Data(JsonArray order, string unit, Action<JsonNode>? rulesAi = null) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = order.DeepClone();
                o["compositions"] = JsonNode.Parse($$"""[{ "age": 1, "units": { "{{unit}}": 1 } }, { "age": 2, "units": { "{{unit}}": 1 } }]""");
            }
        });
        // The scout stays home: its worker would leave the gather counts below. These scenes have no army, so the reserve is
        // named from the start (reserveArmyShare 0, M5-4g's first rule); the BUG-1021 gate's own rows set it back.
        dir.EditJson("common/rules.json", r =>
        {
            r["ai"]!["scoutAfter"] = 3600;
            r["ai"]!["reserveArmyShare"] = 0;
            rulesAi?.Invoke(r["ai"]!);
        });
    });

    /// <summary>The flat scene with a finished Barracks and Crossbow Range for player 0 (Age II's two kinds of building; the trainers).</summary>
    private static Simulation Scene(GameData data, int workers = 5)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human), data, workers: workers);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_barracks"), 44 * AiScenes.FlatSize + 8, out _));
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_crossbow_range"), 44 * AiScenes.FlatSize + 16, out _));
        return sim;
    }

    /// <summary>A think's reserve as the seam saw it at the think's end (the seat clears it then).</summary>
    private sealed record Reserve(int Step, int Gold, int Wood, bool ShortGold, bool ShortWood, float Share);

    /// <summary>The last think's reserve (<see cref="Think"/>).</summary>
    private Reserve _last = new(-1, 0, 0, false, false, 0f);

    /// <summary>
    /// Runs to the next think, sets player 0's purse to <paramref name="gold"/> / <paramref name="wood"/> just before it
    /// (unless negative), thinks; the commands it sent. Its reserve is in <see cref="_last"/>; <paramref name="probe"/> runs
    /// at the think's end, while the reserve still stands.
    /// </summary>
    private List<Command> Think(Simulation sim, List<Command> log, int gold = -1, int wood = -1, Action<AiPlayer>? probe = null)
    {
        AiPlayer ai = sim.AiOf(0)!;
        ai.ThoughtForTests = a =>
        {
            _last = new Reserve(a.ReserveStep, a.ReserveGold, a.ReserveWood, a.ReserveShortGold, a.ReserveShortWood, a.WantedGoldShare());
            probe?.Invoke(a);
        };
        while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
        if (gold >= 0) BuildMaps.SetTotals(sim, 0, gold, wood);
        int before = log.Count;
        sim.Tick();
        Assert.Equal(-1, ai.ReserveStep); // cleared at the think's end: nothing for a save to hold
        return log.Skip(before).ToList();
    }

    private static readonly Lazy<GameData> s_order = new(() => Data(AiScenes.BuildOrder(
        ("time", 0, "train", "malazan_heavy_infantry", 1),  // 0: before the reserved step: spends freely
        ("time", 0, "research", "melee_weapons_1", 0),      // 1: no Armory yet: its requirement, not its cost, holds it back
        ("time", 0, "build", "malazan_armory", 0),          // 2: 100 / 100, the reserved step
        ("time", 0, "research", "age_ii", 0),               // 3: 400 / 200, not reserved while step 2 is
        ("time", 0, "train", "malazan_crossbowman", 3)),    // 4: after it: only above the reserve
        "malazan_crossbowman"));

    /// <summary>
    /// The reserved step is the first due unaffordable build / research / expand in file order whose cost alone holds it
    /// back: with 120 / 110 the earlier Heavy Infantry step trains (66 / 90 left), the Forge research is skipped (no Forge),
    /// the Armory (100 / 100) is reserved, Age II is not; the later Crossbowman step (36 / 45, which 66 / 90 pays) and the
    /// production manager send nothing. Short of both: the gather share stays <c>goldShare</c>.
    /// </summary>
    [Fact]
    public void TheReservedStep_IsTheFirstDueUnaffordableStep_AndBlocksLaterTrainsAndProduction()
    {
        GameData d = s_order.Value;
        Simulation sim = Scene(d);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        List<Command> sent = Think(sim, log, 120, 110);
        List<int> trains = sent.Where(c => c.Kind == CommandKind.Train).Select(c => c.TypeId).ToList();
        Assert.Equal(new[] { Unit(d, "malazan_heavy_infantry") }, trains);
        Assert.Equal(2, _last.Step);
        Assert.Equal((100, 100), (_last.Gold, _last.Wood));
        Assert.Equal((true, true), (_last.ShortGold, _last.ShortWood));
        Assert.Equal(d.AiRules.GoldShare, _last.Share);
        Assert.DoesNotContain(sent, c => c.Kind is CommandKind.Build or CommandKind.Research);
    }

    /// <summary>
    /// The reserved step's own retry is unaffected: at the first think with 300 / 300 and a worker free the Armory is sent; Age II (400 / 200,
    /// 200 / 200 left) is the reserved step then, short of gold only (the share goes up by <c>reserveShift</c>), and the
    /// Crossbowman step after it still waits.
    /// </summary>
    [Fact]
    public void TheReservedStepsOwnRetry_IsSentOnceAffordable_AndTheNextStepIsReserved()
    {
        GameData d = s_order.Value;
        Simulation sim = Scene(d);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        Think(sim, log, 120, 110);
        // The workers walk to their nodes first (a builder is idle or at work, not on its way): the first think with one.
        List<Command> sent = Think(sim, log, 300, 300);
        for (int k = 0; k < 40 && !sent.Any(c => c.Kind == CommandKind.Build); k++) sent = Think(sim, log, 300, 300);
        Assert.Contains(sent, c => c.Kind == CommandKind.Build && c.TypeId == d.FindBuilding("malazan_armory"));
        Assert.DoesNotContain(sent, c => c.Kind == CommandKind.Train);
        Assert.Equal(3, _last.Step);
        Assert.Equal((400, 200), (_last.Gold, _last.Wood));
        Assert.Equal((true, false), (_last.ShortGold, _last.ShortWood));
        Assert.Equal(d.AiRules.GoldShare + d.AiRules.ReserveShift, _last.Share, 5);
    }

    private static readonly Lazy<GameData> s_ageTwo = new(() => Data(AiScenes.BuildOrder(("time", 0, "research", "age_ii", 0)), "malazan_heavy_infantry"));

    /// <summary>
    /// The production manager spends only above the reserve: Age II due at 300 / 300 holds 400 / 200, so no Heavy
    /// Infantry (54 / 20) is trained; a wood-only cost of 50 fits above it, a gold one doesn't; at 460 / 300 the research is sent.
    /// </summary>
    [Fact]
    public void TheProductionManager_SpendsOnlyAboveTheReserve()
    {
        GameData d = s_ageTwo.Value;
        Simulation sim = Scene(d);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        bool[] fits = new bool[3];
        List<Command> sent = Think(sim, log, 300, 300, a =>
            (fits[0], fits[1], fits[2]) = (a.CanAffordAboveReserve(0, 50), a.CanAffordAboveReserve(54, 20), a.CanAffordAboveReserve(0, 101)));
        Assert.Equal(0, _last.Step);
        Assert.DoesNotContain(sent, c => c.Kind is CommandKind.Train or CommandKind.Research);
        Assert.Equal(new[] { true, false, false }, fits);
        sent = Think(sim, log, 460, 300);
        Assert.Contains(sent, c => c.Kind == CommandKind.Research && c.TypeId == d.FindTech("age_ii"));
        Assert.Equal(-1, _last.Step);
    }

    /// <summary>With no due build / research / expand step the reserve is 0 / 0 and production spends as before.</summary>
    [Fact]
    public void NoReservedStep_TheReserveIsZero_AndProductionTrains()
    {
        GameData d = Data(AiScenes.BuildOrder(("time", 3600, "research", "age_ii", 0)), "malazan_heavy_infantry");
        Simulation sim = Scene(d);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        List<Command> sent = Think(sim, log, 300, 300);
        Assert.Equal((-1, 0, 0), (_last.Step, _last.Gold, _last.Wood));
        Assert.Equal(d.AiRules.GoldShare, _last.Share);
        Assert.Contains(sent, c => c.Kind == CommandKind.Train && c.TypeId == Unit(d, "malazan_heavy_infantry"));
    }

    /// <summary>
    /// The no-node guard, every gold mine marked dropped (the gatherers' search finds none), Age II (400 / 200) due. Short of
    /// gold (300 / 250): since BUG-1023 (item 2) the step is not reserved at all (no gathering could pay it, and holding
    /// its wood would bank it for nothing), so a Heavy Infantry (54 / 20) trains; the twin with the mines known holds
    /// 400 / 200 and trains nothing. Not short of gold (450 / 150): gold drops out of the reserve, the wood part stands.
    /// </summary>
    [Theory]
    [InlineData(true, 300, 250)]
    [InlineData(false, 300, 250)]
    [InlineData(true, 450, 150)]
    public void TheNoNodeGuard_AStepShortOfAnUnreachableKindIsNotReserved_AnUnreachableKindDropsOut(bool goldGone, int gold, int wood)
    {
        GameData d = s_ageTwo.Value;
        Simulation sim = Scene(d);
        AiPlayer ai = sim.AiOf(0)!;
        ResourceStore r = sim.World.Resources;
        int mines = 0;
        for (int s = 0; s < r.Capacity && goldGone; s++)
            if (r.Alive[s] && d.Resources[r.TypeId[s]].Resource == ResourceKind.Gold)
            {
                ai.Board.NodeDropped[s] = r.Generation[s];
                mines++;
            }
        Assert.True(!goldGone || mines > 0);
        List<Command> log = AiScenes.Log(sim, 0);
        List<Command> sent = Think(sim, log, gold, wood);
        bool trained = sent.Any(c => c.Kind == CommandKind.Train && c.TypeId == Unit(d, "malazan_heavy_infantry"));
        if (gold < 400 && goldGone)
        {
            Assert.Equal((-1, 0, 0), (_last.Step, _last.Gold, _last.Wood));
            Assert.Equal((false, false), (_last.ShortGold, _last.ShortWood));
            Assert.True(trained);
        }
        else if (goldGone)
        {
            Assert.Equal((0, 0, 200), (_last.Step, _last.Gold, _last.Wood));
            Assert.Equal((false, true), (_last.ShortGold, _last.ShortWood));
            Assert.False(trained); // 150 - 20 wood is under the 200 held
        }
        else
        {
            Assert.Equal((0, 400, 200), (_last.Step, _last.Gold, _last.Wood));
            Assert.Equal((true, false), (_last.ShortGold, _last.ShortWood));
            Assert.False(trained);
        }
    }

    /// <summary>
    /// The economy's house rule is unaffected: with the population within <c>houseWhenPopWithin</c> of the cap and Age II
    /// reserved (400 / 200 of 100 / 60), a Billet (0 / 50) is still placed.
    /// </summary>
    [Fact]
    public void TheHouseRule_IsUnaffectedByTheReserve()
    {
        GameData d = s_ageTwo.Value;
        int keep = d.FindBuilding("malazan_garrison_keep");
        int capHalf = d.Buildings[keep].HalfPopProvided;
        int workers = (capHalf - d.AiRules.HouseHalfPopMargin + 1) / 2;
        Simulation sim = Scene(d, workers);
        Assert.True(sim.World.HalfPopCap[0] - sim.World.HalfPop[0] <= d.AiRules.HouseHalfPopMargin,
            $"cap {sim.World.HalfPopCap[0]} pop {sim.World.HalfPop[0]}");
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        List<Command> sent = Think(sim, log, 100, 60);
        Assert.Equal(0, _last.Step);
        Assert.Contains(sent, c => c.Kind == CommandKind.Build && c.TypeId == d.FindBuilding("malazan_billet"));
    }

    /// <summary>
    /// BUG-0981's <c>age_ii</c> case staged: a Hard Malazan seat with a Barracks and a Crossbow Range, the start purse, Age II
    /// (400 / 200) due at 1:00 and a Heavy Infantry : Crossbowman army mix. Before M5-4g the production manager spent every
    /// think's gold and wood on the mix and Age II was never paid; with the reserve it is researched (or queued) within 6:00.
    /// </summary>
    [Fact]
    public void Bug0981_AnAgeTwoStepStarvedByProduction_IsResearched()
    {
        GameData d = AiScenes.DataWith(dir =>
        {
            dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(("time", 0, "train", "malazan_laborer", 4), ("time", 60, "research", "age_ii", 0));
                o["compositions"] = JsonNode.Parse("""[{ "age": 1, "units": { "malazan_heavy_infantry": 2, "malazan_crossbowman": 1 } }, { "age": 2, "units": { "malazan_heavy_infantry": 1 } }]""");
            }
        });
            // The reserve from the start: this row stages the reserve, the BUG-1021 gate has its own rows.
            dir.EditJson("common/rules.json", r => r["ai"]!["reserveArmyShare"] = 0);
        });
        Simulation sim = Scene(d);
        AiPlayer ai = sim.AiOf(0)!;
        int ageTwo = d.FindTech("age_ii");
        int queuedAt = -1, army = 0;
        for (int t = 0; t < 6 * 60 * SimConstants.TicksPerSecond && queuedAt < 0; t++)
        {
            sim.Tick();
            if (ai.Thinks > 0 && ai.TechResearchedOrQueued(ageTwo)) queuedAt = sim.TickNumber;
        }
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && !d.Units[u.TypeId[i]].IsWorker) army++;
        _out.WriteLine($"Age II queued at tick {queuedAt}; army units {army}; gold {sim.World.Gold[0]} wood {sim.World.Wood[0]}");
        Assert.True(queuedAt > 0, "Age II never queued in 6:00");
    }

    /// <summary>The wanted gold share: <c>goldShare</c> +/- <c>reserveShift</c> when short of one kind only, unchanged otherwise, clamped to 0-1; a shift of 0 returns <c>goldShare</c> exactly.</summary>
    [Theory]
    [InlineData(0.6f, 0.25f, true, false, 0.85f)]
    [InlineData(0.6f, 0.25f, false, true, 0.35f)]
    [InlineData(0.6f, 0.25f, true, true, 0.6f)]
    [InlineData(0.6f, 0.25f, false, false, 0.6f)]
    [InlineData(0.9f, 0.25f, true, false, 1f)]
    [InlineData(0.1f, 0.25f, false, true, 0f)]
    [InlineData(1f, 1f, false, true, 0f)]
    [InlineData(0f, 1f, true, false, 1f)]
    public void TheWantedGoldShare_ShiftsTowardTheLackingKind_Clamped(float goldShare, float shift, bool shortGold, bool shortWood, float expected)
    {
        Assert.Equal(expected, AiPlayer.WantedGoldShare(goldShare, shift, shortGold, shortWood), 5);
    }

    /// <summary>
    /// <c>reserveShift</c> 0 reproduces the pre-M5-4g split exactly (bit for bit <c>goldShare</c>) whatever the reserved step
    /// lacks, and so the gather counts <c>round(goldShare x n)</c> of every worker count.
    /// </summary>
    [Theory]
    [InlineData(0.6f)]
    [InlineData(0.35f)]
    [InlineData(1f)]
    [InlineData(0f)]
    public void AShiftOfZero_IsExactlyGoldShare(float goldShare)
    {
        foreach (bool g in new[] { false, true })
            foreach (bool w in new[] { false, true })
            {
                float share = AiPlayer.WantedGoldShare(goldShare, 0f, g, w);
                Assert.Equal(BitConverter.SingleToInt32Bits(goldShare), BitConverter.SingleToInt32Bits(share));
            }
    }

    /// <summary>
    /// The rebalance follows the shift, one gatherer a think: six workers settle at round(0.35 x 6) = 2 on gold while Age II
    /// lacks only wood (1,000 / 0 each think), then, lacking only gold (0 / 1,000), move to round(0.85 x 6) = 5 on gold, the
    /// count rising by at most one a think.
    /// </summary>
    [Fact]
    public void TheRebalance_MovesOneGathererAThink_TowardTheLackingKind()
    {
        GameData d = s_ageTwo.Value;
        Simulation sim = Scene(d, workers: 6);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 80; k++) Think(sim, log, 1000, 0);
        Assert.Equal((true, false), (_last.ShortWood, _last.ShortGold));
        int onGold = OnGold(sim, ai);
        Assert.Equal((int)MathF.Floor((d.AiRules.GoldShare - d.AiRules.ReserveShift) * 6 + 0.5f), onGold);
        int want = (int)MathF.Floor((d.AiRules.GoldShare + d.AiRules.ReserveShift) * 6 + 0.5f);
        var counts = new List<int> { onGold };
        for (int k = 0; k < 40 && counts[^1] != want; k++)
        {
            Think(sim, log, 0, 1000);
            counts.Add(OnGold(sim, ai));
        }
        _out.WriteLine($"gatherers on gold, think by think: {string.Join(", ", counts)}");
        Assert.Equal(want, counts[^1]);
        for (int k = 1; k < counts.Count; k++) Assert.InRange(counts[k] - counts[k - 1], 0, 1);
    }

    // ---------- BUG-1021: the reserve waits for a wave-sized army or the first wave ----------

    /// <summary>Age II alone, the shipped <c>reserveArmyShare</c> unless <paramref name="share"/> is given.</summary>
    private static GameData GateData(double? share) => Data(AiScenes.BuildOrder(("time", 0, "research", "age_ii", 0)), "malazan_heavy_infantry",
        a => a["reserveArmyShare"] = share ?? TestSim.Data.AiRules.ReserveArmyShare);

    private static readonly Lazy<GameData> s_gate = new(() => GateData(null));

    /// <summary>Spawns <paramref name="count"/> Heavy Infantry for player 0 between its hall and its Barracks, and ticks once so they are in.</summary>
    private static void SpawnArmy(Simulation sim, int count)
    {
        int hi = sim.World.Data.FindUnit("malazan_heavy_infantry");
        for (int k = 0; k < count; k++) sim.Enqueue(Command.SpawnUnit(0, hi, new System.Numerics.Vector2(14.5f + k % 10, 34.5f + k / 10)));
        sim.Tick();
    }

    /// <summary>Heavy Infantry needed for <paramref name="share"/> of the seat's wave (<c>attack.minArmyPop</c> x the difficulty's <c>waveSizeMultiplier</c>), rounded up.</summary>
    private static int Needed(AiPlayer ai, double share)
    {
        int half = ai.Data.Units[ai.Data.FindUnit("malazan_heavy_infantry")].HalfPop;
        return (int)Math.Ceiling(share * ai.Opening.AttackMinHalfPop * ai.Knobs.WaveSizeMultiplier / half - 1e-6);
    }

    /// <summary>
    /// BUG-1021: with the shipped <c>reserveArmyShare</c> (1) and no wave sent, Age II (400 / 200, the purse 300 / 300) is not
    /// reserved while the army is one Heavy Infantry short of the opening's wave: production may spend. With the army
    /// wave-sized it is reserved (400 / 200) and production waits.
    /// </summary>
    [Fact]
    public void Bug1021_TheReserve_WaitsForAWaveSizedArmy()
    {
        GameData d = s_gate.Value;
        Assert.Equal(1f, d.AiRules.ReserveArmyShare);
        Simulation sim = Scene(d);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        int need = Needed(ai, 1);
        Assert.True(need > 1, $"wave {ai.Opening.AttackMinHalfPop} half-pop x {ai.Knobs.WaveSizeMultiplier}");
        SpawnArmy(sim, need - 1);
        bool fits = false;
        Think(sim, log, 300, 300, a => fits = a.CanAffordAboveReserve(54, 20));
        Assert.Equal((-1, 0, 0), (_last.Step, _last.Gold, _last.Wood));
        Assert.True(fits); // production may spend (the pop cap, not the reserve, is what stops a train in this scene)
        Assert.Equal(0, ai.Board.WavesStarted);
        SpawnArmy(sim, 1);
        Think(sim, log, 300, 300);
        Assert.Equal((0, 400, 200), (_last.Step, _last.Gold, _last.Wood));
    }

    /// <summary>BUG-1021: once the opening has sent a wave the reserve is named whatever the army (none here).</summary>
    [Fact]
    public void Bug1021_AfterTheFirstWave_TheReserveIsNamedWithNoArmy()
    {
        GameData d = s_gate.Value;
        Simulation sim = Scene(d);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        List<Command> sent = Think(sim, log, 300, 300);
        Assert.Equal(-1, _last.Step);
        Assert.Contains(sent, c => c.Kind == CommandKind.Train);
        ai.Board.WavesStarted = 1;
        sent = Think(sim, log, 300, 300);
        Assert.Equal((0, 400, 200), (_last.Step, _last.Gold, _last.Wood));
        Assert.DoesNotContain(sent, c => c.Kind == CommandKind.Train);
    }

    /// <summary>
    /// <c>reserveArmyShare</c> is a fraction of the wave: at 0.5 an army of half the wave (rounded up) names the reserve and
    /// one Heavy Infantry fewer doesn't; at 0 the reserve is named with no army (M5-4g's first rule).
    /// </summary>
    [Theory]
    [InlineData(0.5, 0, true)]
    [InlineData(0.5, -1, false)]
    [InlineData(0.0, 0, true)]
    public void Bug1021_TheArmyShare_IsAFractionOfTheWave(double share, int extra, bool reserved)
    {
        GameData d = GateData(share);
        Simulation sim = Scene(d);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        int count = Needed(ai, share) + extra;
        if (count > 0) SpawnArmy(sim, count);
        Think(sim, log, 300, 300);
        Assert.Equal(reserved ? 0 : -1, _last.Step);
    }

    /// <summary>The allocation row, in the serial collection (BUG-1020: a count taken next to the parallel batch can be charged another test's JIT).</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ReserveTests _t;

        public Serial(ITestOutputHelper output) => _t = new ReserveTests(output);

        /// <summary>
        /// A think naming a reserve allocates nothing: every step due and unaffordable (the requirement checks, the no-node
        /// guard's node walk, the shifted gather split and the reserve-bound production all run), 0 B.
        /// </summary>
        [Fact]
        public void AThinkWithAReserve_AllocatesNothing()
        {
            GameData d = s_order.Value;
            Simulation sim = Scene(d);
            AiPlayer ai = sim.AiOf(0)!;
            List<Command> log = AiScenes.Log(sim, 0);
            for (int k = 0; k < 10; k++) _t.Think(sim, log, 60, 10);
            Assert.Equal(2, _t._last.Step); // the Forge research (step 1) held back by its requirement, the Armory (step 2) reserved
            ai.ThoughtForTests = null;
            ai.SentForTests = null;
            while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
            BuildMaps.SetTotals(sim, 0, 60, 10);
            AllocationProbe.AssertZero(() => ai.Think(), _t._out);
        }
    }

    /// <summary>Player 0's workers the AI last sent to gold (its gather bookkeeping) that are gathering or returning.</summary>
    private static int OnGold(Simulation sim, AiPlayer ai)
    {
        UnitStore u = sim.World.Units;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && sim.World.Data.Units[u.TypeId[i]].IsWorker && ai.Board.UnitGeneration[i] == u.Generation[i]
                && (ai.Board.GatherKind[i] & 3) == 1) n++;
        return n;
    }
}
