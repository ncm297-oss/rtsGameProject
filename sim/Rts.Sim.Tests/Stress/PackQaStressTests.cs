using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.QA;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M7-2 (session 2026-11-10-0146), the long attacks on packs and bites: two AI seats training packs for 10,000 ticks
/// (the pop oracle every tick, the build order's pop steps, how long a pack head waits unstarted); a save at every tick of a
/// 400-tick pack match; a cross-process digest of a pack-and-bite match (<see cref="DigestEnv"/>); 50 packs completing
/// inside an allocation probe.
/// </summary>
[Collection(SerialCollection.Name)]
public class PackQaStressTests(ITestOutputHelper output)
{
    /// <summary>When set, <see cref="APackAndBiteMatch_HashesTheSame_TwiceInAProcess"/> writes its digest there (QA's cross-process check).</summary>
    internal const string DigestEnv = "RTS_QA_PACK_DIGEST";

    /// <summary>Both factions' line and ranged units train as packs of <paramref name="n"/> (and every unit bites when <paramref name="bite"/>).</summary>
    private static GameData Packs(int n, bool bite) => AiScenes.DataWith(dir =>
    {
        if (bite) OnHitPassiveTests.BiteAll(dir);
        if (n == 1) return;
        foreach ((string f, string u) in new[] { ("malazan", "malazan_heavy_infantry"), ("malazan", "malazan_crossbowman"), ("whirlwind", "whirlwind_raider") })
            dir.SetUnitField(f, u, "spawnCount", n.ToString());
    });

    private static readonly Lazy<GameData> s_p1 = new(() => Packs(1, false));
    private static readonly Lazy<GameData> s_p3 = new(() => Packs(3, true));
    private static readonly Lazy<GameData> s_p6 = new(() => Packs(6, false));

    private static GameData Variant(int n) => n switch { 1 => s_p1.Value, 3 => s_p3.Value, _ => s_p6.Value };

    internal sealed record SoakStats(int[] PopStepsDone, int[] PopSteps, int[] Trained, int[] MaxHalf, int[] LongestPackWait, int[] FinalHalf, int[] FinalCap);

    private SoakStats Soak(int packSize, ulong seed, int ticks)
    {
        GameData d = Variant(packSize);
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, seed, false, out _, data: d);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        var wait = new int[b.Capacity];
        var longest = new int[2];
        var maxHalf = new int[2];
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                if (w.HalfPop[p] != PackQaTests.Oracle(w, p)) Assert.Fail($"pack {packSize} seed {seed} tick {sim.TickNumber}: player {p} HalfPop {w.HalfPop[p]}, oracle {PackQaTests.Oracle(w, p)}");
                maxHalf[p] = Math.Max(maxHalf[p], w.HalfPop[p]);
            }
            for (int k = 0; k < b.Capacity; k++)
            {
                bool packWaiting = b.Alive[k] && b.QueueCount[k] > 0 && b.Progress[k] == 0 && !b.QueueIsTechAt(k, 0) && !b.UnderConstruction[k]
                    && d.Units[b.QueueTypeAt(k, 0)].SpawnCount > 1;
                wait[k] = packWaiting ? wait[k] + 1 : 0;
                if (packWaiting && wait[k] > longest[b.Owner[k]]) longest[b.Owner[k]] = wait[k];
            }
        }
        var done = new int[2];
        var steps = new int[2];
        var trained = new int[2];
        for (int p = 0; p < 2; p++)
        {
            AiPlayer ai = sim.AiOf(p)!;
            for (int i = 0; i < ai.Opening.BuildOrder.Length; i++)
            {
                if (ai.Opening.BuildOrder[i].Trigger != AiStepTrigger.Pop) continue;
                steps[p]++;
                if (ai.Board.StepDone[i]) done[p]++;
            }
            trained[p] = w.Ledger.Stats.UnitsTrained[p];
        }
        return new SoakStats(done, steps, trained, maxHalf, longest, new[] { w.HalfPop[0], w.HalfPop[1] }, new[] { w.HalfPopCap[0], w.HalfPopCap[1] });
    }

    private static string Show(SoakStats s) =>
        $"pop steps {s.PopStepsDone[0]}/{s.PopSteps[0]}, {s.PopStepsDone[1]}/{s.PopSteps[1]}; trained {s.Trained[0]}, {s.Trained[1]}; max half {s.MaxHalf[0]}, {s.MaxHalf[1]}; " +
        $"final {s.FinalHalf[0]}/{s.FinalCap[0]}, {s.FinalHalf[1]}/{s.FinalCap[1]}; longest pack wait {s.LongestPackWait[0]}, {s.LongestPackWait[1]}";

    /// <summary>
    /// Two Normal AIs, 10,000 ticks, on data where their line and ranged units come as packs of 3 (and every unit bites):
    /// the pop oracle every tick, every pop step the shipped-data run reaches is reached, and no pack head waits unstarted
    /// past 3,000 ticks while the cap is below the hard cap.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void TwoAis_TrainingPacksOf3_For10000Ticks_DoNotStallTheirPopSteps(ulong seed)
    {
        SoakStats shipped = Soak(1, seed, 10_000), packs = Soak(3, seed, 10_000);
        output.WriteLine($"seed {seed} shipped: {Show(shipped)}");
        output.WriteLine($"seed {seed} packs 3: {Show(packs)}");
        for (int p = 0; p < 2; p++)
        {
            Assert.True(packs.PopStepsDone[p] >= shipped.PopStepsDone[p], $"player {p}: pop steps {packs.PopStepsDone[p]} vs shipped {shipped.PopStepsDone[p]}");
            Assert.True(packs.LongestPackWait[p] < 3_000 || packs.FinalCap[p] >= Variant(3).Rules.HalfPopCap, $"player {p}: a pack waited {packs.LongestPackWait[p]} ticks unstarted");
        }
    }

    /// <summary>
    /// The same with packs of 6 (12 half-pop an item, above the AI's 4-half house margin): a report of whether the AI's house
    /// rule (it builds a house only within 2 pop of the cap) lets a pack that needs more room ever start.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void TwoAis_TrainingPacksOf6_For10000Ticks_Report(ulong seed)
    {
        SoakStats packs = Soak(6, seed, 10_000);
        output.WriteLine($"seed {seed} packs 6: {Show(packs)}");
        for (int p = 0; p < 2; p++)
            Assert.True(packs.LongestPackWait[p] < 3_000 || packs.FinalCap[p] >= Variant(6).Rules.HalfPopCap, $"player {p}: a pack of 6 waited {packs.LongestPackWait[p]} ticks unstarted ({Show(packs)})");
    }

    // ------------------------------------------------------------------ the 400-tick pack match, saved at every tick

    /// <summary>A small flat pack match: both sides train packs of 3 in 1.5 s (Heavy Infantry, Raiders), all bite, a brawl in the middle.</summary>
    private static readonly Lazy<GameData> s_quick = new(() => AiScenes.DataWith(dir =>
    {
        OnHitPassiveTests.BiteAll(dir);
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "3");
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "trainTime", "1.5");
        dir.SetUnitField("whirlwind", "whirlwind_raider", "spawnCount", "3");
        dir.SetUnitField("whirlwind", "whirlwind_raider", "trainTime", "2");
    }));

    private const int Size = 64;

    private static Simulation QuickMatch(GameData d, ulong seed)
    {
        var sim = new Simulation(new SimConfig(seed, 2, 400, 4096) { Data = d }, LocalMovementTests.Flat(Size));
        World w = sim.World;
        int keep = d.FindBuilding("malazan_garrison_keep"), hall = d.FindBuilding("whirlwind_holy_camp");
        for (int k = 0; k < 5; k++)
        {
            Assert.True(w.Buildings.Spawn(0, keep, (k < 4 ? 2 + 6 * k : 44) * Size + 3, out _));
            Assert.True(w.Buildings.Spawn(1, hall, (k < 4 ? 2 + 6 * k : 44) * Size + 57, out _));
        }
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), 30 * Size + 8, out _));
        Assert.True(w.Buildings.Spawn(1, d.FindBuilding("whirlwind_raider_camp"), 30 * Size + 53, out _));
        BuildMaps.SetTotals(sim, 0, 100_000, 100_000);
        BuildMaps.SetTotals(sim, 1, 100_000, 100_000);
        CombatScenes.MixedBrawl(sim, 12, new Vector2(64f, 80f), gap: 8f);
        return sim;
    }

    /// <summary>Between ticks: a pack queued on each side every 30 ticks (and cancelled at odd 90s), rallies to the middle, an attack-move every 100.</summary>
    private static void QuickScript(Simulation sim, List<Command> commands)
    {
        World w = sim.World;
        int t = sim.TickNumber;
        int barracks = -1, camp = -1;
        for (int k = 0; k < w.Buildings.Capacity; k++)
        {
            if (!w.Buildings.Alive[k]) continue;
            if (w.Buildings.TypeId[k] == w.Data.FindBuilding("malazan_barracks")) barracks = k;
            if (w.Buildings.TypeId[k] == w.Data.FindBuilding("whirlwind_raider_camp")) camp = k;
        }
        if (t == 1)
        {
            if (barracks >= 0) commands.Add(Command.SetRally(0, w.Buildings.Cell[barracks], CombatScenes.At(sim, 32, 32)));
            if (camp >= 0) commands.Add(Command.SetRally(1, w.Buildings.Cell[camp], CombatScenes.At(sim, 32, 32)));
        }
        if (t % 30 == 3)
        {
            if (barracks >= 0) commands.Add(Command.Train(0, ProductionMaps.In(sim, barracks), w.Data.FindUnit("malazan_heavy_infantry")));
            if (camp >= 0) commands.Add(Command.Train(1, ProductionMaps.In(sim, camp), w.Data.FindUnit("whirlwind_raider")));
        }
        if (t % 90 == 47 && barracks >= 0) commands.Add(Command.CancelTrain(0, ProductionMaps.In(sim, barracks), 0));
        if (t % 100 == 60)
            foreach (int i in ProductionMaps.LiveUnits(w))
                commands.Add(Command.AttackMove(w.Units.Owner[i], new EntityHandle(i, w.Units.Generation[i]), CombatScenes.At(sim, w.Units.Owner[i] == 0 ? 50 : 14, 32)));
    }

    /// <summary>
    /// A save at every tick 0-399 of the pack match: each loads to the same state, re-saves to the same bytes, and runs 60
    /// ticks hash-equal beside the reference (so most saves see a pack start, complete or wait, and bites land).
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void ASaveAtEveryTickOfA400TickPackMatch_ContinuesHashIdentical()
    {
        GameData d = s_quick.Value;
        int[] saveTicks = Enumerable.Range(0, 400).ToArray();
        int started = 0, slowed = 0;
        int slowId = d.FindStatus("slowed");
        var scene = new SaveScene("pack_match", () => QuickMatch(d, 5), (sim, commands) =>
        {
            QuickScript(sim, commands);
            World w = sim.World;
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k] && w.Buildings.Progress[k] > 0 && w.Buildings.QueueCount[k] > 0 && !w.Buildings.QueueIsTechAt(k, 0)
                    && d.Units[w.Buildings.QueueTypeAt(k, 0)].SpawnCount > 1) { started++; break; }
            for (int i = 0; i < w.Units.Capacity; i++)
                if (w.Units.Alive[i] && w.Units.Statuses.IndexOf(i, slowId) >= 0) { slowed++; break; }
        });
        var sw = Stopwatch.StartNew();
        SaveTestRun.Result r = SaveTestRun.RoundTrip(scene, saveTicks, after: 60, observe: sim =>
        {
            for (int p = 0; p < 2; p++)
                if (sim.World.HalfPop[p] != PackQaTests.Oracle(sim.World, p)) Assert.Fail($"tick {sim.TickNumber}: player {p} pop oracle");
        });
        output.WriteLine($"{r.TicksCompared} ticks compared in {sw.ElapsedMilliseconds} ms; script ticks with a pack started {started}, with a slow on {slowed}");
        Assert.Equal(400 * 60, r.TicksCompared);
        Assert.True(started > 100 && slowed > 100, $"weak scene: started {started}, slowed {slowed}");
    }

    // ------------------------------------------------------------------ determinism across processes

    /// <summary>
    /// 3,000 ticks of the pack match (packs on both sides, cancels, bites) twice in a process, hash-equal every tick; with
    /// <see cref="DigestEnv"/> set the digest is written there for a cross-process comparison.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void APackAndBiteMatch_HashesTheSame_TwiceInAProcess()
    {
        (ulong a, int units) = Digest();
        (ulong b, _) = Digest();
        output.WriteLine($"digest {a:X16}, units trained {units}");
        Assert.Equal(a, b);
        Assert.True(units >= 60, $"only {units} units trained");
        string? path = Environment.GetEnvironmentVariable(DigestEnv);
        if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, $"{a:X16} {units}");
    }

    private static (ulong, int) Digest()
    {
        GameData d = s_quick.Value;
        Simulation sim = QuickMatch(d, 7);
        var commands = new List<Command>();
        ulong digest = 14695981039346656037UL;
        for (int t = 0; t < 3_000; t++)
        {
            commands.Clear();
            QuickScript(sim, commands);
            foreach (Command c in commands) sim.Enqueue(c);
            sim.Tick();
            digest = (digest ^ sim.StateHash()) * 1099511628211UL;
        }
        return (digest, sim.World.Ledger.Stats.UnitsTrained[0] + sim.World.Ledger.Stats.UnitsTrained[1]);
    }

    // ------------------------------------------------------------------ allocation under 50 packs

    /// <summary>
    /// Ten Barracks with five 1.5 s packs each (50 packs, 150 units at pop 0.5 under a 100-pop cap) complete inside a 400-tick
    /// probe while a biting brawl runs: zero bytes allocated.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void FiftyPacksCompleting_WithBites_AllocateNothing()
    {
        GameData d = AiScenes.DataWith(dir =>
        {
            OnHitPassiveTests.BiteAll(dir);
            dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "3");
            dir.SetUnitField("malazan", "malazan_heavy_infantry", "trainTime", "1.5");
            dir.SetUnitField("malazan", "malazan_heavy_infantry", "pop", "0.5");
        });
        var sim = new Simulation(new SimConfig(13, 2, 600, 4128) { Data = d }, LocalMovementTests.Flat(96));
        World w = sim.World;
        int keep = d.FindBuilding("malazan_garrison_keep"), heavy = d.FindUnit("malazan_heavy_infantry");
        for (int k = 0; k < 10; k++) Assert.True(w.Buildings.Spawn(0, keep, (2 + 6 * (k % 5)) * 96 + 2 + 6 * (k / 5), out _)); // cap 100 pop
        var trainAt = new Vector2[10];
        for (int k = 0; k < 10; k++)
        {
            Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), (60 + 6 * (k % 5)) * 96 + 4 + 6 * (k / 5), out EntityHandle b));
            trainAt[k] = ProductionMaps.In(sim, b.Index);
        }
        Assert.True(w.Buildings.Spawn(1, d.FindBuilding("whirlwind_holy_camp"), 4 * 96 + 88, out _));
        CombatScenes.FlatBrawl(sim, 20, new Vector2(70f, 40f), gap: 4f, ranks: 5);
        BuildMaps.SetTotals(sim, 0, 1_000_000, 1_000_000);
        for (int k = 0; k < 10; k++) sim.Enqueue(Command.Train(0, trainAt[k], heavy));
        for (int k = 0; k < 60; k++) sim.Tick(); // warm: a pack per barracks
        int packs = 0;
        Action setup = () =>
        {
            for (int k = 0; k < 10; k++)
                for (int n = 0; n < 5; n++) sim.Enqueue(Command.Train(0, trainAt[k], heavy));
        };
        Action ticks = () =>
        {
            for (int t = 0; t < 400; t++)
            {
                int trained = w.Ledger.Stats.UnitsTrained[0];
                sim.Tick();
                packs += (w.Ledger.Stats.UnitsTrained[0] - trained) / 3;
                sim.StateHash();
            }
        };
        int runs = AllocationProbe.AssertZero(ticks, output, setup);
        output.WriteLine($"{packs} packs in {runs} run(s); pop {w.HalfPop[0]}/{w.HalfPopCap[0]} half");
        Assert.True(packs >= 50, $"only {packs} packs in {runs} run(s)");
    }
    // ------------------------------------------------------------------ scale: 2,500 biters

    /// <summary>
    /// 2,500 units (1,250 a side, mixed armies) brawl 300 ticks on the shipped data and on a copy where every unit bites: a
    /// report of ms per tick (wall clock, so noisy beside another track's run) and of slows applied; the biting run must stay
    /// within 3x the plain one (a loose guard against a per-hit cost blow-up, not a perf budget).
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void TwoThousandFiveHundredBiters_TickCost_Report()
    {
        GameData bite = AiScenes.DataWith(OnHitPassiveTests.BiteAll);
        (double plain, _) = Brawl(TestSim.Data);
        (double bitten, int slows) = Brawl(bite);
        output.WriteLine($"2,500 units, 300 ticks: shipped {plain:F2} ms/tick, all biting {bitten:F2} ms/tick ({bitten / plain:F2}x), {slows} unit-ticks slowed");
        Assert.True(slows > 1_000, $"only {slows} slowed unit-ticks");
        Assert.True(bitten < 3 * plain + 1, $"biting {bitten:F2} ms/tick vs plain {plain:F2}");
    }

    private static (double MsPerTick, int Slows) Brawl(GameData d)
    {
        var sim = new Simulation(new SimConfig(3, 2, 2_600, 8 * 2_600 + 32) { Data = d }, LocalMovementTests.Flat(160));
        CombatScenes.MixedBrawl(sim, 1_250, new Vector2(160f, 160f), gap: 8f);
        for (int k = 0; k < 60; k++) sim.Tick();
        int slowed = d.FindStatus("slowed"), slows = 0;
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < 300; k++)
        {
            sim.Tick();
            sw.Stop();
            UnitStore u = sim.World.Units;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.Statuses.IndexOf(i, slowed) >= 0) slows++;
            sw.Start();
        }
        return (sw.Elapsed.TotalMilliseconds / 300, slows);
    }
}
