using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-S7 (BUG-1047 / 1048's rule, session 2026-11-05-1716): the gather reach lifts once the scout's head ring has been
/// seen, a scout lost before that is replaced. Adversarial starts: the nearest forest cleared to 80 / 100 / 120 m (beyond
/// BUG-1047's 66 m rows), the scout killed at its first tick out, 200 and 2,000 ticks in on such a start, every scout
/// killed the moment it is sent (how many workers the replacement rule can cost a seat), and a save / load taken while a
/// replacement is pending. First-wood ticks and the farthest wood a gatherer is sent to are printed beside the
/// <c>ai.gatherReach</c> 512 control (every node in reach).
/// </summary>
[Collection(SerialCollection.Name)]
public class ReachRuleQaTests
{
    private readonly ITestOutputHelper _out;

    public ReachRuleQaTests(ITestOutputHelper output) => _out = output;

    private static GameData Reach512 => s_reach512.Value;

    private static readonly Lazy<GameData> s_reach512 = new(() => AiScenes.DataWith(dir => dir.EditJson("common/rules.json", r => r["ai"]!["gatherReach"] = 512)));

    private static Vector2 HallCentre(World w, int player)
    {
        for (int k = 0; k < w.Buildings.Capacity; k++)
        {
            if (!w.Buildings.Alive[k] || w.Buildings.Owner[k] != player) continue;
            BuildingDef d = w.Data.Buildings[w.Buildings.TypeId[k]];
            int c = w.Buildings.Cell[k], width = w.NavGrid.Width;
            return new Vector2((c % width + d.FootprintWidth * 0.5f) * Map.MapConstants.CellSize, (c / width + d.FootprintHeight * 0.5f) * Map.MapConstants.CellSize);
        }
        return new Vector2(float.NaN);
    }

    private static Vector2 NodeCentre(World w, int k)
    {
        ResourceDef d = w.Data.Resources[w.Resources.TypeId[k]];
        int c = w.Resources.Cell[k], width = w.NavGrid.Width;
        return new Vector2((c % width + d.FootprintWidth * 0.5f) * Map.MapConstants.CellSize, (c / width + d.FootprintHeight * 0.5f) * Map.MapConstants.CellSize);
    }

    /// <summary>Takes every tree within <paramref name="cleared"/> m of <paramref name="hall"/>; the nearest one left.</summary>
    private static float Clear(World w, Vector2 hall, float cleared)
    {
        float nearest = float.MaxValue;
        for (int k = 0; k < w.Resources.Capacity; k++)
        {
            if (!w.Resources.Alive[k] || w.Data.Resources[w.Resources.TypeId[k]].Resource != ResourceKind.Wood) continue;
            float d = Vector2.Distance(NodeCentre(w, k), hall);
            if (d <= cleared) w.Resources.Take(new EntityHandle(k, w.Resources.Generation[k]), int.MaxValue);
            else nearest = Math.Min(nearest, d);
        }
        return nearest;
    }

    /// <summary>The farthest wood node (from <paramref name="hall"/>) a live unit of <paramref name="player"/> is set to gather now.</summary>
    private static float FarthestWoodTarget(World w, int player, Vector2 hall)
    {
        float far = 0f;
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            EntityHandle n = w.Units.GatherNode[i];
            if (!w.Units.Alive[i] || w.Units.Owner[i] != player || n.Generation == 0 || !w.Resources.IsAlive(n)
                || w.Data.Resources[w.Resources.TypeId[n.Index]].Resource != ResourceKind.Wood) continue;
            far = Math.Max(far, Vector2.Distance(NodeCentre(w, n.Index), hall));
        }
        return far;
    }

    private sealed record Run(int FirstWood, int Lifted, float Nearest, float Farthest, int ScoutsLost, int WorkersAt, int Wood);

    /// <summary>
    /// Player 0 of a generated 2-seat Normal v Normal match, trees within <paramref name="cleared"/> m of its hall taken, run
    /// to <paramref name="until"/>. <paramref name="killAt"/>: kill the scout this many ticks after it is sent (-1 never;
    /// 0 its first tick out); <paramref name="killEvery"/> kills every scout, not just the first.
    /// </summary>
    private static Run Play(ulong seed, float cleared, int until, GameData? data = null, int killAt = -1, bool killEvery = false)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Generated(seats, seed, record: false, out _, data: data);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        while (sim.TickNumber < 5) sim.Tick();
        Vector2 hall = HallCentre(w, 0);
        float nearest = cleared > 0 ? Clear(w, hall, cleared) : 0f;
        int firstWood = -1, lifted = -1, lost = 0, sentAt = -1, slot = -1, generation = 0;
        float farthest = 0f;
        bool killedOnce = false;
        while (sim.TickNumber < until)
        {
            sim.Tick();
            if (firstWood < 0 && w.GatheredWood[0] > 0) firstWood = sim.TickNumber;
            if (lifted < 0 && ai.ReachLifted()) lifted = sim.TickNumber;
            if (ai.Board.ScoutPhase == 1 && (ai.Board.ScoutSlot != slot || ai.Board.ScoutGeneration != generation))
            {
                slot = ai.Board.ScoutSlot;
                generation = ai.Board.ScoutGeneration;
                sentAt = sim.TickNumber;
            }
            if (killAt >= 0 && sentAt >= 0 && ai.Board.ScoutPhase == 1 && sim.TickNumber - sentAt == killAt && (killEvery || !killedOnce))
            {
                var h = new EntityHandle(slot, generation);
                if (w.Units.IsAlive(h))
                {
                    CombatSystem.HitUnit(w, new PendingHit(default, 1, h, false, w.Units.Hp[slot]));
                    lost++;
                    killedOnce = true;
                }
            }
            if (sim.TickNumber % 20 == 0) farthest = Math.Max(farthest, FarthestWoodTarget(w, 0, hall));
        }
        int workers = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker) workers++;
        return new Run(firstWood, lifted, nearest, farthest, lost, workers, w.GatheredWood[0]);
    }

    /// <summary>
    /// The nearest forest at 80 / 100 / 120 m (past the 64 m reach and past the 40 m head ring plus a worker's 14 m sight):
    /// the seat gets its first wood within 1,500 ticks of the 512-reach control and sends no gatherer farther than the
    /// control's farthest + 30 m (the lifted reach takes the nearest known node, as the control does).
    /// </summary>
    [Theory]
    [InlineData(1UL, 80f)]
    [InlineData(2UL, 80f)]
    [InlineData(3UL, 80f)]
    [InlineData(1UL, 100f)]
    [InlineData(2UL, 100f)]
    [InlineData(3UL, 100f)]
    [InlineData(1UL, 120f)]
    [InlineData(2UL, 120f)]
    [InlineData(3UL, 120f)]
    public void NearestForestFarPastTheReach_WoodAboutAsSoonAsWithEveryNodeInReach(ulong seed, float cleared)
    {
        Run shipped = Play(seed, cleared, 8000);
        Run control = Play(seed, cleared, 8000, Reach512);
        _out.WriteLine($"seed {seed}, cleared {cleared} m: nearest {shipped.Nearest:F1} m; reach 64: first wood {shipped.FirstWood}, lifted {shipped.Lifted}, farthest target {shipped.Farthest:F1} m, wood {shipped.Wood} by 8000; reach 512: first wood {control.FirstWood}, farthest {control.Farthest:F1} m, wood {control.Wood}");
        Assert.True(shipped.FirstWood > 0, "no wood by tick 8,000");
        if (control.FirstWood > 0)
            Assert.True(shipped.FirstWood <= control.FirstWood + 1500, $"first wood {shipped.FirstWood} vs control {control.FirstWood}");
        Assert.True(shipped.Farthest <= Math.Max(control.Farthest, shipped.Nearest) + 30f, $"farthest wood target {shipped.Farthest:F1} m vs control {control.Farthest:F1} m");
    }

    /// <summary>
    /// The nearest forest at 90 m and the scout killed at its first tick out, 200 ticks in and 2,000 ticks in: the reach
    /// lifts anyway (a replacement circles home), the seat gets wood by tick 6,000, and it loses that one scout only. On the
    /// diff base (ab54ddb) 8 of these 9 rows gathered 0 wood by 6,000; on M6-S7 6 of 9 do (below), the other 3 are
    /// <see cref="ScoutKilledAfterTheRing_BeforeAnyForestIsSeen_TheSeatStillGetsWood"/>.
    /// </summary>
    [Theory]
    [InlineData(1UL, 0)]
    [InlineData(1UL, 200)]
    [InlineData(2UL, 0)]
    [InlineData(2UL, 200)]
    [InlineData(2UL, 2000)]
    [InlineData(3UL, 0)]
    public void ScoutKilled_OnAFarForestStart_TheSeatStillGetsWood(ulong seed, int killAt) => AssertWoodAfterAScoutKill(seed, killAt);

    /// <summary>
    /// BUG-1053: the rows where the scout dies after its head ring is seen (or its replacement does) but before any forest
    /// past 54 m is seen: no scout is sent again, the seat knows no forest (or one 185 m off, seed 3 at 200) and gathers 0
    /// wood by tick 6,000. On ab54ddb these rows gathered 0 too (not a regression of M6-S7; the reach rule doesn't reach it).
    /// </summary>
    [Theory(Skip = "BUG-1053 (open, S3): a scout lost after the head ring is seen is not replaced, and a seat that has seen no forest yet never gets wood")]
    [InlineData(1UL, 2000)]
    [InlineData(3UL, 200)]
    [InlineData(3UL, 2000)]
    public void ScoutKilledAfterTheRing_BeforeAnyForestIsSeen_TheSeatStillGetsWood(ulong seed, int killAt) => AssertWoodAfterAScoutKill(seed, killAt);

    private void AssertWoodAfterAScoutKill(ulong seed, int killAt)
    {
        Run r = Play(seed, 90f, 6000, killAt: killAt);
        _out.WriteLine($"seed {seed}, killed {killAt} ticks out: nearest {r.Nearest:F1} m; lifted {r.Lifted}; first wood {r.FirstWood}; farthest {r.Farthest:F1} m; scouts lost {r.ScoutsLost}; workers {r.WorkersAt}; wood {r.Wood}");
        Assert.True(r.Lifted > 0, "the reach never lifted");
        Assert.True(r.FirstWood > 0 && r.FirstWood <= 6000, "no wood by tick 6,000");
        Assert.True(r.ScoutsLost <= 1);
    }

    /// <summary>
    /// Every scout killed the moment it is sent (an enemy camped at the hall's door, the worst case for "a lost scout is
    /// replaced"): how many workers the seat feeds to the scout role by tick 6,000. Before M6-S7 a dead scout was never
    /// replaced (1 worker); the rule must not bleed the seat's economy dry. Bound: at most 4 scouts lost. BUG-1052: 9 scouts
    /// lost on every seed, the seat at 0 workers and 0 wood by tick 6,000 (ab54ddb: 1 scout, 18-19 workers).
    /// </summary>
    [Theory(Skip = "BUG-1052 (open, S3): a scout lost before its head ring is seen is replaced without limit; a seat whose scouts die at the door feeds every worker to the role")]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void EveryScoutKilledAtOnce_TheSeatDoesNotFeedItsWorkersToTheScoutRole(ulong seed)
    {
        Run r = Play(seed, 0f, 6000, killAt: 0, killEvery: true);
        Run calm = Play(seed, 0f, 6000);
        _out.WriteLine($"seed {seed}: scouts lost {r.ScoutsLost}; lifted {r.Lifted}; workers at 6000 {r.WorkersAt} (unharmed run {calm.WorkersAt}); wood {r.Wood} (unharmed {calm.Wood})");
        Assert.True(r.ScoutsLost <= 4, $"{r.ScoutsLost} scouts sent to die by tick 6,000");
    }

    /// <summary>
    /// A realistic camp on the head ring: four enemy line units at the head ring's first point (the one toward the map
    /// centre, 40 m out), where every scout walks first. Every scout that dies
    /// is counted; the seat must not lose more than 3 workers to the scout role by tick 6,000 and must keep gathering.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void AnEnemyCampOnTheHeadRing_CostsTheSeatAtMostThreeScouts(ulong seed)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Generated(seats, seed, record: false, out _);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        while (sim.TickNumber < 5) sim.Tick();
        Vector2 hall = HallCentre(w, 0);
        Vector2 size = new(w.NavGrid.Width * Map.MapConstants.CellSize, w.NavGrid.Height * Map.MapConstants.CellSize);
        Vector2 toward = Vector2.Normalize(size * 0.5f - hall);
        Vector2 camp = hall + toward * w.Data.AiRules.ScoutRadius;
        int line = Rts.Sim.ViewApi.StartBase.UnitOfSlot(w.Data, w.FactionOf(1), UnitSlot.Line);
        for (int k = 0; k < 4; k++) sim.Enqueue(Commands.Command.SpawnUnit(1, line, camp + new Vector2(3f * (k - 1.5f), 4f)));
        sim.Tick();
        var scouts = new HashSet<(int, int)>();
        int died = 0;
        (int Slot, int Gen) current = (-1, 0);
        while (sim.TickNumber < 6000)
        {
            sim.Tick();
            if (ai.Board.ScoutPhase == 1 && scouts.Add((ai.Board.ScoutSlot, ai.Board.ScoutGeneration))) current = (ai.Board.ScoutSlot, ai.Board.ScoutGeneration);
            if (current.Slot >= 0 && !w.Units.IsAlive(new EntityHandle(current.Slot, current.Gen)))
            {
                died++;
                current = (-1, 0);
            }
        }
        int workers = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker) workers++;
        _out.WriteLine($"seed {seed}: camp {camp}; scouts sent {scouts.Count}, died {died}; lifted {ai.ReachLifted()}; workers {workers}; gold {w.GatheredGold[0]} wood {w.GatheredWood[0]}");
        Assert.True(died <= 3, $"{died} scouts died on the camp");
        Assert.True(w.GatheredWood[0] > 0 && workers > 0);
    }

    /// <summary>
    /// Mutation guard (QA's mutant "a replacement doesn't skip the ring points already seen" survived every test): a scout
    /// killed 400 ticks out has seen part of its head ring; on the think its replacement is sent, those ring points are
    /// already marked visited (the replacement walks only the rest), on seeds 1-3.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void AReplacementScout_SkipsTheRingPointsAlreadySeen(ulong seed)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Generated(seats, seed, record: false, out _);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        while (sim.TickNumber < 5) sim.Tick();
        Clear(w, HallCentre(w, 0), 90f); // so the sweep can't end early on a forest in reach
        while (ai.Board.ScoutPhase != 1 && sim.TickNumber < 2000) sim.Tick();
        for (int t = 0; t < 400 && ai.Board.ScoutPhase == 1; t++) sim.Tick();
        Assert.Equal(1, ai.Board.ScoutPhase);
        Assert.False(ai.ReachLifted(), "setup: the ring was seen within 400 ticks");
        int seenBefore = 0;
        for (int k = 0; k < 8; k++) if (ai.Board.ScoutVisited[k]) seenBefore++;
        var h = new EntityHandle(ai.Board.ScoutSlot, ai.Board.ScoutGeneration);
        CombatSystem.HitUnit(w, new PendingHit(default, 1, h, false, w.Units.Hp[h.Index]));
        while (!(ai.Board.ScoutPhase == 1 && (ai.Board.ScoutGeneration != h.Generation || ai.Board.ScoutSlot != h.Index)) && sim.TickNumber < 4000) sim.Tick();
        Assert.Equal(1, ai.Board.ScoutPhase);
        int seenAfter = 0;
        for (int k = 0; k < 8; k++) if (ai.Board.ScoutVisited[k]) seenAfter++;
        _out.WriteLine($"seed {seed}: ring points visited before the kill {seenBefore}, marked for the replacement {seenAfter} (tick {sim.TickNumber})");
        Assert.True(seenAfter >= 1, "the replacement starts with no ring point marked seen");
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_reachLifted")]
    private static extern ref byte ReachCache(AiPlayer ai);

    /// <summary>
    /// <see cref="ScoutManager.HeadRingSeen"/> and an uncached <see cref="AiPlayer.ReachLifted"/> (the think cache emptied
    /// before each call) allocate nothing, on every seat of the mirror 2v2 while the scouts are out and the rings unseen.
    /// </summary>
    [Fact]
    public void HeadRingSeen_AndAnUncachedReachLifted_AllocateNothing()
    {
        Simulation sim = AiTeamTests.MirrorAllies(1, raiders: 0);
        while (sim.TickNumber < 700) sim.Tick();
        AiPlayer[] ais = Enumerable.Range(0, 4).Select(p => sim.AiOf(p)!).ToArray();
        bool sink = false;
        Action probe = () =>
        {
            foreach (AiPlayer ai in ais)
            {
                sink ^= ScoutManager.HeadRingSeen(ai);
                ReachCache(ai) = 0;
                sink ^= ai.ReachLifted();
                ReachCache(ai) = 0;
            }
        };
        probe();
        AllocationProbe.AssertZero(probe, _out);
        _out.WriteLine($"phases {string.Join(", ", ais.Select(a => a.Board.ScoutPhase))}; lifted {string.Join(", ", ais.Select(a => a.ReachLifted()))} ({sink})");
    }

    /// <summary>
    /// A save taken on the tick a dead scout's replacement is pending (phase 0 again, the waypoints laid) loads into a sim
    /// that follows the unsaved one hash for hash for 2,000 ticks: the "replacement" mark (laid points) survives the save.
    /// </summary>
    [Fact]
    public void ASaveWhileAReplacementIsPending_FollowsTheUnsavedRun()
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation Build()
        {
            Simulation s = AiScenes.Generated(seats, 2, record: false, out _);
            while (s.TickNumber < 5) s.Tick();
            Clear(s.World, HallCentre(s.World, 0), 90f);
            return s;
        }
        Simulation a = Build(), b = Build();
        AiPlayer ai = a.AiOf(0)!, bi = b.AiOf(0)!;
        while (ai.Board.ScoutPhase != 1 && a.TickNumber < 3000) { a.Tick(); b.Tick(); }
        Assert.Equal(1, ai.Board.ScoutPhase);
        for (int t = 0; t < 30; t++) { a.Tick(); b.Tick(); }
        foreach (Simulation s in new[] { a, b })
        {
            AiPlayer p = s.AiOf(0)!;
            var h = new EntityHandle(p.Board.ScoutSlot, p.Board.ScoutGeneration);
            CombatSystem.HitUnit(s.World, new PendingHit(default, 1, h, false, s.World.Units.Hp[h.Index]));
        }
        while (ai.Board.ScoutPhase == 1 && a.TickNumber < 4000) { a.Tick(); b.Tick(); }
        Assert.Equal(0, ai.Board.ScoutPhase);
        Assert.Equal(ai.Board.ScoutPhase, bi.Board.ScoutPhase);
        Simulation loaded = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(b));
        for (int t = 0; t < 2000; t++)
        {
            a.Tick();
            loaded.Tick();
            Assert.True(a.StateHash() == loaded.StateHash(), $"diverged at tick {a.TickNumber}");
        }
        SaveTestRun.AssertSameAi(ai, loaded.AiOf(0), "after 2,000 ticks");
    }
}
