using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M5-3, session 2026-10-12-1930): AI seats that scout, rally, wave and retreat, combat on, over long runs. (1) Malazan v
/// Whirlwind, Normal, the early-wave data, three raiders each in the other's base, a generated 128 map, 6,000 ticks x 3 seeds;
/// (2) four AI seats (Easy, Normal, Hard, Hard) on the flat scene with plenty to spend, 6,000 ticks. After every tick: the
/// world invariants (finite positions on passable ground inside the map, 0 &lt; hp &lt;= max, resources and pop never
/// negative); every AI command of the tick names a live unit of its own player (as the tick before left it) and no unit gets
/// two in one tick; a second run of the same scene has the same hash at every tick. (1)'s recording plays back, every seat
/// human, to every checkpoint.
/// </summary>
public class AiWaveStressTests
{
    private readonly ITestOutputHelper _out;

    public AiWaveStressTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_early = new(() => AiScenes.EarlyWaves(2));

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(11UL)]
    public void TwoWavingAiSeats_SixThousandTicks_Invariants_Determinism_Playback(ulong seed)
    {
        GameData data = s_early.Value;
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, seed, record: true, out ReplayRecorder? rec, data: data, forests: 12, raiders: 3);
        Simulation twin = AiScenes.Generated(seats, seed, record: false, out _, data: data, forests: 12, raiders: 3);
        Run(sim, twin, 6000, $"seed {seed}");
        Assert.True(sim.AiOf(0)!.Board.WavesStarted + sim.AiOf(1)!.Board.WavesStarted > 0, $"seed {seed}: no wave in the run: it proves little");
        Replay r = rec!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? back));
        ReplayResult res = ReplayPlayer.Run(back!, data);
        Assert.True(res.Ok, $"seed {seed}: {res}");
        Assert.Equal(6000, res.TicksRun);
    }

    [Fact]
    public void FourWavingAiSeats_FlatScene_SixThousandTicks_Invariants_Determinism()
    {
        var seats = new PlayerSeat[]
        {
            new(0, SeatController.Ai, AiDifficulty.Easy), new(1, SeatController.Ai, AiDifficulty.Normal),
            new(0, SeatController.Ai, AiDifficulty.Hard), new(1, SeatController.Ai, AiDifficulty.Hard),
        };
        Simulation Make()
        {
            Simulation s = AiScenes.Flat(seats, s_early.Value, units: 600, commands: 1024, combat: true);
            for (int p = 0; p < 4; p++) BuildMaps.SetTotals(s, p, 3000, 3000);
            return s;
        }
        Simulation sim = Make();
        Run(sim, Make(), 6000, "flat 4 seats");
        int waves = 0;
        for (int p = 0; p < 4; p++) waves += sim.AiOf(p)!.Board.WavesStarted;
        Assert.True(waves > 0, "no wave in the run: it proves little");
    }

    /// <summary>Runs <paramref name="sim"/> and <paramref name="twin"/> side by side, checking after every tick (see the type's summary).</summary>
    private void Run(Simulation sim, Simulation twin, int ticks, string label)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        int players = w.Config.PlayerCount;
        var logs = new List<Command>[players];
        for (int p = 0; p < players; p++) logs[p] = sim.AiOf(p) is null ? new List<Command>() : AiScenes.Log(sim, p);
        var seen = new int[players];
        int[] born = new int[u.Capacity];
        bool[] bornOnGround = new bool[u.Capacity];
        bool[] aliveBefore = new bool[u.Capacity];
        int[] ownerBefore = new int[u.Capacity], genBefore = new int[u.Capacity];
        var perUnit = new HashSet<int>();
        int aiCommands = 0, unitOrders = 0, worstTick = 0;
        for (int t = 0; t < ticks; t++)
        {
            for (int i = 0; i < u.Capacity; i++)
            {
                aliveBefore[i] = u.Alive[i];
                ownerBefore[i] = u.Owner[i];
                genBefore[i] = u.Generation[i];
            }
            sim.Tick();
            twin.Tick();
            string at = $"{label} tick {sim.TickNumber}";
            Assert.True(sim.StateHash() == twin.StateHash(), $"{at}: the twin run's hash differs");
            perUnit.Clear();
            int thisTick = 0;
            for (int p = 0; p < players; p++)
            {
                for (int k = seen[p]; k < logs[p].Count; k++)
                {
                    Command c = logs[p][k];
                    aiCommands++;
                    thisTick++;
                    if (!c.IsUnitOrder) continue;
                    unitOrders++;
                    int i = c.Unit.Index;
                    Assert.True((uint)i < (uint)u.Capacity && aliveBefore[i] && ownerBefore[i] == c.Player && genBefore[i] == c.Unit.Generation,
                        $"{at}: player {c.Player} sent {c.Kind} to {c.Unit}, not a live unit of its own");
                    Assert.True(perUnit.Add(i), $"{at}: unit {c.Unit} got two commands in one tick");
                    if (c.Kind is CommandKind.Move or CommandKind.AttackMove)
                        Assert.True(g.WorldToCell(c.Position, out _, out _), $"{at}: {c.Kind} to {c.Position}, off the map"); // a building's centre is blocked: fine
                }
                seen[p] = logs[p].Count;
            }
            worstTick = Math.Max(worstTick, thisTick);
            for (int p = 0; p < players; p++)
                Assert.True(w.Gold[p] >= 0 && w.Wood[p] >= 0 && w.HalfPop[p] >= 0, $"{at}: player {p} gold {w.Gold[p]} wood {w.Wood[p]} pop {w.HalfPop[p]}");
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 pos = u.Position[i];
                Assert.True(float.IsFinite(pos.X) && float.IsFinite(pos.Y), $"{at}: unit {i} at {pos}");
                if (born[i] != u.Generation[i])
                {
                    born[i] = u.Generation[i];
                    bornOnGround[i] = g.WorldToCell(u.PrevPosition[i], out int bx, out int by) && g.IsPassable(bx, by);
                }
                if (bornOnGround[i])
                    Assert.True(g.WorldToCell(pos, out int x, out int y) && g.IsPassable(x, y), $"{at}: unit {i} on blocked ground or off the map at {pos}");
                Assert.True(u.Hp[i] > 0 && u.Hp[i] <= w.MaxHpOf(u.Owner[i], u.TypeId[i]), $"{at}: unit {i} hp {u.Hp[i]}");
            }
        }
        for (int p = 0; p < players; p++)
        {
            AiPlayer? ai = sim.AiOf(p);
            if (ai is null) continue;
            List<Command> l = logs[p];
            _out.WriteLine($"{label} player {p}: waves {ai.Board.WavesStarted}, AttackMoves {l.Count(c => c.Kind == CommandKind.AttackMove)}, " +
                $"Attacks {l.Count(c => c.Kind == CommandKind.Attack)}, Moves {l.Count(c => c.Kind == CommandKind.Move)}, Gathers {l.Count(c => c.Kind == CommandKind.Gather)}, " +
                $"army {ai.ArmyCount}, workers {ai.WorkerCount}, pop {w.HalfPop[p] / 2}, scout phase {ai.Board.ScoutPhase}, known enemy buildings {ai.Board.KnownEnemyBuildings}");
        }
        _out.WriteLine($"{label}: {aiCommands} AI commands ({unitOrders} unit orders), worst tick {worstTick}, units now {u.Count}");
    }
}
