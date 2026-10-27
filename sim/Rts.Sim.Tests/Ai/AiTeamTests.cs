using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S4 criterion 5: the AI in a team match. Four AI seats as 2v2 (teams 1, 2, 1, 2), each seat's raiders in the next
/// (enemy) seat's base and casters there too, early waves: no seat ever sends an Attack, AttackMove or cast at an ally's unit
/// or building, and no wave targets an ally's building; two runs hash equal every tick; a tick stays allocation-free.
/// </summary>
[Collection(SerialCollection.Name)]
public class AiTeamTests
{
    private readonly ITestOutputHelper _out;

    public AiTeamTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_early = new(() => AiScenes.EarlyWaves());

    /// <summary>Four AI seats (Normal, factions alternating), teams 1, 2, 1, 2: every seat's neighbours are enemies, its opposite an ally.</summary>
    internal static PlayerSeat[] TwoVersusTwo() =>
        AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai, SeatController.Ai, SeatController.Ai)
            .Select((s, p) => s with { Team = p % 2 == 0 ? 1 : 2 }).ToArray();

    /// <summary>The 2v2 AI match of seed <paramref name="seed"/>: early waves, three raiders and one of each caster type of each seat in the next seat's base.</summary>
    internal static Simulation Match(ulong seed) =>
        AiScenes.Generated(TwoVersusTwo(), seed, record: false, out _, units: 600, data: s_early.Value, raiders: 3, casters: 1);

    /// <summary>The footprint centre of live building slot <paramref name="k"/>.</summary>
    private static Vector2 Centre(World w, int k)
    {
        BuildingDef d = w.Data.Buildings[w.Buildings.TypeId[k]];
        int c = w.Buildings.Cell[k], width = w.NavGrid.Width;
        return new Vector2((c % width + d.FootprintWidth * 0.5f) * Map.MapConstants.CellSize, (c / width + d.FootprintHeight * 0.5f) * Map.MapConstants.CellSize);
    }

    /// <summary>Whether <paramref name="point"/> lies within <paramref name="slack"/> m of a footprint of an ally (not itself) of <paramref name="p"/>.</summary>
    private static bool AtAnAlliedBuilding(World w, int p, Vector2 point, float slack)
    {
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] == p || w.IsEnemy(p, b.Owner[k])) continue;
            if (Combat.CombatSystem.BuildingDistanceSquared(w, k, point) <= slack * slack) return true;
        }
        return false;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void TwoVersusTwo_NoSeatSendsAWaveAttackOrCastAtAnAlly(ulong seed)
    {
        Simulation sim = Match(seed);
        World w = sim.World;
        var bad = new List<string>();
        int attacks = 0, attackMoves = 0, casts = 0;
        for (int p = 0; p < 4; p++)
        {
            int seat = p;
            sim.AiOf(p)!.SentForTests = c =>
            {
                switch (c.Kind)
                {
                    case CommandKind.Attack:
                        attacks++;
                        int owner = c.TargetIsBuilding ? (w.Buildings.IsAlive(c.Target) ? w.Buildings.Owner[c.Target.Index] : -1)
                            : (w.Units.IsAlive(c.Target) ? w.Units.Owner[c.Target.Index] : -1);
                        if (owner >= 0 && w.AreAllied(seat, owner)) bad.Add($"tick {sim.TickNumber}: seat {seat} Attack on player {owner}'s {(c.TargetIsBuilding ? "building" : "unit")}");
                        break;
                    case CommandKind.AttackMove:
                        attackMoves++;
                        if (AtAnAlliedBuilding(w, seat, c.Position, 0.5f)) bad.Add($"tick {sim.TickNumber}: seat {seat} AttackMove onto an ally's building at {c.Position}");
                        break;
                    case CommandKind.UseAbility:
                        casts++;
                        if (AtAnAlliedBuilding(w, seat, c.Position, 0.5f)) bad.Add($"tick {sim.TickNumber}: seat {seat} cast onto an ally's building at {c.Position}");
                        break;
                }
            };
        }
        for (int t = 0; t < 2500; t++)
        {
            sim.Tick();
            for (int p = 0; p < 4; p++)
            {
                EntityHandle target = sim.AiOf(p)!.Board.WaveTarget;
                if (target.Generation != 0 && w.Buildings.IsAlive(target) && w.AreAllied(p, w.Buildings.Owner[target.Index]))
                    bad.Add($"tick {sim.TickNumber}: seat {p}'s wave targets player {w.Buildings.Owner[target.Index]}'s building");
                // The enemy memory never counts an ally's building.
                Assert.True(sim.AiOf(p)!.Board.KnownEnemyBuildings <= KnownEnemyBuildingsUpperBound(w, p), $"seat {p} counts allied buildings");
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }
        _out.WriteLine($"seed {seed}: {attacks} Attacks, {attackMoves} AttackMoves, {casts} casts; waves {string.Join(", ", Enumerable.Range(0, 4).Select(p => sim.AiOf(p)!.Board.WavesStarted))}");
        Assert.True(attackMoves > 0, "no seat attack-moved: the scene fought nothing");
        Assert.True(w.Losses[0] + w.Losses[2] > 0 && w.Losses[1] + w.Losses[3] > 0, "a side lost nothing: no fight");
    }

    /// <summary>Every live enemy building, plus the player's ghost slots: the most the AI's enemy-building count can be.</summary>
    private static int KnownEnemyBuildingsUpperBound(World w, int p)
    {
        int n = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.IsEnemy(p, w.Buildings.Owner[k])) n++;
        return n + w.Fog.GhostCount(p);
    }

    /// <summary>
    /// BUG-1042's scene (M6-S5): four AI seats, teams 1, 1, 2, 2 on the 4-start 256 map (seats west, east, north, south), so
    /// every seat's ally starts at its hall's mirror, <paramref name="raiders"/> raiders of each seat in the next seat's base.
    /// </summary>
    internal static Simulation MirrorAllies(ulong seed, int raiders = 1)
    {
        PlayerSeat[] seats = Enumerable.Range(0, 4)
            .Select(p => new PlayerSeat(p % 2, SeatController.Ai, (AiDifficulty)((p + (int)seed) % 3), 1 + p / 2)).ToArray();
        return AiScenes.Generated(seats, seed, record: false, out _, units: 800, commands: 2048, raiders: raiders,
            configure: c => c with { Map = c.Map with { Width = 256, Height = 256, Forests = 24, GoldMines = 16, StartLocations = 4 } });
    }

    /// <summary>
    /// BUG-1042 regression (M6-S5): with the ally at the hall's mirror, the scout drops the mirror waypoint (the ally's base)
    /// without walking there, and its first grid point is the one farthest from the ally's buildings. Before the fix every
    /// seat's scout walked to its ally's base (target 8, the mirror) and then swept the ally's half nearest-first. No raiders
    /// (with them, a seat's raider in an enemy's base shows that base to its ally too, and no scout is needed).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void MirrorAllies_TheScout_SkipsTheAllysBase_AndSweepsFarthestFromItFirst(ulong seed)
    {
        Simulation sim = MirrorAllies(seed, raiders: 0);
        World w = sim.World;
        var firstGrid = new int[4];
        Array.Fill(firstGrid, -1);
        var sent = new bool[4];
        while (sim.TickNumber < 3000 && firstGrid.Any(g => g < 0))
        {
            sim.Tick();
            for (int p = 0; p < 4; p++)
            {
                Blackboard b = sim.AiOf(p)!.Board;
                sent[p] |= b.ScoutPhase == 1;
                Assert.True(b.ScoutTarget != Blackboard.ScoutHead - 1, $"seed {seed} tick {sim.TickNumber}: seat {p}'s scout walks to its ally's base (the mirror waypoint)");
                if (firstGrid[p] < 0 && b.ScoutTarget >= Blackboard.ScoutHead)
                {
                    firstGrid[p] = b.ScoutTarget;
                    Assert.True(b.ScoutVisited[Blackboard.ScoutHead - 1], $"seat {p}: the mirror waypoint was not dropped");
                    // The farthest grid point from the ally's buildings, among those not visited when the target was picked.
                    Vector2 chosen = b.ScoutPoints[b.ScoutTarget];
                    float best = AlliedDistance(w, p, chosen);
                    for (int k = Blackboard.ScoutHead; k < b.ScoutPoints.Length; k++)
                        if (!b.ScoutVisited[k] && k != b.ScoutTarget)
                            Assert.True(AlliedDistance(w, p, b.ScoutPoints[k]) <= best + 1e-3f, $"seat {p}: grid point {k} lies farther from the ally than the target {b.ScoutTarget}");
                }
                if (b.ScoutPhase == 2 && firstGrid[p] < 0) firstGrid[p] = int.MaxValue; // released (or dead) before the grid
            }
        }
        Assert.True(sent.All(s => s), "a seat never sent its scout");
        Assert.True(firstGrid.All(g => g >= 0), $"a scout never reached the grid within 3,000 ticks: {string.Join(", ", firstGrid)}");
        _out.WriteLine($"seed {seed}: first grid targets {string.Join(", ", firstGrid)} at tick {sim.TickNumber}");
    }

    /// <summary>Squared distance from <paramref name="at"/> to the nearest building of an ally (not itself) of <paramref name="p"/>; float.MaxValue with none.</summary>
    private static float AlliedDistance(World w, int p, Vector2 at)
    {
        float best = float.MaxValue;
        for (int k = 0; k < w.Buildings.Capacity; k++)
        {
            if (!w.Buildings.Alive[k] || w.Buildings.Owner[k] == p || w.IsEnemy(p, w.Buildings.Owner[k])) continue;
            best = Math.Min(best, Vector2.DistanceSquared(at, Centre(w, k)));
        }
        return best;
    }

    /// <summary>
    /// M6-S5 criterion 3, the AI's half: on the flat AI scene, an AI seat (player 0, team 1) whose own eyes see only its base,
    /// its ally (player 1) with a unit beside the enemy's (player 2, no team) hall. The AI's census counts that hall as a known
    /// enemy building and its nearest enemy building is that hall (its view answers through the ally's eyes); in the
    /// free-for-all twin (every team 0) the AI knows no enemy building of player 2.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEnemyHall_OnlyTheAllySees_IsAKnownEnemyBuilding_ForTheAi(bool allies)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human, SeatController.Human)
            .Select((s, p) => s with { Team = allies && p < 2 ? 1 : 0 }).ToArray();
        Simulation sim = AiScenes.Flat(seats);
        World w = sim.World;
        CombatScenes.Place(sim, 1, CombatScenes.HeavyInfantry, w.NavGrid.CellCenter(36, 14));
        int enemyHall = -1;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 2) enemyHall = k;
        AiPlayer ai = sim.AiOf(0)!;
        for (int t = 0; t < 60; t++) sim.Tick();
        Assert.True(ai.Thinks > 1, "setup: the AI never thought");
        Assert.True(w.Fog.CanSeeBuilding(1, enemyHall), "setup: the ally sees the enemy's hall");
        BuildingDef d = w.Data.Buildings[w.Buildings.TypeId[enemyHall]];
        for (int dy = 0; dy < d.FootprintHeight; dy++)
            for (int dx = 0; dx < d.FootprintWidth; dx++)
                Assert.True(w.Fog.OwnVisibility(0)[w.Buildings.Cell[enemyHall] + dy * w.Fog.Width + dx] != Vision.VisionConstants.Visible, "setup: player 0's own eyes see the hall");
        Assert.Equal(allies, ai.View.TryGetBuilding(enemyHall, out _));
        Assert.Equal(allies, ai.Board.KnownEnemyBuildings > 0);
        Assert.Equal(allies, ai.NearestEnemyBuilding(ai.HallPoint, out EntityHandle nearest, out _, out _) && nearest.Index == enemyHall);
    }

    [Fact]
    public void ATick_OfA2v2WithAiSeats_AllocatesNothing()
    {
        Simulation sim = Match(5);
        for (int t = 0; t < 600; t++) sim.Tick();
        Action ticks = () => { for (int t = 0; t < 40; t++) sim.Tick(); };
        AllocationProbe.AssertZero(ticks, _out);
        for (int p = 0; p < 4; p++) Assert.True(sim.AiOf(p)!.Thinks >= 640 / sim.AiOf(p)!.ThinkTicks - 1, $"seat {p} stopped thinking");
    }
}
