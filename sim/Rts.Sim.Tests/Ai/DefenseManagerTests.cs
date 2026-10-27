using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4b criterion 4: <see cref="DefenseManager"/> on the flat scene (combat off: the commands are what is checked). Player 0
/// (AI, malazan) has its hall at anchor (8, 30); enemies are player 1's Raiders placed by the test and spotted for player 0
/// (a reveal, so the AI sees them through its view). The knobs come from the tests' own rules.json (rule 12): defendRadius
/// 48 m, workerFleeRadius 16 m, rallyReach 6 m, defendFor as given; no scout; a quiet build order.
/// </summary>
public class DefenseManagerTests
{
    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    private static GameData Data(double wavePop = 100, double cooldown = 60, double defendFor = 20) => AiScenes.DataWith(dir =>
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
            ai["defendRadius"] = 48;
            ai["defendFor"] = defendFor;
            ai["workerFleeRadius"] = 16;
            ai["rallyReach"] = 6;
            ai["scoutAfter"] = 3600;
            // M5-4f: these rows pin the M5-4b rules (trigger, point, flee, BUG-0670); the largest defendMinValue keeps every free
            // unit answering, as then. The sized answer: ProportionalDefenseTests.
            ai["defendMinValue"] = 5000;
        });
    });

    private static readonly Lazy<GameData> s_quiet = new(() => Data());

    private static Simulation Scene(GameData data)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data);
        BuildMaps.SetTotals(sim, 0, 0, 0); // nothing trained meanwhile
        return sim;
    }

    /// <summary>The centre (m) of player 0's hall.</summary>
    private static Vector2 Hall(Simulation sim)
    {
        (int x, int y) = AiScenes.HallAt(0);
        BuildingDef d = sim.World.Data.Buildings[sim.World.Buildings.TypeId[sim.World.Buildings.SlotAt(x, y)]];
        return new Vector2((x + d.FootprintWidth * 0.5f) * MapConstants.CellSize, (y + d.FootprintHeight * 0.5f) * MapConstants.CellSize);
    }

    private static List<EntityHandle> Army(Simulation sim, int n, int x, int y)
    {
        var list = new List<EntityHandle>();
        for (int k = 0; k < n; k++) list.Add(CombatScenes.Place(sim, 0, Heavy, sim.World.NavGrid.CellCenter(x + k % 6, y + k / 6)));
        return list;
    }

    /// <summary>Player 1's Raiders at <paramref name="at"/> (1 m apart along y), spotted by player 0.</summary>
    private static List<EntityHandle> Enemies(Simulation sim, int n, Vector2 at)
    {
        var list = new List<EntityHandle>();
        for (int k = 0; k < n; k++)
        {
            EntityHandle h = CombatScenes.Place(sim, 1, CombatScenes.Raider, at + new Vector2(0f, k));
            CombatScenes.Spot(sim, 0, h);
            list.Add(h);
        }
        return list;
    }

    private static Vector2 Centroid(Simulation sim, List<EntityHandle> units)
    {
        Vector2 sum = Vector2.Zero;
        foreach (EntityHandle h in units) sum += sim.World.Units.Position[h.Index];
        return sum / units.Count;
    }

    /// <summary>Ticks to the next think of player 0 and through it (its commands are logged by then).</summary>
    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    /// <summary>
    /// (a) An enemy army seen 30 m from the hall ends the wave in progress (its members get a Move home, the cooldown is set)
    /// and pulls every free army unit with one AttackMove each to the enemy's centroid (a member at the think after its Move);
    /// no wave starts while the threat holds, though the cooldown (1 s) is long over.
    /// </summary>
    [Fact]
    public void AnEnemyArmyNearTheHall_EndsTheWave_AndPullsEveryArmyUnitToIt()
    {
        Simulation sim = Scene(Data(wavePop: 4 * TestSim.Data.Units[Heavy].HalfPop / 2.0, cooldown: 1));
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        // Player 0 knows player 1's hall through a billet of its own in sight of it (MilitaryManagerTests' scene).
        Assert.True(w.Buildings.Spawn(0, w.Data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out _));
        List<EntityHandle> members = Army(sim, 4, 18, 28);
        NextThink(sim);
        Assert.True(ai.Board.WaveActive);
        List<EntityHandle> others = Army(sim, 2, 4, 40);
        List<EntityHandle> enemies = Enemies(sim, 3, Hall(sim) + new Vector2(0f, 30f));
        Vector2 point = Centroid(sim, enemies);
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        int ended = sim.TickNumber - 1;
        Assert.False(ai.Board.WaveActive);
        Assert.True(ai.Board.Defending);
        Assert.Equal(ended + ai.Rules.WaveCooldownTicks, ai.Board.WaveCooldownUntil);
        Assert.Equal(members.OrderBy(h => h.Index), log.Where(c => c.Kind == CommandKind.Move).Select(c => c.Unit));
        Assert.All(log.Where(c => c.Kind == CommandKind.Move), c => Assert.Equal(ai.Board.Rally, c.Position));
        Assert.Equal(others.OrderBy(h => h.Index), log.Where(c => c.Kind == CommandKind.AttackMove).Select(c => c.Unit));
        for (int k = 0; k < 10; k++) NextThink(sim);
        List<Command> pulls = log.Where(c => c.Kind == CommandKind.AttackMove).ToList();
        Assert.Equal(others.Concat(members).OrderBy(h => h.Index), pulls.Select(c => c.Unit).OrderBy(h => h.Index));
        Assert.All(pulls, c => Assert.Equal(point, c.Position));
        Assert.Equal(1, ai.Board.WavesStarted);
        Assert.Equal(1, ai.Board.Defenses);
    }

    /// <summary>
    /// (b) An own building near the hall losing hp between thinks, no enemy seen, sends the army to that building's footprint
    /// centre. A building seen for the first time (a new site at 1 hp) is no hit.
    /// </summary>
    [Fact]
    public void ABuildingHit_WithNoEnemySeen_SendsTheArmyToIt()
    {
        Simulation sim = Scene(s_quiet.Value);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        int billetType = w.Data.FindBuilding("malazan_billet");
        Assert.True(w.Buildings.Spawn(0, billetType, 38 * AiScenes.FlatSize + 16, out EntityHandle billet));
        List<EntityHandle> army = Army(sim, 3, 30, 20);
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        Assert.True(w.Buildings.Spawn(0, billetType, 22 * AiScenes.FlatSize + 16, out _, site: true));
        NextThink(sim);
        Assert.DoesNotContain(log, c => c.Kind == CommandKind.AttackMove);
        Assert.Equal(0, ai.Board.Defenses);
        w.Buildings.SetHp(billet.Index, w.Buildings.Hp[billet.Index] - 10);
        NextThink(sim);
        List<Command> pulls = log.Where(c => c.Kind == CommandKind.AttackMove).ToList();
        Assert.Equal(army.OrderBy(h => h.Index), pulls.Select(c => c.Unit));
        Assert.All(pulls, c => Assert.Equal(ai.BuildingCentre(billetType, w.Buildings.Cell[billet.Index]), c.Position));
    }

    /// <summary>
    /// (c) Workers within workerFleeRadius of the point get one Move to the hall (the only drop-off) and no Gather until defendFor
    /// (5 s here) after their flee (BUG-0671: not after the last trigger, so a raider parked for good doesn't idle them for
    /// good); then they gather again, while the threat holds only at nodes farther than workerFleeRadius from the point, and
    /// they aren't sent fleeing again during this threat.
    /// </summary>
    [Fact]
    public void WorkersNearThePoint_Flee_AndGatherAgainDefendForAfterTheirFlee_AwayFromThePoint()
    {
        Simulation sim = Scene(Data(defendFor: 5));
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        var workers = new List<EntityHandle>();
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0) workers.Add(new EntityHandle(i, w.Units.Generation[i]));
        Assert.Equal(5, workers.Count);
        List<EntityHandle> enemies = Enemies(sim, 1, w.NavGrid.CellCenter(10, 40));
        Vector2 point = w.Units.Position[enemies[0].Index];
        Assert.All(workers, h => Assert.True(Vector2.Distance(w.Units.Position[h.Index], point) <= 16f));
        List<Command> log = AiScenes.Log(sim, 0);
        // The raider stays: the threat holds the whole run.
        for (int k = 0; k < 3 * ai.Rules.DefendForTicks / ai.ThinkTicks; k++)
        {
            NextThink(sim);
            Assert.True(ai.Board.Defending);
        }
        foreach (EntityHandle h in workers)
        {
            Command move = Assert.Single(log, c => c.Unit == h && c.Kind == CommandKind.Move);
            Assert.Equal(Hall(sim), move.Position);
            int fled = move.Tick - 1;
            Command gather = log.First(c => c.Unit == h && c.Kind == CommandKind.Gather);
            // Not before defendFor after the flee (its think stamps the next tick), and at the first think after that.
            Assert.InRange(gather.Tick - 1, fled + ai.Rules.DefendForTicks + 1, fled + ai.Rules.DefendForTicks + ai.ThinkTicks);
            Assert.All(log.Where(c => c.Unit == h && c.Kind == CommandKind.Gather),
                c => Assert.True(Vector2.Distance(c.Position, point) > ai.Rules.WorkerFleeRadius, $"a Gather {Vector2.Distance(c.Position, point):F1} m from the point"));
        }
    }

    /// <summary>
    /// BUG-0671: a worker flees to the own finished drop-off nearest it that is farther than workerFleeRadius from the point,
    /// not to the nearer one beside the threat.
    /// </summary>
    [Fact]
    public void AFleeingWorker_GoesToADropOffAwayFromThePoint()
    {
        Simulation sim = Scene(s_quiet.Value);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        // A second drop-off (another hall) far from the threat, which comes right beside the first hall.
        int hallType = w.Buildings.TypeId[w.Buildings.SlotAt(AiScenes.HallAt(0).X, AiScenes.HallAt(0).Y)];
        Assert.True(w.Buildings.Spawn(0, hallType, 50 * AiScenes.FlatSize + 30, out EntityHandle far));
        var workers = new List<EntityHandle>();
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0) workers.Add(new EntityHandle(i, w.Units.Generation[i]));
        Enemies(sim, 1, Hall(sim) + new Vector2(0f, 6f));
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        Vector2 farCentre = ai.BuildingCentre(hallType, w.Buildings.Cell[far.Index]);
        List<Command> moves = log.Where(c => c.Kind == CommandKind.Move).ToList();
        Assert.NotEmpty(moves);
        Assert.All(moves, c => Assert.Equal(farCentre, c.Position));
    }

    /// <summary>
    /// BUG-0670: an army away in the enemy base (attacking player 1's hall, combat on) when a raider comes 20 m from home gets
    /// a Move home (an AttackMove there would fight on by the enemy hall), then the AttackMove once inside defendRadius of
    /// the hall: all three are home and the raider is dead 45 s later.
    /// </summary>
    [Fact]
    public void AnArmyAwayInTheEnemyBase_GetsAMoveHome_ThenTheAttackMove()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_quiet.Value, combat: true);
        BuildMaps.SetTotals(sim, 0, 0, 0);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        EntityHandle hall1 = w.Buildings.HandleOf(w.Buildings.SlotAt(AiScenes.HallAt(1).X, AiScenes.HallAt(1).Y));
        List<EntityHandle> army = Army(sim, 3, 50, 37);
        for (int t = 0; t < 3; t++) sim.Tick();
        foreach (EntityHandle h in army) sim.Enqueue(Command.Attack(0, h, hall1, isBuilding: true));
        CombatScenes.RunUntil(sim, () => army.All(h => w.Units.State[h.Index] == UnitState.Attacking), 800);
        EntityHandle raider = Enemies(sim, 1, Hall(sim) + new Vector2(0f, -20f))[0];
        w.Units.Hold[raider.Index] = true;
        List<Command> log = AiScenes.Log(sim, 0);
        for (int t = 0; t < 900; t++) sim.Tick();
        foreach (EntityHandle h in army)
        {
            List<Command> orders = log.Where(c => c.Unit == h).ToList();
            Assert.Equal(new[] { CommandKind.Move, CommandKind.AttackMove }, orders.Select(c => c.Kind));
            Assert.True(Vector2.Distance(w.Units.Position[h.Index], Hall(sim)) <= ai.Rules.DefendRadius, $"not home: {w.Units.Position[h.Index]}");
        }
        Assert.False(w.Units.Alive[raider.Index] && w.Units.Generation[raider.Index] == raider.Generation, "the raider lives");
    }

    /// <summary>(d) An enemy seen farther than defendRadius from the hall, or a hit on an own building that far out, triggers nothing.</summary>
    [Fact]
    public void AThreatOutsideTheRadius_TriggersNothing()
    {
        Simulation sim = Scene(s_quiet.Value);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        Army(sim, 3, 18, 28);
        Assert.True(w.Buildings.Spawn(0, w.Data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out EntityHandle far));
        Enemies(sim, 2, Hall(sim) + new Vector2(49f, 0f));
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        w.Buildings.SetHp(far.Index, w.Buildings.Hp[far.Index] - 10);
        for (int k = 0; k < 4; k++) NextThink(sim);
        Assert.DoesNotContain(log, c => c.Kind == CommandKind.AttackMove);
        Assert.Equal(0, ai.Board.Defenses);
        Assert.False(ai.Board.Defending);
    }

    /// <summary>(e) The point is sent again only when it moves more than rallyReach (6 m): a 4 m shift sends nothing, a 10 m one one AttackMove each.</summary>
    [Fact]
    public void ThePoint_IsSentAgain_OnlyWhenItMovesPastRallyReach()
    {
        Simulation sim = Scene(s_quiet.Value);
        World w = sim.World;
        List<EntityHandle> army = Army(sim, 3, 2, 50);
        List<EntityHandle> enemies = Enemies(sim, 2, Hall(sim) + new Vector2(0f, 30f));
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        Assert.Equal(3, log.Count(c => c.Kind == CommandKind.AttackMove));
        foreach (EntityHandle h in enemies) w.Units.Position[h.Index] += new Vector2(4f, 0f);
        for (int k = 0; k < 3; k++) NextThink(sim);
        Assert.Equal(3, log.Count(c => c.Kind == CommandKind.AttackMove));
        foreach (EntityHandle h in enemies) w.Units.Position[h.Index] += new Vector2(6f, 0f);
        for (int k = 0; k < 3; k++) NextThink(sim);
        List<Command> pulls = log.Where(c => c.Kind == CommandKind.AttackMove).ToList();
        Assert.Equal(6, pulls.Count);
        Assert.Equal(army.OrderBy(h => h.Index), pulls.Skip(3).Select(c => c.Unit));
        Assert.All(pulls.Skip(3), c => Assert.Equal(Centroid(sim, enemies), c.Position));
    }

    /// <summary>
    /// M6-S4: an ally's army (player 1, human, teams 1 / 1) parked beside the AI's hall for 2,000 ticks never sets
    /// <see cref="Blackboard.Defending"/>; the same army of an enemy (team 0) does at once (the scene's control).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAlliedArmyBesideTheHall_NeverTriggersTheDefense(bool allied)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human)
            .Select(s => s with { Team = allied ? 1 : 0 }).ToArray();
        Simulation sim = AiScenes.Flat(seats, combat: true);
        World w = sim.World;
        (int hx, int hy) = AiScenes.HallAt(0);
        int line = Rts.Sim.ViewApi.StartBase.UnitOfSlot(w.Data, w.FactionOf(1), UnitSlot.Line);
        var army = new List<EntityHandle>();
        for (int k = 0; k < 8; k++)
        {
            EntityHandle h = CombatScenes.Place(sim, 1, line, w.NavGrid.CellCenter(hx + 5 + k % 4, hy - 3 + k / 4));
            w.Units.Hold[h.Index] = true;
            army.Add(h);
        }
        AiPlayer ai = sim.AiOf(0)!;
        bool everDefending = false;
        int livingArmy = 0;
        for (int t = 0; t < 2000; t++)
        {
            sim.Tick();
            everDefending |= ai.Board.Defending;
            if (!allied && everDefending) break;
        }
        foreach (EntityHandle h in army) if (w.Units.IsAlive(h)) livingArmy++;
        if (allied)
        {
            Assert.False(everDefending, "an ally's army beside the hall set Defending");
            Assert.Equal(0, ai.Board.Defenses);
            Assert.Equal(8, livingArmy);
            Assert.Equal(0, ai.IntruderCount);
        }
        else Assert.True(everDefending, "control: an enemy army beside the hall never set Defending");
    }
}
