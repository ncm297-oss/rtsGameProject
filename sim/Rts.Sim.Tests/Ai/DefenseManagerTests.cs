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
            root["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
            root["attack"] = new JsonObject { ["minArmyPop"] = wavePop, ["minArmyCost"] = 0 };
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
    /// (5 s here) after the last trigger; then they gather again.
    /// </summary>
    [Fact]
    public void WorkersNearThePoint_Flee_AndGatherOnlyOnceTheThreatIsOver()
    {
        Simulation sim = Scene(Data(defendFor: 5));
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        var workers = new List<EntityHandle>();
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0) workers.Add(new EntityHandle(i, w.Units.Generation[i]));
        Assert.Equal(5, workers.Count);
        List<EntityHandle> enemies = Enemies(sim, 1, w.NavGrid.CellCenter(10, 40));
        Assert.All(workers, h => Assert.True(Vector2.Distance(w.Units.Position[h.Index], w.Units.Position[enemies[0].Index]) <= 16f));
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 3; k++) NextThink(sim);
        int lastTrigger = sim.TickNumber - 1;
        w.Units.Free(enemies[0]);
        while (sim.TickNumber < lastTrigger + ai.Rules.DefendForTicks + 4 * ai.ThinkTicks) NextThink(sim);
        foreach (EntityHandle h in workers)
        {
            Command move = Assert.Single(log, c => c.Unit == h && c.Kind == CommandKind.Move);
            Assert.Equal(Hall(sim), move.Position);
            Command gather = log.First(c => c.Unit == h && c.Kind == CommandKind.Gather);
            // Not before the threat is over (its think stamps the next tick), and at the first think after it.
            Assert.InRange(gather.Tick - 1, lastTrigger + ai.Rules.DefendForTicks + 1, lastTrigger + ai.Rules.DefendForTicks + ai.ThinkTicks);
        }
        Assert.False(ai.Board.Defending);
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
}
