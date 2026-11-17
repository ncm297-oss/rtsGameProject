using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-S12 (QA focus "full"), the long rows: the blind re-scout as a drain (an enemy camped round the base kills every
/// scout: workers lost in 20 minutes); a save / load taken mid-sweep of a re-scout; and an 8-seed fuzz of AI matches on
/// generated maps with seeded interference (the scout killed, a drop-off razed, a loaded worker killed) checking resource
/// conservation every tick (nodes + cargo + delivered never grows, and falls only by the cargo of units that died or let a
/// load go that tick: a Gather of the other kind drops the load, BUG-0594's rule),
/// the <see cref="CriterionMatch"/> checks (fog oracle, census, entities, refusal streaks), no negative stock, and every AI's
/// rally point finite and on the map.
/// </summary>
[Trait("Category", "Stress")]
public class M6S12AiRulesQaStressTests(ITestOutputHelper output)
{
    /// <summary>
    /// The drain: the blind scene (no node of either kind known, so every worker is idle), each scout killed the tick it is
    /// sent (an enemy camped at the door, as <c>ReachRuleQaTests.EveryScoutKilledAtOnce_*</c> stages it), for 20 minutes
    /// (24,000 ticks): re-scouts sent, workers lost, workers trained, gold left, printed. Asserted: at most one re-scout per
    /// <c>ai.scoutAfter</c> window. Whether the loss itself is bounded is reported (BUG-1557 if it is not).
    /// </summary>
    [Fact]
    public void EveryScoutKilledAtOnce_TheBlindSeatsDrain_20Minutes_Report()
    {
        Simulation sim = M6S12AiRulesQaTests.BlindScene(keepMines: false);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        int window = Math.Max(ai.Rules.ScoutAfterTicks, ai.ThinkTicks);
        int rescouts = 0, killed = 0, gold0 = w.Gold[0], firstNoWorker = -1;
        int trained0 = w.UnitsTrained[0], losses0 = w.UnitLosses[0];
        byte phase = ai.Board.ScoutPhase;
        var perWindow = new Dictionary<int, int>();
        var at5Min = "";
        while (sim.TickNumber < 24_000 && !sim.MatchOver)
        {
            sim.Tick();
            Blackboard b = ai.Board;
            if (phase == 2 && b.ScoutPhase == 1)
            {
                rescouts++;
                int win = sim.TickNumber / window;
                perWindow[win] = perWindow.TryGetValue(win, out int c) ? c + 1 : 1;
            }
            if (b.ScoutPhase == 1 && b.ScoutSlot >= 0 && w.Units.IsAlive(new EntityHandle(b.ScoutSlot, b.ScoutGeneration)))
            {
                CombatSystem.HitUnit(w, new PendingHit(default, 1, new EntityHandle(b.ScoutSlot, b.ScoutGeneration), false, w.Units.Hp[b.ScoutSlot]));
                killed++;
            }
            phase = b.ScoutPhase;
            int workers = Workers(w);
            if (workers == 0 && firstNoWorker < 0) firstNoWorker = sim.TickNumber;
            if (sim.TickNumber == 6000) at5Min = $"at 5 min: killed {killed}, workers {workers}, gold {w.Gold[0]}";
        }
        int most = perWindow.Count == 0 ? 0 : perWindow.Values.Max();
        output.WriteLine($"20 min: re-scouts {rescouts}, scouts killed {killed}, own units lost {w.UnitLosses[0] - losses0}, trained {w.UnitsTrained[0] - trained0}, "
            + $"workers alive {Workers(w)} (none from tick {firstNoWorker}), gold {gold0} -> {w.Gold[0]}, most re-scouts in one window {most}; {at5Min}");
        Assert.True(most <= 1, $"{most} re-scouts in one ai.scoutAfter window");
    }

    private static int Workers(World w)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Data.Units[w.Units.TypeId[i]].IsWorker) n++;
        return n;
    }

    /// <summary>
    /// QA focus "save / load mid-sweep": the blind scene's re-scout, saved once it has visited two waypoints and is walking to
    /// a third; the load is byte-identical, keeps the sweep (phase, slot, points, visited marks) and follows the run hash for
    /// hash for 3,000 ticks.
    /// </summary>
    [Fact]
    public void AReScout_SavedMidSweep_LoadsAndFollowsTheRun()
    {
        Simulation a = M6S12AiRulesQaTests.BlindScene(keepMines: false);
        GameData data = a.World.Data;
        AiPlayer ai = a.AiOf(0)!;
        Simulation? loaded = null;
        int savedAt = -1;
        while (a.TickNumber < 20_000 && (savedAt < 0 || a.TickNumber < savedAt + 3000))
        {
            a.Tick();
            if (loaded != null)
            {
                loaded.Tick();
                Assert.True(a.StateHash() == loaded.StateHash(), $"the loaded save diverged at tick {a.TickNumber} (saved at {savedAt})");
                continue;
            }
            Blackboard b = ai.Board;
            int visited = 0;
            for (int k = 0; k < Blackboard.ScoutHead; k++) if (b.ScoutVisited[k]) visited++;
            if (b.ScoutPhase != 1 || b.ScoutTarget < 0 || a.World.Units.State[b.ScoutSlot] != UnitState.Moving || visited < 2) continue;
            // At least two head points visited by walking (more than were explored when it was laid).
            savedAt = a.TickNumber;
            byte[] bytes = SaveTestRun.Save(a);
            loaded = SaveTestRun.Load(data, bytes);
            Assert.Equal(bytes, SaveTestRun.Save(loaded));
            SaveTestRun.AssertSameState(a, loaded, "at the mid-sweep save");
            Blackboard lb = loaded.AiOf(0)!.Board;
            Assert.Equal(b.ScoutPhase, lb.ScoutPhase);
            Assert.Equal(b.ScoutSlot, lb.ScoutSlot);
            Assert.Equal(b.ScoutTarget, lb.ScoutTarget);
            Assert.Equal(b.ScoutPoints, lb.ScoutPoints);
            Assert.Equal(b.ScoutVisited, lb.ScoutVisited);
            output.WriteLine($"saved at tick {savedAt}: scout {b.ScoutSlot} to point {b.ScoutTarget}, {visited} head points visited");
        }
        Assert.True(savedAt >= 0, "the re-scout never got two head points in: the row proves nothing");
    }

    /// <summary>
    /// Determinism of the rally search across its shift and its fall-back (QA's survey: a generated four-seat map, seed 4,
    /// player 0's point moves out of a mouth at tick 1,560 to 6 m from its hall and back to the nominal point, on a building, at
    /// 2,920): twins hash alike on every tick to 6,000, and a save taken at every change of any seat's rally point loads
    /// byte-identical and follows the run hash for hash for 300 ticks.
    /// </summary>
    [Fact]
    public void Generated4Seed4_RallyShiftAndFallBack_TwinsAndSaves()
    {
        Simulation a = CriterionMatch.Build("generated", 4, 4), b = CriterionMatch.Build("generated", 4, 4);
        GameData data = a.World.Data;
        int n = a.World.Config.PlayerCount;
        var version = new int[n];
        var followers = new List<(Simulation Sim, int Until)>();
        int saves = 0;
        while (a.TickNumber < 6000)
        {
            a.Tick();
            b.Tick();
            ulong h = a.StateHash();
            Assert.True(h == b.StateHash(), $"twins differ after tick {a.TickNumber}");
            for (int k = followers.Count - 1; k >= 0; k--)
            {
                followers[k].Sim.Tick();
                Assert.True(h == followers[k].Sim.StateHash(), $"a save loaded at a rally change diverged at tick {a.TickNumber}");
                if (a.TickNumber >= followers[k].Until) followers.RemoveAt(k);
            }
            for (int p = 0; p < n; p++)
            {
                int v = a.AiOf(p)!.Board.RallyVersion;
                if (v == version[p]) continue;
                version[p] = v;
                byte[] bytes = SaveTestRun.Save(a);
                Simulation loaded = SaveTestRun.Load(data, bytes);
                Assert.Equal(bytes, SaveTestRun.Save(loaded));
                Assert.Equal(a.AiOf(p)!.Board.Rally, loaded.AiOf(p)!.Board.Rally);
                followers.Add((loaded, a.TickNumber + 300));
                saves++;
            }
        }
        output.WriteLine($"twins alike to 6,000; {saves} saves at rally changes, each followed 300 ticks");
        Assert.True(saves >= 4, "too few rally changes: the row proves little");
    }

    /// <summary>The fuzz rows: generated maps, 2 Normal seats, seeds 1-8, 12,000 ticks.</summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    [InlineData(7UL)]
    [InlineData(8UL)]
    public void FuzzWithInterference_ConservationAndChecks(ulong seed)
    {
        Simulation sim = CriterionMatch.Build("generated", seed);
        World w = sim.World;
        UnitStore u = w.Units;
        var rng = new Random((int)seed * 7919);
        int n = w.Config.PlayerCount;
        long last = Total(w);
        var prevAlive = new bool[u.Capacity];
        var prevGen = new int[u.Capacity];
        var prevCarry = new int[u.Capacity];
        var prevCargo = new int[u.Capacity];
        int kills = 0, razes = 0, scoutKills = 0, lossTicks = 0;
        long lost = 0;
        string? bad = null;
        float width = w.NavGrid.Width * MapConstants.CellSize, height = w.NavGrid.Height * MapConstants.CellSize;
        CriterionMatch.Result r = CriterionMatch.Play(sim, 12_000, checks: true, onTick: s =>
        {
            int now = s.TickNumber;
            // Conservation over the tick just played.
            long total = Total(w);
            long allowance = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!prevAlive[i]) continue;
                if (!u.Alive[i] || u.Generation[i] != prevGen[i]) allowance += prevCarry[i];
                else if (u.Cargo[i] < prevCargo[i])
                {
                    // A load that left the unit: delivered (counted in the stats) or dropped by an order of the other kind
                    // (BUG-0594's rule, the rebalance's Gather): at most what it held plus one tick's gathering.
                    allowance += prevCargo[i] - u.Cargo[i] + w.Data.CarryOf(u.TypeId[i]);
                }
            }
            if (total > last) bad ??= $"tick {now}: nodes + cargo + delivered grew {last} -> {total}";
            else if (last - total > allowance) bad ??= $"tick {now}: {last - total} gold/wood vanished, only {allowance} could die with units";
            if (total < last) { lossTicks++; lost += last - total; }
            for (int p = 0; p < n; p++)
            {
                if (w.Gold[p] < 0 || w.Wood[p] < 0) bad ??= $"tick {now}: player {p} stock {w.Gold[p]} / {w.Wood[p]}";
                AiPlayer? ai = s.AiOf(p);
                if (ai == null || ai.Board.RallyVersion == 0) continue;
                Vector2 rp = ai.Board.Rally;
                if (!float.IsFinite(rp.X) || !float.IsFinite(rp.Y) || rp.X < 0 || rp.Y < 0 || rp.X > width || rp.Y > height) bad ??= $"tick {now}: player {p}'s rally point {rp}";
                int ss = ai.Board.ScoutSlot;
                if (ai.Board.ScoutPhase == 1 && (ss < 0 || ss >= u.Capacity)) bad ??= $"tick {now}: player {p} scout slot {ss} in phase 1";
            }
            // Interference, seeded: every 300 ticks one of: kill a seat's scout, raze a non-hall drop-off, kill a loaded worker.
            if (now % 300 == 0 && now > 600)
            {
                int what = rng.Next(3), p = rng.Next(n);
                AiPlayer? ai = s.AiOf(p);
                if (what == 0 && ai != null && ai.Board.ScoutPhase == 1 && u.IsAlive(new EntityHandle(ai.Board.ScoutSlot, ai.Board.ScoutGeneration)))
                {
                    CombatSystem.HitUnit(w, new PendingHit(default, (p + 1) % n, new EntityHandle(ai.Board.ScoutSlot, ai.Board.ScoutGeneration), false, u.Hp[ai.Board.ScoutSlot]));
                    scoutKills++;
                }
                else if (what == 1)
                {
                    BuildingStore b = w.Buildings;
                    int hallType = StartBase.BuildingOfSlot(w.Data, w.FactionOf(p), BuildingSlot.TownHall);
                    for (int k = 0; k < b.Capacity; k++)
                    {
                        if (!b.Alive[k] || b.Owner[k] != p || b.TypeId[k] == hallType || !w.Data.Buildings[b.TypeId[k]].DropOff) continue;
                        CombatSystem.HitBuilding(w, new PendingHit(default, (p + 1) % n, b.HandleOf(k), true, b.Hp[k] + 1));
                        razes++;
                        break;
                    }
                }
                else
                {
                    int start = rng.Next(u.Capacity);
                    for (int j = 0; j < u.Capacity; j++)
                    {
                        int i = (start + j) % u.Capacity;
                        if (!u.Alive[i] || u.Owner[i] != p || u.Cargo[i] == 0) continue;
                        CombatSystem.HitUnit(w, new PendingHit(default, (p + 1) % n, new EntityHandle(i, u.Generation[i]), false, u.Hp[i]));
                        kills++;
                        break;
                    }
                }
            }
            // A kill between ticks takes its cargo with it: the next tick's allowance counts it.
            for (int i = 0; i < u.Capacity; i++)
            {
                prevAlive[i] = u.Alive[i];
                prevGen[i] = u.Generation[i];
                prevCarry[i] = u.Alive[i] ? u.Cargo[i] + w.Data.CarryOf(u.TypeId[i]) : 0;
                prevCargo[i] = u.Alive[i] ? u.Cargo[i] : 0;
            }
            last = Total(w); // after the interference
        });
        output.WriteLine($"seed {seed}: to {r.EndTick} (over {r.Over}, winner {r.Winner}), thinks {r.Thinks}, oracle {r.OracleChecks}, refusals {r.RefusalText}, longest streak {r.LongestStreak}; "
            + $"interference: scouts killed {scoutKills}, drop-offs razed {razes}, loaded workers killed {kills}; conserved total {last}, lost with dead units {lost} over {lossTicks} ticks");
        Assert.Null(r.Error);
        Assert.Null(bad);
    }

    /// <summary>Gold and wood in nodes, in live units' cargo, and delivered (the stats), over every player.</summary>
    private static long Total(World w)
    {
        (long gold, long wood) = GatherMaps.Conserved(w);
        long delivered = 0;
        for (int p = 0; p < w.Config.PlayerCount; p++) delivered += w.GatheredGold[p] + w.GatheredWood[p];
        // Conserved counts the stock too; stock moves with spending, so take it back out.
        long stock = 0;
        for (int p = 0; p < w.Config.PlayerCount; p++) stock += w.Gold[p] + w.Wood[p];
        return gold + wood - stock + delivered;
    }
}
