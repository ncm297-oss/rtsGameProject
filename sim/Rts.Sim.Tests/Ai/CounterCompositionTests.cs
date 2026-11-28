using System.Collections.Immutable;
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
/// M5-9a (D14 c, docs/03 "Implementation (M5-9a)"): the counter memory (per enemy unit slot, fed by the census from the seat's
/// <see cref="PlayerView"/> only, fading over <c>ai.counterMemory</c>) and the production shift (a composition weight x (1 +
/// <c>counterStrength</c> x the share of the remembered enemy its slot counters)). Criteria 2-5 of the brief.
/// </summary>
public class CounterCompositionTests
{
    private const int Tps = SimConstants.TicksPerSecond;

    /// <summary>Where the sighted enemy stands (m): far from both halls (no defense), off the scout's early ring.</summary>
    private static readonly Vector2 Far = new(84f, 12f);

    private readonly ITestOutputHelper _out;

    public CounterCompositionTests(ITestOutputHelper output) => _out = output;

    private static int Unit(string key) => TestSim.Data.FindUnit(key);

    private static int Shock => (int)UnitSlot.Shock;

    /// <summary>Ticks to the next think of player 0's AI (the tick after it has thought).</summary>
    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    /// <summary>A Malazan AI seat (player 0) at <paramref name="difficulty"/> playing <c>standard</c>, and a Whirlwind human seat that sends nothing.</summary>
    private static Simulation Scene(AiDifficulty difficulty, GameData? data = null)
    {
        ulong seed = Content.AiOpeningSeeds.Smallest("malazan", 0, difficulty);
        PlayerSeat[] seats = { new(0, SeatController.Ai, difficulty), new(1, SeatController.Human, difficulty) };
        Simulation sim = AiScenes.Flat(seats, data, seed: seed);
        Assert.Equal(0, sim.AiOpening(0));
        return sim;
    }

    /// <summary>
    /// Spawns <paramref name="count"/> of <paramref name="type"/> for player 1 at <see cref="Far"/>; when <paramref name="seen"/>
    /// they are revealed to player 0 for the next think only (none of its units stands near). Returns their handles.
    /// </summary>
    private static List<EntityHandle> Spawn(Simulation sim, int type, int count, bool seen)
    {
        var list = new List<EntityHandle>();
        for (int k = 0; k < count; k++)
        {
            EntityHandle h = CombatScenes.Place(sim, 1, type, Far + new Vector2(1.2f * (k % 4), 1.2f * (k / 4)));
            if (seen) sim.World.Fog.Reveal(h, 0, sim.TickNumber + sim.AiOf(0)!.ThinkTicks);
            list.Add(h);
        }
        return list;
    }

    private static void Free(Simulation sim, List<EntityHandle> units)
    {
        foreach (EntityHandle h in units)
            if (sim.World.Units.IsAlive(h)) sim.World.Units.Free(h);
    }

    /// <summary>The finished-building count of player 0 of <paramref name="key"/>.</summary>
    private static bool HasFinished(Simulation sim, string key)
    {
        World w = sim.World;
        int type = w.Data.FindBuilding(key);
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 0 && w.Buildings.TypeId[i] == type && !w.Buildings.UnderConstruction[i]) return true;
        return false;
    }

    /// <summary>
    /// The criterion-2 run: the scene ticked until the barracks and the crossbow range stand finished, then 8 Horse Raiders
    /// (Whirlwind Shock) seen for one think (<paramref name="seen"/>) or standing out of every sight, removed after that think;
    /// then three minutes. Returns Heavy Infantry and Crossbowmen live + queued at the end, and the memory after the sighting.
    /// </summary>
    private (int Line, int Ranged, int[] Count, int[] Tick) Run(AiDifficulty difficulty, bool seen, GameData? data = null) =>
        Stage(difficulty, seen, data, producersEach: 1);

    /// <summary>
    /// M6-S11 (BUG-1462, outcome A): the criterion-2 run with <see cref="WideProducers"/> barracks and crossbow ranges, the
    /// extra ones spawned finished the think the first pair stands, and the purse paid that many times the one-pair boundary,
    /// so each producer still trains only what the weights pick and twice the units land inside the 180 s window. Same purse,
    /// same sighting, same seed for every difficulty; see <see cref="AHardSeat_ShiftsItsLineShareFurther_ThanANormalSeat"/>.
    /// </summary>
    private (int Line, int Ranged, int[] Count, int[] Tick) RunWide(AiDifficulty difficulty, bool seen, GameData? data = null) =>
        Stage(difficulty, seen, data, WideProducers);

    /// <summary>Barracks and crossbow ranges each the wide run stands (M6-S11): 18 units in the window instead of 9.</summary>
    private const int WideProducers = 2;

    /// <summary>
    /// Spawns a finished <paramref name="key"/> for player 0 on the flat scene's west half, at the first anchor (row by row from
    /// the top) whose footprint and a one-cell margin round it are open, with no unit in them and the never-seal rule kept.
    /// </summary>
    private static void SpawnProducer(Simulation sim, string key)
    {
        World w = sim.World;
        int type = w.Data.FindBuilding(key);
        BuildingDef def = w.Data.Buildings[type];
        const float cs = Rts.Sim.Map.MapConstants.CellSize;
        for (int y = 2; y < AiScenes.FlatSize - def.FootprintHeight - 2; y++)
            for (int x = 2; x < AiScenes.FlatSize / 2 - def.FootprintWidth; x++)
            {
                bool open = true;
                for (int cy = y - 1; cy <= y + def.FootprintHeight && open; cy++)
                    for (int cx = x - 1; cx <= x + def.FootprintWidth && open; cx++)
                        open = w.NavGrid.IsPassable(cx, cy) && (w.NavGrid.FlagsAt(cx, cy) & Rts.Sim.Map.NavFlags.Resource) == 0;
                if (!open || !w.Buildings.Fits(type, y * AiScenes.FlatSize + x) || !w.Seal.KeepsConnected(x, y, def.FootprintWidth, def.FootprintHeight)) continue;
                for (int i = 0; i < w.Units.Capacity && open; i++)
                {
                    if (!w.Units.Alive[i]) continue;
                    Vector2 p = w.Units.Position[i];
                    open = p.X < (x - 1) * cs || p.X >= (x + def.FootprintWidth + 1) * cs || p.Y < (y - 1) * cs || p.Y >= (y + def.FootprintHeight + 1) * cs;
                }
                if (!open) continue;
                Assert.True(w.Buildings.Spawn(0, type, y * AiScenes.FlatSize + x, out _));
                return;
            }
        Assert.Fail($"no room for a {key}");
    }

    private (int Line, int Ranged, int[] Count, int[] Tick) Stage(AiDifficulty difficulty, bool seen, GameData? data, int producersEach)
    {
        Simulation sim = Scene(difficulty, data);
        AiPlayer ai = sim.AiOf(0)!;
        while (!(HasFinished(sim, "malazan_barracks") && HasFinished(sim, "malazan_crossbow_range")))
        {
            NextThink(sim);
            Assert.True(sim.TickNumber < 9000, "no barracks and crossbow range by 7:30");
        }
        // M6-S10 re-staging (rule 57 / criterion 10). From M6-S10 (the Keep-corner cure, gatherOverlap 0.5) this flat seat is
        // wood-bound once its producers stand (gold 312-506 banked, wood 5-10): the seen and unseen runs trained the same
        // 10 Line / 8 Ranged (Hard 10 / 9) and the counter weights never chose (on c684902: 11 v 10 Line, Normal). The weights
        // decide only on a think where both producers are free and the bank pays for one unit. Wood topped up alone leaves the
        // seat producer-bound (11 / 10 both ways); a gold trickle of half the two producers' rate is a round robin (the
        // producer that just trained is busy while the other is paid for: 6 / 7 both ways). So, from here on, each tick:
        // player 0's wood is topped up to WoodTopUp and the gold its workers deliver is replaced by a trickle at the gold-bound
        // boundary, the cheaper unit's gold over the longer train time (both from the data): the bank pays for a unit no
        // faster than a producer finishes one, so each purchase is the weights' choice. Seen and unseen runs get the same
        // staging; the AI, the sighting and the memory are untouched. Normal: 6 / 3 seen v 5 / 4 unseen.
        int line = Unit("malazan_heavy_infantry"), ranged = Unit("malazan_crossbowman");
        for (int k = 1; k < producersEach; k++)
        {
            SpawnProducer(sim, "malazan_barracks");
            SpawnProducer(sim, "malazan_crossbow_range");
        }
        int lineBefore = ai.Live[line] + ai.Queued[line], rangedBefore = ai.Live[ranged] + ai.Queued[ranged];
        List<EntityHandle> raiders = Spawn(sim, Unit("whirlwind_horse_raider"), 8, seen);
        var purse = new Purse(sim, line, ranged, producersEach);
        purse.Think();
        Free(sim, raiders);
        int[] count = (int[])ai.Board.SeenSlotCount.Clone(), tick = (int[])ai.Board.SeenSlotTick.Clone();
        int end = sim.TickNumber + 180 * Tps;
        while (sim.TickNumber < end) purse.Think();
        _out.WriteLine($"{difficulty} seen {seen} producers {producersEach}: sighting at {sim.TickNumber - 180 * Tps}, line {lineBefore} -> {ai.Live[line] + ai.Queued[line]}, "
            + $"ranged {rangedBefore} -> {ai.Live[ranged] + ai.Queued[ranged]}, memory [{string.Join(",", count)}] at [{string.Join(",", tick)}], "
            + $"gold {sim.World.Gold[0]} wood {sim.World.Wood[0]} at the end");
        return (ai.Live[line] + ai.Queued[line], ai.Live[ranged] + ai.Queued[ranged], count, tick);
    }

    /// <summary>Player 0's wood the criterion-2 run keeps topped up after its producers stand (M6-S10 re-staging; see <see cref="Run"/>).</summary>
    private const int WoodTopUp = 2000;

    /// <summary>
    /// M6-S10 re-staging of the criterion-2 run (see <see cref="Run"/>): each tick tops player 0's wood up to
    /// <see cref="WoodTopUp"/>, takes back the gold its workers delivered in the tick and pays a trickle instead,
    /// min(Line gold, Ranged gold) / max(Line train ticks, Ranged train ticks) per tick, carried in a remainder so none is lost.
    /// </summary>
    private sealed class Purse
    {
        private readonly Simulation _sim;
        private readonly long _num, _den; // gold per tick = _num / _den: the gold-bound boundary
        private long _carry;

        public Purse(Simulation sim, int line, int ranged, int producersEach = 1)
        {
            _sim = sim;
            UnitDef l = sim.World.Data.Units[line], r = sim.World.Data.Units[ranged];
            _num = (long)Math.Min(l.CostGold, r.CostGold) * producersEach; // M6-S11: each pair of producers its own boundary's worth
            _den = Math.Max(l.TrainTicks, r.TrainTicks);
        }

        public void Think()
        {
            // Tick by tick (as NextThink): the seat thinks at the start of a tick, before that tick's deliveries, so it
            // never sees gold the purse takes back before its Train applies.
            int think = _sim.AiOf(0)!.ThinkTicks;
            do Tick(); while ((_sim.TickNumber - 1) % think != 0);
        }

        private void Tick()
        {
            World w = _sim.World;
            if (w.Ledger.Wood[0] < WoodTopUp) w.Ledger.Wood[0] = WoodTopUp;
            int delivered = w.GatheredGold[0];
            _sim.Tick();
            delivered = w.GatheredGold[0] - delivered;
            _carry += _num;
            int paid = (int)(_carry / _den);
            _carry -= paid * _den;
            w.Ledger.Gold[0] = Math.Max(0, w.Ledger.Gold[0] - delivered) + paid;
        }
    }

    /// <summary>Criterion 2 (a) and (c): a Normal seat that saw 8 Horse Raiders once trains more Line than the same seat and seed that did not; the unseen run's memory is all zeros.</summary>
    [Fact]
    public void ANormalSeat_ThatSawShock_TrainsMoreLine_ThanOneThatDidNot()
    {
        var seen = Run(AiDifficulty.Normal, seen: true);
        var unseen = Run(AiDifficulty.Normal, seen: false);
        Assert.Equal(8, seen.Count[Shock]);
        Assert.Equal(new int[Blackboard.SlotCount], unseen.Count);
        Assert.Equal(new int[Blackboard.SlotCount], unseen.Tick);
        Assert.True(seen.Line > unseen.Line, $"line {seen.Line} seen vs {unseen.Line} unseen");
    }

    /// <summary>Criterion 2 (b): the same sighting moves a Hard seat's Line share further than a Normal seat's (strength 1 against 0.5).</summary>
    // M6-S10: red on the re-staged run (see Run): Normal and Hard both go 5 / 4 unseen -> 6 / 3 seen, shift 0.111 each; nine
    // units in the 180 s memory window cannot tell Line weighted x 1.5 from x 2 (the deficit pick's 9th unit is the 6th Line at
    // both weights, BUG-1462). M6-S11 (outcome A): the row runs the wide staging (RunWide: two producers of each kind, twice the
    // purse), 18 units in the same window, where x 1.5 and x 2 part by a whole unit; CounterRestageQaTests holds its strength-0
    // controls for Normal and Hard.
    [Fact]
    public void AHardSeat_ShiftsItsLineShareFurther_ThanANormalSeat()
    {
        static double Share((int Line, int Ranged, int[] _, int[] __) r) => (double)r.Line / (r.Line + r.Ranged);
        double normal = Share(RunWide(AiDifficulty.Normal, true)) - Share(RunWide(AiDifficulty.Normal, false));
        double hard = Share(RunWide(AiDifficulty.Hard, true)) - Share(RunWide(AiDifficulty.Hard, false));
        _out.WriteLine($"line share shift: normal {normal:0.000}, hard {hard:0.000}");
        Assert.True(normal > 0, $"normal shift {normal}");
        Assert.True(hard > normal, $"hard shift {hard} vs normal {normal}");
        // The pick itself: 17 Heavy Infantry / 9 Crossbowmen against all-Shock memory. Normal weights Line 35 x 1.5 = 52.5
        // (17 / 52.5 = 0.324 > 9 / 30 = 0.300: still a Crossbowman); Hard 35 x 2 = 70 (0.243: Heavy Infantry).
        int line = Unit("malazan_heavy_infantry"), ranged = Unit("malazan_crossbowman");
        static void AllShock(Blackboard b, AiPlayer _) { b.SeenSlotCount[Shock] = 8; b.SeenSlotTick[Shock] = 0; }
        Assert.Equal(ranged, FirstTrain(Pick.Value, AllShock, AiDifficulty.Normal, lineUnits: 17));
        Assert.Equal(line, FirstTrain(Pick.Value, AllShock, AiDifficulty.Hard, lineUnits: 17));
        Assert.Equal(ranged, FirstTrain(Pick.Value, null, AiDifficulty.Hard, lineUnits: 17));
    }

    // ---------- the pick (criterion 3): a seat whose build order sends nothing, both producers standing ----------

    /// <summary>
    /// Data whose Malazan openings send nothing for an hour (one step at 3,600 s) and train Heavy Infantry 35 / Crossbowmen 30
    /// in both ages; <paramref name="strength"/> for Normal when given.
    /// </summary>
    private static GameData PickData(double? strength = null) => AiScenes.DataWithAi("malazan", root =>
    {
        foreach (JsonNode? o in root["openings"]!.AsArray())
        {
            AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 3600, "build", "malazan_billet", 0)));
            o["compositions"] = new JsonArray(
                new JsonObject { ["age"] = 1, ["units"] = new JsonObject { ["malazan_heavy_infantry"] = 35, ["malazan_crossbowman"] = 30 } },
                new JsonObject { ["age"] = 2, ["units"] = new JsonObject { ["malazan_heavy_infantry"] = 35, ["malazan_crossbowman"] = 30 } });
        }
        if (strength != null) root["difficulties"]!["normal"]!["counterStrength"] = strength.Value;
    });

    private static readonly Lazy<GameData> Pick = new(() => PickData());

    /// <summary>
    /// The pick scene: a Malazan seat (Normal by default) with a finished barracks and crossbow range and 11 (<paramref name="lineUnits"/>) Heavy Infantry / 9 Crossbowmen
    /// (Heavy Infantry 11 / 35 = 0.314 of its weight, Crossbowmen 9 / 30 = 0.300: the baseline picks a Crossbowman; Line
    /// weighted 1.5 picks Heavy Infantry, 11 / 52.5 = 0.210), its memory set by <paramref name="memory"/> before the first think.
    /// Returns the unit type of the first Train the seat sends at tick 0.
    /// </summary>
    private static int FirstTrain(GameData data, Action<Blackboard, AiPlayer>? memory, AiDifficulty difficulty = AiDifficulty.Normal, int lineUnits = 11)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, difficulty), new(1, SeatController.Human, difficulty) };
        Simulation sim = AiScenes.Flat(seats, data);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_barracks"), 20 * AiScenes.FlatSize + 4, out _));
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_crossbow_range"), 20 * AiScenes.FlatSize + 12, out _));
        for (int k = 0; k < lineUnits + 9; k++)
            CombatScenes.Place(sim, 0, data.FindUnit(k < lineUnits ? "malazan_heavy_infantry" : "malazan_crossbowman"), w.NavGrid.CellCenter(14 + k % 6, 40 + k / 6));
        AiPlayer ai = sim.AiOf(0)!;
        memory?.Invoke(ai.Board, ai);
        List<Command> log = AiScenes.Log(sim, 0);
        sim.Tick();
        Command train = Assert.Single(log, c => c.Kind == CommandKind.Train);
        return train.TypeId;
    }

    /// <summary>Criterion 3: the pick with no memory is the baseline's; with Shock remembered it is Line; past <c>counterMemory</c> it is the baseline's again.</summary>
    [Fact]
    public void ThePick_ShiftsWhileShockIsRemembered_AndIsTheBaselinesOnceTheMemoryHasFaded()
    {
        int line = Unit("malazan_heavy_infantry"), ranged = Unit("malazan_crossbowman");
        int memory = Pick.Value.AiRules.CounterMemoryTicks;
        Assert.Equal(180 * Tps, memory);
        Assert.Equal(ranged, FirstTrain(Pick.Value, null));
        // A sighting of 8 at tick 0 (the first think's Now): fully remembered.
        Assert.Equal(line, FirstTrain(Pick.Value, (b, _) => { b.SeenSlotCount[Shock] = 8; b.SeenSlotTick[Shock] = 0; }));
        // One tick short of the window: faded to 8 / 3,600, still the whole remembered enemy, so the shift is whole.
        Assert.Equal(line, FirstTrain(Pick.Value, (b, _) => { b.SeenSlotCount[Shock] = 8; b.SeenSlotTick[Shock] = -(memory - 1); }));
        // Now driven to the window's end: faded to 0, nothing remembered, the baseline's pick.
        Assert.Equal(ranged, FirstTrain(Pick.Value, (b, _) => { b.SeenSlotCount[Shock] = 8; b.SeenSlotTick[Shock] = -memory; }));
        Assert.Equal(ranged, FirstTrain(Pick.Value, (b, _) => { b.SeenSlotCount[Shock] = 8; b.SeenSlotTick[Shock] = -10 * memory; }));
        // A remembered slot nothing in the composition counters (Line: countered by casters and siege) shifts nothing.
        Assert.Equal(ranged, FirstTrain(Pick.Value, (b, _) => { b.SeenSlotCount[(int)UnitSlot.Line] = 8; b.SeenSlotTick[(int)UnitSlot.Line] = 0; }));
        // Strength 0 on Normal: no shift, whatever is remembered.
        Assert.Equal(ranged, FirstTrain(PickData(strength: 0), (b, _) => { b.SeenSlotCount[Shock] = 8; b.SeenSlotTick[Shock] = 0; }));
    }

    /// <summary>The fade's formula at its edges: the count at the sighting, half at half the window, 0 at and past its end; the share each slot counters.</summary>
    [Fact]
    public void FadedSlot_FallsLinearlyToZero_OverTheMemory_AndTheShareIsOfTheFadedTotal()
    {
        Simulation sim = Scene(AiDifficulty.Normal);
        AiPlayer ai = sim.AiOf(0)!;
        int memory = ai.Rules.CounterMemoryTicks;
        NextThink(sim); // Now = 0
        ai.Board.SeenSlotCount[Shock] = 8;
        ai.Board.SeenSlotTick[Shock] = 0;
        Assert.Equal(8f, ai.FadedSlot(Shock));
        ai.Board.SeenSlotTick[Shock] = -memory / 2;
        Assert.Equal(4f, ai.FadedSlot(Shock));
        ai.Board.SeenSlotTick[Shock] = -memory;
        Assert.Equal(0f, ai.FadedSlot(Shock));
        ai.Board.SeenSlotTick[Shock] = -memory - 1;
        Assert.Equal(0f, ai.FadedSlot(Shock));
        Assert.Equal(0f, ai.FadedSlot((int)UnitSlot.Line)); // never seen

        // The worked example of docs/03: 6 Horse Raiders (Shock) and 2 Desert Archers (Ranged), both seen now. Line counters
        // Shock (6 / 8), Shock counters Ranged (2 / 8), casters and siege counter neither, so their share is 0.
        float[] share = Shares(sim, (Shock, 6), ((int)UnitSlot.Ranged, 2));
        Assert.Equal(new[] { 0f, 0.75f, 0f, 0.25f, 0f, 0f, 0f }, share);
        // Every slot seen once: each own slot's share is the number of enemy slots it counters over 6 (workers are never counted).
        share = Shares(sim, (1, 1), (2, 1), (3, 1), (4, 1), (5, 1), (6, 1));
        Assert.Equal(new[] { 0f, 2f / 6, 1f / 6, 3f / 6, 1f / 6, 1f / 6, 0f }, share);
        // Nothing remembered: every share 0, no division.
        Assert.Equal(new float[7], Shares(sim));
    }

    /// <summary>The <see cref="AiPlayer.CounterShare"/> the next think computes from a memory of the given (slot, count) sightings made at its own tick.</summary>
    private static float[] Shares(Simulation sim, params (int Slot, int Count)[] seen)
    {
        AiPlayer ai = sim.AiOf(0)!;
        float[] got = Array.Empty<float>();
        int next = (sim.TickNumber + ai.ThinkTicks - 1) / ai.ThinkTicks * ai.ThinkTicks;
        Array.Clear(ai.Board.SeenSlotCount);
        Array.Clear(ai.Board.SeenSlotTick);
        foreach ((int slot, int count) in seen)
        {
            ai.Board.SeenSlotCount[slot] = count;
            ai.Board.SeenSlotTick[slot] = next;
        }
        ai.ThoughtForTests = a => got = (float[])a.CounterShare.Clone();
        NextThink(sim);
        ai.ThoughtForTests = null;
        Assert.Equal(new float[7], ai.CounterShare); // think scratch: cleared after the think
        return got;
    }

    /// <summary>Criterion 3: a sighting at least as big as the faded value replaces it; a smaller one leaves it (and its tick).</summary>
    [Fact]
    public void ABiggerSighting_ReplacesAFadingOne_ASmallerOneDoesNot()
    {
        Simulation sim = Scene(AiDifficulty.Normal);
        AiPlayer ai = sim.AiOf(0)!;
        int memory = ai.Rules.CounterMemoryTicks, horse = Unit("whirlwind_horse_raider");
        List<EntityHandle> eight = Spawn(sim, horse, 8, seen: true);
        NextThink(sim); // tick 0's think
        Free(sim, eight);
        Assert.Equal((8, 0), (ai.Board.SeenSlotCount[Shock], ai.Board.SeenSlotTick[Shock]));
        while (sim.TickNumber <= memory / 2) NextThink(sim);
        // Half the window gone (the faded value is about 4): 3 seen now does not replace it.
        List<EntityHandle> three = Spawn(sim, horse, 3, seen: true);
        NextThink(sim);
        Free(sim, three);
        Assert.Equal((8, 0), (ai.Board.SeenSlotCount[Shock], ai.Board.SeenSlotTick[Shock]));
        // 5 does (5 >= 8 x (1 - age / memory) once more than 3 / 8 of the window is gone).
        List<EntityHandle> five = Spawn(sim, horse, 5, seen: true);
        int at = sim.TickNumber - 1 + ai.ThinkTicks - (sim.TickNumber - 1) % ai.ThinkTicks;
        NextThink(sim);
        Free(sim, five);
        Assert.Equal((5, at), (ai.Board.SeenSlotCount[Shock], ai.Board.SeenSlotTick[Shock]));
        // Workers are never counted, nor an ally's units (none here), nor units out of sight.
        List<EntityHandle> workers = Spawn(sim, Unit("whirlwind_camp_follower"), 12, seen: true);
        List<EntityHandle> hidden = Spawn(sim, Unit("whirlwind_raider"), 12, seen: false);
        NextThink(sim);
        Free(sim, workers);
        Free(sim, hidden);
        Assert.Equal(new[] { 0, 0, 0, 5, 0, 0, 0 }, ai.Board.SeenSlotCount);
    }

    // ---------- bit-identical Easy, and an empty table (criterion 4) ----------

    /// <summary>Every command both AI seats had accepted over <paramref name="ticks"/> of an AI-versus-AI match with early waves on the generated map, and each seat's memory at the end.</summary>
    private static (List<Command>[] Logs, int[][] Memory) Match(AiDifficulty difficulty, GameData data, int ticks)
    {
        PlayerSeat[] seats = AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, 2, record: false, out _, data: data);
        var logs = new[] { AiScenes.Log(sim, 0), AiScenes.Log(sim, 1) };
        for (int t = 0; t < ticks && !sim.MatchOver; t++) sim.Tick();
        Assert.True(sim.TickNumber > 6000, $"the match ended at {sim.TickNumber}");
        return (logs, new[] { (int[])sim.AiOf(0)!.Board.SeenSlotCount.Clone(), (int[])sim.AiOf(1)!.Board.SeenSlotCount.Clone() });
    }

    /// <summary>
    /// Criterion 4: an Easy match (strength 0) whose seats see each other's armies sends exactly the commands it sends with
    /// <c>ai.counters</c> emptied: the memory is fed, the shift is a multiply by exactly 1.
    /// </summary>
    [Fact]
    public void AnEasyMatch_WithTheMemoryFed_SendsTheSameCommands_AsWithTheTableEmptied()
    {
        GameData shipped = AiScenes.DataWith(dir => EarlyWavesEdit(dir));
        GameData empty = AiScenes.DataWith(dir => { EarlyWavesEdit(dir); EmptyTable(dir); });
        var a = Match(AiDifficulty.Easy, shipped, 12_000);
        var b = Match(AiDifficulty.Easy, empty, 12_000);
        Assert.True(a.Memory[0].Sum() + a.Memory[1].Sum() > 0, "no sighting: the row proves nothing");
        AssertSameLogs(a.Logs, b.Logs);
    }

    /// <summary>Criterion 4 / QA focus 3: on Normal, an emptied table is a no-op too: the same commands as Normal with strength 0.</summary>
    [Fact]
    public void ANormalMatch_WithTheTableEmptied_SendsTheSameCommands_AsWithStrengthZero()
    {
        GameData empty = AiScenes.DataWith(dir => { EarlyWavesEdit(dir); EmptyTable(dir); });
        GameData zero = AiScenes.DataWith(dir =>
        {
            EarlyWavesEdit(dir);
            foreach (string f in TestSim.ShippedFactions)
                dir.EditJson($"factions/{f}/ai.json", r => r["difficulties"]!["normal"]!["counterStrength"] = 0);
        });
        var a = Match(AiDifficulty.Normal, empty, 12_000);
        var b = Match(AiDifficulty.Normal, zero, 12_000);
        Assert.True(a.Memory[0].Sum() + a.Memory[1].Sum() > 0, "no sighting: the row proves nothing");
        AssertSameLogs(a.Logs, b.Logs);
    }

    /// <summary>
    /// Criterion 4 / QA focus 2: QA's <c>NonHardMatchPinQaTests</c> rows are Easy v Normal or Normal v Normal, so their digests
    /// move with Normal's shift. With Normal's <c>counterStrength</c> at 0 (Easy's already is), (round 2) <c>ai.starvedWaveAfter</c>
    /// at 0 and (M5-9b) <c>ai.farExpandReach</c> at 0 every row reproduces the digest it pinned before M5-9a (c11f5f0) exactly:
    /// the memory, the stamp, the far expansion's state, the hash's new fields and the format change move no play, and the
    /// only changes in those matches are the three levers. <b>M5-H6</b>: its two rule changes (a lost House step reopened
    /// only when the population needs the house, BUG-1116; the hunt's free starts from its second hunt, BUG-1157) have no
    /// knob (the brief allowed none), so six of the eight rows now pin the M5-H6 play at the three knobs 0, the pre-M5-9a
    /// digest kept in each comment; Raraku and Vathar Easy lose no house that matters and are unchanged. The row still proves
    /// that the three knobs at 0 turn M5-9a / M5-9b off: the new digests are the M5-H6 rules alone.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("raraku", 1UL, AiDifficulty.Normal, "D370FF93D4B4E8D0 33086 1882")] // M6-S15 (the AI spends what it has, a hall-less seat's small drop-off, a hunt the defense cuts is resumed: BUG-1890 / 1893 / 1855): was 8589A58ACAA74D65 34420 1624; M6-S14 (the AI-play session: the hunt's resume, the worker floor, the rally search's memory, the blind re-scout's count, the rally point's memory): was A6A9B1138705D7CD 26167 1572; M6-S12 (the AI rules of 2026-11-16): was E613B526945067BD 36000 1844; M6-SH2 (a site is no drop-off, an AI with no worker retrains: BUG-1520 / 1465): was 89895C96A3BAEA61 25238 1788; M6-S10 (the Keep-corner cure, gatherOverlap 0.5): was ABF56AB721E8B0DC 28193 1531; M7-3b (tech / ability ids by faction order; outcomes unchanged): was 4765275972CD8BD8; pre-M5-9a: E0612240C3E713D2 28083 1476
    [InlineData("raraku", 1UL, AiDifficulty.Easy, "ADAE5D544E5A5434 23684 1365")] // M6-S15 (the AI spends what it has, a hall-less seat's small drop-off, a hunt the defense cuts is resumed: BUG-1890 / 1893 / 1855): was 212EC13B3160EBCC 23652 1362; M6-S14 (the AI-play session: the hunt's resume, the worker floor, the rally search's memory, the blind re-scout's count, the rally point's memory): was BB8F5963928F5F5D 25519 1527; M6-S12 (the AI rules of 2026-11-16): was 38EF617A08D2C6B9 36000 1769; M6-SH2 (a site is no drop-off, an AI with no worker retrains: BUG-1520 / 1465): was F475E235D4DC5585 32047 3393; M6-S10 (the Keep-corner cure, gatherOverlap 0.5): was B2B828CADAC9DC88 24498 1448; M7-3b (tech / ability ids by faction order; outcomes unchanged): was 0D877B9258F20265
    [InlineData("pale_hills", 1UL, AiDifficulty.Normal, "E21C622A96812B18 25319 1877")] // M6-S14 (the AI-play session: the hunt's resume, the worker floor, the rally search's memory, the blind re-scout's count, the rally point's memory): was CE3EEB6CB5607864 33730 3031; M6-S12 (the AI rules of 2026-11-16): was 65C68DBFADAC314C 25683 1797; M6-SH2 (a site is no drop-off, an AI with no worker retrains: BUG-1520 / 1465): was 08C5B82961984DB2 36000 2925; M6-S10 (the Keep-corner cure, gatherOverlap 0.5): was 2E5531DF16DDEA11 36000 3483; M7-3b (tech / ability ids by faction order; outcomes unchanged): was A0884E7E95EEA8AB; pre-M5-9a: 69C4358E67DF7FC3 34219 3144
    [InlineData("pale_hills", 1UL, AiDifficulty.Easy, "6DB1B6DCDBB36D88 34105 2578")] // M6-S15 (the AI spends what it has, a hall-less seat's small drop-off, a hunt the defense cuts is resumed: BUG-1890 / 1893 / 1855): was F58BA8995A84BAD0 34527 2788; M6-S14 (the AI-play session: the hunt's resume, the worker floor, the rally search's memory, the blind re-scout's count, the rally point's memory): was 49A5897051C46C70 36000 2516; M6-S12 (the AI rules of 2026-11-16): was D453F244B1AC6989 36000 2334; M6-SH2 (a site is no drop-off, an AI with no worker retrains: BUG-1520 / 1465): was 91A60B09763056AB 36000 2130; M6-S10 (the Keep-corner cure, gatherOverlap 0.5): was 42DF7252D93BC98E 36000 2486; M7-3b (tech / ability ids by faction order; outcomes unchanged): was 07EC9909C8DDF1C3; pre-M5-9a: 62F16C733AF6D9C8 36000 2601
    [InlineData("pale_hills", 3UL, AiDifficulty.Normal, "0C7197B59624D957 23576 1756")] // M6-S14 (the AI-play session: the hunt's resume, the worker floor, the rally search's memory, the blind re-scout's count, the rally point's memory): was C799951B70828D00 32389 2968; D33 (the data track's balance pass: cadre_rush waves at 16 army pop, holy_war at 14): was 1F9BED1DF34F35A1 36000 2591; M6-S12 (the AI rules of 2026-11-16): was CB08E4E210AD11C4 36000 2413; M6-SH2 (a site is no drop-off, an AI with no worker retrains: BUG-1520 / 1465): was 0AB9B0B17DF4FACD 27501 2539; M6-S10 (the Keep-corner cure, gatherOverlap 0.5): was B812C76579CC4AE2 28517 2459; M7-3b (tech / ability ids by faction order; outcomes unchanged): was 9753F06856D81476; pre-M5-9a: C9CB142CF3C97C88 29562 3103
    [InlineData("pale_hills", 3UL, AiDifficulty.Easy, "4761510001C9E292 36000 3344")] // M6-S15 (the AI spends what it has, a hall-less seat's small drop-off, a hunt the defense cuts is resumed: BUG-1890 / 1893 / 1855): was E7A2F347C8B826DD 36000 3153; M6-S14 (the AI-play session: the hunt's resume, the worker floor, the rally search's memory, the blind re-scout's count, the rally point's memory): was B83B3E28F0A0D6EB 36000 2132; D33 (the data track's balance pass: cadre_rush waves at 16 army pop, holy_war at 14): was 036E6627668A1BB9 30737 2390; M6-S12 (the AI rules of 2026-11-16): was 5470B5C79FC5E953 36000 3153; M6-SH2 fix round (BUG-1151: a rebalance Gather dropped at a gone mine marks it): was 1EAF7C1D0E471B75 36000 2639; M6-SH2 (a site is no drop-off, an AI with no worker retrains: BUG-1520 / 1465): was 6B4D02834976BA44 21671 1226; M6-S10 (the Keep-corner cure, gatherOverlap 0.5): was 0C9E51DFCBF08050 33508 3033; M7-3b (tech / ability ids by faction order; outcomes unchanged): was BC6868F49655A4DE; pre-M5-9a: 4FF13AE84024D0BB 33524 3075
    [InlineData("vathar_crossing", 1UL, AiDifficulty.Normal, "F72BC27D282F61E2 22769 1448")] // M6-S14 (the AI-play session: the hunt's resume, the worker floor, the rally search's memory, the blind re-scout's count, the rally point's memory): was B4CC2DD42DF94E35 23235 1282; M6-S12 (the AI rules of 2026-11-16): was 6D4AD246D96A6424 24697 1780; M6-SH2 (a site is no drop-off, an AI with no worker retrains: BUG-1520 / 1465): was 05FF8F5BE84E914C 23359 1458; M6-S10 (the Keep-corner cure, gatherOverlap 0.5): was EDB60FD8CE0D203B 23219 1495; M7-3b (tech / ability ids by faction order; outcomes unchanged): was 72BC8BD819A35B0F; pre-M5-9a: 4FA98DA390936D9B 22794 1395
    [InlineData("vathar_crossing", 1UL, AiDifficulty.Easy, "8E00ED086D869E8D 23469 1202")] // M6-S14 (the AI-play session: the hunt's resume, the worker floor, the rally search's memory, the blind re-scout's count, the rally point's memory): was 0554D0E7F8C6985A 27668 1226; M6-S12 (the AI rules of 2026-11-16): was C49501797E76536B 27314 1913; M6-SH2 (a site is no drop-off, an AI with no worker retrains: BUG-1520 / 1465): was B7607296410EA90A 28279 1792; M6-S10 (the Keep-corner cure, gatherOverlap 0.5): was 642A59FFA8E6EF34 36000 1798; M7-3b (tech / ability ids by faction order; outcomes unchanged): was 325B1E0137F84D8D
    public void TheNonHardPins_WithNormalStrengthZero_PlayAsBeforeThisTask(string map, ulong seed, AiDifficulty first, string before)
    {
        GameData zero = NormalStrengthZero.Value;
        int size = zero.Maps[zero.FindMap(map)].Size;
        PlayerSeat[] seats = { new(0, SeatController.Ai, first), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Generated(seats, seed, false, out _, data: zero, configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
        var log = new List<Command>();
        for (int p = 0; p < 2; p++) sim.AiOf(p)!.SentForTests = c => log.Add(c);
        ulong digest = 14695981039346656037UL;
        int logged = 0;
        while (sim.TickNumber < 36_000 && !sim.MatchOver)
        {
            sim.Tick();
            digest = Fold(digest, sim.StateHash());
            for (; logged < log.Count; logged++)
            {
                Command c = log[logged];
                ulong[] words =
                {
                    (ulong)c.Kind, (ulong)(uint)c.Player, (ulong)(uint)c.Tick, (ulong)(uint)c.Sequence, (ulong)(uint)c.TypeId,
                    (ulong)(uint)BitConverter.SingleToInt32Bits(c.Position.X), (ulong)(uint)BitConverter.SingleToInt32Bits(c.Position.Y),
                    (ulong)(uint)c.Unit.Index, (ulong)(uint)c.Unit.Generation, (ulong)(uint)c.Target.Index, (ulong)(uint)c.Target.Generation,
                    c.TargetIsBuilding ? 1UL : 0UL, (ulong)(uint)c.Flags,
                };
                foreach (ulong word in words) digest = Fold(digest, word);
            }
        }
        string got = $"{digest:X16} {sim.TickNumber} {log.Count}";
        _out.WriteLine($"{map} seed {seed} {first} v Normal, Normal strength 0, starved wave and far expansion off: {got}");
        Assert.Equal(before, got);
    }

    /// <summary>The fold of QA's <c>NonHardMatchPinQaTests</c> (FNV-1a over a word's 8 bytes).</summary>
    private static ulong Fold(ulong digest, ulong word)
    {
        for (int b = 0; b < 8; b++)
        {
            digest ^= (word >> (8 * b)) & 0xFF;
            digest *= 1099511628211UL;
        }
        return digest;
    }

    /// <summary>Normal's <c>counterStrength</c> 0 in both factions, (round 2) <c>ai.starvedWaveAfter</c> 0 and (M5-9b) <c>ai.farExpandReach</c> 0: every lever of M5-9a / M5-9b off.</summary>
    private static readonly Lazy<GameData> NormalStrengthZero = new(() => AiScenes.DataWith(dir =>
    {
        foreach (string f in TestSim.ShippedFactions)
            dir.EditJson($"factions/{f}/ai.json", r => r["difficulties"]!["normal"]!["counterStrength"] = 0);
        dir.EditJson("common/rules.json", r =>
        {
            r["ai"]!["starvedWaveAfter"] = 0; // M5-9a round 2: the starved wave off
            r["ai"]!["farExpandReach"] = 0; // M5-9b: the far expansion off
        });
    }));

    private static void EarlyWavesEdit(TestDataDir dir)
    {
        foreach (string f in TestSim.ShippedFactions)
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                foreach (JsonNode? o in root["openings"]!.AsArray())
                    o!["attack"] = new JsonObject { ["minArmyPop"] = 4, ["minArmyCost"] = 0 };
            });
    }

    private static void EmptyTable(TestDataDir dir) => dir.EditJson("common/rules.json", r =>
    {
        JsonObject counters = r["ai"]!["counters"]!.AsObject();
        foreach (string slot in DataLimits.SlotIds) counters[slot] = new JsonArray();
    });

    private static void AssertSameLogs(List<Command>[] a, List<Command>[] b)
    {
        for (int p = 0; p < a.Length; p++)
        {
            Assert.True(a[p].Count == b[p].Count, $"player {p}: {a[p].Count} commands vs {b[p].Count}");
            for (int i = 0; i < a[p].Count; i++)
                Assert.True(a[p][i].Equals(b[p][i]), $"player {p}: command {i} differs ({a[p][i].Kind} vs {b[p][i].Kind})");
        }
    }

    // ---------- save / load (criterion 5) ----------

    /// <summary>
    /// Criterion 5: a Normal seat saved with a non-empty memory (8 Shock at tick 0, then 5 later) loads with the same counts and
    /// ticks, re-saves byte for byte (format 8), and follows the run hash for hash for 600 ticks, the seats equal at the end.
    /// </summary>
    [Fact]
    public void ASeatWithAMemory_SavedAndLoaded_KeepsIt_AndFollowsTheRun()
    {
        Simulation sim = Scene(AiDifficulty.Normal);
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> eight = Spawn(sim, Unit("whirlwind_horse_raider"), 8, seen: true);
        List<EntityHandle> archers = Spawn(sim, Unit("whirlwind_desert_archer"), 3, seen: true);
        NextThink(sim);
        Free(sim, eight);
        Free(sim, archers);
        for (int t = 0; t < 30 * Tps; t++) sim.Tick();
        Assert.Equal(new[] { 0, 0, 3, 8, 0, 0, 0 }, ai.Board.SeenSlotCount);
        byte[] bytes = SaveTestRun.Save(sim);
        Assert.Equal(Rts.Sim.Save.SaveFormat.HuntMaplessVersion, BitConverter.ToInt32(bytes, Rts.Sim.Save.SaveFormat.MagicLength));
        Simulation load = SaveTestRun.Load(TestSim.Data, bytes);
        AiPlayer loaded = load.AiOf(0)!;
        Assert.Equal(ai.Board.SeenSlotCount, loaded.Board.SeenSlotCount);
        Assert.Equal(ai.Board.SeenSlotTick, loaded.Board.SeenSlotTick);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        SaveTestRun.AssertSameState(sim, load, "memory");
        List<Command> log = AiScenes.Log(sim, 0), loadLog = AiScenes.Log(load, 0);
        for (int t = 0; t < 600; t++)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
        SaveTestRun.AssertSameState(sim, load, "memory + 600");
        Assert.Equal(log.Count, loadLog.Count);
    }
}
