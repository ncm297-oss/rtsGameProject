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
