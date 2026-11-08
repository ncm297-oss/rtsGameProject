using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-5 (session 2026-11-08-0726), focus 2: the refusal seam (<see cref="Simulation.RefusedForTests"/>) under a scripted
/// human seat that sends deliberately bad commands every tick next to a Normal AI seat: dead handles (a live slot's next
/// generation, a slot past the store, a negative index), another player's unit, off-map points (every positional kind),
/// Trains past the production queue's capacity or the gold, Trains at open ground; mixed with good Moves. Every bad command
/// of the script must be reported exactly once, at the tick it applies, with the refusal the state predicts (the script's
/// commands apply first in phase 1: player 0, sequence order); a good one never. A twin sim without the hook, fed the same
/// commands, has the same hash on every tick.
/// </summary>
[Collection(SerialCollection.Name)]
public class CommandRefusalSeamFuzzQaTests
{
    private readonly ITestOutputHelper _out;

    public CommandRefusalSeamFuzzQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>Odd seeds start on the <c>high</c> preset (1,000 gold: the queue fills before the gold runs out), even on standard.</summary>
    private static Simulation Scene(ulong seed) =>
        AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Human, SeatController.Ai), units: 128, commands: 2048, seed: seed,
            startingResources: seed % 2 == 1 ? "high" : StartingResourcesDef.StandardId);

    private static List<EntityHandle> Live(World w, int owner)
    {
        var list = new List<EntityHandle>();
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == owner) list.Add(new EntityHandle(i, w.Units.Generation[i]));
        return list;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void ScriptedBadCommands_EachReportedOnce_WithThePredictedRefusal_AndTheHashUnchanged(ulong seed)
    {
        Simulation hooked = Scene(seed), plain = Scene(seed);
        World w = hooked.World;
        GameData d = w.Data;
        var rng = new Random((int)seed * 7919); // test-side script only, never the sim
        (int hx, int hy) = AiScenes.HallAt(0);
        Vector2 hall = w.NavGrid.CellCenter(hx + 1, hy + 1), open = w.NavGrid.CellCenter(30, 40);
        int worker = StartBase.UnitOfSlot(d, w.FactionOf(0), UnitSlot.Worker);
        int hallSlot = w.Buildings.SlotAt(hx, hy);
        int cost = d.Units[worker].CostGold, costWood = d.Units[worker].CostWood;

        var seen = new List<(Command C, CommandRefusal Why, int Detail, int Tick)>();
        hooked.RefusedForTests = (c, why, detail) => seen.Add((c, why, detail, hooked.TickNumber));
        // Expected, by player 0's sequence: (refusal, detail) or None for an applied command.
        var expected = new Dictionary<int, (CommandRefusal Why, int Detail, int ApplyTick)>();
        var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int sentBad = 0, sentGood = 0, aiRefused = 0;
        Vector2[] offMap = { new(-5f, -5f), new(1e6f, 3f), new(3f, w.NavGrid.Height * Map.MapConstants.CellSize + 0.01f), new(-0.001f, 10f) };

        void Send(Command c, CommandRefusal why, int detail = 0)
        {
            hooked.Enqueue(c);
            plain.Enqueue(c);
            // The sim stamps player 0's sequence from 0 up, one per command: the script is player 0's only sender.
            expected[sentBad + sentGood] = (why, detail, hooked.TickNumber + 1);
            if (why == CommandRefusal.None) sentGood++; else sentBad++;
        }

        // A Train at the hall is judged against the state its tick starts from, known only after the tick between: a command
        // enqueued at tick T is stamped T + 1 and applies in the Tick() that starts at T + 1 (after production ran once more).
        var pendingTrains = new List<int>();
        void SendHallTrain()
        {
            pendingTrains.Add(sentBad + sentGood);
            hooked.Enqueue(Command.Train(0, hall, worker));
            plain.Enqueue(Command.Train(0, hall, worker));
            expected[sentBad + sentGood] = (CommandRefusal.None, -1, hooked.TickNumber + 1); // resolved below
            sentBad++;
        }

        for (int t = 0; t < 1500; t++)
        {
            List<EntityHandle> mine = Live(w, 0), theirs = Live(w, 1);
            int burst = rng.Next(0, 4);
            for (int b = 0; b < burst; b++)
            {
                switch (rng.Next(0, 9))
                {
                    case 0: // dead handle: a live slot's next generation
                        if (mine.Count > 0)
                        {
                            EntityHandle h = mine[rng.Next(mine.Count)];
                            Send(Command.Move(0, new EntityHandle(h.Index, h.Generation + 1), open), CommandRefusal.UnitGone);
                        }
                        break;
                    case 1: // dead handle: out of the store's range, both ways
                        Send(Command.Move(0, new EntityHandle(rng.Next(2) == 0 ? w.Units.Capacity + rng.Next(1000) : -1 - rng.Next(1000), 1), open), CommandRefusal.UnitGone);
                        break;
                    case 2: // another player's unit
                        if (theirs.Count > 0) Send(Command.AttackMove(0, theirs[rng.Next(theirs.Count)], open), CommandRefusal.NotOwner);
                        break;
                    case 3: // off-map point, every positional kind
                        if (mine.Count > 0)
                        {
                            EntityHandle h = mine[rng.Next(mine.Count)];
                            Vector2 p = offMap[rng.Next(offMap.Length)];
                            Command c = rng.Next(4) switch
                            {
                                0 => Command.Move(0, h, p),
                                1 => Command.AttackMove(0, h, p, queued: rng.Next(2) == 0),
                                2 => Command.Gather(0, h, p),
                                _ => Command.Build(0, h, StartBase.BuildingOfSlot(d, w.FactionOf(0), BuildingSlot.House), p),
                            };
                            Send(c, CommandRefusal.OffMap);
                        }
                        break;
                    case 4: // a Train at open ground
                        Send(Command.Train(0, open, worker), CommandRefusal.TrainRefused, (int)TrainError.NoBuilding);
                        break;
                    case 5:
                    case 6: // a burst of Trains past the queue's capacity (or the gold)
                        if (!w.Buildings.Alive[hallSlot]) break;
                        int n = rng.Next(1, EconomyConstants.ProductionQueueCapacity + 3);
                        for (int k = 0; k < n; k++) SendHallTrain();
                        break;
                    case 7: // a dead handle spammed: the same command three times
                        for (int k = 0; k < 3; k++) Send(Command.Stop(0, new EntityHandle(w.Units.Capacity - 1, 9999)), CommandRefusal.UnitGone);
                        break;
                    default: // a good Move
                        if (mine.Count > 0) Send(Command.Move(0, mine[rng.Next(mine.Count)], w.NavGrid.CellCenter(20 + rng.Next(20), 20 + rng.Next(20))), CommandRefusal.None);
                        break;
                }
            }
            hooked.Tick();
            plain.Tick();
            Assert.True(plain.StateHash() == hooked.StateHash(), $"seed {seed}: the hook changed the hash at tick {hooked.TickNumber}");
            // Now the state this tick's batch applies to: phase 1 runs player 0's commands first, in sequence order, and the
            // batch's other commands are all dropped or Moves (no cost, no queue).
            int queue = w.Buildings.Alive[hallSlot] ? w.Buildings.QueueCount[hallSlot] : 0;
            int gold = w.Gold[0], wood = w.Wood[0];
            foreach (int s in pendingTrains)
            {
                int applyTick = expected[s].ApplyTick;
                if (queue >= EconomyConstants.ProductionQueueCapacity) expected[s] = (CommandRefusal.TrainRefused, (int)TrainError.QueueFull, applyTick);
                else if (gold < cost || wood < costWood) expected[s] = (CommandRefusal.TrainRefused, (int)TrainError.CannotAfford, applyTick);
                else
                {
                    expected[s] = (CommandRefusal.None, 0, applyTick);
                    queue++;
                    gold -= cost;
                    wood -= costWood;
                    sentBad--;
                    sentGood++;
                }
            }
            pendingTrains.Clear();
        }
        hooked.Tick();
        plain.Tick();
        Assert.Equal(plain.StateHash(), hooked.StateHash());

        // Every refusal of player 0 matches its prediction, once; every predicted refusal is seen.
        var reported = new HashSet<int>();
        foreach ((Command c, CommandRefusal why, int detail, int tick) in seen)
        {
            if (c.Player != 0)
            {
                aiRefused++;
                continue;
            }
            string kind = $"{c.Kind}:{why}" + (why == CommandRefusal.TrainRefused ? ":" + (TrainError)detail : "");
            tally[kind] = tally.TryGetValue(kind, out int k) ? k + 1 : 1;
            Assert.True(reported.Add(c.Sequence), $"seed {seed}: sequence {c.Sequence} ({c.Kind}) reported twice");
            Assert.True(expected.TryGetValue(c.Sequence, out var e), $"seed {seed}: sequence {c.Sequence} unknown");
            Assert.True(e.Why == why && e.Detail == detail, $"seed {seed}: sequence {c.Sequence} {c.Kind} at tick {c.Tick}: expected {e.Why}:{e.Detail}, reported {why}:{detail}");
            Assert.Equal(e.ApplyTick, c.Tick);
            Assert.Equal(c.Tick, tick);
        }
        foreach ((int s, (CommandRefusal why, int _, int _)) in expected)
            if (why != CommandRefusal.None) Assert.True(reported.Contains(s), $"seed {seed}: sequence {s} ({why}) never reported");
            else Assert.False(reported.Contains(s), $"seed {seed}: good command {s} reported");
        if (seed % 2 == 1) Assert.True(tally.ContainsKey("Train:TrainRefused:QueueFull"), "the high preset never filled the queue");
        _out.WriteLine($"seed {seed}: {sentBad} bad, {sentGood} good sent; reported {reported.Count}; AI refusals {aiRefused}; {string.Join(", ", tally.Select(kv => $"{kv.Key} {kv.Value}"))}");
        Assert.Equal(sentBad, reported.Count);
    }
}
