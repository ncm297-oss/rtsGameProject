using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
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
/// M5-2: player 1 is an AI seat (Hard) playing through the same door under the random commands (many of them for its own
/// units and buildings): every command it sends is legal for what it saw at its think (<see cref="CheckAiCommand"/>), the
/// twins stay equal, and the replay (its commands logged, played back without the AI) still matches every checkpoint.
/// </summary>
public class CommandDoorFuzzStressTests
{
    private readonly ITestOutputHelper _out;

    public CommandDoorFuzzStressTests(ITestOutputHelper output) => _out = output;

    private const int Players = 2;

    /// <summary>docs/03: a defined kind, no flag but Queued, and Queued only on a unit order (Move, Stop, HoldPosition, AttackMove, Gather, Build, Repair, Attack; not SpawnBuilding, M3-2, Cancel, M3-3, the production kinds, M3-4, or Research, M3-5); an attack target on an Attack only (M4-2a).</summary>
    private static bool OracleWellFormed(int kind, int flags, bool hasTarget)
    {
        if (kind < 0 || kind > 17) return false; // M3-4: 11-14 Train, CancelTrain, SetRally, ClearRally; M3-5: 15 Research; M4-2a: 16 Attack; M4-4a: 17 UseAbility
        if ((flags & ~1) != 0) return false;
        if (hasTarget && kind != 16) return false;
        return flags == 0 || (kind >= 2 && kind <= 10 && kind != 6 && kind != 9) || kind == 16 || kind == 17;
    }

    /// <summary>M4-2a: an Attack's target: a live unit or building of either player most of the time, else a stale or bogus handle.</summary>
    private static Command AttackOn(ref SimRng rng, Simulation sim, int player, EntityHandle unit, bool queued)
    {
        bool building = rng.NextInt(0, 4) == 0;
        int capacity = building ? sim.World.Buildings.Capacity : sim.World.Units.Capacity;
        int start = rng.NextInt(0, capacity);
        EntityHandle target = new(start, 1);
        for (int n = 0; n < capacity; n++)
        {
            int k = (start + n) % capacity;
            bool alive = building ? sim.World.Buildings.Alive[k] : sim.World.Units.Alive[k];
            if (!alive) continue;
            target = building ? sim.World.Buildings.HandleOf(k) : new EntityHandle(k, sim.World.Units.Generation[k]);
            break;
        }
        target = rng.NextInt(0, 6) switch
        {
            0 => new EntityHandle(target.Index, target.Generation + 1),
            1 => new EntityHandle(-1, 0),
            _ => target,
        };
        return Command.Attack(player, unit, target, building, queued);
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
        // M3-H2: the building kinds aim at a live building's point most of the time (else the random point).
        Vector2 at = rng.NextInt(0, 4) != 0 ? BuildingPoint(ref rng, sim, p) : p;
        int buildingCell = rng.NextInt(0, 3) != 0 && sim.World.NavGrid.WorldToCell(at, out int bx, out int by) ? by * sim.World.NavGrid.Width + bx : rng.NextInt(-2, sim.World.NavGrid.Width * sim.World.NavGrid.Height + 2);
        Command c = rng.NextInt(0, 19) switch
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
            // M3-H2: every kind through the door (8-10 Build, Cancel, Repair, M3-3; 11-14 production, M3-4; 15 Research, M3-5).
            12 => Command.Build(player, h, rng.NextInt(-1, TestSim.Data.Buildings.Length + 1), p, queued),
            13 => rng.NextInt(0, 2) == 0 ? Command.Cancel(player, at) : Command.Repair(player, h, at, queued),
            14 => Command.Train(player, at, rng.NextInt(-1, TestSim.UnitTypeCount + 1)),
            15 => rng.NextInt(0, 2) == 0 ? Command.CancelTrain(player, at, rng.NextInt(-1, 7)) : Command.Research(player, at, rng.NextInt(-1, TestSim.Data.Techs.Length + 1)),
            16 => rng.NextInt(0, 2) == 0 ? Command.SetRally(player, buildingCell, p) : Command.ClearRally(player, at),
            // M4-2a: Attack, on live, stale and bogus targets of either player, units and buildings.
            10 or 11 => AttackOn(ref rng, sim, player, h, queued),
            // M4-4a: UseAbility, any index (most units have none; a mage has one).
            17 => Command.UseAbility(player, h, rng.NextInt(-1, 3), p, queued),
            _ => Command.Move(player, h, p, queued),
        };
        // About one command in five is malformed in one way.
        switch (rng.NextInt(0, 25))
        {
            case 0: c.Kind = (CommandKind)(18 + rng.NextInt(0, 3)); break; // 8-10 Build, Cancel, Repair (M3-3); 11-14 production (M3-4); 15 Research (M3-5); 16 Attack (M4-2a)
            case 1: c.Kind = (CommandKind)(-1 - rng.NextInt(0, 3)); break;
            case 2: c.Kind = (CommandKind)int.MinValue; break;
            case 3: c.Flags = 2 << rng.NextInt(0, 30); break;
            case 4: c.Flags = -1; break;
            case 5: c.Flags = Command.QueuedFlag; break; // malformed only on Noop / SpawnUnit / SpawnBuilding
            case 6: c.Player = rng.NextInt(0, 2) == 0 ? -1 : Players + rng.NextInt(0, 3); break;
            case 7: c.Target = new EntityHandle(rng.NextInt(0, 4), 1); break; // M4-2a: malformed except on an Attack
            case 8: c.TargetIsBuilding = true; break;
        }
        return c;
    }

    /// <summary>The center of a random live building's anchor cell, or <paramref name="fallback"/> with none.</summary>
    private static Vector2 BuildingPoint(ref SimRng rng, Simulation sim, Vector2 fallback)
    {
        BuildingStore b = sim.World.Buildings;
        NavGrid g = sim.World.NavGrid;
        int start = rng.NextInt(0, b.Capacity);
        for (int n = 0; n < b.Capacity; n++)
        {
            int k = (start + n) % b.Capacity;
            if (b.Alive[k]) return g.CellCenter(b.Cell[k] % g.Width, b.Cell[k] / g.Width);
        }
        return fallback;
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
            // Re-baselined for M4-1 (BUG-0135): a holder fights what comes in reach, so it is Idle or Attacking, never walking.
            if (u.Hold[i]) Assert.True(u.State[i] is UnitState.Idle or UnitState.Attacking && u.GoalCell[i] == -1, $"{at}: holding but {u.State[i]}, goal cell {u.GoalCell[i]}");
        }
    }

    /// <summary>M5-2: what the AI seat (player 1) could see at its think: the units and buildings as the previous tick left them.</summary>
    private sealed class AiSnapshot
    {
        public readonly bool[] UnitAlive;
        public readonly int[] UnitOwner, UnitGeneration;
        public readonly bool[] BuildingAlive, BuildingSite;
        public readonly int[] BuildingOwner, BuildingType, BuildingCell;
        public readonly int Tick;

        public AiSnapshot(World w)
        {
            Tick = w.TickNumber;
            UnitAlive = w.Units.Alive.ToArray();
            UnitOwner = w.Units.Owner.ToArray();
            UnitGeneration = w.Units.Generation.ToArray();
            BuildingAlive = w.Buildings.Alive.ToArray();
            BuildingSite = w.Buildings.UnderConstruction.ToArray();
            BuildingOwner = w.Buildings.Owner.ToArray();
            BuildingType = w.Buildings.TypeId.ToArray();
            BuildingCell = w.Buildings.Cell.ToArray();
        }
    }

    /// <summary>
    /// M5-2: an AI command is legal for what its seat saw: its own player, one of the kinds it sends (Build, Train, Research,
    /// Gather; M5-3 Move, Stop, AttackMove, Attack; M5-4c UseAbility), stamped for the next tick; a unit order names a live unit of its own (a
    /// UseAbility: one of the abilities its type lists, at a point on the map); a Train or Research points into one of
    /// its own finished buildings that trains the unit or may research the tech; a Build names a building of its faction on
    /// ground its player had explored, at a cell inside the map.
    /// </summary>
    private static void CheckAiCommand(World w, AiSnapshot seen, in Command c, string at)
    {
        Assert.True(c.Player == 1, $"{at}: the AI sent a command for player {c.Player}");
        Assert.True(c.IsWellFormed(), $"{at}: a malformed AI command");
        Assert.True(c.Tick == seen.Tick + 1, $"{at}: stamped {c.Tick}");
        Assert.True(c.Kind is CommandKind.Build or CommandKind.Train or CommandKind.Research or CommandKind.Gather
            or CommandKind.Move or CommandKind.Stop or CommandKind.AttackMove or CommandKind.Attack or CommandKind.UseAbility, $"{at}: kind {c.Kind}");
        NavGrid g = w.NavGrid;
        // M5-4c: the ability manager's casts: an own live unit, an ability its type lists, a point on the map.
        if (c.Kind == CommandKind.UseAbility)
        {
            int i = c.Unit.Index;
            Assert.True((uint)i < (uint)seen.UnitAlive.Length && seen.UnitAlive[i] && seen.UnitOwner[i] == 1 && seen.UnitGeneration[i] == c.Unit.Generation,
                $"{at}: UseAbility for unit {c.Unit}, not a live unit of its own");
            Assert.True(!w.Units.Alive[i] || w.Units.Generation[i] != c.Unit.Generation || (uint)c.TypeId < (uint)w.Data.Units[w.Units.TypeId[i]].Abilities.Length,
                $"{at}: UseAbility of ability {c.TypeId}, not one its type lists");
            Assert.True(g.WorldToCell(c.Position, out _, out _), $"{at}: UseAbility off the map at {c.Position}");
            return;
        }
        // M5-3: the scout's and the army's orders: an own live unit; an attack on a live enemy building.
        if (c.Kind is CommandKind.Move or CommandKind.Stop or CommandKind.AttackMove or CommandKind.Attack)
        {
            int i = c.Unit.Index;
            Assert.True((uint)i < (uint)seen.UnitAlive.Length && seen.UnitAlive[i] && seen.UnitOwner[i] == 1 && seen.UnitGeneration[i] == c.Unit.Generation,
                $"{at}: {c.Kind} for unit {c.Unit}, not a live unit of its own");
            if (c.Kind is CommandKind.Move or CommandKind.AttackMove)
                Assert.True(g.WorldToCell(c.Position, out _, out _), $"{at}: {c.Kind} off the map at {c.Position}");
            if (c.Kind == CommandKind.Attack)
            {
                int b = c.Target.Index;
                Assert.True(c.TargetIsBuilding && (uint)b < (uint)seen.BuildingAlive.Length && seen.BuildingAlive[b] && seen.BuildingOwner[b] != 1,
                    $"{at}: Attack on building {c.Target}, not a live enemy one");
            }
            return;
        }
        Assert.True(g.WorldToCell(c.Position, out int x, out int y), $"{at}: {c.Kind} off the map at {c.Position}");
        if (c.Kind is CommandKind.Build or CommandKind.Gather)
        {
            int i = c.Unit.Index;
            Assert.True((uint)i < (uint)seen.UnitAlive.Length && seen.UnitAlive[i] && seen.UnitOwner[i] == 1 && seen.UnitGeneration[i] == c.Unit.Generation,
                $"{at}: {c.Kind} for unit {c.Unit}, not a live unit of its own");
            Assert.True(w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker || !w.Units.Alive[i] || w.Units.Generation[i] != c.Unit.Generation, $"{at}: {c.Kind} for a non-worker");
        }
        if (c.Kind == CommandKind.Build)
        {
            Assert.True((uint)c.TypeId < (uint)w.Data.Buildings.Length && w.Data.Buildings[c.TypeId].Faction == w.FactionOf(1), $"{at}: Build of type {c.TypeId}");
            BuildingDef def = w.Data.Buildings[c.TypeId];
            for (int dy = 0; dy < def.FootprintHeight; dy++)
                for (int dx = 0; dx < def.FootprintWidth; dx++)
                    Assert.True(w.Fog.IsExplored(1, (y + dy) * g.Width + x + dx), $"{at}: Build on unexplored ground");
        }
        if (c.Kind is CommandKind.Train or CommandKind.Research)
        {
            int k = -1;
            for (int s = 0; s < seen.BuildingAlive.Length; s++)
            {
                if (!seen.BuildingAlive[s]) continue;
                BuildingDef d = w.Data.Buildings[seen.BuildingType[s]];
                int ax = seen.BuildingCell[s] % g.Width, ay = seen.BuildingCell[s] / g.Width;
                if (x >= ax && y >= ay && x < ax + d.FootprintWidth && y < ay + d.FootprintHeight) k = s;
            }
            Assert.True(k >= 0 && seen.BuildingOwner[k] == 1 && !seen.BuildingSite[k], $"{at}: {c.Kind} not at an own finished building");
            if (c.Kind == CommandKind.Train)
                Assert.True(w.Data.Units[c.TypeId].TrainedAtTypeId == seen.BuildingType[k], $"{at}: Train of {c.TypeId} where it isn't trained");
            else
                Assert.Contains(c.TypeId, w.Data.TechsResearchableAt(seen.BuildingType[k]));
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
            with
            {
                Map = MapGenParams.Default with { Forests = 6, GoldMines = 4 },
                // M5-2: player 1 is played by the AI too.
                Seats = ImmutableArray.Create(new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal), new PlayerSeat(1, SeatController.Ai, AiDifficulty.Hard)),
            };
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
        int accepted = 0, malformed = 0, badPlayer = 0, full = 0, aiCommands = 0;
        var perKind = new int[18];
        for (int t = 0; t < ticks; t++)
        {
            int n = rng.NextInt(0, 2) == 0 ? rng.NextInt(0, 6) : rng.NextInt(0, 36); // bursts fill the queue (36 since M3-2: longer runs of orders that drop at apply)
            for (int k = 0; k < n; k++)
            {
                Command c = RandomCommand(ref rng, a, open, nodes);
                ulong hashA = a.StateHash(), hashB = b.StateHash();
                int pending = a.PendingCommandCount;
                bool playerOk = (uint)c.Player < Players;
                bool wellFormed = OracleWellFormed((int)c.Kind, c.Flags, c.Target != default || c.TargetIsBuilding);
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
                    perKind[(int)c.Kind]++;
                    Assert.Equal(pending + 1, a.PendingCommandCount);
                    continue;
                }
                Assert.True(a.StateHash() == hashA && b.StateHash() == hashB, $"seed {seed} tick {t}: a refused {c.Kind} (flags {c.Flags}, player {c.Player}) changed the state");
                Assert.Equal(pending, a.PendingCommandCount);
            }
            var seen = new AiSnapshot(a.World);
            int logged = rec.CommandCount;
            a.Tick();
            b.Tick();
            // M5-2: the AI's commands are the ones logged during the tick (its think runs at the tick's start).
            for (int i = logged; i < rec.CommandCount; i++)
            {
                CheckAiCommand(a.World, seen, rec.CommandAt(i), $"seed {seed} tick {a.TickNumber - 1}");
                aiCommands++;
            }
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {a.TickNumber}");
            CheckInvariants(a, born, bornAt, bornOnGround);
        }
        _out.WriteLine($"seed {seed}: {a.World.Buildings.Count} buildings, gold {a.World.Gold[0]} / {a.World.Gold[1]}, wood {a.World.Wood[0]} / {a.World.Wood[1]}");
        Replay r = rec.ToReplay();
        Assert.Equal(accepted + aiCommands, r.Commands.Length);
        Assert.Equal(a.AiOf(1)!.CommandsSent, aiCommands);
        Assert.True(aiCommands > 0, $"seed {seed}: the AI seat sent nothing");
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? back));
        ReplayResult res = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(res.Ok, $"seed {seed}: replay {res.Error} at tick {res.Tick}");
        Assert.Equal(ticks, res.TicksRun);
        _out.WriteLine($"seed {seed}: {accepted} accepted, {malformed} malformed refused, {badPlayer} unknown players refused, {full} refused full; {aiCommands} AI commands; {a.World.Units.Count} units alive at the end; replay of {r.Checkpoints.Length} checkpoints matched");
        Assert.True(accepted > 1000 && malformed > 100 && badPlayer > 20 && full > 20, "precondition: every door path exercised");
        // M3-H2: every defined kind, 0 to 16 (M4-2a: Attack), went through the door accepted.
        Assert.True(perKind.All(n => n > 0), "accepted per kind: " + string.Join(", ", perKind));
    }

    /// <summary>
    /// M5-4b: a random player is defeated mid-run (its buildings and workers taken away between ticks, so nothing of it can
    /// stand again) while both players' random commands keep coming. Every later command of the defeated player, of any kind,
    /// is dropped where it applies: the run hashes equal, after every tick, to a twin fed the same stream with a Noop of
    /// that player in place of each of them (a command never sent would differ by the player's hashed sequence counter
    /// alone). The defeated player's other units stay.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void ADefeatedPlayersLaterCommands_AreAllDropped_HashEqualToTheFilteredStream(ulong seed)
    {
        const int ticks = 1200, unitCapacity = 64, commandCapacity = 40;
        SimConfig config = TestSim.Config(Seed: seed, PlayerCount: Players, UnitCapacity: unitCapacity, CommandCapacity: commandCapacity)
            with { Map = MapGenParams.Default with { Forests = 6, GoldMines = 4 } };
        var a = new Simulation(config);
        var b = new Simulation(config);
        NavGrid g = a.World.NavGrid;
        var open = new List<Vector2>();
        for (int c = 0; c < g.Width * g.Height; c += 7)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(g.CellCenter(c % g.Width, c / g.Width));
        var nodes = new List<Vector2>();
        for (int n = 0; n < a.World.Resources.Capacity; n++)
            if (a.World.Resources.Alive[n]) nodes.Add(g.CellCenter(a.World.Resources.Cell[n] % g.Width, a.World.Resources.Cell[n] / g.Width));
        var rng = new SimRng(seed, 4242);
        int loser = rng.NextInt(0, Players), defeatAt = rng.NextInt(300, 700), dropped = 0, kept = 0;
        // Each player builds first: a building of its own, through the door (the house of its faction), on both twins.
        for (int p = 0; p < Players; p++)
        {
            int house = Rts.Sim.ViewApi.StartBase.BuildingOfSlot(TestSim.Data, a.World.FactionOf(p), BuildingSlot.House);
            Command spawn = Command.SpawnBuilding(p, house, open[(p + 1) * open.Count / 3]);
            a.Enqueue(spawn);
            b.Enqueue(spawn);
        }
        for (int t = 0; t < ticks; t++)
        {
            if (t == defeatAt)
            {
                // Between ticks: the loser's buildings and workers go, on both twins alike (test seams).
                foreach (Simulation s in new[] { a, b })
                {
                    World w = s.World;
                    for (int k = 0; k < w.Buildings.Capacity; k++)
                        if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == loser) w.Buildings.Free(w.Buildings.HandleOf(k));
                    for (int i = 0; i < w.Units.Capacity; i++)
                        if (w.Units.Alive[i] && w.Units.Owner[i] == loser && w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker)
                            w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
                }
            }
            bool gone = a.World.IsDefeated(loser);
            Assert.Equal(gone, b.World.IsDefeated(loser));
            // The tick of the defeat itself takes no commands: one applying with it could place a building again.
            int n = t == defeatAt ? 0 : rng.NextInt(0, 2) == 0 ? rng.NextInt(0, 6) : rng.NextInt(0, 20);
            for (int k = 0; k < n; k++)
            {
                Command c = RandomCommand(ref rng, a, open, nodes);
                bool loserBeforeDefeat = t > defeatAt && !gone && c.Player == loser; // between the seam and the defeat
                if ((uint)c.Player >= Players || !OracleWellFormed((int)c.Kind, c.Flags, c.Target != default || c.TargetIsBuilding)
                    || a.PendingCommandCount >= commandCapacity || loserBeforeDefeat) continue;
                a.Enqueue(c);
                if (gone && c.Player == loser)
                {
                    b.Enqueue(Command.Noop(loser));
                    dropped++;
                }
                else
                {
                    b.Enqueue(c);
                    kept++;
                }
            }
            a.Tick();
            b.Tick();
            if (a.PendingCommandCount == 0)
                Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: the twins differ after tick {a.TickNumber} (player {loser} defeated: {gone})");
        }
        Assert.True(a.World.IsDefeated(loser), $"seed {seed}: player {loser} was never defeated");
        Assert.False(a.World.IsDefeated(1 - loser));
        int left = 0;
        for (int i = 0; i < a.World.Units.Capacity; i++)
            if (a.World.Units.Alive[i] && a.World.Units.Owner[i] == loser) left++;
        _out.WriteLine($"seed {seed}: player {loser} defeated at about tick {defeatAt}; {dropped} of its commands dropped, {kept} applied; {left} of its units left");
        Assert.True(dropped > 100, $"seed {seed}: only {dropped} commands of the defeated player were sent");
    }
}
