using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Rts.Sim.Vision;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-S5 fix (BUG-1045, <c>ai.gatherReach</c>, session 2026-11-05-0805). The reach rule's blind spots: a seat whose scout
/// dies before the sweep ends (M6-S7: the reach holds, and it must not idle), a seat with no finished drop-off (every
/// node in reach), a start whose nearest forest lies just past the reach (M6-S7: BUG-1047 / 1048 fixed, both rows un-skipped); <see cref="AiPlayer.InGatherReach"/> and
/// <see cref="ScoutManager.FoundEnough"/> allocate nothing; the BUG-1045 mirror-start 2v2 recorded through the scout's
/// phases plays back (format 7) to every checkpoint.
/// </summary>
[Collection(SerialCollection.Name)]
public class GatherReachQaTests
{
    private readonly ITestOutputHelper _out;

    public GatherReachQaTests(ITestOutputHelper output) => _out = output;

    private static Vector2 Centre(World w, int slot)
    {
        BuildingDef d = w.Data.Buildings[w.Buildings.TypeId[slot]];
        int c = w.Buildings.Cell[slot], width = w.NavGrid.Width;
        return new Vector2((c % width + d.FootprintWidth * 0.5f) * Map.MapConstants.CellSize, (c / width + d.FootprintHeight * 0.5f) * Map.MapConstants.CellSize);
    }

    /// <summary>The live wood nodes' slots whose centre (as the AI measures it) lies within <paramref name="radius"/> m of <paramref name="at"/>.</summary>
    private static List<int> WoodWithin(World w, AiPlayer ai, Vector2 at, float radius)
    {
        var list = new List<int>();
        ResourceStore r = w.Resources;
        for (int k = 0; k < r.Capacity; k++)
            if (r.Alive[k] && w.Data.Resources[r.TypeId[k]].Resource == ResourceKind.Wood && Vector2.Distance(ai.NodeCentre(r.TypeId[k], r.Cell[k]), at) <= radius)
                list.Add(k);
        return list;
    }

    private static float NearestWood(World w, AiPlayer ai, Vector2 at)
    {
        float best = float.MaxValue;
        ResourceStore r = w.Resources;
        for (int k = 0; k < r.Capacity; k++)
            if (r.Alive[k] && w.Data.Resources[r.TypeId[k]].Resource == ResourceKind.Wood)
                best = Math.Min(best, Vector2.Distance(ai.NodeCentre(r.TypeId[k], r.Cell[k]), at));
        return best;
    }

    // ---------- (a) the scout dies before the sweep ends ----------

    /// <summary>
    /// BUG-1045's seat 3 (mirror start, seed 1) with its scout killed on the first tick it is out (phase 1), run to tick
    /// 4,000. Returns the sim, the tick the scout died, the workers on wood at the end and the nearest known forest.
    /// </summary>
    private static (Simulation Sim, int KilledAt, int WoodWorkers, float NearestKnownWood, float FarthestWoodTarget) ScoutKilledAtOnce()
    {
        Simulation sim = AiTeamTests.MirrorAllies(1);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(3)!;
        while (ai.Board.ScoutPhase != 1 && sim.TickNumber < 2000) sim.Tick();
        Assert.Equal(1, ai.Board.ScoutPhase);
        int scout = ai.Board.ScoutSlot;
        var h = new EntityHandle(scout, w.Units.Generation[scout]);
        int killedAt = sim.TickNumber;
        CombatSystem.HitUnit(w, new PendingHit(default, 0, h, false, w.Units.Hp[scout]));
        Assert.False(w.Units.IsAlive(h));
        for (int t = 0; t < 2 * ai.ThinkTicks; t++) sim.Tick();
        // M6-S7 (BUG-1048): a scout lost before its head ring was seen is replaced (the phase goes back to 0 and the next
        // think sends another worker), and the dead scout no longer lifts the reach; only the head ring being seen does.
        Assert.NotEqual(2, ai.Board.ScoutPhase);
        Assert.NotEqual(scout, ai.Board.ScoutSlot);
        Assert.False(ai.ReachLifted(), "the scout died on its first tick out: its head ring can't have been seen yet");
        Assert.False(ai.InGatherReach(new Vector2(1f, 1f)) && ai.InGatherReach(new Vector2(511f, 511f)), "a dead scout lifted the reach: both far corners in reach");
        while (sim.TickNumber < 4000) sim.Tick();
        int woodWorkers = 0;
        float farthest = 0f;
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            EntityHandle n = w.Units.GatherNode[i];
            if (!w.Units.Alive[i] || w.Units.Owner[i] != 3 || n.Generation == 0 || !w.Resources.IsAlive(n)
                || w.Data.Resources[w.Resources.TypeId[n.Index]].Resource != ResourceKind.Wood) continue;
            woodWorkers++;
            farthest = Math.Max(farthest, Vector2.Distance(ai.NodeCentre(w.Resources.TypeId[n.Index], w.Resources.Cell[n.Index]), ai.HallPoint));
        }
        float known = float.MaxValue;
        int s = -1;
        while (ai.View.NextResource(ref s, out SeenResource node))
            if (w.Data.Resources[node.TypeId].Resource == ResourceKind.Wood)
                known = Math.Min(known, Vector2.Distance(ai.NodeCentre(node.TypeId, node.Cell), ai.HallPoint));
        return (sim, killedAt, woodWorkers, known, farthest);
    }

    /// <summary>
    /// A seat whose scout dies before the sweep ends (BUG-1045's seat 3, scout killed on its first tick out): M6-S7
    /// (BUG-1048) its reach still holds (the head ring is unseen), another worker is sent to scout, and it does not idle:
    /// workers are sent to wood. (Before M6-S7 the death ended the scouting and lifted the reach: this row was
    /// <c>..._EveryKnownNodeIsInReach_...</c>.)
    /// </summary>
    [Fact]
    public void TheScoutDiesBeforeTheSweepEnds_TheReachHolds_AndTheSeatSendsWorkersToWood()
    {
        (Simulation sim, int killedAt, int woodWorkers, float known, float farthest) = ScoutKilledAtOnce();
        World w = sim.World;
        _out.WriteLine($"scout killed at tick {killedAt}; by tick 4,000 seat 3 gathered gold {w.GatheredGold[3]} wood {w.GatheredWood[3]}; " +
            $"{woodWorkers} workers on wood, the farthest {farthest:F0} m from the hall; nearest known forest {known:F0} m");
        Assert.True(woodWorkers > 0, "seat 3 idles its wood: no worker on a forest");
    }

    /// <summary>
    /// BUG-1048: the same seat still gets wood near home: by tick 4,000 it has gathered some (BUG-1045's own bar). Before
    /// M6-S7 the dead scout lifted the reach, and knowing only its ally's forests (its own, 17.5 m off, unseen) it walked its
    /// wood gatherers 300+ m north: 0 wood by tick 4,000.
    /// </summary>
    [Fact]
    public void TheScoutDiesBeforeTheSweepEnds_TheSeatStillGathersWood_By4000()
    {
        (Simulation sim, _, _, float known, float farthest) = ScoutKilledAtOnce();
        Assert.True(sim.World.GatheredWood[3] > 0, $"seat 3 gathered no wood by tick 4,000 (nearest known forest {known:F0} m, wood target up to {farthest:F0} m)");
    }

    // ---------- (a) no finished drop-off ----------

    /// <summary>
    /// A generated 2-seat match's player 0 at its first hall (its head ring unseen, so the reach holds): a point 100 m off its
    /// hall is out of reach and one 20 m off in, the edge exact; with its hall killed (no finished drop-off) every node is in
    /// reach (nothing to measure from). M6-S7: the flat scene this row used is explored whole, so its ring is seen at once
    /// and the reach lifted (<see cref="TheFlatScene_ExploredWhole_HasItsReachLifted_FromTheFirstThink"/>).
    /// </summary>
    [Fact]
    public void NoFinishedDropOff_EveryNodeIsInReach_WithTheHall_OnlyNearOnes()
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Generated(seats, 1, record: false, out _);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        while (!ai.HasDropOff && sim.TickNumber < 50) sim.Tick();
        Assert.True(ai.HasDropOff && !ai.ReachLifted(), "setup");
        Vector2 hall = ai.HallPoint;
        Assert.True(ai.InGatherReach(hall + new Vector2(20f, 0f)));
        Assert.False(ai.InGatherReach(hall + new Vector2(100f, 0f)));
        Assert.True(ai.InGatherReach(hall + new Vector2(ai.Rules.GatherReach - 0.01f, 0f)), "just inside the reach");
        Assert.False(ai.InGatherReach(hall + new Vector2(ai.Rules.GatherReach + 0.01f, 0f)), "just outside the reach");
        int hallSlot = -1;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0) hallSlot = k;
        CombatSystem.HitBuilding(w, new PendingHit(default, 1, new EntityHandle(hallSlot, w.Buildings.Generation[hallSlot]), true, w.Buildings.Hp[hallSlot]));
        for (int t = 0; t < 2 * ai.ThinkTicks; t++) sim.Tick();
        Assert.False(ai.HasDropOff);
        Assert.True(ai.InGatherReach(hall + new Vector2(100f, 0f)), "no drop-off: a far node is out of reach");
    }

    /// <summary>
    /// M6-SH1 (BUG-1049 item 4, the mutant "a drop-off site counts as an anchor" survived every test): while the reach holds,
    /// a second drop-off of the seat's faction placed about 100 m from the hall as a construction site doesn't bring a node
    /// beside it into reach; the same building finished does. (Item 3, the soak's starved line: see the comment by
    /// <c>Stress/TeamAiSoakStressTests.RaidedTicksIfStarved</c>.)
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASecondDropOff_AnchorsTheReach_OnlyOnceFinished(bool site)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Generated(seats, 1, record: false, out _);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        while (!ai.HasDropOff && sim.TickNumber < 50) sim.Tick();
        Assert.True(ai.HasDropOff && !ai.ReachLifted(), "setup");
        Vector2 hall = ai.HallPoint;
        int type = -1;
        foreach (BuildingDef d in w.Data.Buildings)
            if (d.Faction == w.FactionOf(0) && d.DropOff && d.Slot != BuildingSlot.TownHall) type = d.Id;
        Assert.True(type >= 0, "setup: the faction has no second drop-off");
        BuildingDef def = w.Data.Buildings[type];
        int cell = -1;
        Vector2 centre = default;
        for (int c = 0; c < w.NavGrid.Width * w.NavGrid.Height && cell < 0; c++)
        {
            var at = new Vector2((c % w.NavGrid.Width + def.FootprintWidth * 0.5f) * Map.MapConstants.CellSize, (c / w.NavGrid.Width + def.FootprintHeight * 0.5f) * Map.MapConstants.CellSize);
            float d = Vector2.Distance(at, hall);
            if (d < 95f || d > 110f || !w.Buildings.Fits(type, c)) continue;
            cell = c;
            centre = at;
        }
        Assert.True(cell >= 0, "setup: no free cell 95-110 m from the hall");
        Assert.False(ai.InGatherReach(centre), "setup: the spot is in the hall's reach");
        Assert.True(w.Buildings.Spawn(0, type, cell, out _, site));
        for (int t = 0; t < 2 * ai.ThinkTicks; t++) sim.Tick();
        Assert.False(ai.ReachLifted(), "setup: the reach lifted");
        Assert.Equal(!site, ai.InGatherReach(centre));
    }

    /// <summary>
    /// M6-S7: on the flat AI scene (every cell explored) the head ring round the hall is seen from the first think, so the
    /// reach has lifted: a point 100 m off the hall is in reach.
    /// </summary>
    [Fact]
    public void TheFlatScene_ExploredWhole_HasItsReachLifted_FromTheFirstThink()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats);
        AiPlayer ai = sim.AiOf(0)!;
        sim.Tick();
        Assert.True(ai.HasDropOff && ai.Board.ScoutPhase != 2, "setup");
        Assert.True(ai.ReachLifted());
        Assert.True(ai.InGatherReach(ai.HallPoint + new Vector2(100f, 0f)));
    }

    // ---------- (a) the nearest forest just past the reach ----------

    /// <summary>
    /// A generated 2-seat free-for-all (128 map, two Normal AI seats) with every tree within <paramref name="cleared"/> m of
    /// player 0's hall taken once the hall stands, so its nearest forest lies past the shipped 64 m (66.6, 90.0, 77.0 m on
    /// seeds 1-3). Runs to <paramref name="until"/> or the first wood player 0 gathers; returns that tick (-1: none), the
    /// tick its scout is done (-1: not yet) and the nearest forest's distance.
    /// </summary>
    private static (int FirstWood, int ScoutDone, float Nearest) NearestForestPastReach(ulong seed, float cleared, double reach, int until)
    {
        GameData data = reach == 64 ? TestSim.Data : AiScenes.DataWith(dir => dir.EditJson("common/rules.json", r => r["ai"]!["gatherReach"] = reach));
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Generated(seats, seed, record: false, out _, data: data);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        int hallSlot = -1;
        while (hallSlot < 0 && sim.TickNumber < 5) // the hall and workers spawn through commands
        {
            sim.Tick();
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0) hallSlot = k;
        }
        Assert.True(hallSlot >= 0, "setup: no hall");
        Vector2 hall = Centre(w, hallSlot);
        foreach (int k in WoodWithin(w, ai, hall, cleared))
            w.Resources.Take(new EntityHandle(k, w.Resources.Generation[k]), int.MaxValue);
        float nearest = NearestWood(w, ai, hall);
        int firstWood = -1, scoutDone = -1;
        while (sim.TickNumber < until && firstWood < 0)
        {
            sim.Tick();
            if (firstWood < 0 && w.GatheredWood[0] > 0) firstWood = sim.TickNumber;
            if (scoutDone < 0 && ai.Board.ScoutPhase == 2) scoutDone = sim.TickNumber;
        }
        return (firstWood, scoutDone, nearest);
    }

    /// <summary>
    /// Control for BUG-1047: the same starts with <c>ai.gatherReach</c> 512 (every node in reach, the pre-M6-S5 choice) gather
    /// their first wood by tick 5,000 (measured 2,931 / 3,658 / 3,912).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void NearestForestJustPastTheReach_AtReach512_WoodByTick5000(ulong seed)
    {
        (int firstWood, int scoutDone, float nearest) = NearestForestPastReach(seed, 66f, 512, 5000);
        _out.WriteLine($"seed {seed}: nearest forest {nearest:F1} m; reach 512: first wood at tick {firstWood}, scout done at {scoutDone}");
        Assert.True(nearest > 64f, "setup: a forest is left within the shipped reach");
        Assert.True(firstWood > 0, $"seed {seed}: no wood by tick 5,000 even with every node in reach");
    }

    /// <summary>
    /// BUG-1047: at the shipped 64 m, a start whose nearest forest lies just past the reach gathers no wood until its scout has
    /// walked every waypoint of the map (tick 8,741-9,641 on seeds 1-3), fields no army and is overrun; seeds 2 and 3 gathered
    /// 0 wood in 30,000 ticks. It should get its first wood about as soon as with every node in reach: by tick 5,000.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void NearestForestJustPastTheReach_AtTheShippedReach_WoodByTick5000(ulong seed)
    {
        (int firstWood, int scoutDone, float nearest) = NearestForestPastReach(seed, 66f, 64, 5000);
        _out.WriteLine($"seed {seed}: nearest forest {nearest:F1} m; reach 64: first wood at tick {firstWood}, scout done at {scoutDone}");
        Assert.True(firstWood > 0, $"seed {seed}: no wood by tick 5,000 (nearest forest {nearest:F1} m, scout done at {scoutDone})");
    }

    // ---------- (c) allocation ----------

    /// <summary>
    /// <see cref="AiPlayer.InGatherReach"/> over every known node and <see cref="ScoutManager.FoundEnough"/> for every seat of
    /// the mirror-start 2v2 while the scouts are out (the reach loop runs: phase 1, a finished hall) allocate nothing.
    /// </summary>
    [Fact]
    public void InGatherReach_AndFoundEnough_AllocateNothing_WhileTheScoutsAreOut()
    {
        Simulation sim = AiTeamTests.MirrorAllies(1, raiders: 0);
        while (sim.TickNumber < 1000) sim.Tick();
        AiPlayer[] ais = Enumerable.Range(0, 4).Select(p => sim.AiOf(p)!).ToArray();
        Assert.Contains(ais, a => a.Board.ScoutPhase == 1 && a.HasDropOff);
        int calls = 0;
        bool sink = false;
        Action probe = () =>
        {
            foreach (AiPlayer ai in ais)
            {
                int s = -1;
                while (ai.View.NextResource(ref s, out SeenResource n))
                {
                    sink ^= ai.InGatherReach(ai.NodeCentre(n.TypeId, n.Cell));
                    calls++;
                }
                sink ^= ScoutManager.FoundEnough(ai);
                KnowsWood(ai) = 0; // the think's cache emptied: KnowsNode walks the nodes (through InGatherReach) again
                KnowsGold(ai) = 0;
                sink ^= ai.KnowsNode(ResourceKind.Wood) | ai.KnowsNode(ResourceKind.Gold);
            }
        };
        probe();
        AllocationProbe.AssertZero(probe, _out);
        _out.WriteLine($"{calls} InGatherReach calls probed ({sink})");
        Assert.True(calls > 50, "setup: too few known nodes");
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_knowsWood")]
    private static extern ref byte KnowsWood(AiPlayer ai);

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_knowsGold")]
    private static extern ref byte KnowsGold(AiPlayer ai);

    /// <summary>
    /// <see cref="AiPlayer.KnowsNode"/> (the reserve's and the build order's "a gatherer could fill it" guard, BUG-1023) applies
    /// the reach too: on BUG-1045's mirror start (no raiders, so nobody defends) with every tree within 66 m of seat 3's
    /// hall taken, seat 3 knows only its ally's forests, beyond the reach; on every tick to 1,500 <c>KnowsNode(Wood)</c>
    /// answers exactly whether a known, unmarked forest is in <see cref="AiPlayer.InGatherReach"/>. QA's mutation run found the reach clause in <c>WalkForNode</c> untested (removing
    /// it passed every test); this row fails then. Every Gather the seat sends meanwhile targets a node in reach
    /// (<c>EconomyManager.NearestNode</c>'s half). M6-S7: unchanged by BUG-1047 / 1048's rule (a Gather sent once the reach has
    /// lifted, the head ring seen, is in reach by definition).
    /// </summary>
    [Fact]
    public void KnowsNode_AndTheGathersSent_CountOnlyNodesInGatherReach_WhileTheScoutIsOut()
    {
        Simulation sim = AiTeamTests.MirrorAllies(1, raiders: 0);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(3)!;
        int hallSlot = -1;
        while (hallSlot < 0 && sim.TickNumber < 5)
        {
            sim.Tick();
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 3) hallSlot = k;
        }
        Assert.True(hallSlot >= 0, "setup: no hall");
        foreach (int k in WoodWithin(w, ai, Centre(w, hallSlot), 66f))
            w.Resources.Take(new EntityHandle(k, w.Resources.Generation[k]), int.MaxValue);
        int farOnly = 0, checkedTicks = 0, gathers = 0, seen = 0;
        List<Rts.Sim.Commands.Command> log = AiScenes.Log(sim, 3);
        while (sim.TickNumber < 1500)
        {
            sim.Tick();
            // NearestNode's half (QA's mutation run: removing its reach check failed only one soak row): no Gather sent
            // while the scout is out goes to a node beyond the reach of every own finished drop-off.
            for (; seen < log.Count; seen++)
            {
                if (log[seen].Kind != Rts.Sim.Commands.CommandKind.Gather || ai.Board.ScoutPhase == 2 || !ai.HasDropOff) continue;
                gathers++;
                Assert.True(ai.InGatherReach(log[seen].Position), $"tick {sim.TickNumber}: a Gather to {log[seen].Position} beyond the reach while the scout is out");
            }
            if (ai.Board.Defending || !ai.HasDropOff) continue;
            bool anyWood = false, inReach = false;
            int s = -1;
            while (ai.View.NextResource(ref s, out SeenResource n))
            {
                if (w.Data.Resources[n.TypeId].Resource != ResourceKind.Wood || ai.Board.NodeDropped[s] == n.Handle.Generation) continue;
                anyWood = true;
                inReach |= ai.InGatherReach(ai.NodeCentre(n.TypeId, n.Cell));
            }
            if (anyWood && !inReach) farOnly++;
            KnowsWood(ai) = 0;
            Assert.True(inReach == ai.KnowsNode(ResourceKind.Wood), $"tick {sim.TickNumber}: KnowsNode(Wood) {!inReach}, a forest in reach {inReach} (scout phase {ai.Board.ScoutPhase})");
            checkedTicks++;
        }
        _out.WriteLine($"{checkedTicks} ticks checked; {farOnly} with only far forests known; {gathers} Gathers while the scout was out");
        Assert.True(farOnly > 0, "setup: seat 3 never knew only forests beyond the reach");
    }

    // ---------- (b) the BUG-1045 scene recorded and played back ----------

    /// <summary>
    /// BUG-1045's mirror-start 2v2 (seed 1, one raider each, four AI seats, teams 1, 1, 2, 2) recorded for 4,000 ticks
    /// (through the scouts' sweep, the reach rule's window and seat 3's first wood) as a format-7 replay text, read back and
    /// played with no AI: equal at all 40 checkpoints.
    /// </summary>
    [Fact]
    public void TheBug1045MirrorStart2v2_Recorded4000Ticks_PlaysBackToEveryCheckpoint()
    {
        PlayerSeat[] seats = Enumerable.Range(0, 4)
            .Select(p => new PlayerSeat(p % 2, SeatController.Ai, (AiDifficulty)((p + 1) % 3), 1 + p / 2)).ToArray();
        Simulation sim = AiScenes.Generated(seats, 1, record: true, out ReplayRecorder? rec, units: 800, commands: 2048, raiders: 1,
            configure: c => c with { Map = c.Map with { Width = 256, Height = 256, Forests = 24, GoldMines = 16, StartLocations = 4 } });
        int seat3Done = -1;
        for (int t = 0; t < 4000; t++)
        {
            sim.Tick();
            if (seat3Done < 0 && sim.AiOf(3)!.Board.ScoutPhase == 2) seat3Done = sim.TickNumber;
        }
        Assert.True(sim.World.GatheredWood[3] > 0, "setup: seat 3 gathered no wood (BUG-1045's scene not reproduced)");
        Replay replay = rec!.ToReplay();
        Assert.Equal(Replay.PresetlessFormatVersion, replay.FormatVersion); // M6-S6: a team match at standard stays format 7
        Assert.Equal(7, replay.FormatVersion);
        Assert.Equal(new[] { 1, 1, 2, 2 }, replay.Seats.Select(s => s.Team).ToArray());
        byte[] bytes = ReplayFormat.Write(replay);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? back));
        ReplayResult played = ReplayPlayer.Run(back!, TestSim.Data);
        _out.WriteLine($"seat 3's scout done at tick {seat3Done}; {back!.Checkpoints.Length} checkpoints; {back.Commands.Length} commands; {played}");
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(40, back.Checkpoints.Length);
        Assert.Equal(4000, played.TicksRun);
    }
}
