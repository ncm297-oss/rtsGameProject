using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-3 criteria 2-4: <see cref="MilitaryManager"/> on the flat scene (combat off: the commands are what is checked). Player 0
/// (AI, malazan) knows player 1's hall through a billet of its own placed in sight of it (or, once the billet is gone, as a
/// ghost); its army is Heavy Infantry placed by the test. Thresholds come from the tests' own ai.json (rule 12).
/// </summary>
public class MilitaryManagerTests
{
    private readonly ITestOutputHelper _out;

    public MilitaryManagerTests(ITestOutputHelper output) => _out = output;

    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    private static int HeavyCost => TestSim.Data.Units[Heavy].CostGold + TestSim.Data.Units[Heavy].CostWood;

    private static int HeavyHalfPop => TestSim.Data.Units[Heavy].HalfPop;

    /// <summary>A quiet malazan order with a wave at <paramref name="pop"/> pop and <paramref name="cost"/>, Easy x 0.6 and Normal x 1, the loss fraction and cooldown given.</summary>
    private static GameData Data(double pop, int cost, double loss = 0.5, double cooldown = 60) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            root["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
            root["attack"] = new JsonObject { ["minArmyPop"] = pop, ["minArmyCost"] = cost };
            root["retreat"]!["lossFraction"] = loss;
            root["difficulties"]!["easy"]!["waveSizeMultiplier"] = 0.6;
            root["difficulties"]!["normal"]!["waveSizeMultiplier"] = 1;
        });
        dir.EditJson("common/rules.json", r => r["ai"]!["waveCooldown"] = cooldown);
    });

    private static readonly Lazy<GameData> s_four = new(() => Data(4 * HeavyHalfPop / 2.0, 4 * HeavyCost));

    /// <summary>
    /// The scene: player 0's billet in sight of player 1's hall (and, with <paramref name="tent"/>, a tent of player 1 beside
    /// it); one tick so the AI has seen them; with <paramref name="ghost"/> the billet is then removed, so the hall is a ghost.
    /// </summary>
    private static Simulation Scene(GameData data, AiDifficulty difficulty, out EntityHandle hall, bool ghost = false, bool tent = false)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Human), data);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out EntityHandle billet));
        if (tent) Assert.True(w.Buildings.Spawn(1, data.FindBuilding("whirlwind_tent"), 26 * AiScenes.FlatSize + 49, out _));
        hall = default;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 1 && w.Buildings.TypeId[k] == data.FindBuilding("whirlwind_holy_camp"))
                hall = new EntityHandle(k, w.Buildings.Generation[k]);
        sim.Tick();
        Assert.True(sim.AiOf(0)!.Board.KnownEnemyBuildings > 0);
        if (ghost)
        {
            Assert.True(w.Buildings.Free(billet));
            do sim.Tick(); while (sim.TickNumber % 4 != 2); // through a fog update (ticks 1, 5, ...) without the billet
            Assert.True(sim.AiOf(0)!.View.TryGetGhost(hall.Index, out _), "precondition: the hall is a ghost");
        }
        return sim;
    }

    private static List<EntityHandle> Army(Simulation sim, int n, int x = 18, int y = 28)
    {
        var list = new List<EntityHandle>();
        for (int k = 0; k < n; k++) list.Add(CombatScenes.Place(sim, 0, Heavy, sim.World.NavGrid.CellCenter(x + k % 6, y + k / 6)));
        return list;
    }

    /// <summary>Ticks to the next think of player 0 and through it (its commands are logged by then).</summary>
    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    private static Vector2 Centre(Simulation sim, EntityHandle building) =>
        sim.AiOf(0)!.BuildingCentre(sim.World.Buildings.TypeId[building.Index], sim.World.Buildings.Cell[building.Index]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtBothThresholds_EveryArmyUnit_AttackMovesToTheHall_WithinOneThink(bool ghost)
    {
        Simulation sim = Scene(s_four.Value, AiDifficulty.Normal, out EntityHandle hall, ghost);
        List<Command> log = AiScenes.Log(sim, 0);
        List<EntityHandle> army = Army(sim, 4);
        NextThink(sim);
        List<Command> attacks = log.Where(c => c.Kind == CommandKind.AttackMove).ToList();
        Assert.Equal(army.OrderBy(h => h.Index), attacks.Select(c => c.Unit));
        Assert.All(attacks, c => Assert.Equal(Centre(sim, hall), c.Position));
        Assert.Equal(1, sim.AiOf(0)!.Board.WavesStarted);
    }

    [Theory]
    [InlineData(3, 0)] // a unit short of the pop
    [InlineData(4, 1)] // the pop, 1 short of the cost
    public void BelowEitherThreshold_NoWave(int units, int costShort)
    {
        GameData data = costShort == 0 ? s_four.Value : Data(4 * HeavyHalfPop / 2.0, 4 * HeavyCost + costShort);
        Simulation sim = Scene(data, AiDifficulty.Normal, out _);
        List<Command> log = AiScenes.Log(sim, 0);
        Army(sim, units);
        for (int k = 0; k < 5; k++) NextThink(sim);
        Assert.DoesNotContain(log, c => c.Kind is CommandKind.AttackMove or CommandKind.Attack);
        Assert.Equal(0, sim.AiOf(0)!.Board.WavesStarted);
    }

    /// <summary>Easy's 0.6 multiplier: three units (6 half-pop of a 10 half-pop threshold) wave on Easy, not on Normal.</summary>
    [Fact]
    public void Easy_WavesAtASmallerArmyThanNormal()
    {
        GameData data = Data(5 * HeavyHalfPop / 2.0, 0);
        foreach ((AiDifficulty d, int waves) in new[] { (AiDifficulty.Easy, 1), (AiDifficulty.Normal, 0) })
        {
            Simulation sim = Scene(data, d, out _);
            Army(sim, 3);
            for (int k = 0; k < 3; k++) NextThink(sim);
            Assert.True(waves == sim.AiOf(0)!.Board.WavesStarted, $"{d}: {sim.AiOf(0)!.Board.WavesStarted} waves");
        }
    }

    /// <summary>The target razed mid-wave: the next known enemy building is the new target (every member sent again), or with none the wave ends and the members Move home.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARazedTarget_Retargets_OrTheWaveEndsHome(bool another)
    {
        Simulation sim = Scene(s_four.Value, AiDifficulty.Normal, out EntityHandle hall, tent: another);
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> army = Army(sim, 4);
        NextThink(sim);
        Assert.True(ai.Board.WaveActive);
        EntityHandle first = ai.Board.WaveTarget; // the known enemy building nearest the rally point
        if (!another) Assert.Equal(hall, first);
        List<Command> log = AiScenes.Log(sim, 0);
        Assert.True(sim.World.Buildings.Free(first));
        NextThink(sim);
        if (another)
        {
            Assert.True(ai.Board.WaveActive);
            Assert.NotEqual(first, ai.Board.WaveTarget);
            Assert.True(sim.World.Buildings.IsAlive(ai.Board.WaveTarget));
            List<Command> again = log.Where(c => c.Kind == CommandKind.AttackMove).ToList();
            Assert.Equal(army.OrderBy(h => h.Index), again.Select(c => c.Unit));
            Assert.All(again, c => Assert.Equal(Centre(sim, ai.Board.WaveTarget), c.Position));
        }
        else
        {
            Assert.False(ai.Board.WaveActive);
            List<Command> home = log.Where(c => c.Kind == CommandKind.Move && army.Contains(c.Unit)).ToList();
            Assert.Equal(army.OrderBy(h => h.Index), home.Select(c => c.Unit));
            Assert.All(home, c => Assert.Equal(ai.Board.Rally, c.Position));
        }
    }

    /// <summary>
    /// Criterion 3: a wave that loses half its starting half-pop (the test's lossFraction 0.5) sends every live member home
    /// within one think and ends; no wave starts before the cooldown, and one starts at the first think after it.
    /// </summary>
    [Fact]
    public void LosingTheLossFraction_SendsEveryLiveMemberHome_AndTheCooldownHolds()
    {
        Simulation sim = Scene(s_four.Value, AiDifficulty.Normal, out _);
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> army = Army(sim, 6);
        NextThink(sim);
        Assert.True(ai.Board.WaveActive);
        Assert.Equal(6 * HeavyHalfPop, ai.Board.WaveStartHalfPop);
        // 4 of 6 die: 2 left, a third of the start, under half.
        foreach (EntityHandle h in army.Take(4)) CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, h, false, sim.World.Units.Hp[h.Index]));
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        int retreat = sim.TickNumber - 1;
        Assert.False(ai.Board.WaveActive);
        List<Command> home = log.Where(c => c.Kind == CommandKind.Move).ToList();
        Assert.Equal(army.Skip(4).OrderBy(h => h.Index), home.Select(c => c.Unit));
        Assert.All(home, c => Assert.Equal(ai.Board.Rally, c.Position));
        Assert.Equal(retreat + ai.Rules.WaveCooldownTicks, ai.Board.WaveCooldownUntil);
        Army(sim, 6, 18, 40);
        // The cooldown's end, fixed now: the next wave moves WaveCooldownUntil on (and with combat off its members give up on
        // the hall after one Attack each, BUG-0591, so that wave retreats too).
        int until = ai.Board.WaveCooldownUntil;
        while (sim.TickNumber <= until - ai.ThinkTicks)
        {
            NextThink(sim);
            Assert.Equal(1, ai.Board.WavesStarted);
        }
        while (sim.TickNumber <= until) NextThink(sim);
        Assert.Equal(2, ai.Board.WavesStarted);
    }

    /// <summary>
    /// Criterion 3's bound: attack, lose every member, rebuild, attack again for 6,000 ticks: at most 6,000 / cooldown + 1 waves
    /// with a 10 s cooldown; with the cooldown at 0 the same loop starts far more (the bound is the cooldown's).
    /// </summary>
    [Fact]
    public void AttackRetreatLoop_IsBoundedByTheCooldown()
    {
        int Loop(GameData data)
        {
            Simulation sim = Scene(data, AiDifficulty.Normal, out _);
            AiPlayer ai = sim.AiOf(0)!;
            UnitStore u = sim.World.Units;
            int spawned = 0;
            while (sim.TickNumber < 6000)
            {
                int alive = 0;
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i] || u.Owner[i] != 0 || u.TypeId[i] != Heavy) continue;
                    if (ai.Board.WaveActive && ai.Board.WaveGeneration[i] == u.Generation[i])
                        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, new EntityHandle(i, u.Generation[i]), false, u.Hp[i]));
                    else alive++;
                }
                for (; alive < 4; alive++, spawned++)
                    CombatScenes.Place(sim, 0, Heavy, sim.World.NavGrid.CellCenter(16 + spawned % 8, 20 + spawned / 8 % 6));
                NextThink(sim);
            }
            return ai.Board.WavesStarted;
        }
        int cooldown = Data(4 * HeavyHalfPop / 2.0, 0, cooldown: 10).AiRules.WaveCooldownTicks;
        int bounded = Loop(Data(4 * HeavyHalfPop / 2.0, 0, cooldown: 10)), unbounded = Loop(Data(4 * HeavyHalfPop / 2.0, 0, cooldown: 0));
        _out.WriteLine($"waves: {bounded} with a {cooldown}-tick cooldown, {unbounded} with none");
        Assert.InRange(bounded, 2, 6000 / cooldown + 1);
        Assert.True(unbounded > 6000 / cooldown + 1, $"{unbounded} waves without a cooldown: the bound test can't tell");
    }

    /// <summary>
    /// Criterion 4: the rally point lies rallyDistance from the hall's centre toward the nearest known enemy building; each idle
    /// army unit away from it gets one Move there, never re-sent (pending, walking, arrived), and one within reach gets none.
    /// </summary>
    [Fact]
    public void Rally_OneMoveEachToThePoint_TowardTheEnemy_NoResend()
    {
        Simulation sim = Scene(Data(50, 0), AiDifficulty.Normal, out EntityHandle hall);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        List<EntityHandle> far = Army(sim, 3, 20, 50);
        Assert.True(ai.TryHallPoint(out Vector2 own));
        Vector2 rally = ai.Board.Rally;
        Assert.InRange(Vector2.Distance(own, rally), ai.Rules.RallyDistance - 0.01f, ai.Rules.RallyDistance + 0.01f);
        Vector2 toward = Vector2.Normalize(Centre(sim, hall) - own);
        Assert.InRange(Vector2.Dot(Vector2.Normalize(rally - own), toward), 0.9999f, 1.0001f);
        EntityHandle near = CombatScenes.Place(sim, 0, Heavy, rally + new Vector2(1, 0));
        for (int k = 0; k < 40; k++) NextThink(sim);
        List<Command> moves = log.Where(c => c.Kind == CommandKind.Move && (far.Contains(c.Unit) || c.Unit == near)).ToList();
        Assert.Equal(far.OrderBy(h => h.Index), moves.Select(c => c.Unit));
        Assert.All(moves, c => Assert.Equal(rally, c.Position));
        Assert.All(moves, c => Assert.Equal(moves[0].Tick, c.Tick)); // one think, one each
    }
}
