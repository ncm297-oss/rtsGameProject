using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M5-3, session 2026-10-12-1930): attacks on the scout and the military manager. A wave sent at the ghost of a building
/// razed out of sight; a wave into two towers (members die, the retreat fires, the loop stays bounded); a wave start bigger
/// than the command queue (the rest go next think, once each); a scout waypoint on an unreachable plateau; a scout killed
/// while its Stop is pending; and a hash twin: two AI seats whose worlds differ only in what neither can see think alike,
/// command for command and blackboard for blackboard, every think.
/// </summary>
public class AiMilitaryQaTests
{
    private readonly ITestOutputHelper _out;

    public AiMilitaryQaTests(ITestOutputHelper output) => _out = output;

    private static int Unit(string id) => TestSim.Data.FindUnit(id);

    private static int Building(string id) => TestSim.Data.FindBuilding(id);

    /// <summary>A quiet malazan order (nothing for an hour), a wave at <paramref name="pop"/> pop and <paramref name="cost"/>, the loss fraction and cooldown given.</summary>
    private static GameData Data(double pop, int cost, double loss = 0.5, double cooldown = 60) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            root["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
            root["attack"] = new JsonObject { ["minArmyPop"] = pop, ["minArmyCost"] = cost };
            root["retreat"]!["lossFraction"] = loss;
            root["difficulties"]!["normal"]!["waveSizeMultiplier"] = 1;
        });
        dir.EditJson("common/rules.json", r => r["ai"]!["waveCooldown"] = cooldown);
    });

    private static readonly Lazy<GameData> s_onePop = new(() => Data(1, 0));

    /// <summary>
    /// Player 0 (AI, Normal, malazan) saw player 1's hall through a billet of its own at tick 0; with <paramref name="ghost"/> the
    /// billet is then removed and a fog update passes, so the hall is a ghost.
    /// </summary>
    private static Simulation Scene(GameData data, out EntityHandle hall, bool ghost, bool combat = false, int units = 256, int commands = 512)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data,
            units: units, commands: commands, combat: combat);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out EntityHandle billet));
        hall = default;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 1 && w.Buildings.TypeId[k] == data.FindBuilding("whirlwind_holy_camp"))
                hall = new EntityHandle(k, w.Buildings.Generation[k]);
        sim.Tick();
        Assert.True(sim.AiOf(0)!.Board.KnownEnemyBuildings > 0);
        if (ghost)
        {
            Assert.True(w.Buildings.Free(billet));
            do sim.Tick(); while (sim.TickNumber % 4 != 2);
            Assert.True(sim.AiOf(0)!.View.TryGetGhost(hall.Index, out _), "precondition: the hall is a ghost");
        }
        return sim;
    }

    private static List<EntityHandle> Army(Simulation sim, int type, int n, int x, int y, int wide = 6)
    {
        var list = new List<EntityHandle>();
        for (int k = 0; k < n; k++) list.Add(CombatScenes.Place(sim, 0, type, sim.World.NavGrid.CellCenter(x + k % wide, y + k / wide)));
        return list;
    }

    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    // ---------- the wave's target ----------

    /// <summary>
    /// The hall is razed while player 0 can't see it: its ghost stays. A wave sent at the ghost walks there, sees the ground empty
    /// (the ghost clears), and with no other enemy building known ends and goes home: one AttackMove each (none re-sent while
    /// walking), never an Attack (the target was never seen alive), every member a Move to the rally point, no second wave.
    /// </summary>
    [Fact]
    public void AWaveAtTheGhostOfABuildingRazedUnseen_EndsHome_WithoutResending()
    {
        Simulation sim = Scene(s_onePop.Value, out EntityHandle hall, ghost: true);
        AiPlayer ai = sim.AiOf(0)!;
        Assert.True(sim.World.Buildings.Free(hall)); // razed out of sight
        sim.Tick();
        Assert.True(ai.View.TryGetGhost(hall.Index, out _), "precondition: the razed hall is still a ghost");
        List<Command> log = AiScenes.Log(sim, 0);
        List<EntityHandle> army = Army(sim, Unit("malazan_heavy_infantry"), 4, 18, 28);
        NextThink(sim);
        Assert.True(ai.Board.WaveActive);
        Assert.Equal(hall, ai.Board.WaveTarget);
        int ended = -1;
        while (sim.TickNumber < 3000)
        {
            sim.Tick();
            if (ended < 0 && !ai.Board.WaveActive) ended = sim.TickNumber;
        }
        var mine = log.Where(c => army.Contains(c.Unit)).ToList();
        _out.WriteLine($"wave ended at tick {ended}; {mine.Count} commands to the members: " +
            string.Join(", ", mine.GroupBy(c => c.Kind).Select(g => $"{g.Key} {g.Count()}")));
        Assert.True(ended > 0, "the wave at a razed ghost never ended");
        Assert.False(ai.View.TryGetGhost(hall.Index, out _), "the ghost was never cleared");
        Assert.DoesNotContain(mine, c => c.Kind == CommandKind.Attack);
        foreach (EntityHandle h in army)
        {
            Assert.Equal(1, mine.Count(c => c.Unit == h && c.Kind == CommandKind.AttackMove));
            Assert.Equal(1, mine.Count(c => c.Unit == h && c.Kind == CommandKind.Move));
        }
        Assert.Equal(1, ai.Board.WavesStarted);
    }

    /// <summary>
    /// A wave into two towers (combat on): six Crossbowmen attack-move to player 1's hall past two Lookout Towers. Members die,
    /// the retreat fires (every live member a Move home in the same think), and over 6,000 ticks the attack-retreat loop starts
    /// at most 6,000 / cooldown + 1 waves; no unit gets more than one command a think, and no member is re-sent an AttackMove
    /// while it walks or fights (only once Idle).
    /// </summary>
    [Fact]
    public void AWaveIntoTwoTowers_Retreats_AndTheLoopStaysBounded()
    {
        GameData data = Data(1, 0, loss: 0.5, cooldown: 30);
        Simulation sim = Scene(data, out _, ghost: true, combat: true);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        int tower = data.FindBuilding("whirlwind_lookout_tower");
        Assert.True(w.Buildings.Spawn(1, tower, 25 * AiScenes.FlatSize + 45, out EntityHandle t1));
        Assert.True(w.Buildings.Spawn(1, tower, 36 * AiScenes.FlatSize + 45, out EntityHandle t2));
        List<Command> log = AiScenes.Log(sim, 0);
        List<EntityHandle> army = Army(sim, Unit("malazan_crossbowman"), 6, 18, 28);
        int retreats = 0, endedAllDead = 0, worstPerUnitPerTick = 0, resentWhileBusy = 0;
        bool wasActive = false;
        var stateAtSend = new Dictionary<(int Tick, EntityHandle Unit), UnitState>();
        while (sim.TickNumber < 6000)
        {
            // What each member was doing when the think that sends at this tick + 1 ran.
            var before = army.Where(h => w.Units.IsAlive(h)).ToDictionary(h => h, h => w.Units.State[h.Index]);
            int logged = log.Count, wavesBefore = ai.Board.WavesStarted;
            EntityHandle targetBefore = ai.Board.WaveTarget;
            sim.Tick();
            foreach (var g in log.Skip(logged).GroupBy(c => c.Unit)) worstPerUnitPerTick = Math.Max(worstPerUnitPerTick, g.Count());
            // An AttackMove that isn't a wave start or a re-target goes only to a member standing Idle.
            bool owed = ai.Board.WavesStarted != wavesBefore || ai.Board.WaveTarget != targetBefore;
            foreach (Command c in log.Skip(logged))
                if (!owed && c.Kind == CommandKind.AttackMove && before.TryGetValue(c.Unit, out UnitState s) && s != UnitState.Idle)
                    resentWhileBusy++;
            if (wasActive && !ai.Board.WaveActive)
            {
                if (army.Any(h => w.Units.IsAlive(h))) retreats++;
                else endedAllDead++;
            }
            wasActive = ai.Board.WaveActive;
        }
        int alive = army.Count(h => w.Units.IsAlive(h));
        int cooldown = ai.Rules.WaveCooldownTicks;
        _out.WriteLine($"waves {ai.Board.WavesStarted}, retreats with survivors {retreats}, ended all dead {endedAllDead}, members alive {alive}/6, " +
            $"towers alive {w.Buildings.IsAlive(t1)} {w.Buildings.IsAlive(t2)}; commands {string.Join(", ", log.Where(c => army.Contains(c.Unit)).GroupBy(c => c.Kind).Select(g => $"{g.Key} {g.Count()}"))}; " +
            $"worst per unit per tick {worstPerUnitPerTick}");
        Assert.True(alive < 6, "no member died: the towers never fought");
        Assert.True(retreats >= 1, "no retreat with survivors fired");
        Assert.InRange(ai.Board.WavesStarted, 1, 6000 / cooldown + 1);
        Assert.Equal(1, worstPerUnitPerTick);
        Assert.Equal(0, resentWhileBusy);
    }

    /// <summary>
    /// The command queue (408, as the brief's scene) is 300 commands full when a 200-unit wave starts: the AI sends what fits
    /// (108, the queue then full: nothing more that think), and the rest at the next think; every member gets exactly one
    /// AttackMove across the two thinks, none twice.
    /// </summary>
    [Fact]
    public void AWaveStartThatFillsTheQueue_SendsTheRestNextThink_OnceEach()
    {
        Simulation sim = Scene(s_onePop.Value, out _, ghost: false, units: 600, commands: 408);
        AiPlayer ai = sim.AiOf(0)!;
        // Past the economy's first thinks (idle workers to gather), so the wave's think sends only the wave.
        for (int k = 0; k < 3; k++) NextThink(sim);
        List<EntityHandle> army = Army(sim, Unit("malazan_heavy_infantry"), 200, 20, 3, wide: 10);
        List<Command> log = AiScenes.Log(sim, 0);
        int think = ai.ThinkTicks;
        while (sim.TickNumber % think != 0) sim.Tick();
        EntityHandle filler = default;
        for (int i = 0; i < sim.World.Units.Capacity && filler.Generation == 0; i++)
            if (sim.World.Units.Alive[i] && sim.World.Units.Owner[i] == 1) filler = new EntityHandle(i, sim.World.Units.Generation[i]);
        for (int k = 0; k < 300; k++) sim.Enqueue(Command.Stop(1, filler));
        Assert.Equal(300, sim.PendingCommandCount);
        sim.Tick(); // the think: 108 fit
        int first = log.Count;
        Assert.True(ai.Board.WaveActive);
        Assert.Equal(108, first);
        NextThink(sim);
        List<Command> attacks = log.Where(c => c.Kind == CommandKind.AttackMove).ToList();
        _out.WriteLine($"first think {first} sent, after the next {log.Count} ({attacks.Count} AttackMoves)");
        Assert.Equal(army.OrderBy(h => h.Index), attacks.Select(c => c.Unit).OrderBy(h => h.Index));
        Assert.Equal(2, attacks.Select(c => c.Tick).Distinct().Count());
        Assert.Equal(1, ai.Board.WavesStarted);
    }

    /// <summary>
    /// A retreat whose think finds the queue nearly full (3 free of 64): three walking members get their Move home that think,
    /// the other five at the next (the owed flags outlive the wave; they are walking, so the rally rule for Idle units would
    /// never send them). Every survivor gets exactly one Move to the rally point.
    /// </summary>
    [Fact]
    public void ARetreatThatFindsTheQueueFull_SendsTheRestHomeNextThink()
    {
        Simulation sim = Scene(s_onePop.Value, out _, ghost: false, units: 300, commands: 64);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        for (int k = 0; k < 3; k++) NextThink(sim);
        List<EntityHandle> army = Army(sim, Unit("malazan_heavy_infantry"), 20, 16, 4, wide: 10);
        NextThink(sim);
        Assert.True(ai.Board.WaveActive);
        Assert.Equal(20, army.Count(h => ai.Board.WaveGeneration[h.Index] == h.Generation));
        foreach (EntityHandle h in army.Take(12)) CombatSystem.HitUnit(w, new PendingHit(default, 1, h, false, w.Units.Hp[h.Index]));
        List<EntityHandle> survivors = army.Skip(12).ToList();
        while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
        Assert.All(survivors, h => Assert.Equal(UnitState.Moving, w.Units.State[h.Index]));
        EntityHandle filler = default;
        for (int i = 0; i < w.Units.Capacity && filler.Generation == 0; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) filler = new EntityHandle(i, w.Units.Generation[i]);
        List<Command> log = AiScenes.Log(sim, 0);
        while (sim.PendingCommandCount < 61) sim.Enqueue(Command.Stop(1, filler));
        sim.Tick(); // the retreat think: 3 fit
        Assert.False(ai.Board.WaveActive);
        int first = log.Count(c => c.Kind == CommandKind.Move);
        NextThink(sim);
        List<Command> home = log.Where(c => c.Kind == CommandKind.Move && survivors.Contains(c.Unit)).ToList();
        _out.WriteLine($"retreat think sent {first} Moves; after the next think {home.Count} for {survivors.Count} survivors");
        Assert.Equal(3, first);
        Assert.Equal(survivors.OrderBy(h => h.Index), home.Select(c => c.Unit).OrderBy(h => h.Index));
        Assert.All(home, c => Assert.Equal(ai.Board.Rally, c.Position));
    }

    /// <summary>
    /// BUG-0591: a wave member that stands Idle by the seen target and can't fight it (here its attack targets units only: the
    /// test's own units.json) is sent <c>Attack</c> at every think for as long as the wave lasts, and the wave never ends (no
    /// loss, the target stands). In AI-vs-AI play the same loop came from BUG-0590 (a raider pushed against a keep: 278
    /// Attacks in one wave, <c>AiWaveStressTests</c> seed 6). A member should be given up on (or the wave ended) after a bounded
    /// number of tries.
    /// </summary>
    [Fact]
    public void AMemberThatCantHarmTheTarget_IsNotSentAttackEveryThinkForever()
    {
        GameData data = AiScenes.DataWith(dir =>
        {
            dir.EditJson("factions/malazan/ai.json", root =>
            {
                root["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
                root["attack"] = new JsonObject { ["minArmyPop"] = 1, ["minArmyCost"] = 0 };
                root["difficulties"]!["normal"]!["waveSizeMultiplier"] = 1;
            });
            dir.SetUnitField("malazan", "malazan_heavy_infantry", "attack.targets", "\"units\"");
        });
        Simulation sim = Scene(data, out EntityHandle hall, ghost: false, combat: true);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        // Two members already beside the hall's west face (anchor (52, 30)).
        List<EntityHandle> army = Army(sim, data.FindUnit("malazan_heavy_infantry"), 2, 51, 31, wide: 1);
        for (int k = 0; k < 40; k++) NextThink(sim);
        int attacks = log.Count(c => c.Kind == CommandKind.Attack);
        _out.WriteLine($"wave active {ai.Board.WaveActive}, waves {ai.Board.WavesStarted}; over 40 thinks: {attacks} Attacks, {log.Count(c => c.Kind == CommandKind.AttackMove)} AttackMoves");
        Assert.True(attacks <= 3 * army.Count, $"{attacks} Attacks in 40 thinks to {army.Count} members that can't hurt the target");
    }

    /// <summary>
    /// BUG-0590 (combat, since M4-2b): a melee unit whose body overlaps a building's footprint edge (its centre 0.013 m off the
    /// edge, as a crowd push leaves it) is ordered to attack the building: it never strikes, standing Idle. Its edge-to-edge gap
    /// is negative, and <c>InReach</c>'s <c>!(gap &lt; MinRange)</c> reads that as inside a minimum range of 0. At 0.40 m it hits.
    /// </summary>
    [Fact]
    public void AMeleeUnitOverlappingAFootprintEdge_StrikesTheBuilding()
    {
        Simulation sim = CombatScenes.Flat(48, 16);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(1, w.Data.FindBuilding("malazan_garrison_keep"), 20 * 48 + 20, out EntityHandle keep)); // y from 40 m
        EntityHandle raider = CombatScenes.Place(sim, 0, w.Data.FindUnit("whirlwind_raider"), new Vector2(46.97f, 40f - 0.013f));
        sim.Tick();
        sim.Tick();
        int hp = w.Buildings.Hp[keep.Index];
        sim.Enqueue(Command.Attack(0, raider, keep, isBuilding: true));
        for (int t = 0; t < 200; t++) sim.Tick();
        _out.WriteLine($"raider {w.Units.State[raider.Index]} at {w.Units.Position[raider.Index]}; keep hp {hp} -> {w.Buildings.Hp[keep.Index]}");
        Assert.True(w.Buildings.Hp[keep.Index] < hp, "the raider touching the keep never struck it");
    }

    // ---------- the scout ----------

    /// <summary>The flat AI scene (as <see cref="AiScenes.Flat"/>) on a given heightmap.</summary>
    private static Simulation FlatOn(Heightmap map, PlayerSeat[] seats, GameData data)
    {
        var config = new SimConfig(3, seats.Length, 256, 512) { Data = data, Seats = seats.ToImmutableArray(), Combat = false };
        var sim = new Simulation(config, map);
        World w = sim.World;
        int mine = data.FindResource("gold_mine"), tree = data.FindResource("tree");
        for (int p = 0; p < seats.Length; p++)
        {
            (int hx, int hy) = AiScenes.HallAt(p);
            Assert.True(w.Buildings.Spawn(p, StartBase.BuildingOfSlot(data, seats[p].Faction, BuildingSlot.TownHall), hy * AiScenes.FlatSize + hx, out _));
            int toward = p % 2 == 0 ? 1 : -1;
            for (int m = 0; m < 2; m++)
                Assert.True(w.Resources.Spawn(mine, (hy - 6) * AiScenes.FlatSize + hx + toward * 2 + 3 * m * toward, data.Rules.StartMineGold, out _));
            for (int t = 0; t < 12; t++)
                Assert.True(w.Resources.Spawn(tree, (hy + 9 + t / 4) * AiScenes.FlatSize + hx + t % 4, data.Rules.TreeWood, out _));
            int worker = StartBase.UnitOfSlot(data, seats[p].Faction, UnitSlot.Worker);
            for (int k = 0; k < 5; k++) CombatScenes.Place(sim, p, worker, w.NavGrid.CellCenter(hx + k % 4, hy + 5 + k / 4));
        }
        return TestSim.Explored(sim);
    }

    private static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWithAi("malazan", root =>
        root["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1))));

    /// <summary>
    /// The scout's first waypoint (east, 40 m from the hall) lies on a plateau no ramp reaches. The scout walks as near as it can,
    /// counts the point visited, and moves on: one Move to that point only, a Move to the next point after it, and the sweep
    /// carries on (it ends: the enemy hall is found).
    /// </summary>
    [Fact]
    public void AWaypointOnAnUnreachablePlateau_IsGivenUp_AndTheSweepMovesOn()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        // Where the first waypoint falls on the flat scene.
        Simulation probe = FlatOn(LocalMovementTests.Flat(AiScenes.FlatSize), seats, s_quiet.Value);
        while (probe.AiOf(0)!.Board.ScoutPhase == 0) probe.Tick();
        Vector2 p0 = probe.AiOf(0)!.Board.ScoutPoints[0];
        Assert.True(probe.World.NavGrid.WorldToCell(p0, out int cx, out int cy));
        // A 13 x 13 level-2 plateau round it (its rim is cliff), no ramp.
        var rows = new string[AiScenes.FlatSize];
        for (int y = 0; y < AiScenes.FlatSize; y++)
        {
            var row = new char[AiScenes.FlatSize];
            for (int x = 0; x < AiScenes.FlatSize; x++) row[x] = Math.Abs(x - cx) <= 6 && Math.Abs(y - cy) <= 6 ? '2' : '0';
            rows[y] = new string(row);
        }
        Simulation sim = FlatOn(LocalMovementTests.Rows(rows), seats, s_quiet.Value);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        while (sim.TickNumber < 6000 && ai.Board.ScoutPhase != 2) sim.Tick();
        Assert.Equal(p0, ai.Board.ScoutPoints[0]);
        List<Command> moves = log.Where(c => c.Kind == CommandKind.Move).ToList();
        _out.WriteLine($"scout phase {ai.Board.ScoutPhase} at tick {sim.TickNumber}; plateau round cell ({cx}, {cy}); moves: " +
            string.Join(" ", moves.Select(m => $"{m.Tick}:{Array.IndexOf(ai.Board.ScoutPoints, m.Position)}")));
        Assert.Equal(1, moves.Count(m => m.Position == p0));
        Assert.Contains(moves, m => m.Position == ai.Board.ScoutPoints[1]);
        Assert.True(ai.Board.ScoutVisited[0]);
        Assert.Equal(2, ai.Board.ScoutPhase);
    }

    /// <summary>
    /// The scout is killed in the tick its release Stop applies (the sweep ended: the AI knew a forest, a mine and the enemy):
    /// no crash, no replacement, the scout slot cleared, and the worker that takes the slot next is an ordinary worker.
    /// </summary>
    [Fact]
    public void AScoutKilledAsItsStopApplies_IsNotReplaced_AndItsSlotIsNotTheScoutsAnyMore()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_quiet.Value);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        while (sim.TickNumber < 8000 && !log.Any(c => c.Kind == CommandKind.Stop)) sim.Tick();
        Command stop = Assert.Single(log, c => c.Kind == CommandKind.Stop);
        Assert.Equal(2, ai.Board.ScoutPhase);
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, stop.Unit, false, sim.World.Units.Hp[stop.Unit.Index]));
        Assert.False(sim.World.Units.IsAlive(stop.Unit));
        int before = log.Count(c => c.Kind == CommandKind.Move);
        for (int t = 0; t < 100 * ai.ThinkTicks; t++) sim.Tick();
        Assert.Equal(2, ai.Board.ScoutPhase);
        Assert.Equal(-1, ai.Board.ScoutSlot);
        Assert.Equal(before, log.Count(c => c.Kind == CommandKind.Move));
        Assert.False(ai.IsScout(stop.Unit.Index, stop.Unit.Generation));
    }

    // ---------- reading nothing hidden ----------

    /// <summary>
    /// Hash twin, two AI seats (players 0 and 1, Normal, early waves, plenty to spend, combat off) and a human player 2. The twins
    /// differ only in player 2's eight Heavy Infantry: south-west corner and full hp in A, south-middle and a third of their hp in B.
    /// Until one of those units is in a seat's sight (in either twin), that seat's every think must be the same in both: the same
    /// commands, and the same blackboard (enemy memory, rally, wave, scout). Checked after every tick for 4,000 ticks.
    /// </summary>
    [Fact]
    public void TwoAiSeats_TwinWorldsDifferingOutOfSight_ThinkAlike()
    {
        GameData data = AiScenes.EarlyWaves(1);
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai, SeatController.Human);
        Simulation a = AiScenes.Flat(seats, data), b = AiScenes.Flat(seats, data);
        int heavy = Unit("malazan_heavy_infantry");
        var hidden = new List<int>();
        for (int k = 0; k < 8; k++)
        {
            EntityHandle ha = CombatScenes.Place(a, 2, heavy, a.World.NavGrid.CellCenter(2 + k % 4, 59 + k / 4));
            EntityHandle hb = CombatScenes.Place(b, 2, heavy, b.World.NavGrid.CellCenter(30 + k % 4, 59 + k / 4));
            Assert.Equal(ha, hb);
            b.World.Units.Hp[hb.Index] /= 3;
            hidden.Add(ha.Index);
        }
        foreach (Simulation s in new[] { a, b })
            for (int p = 0; p < 2; p++) BuildMaps.SetTotals(s, p, 5000, 5000);
        var logA = new[] { AiScenes.Log(a, 0), AiScenes.Log(a, 1) };
        var logB = new[] { AiScenes.Log(b, 0), AiScenes.Log(b, 1) };
        int[] stop = { -1, -1 };
        int compared = 0;
        while (a.TickNumber < 4000 && (stop[0] < 0 || stop[1] < 0))
        {
            a.Tick();
            b.Tick();
            for (int p = 0; p < 2; p++)
            {
                if (stop[p] >= 0) continue;
                AiPlayer x = a.AiOf(p)!, y = b.AiOf(p)!;
                Assert.True(logA[p].SequenceEqual(logB[p]), $"tick {a.TickNumber} player {p}: commands differ (first at {FirstDiff(logA[p], logB[p])})");
                Blackboard bx = x.Board, by = y.Board;
                Assert.True((bx.KnownEnemyBuildings, bx.EnemyArmyCount, bx.EnemyArmyCentroid, bx.EnemyArmyTick) == (by.KnownEnemyBuildings, by.EnemyArmyCount, by.EnemyArmyCentroid, by.EnemyArmyTick),
                    $"tick {a.TickNumber} player {p}: enemy memory differs");
                Assert.True((bx.Rally, bx.RallyVersion, bx.WaveActive, bx.WavesStarted, bx.WaveTarget, bx.WaveStartHalfPop) == (by.Rally, by.RallyVersion, by.WaveActive, by.WavesStarted, by.WaveTarget, by.WaveStartHalfPop),
                    $"tick {a.TickNumber} player {p}: military state differs");
                Assert.True((bx.ScoutPhase, bx.ScoutSlot, bx.ScoutTarget) == (by.ScoutPhase, by.ScoutSlot, by.ScoutTarget), $"tick {a.TickNumber} player {p}: scout differs");
                Assert.Equal(x.CommandsSent, y.CommandsSent);
                compared++;
                // From the next think on, a hidden unit in sight may rightly change what this seat does.
                foreach (int slot in hidden)
                    if (x.View.TryGetUnit(slot, out _) || y.View.TryGetUnit(slot, out _)) stop[p] = a.TickNumber;
            }
        }
        _out.WriteLine($"compared {compared} seat-ticks; player 0 until {(stop[0] < 0 ? "the end" : stop[0])}, player 1 until {(stop[1] < 0 ? "the end" : stop[1])}; " +
            $"commands {a.AiOf(0)!.CommandsSent} / {a.AiOf(1)!.CommandsSent}, waves {a.AiOf(0)!.Board.WavesStarted} / {a.AiOf(1)!.Board.WavesStarted}, " +
            $"scout phases {a.AiOf(0)!.Board.ScoutPhase} / {a.AiOf(1)!.Board.ScoutPhase}");
        Assert.True(compared > 2000, $"only {compared} seat-ticks compared");
        Assert.True(a.AiOf(0)!.Board.ScoutPhase > 0 && a.AiOf(1)!.Board.ScoutPhase > 0, "a seat never scouted: the twin proves little");
    }

    /// <summary>
    /// The new public <see cref="PlayerView.TryGetCargo"/> answers for the player's own live units only: for every unit slot
    /// (and -1, the capacity, int.MaxValue), true exactly when the slot holds a live unit of the viewer, with that unit's load.
    /// Enemy cargo, dead slots and out-of-range slots answer false, 0 and gold.
    /// </summary>
    [Fact]
    public void TryGetCargo_AnswersOwnLiveUnitsOnly()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Human, SeatController.Human));
        World w = sim.World;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i])
            {
                w.Units.Cargo[i] = 3 + i % 5;
                w.Units.CargoKind[i] = i % 2 == 0 ? ResourceKind.Wood : ResourceKind.Gold;
            }
        CombatSystem.HitUnit(w, new PendingHit(default, 1, new EntityHandle(0, w.Units.Generation[0]), false, w.Units.Hp[0])); // a dead slot
        for (int p = 0; p < 2; p++)
        {
            var view = new PlayerView(w, p);
            for (int i = -1; i <= w.Units.Capacity; i++)
            {
                bool own = i >= 0 && i < w.Units.Capacity && w.Units.Alive[i] && w.Units.Owner[i] == p;
                bool got = view.TryGetCargo(i, out int amount, out ResourceKind kind);
                Assert.True(own == got, $"player {p} slot {i}: answered {got}");
                if (own) Assert.Equal((w.Units.Cargo[i], w.Units.CargoKind[i]), (amount, kind));
                else Assert.Equal((0, ResourceKind.Gold), (amount, kind));
            }
            Assert.False(view.TryGetCargo(int.MaxValue, out _, out _));
        }
    }

    private static string FirstDiff(List<Command> x, List<Command> y)
    {
        int n = Math.Min(x.Count, y.Count);
        for (int i = 0; i < n; i++)
            if (!x[i].Equals(y[i])) return $"#{i}: {x[i].Kind} {x[i].Unit} {x[i].Position} v {y[i].Kind} {y[i].Unit} {y[i].Position}";
        return $"lengths {x.Count} v {y.Count}";
    }
}
