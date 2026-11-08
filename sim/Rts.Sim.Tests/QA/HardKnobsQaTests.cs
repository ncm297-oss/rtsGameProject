using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.HardKnobsScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-4c-2 (2026-11-07-0926): the Hard knobs' edges. The wounded boundary for every unit type of both factions; the
/// focus-fire order churn in a 20 v 20 melee; the pull-back edges (the whole army wounded, 3 wounded + 2 healthy at the
/// wave gate, a wounded unit hit again at home); the derived gather rate across a save and load of a Hard seat; and the
/// fog oracle at every think of a 4-seat Hard Pale Hills match.
/// </summary>
public class HardKnobsQaTests
{
    private readonly ITestOutputHelper _out;

    public HardKnobsQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// BUG-1122 (a): docs/03 "a unit is wounded when its hp is below its faction's retreat.hpFraction of its max hp": for every unit type of
    /// both factions whose threshold is a whole number of hp, a unit at exactly the threshold is not wounded and one hp less is.
    /// </summary>
    [Fact]
    public void Wounded_AtExactlyTheThreshold_IsNotWounded_ForEveryUnitType()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Ai));
        var faults = new List<string>();
        int checkedTypes = 0;
        for (int p = 0; p < 2; p++)
        {
            AiPlayer ai = sim.AiOf(p)!;
            decimal fraction = Math.Round((decimal)ai.Def.RetreatHpFraction, 6);
            for (int t = 0; t < sim.World.Data.Units.Length; t++)
            {
                UnitDef def = sim.World.Data.Units[t];
                if (def.Faction != sim.World.FactionOf(p)) continue;
                int max = ai.View.MaxHpOf(t);
                decimal threshold = fraction * max;
                if (threshold != Math.Floor(threshold)) continue;
                int at = (int)threshold;
                checkedTypes++;
                if (MilitaryManager.Wounded(ai, new SeenUnit(default, p, t, Vector2.Zero, at, UnitState.Idle)))
                    faults.Add($"{def.Key} ({max} hp) at exactly {at} hp counts as wounded");
                if (!MilitaryManager.Wounded(ai, new SeenUnit(default, p, t, Vector2.Zero, at - 1, UnitState.Idle)))
                    faults.Add($"{def.Key} ({max} hp) at {at - 1} hp is not wounded");
            }
        }
        _out.WriteLine($"{checkedTypes} types with a whole-hp threshold checked");
        Assert.True(checkedTypes > 0);
        Assert.True(faults.Count == 0, string.Join("; ", faults));
    }

    /// <summary>
    /// The order-churn guard (BUG-0595/0596) under focus fire: 20 Hard Heavy Infantry meet 20 Raiders (combat on). No member
    /// is sent Attack on one enemy more than 4 times, and no member more than 20 Attacks in all over the fight.
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Hard)]
    [InlineData(AiDifficulty.Normal)] // the baseline: no unit Attack at all
    public void FocusFire_In20v20Melee_OrdersPerMemberAndTarget_AreBounded(AiDifficulty difficulty)
    {
        Simulation sim = Scene(difficulty, combat: true);
        List<EntityHandle> members = Army(sim, 20, 14, 26);
        List<EntityHandle> raiders = Army(sim, 20, 36, 26, owner: 1, type: Raider);
        List<Command> log = AiScenes.Log(sim, 0);
        int thinks = 0;
        sim.AiOf(0)!.ThoughtForTests = _ => thinks++;
        int t = 0;
        for (; t < 4000 && members.Any(m => sim.World.Units.IsAlive(m)) && raiders.Any(r => sim.World.Units.IsAlive(r)); t++) sim.Tick();
        var attacks = log.Where(c => c.Kind == CommandKind.Attack && !c.TargetIsBuilding).ToList();
        int maxPair = attacks.GroupBy(c => (c.Unit, c.Target)).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        int maxMember = attacks.GroupBy(c => c.Unit).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        _out.WriteLine($"{t} ticks, {thinks} thinks, {attacks.Count} unit Attacks, max per member+target {maxPair}, max per member {maxMember}; " +
            $"alive: Hard {members.Count(m => sim.World.Units.IsAlive(m))}, raiders {raiders.Count(r => sim.World.Units.IsAlive(r))}; other commands {log.Count - attacks.Count}");
        if (difficulty != AiDifficulty.Hard)
        {
            Assert.Empty(attacks);
            return;
        }
        Assert.NotEmpty(attacks);
        Assert.True(maxPair <= 4, $"a member was sent Attack on one enemy {maxPair} times");
        Assert.True(maxMember <= 20, $"a member was sent {maxMember} Attacks over one fight of 20 enemies");
    }

    /// <summary>
    /// BUG-1121: focus fire must not lose a fight Normal wins. 20 Heavy Infantry of player 0 (AI) meet 20 Raiders standing
    /// still (combat on, the flat scene): on Normal the wave kills every Raider; on Hard it must too. Before the fix Hard lost
    /// all 20 with 13 Raiders left (the members, each under an explicit Attack on the one focus, held it while the Raiders
    /// hit them).
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void A20HeavyWave_Against20Raiders_WinsOnEveryDifficulty(AiDifficulty difficulty)
    {
        Simulation sim = Scene(difficulty, combat: true);
        List<EntityHandle> members = Army(sim, 20, 14, 26);
        List<EntityHandle> raiders = Army(sim, 20, 36, 26, owner: 1, type: Raider);
        int t = 0;
        for (; t < 4000 && members.Any(m => sim.World.Units.IsAlive(m)) && raiders.Any(r => sim.World.Units.IsAlive(r)); t++) sim.Tick();
        int alive = members.Count(m => sim.World.Units.IsAlive(m)), left = raiders.Count(r => sim.World.Units.IsAlive(r));
        _out.WriteLine($"{difficulty}: {t} ticks, Heavy Infantry alive {alive}, Raiders alive {left}");
        Assert.True(left == 0 && alive > 0, $"{difficulty} lost the fight: {alive} Heavy Infantry alive, {left} Raiders alive");
    }

    /// <summary>
    /// Pull-back, the whole wave wounded: every member is sent home once and the wave ends with its cooldown. BUG-1123's rule
    /// (fix round 2; the row asserted "nobody goes out again" before it): the next wave takes all four out again, each flagged
    /// <see cref="MilitaryManager.FightsOn"/> and sent one AttackMove, and none is sent home a second time.
    /// </summary>
    [Fact]
    public void PullBack_TheWholeWaveWounded_EndsTheWave_ThenTheNextWaveTakesThemAndKeepsThem()
    {
        Simulation sim = Scene(AiDifficulty.Hard, combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> members = Army(sim, 4, 18, 28);
        NextThink(sim);
        Assert.True(ai.Board.WaveActive);
        List<Command> log = AiScenes.Log(sim, 0);
        foreach (EntityHandle m in members) sim.World.Units.Hp[m.Index] = 20;
        NextThink(sim);
        Assert.False(ai.Board.WaveActive, "a wave of only wounded members went on");
        foreach (EntityHandle m in members)
            Assert.Equal(CommandKind.Move, Assert.Single(log, c => c.Unit == m).Kind);
        for (int k = 0; k < 30 && ai.Board.WavesStarted < 2; k++) NextThink(sim);
        Assert.Equal(2, ai.Board.WavesStarted);
        for (int k = 0; k < 6; k++) NextThink(sim);
        foreach (EntityHandle m in members)
        {
            Assert.Equal(m.Generation, ai.Board.WaveGeneration[m.Index]);
            Assert.NotEqual(0, ai.Board.WaveFlag[m.Index] & MilitaryManager.FightsOn);
            Assert.Equal(1, log.Count(c => c.Unit == m && c.Kind == CommandKind.Move));
            Assert.Equal(1, log.Count(c => c.Unit == m && c.Kind == CommandKind.AttackMove));
        }
    }

    /// <summary>
    /// The wave gate with 3 wounded + 2 healthy (a wave needs 4). BUG-1123's rule (fix round 2; before it the wounded didn't
    /// count and no wave started): all five go, the start half-pop theirs, the three wounded flagged
    /// <see cref="MilitaryManager.FightsOn"/> and the two healthy not.
    /// </summary>
    [Fact]
    public void WaveGate_ThreeWoundedAndTwoHealthy_StartsAWaveOfAllFive_TheWoundedFightingItOut()
    {
        Simulation sim = Scene(AiDifficulty.Hard, combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> wounded = Army(sim, 3, 18, 28);
        foreach (EntityHandle h in wounded) sim.World.Units.Hp[h.Index] = 38; // below 39 = 0.3 x 130
        List<EntityHandle> healthy = Army(sim, 2, 18, 30);
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 10 && ai.Board.WavesStarted == 0; k++) NextThink(sim);
        Assert.Equal(1, ai.Board.WavesStarted);
        Assert.Equal(5 * TestSim.Data.Units[Heavy].HalfPop, ai.Board.WaveStartHalfPop);
        foreach (EntityHandle h in healthy)
        {
            Assert.Equal(h.Generation, ai.Board.WaveGeneration[h.Index]);
            Assert.Equal(0, ai.Board.WaveFlag[h.Index] & MilitaryManager.FightsOn);
        }
        foreach (EntityHandle h in wounded)
        {
            Assert.Equal(h.Generation, ai.Board.WaveGeneration[h.Index]);
            Assert.NotEqual(0, ai.Board.WaveFlag[h.Index] & MilitaryManager.FightsOn);
            Assert.Contains(log, c => c.Unit == h && c.Kind == CommandKind.AttackMove);
        }
    }

    /// <summary>A pulled-back member hit again at home (its hp cut further while it walks back and once there): sent home once, never re-sent out, no command churn.</summary>
    [Fact]
    public void PullBack_AMemberHitAgainAtHome_IsSentHomeOnce()
    {
        Simulation sim = Scene(AiDifficulty.Hard, combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> members = Army(sim, 4, 18, 28);
        NextThink(sim);
        List<Command> log = AiScenes.Log(sim, 0);
        sim.World.Units.Hp[members[0].Index] = 30;
        NextThink(sim);
        for (int k = 0; k < 40; k++)
        {
            NextThink(sim);
            if (k % 5 == 0 && sim.World.Units.Hp[members[0].Index] > 2) sim.World.Units.Hp[members[0].Index] -= 2;
        }
        Assert.True(sim.World.Units.IsAlive(members[0]));
        List<Command> mine = log.Where(c => c.Unit == members[0]).ToList();
        _out.WriteLine($"commands to the wounded member: {string.Join(", ", mine.Select(c => $"{c.Tick}:{c.Kind}"))}");
        Assert.Equal(CommandKind.Move, mine[0].Kind);
        Assert.True(mine.Count <= 2, $"{mine.Count} commands to a wounded unit at home");
        Assert.DoesNotContain(mine, c => c.Kind is CommandKind.AttackMove or CommandKind.Attack);
    }

    /// <summary>
    /// The knobs at their edges play without fault: <c>ai.focusReach</c> 1 and 64 m, <c>retreat.hpFraction</c> 0 (nobody is
    /// ever wounded) and 1 (any scratch is): the focus scene on Hard, combat on, 2,400 ticks; every focus a live enemy unit the
    /// seat sees (a focus at all from 14 m up); two runs hash-equal. Prints the damaged members sent home mid-wave.
    /// </summary>
    [Theory]
    [InlineData(1.0, 0.3)]
    [InlineData(64.0, 0.3)]
    [InlineData(14.0, 0.0)]
    [InlineData(14.0, 1.0)]
    public void TheKnobsAtTheirEdges_PlayWithoutFault(double reach, double fraction)
    {
        GameData data = AiScenes.DataWith(dir =>
        {
            int half = TestSim.Data.Units[Heavy].HalfPop;
            dir.EditJson("factions/malazan/ai.json", root =>
            {
                foreach (System.Text.Json.Nodes.JsonNode? o in root["openings"]!.AsArray())
                {
                    o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
                    o["attack"] = new System.Text.Json.Nodes.JsonObject { ["minArmyPop"] = 4 * half / 2.0, ["minArmyCost"] = 0 };
                }
                root["difficulties"]!["hard"]!["waveSizeMultiplier"] = 1;
                root["retreat"]!["hpFraction"] = fraction;
            });
            dir.EditJson("common/rules.json", r =>
            {
                r["ai"]!["scoutAfter"] = 3600;
                r["ai"]!["waveCooldown"] = 5;
                r["ai"]!["focusReach"] = reach;
            });
        });
        ulong Run(out int picks, out int pulls, out string? bad)
        {
            Simulation sim = Scene(AiDifficulty.Hard, combat: true, data);
            Army(sim, 8, 18, 28);
            Army(sim, 8, 36, 27, owner: 1, type: Raider);
            World w = sim.World;
            AiPlayer ai = sim.AiOf(0)!;
            int p = 0, q = 0;
            string? fault = null;
            ai.ThoughtForTests = a =>
            {
                EntityHandle f = a.FocusTarget;
                if (f.Generation == 0) return;
                p++;
                if (!(w.Units.IsAlive(f) && w.IsEnemy(0, w.Units.Owner[f.Index]) && w.Fog.CanSeeUnit(0, f.Index))) fault ??= $"tick {a.Now}: focus {f.Index} not a seen enemy";
            };
            ai.SentForTests = c =>
            {
                if (c.Kind == CommandKind.Move && ai.Board.WaveActive && ai.Board.WaveGeneration[c.Unit.Index] == c.Unit.Generation
                    && w.Units.Hp[c.Unit.Index] < w.MaxHp(c.Unit.Index)) q++;
            };
            var h = new StateHasher();
            for (int t = 0; t < 2400; t++)
            {
                sim.Tick();
                h.Add(sim.StateHash());
            }
            (picks, pulls, bad) = (p, q, fault);
            return h.Value;
        }
        ulong first = Run(out int picks, out int pulls, out string? bad);
        ulong second = Run(out _, out _, out _);
        _out.WriteLine($"focusReach {reach}, hpFraction {fraction}: {picks} picks, {pulls} damaged members sent home mid-wave, digest {first:X16}");
        Assert.True(bad == null, bad);
        Assert.Equal(first, second);
        if (reach >= 14.0) Assert.True(picks > 0, "no focus at all");
    }

    /// <summary>
    /// The derived gather rate across a save: a Hard AI seat's rates are 1.25 x the data's (a human seat's the data's, bit for
    /// bit), equal after a load, and the loaded match follows the run hash for hash for 1,000 ticks.
    /// </summary>
    [Fact]
    public void AHardSeat_SavedAndLoaded_KeepsItsGatherRate_AndFollowsTheRun()
    {
        Simulation sim = AiScenes.Flat(new[] { new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard), new PlayerSeat(1, SeatController.Human, AiDifficulty.Normal) });
        RulesDef r = sim.World.Data.Rules;
        Assert.Equal(r.GoldPerTick * 1.25f, sim.World.GoldPerTickOf[0]);
        Assert.Equal(r.WoodPerTick * 1.25f, sim.World.WoodPerTickOf[0]);
        Assert.Equal(BitConverter.SingleToInt32Bits(r.GoldPerTick), BitConverter.SingleToInt32Bits(sim.World.GoldPerTickOf[1]));
        for (int t = 0; t < 1537; t++) sim.Tick(); // mid-load: workers carrying part loads
        UnitStore u = sim.World.Units;
        Assert.Contains(Enumerable.Range(0, u.Capacity), i => u.Alive[i] && u.Owner[i] == 0 && u.GatherProgress[i] > 0f && u.GatherProgress[i] < 1f);
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation load = SaveTestRun.Load(TestSim.Data, bytes);
        Assert.Equal(sim.World.GoldPerTickOf, load.World.GoldPerTickOf);
        Assert.Equal(sim.World.WoodPerTickOf, load.World.WoodPerTickOf);
        for (int t = 0; t < 1000; t++)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
    }

    /// <summary>
    /// The fog oracle on a 4-seat Hard Pale Hills match (seed 1, 20,000 ticks or to the end): at every think of every seat a
    /// focus is a live unit of an enemy of the seat that its fog grants it, and every unit Attack a seat sends is on a live
    /// enemy unit it sees then. Also prints the leaves (a healthy member sent home while its wave goes on) per seat.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(AiDifficulty.Hard)]
    [InlineData(AiDifficulty.Normal)] // the leaves' baseline: no focus, nobody pulled
    public void PaleHills_FourSeats_TheFocusIsAlwaysSeen(AiDifficulty difficulty)
    {
        PlayerSeat[] seats = Enumerable.Range(0, 4).Select(p => new PlayerSeat(p % 2, SeatController.Ai, difficulty)).ToArray();
        Simulation sim = AiScenes.Generated(seats, 1, false, out _, units: 1000, commands: 2048,
            configure: c => c with { MapId = MapFileTests.PaleHills, Map = c.Map with { Width = 256, Height = 256 } });
        World w = sim.World;
        string? bad = null;
        int picks = 0, attacks = 0;
        var leaves = new int[4];
        var pulls = new int[4];
        var free = new int[4]; // round 1: AttackMoves to a member flagged Focused (BUG-1121's free re-send)
        var orders = new Dictionary<EntityHandle, int>(); // Attack + AttackMove per unit, over the match
        for (int p = 0; p < 4; p++)
        {
            int seat = p;
            AiPlayer ai = sim.AiOf(p)!;
            ai.ThoughtForTests = a =>
            {
                EntityHandle f = a.FocusTarget;
                if (f.Generation == 0) return;
                picks++;
                bool ok = w.Units.IsAlive(f) && w.IsEnemy(seat, w.Units.Owner[f.Index]) && w.Fog.CanSeeUnit(seat, f.Index);
                if (!ok) bad ??= $"tick {a.Now}: seat {seat}'s focus {f.Index} is not a live enemy unit it sees";
            };
            ai.SentForTests = c =>
            {
                int i = c.Unit.Index;
                if (c.Kind is CommandKind.Attack or CommandKind.AttackMove) orders[c.Unit] = orders.GetValueOrDefault(c.Unit) + 1;
                if (c.Kind == CommandKind.AttackMove && ai.Board.WaveGeneration[i] == c.Unit.Generation && MilitaryManager.Owed(ai.Board, i) == MilitaryManager.Focused) free[seat]++;
                if (c.Kind == CommandKind.Attack && !c.TargetIsBuilding)
                {
                    attacks++;
                    bool ok = w.Units.IsAlive(c.Target) && w.IsEnemy(seat, w.Units.Owner[c.Target.Index]) && w.Fog.CanSeeUnit(seat, c.Target.Index);
                    if (!ok) bad ??= $"tick {sim.TickNumber}: seat {seat} sent Attack on {c.Target.Index}, not a live enemy unit it sees";
                }
                if (c.Kind == CommandKind.Move && ai.Board.WaveGeneration[i] == c.Unit.Generation && ai.Board.WaveActive && w.Units.IsAlive(c.Unit))
                {
                    if (w.Units.Hp[i] < ai.Def.RetreatHpFraction * w.MaxHp(i)) pulls[seat]++;
                    else leaves[seat]++;
                }
            };
        }
        while (sim.TickNumber < 20_000 && !sim.MatchOver && bad == null) sim.Tick();
        _out.WriteLine($"tick {sim.TickNumber}, over {sim.MatchOver}: {picks} focus picks, {attacks} unit Attacks checked; pulls {string.Join("/", pulls)}, leaves {string.Join("/", leaves)}; " +
            $"free re-sends {string.Join("/", free)}; Attack + AttackMove per unit: max {orders.Values.DefaultIfEmpty(0).Max()}, units {orders.Count}, total {orders.Values.Sum()}");
        Assert.True(bad == null, bad);
        if (difficulty == AiDifficulty.Hard) Assert.True(picks > 0, "nobody focused");
        else Assert.Equal((0, 0), (picks, attacks)); // a Normal seat focuses nothing (its "pulls" are the old Leave of a member below the fraction)
    }
}
