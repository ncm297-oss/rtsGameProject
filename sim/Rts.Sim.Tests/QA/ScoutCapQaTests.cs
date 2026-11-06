using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-SH1 (session 2026-11-05-2148): the scout cap (BUG-1052, <c>ai.scoutReplacements</c>, read from the own seat's
/// saved <c>UnitLosses</c>) and the re-scout of a kind with no known node (BUG-1053), attacked on seeds 1-10. Scenes: every
/// scout killed the moment it is sent; one scout killed 0 / 200 / 2,000 ticks out on a start with its trees within 90 m
/// taken; a camp of 4 enemy line units on the head ring; a camp that walks the ring; a seat that has lost workers
/// before its scout goes out. Per row: the scouts lost, the workers kept, the farthest gather target from the hall (the
/// brief's 150 m rule), the wood. And a save taken between two camp kills (combat deaths, not test kills) that loads equal
/// and plays on hash-equal.
/// </summary>
[Collection(SerialCollection.Name)]
public class ScoutCapQaTests
{
    private readonly ITestOutputHelper _out;

    public ScoutCapQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>The brief's rule (b): no capped row may send a gatherer farther than this from its hall.</summary>
    private const float FarthestAllowed = 150f;

    private static PlayerSeat[] Seats() => new PlayerSeat[] { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };

    /// <summary><c>CappedAt</c>: the tick the sweep ended on a scout's death (phase 2, the scout dead); <c>FarthestAfterCap</c> the farthest gather target from then on.</summary>
    private sealed record Row(int Sent, int Lost, int Workers, float Farthest, int Wood, int FirstWood, bool KnowsWood, int CappedAt, int Losses, float FarthestAfterCap);

    /// <summary>The farthest node (any kind) a live unit of <paramref name="player"/> is set to gather, from <paramref name="hall"/>.</summary>
    internal static float FarthestGather(World w, int player, Vector2 hall)
    {
        float far = 0f;
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            EntityHandle n = w.Units.GatherNode[i];
            if (!w.Units.Alive[i] || w.Units.Owner[i] != player || n.Generation == 0 || !w.Resources.IsAlive(n)) continue;
            ResourceDef d = w.Data.Resources[w.Resources.TypeId[n.Index]];
            int c = w.Resources.Cell[n.Index];
            var centre = new Vector2((c % w.NavGrid.Width + d.FootprintWidth * 0.5f) * Map.MapConstants.CellSize,
                (c / w.NavGrid.Width + d.FootprintHeight * 0.5f) * Map.MapConstants.CellSize);
            far = Math.Max(far, Vector2.Distance(centre, hall));
        }
        return far;
    }

    private static int Workers(World w, int player)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == player && w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker) n++;
        return n;
    }

    private static bool KnowsWood(AiPlayer ai, World w)
    {
        bool wood = false;
        for (int s = -1; ai.View.NextResource(ref s, out SeenResource node);)
            wood |= w.Data.Resources[node.TypeId].Resource == ResourceKind.Wood;
        return wood;
    }

    /// <summary>
    /// Plays player 0 of a 2-seat Normal match to <paramref name="until"/>. <paramref name="cleared"/>: trees within that many
    /// m of the hall taken at tick 5. <paramref name="killAt"/> / <paramref name="killEvery"/> as in ReachRuleQaTests.
    /// <paramref name="before"/> runs at tick 5 (a camp spawned, workers killed); <paramref name="each"/> after every tick.
    /// </summary>
    private static Row Play(ulong seed, int until, float cleared = 0f, int killAt = -1, bool killEvery = false,
        Action<Simulation, Vector2>? before = null, Action<Simulation, Vector2>? each = null, int raiders = 0)
    {
        Simulation sim = AiScenes.Generated(Seats(), seed, record: false, out _, raiders: raiders);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        while (sim.TickNumber < 5) sim.Tick();
        Vector2 hall = ReachRuleQaTests.HallCentre(w, 0);
        if (cleared > 0) ReachRuleQaTests.Clear(w, hall, cleared);
        before?.Invoke(sim, hall);
        int sent = 0, lost = 0, sentAt = -1, firstWood = -1, cappedAt = -1, slot = -1, gen = 0;
        float farthest = 0f, afterCap = 0f;
        bool killedOnce = false;
        while (sim.TickNumber < until)
        {
            sim.Tick();
            each?.Invoke(sim, hall);
            Blackboard b = ai.Board;
            if (b.ScoutPhase == 1 && (b.ScoutSlot != slot || b.ScoutGeneration != gen))
            {
                slot = b.ScoutSlot;
                gen = b.ScoutGeneration;
                sentAt = sim.TickNumber;
                sent++;
            }
            if (killAt >= 0 && sentAt >= 0 && b.ScoutPhase == 1 && sim.TickNumber - sentAt == killAt && (killEvery || !killedOnce))
            {
                var h = new EntityHandle(slot, gen);
                if (w.Units.IsAlive(h))
                {
                    CombatSystem.HitUnit(w, new PendingHit(default, 1, h, false, w.Units.Hp[slot]));
                    killedOnce = true;
                }
            }
            if (cappedAt < 0 && b.ScoutPhase == 2 && slot >= 0 && !w.Units.IsAlive(new EntityHandle(slot, gen))) cappedAt = sim.TickNumber;
            if (firstWood < 0 && w.GatheredWood[0] > 0) firstWood = sim.TickNumber;
            if (sim.TickNumber % 20 == 0)
            {
                float f = FarthestGather(w, 0, hall);
                farthest = Math.Max(farthest, f);
                if (cappedAt >= 0) afterCap = Math.Max(afterCap, f);
            }
        }
        // Scouts lost: every scout sent but the last, which is dead or alive at the end (an upper bound).
        lost = sent - (slot >= 0 && w.Units.IsAlive(new EntityHandle(slot, gen)) ? 1 : 0);
        return new Row(sent, lost, Workers(w, 0), farthest, w.GatheredWood[0], firstWood, KnowsWood(ai, w), cappedAt, w.UnitLosses[0], afterCap);
    }

    public static TheoryData<ulong> Seeds1To10()
    {
        var d = new TheoryData<ulong>();
        for (ulong s = 1; s <= 10; s++) d.Add(s);
        return d;
    }

    /// <summary>
    /// Every scout killed on its first tick out, seeds 1-10: at most <c>ai.scoutReplacements</c> + 1 (3) scouts sent, at least
    /// 15 workers at tick 6,000, and no gatherer sent more than 150 m from its hall once the capped reach lifts (the brief's
    /// rule for lifting rather than holding). Wood is printed, and asserted where the seat knows a forest.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Seeds1To10))]
    public void EveryScoutKilledAtTheDoor_Seeds1To10_CapHolds_WorkersKept_GatherWithin150m(ulong seed)
    {
        Row r = Play(seed, 6000, killAt: 0, killEvery: true);
        Row calm = Play(seed, 6000);
        _out.WriteLine($"ROW door seed {seed}: sent {r.Sent}; losses {r.Losses}; capped at {r.CappedAt}; workers {r.Workers} (calm {calm.Workers}); farthest {r.Farthest:F1} m (after the cap {r.FarthestAfterCap:F1}; calm {calm.Farthest:F1}); wood {r.Wood} (first {r.FirstWood}; calm {calm.Wood}); forest known {r.KnowsWood}");
        Assert.True(r.CappedAt > 0, "the sweep never ended on a death");
        Assert.True(r.Sent <= TestSim.Data.AiRules.ScoutReplacements + 1, $"{r.Sent} scouts sent");
        Assert.True(r.Workers >= 15, $"{r.Workers} workers");
        Assert.True(r.Farthest <= FarthestAllowed, $"a gatherer sent {r.Farthest:F1} m from the hall");
        Assert.True(r.Wood > 0 || !r.KnowsWood, "a forest known and no wood by 6,000");
    }

    /// <summary>
    /// One scout killed 0 / 200 / 2,000 ticks out on a start whose trees within 90 m are taken, seeds 1-10, beside the same
    /// start unharmed: printed (first wood, the farthest gather target, own losses: the far gatherers meet the enemy).
    /// Asserted: at most <c>ai.scoutReplacements</c> + 1 scouts sent for one death, and at least 14 workers. The far gather
    /// targets are printed only: on this start the unharmed run already sends gatherers 102-258 m (M6-S7's lifted reach takes
    /// the nearest known forest), and the kill rows' farthest differ from it only on seed 9 at 0 (251 v 204 m) and seed 3 at
    /// 200 / 2,000 (186 v 151 m, BUG-1053's open rows); none of these rows is capped.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Seeds1To10))]
    public void OneScoutKilled_At0_200_2000_OnAFarForestStart_Seeds1To10(ulong seed)
    {
        Row calm = Play(seed, 6000, cleared: 90f);
        _out.WriteLine($"ROW unharmed seed {seed}: sent {calm.Sent}; losses {calm.Losses}; workers {calm.Workers}; farthest {calm.Farthest:F1} m; wood {calm.Wood} (first {calm.FirstWood})");
        var fails = new List<string>();
        foreach (int at in new[] { 0, 200, 2000 })
        {
            Row r = Play(seed, 6000, cleared: 90f, killAt: at);
            _out.WriteLine($"ROW once@{at} seed {seed}: sent {r.Sent}; losses {r.Losses}; workers {r.Workers}; farthest {r.Farthest:F1} m; wood {r.Wood} (first {r.FirstWood}); forest known {r.KnowsWood}");
            if (r.Sent > TestSim.Data.AiRules.ScoutReplacements + 1) fails.Add($"@{at}: {r.Sent} scouts sent for one death");
            if (r.Workers < 14) fails.Add($"@{at}: {r.Workers} workers");
        }
        Assert.True(fails.Count == 0, string.Join("; ", fails));
    }

    private static Vector2 Toward(World w, Vector2 hall)
    {
        Vector2 size = new(w.NavGrid.Width * Map.MapConstants.CellSize, w.NavGrid.Height * Map.MapConstants.CellSize);
        return Vector2.Normalize(size * 0.5f - hall);
    }

    private static void SpawnCamp(Simulation sim, Vector2 at)
    {
        int line = Rts.Sim.ViewApi.StartBase.UnitOfSlot(sim.World.Data, sim.World.FactionOf(1), UnitSlot.Line);
        for (int k = 0; k < 4; k++) sim.Enqueue(Command.SpawnUnit(1, line, at + new Vector2(3f * (k - 1.5f), 4f)));
    }

    /// <summary>
    /// A camp of 4 enemy line units on the head ring's first point, seeds 1-10: scouts that die there are capped (at most 3
    /// sent), the seat keeps 15+ workers, gets wood where it knows a forest, and after the cap sends no gatherer farther than
    /// 150 m or the unharmed run's farthest, whichever is more (seeds 6, 8, 9 gather 175-246 m out unharmed: later mines).
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Seeds1To10))]
    public void ACampOfFourOnTheRing_Seeds1To10(ulong seed)
    {
        Row r = Play(seed, 6000, before: (sim, hall) => SpawnCamp(sim, hall + Toward(sim.World, hall) * sim.World.Data.AiRules.ScoutRadius));
        Row calm = Play(seed, 6000);
        _out.WriteLine($"ROW camp seed {seed}: sent {r.Sent}; losses {r.Losses}; capped at {r.CappedAt}; workers {r.Workers} (calm {calm.Workers}); farthest {r.Farthest:F1} m (after the cap {r.FarthestAfterCap:F1}; calm {calm.Farthest:F1}); wood {r.Wood} (first {r.FirstWood}); forest known {r.KnowsWood}");
        Assert.True(r.Sent <= TestSim.Data.AiRules.ScoutReplacements + 1, $"{r.Sent} scouts sent");
        Assert.True(r.Workers >= 15, $"{r.Workers} workers");
        Assert.True(r.FarthestAfterCap <= Math.Max(FarthestAllowed, calm.Farthest), $"a gatherer sent {r.FarthestAfterCap:F1} m from the hall after the cap (calm {calm.Farthest:F1})");
        Assert.True(r.Wood > 0 || !r.KnowsWood, "a forest known and no wood by 6,000");
    }

    /// <summary>
    /// The camp walks the head ring: every 100 ticks its 4 units are ordered to the next compass point 40 m round the hall
    /// (clockwise), so it meets scouts wherever they are on the ring, seeds 1-10. Scouts bounded as for the still camp; the
    /// workers are printed only (the walking camp kills gatherers too, seed 9 down to 10, with one scout sent).
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Seeds1To10))]
    public void ACampWalkingTheRing_Seeds1To10(ulong seed)
    {
        var camp = new List<EntityHandle>();
        int point = 0;
        Row r = Play(seed, 6000,
            before: (sim, hall) => SpawnCamp(sim, hall + Toward(sim.World, hall) * sim.World.Data.AiRules.ScoutRadius),
            each: (sim, hall) =>
            {
                World w = sim.World;
                if (camp.Count == 0)
                    for (int i = 0; i < w.Units.Capacity; i++)
                        if (w.Units.Alive[i] && w.Units.Owner[i] == 1 && w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Line) camp.Add(new EntityHandle(i, w.Units.Generation[i]));
                if (sim.TickNumber % 100 != 0) return;
                point = (point + 1) % 8;
                float a = point * MathF.PI / 4f;
                Vector2 to = hall + new Vector2(MathF.Cos(a), MathF.Sin(a)) * w.Data.AiRules.ScoutRadius;
                foreach (EntityHandle h in camp)
                    if (w.Units.IsAlive(h)) sim.Enqueue(Command.Move(1, h, to));
            });
        Row calm = Play(seed, 6000);
        _out.WriteLine($"ROW moving-camp seed {seed}: sent {r.Sent}; losses {r.Losses}; capped at {r.CappedAt}; workers {r.Workers} (calm {calm.Workers}); farthest {r.Farthest:F1} m (after the cap {r.FarthestAfterCap:F1}; calm {calm.Farthest:F1}); wood {r.Wood} (first {r.FirstWood}); forest known {r.KnowsWood}");
        Assert.True(r.Sent <= TestSim.Data.AiRules.ScoutReplacements + 1, $"{r.Sent} scouts sent");
        Assert.True(r.FarthestAfterCap <= Math.Max(FarthestAllowed, calm.Farthest), $"a gatherer sent {r.FarthestAfterCap:F1} m from the hall after the cap (calm {calm.Farthest:F1})");
    }

    /// <summary>
    /// A seat that has lost 3 workers before its scout is sent, then loses that scout before the ring is seen. QA's first
    /// version pinned the M6-SH1 semantics (the cap read the 3 earlier losses: 1 scout sent, the sweep ended on the death,
    /// gatherers within 150 m, and no wood: BUG-1055). M6-SH1 round 1 (the developer, for BUG-1055): the first scout's death
    /// is always followed, so 2 scouts are sent (the cap still bounds the second), the sweep doesn't end on the first death,
    /// and the gatherers go no farther than the same far-forest start unharmed sends them (seeds 1-3: 210.6 / 174.2 /
    /// 151.2 m; no 150 m bound: that was the capped rows' rule).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void ASeatPastTheCapBeforeItsFirstScout_ItsFirstScoutIsReplaced_AndGathersNoFartherThanUnharmed(ulong seed)
    {
        Row r = Play(seed, 6000, cleared: 90f, killAt: 30, before: (sim, hall) =>
        {
            World w = sim.World;
            int killed = 0;
            for (int i = 0; i < w.Units.Capacity && killed < 3; i++)
            {
                if (!w.Units.Alive[i] || w.Units.Owner[i] != 0) continue;
                CombatSystem.HitUnit(w, new PendingHit(default, 1, new EntityHandle(i, w.Units.Generation[i]), false, w.Units.Hp[i]));
                killed++;
            }
        });
        Row calm = Play(seed, 6000, cleared: 90f);
        _out.WriteLine($"ROW pre-losses seed {seed}: sent {r.Sent}; losses {r.Losses}; capped at {r.CappedAt}; workers {r.Workers}; farthest {r.Farthest:F1} m (unharmed {calm.Farthest:F1}); wood {r.Wood} (first {r.FirstWood}); forest known {r.KnowsWood}");
        Assert.Equal(2, r.Sent);
        Assert.True(r.CappedAt < 0, "the sweep ended on the first scout's death");
        Assert.True(r.Farthest <= Math.Max(FarthestAllowed, calm.Farthest), $"a gatherer sent {r.Farthest:F1} m from the hall (unharmed {calm.Farthest:F1})");
    }

    /// <summary>
    /// BUG-1055: the same seat (3 workers lost before its scout goes out, its trees within 90 m taken, the scout killed 30
    /// ticks out) knows no forest when the cap ends its sweep, and the cap also bars BUG-1053's re-scout, so it gathers no
    /// wood by tick 6,000 on seeds 1-3 (measured M6-SH1: wood 0, "forest known False"). With the cap off the scout is
    /// replaced and the seat gets wood. Un-skipped in M6-SH1 round 1 (the first scout's death is always followed): first
    /// wood at ticks 4,979 / 3,144 / 4,688.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void ASeatPastTheCapBeforeItsFirstScout_OnAFarForestStart_StillGetsWood(ulong seed)
    {
        Row r = Play(seed, 6000, cleared: 90f, killAt: 30, before: (sim, hall) =>
        {
            World w = sim.World;
            int killed = 0;
            for (int i = 0; i < w.Units.Capacity && killed < 3; i++)
            {
                if (!w.Units.Alive[i] || w.Units.Owner[i] != 0) continue;
                CombatSystem.HitUnit(w, new PendingHit(default, 1, new EntityHandle(i, w.Units.Generation[i]), false, w.Units.Hp[i]));
                killed++;
            }
        });
        _out.WriteLine($"seed {seed}: sent {r.Sent}; losses {r.Losses}; wood {r.Wood} (first {r.FirstWood}); forest known {r.KnowsWood}");
        Assert.True(r.Wood > 0, "no wood by tick 6,000");
    }

    /// <summary>Kills player 0's scout in <paramref name="sim"/> if it is out, alive and was sent <paramref name="after"/> ticks ago (tracked by the caller).</summary>
    private static bool KillScoutIfDue(Simulation sim, ref int slot, ref int gen, ref int sentAt, int after)
    {
        Blackboard b = sim.AiOf(0)!.Board;
        if (b.ScoutPhase == 1 && (b.ScoutSlot != slot || b.ScoutGeneration != gen))
        {
            slot = b.ScoutSlot;
            gen = b.ScoutGeneration;
            sentAt = sim.TickNumber;
        }
        var h = new EntityHandle(slot, gen);
        if (b.ScoutPhase != 1 || sim.TickNumber - sentAt != after || !sim.World.Units.IsAlive(h)) return false;
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, h, false, sim.World.Units.Hp[slot]));
        return true;
    }

    /// <summary>
    /// A save taken between two scout deaths, on seeds 2-5 (the developer's row is seed 1): every scout killed on its first
    /// tick out, the save made on the first tick after the second death is judged (a replacement pending, own losses 2: the
    /// next death caps). The loaded seat equals the saved one, the loaded run follows the unsaved one hash for hash to tick
    /// 6,000 with the same kills, the cap engages on the same tick in both, and the seats are equal at the end. And
    /// <paramref name="seed"/> on a far-forest start (trees within 90 m taken) with the scout killed 2,000 ticks out: the
    /// save is taken while BUG-1053's re-scout is pending, with the same checks.
    /// </summary>
    [Theory]
    [InlineData(2UL, false)]
    [InlineData(3UL, false)]
    [InlineData(4UL, false)]
    [InlineData(5UL, false)]
    [InlineData(1UL, true)]
    [InlineData(3UL, true)]
    public void ASaveBetweenTwoScoutDeaths_LoadsEqual_AndPlaysOnHashEqual(ulong seed, bool rescout)
    {
        Simulation a = AiScenes.Generated(Seats(), seed, record: false, out _);
        while (a.TickNumber < 5) a.Tick();
        if (rescout) ReachRuleQaTests.Clear(a.World, ReachRuleQaTests.HallCentre(a.World, 0), 90f);
        int after = rescout ? 2000 : 0, deathsToSave = rescout ? 1 : 2;
        AiPlayer ai = a.AiOf(0)!;
        int sa = -1, ga = 0, ta = -1, sl = -1, gl = 0, tl = -1, deaths = 0, savedAt = -1, cappedA = -1, cappedL = -1;
        bool judged = false;
        Simulation? loaded = null;
        while (a.TickNumber < 6000)
        {
            a.Tick();
            bool ka = KillScoutIfDue(a, ref sa, ref ga, ref ta, after);
            if (loaded != null)
            {
                loaded.Tick();
                bool kl = KillScoutIfDue(loaded, ref sl, ref gl, ref tl, after);
                Assert.Equal(ka, kl);
                Assert.True(a.StateHash() == loaded.StateHash(), $"diverged at tick {a.TickNumber} (saved at {savedAt})");
                if (cappedL < 0 && loaded.AiOf(0)!.Board.ScoutPhase == 2) cappedL = loaded.TickNumber;
            }
            if (cappedA < 0 && ai.Board.ScoutPhase == 2) cappedA = a.TickNumber;
            if (ka) { deaths++; judged = false; }
            if (loaded == null && deaths == deathsToSave && !judged && ai.Board.ScoutPhase == 0)
            {
                judged = true;
                loaded = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(a));
                savedAt = a.TickNumber;
                (sl, gl, tl) = (sa, ga, ta);
                SaveTestRun.AssertSameAi(ai, loaded.AiOf(0), $"on load at {savedAt}");
                // Asked of both (the answer is cached until the next think), after the compare: the re-scout row's ring was seen.
                bool liftedA = ai.ReachLifted(), liftedL = loaded.AiOf(0)!.ReachLifted();
                Assert.Equal(liftedA, liftedL);
                Assert.True(!rescout || liftedA, "setup: the re-scout row's ring was not seen before the kill");
            }
        }
        _out.WriteLine($"seed {seed}{(rescout ? " (re-scout)" : "")}: {deaths} scouts killed; saved at {savedAt} (own losses then {deathsToSave}); capped / ended {cappedA} / {cappedL}; own losses at 6,000 {a.World.UnitLosses[0]}");
        Assert.True(loaded != null, "no save was taken: the scene never reached the deaths");
        SaveTestRun.AssertSameAi(ai, loaded!.AiOf(0), "at 6,000");
        Assert.Equal(cappedA, cappedL);
    }

    /// <summary>
    /// Mutation guard (QA's mutant "KnowsEveryKind counts a node the economy dropped as unreachable" survived every other
    /// test): a seat that knows forests, all of them marked dropped (<c>Blackboard.NodeDropped</c>, as the economy marks an
    /// unreachable node), and loses its scout after the ring is seen, sends one again (phase 0), seeds 1-3. Unmarked, the
    /// same death ends the sweep (the developer's <c>ASeatKnowingANodeOfEveryKind_NeverReScouts</c>).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void EveryKnownForestDropped_TheScoutLostAfterTheRing_IsSentAgain(ulong seed)
    {
        Simulation sim = AiScenes.Generated(Seats(), seed, record: false, out _);
        AiPlayer ai = sim.AiOf(0)!;
        while (ai.Board.ScoutPhase != 1 && sim.TickNumber < 3000) sim.Tick();
        while (!ai.ReachLifted() && ai.Board.ScoutPhase == 1 && sim.TickNumber < 6000) sim.Tick();
        Assert.True(ai.ReachLifted() && ai.Board.ScoutPhase == 1, "setup: the sweep ended before the ring was seen");
        Assert.True(ScoutManager.KnowsEveryKind(ai), "setup: the seat knows no node of some kind");
        int marked = 0;
        for (int s = -1; ai.View.NextResource(ref s, out SeenResource node);)
        {
            if (sim.World.Data.Resources[node.TypeId].Resource != ResourceKind.Wood) continue;
            if (ai.Board.NodeDropped[s] == 0) ai.Board.NodeMarks++;
            ai.Board.NodeDropped[s] = node.Handle.Generation;
            marked++;
        }
        Assert.True(marked > 0);
        Assert.False(ScoutManager.KnowsEveryKind(ai), "every forest dropped, the seat still counts one as known");
        var h = new EntityHandle(ai.Board.ScoutSlot, ai.Board.ScoutGeneration);
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, h, false, sim.World.Units.Hp[h.Index]));
        for (int t = 0; t < 400 && ai.Board.ScoutPhase == 1; t++) sim.Tick();
        _out.WriteLine($"seed {seed}: {marked} forests marked dropped; phase after the death {ai.Board.ScoutPhase} (tick {sim.TickNumber})");
        Assert.Equal(0, ai.Board.ScoutPhase);
    }
}
