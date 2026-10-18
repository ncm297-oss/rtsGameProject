using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M5-4b, session 2026-10-13-0916): the AI's base defense (<see cref="DefenseManager"/>, docs/03 "Implementation
/// (M5-4b)") attacked: flickering threats, the radius edge, a wave pulled home and restarting, the army away in the enemy
/// base (BUG-0670), a parked raider (BUG-0671), a worker fleeing with cargo, the knobs' edges. The flat AI scene: player 0
/// (AI, malazan) has its hall at anchor (8, 30); enemies are player 1's Raiders placed and spotted by the test. Knobs set
/// by the test's own rules.json (rule 12).
/// </summary>
public class DefenseQaTests
{
    private readonly ITestOutputHelper _out;

    public DefenseQaTests(ITestOutputHelper output) => _out = output;

    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    private static GameData Data(double wavePop = 100, double cooldown = 60, double defendFor = 20, double defendRadius = 48) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
            AiScenes.Opening(root)["attack"] = new JsonObject { ["minArmyPop"] = wavePop, ["minArmyCost"] = 0 };
            root["difficulties"]!["normal"]!["waveSizeMultiplier"] = 1;
        });
        dir.EditJson("common/rules.json", r =>
        {
            JsonNode ai = r["ai"]!;
            ai["waveCooldown"] = cooldown;
            ai["defendRadius"] = defendRadius;
            ai["defendFor"] = defendFor;
            ai["workerFleeRadius"] = 16;
            ai["rallyReach"] = 6;
            ai["scoutAfter"] = 3600;
            // M5-4f (re-staged by the dev): these rows attack the trigger, the point, BUG-0670 / 0671 / 0673 and the knobs, not
            // the sized answer, and were written when every free unit answered. The largest defendMinValue (5,000, above any
            // army here) keeps that: every free unit answers, and an unseen hit outweighs any home army (the wave comes home).
            // The sizing has its own rows (ProportionalDefenseTests); the knob-edge Theory below keeps the shipped ones.
            ai["defendMinValue"] = 5000;
        });
    });

    private static readonly Lazy<GameData> s_quiet = new(() => Data());

    private static Simulation Scene(GameData data, bool combat = false, int workers = 5)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data, combat: combat, workers: workers);
        BuildMaps.SetTotals(sim, 0, 0, 0); // nothing trained meanwhile
        return sim;
    }

    private static Vector2 Hall(Simulation sim, int player = 0)
    {
        (int x, int y) = AiScenes.HallAt(player);
        BuildingDef d = sim.World.Data.Buildings[sim.World.Buildings.TypeId[sim.World.Buildings.SlotAt(x, y)]];
        return new Vector2((x + d.FootprintWidth * 0.5f) * MapConstants.CellSize, (y + d.FootprintHeight * 0.5f) * MapConstants.CellSize);
    }

    private static List<EntityHandle> Army(Simulation sim, int n, int x, int y)
    {
        var list = new List<EntityHandle>();
        for (int k = 0; k < n; k++) list.Add(CombatScenes.Place(sim, 0, Heavy, sim.World.NavGrid.CellCenter(x + k % 6, y + k / 6)));
        return list;
    }

    private static EntityHandle Enemy(Simulation sim, Vector2 at)
    {
        EntityHandle h = CombatScenes.Place(sim, 1, CombatScenes.Raider, at);
        CombatScenes.Spot(sim, 0, h);
        sim.World.Units.Hold[h.Index] = true;
        return h;
    }

    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    /// <summary>
    /// A threat seen one think and gone (out of the radius, still seen) the next, eight times over, within defendFor: one
    /// AttackMove per army unit in all, never one each think.
    /// </summary>
    [Fact]
    public void AFlickeringThreat_AtOneSpot_SendsTheArmyOnce()
    {
        Simulation sim = Scene(s_quiet.Value);
        World w = sim.World;
        List<EntityHandle> army = Army(sim, 3, 2, 50);
        Vector2 inside = Hall(sim) + new Vector2(0f, 30f), outside = new(120f, 120f);
        EntityHandle e = Enemy(sim, inside);
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 8; k++)
        {
            w.Units.Position[e.Index] = k % 2 == 0 ? inside : outside;
            NextThink(sim);
        }
        Assert.Equal(army.Count, log.Count(c => c.Kind == CommandKind.AttackMove));
        Assert.Equal(1, sim.AiOf(0)!.Board.Defenses);
    }

    /// <summary>
    /// Two raiders taking turns in the radius at opposite sides of the hall (60 m apart), one think each, for ten thinks: the
    /// point moves past rallyReach every think, so the whole army is re-sent every think and walks back and forth between
    /// the two (BUG-0673). The QA focus: a flickering threat must not re-issue orders each think.
    /// </summary>
    [Fact]
    public void TwoRaidersTakingTurns_DoNotReSendTheArmyEveryThink()
    {
        Simulation sim = Scene(s_quiet.Value);
        World w = sim.World;
        List<EntityHandle> army = Army(sim, 3, 2, 50);
        Vector2 north = Hall(sim) + new Vector2(0f, -30f), south = Hall(sim) + new Vector2(0f, 30f), away = new(120f, 120f);
        EntityHandle a = Enemy(sim, north), b = Enemy(sim, away);
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 10; k++)
        {
            w.Units.Position[a.Index] = k % 2 == 0 ? north : away;
            w.Units.Position[b.Index] = k % 2 == 0 ? away : south;
            NextThink(sim);
        }
        int sent = log.Count(c => c.Kind == CommandKind.AttackMove);
        _out.WriteLine($"{sent} AttackMoves to {army.Count} units over 10 thinks; defense versions {sim.AiOf(0)!.Board.DefenseVersion}");
        Assert.True(sent <= 2 * army.Count, $"{sent} AttackMoves to {army.Count} units in 10 thinks: re-sent every think");
    }

    /// <summary>The radius edge from the hall's centre: an enemy 47 m and 48 m out triggers, 49 m doesn't (each on its own scene).</summary>
    [Theory]
    [InlineData(47f, true)]
    [InlineData(48f, true)]
    [InlineData(49f, false)]
    public void AThreatAtTheRadiusEdge(float metres, bool triggers)
    {
        Simulation sim = Scene(s_quiet.Value);
        Army(sim, 2, 2, 50);
        Enemy(sim, Hall(sim) + new Vector2(metres, 0f));
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        NextThink(sim);
        Assert.Equal(triggers, sim.AiOf(0)!.Board.Defenses > 0);
        Assert.Equal(triggers ? 2 : 0, log.Count(c => c.Kind == CommandKind.AttackMove));
    }

    /// <summary>
    /// A wave mid-march is called home by a threat (ended, the cooldown set, no wave while it holds), and the wave rule starts
    /// a new wave once the threat is over (defendFor 2 s) and the cooldown (1 s) has run.
    /// </summary>
    [Fact]
    public void AWavePulledHomeMidMarch_StartsAgain_AfterDefendFor()
    {
        Simulation sim = Scene(Data(wavePop: 4 * TestSim.Data.Units[Heavy].HalfPop / 2.0, cooldown: 1, defendFor: 2));
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        Assert.True(w.Buildings.Spawn(0, w.Data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out _));
        Army(sim, 4, 18, 28);
        NextThink(sim);
        Assert.True(ai.Board.WaveActive);
        for (int k = 0; k < 3; k++) NextThink(sim); // marching
        EntityHandle e = Enemy(sim, Hall(sim) + new Vector2(0f, 30f));
        NextThink(sim);
        Assert.False(ai.Board.WaveActive);
        Assert.True(ai.Board.Defending);
        int heldFrom = sim.TickNumber;
        for (int k = 0; k < 5; k++)
        {
            NextThink(sim);
            Assert.False(ai.Board.WaveActive, "a wave started while the threat held");
        }
        w.Units.Free(e);
        int ended = -1;
        for (int k = 0; k < 40 && ai.Board.WavesStarted < 2; k++)
        {
            NextThink(sim);
            if (ended < 0 && !ai.Board.Defending) ended = sim.TickNumber;
        }
        _out.WriteLine($"threat from {heldFrom}, over at {ended}, waves {ai.Board.WavesStarted} at {sim.TickNumber}");
        Assert.True(ended > 0, "the threat never ended");
        Assert.Equal(2, ai.Board.WavesStarted);
    }

    /// <summary>
    /// BUG-0670: the army is away in the enemy base (attacking player 1's hall, combat on) when a raider holds 20 m from
    /// player 0's hall. Before the fix the defense's AttackMove acquired the enemy hall they stood by and two of the three
    /// fought on there for the whole 60 s threat. docs/02 "AI opponent": defend the base when attacked, first. The check:
    /// within 60 s, at a tick **while the threat still holds** (so the rally rule after it can't be what brought them), all
    /// three are alive within defendRadius of home; and the raider dies (they defended, not just stood home). Round 1
    /// (fix 2a3da00): the army now comes home and kills the raider, so the threat ends before 60 s; the row samples during
    /// the threat instead of at its end (the old precondition "still defending at 60 s" no longer holds by design).
    /// </summary>
    [Fact]
    public void TheArmyAwayInTheEnemyBase_ComesHomeToDefend()
    {
        Simulation sim = Scene(s_quiet.Value, combat: true);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        EntityHandle hall1 = w.Buildings.HandleOf(w.Buildings.SlotAt(AiScenes.HallAt(1).X, AiScenes.HallAt(1).Y));
        List<EntityHandle> army = Army(sim, 3, 50, 37);
        for (int t = 0; t < 3; t++) sim.Tick(); // the AI's first think rallies them (idle); the Attack comes after
        foreach (EntityHandle h in army) sim.Enqueue(Command.Attack(0, h, hall1, isBuilding: true));
        CombatScenes.RunUntil(sim, () => army.All(h => w.Units.State[h.Index] == UnitState.Attacking), 800);
        Assert.True(army.All(h => w.Units.State[h.Index] == UnitState.Attacking),
            "not all attacking the hall: " + string.Join("; ", army.Select(h => $"{w.Units.State[h.Index]} at {w.Units.Position[h.Index]} tgt {w.Units.Target[h.Index]}")));
        EntityHandle raider = Enemy(sim, Hall(sim) + new Vector2(0f, -20f));
        int start = sim.TickNumber, homeAt = -1;
        bool Home(EntityHandle h) => w.Units.Alive[h.Index] && w.Units.Generation[h.Index] == h.Generation
            && Vector2.Distance(w.Units.Position[h.Index], Hall(sim)) <= ai.Rules.DefendRadius;
        for (int t = 0; t < 1200; t++)
        {
            sim.Tick();
            if (homeAt < 0 && ai.Board.Defending && army.All(Home)) homeAt = sim.TickNumber;
        }
        string where = string.Join("; ", army.Select(h => $"{w.Units.State[h.Index]} {Vector2.Distance(w.Units.Position[h.Index], Hall(sim)):F0} m from home"));
        bool raiderDead = !(w.Units.Alive[raider.Index] && w.Units.Generation[raider.Index] == raider.Generation);
        _out.WriteLine($"threat from {start}; all home while defending at {homeAt}; raider dead {raiderDead}; defending at the end {ai.Board.Defending}; {where}");
        Assert.True(homeAt > 0, $"not all {army.Count} soldiers home while the threat held within 60 s: {where}");
        Assert.True(raiderDead, $"the raider 20 m from the hall still lives after 60 s: {where}");
    }

    /// <summary>
    /// BUG-0673 variant: three raiders taking turns at three spots round the hall (120 degrees apart, 30 m out), one think
    /// each, for twelve thinks. The fix moves the point on the second trigger in a row away from it; with three spots every
    /// trigger is away from the current point, so it measures how often the army is re-sent. Reported, gated loosely: no
    /// more than one re-send per two thinks (the two-spot case is gated at 2x by the row above).
    /// </summary>
    [Fact]
    public void ThreeRaidersTakingTurns_ReSendTheArmyAtMostEveryOtherThink()
    {
        Simulation sim = Scene(s_quiet.Value);
        World w = sim.World;
        List<EntityHandle> army = Army(sim, 3, 2, 50);
        Vector2 hall = Hall(sim), away = new(120f, 120f);
        Vector2[] spots = { hall + new Vector2(0f, -30f), hall + new Vector2(26f, 15f), hall + new Vector2(-26f, 15f) };
        // Keep the spots on the map (the hall is near the west edge).
        for (int i = 0; i < 3; i++) spots[i] = Vector2.Clamp(spots[i], new Vector2(2f, 2f), new Vector2(AiScenes.FlatSize * MapConstants.CellSize - 2f));
        var raiders = new EntityHandle[3];
        for (int i = 0; i < 3; i++) raiders[i] = Enemy(sim, away + new Vector2(i * 2f, 0f));
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 12; k++)
        {
            for (int i = 0; i < 3; i++) w.Units.Position[raiders[i].Index] = k % 3 == i ? spots[i] : away + new Vector2(i * 2f, 0f);
            NextThink(sim);
        }
        int sent = log.Count(c => c.Kind is CommandKind.AttackMove or CommandKind.Move && army.Contains(c.Unit));
        _out.WriteLine($"{sent} army orders to {army.Count} units over 12 thinks; defense versions {sim.AiOf(0)!.Board.DefenseVersion}");
        Assert.True(sent <= 7 * army.Count, $"{sent} army orders to {army.Count} units in 12 thinks");
    }

    /// <summary>
    /// The dev-flagged rule of the BUG-0670 fix: units on the Move home don't answer hits. Three Heavy Infantry are fighting
    /// three Raiders (not holding) in the enemy base when a fourth Raider holds 20 m from player 0's hall. Reported (the
    /// Producer's call whether a chased army should turn): the soldiers alive when they get home and the enemy losses on the
    /// way. Gated only on the scene doing what it says (the Move home is sent).
    /// </summary>
    [Fact]
    public void AnArmyChasedOnItsMoveHome_Reported()
    {
        Simulation sim = Scene(s_quiet.Value, combat: true);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> army = Army(sim, 3, 50, 37);
        NextThink(sim);
        var chasers = new List<EntityHandle>();
        for (int k = 0; k < 3; k++)
        {
            EntityHandle c = CombatScenes.Place(sim, 1, CombatScenes.Raider, w.NavGrid.CellCenter(50 + k, 35));
            CombatScenes.Spot(sim, 0, c);
            chasers.Add(c);
            sim.Enqueue(Command.Attack(1, c, army[k], isBuilding: false));
        }
        for (int t = 0; t < 60; t++) sim.Tick(); // the fight starts
        Enemy(sim, Hall(sim) + new Vector2(0f, -20f));
        List<Command> log = AiScenes.Log(sim, 0);
        int homeAt = -1;
        for (int t = 0; t < 1200 && homeAt < 0; t++)
        {
            sim.Tick();
            if (army.All(h => !w.Units.Alive[h.Index] || w.Units.Generation[h.Index] != h.Generation
                    || Vector2.Distance(w.Units.Position[h.Index], Hall(sim)) <= ai.Rules.DefendRadius)) homeAt = sim.TickNumber;
        }
        int alive = army.Count(h => w.Units.Alive[h.Index] && w.Units.Generation[h.Index] == h.Generation);
        int chasersAlive = chasers.Count(h => w.Units.Alive[h.Index] && w.Units.Generation[h.Index] == h.Generation);
        _out.WriteLine($"orders {string.Join(", ", log.Where(c => army.Contains(c.Unit)).Select(c => $"{c.Kind}@{c.Tick}"))}; "
            + $"home (or dead) at {homeAt}; soldiers alive {alive}/3; chasers alive {chasersAlive}/3");
        Assert.Contains(log, c => c.Kind == CommandKind.Move && army.Contains(c.Unit));
    }

    /// <summary>
    /// BUG-0670 fix at the knob's low edge: with defendRadius 8 m (the minimum) "inside the base" is 8 m from the hall's
    /// centre, so an army waiting 20 m out (its usual rally distance) is "outside" and gets the plain Move, which doesn't
    /// answer hits. A raider holding 6 m from the hall (combat on): within 30 s the army must still kill it.
    /// </summary>
    [Fact]
    public void ASmallDefendRadius_TheArmyOnItsMoveHome_StillKillsTheRaider()
    {
        Simulation sim = Scene(Data(defendRadius: 8), combat: true);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> army = Army(sim, 3, 18, 40);
        NextThink(sim);
        EntityHandle raider = Enemy(sim, Hall(sim) + new Vector2(6f, 0f));
        List<Command> log = AiScenes.Log(sim, 0);
        for (int t = 0; t < 600; t++) sim.Tick();
        bool dead = !(w.Units.Alive[raider.Index] && w.Units.Generation[raider.Index] == raider.Generation);
        string orders = string.Join(", ", log.Where(c => army.Contains(c.Unit)).Select(c => $"{c.Kind}@{c.Tick}"));
        _out.WriteLine($"army orders: {orders}; raider dead {dead}; army states "
            + string.Join("; ", army.Select(h => $"{w.Units.State[h.Index]} {Vector2.Distance(w.Units.Position[h.Index], Hall(sim)):F0} m")));
        Assert.True(dead, $"the raider 6 m from the hall lives after 30 s; orders {orders}");
    }

    /// <summary>
    /// BUG-0671: one enemy soldier parks (holds, combat off so nobody dies) 12 m from the hall by the forest. The workers
    /// near it flee to the hall and the economy leaves them alone while the threat holds, which is for good: no Gather is
    /// sent for 100 s, though the two gold mines on the far side of the hall are out of the raider's reach.
    /// </summary>
    [Fact]
    public void AParkedRaider_DoesNotStopTheEconomyForGood()
    {
        Simulation sim = Scene(s_quiet.Value);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        for (int k = 0; k < 10; k++) NextThink(sim); // the workers gather
        Enemy(sim, Hall(sim) + new Vector2(0f, 14f));
        List<Command> log = AiScenes.Log(sim, 0);
        for (int t = 0; t < 2000; t++) sim.Tick();
        int moves = log.Count(c => c.Kind == CommandKind.Move), gathers = log.Count(c => c.Kind == CommandKind.Gather);
        int idleFled = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Units.State[i] == UnitState.Idle && ai.IsFleeing(i, w.Units.Generation[i])) idleFled++;
        _out.WriteLine($"flee Moves {moves}, Gathers after {gathers}, fled workers idle at the end {idleFled}, defending {ai.Board.Defending}");
        Assert.True(moves > 0, "nobody fled: the scene proves little");
        Assert.True(idleFled == 0, $"{idleFled} of the {moves} fled workers stand idle 100 s later: the economy leaves them alone while the raider stays");
    }

    /// <summary>
    /// A worker fleeing with cargo: the single worker, carrying gold at the mine, flees from a raider placed beside it; the
    /// threat ends (the raider removed, defendFor 2 s) and it goes back to work. Over the whole run no load vanishes: every
    /// fall of its cargo is matched by the same rise of the player's gold and wood.
    /// </summary>
    [Fact]
    public void AWorkerFleeingWithCargo_LosesNoLoad()
    {
        Simulation sim = Scene(Data(defendFor: 2), workers: 1);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        EntityHandle worker = default;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0) worker = new EntityHandle(i, w.Units.Generation[i]);
        int lost = 0, fled = 0, carried = 0;
        EntityHandle raider = default;
        int prevCargo = 0, prevBank = w.Gold[0] + w.Wood[0];
        for (int t = 0; t < 3000; t++)
        {
            sim.Tick();
            int cargo = w.Units.Cargo[worker.Index], bank = w.Gold[0] + w.Wood[0];
            if (cargo < prevCargo && bank - prevBank < prevCargo - cargo) lost += prevCargo - cargo - Math.Max(0, bank - prevBank);
            // Some cargo, at the mine: a raider beside it.
            if (raider == default && cargo >= 3 && w.Units.State[worker.Index] == UnitState.Gathering && t > 100)
            {
                raider = Enemy(sim, w.Units.Position[worker.Index] + new Vector2(3f, 0f));
                carried = cargo;
            }
            if (raider != default && ai.Board.FledGeneration[worker.Index] == worker.Generation) fled++;
            if (raider != default && w.Units.Alive[raider.Index] && fled > 0 && ai.Board.Defending && t % 200 == 0) w.Units.Free(raider);
            prevCargo = cargo;
            prevBank = bank;
        }
        _out.WriteLine($"carried {carried} when the raider came; fled for {fled} ticks; lost {lost}; bank {w.Gold[0]}/{w.Wood[0]}");
        Assert.True(raider != default && fled > 0, "the worker never fled: the scene proves little");
        Assert.Equal(0, lost);
        Assert.True(w.Gold[0] + w.Wood[0] > 0, "nothing gathered after the threat");
    }

    /// <summary>
    /// The knobs' edges as data: defendFor 0 (the threat holds only on the think it is seen) and defendRadius 256 (the whole
    /// 128 m map), two AI seats with combat on for 4,000 ticks: no exception, a deterministic twin, and with defendFor 0 the
    /// threat doesn't outlive its trigger.
    /// </summary>
    [Theory]
    [InlineData(0.0, 48.0)]
    [InlineData(20.0, 256.0)]
    [InlineData(0.0, 256.0)]
    public void TheKnobsAtTheirEdges_RunTwoAiSeats_Deterministically(double defendFor, double defendRadius)
    {
        GameData data = AiScenes.DataWith(dir =>
        {
            foreach (string f in new[] { "malazan", "whirlwind" })
                dir.EditJson($"factions/{f}/ai.json", root => AiScenes.Opening(root)["attack"] = new JsonObject { ["minArmyPop"] = 2, ["minArmyCost"] = 0 });
            dir.EditJson("common/rules.json", r =>
            {
                r["ai"]!["defendFor"] = defendFor;
                r["ai"]!["defendRadius"] = defendRadius;
                r["ai"]!["waveCooldown"] = 5;
                r["ai"]!["scoutAfter"] = 1;
            });
        });
        Simulation Make()
        {
            Simulation s = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), data, combat: true);
            BuildMaps.SetTotals(s, 0, 2000, 2000);
            BuildMaps.SetTotals(s, 1, 2000, 2000);
            for (int k = 0; k < 6; k++)
            {
                CombatScenes.Place(s, 0, Heavy, s.World.NavGrid.CellCenter(16 + k % 3, 24 + k / 3));
                CombatScenes.Place(s, 1, CombatScenes.Raider, s.World.NavGrid.CellCenter(46 + k % 3, 24 + k / 3));
            }
            return s;
        }
        Simulation a = Make(), b = Make();
        for (int t = 0; t < 4000; t++)
        {
            a.Tick();
            b.Tick();
            if (t % 50 == 0) Assert.Equal(a.StateHash(), b.StateHash());
            for (int p = 0; p < 2; p++)
            {
                AiPlayer ai = a.AiOf(p)!;
                if (defendFor == 0 && ai.Board.Defending) Assert.True(ai.Board.DefendTick >= ai.Now, $"tick {t}: player {p} defends past its trigger");
            }
        }
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.True(a.AiOf(0)!.Board.Defenses + a.AiOf(1)!.Board.Defenses > 0, "nobody defended: the run proves little");
        _out.WriteLine($"defendFor {defendFor}, radius {defendRadius}: defenses {a.AiOf(0)!.Board.Defenses}/{a.AiOf(1)!.Board.Defenses}, waves {a.AiOf(0)!.Board.WavesStarted}/{a.AiOf(1)!.Board.WavesStarted}");
    }
}
