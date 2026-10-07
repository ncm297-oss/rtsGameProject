using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// Tick-loop invariant fuzz at the command door (QA, session 2026-10-06-0905, after M1-9 / BUG-0054):
/// seeded streams that mix every command kind (queued and not, live, stale, foreign and bogus handles,
/// NaN and off-map points, bad type ids) with malformed commands (undefined kinds, unknown flag bits,
/// the queued flag on Noop / SpawnUnit, unknown players) and a queue kept near full. Every Enqueue is
/// checked against an independent oracle of docs/03 "Tick model": refused ones throw the documented
/// exception and change nothing (hash, pending count, sequence numbers); accepted ones queue exactly one.
/// After every tick: twin hashes equal, units finite on passable ground, owners valid, holders Idle with no
/// goal, queues in range, pending within capacity. The recording reads back and replays to every checkpoint.
/// </summary>
public class CommandDoorFuzzStressTests
{
    private readonly ITestOutputHelper _out;

    public CommandDoorFuzzStressTests(ITestOutputHelper output) => _out = output;

    private const int Players = 2;

    /// <summary>docs/03: a defined kind, no flag but Queued, and Queued only on a unit order (Move, Stop, HoldPosition, AttackMove, Gather, Build, Repair; not SpawnBuilding, M3-2, or Cancel, M3-3).</summary>
    private static bool OracleWellFormed(int kind, int flags)
    {
        if (kind < 0 || kind > 10) return false;
        if ((flags & ~1) != 0) return false;
        return flags == 0 || (kind >= 2 && kind != 6 && kind != 9);
    }

    private static Command RandomCommand(ref SimRng rng, Simulation sim, List<Vector2> open, List<Vector2> nodes)
    {
        UnitStore u = sim.World.Units;
        int slot = rng.NextInt(0, u.Capacity);
        EntityHandle h = rng.NextInt(0, 10) switch
        {
            0 => new EntityHandle(slot, u.Generation[slot] + 1),
            1 => new EntityHandle(-1, 0),
            2 => new EntityHandle(u.Capacity + 5, 0),
            _ => new EntityHandle(slot, u.Generation[slot]),
        };
        int player = u.Alive[slot] && rng.NextInt(0, 6) != 0 ? u.Owner[slot] : rng.NextInt(0, Players);
        Vector2 p = rng.NextInt(0, 25) switch
        {
            0 => new Vector2(float.NaN, 3f),
            1 => new Vector2(-50f, 1e9f),
            2 => new Vector2(float.PositiveInfinity, 0f),
            _ => open[rng.NextInt(0, open.Count)] + new Vector2(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f),
        };
        bool queued = rng.NextInt(0, 2) == 0;
        Command c = rng.NextInt(0, 12) switch
        {
            0 or 1 => Command.SpawnUnit(rng.NextInt(0, Players), rng.NextInt(-1, TestSim.UnitTypeCount + 1), p),
            2 => Command.Noop(player),
            3 or 4 => Command.Move(player, h, p, queued),
            5 => Command.AttackMove(player, h, p, queued),
            6 => Command.Stop(player, h, queued),
            7 => Command.HoldPosition(player, h, queued),
            // M3-2: a rare building (each one blocks 16 cells for good), and Gathers, half of them on a node.
            8 => rng.NextInt(0, 8) == 0 ? Command.SpawnBuilding(rng.NextInt(0, Players), rng.NextInt(-1, TestSim.Data.Buildings.Length + 1), p) : Command.Move(player, h, p, queued),
            9 => Command.Gather(player, h, rng.NextInt(0, 2) == 0 ? nodes[rng.NextInt(0, nodes.Count)] : p, queued),
            _ => Command.Move(player, h, p, queued),
        };
        // About one command in five is malformed in one way.
        switch (rng.NextInt(0, 25))
        {
            case 0: c.Kind = (CommandKind)(11 + rng.NextInt(0, 3)); break; // 8-10 are Build, Cancel, Repair since M3-3
            case 1: c.Kind = (CommandKind)(-1 - rng.NextInt(0, 3)); break;
            case 2: c.Kind = (CommandKind)int.MinValue; break;
            case 3: c.Flags = 2 << rng.NextInt(0, 30); break;
            case 4: c.Flags = -1; break;
            case 5: c.Flags = Command.QueuedFlag; break; // malformed only on Noop / SpawnUnit / SpawnBuilding
            case 6: c.Player = rng.NextInt(0, 2) == 0 ? -1 : Players + rng.NextInt(0, 3); break;
        }
        return c;
    }

    /// <summary>
    /// Per-tick invariants. A raw SpawnUnit may place a unit off the map or on blocked ground (docs/03
    /// "Movement": such a unit stops); <paramref name="born"/> / <paramref name="bornAt"/> track each
    /// slot's generation and spawn point, and a unit born off passable ground must never move.
    /// </summary>
    private static void CheckInvariants(Simulation sim, int[] born, Vector2[] bornAt, bool[] bornOnGround)
    {
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        Assert.True(sim.PendingCommandCount <= sim.World.Config.CommandCapacity);
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            string at = $"tick {sim.TickNumber} unit {i}";
            Vector2 p = u.Position[i];
            Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"{at}: position {p}");
            if (born[i] != u.Generation[i])
            {
                born[i] = u.Generation[i];
                bornAt[i] = u.PrevPosition[i];
                // Judged at birth: since M3-2 a building can later cover a cell a unit was born on and left.
                bornOnGround[i] = g.WorldToCell(bornAt[i], out int bx, out int by) && g.IsPassable(bx, by);
            }
            if (bornOnGround[i])
                Assert.True(g.WorldToCell(p, out int x, out int y) && g.IsPassable(x, y), $"{at}: on blocked ground or off the map at {p}");
            else
                Assert.True(p == bornAt[i], $"{at}: spawned off passable ground at {bornAt[i]}, moved to {p}");
            Assert.True((uint)u.Owner[i] < Players, $"{at}: owner {u.Owner[i]}");
            Assert.True((uint)u.QueueCount[i] <= OrderConstants.QueueCapacity, $"{at}: queue {u.QueueCount[i]}");
            if (u.Hold[i]) Assert.True(u.State[i] == UnitState.Idle && u.GoalCell[i] == -1, $"{at}: holding but {u.State[i]}, goal cell {u.GoalCell[i]}");
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    [InlineData(7UL)]
    [InlineData(8UL)]
    public void MixedAndMalformedCommands_2000Ticks_DoorOracle_TwinsAndInvariants_ReplayRoundTrip(ulong seed)
    {
        const int ticks = 2000, unitCapacity = 64, commandCapacity = 40;
        // M3-2: with mines and forests, so Gather orders land on nodes and SpawnBuilding meets them.
        SimConfig config = TestSim.Config(Seed: seed, PlayerCount: Players, UnitCapacity: unitCapacity, CommandCapacity: commandCapacity)
            with { Map = MapGenParams.Default with { Forests = 6, GoldMines = 4 } };
        var a = new Simulation(config);
        var rec = new ReplayRecorder(a, checkpointInterval: 50);
        var b = new Simulation(config);
        NavGrid g = a.World.NavGrid;
        var open = new List<Vector2>();
        for (int c = 0; c < g.Width * g.Height; c += 7)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(g.CellCenter(c % g.Width, c / g.Width));
        var nodes = new List<Vector2>();
        for (int n = 0; n < a.World.Resources.Capacity; n++)
            if (a.World.Resources.Alive[n]) nodes.Add(g.CellCenter(a.World.Resources.Cell[n] % g.Width, a.World.Resources.Cell[n] / g.Width));
        Assert.NotEmpty(nodes);
        var rng = new SimRng(seed, 8128);
        var born = new int[unitCapacity];
        Array.Fill(born, -1);
        var bornAt = new Vector2[unitCapacity];
        var bornOnGround = new bool[unitCapacity];
        int accepted = 0, malformed = 0, badPlayer = 0, full = 0;
        for (int t = 0; t < ticks; t++)
        {
            int n = rng.NextInt(0, 2) == 0 ? rng.NextInt(0, 6) : rng.NextInt(0, 36); // bursts fill the queue (36 since M3-2: longer runs of orders that drop at apply)
            for (int k = 0; k < n; k++)
            {
                Command c = RandomCommand(ref rng, a, open, nodes);
                ulong hashA = a.StateHash(), hashB = b.StateHash();
                int pending = a.PendingCommandCount;
                bool playerOk = (uint)c.Player < Players;
                bool wellFormed = OracleWellFormed((int)c.Kind, c.Flags);
                bool room = pending < commandCapacity;
                if (!playerOk)
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => a.Enqueue(c));
                    Assert.Throws<ArgumentOutOfRangeException>(() => b.Enqueue(c));
                    badPlayer++;
                }
                else if (!wellFormed)
                {
                    Assert.Throws<ArgumentException>(() => a.Enqueue(c));
                    Assert.Throws<ArgumentException>(() => b.Enqueue(c));
                    malformed++;
                }
                else if (!room)
                {
                    Assert.Throws<InvalidOperationException>(() => a.Enqueue(c));
                    Assert.Throws<InvalidOperationException>(() => b.Enqueue(c));
                    full++;
                }
                else
                {
                    a.Enqueue(c);
                    b.Enqueue(c);
                    accepted++;
                    Assert.Equal(pending + 1, a.PendingCommandCount);
                    continue;
                }
                Assert.True(a.StateHash() == hashA && b.StateHash() == hashB, $"seed {seed} tick {t}: a refused {c.Kind} (flags {c.Flags}, player {c.Player}) changed the state");
                Assert.Equal(pending, a.PendingCommandCount);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {a.TickNumber}");
            CheckInvariants(a, born, bornAt, bornOnGround);
        }
        _out.WriteLine($"seed {seed}: {a.World.Buildings.Count} buildings, gold {a.World.Gold[0]} / {a.World.Gold[1]}, wood {a.World.Wood[0]} / {a.World.Wood[1]}");
        Replay r = rec.ToReplay();
        Assert.Equal(accepted, r.Commands.Length);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? back));
        ReplayResult res = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(res.Ok, $"seed {seed}: replay {res.Error} at tick {res.Tick}");
        Assert.Equal(ticks, res.TicksRun);
        _out.WriteLine($"seed {seed}: {accepted} accepted, {malformed} malformed refused, {badPlayer} unknown players refused, {full} refused full; {a.World.Units.Count} units alive at the end; replay of {r.Checkpoints.Length} checkpoints matched");
        Assert.True(accepted > 1000 && malformed > 100 && badPlayer > 20 && full > 20, "precondition: every door path exercised");
    }
}
