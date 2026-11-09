using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M5-9a round 1's focus (full tier; round 1 had no QA pass): the counter formula's edges (a memory of one slot, a faded
/// total just above 0, the exact end of the memory, a seen slot nothing in the composition counters) and the behavioural
/// sanity row (a Hard seat that has watched 20 Horse Raiders for three minutes trains visibly more Line than one that saw none).
/// docs/03 "Implementation (M5-9a)": the share is the memory's composition, not its size.
/// </summary>
public class CounterMemoryQaTests
{
    private const int Tps = SimConstants.TicksPerSecond;
    private static readonly Vector2 Far = new(84f, 12f);
    private readonly ITestOutputHelper _out;

    public CounterMemoryQaTests(ITestOutputHelper output) => _out = output;

    private static Simulation Scene(AiDifficulty difficulty, GameData? data = null)
    {
        ulong seed = Rts.Sim.Tests.Content.AiOpeningSeeds.Smallest("malazan", 0, difficulty);
        PlayerSeat[] seats = { new(0, SeatController.Ai, difficulty), new(1, SeatController.Human, difficulty) };
        return AiScenes.Flat(seats, data, seed: seed);
    }

    /// <summary>The shares of player 0's next think, the memory forged before it to <paramref name="count"/> per slot at <paramref name="tick"/>.</summary>
    private static float[] SharesAfterForging(Simulation sim, int[] count, int[] tick)
    {
        AiPlayer ai = sim.AiOf(0)!;
        while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
        Array.Copy(count, ai.Board.SeenSlotCount, Blackboard.SlotCount);
        Array.Copy(tick, ai.Board.SeenSlotTick, Blackboard.SlotCount);
        float[]? shares = null;
        ai.ThoughtForTests = a => shares = (float[])a.CounterShare.Clone();
        sim.Tick();
        ai.ThoughtForTests = null;
        return shares!;
    }

    /// <summary>
    /// Edges: one remembered slot (Shock) gives Line, its only counter in <c>ai.counters</c>, the whole share (1) and every
    /// other slot 0; a sighting one tick from fading (a faded total just above 0) still gives exactly 1 (the share is the
    /// memory's composition), every share finite and in [0, 1]; at the exact end of the memory every share is 0 (nothing
    /// remembered, no division by zero). Two slots, Shock fresh and Ranged one tick from fading: Line's share is near 1, Shock's
    /// (Ranged's counter) near 0, the two summing to 1 since nothing else counters either.
    /// </summary>
    [Fact]
    public void TheShare_AtTheFormulasEdges_IsFiniteBoundedAndAsDocumented()
    {
        int memory = TestSim.Data.AiRules.CounterMemoryTicks, shock = (int)UnitSlot.Shock, ranged = (int)UnitSlot.Ranged, line = (int)UnitSlot.Line;
        Simulation sim = Scene(AiDifficulty.Normal);
        for (int t = 0; t < memory + 40; t++) sim.Tick(); // past one memory, so "now - memory + 1" is a valid tick
        var count = new int[Blackboard.SlotCount];
        var tick = new int[Blackboard.SlotCount];
        int now = sim.TickNumber + (sim.AiOf(0)!.ThinkTicks - sim.TickNumber % sim.AiOf(0)!.ThinkTicks) % sim.AiOf(0)!.ThinkTicks;

        count[shock] = 5;
        tick[shock] = now;
        float[] fresh = SharesAfterForging(sim, count, tick);
        _out.WriteLine($"one fresh slot: [{string.Join(", ", fresh)}]");
        Assert.Equal(1f, fresh[line]);
        for (int s = 0; s < Blackboard.SlotCount; s++)
            if (s != line && !TestSim.Data.AiRules.Counters((UnitSlot)s, UnitSlot.Shock)) Assert.Equal(0f, fresh[s]);

        now = sim.TickNumber + (sim.AiOf(0)!.ThinkTicks - sim.TickNumber % sim.AiOf(0)!.ThinkTicks) % sim.AiOf(0)!.ThinkTicks;
        tick[shock] = now - memory + 1; // one tick from faded: 5 / memory left
        float[] fading = SharesAfterForging(sim, count, tick);
        _out.WriteLine($"one slot one tick from faded: [{string.Join(", ", fading)}]");
        Assert.Equal(1f, fading[line]);
        foreach (float f in fading) Assert.True(float.IsFinite(f) && f >= 0f && f <= 1f, $"share {f}");

        now = sim.TickNumber + (sim.AiOf(0)!.ThinkTicks - sim.TickNumber % sim.AiOf(0)!.ThinkTicks) % sim.AiOf(0)!.ThinkTicks;
        tick[shock] = now - memory; // exactly faded
        float[] gone = SharesAfterForging(sim, count, tick);
        _out.WriteLine($"exactly faded: [{string.Join(", ", gone)}]");
        foreach (float f in gone) Assert.Equal(0f, f);

        now = sim.TickNumber + (sim.AiOf(0)!.ThinkTicks - sim.TickNumber % sim.AiOf(0)!.ThinkTicks) % sim.AiOf(0)!.ThinkTicks;
        tick[shock] = now;
        count[ranged] = 6;
        tick[ranged] = now - memory + 1;
        float[] two = SharesAfterForging(sim, count, tick);
        _out.WriteLine($"Shock fresh + Ranged fading: [{string.Join(", ", two)}]");
        Assert.InRange(two[line], 0.99f, 1f);
        Assert.InRange(two[shock], 0f, 0.01f);
        foreach (float f in two) Assert.True(float.IsFinite(f) && f >= 0f && f <= 1f, $"share {f}");
    }

    /// <summary>
    /// Edge: a seen slot that no slot of the composition counters (Unique: <c>ai.counters.unique</c> is empty) shifts nothing:
    /// every share is 0, so the seat's training is the unshifted one (the same command log as a seat that saw nothing, over
    /// three minutes).
    /// </summary>
    [Fact]
    public void ASeenSlotWithNoCounter_ShiftsNothing()
    {
        Assert.DoesNotContain(Enumerable.Range(0, Blackboard.SlotCount), s => TestSim.Data.AiRules.Counters((UnitSlot)s, UnitSlot.Unique));
        var logs = new List<Rts.Sim.Commands.Command>[2];
        for (int run = 0; run < 2; run++)
        {
            Simulation sim = Scene(AiDifficulty.Hard);
            logs[run] = AiScenes.Log(sim, 0);
            AiPlayer ai = sim.AiOf(0)!;
            bool anyShare = false;
            ai.ThoughtForTests = a =>
            {
                if (run == 0)
                {
                    a.Board.SeenSlotCount[(int)UnitSlot.Unique] = 20;
                    a.Board.SeenSlotTick[(int)UnitSlot.Unique] = a.Now;
                }
                foreach (float f in a.CounterShare) anyShare |= f != 0f;
            };
            while (sim.TickNumber < 6000) sim.Tick();
            Assert.False(anyShare);
        }
        Assert.Equal(logs[1].Count, logs[0].Count);
        for (int i = 0; i < logs[0].Count; i++) Assert.True(logs[0][i].Equals(logs[1][i]), $"command {i} differs");
    }

    /// <summary>
    /// The behavioural sanity row (round 1's QA focus): a Hard Malazan seat that keeps seeing 20 Horse Raiders (Whirlwind
    /// Shock, revealed for every think for three minutes, out of reach of its base) trains visibly more Line (Heavy Infantry)
    /// than the same seat and seed that never saw them: at least 3 more, and a larger Line share of its Line + Ranged.
    /// Fails (BUG-1156): the memory holds 20 Shock and Line's share is 1 on every think, but the barracks takes a Heavy
    /// Infantry whenever its queue empties in both runs (one Train a think, empty queues only: 10 orders in the 3 minutes
    /// each), so the shift only decides the thinks both producers are free and drops one Crossbowman.
    /// </summary>
    [Fact(Skip = "BUG-1156 (open): the barracks takes a Heavy Infantry whenever free either way: seen Line 11 / Ranged 8, unseen 11 / 9")]
    [Trait("Category", "Stress")]
    public void AHardSeat_ThatWatched20HorseRaidersForThreeMinutes_TrainsVisiblyMoreLine()
    {
        (int line, int ranged) seen = Run(true), unseen = Run(false);
        _out.WriteLine($"seen: line {seen.line}, ranged {seen.ranged}; unseen: line {unseen.line}, ranged {unseen.ranged}");
        Assert.True(seen.line >= unseen.line + 3, $"line {seen.line} vs {unseen.line}");
        Assert.True((float)seen.line / Math.Max(1, seen.line + seen.ranged) > (float)unseen.line / Math.Max(1, unseen.line + unseen.ranged));
    }

    private (int Line, int Ranged) Run(bool seen)
    {
        Simulation sim = Scene(AiDifficulty.Hard);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        int barracks = w.Data.FindBuilding("malazan_barracks"), range = w.Data.FindBuilding("malazan_crossbow_range");
        bool Finished(int type)
        {
            for (int i = 0; i < w.Buildings.Capacity; i++)
                if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 0 && w.Buildings.TypeId[i] == type && !w.Buildings.UnderConstruction[i]) return true;
            return false;
        }
        while (!(Finished(barracks) && Finished(range)))
        {
            sim.Tick();
            Assert.True(sim.TickNumber < 9000, "no barracks and crossbow range by 7:30");
        }
        int raider = w.Data.FindUnit("whirlwind_horse_raider");
        var raiders = new List<EntityHandle>();
        for (int k = 0; k < 20; k++) raiders.Add(CombatScenes.Place(sim, 1, raider, Far + new Vector2(1.2f * (k % 5), 1.2f * (k / 5))));
        int end = sim.TickNumber + 180 * Tps;
        int heavy = w.Data.FindUnit("malazan_heavy_infantry"), xbow = w.Data.FindUnit("malazan_crossbowman");
        float lineShare = 0f;
        int trainsLine = 0, trainsRanged = 0, thinks = 0;
        List<Rts.Sim.Commands.Command> log = AiScenes.Log(sim, 0);
        int logged = log.Count;
        ai.ThoughtForTests = a =>
        {
            lineShare = Math.Max(lineShare, a.CounterShare[(int)UnitSlot.Line]);
            thinks++;
        };
        while (sim.TickNumber < end)
        {
            if (seen)
                foreach (EntityHandle h in raiders)
                    if (w.Units.IsAlive(h)) w.Fog.Reveal(h, 0, sim.TickNumber + ai.ThinkTicks);
            // Keep the raiders where they stand (a human seat sends nothing; they don't wander), and out of the fight.
            sim.Tick();
        }
        for (int i = logged; i < log.Count; i++)
            if (log[i].Kind == Rts.Sim.Commands.CommandKind.Train) { if (log[i].TypeId == heavy) trainsLine++; else if (log[i].TypeId == xbow) trainsRanged++; }
        _out.WriteLine($"seen {seen}: memory [{string.Join(",", ai.Board.SeenSlotCount)}], max Line share {lineShare}, {thinks} thinks; "
            + $"Train orders in the 3 min: Heavy Infantry {trainsLine}, Crossbowmen {trainsRanged}; gold {ai.Gold}, wood {ai.Wood}");
        ai.ThoughtForTests = null;
        return (ai.Live[heavy] + ai.Queued[heavy], ai.Live[xbow] + ai.Queued[xbow]);
    }
}
