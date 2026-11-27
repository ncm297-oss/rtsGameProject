using System.Buffers.Binary;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Save;
using Rts.Sim.Tests.Draw;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M6-S13, session 2026-11-25-1437): attacks on the draw rule. An independent oracle (<see cref="DrawQaOracle"/>, every
/// term scanned from the stores) is compared with the sim every tick under seeded random orders with cancels, trains,
/// gathers and kills; the edges the brief's QA focus names (refund paths, teams, a defeat at clock N - 1, the latch after a
/// draw, the last gold leaving a node with its worker), saves at every 500th tick of a clock run on the shipped wait, and a
/// forged save part the developer's list does not cover.
/// </summary>
public class DrawRuleQaTests
{
    private readonly ITestOutputHelper _out;

    public DrawRuleQaTests(ITestOutputHelper output) => _out = output;

    // ---------------- the oracle fuzz ----------------

    /// <summary>A small staged match for seed <paramref name="seed"/>: 2-4 seats (a 2v2 sometimes), a hall each, 0-3 soldiers, 0-2 workers, a site sometimes, banks near the cheapest unit, 0-2 small gold nodes.</summary>
    internal static Simulation Scene(ulong seed, GameData data, out SimRng rng)
    {
        rng = new SimRng(seed, 1730);
        int players = 2 + rng.NextInt(0, 3);
        int[]? teams = players == 4 && rng.NextInt(0, 2) == 0 ? new[] { 1, 2, 1, 2 } : null;
        Simulation sim = DrawScenes.Empty(data, players, 48, teams, seed: seed);
        World w = sim.World;
        var banks = new int[players];
        var woods = new int[players];
        for (int p = 0; p < players; p++)
        {
            (int x, int y) = DrawScenes.Corner(sim, p);
            DrawScenes.Building(sim, p, x, y, rng.NextInt(0, 2) == 0 ? BuildingSlot.TownHall : BuildingSlot.InfantryHall);
            // Biased toward a spent start (most banks short, few workers, few nodes) so the clock runs in most scenes and the
            // orders (trains, cancels, gathers, kills) push it across the boundary both ways.
            int soldiers = 1 + rng.NextInt(0, 3), workers = rng.NextInt(0, 4) == 0 ? 1 : 0;
            for (int k = 0; k < soldiers; k++) DrawScenes.Unit(sim, p, x + k, y + 5);
            for (int k = 0; k < workers; k++) DrawScenes.Unit(sim, p, x + k, y + 7, UnitSlot.Worker);
            // A site that costs gold, so a Cancel refunds some (the House costs none).
            if (rng.NextInt(0, 3) == 0) DrawScenes.Building(sim, p, x + 6, y, BuildingSlot.CasterHall, site: true);
            int cheapest = DrawScenes.Cheapest(sim, p);
            int roll = rng.NextInt(0, 20);
            banks[p] = roll < 10 ? 0 : roll < 15 ? cheapest - 1 : roll < 17 ? cheapest : rng.NextInt(0, 2 * cheapest);
            // M6-SH3: wood is counted now; the bank holds the cheapest unit's wood, one short, or none.
            int wood = Draw.DrawReachTests.CheapestByGold(w, p).CostWood, woodRoll = rng.NextInt(0, 3);
            woods[p] = woodRoll == 0 ? 0 : woodRoll == 1 ? wood - 1 : wood;
        }
        int nodes = rng.NextInt(0, 3) == 0 ? 1 : 0;
        for (int n = 0; n < nodes; n++) DrawScenes.GoldNode(sim, 18 + 6 * n, 22, 1 + rng.NextInt(0, 30));
        DrawScenes.Broke(sim);
        for (int p = 0; p < players; p++)
        {
            w.Ledger.Gold[p] = banks[p];
            w.Ledger.Wood[p] = woods[p];
        }
        return sim;
    }

    /// <summary>One random order (or a soldier set to 1 hp) for a random seat.</summary>
    internal static void Disturb(Simulation sim, ref SimRng rng)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        var live = new List<int>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) live.Add(i);
        var builds = new List<int>();
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k]) builds.Add(k);
        int kind = rng.NextInt(0, 11);
        if (kind <= 5 && live.Count > 0)
        {
            int i0 = live[rng.NextInt(0, live.Count)];
            var h = new EntityHandle(i0, u.Generation[i0]);
            int owner = u.Owner[i0];
            var to = new Vector2(4f + rng.NextFloat() * 88f, 4f + rng.NextFloat() * 88f);
            int j = live[rng.NextInt(0, live.Count)];
            switch (kind)
            {
                case 0: sim.Enqueue(Command.Move(owner, h, to)); break;
                case 1: sim.Enqueue(Command.AttackMove(owner, h, u.Position[j])); break;
                case 2:
                    if (u.Owner[j] != owner && w.IsEnemy(owner, u.Owner[j]))
                    {
                        var t = new EntityHandle(j, u.Generation[j]);
                        CombatScenes.Spot(sim, owner, t);
                        sim.Enqueue(Command.Attack(owner, h, t, isBuilding: false));
                    }
                    break;
                case 3: sim.Enqueue(Command.Stop(owner, h)); break;
                case 4:
                    // A gather order to a random gold node (or a stop when none).
                    ResourceStore r = w.Resources;
                    for (int n = 0; n < r.Capacity; n++)
                        if (r.Alive[n] && rng.NextInt(0, 2) == 0)
                        {
                            int width = w.NavGrid.Width, cell = r.Cell[n];
                            sim.Enqueue(Command.Gather(owner, h, new Vector2(cell % width + 0.5f, cell / width + 0.5f) * Map.MapConstants.CellSize));
                            break;
                        }
                    break;
                default: u.Hp[i0] = 1; break; // the next hit kills it: more deaths, more restarts
            }
            return;
        }
        if (builds.Count == 0) return;
        int k0 = builds[rng.NextInt(0, builds.Count)];
        int p = b.Owner[k0];
        var at = DrawScenes.Centre(sim, new EntityHandle(k0, b.Generation[k0]));
        switch (kind)
        {
            case 6:
            case 7:
                var trains = w.Data.UnitsTrainedAt(b.TypeId[k0]);
                if (trains.Length > 0) sim.Enqueue(Command.Train(p, at, trains[rng.NextInt(0, trains.Length)]));
                break;
            case 8: sim.Enqueue(Command.CancelTrain(p, at, rng.NextInt(0, 3))); break;
            case 9: sim.Enqueue(Command.Cancel(p, at)); break; // a site's refund (dropped for a finished building)
            default:
                BuildingSlot slot = w.Data.Buildings[b.TypeId[k0]].Slot;
                for (int t = 0; t < w.Data.Techs.Length; t++)
                    if (w.Data.Techs[t].ResearchedAtSlot == slot && (w.Data.Techs[t].Faction < 0 || w.Data.Techs[t].Faction == w.Data.Buildings[b.TypeId[k0]].Faction))
                    {
                        sim.Enqueue(Command.Research(p, at, t));
                        break;
                    }
                break;
        }
    }

    /// <summary>The counts the per-tick check reads against a scan of the stores; a failure text or null.</summary>
    internal static string? CountersAgree(World w)
    {
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        int n = w.Config.PlayerCount;
        var workers = new int[n];
        var queued = new int[n];
        var carried = new long[n];
        var carriedWood = new long[n];
        var training = new int[n];
        var dropOffs = new int[n];
        int types = w.Data.Units.Length;
        var waiting = new int[n * types];
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            int o = u.Owner[i];
            if ((uint)o >= (uint)n) continue;
            if (w.Data.Units[u.TypeId[i]].IsWorker) workers[o]++;
            if (u.CargoKind[i] == ResourceKind.Gold) carried[o] += u.Cargo[i];
            else carriedWood[o] += u.Cargo[i];
        }
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || (uint)b.Owner[k] >= (uint)n) continue;
            if (!b.UnderConstruction[k] && w.Data.Buildings[b.TypeId[k]].DropOff) dropOffs[b.Owner[k]]++;
            for (int q = 0; q < b.QueueCount[k]; q++)
            {
                if (b.QueueIsTechAt(k, q)) continue;
                queued[b.Owner[k]]++;
                if (q == 0 && b.Progress[k] > 0) training[b.Owner[k]]++;
                else waiting[b.Owner[k] * types + b.QueueTypeAt(k, q)]++;
            }
        }
        long goldLeft = 0, woodLeft = 0;
        for (int r = 0; r < w.Resources.Capacity; r++)
        {
            if (!w.Resources.Alive[r]) continue;
            if (w.Data.Resources[w.Resources.TypeId[r]].Resource == ResourceKind.Gold) goldLeft += w.Resources.Remaining[r];
            else woodLeft += w.Resources.Remaining[r];
        }
        for (int p = 0; p < n; p++)
        {
            if (workers[p] != w.Ledger.LiveWorkers[p]) return $"p{p} workers counted {w.Ledger.LiveWorkers[p]}, scanned {workers[p]}";
            if (queued[p] != w.Ledger.QueuedUnits[p]) return $"p{p} queued counted {w.Ledger.QueuedUnits[p]}, scanned {queued[p]}";
            if (carried[p] != w.Ledger.CarriedGold[p]) return $"p{p} carried counted {w.Ledger.CarriedGold[p]}, scanned {carried[p]}";
            // M6-SH3: the counts the exact rule added.
            if (carriedWood[p] != w.Ledger.CarriedWood[p]) return $"p{p} carried wood counted {w.Ledger.CarriedWood[p]}, scanned {carriedWood[p]}";
            if (training[p] != w.Ledger.TrainingUnits[p]) return $"p{p} training counted {w.Ledger.TrainingUnits[p]}, scanned {training[p]}";
            if (dropOffs[p] != w.Ledger.FinishedDropOffs[p]) return $"p{p} finished drop-offs counted {w.Ledger.FinishedDropOffs[p]}, scanned {dropOffs[p]}";
            for (int t = 0; t < types; t++)
                if (waiting[p * types + t] != w.Ledger.WaitingUnits(p, t)) return $"p{p} waiting of type {t} counted {w.Ledger.WaitingUnits(p, t)}, scanned {waiting[p * types + t]}";
        }
        if (goldLeft != w.Resources.GoldLeft) return $"gold left counted {w.Resources.GoldLeft}, scanned {goldLeft}";
        if (woodLeft != w.Resources.WoodLeft) return $"wood left counted {w.Resources.WoodLeft}, scanned {woodLeft}";
        return null;
    }

    internal readonly record struct FuzzResult(bool Drawn, bool Won, int Restarts, int Ticks);

    /// <summary>Plays one fuzzed scene to <paramref name="maxTicks"/> (or 60 ticks past its end), checking the oracle, the counters and the draw tick every tick.</summary>
    internal static FuzzResult Fuzz(ulong seed, int maxTicks)
    {
        Simulation sim = Scene(seed, DrawScenes.Short, out SimRng rng);
        World w = sim.World;
        var oracle = new DrawQaOracle(sim);
        int overAt = -1;
        int players = w.Config.PlayerCount;
        var defeatedBefore = new bool[players];
        while (sim.TickNumber < maxTicks && (overAt < 0 || sim.TickNumber < overAt + 60))
        {
            if (rng.NextInt(0, 3) == 0) Disturb(sim, ref rng);
            for (int p = 0; p < players; p++) defeatedBefore[p] = w.IsDefeated(p);
            bool drawnBefore = w.MatchDrawn;
            oracle.BeforeTick();
            sim.Tick();
            string? fail = oracle.AfterTick() ?? CountersAgree(w);
            if (fail != null) Assert.Fail($"seed {seed}: {fail}");
            if (w.MatchDrawn && !drawnBefore)
            {
                for (int p = 0; p < players; p++) Assert.True(defeatedBefore[p] == w.IsDefeated(p), $"seed {seed}: p{p}'s defeated flag changed on the draw tick");
                Assert.True(w.DefeatEvents.Length == 0, $"seed {seed}: a defeat event on the draw tick");
            }
            if (drawnBefore) Assert.True(w.MatchDrawn && w.MatchOver, $"seed {seed}: the draw was taken back at tick {sim.TickNumber}");
            if (sim.MatchOver && overAt < 0) overAt = sim.TickNumber;
        }
        return new FuzzResult(w.MatchDrawn, sim.MatchOver && !w.MatchDrawn, oracle.RestartsOnDeath + oracle.RestartsOnDefeat + oracle.RestartsUnspent, sim.TickNumber);
    }

    public static TheoryData<int> Batches()
    {
        var rows = new TheoryData<int>();
        for (int b = 0; b < 8; b++) rows.Add(b);
        return rows;
    }

    /// <summary>200 seeds (8 x 25) of the oracle fuzz with cancels, trains, gathers, research and kills, 1,500 ticks each.</summary>
    [Theory]
    [MemberData(nameof(Batches))]
    public void TheClockAndTheAnswer_MatchAnIndependentOracle_EveryTick_UnderRandomOrders(int batch)
    {
        int drawn = 0, won = 0, open = 0, restarts = 0;
        for (int k = 0; k < 25; k++)
        {
            FuzzResult r = Fuzz((ulong)(batch * 25 + k + 1), 1500);
            if (r.Drawn) drawn++;
            else if (r.Won) won++;
            else open++;
            restarts += r.Restarts;
        }
        _out.WriteLine($"batch {batch}: drawn {drawn}, won {won}, still open {open}, clock restarts {restarts}");
        Assert.True(drawn >= 5, $"only {drawn} of 25 fuzzed scenes drew: the fuzz is not reaching the rule");
    }

    /// <summary>
    /// The counters equal a scan every tick of a four-seat AI match with Teblor seats (war-dog packs, gathering, cancels by the
    /// AI), through a save and load every 3,000 ticks (the load's recount equals the counters it replaces).
    /// </summary>
    [Fact]
    public void TheCounters_EqualAScan_EveryTick_FourSeatsWithTeblor_ThroughSavesAndLoads()
    {
        int teblor = TestSim.Data.FindFaction("teblor");
        PlayerSeat[] seats =
        {
            new(teblor, SeatController.Ai, AiDifficulty.Hard), new(0, SeatController.Ai, AiDifficulty.Normal),
            new(teblor, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Hard),
        };
        Simulation sim = AiScenes.Generated(seats, 7, false, out _, units: 1024);
        int loads = 0;
        long carriedSeen = 0, queuedSeen = 0;
        for (int t = 0; t < 18_000 && !sim.MatchOver; t++)
        {
            sim.Tick();
            string? fail = CountersAgree(sim.World);
            if (fail != null) Assert.Fail($"tick {sim.TickNumber}: {fail}");
            for (int p = 0; p < 4; p++)
            {
                carriedSeen += sim.World.Ledger.CarriedGold[p];
                queuedSeen += sim.World.Ledger.QueuedUnits[p];
            }
            if (sim.TickNumber % 3000 == 0)
            {
                ulong h = sim.StateHash();
                sim = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(sim));
                Assert.Equal(h, sim.StateHash());
                fail = CountersAgree(sim.World);
                if (fail != null) Assert.Fail($"after the load at {sim.TickNumber}: {fail}");
                loads++;
            }
        }
        _out.WriteLine($"to tick {sim.TickNumber}, {loads} loads, over {sim.MatchOver}");
        Assert.True(carriedSeen > 0 && queuedSeen > 0);
    }

    // ---------------- the edges ----------------

    private static void Run(Simulation sim, DrawQaOracle o, int ticks)
    {
        for (int t = 0; t < ticks; t++)
        {
            o.BeforeTick();
            sim.Tick();
            string? fail = o.AfterTick() ?? CountersAgree(sim.World);
            if (fail != null) Assert.Fail(fail);
        }
    }

    /// <summary>A site cancelled while the match is spent refunds its gold: not spent on that tick, the clock back to 0.</summary>
    [Fact]
    public void ASiteCancelRefund_MakesTheMatchNotSpent_OnThatTick()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short);
        World w = sim.World;
        // The faction's building with the highest gold cost, as a fresh site (work 0: the whole cost comes back).
        int faction = w.FactionOf(0), type = -1;
        for (int t = 0; t < w.Data.Buildings.Length; t++)
            if (w.Data.Buildings[t].Faction == faction && (type < 0 || w.Data.Buildings[t].CostGold > w.Data.Buildings[type].CostGold)) type = t;
        Assert.True(w.Data.Buildings[type].CostGold >= DrawScenes.Cheapest(sim, 0));
        Assert.True(w.Buildings.Spawn(0, type, 24 * w.NavGrid.Width + 24, out EntityHandle site, true));
        var o = new DrawQaOracle(sim);
        Run(sim, o, 30);
        Assert.Equal(30, w.DrawClock);
        sim.Enqueue(Command.Cancel(0, DrawScenes.Centre(sim, site)));
        Run(sim, o, 2);
        Assert.False(w.Buildings.IsAlive(site));
        Assert.True(w.Gold[0] >= DrawScenes.Cheapest(sim, 0));
        Assert.Equal(0, w.DrawClock);
        Assert.False(w.MatchSpent);
    }

    /// <summary>
    /// 2v2: one side spent, the other side's one seat with gold keeps the match un-spent; its bank drained, spent; that seat's
    /// defeat restarts the clock and the match is still spent (its ally stands), so it draws; nobody else defeated.
    /// </summary>
    [Fact]
    public void TwoVersusTwo_OneSeatWithGold_ThenItsDefeat_ThenADraw()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short, players: 4, soldiers: 1, teams: new[] { 1, 2, 1, 2 });
        World w = sim.World;
        // Seat 3: its soldier gone, a bank at the cheapest unit.
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 3) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        w.Ledger.Gold[3] = DrawScenes.Cheapest(sim, 3);
        w.Ledger.Wood[3] = Draw.DrawReachTests.CheapestByGold(w, 3).CostWood; // M6-SH3: wood is counted
        var o = new DrawQaOracle(sim);
        Run(sim, o, 50);
        Assert.Equal(0, w.DrawClock);
        Assert.False(w.MatchSpent);
        w.Ledger.Gold[3] = 0;
        w.Ledger.Wood[3] = 0;
        Run(sim, o, 20);
        Assert.Equal(20, w.DrawClock);
        int hall3 = -1;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 3) hall3 = k;
        Assert.True(w.Buildings.Free(new EntityHandle(hall3, w.Buildings.Generation[hall3])));
        Run(sim, o, 1);
        Assert.True(w.IsDefeated(3));
        Assert.Equal(0, w.DrawClock);
        Assert.False(sim.MatchOver);
        Run(sim, o, DrawScenes.ShortTicks);
        Assert.True(w.MatchDrawn);
        Assert.True(sim.MatchOver);
        Assert.False(w.IsDefeated(0) || w.IsDefeated(1) || w.IsDefeated(2));
    }

    /// <summary>Three seats; at clock N - 1 one seat falls (no unit dies): no draw on that tick, the clock restarts, the draw comes N ticks later.</summary>
    [Fact]
    public void ADefeatAtClockNMinus1_IsNotADraw_TheDrawComesAFullWaitLater()
    {
        Simulation sim = DrawScenes.Empty(DrawScenes.Short, 3);
        World w = sim.World;
        for (int p = 0; p < 2; p++)
        {
            (int x, int y) = DrawScenes.Corner(sim, p);
            DrawScenes.Building(sim, p, x, y);
            DrawScenes.Unit(sim, p, x, y + 5);
        }
        (int x2, int y2) = DrawScenes.Corner(sim, 2);
        EntityHandle hall2 = DrawScenes.Building(sim, 2, x2, y2);
        DrawScenes.Broke(sim);
        var o = new DrawQaOracle(sim);
        int n = DrawScenes.ShortTicks;
        Run(sim, o, n - 1);
        Assert.Equal(n - 1, w.DrawClock);
        Assert.True(w.Buildings.Free(hall2));
        Run(sim, o, 1);
        Assert.True(w.IsDefeated(2));
        Assert.False(w.MatchDrawn);
        Assert.Equal(0, w.DrawClock);
        Run(sim, o, n - 1);
        Assert.False(sim.MatchOver);
        Run(sim, o, 1);
        Assert.True(w.MatchDrawn);
    }

    /// <summary>
    /// After a draw: a seat that never built places its first building and trains (the latch holds), and a seat that did build
    /// loses everything (MatchOver and the draw stay; Q4 of the developer: the draw stands).
    /// </summary>
    [Fact]
    public void AfterTheDraw_FirstBuildingsAndALaterFall_DoNotTakeItBack()
    {
        Simulation sim = DrawScenes.Empty(DrawScenes.Short, 3);
        World w = sim.World;
        for (int p = 0; p < 2; p++)
        {
            (int x, int y) = DrawScenes.Corner(sim, p);
            DrawScenes.Building(sim, p, x, y);
            DrawScenes.Unit(sim, p, x, y + 5);
        }
        DrawScenes.Broke(sim);
        DrawScenes.RunToEnd(sim, 3 * DrawScenes.ShortTicks);
        Assert.True(w.MatchDrawn);
        (int x2, int y2) = DrawScenes.Corner(sim, 2);
        EntityHandle hall2 = DrawScenes.Building(sim, 2, x2, y2, BuildingSlot.TownHall);
        w.Ledger.Gold[2] = 10_000;
        sim.Enqueue(Command.Train(2, DrawScenes.Centre(sim, hall2), w.Data.UnitsTrainedAt(w.Buildings.TypeId[hall2.Index])[0]));
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 1) w.Buildings.Free(new EntityHandle(k, w.Buildings.Generation[k]));
        int clock = w.DrawClock;
        for (int t = 0; t < 400; t++) sim.Tick();
        Assert.True(w.IsDefeated(1), "seat 1 should fall after the draw (the defeat rule still runs)");
        Assert.True(w.MatchDrawn && sim.MatchOver);
        Assert.Equal(clock, w.DrawClock);
        Assert.Equal(-1, w.DrawTicksLeft);
        Assert.True(w.LiveUnits(2) > 0, "seat 2 trained nothing after the draw");
    }

    /// <summary>The last gold of a node is taken by a worker that then dies carrying it: the gold is gone from every term on that tick.</summary>
    [Fact]
    public void TheLastGoldOfANode_DiesWithItsCarrier()
    {
        Simulation sim = DrawScenes.Empty(DrawScenes.Short, 2);
        World w = sim.World;
        EntityHandle hall0 = DrawScenes.Building(sim, 0, 8, 8, BuildingSlot.TownHall);
        DrawScenes.Building(sim, 1, 40, 40);
        DrawScenes.Unit(sim, 1, 40, 45);
        DrawScenes.Broke(sim);
        int cheapest = DrawScenes.Cheapest(sim, 0);
        w.Ledger.Gold[0] = cheapest - 2;
        w.Ledger.Wood[0] = Draw.DrawReachTests.CheapestByGold(w, 0).CostWood; // M6-SH3: wood is counted (the Town Hall is the drop-off)
        EntityHandle node = DrawScenes.GoldNode(sim, 20, 9, 2);
        EntityHandle worker = DrawScenes.Unit(sim, 0, 18, 10, UnitSlot.Worker);
        sim.Enqueue(Command.Gather(0, worker, new Vector2(21f, 10f) * Map.MapConstants.CellSize));
        var o = new DrawQaOracle(sim);
        int t = 0;
        while (w.Resources.IsAlive(node) && t++ < 400) Run(sim, o, 1);
        Assert.False(w.Resources.IsAlive(node), "the node was never emptied");
        Assert.Equal(2, w.Ledger.CarriedGold[0]);
        Assert.True(w.CanStillTrain(0), "bank + carried = the cheapest unit");
        Assert.False(w.MatchSpent);
        // It dies before it delivers.
        w.Units.Hp[worker.Index] = 1;
        EntityHandle killer = DrawScenes.Unit(sim, 1, 0, 0);
        w.Units.Position[killer.Index] = w.Units.Position[worker.Index] + new Vector2(0.9f, 0f);
        CombatScenes.Spot(sim, 1, worker);
        sim.Enqueue(Command.Attack(1, killer, worker, isBuilding: false));
        sim.Enqueue(Command.Stop(0, worker));
        t = 0;
        while (w.Units.IsAlive(worker) && t++ < 200) Run(sim, o, 1);
        Assert.False(w.Units.IsAlive(worker));
        Assert.Equal(0, w.Ledger.CarriedGold[0]);
        Assert.False(w.CanStillTrain(0));
        Run(sim, o, 3);
        Assert.True(w.MatchSpent);
        Assert.True(w.DrawClock > 0);
        Assert.True(w.Buildings.IsAlive(hall0));
    }

    // ---------------- saves ----------------

    /// <summary>On the shipped data (the 300 s wait): saves at every 500th tick of the clock each load and draw on the same tick with the same hash.</summary>
    [Fact]
    public void SavesAtEvery500thTickOfTheClock_OnTheShippedWait_DrawOnTheSameTick()
    {
        Simulation sim = DrawScenes.Spent(TestSim.Data);
        int wait = sim.World.DrawWaitTicks;
        Assert.Equal(6000, wait);
        var saves = new List<byte[]>();
        while (!sim.MatchOver)
        {
            sim.Tick();
            if (sim.World.DrawClock > 0 && sim.World.DrawClock % 500 == 0 && !sim.MatchOver) saves.Add(SaveTestRun.Save(sim));
            Assert.True(sim.TickNumber < 2 * wait);
        }
        Assert.True(sim.World.MatchDrawn);
        int drawTick = sim.TickNumber;
        ulong drawHash = sim.StateHash();
        Assert.Equal(wait / 500 - 1, saves.Count);
        foreach (byte[] bytes in saves)
        {
            Simulation l = SaveTestRun.Load(TestSim.Data, bytes);
            while (!l.MatchOver) l.Tick();
            Assert.True(l.World.MatchDrawn);
            Assert.Equal(drawTick, l.TickNumber);
            Assert.Equal(drawHash, l.StateHash());
        }
        _out.WriteLine($"drawn at {drawTick}; {saves.Count} saves each drew on the same tick");
    }

    /// <summary>An AI match that draws (the starved map, a 60 s wait): a save every 100th tick of the clock; each load draws on the same tick with the same hash.</summary>
    [Fact]
    public void AnAiMatchThatDraws_SavedEvery100thClockTick_DrawsOnTheSameTick()
    {
        GameData data = DrawScenes.DataWith(60, DrawScenes.StarveTheMap);
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Hard) };
        Simulation sim = AiScenes.Generated(seats, 2, false, out _, data: data);
        var saves = new List<byte[]>();
        while (!sim.MatchOver && sim.TickNumber < 20_000)
        {
            sim.Tick();
            if (sim.World.DrawClock > 0 && sim.World.DrawClock % 100 == 0 && !sim.MatchOver) saves.Add(SaveTestRun.Save(sim));
        }
        Assert.True(sim.World.MatchDrawn, $"not drawn by {sim.TickNumber}");
        Assert.NotEmpty(saves);
        foreach (byte[] bytes in saves)
        {
            Simulation l = SaveTestRun.Load(data, bytes);
            while (!l.MatchOver && l.TickNumber < sim.TickNumber + 10) l.Tick();
            Assert.True(l.World.MatchDrawn);
            Assert.Equal(sim.TickNumber, l.TickNumber);
            Assert.Equal(sim.StateHash(), l.StateHash());
        }
        _out.WriteLine($"drawn at {sim.TickNumber}; {saves.Count} saves");
    }

    /// <summary>
    /// A forged DRAW part the developer's refusal list does not hold: a clock running on a match already won (the result flag
    /// set, not drawn). The sim never writes it (a won tick sets the clock to 0), so a strict reader refuses it. Red on the
    /// M6-S13 head (loads with SaveError.None): BUG-1730; refused since M6-SH3 (World.ReadDraw).
    /// </summary>
    [Fact]
    public void AForgedClock_OnAWonMatch_IsRefused()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short);
        for (int t = 0; t < 10; t++) sim.Tick();
        byte[] bytes = SaveTestRun.Save(sim);
        int at = DrawSaveReplayTests.DrawAt(bytes);
        Assert.True(at > 0);
        Assert.Equal(0, bytes[at - 1]); // the result flag just before the part
        bytes[at - 1] = 1;
        SaveError e = SaveTestRun.TryLoad(DrawScenes.Short, SaveBytes.Rechecksum(bytes));
        _out.WriteLine($"clock {sim.World.DrawClock} on a won match: {e}");
        Assert.Equal(SaveError.Malformed, e);
    }
}
