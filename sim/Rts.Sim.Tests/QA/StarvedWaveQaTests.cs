using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M5-9a round 2 (BUG-1153, the starved-side wave; docs/03 "Implementation (M5-9a)"): attacks on the rule the developer's
/// <c>StarvedWaveTests</c> do not cover: (h) a starved wave that retreats charges again on the first think after the cooldown
/// and the loop ends when the army is gone; a starved seat with no known enemy building hunts with no wave sent before;
/// <c>ai.starvedMinArmy</c> at its boundary; wood starvation (gold banked, no forest known); a mixed shortage with one kind
/// still gatherable is not starvation; and the think path of a starved seat allocates nothing.
/// </summary>
[Collection(SerialCollection.Name)]
public class StarvedWaveQaTests
{
    private readonly ITestOutputHelper _out;

    public StarvedWaveQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>Malazan openings that build nothing for an hour and train Heavy Infantry (54 g / 20 w) and Crossbowmen (36 g / 45 w) in both ages; rules edited by <paramref name="rules"/>.</summary>
    private static GameData Data(Action<JsonNode>? rules = null) => AiScenes.DataWith(dir =>
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
        if (rules != null) dir.EditJson("common/rules.json", rules);
    });

    private static readonly Lazy<GameData> Shipped = new(() => Data());

    private static void EmptyAll(World w, ResourceKind kind)
    {
        for (int i = 0; i < w.Resources.Capacity; i++)
            if (w.Resources.Alive[i] && w.Data.Resources[w.Resources.TypeId[i]].Resource == kind)
                w.Resources.Take(w.Resources.HandleOf(i), int.MaxValue);
    }

    /// <summary>
    /// A Malazan AI seat (0) on the flat map, fog on, with every node of <paramref name="emptied"/> emptied before tick 0,
    /// <paramref name="gold"/> / <paramref name="wood"/> banked, <paramref name="army"/> Heavy Infantry by its hall; a Whirlwind
    /// human seat (1) that sends nothing, with a tent beside the army when <paramref name="tent"/> (else its hall is removed
    /// and one Raider stands in the far corner, so seat 0 knows no enemy building).
    /// </summary>
    private static (Simulation Sim, List<EntityHandle> Army) Scene(AiDifficulty difficulty, GameData data, ResourceKind emptied, int gold, int wood,
        int army = 3, bool tent = true)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, difficulty), new(1, SeatController.Human, difficulty) };
        Simulation sim = AiScenes.Flat(seats, data, explored: false);
        World w = sim.World;
        EmptyAll(w, emptied);
        w.Ledger.Gold[0] = gold;
        w.Ledger.Wood[0] = wood;
        int heavy = data.FindUnit("malazan_heavy_infantry");
        var list = new List<EntityHandle>();
        for (int k = 0; k < army; k++) list.Add(CombatScenes.Place(sim, 0, heavy, w.NavGrid.CellCenter(15 + k % 5, 27 - k / 5)));
        if (tent) Assert.True(w.Buildings.Spawn(1, data.FindBuilding("whirlwind_tent"), 27 * AiScenes.FlatSize + 20, out _));
        else
        {
            for (int i = 0; i < w.Buildings.Capacity; i++)
                if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 1) w.Buildings.Free(w.Buildings.HandleOf(i));
            CombatScenes.Place(sim, 1, data.FindUnit("whirlwind_raider"), w.NavGrid.CellCenter(61, 61));
        }
        return (sim, list);
    }

    private static int FirstThinkFrom(int tick, int think) => (tick + think - 1) / think * think;

    /// <summary>
    /// QA focus (h): the starved wave of three Heavy Infantry starts on the first think at the window; two members are killed
    /// right after, so the wave retreats (live half-pop under (1 - lossFraction) of the start's) and the cooldown starts; the
    /// survivor, still starved, waves again on exactly the first think at or after the cooldown's end (the loop does not sit);
    /// when the last unit dies the seat is no longer starved ((iii): no free army), the stamp clears and no wave follows.
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void AStarvedWave_ThatRetreats_ChargesAgainAfterTheCooldown_AndStopsWhenTheArmyIsGone(AiDifficulty difficulty)
    {
        (Simulation sim, List<EntityHandle> army) = Scene(difficulty, Shipped.Value, ResourceKind.Gold, 0, 100);
        AiPlayer ai = sim.AiOf(0)!;
        int window = ai.Rules.StarvedWaveAfterTicks, think = ai.ThinkTicks, cooldown = ai.Rules.WaveCooldownTicks;
        var waveAt = new List<int>();
        int endedAt = -1, prevWaves = 0;
        bool wasActive = false;
        ai.ThoughtForTests = a =>
        {
            if (a.Board.WavesStarted + a.Board.HuntsStarted > prevWaves) waveAt.Add(a.Now);
            prevWaves = a.Board.WavesStarted + a.Board.HuntsStarted;
            if (wasActive && !a.Board.WaveActive && endedAt < 0) endedAt = a.Now;
            wasActive = a.Board.WaveActive;
        };
        while (waveAt.Count == 0 && sim.TickNumber < 3 * window) sim.Tick();
        Assert.Equal(new[] { FirstThinkFrom(window, think) }, waveAt);
        sim.World.Units.Free(army[0]);
        sim.World.Units.Free(army[1]);
        int killed = sim.TickNumber;
        while (endedAt < 0 && sim.TickNumber < killed + 10 * think) sim.Tick();
        Assert.True(endedAt >= 0, "the wave did not retreat after losing two of three members");
        int until = ai.Board.WaveCooldownUntil;
        Assert.Equal(endedAt + cooldown, until);
        Assert.True(ai.Board.StarvedSince >= 0, "the survivor's seat lost its stamp on the retreat");
        while (waveAt.Count < 2 && sim.TickNumber < until + 10 * think) sim.Tick();
        _out.WriteLine($"{difficulty}: think {think}, window {window}, cooldown {cooldown}; waves at {string.Join(", ", waveAt)}; retreat at {endedAt}, cooldown until {until}");
        Assert.Equal(2, waveAt.Count);
        Assert.Equal(FirstThinkFrom(until, think), waveAt[1]);
        Assert.Equal((0f, 0f), (ai.Board.WaveMinHalfPop, ai.Board.WaveMinCost));

        // The last unit gone: no free army, so not starved; the stamp clears and nothing more starts.
        sim.World.Units.Free(army[2]);
        int last = sim.TickNumber;
        while (sim.TickNumber < last + cooldown + 4 * think) sim.Tick();
        Assert.Equal(-1, ai.Board.StarvedSince);
        Assert.Equal(2, waveAt.Count);
    }

    /// <summary>
    /// Criterion 2 (scope 2, <c>TryHunt</c>): a starved seat that knows no enemy building (the enemy's hall removed, one Raider in
    /// the far corner) and has never sent a wave starts a hunt on the first think at or after the window, not before; with the
    /// knob at 0 it never hunts (M5-4d's rule: no hunt before a wave).
    /// </summary>
    [Fact]
    public void AStarvedSeat_KnowingNoEnemyBuilding_HuntsWithNoWaveSentBefore()
    {
        (Simulation sim, _) = Scene(AiDifficulty.Normal, Shipped.Value, ResourceKind.Gold, 0, 100, tent: false);
        AiPlayer ai = sim.AiOf(0)!;
        int window = ai.Rules.StarvedWaveAfterTicks, think = ai.ThinkTicks;
        int huntAt = -1, knownAtHunt = -1;
        ai.ThoughtForTests = a =>
        {
            if (huntAt < 0 && a.Board.HuntsStarted > 0)
            {
                huntAt = a.Now;
                knownAtHunt = a.Board.KnownEnemyBuildings;
            }
        };
        while (huntAt < 0 && sim.TickNumber < 3 * window) sim.Tick();
        _out.WriteLine($"stamp {ai.Board.StarvedSince}, hunt at {huntAt}, known enemy buildings {knownAtHunt}, waves {ai.Board.WavesStarted}, over {sim.MatchOver}");
        Assert.False(sim.MatchOver);
        Assert.Equal(0, ai.Board.StarvedSince);
        Assert.Equal(FirstThinkFrom(window, think), huntAt);
        Assert.Equal(0, knownAtHunt);
        Assert.Equal(0, ai.Board.WavesStarted);
        Assert.True(ai.Board.Hunting);

        (Simulation off, _) = Scene(AiDifficulty.Normal, Off.Value, ResourceKind.Gold, 0, 100, tent: false);
        while (off.TickNumber < 3 * window) off.Tick();
        Assert.Equal(0, off.AiOf(0)!.Board.HuntsStarted);
        Assert.Equal(-1, off.AiOf(0)!.Board.StarvedSince);
    }

    private static readonly Lazy<GameData> Off = new(() => Data(r => r["ai"]!["starvedWaveAfter"] = 0));

    private static readonly Lazy<GameData> MinArmy3 = new(() => Data(r => r["ai"]!["starvedMinArmy"] = 3));

    private static readonly Lazy<GameData> MinArmy4 = new(() => Data(r => r["ai"]!["starvedMinArmy"] = 4));

    /// <summary>
    /// <c>ai.starvedMinArmy</c> at its boundary ((iii)): three free Heavy Infantry are starved with the minimum at 3 (stamped at
    /// tick 0, a wave at the window) and never with it at 4 (no stamp, no wave in three windows).
    /// </summary>
    [Fact]
    public void StarvedMinArmy_AtTheBoundary_ThreeOfThreeCounts_ThreeOfFourDoesNot()
    {
        (Simulation at, _) = Scene(AiDifficulty.Normal, MinArmy3.Value, ResourceKind.Gold, 0, 100);
        AiPlayer a = at.AiOf(0)!;
        int window = a.Rules.StarvedWaveAfterTicks;
        while (at.TickNumber < window + 2 * a.ThinkTicks) at.Tick();
        Assert.Equal(0, a.Board.StarvedSince);
        Assert.Equal(1, a.Board.WavesStarted);

        (Simulation over, _) = Scene(AiDifficulty.Normal, MinArmy4.Value, ResourceKind.Gold, 0, 100);
        AiPlayer o = over.AiOf(0)!;
        for (int t = 0; t < 3 * window; t++)
        {
            over.Tick();
            Assert.Equal(-1, o.Board.StarvedSince);
        }
        Assert.Equal(0, o.Board.WavesStarted + o.Board.HuntsStarted);
    }

    /// <summary>
    /// Wood starvation: 1,000 gold banked (and mines worked) but every tree emptied and 0 wood: every composition unit is short
    /// of wood only, no forest is known and no worker can be at wood, so the seat is stamped on its first think and waves at
    /// the window. A forest appearing by the hall (a tree spawned in reach) clears the stamp within a few thinks.
    /// </summary>
    [Fact]
    public void AWoodStarvedSeat_WithGoldBanked_WavesAtTheWindow_AndATreeInReachClearsIt()
    {
        (Simulation sim, _) = Scene(AiDifficulty.Normal, Shipped.Value, ResourceKind.Wood, 1000, 0);
        AiPlayer ai = sim.AiOf(0)!;
        int window = ai.Rules.StarvedWaveAfterTicks, think = ai.ThinkTicks;
        int waveAt = -1;
        ai.ThoughtForTests = a => { if (waveAt < 0 && a.Board.WavesStarted > 0) waveAt = a.Now; };
        while (waveAt < 0 && sim.TickNumber < 3 * window) sim.Tick();
        _out.WriteLine($"wood-starved: stamp {ai.Board.StarvedSince}, wave at {waveAt}, gold {ai.Gold}, wood {ai.Wood}");
        Assert.Equal(0, ai.Board.StarvedSince);
        Assert.Equal(FirstThinkFrom(window, think), waveAt);

        (Simulation tree, _) = Scene(AiDifficulty.Normal, Shipped.Value, ResourceKind.Wood, 1000, 0);
        AiPlayer t = tree.AiOf(0)!;
        while (tree.TickNumber < window / 2) tree.Tick();
        Assert.Equal(0, t.Board.StarvedSince);
        (int hx, int hy) = AiScenes.HallAt(0);
        Assert.True(tree.World.Resources.Spawn(tree.World.Data.FindResource("tree"), (hy + 5) * AiScenes.FlatSize + hx + 1, 500, out _));
        int spawned = tree.TickNumber;
        while (tree.TickNumber < spawned + 4 * think) tree.Tick();
        _out.WriteLine($"tree at {spawned}: stamp now {t.Board.StarvedSince}");
        Assert.Equal(-1, t.Board.StarvedSince);
        while (tree.TickNumber < 2 * window) tree.Tick();
        Assert.Equal(0, t.Board.WavesStarted);
    }

    /// <summary>
    /// (ii) is per short kind: 40 gold / 30 wood makes Heavy Infantry short of gold (54) and Crossbowmen short of wood (45).
    /// With every mine emptied but the forest standing (wood known, workers on it) the seat is <b>not</b> starved: wood buys
    /// a Crossbowman. With both emptied it is.
    /// </summary>
    [Fact]
    public void AMixedShortage_WithOneKindStillGatherable_IsNotStarved_BothEmptiedIs()
    {
        (Simulation sim, _) = Scene(AiDifficulty.Normal, Shipped.Value, ResourceKind.Gold, 40, 30);
        AiPlayer ai = sim.AiOf(0)!;
        int window = ai.Rules.StarvedWaveAfterTicks;
        // Keep the bank under both costs (the workers gather wood): reset it before every think.
        for (int t = 0; t < 2 * window; t++)
        {
            sim.World.Ledger.Gold[0] = 40;
            sim.World.Ledger.Wood[0] = 30;
            sim.Tick();
            Assert.Equal(-1, ai.Board.StarvedSince);
        }
        Assert.Equal(0, ai.Board.WavesStarted);

        (Simulation both, _) = Scene(AiDifficulty.Normal, Shipped.Value, ResourceKind.Gold, 40, 30);
        EmptyAll(both.World, ResourceKind.Wood);
        AiPlayer b = both.AiOf(0)!;
        while (both.TickNumber < window + 2 * b.ThinkTicks) both.Tick();
        Assert.Equal(0, b.Board.StarvedSince);
        Assert.Equal(1, b.Board.WavesStarted);
    }

    /// <summary>
    /// Constraint: a starved seat's think (the census's starved check walking the nodes and workers, stamped and due, a wave
    /// on) allocates nothing. Wood-starved (gold banked, trees emptied) so the bank never dips through a cost of the other kind.
    /// </summary>
    [Fact]
    public void AStarvedSeatsThink_AllocatesNothing()
    {
        (Simulation sim, _) = Scene(AiDifficulty.Hard, Shipped.Value, ResourceKind.Wood, 1000, 0, army: 12);
        AiPlayer ai = sim.AiOf(0)!;
        int window = ai.Rules.StarvedWaveAfterTicks;
        while (sim.TickNumber < window + 40) sim.Tick();
        Assert.True(ai.StarvedWaveDue);
        while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
        // The check alone first (a think just sent orders leaves no unit free until they apply, so it reads false after).
        Assert.True(ai.Starved());
        Action starved = () => ai.Starved();
        AllocationProbe.AssertZero(starved, _out);
        Action think = () => ai.Think();
        AllocationProbe.AssertZero(think, _out);
    }

    /// <summary>
    /// BUG-1154 (QA, fail-first): clause (ii) clears the seat when <b>any</b> short kind is gatherable, even when every
    /// composition entry is also short of a kind it can never get. 0 gold with every mine emptied (no gold ever again) and
    /// wood under each unit's wood cost with the forest worked: no unit can ever be paid for (each needs gold), yet the seat
    /// is never stamped. Seen first in the developer's scene shape at Hard with 12 Heavy Infantry: 100 wood spent to 40 at
    /// tick 480 cleared the stamp (a Crossbowman's 45 wood short, and wood gathered) and restamped it at 610, moving the wave.
    /// </summary>
    [Fact]
    public void AGoldStarvedSeat_WithWoodUnderACost_ButNoGoldEver_IsStarved()
    {
        (Simulation sim, _) = Scene(AiDifficulty.Normal, Shipped.Value, ResourceKind.Gold, 0, 0);
        AiPlayer ai = sim.AiOf(0)!;
        int window = ai.Rules.StarvedWaveAfterTicks, think = ai.ThinkTicks;
        int waveAt = -1;
        ai.ThoughtForTests = a => { if (waveAt < 0 && a.Board.WavesStarted > 0) waveAt = a.Now; };
        // Hold the bank at 0 gold / 0 wood (what it gathers is wood only; keep it under every wood cost).
        while (sim.TickNumber < window + 2 * think)
        {
            sim.World.Ledger.Wood[0] = 0;
            sim.Tick();
        }
        _out.WriteLine($"0 gold / 0 wood, no mine: stamp {ai.Board.StarvedSince}, wave at {waveAt}");
        Assert.Equal(0, ai.Board.StarvedSince);
        Assert.Equal(FirstThinkFrom(window, think), waveAt);
    }

    /// <summary>
    /// Criterion 8 (the AI's state out of <c>StateHash</c>): forging a seat's starved stamp and counter memory between ticks
    /// leaves <see cref="Simulation.StateHash"/> unchanged; and the knob's 0 = off is not reachable by a positive value that
    /// rounds to 0 ticks without an error (BUG-1155 item 1, fail-first: 0.01 s loads as "off").
    /// </summary>
    [Fact]
    public void TheStampAndTheMemory_AreNotInTheStateHash()
    {
        (Simulation sim, _) = Scene(AiDifficulty.Normal, Shipped.Value, ResourceKind.Gold, 0, 100);
        while (sim.TickNumber < 400) sim.Tick();
        AiPlayer ai = sim.AiOf(0)!;
        ulong before = sim.StateHash();
        ai.Board.StarvedSince = 7;
        ai.Board.SeenSlotCount[(int)UnitSlot.Shock] = 9;
        ai.Board.SeenSlotTick[(int)UnitSlot.Shock] = 380;
        Assert.Equal(before, sim.StateHash());
    }

    [Fact]
    public void StarvedWaveAfter_APositiveValueThatRoundsToZeroTicks_IsRefused()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["ai"]!["starvedWaveAfter"] = 0.01);
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        _out.WriteLine($"ok {load.Ok}, ticks {(load.Ok ? load.Data!.AiRules.StarvedWaveAfterTicks : -1)}, errors {string.Join("; ", load.Errors)}");
        Assert.False(load.Ok);
        Assert.Contains(load.Errors, e => e.ToString().Contains("ai.starvedWaveAfter"));
    }

    /// <summary>
    /// Save fuzz over the <c>CTRM</c> section (QA focus: forged counts / ticks / stamp): every byte of the section of the small
    /// AI save set to 0x00, 0x01, 0x02, 0x7F, 0x80 and 0xFF in turn (rechecksummed) either loads or is refused, never throws; a load that
    /// is accepted holds every memory count in 0..unit slots, every tick and the stamp (-1 or 0..) at most the seat's last
    /// think, so nothing out of range gets past the reader.
    /// </summary>
    [Fact]
    public void EveryByteOfTheCounterSection_Forged_LoadsInRangeOrIsRefused_NeverThrows()
    {
        byte[] good = Rts.Sim.Tests.Save.SaveBytes.SmallAi();
        int at = Rts.Sim.Tests.Save.PreMapFixtures.CountersAt(good, 1);
        int end = good.Length - Rts.Sim.Save.SaveFormat.ChecksumLength - 4;
        Assert.Equal("CTRM"u8.ToArray(), good[at..(at + 4)]);
        int loads = 0, refusals = 0;
        foreach (byte value in new byte[] { 0x00, 0x01, 0x02, 0x7F, 0x80, 0xFF })
            for (int i = at; i < end; i++)
            {
                byte[] bad = (byte[])good.Clone();
                if (bad[i] == value) continue;
                bad[i] = value;
                bad = Rts.Sim.Tests.Save.SaveBytes.Rechecksum(bad);
                using var stream = new MemoryStream(bad);
                if (!Simulation.TryLoad(TestSim.Data, stream, out Simulation? sim, out _)) { refusals++; continue; }
                loads++;
                AiPlayer ai = sim!.AiOf(0)!;
                for (int e = 0; e < Blackboard.SlotCount; e++)
                {
                    Assert.InRange(ai.Board.SeenSlotCount[e], 0, ai.View.UnitSlots);
                    Assert.InRange(ai.Board.SeenSlotTick[e], 0, ai.Now);
                }
                Assert.True(ai.Board.StarvedSince == -1 || ai.Board.StarvedSince >= 0 && ai.Board.StarvedSince <= ai.Now, $"stamp {ai.Board.StarvedSince} loaded");
            }
        _out.WriteLine($"CTRM bytes {at}..{end}: {loads} forged saves loaded in range, {refusals} refused");
        Assert.True(refusals > 0 && loads > 0, $"{loads} loads, {refusals} refusals");
    }
}
