using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-9a round 2 (BUG-1153, BUG-1068 candidate (b); docs/03 "Implementation (M5-9a)"): a seat that can pay for no unit of
/// its composition, knows no node of the short kind it may gather, has no worker on that kind and has at least
/// <c>ai.starvedMinArmy</c> free army units is <b>starved</b>; once it has been starved for <c>ai.starvedWaveAfter</c> running
/// it waves (or hunts) with every free army unit, whatever its opening's threshold. Knob 0 turns the rule off.
/// </summary>
public class StarvedWaveTests
{
    private readonly ITestOutputHelper _out;

    public StarvedWaveTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Malazan openings whose build order sends nothing for an hour and that train Heavy Infantry (54 gold) / Crossbowmen
    /// (36 gold) in both ages; <paramref name="after"/> for <c>ai.starvedWaveAfter</c> when given (the shipped 60 s otherwise).
    /// </summary>
    private static GameData StarvedData(double? after = null) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "build", "malazan_billet", 0));
                o["compositions"] = new JsonArray(
                    new JsonObject { ["age"] = 1, ["units"] = new JsonObject { ["malazan_heavy_infantry"] = 35, ["malazan_crossbowman"] = 30 } },
                    new JsonObject { ["age"] = 2, ["units"] = new JsonObject { ["malazan_heavy_infantry"] = 35, ["malazan_crossbowman"] = 30 } });
            }
        });
        if (after != null) dir.EditJson("common/rules.json", r => r["ai"]!["starvedWaveAfter"] = after.Value);
    });

    private static readonly Lazy<GameData> Shipped = new(() => StarvedData());

    /// <summary>
    /// The starved scene: a Malazan AI seat (player 0) at <paramref name="difficulty"/> on the flat map with <b>every gold mine
    /// emptied</b> before tick 0 (so it knows none and no worker can be at gold), 0 gold and 100 wood (every composition unit
    /// short of gold only), three Heavy Infantry by its hall (3 pop: under every difficulty's wave threshold) and a Whirlwind
    /// human seat that sends nothing, with a tent beside the army (a known enemy building to wave on).
    /// </summary>
    private static Simulation Scene(AiDifficulty difficulty, GameData data)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, difficulty), new(1, SeatController.Human, difficulty) };
        Simulation sim = AiScenes.Flat(seats, data, explored: false);
        World w = sim.World;
        for (int i = 0; i < w.Resources.Capacity; i++)
            if (w.Resources.Alive[i] && data.Resources[w.Resources.TypeId[i]].Resource == ResourceKind.Gold)
                w.Resources.Take(w.Resources.HandleOf(i), int.MaxValue);
        w.Ledger.Gold[0] = 0;
        w.Ledger.Wood[0] = 100;
        int heavy = data.FindUnit("malazan_heavy_infantry");
        for (int k = 0; k < 3; k++) CombatScenes.Place(sim, 0, heavy, w.NavGrid.CellCenter(15 + k, 27));
        Assert.True(w.Buildings.Spawn(1, data.FindBuilding("whirlwind_tent"), 27 * AiScenes.FlatSize + 20, out _));
        return sim;
    }

    /// <summary>What a run saw on player 0's thinks: the first stamp, whether it was ever cleared after, and the first think a wave was started at.</summary>
    private sealed class Watch
    {
        public int Stamp = -1, WaveAt = -1, ClearedAt = -1, DefendedAt = -1;
        public float WaveMinHalfPop = -1, WaveMinCost = -1;

        public void Attach(AiPlayer ai) => ai.ThoughtForTests = a =>
        {
            int s = a.Board.StarvedSince;
            if (Stamp < 0 && s >= 0) Stamp = s;
            if (Stamp >= 0 && s < 0 && ClearedAt < 0) ClearedAt = a.Now;
            if (a.Board.Defending && DefendedAt < 0) DefendedAt = a.Now;
            if (WaveAt < 0 && a.Board.WavesStarted > 0)
            {
                WaveAt = a.Now;
                WaveMinHalfPop = a.Board.WaveMinHalfPop;
                WaveMinCost = a.Board.WaveMinCost;
            }
        };
    }

    /// <summary>The first think tick at or after <paramref name="tick"/> of a seat thinking every <paramref name="think"/> ticks.</summary>
    private static int FirstThinkFrom(int tick, int think) => (tick + think - 1) / think * think;

    /// <summary>
    /// Criterion 5 (QA focus (a)): at every difficulty the scripted starved seat is stamped on its first think, stays stamped,
    /// and starts a wave with its three Heavy Infantry (no threshold kept) on exactly the first think at or after the stamp +
    /// <c>ai.starvedWaveAfter</c>, not one think earlier. With the knob at 0 the same scene is never stamped and never waves.
    /// A window of 60.5 s (1,210 ticks, no think's multiple) waves on the think after it: 1,220 on Normal, 1,240 on Easy.
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Easy, 60.0)]
    [InlineData(AiDifficulty.Normal, 60.0)]
    [InlineData(AiDifficulty.Hard, 60.0)]
    [InlineData(AiDifficulty.Easy, 60.5)]
    [InlineData(AiDifficulty.Normal, 60.5)]
    [InlineData(AiDifficulty.Hard, 60.5)]
    public void AStarvedSeat_WavesAfterTheWindow_AndNotBefore(AiDifficulty difficulty, double seconds)
    {
        Simulation sim = Scene(difficulty, seconds == 60.0 ? Shipped.Value : HalfSecond.Value);
        AiPlayer ai = sim.AiOf(0)!;
        int window = ai.Rules.StarvedWaveAfterTicks;
        Assert.Equal((int)(seconds * SimConstants.TicksPerSecond), window);
        var watch = new Watch();
        watch.Attach(ai);
        while (sim.TickNumber < 3 * window && watch.WaveAt < 0) sim.Tick();
        _out.WriteLine($"{difficulty}: think {ai.ThinkTicks}, stamp {watch.Stamp}, wave at {watch.WaveAt}, cleared {watch.ClearedAt}, defended {watch.DefendedAt}");
        Assert.Equal(0, watch.Stamp); // the first think: tick 0
        Assert.Equal(-1, watch.ClearedAt);
        Assert.Equal(-1, watch.DefendedAt);
        Assert.Equal(FirstThinkFrom(watch.Stamp + window, ai.ThinkTicks), watch.WaveAt);
        Assert.Equal((0f, 0f), (watch.WaveMinHalfPop, watch.WaveMinCost)); // no threshold kept: the defense may pull any member
        Assert.Equal(1, ai.Board.WavesStarted);
        int members = 0;
        for (int k = 0; k < ai.ArmyCount; k++)
            if (MilitaryManager.Member(ai, ai.ArmySlots[k], out _)) members++;
        Assert.Equal(3, members);

        // Knob 0: the rule is off. Same scene, never stamped, never a wave.
        Simulation off = Scene(difficulty, Off.Value);
        AiPlayer offAi = off.AiOf(0)!;
        Assert.Equal(0, offAi.Rules.StarvedWaveAfterTicks);
        for (int t = 0; t < 2 * window; t++)
        {
            off.Tick();
            Assert.Equal(-1, offAi.Board.StarvedSince);
        }
        Assert.Equal(0, offAi.Board.WavesStarted);
        Assert.Equal(0, offAi.Board.HuntsStarted);
    }

    private static readonly Lazy<GameData> Off = new(() => StarvedData(after: 0));

    private static readonly Lazy<GameData> HalfSecond = new(() => StarvedData(after: 60.5));

    /// <summary>
    /// Criterion 5 (QA focus (b)): a stamped seat that sees a gold mine in reach (a node of the short kind it may gather) is
    /// cleared on the next thinks and never waves at the old window; one whose bank is refilled (it can pay again) is cleared
    /// too, and stamped afresh when it is starved again, waving a window after the new stamp, not the old.
    /// </summary>
    [Fact]
    public void AStarvedSeat_ThatRegainsIncome_ClearsTheStamp()
    {
        GameData data = Shipped.Value;
        // A gold mine appears by the hall at 30 s.
        Simulation sim = Scene(AiDifficulty.Normal, data);
        AiPlayer ai = sim.AiOf(0)!;
        int window = ai.Rules.StarvedWaveAfterTicks, think = ai.ThinkTicks;
        var watch = new Watch();
        watch.Attach(ai);
        while (sim.TickNumber < window / 2) sim.Tick();
        Assert.Equal(0, ai.Board.StarvedSince);
        (int hx, int hy) = AiScenes.HallAt(0);
        Assert.True(sim.World.Resources.Spawn(data.FindResource("gold_mine"), (hy - 6) * AiScenes.FlatSize + hx + 2, data.Rules.StartMineGold, out _));
        while (sim.TickNumber < 3 * window) sim.Tick();
        _out.WriteLine($"mine: stamp {watch.Stamp}, cleared at {watch.ClearedAt}, wave at {watch.WaveAt}, gold {sim.World.Gold[0]}");
        Assert.InRange(watch.ClearedAt, window / 2, window / 2 + 3 * think); // seen within a few thinks of the mine
        Assert.Equal(-1, ai.Board.StarvedSince);
        Assert.Equal(0, ai.Board.WavesStarted);

        // The bank refilled at 30 s (it can pay for a Heavy Infantry), emptied again at 40 s: cleared, then a fresh stamp.
        Simulation bank = Scene(AiDifficulty.Normal, data);
        AiPlayer b = bank.AiOf(0)!;
        var seen = new Watch();
        seen.Attach(b);
        while (bank.TickNumber < window / 2) bank.Tick();
        bank.World.Ledger.Gold[0] = 1000;
        while (bank.TickNumber < window / 2 + 10 * think) bank.Tick();
        Assert.Equal(FirstThinkFrom(window / 2, think), seen.ClearedAt);
        Assert.Equal(-1, b.Board.StarvedSince);
        bank.World.Ledger.Gold[0] = 0;
        int emptied = bank.TickNumber;
        bank.Tick();
        while (b.Board.StarvedSince < 0) bank.Tick();
        int restamp = b.Board.StarvedSince;
        Assert.Equal(FirstThinkFrom(emptied, think), restamp);
        while (bank.TickNumber < restamp + 2 * window && seen.WaveAt < 0) bank.Tick();
        _out.WriteLine($"bank: stamp {seen.Stamp}, cleared {seen.ClearedAt}, restamp {restamp}, wave at {seen.WaveAt}");
        Assert.Equal(FirstThinkFrom(restamp + window, think), seen.WaveAt);
    }

    /// <summary>
    /// QA focus (c), the <c>Defending</c> guard: a stamped seat whose base is raided (a Whirlwind Raider by its hall at 30 s) is
    /// cleared on the first think its census sees the defense, is never stamped on a think that starts defended (its known
    /// nodes by the defense point are hidden then, which is no starvation), and so does not wave at the old window.
    /// </summary>
    [Fact]
    public void AStarvedSeat_UnderARaid_IsClearedAndNotStampedWhileDefended()
    {
        Simulation sim = Scene(AiDifficulty.Normal, Shipped.Value);
        AiPlayer ai = sim.AiOf(0)!;
        int window = ai.Rules.StarvedWaveAfterTicks;
        bool defendedBefore = false;
        int defendedThinks = 0, stampedWhileDefended = 0, firstDefended = -1, clearedAt = -1;
        ai.ThoughtForTests = a =>
        {
            if (defendedBefore)
            {
                defendedThinks++;
                if (a.Board.StarvedSince >= 0) stampedWhileDefended++;
                if (clearedAt < 0 && a.Board.StarvedSince < 0) clearedAt = a.Now;
            }
            if (a.Board.Defending && firstDefended < 0) firstDefended = a.Now;
            defendedBefore = a.Board.Defending;
        };
        while (sim.TickNumber < window / 2) sim.Tick();
        Assert.Equal(0, ai.Board.StarvedSince);
        (int hx, int hy) = AiScenes.HallAt(0);
        CombatScenes.Place(sim, 1, sim.World.Data.FindUnit("whirlwind_raider"), sim.World.NavGrid.CellCenter(hx + 2, hy + 6));
        while (sim.TickNumber < window + 2 * ai.ThinkTicks) sim.Tick();
        _out.WriteLine($"defended from {firstDefended}, {defendedThinks} thinks defended, cleared at {clearedAt}, stamped while defended {stampedWhileDefended}, waves {ai.Board.WavesStarted}");
        Assert.InRange(firstDefended, window / 2, window / 2 + 2 * ai.ThinkTicks);
        Assert.True(defendedThinks > 0);
        Assert.Equal(firstDefended + ai.ThinkTicks, clearedAt);
        Assert.Equal(0, stampedWhileDefended);
        Assert.Equal(0, ai.Board.WavesStarted);
    }

    /// <summary>
    /// Criterion 5 (QA focus (g)): a seat saved mid-stamp (half the window gone) loads with the same stamp, re-saves byte for
    /// byte, and follows the run hash for hash to the same wave tick (the first think at or after the window), the seats equal
    /// after it.
    /// </summary>
    [Fact]
    public void ASeatMidStamp_SavedAndLoaded_WavesAtTheSameTick()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(AiDifficulty.Normal, data);
        AiPlayer ai = sim.AiOf(0)!;
        int window = ai.Rules.StarvedWaveAfterTicks;
        while (sim.TickNumber < window / 2 + 7) sim.Tick(); // between thinks
        Assert.Equal(0, ai.Board.StarvedSince);
        Assert.Equal(0, ai.Board.WavesStarted);
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation load = SaveTestRun.Load(data, bytes);
        AiPlayer loaded = load.AiOf(0)!;
        Assert.Equal(0, loaded.Board.StarvedSince);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        SaveTestRun.AssertSameState(sim, load, "mid-stamp");
        var a = new Watch();
        var b = new Watch();
        a.Attach(ai);
        b.Attach(loaded);
        while (sim.TickNumber < 2 * window)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
        _out.WriteLine($"wave at {a.WaveAt} (run) and {b.WaveAt} (loaded)");
        Assert.Equal(FirstThinkFrom(window, ai.ThinkTicks), a.WaveAt);
        Assert.Equal(a.WaveAt, b.WaveAt);
        SaveTestRun.AssertSameState(sim, load, "after the wave");
    }

    /// <summary>
    /// Criterion 4, the knob-0 twin on a whole Normal v Normal match: Vathar Crossing seed 1 (the literal M5-5 row) played with
    /// <c>ai.starvedWaveAfter</c> 0 sends exactly the commands it sends with the shipped knob, where no seat's stamp ever
    /// comes due before the end; with the knob at 0 no seat is ever stamped.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void AWholeNormalMatch_WithTheKnobAtZero_SendsTheSameCommands_AsShipped()
    {
        GameData off = AiScenes.DataWith(dir => dir.EditJson("common/rules.json", r => r["ai"]!["starvedWaveAfter"] = 0));
        var (shippedLogs, shippedEnd, due) = Whole(TestSim.Data);
        var (offLogs, offEnd, offStamped) = Whole(off);
        _out.WriteLine($"shipped ends {shippedEnd} (a stamp due: {due}), off ends {offEnd} (a stamp ever: {offStamped})");
        Assert.False(due);
        Assert.False(offStamped);
        Assert.Equal(shippedEnd, offEnd);
        for (int p = 0; p < 2; p++)
        {
            Assert.True(shippedLogs[p].Count == offLogs[p].Count, $"player {p}: {shippedLogs[p].Count} commands vs {offLogs[p].Count}");
            for (int i = 0; i < shippedLogs[p].Count; i++)
                Assert.True(shippedLogs[p][i].Equals(offLogs[p][i]), $"player {p}: command {i} differs");
        }
    }

    /// <summary>
    /// BUG-1154 (the developer's finding: "can pay for a unit" ignored whether production would train it): production waits
    /// for its most under-weighted unit rather than buy a cheaper one, so the check reads that unit. The starved scene plus a
    /// finished Barracks and Crossbow Range, three Crossbowmen beside the three Heavy Infantry (so Heavy Infantry is the pick)
    /// and the bank held at 40 gold: a Crossbowman (36 g) is payable but never trained, a Heavy Infantry (54 g) never payable
    /// (no gold ever again). Starved on the first think, waves at the window. The twin without the Barracks (the pick is then
    /// the payable Crossbowman, which production trains) is never stamped.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASeat_WhosePickIsNeverPayable_IsStarved_ThoughACheaperUnitIsPayable(bool barracks)
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(AiDifficulty.Normal, data);
        World w = sim.World;
        if (barracks) Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_barracks"), 44 * AiScenes.FlatSize + 8, out _));
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_crossbow_range"), 44 * AiScenes.FlatSize + 18, out _));
        int bow = data.FindUnit("malazan_crossbowman"), heavy = data.FindUnit("malazan_heavy_infantry");
        for (int k = 0; k < 3; k++) CombatScenes.Place(sim, 0, bow, w.NavGrid.CellCenter(15 + k, 29));
        AiPlayer ai = sim.AiOf(0)!;
        int window = ai.Rules.StarvedWaveAfterTicks;
        var watch = new Watch();
        watch.Attach(ai);
        // The pick at the first think's end (the queues are still empty in the starved case: nothing was bought).
        int pick = -2, trained = 0;
        Action<AiPlayer> inner = ai.ThoughtForTests!;
        ai.ThoughtForTests = a => { inner(a); if (pick == -2) pick = ProductionManager.Pick(a); };
        while (sim.TickNumber < window + 2 * ai.ThinkTicks)
        {
            w.Ledger.Gold[0] = 40;
            sim.Tick();
            if (w.Ledger.Gold[0] < 40) trained++;
        }
        _out.WriteLine($"barracks {barracks}: first pick {pick}, stamp {watch.Stamp}, cleared {watch.ClearedAt}, wave at {watch.WaveAt}, ticks the bank dropped {trained}");
        if (barracks)
        {
            Assert.Equal(heavy, pick);
            Assert.Equal(0, watch.Stamp);
            Assert.Equal(-1, watch.ClearedAt);
            Assert.Equal(FirstThinkFrom(window, ai.ThinkTicks), watch.WaveAt);
            Assert.Equal(0, trained); // it never bought the payable Crossbowman: why it is starved
        }
        else
        {
            Assert.Equal(-1, watch.Stamp);
            Assert.Equal(-1, watch.WaveAt);
            Assert.True(trained > 0, "the payable pick (a Crossbowman) was never trained");
        }
    }

    /// <summary>Vathar Crossing seed 1, Normal v Normal, to its end or 36,000: both seats' accepted commands, the end tick, and whether any seat had a stamp at least the window old (with the knob at 0: any stamp at all).</summary>
    private static (List<Command>[] Logs, int End, bool Flag) Whole(GameData data)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        int size = data.Maps[data.FindMap("vathar_crossing")].Size;
        Simulation sim = AiScenes.Generated(seats, 1, false, out _, data: data,
            configure: c => c with { MapId = "vathar_crossing", Map = c.Map with { Width = size, Height = size } });
        var logs = new[] { AiScenes.Log(sim, 0), AiScenes.Log(sim, 1) };
        bool flag = false;
        while (sim.TickNumber < 36_000 && !sim.MatchOver)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                AiPlayer ai = sim.AiOf(p)!;
                int s = ai.Board.StarvedSince;
                if (ai.Rules.StarvedWaveAfterTicks == 0 ? s != -1 : s >= 0 && sim.TickNumber - s >= ai.Rules.StarvedWaveAfterTicks) flag = true;
            }
        }
        Assert.True(sim.MatchOver, "the match did not end");
        return (logs, sim.TickNumber, flag);
    }
}
